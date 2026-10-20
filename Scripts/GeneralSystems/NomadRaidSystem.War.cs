using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 劫掠之战(只抢不占，EmpireWarType.劫掠)：
//   · 攻方破城不易主，改为洗劫(见 CityPatch.FinishedCapture → OnSack)：拿走这座城金帛三成(都城拿国库三成)、各样存粮三成、
//     背景人口百分之八(掳到出兵各部的都城)，按开战时各部出兵多少分；同一座城一场仗只洗劫一回；遭劫之国五年内不再遭劫；
//   · 抢够就退：洗劫的城数到了要抢的数(单国一城，联盟每部一城，至多四城)，当场判攻方胜、罢兵；
//   · 打满 RaidYears 年还没抢够也退兵：抢到过就算得手(攻方胜)，一城没抢到算被打退(守方胜)；
//   · 收场：战争结束、联盟解散，出过兵的部从这时起 RaidCooldownYears 年内不再出兵；守方胜不索赔(打退就是，见 WarGoalSystem)；
//     买了和、判攻方胜(抢够了或守方求和)、议和时抢到过都算得手，判守方胜或一城没抢到就罢兵算被打退；
//     得手、被打退、联盟散伙各记一笔。旧档里打着的劫掠之战补一份记录(盟主 = 主攻方，抢一城即退，同旧规)。
public static partial class NomadRaidSystem
{
    private const float RaidYears = 1f;

    // 劫掠之战的记录(旧档补出来)
    private static RaidWarState Ensure(War war)
    {
        var extra = war.GetOrCreate();
        if (extra.raid != null) return extra.raid;
        Kingdom leader = war.main_attacker, target = war.main_defender;
        List<Kingdom> members = war._list_attackers.Where(Alive).ToList();
        extra.raid = new RaidWarState
        {
            leader_id = leader?.id ?? -1L, leader_name = leader?.GetKingdomName() ?? "", target_name = target?.GetKingdomName() ?? "",
            members = members.Select(member => member.id).ToList(),
            troops = members.ToDictionary(member => member.id, member => Mathf.Max(1f, Troops(member))),
            goal = 1f
        };
        return extra.raid;
    }

    private static List<War> RaidWars() =>
        World.world.wars.list.Where(war => war?.data != null && war.isAlive() && !war.hasEnded() &&
                                           war.GetEmpireWarType() == EmpireWarType.劫掠).ToList();

    private static void MonthTick()
    {
        Aftermath();
        foreach (War war in RaidWars())
        {
            RaidWarState state = Ensure(war);
            if (WarSituation.WarYears(war) >= RaidYears)
            {
                LogService.LogInfo($"{LogTag} {war.data.name} 打满一年退兵(洗劫{state.sacked:0.#}/{state.goal:0.#}城)");
                World.world.wars.endWar(war, state.sacked > 0f ? WarWinner.Attackers : WarWinner.Defenders);
                continue;
            }
            MonthlyCourse(war, state);
        }
    }

    // 劫掠之战每月的进展(关隘折损、买和，见应对一节)
    static partial void MonthlyCourse(War war, RaidWarState state);

    #region 洗劫

