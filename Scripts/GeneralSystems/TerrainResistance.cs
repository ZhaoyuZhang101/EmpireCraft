using System;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 地形阻力(用户 2026-10-16 定："按照地形和距离来判断辐射范围，比如有山川和长城，文化的传播就会减缓甚至衰减""河流过河加阻力")：
// 走进一块地的代价，国家控制力(AdministrativeControlSystem)与文化辐射(CultureRadiation)共用——
//   平原、城池 1，森林 1.3，丘陵 1.6，沙漠、雪地 2，沼泽、丛林 2.5，山地 4，几乎走不通的高山 8；
//   过河(水多的地块)+2，有路(有桥)不加；有城墙(原版城墙或图层长城，见 MapWallSystem)+6；有路的地块阻力减半。
//   城墙上有关城(关门，见 WallGateSystem)的地块：守关一方与同一势力、并肩作战的友邦、同盟从关门过，不加城墙阻力；别人照旧 +6。
//   海面走不了(跨海按水师辐射另算)。
// 无人区(用户 2026-10-29 "之前的沙漠和冻土什么的地块扩展就算了……作出无人区地块"，见 WildernessPassage)：
//   对走的一方过不去的无人区(没修路的山脉、丛林、沼泽；真游牧惯走沙漠冻土、阿拉伯制度线惯走沙漠)走不了，同海面；
//   有路(山口、商道)的照常按地形阻力走。控制力、文化辐射都按这个算；陆路不通时的直线估算见 LineCost。
//   以前的"天堑"(用户 2026-10-26：山地、冻土、沙漠连成片，离边越远越难走，第二层 ×2、第三层起 ×4)撤了，
//   不在无人区里的山、沙只按上面的地形阻力；无人区开关关掉时处处只按地形阻力。
public static class TerrainResistance
{
    public const float RiverCrossing = 2f, WallCrossing = 6f, RoadFactor = 0.5f, Ridge = 8f;
    // 直线估算穿过的无人区(绕得过去时)：要绕着走，按那块地形阻力的两倍估
    private const float WildDetour = 2f;
    // 地块里有没有城墙(按地块编号，0 没查过、1 有、2 有而且有关门、-1 没有)：原版城墙格只经设地面格、顶层格两处变(城墙格不会冻上)，
    // 那两处一改就把这块地作废(见 MapWallPatch)；图层长城、关门变了全部重查。以前每块地一分钟(真实时间)就过期重扫 64 格
    private static sbyte[] _walls = new sbyte[0];
    private static int _wallVersion = -1;

    public static void ResetWorldState()
    {
        _walls = new sbyte[0];
        _wallVersion = -1;
        WallPassage.ResetWorldState();
        WildernessPassage.ResetWorldState();
    }

    // 地块里有格子改了：城墙下次用到时重查(军团过城墙的地块对也跟着作废，见 WallPassage)
    public static void InvalidateWall(TileZone zone)
    {
        WallPassage.InvalidateZone(zone);
        if (zone != null && zone.id >= 0 && zone.id < _walls.Length) _walls[zone.id] = 0;
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

    // 走进这块地的阻力；海面、对 traveller 过不去的无人区(见 WildernessPassage，按 traveller 的习性判)走不了，返回 -1。
    // traveller、other：谁要过去(过城墙上的关门看守关的放不放，见 WallGateSystem.LetsThrough；
    // 两头都给的要两国都放才算，例如两城之间的教化；不给的当外人，关门不开，无人区按定居的判)
    // 只看地形的阻力(不看无人区、城墙、路)：本国的无人区里传政令按它的几倍算(见 AdministrativeControlSystem)
    public static float TerrainStep(TileZone zone)
    {
        if (zone == null || zone.world_edge || SeaRegionSystem.IsSea(zone)) return -1f;
        FrontTerrain terrain = TerrainSystem.Of(zone, out bool impassable);
        return impassable ? Ridge : Base(terrain);
    }

    public static float Step(TileZone zone, Kingdom traveller = null, Kingdom other = null)
    {
        if (zone == null || zone.world_edge || SeaRegionSystem.IsSea(zone)) return -1f;
        if (!WildernessPassage.Passable(zone, traveller)) return -1f;
        FrontTerrain terrain = TerrainSystem.Of(zone, out bool impassable);
        float cost = impassable ? Ridge : Base(terrain);
        bool road = RoadNetwork.IsRoad(zone);
        if (terrain == FrontTerrain.River && !road) cost += RiverCrossing;
        sbyte wall = WallState(zone);
        if (wall > 0 && !(wall == 2 && traveller != null && WallGateSystem.LetsThrough(zone, traveller) &&
                          (other == null || WallGateSystem.LetsThrough(zone, other))))
            cost += WallCrossing;
        return road ? cost * RoadFactor : cost;
    }

    // 两块地之间沿直线走过去的阻力(按一块地一步取样，各取 Step)：开战够不够得着、陆路不通的飞地、开战定目标的估算用(见 WarReach、
    // AdministrativeControlSystem)。中间的海面按一步 SeaStep 算(近岸行船)；traveller 过关门同 Step。
    // 直线穿过对 traveller 过不去的无人区：调用方先确认绕得过去(见 WildernessPassage.Reachable)，这里每块按地形阻力的两倍估绕路
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
            if (zone != null && !WildernessPassage.Passable(zone, traveller))
            {
                FrontTerrain terrain = TerrainSystem.Of(zone, out bool impassable);
                total += (impassable ? Ridge : Base(terrain)) * WildDetour;
                continue;
            }
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
