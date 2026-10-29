using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using Newtonsoft.Json;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图各时代雨林、大泽的开发度(用户 2026-10-29 "然后之前说的沼泽雨林什么的开发度也要在剧本模式里面体现出来,
// 比如春秋战国到漢南方都没怎么开发的")：地球地表网格(earth.bytes)是按今天的气候烘的，岭南、闽、江南、云梦全是草地，
// 开局一样是熟地；这里按时代把当年没开出来的地改回雨林、大泽，再给开发度(见 WildernessDevelopmentSystem)：
//   · 数据：WorldMaps/development.json 的若干片(经纬框或多边形，先写的优先)，每片定改成雨林还是大泽、各时代的开发度
//     (战国岭南、闽、黔、海南近乎零，江东、云梦一二成；东汉岭南一成；三国江东四五成；盛唐江南七八成、岭南四成；
//     南宋江南开满、闽粤六七成；明末除黔、桂西、武陵、海南腹地外都开满，见各片的 note)；没写这个时代、或开满(100)的不动；
//   · 离城越近越高：给的是远离城的腹地的数，地块按离最近的城心多远(度，经向按纬度折算)× (1 + e^(−距离/Reach))，
//     城边上近两倍，到 100 为止——郡县治所一带先开、山里最后(秦汉岭南只有番禺、苍梧几处治所周围是熟地)；算出来开满的不改；
//   · 先改地形：地块中心落在哪一片，这块地里有草木的平地格(草地、稀树草原、桦林、枫林的高低两种)改成同一高度的丛林或沼泽格
//     (高地仍是高地；丘陵、山地、沙地、水面、田、路都不动，所以多山的云南、台湾改了也成不了片，不列)；
//     城心那块、有房屋的、村镇厂区占着的、古战争兼容模组的城的地块留给城不改(同 WildernessSystem.Regroup 的 anchored)；
//     这一类格子占不到陆地一半的(山丘、水面太多)成不了无人区，也不改；
//   · 再当场划区(WildernessSystem.BuildNow，平时它等进世界安排做完才划)：改成丛林、沼泽的地块照门槛连片，
//     开局铺满时归了城的从城里划出来；排在进世界安排的最后(别的步骤盖房、铺村镇在前，留给城的地块才算得准)；
//   · 最后设开发度：划进雨林、大泽无人区的地块按上面算的开发度设(SetDevelopment 当场把相应比例的格子改回草地，
//     从挨着城的一侧改起；这时地块已不归城，开发中的照记下的类型留在无人区)；开发度为零的整块留着丛林、沼泽；
//     没划进去的(城心一带、零碎成不了片的、被判成山脉的)改过的格子原样改回。
// 只在剧本开局做一次(进世界安排随剧本排，读档不再做)；改了的格子、开发度随存档。几百块地、几万格，一次做完。
public sealed class WorldMapDevArea
{
    public string id = "";
    public string zh = "";
    // 改成哪一类：jungle 雨林、swamp 大泽
    public string kind = "jungle";
    // 经纬框 [西, 南, 东, 北]，可几个
    public List<float[]> box = new();
    // 多边形(顶点 [经度, 纬度] 顺次相连)，可几个
    public List<List<float[]>> poly = new();
    // 各时代的开发度(时代 id → 0~100，远离城的腹地的)；没写的时代按 all，all 也没写(为负)的这一片这个时代不动
    public Dictionary<string, float> dev = new();
    public float all = -1f;

    // 范围的外框(先比外框再细比)、是不是大泽
    [JsonIgnore] public float West, South, East, North;
    [JsonIgnore] public bool Swamp;
}

public static class WorldMapDevelopment
{
    // 离城越近开发度越高：腹地的数 × (1 + e^(−距离/Reach))，距离按度
    private const float Reach = 1f;
    // 陆地不到几格的地块不改(同 WildernessSystem.MinLand)；这一类格子要占陆地几成才改(同 WildernessSystem 外沿的门槛)
    private const int MinLand = 16;
    private const float WetShare = 0.5f;

    private sealed class AreaFile
    {
        public List<WorldMapDevArea> areas = new();
    }

    private static List<WorldMapDevArea> _areas;

    // 改了格子的一块地：在哪一片、要设的开发度、改过的格子在 changed/original 两张表里的起止
    private struct Plot
    {
        public TileZone Zone;
        public int Area;
        public float Dev;
        public int Start, Count;
    }

    #region 数据

    private static bool EnsureAreas()
    {
        if (_areas != null) return _areas.Count > 0;
        _areas = new List<WorldMapDevArea>();
        string path = Path.Combine(WorldMapData.Folder, "development.json");
        try
        {
            if (File.Exists(path))
                _areas = JsonConvert.DeserializeObject<AreaFile>(File.ReadAllText(path))?.areas ?? new List<WorldMapDevArea>();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 开发度表读不出来 {path}: {exception.Message}");
        }
        _areas.RemoveAll(area => area == null || string.IsNullOrEmpty(area.id) || area.kind != "jungle" && area.kind != "swamp");
        foreach (WorldMapDevArea area in _areas)
        {
            area.box = area.box?.Where(b => b != null && b.Length >= 4).ToList() ?? new List<float[]>();
            area.poly = area.poly?.Where(ring => ring != null && ring.Count >= 3 && ring.All(p => p != null && p.Length >= 2)).ToList() ??
                        new List<List<float[]>>();
            area.dev ??= new Dictionary<string, float>();
            area.Swamp = area.kind == "swamp";
            Measure(area);
        }
        return _areas.Count > 0;
    }

    private static void Measure(WorldMapDevArea area)
    {
        area.West = area.South = float.MaxValue;
        area.East = area.North = float.MinValue;
        foreach (float[] b in area.box)
        {
            area.West = Mathf.Min(area.West, b[0]);
            area.South = Mathf.Min(area.South, b[1]);
            area.East = Mathf.Max(area.East, b[2]);
            area.North = Mathf.Max(area.North, b[3]);
        }
        foreach (float[] p in area.poly.SelectMany(ring => ring))
        {
            area.West = Mathf.Min(area.West, p[0]);
            area.South = Mathf.Min(area.South, p[1]);
            area.East = Mathf.Max(area.East, p[0]);
            area.North = Mathf.Max(area.North, p[1]);
        }
    }

    // 这一片这个时代的开发度；不动为负
    private static float ValueIn(WorldMapDevArea area, string era) =>
        area.dev.TryGetValue(era ?? "", out float value) ? value : area.all;

    // 这个经纬度落在哪一片(先写的优先)；都不在为 -1
    private static int AreaAt(float lon, float lat)
    {
        for (int i = 0; i < _areas.Count; i++)
        {
            WorldMapDevArea area = _areas[i];
            if (lon < area.West || lon > area.East || lat < area.South || lat > area.North) continue;
            foreach (float[] b in area.box)
                if (lon >= b[0] && lon <= b[2] && lat >= b[1] && lat <= b[3]) return i;
            foreach (List<float[]> ring in area.poly)
                if (Inside(ring, lon, lat)) return i;
        }
        return -1;
    }

    // 点在不在多边形里(射线法)
    private static bool Inside(List<float[]> ring, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            float xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    #endregion

    #region 开局

    // 进世界安排的最后一步(WorldMapScenario.QueueAfterLoad 的"雨林大泽开发度")
    public static void Apply(WorldMapEra era, WorldMapFrame frame, Vector2Int size)
    {
        if (era == null || frame == null || size.x <= 0 || size.y <= 0 || !WorldMapWilderness.OnEarth) return;
        // 无人区关着：没有雨林大泽可开发，地形也不改
        if (!WildernessSystem.Enabled) return;
        List<TileZone> zones = World.world?.zone_calculator?.zones;
        if (zones == null || !EnsureAreas()) return;
        var values = new float[_areas.Count];
        bool any = false;
        for (int i = 0; i < _areas.Count; i++)
        {
            values[i] = ValueIn(_areas[i], era.id);
            any |= values[i] >= 0f && values[i] < WildernessDevelopmentSystem.Full;
        }
        if (!any) return;
        TopTileType jungleLow = AssetManager.top_tiles.get("jungle_low"), jungleHigh = AssetManager.top_tiles.get("jungle_high");
        TopTileType swampLow = AssetManager.top_tiles.get("swamp_low"), swampHigh = AssetManager.top_tiles.get("swamp_high");
        if (jungleLow == null || jungleHigh == null || swampLow == null || swampHigh == null)
        {
            LogService.LogWarning("[EmpireCraft][世界地图] 找不到丛林、沼泽的顶层格，雨林大泽开发度不设");
            return;
        }
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        frame.Bounds(size, out float bottom, out float degrees);
        HashSet<int> kept = KeptZones();
        List<Vector2Int> seats = Seats();
        var plots = new List<Plot>();
        var changed = new List<WorldTile>();
        var original = new List<TopTileType>();

        // 一、改地形
        foreach (TileZone zone in zones)
        {
            WorldTile center = zone?.centerTile;
            if (center == null || zone.tiles == null || zone.id < 0 || kept.Contains(zone.id)) continue;
            float lon = frame.west + (center.x + 0.5f) * degrees, lat = bottom + (center.y + 0.5f) * degrees;
            int index = AreaAt(lon, lat);
            if (index < 0) continue;
            float value = values[index];
            if (value < 0f || value >= WildernessDevelopmentSystem.Full || !Convertible(zone)) continue;
            float dev = value <= 0f ? 0f
                : Mathf.Min(WildernessDevelopmentSystem.Full, value * (1f + Mathf.Exp(-SeatDistance(center, lat, degrees, seats) / Reach)));
            if (dev >= WildernessDevelopmentSystem.Full) continue;
            bool swamp = _areas[index].Swamp;
            TopTileType low = swamp ? swampLow : jungleLow, high = swamp ? swampHigh : jungleHigh;
            int start = changed.Count;
            foreach (WorldTile tile in zone.tiles)
            {
                if (!Vegetated(tile, out bool upland)) continue;
                changed.Add(tile);
                original.Add(tile.top_type);
                // 同 WildernessDevelopmentSystem.Clear 改回草地：不闪白点，地面格不动(高地仍是高地)
                MapAction.terraformTop(tile, upland ? high : low, TerraformLibrary.nothing);
            }
            plots.Add(new Plot { Zone = zone, Area = index, Dev = dev, Start = start, Count = changed.Count - start });
        }
        if (plots.Count == 0) return;

        // 二、当场划区(见类头)
        WildernessSystem.BuildNow();

        // 三、划进雨林大泽的设开发度，没划进去的改回
        var wildCount = new int[_areas.Count];
        var devSum = new float[_areas.Count];
        int wild = 0, restored = 0;
        foreach (Plot plot in plots)
        {
            WildKind kind = WildernessSystem.RegionOf(plot.Zone)?.Kind ?? WildKind.None;
            if (WildernessDevelopmentSystem.Develops(kind))
            {
                wild++;
                wildCount[plot.Area]++;
                devSum[plot.Area] += plot.Dev;
                if (plot.Dev > 0f) WildernessDevelopmentSystem.SetDevelopment(plot.Zone, plot.Dev);
                continue;
            }
            for (int i = plot.Start; i < plot.Start + plot.Count; i++)
            {
                WorldTile tile = changed[i];
                if (original[i] != null && IsWet(tile.top_type)) MapAction.terraformTop(tile, original[i], TerraformLibrary.nothing);
            }
            if (plot.Count > 0) restored++;
        }
        var summary = new List<string>();
        for (int i = 0; i < _areas.Count; i++)
            if (wildCount[i] > 0) summary.Add($"{_areas[i].zh} {wildCount[i]} 块均 {devSum[i] / wildCount[i]:0}");
        LogService.LogInfo($"[EmpireCraft][世界地图] {era.id} 雨林大泽开发度：{plots.Count} 块地改了 {changed.Count} 格，" +
                           $"{wild} 块划进无人区({string.Join("、", summary)})，{restored} 块没成片改回，用时 {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    // 留给城不改的地块：城心那块、村镇厂区占着的、古战争兼容模组的城的(同 WildernessSystem.Regroup 的 anchored)
    private static HashSet<int> KeptZones()
    {
        var kept = new HashSet<int>();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            if (AncientWarfareCompatibility.OwnsObject(city))
            {
                foreach (TileZone zone in city.zones)
                    if (zone != null) kept.Add(zone.id);
                continue;
            }
            TileZone seat = city.getTile()?.zone;
            if (seat != null) kept.Add(seat.id);
        }
        foreach (int id in RuralSettlementSystem.SettledZones()) kept.Add(id);
        foreach (int id in FactorySystem.CoveredZones()) kept.Add(id);
        return kept;
    }

    // 各城城心(格子坐标)：开发度按离最近的城多远算
    private static List<Vector2Int> Seats()
    {
        var seats = new List<Vector2Int>();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            WorldTile seat = city.getTile();
            if (seat != null) seats.Add(new Vector2Int(seat.x, seat.y));
        }
        return seats;
    }

    // 离最近的城心几度(经向按纬度折算)；没有城为无穷远(只按腹地的数)
    private static float SeatDistance(WorldTile center, float lat, float degrees, List<Vector2Int> seats)
    {
        float squeeze = Mathf.Cos(lat * Mathf.Deg2Rad), best = float.MaxValue;
        foreach (Vector2Int seat in seats)
        {
            float dx = (seat.x - center.x) * squeeze, dy = seat.y - center.y;
            best = Mathf.Min(best, dx * dx + dy * dy);
        }
        return best == float.MaxValue ? float.MaxValue : Mathf.Sqrt(best) * degrees;
    }

    // 这块地改不改：陆地够 MinLand 格；没有房屋(城里的建筑，开局按人口盖的房不压在丛林里)；
    // 改完丛林、沼泽格(连原有的)能占到陆地一半(成得了无人区)
    private static bool Convertible(TileZone zone)
    {
        int land = 0, wet = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            if (type == null || type.liquid || type.lava) continue;
            land++;
            Building building = tile.building;
            if (building?.asset != null && building.asset.building_type == BuildingType.Building_Civ) return false;
            if (IsWet(tile.top_type) || Vegetated(tile, out _)) wet++;
        }
        return land >= MinLand && wet >= land * WetShare;
    }

    // 有草木的平地格(草地、稀树草原、桦林、枫林，高低两种)；upland：高地那一种
    private static bool Vegetated(WorldTile tile, out bool upland)
    {
        upland = false;
        TopTileType top = tile?.top_type;
        TileTypeBase type = tile?.Type;
        if (top == null || type == null || type.liquid || type.lava) return false;
        string id = top.id ?? "";
        upland = id.EndsWith("_high", StringComparison.Ordinal);
        if (!upland && !id.EndsWith("_low", StringComparison.Ordinal)) return false;
        return top.biome_id is "biome_grass" or "biome_savanna" or "biome_birch" or "biome_maple";
    }

    private static bool IsWet(TopTileType top) => top != null && top.biome_id is "biome_jungle" or "biome_swamp";

    #endregion
}