    // 劫掠之战里攻方破城(CityPatch.FinishedCapture)：洗劫，城不易主；返回是否抢够(调用处随即判攻方胜、罢兵)
    public static bool OnSack(War war, City city, Kingdom raider)
    {
        try
        {
            Kingdom victim = city?.kingdom;
            if (war?.data == null || victim?.data == null) return false;
            RaidWarState state = Ensure(war);
            victim.GetOrCreate().raided_at = World.world.getCurWorldTime();
            if (state.sacked_cities.Contains(city.id)) return state.sacked >= state.goal;
            state.sacked_cities.Add(city.id);
            float factor = SteppeRules.LootFactor(state.defense);
            float moneyFactor = factor, foodFactor = factor, peopleFactor = factor;
            bool held = Steadfast(city);
            if (held)
            {
                moneyFactor *= SteadfastMoney;
                foodFactor *= SteadfastFood;
                peopleFactor *= SteadfastPeople;
            }
            List<(Kingdom member, float share)> shares = Shares(war, state);
            int money = TakeMoney(city, victim, moneyFactor);
            int food = 0;
            float people = 0f;
            if (shares.Count > 0)
            {
                foreach ((Kingdom member, float share) in shares)
                    member.AddMoney(Mathf.FloorToInt(money * share), TreasuryCategory.Diplomacy);
                food = TakeFood(city, shares, foodFactor);
                people = TakePeople(city, shares, peopleFactor);
            }
            state.loot_money += money;
            state.loot_food += food;
            state.loot_people += Mathf.RoundToInt(people);
            state.sacked += held ? 0.5f : 1f;
            string name = raider?.GetKingdomName() ?? state.leader_name;
            TranslateHelper.LogEventOnly(string.Format(LM.Get(held ? "nomad_raid_sack_held_log" : "nomad_raid_sack_log"), name,
                victim.GetKingdomName(), city.GetCityName(), money, food, Mathf.RoundToInt(people)), raider ?? victim);
            LogService.LogInfo($"{LogTag} {war.data.name}：{name}洗劫{victim.GetKingdomName()}的{city.GetCityName()}" +
                               $"{(held ? "(屯田边城，守住大半)" : "")}，金帛{money} 粮{food} 人口{people:0}，" +
                               $"已抢{state.sacked:0.#}/{state.goal:0.#}城，关隘之固{state.defense:0.00}");
            return state.sacked >= state.goal;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 洗劫{city?.data?.name}结算失败: {exception.Message}");
            return true;
        }
    }

    // 出兵各部分战利品的份额：开战时的兵力占比；已退出、亡了的不分
    private static List<(Kingdom member, float share)> Shares(War war, RaidWarState state)
    {
        var alive = new List<(Kingdom, float)>();
        foreach (long id in state.members)
        {
            Kingdom member = LiveKingdom(id);
            if (member == null || !war.isAttacker(member)) continue;
            alive.Add((member, state.troops.TryGetValue(id, out float troops) ? Mathf.Max(1f, troops) : 1f));
        }
        if (alive.Count == 0 && war.main_attacker is Kingdom main && Alive(main)) alive.Add((main, 1f));
        float total = alive.Sum(entry => entry.Item2);
        return alive.Select(entry => (entry.Item1, entry.Item2 / Mathf.Max(1f, total))).ToList();
    }

    // 金帛：都城拿国库、别的城拿城库，只拿现钱(欠债不能当战利品转给胜方)
    private static int TakeMoney(City city, Kingdom victim, float factor)
    {
        bool capital = city.isCapitalCity();
        int amount = Mathf.Max(0, Mathf.FloorToInt((capital ? victim.GetMoney() : city.GetMoney()) * SteppeRules.MoneyShare * factor));
        if (amount <= 0) return 0;
        if (capital) victim.SubMoney(amount, TreasuryCategory.Diplomacy);
        else city.SubMoney(amount, TreasuryCategory.Diplomacy);
        return amount;
    }

    private static List<string> _foodIds;

    private static List<string> FoodIds() =>
        _foodIds ??= AssetManager.resources?.list?.Where(asset => asset != null && asset.food).Select(asset => asset.id).ToList()
                     ?? new List<string>();

    // 各样存粮拿走几成，运到各部都城
    private static int TakeFood(City city, List<(Kingdom member, float share)> shares, float factor)
    {
        int moved = 0;
        foreach (string id in FoodIds())
        {
            int have = city.getResourcesAmount(id);
            int take = Mathf.FloorToInt(have * SteppeRules.FoodShare * factor);
            if (take <= 0) continue;
            foreach ((Kingdom member, float share) in shares)
            {
                City home = member.capital;
                int part = Mathf.FloorToInt(take * share);
                if (home?.data == null || home.isRekt() || part <= 0) continue;
                moved += CityResourceTransferSystem.Move(city, home, id, part);
            }
        }
        return moved;
    }

