using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 地形阻力(用户 2026-10-16 定："按照地形和距离来判断辐射范围，比如有山川和长城，文化的传播就会减缓甚至衰减""河流过河加阻力")：
// 走进一块地的代价，国家控制力(AdministrativeControlSystem)与文化辐射(CultureRadiation)共用——
//   平原、城池 1，森林 1.3，丘陵 1.6，沙漠、雪地 2，沼泽、丛林 2.5，山地 4，几乎走不通的高山 8；
//   过河(水多的地块)+2，有路(有桥)不加；有城墙(原版城墙或图层长城，见 MapWallSystem)+6；有路的地块阻力减半。
//   海面走不了(跨海按水师辐射另算)。
public static class TerrainResistance
{
    public const float RiverCrossing = 2f, WallCrossing = 6f, RoadFactor = 0.5f, Ridge = 8f;
    // 地块里有没有城墙：原版城墙不常变，一分钟(真实时间)重查一次；图层长城变了立即重查
    private const float WallRefreshSeconds = 60f;
    private static readonly Dictionary<int, (bool wall, float at)> Walls = new();
    private static int _wallVersion = -1;

    public static void ResetWorldState()
    {
        Walls.Clear();
        _wallVersion = -1;
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

    // 走进这块地的阻力；海面走不了返回 -1
    public static float Step(TileZone zone)
    {
        if (zone == null || zone.world_edge || SeaRegionSystem.IsSea(zone)) return -1f;
        FrontTerrain terrain = TerrainSystem.Of(zone);
        float cost = TerrainSystem.Impassable(zone) ? Ridge : Base(terrain);
        bool road = RoadNetwork.IsRoad(zone);
        if (terrain == FrontTerrain.River && !road) cost += RiverCrossing;
        if (HasWall(zone)) cost += WallCrossing;
        return road ? cost * RoadFactor : cost;
    }

    public static bool HasWall(TileZone zone)
    {
        if (zone?.tiles == null) return false;
        if (_wallVersion != MapWallSystem.Version)
        {
            Walls.Clear();
            _wallVersion = MapWallSystem.Version;
        }
        float now = Time.unscaledTime;
        if (Walls.TryGetValue(zone.id, out var hit) && now >= hit.at && now - hit.at < WallRefreshSeconds) return hit.wall;
        bool wall = false;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.Type == null || !tile.Type.wall && !MapWallSystem.Contains(tile)) continue;
            wall = true;
            break;
        }
        if (Walls.Count > 200000) Walls.Clear();
        Walls[zone.id] = (wall, now);
        return wall;
    }
}
