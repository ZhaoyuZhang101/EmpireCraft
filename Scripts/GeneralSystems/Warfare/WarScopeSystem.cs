using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 就近动员：打仗不再全国征兵——
//   · 只有离敌境近的城(城心到最近的敌城不超过 NearDistance 格，或城里有地块被敌军占着)按战时兵额征兵，
//     兵员也先从这些城抽；远处的城照太平时的常备兵额，那里的军留守驻地，不开赴前线；
//     范围里没有驻军的军镇(与敌国不接壤、跨海，或边境只有不驻军的小城)时，从离敌城最近的一座军镇出兵；
//     谋划开战时只有谋划的国家在朝向目标国的一侧集结；
//   · 打不过了逐级扩大：本国有城被敌军占着，或受攻的城分不出足够守军，算吃紧(吃紧一个月加一、缓和一个月减一)——
//     一级：吃紧满四个月，范围翻倍，京师禁军出征；二级：吃紧满一年、而且确实危急(都城被占，或三成以上的城有地块被占)，
//     举国总动员。连续三个月没吃紧降一级，太平时归零(以前吃紧一个月就升一级，打仗总有地块被占，第二个月就举国总动员、
//     远方的军开往前线)；
//   · 攻不动也扩大(见 Stalled)：有军在进攻、战果却连着半年没再创新高，范围扩到一级(不往举国总动员上数)，后方的军上得来；
//   · 远方的援军走决议或中央命令："出征"的国家(求援、驰援邻省、发兵勤王决议通过，或奉中央诏令)至少按一级动员，
//     军队可以开往任何战线；中央吃紧(一级以上)时诏令听命于中央、随中央参战的成员出征。
public static class WarScopeSystem
{
    private const float NearDistance = 75f;
    public const int Wider = 1, Total = 2;
    private const int CalmMonthsToEase = 3;
    // 吃紧几个月扩大一级、几个月才举国总动员；总动员还要确实危急(三成以上的城有地块被占，或都城被占)
    private const int WiderMonths = 4, TotalMonths = 12;
    private const float SevereShare = 0.3f;

    private static readonly HashSet<long> Scope = new();
    private static readonly HashSet<long> Shortfall = new();
    private static readonly List<Vector2> HostileCenters = new();
    private static double _builtAt = -1d;
    private static MapBox _world;

    public static void ResetWorldState()
    {
        Scope.Clear();
        Shortfall.Clear();
        _builtAt = -1d;
    }

    public static int Level(Kingdom kingdom) => kingdom?.data == null ? 0 : kingdom.GetOrCreate().war_mobilization_level;

    public static bool DispatchedFor(Kingdom kingdom, War war) =>
        war?.data != null && kingdom?.data != null && kingdom.GetOrCreate().expedition_wars?.Contains(war.data.id) == true;

    // 为某场仗出征(决议通过或奉诏)
    public static bool Dispatched(Kingdom kingdom)
    {
        List<long> ids = kingdom?.data == null ? null : kingdom.GetOrCreate().expedition_wars;
        if (ids == null || ids.Count == 0) return false;
        foreach (long id in ids)
            if (Alive(World.world.wars.get(id)) is War war && war.hasKingdom(kingdom)) return true;
        return false;
    }

    // 军队可以开往任何战线：出征或举国总动员
    public static bool Unbounded(Kingdom kingdom) => Level(kingdom) >= Total || Dispatched(kingdom);

    public static bool Dispatch(Kingdom kingdom, War war)
    {
        if (kingdom?.data == null || Alive(war) == null) return false;
        var data = kingdom.GetOrCreate();
        data.expedition_wars ??= new List<long>();
        if (data.expedition_wars.Contains(war.data.id)) return false;
        data.expedition_wars.Add(war.data.id);
        _builtAt = -1d;
        return true;
    }

    public static void ReportShortfall(Kingdom kingdom)
    {
        if (kingdom?.data != null) Shortfall.Add(kingdom.id);
    }

    // 这座城按战时兵额征兵(战备状态)
    public static bool InScope(City city)
    {
        if (city?.data == null || city.kingdom == null || World.world == null) return false;
        double now = World.world.getCurWorldTime();
        // 每个游戏月重算一次(征兵按月结；以前每个游戏秒，开快时一秒重算好几次，性能日志里每帧 6.5 毫秒)
        if (!ReferenceEquals(_world, World.world) || _builtAt < 0d || now < _builtAt || now - _builtAt >= RebuildSeconds)
        {
            _world = World.world;
            _builtAt = now;
            Rebuild();
        }
        return Scope.Contains(city.id);
    }

    private static War Alive(War war) => war?.data != null && war.isAlive() && !war.hasEnded() ? war : null;

