using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 文化辐射(用户 2026-10-16 定："文化辐射也存在范围……按照地形和距离来判断辐射范围，比如有山川和长城，文化的传播就会减缓甚至衰减")：
//   · 城际阻力图：每座城一个点；接壤的城、同一块陆地上相距不远的城之间连线，线的阻力 = 两城之间最省力那条路上各地块的阻力之和
//     (见 TerrainResistance：山、过河、长城难走，有路好走)；港口之间在本国水师辐射内(见 StraitReach)可走海路；跨入别国再 +1。
//   · 文化中心：每座城都向外辐射本城的主文化，势能 = √户数，京师 ×2、王国都城 ×1.5，庙宇、书院、大学各加一截；
//     沿阻力图按 e^(−阻力/衰减距离) 衰减，衰减距离由本国科技拉长(文字起步，印刷、科学方法、电力、计算机逐级加)，三倍衰减距离外不算。
//   · 每座城收到的各文化辐射之和，折成比例(本城自己的辐射在内，所以腹地总是本文化为主，边地、山后、长城外才混杂)：
//       - 城市文化比例的目标三成看辐射、七成看本城人口(见 CultureService.ApplyPopulationPressure)；
//       - 背景人口同化、同化政令的速度乘 0.4 + 1.2 × 官方文化在本城的辐射比例(山高皇帝远的地方同化慢)；
//       - 科技接触：辐射到本城一成五以上的文化也算接触过(见 TechnologySystem.RebuildContacts)。
//   · 每年重算一次，分帧做(每帧至多 2 毫秒)；读档后第一次算完之前，各处都当没有辐射数据(照旧)。
//   · 游戏里叫"教化"(贴合模组的帝国主题，用户 2026-10-16："要贴合现在系统和模组名称的主题")，地图上见教化图层(Layer/RadianceMap.cs)。
public static class CultureRadiation
{
    private const float RealmCapitalFactor = 2f, CapitalFactor = 1.5f;
    private const float TempleBonus = 0.15f, LibraryBonus = 0.25f, UniversityBonus = 0.35f;
    private const int BuildingCap = 3;
    private const float BaseLength = 8f;
    private static readonly (string tech, float length)[] LengthTechs =
        { ("writing", 2f), ("printing", 4f), ("scientific_method", 3f), ("electricity", 5f), ("computing", 8f) };
    private const float CutoffLengths = 3f;
    private const int LinkTiles = 64;
    private const int MaxExpand = 2500;
    private const float BorderCost = 1f, SeaEmbark = 2f, SeaTilesPerCost = 16f;
    public const float PressureWeight = 0.3f;
    // 科技接触：辐射比例到这么多算接触过
    public const float ContactShare = 0.15f;
    private const float MinShare = 0.01f;
    private const double FrameMilliseconds = 2d;

    private enum Phase { Idle, Links, Sources }

    private static Phase _phase = Phase.Idle;
    private static object _world;
    private static double _lastPass = -1d;
    private static int _cursor;
    private static readonly List<City> Cities = new();
    private static readonly Dictionary<long, City> ById = new();
    private static readonly List<(City a, City b)> Links = new();
    private static readonly Dictionary<long, Dictionary<long, float>> NewEdges = new();
    private static readonly Dictionary<long, List<(long to, float cost)>> Sea = new();
    private static readonly List<(City city, string culture, float strength, float length)> Sources = new();
    private static readonly Dictionary<long, Dictionary<string, float>> Fields = new();
    private static Dictionary<long, Dictionary<string, float>> _shares = new();
    private static Dictionary<long, float> _intensity = new();
    // 各城教化最盛的文化(图层每帧要问，推算完就记下)
    private static Dictionary<long, (string culture, float share)> _dominant = new();
    private static readonly MinHeap<TileZone> ZoneHeap = new();
    private static readonly Dictionary<TileZone, float> ZoneCost = new();
    private static readonly HashSet<TileZone> ZoneDone = new();
    private static readonly MinHeap<long> CityHeap = new();
    private static readonly Dictionary<long, float> CityDone = new();

    // 结果每更新一次加一(图层据此重画)
    public static int Version { get; private set; }

