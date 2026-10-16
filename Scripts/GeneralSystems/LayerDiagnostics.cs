using System.Collections.Generic;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 图层诊断(用户 2026-10-16 反馈"这些紫色的""貌似哪个图层都有"、国内一道道黑线，悬停紫色那块什么都不显示，
// 开关平滑国界也还在)：纯净地图下每分钟分帧扫一遍色块贴图，记两类不该有的像素，写进日志 [图层诊断]：
//   · 水面上色：不归任何城的块里，水面的格子却不透明(水面本该空着，露出底下的水)；
//   · 无主陆地上色：不归任何城、图层也查不到归属的块里，陆地格子不透明；
//   · 多余的边框：这块地连同周围八块在图层上都归同一家，块里却还有边框像素(国界早该没了)。
// 多余的边框查到就修(用户 2026-10-17 "这里的方块边界的残留又出现了"，确认"两边是同一国，不该有线")：
// 丢掉这块地连同四邻的上色缓存、重画(见 SmoothBorders.Repaint)；挨着水的也修(海岸本来就不画边框，这里的边框一样是残留)，
// 扫描由每分钟一轮改为每十五秒一轮，残线至多停留十几秒。
// 每类记前几块的坐标、像素颜色、平滑国界记下的状态；数目没变就不重复写。
public static class LayerDiagnostics
{
    private const float IntervalSeconds = 15f, FirstDelaySeconds = 15f;
    // 本轮修了几块(写进日志)
    private static int _repaired;
    private const int ZonesPerFrame = 400, Examples = 4;

    private static float _next = -1f;
    private static int _cursor = -1;
    private static object _world;
    private static string _lastSummary;
    private static int _water, _land, _outline;
    private static readonly List<string> WaterExamples = new(), LandExamples = new(), OutlineExamples = new();

    public static void Tick()
    {
        MapBox world = World.world;
        if (!PureMapMode.Enabled || world?.zone_calculator?.zones == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _cursor = -1;
            _next = Time.unscaledTime + FirstDelaySeconds;
            _lastSummary = null;
        }
        if (_cursor < 0)
        {
            if (Time.unscaledTime < _next) return;
            _cursor = 0;
            _water = _land = _outline = _repaired = 0;
            WaterExamples.Clear();
            LandExamples.Clear();
            OutlineExamples.Clear();
        }
        ZoneCalculator calc = world.zone_calculator;
        Color32[] pixels = calc.pixels;
        MetaTypeAsset layer = world.getCachedMapMetaAsset();
        List<TileZone> zones = calc.zones;
        if (pixels == null || layer == null)
        {
            _cursor = -1;
            _next = Time.unscaledTime + IntervalSeconds;
            return;
        }
        int end = Mathf.Min(zones.Count, _cursor + ZonesPerFrame);
        for (; _cursor < end; _cursor++) Check(zones[_cursor], pixels, layer, calc);
        if (_cursor < zones.Count) return;
        _cursor = -1;
        _next = Time.unscaledTime + IntervalSeconds;
        string summary = $"图层 {layer.id}：水面上色 {_water} 块，无主陆地上色 {_land} 块，多余的边框 {_outline} 块，修了 {_repaired} 块残留边框";
        if (summary == _lastSummary) return;
        _lastSummary = summary;
        var text = new StringBuilder("[EmpireCraft][图层诊断] ").Append(summary);
        Append(text, "水面上色", WaterExamples);
        Append(text, "无主陆地上色", LandExamples);
        Append(text, "多余的边框", OutlineExamples);
        LogService.LogInfo(text.ToString());
    }

    private static void Append(StringBuilder text, string title, List<string> examples)
    {
        if (examples.Count == 0) return;
        text.Append("；").Append(title).Append("例：").Append(string.Join(" | ", examples));
    }

    private static IMetaObject MetaOf(MetaTypeAsset layer, TileZone zone)
    {
        if (zone == null) return null;
        try
        {
            IMetaObject meta = layer.tile_get_metaobject_0?.Invoke(zone);
            return meta != null && meta.isAlive() ? meta : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Hex(Color32 color) => $"#{color.r:X2}{color.g:X2}{color.b:X2}/{color.a}";

    private static void Check(TileZone zone, Color32[] pixels, MetaTypeAsset layer, ZoneCalculator calc)
    {
        if (zone?.tiles == null) return;
        IMetaObject meta = MetaOf(layer, zone);
        WorldTile waterHit = null, landHit = null, outlineHit = null;
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile?.data?.tile_id ?? -1;
            if (id < 0 || id >= pixels.Length || pixels[id].a == 0 || tile.Type == null) continue;
            bool water = tile.Type.ocean || tile.Type.liquid && !tile.Type.lava;
            if (water && zone.city == null) waterHit ??= tile;
            else if (!water && zone.city == null && meta == null) landHit ??= tile;
            if (!water && SmoothBorders.IsOutline(id)) outlineHit ??= tile;
        }
        if (waterHit != null && _water++ < Examples) WaterExamples.Add(Example(zone, waterHit, pixels, meta));
        if (landHit != null && _land++ < Examples) LandExamples.Add(Example(zone, landHit, pixels, meta));
        if (outlineHit == null || meta == null) return;
        // 周围八块都归同一家(地图边上的不算)：块里的边框是多余的，查到就修
        bool wet = false;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            TileZone next = calc.getZone(zone.x + dx, zone.y + dy);
            if (next == null || next.world_edge || !ReferenceEquals(MetaOf(layer, next), meta)) return;
            foreach (WorldTile tile in next.tiles)
                if (tile?.Type != null && (tile.Type.ocean || tile.Type.liquid)) wet = true;
        }
        SmoothBorders.Repaint(zone);
        _repaired++;
        if (!wet && _outline++ < Examples) OutlineExamples.Add(Example(zone, outlineHit, pixels, meta));
    }

    private static string Example(TileZone zone, WorldTile tile, Color32[] pixels, IMetaObject meta)
    {
        int id = tile.data.tile_id;
        string owner = meta is Kingdom kingdom ? kingdom.name : meta is City city ? city.name : meta?.GetType().Name ?? "无";
        return $"块{zone.id}({zone.x},{zone.y}) 格({tile.x},{tile.y}) {tile.Type.id} 色{Hex(pixels[id])} 城{zone.city?.name ?? "无"} " +
               $"图层归属 {owner}；{SmoothBorders.Describe(id, zone.id)}";
    }
}
