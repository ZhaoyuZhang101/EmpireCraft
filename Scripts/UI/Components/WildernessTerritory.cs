using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 无人区并进国土(用户 2026-10-29："这些无人地块一般会被纳入有实体城市的法理。一旦任何一个国家获取了这个法理，那么这个无人地块就会
// 自动被纳入国家范围，主要是模拟西藏俄罗斯之类的准备的")：挂了法理、被某国取得的无人区地块(WildernessSystem.OwnerOf)，
// 在国家图层、帝国图层上画成那一国的。做法同图层补洞(见 EnclaveFill)，套在这两个图层的归属查询外面：
//   · 归属查询返回那一国(帝国图层取它的城在图层上的样子)：原版按它上色、算边线，国界把这些地块圈进去，国土与它的无人区之间不画线；
//     平滑国界、悬停高亮、点选都按这个；只管"按城池归属"那一档；
//   · 原版只画城的地块，画完本图层后补画这些地块；平滑国界的整图画布不走原版巡检，按这里的归属逐格涂：
//     无人区轮廓里的格(见 WildernessShape)按这片各块的持有国平滑涂色，同被占的城(见 SmoothBorders.FillAt)；
//     城换了主，跟着它的无人区随即改画(见 Nudge)；太小的碎块并进四周挨得最多的那一国(见 WildernessSystem.RefreshFragments)；
//   · 同国土一个样(用户 10-30 "像王国纳入帝国图层或者城市纳入王国一样……平时只能看到帝国名称，鼠标放上去才会显示无人区的名字")：
//     国色不淡、不撒纹样、不标无人区名(只标在没并进国土的那部分，见 TerritoryLabelRenderer.SubmitWilderness)；
//     国名、帝国名的铭牌把并进的地块算进去排版(见 ZonesOf)；
//   · 行政区与法理(用户 10-30 "无人区也应该属于行政区划，或者王国级法理"，选"剧本按州府锚点划")：每块无人区跟着一座城
//     (WildernessSystem.OriginOf：剧本州府锚点定的城，否则所挂王国法理里最近的城)。国家图层上那座城属于持有国(或同一帝国的省)
//     就画成那座城所在的国(省)，帝国图层是所属帝国；王国法理、帝国法理、天下图层上画成那座城在图层上的样子(所挂的王国法理、
//     它的帝国法理、天下)，各档都画(流动的那档除外)；
//   · 悬停这些地块时原版的国家提示让给无人区提示(名字、类型，加一行归属，见 WildernessOverlay)。
// 每 RefreshSeconds 秒、区域变了(Version)、换图层时重算正显示的那个图层；变了的地块连同四邻重画。
// 接管每帧对一遍(见 Hook、GeneralSystems.LayerHooks)：以前只在头一回套、三样委托缺一样就整个图层不接管，套上之后别处再换委托也不知道——
// 提示里写着归了某国，国家图层上却没画进去(用户 2026-10-29 "可是这个山脉也没有归属""至少没有被纳入国家图层")。
// 区域变了时，划进、划出无人区的地块连同四邻重画色块与城市平面图层(城的地块被划走，城那一层的颜色、边界跟着变)
public static class WildernessTerritory
{
    private const float RefreshSeconds = 2f;

    private sealed class Layer
    {
        public MetaTypeAsset asset;
        // 套在里面的原委托(取一国在本图层上的样子用)；套上去的委托(对一遍还是不是它)
        public MetaZoneGetMeta full, fullHook;
        public MetaZoneGetMetaSimple simple, simpleHook;
        public MetaZoneDrawAction drawHook;
        // 法理、天下图层：各档(流动的除外)都并进，归属照无人区跟着的城在图层上的样子；国家、帝国图层只在按城池归属那一档
        public bool titles;
        public Dictionary<TileZone, IMetaObject> fill = new();
        public Dictionary<TileZone, Kingdom> owners = new();
        // 按国分的并进地块(铭牌排版用)
        public Dictionary<Kingdom, List<TileZone>> byOwner = new();
        // 按跟着的城分的并进地块(法理、天下的铭牌排版用：铭牌算哪些城，就连同跟着这些城的无人区，见 ZonesFollowing)
        public Dictionary<City, List<TileZone>> byOrigin = new();
        // 上一轮的几张表，下一轮清空了接着用(同上面几张轮换)
        public Dictionary<TileZone, IMetaObject> spareFill = new();
        public Dictionary<TileZone, Kingdom> spareOwners = new();
        public Dictionary<City, List<TileZone>> spareByOrigin = new();
        // 日志：上回记的并进块数、下回最早何时再记
        public int logged = -1;
        public float logAt;
    }

    private static readonly List<Layer> Layers = new();
    private static readonly Dictionary<Kingdom, IMetaObject> Metas = new();
    // 上次见到的划区(地块 → 区域编号)：区域变了时比出划进、划出的地块
    private static readonly Dictionary<int, int> Regions = new(), Fresh = new();
    private static float _next;
    private static object _world;
    private static MetaTypeAsset _shown;
    private static int _shownOption = -1;
    private static int _version = int.MinValue;

    // 正显示的图层上并进国土的地块变了、换了图层就加一(无人区名、国名铭牌据此重排)
    internal static int Revision { get; private set; }

    public static void ResetWorldState()
    {
        foreach (Layer layer in Layers)
        {
            layer.fill.Clear();
            layer.owners.Clear();
            layer.byOwner.Clear();
            layer.byOrigin.Clear();
            layer.logged = -1;
        }
        Regions.Clear();
        _world = null;
        _next = 0f;
        _version = int.MinValue;
    }

    // 高亮(见 SmoothBorders.FinishFrame)：高亮的城跟着的无人区地块一起提亮，同城的地一个样；国家、帝国图层上再连同这些城所属的国
    // 并进的无人区——以前只提亮城的地块连同周围两圈，更远的无人区不亮，亮暗之间切成一块块方的(用户 10-30 "这里高亮这里依旧有方块
    // 边界残留")；后来按归属对象找，高亮里只要沾上一个别的归属对象，整张图上挂它的无人区都亮了(用户 10-30 "不仅对应的法理亮，
    // 整个图都亮了""高亮应该只有对应的地区和争议地区")
    private static readonly HashSet<City> LitCities = new();
    private static readonly HashSet<Kingdom> LitKingdoms = new();
    private static readonly List<int> LitWild = new();

    internal static void AddClaimed(HashSet<int> zones)
    {
        if (zones == null || zones.Count == 0 || _shown == null) return;
        Layer shown = null;
        foreach (Layer layer in Layers)
            if (ReferenceEquals(layer.asset, _shown))
            {
                shown = layer;
                break;
            }
        ZoneCalculator calc = World.world?.zone_calculator;
        if (shown == null || shown.fill.Count == 0 || calc == null) return;
        foreach (int id in zones)
        {
            City city = calc.getZoneByID(id)?.city;
            if (city == null || !LitCities.Add(city)) continue;
            if (!shown.titles && city.kingdom != null) LitKingdoms.Add(city.kingdom);
        }
        foreach (City city in LitCities)
            if (shown.byOrigin.TryGetValue(city, out List<TileZone> following))
                foreach (TileZone zone in following) LitWild.Add(zone.id);
        foreach (Kingdom kingdom in LitKingdoms)
            if (shown.byOwner.TryGetValue(kingdom, out List<TileZone> owned))
                foreach (TileZone zone in owned) LitWild.Add(zone.id);
        foreach (int id in LitWild) zones.Add(id);
        LitCities.Clear();
        LitKingdoms.Clear();
        LitWild.Clear();
    }

    // 正显示的图层上这块无人区并进了(画成了国、省、法理的样子；见 TerritoryLabelRenderer 无人区名只标没并进的部分)
    internal static bool Claims(TileZone zone)
    {
        if (zone == null || zone.city != null || _shown == null) return false;
        foreach (Layer layer in Layers)
            if (ReferenceEquals(layer.asset, _shown)) return layer.fill.ContainsKey(zone);
        return false;
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

    // 地图悬停与实际染色使用同一行政归属；无人区没有实体城市，不能只读 zone.city。
    internal static Kingdom KingdomAt(TileZone zone)
    {
        if (zone == null) return null;
        City city = zone.city;
        if (city != null && (city.data == null || city.isRekt())) return null;
        Kingdom kingdom = city != null ? city.kingdom : WildernessSystem.OwnerOf(zone) ?? WorldMapWilderness.KnownOwner(zone);
        return kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral() ? kingdom : null;
    }

    internal static City AdministrativeCityAt(TileZone zone)
    {
        Kingdom kingdom = KingdomAt(zone);
        if (kingdom == null) return null;
        City city = zone.city ?? WildernessSystem.OriginOf(zone) ?? WorldMapWilderness.KnownCity(zone);
        return city?.data != null && !city.isRekt() && SameRealm(city.kingdom, kingdom) ? city : null;
    }

    // 只替换显示对象，不改地图查询返回的帝国或地块的真实归属。
    internal static IMetaObject ProvinceShade(TileZone zone, IMetaObject meta, Kingdom hovered)
    {
        return hovered != null && meta is EmpireCraft.Scripts.Layer.Empire empire && KingdomAt(zone) == hovered &&
               EmpireMembershipService.MapEmpire(hovered) == empire ? hovered : meta;
    }

    // 正显示的图层上并进这一国的无人区地块(没有为空)
    private static readonly List<TileZone> None = new();

    internal static List<TileZone> ZonesOf(Kingdom kingdom)
    {
        if (kingdom == null || _shown == null) return None;
        foreach (Layer layer in Layers)
            if (ReferenceEquals(layer.asset, _shown))
                return layer.byOwner.TryGetValue(kingdom, out List<TileZone> zones) ? zones : None;
        return None;
    }

    // 正显示的图层上并进了、跟着这座城的无人区地块(没有为空)：法理、天下的铭牌连同它们排版，同城的地一个样
    // (用户 10-30 "你的法理的铭牌也要算无人区啊""都成他的行政区划了怎么还没算入领土")
    internal static List<TileZone> ZonesFollowing(City city)
    {
        if (city == null || _shown == null) return None;
        foreach (Layer layer in Layers)
            if (ReferenceEquals(layer.asset, _shown))
                return layer.byOrigin.TryGetValue(city, out List<TileZone> zones) ? zones : None;
        return None;
    }

    // 套在国家、帝国图层的归属查询、画法外面；每帧对一遍：自己套的那层不在眼下的委托里了(被别处换过，或头一回时还没有)
    // 就套在新的外面(见 LayerHooks，图层补洞也套这几样)。里面那个记在本地(不经 Layer 的字段转)，再套一层也不会自己调自己
    private static void Hook()
    {
        List<MetaTypeAsset> assets = AssetManager.meta_type_library?.list;
        if (assets == null) return;
        foreach (MetaTypeAsset asset in assets)
        {
            if (asset == null || asset.map_mode != MetaType.Kingdom && asset.map_mode != MetaTypeExtension.Empire &&
                asset.map_mode != MetaTypeExtension.KingdomTitle && asset.map_mode != MetaTypeExtension.Tianxia) continue;
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
                layer = new Layer
                {
                    asset = asset,
                    titles = asset.map_mode == MetaTypeExtension.KingdomTitle || asset.map_mode == MetaTypeExtension.Tianxia
                };
                Layers.Add(layer);
                var tooltip = asset.check_cursor_tooltip;
                if (tooltip != null)
                    asset.check_cursor_tooltip = (zone, shown, option) => WildernessOverlay.TipShown(zone) || tooltip(zone, shown, option);
                LogService.LogInfo($"[EmpireCraft][无人区并进国土] 接管图层 {asset.id}" +
                                   $"(按块取归属{(asset.tile_get_metaobject_0 != null ? "有" : "无")}，画法{(asset.draw_zones != null ? "有" : "无")})");
            }
            MetaZoneGetMeta full = asset.tile_get_metaobject;
            if (full != null && !LayerHooks.Contains(full, layer.fullHook))
            {
                if (layer.fullHook != null) LogService.LogInfo($"[EmpireCraft][无人区并进国土] 图层 {asset.id} 的归属查询被换过，重新套上");
                layer.full = full;
                layer.fullHook = (zone, option) => (option == 0 || layer.titles) && Claimed(layer, zone) is IMetaObject meta ? meta : full(zone, option);
                LayerHooks.Add(layer.fullHook, full);
                asset.tile_get_metaobject = layer.fullHook;
            }
            MetaZoneGetMetaSimple simple = asset.tile_get_metaobject_0;
            if (simple != null && !LayerHooks.Contains(simple, layer.simpleHook))
            {
                layer.simple = simple;
                layer.simpleHook = zone => Claimed(layer, zone) ?? simple(zone);
                LayerHooks.Add(layer.simpleHook, simple);
                asset.tile_get_metaobject_0 = layer.simpleHook;
            }
            MetaZoneDrawAction draw = asset.draw_zones;
            if (draw != null && !LayerHooks.Contains(draw, layer.drawHook))
            {
                layer.drawHook = current =>
                {
                    draw(current);
                    DrawClaimed(layer, current);
                };
                LayerHooks.Add(layer.drawHook, draw);
                asset.draw_zones = layer.drawHook;
            }
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
        if (layer.fill.Count == 0 || !layer.titles && asset.getZoneOptionState() != 0 || asset.isMetaZoneOptionSelectedFluid()) return;
        ZoneCalculator zones = World.world?.zone_calculator;
        if (zones == null) return;
        MetaZoneGetMetaSimple getter = asset.tile_get_metaobject_0;
        // 法理图层按眼下这一档取(_0 永远是帝国法理那一档；王国法理那档的边线要跟邻城的王国法理比)
        if (layer.titles && asset.tile_get_metaobject != null)
        {
            int option = asset.getZoneOptionState();
            MetaZoneGetMeta full = asset.tile_get_metaobject;
            getter = zone => full(zone, option);
        }
        if (getter == null) return;
        if (asset.map_mode == MetaTypeExtension.Empire && asset.getZoneOptionState() == 0)
        {
            Kingdom hovered = KingdomAt(World.world.getMouseTilePosCachedFrame()?.zone);
            MetaZoneGetMetaSimple actual = getter;
            getter = zone => ProvinceShade(zone, actual(zone), hovered);
        }
        foreach (TileZone zone in layer.fill.Keys)
        {
            if (zone.city != null) continue;
            zones.drawBegin();
            zones.drawZoneMeta(zone, asset, getter);
            zones.drawEnd(zone);
        }
    }

    // 城换了主(整图画布扫到的，见 SmoothBorders.CanvasProcess)：跟着这些城的无人区很快重算归属，不等满 RefreshSeconds
    // (用户 2026-11-06 "一旦城市变更政权对应的无人区也要变更, 不然会导致飞地")；攒 NudgeSeconds 一起算，一场仗连丢几城只算一回
    private const float NudgeSeconds = 0.2f;

    internal static void Nudge() => _next = Mathf.Min(_next, Time.unscaledTime + NudgeSeconds);

    // 每帧(PureMapPatch.AfterZoneAnimations，纯净地图开没开都调)
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || !Config.game_loaded) return;
        Hook();
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        // 与整图画布、原版巡检画的同一个图层(见 SmoothBorders.DrawCanvas)
        MetaTypeAsset shown = world.zone_calculator._mode_asset ?? world.getCachedMapMetaAsset();
        int option = shown?.getZoneOptionState() ?? 0;
        // 换图层、换档(法理图层的王国法理、帝国法理两档并进的样子不同)都重算
        bool switched = !ReferenceEquals(shown, _shown) || option != _shownOption;
        _shown = shown;
        _shownOption = option;
        if (switched) Revision++;
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
        // 没有城、图层上也不归谁的：清掉原来(城)的颜色。整图画布画着时不清：画布自己按格重画(上面的重画记号)，
        // 整块清掉会连邻城伸进这块的平滑边一起抹掉，闪一下
        if (zone.city == null && !SmoothBorders.CanvasActive && shown?.tile_get_metaobject_0?.Invoke(zone) == null)
            calc.drawZoneClear(zone);
    }

    // 每 2 秒整片重算一回(世界地图上两万来块)：以前每回新建三四张这么大的表，性能日志里「无人区并进国土」每帧分配两百来 KB；
    // 现在两套表轮换、清空了接着用，按城、按国分的地块列表回收再用(读这些列表的地方都当场抄走，见 ZonesOf、ZonesFollowing)
    private static readonly Stack<List<TileZone>> ListPool = new();
    private static readonly HashSet<TileZone> Changed = new();

    private static void Recycle<TKey>(Dictionary<TKey, List<TileZone>> lists)
    {
        foreach (List<TileZone> list in lists.Values)
        {
            list.Clear();
            if (ListPool.Count < 4096) ListPool.Push(list);
        }
        lists.Clear();
    }

    private static List<TileZone> RentList() => ListPool.Count > 0 ? ListPool.Pop() : new List<TileZone>();

    private static void Refresh(Layer layer, MapBox world)
    {
        Dictionary<TileZone, IMetaObject> fill = layer.spareFill;
        Dictionary<TileZone, Kingdom> owners = layer.spareOwners;
        Dictionary<City, List<TileZone>> byOrigin = layer.spareByOrigin;
        fill.Clear();
        owners.Clear();
        Recycle(byOrigin);
        ZoneCalculator calc = world.zone_calculator;
        Metas.Clear();
        CityMetas.Clear();
        if (WildernessSystem.Enabled)
            foreach (WildRegion region in WildernessSystem.Regions)
            {
                if (region?.Zones == null) continue;
                foreach (int id in region.Zones)
                {
                    TileZone zone = calc.getZoneByID(id);
                    if (zone == null || zone.city != null) continue;
                    // 跟着的城(行政区、所挂法理)
                    City origin = WildernessSystem.OriginOf(zone);
                    if (layer.titles)
                    {
                        IMetaObject seen = MetaAt(layer, origin);
                        if (seen == null) continue;
                        fill[zone] = seen;
                        Follow(byOrigin, origin, zone);
                        continue;
                    }
                    Kingdom owner = WildernessSystem.OwnerOf(zone);
                    if (owner?.data == null || owner.isRekt()) continue;
                    // 跟着的城属于持有国(或同一帝国的省)：画成那座城所在的国(省)；太小并进邻国的碎块按并进的国画
                    Kingdom drawn = owner;
                    IMetaObject meta = null;
                    if (origin?.kingdom != null && SameRealm(origin.kingdom, owner) && !WildernessSystem.MergedInto(zone, owner))
                    {
                        meta = MetaAt(layer, origin);
                        if (meta != null) drawn = origin.kingdom;
                    }
                    if (meta == null && !Metas.TryGetValue(owner, out meta)) Metas[owner] = meta = MetaOf(layer, owner);
                    if (meta == null) continue;
                    fill[zone] = meta;
                    owners[zone] = drawn;
                    if (drawn == origin?.kingdom) Follow(byOrigin, origin, zone);
                }
            }
        // 历史剧本中退出无人区的山谷、已开垦地仍跟随原法理；不凭空立城或绕过城市地块上限。
        foreach (int id in WorldMapWilderness.KnownTerritory())
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone == null || zone.city != null || WildernessSystem.IsWild(zone)) continue;
            City origin = WorldMapWilderness.KnownCity(zone);
            Kingdom owner = WorldMapWilderness.KnownOwner(zone);
            IMetaObject meta = origin == null ? null : MetaAt(layer, origin);
            if (layer.titles)
            {
                if (meta == null) continue;
            }
            else
            {
                if (owner == null) continue;
                if (meta == null && !Metas.TryGetValue(owner, out meta)) Metas[owner] = meta = MetaOf(layer, owner);
                if (meta == null) continue;
                owners[zone] = owner;
            }
            fill[zone] = meta;
            Follow(byOrigin, origin, zone);
        }
        Metas.Clear();
        CityMetas.Clear();
        // 变了的地块连同四邻重画(边线跟着变)
        HashSet<TileZone> changed = Changed;
        changed.Clear();
        foreach (KeyValuePair<TileZone, IMetaObject> pair in layer.fill)
            if (!fill.TryGetValue(pair.Key, out IMetaObject after) || !ReferenceEquals(after, pair.Value)) changed.Add(pair.Key);
        foreach (KeyValuePair<TileZone, IMetaObject> pair in fill)
            if (!layer.fill.TryGetValue(pair.Key, out IMetaObject before) || !ReferenceEquals(before, pair.Value)) changed.Add(pair.Key);
        // 同帝国省份间改隶时帝国对象没变，行政归属缓存仍必须刷新。
        foreach (KeyValuePair<TileZone, Kingdom> pair in layer.owners)
            if (!owners.TryGetValue(pair.Key, out Kingdom after) || after != pair.Value) changed.Add(pair.Key);
        foreach (KeyValuePair<TileZone, Kingdom> pair in owners)
            if (!layer.owners.TryGetValue(pair.Key, out Kingdom before) || before != pair.Value) changed.Add(pair.Key);
        layer.spareFill = layer.fill;
        layer.spareOwners = layer.owners;
        layer.spareByOrigin = layer.byOrigin;
        layer.fill = fill;
        layer.owners = owners;
        layer.byOrigin = byOrigin;
        if (changed.Count > 0 || layer.byOwner.Count == 0 && owners.Count > 0)
        {
            Dictionary<Kingdom, List<TileZone>> byOwner = layer.byOwner;
            Recycle(byOwner);
            foreach (KeyValuePair<TileZone, Kingdom> pair in owners)
            {
                if (!byOwner.TryGetValue(pair.Value, out List<TileZone> zones)) byOwner[pair.Value] = zones = RentList();
                zones.Add(pair.Key);
            }
            Revision++;
        }
        // 日志：并进的块数变了记一条(至多半分钟一条)——图层上没画进去时据此分清是没算出归属，还是算出了没画上
        float now = Time.unscaledTime;
        if (fill.Count != layer.logged && now >= layer.logAt)
        {
            layer.logged = fill.Count;
            layer.logAt = now + 30f;
            LogService.LogInfo($"[EmpireCraft][无人区并进国土] {layer.asset.id}：{fill.Count} 块并进 {new HashSet<Kingdom>(owners.Values).Count} 国");
        }
        foreach (TileZone zone in changed)
        {
            zone.resetRenderHelpers();
            foreach (TileZone next in zone.neighbours ?? global::System.Array.Empty<TileZone>()) next?.resetRenderHelpers();
            // 整图画布画着时不清(同 Repaint)
            if (!fill.ContainsKey(zone) && zone.city == null && !SmoothBorders.CanvasActive) calc.drawZoneClear(zone);
            SmoothBorders.ClaimChanged(zone.id);
            WildernessOverlay.MarkAround(zone);
        }
        changed.Clear();
    }

    private static void Follow(Dictionary<City, List<TileZone>> byOrigin, City origin, TileZone zone)
    {
        if (origin == null) return;
        if (!byOrigin.TryGetValue(origin, out List<TileZone> zones)) byOrigin[origin] = zones = RentList();
        zones.Add(zone);
    }

    private static readonly Dictionary<City, IMetaObject> CityMetas = new();

    // 这座城的地块在本图层上的样子(一轮里每座城只取一次)
    private static IMetaObject MetaAt(Layer layer, City city)
    {
        if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) return null;
        if (CityMetas.TryGetValue(city, out IMetaObject meta)) return meta;
        TileZone anchor = city.getTile()?.zone ?? city.zones[0];
        // 按眼下这一档取(法理图层的 _0 永远是帝国法理那一档，王国法理那一档要按档取，见 EmpireCraftMetaTypeLibrary)
        meta = layer.full != null ? layer.full(anchor, layer.asset.getZoneOptionState()) : layer.simple?.Invoke(anchor);
        if (meta != null && !meta.isAlive()) meta = null;
        CityMetas[city] = meta;
        return meta;
    }

    // 同一国，或同一帝国(帝国的省)
    private static bool SameRealm(Kingdom a, Kingdom b)
    {
        if (a == b) return true;
        if (a == null || b == null) return false;
        EmpireCraft.Scripts.Layer.Empire empire = a.GetEmpire();
        return empire != null && empire == b.GetEmpire();
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
        IMetaObject meta = anchor == null ? null : layer.simple != null ? layer.simple(anchor) : layer.full?.Invoke(anchor, 0);
        return meta != null && meta.isAlive() ? meta : owner;
    }
}
