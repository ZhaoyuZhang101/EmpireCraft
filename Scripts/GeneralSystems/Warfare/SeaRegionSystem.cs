using System;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 海区：海军(NavySystem)的地图单位。
//   · 一个地块区块(8×8 格)几乎全是水(陆地 ≤ 16 格)就算海；
//   · 连通的海面从一块未分的海开始向外广度扩张，满 RegionSize 块切成一个海区，大洋因此分成许多海区，制海权可以局部争夺；
//   · 记下海区之间的相邻关系(找航线用)、海区的中心块(画兵牌)、沿岸港口城(港口城的地块与海区相邻)。
// 读档或换地图后第一次用到时建一次；海陆变了(造岛、填海)最多每年重建一次。
// 以前不论变没变每年整图重划一次(排序全图地块、广度扩张)；现在设地面格(MapWallPatch)时看这块地算不算海变没变，
// 变了才记下要重划，到一年照旧重划，没变就不划；十年兜底重划一次
public static class SeaRegionSystem
{
    private const int RegionSize = 60;
    private const int MaxGround = 16;
    private const int FallbackYears = 10;

    public sealed class Region
    {
        public int Id;
        public readonly List<TileZone> Zones = new();
        public readonly HashSet<int> Neighbours = new();
        public TileZone Center;
        // 所在水域(连通的海区同一个编号)：隔着大陆的两片海到不了
        public int Basin = -1;
        // 制海权：最近一次海战胜出(或独自在场)的一方，及其时间
        public long ControllerId = -1L;
        public double ControlledAt = -1d;
    }

    private static readonly List<Region> Regions = new();
    private static readonly Dictionary<int, int> RegionByZone = new();
    private static object _world;
    private static double _builtAt = -1d;
    // 上次划分时各地块算不算海(按地块编号)；有地块变了就要重划
    private static bool[] _seaAtBuild = new bool[0];
    private static bool _dirty;

    public static IReadOnlyList<Region> All
    {
        get
        {
            EnsureBuilt();
            return Regions;
        }
    }

    public static void ResetWorldState()
    {
        Regions.Clear();
        RegionByZone.Clear();
        ShoreCache.Clear();
        _world = null;
        _builtAt = -1d;
        _seaAtBuild = new bool[0];
        _dirty = false;
    }

    // 设地面格之后(MapWallPatch)：这块地算不算海和上次划分时不一样了，记下要重划
    public static void OnTileChanged(TileZone zone)
    {
        if (_dirty || zone == null || zone.id < 0 || zone.id >= _seaAtBuild.Length) return;
        if (_seaAtBuild[zone.id] != IsSea(zone)) _dirty = true;
    }

    // 城的海岸：本城临海的陆上地块(中心格坐标)及外面海区所在的水域；Offset 是离城心最远的一处(按距离粗筛用)。
    // 水师辐射量到对方最近的海岸，不量城心(玩家反馈"两个隔海的国家一千五百年没打下来"：城心在内陆几十格的城，
    // 隔着窄海也"够不着")。按城缓存，一个月或城的地块数变了重算
    public sealed class Shore
    {
        public readonly List<(Vector2 at, int basin)> Points = new();
        public float Offset;
    }

    private static readonly Dictionary<long, (double at, int zones, Shore shore)> ShoreCache = new();

    public static Shore ShoreOf(City city)
    {
        var empty = new Shore();
        if (city?.zones == null || city.data == null || World.world == null) return empty;
        EnsureBuilt();
        double now = World.world.getCurWorldTime();
        if (ShoreCache.TryGetValue(city.id, out var cached) && cached.zones == city.zones.Count && now >= cached.at &&
            Date.getMonthsSince(cached.at) < 1)
            return cached.shore;
        var shore = new Shore();
        foreach (TileZone zone in city.zones)
        {
            if (zone?.centerTile == null || IsSea(zone)) continue;
            var at = new Vector2(zone.centerTile.x, zone.centerTile.y);
            var basins = new HashSet<int>();
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
                if (next != null && RegionByZone.TryGetValue(next.id, out int id) && basins.Add(Regions[id].Basin))
                    shore.Points.Add((at, Regions[id].Basin));
            if (basins.Count > 0) shore.Offset = Mathf.Max(shore.Offset, (at - city.city_center).magnitude);
        }
        if (ShoreCache.Count > 4096) ShoreCache.Clear();
        ShoreCache[city.id] = (now, city.zones.Count, shore);
        return shore;
    }

    public static bool IsSea(TileZone zone) =>
        zone != null && !zone.world_edge && zone.tiles_with_ground <= MaxGround && zone.tiles_with_liquid > 0;

    public static Region Get(int id)
    {
        EnsureBuilt();
        return id >= 0 && id < Regions.Count ? Regions[id] : null;
    }

    public static Region Of(TileZone zone)
    {
        EnsureBuilt();
        return zone != null && RegionByZone.TryGetValue(zone.id, out int id) ? Regions[id] : null;
    }

    private static void EnsureBuilt()
    {
        if (World.world?.zone_calculator == null) return;
        double now = World.world.getCurWorldTime();
        bool stale = !ReferenceEquals(_world, World.world) || Regions.Count == 0 && _builtAt < 0d ||
                     _builtAt >= 0d && (now < _builtAt ||
                                        Date.getYearsSince(_builtAt) >= (_dirty ? 1 : FallbackYears));
        if (!stale) return;
        _world = World.world;
        _builtAt = now;
        using var timing = FrameProfiler.Measure("海区·划分");
        try
        {
            Build();
            _dirty = false;
        }
        catch (Exception exception)
        {
            // 划分失败照旧一年后再试
            _dirty = true;
            LogService.LogWarning($"[EmpireCraft][海区] 划分失败: {exception.Message}");
        }
    }

