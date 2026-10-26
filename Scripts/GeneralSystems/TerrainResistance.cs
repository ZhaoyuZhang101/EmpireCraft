using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 地形阻力(用户 2026-10-16 定："按照地形和距离来判断辐射范围，比如有山川和长城，文化的传播就会减缓甚至衰减""河流过河加阻力")：
// 走进一块地的代价，国家控制力(AdministrativeControlSystem)与文化辐射(CultureRadiation)共用——
//   平原、城池 1，森林 1.3，丘陵 1.6，沙漠、雪地 2，沼泽、丛林 2.5，山地 4，几乎走不通的高山 8；
//   过河(水多的地块)+2，有路(有桥)不加；有城墙(原版城墙或图层长城，见 MapWallSystem)+6；有路的地块阻力减半。
//   城墙上有关城(关门，见 WallGateSystem)的地块：守关一方与同一势力、并肩作战的友邦、同盟从关门过，不加城墙阻力；别人照旧 +6。
//   海面走不了(跨海按水师辐射另算)。
// 天堑(用户 2026-10-26："过厚的山脉，冻土与沙地也会阻隔控制力，如此一来就可以模拟现实的情况，印度打不到西藏，中国打不到欧洲")：
//   山地(连同走不通的高山)、雪原冻土、沙漠连成的一片，离这片的边(别的地形、有城的地块、海)越远越难走——
//   边上一层照旧，第二层 ×2，第三层起 ×4(见 BarrierDepth)。薄薄一道山岭翻得过去，青藏、撒哈拉那样的腹地就鞭长莫及；
//   有城的地块(绿洲、山口)不算天堑，驿道照旧减半。控制力、文化辐射、开战够不够得着都按这个算(见 LineCost)。
public static class TerrainResistance
{
    public const float RiverCrossing = 2f, WallCrossing = 6f, RoadFactor = 0.5f, Ridge = 8f;
    // 天堑腹地的阻力倍数：第二层、第三层起；往里看三层就够
    private const float SecondLayer = 2f, InnerLayer = 4f;
    private const int MaxDepth = 3;
    // 天堑深浅缓存(按地块编号，0 没算过)：地形一年里难得变，一年重算一次；城墙格那两处改动当场作废那一块(见 InvalidateWall)
    private const int DepthRefreshMonths = 12;
    private static sbyte[] _depth = new sbyte[0];
    private static double[] _depthAt = new double[0];
    // 地块里有没有城墙(按地块编号，0 没查过、1 有、2 有而且有关门、-1 没有)：原版城墙格只经设地面格、顶层格两处变(城墙格不会冻上)，
    // 那两处一改就把这块地作废(见 MapWallPatch)；图层长城、关门变了全部重查。以前每块地一分钟(真实时间)就过期重扫 64 格
    private static sbyte[] _walls = new sbyte[0];
    private static int _wallVersion = -1;

    public static void ResetWorldState()
    {
        _walls = new sbyte[0];
        _wallVersion = -1;
        _depth = new sbyte[0];
        _depthAt = new double[0];
        WallPassage.ResetWorldState();
    }

    // 地块里有格子改了：城墙下次用到时重查(军团过城墙的地块对也跟着作废，见 WallPassage)；这一块的天堑深浅也重算
    public static void InvalidateWall(TileZone zone)
    {
        WallPassage.InvalidateZone(zone);
        if (zone != null && zone.id >= 0 && zone.id < _walls.Length) _walls[zone.id] = 0;
        if (zone != null && zone.id >= 0 && zone.id < _depth.Length) _depth[zone.id] = 0;
    }

    public static float Base(FrontTerrain terrain) => terrain switch
    {
        FrontTerrain.Forest => 1.3f,
        FrontTerrain.Hills => 1.6f,
        FrontTerrain.Desert or FrontTerrain.Snow => 2f,
        FrontTerrain.Swamp or FrontTerrain.Jungle => 2.5f,
        FrontTerrain.Mountain => 4f,
        _ => 1f
    };

    // 走进这块地的阻力；海面走不了返回 -1。traveller、other：谁要过去(过城墙上的关门看守关的放不放，见 WallGateSystem.LetsThrough；
    // 两头都给的要两国都放才算，例如两城之间的教化；不给的当外人，关门不开)
    public static float Step(TileZone zone, Kingdom traveller = null, Kingdom other = null)
    {
        if (zone == null || zone.world_edge || SeaRegionSystem.IsSea(zone)) return -1f;
        FrontTerrain terrain = TerrainSystem.Of(zone, out bool impassable);
        float cost = impassable ? Ridge : Base(terrain);
        if (Barrier(zone, terrain, impassable))
        {
            int depth = BarrierDepth(zone);
            cost *= depth >= MaxDepth ? InnerLayer : depth == 2 ? SecondLayer : 1f;
        }
        bool road = RoadNetwork.IsRoad(zone);
        if (terrain == FrontTerrain.River && !road) cost += RiverCrossing;
        sbyte wall = WallState(zone);
        if (wall > 0 && !(wall == 2 && traveller != null && WallGateSystem.LetsThrough(zone, traveller) &&
                          (other == null || WallGateSystem.LetsThrough(zone, other))))
            cost += WallCrossing;
        return road ? cost * RoadFactor : cost;
    }

