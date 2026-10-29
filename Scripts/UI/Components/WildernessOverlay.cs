using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EmpireCraft.Scripts.UI.Components;

// 无人区图层(用户 2026-10-29："……作出无人区地块，可以有多个类型比如山脉冻土沙漠，将连片的沙漠或者山脉全覆盖……
// 主要是为了吧地图填满，同时能更有效地阻隔国家军队和更容易计算控制力"；划区见 WildernessSystem)：
// 一格一像素的分块贴图，做法同城市平面图层(见 CityPlanOverlay)：
//   · 铺在国家色块之下、路网之上(与城市平面图层同一档；无人区地块没有城，两层不重叠)：国色、国界压在上面，
//     平滑国界伸进来的国土照样盖住它；贴图着色同色块(随地图不透明度设置)，地形浮雕透上来；
//   · 底色取划区时定的 WildRegion.Color(按类型定色系、按编号微调，挨着的两片不至于一个颜色)；两种画法：
//       类型色(纯净地图下各图层；普通地图的国家、帝国、城市平面图层)：山脉灰褐、沙漠沙黄、冻原冰白偏蓝、雨林深绿、大泽青绿，
//       半透明铺满(不透明度取国色的 FillShare)，再按类型撒纹样(山的人字、沙丘短纹、雪花、林团、沼泽横纹)；
//       外沿画一道浅色虚线，只画在挨着无主地、别片无人区的边上(挨着国土的那边有国界，不再画)；
//       并进国土的地块(挂了法理、被某国取得，见 WildernessTerritory)由国色画，这层只留纹样，看得出是这国的山脉、沙漠；
//       原版城市图层(用户 2026-10-29 "无人区层基本和城市层级同级，只要调到城市图层就会自动出现")：每片像一座城一样画成一块领地，
//       整片底色(不透明度同国色)，边线同城界(实线、深一档)，没有纹样；
//     普通地图拉近到原版只画边框(色块内部透明)时也只画边线；
//   · 平滑国界下(由整图画布画的图层)按格贴着地形画(用户 2026-10-29 "平滑模式下图层要完全贴合地形"；范围见 WildernessShape)：
//     无人区的格才填色、撒纹样，包在里面的洞露地形(城市图层上当领地一起填)；外沿虚线按格描，挨着同一片(连同洞)、水面、
//     有归属的格(国界在那边，画布上的归属)不描；画布上有归属的无人区格是并进了国土，只留纹样。国、城的色块在这些格上让开(见画布)，
//     两层不叠色、不留缝。平滑国界关着、别的图层上照旧按地块画；
//   · 区域变了(WildernessSystem.Version)、换图层、换画法时整片重画(镜头里的先画)，每帧画到 BuildBudgetMs 为止；
//     另每帧核对一段地块(SweepPerFrame)：样子对不上(并进、脱离国土，挨着的地块有了城；贴地形时是这块重算过)的连同四邻重画；
//     贴地形时格子变了由 WildernessShape 通知，画布上的归属变了由画布通知(见 SmoothBorders.CanvasProcess)；
//   · 悬停(点一下也行)无人区：提示名字、类型、多少地块、归属哪国(哪个法理)与"没修路的地方军队过不去"。
// 无人区关着(WildernessSystem.Enabled 为假)或没有区域时什么都不画
public static class WildernessOverlay
{
    private const int ChunkTiles = 64;
    private const double BuildBudgetMs = 2d;
    private const int SweepPerFrame = 256;
    // 类型色铺满的不透明度占国色的几成(略淡于国土，国家仍是主角)；并进国土的地块纹样的不透明度
    private const float FillShare = 0.85f;
    private const byte MarkAlpha = 150, AbsorbedMarkAlpha = 120;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000d;
    private static readonly Color32 Clear = new(0, 0, 0, 0);

    private enum Look { None, Fill, Province }

    private sealed class Chunk
    {
        public int X0, Y0, W, H;
        public Color32[] Pixels;
        public int Painted;
        public bool Dirty;
        public GameObject Object;
        public SpriteRenderer Renderer;
        public Texture2D Texture;
    }