    private static void Build()
    {
        // 记下这次划分时各地块算不算海
        List<TileZone> all = World.world.zone_calculator.zones;
        int size = all.Count;
        foreach (TileZone zone in all)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        if (_seaAtBuild.Length != size) _seaAtBuild = new bool[size];
        foreach (TileZone zone in all)
            if (zone != null && zone.id >= 0) _seaAtBuild[zone.id] = IsSea(zone);
        // 保留旧的制海权(按中心块对上)
        var oldControl = new Dictionary<int, (long, double)>();
        foreach (Region region in Regions)
            if (region.Center != null && region.ControllerId >= 0)
                oldControl[region.Center.id] = (region.ControllerId, region.ControlledAt);
        Regions.Clear();
        RegionByZone.Clear();
        ShoreCache.Clear();
        List<TileZone> zones = World.world.zone_calculator.zones;
        foreach (TileZone seed in zones.OrderBy(zone => zone.y).ThenBy(zone => zone.x))
        {
            if (!IsSea(seed) || RegionByZone.ContainsKey(seed.id)) continue;
            var region = new Region { Id = Regions.Count };
            var queue = new Queue<TileZone>();
            queue.Enqueue(seed);
            RegionByZone[seed.id] = region.Id;
            while (queue.Count > 0 && region.Zones.Count < RegionSize)
            {
                TileZone zone = queue.Dequeue();
                region.Zones.Add(zone);
                foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                {
                    if (!IsSea(next) || RegionByZone.ContainsKey(next.id)) continue;
                    if (region.Zones.Count + queue.Count >= RegionSize) break;
                    RegionByZone[next.id] = region.Id;
                    queue.Enqueue(next);
                }
            }
            // 排队但没轮到的块还给后面的海区
            foreach (TileZone left in queue) RegionByZone.Remove(left.id);
            Regions.Add(region);
        }
        // 中心块：离各块平均位置最近的一块(画兵牌、旗舰驶向的位置)
        foreach (Region region in Regions)
        {
            float cx = (float)region.Zones.Average(zone => zone.x), cy = (float)region.Zones.Average(zone => zone.y);
            region.Center = region.Zones.OrderBy(zone => (zone.x - cx) * (zone.x - cx) + (zone.y - cy) * (zone.y - cy))
                .First();
        }
        foreach (Region region in Regions)
            foreach (TileZone zone in region.Zones)
                foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                    if (next != null && RegionByZone.TryGetValue(next.id, out int other) && other != region.Id)
                        region.Neighbours.Add(other);
        int basins = 0;
        foreach (Region seed in Regions)
        {
            if (seed.Basin >= 0) continue;
            seed.Basin = basins;
            var open = new Stack<Region>();
            open.Push(seed);
            while (open.Count > 0)
                foreach (int next in open.Pop().Neighbours)
                    if (Regions[next].Basin < 0)
                    {
                        Regions[next].Basin = basins;
                        open.Push(Regions[next]);
                    }
            basins++;
        }
        foreach (Region region in Regions)
            if (region.Center != null && oldControl.TryGetValue(region.Center.id, out var control))
                (region.ControllerId, region.ControlledAt) = control;
    }

    // 与城市地块相邻的海区(港口城出海、登陆点)
    public static List<Region> RegionsAround(City city)
    {
        var result = new List<Region>();
        if (city?.zones == null) return result;
        EnsureBuilt();
        var seen = new HashSet<int>();
        foreach (TileZone zone in city.zones)
            foreach (TileZone next in zone?.neighbours_all ?? Array.Empty<TileZone>())
                if (next != null && RegionByZone.TryGetValue(next.id, out int id) && seen.Add(id))
                    result.Add(Regions[id]);
        return result;
    }

    // 两组海区之间的最短航线(按海区数，含首尾)；不通返回 null。blocked：不能经过的海区(终点除外)
    public static List<Region> Route(IEnumerable<Region> from, ICollection<Region> to, Func<Region, bool> blocked = null)
    {
        EnsureBuilt();
        var targets = new HashSet<int>(to.Where(region => region != null).Select(region => region.Id));
        if (targets.Count == 0) return null;
        var previous = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (Region start in from)
        {
            if (start == null || previous.ContainsKey(start.Id)) continue;
            if (blocked != null && blocked(start) && !targets.Contains(start.Id)) continue;
            previous[start.Id] = -1;
            queue.Enqueue(start.Id);
        }
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (targets.Contains(current))
            {
                var path = new List<Region>();
                for (int id = current; id >= 0; id = previous[id]) path.Add(Regions[id]);
                path.Reverse();
                return path;
            }
            foreach (int next in Regions[current].Neighbours)
            {
                if (previous.ContainsKey(next)) continue;
                if (blocked != null && !targets.Contains(next) && blocked(Regions[next])) continue;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }
        return null;
    }

    public static float Distance(Region a, Region b) =>
        a?.Center == null || b?.Center == null ? float.MaxValue
            : Mathf.Max(Mathf.Abs(a.Center.x - b.Center.x), Mathf.Abs(a.Center.y - b.Center.y));
}
