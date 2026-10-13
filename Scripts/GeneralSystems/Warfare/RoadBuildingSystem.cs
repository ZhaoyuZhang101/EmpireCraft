using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一条路(存在两城里 id 小的那座城，见 CityExtraData.road_links)：通往哪座城、等级、在修的等级与进度、路况、出钱的国家
public sealed class RoadLink
{
    public long to = -1L;
    public int grade;
    public int building;
    public float progress;
    public float condition = 1f;
    public long payer = -1L;
}

// 修路与养路：路不是生来就有的，要国家出钱一段一段修，修好了每年还要出钱养护。
//   · 等级按本文化的科技：土路(起初) → 石板驿道(砌石) → 官道(营造，能架桥) → 碎石公路(工业化，能开隧道) → 柏油公路(摩托化)；
//     等级越高造价越高，能翻越的地形越难(地形代价可以是直线距离的 1.5 → 3.5 倍)，所以早期只有平原谷地有路，山区后来才通；
//   · 修路(每月)：各国同时开几项工程(一项起，每八座城多一项)，先修通都城的、人口多的、好修的；
//     每月按等级施工几个地形单位，按造价从国库出(建造)，钱不够就停工；修到头这条路才通(升级时旧路照常用)；
//     一条路两头都是本国的城，或另一头是同一帝国的城，才由本国来修；
//   · 养路(每年)：造价的 5% 由路一头所在的国家出(维护)，付不起路况变差，三年失修降一等，土路失修就荒了；
//     两头有战事的路每月受损；
//   · 旧存档(世界已过三十年、还没有任何路)：路网规划算完后按各国现有科技一次铺好，之后照常养护、升级。
// 路的走向见 RoadNetwork(路网规划)，画法见 RoadOverlay，行军走路见 CorpsSystem。
public static class RoadBuildingSystem
{
    public const int MaxGrade = 5;
    // 各等级：每地形单位造价、每月施工地形单位、能翻越的地形(代价/直线距离)
    private static readonly float[] Cost = { 0f, 2f, 5f, 10f, 20f, 40f };
    private static readonly float[] Speed = { 0f, 3f, 2f, 2f, 4f, 6f };
    private static readonly float[] Detour = { 0f, 1.5f, 1.8f, 2.3f, 3f, 3.5f };
    private const float UpkeepShare = 0.05f, DecayPerYear = 0.34f, WarDamagePerMonth = 0.08f;
    private const int CitiesPerProject = 8;
    private const float SeedAfterYears = 30f;

    private static readonly Dictionary<(long, long), RoadLink> Links = new();
    private static double _lastMonth = -1d;
    private static int _months;
    private static bool? _seed;
    private static object _world;

    public static IReadOnlyDictionary<(long, long), RoadLink> All => Links;

    public static float DetourLimit(int grade) => Detour[Mathf.Clamp(grade, 1, MaxGrade)];

    public static void ResetWorldState()
    {
        Links.Clear();
        _lastMonth = -1d;
        _months = 0;
        _seed = null;
        _world = null;
    }

    // 本国科技能修的最高等级
    public static int GradeFor(Kingdom kingdom)
    {
        string culture = kingdom == null ? null : TechnologySystem.GetCultureOf(kingdom);
        if (string.IsNullOrEmpty(culture) || !TechnologySystem.IsActiveFor(culture)) return 2;
        if (TechnologySystem.HasTech(culture, "motorization")) return 5;
        if (TechnologySystem.HasTech(culture, "industrialization")) return 4;
        if (TechnologySystem.HasTech(culture, "architecture")) return 3;
        return TechnologySystem.HasTech(culture, "masonry") ? 2 : 1;
    }

    public static RoadLink Get((long, long) key) => Links.TryGetValue(key, out RoadLink link) ? link : null;

