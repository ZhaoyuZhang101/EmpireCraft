using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.GeneralSystems;

// 跨海登陆的海滩与海外补给(玩家反馈 2026-10-16："远洋打仗还是有问题，两个隔海的国家一千五百年没打下来"，
// 还有从大陆出去的殖民地，要等陆地连上了才打得动)：
//   以前上船、补给都只看"目标城自己临不临海"——内陆的城求不出航线，派去的军在港口等一辈子；海外的军一站进内陆城的地块
//   就算断了补给，战力只剩三成五、逐月减员，登陆后往内陆一推就化掉。现在按"那片陆地的海岸"算：
//   · 海滩(Beachhead)：目标所在那片陆地上、沿陆路离目标最近的临海地块(敌占、无主、己方的都行)，航线从本国港口出发、
//     不经敌方制海、在舰种航程内；船在那里靠岸，上岸后走陆路开往目标(见 CorpsSystem.Embark/Disembark)；
//   · 海外补给：同一片陆地上沿运粮路线走得到的临海地块(己方控制、无主，或军脚下的那片海滩)，外面的海区本国的船到得了，
//     就能从海上卸粮(见 LogisticsSystem.SupplyLine 与 Reachable)；一处都没有才算断了补给。
public static class SeaLanding
{
    // 沿陆路找海滩最多展开几块地块、最多试几处(外面的海区都试过的岸不再重试)
    private const int MaxSearchZones = 2500, MaxShoreTries = 8;

    // 各国的船能到的海区(海区 id → 航线经过几个海区，含首尾)，同一世界时刻内缓存(后勤月度结算一次问很多支军)
    private static readonly Dictionary<long, (double at, Dictionary<int, int> hops)> ReachCache = new();

    public static void ResetWorldState() => ReachCache.Clear();

    // 陆上地块外面相邻的海区
    public static List<SeaRegionSystem.Region> Waters(TileZone zone)
    {
        var result = new List<SeaRegionSystem.Region>();
        if (zone == null || SeaRegionSystem.IsSea(zone)) return result;
        foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            if (SeaRegionSystem.Of(next) is { } region && !result.Contains(region))
                result.Add(region);
        return result;
    }

    // 本方(连同宗主中央)的船从本国港口出发、不经敌方制海能到的海区 → 航线经过几个海区(含首尾)。
    // 敌方制海的海区能驶入(登陆点、卸粮点)但不能穿过，与 NavySystem.LandingRoute 的规矩一致
    public static Dictionary<int, int> Reachable(Kingdom side)
    {
        if (side?.data == null || side.isRekt() || World.world == null) return new Dictionary<int, int>();
        double now = World.world.getCurWorldTime();
        if (ReachCache.TryGetValue(side.id, out var cached) && cached.at == now) return cached.hops;
        var hops = new Dictionary<int, int>();
        var queue = new Queue<SeaRegionSystem.Region>();
        foreach (SeaRegionSystem.Region start in NavySystem.HomeWaters(side))
        {
            if (start == null || hops.ContainsKey(start.Id)) continue;
            hops[start.Id] = 1;
            if (!NavySystem.IsHostile(side, start)) queue.Enqueue(start);
        }
        while (queue.Count > 0)
        {
            SeaRegionSystem.Region current = queue.Dequeue();
            int next = hops[current.Id] + 1;
            foreach (int id in current.Neighbours)
            {
                if (hops.ContainsKey(id) || SeaRegionSystem.Get(id) is not { } region) continue;
                hops[id] = next;
                if (!NavySystem.IsHostile(side, region)) queue.Enqueue(region);
            }
        }
        if (ReachCache.Count > 512) ReachCache.Clear();
        ReachCache[side.id] = (now, hops);
        return hops;
    }

    // 沿陆路由近及远的临海地块：同一片陆地，不进海面与不可通行的高山(起点本身临海也算)
    public static IEnumerable<TileZone> Shores(TileZone from)
    {
        if (from == null) yield break;
        var seen = new HashSet<int> { from.id };
        var queue = new Queue<TileZone>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            TileZone zone = queue.Dequeue();
            if ((zone == from || !TerrainSystem.Impassable(zone)) && Waters(zone).Count > 0) yield return zone;
            if (seen.Count >= MaxSearchZones) continue;
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (next == null || next.world_edge || !seen.Add(next.id)) continue;
                if (SeaRegionSystem.IsSea(next) || TerrainSystem.Impassable(next) || !CorpsSystem.SameLand(next, from)) continue;
                queue.Enqueue(next);
            }
        }
    }

    // 海滩：from 所在那片陆地上沿陆路最近、能靠岸的临海地块与航线——航线从 start(空则本方全部出海口)出发、
    // 不经敌方制海、不超过舰种航程(见 NavySystem.MaxRange)。找不到返回 (null, null)
    public static (TileZone shore, List<SeaRegionSystem.Region> route) Beachhead(Kingdom side, TileZone from,
        IEnumerable<SeaRegionSystem.Region> start = null)
    {
        if (side?.data == null || side.isRekt() || from == null) return (null, null);
        List<SeaRegionSystem.Region> origin = (start ?? NavySystem.HomeWaters(side)).Where(region => region != null).Distinct().ToList();
        if (origin.Count == 0) return (null, null);
        int range = NavySystem.MaxRange(side);
        var tried = new HashSet<int>();
        int tries = 0;
        foreach (TileZone zone in Shores(from))
        {
            List<SeaRegionSystem.Region> waters = Waters(zone).Where(region => !tried.Contains(region.Id)).ToList();
            if (waters.Count == 0) continue;
            foreach (SeaRegionSystem.Region region in waters) tried.Add(region.Id);
            List<SeaRegionSystem.Region> route = SeaRegionSystem.Route(origin, waters, region => NavySystem.IsHostile(side, region));
            if (route != null && route.Count > 0 && route.Count <= range) return (zone, route);
            if (++tries >= MaxShoreTries) break;
        }
        return (null, null);
    }
}
