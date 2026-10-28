using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本的长城与关隘(用户 10-29 "我的所有剧本里面好像都没有长城和关口")：eras/*.json 的 walls(经纬度折线，见
// Tools/WorldMap/walls_source.py)与 passes，进世界后先铺墙、后筑关：
//   · 墙：相邻两点之间在走廊里找最省的路——山、丘陵便宜，水面、岩浆不走，城心那块绕开——所以墙沿山脊走、八邻连通；
//     纯净地图下铺图层城墙(MapWallSystem.Add，挡兵、路网绕开)，普通地图下直接铺原版城墙顶层格(山上不铺)；
//   · 关：纯净地图下筑关城(RuralSettlementSystem.Place，压着城墙的那格成关门，路从关门过墙)，名字照剧本；
//     点在山上筑不成的，往外找最近筑得成的一格；普通地图没有关城。
internal static class WorldMapWalls
{
    // 走廊：两点连线外扩 CorridorShare 的长度(至少 CorridorMin 格)
    private const float CorridorShare = 0.3f;
    private const int CorridorMin = 6, PassSearch = 4;
    private const float MountainCost = 0.6f, HillCost = 0.75f, CityCost = 4f;

    public static void Build(WorldMapEra era, WorldMapFrame frame, Vector2Int size)
    {
        if (era == null || frame == null || World.world == null) return;
        TopTileType vanilla = WallType();
        if (vanilla == null) return;
        foreach (WorldMapWall wall in era.walls ?? new List<WorldMapWall>())
        {
            List<Vector2Int> points = (wall.pts ?? new List<float[]>()).Where(point => point != null && point.Length >= 2)
                .Select(point => EarthTerrain.TileOf(frame, size, point[0], point[1])).ToList();
            int laid = 0;
            for (int i = 0; i + 1 < points.Count; i++)
                foreach (WorldTile tile in Route(points[i], points[i + 1]))
                    if (Lay(tile, vanilla)) laid++;
            LogService.LogInfo($"[EmpireCraft][世界地图] 长城 {wall.Name}：{laid} 格");
        }
        if (!PureMapMode.Enabled) return;
        foreach (WorldMapPass pass in era.passes ?? new List<WorldMapPass>())
        {
            if (pass.at == null || pass.at.Length < 2) continue;
            Vector2Int at = EarthTerrain.TileOf(frame, size, pass.at[0], pass.at[1]);
            RuralSettlement fort = Fort(at);
            if (fort == null)
            {
                LogService.LogInfo($"[EmpireCraft][世界地图] 关隘 {pass.Name} 没筑成");
                continue;
            }
            RuralSettlementSystem.Rename(fort, pass.Name);
        }
    }

    private static TopTileType WallType()
    {
        TopTileType fallback = null;
        foreach (TopTileType type in AssetManager.top_tiles?.list ?? new List<TopTileType>())
        {
            if (type?.wall != true) continue;
            if (type.id == "wall_order") return type;
            fallback ??= type;
        }
        return fallback;
    }

    private static bool Inside(Vector2Int point) =>
        point.x >= 2 && point.y >= 2 && point.x < MapBox.width - 2 && point.y < MapBox.height - 2;

    private static bool Passable(WorldTile tile) => tile?.Type != null && !tile.Type.liquid && !tile.Type.lava;

    private static bool CityCore(WorldTile tile) =>
        tile.zone?.city is City city && city.getTile()?.zone == tile.zone;

    private static float Cost(WorldTile tile)
    {
        TileTypeBase type = tile.Type;
        float cost = type.mountains || type.summit ? MountainCost
            : type.rocks || type.id == "hills" || type.id == "snow_hills" || tile.main_type?.id == "hills" ? HillCost : 1f;
        return CityCore(tile) ? cost + CityCost : cost;
    }

    // 走廊里的最短路(八邻)；走不通(隔着水)时照直线连，水面上的格子铺不上自然断开
    private static List<WorldTile> Route(Vector2Int a, Vector2Int b)
    {
        var path = new List<WorldTile>();
        if (!Inside(a) || !Inside(b)) return path;
        int margin = Mathf.Max(CorridorMin, Mathf.CeilToInt(Vector2Int.Distance(a, b) * CorridorShare));
        int x0 = Mathf.Max(0, Mathf.Min(a.x, b.x) - margin), x1 = Mathf.Min(MapBox.width - 1, Mathf.Max(a.x, b.x) + margin);
        int y0 = Mathf.Max(0, Mathf.Min(a.y, b.y) - margin), y1 = Mathf.Min(MapBox.height - 1, Mathf.Max(a.y, b.y) + margin);
        int width = x1 - x0 + 1, count = width * (y1 - y0 + 1);
        int Index(int x, int y) => (y - y0) * width + (x - x0);
        var best = new float[count];
        var from = new int[count];
        for (int i = 0; i < count; i++)
        {
            best[i] = float.MaxValue;
            from[i] = -1;
        }
        int start = Index(a.x, a.y), goal = Index(b.x, b.y);
        best[start] = 0f;
        var heap = new WorldMapZones.MinHeap();
        heap.Push(0f, start);
        while (heap.Count > 0)
        {
            heap.Pop(out float key, out int index);
            if (key > best[index]) continue;
            if (index == goal) break;
            int x = x0 + index % width, y = y0 + index / width;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                WorldTile tile = World.world.GetTileSimple(nx, ny);
                if (!Passable(tile)) continue;
                float next = key + (dx != 0 && dy != 0 ? 1.414f : 1f) * Cost(tile);
                int neighbour = Index(nx, ny);
                if (next >= best[neighbour]) continue;
                best[neighbour] = next;
                from[neighbour] = index;
                heap.Push(next, neighbour);
            }
        }
        if (from[goal] < 0 && goal != start) return Straight(a, b);
        for (int index = goal; index >= 0; index = from[index])
        {
            path.Add(World.world.GetTileSimple(x0 + index % width, y0 + index / width));
            if (index == start) break;
        }
        path.Reverse();
        return path;
    }

    private static List<WorldTile> Straight(Vector2Int a, Vector2Int b)
    {
        var path = new List<WorldTile>();
        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : i / (float)steps;
            path.Add(World.world.GetTileSimple(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t))));
        }
        return path;
    }

    private static bool Lay(WorldTile tile, TopTileType vanilla)
    {
        if (!Passable(tile) || tile.Type.wall || CityCore(tile)) return false;
        if (PureMapMode.Enabled) return MapWallSystem.Add(tile, vanilla.id);
        if (tile.top_type?.wall == true || tile.Type.mountains || tile.Type.summit || tile.hasBuilding()) return false;
        tile.setTileTypes(tile.main_type, vanilla);
        return true;
    }

    // 筑关：先吸到最近的城墙格(关门)；筑不成(山上、城心)的往外 PassSearch 格内找筑得成的
    private static RuralSettlement Fort(Vector2Int at)
    {
        if (!Inside(at)) return null;
        WorldTile origin = World.world.GetTileSimple(at.x, at.y);
        if (origin == null) return null;
        for (int radius = 0; radius <= PassSearch; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                WorldTile tile = World.world.GetTileSimple(at.x + dx, at.y + dy);
                if (tile == null) continue;
                WorldTile target = WallGateSystem.SnapToWall(tile) ?? tile;
                if (RuralSettlementSystem.At(target.x, target.y) is RuralSettlement existing) return existing.fort ? existing : null;
                RuralSettlementSystem.Place(target, town: false, fort: true);
                if (RuralSettlementSystem.At(target.x, target.y) is RuralSettlement made) return made;
            }
        return null;
    }
}
