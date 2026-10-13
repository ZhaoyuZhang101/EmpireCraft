using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 开战目标(战后分配的一部分，参照钢铁雄心：战时占领与最终归属分开)：国战开打时按形势定一个目标，议和时按目标结算——
//   · 吞并：攻方兵力两倍以上、与对方接壤、对方是小国；
//   · 割地(局部入侵)：接壤、兵力相当；
//   · 赔款：不接壤(跨海)或攻方兵力不如对方；
//   · 劫掠：原有的劫掠战争(洗劫第一座城即收兵，见 CityPatch)。
// 议和时按这场仗里双方各丢了多少城(含被对方占了一半以上地块的城)判胜负，相差不到一城就是白和——不赔不割：
//   攻方胜：赔款 → 对方赔款；割地 → 被攻方占了一半以上的城割给占领者；吞并 → 同割地，对方丢了一半城或都城时余下的城一并吞并；
//   守方胜：攻方赔款(劫掠的不赔，打退就是)。
// 以前不论怎么收场都按"有胜负"处理，议和(原版最常见的结局)也让攻方赔款，几乎每场仗打完都赔款。
// 叛乱、独立、索取法理、去帝号这类有自己结局的战争不在此列，照旧各按各的规则。
public static class WarGoalSystem
{
    // 吞并：攻方兵力至少是对方几倍、对方城数不超过多少(或不超过攻方一半)
    private const float AnnexStrength = 2f;
    private const int AnnexSmallCities = 3;
    // 割地：攻方兵力至少是对方几成
    private const float ConquestStrength = 0.8f;
    // 占了一座城一半以上的地块才算到手
    private const float HeldShare = 0.5f;
    // 每帧最多易主几座城(易主有原版开销)
    private const int CessionsPerFrame = 3;

    private static readonly HashSet<EmpireWarType> OwnEnding = new()
    {
        EmpireWarType.游牧扩张, EmpireWarType.迫使朝贡, EmpireWarType.藩王索取皇位, EmpireWarType.派系叛乱,
        EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱, EmpireWarType.地方独立,
        EmpireWarType.附庸独立, EmpireWarType.清君侧, EmpireWarType.索取法理, EmpireWarType.去帝号,
        EmpireWarType.帝国正统, EmpireWarType.不奉诏, EmpireWarType.获取帝国
    };

    private static readonly List<(long city, long to)> PendingCessions = new();

    public static void ResetWorldState() => PendingCessions.Clear();

    private static bool Applies(War war) =>
        war?.data != null && war.getAsset() != WarTypeLibrary.rebellion && !OwnEnding.Contains(war.GetEmpireWarType());

