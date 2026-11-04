using System.Collections.Generic;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 沙地只算沙滩(用户 2026-11-02 "沙滩要区分一下不然都没法建港口了""沙滩不是沙漠")：游戏里沙地(没有地表的 sand)不再算沙漠
// (无人区、纯荒地都只认荒漠，见 WildernessSystem.ComputeMarks、TerrainSystem)，世界地图的 earth.bytes 也把大漠腹地的成片沙地改成了荒漠
// (见 Tools/WorldMap/build_earth.py 的 beaches)。这之前开的世界地图局，存档里撒哈拉、阿拉伯、塔克拉玛干的沙海还是沙地，
// 一读档就退出沙漠无人区、城扩得进去——读档头一回划区前把离水 Reach 格以外的沙地改成荒漠(同 earth.bytes 的改法)。
// 只在世界地图开的局做(WorldMapWilderness.OnEarth，随存档)；做过后没有腹地沙地，以后再读档扫一遍也不改什么
public static class EarthBeaches
{
    // 离水(海、湖、河)几格以内的沙地留作沙滩
    private const int Reach = 3;

    // 返回改了几格
    internal static int Migrate()
    {
        MapBox world = World.world;
        WorldTile[] tiles = world?.tiles_list;
        if (tiles == null || tiles.Length == 0) return 0;
        int width = MapBox.width, height = MapBox.height;
        if (width <= 0 || height <= 0 || tiles.Length < width * height) return 0;
        bool any = false;
        foreach (WorldTile tile in tiles)
            if (Sand(tile))
            {
                any = true;
                break;
            }
        if (!any) return 0;
        TopTileType desert = AssetManager.top_tiles?.get("desert_low");
        if (desert == null) return 0;
        // 离水几格：从水面往外一圈圈走到 Reach 为止(八方向)
        var distance = new byte[width * height];
        var queue = new Queue<int>();
        for (int i = 0; i < width * height; i++)
        {
            TileTypeBase type = tiles[i]?.Type;
            if (type != null && type.liquid)
            {
                distance[i] = 1;
                queue.Enqueue(i);
            }
        }
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            int step = distance[at];
            if (step > Reach) continue;
            int x = at % width, y = at / width;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int next = ny * width + nx;
                if (distance[next] != 0) continue;
                distance[next] = (byte)(step + 1);
                queue.Enqueue(next);
            }
        }
        int changed = 0;
        for (int i = 0; i < width * height; i++)
        {
            WorldTile tile = tiles[i];
            if (distance[i] != 0 || !Sand(tile)) continue;
            // 同 WorldMapScenario.MakeOasis 改地表：低地荒漠，高度取那一种的下限
            tile.setTileTypes("soil_low", desert);
            tile.Height = tile.Type.height_min;
            changed++;
        }
        if (changed > 0) LogService.LogInfo($"[EmpireCraft][世界地图] 旧档大漠腹地的沙地 {changed} 格改成荒漠(沙地只算沙滩)");
        return changed;
    }

    // 没有地表的沙地(沙滩那一种)
    private static bool Sand(WorldTile tile) => tile?.main_type != null && tile.main_type.sand && tile.top_type == null;
}