    private static readonly Dictionary<int, Chunk> Chunks = new();
    // 每块地画成的样子(见 KeyOf)：0(不是无人区、没有城)不记
    private static readonly Dictionary<int, int> Drawn = new();
    private static readonly HashSet<int> Dirty = new();
    private static readonly Queue<int> DirtyQueue = new();
    private static readonly HashSet<int> Marked = new();
    private static object _world;
    private static int _width, _height, _cols, _sweep, _option, _version = int.MinValue;
    private static Look _look;
    private static bool _bare, _primed, _fitted, _canvas;
    private static MetaTypeAsset _layer;

    public static void ResetWorldState()
    {
        foreach (Chunk chunk in Chunks.Values)
        {
            if (chunk.Object != null) Object.Destroy(chunk.Object);
            if (chunk.Texture != null) Object.Destroy(chunk.Texture);
        }
        Chunks.Clear();
        Drawn.Clear();
        Dirty.Clear();
        DirtyQueue.Clear();
        _world = null;
        _sweep = 0;
        _primed = false;
        _version = int.MinValue;
        HideTip();
    }

    public static void MarkZone(TileZone zone)
    {
        if (zone != null && Dirty.Add(zone.id)) DirtyQueue.Enqueue(zone.id);
    }

    // 这块地连同四邻(外沿虚线、城界随邻块变)
    internal static void MarkAround(TileZone zone)
    {
        if (zone == null) return;
        MarkZone(zone);
        MarkZone(zone.zone_up);
        MarkZone(zone.zone_down);
        MarkZone(zone.zone_left);
        MarkZone(zone.zone_right);
    }

    // 每帧(PureMapPatch.AfterZoneAnimations，纯净地图开没开都调)
    public static void Update()
    {
        MapBox world = World.world;
        if (!ReferenceEquals(_world, world) || MapBox.width != _width || MapBox.height != _height)
        {
            ResetWorldState();
            _world = world;
            _width = MapBox.width;
            _height = MapBox.height;
            _cols = (_width + ChunkTiles - 1) / ChunkTiles;
        }
        ZoneCalculator calc = world?.zone_calculator;
        SpriteRenderer source = calc?.sprRnd;
        MetaTypeAsset layer = world?.getCachedMapMetaAsset();
        Look look = source?.sprite == null || !source.enabled || _width <= 0 || _height <= 0 ? Look.None : LookOf(layer);
        _tipsOn = look != Look.None;
        // 名字标签(见 TerritoryLabelRenderer.SubmitWilderness)：没有图层提交过铭牌时也要有宿主
        if (WildernessSystem.Enabled && WildernessSystem.Regions.Count > 0) TerritoryLabelRenderer.KeepHost();
        if (look == Look.None)
        {
            _primed = false;
            Hide();
            HideTip();
            return;
        }
        int option = layer?.getZoneOptionState() ?? 0;
        // 普通地图拉近时原版国土只画边框(色块内部透明)：无人区也只画边线
        bool bare = !PureMapMode.Enabled && calc._cached_should_be_clear_color;
        // 平滑国界开着：哪个图层都按格贴地形画(先对上这一帧的格子)。以前只在整图画布画的图层(国家、帝国、城市、联盟)上贴，
        // 别的图层又退回按地块的方格(用户 2026-10-29 "而且我都开了平滑了""这怎么还是方的")；
        // 画布画这个图层时才看画布上的归属(并进国土的只留纹样)，不画时没有国土可并
        WildernessShape.Tick();
        bool fitted = WildernessShape.Active;
        bool canvas = fitted && SmoothBorders.CanvasDraws(layer);
        if (!_primed || look != _look || bare != _bare || !ReferenceEquals(layer, _layer) || option != _option ||
            WildernessSystem.Version != _version || fitted != _fitted || canvas != _canvas)
        {
            _primed = true;
            _look = look;
            _bare = bare;
            _layer = layer;
            _option = option;
            _version = WildernessSystem.Version;
            _fitted = fitted;
            _canvas = canvas;
            MarkAll(calc);
        }
        using (FrameProfiler.Measure("无人区图层·重画"))
        {
            Sweep(calc);
            Build(calc);
            Upload(source);
        }
        Show(source);
        Hover();
    }

