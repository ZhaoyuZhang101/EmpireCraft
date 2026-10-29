using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 无人区并进国土(用户 2026-10-29："这些无人地块一般会被纳入有实体城市的法理。一旦任何一个国家获取了这个法理，那么这个无人地块就会
// 自动被纳入国家范围，主要是模拟西藏俄罗斯之类的准备的")：挂了法理、被某国取得的无人区地块(WildernessSystem.OwnerOf)，
// 在国家图层、帝国图层上画成那一国的。做法同图层补洞(见 EnclaveFill)，套在这两个图层的归属查询外面：
//   · 归属查询返回那一国(帝国图层取它的城在图层上的样子)：原版按它上色、算边线，国界把这些地块圈进去，国土与它的无人区之间不画线；
//     平滑国界、悬停高亮、点选都按这个；只管"按城池归属"那一档；
//   · 原版只画城的地块，画完本图层后补画这些地块；
//   · 纯净地图下这些地块的国色淡一些，底下无人区图层的类型纹样透上来(见 PureMapPatch.FinishZone、WildernessOverlay)；
//   · 悬停这些地块时原版的国家提示让给无人区提示(名字、类型，加一行归属，见 WildernessOverlay)。
// 每 RefreshSeconds 秒、区域变了(Version)、换图层时重算正显示的那个图层；变了的地块连同四邻重画。
// 区域变了时，划进、划出无人区的地块连同四邻重画色块与城市平面图层(城的地块被划走，城那一层的颜色、边界跟着变)
public static class WildernessTerritory
{
    private const float RefreshSeconds = 2f;

    private sealed class Layer
    {
        public MetaTypeAsset asset;
        public MetaZoneGetMeta full;
        public MetaZoneGetMetaSimple simple;
        public MetaZoneDrawAction draw;
        public Dictionary<TileZone, IMetaObject> fill = new();
        public Dictionary<TileZone, Kingdom> owners = new();
    }

    private static readonly List<Layer> Layers = new();
    private static readonly Dictionary<Kingdom, IMetaObject> Metas = new();
    // 上次见到的划区(地块 → 区域编号)：区域变了时比出划进、划出的地块
    private static readonly Dictionary<int, int> Regions = new(), Fresh = new();
    private static bool _installed;
    private static float _next;
    private static object _world;
    private static MetaTypeAsset _shown;
    private static int _version = int.MinValue;

    public static void ResetWorldState()
    {
        foreach (Layer layer in Layers)
        {
            layer.fill.Clear();
            layer.owners.Clear();
        }
        Regions.Clear();
        _world = null;
        _next = 0f;
        _version = int.MinValue;
    }

    // 正显示的图层上这块无人区画成了哪一国的(没有为 null；帝国金边按它算，见 PureMapPatch.AfterApplyColors)
    internal static Kingdom Owner(TileZone zone)
    {
        if (zone == null || zone.city != null || _shown == null) return null;
        foreach (Layer layer in Layers)
            if (ReferenceEquals(layer.asset, _shown))
                return layer.owners.TryGetValue(zone, out Kingdom owner) && owner?.data != null && !owner.isRekt() ? owner : null;
        return null;
    }

    private static void Install()
    {
        if (_installed || AssetManager.meta_type_library?.list == null) return;
        _installed = true;
        foreach (MetaTypeAsset asset in AssetManager.meta_type_library.list)
        {
            if (asset == null || asset.map_mode != MetaType.Kingdom && asset.map_mode != MetaTypeExtension.Empire) continue;
            if (asset.tile_get_metaobject == null || asset.tile_get_metaobject_0 == null || asset.draw_zones == null) continue;
            var layer = new Layer
            {
                asset = asset, full = asset.tile_get_metaobject, simple = asset.tile_get_metaobject_0, draw = asset.draw_zones
            };
            Layers.Add(layer);
            asset.tile_get_metaobject = (zone, option) =>
                option == 0 && Claimed(layer, zone) is IMetaObject meta ? meta : layer.full(zone, option);
            asset.tile_get_metaobject_0 = zone => Claimed(layer, zone) ?? layer.simple(zone);
            asset.draw_zones = current =>
            {
                layer.draw(current);
                DrawClaimed(layer, current);
            };
            var tooltip = asset.check_cursor_tooltip;
            if (tooltip != null)
                asset.check_cursor_tooltip = (zone, shown, option) => WildernessOverlay.TipShown(zone) || tooltip(zone, shown, option);
        }
    }

    // 有城的地块(核心保证与无人区互斥；没来得及重算的也不抢)不算
    private static IMetaObject Claimed(Layer layer, TileZone zone) =>
        zone != null && zone.city == null && layer.fill.TryGetValue(zone, out IMetaObject meta) && meta != null && meta.isAlive()
            ? meta
            : null;

    // 原版画完本图层后补画并进国土的无人区(按城池归属那一档)
    private static void DrawClaimed(Layer layer, MetaTypeAsset asset)
    {
        if (layer.fill.Count == 0 || asset.getZoneOptionState() != 0 || asset.isMetaZoneOptionSelectedFluid()) return;
        ZoneCalculator zones = World.world?.zone_calculator;
        if (zones == null) return;
        MetaZoneGetMetaSimple getter = asset.tile_get_metaobject_0;
        foreach (TileZone zone in layer.fill.Keys)
        {
            if (zone.city != null) continue;
            zones.drawBegin();
            zones.drawZoneMeta(zone, asset, getter);
            zones.drawEnd(zone);
        }
    }

    // 每帧(PureMapPatch.AfterZoneAnimations，纯净地图开没开都调)
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || !Config.game_loaded) return;
        Install();
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        MetaTypeAsset shown = world.getCachedMapMetaAsset();
        bool switched = !ReferenceEquals(shown, _shown);
        _shown = shown;
        bool regions = WildernessSystem.Version != _version;
        if (regions)
        {
            _version = WildernessSystem.Version;
            RegionsChanged(world, shown);
        }
        float now = Time.unscaledTime;
        if (!switched && !regions && now < _next && _next - now <= RefreshSeconds) return;
        _next = now + RefreshSeconds;
        using (FrameProfiler.Measure("无人区并进国土"))
            foreach (Layer layer in Layers)
                if (ReferenceEquals(layer.asset, shown)) Refresh(layer, world);
    }

    // 划进、划出无人区的地块(连同四邻)重画：城的地块被划走、划回，色块与城市平面图层都跟着变
    private static void RegionsChanged(MapBox world, MetaTypeAsset shown)
    {
        Fresh.Clear();
        if (WildernessSystem.Enabled)
            foreach (WildRegion region in WildernessSystem.Regions)
                if (region?.Zones != null)
                    foreach (int id in region.Zones) Fresh[id] = region.Id;
        ZoneCalculator calc = world.zone_calculator;
        foreach (KeyValuePair<int, int> pair in Regions)
            if (!Fresh.TryGetValue(pair.Key, out int now) || now != pair.Value) Repaint(calc, shown, calc.getZoneByID(pair.Key));
        foreach (KeyValuePair<int, int> pair in Fresh)
            if (!Regions.ContainsKey(pair.Key)) Repaint(calc, shown, calc.getZoneByID(pair.Key));
        Regions.Clear();
        foreach (KeyValuePair<int, int> pair in Fresh) Regions[pair.Key] = pair.Value;
        Fresh.Clear();
    }

    private static void Repaint(ZoneCalculator calc, MetaTypeAsset shown, TileZone zone)
    {
        if (zone == null) return;
        zone.resetRenderHelpers();
        foreach (TileZone next in zone.neighbours ?? global::System.Array.Empty<TileZone>()) next?.resetRenderHelpers();
        CityPlanOverlay.MarkZone(zone);
        // 没有城、图层上也不归谁的：清掉原来(城)的颜色
        if (zone.city == null && shown?.tile_get_metaobject_0?.Invoke(zone) == null) calc.drawZoneClear(zone);
    }

    private static void Refresh(Layer layer, MapBox world)
    {
        var fill = new Dictionary<TileZone, IMetaObject>();
        var owners = new Dictionary<TileZone, Kingdom>();
        ZoneCalculator calc = world.zone_calculator;
        Metas.Clear();
        if (WildernessSystem.Enabled)
            foreach (WildRegion region in WildernessSystem.Regions)
            {
                if (region?.Zones == null) continue;
                foreach (int id in region.Zones)
                {
                    TileZone zone = calc.getZoneByID(id);
                    if (zone == null || zone.city != null) continue;
                    Kingdom owner = WildernessSystem.OwnerOf(zone);
                    if (owner?.data == null || owner.isRekt()) continue;
                    if (!Metas.TryGetValue(owner, out IMetaObject meta)) Metas[owner] = meta = MetaOf(layer, owner);
                    if (meta == null) continue;
                    fill[zone] = meta;
                    owners[zone] = owner;
                }
            }
        Metas.Clear();
        // 变了的地块连同四邻重画(边线跟着变)
        var changed = new HashSet<TileZone>();
        foreach (KeyValuePair<TileZone, IMetaObject> pair in layer.fill)
            if (!fill.TryGetValue(pair.Key, out IMetaObject now) || !ReferenceEquals(now, pair.Value)) changed.Add(pair.Key);
        foreach (KeyValuePair<TileZone, IMetaObject> pair in fill)
            if (!layer.fill.TryGetValue(pair.Key, out IMetaObject before) || !ReferenceEquals(before, pair.Value)) changed.Add(pair.Key);
        layer.fill = fill;
        layer.owners = owners;
        foreach (TileZone zone in changed)
        {
            zone.resetRenderHelpers();
            foreach (TileZone next in zone.neighbours ?? global::System.Array.Empty<TileZone>()) next?.resetRenderHelpers();
            if (!fill.ContainsKey(zone) && zone.city == null) calc.drawZoneClear(zone);
            WildernessOverlay.MarkAround(zone);
        }
    }

    // 这一国在本图层上的样子：取它一座城的地块在图层上的归属(帝国图层是所属帝国)，取不到就是它自己
    private static IMetaObject MetaOf(Layer layer, Kingdom owner)
    {
        City seat = owner.capital != null && !owner.capital.isRekt() ? owner.capital : null;
        if (seat == null && owner.cities != null)
            foreach (City city in owner.cities)
                if (city != null && !city.isRekt())
                {
                    seat = city;
                    break;
                }
        TileZone anchor = seat?.zones != null && seat.zones.Count > 0 ? seat.zones[0] : null;
        IMetaObject meta = anchor != null ? layer.simple(anchor) : null;
        return meta != null && meta.isAlive() ? meta : owner;
    }
}
