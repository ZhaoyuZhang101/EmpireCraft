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

// 出师先集结(群友 2026-11-22 "加个可以在宣战之前先让主力军队集结，不然大帝国各地军队抵达前线时间不同，反而给小国偷掉不少地"
// "大军集结于某地，听命行事""集结兵力优先调用临近省份，省的开战后再支援邻省")：
//   帝国对外开战主要走朝堂诉求(对外扩张、扩张地盘、游牧扩张、迫使朝贡、兴兵讨伐)，以前诉求一通过当场宣战，
//   先陈兵边境再宣战的那道门槛(WarReadinessSystem)只拦谋划开战，诉求宣的仗全绕过去了。现在诉求通过只是"定策"：
//   · 朝廷与离目标近的听调遣的成员国一起动员(CityPopulationSystem.IsMobilizing)，各自范围里的军开往边境——
//     就近的省先动，远处的照旧留守，开战后打不动才上来(WarScopeSystem 的逐级扩大照旧)；
//   · 集结的兵不如对方举国之兵(WideEdge)：两个月后范围翻倍、京师的军也上(muster_wide)；
//   · 范围里的军补到 FilledShare、七成五开到边境就宣战；等满 MaxWaitMonths 个月还没好的仓促开战，
//     可集结的兵连对方一半(AbortEdge)都不到就作罢(知难而退——以前比的是全国总兵力，到不了前线的也算)；
//   · 不战而屈人之兵(同一份反馈"某国闻大军压境，惊惧不已，乞求纳土归附")：集结已毕、兵力是对方 DeterEdge 倍以上，
//     对方是三城以内的小国，DeterChance 的机会当场称臣(附庸或朝贡，见 SmallStateSubmissionSystem.Submit)，仗不打了；
//   · 集结的兵过十万的记一笔大事，开战后主将用最高一档的官名(见 WarCommandSystem.TitleKey)。
//   不在军团模式、与目标不接壤(跨海)、以战养战的起义军：照旧当场开战。定下的目标存在王国数据里，读档接着集结。
public static class WarMuster
{
    private const float FilledShare = 0.6f, WideEdge = 1f, AbortEdge = 0.5f, DeterEdge = 4f, DeterChance = 0.5f;
    private const int MaxWaitMonths = 10, WideAfterMonths = 2, DeterMaxCities = 3;
    public const float GrandHost = 100000f;

    // 正在集结的国 → 随它一起动员的成员国(每月更新；读档后头一个月结补回来)
    private static readonly Dictionary<long, List<Kingdom>> Active = new();
    private static readonly HashSet<long> Follower = new();