    private static bool AtWar(Kingdom kingdom) => kingdom.hasEnemies() || FrontSystem.Realm(kingdom).hasEnemies();

    private static bool Occupied(City city) =>
        city?.data != null && !city.isRekt() && city.GetOrCreate().OccupiedStatus?.Any(pair => pair.Value?.Count > 0) == true;

    private const double RebuildSeconds = 5d;

    private static void Rebuild()
    {
        using var timing = FrameProfiler.Measure("就近动员·重算");
        Scope.Clear();
        var warring = new HashSet<Kingdom>();
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral() && AtWar(kingdom)) warring.Add(kingdom);
        // 各帝国(按核心)名下在打仗的国(见 CollectHostiles)
        ByRealm.Clear();
        foreach (Kingdom kingdom in warring)
        {
            Kingdom realm = FrontSystem.Realm(kingdom);
            if (!ByRealm.TryGetValue(realm, out List<Kingdom> members)) ByRealm[realm] = members = new List<Kingdom>();
            members.Add(kingdom);
        }
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() || kingdom.cities == null ||
                kingdom.cities.Count == 0) continue;
            bool war = warring.Contains(kingdom);
            if (!war && !CityPopulationSystem.IsMobilizing(kingdom)) continue;
            // 出师先集结：就近的兵不够看时范围翻倍、京师的军也上(见 WarMuster)
            int level = war ? Math.Max(Level(kingdom), Dispatched(kingdom) ? Wider : 0) : WarMuster.Wide(kingdom) ? Wider : 0;
            HostileCenters.Clear();
            if (war)
            {
                CollectHostiles(kingdom, warring);
                if (VerifyHostiles) VerifyAgainstScan(kingdom, warring);
                foreach (Kingdom other in Hostiles) AddCenters(other);
            }
            else AddCenters(CityPopulationSystem.EnemyOf(kingdom));
            float radius = NearDistance * (level >= Wider ? 2f : 1f);
            // 军团只驻在军镇(都城、军区首都、州郡，见 CityPopulationSystem.PostOf)：范围里得有军镇才有兵可出
            CityPopulationSystem.ArmyDoctrine doctrine = CityPopulationSystem.DoctrineOf(kingdom);
            bool Post(City city) => CityPopulationSystem.PostOf(city, doctrine) != CityPopulationSystem.ArmyPost.None;
            bool garrisoned = false;
            City staging = null;
            float stagingDistance = float.MaxValue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                float nearest = Nearest(city);
                if (level >= Total || nearest <= radius * radius || Occupied(city))
                {
                    Scope.Add(city.id);
                    garrisoned |= Post(city);
                }
                else if (nearest < stagingDistance && Post(city))
                {
                    staging = city;
                    stagingDistance = nearest;
                }
            }
            if (level >= Wider && kingdom.capital?.data != null && !kingdom.capital.isRekt())
            {
                Scope.Add(kingdom.capital.id);
                garrisoned = true;
            }
            // 范围里没有驻军的军镇(跨海、或边境只有不驻军的小城)：交战的主方从离敌城最近的军镇出兵；随宗主参战的远方成员不出。
            // 以前挑离敌城最近的任意一座城，多半是不驻军的港口小城，一兵也出不了；本国又没吃紧、动员不会扩大，
            // 隔海的仗就永远没人去打(玩家反馈"两个隔海的国家一千五百年没打下来")
            // 随中央集结的成员国(见 WarMuster)只出就近的兵，范围里没有军镇就不出
            if (!garrisoned && staging != null && (war ? LeadsWar(kingdom) : !WarMuster.Following(kingdom))) Scope.Add(staging.id);
        }
    }

    // 这座城离那一国最近的城有多远(格，按城心)；那国没有城时为极大
    public static float DistanceTo(City city, Kingdom target)
    {
        float best = float.MaxValue;
        if (city?.data == null || target?.cities == null) return best;
        Vector2 at = city.city_center;
        foreach (City other in target.cities)
        {
            if (other?.data == null || other.isRekt()) continue;
            float distance = (other.city_center - at).sqrMagnitude;
            if (distance < best) best = distance;
        }
        return best == float.MaxValue ? best : Mathf.Sqrt(best);
    }

    // 这一国有城在那一国的就近范围里(随中央集结的成员按这个挑，见 WarMuster)
    public static bool Near(Kingdom kingdom, Kingdom target)
    {
        if (kingdom?.cities == null || target?.cities == null) return false;
        foreach (City city in kingdom.cities)
            if (city?.data != null && !city.isRekt() && DistanceTo(city, target) <= NearDistance) return true;
        return false;
    }

    private static readonly Dictionary<Kingdom, List<Kingdom>> ByRealm = new();
    private static readonly HashSet<Kingdom> Hostiles = new();
    // 排查用(独立核心的 --verify-scope)：敌方照老办法(把在打仗的国逐个问 FrontSystem.Hostile)再找一遍，对不上的记数
    public static bool VerifyHostiles;
    public static int HostileChecks, HostileMismatches;

    // 这一国的敌方(在打仗的国里同它敌对的)：同逐个问 FrontSystem.Hostile 是一个结果，只是从本国与本帝国核心的敌国名单出发找——
    // 敌国名单里的国、敌国名单里某个帝国核心名下在打仗的成员。以前四百国各把三百多个在打仗的国问一遍(十几万次)，
    // 重算一回五毫秒，一个月要重算四五回
    private static void CollectHostiles(Kingdom kingdom, HashSet<Kingdom> warring)
    {
        Hostiles.Clear();
        Kingdom realm = FrontSystem.Realm(kingdom);
        // 本国自己的敌人：不论同不同帝国都算；敌人是别的帝国的核心，那个帝国在打仗的成员也算
        foreach (Kingdom enemy in kingdom.getEnemiesKingdoms())
        {
            if (enemy?.data == null || enemy == kingdom || enemy.isRekt()) continue;
            if (warring.Contains(enemy)) Hostiles.Add(enemy);
            if (enemy != realm && ByRealm.TryGetValue(enemy, out List<Kingdom> members))
                foreach (Kingdom member in members)
                    if (member != kingdom) Hostiles.Add(member);
        }
        // 本帝国核心的敌人(不在本帝国里的)：它自己、它是别的帝国的核心时名下在打仗的成员
        foreach (Kingdom enemy in realm.getEnemiesKingdoms())
        {
            if (enemy?.data == null || enemy == kingdom || enemy.isRekt()) continue;
            if (warring.Contains(enemy) && FrontSystem.Realm(enemy) != realm) Hostiles.Add(enemy);
            if (enemy != realm && ByRealm.TryGetValue(enemy, out List<Kingdom> members))
                foreach (Kingdom member in members)
                    if (member != kingdom) Hostiles.Add(member);
        }
    }

    private static void VerifyAgainstScan(Kingdom kingdom, HashSet<Kingdom> warring)
    {
        HostileChecks++;
        int scanned = 0;
        bool same = true;
        foreach (Kingdom other in warring)
        {
            if (!FrontSystem.Hostile(kingdom, other)) continue;
            scanned++;
            if (!Hostiles.Contains(other)) same = false;
        }
        if (same && scanned == Hostiles.Count) return;
        if (HostileMismatches++ < 5)
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][就近动员] 核对不上：{kingdom.GetKingdomName()} 逐个问得 {scanned} 国，按名单找得 {Hostiles.Count} 国");
    }

    private static void AddCenters(Kingdom kingdom)
    {
        if (kingdom?.cities == null || kingdom.isRekt()) return;
        foreach (City city in kingdom.cities)
            if (city?.data != null && !city.isRekt()) HostileCenters.Add(city.city_center);
    }

    private static float Nearest(City city)
    {
        float best = float.MaxValue;
        Vector2 at = city.city_center;
        foreach (Vector2 center in HostileCenters)
        {
            float distance = (center - at).sqrMagnitude;
            if (distance < best) best = distance;
        }
        return best;
    }

    private static bool LeadsWar(Kingdom kingdom) =>
        kingdom.getWars().Any(war => Alive(war) != null && (war.main_attacker == kingdom || war.main_defender == kingdom));

    // 每月(征兵前)：吃紧升级、缓和降级；中央吃紧时诏令成员出征
    public static void MonthTick(Kingdom kingdom)
    {
        var data = kingdom.GetOrCreate();
        if (!AtWar(kingdom))
        {
            data.war_mobilization_level = 0;
            data.war_calm_months = 0;
            data.war_pressure_months = 0;
            data.war_gain_peak = 0;
            data.war_stall_months = 0;
            data.expedition_wars?.Clear();
            Shortfall.Remove(kingdom.id);
            return;
        }
        data.expedition_wars?.RemoveAll(id => Alive(World.world.wars.get(id)) == null);
        bool losing = Shortfall.Remove(kingdom.id) || kingdom.cities?.Any(Occupied) == true;
        bool stalled = Stalled(kingdom, data);
        int before = data.war_mobilization_level;
        if (!losing && stalled)
        {
            // 攻不动：不算吃紧(不往举国总动员上数)，范围扩到一级、也不往回降，后方的军才上得来
            data.war_calm_months = 0;
            data.war_mobilization_level = Math.Max(before, Wider);
        }
        else if (losing)
        {
            data.war_calm_months = 0;
            data.war_pressure_months++;
            int cities = kingdom.cities?.Count ?? 0;
            bool severe = Occupied(kingdom.capital) ||
                          cities > 0 && kingdom.cities.Count(Occupied) >= Math.Max(2, Mathf.CeilToInt(cities * SevereShare));
            int target = data.war_pressure_months >= TotalMonths && severe ? Total
                : data.war_pressure_months >= WiderMonths ? Wider : 0;
            data.war_mobilization_level = Math.Max(before, target);
        }
        else
        {
            data.war_pressure_months = Math.Max(0, data.war_pressure_months - 1);
            if (before > 0 && ++data.war_calm_months >= CalmMonthsToEase)
            {
                data.war_calm_months = 0;
                data.war_mobilization_level = before - 1;
            }
        }
        if (data.war_mobilization_level != before)
        {
            _builtAt = -1d;
            if (data.war_mobilization_level > before)
                FrontSystem.Log(kingdom, LM.Get(data.war_mobilization_level >= Total ? "corps_log_mobilize_total" : "corps_log_mobilize_wider"));
        }
        if (data.war_mobilization_level >= Wider) OrderMembers(kingdom);
    }

    // 攻不动(用户 2026-11-16 "大量的士兵都没有进攻欲望"：以前只有本国吃紧才扩大动员，打顺风仗、僵持着的时候后方的军永远不上来)：
    // 有军在进攻，战果(本国城数 ×1000 + 本国或宗主名下占着的敌方地块数)却连着 StallMonths 个月没再创新高
    private static bool Stalled(Kingdom kingdom, KingdomExtension.KingdomExtraData data)
    {
        bool attacking = false;
        foreach (EmpireCraft.Scripts.Data.CorpsData corps in data.corps ?? new List<EmpireCraft.Scripts.Data.CorpsData>())
            if (corps.task == "attack" && corps.strength >= 1f)
            {
                attacking = true;
                break;
            }
        if (!attacking)
        {
            data.war_stall_months = 0;
            return false;
        }
        int gain = (kingdom.cities?.Count ?? 0) * 1000;
        Kingdom realm = FrontSystem.Realm(kingdom);
        foreach (Kingdom other in World.world.kingdoms)
        {
            if (other?.data == null || other.isRekt() || other.cities == null || !FrontSystem.Hostile(kingdom, other)) continue;
            foreach (City city in other.cities)
            {
                Dictionary<long, List<int>> occupied = city?.data == null || city.isRekt() ? null : city.GetOrCreate().OccupiedStatus;
                if (occupied == null || occupied.Count == 0) continue;
                if (occupied.TryGetValue(kingdom.id, out List<int> own) && own != null) gain += own.Count;
                if (realm != kingdom && occupied.TryGetValue(realm.id, out List<int> held) && held != null) gain += held.Count;
            }
        }
        if (gain > data.war_gain_peak)
        {
            data.war_gain_peak = gain;
            data.war_stall_months = 0;
            return false;
        }
        // 认定攻不动的那个月把最高数落回眼下：往后比这时多拿一块地就算又动了，不因早先丢过地一直算攻不动
        if (++data.war_stall_months == StallMonths) data.war_gain_peak = gain;
        return data.war_stall_months >= StallMonths;
    }

    private const int StallMonths = 6;

    // 中央吃紧：诏令听命于中央、随中央参战的成员出征(战国时天子已号令不动诸侯)
    private static void OrderMembers(Kingdom core)
    {
        Empire empire = core.GetEmpire();
        if (empire?.CoreKingdom != core || empire.kingdoms_list == null || ZhouEraSystem.Era(empire) >= ZhouEraSystem.Warring)
            return;
        // 哈里发国边远的行省不奉诏远征，仗打到家门口才就近应战(用户 2026-10-21 选"边远行省不听调遣"，见 EmirateSystem.DistantProvinces)
        HashSet<long> distant = EmirateSystem.DistantProvinces(empire);
        foreach (War war in core.getWars().ToList())
        {
            if (Alive(war) == null) continue;
            bool attacking = war.isAttacker(core);
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (member?.data == null || member == core || member.isRekt() || member.IsFactionRebelling() ||
                    member.IsLocalRebelling() || !EmpirePeacePatch.Obeys(empire, member) || distant.Contains(member.id) ||
                    !(attacking ? war.isAttacker(member) : war.isDefender(member)) || !Dispatch(member, war)) continue;
                FrontSystem.Log(core, string.Format(LM.Get("corps_log_court_order"), member.GetKingdomName()));
                FrontSystem.Log(member, LM.Get("corps_log_obey_order"));
            }
        }
    }
}