    // 从各城的存档重建索引(城没了的路随之消失)
    public static void Refresh()
    {
        if (!ReferenceEquals(_world, World.world))
        {
            ResetWorldState();
            _world = World.world;
        }
        Links.Clear();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt()) continue;
            List<RoadLink> list = city.GetOrCreate().road_links;
            if (list == null || list.Count == 0) continue;
            list.RemoveAll(link => link == null || link.grade <= 0 && link.building <= 0 ||
                                   !(World.world.cities.get(link.to) is City other) || other.isRekt());
            foreach (RoadLink link in list) Links[(city.data.id, link.to)] = link;
        }
    }

    private static RoadLink Create((long, long) key)
    {
        City lower = World.world.cities.get(key.Item1);
        if (lower?.data == null) return null;
        var link = new RoadLink { to = key.Item2 };
        (lower.GetOrCreate().road_links ??= new List<RoadLink>()).Add(link);
        Links[key] = link;
        return link;
    }

    // 每帧由 RoadNetwork.Tick 调；按游戏时间过一个月结算一次
    public static void Tick()
    {
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        bool first = _lastMonth < 0d;
        _lastMonth = now;
        if (first) return;
        try
        {
            MonthTick();
            if (++_months % 12 == 0) YearTick();
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][修路] 结算失败: {exception.Message}");
        }
    }

    // 旧存档：路网规划算完后一次按各国科技铺好
    public static void SeedIfOldWorld()
    {
        _seed ??= Links.Count == 0 && Date.getYearsSince(0d) > SeedAfterYears;
        if (_seed != true || !RoadNetwork.PlansComplete) return;
        _seed = false;
        foreach (var pair in RoadNetwork.Plans)
        {
            if (Get(pair.Key) != null || pair.Value.zones.Count == 0) continue;
            Kingdom owner = World.world.cities.get(pair.Key.Item1)?.kingdom;
            int grade = GradeFor(owner);
            while (grade > 0 && !pair.Value.Buildable(grade)) grade--;
            if (grade <= 0) continue;
            RoadLink link = Create(pair.Key);
            if (link == null) continue;
            link.grade = grade;
            link.payer = owner?.id ?? -1L;
        }
        RoadNetwork.MarkDirty();
    }

    private static void MonthTick()
    {
        bool changed = false;
        // 战事毁路
        foreach (var pair in Links)
            if (pair.Value.grade > 0 && (FrontSystem.Battles.ContainsKey(pair.Key.Item1) || FrontSystem.Battles.ContainsKey(pair.Key.Item2)))
                changed |= Damage(pair.Value, WarDamagePerMonth);
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.cities == null || kingdom.cities.Count == 0) continue;
            var projects = Links.Where(pair => pair.Value.building > 0 && pair.Value.payer == kingdom.id).ToList();
            foreach (var project in projects) changed |= Advance(kingdom, project.Key, project.Value);
            int slots = 1 + kingdom.cities.Count / CitiesPerProject;
            if (projects.Count(project => project.Value.building > 0) < slots) changed |= StartProject(kingdom);
        }
        if (changed) RoadNetwork.MarkDirty();
    }

    private static bool Advance(Kingdom kingdom, (long, long) key, RoadLink link)
    {
        RoadNetwork.RoadPlan plan = RoadNetwork.PlanOf(key);
        if (plan == null || plan.zones.Count == 0 || !Owns(kingdom, key))
        {
            // 路线没了或两头都不归本国了：停工
            link.building = 0;
            link.progress = 0f;
            return true;
        }
        float step = Mathf.Min(Speed[link.building], plan.terrain - link.progress);
        int cost = Mathf.CeilToInt(step * (Cost[link.building] - Cost[link.grade]));
        if (cost > 0 && kingdom.GetMoney() < cost) return false;
        if (cost > 0) kingdom.SubMoney(cost, TreasuryCategory.Construction);
        link.progress += step;
        if (link.progress < plan.terrain - 0.01f) return true;
        link.grade = link.building;
        link.building = 0;
        link.progress = 0f;
        link.condition = 1f;
        return true;
    }

    // 挑一项新工程：先修通都城的、人口多的、好修的；国库至少要够三个月的施工费
    private static bool StartProject(Kingdom kingdom)
    {
        int target = GradeFor(kingdom);
        City capital = kingdom.capital;
        (long, long) best = default;
        float bestScore = float.MinValue;
        foreach (var pair in RoadNetwork.Plans)
        {
            RoadNetwork.RoadPlan plan = pair.Value;
            if (plan.zones.Count == 0 || !plan.Buildable(target) || !Owns(kingdom, pair.Key)) continue;
            RoadLink link = Get(pair.Key);
            if (link != null && (link.building > 0 || link.grade >= target)) continue;
            City a = World.world.cities.get(pair.Key.Item1), b = World.world.cities.get(pair.Key.Item2);
            float score = (a == capital || b == capital ? 1000f : 0f) +
                          (CityPopulationSystem.GetBackgroundTotal(a) + CityPopulationSystem.GetBackgroundTotal(b)) / 100f -
                          plan.terrain * 2f - (link?.grade ?? 0) * 50f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = pair.Key;
        }
        if (bestScore == float.MinValue) return false;
        RoadLink chosen = Get(best);
        float monthly = Speed[target] * (Cost[target] - Cost[chosen?.grade ?? 0]);
        if (kingdom.GetMoney() < monthly * 3f) return false;
        chosen ??= Create(best);
        if (chosen == null) return false;
        chosen.building = target;
        chosen.progress = 0f;
        chosen.payer = kingdom.id;
        return true;
    }

    // 本国能修这条路：一头是本国的城，另一头是本国或同一帝国的城
    private static bool Owns(Kingdom kingdom, (long, long) key)
    {
        Kingdom a = World.world.cities.get(key.Item1)?.kingdom, b = World.world.cities.get(key.Item2)?.kingdom;
        if (a != kingdom && b != kingdom) return false;
        Kingdom other = a == kingdom ? b : a;
        if (other == kingdom) return true;
        var empire = kingdom.GetEmpire();
        return empire != null && other != null && other.GetEmpire() == empire;
    }

    // 养路：造价的 5% 由路一头所在的国家出，付不起路况变差
    private static void YearTick()
    {
        bool changed = false;
        foreach (var pair in Links.ToList())
        {
            RoadLink link = pair.Value;
            if (link.grade <= 0) continue;
            RoadNetwork.RoadPlan plan = RoadNetwork.PlanOf(pair.Key);
            Kingdom payer = World.world.cities.get(pair.Key.Item1)?.kingdom ?? World.world.cities.get(pair.Key.Item2)?.kingdom;
            int upkeep = Mathf.CeilToInt((plan?.terrain ?? 5f) * Cost[link.grade] * UpkeepShare);
            if (payer?.data != null && !payer.isRekt() && !payer.wild && payer.GetMoney() >= upkeep)
            {
                if (upkeep > 0) payer.SubMoney(upkeep, TreasuryCategory.Maintenance);
                link.condition = Mathf.Min(1f, link.condition + 0.25f);
                continue;
            }
            changed |= Damage(link, DecayPerYear);
        }
        if (changed) RoadNetwork.MarkDirty();
    }

    // 路况变差；坏到底降一等(土路就荒了)
    private static bool Damage(RoadLink link, float amount)
    {
        link.condition -= amount;
        if (link.condition > 0f) return false;
        link.grade--;
        link.condition = 1f;
        return true;
    }
}