    public static void ResetWorldState()
    {
        Active.Clear();
        Follower.Clear();
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    public static bool Mustering(Kingdom kingdom) => kingdom != null && Active.ContainsKey(kingdom.id);

    // 随中央集结的成员国(自己不是定策的一方)
    public static bool Following(Kingdom kingdom) => kingdom != null && Follower.Contains(kingdom.id);

    public static bool Wide(Kingdom kingdom) => Mustering(kingdom) && kingdom.GetOrCreate().muster_wide;

    // 集结了几个月(随中央集结的成员按中央的算)：征兵从平时的成数往全征爬(见 CorpsSystem.WarLevyShare)
    public static int Months(Kingdom kingdom)
    {
        if (!Alive(kingdom)) return 0;
        Kingdom lead = Mustering(kingdom) ? kingdom : Following(kingdom) ? FrontSystem.Realm(kingdom) : null;
        double since = lead?.data == null ? -1d : lead.GetOrCreate().muster_since;
        return since < 0d || World.world.getCurWorldTime() < since ? 0 : Date.getMonthsSince(since);
    }

    // 定策的国、目标、随同动员的成员(见 CityPopulationSystem.IsMobilizing)
    public static IEnumerable<(Kingdom author, Kingdom target, List<Kingdom> followers)> Entries()
    {
        if (Active.Count == 0 || World.world?.kingdoms == null) yield break;
        foreach (KeyValuePair<long, List<Kingdom>> pair in Active.ToList())
        {
            Kingdom author = World.world.kingdoms.get(pair.Key);
            Kingdom target = Alive(author) ? World.world.kingdoms.get(author.GetOrCreate().muster_target_id) : null;
            if (Alive(target)) yield return (author, target, pair.Value);
        }
    }

    // 朝议定下要打：军团模式下先集结，返回 true 表示受理了(当场开战或开始集结)。
    // announce：开战时走带通告的入口(startWar)，否则直接立战(newWar)，同各诉求原先的写法
    public static bool Declare(Kingdom attacker, Kingdom target, EmpireWarType type, WarGoal goal = WarGoal.None,
        bool locked = false, bool announce = true)
    {
        if (!Alive(attacker) || !Alive(target) || attacker == target || attacker.isInWarWith(target)) return false;
        if (!CorpsSystem.Enabled || CorpsSystem.Insurgent(attacker) || !Borders(attacker, target))
            return Start(attacker, target, type, goal, locked, announce) != null;
        var data = attacker.GetOrCreate();
        // 已经在为别的仗集结：不换目标
        if (data.muster_target_id >= 0 && Alive(World.world.kingdoms.get(data.muster_target_id))) return false;
        data.muster_target_id = target.id;
        data.muster_since = World.world.getCurWorldTime();
        data.muster_war_type = (int)type;
        data.muster_goal = (int)goal;
        data.muster_goal_locked = locked;
        data.muster_announce = announce;
        data.muster_wide = false;
        Active[attacker.id] = Followers(attacker, target);
        RebuildFollowers();
        City at = Staging(attacker, target);
        FrontSystem.Log(attacker, string.Format(LM.Get("war_muster_begin"), attacker.GetKingdomName(), at?.GetCityName() ?? "", target.GetKingdomName()));
        LogService.LogInfo($"[EmpireCraft][集结] {attacker.GetKingdomName()} 定策伐 {target.GetKingdomName()}，随同动员 {Active[attacker.id].Count} 国");
        return true;
    }

    private static War Start(Kingdom attacker, Kingdom target, EmpireWarType type, WarGoal goal, bool locked, bool announce)
    {
        War war = announce
            ? World.world.diplomacy.startWar(attacker, target, WarTypeLibrary.normal)
            : DiplomacyHelpers.wars.newWar(attacker, target, WarTypeLibrary.normal);
        if (war == null) return null;
        war.SetEmpireWarType(type);
        if (goal != WarGoal.None) WarGoalSystem.Preset(war, goal);
        if (locked) war.GetOrCreate().war_goal_locked = true;
        return war;
    }

    // 接壤：本国(或听调遣的成员)有朝向目标的边境。不接壤的(跨海、隔着别国)没有边境可陈兵，照旧当场开战
    private static bool Borders(Kingdom attacker, Kingdom target)
    {
        try
        {
            // 中央自己不挨着、挨着的是听调遣的省：由那些省陈兵边境
            foreach (Kingdom side in new[] { attacker }.Concat(Followers(attacker, target)))
                if (KingdomFrontLineHelper.GetFriendlyFrontSourceZonesFacingEnemy(side, target).Any(KingdomFrontLineHelper.IsValidZone))
                    return true;
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 随中央一起动员的成员：听调遣、不在叛乱，有城离目标近(同 WarScopeSystem 的就近范围)
    private static List<Kingdom> Followers(Kingdom attacker, Kingdom target)
    {
        var result = new List<Kingdom>();
        Empire empire = attacker.GetEmpire();
        if (empire?.CoreKingdom != attacker || empire.kingdoms_list == null) return result;
        foreach (Kingdom member in empire.kingdoms_list)
        {
            if (!Alive(member) || member == attacker || member == target || member.hasEnemies() || member.IsFactionRebelling() ||
                member.IsLocalRebelling() || !GamePatches.EmpirePeacePatch.Obeys(empire, member)) continue;
            if (WarScopeSystem.Near(member, target)) result.Add(member);
        }
        return result;
    }

    private static void RebuildFollowers()
    {
        Follower.Clear();
        foreach (List<Kingdom> members in Active.Values)
            foreach (Kingdom member in members)
                if (Alive(member) && !Active.ContainsKey(member.id)) Follower.Add(member.id);
    }

    // 集结在哪座城(记事用)：本国离目标最近的军镇，没有就都城
    private static City Staging(Kingdom attacker, Kingdom target)
    {
        City best = null;
        float bestDistance = float.MaxValue;
        CityPopulationSystem.ArmyDoctrine doctrine = CityPopulationSystem.DoctrineOf(attacker);
        foreach (Kingdom side in new[] { attacker }.Concat(Active.TryGetValue(attacker.id, out List<Kingdom> members) ? members : new List<Kingdom>()))
        {
            if (!Alive(side) || side.cities == null) continue;
            foreach (City city in side.cities)
            {
                if (city?.data == null || city.isRekt() || CityPopulationSystem.PostOf(city, doctrine) == CityPopulationSystem.ArmyPost.None) continue;
                float distance = WarScopeSystem.DistanceTo(city, target);
                if (distance >= bestDistance) continue;
                best = city;
                bestDistance = distance;
            }
        }
        return best ?? attacker.capital;
    }

    private static void Clear(Kingdom kingdom)
    {
        var data = kingdom.GetOrCreate();
        data.muster_target_id = -1L;
        data.muster_since = -1d;
        data.muster_wide = false;
        Active.Remove(kingdom.id);
        RebuildFollowers();
    }

    // 能集结到前线的兵：定策的国与随同成员动员范围里的正规军
    private static float Host(Kingdom kingdom, List<Kingdom> followers)
    {
        float total = 0f;
        foreach (Kingdom side in new[] { kingdom }.Concat(followers))
        {
            if (!Alive(side)) continue;
            foreach (CorpsData corps in CorpsSystem.Of(side))
                if (!corps.militia && corps.order_id < 0 && WarScopeSystem.InScope(CorpsSystem.Home(corps))) total += Mathf.Max(0f, corps.strength);
        }
        return total;
    }

    // 对方举国之兵(连同它的宗主中央)
    private static float Strength(Kingdom target)
    {
        float total = 0f;
        foreach (Kingdom side in new[] { target, FrontSystem.Realm(target) }.Distinct())
        {
            if (!Alive(side)) continue;
            foreach (CorpsData corps in CorpsSystem.Of(side))
                if (!corps.militia) total += Mathf.Max(0f, corps.strength);
        }
        return total;
    }

    // 每月(各国月结，征兵之前)
    public static void MonthTick(Kingdom kingdom)
    {
        if (!Alive(kingdom)) return;
        var data = kingdom.GetOrCreate();
        if (data.muster_target_id < 0)
        {
            if (Active.Remove(kingdom.id)) RebuildFollowers();
            return;
        }
        Kingdom target = World.world.kingdoms.get(data.muster_target_id);
        var type = (EmpireWarType)data.muster_war_type;
        var goal = (WarGoal)Math.Max(0, data.muster_goal);
        string name = kingdom.GetKingdomName(), targetName = target?.GetKingdomName() ?? "";
        // 目标没了、已经打起来了、和约或盟约不许打、自己已经顾不上：作罢
        if (!Alive(target) || kingdom.isInWarWith(target) || !WarGate.CanDeclare(kingdom, target) ||
            WarSituation.Overstretched(kingdom) || CorpsSystem.Unpaid(kingdom))
        {
            if (Alive(target) && !kingdom.isInWarWith(target))
                FrontSystem.Log(kingdom, string.Format(LM.Get("war_muster_called_off"), name, targetName));
            Clear(kingdom);
            return;
        }
        if (!CorpsSystem.Enabled)
        {
            Clear(kingdom);
            Start(kingdom, target, type, goal, data.muster_goal_locked, data.muster_announce);
            return;
        }
        List<Kingdom> followers = Active[kingdom.id] = Followers(kingdom, target);
        RebuildFollowers();
        int months = data.muster_since < 0d || World.world.getCurWorldTime() < data.muster_since ? 0 : Date.getMonthsSince(data.muster_since);
        float host = Host(kingdom, followers), theirs = Strength(target);
        bool ready = months >= 1 && WarReadinessSystem.Ready(new[] { kingdom }.Concat(followers), FilledShare);
        if (!ready && months < MaxWaitMonths)
        {
            // 就近的兵不够看：范围翻倍、京师的军也上
            if (!data.muster_wide && months >= WideAfterMonths && host < theirs * WideEdge)
            {
                data.muster_wide = true;
                FrontSystem.Log(kingdom, string.Format(LM.Get("war_muster_wider"), name, targetName));
            }
            return;
        }
        Clear(kingdom);
        // 知难而退：到得了前线的兵连对方一半都不到
        if (!ready && host < theirs * AbortEdge)
        {
            FrontSystem.Log(kingdom, string.Format(LM.Get("war_muster_abandoned"), name, targetName));
            LogService.LogInfo($"[EmpireCraft][集结] {name} 伐 {targetName} 作罢：可集结 {host:0} 对 {theirs:0}");
            return;
        }
        if (ready && Deterred(kingdom, target, host, theirs)) return;
        string text = string.Format(LM.Get(ready ? "war_ready_massed" : "war_ready_rushed"), name, targetName);
        FrontSystem.Log(kingdom, text);
        if (host >= GrandHost)
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_muster_grand"), name, Mathf.RoundToInt(host / 10000f), targetName), kingdom);
        Start(kingdom, target, type, goal, data.muster_goal_locked, data.muster_announce);
        LogService.LogInfo($"[EmpireCraft][集结] {name} 向 {targetName} 开战：集结 {host:0} 对 {theirs:0}，等了 {months} 月{(ready ? "" : "(仓促)")}");
    }

    // 不战而屈人之兵：大军压境，三城以内的小国称臣(附庸或朝贡)
    private static bool Deterred(Kingdom kingdom, Kingdom target, float host, float theirs)
    {
        if (DiplomacyLaws.VassalBlocked || host < Mathf.Max(1f, theirs) * DeterEdge || target.IsEmpire() || target.IsInEmpire() ||
            (target.cities?.Count ?? 0) > DeterMaxCities || FeudalVassalService.GetOverlord(target) != null ||
            RulerTraitSystem.FounderShield(target) || target.hasEnemies() || UnityEngine.Random.value >= DeterChance) return false;
        if (!SmallStateSubmissionSystem.Submit(kingdom, target)) return false;
        // 乞和纳质：把储君送到上国都城为质(见 HostageSystem)
        HostageSystem.Send(kingdom, target);
        FrontSystem.Log(kingdom, string.Format(LM.Get("war_muster_deterred"), target.GetKingdomName(), kingdom.GetKingdomName()));
        LogService.LogInfo($"[EmpireCraft][集结] {target.GetKingdomName()} 闻 {kingdom.GetKingdomName()} 大军压境称臣：{host:0} 对 {theirs:0}");
        return true;
    }
}
