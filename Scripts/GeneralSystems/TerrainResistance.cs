using System;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 地形阻力(用户 2026-10-16 定："按照地形和距离来判断辐射范围，比如有山川和长城，文化的传播就会减缓甚至衰减""河流过河加阻力")：
// 走进一块地的代价，国家控制力(AdministrativeControlSystem)与文化辐射(CultureRadiation)共用——
//   平原、城池 1，森林 1.3，丘陵 1.6，沙漠、雪地 2，沼泽、丛林 2.5，山地 4，几乎走不通的高山 8；
//   过河(水多的地块)+2，有路(有桥)不加；有城墙(原版城墙或图层长城，见 MapWallSystem)+6；有路的地块阻力减半。
//   城墙上有关城(关门，见 WallGateSystem)的地块：守关一方与同一势力、并肩作战的友邦、同盟从关门过，不加城墙阻力；别人照旧 +6。
//   海面走不了(跨海按水师辐射另算)。
public static class TerrainResistance
{
    public const float RiverCrossing = 2f, WallCrossing = 6f, RoadFactor = 0.5f, Ridge = 8f;
    // 地块里有没有城墙(按地块编号，0 没查过、1 有、2 有而且有关门、-1 没有)：原版城墙格只经设地面格、顶层格两处变(城墙格不会冻上)，
    // 那两处一改就把这块地作废(见 MapWallPatch)；图层长城、关门变了全部重查。以前每块地一分钟(真实时间)就过期重扫 64 格
    private static sbyte[] _walls = new sbyte[0];
    private static int _wallVersion = -1;

    public static void ResetWorldState()
    {
        _walls = new sbyte[0];
        _wallVersion = -1;
    }

    // 地块里有格子改了：城墙下次用到时重查
    public static void InvalidateWall(TileZone zone)
    {
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

    // 走进这块地的阻力；海面走不了返回 -1。traveller、other：谁要过去(过城墙上的关门看守关的放不放，见 WallGateSystem.LetsThrough；
    // 两头都给的要两国都放才算，例如两城之间的教化；不给的当外人，关门不开)
    public static float Step(TileZone zone, Kingdom traveller = null, Kingdom other = null)
    {
        if (zone == null || zone.world_edge || SeaRegionSystem.IsSea(zone)) return -1f;
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
