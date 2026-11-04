using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诊断用的只读查询(见 FrontSystem.PushDiagnostics)：这块地在当前图层上取到的归属，整图画布上记着的是什么
public static partial class SmoothBorders
{
    internal static string DescribeDisplay(TileZone zone)
    {
        if (zone == null) return "无";
        try
        {
            MetaTypeAsset layer = World.world?.getCachedMapMetaAsset();
            if (layer == null) return "无图层";
            int option = layer.getZoneOptionState();
            string text = $"图层 {layer.id} 归属 {MetaLabel(LayerMeta(layer, option, zone))} 控制方 {MetaLabel(RealMetaOf(layer, option, zone))} 推进方 {MetaLabel(PushMetaOf(layer, zone))}";
            if (!CanvasActive || !ReferenceEquals(layer, _canvasLayer) || zone.id < 0 || zone.id >= _zoneCity.Length) return text + " 画布 未接手";
            CountCanvas(zone, out int land, out int blank);
            return text + $" 画布 城{_zoneCity[zone.id]} 控制{_zoneShade[zone.id]} 推进{_zonePush[zone.id]} 空格{blank}/{land}";
        }
        catch (Exception exception)
        {
            return $"取显示失败({exception.Message})";
        }
    }

    private static string MetaLabel(IMetaObject meta) => meta == null ? "无" : $"{meta.name}#{meta.getID()}";

    // 这块地归城、在按城归属的图层上，画布却有一大半陆地格没有归属，且不是这一帧刚要重画的
    internal static bool CanvasBlank(TileZone zone)
    {
        if (zone?.city == null || !CanvasActive || !_canvasCityKeyed || zone.id < 0 || zone.id >= _zoneCity.Length || CanvasDirty.Contains(zone.id))
            return false;
        CountCanvas(zone, out int land, out int blank);
        return land > 0 && blank * 2 > land;
    }

    private static void CountCanvas(TileZone zone, out int land, out int blank)
    {
        land = blank = 0;
        int zx = zone.id % _zonesX, zy = zone.id / _zonesX;
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize; x++)
        {
            int t = y * _canvasW + x;
            if (t < 0 || t >= _water.Length || t >= _owner.Length || _water[t]) continue;
            land++;
            if (_owner[t] == 0) blank++;
        }
    }
}
