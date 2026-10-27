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
//   · 修路(每月)：各国同时开几项工程(一项起，每八座城多一项，好大喜功之君再多一项)，先修通都城的、人口多的、好修的；
//     每月按等级施工几个地形单位，按造价从国库出(建造)，钱不够就停工；修到头这条路才通(升级时旧路照常用)；
//     施工要征发民力，按施工的地形单位记工役(徭役，见 CorveeSystem)；
//     一条路两头都是本国的城，或另一头是同一帝国的城，才由本国来修；
//   · 养路(每年)：造价的 5% 由路一头所在的国家出(维护)，付不起路况变差，三年失修降一等，土路失修就荒了；
//     两头有战事的路每月受损；
//   · 旧存档(世界已过三十年、还没有任何路)：路网规划算完后按各国现有科技一次铺好，之后照常养护、升级。
//   · 跨国的路：两国议定通路之约(决议，见 RoadTreatySystem)才修，哪一国开工都行，造价两国各出一半(对方出不起由开工的一方垫上)；
//   · 统一了的帝国(用户 2026-10-17 "统一了也能使用国库的钱来修路和维护道路""把不连通的联通")：帝国核心也出钱修境内
//     任意两城之间的路，工程数按全帝国的城数算；地方出不起养路钱的由中央国库代付；春秋、战国的周室不算统一。
//   · 把不连通的连通：挑新工程时，一头已通到本国(帝国)都城的路网、一头还没通的路最优先。
//   · 神力：连路(点两座城当场修通一串干道，见 ConnectByPower)；笔刷画的路按格另存，同样养护(见 RoadPaintSystem)。
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

    // 神力画的路(见 RoadPaintSystem)：一格一年的养路钱(同等级干道一个地形单位的养路钱按格折算)
    internal static float UpkeepPerTile(int grade) =>
        Cost[Mathf.Clamp(grade, 0, MaxGrade)] * UpkeepShare / RoadPaintSystem.TilesPerUnit;

    // 统一了的帝国替地方代付养路钱的中央；没有为 null
    internal static Kingdom CentralPayer(Kingdom payer)
    {
        Kingdom core = payer?.GetEmpire()?.CoreKingdom;
        return core != null && core != payer && CenterOf(core) != null ? core : null;
    }

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
            // 不修路的城(游牧营地，见 CityStyleSystem.Roadless)的旧路随之废弃，不再养护
            list.RemoveAll(link => link == null || link.grade <= 0 && link.building <= 0 ||
                                   !(World.world.cities.get(link.to) is City other) || other.isRekt() ||
                                   CityStyleSystem.Roadless(city) || CityStyleSystem.Roadless(other));
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
        using var timing = FrameProfiler.Measure("路网·修路月结");
        bool changed = false;
        // 战事毁路
        foreach (var pair in Links)
            if (pair.Value.grade > 0 && (FrontSystem.Battles.ContainsKey(pair.Key.Item1) || FrontSystem.Battles.ContainsKey(pair.Key.Item2)))
                changed |= Damage(pair.Value, WarDamagePerMonth);
        // 在修的工程按出资国分好、能修的路线按国家分好、修通的路连成的图，每月各建一次
        // (以前每国各扫一遍全部工程与全部路线、各建一遍图，二百多国的大图每月上百毫秒，性能日志"路网·结算"峰值四百多毫秒)
        var projectsBy = new Dictionary<long, List<(long, long)>>();
        foreach (var pair in Links)
        {
            if (pair.Value.building <= 0) continue;
            if (!projectsBy.TryGetValue(pair.Value.payer, out List<(long, long)> list)) projectsBy[pair.Value.payer] = list = new List<(long, long)>();
            list.Add(pair.Key);
        }
        Dictionary<Kingdom, List<(long, long)>> candidates = Candidates();
        Dictionary<long, List<long>> graph = Graph();
        var reachedBy = new Dictionary<long, HashSet<long>>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.cities == null || kingdom.cities.Count == 0) continue;
            int active = 0;
            if (projectsBy.TryGetValue(kingdom.id, out List<(long, long)> projects))
                foreach ((long, long) key in projects)
                {
                    RoadLink link = Links[key];
                    changed |= Advance(kingdom, key, link);
                    if (link.building > 0) active++;
                }
            // 统一了的帝国：中央的工程数按全帝国的城数算
            int cities = CenterOf(kingdom) is Layer.Empire center
                ? center.kingdoms_list.Sum(member => member?.cities?.Count ?? 0) : kingdom.cities.Count;
            // 好大喜功之君多开一项工程(见 CorveeSystem)
            int slots = 1 + cities / CitiesPerProject + CorveeSystem.ExtraRoadProjects(kingdom);
            if (active < slots && candidates.TryGetValue(kingdom, out List<(long, long)> plans))
                changed |= StartProject(kingdom, plans, graph, reachedBy);
        }
        changed |= RoadPaintSystem.MonthlyWarDamage(WarDamagePerMonth);
        if (changed) RoadNetwork.MarkDirty();
    }

    // 各国可能修的路线：一头的城归它的(本国、同帝国、通路之约的都在其中)，统一了的帝国的中央另收境内全部路线；是否真能修仍看 Owns
    private static Dictionary<Kingdom, List<(long, long)>> Candidates()
    {
        var result = new Dictionary<Kingdom, List<(long, long)>>();
        void Add(Kingdom kingdom, (long, long) key)
        {
            if (kingdom == null) return;
            if (!result.TryGetValue(kingdom, out List<(long, long)> list)) result[kingdom] = list = new List<(long, long)>();
            if (list.Count == 0 || list[list.Count - 1] != key) list.Add(key);
        }
        foreach (var pair in RoadNetwork.Plans)
        {
            if (pair.Value.zones.Count == 0) continue;
            Kingdom a = World.world.cities.get(pair.Key.Item1)?.kingdom, b = World.world.cities.get(pair.Key.Item2)?.kingdom;
            Add(a, pair.Key);
            Add(b, pair.Key);
            Kingdom core = a?.GetEmpire()?.CoreKingdom;
            if (core != null && core != a && core != b && CenterOf(core) != null) Add(core, pair.Key);
        }
        return result;
    }

    // 修通的路(任何等级)连成的图
    private static Dictionary<long, List<long>> Graph()
    {
        var next = new Dictionary<long, List<long>>();
        foreach (var pair in Links)
        {
            if (pair.Value.grade <= 0) continue;
            if (!next.TryGetValue(pair.Key.Item1, out List<long> a)) next[pair.Key.Item1] = a = new List<long>();
            if (!next.TryGetValue(pair.Key.Item2, out List<long> b)) next[pair.Key.Item2] = b = new List<long>();
            a.Add(pair.Key.Item2);
            b.Add(pair.Key.Item1);
        }
        return next;
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
        // 通路之约的路：对方出一半(出不起由本国垫上)
        Kingdom partner = TreatyPartner(kingdom, key);
        int share = partner != null && partner.GetMoney() >= cost / 2 ? cost / 2 : 0;
        if (cost - share > 0 && kingdom.GetMoney() < cost - share) return false;
        if (cost - share > 0) kingdom.SubMoney(cost - share, TreasuryCategory.Construction);
        if (share > 0) partner.SubMoney(share, TreasuryCategory.Construction);
        link.progress += step;
        // 修路征发民力：按这个月施工的地形单位记工役(见 CorveeSystem)
        CorveeSystem.Add(kingdom, step * CorveeSystem.RoadPerTerrain, false);
        if (link.progress < plan.terrain - 0.01f) return true;
        link.grade = link.building;
        link.building = 0;
        link.progress = 0f;
        link.condition = 1f;
        return true;
    }

    // 挑一项新工程：先修通都城的、人口多的、好修的；国库至少要够三个月的施工费
    private static bool StartProject(Kingdom kingdom, List<(long, long)> keys, Dictionary<long, List<long>> graph,
        Dictionary<long, HashSet<long>> reachedBy)
    {
        int target = GradeFor(kingdom);
        City capital = kingdom.capital;
        // 把不连通的连通：已经由修通的路连到都城的城(帝国用帝国核心的都城)；同一都城这个月只算一遍
        City from = kingdom.GetEmpire()?.CoreKingdom?.capital ?? capital;
        long fromId = from?.data?.id ?? -1L;
        if (!reachedBy.TryGetValue(fromId, out HashSet<long> reached)) reachedBy[fromId] = reached = Reached(from, graph);
        (long, long) best = default;
        float bestScore = float.MinValue;
        int bestGrade = target;
        foreach ((long, long) key in keys)
        {
            if (RoadNetwork.PlanOf(key) is not RoadNetwork.RoadPlan plan) continue;
            var pair = new KeyValuePair<(long, long), RoadNetwork.RoadPlan>(key, plan);
            // 每条路按两头城市风格能修的最高一级(林地只修土路，见 RoadNetwork.GradeCap)：本国路级高了也照修这一级，不整条跳过
            int grade = Mathf.Min(target, RoadNetwork.GradeCap(pair.Key));
            if (plan.zones.Count == 0 || grade <= 0 || !plan.Buildable(grade) || !Owns(kingdom, pair.Key)) continue;
            RoadLink link = Get(pair.Key);
            if (link != null && (link.building > 0 || link.grade >= grade)) continue;
            City a = World.world.cities.get(pair.Key.Item1), b = World.world.cities.get(pair.Key.Item2);
            bool joins = reached.Contains(pair.Key.Item1) != reached.Contains(pair.Key.Item2);
            float score = (joins ? 1500f : 0f) + (a == capital || b == capital ? 1000f : 0f) +
                          (CityPopulationSystem.GetBackgroundTotal(a) + CityPopulationSystem.GetBackgroundTotal(b)) / 100f -
                          plan.terrain * 2f - (link?.grade ?? 0) * 50f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = pair.Key;
            bestGrade = grade;
        }
        if (bestScore == float.MinValue) return false;
        RoadLink chosen = Get(best);
        float monthly = Speed[bestGrade] * (Cost[bestGrade] - Cost[chosen?.grade ?? 0]);
        if (kingdom.GetMoney() < monthly * 3f) return false;
        chosen ??= Create(best);
        if (chosen == null) return false;
        chosen.building = bestGrade;
        chosen.progress = 0f;
        chosen.payer = kingdom.id;
        return true;
    }

    // 本国能修这条路：一头是本国的城，另一头是本国或同一帝国的城，或与本国议定了通路之约的邻国的城；
    // 统一了的帝国的核心：境内任意两城之间
    private static bool Owns(Kingdom kingdom, (long, long) key)
    {
        Kingdom a = World.world.cities.get(key.Item1)?.kingdom, b = World.world.cities.get(key.Item2)?.kingdom;
        if (a != kingdom && b != kingdom)
            return CenterOf(kingdom) is Layer.Empire center && a != null && b != null && a.GetEmpire() == center &&
                   b.GetEmpire() == center;
        Kingdom other = a == kingdom ? b : a;
        if (other == kingdom) return true;
        var empire = kingdom.GetEmpire();
        if (empire != null && other != null && other.GetEmpire() == empire) return true;
        return RoadTreatySystem.Has(kingdom, other);
    }

    // 通路之约的路：另一头是议定了通路之约、不在同一帝国的邻国
    private static Kingdom TreatyPartner(Kingdom kingdom, (long, long) key)
    {
        Kingdom a = World.world.cities.get(key.Item1)?.kingdom, b = World.world.cities.get(key.Item2)?.kingdom;
        Kingdom other = a == kingdom ? b : b == kingdom ? a : null;
        if (other == null || other == kingdom || kingdom.GetEmpire() != null && other.GetEmpire() == kingdom.GetEmpire()) return null;
        return RoadTreatySystem.Has(kingdom, other) ? other : null;
    }

    // 统一了的帝国：本国是帝国核心(春秋、战国的周室不算)
    private static Layer.Empire CenterOf(Kingdom kingdom)
    {
        Layer.Empire empire = kingdom?.GetEmpire();
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom != kingdom) return null;
        return ZhouEraSystem.Era(empire) >= ZhouEraSystem.Spring ? null : empire;
    }

    // 由修通的路(任何等级)连到 from 的城
    private static HashSet<long> Reached(City from, Dictionary<long, List<long>> next)
    {
        var reached = new HashSet<long>();
        if (from?.data == null) return reached;
        var queue = new Queue<long>();
        reached.Add(from.data.id);
        queue.Enqueue(from.data.id);
        while (queue.Count > 0)
            if (next.TryGetValue(queue.Dequeue(), out List<long> list))
                foreach (long id in list)
                    if (reached.Add(id)) queue.Enqueue(id);
        return reached;
    }

    // 养路：造价的 5% 由路一头所在的国家出，付不起路况变差
    private static void YearTick()
    {
        RoadTreatySystem.Yearly();
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
            // 统一了的帝国：地方出不起，中央国库代付
            Kingdom core = payer?.GetEmpire()?.CoreKingdom;
            if (core != null && core != payer && CenterOf(core) != null && core.GetMoney() >= upkeep)
            {
                if (upkeep > 0) core.SubMoney(upkeep, TreasuryCategory.Maintenance);
                link.condition = Mathf.Min(1f, link.condition + 0.25f);
                continue;
            }
            changed |= Damage(link, DecayPerYear);
        }
        changed |= RoadPaintSystem.YearlyUpkeep(DecayPerYear);
        if (changed) RoadNetwork.MarkDirty();
    }

    // 神力连路(用户 2026-10-22，选"当场修通一串干道""照常养护")：从 from 沿规划好的相邻城干道找代价最小的一串到 to，
    // 当场修通、不要钱，之后照常养护。每段按两头国家科技能修的最高一级(不超过城市风格上限)，地形太险修不通的往上提到
    // 修得通的那一级，还修不通的段不走；已修到这一级的路走起来便宜(尽量沿旧路)，比要修的等级高的不降。
    // 返回这一串共几段(连不上为 -1)；built 是新修或加修的段数
    public static int ConnectByPower(City from, City to, out int built)
    {
        built = 0;
        if (from?.data == null || to?.data == null || from == to || World.world?.cities == null) return -1;
        Refresh();
        var edges = new Dictionary<long, List<(long other, (long, long) key, int grade, float cost)>>();
        void Add(long a, long b, (long, long) key, int grade, float cost)
        {
            if (!edges.TryGetValue(a, out var list)) edges[a] = list = new List<(long, (long, long), int, float)>();
            list.Add((b, key, grade, cost));
        }
        foreach (var pair in RoadNetwork.Plans)
        {
            int grade = PowerGrade(pair.Key, pair.Value);
            if (grade <= 0) continue;
            RoadLink existing = Get(pair.Key);
            float cost = Mathf.Max(0.1f, pair.Value.terrain) * (existing != null && existing.grade >= grade ? 0.3f : 1f);
            Add(pair.Key.Item1, pair.Key.Item2, pair.Key, grade, cost);
            Add(pair.Key.Item2, pair.Key.Item1, pair.Key, grade, cost);
        }
        // 城数不过几百：朴素的最短路就够
        long start = from.data.id, goal = to.data.id;
        var distance = new Dictionary<long, float> { [start] = 0f };
        var back = new Dictionary<long, (long previous, (long, long) key, int grade)>();
        var done = new HashSet<long>();
        while (true)
        {
            long node = -1L;
            float best = float.MaxValue;
            foreach (var pair in distance)
                if (!done.Contains(pair.Key) && pair.Value < best)
                {
                    best = pair.Value;
                    node = pair.Key;
                }
            if (node < 0 || node == goal) break;
            done.Add(node);
            if (!edges.TryGetValue(node, out var list)) continue;
            foreach (var (other, key, grade, cost) in list)
            {
                if (done.Contains(other) || distance.TryGetValue(other, out float known) && known <= best + cost) continue;
                distance[other] = best + cost;
                back[other] = (node, key, grade);
            }
        }
        if (!back.ContainsKey(goal)) return -1;
        int segments = 0;
        for (long node = goal; node != start; node = back[node].previous)
        {
            var (_, key, grade) = back[node];
            segments++;
            RoadLink link = Get(key) ?? Create(key);
            if (link == null || link.grade >= grade) continue;
            link.grade = grade;
            // 在修更高一级的工程照修(以前一律清掉，已花的钱和进度白丢)
            if (link.building <= grade)
            {
                link.building = 0;
                link.progress = 0f;
            }
            link.condition = 1f;
            built++;
        }
        RoadNetwork.RefreshNow();
        RoadPaintSystem.RequestRedraw();
        return segments;
    }

    // 神力修这段路的等级：两头国家科技能修的最高一级，修不通往上提，到城市风格上限还修不通为 0
    private static int PowerGrade((long, long) key, RoadNetwork.RoadPlan plan)
    {
        int cap = RoadNetwork.GradeCap(key);
        if (cap <= 0 || plan.zones.Count == 0) return 0;
        Kingdom a = World.world.cities.get(key.Item1)?.kingdom, b = World.world.cities.get(key.Item2)?.kingdom;
        int grade = Mathf.Clamp(Math.Max(GradeFor(a), GradeFor(b)), 1, cap);
        while (grade < cap && !plan.Buildable(grade)) grade++;
        return plan.Buildable(grade) ? grade : 0;
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