    // 掳掠背景人口(无小人模式)，迁到各部都城
    private static float TakePeople(City city, List<(Kingdom member, float share)> shares, float factor)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return 0f;
        float take = CityPopulationSystem.CivilianBackground(city) * SteppeRules.PeopleShare * factor;
        if (take < 1f) return 0f;
        float moved = 0f;
        foreach ((Kingdom member, float share) in shares)
        {
            City home = member.capital;
            if (home?.data == null || home.isRekt() || home == city) continue;
            moved += CityPopulationSystem.TransferCivilianBackground(city, home, take * share);
        }
        return moved;
    }

    #endregion

    #region 收场

    // 战争收尾(WarPatch.end_war，结算占领之前)：联盟散伙、记史书，盟主威望(见雄主一节)
    public static void OnWarEnded(War war, WarWinner winner)
    {
        if (war?.data == null || war.GetEmpireWarType() != EmpireWarType.劫掠) return;
        try
        {
            RaidWarState state = Ensure(war);
            if (state.settled) return;
            state.settled = true;
            double now = World.world.getCurWorldTime();
            // 得手：买了和、判攻方胜(抢够了，或守方欠饷求和)、议和时抢到过；被打退：判守方胜，或一城没抢到就罢兵
            bool success = state.bought_off > 0 || winner == WarWinner.Attackers || winner != WarWinner.Defenders && state.sacked > 0f;
            var members = state.members.Select(LiveKingdom).Where(member => member != null).ToList();
            foreach (Kingdom member in members)
            {
                SteppeState steppe = State(member);
                if (steppe.raid_war_id == war.data.id) steppe.raid_war_id = -1L;
                steppe.last_raid_at = now;
            }
            Kingdom leader = LiveKingdom(state.leader_id);
            Kingdom target = war.main_defender;
            var involved = members.Concat(new[] { target }).ToList();
            if (state.bought_off == 0)
            {
                string key = success ? "nomad_raid_withdraw_history" : "nomad_raid_repulsed_history";
                Chronicle(string.Format(LM.Get(key), state.leader_name, state.target_name, Mathf.CeilToInt(state.sacked),
                    state.loot_money, state.loot_food, state.loot_people), involved);
            }
            if (state.members.Count > 1)
                Chronicle(string.Format(LM.Get("nomad_raid_disband_history"), state.leader_name), members);
            LogService.LogInfo($"{LogTag} {war.data.name} 收场({winner}{(state.bought_off > 0 ? "，买和" + state.bought_off : "")})：" +
                               $"{(success ? "得手" : "被打退")}，洗劫{state.sacked:0.#}/{state.goal:0.#}城，金帛{state.loot_money}" +
                               $" 粮{state.loot_food} 人口{state.loot_people}，买和金{state.ransom}，出兵{state.members.Count}部");
            AfterRaid(war, state, leader, members, success);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} {war.data.name} 收场结算失败: {exception.Message}");
        }
    }

    // 盟主威望与成雄主、失败内讧(见雄主一节)
    static partial void AfterRaid(War war, RaidWarState state, Kingdom leader, List<Kingdom> members, bool success);

    #endregion

    #region 单国劫掠诉求(TempFac_劫掠)

    // 有劫掠传统的帝国朝堂提"劫掠"：要今年草原压力够、能出兵，挑一家比自己弱的接壤定居之国；没有返回 null
    public static Kingdom ClaimTarget(Kingdom core)
    {
        if (!IsRealm(core) || !IsRaider(core) || !CanRaid(core)) return null;
        SteppeState state = State(core);
        if (state.pressure_at < 0d || YearsSince(state.pressure_at) > 1.5f || state.pressure < SteppeRules.RaidThreshold) return null;
        return PickTarget(core, new List<Kingdom> { core }).target;
    }

    // 诉求施行：单独劫掠(开战前再查一遍)
    public static void RaidFromClaim(Kingdom core, Kingdom target)
    {
        if (!IsRealm(core) || !CanRaid(core) || !Raidable(core, new List<Kingdom> { core }, target)) return;
        Launch(new List<Kingdom> { core }, target, 0d);
    }

    #endregion
}