    // 天堑地块：山地、走不通的高山、雪原冻土、沙漠；城心所在的地块(绿洲、山口的城)不算
    private static bool Barrier(TileZone zone, FrontTerrain terrain, bool impassable) =>
        (impassable || terrain is FrontTerrain.Mountain or FrontTerrain.Snow or FrontTerrain.Desert) &&
        !(zone.city != null && zone.city.getTile()?.zone == zone);

    private static bool Barrier(TileZone zone)
    {
        if (zone == null || zone.world_edge || SeaRegionSystem.IsSea(zone)) return false;
        FrontTerrain terrain = TerrainSystem.Of(zone, out bool impassable);
        return Barrier(zone, terrain, impassable);
    }

    // 天堑深浅：不是天堑 0；离这片天堑的边(别的地形、有城的地块、海)隔几层——挨着边的 1，往里 2，再往里 3(封顶)。
    // 地图边缘不算边
    public static int BarrierDepth(TileZone zone)
    {
        if (zone == null || zone.id < 0) return 0;
        int id = zone.id;
        if (id >= _depth.Length)
        {
            int size = Mathf.Max(id + 1, World.world?.zone_calculator?.zones?.Count ?? 0);
            Array.Resize(ref _depth, size);
            Array.Resize(ref _depthAt, size);
        }
        double now = World.world?.getCurWorldTime() ?? 0d;
        if (_depth[id] != 0 && now >= _depthAt[id] && Date.getMonthsSince(_depthAt[id]) < DepthRefreshMonths) return _depth[id] - 1;
        int depth = MeasureDepth(zone);
        _depth[id] = (sbyte)(depth + 1);
        _depthAt[id] = now;
        return depth;
    }

    private static readonly List<TileZone> Ring = new(), NextRing = new();
    private static readonly HashSet<TileZone> Seen = new();

    // 一层层往外找天堑的边，找到第几层就是几；三层内都是天堑的算 3
    private static int MeasureDepth(TileZone zone)
    {
        if (!Barrier(zone)) return 0;
        Ring.Clear();
        Seen.Clear();
        Ring.Add(zone);
        Seen.Add(zone);
        for (int layer = 1; layer < MaxDepth; layer++)
        {
            NextRing.Clear();
            foreach (TileZone current in Ring)
                foreach (TileZone next in current.neighbours ?? Array.Empty<TileZone>())
                {
                    if (next == null || next.world_edge || !Seen.Add(next)) continue;
                    if (!Barrier(next)) return layer;
                    NextRing.Add(next);
                }
            Ring.Clear();
            Ring.AddRange(NextRing);
            if (Ring.Count == 0) break;
        }
        return MaxDepth;
    }

    // 两块地之间沿直线走过去的阻力(按一块地一步取样，各取 Step)：开战够不够得着、陆路不通的飞地、开战定目标的估算用(见 WarReach、
    // AdministrativeControlSystem)。中间的海面按一步 SeaStep 算(近岸行船)；traveller 过关门同 Step
    public const float SeaStep = 1f;

    public static float LineCost(TileZone from, TileZone to, Kingdom traveller = null)
    {
        if (from == null || to == null) return 0f;
        if (from == to) return 0f;
        ZoneCalculator calculator = World.world?.zone_calculator;
        if (calculator == null) return Vector2.Distance(new Vector2(from.x, from.y), new Vector2(to.x, to.y));
        float dx = to.x - from.x, dy = to.y - from.y;
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dy * dy)));
        float total = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            TileZone zone = i == steps ? to : calculator.getZone(Mathf.RoundToInt(from.x + dx * t), Mathf.RoundToInt(from.y + dy * t));
            // 同一块地取到两次(斜着走)照样算一步：一步就是一块地的长度
            float step = zone == null ? SeaStep : Step(zone, traveller);
            total += step < 0f ? SeaStep : step;
        }
        return total;
    }

    public static bool HasWall(TileZone zone) => WallState(zone) > 0;

    // -1 没有城墙、1 有城墙、2 有城墙且有关门(缓存里 0 是还没查过)
    private static sbyte WallState(TileZone zone)
    {
        if (zone?.tiles == null) return -1;
        if (_wallVersion != MapWallSystem.Version)
        {
            Array.Clear(_walls, 0, _walls.Length);
            _wallVersion = MapWallSystem.Version;
        }
        int id = zone.id;
        if (id < 0) return ScanWall(zone);
        if (id >= _walls.Length)
            Array.Resize(ref _walls, Mathf.Max(id + 1, World.world?.zone_calculator?.zones?.Count ?? 0));
        if (_walls[id] != 0) return _walls[id];
        return _walls[id] = ScanWall(zone);
    }

    private static sbyte ScanWall(TileZone zone)
    {
        sbyte state = -1;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.Type == null || !tile.Type.wall && !MapWallSystem.Contains(tile)) continue;
            if (WallGateSystem.IsGate(tile)) return 2;
            state = 1;
        }
        return state;
    }
}