    public static WarGoal Get(War war)
    {
        if (war?.data == null) return WarGoal.None;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠) return WarGoal.劫掠;
        return Applies(war) ? war.GetOrCreate().war_goal : WarGoal.None;
    }

    // 开打时定目标(WarPatch.update 每场仗第一次结算时调用)；读档后已定的不变
    public static void Ensure(War war)
    {
        if (war?.data == null || war.hasEnded()) return;
        var data = war.GetOrCreate();
        if (data.war_goal != WarGoal.None || !Applies(war)) return;
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        if (attacker?.data == null || defender?.data == null || attacker.isRekt() || defender.isRekt()) return;
        if (war.GetEmpireWarType() == EmpireWarType.劫掠)
        {
            data.war_goal = WarGoal.劫掠;
            return;
        }
        float strength = WarSituation.StrengthRatio(defender, attacker);
        int theirs = WarSituation.SideCities(defender), ours = WarSituation.SideCities(attacker);
        bool borders = attacker.IsNeighbourWith(defender);
        data.war_goal = !borders ? WarGoal.赔款
            : strength >= AnnexStrength && (theirs <= AnnexSmallCities || theirs * 2 <= ours) ? WarGoal.吞并
            : strength >= ConquestStrength ? WarGoal.割地
            : WarGoal.赔款;
        data.war_goal_capital_id = defender.capital?.data != null ? defender.capital.id : -1L;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_goal_declared"), attacker.GetKingdomName(),
            defender.GetKingdomName(), Name(data.war_goal)), attacker);
    }

    public static string Name(WarGoal goal) => LM.Get("war_goal_" + goal);

    // 战争窗口：目标与战况(双方各已得几城)
    public static string Describe(War war)
    {
        WarGoal goal = Get(war);
        if (goal == WarGoal.None) return null;
        if (war.hasEnded()) return Name(goal);
        (int attackerGain, int defenderGain) = Gains(war);
        return string.Format(LM.Get("war_goal_standing"), Name(goal), attackerGain, defenderGain);
    }

    // 攻守双方在这场仗里各得几城：对方参战以来少了的城 + 己方占了一半以上地块的对方城市
    private static (int attackerGain, int defenderGain) Gains(War war)
    {
        Kingdom attacker = war.main_attacker, defender = war.main_defender;
        int defenderLost = Math.Max(0, WarSituation.CitiesLost(war, defender));
        int attackerLost = Math.Max(0, WarSituation.CitiesLost(war, attacker));
        return (defenderLost + HeldCities(war, war._list_defenders, war._list_attackers).Count,
            attackerLost + HeldCities(war, war._list_attackers, war._list_defenders).Count);
    }

    // owners 的城里被 occupiers 一方占了一半以上地块的：城 → 占得最多的那国
    private static List<(City city, Kingdom holder)> HeldCities(War war, IEnumerable<Kingdom> owners, IEnumerable<Kingdom> occupiers)
    {
        var result = new List<(City, Kingdom)>();
        List<Kingdom> side = occupiers.Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
        foreach (Kingdom owner in owners)
        {
            if (owner?.cities == null || owner.isRekt()) continue;
            foreach (City city in owner.cities)
            {
                if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) continue;
                int total = 0, best = 0;
                Kingdom holder = null;
                foreach (Kingdom kingdom in side)
                {
                    int count = city.GetOccupiedTileZoneCount(kingdom);
                    if (count <= 0) continue;
                    total += count;
                    if (count > best)
                    {
                        best = count;
                        holder = kingdom;
                    }
                }
                if (holder != null && total >= city.zones.Count * HeldShare) result.Add((city, holder));
            }
        }
        return result;
    }

    // 战争收尾时(WarPatch.end_war，清掉占领之前)：判胜负、按目标结算
    public static void Settle(War war, WarWinner winner)
    {
        try
        {
            if (war?.data == null || winner == WarWinner.Nobody || winner == WarWinner.Merged || !Applies(war) &&
                war.GetEmpireWarType() != EmpireWarType.劫掠) return;
            Kingdom attacker = war.main_attacker, defender = war.main_defender;
            if (attacker?.data == null || defender?.data == null || attacker == defender) return;
            WarGoal goal = Get(war);
            if (goal == WarGoal.None) goal = WarGoal.赔款;
            (int attackerGain, int defenderGain) = Gains(war);
            bool attackersWon = winner == WarWinner.Attackers || winner == WarWinner.Peace && attackerGain >= defenderGain + 1;
            bool defendersWon = winner == WarWinner.Defenders || winner == WarWinner.Peace && defenderGain >= attackerGain + 1;
            if (attackersWon)
            {
                if (defender.isRekt()) return;
                switch (goal)
                {
                    case WarGoal.赔款:
                        WarReparationsSystem.Impose(defender, attacker);
                        break;
                    case WarGoal.割地:
                        Cede(war, attacker, defender, annex: false);
                        break;
                    case WarGoal.吞并:
                        Cede(war, attacker, defender, annex: true);
                        break;
                }
            }
            else if (defendersWon)
            {
                if (goal != WarGoal.劫掠 && !attacker.isRekt()) WarReparationsSystem.Impose(attacker, defender);
            }
            else if (!attacker.isRekt() && !defender.isRekt())
            {
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_white"), attacker.GetKingdomName(),
                    defender.GetKingdomName()), attacker);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][开战目标] 议和结算失败: {exception.Message}");
        }
    }

    // 割让攻方占了一半以上的城；吞并且对方丢了一半城或都城时，主守方余下的城一并归主攻方(帝国核心不整体吞并，只割城)
    private static void Cede(War war, Kingdom attacker, Kingdom defender, bool annex)
    {
        List<(City city, Kingdom holder)> held = HeldCities(war, war._list_defenders, war._list_attackers);
        foreach ((City city, Kingdom holder) in held) PendingCessions.Add((city.id, holder.id));
        int ceded = held.Count;
        if (annex && !defender.IsEmpire())
        {
            Dictionary<long, int> initial = war.GetOrCreate().initial_cities;
            int before = initial != null && initial.TryGetValue(WarSituation.SideKey(defender), out int count) ? count : 0;
            int lost = Math.Max(0, WarSituation.CitiesLost(war, defender)) + held.Count;
            City capital = World.world.cities.get(war.GetOrCreate().war_goal_capital_id);
            bool capitalTaken = capital?.data != null && (capital.kingdom != defender || held.Any(entry => entry.city == capital));
            if (capitalTaken || before > 0 && lost * 2 >= before)
            {
                var queued = new HashSet<long>(held.Select(entry => entry.city.id));
                foreach (City city in defender.cities.ToList())
                    if (city?.data != null && !city.isRekt() && queued.Add(city.id)) PendingCessions.Add((city.id, attacker.id));
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_annexed"), attacker.GetKingdomName(),
                    defender.GetKingdomName()), attacker);
                return;
            }
        }
        if (ceded > 0)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_ceded"), attacker.GetKingdomName(),
                defender.GetKingdomName(), ceded), attacker);
    }

    // 战争结束后几帧内逐城易主(不在原版结束战争的同一帧里改归属)
    public static void Tick()
    {
        if (PendingCessions.Count == 0 || World.world == null) return;
        int done = 0;
        while (PendingCessions.Count > 0 && done < CessionsPerFrame)
        {
            (long cityId, long toId) = PendingCessions[0];
            PendingCessions.RemoveAt(0);
            City city = World.world.cities.get(cityId);
            Kingdom to = World.world.kingdoms.get(toId);
            if (city?.data == null || city.isRekt() || to?.data == null || to.isRekt() || city.kingdom == to) continue;
            try
            {
                city.joinAnotherKingdom(to);
                done++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][开战目标] {city.data.name} 割让失败: {exception.Message}");
            }
        }
    }
}