    public static void ResetWorldState()
    {
        _phase = Phase.Idle;
        _world = null;
        _lastPass = -1d;
        _cursor = 0;
        Cities.Clear();
        ById.Clear();
        Links.Clear();
        NewEdges.Clear();
        Sea.Clear();
        Sources.Clear();
        Fields.Clear();
        _shares = new Dictionary<long, Dictionary<string, float>>();
        _intensity = new Dictionary<long, float>();
        _dominant = new Dictionary<long, (string culture, float share)>();
        Version++;
    }

    #region 查询

    public static bool Ready => _shares.Count > 0;

    // 本城收到的各文化辐射比例(0~1，合计 1)；还没算过返回 null
    public static IReadOnlyDictionary<string, float> SharesOf(City city) =>
        city?.data != null && _shares.TryGetValue(city.id, out Dictionary<string, float> shares) ? shares : null;

    // 某文化在本城的辐射比例；还没算过返回 -1
    public static float Share(City city, string culture)
    {
        IReadOnlyDictionary<string, float> shares = SharesOf(city);
        if (shares == null || string.IsNullOrEmpty(culture)) return -1f;
        return shares.TryGetValue(culture, out float share) ? share : 0f;
    }

    // 同化速度倍数：0.4 + 1.2 × 该文化在本城的辐射比例(没有数据时 1)
    public static float AssimilationFactor(City city, string culture)
    {
        float share = Share(city, culture);
        return share < 0f ? 1f : 0.4f + 1.2f * share;
    }

    // 本城收到的辐射总量
    public static float Intensity(City city) =>
        city?.data != null && _intensity.TryGetValue(city.id, out float value) ? value : 0f;

    public static (string culture, float share) Dominant(City city) =>
        city?.data != null && _dominant.TryGetValue(city.id, out var best) ? best : (null, 0f);

    #endregion

    #region 分帧结算

