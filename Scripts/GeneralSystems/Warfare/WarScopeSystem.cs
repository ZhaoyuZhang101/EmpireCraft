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
//     与敌国不接壤(跨海)时，从离敌城最近的一座城出兵；谋划开战时只有谋划的国家在朝向目标国的一侧集结；
//   · 打不过了逐级扩大：本国有城被敌军占着，或受攻的城分不出足够守军，算吃紧(吃紧一个月加一、缓和一个月减一)——
//     一级：吃紧满四个月，范围翻倍，京师禁军出征；二级：吃紧满一年、而且确实危急(都城被占，或三成以上的城有地块被占)，
//     举国总动员。连续三个月没吃紧降一级，太平时归零(以前吃紧一个月就升一级，打仗总有地块被占，第二个月就举国总动员、
//     远方的军开往前线)；
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
        if (!ReferenceEquals(_world, World.world) || _builtAt < 0d || now < _builtAt || now - _builtAt >= 1d)
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

    private static void Rebuild()
    {
        using var timing = FrameProfiler.Measure("就近动员·重算");
        Scope.Clear();
        var warring = new List<Kingdom>();
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral() && AtWar(kingdom)) warring.Add(kingdom);
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() || kingdom.cities == null ||
                kingdom.cities.Count == 0) continue;
            bool war = warring.Contains(kingdom);
            if (!war && !CityPopulationSystem.IsMobilizing(kingdom)) continue;
            int level = war ? Math.Max(Level(kingdom), Dispatched(kingdom) ? Wider : 0) : 0;
            HostileCenters.Clear();
            if (war)
            {
                foreach (Kingdom other in warring)
                    if (FrontSystem.Hostile(kingdom, other)) AddCenters(other);
            }
            else AddCenters(CityPopulationSystem.EnemyOf(kingdom));
            float radius = NearDistance * (level >= Wider ? 2f : 1f);
            bool any = false;
            City staging = null;
            float stagingDistance = float.MaxValue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                float nearest = Nearest(city);
                if (level >= Total || nearest <= radius * radius || Occupied(city))
                {
                    Scope.Add(city.id);
                    any = true;
                }
                else if (nearest < stagingDistance)
                {
                    staging = city;
                    stagingDistance = nearest;
                }
            }
            if (level >= Wider && kingdom.capital?.data != null && !kingdom.capital.isRekt()) Scope.Add(kingdom.capital.id);
            // 与敌国不接壤(跨海)：交战的主方从离敌城最近的城出兵；随宗主参战的远方成员不出
            if (!any && staging != null && (!war || LeadsWar(kingdom))) Scope.Add(staging.id);
        }
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
            data.expedition_wars?.Clear();
            Shortfall.Remove(kingdom.id);
            return;
        }
        data.expedition_wars?.RemoveAll(id => Alive(World.world.wars.get(id)) == null);
        bool losing = Shortfall.Remove(kingdom.id) || kingdom.cities?.Any(Occupied) == true;
        int before = data.war_mobilization_level;
        if (losing)
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

    // 中央吃紧：诏令听命于中央、随中央参战的成员出征(战国时天子已号令不动诸侯)
    private static void OrderMembers(Kingdom core)
    {
        Empire empire = core.GetEmpire();
        if (empire?.CoreKingdom != core || empire.kingdoms_list == null || ZhouEraSystem.Era(empire) >= ZhouEraSystem.Warring)
            return;
        foreach (War war in core.getWars().ToList())
        {
            if (Alive(war) == null) continue;
            bool attacking = war.isAttacker(core);
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (member?.data == null || member == core || member.isRekt() || member.IsFactionRebelling() ||
                    member.IsLocalRebelling() || !EmpirePeacePatch.Obeys(empire, member) ||
                    !(attacking ? war.isAttacker(member) : war.isDefender(member)) || !Dispatch(member, war)) continue;
                FrontSystem.Log(core, string.Format(LM.Get("corps_log_court_order"), member.GetKingdomName()));
                FrontSystem.Log(member, LM.Get("corps_log_obey_order"));
            }
        }
    }
}