    // 纯净地图下哪个图层都画；普通地图下国家、帝国、城市图层画
    private static Look LookOf(MetaTypeAsset layer)
    {
        if (!WildernessSystem.Enabled || WildernessSystem.Regions.Count == 0) return Look.None;
        MetaType mode = layer?.map_mode ?? MetaType.None;
        if (mode == MetaType.City) return Look.Province;
        if (PureMapMode.Enabled) return Look.Fill;
        return mode == MetaType.Kingdom || mode == MetaTypeExtension.Empire || CityViewMap.Active ? Look.Fill : Look.None;
    }

    // 区域变了、换了图层或画法：原来画过的与眼下的无人区地块(贴地形时连同伸出去、有无人区格的地块)都重画
    private static void MarkAll(ZoneCalculator calc)
    {
        Marked.Clear();
        foreach (KeyValuePair<int, int> pair in Drawn)
            if (pair.Value >= 2) Marked.Add(pair.Key);
        foreach (WildRegion region in WildernessSystem.Regions)
            if (region?.Zones != null)
                foreach (int id in region.Zones) Marked.Add(id);
        if (_fitted)
            foreach (int id in WildernessShape.TouchedZones()) Marked.Add(id);
        foreach (int id in Marked) MarkZone(calc.getZoneByID(id));
        Marked.Clear();
    }

    // 画布整张画过(换图层、进图，见 SmoothBorders.CanvasBuild)：贴地形画的块看画布上的归属，都重画
    internal static void CanvasRebuilt()
    {
        if (!_fitted) return;
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return;
        foreach (int id in WildernessShape.TouchedZones()) MarkZone(calc.getZoneByID(id));
    }

    // 一块地该画成的样子：0 不是无人区、没有城；1 有城(无人区外沿虚线不画向它)；2 以上是哪片无人区、什么类型、并进国土没有。
    // 贴地形时：0 没有无人区的格，2 以上是这块重算过几次(格子变了、周围是不是无人区变了都加一)
    private static int KeyOf(TileZone zone, out WildRegion region, out bool absorbed)
    {
        absorbed = false;
        if (_fitted)
        {
            region = null;
            return WildernessShape.ZoneTouched(zone.id) ? (WildernessShape.ZoneStamp(zone.id) & 0x3FFFFFFF) + 2 : 0;
        }
        region = zone.city == null ? WildernessSystem.RegionOf(zone) : null;
        if (region == null || region.Kind == WildKind.None) return zone.city != null ? 1 : 0;
        absorbed = Absorbed(zone);
        return ((region.Id & 0xFFFFF) << 4 | (int)region.Kind << 1 | (absorbed ? 1 : 0)) + 2;
    }

    // 并进国土：国家、帝国图层(按城池归属那一档)上有归属的无主地块——挂了法理的无人区(见 WildernessTerritory)、被一国包住的(见 EnclaveFill)
    private static bool Absorbed(TileZone zone)
    {
        if (_look != Look.Fill || _layer == null || _option != 0) return false;
        if (_layer.map_mode != MetaType.Kingdom && _layer.map_mode != MetaTypeExtension.Empire) return false;
        return !_layer.isMetaZoneOptionSelectedFluid() && _layer.tile_get_metaobject_0?.Invoke(zone) != null;
    }

    // 逐段核对：样子对不上的连同四邻重画
    private static void Sweep(ZoneCalculator calc)
    {
        List<TileZone> all = calc.zones;
        if (all == null || all.Count == 0) return;
        for (int i = 0; i < SweepPerFrame && i < all.Count; i++)
        {
            if (_sweep >= all.Count) _sweep = 0;
            TileZone zone = all[_sweep++];
            if (zone == null) continue;
            int drawn = Drawn.TryGetValue(zone.id, out int key) ? key : 0;
            if (KeyOf(zone, out _, out _) != drawn) MarkAround(zone);
        }
    }

