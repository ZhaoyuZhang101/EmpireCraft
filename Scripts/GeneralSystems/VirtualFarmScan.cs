using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems;

// 保留原版候选集合：同城、风车半径 9、风车所属 region 及邻接 region 的 chunk。
// 从这些 chunk 的全部地块扫描改为直接读取圆内的 253 格。
public static class VirtualFarmScan
{
    public static long Scans { get; private set; }
    public static long TileChecks { get; private set; }
    public static void Reset() { Scans = TileChecks = 0; }

    public static void Refresh(City city)
    {
        using var timing = FrameProfiler.Measure("虚拟人口·风车农田扫描");
        city.calculated_place_for_farms.Clear();
        city.calculated_grown_wheat.Clear();
        city.calculated_farm_fields.Clear();
        city.calculated_crops.Clear();
        WorldTile mill = city.getBuildingOfType("type_windmill")?.current_tile;
        if (mill?.region != null)
        {
            var chunks = new HashSet<MapChunk> { mill.region.chunk };
            foreach (MapRegion neighbour in mill.region.neighbours) chunks.Add(neighbour.chunk);
            for (int dx = -9; dx <= 9; dx++)
                for (int dy = -9; dy <= 9; dy++)
                {
                    if (dx * dx + dy * dy > 81) continue;
                    TileChecks++;
                    WorldTile tile = World.world.GetTile(mill.x + dx, mill.y + dy);
                    if (tile?.zone?.city != city || !chunks.Contains(tile.chunk)) continue;
                    if (tile.Type.can_be_farm) city.calculated_place_for_farms.Add(tile);
                    if (!tile.Type.farm_field) continue;
                    city.calculated_farm_fields.Add(tile);
                    Building crop = tile.building;
                    if (crop?.asset?.wheat != true) continue;
                    city.calculated_crops.Add(tile);
                    if (crop.component_wheat.isMaxLevel()) city.calculated_grown_wheat.Add(tile);
                }
        }
        city.calculated_place_for_farms.checkAddRemove();
        city.calculated_farm_fields.checkAddRemove();
        city.calculated_crops.checkAddRemove();
        city.calculated_grown_wheat.checkAddRemove();
        Scans++;
    }
}
