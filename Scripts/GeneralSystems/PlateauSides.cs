namespace EmpireCraft.Scripts.GeneralSystems;

// 高原外缘作界线(用户 2026-11-10 "国界线也能更合理一点"，问答定"既挡兵也当界线"、法理"同长城，法理不动")：
// 两块地(两座城)一边在某种高原上、另一边在平地或另一种高原上，就算隔着外缘(哪一边见 TerrainSystem.PlateauSide：
// 某种高原格占陆地过半是那一种，一成五以下是平地，其间是外缘那一圈，两边都算、不隔)。
// 用法同长城(见 WorldMap.FrontierWalls.Separates、Behind，随机图也算)：开局铺辖地不领外缘那边的地、同一法理隔着外缘的城分设行政区、
// 无人区跟着这一侧的城；城市扩张不翻外缘(CityExpansionPatch.CanAddZone，神力手动划地不管)。王国、帝国法理照史实，不按外缘切。
public static class PlateauSides
{
    public static bool Differ(TileZone a, TileZone b)
    {
        if (a == null || b == null || a == b || PlateauTerrain.Alpine == null) return false;
        byte sa = TerrainSystem.PlateauSide(a), sb = TerrainSystem.PlateauSide(b);
        return sa != sb && sa != TerrainSystem.EdgeSide && sb != TerrainSystem.EdgeSide;
    }

    public static bool Differ(City city, TileZone zone) => Differ(city?.getTile()?.zone, zone);

    public static bool Differ(City a, City b) => Differ(a?.getTile()?.zone, b?.getTile()?.zone);
}