    // 画排着的地块，画到预算为止：先过一遍只画镜头里的，还有时间再画镜头外的
    private static void Build(ZoneCalculator calc)
    {
        if (DirtyQueue.Count == 0) return;
        long deadline = Stopwatch.GetTimestamp() + (long)(BuildBudgetMs * TicksPerMs);
        for (int pass = 0; pass < 2; pass++)
        {
            int count = DirtyQueue.Count;
            for (int i = 0; i < count; i++)
            {
                if (Stopwatch.GetTimestamp() > deadline) return;
                int id = DirtyQueue.Dequeue();
                TileZone zone = calc.getZoneByID(id);
                if (zone == null)
                {
                    Dirty.Remove(id);
                    continue;
                }
                if (pass == 0 && !zone.visible)
                {
                    DirtyQueue.Enqueue(id);
                    continue;
                }
                Dirty.Remove(id);
                Draw(zone);
            }
        }
    }

    private static void Draw(TileZone zone)
    {
        if (_fitted)
        {
            DrawFitted(zone);
            return;
        }
        int key = KeyOf(zone, out WildRegion region, out bool absorbed);
        bool painted = Drawn.TryGetValue(zone.id, out int before) && before >= 2;
        if (key == 0) Drawn.Remove(zone.id);
        else Drawn[zone.id] = key;
        if (zone.tiles == null) return;
        if (key < 2)
        {
            if (painted)
                foreach (WorldTile tile in zone.tiles)
                    if (tile != null) Put(tile, Clear);
            return;
        }
        Alphas();
        bool province = _look == Look.Province;
        Color32 fill = ColorOf(region);
        fill.a = province ? _inside : (byte)(_inside * FillShare);
        // 城界画法：深一档的实线；类型色画法：浅色虚线
        Color32 edge = province ? Shade(fill, 0.62f) : Mix(fill, new Color32(255, 255, 255, 255), 0.55f);
        edge.a = province ? _edge : (byte)(_edge * 0.8f);
        Color32 mark = MarkOf(region.Kind);
        mark.a = absorbed ? AbsorbedMarkAlpha : MarkAlpha;
        bool marks = !province && !_bare;
        bool up = Line(zone.zone_up, region), down = Line(zone.zone_down, region);
        bool left = Line(zone.zone_left, region), right = Line(zone.zone_right, region);
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile == null) continue;
            // 水面不涂(同国色：领土只到岸边)
            if (Water(tile))
            {
                Put(tile, Clear);
                continue;
            }
            Color32 color = absorbed || _bare ? Clear : fill;
            if (marks && Mark(region.Kind, tile.x, tile.y)) color = mark;
            if (!absorbed && Edge(tile, up, down, left, right) && (province || ((tile.x + tile.y) >> 1 & 1) == 0)) color = edge;
            Put(tile, color);
        }
    }

    // 这一边画不画边线：城市图层下挨着别的东西都画(同城界)；类型色画法只画向无主地、别片没并进国土的无人区
    private static bool Line(TileZone other, WildRegion region)
    {
        if (other == null) return false;
        WildRegion next = other.city == null ? WildernessSystem.RegionOf(other) : null;
        if (ReferenceEquals(next, region)) return false;
        if (_look == Look.Province) return true;
        if (other.city != null) return false;
        return next == null || !Absorbed(other);
    }

    private static bool Edge(WorldTile tile, bool up, bool down, bool left, bool right)
    {
        WorldTileZoneBorder border = tile.world_tile_zone_border;
        if (border == null || !border.border) return false;
        return up && border.border_up && !Water(tile.tile_up) || down && border.border_down && !Water(tile.tile_down) ||
               left && border.border_left && !Water(tile.tile_left) || right && border.border_right && !Water(tile.tile_right);
    }

    private static bool Water(WorldTile tile)
    {
        TileTypeBase ground = tile?.Type;
        return ground != null && (ground.ocean || ground.liquid && !ground.lava);
    }

    // ───────── 平滑国界下按格贴地形(见 WildernessShape) ─────────

    // 一块地按格画：无人区的格填色、撒纹样；包在里面的洞露地形(城市图层上当领地一起填，不留窟窿)；
    // 画布上有归属的格(并进国土)只留纹样、不描边；外沿虚线(城市图层是实线)按格描(见 FittedSide)
    private static void DrawFitted(TileZone zone)
    {
        int key = KeyOf(zone, out _, out _);
        bool painted = Drawn.TryGetValue(zone.id, out int before) && before >= 2;
        if (key == 0) Drawn.Remove(zone.id);
        else Drawn[zone.id] = key;
        if (zone.tiles == null) return;
        if (key < 2)
        {
            if (painted)
                foreach (WorldTile tile in zone.tiles)
                    if (tile != null) Put(tile, Clear);
            return;
        }
        Alphas();
        bool province = _look == Look.Province, marks = !province && !_bare;
        WildRegion region = null;
        int shown = 0;
        Color32 fill = Clear, edge = Clear, mark = Clear, faint = Clear;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.data == null) continue;
            int id = tile.data.tile_id;
            int slot = WildernessShape.AreaAt(id);
            bool hole = slot != 0 && WildernessShape.HoleAt(id);
            if (slot == 0 || Water(tile) || hole && !province)
            {
                Put(tile, Clear);
                continue;
            }
            if (slot != shown)
            {
                // 一块地里可能有两片的格(伸出去的、B 样条圆角)：换片时换颜色
                shown = slot;
                region = WildernessShape.RegionOfSlot(slot);
                if (region != null)
                {
                    fill = ColorOf(region);
                    fill.a = province ? _inside : (byte)(_inside * FillShare);
                    edge = province ? Shade(fill, 0.62f) : Mix(fill, new Color32(255, 255, 255, 255), 0.55f);
                    edge.a = province ? _edge : (byte)(_edge * 0.8f);
                    mark = MarkOf(region.Kind);
                    faint = mark;
                    mark.a = MarkAlpha;
                    faint.a = AbsorbedMarkAlpha;
                }
            }
            if (region == null)
            {
                Put(tile, Clear);
                continue;
            }
            bool absorbed = Owned(id);
            Color32 color = absorbed || _bare ? Clear : fill;
            if (marks && !hole && Mark(region.Kind, tile.x, tile.y)) color = absorbed ? faint : mark;
            if (!absorbed && (province || ((tile.x + tile.y) >> 1 & 1) == 0) &&
                (FittedSide(tile.tile_up, slot, province) || FittedSide(tile.tile_down, slot, province) ||
                 FittedSide(tile.tile_left, slot, province) || FittedSide(tile.tile_right, slot, province)))
                color = edge;
            Put(tile, color);
        }
    }

    // 这一边描不描：挨着同一片的格(连同洞)、水面、地图外不描；城市图层上挨着别的都描(同城界)；
    // 别的图层只描向无主的地、别片没并进国土的无人区——挨着有归属的格(国土、别片并进国土的)那边有国界，同按地块画时
    private static bool FittedSide(WorldTile next, int slot, bool province)
    {
        if (next?.data == null || Water(next)) return false;
        int id = next.data.tile_id;
        if (WildernessShape.AreaAt(id) == slot) return false;
        return province || !Owned(id);
    }

    // 画布上这一格归了谁(只在画布画眼下这个图层时算；别的图层画布上是上一个图层的归属，不算)
    private static bool Owned(int tileId) => _canvas && SmoothBorders.CanvasOwns(tileId);

    // ───────── 颜色与纹样 ─────────

    // 这一片的底色：划区时按类型定色系、按编号微调(WildRegion.Color，挨着的两片不至于一个颜色)；还没定的按类型
    internal static Color32 ColorOf(WildRegion region)
    {
        Color32 color = region.Color;
        if (color.a == 0) color = BaseOf(region.Kind);
        color.a = 255;
        return color;
    }

    // 各类型的底色(山脉灰褐、沙漠沙黄、冻原冰白偏蓝、雨林深绿、大泽青绿)
    private static Color32 BaseOf(WildKind kind) => kind switch
    {
        WildKind.Mountain => new Color32(140, 122, 102, 255),
        WildKind.Desert => new Color32(232, 198, 114, 255),
        WildKind.Tundra => new Color32(214, 232, 248, 255),
        WildKind.Jungle => new Color32(42, 108, 58, 255),
        WildKind.Swamp => new Color32(64, 136, 120, 255),
        _ => new Color32(160, 160, 150, 255)
    };

    // 纹样色：山、沙、林、泽深一档，雪原的雪花是白的
    private static Color32 MarkOf(WildKind kind) => kind switch
    {
        WildKind.Mountain => new Color32(88, 72, 56, 255),
        WildKind.Desert => new Color32(178, 138, 64, 255),
        WildKind.Tundra => new Color32(255, 255, 255, 255),
        WildKind.Jungle => new Color32(20, 66, 32, 255),
        WildKind.Swamp => new Color32(30, 92, 108, 255),
        _ => new Color32(100, 100, 96, 255)
    };

    // 纹样：按格坐标错行铺开，同一处每次一样(一格一像素，拉近了才看得清)
    private static bool Mark(WildKind kind, int x, int y)
    {
        switch (kind)
        {
            case WildKind.Mountain:
            {
                // 人字(山峰)：7×6 一组，错行半组
                int lx = (x + y / 6 % 2 * 3) % 7, ly = y % 6;
                return ly == 1 && (lx == 1 || lx == 3) || ly == 2 && lx == 2;
            }
            case WildKind.Desert:
            {
                // 沙丘短纹
                int lx = (x + y / 4 % 2 * 3) % 6, ly = y % 4;
                return ly == 1 && lx <= 1;
            }
            case WildKind.Tundra:
            {
                // 雪花(小十字)
                int lx = (x + y / 7 % 2 * 3) % 7, ly = y % 7;
                return lx == 3 && ly >= 2 && ly <= 4 || ly == 3 && (lx == 2 || lx == 4);
            }
            case WildKind.Jungle:
            {
                // 林团
                int lx = (x + y / 5 % 2 * 2) % 5, ly = y % 5;
                return lx >= 1 && lx <= 2 && ly >= 1 && ly <= 2;
            }
            case WildKind.Swamp:
            {
                // 沼泽横纹
                int lx = (x + y / 4 % 2 * 3) % 6, ly = y % 4;
                return ly == 1 && lx <= 2;
            }
        }
        return false;
    }

    private static Color32 Shade(Color32 color, float k) => new(Byte(color.r * k), Byte(color.g * k), Byte(color.b * k), color.a);

    private static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

    private static byte Byte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);

    // 国色色块的不透明度(原版国色的内部色、边框色，见 ColorAsset)：量一份原版颜色，量不到用默认
    private static byte _inside, _edge;

    private static void Alphas()
    {
        if (_inside > 0) return;
        _inside = 150;
        _edge = 255;
        try
        {
            List<ColorAsset> list = AssetManager.culture_colors_library?.list;
            if (list == null || list.Count == 0 || list[0] == null) return;
            byte inside = list[0].getColorBorderInsideAlpha32().a, edge = list[0].getColorMainSecond32().a;
            if (inside > 0) _inside = inside;
            if (edge > 0) _edge = edge;
        }
        catch
        {
            // 用默认
        }
    }

    // ───────── 分块贴图(同 CityPlanOverlay) ─────────

    private static void Put(WorldTile tile, Color32 color)
    {
        int x = tile.x, y = tile.y;
        if (x < 0 || y < 0 || x >= _width || y >= _height) return;
        int key = y / ChunkTiles * _cols + x / ChunkTiles;
        if (!Chunks.TryGetValue(key, out Chunk chunk))
        {
            if (color.a == 0) return;
            int x0 = x / ChunkTiles * ChunkTiles, y0 = y / ChunkTiles * ChunkTiles;
            chunk = new Chunk
            {
                X0 = x0, Y0 = y0, W = Mathf.Min(ChunkTiles, _width - x0), H = Mathf.Min(ChunkTiles, _height - y0)
            };
            chunk.Pixels = new Color32[chunk.W * chunk.H];
            Chunks[key] = chunk;
        }
        int i = (y - chunk.Y0) * chunk.W + (x - chunk.X0);
        Color32 old = chunk.Pixels[i];
        if (old.r == color.r && old.g == color.g && old.b == color.b && old.a == color.a) return;
        if (old.a == 0 && color.a > 0) chunk.Painted++;
        else if (old.a > 0 && color.a == 0) chunk.Painted--;
        chunk.Pixels[i] = color;
        chunk.Dirty = true;
    }

    private static void Upload(SpriteRenderer source)
    {
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit;
        foreach (Chunk chunk in Chunks.Values)
        {
            if (!chunk.Dirty) continue;
            chunk.Dirty = false;
            if (chunk.Texture == null)
                chunk.Texture = new Texture2D(chunk.W, chunk.H, TextureFormat.RGBA32, false)
                {
                    name = "EmpireCraftWilderness", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
            chunk.Texture.SetPixels32(chunk.Pixels);
            chunk.Texture.Apply(false);
            if (chunk.Object == null)
            {
                chunk.Object = new GameObject("EmpireCraftWilderness");
                chunk.Renderer = chunk.Object.AddComponent<SpriteRenderer>();
                chunk.Renderer.sprite = Sprite.Create(chunk.Texture, new Rect(0f, 0f, chunk.W, chunk.H), Vector2.zero, ppu);
            }
            if (chunk.Object.transform.parent != source.transform) chunk.Object.transform.SetParent(source.transform, false);
            chunk.Object.transform.localPosition =
                new Vector3((chunk.X0 - reference.pivot.x) / ppu, (chunk.Y0 - reference.pivot.y) / ppu, 0f);
        }
    }

    private static void Show(SpriteRenderer source)
    {
        foreach (Chunk chunk in Chunks.Values)
        {
            if (chunk.Renderer == null) continue;
            bool on = chunk.Painted > 0;
            if (chunk.Renderer.enabled != on) chunk.Renderer.enabled = on;
            if (!on) continue;
            chunk.Renderer.sortingLayerID = source.sortingLayerID;
            // 色块之下、路网之上(城市平面图层也是这一档，两层不重叠)；着色同色块：随地图不透明度设置
            chunk.Renderer.sortingOrder = source.sortingOrder - 1;
            if (chunk.Renderer.color != source.color) chunk.Renderer.color = source.color;
        }
    }

    private static void Hide()
    {
        foreach (Chunk chunk in Chunks.Values)
            if (chunk.Renderer != null && chunk.Renderer.enabled) chunk.Renderer.enabled = false;
    }

    // ───────── 悬停、点选提示 ─────────
    // 同文化图层的悬停提示(见 EmpireCraftNamePlateLibrary.UpdateCultureShareTooltip)：换了区域(或归属)才重显，移开时显式收起；
    // 点一下(没拖动)也重显一遍

    public const string TooltipType = "empirecraft_wilderness";
    private static WildRegion _tip;
    private static TileZone _tipZone;
    private static Kingdom _tipOwner;
    private static Vector3 _pressedAt;
    private static bool _pressed, _tipsOn;

    // 国家、帝国图层上并进国土的无人区：原版的国家提示让给无人区提示(见 WildernessTerritory)
    internal static bool TipShown(TileZone zone) =>
        _tipsOn && zone != null && zone.city == null && WildernessSystem.RegionOf(zone) != null;

    private static void Hover()
    {
        bool clicked = false;
        if (Input.GetMouseButtonDown(0))
        {
            _pressedAt = Input.mousePosition;
            _pressed = !World.world.isOverUI();
        }
        else if (Input.GetMouseButtonUp(0) && _pressed)
        {
            _pressed = false;
            clicked = (Input.mousePosition - _pressedAt).sqrMagnitude <= 64f;
        }
        WorldTile tile = OverPanels() ? null : World.world.getMouseTilePosCachedFrame();
        TileZone zone = tile?.zone;
        WildRegion region = zone != null && zone.city == null ? WildernessSystem.RegionOf(zone) : null;
        if (region != null && region.Kind == WildKind.None) region = null;
        Kingdom owner = region != null ? WildernessSystem.OwnerOf(zone) : null;
        if (ReferenceEquals(region, _tip) && owner == _tipOwner && !clicked) return;
        HideTip();
        if (region == null) return;
        EnsureTooltip();
        _tip = region;
        _tipZone = zone;
        _tipOwner = owner;
        Tooltip.show(region, TooltipType, new TooltipData { tooltip_scale = 0.7f, is_sim_tooltip = true });
    }

    private static PointerEventData _pointer;
    private static readonly List<RaycastResult> Hits = new();

    // 鼠标压在界面面板上(不算提示框自己、不算地图浮层)。提示框一弹出来就在鼠标底下，以前按 isOverUI 一律算压在界面上，
    // 提示随即收起、下一帧又弹，闪个不停(用户 2026-10-29 "我鼠标放上去都没有tooltips 刚显示出来就闪没了")
    private static bool OverPanels()
    {
        if (!World.world.isOverUI()) return false;
        if (EventSystem.current == null) return true;
        _pointer ??= new PointerEventData(EventSystem.current);
        _pointer.position = Input.mousePosition;
        Hits.Clear();
        EventSystem.current.RaycastAll(_pointer, Hits);
        foreach (RaycastResult hit in Hits)
            if (hit.gameObject != null && !InTooltip(hit.gameObject.transform) && !MapOverlayCanvas.IsMap(hit.gameObject.transform))
                return true;
        return false;
    }

    // 是提示框(原版提示框挂着 Tooltip，预制体名也带 tooltip)
    private static bool InTooltip(Transform at)
    {
        if (at.GetComponentInParent<Tooltip>() != null) return true;
        for (Transform t = at; t != null; t = t.parent)
            if (t.name.IndexOf("tooltip", global::System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static void HideTip()
    {
        if (_tip != null) Tooltip.hideTooltip(_tip, true, TooltipType);
        _tip = null;
        _tipZone = null;
        _tipOwner = null;
    }

    private static void EnsureTooltip()
    {
        if (AssetManager.tooltips != null && !AssetManager.tooltips.dict.ContainsKey(TooltipType))
            AssetManager.tooltips.add(new TooltipAsset
            {
                id = TooltipType,
                prefab_id = "tooltips/tooltip_normal",
                callback = ShowTooltip
            });
    }

    // 名字 · 类型 · 多少地块；有归属的加一行"属某国(某法理)"；末尾一句说明
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        WildRegion region = _tip;
        if (region == null) return;
        tooltip.clear();
        string kind = WildernessNaming.KindName(region.Kind) ?? "";
        Color32 color = ColorOf(region);
        string hex = $"#{color.r:X2}{color.g:X2}{color.b:X2}";
        tooltip.setTitle(string.IsNullOrEmpty(region.Name) ? kind : region.Name, "wild_tooltip_title", hex);
        tooltip.addLineText(NeoModLoader.General.LM.Get("wild_tooltip_kind"), kind, hex, pPercent: false, pLocalize: false);
        tooltip.addLineText(NeoModLoader.General.LM.Get("wild_tooltip_zones"),
            string.Format(NeoModLoader.General.LM.Get("wild_tooltip_zones_value"), region.Zones?.Count ?? 0), "#FFFFFF",
            pPercent: false, pLocalize: false);
        Kingdom owner = _tipOwner;
        // 归属：永久中立区；取得挂接法理的国家写明法理；周边的城同属一国的写明(见 WildernessSystem.OwnerOf)
        if (region.Neutral)
            tooltip.addLineText(NeoModLoader.General.LM.Get("wild_tooltip_owner"), NeoModLoader.General.LM.Get("wild_tooltip_neutral"),
                "#BFD8E6", pPercent: false, pLocalize: false);
        else if (owner?.data != null && !owner.isRekt())
        {
            string title = _tipZone != null && WildernessSystem.OwnedByTitle(_tipZone)
                ? WildernessSystem.TitleOf(_tipZone)?.data?.name : null;
            string value = !string.IsNullOrEmpty(title)
                ? string.Format(NeoModLoader.General.LM.Get("wild_tooltip_owner_title"), owner.name, title)
                : string.Format(NeoModLoader.General.LM.Get("wild_tooltip_owner_surround"), owner.name);
            tooltip.addLineText(NeoModLoader.General.LM.Get("wild_tooltip_owner"), value, "#E6C27A", pPercent: false,
                pLocalize: false);
        }
        tooltip.addBottomDescription(NeoModLoader.General.LM.Get("wild_tooltip_hint"));
    }
}
