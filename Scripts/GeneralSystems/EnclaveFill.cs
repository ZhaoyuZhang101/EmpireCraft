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
        // 套在里面的原委托；套上去的委托(每帧对一遍还是不是它，同 UI.Components.WildernessTerritory.Hook)
        public MetaZoneGetMeta full, fullHook;
        public MetaZoneGetMetaSimple simple, simpleHook;
        public MetaZoneDrawAction drawHook;
        public Dictionary<TileZone, IMetaObject> fill = new();
    }

    private static readonly List<Layer> Layers = new();
    private static float _next;
    private static object _world;

    public static void ResetWorldState()
    {
        foreach (Layer layer in Layers) layer.fill.Clear();
        _world = null;
        _next = 0f;
    }

    // 套在国家图层、帝国图层上(只管"按城池归属"那一档)
    // 每帧对一遍：自己套的那层不在眼下的委托里了(被别处换过，或头一回时还没有)就套在新的外面(见 LayerHooks)；
    // 缺按块取归属、画法的也照样接管归属查询
    // (以前只在头一回套、三样缺一样就整个图层不管)。里面那个记在本地，再套一层也不会自己调自己
    private static void Install()
    {
        List<MetaTypeAsset> assets = AssetManager.meta_type_library?.list;
        if (assets == null) return;
        foreach (MetaTypeAsset asset in assets)
        {
            if (asset == null || asset.map_mode != MetaType.Kingdom && asset.map_mode != MetaTypeExtension.Empire) continue;
            Layer layer = null;
            foreach (Layer known in Layers)
                if (ReferenceEquals(known.asset, asset))
                {
                    layer = known;
                    break;
                }
            if (layer == null)
            {
                if (asset.tile_get_metaobject == null) continue;
                layer = new Layer { asset = asset };
                Layers.Add(layer);
            }
            MetaZoneGetMeta full = asset.tile_get_metaobject;
            if (full != null && !LayerHooks.Contains(full, layer.fullHook))
            {
                layer.full = full;
                layer.fullHook = (zone, option) =>
                    option == 0 && zone != null && layer.fill.TryGetValue(zone, out IMetaObject meta) ? meta : full(zone, option);
                LayerHooks.Add(layer.fullHook, full);
                asset.tile_get_metaobject = layer.fullHook;
            }
            MetaZoneGetMetaSimple simple = asset.tile_get_metaobject_0;
            if (simple != null && !LayerHooks.Contains(simple, layer.simpleHook))
            {
                layer.simple = simple;
                layer.simpleHook = zone => zone != null && layer.fill.TryGetValue(zone, out IMetaObject meta) ? meta : simple(zone);
                LayerHooks.Add(layer.simpleHook, simple);
                asset.tile_get_metaobject_0 = layer.simpleHook;
            }
            MetaZoneDrawAction draw = asset.draw_zones;
            if (draw != null && !LayerHooks.Contains(draw, layer.drawHook))
            {
                layer.drawHook = current =>
                {
                    draw(current);
                    DrawFill(layer, current);
                };
                LayerHooks.Add(layer.drawHook, draw);
                asset.draw_zones = layer.drawHook;
            }
        }
    }

    // 图层本来按城池归属取的(套在里面的那个)
    private static IMetaObject Original(Layer layer, TileZone zone) =>
        layer.simple != null ? layer.simple(zone) : layer.full?.Invoke(zone, 0);

    // 原版画完本图层后补画包住的无主地(按城池归属那一档)
    private static void DrawFill(Layer layer, MetaTypeAsset asset)
    {
        if (layer.fill.Count == 0 || asset.getZoneOptionState() != 0 || asset.isMetaZoneOptionSelectedFluid()) return;
        ZoneCalculator zones = World.world?.zone_calculator;
        if (zones == null) return;
        MetaZoneGetMetaSimple getter = asset.tile_get_metaobject_0;
        if (getter == null) return;
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
        // 与整图画布、原版巡检画的同一个图层(见 SmoothBorders.DrawCanvas)
        MetaTypeAsset shown = world.zone_calculator._mode_asset ?? world.getCachedMapMetaAsset();
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
                    IMetaObject meta = Original(layer, next);
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
