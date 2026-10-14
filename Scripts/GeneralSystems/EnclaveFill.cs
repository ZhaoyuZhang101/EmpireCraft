using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 图层补洞(用户 2026-10-14："如果某个山脉被地块包围，那就把那个山脉也纳入进帝国或者国家图层")：
// 领土仍守原版规矩(山地、走不到的地不领，见 CityExpansionPatch)，只在国家图层、帝国图层上把被一家包住的无主地画成它的：
//   · 连成一片的无主地块(没有城；湖也算)四面只挨着同一家(图层按城池归属那一档算出的国家/帝国)，不通海、不靠地图边，
//     就算这一家的——画上它的颜色，图层的归属查询也返回它(平滑国界、悬停高亮、点选都按这个，边线自然消失)；
//   · 被遗弃的空城不算无主地，不会被涂掉；一片太大的(超过 MaxZones 块)不补。
// 每隔几秒重算一次，变了的地块连同四邻重画。
public static class EnclaveFill
{
    private const float RefreshSeconds = 5f;
    private const int MaxZones = 400;

    private sealed class Layer
    {
        public MetaTypeAsset asset;
        public MetaZoneGetMeta full;
        public MetaZoneGetMetaSimple simple;
        public MetaZoneDrawAction draw;
        public Dictionary<TileZone, IMetaObject> fill = new();
    }

    private static readonly List<Layer> Layers = new();
    private static bool _installed;
    private static float _next;
    private static object _world;

    public static void ResetWorldState()
    {
        foreach (Layer layer in Layers) layer.fill.Clear();
        _world = null;
        _next = 0f;
    }

    // 套在国家图层、帝国图层上(只管"按城池归属"那一档)
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
                option == 0 && zone != null && layer.fill.TryGetValue(zone, out IMetaObject meta) ? meta : layer.full(zone, option);
            asset.tile_get_metaobject_0 = zone =>
                zone != null && layer.fill.TryGetValue(zone, out IMetaObject meta) ? meta : layer.simple(zone);
            asset.draw_zones = current =>
            {
                layer.draw(current);
                DrawFill(layer, current);
            };
        }
    }

    // 原版画完本图层后补画包住的无主地(按城池归属那一档)
    private static void DrawFill(Layer layer, MetaTypeAsset asset)
    {
        if (layer.fill.Count == 0 || asset.getZoneOptionState() != 0 || asset.isMetaZoneOptionSelectedFluid()) return;
        ZoneCalculator zones = World.world?.zone_calculator;
        if (zones == null) return;
        MetaZoneGetMetaSimple getter = asset.tile_get_metaobject_0;
        foreach (TileZone zone in layer.fill.Keys)
        {
            zones.drawBegin();
            zones.drawZoneMeta(zone, asset, getter);
            zones.drawEnd(zone);
        }
    }

    // 每帧(ModClass.Update)；每隔几秒重算一次
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        Install();
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        float now = Time.unscaledTime;
        if (now < _next && _next - now <= RefreshSeconds) return;
        _next = now + RefreshSeconds;
        try
        {
            using (FrameProfiler.Measure("图层补洞")) foreach (Layer layer in Layers) Refresh(layer, world.zone_calculator.zones);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][图层补洞] 重算失败: {exception.Message}");
        }
    }

    private static void Refresh(Layer layer, List<TileZone> zones)
    {
        var fill = new Dictionary<TileZone, IMetaObject>();
        var seen = new HashSet<TileZone>();
        var component = new List<TileZone>();
        var queue = new Queue<TileZone>();
        foreach (TileZone start in zones)
        {
            if (start == null || start.city != null || !seen.Add(start)) continue;
            component.Clear();
            queue.Clear();
            queue.Enqueue(start);
            bool open = false;
            IMetaObject owner = null;
            bool mixed = false;
            while (queue.Count > 0)
            {
                TileZone zone = queue.Dequeue();
                component.Add(zone);
                if (zone.world_edge || SeaRegionSystem.IsSea(zone)) open = true;
                foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                {
                    if (next == null)
                    {
                        open = true;
                        continue;
                    }
                    if (next.city == null)
                    {
                        if (seen.Add(next)) queue.Enqueue(next);
                        continue;
                    }
                    IMetaObject meta = layer.simple(next);
                    if (meta == null || owner != null && !ReferenceEquals(owner, meta)) mixed = true;
                    else owner = meta;
                }
            }
            if (open || mixed || owner == null || component.Count > MaxZones) continue;
            foreach (TileZone zone in component) fill[zone] = owner;
        }
        // 变了的地块连同四邻重画(边线跟着变)
        var changed = new HashSet<TileZone>();
        foreach (var pair in layer.fill)
            if (!fill.TryGetValue(pair.Key, out IMetaObject now) || !ReferenceEquals(now, pair.Value)) changed.Add(pair.Key);
        foreach (var pair in fill)
            if (!layer.fill.TryGetValue(pair.Key, out IMetaObject before) || !ReferenceEquals(before, pair.Value)) changed.Add(pair.Key);
        layer.fill = fill;
        foreach (TileZone zone in changed)
        {
            zone.resetRenderHelpers();
            foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>()) next?.resetRenderHelpers();
            if (!fill.ContainsKey(zone)) World.world.zone_calculator.drawZoneClear(zone);
        }
    }
}