    // 每帧(EmpireCraftStrategicScheduler)；每年一轮
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        if (!SimulationFrameBudget.HasTime) return;
        var clock = Stopwatch.StartNew();
        try
        {
            using (FrameProfiler.Measure("文化辐射"))
            {
                if (_phase == Phase.Idle)
                {
                    double now = world.getCurWorldTime();
                    if (_lastPass >= 0d && now >= _lastPass && Date.getYearsSince(_lastPass) < 1) return;
                    _lastPass = now;
                    Begin();
                }
                while (_phase != Phase.Idle && clock.Elapsed.TotalMilliseconds < FrameMilliseconds) Step();
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][文化辐射] 结算失败: {exception.Message}");
            _phase = Phase.Idle;
        }
    }

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.kingdom != null && !city.kingdom.wild &&
                                           city.zones != null && city.zones.Count > 0 && !AncientWarfareCompatibility.OwnsObject(city);

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static void Begin()
    {
        Cities.Clear();
        ById.Clear();
        Links.Clear();
        NewEdges.Clear();
        Sea.Clear();
        Sources.Clear();
        Fields.Clear();
        foreach (City city in World.world.cities.list)
        {
            if (!Valid(city) || city.getTile() == null) continue;
            Cities.Add(city);
            ById[city.id] = city;
        }
        // 连线：接壤的城；同一块陆地上相距不过 LinkTiles 格的城(按 LinkTiles 见方的格子找)
        var seen = new HashSet<(long, long)>();
        void Link(City a, City b)
        {
            if (a == b || !ById.ContainsKey(a.id) || !ById.ContainsKey(b.id)) return;
            (long, long) key = a.id < b.id ? (a.id, b.id) : (b.id, a.id);
            if (seen.Add(key)) Links.Add((a, b));
        }
        var grid = new Dictionary<(int, int), List<City>>();
        foreach (City city in Cities)
        {
            WorldTile tile = city.getTile();
            (int, int) cell = (tile.x / LinkTiles, tile.y / LinkTiles);
            if (!grid.TryGetValue(cell, out List<City> list)) grid[cell] = list = new List<City>();
            list.Add(city);
        }
        foreach (City city in Cities)
        {
            foreach (City neighbour in city.neighbours_cities) Link(city, neighbour);
            WorldTile tile = city.getTile();
            int cx = tile.x / LinkTiles, cy = tile.y / LinkTiles;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy), out List<City> list)) continue;
                    foreach (City other in list)
                    {
                        if (other == city || other.id < city.id) continue;
                        WorldTile at = other.getTile();
                        if (Toolbox.SquaredDistTile(tile, at) <= LinkTiles * LinkTiles && tile.isSameIsland(at)) Link(city, other);
                    }
                }
        }
        _cursor = 0;
        _phase = Phase.Links;
    }

    private static void Step()
    {
        if (_phase == Phase.Links)
        {
            if (_cursor >= Links.Count)
            {
                BuildSea();
                BuildSources();
                _cursor = 0;
                _phase = Phase.Sources;
                return;
            }
            (City a, City b) = Links[_cursor++];
            if (!Valid(a) || !Valid(b)) return;
            float cost = LandCost(AdministrativeControlSystem.CenterZone(a), AdministrativeControlSystem.CenterZone(b), a.kingdom, b.kingdom);
            if (cost < 0f) return;
            AddEdge(a.id, b.id, cost);
            AddEdge(b.id, a.id, cost);
            return;
        }
        if (_cursor >= Sources.Count)
        {
            Commit();
            return;
        }
        var source = Sources[_cursor++];
        if (Valid(source.city)) Radiate(source.city.id, source.culture, source.strength, source.length);
    }

    private static void AddEdge(long from, long to, float cost)
    {
        if (!NewEdges.TryGetValue(from, out Dictionary<long, float> edges)) NewEdges[from] = edges = new Dictionary<long, float>();
        edges[to] = cost;
    }

    // 两城城心之间最省力那条路的阻力(A*，按地块；找不到返回 -1)。城墙上的关门(见 WallGateSystem)两城所属的国都放行才不加城墙阻力
    private static float LandCost(TileZone from, TileZone to, Kingdom fromKingdom, Kingdom toKingdom)
    {
        if (from == null || to == null) return -1f;
        if (from == to) return 0f;
        ZoneHeap.Clear();
        ZoneCost.Clear();
        ZoneDone.Clear();
        ZoneCost[from] = 0f;
        ZoneHeap.Push(Estimate(from, to), from);
        int expanded = 0;
        while (ZoneHeap.Count > 0 && expanded < MaxExpand)
        {
            TileZone zone = ZoneHeap.Pop().item;
            if (!ZoneDone.Add(zone)) continue;
            expanded++;
            float cost = ZoneCost[zone];
            if (zone == to) return cost;
            foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
            {
                if (next == null || ZoneDone.Contains(next)) continue;
                float step = TerrainResistance.Step(next, fromKingdom, toKingdom);
                if (step < 0f) continue;
                float total = cost + step;
                if (ZoneCost.TryGetValue(next, out float old) && old <= total) continue;
                ZoneCost[next] = total;
                ZoneHeap.Push(total + Estimate(next, to), next);
            }
        }
        return -1f;
    }

    private static float Estimate(TileZone a, TileZone b) => (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)) * TerrainResistance.RoadFactor;

    // 海路：港口之间，在出发一方水师辐射内、同一片水域
    private static void BuildSea()
    {
        var ports = new List<(City city, HashSet<int> basins)>();
        foreach (City city in Cities)
        {
            if (NavySystem.Port(city) == null) continue;
            var basins = new HashSet<int>(SeaRegionSystem.RegionsAround(city).Select(region => region.Basin));
            if (basins.Count > 0) ports.Add((city, basins));
        }
        foreach (var from in ports)
        {
            float radius = StraitReach.Radius(from.city.kingdom);
            if (radius <= 0f) continue;
            foreach (var to in ports)
            {
                if (to.city == from.city) continue;
                float distance = (from.city.city_center - to.city.city_center).magnitude;
                if (distance > radius || !from.basins.Overlaps(to.basins)) continue;
                if (!Sea.TryGetValue(from.city.id, out List<(long, float)> list)) Sea[from.city.id] = list = new List<(long, float)>();
                list.Add((to.city.id, SeaEmbark + distance / SeaTilesPerCost));
            }
        }
    }

    private static void BuildSources()
    {
        var lengths = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (City city in Cities)
        {
            string culture = CultureService.GetMainCulture(city, false);
            if (!CultureService.IsValidCulture(culture)) continue;
            float strength = Mathf.Sqrt(Mathf.Max(1, CityPopulationSystem.Households(city)));
            Kingdom realm = Realm(city.kingdom);
            if (realm?.capital == city) strength *= RealmCapitalFactor;
            else if (city.kingdom.capital == city) strength *= CapitalFactor;
            strength *= 1f + TempleBonus * Mathf.Min(BuildingCap, city.countBuildingsType("type_temple")) +
                        LibraryBonus * Mathf.Min(BuildingCap, city.countBuildingsType("type_library")) +
                        UniversityBonus * Mathf.Min(BuildingCap, city.countBuildingsType("type_university"));
            // 衰减距离看本国(施教的一方)的科技
            string techCulture = TechnologySystem.GetCultureOf(city);
            if (!lengths.TryGetValue(techCulture ?? "", out float length))
            {
                length = BaseLength;
                if (!string.IsNullOrEmpty(techCulture))
                    foreach ((string tech, float add) in LengthTechs)
                        if (TechnologySystem.HasTech(techCulture, tech)) length += add;
                lengths[techCulture ?? ""] = length;
            }
            Sources.Add((city, culture, strength, length));
        }
    }

    // 从一座城向外辐射(有界的最短路)
    private static void Radiate(long sourceId, string culture, float strength, float length)
    {
        float cutoff = length * CutoffLengths;
        CityHeap.Clear();
        CityDone.Clear();
        CityHeap.Push(0f, sourceId);
        while (CityHeap.Count > 0)
        {
            (float distance, long id) = CityHeap.Pop();
            if (CityDone.ContainsKey(id)) continue;
            CityDone[id] = distance;
            if (!Fields.TryGetValue(id, out Dictionary<string, float> field)) Fields[id] = field = new Dictionary<string, float>(StringComparer.Ordinal);
            field.TryGetValue(culture, out float value);
            field[culture] = value + strength * Mathf.Exp(-distance / length);
            if (!ById.TryGetValue(id, out City city) || !Valid(city)) continue;
            Kingdom realm = Realm(city.kingdom);
            if (NewEdges.TryGetValue(id, out Dictionary<long, float> land))
                foreach (KeyValuePair<long, float> edge in land) Relax(edge.Key, edge.Value);
            if (Sea.TryGetValue(id, out List<(long to, float cost)> sea))
                foreach ((long to, float cost) in sea) Relax(to, cost);

            void Relax(long to, float cost)
            {
                if (CityDone.ContainsKey(to) || !ById.TryGetValue(to, out City next) || !Valid(next)) return;
                float total = distance + cost + (Realm(next.kingdom) != realm ? BorderCost : 0f);
                if (total <= cutoff) CityHeap.Push(total, to);
            }
        }
    }

    private static void Commit()
    {
        var shares = new Dictionary<long, Dictionary<string, float>>();
        var intensity = new Dictionary<long, float>();
        foreach (KeyValuePair<long, Dictionary<string, float>> pair in Fields)
        {
            float total = pair.Value.Values.Sum();
            if (total <= 0f) continue;
            var normalized = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, float> culture in pair.Value)
            {
                float share = culture.Value / total;
                if (share >= MinShare) normalized[culture.Key] = share;
            }
            float kept = normalized.Values.Sum();
            if (kept <= 0f) continue;
            foreach (string key in normalized.Keys.ToList()) normalized[key] /= kept;
            shares[pair.Key] = normalized;
            intensity[pair.Key] = total;
        }
        var dominant = new Dictionary<long, (string culture, float share)>();
        foreach (KeyValuePair<long, Dictionary<string, float>> pair in shares)
        {
            KeyValuePair<string, float> best = pair.Value.OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal).First();
            dominant[pair.Key] = (best.Key, best.Value);
        }
        _shares = shares;
        _intensity = intensity;
        _dominant = dominant;
        Version++;
        int mixed = shares.Values.Count(value => value.Values.Max() < 0.8f);
        LogService.LogInfo($"[EmpireCraft][文化辐射] {Cities.Count} 城、连线 {NewEdges.Values.Sum(edges => edges.Count) / 2}、" +
                           $"海路 {Sea.Values.Sum(list => list.Count)}、文化混杂(主文化不到八成)的城 {mixed}");
        Fields.Clear();
        Sources.Clear();
        Links.Clear();
        _phase = Phase.Idle;
    }

    #endregion
}
