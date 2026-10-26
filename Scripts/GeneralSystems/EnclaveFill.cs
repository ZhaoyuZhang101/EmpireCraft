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
// 开销(用户 2026-10-27 反馈"什么都不放、地图够大也卡")：以前每 5 秒对两个图层各把全图连成一片的无主地搜一遍(空地图整张图都是
// 一片)，每次新建几千项的集合。现在只算正显示的图层(切过去当场重算)，没有城就不搜；只从挨着城的无主地起搜，一片通海、靠地图边、
// 挨着两家、超过 MaxZones 块就当场作废，搜过的地块记着片号，别处连到作废的片也立刻作废；搜索用的集合复用。
public static class EnclaveFill
{
    private const float RefreshSeconds = 5f;
    private const int MaxZones = 400;
    private static readonly Dictionary<TileZone, int> Seen = new();
    private static readonly List<TileZone> Component = new();
    private static readonly Queue<TileZone> Pending = new();
    private static MetaTypeAsset _shown;

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
        MetaTypeAsset shown = world.getCachedMapMetaAsset();
        bool switched = !ReferenceEquals(shown, _shown);
        _shown = shown;
        if (!switched && now < _next && _next - now <= RefreshSeconds) return;
        _next = now + RefreshSeconds;
        try
        {
            using (FrameProfiler.Measure("图层补洞"))
                foreach (Layer layer in Layers)
                    if (ReferenceEquals(layer.asset, shown)) Refresh(layer, world);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][图层补洞] 重算失败: {exception.Message}");
        }
    }

    private static void Refresh(Layer layer, MapBox world)
    {
        var fill = new Dictionary<TileZone, IMetaObject>();
        if (world.cities.Count > 0) Search(layer, world.zone_calculator.zones, fill);
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

    // 找被一家包住的无主地：只从挨着城的无主地起搜；一片作废时搜过的地块仍记着片号，别处连到它们也随之作废
    private static void Search(Layer layer, List<TileZone> zones, Dictionary<TileZone, IMetaObject> fill)
    {
        Seen.Clear();
        int id = 0;
        foreach (TileZone start in zones)
        {
            if (start == null || start.city != null || Seen.ContainsKey(start) || !BordersCity(start)) continue;
            id++;
            Component.Clear();
            Pending.Clear();
            Pending.Enqueue(start);
            Seen[start] = id;
            bool rejected = false;
            IMetaObject owner = null;
            while (Pending.Count > 0 && !rejected)
            {
                TileZone zone = Pending.Dequeue();
                Component.Add(zone);
                if (zone.world_edge || SeaRegionSystem.IsSea(zone) || Component.Count + Pending.Count > MaxZones)
                {
                    rejected = true;
                    break;
                }
                foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                {
                    if (next == null)
                    {
                        rejected = true;
                        break;
                    }
                    if (next.city == null)
                    {
                        // 别的片搜过的：那片已作废(没作废的片是封闭的，连不到这里)
                        if (Seen.TryGetValue(next, out int other))
                        {
                            if (other == id) continue;
                            rejected = true;
                            break;
                        }
                        Seen[next] = id;
                        Pending.Enqueue(next);
                        continue;
                    }
                    IMetaObject meta = layer.simple(next);
                    if (meta == null || owner != null && !ReferenceEquals(owner, meta))
                    {
                        rejected = true;
                        break;
                    }
                    owner = meta;
                }
            }
            if (rejected || owner == null) continue;
            foreach (TileZone zone in Component) fill[zone] = owner;
        }
        Component.Clear();
        Pending.Clear();
    }

    private static bool BordersCity(TileZone zone)
    {
        foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
            if (next?.city != null) return true;
        return false;
    }
}
