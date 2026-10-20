using System.Collections.Generic;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 图层诊断(用户 2026-10-16 反馈"这些紫色的""貌似哪个图层都有"、国内一道道黑线，悬停紫色那块什么都不显示，
// 开关平滑国界也还在)：纯净地图下每分钟分帧扫一遍色块贴图，记两类不该有的像素，写进日志 [图层诊断]：
//   · 水面上色：不归任何城的块里，水面的格子却不透明(水面本该空着，露出底下的水)；
//   · 无主陆地上色：不归任何城、图层也查不到归属的块里，陆地格子不透明；
//   · 多余的边框：块里有边框像素，四邻陆地格记下的归属却都同它一样(国界早该没了)。
// 多余的边框查到就修(用户 2026-10-17 "这里的方块边界的残留又出现了"，确认"两边是同一国，不该有线")：
// 丢掉这块地连同四邻的上色缓存、重画(见 SmoothBorders.Repaint)，扫描由每分钟一轮改为每十五秒一轮，残线至多停留十几秒；
// 修过一回下一轮还在的记进日志、不再修(那是画法本身的问题)。
// 每类记前几块的坐标、像素颜色、平滑国界记下的状态；数目没变就不重复写。
// 色块不刷新(用户 2026-10-19 转玩家反馈"平滑模式下色块不刷新")：根治在重画指纹(见 SmoothBorders.Fingerprint)，变了下一帧就重画；
// 这里平滑国界开着时(不论纯净地图)只记日志——上次画成的归属或颜色与眼下的实际归属对不上、过了一轮还没重画的块，
// 说明还有指纹没算进去的变动(用户定"定时扫描只留作日志诊断"，不在这里补画，免得色块过十几秒突然跳)。
public static class LayerDiagnostics
{
    private const float IntervalSeconds = 15f, FirstDelaySeconds = 15f;
    // 本轮修了几块(写进日志)；修过一回下一轮还在的不再修(是真的画法问题，不是残留，免得每十五秒重画一回)
    private static int _repaired;
    private static readonly HashSet<int> RepairedOnce = new(), GaveUp = new();
    private const int ZonesPerFrame = 400, Examples = 4;

    private static float _next = -1f;
    private static int _cursor = -1;
    private static object _world;
    private static string _lastSummary;
    private static int _water, _land, _outline, _stale;
    private static readonly List<string> WaterExamples = new(), LandExamples = new(), OutlineExamples = new(), StaleExamples = new();
    // 上一轮就对不上的块：这一轮还对不上才算(刚变、下一帧就会重画的不算)
    private static readonly HashSet<int> StaleSeen = new();
    private static object _staleLayer;

    // 诊断出错只记一次、本轮作罢：以前异常直接抛进 ModClass.Update，排在后面的各系统这一帧都不跑
    private static bool _failedReported;

    public static void Tick()
    {
        try
        {
            TickCore();
        }
        catch (global::System.Exception exception)
        {
            _cursor = -1;
            _next = Time.unscaledTime + IntervalSeconds;
            if (_failedReported) return;
            _failedReported = true;
            LogService.LogWarning($"[EmpireCraft][图层诊断] 诊断出错，本轮作罢：{exception}");
        }
    }

    private static void TickCore()
    {
        MapBox world = World.world;
        if (!PureMapMode.Enabled && !SmoothBorders.Enabled || world?.zone_calculator?.zones == null || !Config.game_loaded ||
            SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _cursor = -1;
            RepairedOnce.Clear();
            GaveUp.Clear();
            StaleSeen.Clear();
            _next = Time.unscaledTime + FirstDelaySeconds;
            _lastSummary = null;
        }
        if (_cursor < 0)
        {
            if (Time.unscaledTime < _next) return;
            _cursor = 0;
            _water = _land = _outline = _repaired = _stale = 0;
            WaterExamples.Clear();
            LandExamples.Clear();
            OutlineExamples.Clear();
            StaleExamples.Clear();
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
        // 换了图层：上一层记下的"没刷新"不算数
        if (!ReferenceEquals(_staleLayer, layer))
        {
            _staleLayer = layer;
            StaleSeen.Clear();
        }
        int end = Mathf.Min(zones.Count, _cursor + ZonesPerFrame);
        for (; _cursor < end; _cursor++) Check(zones[_cursor], pixels, layer, calc);
        if (_cursor < zones.Count) return;
        _cursor = -1;
        _next = Time.unscaledTime + IntervalSeconds;
        string summary = $"图层 {layer.id}：水面上色 {_water} 块，无主陆地上色 {_land} 块，多余的边框 {_outline} 块，修了 {_repaired} 块残留边框，" +
                         $"没刷新的色块 {_stale} 块";
        if (summary == _lastSummary) return;
        _lastSummary = summary;
        var text = new StringBuilder("[EmpireCraft][图层诊断] ").Append(summary);
        Append(text, "水面上色", WaterExamples);
        Append(text, "无主陆地上色", LandExamples);
        Append(text, "多余的边框", OutlineExamples);
        Append(text, "没刷新的色块", StaleExamples);
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
        CheckStale(zone, pixels, layer);
        if (!PureMapMode.Enabled) return;
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
        // 逐格判残留(这一格记为边框，四邻陆地格的归属都同它一样)：真的国界、占领、逐格推进、悬停的省界都不算
        WorldTile stale = null;
        if (outlineHit != null)
            foreach (WorldTile tile in zone.tiles)
                if (SmoothBorders.StaleOutline(tile)) { stale = tile; break; }
        if (stale == null)
        {
            // 干净了：以后再出残留照样修
            RepairedOnce.Remove(zone.id);
            GaveUp.Remove(zone.id);
            return;
        }
        if (GaveUp.Contains(zone.id)) return;
        if (!RepairedOnce.Add(zone.id))
        {
            GaveUp.Add(zone.id);
            if (_outline++ < Examples) OutlineExamples.Add(Example(zone, stale, pixels, meta) + "(修过仍在，不再修)");
            return;
        }
        SmoothBorders.Repaint(zone);
        _repaired++;
    }

    private static void CheckStale(TileZone zone, Color32[] pixels, MetaTypeAsset layer)
    {
        if (!SmoothBorders.StaleOwner(layer, zone, pixels, out string detail))
        {
            StaleSeen.Remove(zone.id);
            return;
        }
        if (StaleSeen.Add(zone.id)) return;
        if (_stale++ < Examples) StaleExamples.Add($"块{zone.id}({zone.x},{zone.y}) 城{zone.city?.name ?? "无"} {detail}");
    }

    private static string Example(TileZone zone, WorldTile tile, Color32[] pixels, IMetaObject meta)
    {
        int id = tile.data.tile_id;
        string owner = meta is Kingdom kingdom ? kingdom.name : meta is City city ? city.name : meta?.GetType().Name ?? "无";
        return $"块{zone.id}({zone.x},{zone.y}) 格({tile.x},{tile.y}) {tile.Type.id} 色{Hex(pixels[id])} 城{zone.city?.name ?? "无"} " +
               $"图层归属 {owner}；{SmoothBorders.Describe(id, zone.id)}";
    }
}
