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
// 读档或换地图后第一次用到时建一次；海陆变了(造岛、填海)每年重建一次。
public static class SeaRegionSystem
{
    private const int RegionSize = 60;
    private const int MaxGround = 16;

    public sealed class Region
    {
        public int Id;
        public readonly List<TileZone> Zones = new();
        public readonly HashSet<int> Neighbours = new();
        public TileZone Center;
        // 制海权：最近一次海战胜出(或独自在场)的一方，及其时间
        public long ControllerId = -1L;
        public double ControlledAt = -1d;
    }

    private static readonly List<Region> Regions = new();
    private static readonly Dictionary<int, int> RegionByZone = new();
    private static object _world;
    private static double _builtAt = -1d;

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
        _world = null;
        _builtAt = -1d;
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
                     _builtAt >= 0d && (now < _builtAt || Date.getYearsSince(_builtAt) >= 1);
        if (!stale) return;
        _world = World.world;
        _builtAt = now;
        try { Build(); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][海区] 划分失败: {exception.Message}"); }
    }

    private static void Build()
    {
        // 保留旧的制海权(按中心块对上)
        var oldControl = new Dictionary<int, (long, double)>();
        foreach (Region region in Regions)
            if (region.Center != null && region.ControllerId >= 0)
                oldControl[region.Center.id] = (region.ControllerId, region.ControlledAt);
        Regions.Clear();
        RegionByZone.Clear();
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
