using System;
using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 整图画布(用户 2026-10-27 "图层能够瞬间切换且没有残留，而不是一个国家一个国家的慢慢变"，选"归属与颜色分开、只有版图变了才重算形状"、
// 城换主人时保留翻牌)：以前平滑国界跟着原版巡检一块块画——每块地逐格算平滑归属、查色、存缓存，一块约 0.1 毫秒，全图两万多块要画好几秒，
// 只能分帧排队；换颜色、换帝国、宣战议和、换图层都要整片重排，排不完的就是残留、一国一国地变。
// 现在平滑国界接手的图层(国家、帝国、城市、文化、宗教、联盟、头衔……)都不走原版巡检，由这里直接画整张图：
//   · 形状：每格先按周围 3×3 块的城做 B 样条加噪声平滑出属于哪座城(城的轮廓，同 SmoothedOwnerAt)，三个图层共用、存着，
//     只有地块归属的城变了、水陆变了才重算那几块；被占、逐格推进的城在城的轮廓里另按各块的实际控制方平滑(同 OwnerAt)；
//   · 归属：城 → 图层上的一方(国家、帝国、悬停的省)，几帧对一遍各城，变了的城只重涂它的格；
//     不按城池归属的图层(文化、宗教、联盟……)各块的归属按块取，每格按周围 3×3 块的归属平滑(同 SmoothedOwnerAt)，
//     分几帧对一遍各块，变了的块连同四周重算；
//   · 颜色：边框、战线朱红、帝国金边、兼属斜纹、提亮都按格现查，换图层整张一遍、当帧完成，不翻牌；
//   · 城换了主(不算被占、收复)：镜头里的地块照旧翻牌，从新主人那边一圈圈翻过来(同 PlanSweeps)，挨着的邻块翻到一半时跟着换；
//   · 原版各处让地块重画的信号(战线、兼属斜纹、水陆、占领、推进……，见 Stamp、Repaint)都记成要重画的块，下一帧画好。
// 原版画法的图层(教化、政令、财富这类热力图)照旧走原版巡检(ZonePaintBudget 分帧)。
// 画布出错时本局退回原版巡检(见 CanvasDraw)。
public static partial class SmoothBorders
{
    // 画布正画着的图层；画布建好了(整张画过)
    private static MetaTypeAsset _canvasLayer;
    private static int _canvasOption = int.MinValue;
    private static object _canvasWorld;
    private static bool _canvasReady;
    private static int _canvasW, _canvasH, _zonesX, _zonesY;

    // 各格：属于哪座城(CityTable 编号)、是不是水；城的轮廓建好了(三个图层共用，换图层不重算)
    private static ushort[] _tileCity = Array.Empty<ushort>();
    private static bool[] _water = Array.Empty<bool>();
    private static bool _geometryReady;

    // 各块：属于哪座城、实际控制方(含被占、悬停的省)、逐格推进方、图层本来的归属(不算被占、悬停)与它的样子；这一层算过没有
    private static int[] _zoneCity = Array.Empty<int>(), _zoneShade = Array.Empty<int>(), _zonePush = Array.Empty<int>(),
        _zoneOwner = Array.Empty<int>(), _zoneOwnerKey = Array.Empty<int>();
    private static bool[] _zoneSeen = Array.Empty<bool>();

    // 城的编号表(与归属编号表分开：城的轮廓不随图层、不随归属编号表重排而变)
    private static readonly List<City> CityTable = new() { null };
    private static readonly Dictionary<City, int> CityIndex = new();
    private static readonly List<int> CityHash = new() { 0 };
    // 各城(按 CityTable 编号)：显示成谁(OwnerTable 编号，悬停的省)、本来的归属与它的样子、有没有被占或逐格推进的块
    private static int[] _cityShade = Array.Empty<int>(), _cityOwner = Array.Empty<int>(), _cityOwnerKey = Array.Empty<int>();
    private static bool[] _cityContested = Array.Empty<bool>(), _citySeen = Array.Empty<bool>();
    // 各城本轮算过归属没有(一轮一城只取一次)；各块的轮廓按周围 3×3 块的城算的(城变了重算)
    private static int[] _cityPass = Array.Empty<int>(), _shapeKey = Array.Empty<int>();
    private static int _canvasPass;

    // 要重画的块(原版各处的重画信号、扫出来的变动)；这一轮换主不翻的块(逐格推进吃下的，见 Settle)
    private static readonly HashSet<int> CanvasDirty = new();
    private static readonly HashSet<int> CanvasNoFlip = new();
    private static int _cityCursor, _zoneCursor;
    // 这一层按城池归属(城的轮廓)；不按城的图层分几帧对一遍各块
    private static bool _canvasCityKeyed;
    private const int ZoneScanParts = 10;
    private static Kingdom _canvasHover;
    private static int _paletteStamp;
    private const int CityScanParts = 3;
    // 贴图上传：变了的那一帧传，至多每 UploadInterval 秒一次(整张画、换图层当帧传)
    private const float UploadInterval = 0.05f;
    private static float _uploadAt = -10f;
    private static bool _uploadNow;
    // 画布出过错：本局退回原版巡检(只记一次错)
    private static bool _canvasBroken;

    // 画布接手的图层：平滑国界接手的(原版按地块取归属的、城市、联盟)
    private static bool CanvasHandles(MetaTypeAsset layer, int option) =>
        Enabled && !_canvasBroken && layer != null && Smoothable(layer);

    internal static bool CanvasActive => _canvasLayer != null && _canvasReady;

    private static void CanvasReset()
    {
        _canvasLayer = null;
        _canvasOption = int.MinValue;
        _canvasReady = false;
        _geometryReady = false;
        _canvasWorld = null;
        CanvasDirty.Clear();
        CanvasNoFlip.Clear();
        CityTable.Clear();
        CityTable.Add(null);
        CityIndex.Clear();
        CityHash.Clear();
        CityHash.Add(0);
        _canvasHover = null;
    }

    // 不再由画布画(换到别的图层、平滑国界关了)：下次回来整张重画
    private static void CanvasRelease()
    {
        _canvasLayer = null;
        _canvasOption = int.MinValue;
        _canvasReady = false;
    }

    // 一块地要重画(原版各处让它重画的信号，见 Stamp)：画布建好了才记(没建好的整张画时一起画)
    private static void CanvasMark(TileZone zone)
    {
        if (zone == null || !_geometryReady || zone.id < 0 || zone.id >= _zoneSeen.Length) return;
        CanvasDirty.Add(zone.id);
    }

    private static int CityOf(City city)
    {
        if (city == null || !city.isAlive()) return 0;
        if (CityIndex.TryGetValue(city, out int index)) return index;
        index = CityTable.Count;
        // 编号满了(亡城一直占着)：下一帧整张重建
        if (index >= ushort.MaxValue)
        {
            _geometryReady = false;
            _canvasReady = false;
            return 0;
        }
        CityTable.Add(city);
        CityIndex[city] = index;
        CityHash.Add(city.GetHashCode());
        EnsureCitySlots(index + 1);
        return index;
    }

    private static void EnsureCitySlots(int count)
    {
        if (_cityShade.Length >= count) return;
        int size = Mathf.Max(count, _cityShade.Length * 2, 256);
        Array.Resize(ref _cityShade, size);
        Array.Resize(ref _cityOwner, size);
        Array.Resize(ref _cityOwnerKey, size);
        Array.Resize(ref _cityContested, size);
        Array.Resize(ref _citySeen, size);
        Array.Resize(ref _cityPass, size);
    }

    #region 每帧

    // 原版巡检之前(ZonePaintBudget.BeforeRedraw)：画布接手的图层在这里画，返回 true 时跳过原版巡检
    internal static bool CanvasDraw(ZoneCalculator calc)
    {
        if (_canvasBroken) return false;
        try
        {
            return DrawCanvas(calc);
        }
        catch (Exception exception)
        {
            _canvasBroken = true;
            CanvasRelease();
            LogService.LogError($"[EmpireCraft][整图画布] 出错，本局改回原版巡检上色：{exception}");
            ZonePaintBudget.RepaintEverything();
            return false;
        }
    }

    private static bool DrawCanvas(ZoneCalculator calc)
    {
        MetaTypeAsset layer = calc?._mode_asset;
        if (!Enabled || calc?.pixels == null || layer == null || World.world == null || !Config.game_loaded || SmoothLoader.isLoading())
        {
            if (_canvasLayer != null) CanvasRelease();
            return false;
        }
        int option = layer.getZoneOptionState();
        if (!CanvasHandles(layer, option))
        {
            if (_canvasLayer != null) CanvasRelease();
            return false;
        }
        using var timing = FrameProfiler.Measure("平滑国界·整图画布");
        EnsurePixelState(calc.pixels);
        if (!ReferenceEquals(_canvasWorld, World.world) || _canvasW != MapBox.width || _canvasH != MapBox.height ||
            _zonesX != calc.zones_total_x || _zonesY != calc.zones_total_y)
        {
            CanvasReset();
            _canvasWorld = World.world;
        }
        if (!_canvasReady || !ReferenceEquals(layer, _canvasLayer) || option != _canvasOption)
        {
            CanvasBuild(calc, layer, option);
            _uploadNow = true;
        }
        else
        {
            CanvasPalette(calc);
            CanvasScan(calc, layer, option);
            CanvasProcess(calc, layer, option);
        }
        float now = Time.unscaledTime;
        if (calc._dirty && (_uploadNow || now < _uploadAt || now - _uploadAt >= UploadInterval))
        {
            calc._dirty = false;
            _uploadNow = false;
            _uploadAt = now;
            calc.updatePixels();
        }
        return true;
    }

    // 整张画：换到画布接手的图层、进图、画布作废后。城的轮廓没建过才建(换图层不重算)
    private static void CanvasBuild(ZoneCalculator calc, MetaTypeAsset layer, int option)
    {
        long started = Stopwatch.GetTimestamp();
        _canvasW = MapBox.width;
        _canvasH = MapBox.height;
        _zonesX = calc.zones_total_x;
        _zonesY = calc.zones_total_y;
        int tiles = _canvasW * _canvasH, zones = _zonesX * _zonesY;
        if (_tileCity.Length != tiles)
        {
            _tileCity = new ushort[tiles];
            _water = new bool[tiles];
            _geometryReady = false;
        }
        if (_zoneSeen.Length != zones)
        {
            _zoneCity = new int[zones];
            _zoneShade = new int[zones];
            _zonePush = new int[zones];
            _zoneOwner = new int[zones];
            _zoneOwnerKey = new int[zones];
            _zoneSeen = new bool[zones];
            _shapeKey = new int[zones];
            _geometryReady = false;
        }
        // 归属编号表攒多了：整张重画时清空重来(建的途中重排会打乱各块记下的编号)
        if (OwnerTable.Count > MaxOwners / 2)
        {
            Snapshots.Clear();
            ResetOwners();
        }
        ResetTransitions();
        ZoneFlipOverlay.Hide();
        ZonePaintBudget.ResetLayerPending();
        LastMeta.Clear();
        bool geometry = !_geometryReady;
        _canvasCityKeyed = CityKeyed(layer, option);
        _canvasLayer = layer;
        _canvasOption = option;
        _sweepLayer = layer;
        _sweepOption = option;
        _canvasHover = HoveredProvince(layer, option);
        _hover = _canvasHover;
        Array.Clear(_zoneSeen, 0, _zoneSeen.Length);
        Array.Clear(_citySeen, 0, _citySeen.Length);
        _canvasPass++;
        CanvasNoFlip.Clear();
        if (geometry)
        {
            // 水陆、各块的城：整张重算
            for (int y = 0; y < _canvasH; y++)
            for (int x = 0; x < _canvasW; x++)
                _water[y * _canvasW + x] = IsWater(x, y);
            CanvasDirty.Clear();
        }
        // 各块的城、归属
        for (int id = 0; id < zones; id++) RefreshZone(calc, layer, option, id, out _, out _, out _);
        if (geometry)
        {
            for (int zy = 0; zy < _zonesY; zy++)
            for (int zx = 0; zx < _zonesX; zx++)
                BuildCityShape(zx, zy);
            _geometryReady = true;
        }
        else
        {
            // 画布没开着时水陆变了的块(有重画信号)、周围的城变了的块(比轮廓记下的 3×3 块的城)：只重算这几块的轮廓
            foreach (int id in CanvasDirty)
                if (RefreshWater(id)) MarkShapeAround(id);
            RepairCityShapes();
        }
        CanvasDirty.Clear();
        _paletteStamp = PaletteStamp(calc);
        BeginCanvasColors();
        for (int zy = 0; zy < _zonesY; zy++)
        for (int zx = 0; zx < _zonesX; zx++)
            OwnZone(zx, zy);
        for (int zy = 0; zy < _zonesY; zy++)
        for (int zx = 0; zx < _zonesX; zx++)
            ColorZone(calc.pixels, zx, zy);
        _canvasReady = true;
        calc.setDirty();
        double ms = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        LogService.LogInfo($"[EmpireCraft][全图换色] {layer.id}：整图画布{(geometry ? "连同城的轮廓" : "")}整张画好，用了 {ms:0} 毫秒");
    }

    // 轮廓按的周围 3×3 块的城与眼下对不上(画布没开着时城的地块变了)、水陆变了的块：重算轮廓
    private static void RepairCityShapes()
    {
        for (int zy = 0; zy < _zonesY; zy++)
        for (int zx = 0; zx < _zonesX; zx++)
            if (_shapeKey[zy * _zonesX + zx] != ShapeKey(zx, zy)) BuildCityShape(zx, zy);
    }

    // 水陆变了：这块连同周围八块的轮廓作废(格的归属看周围 3×3 块)
    private static void MarkShapeAround(int id)
    {
        int zx = id % _zonesX, zy = id / _zonesX;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (zx + dx >= 0 && zx + dx < _zonesX && zy + dy >= 0 && zy + dy < _zonesY) _shapeKey[(zy + dy) * _zonesX + zx + dx] = 0;
    }

    private static int ShapeKey(int zx, int zy)
    {
        unchecked
        {
            int key = 17;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                key = key * 31 + ZoneCityAt(zx + dx, zy + dy);
            return key == 0 ? 1 : key;
        }
    }

    // 选中、只看收藏、近看清空内部这些原版的整体显示设置：变了就整张重涂(只查色，不重算形状)
    private static void CanvasPalette(ZoneCalculator calc)
    {
        int stamp = PaletteStamp(calc);
        if (stamp == _paletteStamp) return;
        _paletteStamp = stamp;
        BeginCanvasColors();
        for (int zy = 0; zy < _zonesY; zy++)
        for (int zx = 0; zx < _zonesX; zx++)
            ColorZone(calc.pixels, zx, zy);
        // 正在翻的块：新样子换成眼下的，看到的那一面照旧
        if (Transitions.Count > 0)
            foreach (KeyValuePair<int, Transition> pair in Transitions)
            {
                TileZone zone = calc.getZone(pair.Key % _zonesX, pair.Key / _zonesX);
                if (zone == null) continue;
                pair.Value.newPixels = BaseFace(zone);
                pair.Value.phase = -1;
                WriteFlip(calc.pixels, zone, pair.Value, Time.unscaledTime);
            }
        calc.setDirty();
    }

    private static int PaletteStamp(ZoneCalculator calc)
    {
        unchecked
        {
            int stamp = calc._cached_should_be_clear_color ? 1 : 0;
            stamp = stamp * 31 + (calc._cached_ony_favorited_meta ? 1 : 0);
            stamp = stamp * 31 + (calc._cached_check_animation && !calc._selected_nano_object.isRekt()
                ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(calc._selected_nano_object) : 0);
            stamp = stamp * 31 + (SelectedUnit.isSet() ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(SelectedUnit.unit) : 0);
            stamp = stamp * 31 + (PureMapMode.Enabled ? 1 : 0) + (CorpsSystem.Enabled ? 2 : 0);
            return stamp;
        }
    }

    // 找变动：各块的城(每帧全图比一遍，只比引用)、各城的归属与样子(几帧轮一遍)、悬停的省、逐格推进的块
    private static void CanvasScan(ZoneCalculator calc, MetaTypeAsset layer, int option)
    {
        List<TileZone> zones = calc.zones;
        foreach (TileZone zone in zones)
        {
            int id = zone.id;
            City city = zone.city;
            int known = _zoneCity[id];
            if (known == 0 ? city == null || !city.isAlive() : ReferenceEquals(CityTable[known], city) && city.isAlive()) continue;
            CanvasDirty.Add(id);
        }
        // 悬停的省(帝国图层)：新旧两省的城当场重画，跟手
        Kingdom hover = HoveredProvince(layer, option);
        if (hover != _canvasHover)
        {
            MarkKingdom(_canvasHover);
            MarkKingdom(hover);
            _canvasHover = hover;
        }
        _hover = _canvasHover;
        // 不按城的图层：各块分几帧轮一遍
        if (!_canvasCityKeyed)
        {
            int total = _zoneSeen.Length, count = Mathf.CeilToInt(total / (float)ZoneScanParts);
            for (int i = 0; i < count; i++)
            {
                if (_zoneCursor >= total) _zoneCursor = 0;
                int id = _zoneCursor++;
                TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
                ZoneMetas(layer, option, zone, out IMetaObject owner, out IMetaObject shade);
                if (IndexOf(shade) != _zoneShade[id] || IndexOf(owner) != _zoneOwner[id] ||
                    CanvasOwnerKey(layer, owner) != _zoneOwnerKey[id]) CanvasDirty.Add(id);
            }
        }
        // 各城：分几帧轮一遍
        List<City> cities = _canvasCityKeyed ? World.world.cities?.list : null;
        if (cities != null && cities.Count > 0)
        {
            int count = Mathf.CeilToInt(cities.Count / (float)CityScanParts);
            for (int i = 0; i < count; i++)
            {
                if (_cityCursor >= cities.Count) _cityCursor = 0;
                City city = cities[_cityCursor++];
                if (city?.zones == null || city.zones.Count == 0 || !city.isAlive()) continue;
                ScanCity(layer, option, city);
            }
        }
        // 逐格推进的块：推进方变了的
        PushIds.Clear();
        FrontSystem.CopyPushZones(PushIds);
        foreach (int id in PushIds)
        {
            if (id < 0 || id >= _zonePush.Length) continue;
            TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
            if (IndexOf(PushMetaOf(layer, zone)) != _zonePush[id]) CanvasDirty.Add(id);
        }
    }

    private static readonly List<int> PushIds = new();

    private static void MarkKingdom(Kingdom kingdom)
    {
        if (kingdom?.cities == null) return;
        foreach (City city in kingdom.cities)
            if (city?.zones != null)
                foreach (TileZone zone in city.zones)
                    if (zone != null) CanvasDirty.Add(zone.id);
    }

    // 一座城的归属、样子、被占的块：变了就记下它的块
    private static void ScanCity(MetaTypeAsset layer, int option, City city)
    {
        int index = CityOf(city);
        if (index == 0) return;
        TileZone first = city.zones[0];
        IMetaObject owner = LayerMeta(layer, option, first);
        int ownerIndex = IndexOf(owner), ownerKey = CanvasOwnerKey(layer, owner);
        bool contested = ControlLayer(layer) && city.kingdom != null && city.HasOccupiedZones();
        bool changed = !_citySeen[index] || _cityOwner[index] != ownerIndex || _cityOwnerKey[index] != ownerKey ||
                       _cityShade[index] != IndexOf(ShadeOf(first, owner)) || contested != _cityContested[index];
        if (!changed && contested)
        {
            // 被占的城：各块的实际控制方逐块比
            foreach (TileZone zone in city.zones)
            {
                if (zone == null) continue;
                if (IndexOf(ShadeOf(zone, RealMetaOf(layer, option, zone))) == _zoneShade[zone.id]) continue;
                changed = true;
                break;
            }
        }
        if (!changed) return;
        foreach (TileZone zone in city.zones)
            if (zone != null) CanvasDirty.Add(zone.id);
    }

    // 图层上一方的样子：颜色、所属帝国(金边)；国家图层再算战国的周室势力圈(加粗描金)
    private static int CanvasOwnerKey(MetaTypeAsset layer, IMetaObject owner)
    {
        if (owner == null) return 0;
        int key = AppearanceKey(owner);
        if (layer.map_mode == MetaType.Kingdom && owner is Kingdom kingdom)
            unchecked
            {
                key = key * 31 + (int)(ZhouEraSystem.SphereOf(kingdom)?.id ?? -1L);
            }
        return key;
    }

    #endregion

    #region 重画变了的块

    private static readonly List<int> RefreshList = new();
    private static readonly HashSet<int> ShapeZones = new(), OwnerZones = new(), PaintZones = new(), LeadZones = new();
    private static readonly Dictionary<int, Color32[]> OldFaces = new();

    private static void CanvasProcess(ZoneCalculator calc, MetaTypeAsset layer, int option)
    {
        if (CanvasDirty.Count == 0) return;
        _canvasPass++;
        RefreshList.Clear();
        RefreshList.AddRange(CanvasDirty);
        CanvasDirty.Clear();
        ShapeZones.Clear();
        OwnerZones.Clear();
        PaintZones.Clear();
        LeadZones.Clear();
        foreach (int id in RefreshList)
        {
            if (id < 0 || id >= _zoneSeen.Length) continue;
            bool waterChanged = RefreshWater(id);
            RefreshZone(calc, layer, option, id, out bool cityChanged, out _, out bool political);
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = zx + dx, ny = zy + dy;
                if (nx < 0 || nx >= _zonesX || ny < 0 || ny >= _zonesY) continue;
                int near = ny * _zonesX + nx;
                if (_canvasCityKeyed && (cityChanged || waterChanged)) ShapeZones.Add(near);
                OwnerZones.Add(near);
            }
            if (political && !CanvasNoFlip.Contains(id)) LeadZones.Add(id);
        }
        CanvasNoFlip.Clear();
        foreach (int id in OwnerZones)
        {
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = zx + dx, ny = zy + dy;
                if (nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY) PaintZones.Add(ny * _zonesX + nx);
            }
        }
        // 换主翻牌：镜头里换了主的块与挨着它们的块，先记下旧样子
        OldFaces.Clear();
        bool flips = LeadZones.Count > 0;
        if (flips)
            foreach (int id in PaintZones)
            {
                TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
                if (zone == null || !InFlipView(zone)) continue;
                Color32[] face = CanvasFace(zone);
                if (face != null) OldFaces[id] = face;
            }
        foreach (int id in ShapeZones) BuildCityShape(id % _zonesX, id / _zonesX);
        foreach (int id in OwnerZones) OwnZone(id % _zonesX, id / _zonesX);
        BeginCanvasColors();
        foreach (int id in PaintZones) ColorZone(calc.pixels, id % _zonesX, id / _zonesX);
        // 正在翻的块又画了一遍：新样子换成眼下的，牌照旧翻
        foreach (int id in PaintZones)
        {
            if (!Transitions.TryGetValue(id, out Transition running)) continue;
            TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
            if (zone == null) continue;
            running.newPixels = BaseFace(zone);
            running.phase = -1;
            WriteFlip(calc.pixels, zone, running, Time.unscaledTime);
        }
        if (flips) PlanCanvasFlips(calc);
        calc.setDirty();
    }

    // 这块地眼下看到的样子(正在翻的取看到的那一面)
    private static Color32[] CanvasFace(TileZone zone) =>
        Transitions.ContainsKey(zone.id) ? Capture(zone)?.oldPixels : BaseFace(zone);

    // 这块地刚画好的样子(不提亮的底色)
    private static Color32[] BaseFace(TileZone zone)
    {
        int[] ids = PixelIds(zone);
        var face = new Color32[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            face[i] = id >= 0 && id < _base.Length ? _base[id] : default;
        }
        return face;
    }

    // 一块地的城、归属：变了说明要重算(城变了重算轮廓；本来的归属或它的样子变了是换主，要翻牌)
    private static void RefreshZone(ZoneCalculator calc, MetaTypeAsset layer, int option, int id, out bool cityChanged,
        out bool shadeChanged, out bool political)
    {
        TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
        int city = CityOf(zone?.city);
        int owner = 0, ownerKey = 0, shade = 0, push = 0;
        if (!_canvasCityKeyed)
        {
            // 不按城的图层：归属按块取
            ZoneMetas(layer, option, zone, out IMetaObject ownerMeta, out IMetaObject shadeMeta);
            owner = IndexOf(ownerMeta);
            ownerKey = CanvasOwnerKey(layer, ownerMeta);
            shade = IndexOf(shadeMeta);
        }
        else if (zone != null && city != 0)
        {
            RefreshCity(layer, option, city, zone);
            owner = _cityOwner[city];
            ownerKey = _cityOwnerKey[city];
            shade = _cityContested[city] ? IndexOf(ShadeOf(zone, RealMetaOf(layer, option, zone))) : _cityShade[city];
            push = IndexOf(PushMetaOf(layer, zone));
        }
        bool seen = _zoneSeen[id];
        cityChanged = !seen || _zoneCity[id] != city;
        shadeChanged = !seen || _zoneShade[id] != shade || _zonePush[id] != push;
        political = seen && _canvasReady && owner != 0 && _zoneOwner[id] != 0 &&
                    (_zoneOwner[id] != owner || _zoneOwnerKey[id] != ownerKey) &&
                    (!_canvasCityKeyed || city != 0 && _zoneCity[id] != 0);
        _zoneCity[id] = city;
        _zoneShade[id] = shade;
        _zonePush[id] = push;
        _zoneOwner[id] = owner;
        _zoneOwnerKey[id] = ownerKey;
        _zoneSeen[id] = true;
    }

    // 不按城的图层：一块地本来的归属与显示成谁(国家、帝国图层别的档按实际控制方，被占的算占领方)
    private static void ZoneMetas(MetaTypeAsset layer, int option, TileZone zone, out IMetaObject owner, out IMetaObject shade)
    {
        owner = zone == null ? null : LayerMeta(layer, option, zone);
        shade = zone == null ? null : ShadeOf(zone, ControlLayer(layer) ? RealMetaOf(layer, option, zone) : owner);
    }

    // 城池归属的图层：一座城的归属、显示成谁、有没有被占的块，一轮只取一次
    private static void RefreshCity(MetaTypeAsset layer, int option, int city, TileZone zone)
    {
        if (_cityPass[city] == _canvasPass) return;
        _cityPass[city] = _canvasPass;
        City c = CityTable[city];
        IMetaObject meta = LayerMeta(layer, option, zone);
        _cityOwner[city] = IndexOf(meta);
        _cityOwnerKey[city] = CanvasOwnerKey(layer, meta);
        _cityShade[city] = IndexOf(ShadeOf(zone, meta));
        _cityContested[city] = ControlLayer(layer) && c.kingdom != null && c.HasOccupiedZones();
        _citySeen[city] = true;
    }

    // 一块地各格是不是水；变了返回 true
    private static bool RefreshWater(int id)
    {
        int zx = id % _zonesX, zy = id / _zonesX;
        bool changed = false;
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize; x++)
        {
            int t = y * _canvasW + x;
            bool water = IsWater(x, y);
            if (_water[t] == water) continue;
            _water[t] = water;
            changed = true;
        }
        return changed;
    }

    #endregion

    #region 形状与归属

    // 一块地各格属于哪座城(同 SmoothedOwnerAt(x, y, cities: true))：周围 3×3 块同一座城(或都没有)直接填
    private static void BuildCityShape(int zx, int zy)
    {
        _shapeKey[zy * _zonesX + zx] = ShapeKey(zx, zy);
        int center = _zoneCity[zy * _zonesX + zx];
        bool uniform = true;
        for (int dy = -1; dy <= 1 && uniform; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (ZoneCityAt(zx + dx, zy + dy) != center)
            {
                uniform = false;
                break;
            }
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize; x++)
        {
            int t = y * _canvasW + x;
            _tileCity[t] = _water[t] ? (ushort)0 : uniform ? (ushort)center : (ushort)CityAt(x, y);
        }
    }

    private static int ZoneCityAt(int zx, int zy) =>
        zx < 0 || zx >= _zonesX || zy < 0 || zy >= _zonesY ? 0 : _zoneCity[zy * _zonesX + zx];

    private static readonly int[] CandidateIds = new int[9];

    // B 样条加噪声：同 SmoothedOwnerAt，候选按城编号、噪声按城的散列
    private static int CityAt(int x, int y)
    {
        int zx = x / ZoneSize, zy = y / ZoneSize;
        float tx = (x + 0.5f) / ZoneSize - zx - 0.5f, ty = (y + 0.5f) / ZoneSize - zy - 0.5f;
        float ax0 = 0.5f * (0.5f - tx) * (0.5f - tx), ax1 = 0.75f - tx * tx, ax2 = 0.5f * (0.5f + tx) * (0.5f + tx);
        float ay0 = 0.5f * (0.5f - ty) * (0.5f - ty), ay1 = 0.75f - ty * ty, ay2 = 0.5f * (0.5f + ty) * (0.5f + ty);
        int count = 0;
        for (int j = -1; j <= 1; j++)
        {
            float wy = j < 0 ? ay0 : j == 0 ? ay1 : ay2;
            for (int i = -1; i <= 1; i++)
            {
                int candidate = ZoneCityAt(zx + i, zy + j);
                float w = wy * (i < 0 ? ax0 : i == 0 ? ax1 : ax2);
                int k = 0;
                while (k < count && CandidateIds[k] != candidate) k++;
                if (k == count)
                {
                    CandidateIds[count] = candidate;
                    Weights[count] = 0f;
                    count++;
                }
                Weights[k] += w;
            }
        }
        if (count == 1) return CandidateIds[0];
        int best = 0;
        float bestScore = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            float score = Weights[k] + NoiseAmplitude * (Noise(x, y, CityHash[CandidateIds[k]]) - 0.5f);
            if (score <= bestScore) continue;
            bestScore = score;
            best = CandidateIds[k];
        }
        return best;
    }

    // 一块地各格归谁(同 OwnerAt)：先看城的轮廓，被占、逐格推进的城在轮廓里按各块的实际控制方平滑
    private static void OwnZone(int zx, int zy)
    {
        if (!_canvasCityKeyed)
        {
            OwnZoneByZones(zx, zy);
            return;
        }
        bool control = ControlLayer(_canvasLayer);
        int zoneId = zy * _zonesX + zx;
        TileZone zone = World.world.zone_calculator.getZone(zx, zy);
        int push = _zonePush[zoneId], zoneCity = _zoneCity[zoneId];
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize; x++)
        {
            int t = y * _canvasW + x;
            int city = _tileCity[t];
            int owner;
            if (city == 0) owner = 0;
            else if (!control) owner = IndexOf(CityTable[city]);
            else if (push != 0 && zoneCity == city && FrontSystem.SmoothCovers(zone, x, y)) owner = push;
            else if (!_cityContested[city]) owner = _cityShade[city];
            else owner = ShadeAt(x, y, city);
            _owner[t] = owner;
        }
    }

    // 不按城的图层：每格按周围 3×3 块的归属平滑(同 SmoothedOwnerAt(x, y, false))；周围 3×3 块同属一方直接填
    private static void OwnZoneByZones(int zx, int zy)
    {
        int center = _zoneShade[zy * _zonesX + zx];
        bool uniform = true;
        for (int dy = -1; dy <= 1 && uniform; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = zx + dx, ny = zy + dy;
            int near = nx < 0 || nx >= _zonesX || ny < 0 || ny >= _zonesY ? 0 : _zoneShade[ny * _zonesX + nx];
            if (near == center) continue;
            uniform = false;
            break;
        }
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize; x++)
        {
            int t = y * _canvasW + x;
            _owner[t] = _water[t] ? 0 : uniform ? center : ShadeAt(x, y, 0);
        }
    }

    // 城的轮廓里按各块的实际控制方平滑(同 SmoothedOwnerAt(x, y, false, city))；city 为 0 时不限城(地图外的块算无主)
    private static int ShadeAt(int x, int y, int city)
    {
        int zx = x / ZoneSize, zy = y / ZoneSize;
        float tx = (x + 0.5f) / ZoneSize - zx - 0.5f, ty = (y + 0.5f) / ZoneSize - zy - 0.5f;
        float ax0 = 0.5f * (0.5f - tx) * (0.5f - tx), ax1 = 0.75f - tx * tx, ax2 = 0.5f * (0.5f + tx) * (0.5f + tx);
        float ay0 = 0.5f * (0.5f - ty) * (0.5f - ty), ay1 = 0.75f - ty * ty, ay2 = 0.5f * (0.5f + ty) * (0.5f + ty);
        int count = 0;
        for (int j = -1; j <= 1; j++)
        {
            float wy = j < 0 ? ay0 : j == 0 ? ay1 : ay2;
            for (int i = -1; i <= 1; i++)
            {
                int nx = zx + i, ny = zy + j;
                bool inside = nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY;
                if (city != 0 && (!inside || _zoneCity[ny * _zonesX + nx] != city)) continue;
                int candidate = inside ? _zoneShade[ny * _zonesX + nx] : 0;
                float w = wy * (i < 0 ? ax0 : i == 0 ? ax1 : ax2);
                int k = 0;
                while (k < count && CandidateIds[k] != candidate) k++;
                if (k == count)
                {
                    CandidateIds[count] = candidate;
                    Weights[count] = 0f;
                    count++;
                }
                Weights[k] += w;
            }
        }
        if (count == 0) return city == 0 ? 0 : _cityShade[city];
        if (count == 1) return CandidateIds[0];
        int best = 0;
        float bestScore = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            float score = Weights[k] + NoiseAmplitude * (Noise(x, y, OwnerTable[CandidateIds[k]]?.GetHashCode() ?? 0) - 0.5f);
            if (score <= bestScore) continue;
            bestScore = score;
            best = CandidateIds[k];
        }
        return best;
    }

    #endregion

    #region 上色

    // 各方的颜色(同原版 colorZone 与 ZonePaintBudget.BeforeSelectionColors 的选中变淡)、所属帝国(金边)：一轮里按编号记一次
    private static Color32[] _palIn = Array.Empty<Color32>(), _palOut = Array.Empty<Color32>();
    private static Empire[] _palEmpire = Array.Empty<Empire>();
    private static int[] _palEpoch = Array.Empty<int>();
    private static int _canvasEpoch;
    private static readonly Dictionary<long, bool> CanvasHostility = new();
    private static readonly Dictionary<City, Color32[]> CanvasStripes = new();
    private static bool _canvasFront, _canvasGold, _canvasPure, _canvasUnitSet;
    private static object _canvasUnitMeta;

    private static void BeginCanvasColors()
    {
        _canvasEpoch++;
        CanvasHostility.Clear();
        CanvasStripes.Clear();
        _canvasFront = CorpsSystem.Enabled && ControlLayer(_canvasLayer);
        _canvasGold = _canvasLayer.map_mode == MetaType.Kingdom;
        _canvasPure = PureMapMode.Enabled;
        // 选中的单位所属的那一方(原版只显示它)：一轮取一次(没有国家的单位取联盟会出错)
        _canvasUnitSet = SelectedUnit.isSet();
        try
        {
            _canvasUnitMeta = _canvasUnitSet ? SelectedUnit.unit.getMetaObjectOfType(_canvasLayer.map_mode) : null;
        }
        catch (Exception)
        {
            _canvasUnitMeta = null;
        }
        BeginLift();
    }

    private static void Palette(int owner)
    {
        if (owner >= _palEpoch.Length)
        {
            int size = Mathf.Max(owner + 1, _palEpoch.Length * 2, 256);
            Array.Resize(ref _palIn, size);
            Array.Resize(ref _palOut, size);
            Array.Resize(ref _palEmpire, size);
            Array.Resize(ref _palEpoch, size);
        }
        if (_palEpoch[owner] == _canvasEpoch) return;
        _palEpoch[owner] = _canvasEpoch;
        IMetaObject meta = OwnerTable[owner];
        // 刚灭亡、还没重画到的一方(各块下一帧就改)：取色出错按透明
        try
        {
            CanvasColors(meta, out _palIn[owner], out _palOut[owner]);
            _palEmpire[owner] = EmpireOf(meta);
        }
        catch (Exception)
        {
            _palIn[owner] = _palOut[owner] = new Color32(0, 0, 0, 0);
            _palEmpire[owner] = null;
        }
    }

    private static void CanvasColors(IMetaObject meta, out Color32 inside, out Color32 outside)
    {
        ZoneCalculator calc = World.world.zone_calculator;
        ColorAsset color = SafeColor(meta);
        if (color == null)
        {
            inside = outside = new Color32(0, 0, 0, 0);
            return;
        }
        outside = color.getColorMainSecond32();
        inside = calc._cached_should_be_clear_color ? calc._color_clear : color.getColorBorderInsideAlpha32();
        bool selected = false;
        if (calc._cached_check_animation)
        {
            if (calc.shouldShowSelectionFor(meta)) selected = true;
            else if (!calc._selected_nano_object.isRekt())
            {
                inside.a = (byte)(inside.a * 0.6f);
                outside.a = (byte)(outside.a * 0.6f);
            }
        }
        bool shown = selected || (!_canvasUnitSet ? calc.checkShouldDrawObject(meta.isFavorite()) : ReferenceEquals(_canvasUnitMeta, meta));
        if (shown) return;
        inside.a = (byte)(inside.a * 0.5f);
        outside.a = (byte)(outside.a * 0.5f);
        if (calc._cached_should_be_clear_color) inside = calc._color_clear;
    }

    // 一块地各格上色：边框、战线、金边、兼属斜纹，存底色后按高亮提亮
    private static void ColorZone(Color32[] pixels, int zx, int zy)
    {
        int zoneId = zy * _zonesX + zx;
        int native = _zoneShade[zoneId];
        Color32[] stripes = null;
        if (_canvasPure && _zoneCity[zoneId] != 0)
        {
            City city = CityTable[_zoneCity[zoneId]];
            if (!CanvasStripes.TryGetValue(city, out stripes)) CanvasStripes[city] = stripes = DeJureShareSystem.StripeColors(city);
        }
        int width = _canvasW, height = _canvasH;
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize; x++)
        {
            int t = y * width + x;
            int owner = _owner[t];
            Color32 color;
            bool edge = false;
            if (owner == 0)
                color = new Color32(0, 0, 0, 0);
            else
            {
                Palette(owner);
                edge = x + 1 < width && Differs(t + 1, owner) || x > 0 && Differs(t - 1, owner) ||
                       y + 1 < height && Differs(t + width, owner) || y > 0 && Differs(t - width, owner);
                if (edge)
                {
                    if (_canvasFront && (Hostile(owner, x + 1 < width ? t + 1 : -1) || Hostile(owner, x > 0 ? t - 1 : -1) ||
                                         Hostile(owner, y + 1 < height ? t + width : -1) || Hostile(owner, y > 0 ? t - width : -1)))
                        color = FrontRed;
                    else if (_canvasGold && _palEmpire[owner] is Empire empire &&
                             (GoldAgainst(empire, x + 1 < width ? t + 1 : -1) || GoldAgainst(empire, x > 0 ? t - 1 : -1) ||
                              GoldAgainst(empire, y + 1 < height ? t + width : -1) || GoldAgainst(empire, y > 0 ? t - width : -1)))
                        color = EmpireGold;
                    else color = _palOut[owner];
                }
                else
                {
                    color = _palIn[owner];
                    // 兼属：每两格换一种颜色(同 PureMapPatch.FinishZone)；归邻国、邻城的格不画本块的斜纹
                    if (stripes != null && owner == native && color.a != 0)
                    {
                        int band = (x + y) / 2 % (stripes.Length + 1);
                        if (band > 0)
                        {
                            Color32 stripe = stripes[band - 1];
                            color = new Color32(stripe.r, stripe.g, stripe.b, color.a);
                        }
                    }
                }
            }
            _base[t] = color;
            _hasBase[t] = true;
            _outline[t] = edge;
            _foreign[t] = owner != native;
            pixels[t] = owner == 0 ? color : Lifted(t);
        }
    }

    // 相邻格归属不同(水面不算：海岸本身不画边框)
    private static bool Differs(int t, int owner) => !_water[t] && _owner[t] != owner;

    // 相邻格是交战的一方且本月真打起来了
    private static bool Hostile(int owner, int next)
    {
        if (next < 0 || _water[next]) return false;
        int other = _owner[next];
        if (other == 0 || other == owner) return false;
        long key = ((long)owner << 32) | (uint)other;
        if (CanvasHostility.TryGetValue(key, out bool hostile)) return hostile;
        hostile = AtWarMeta(OwnerTable[owner], OwnerTable[other]);
        CanvasHostility[key] = hostile;
        return hostile;
    }

    // 相邻格不属于这个帝国(同 EmpireDiffers：水面、无主地也算外面)
    private static bool GoldAgainst(Empire empire, int next)
    {
        if (next < 0) return false;
        int other = _owner[next];
        if (other == 0) return true;
        Palette(other);
        return _palEmpire[other] != empire;
    }

    #endregion

    #region 换主翻牌

    // 镜头里换了主的块按新主人分组，各组从挨着新主人的块起一圈圈翻(同 PlanSweeps)；挨着它们、样子也变了的块翻到一半时跟着换
    private static void PlanCanvasFlips(ZoneCalculator calc)
    {
        float now = Time.unscaledTime;
        var leads = new Dictionary<int, Transition>();
        var groups = new Dictionary<(int owner, int key), List<TileZone>>();
        foreach (int id in LeadZones)
        {
            if (!OldFaces.TryGetValue(id, out Color32[] old)) continue;
            TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
            Color32[] fresh = zone == null ? null : BaseFace(zone);
            if (fresh == null || Same(old, fresh)) continue;
            leads[id] = new Transition { oldPixels = old, newPixels = fresh, created = now };
            var key = (_zoneOwner[id], _zoneOwnerKey[id]);
            if (!groups.TryGetValue(key, out List<TileZone> list)) groups[key] = list = new List<TileZone>();
            list.Add(zone);
        }
        foreach (KeyValuePair<(int owner, int key), List<TileZone>> group in groups)
            PlanCanvasGroup(calc, group.Key.owner, group.Key.key, group.Value, leads, now);
        foreach (KeyValuePair<int, Transition> pair in leads)
        {
            TileZone zone = calc.getZone(pair.Key % _zonesX, pair.Key / _zonesX);
            Transitions[pair.Key] = pair.Value;
            WriteFlip(calc.pixels, zone, pair.Value, now);
        }
        // 跟着换的邻块：挨着的换主块里最早翻到一半的那一刻直接换
        foreach (KeyValuePair<int, Color32[]> pair in OldFaces)
        {
            int id = pair.Key;
            if (leads.ContainsKey(id)) continue;
            TileZone zone = calc.getZone(id % _zonesX, id / _zonesX);
            if (zone == null) continue;
            float at = float.MaxValue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = zone.x + dx, ny = zone.y + dy;
                if (nx < 0 || nx >= _zonesX || ny < 0 || ny >= _zonesY) continue;
                if (leads.TryGetValue(ny * _zonesX + nx, out Transition lead)) at = Mathf.Min(at, lead.flipAt + lead.duration / 2f);
            }
            if (at == float.MaxValue) continue;
            Color32[] fresh = BaseFace(zone);
            if (fresh == null || Same(pair.Value, fresh)) continue;
            var follower = new Transition { oldPixels = pair.Value, newPixels = fresh, created = now, follow = true, planned = true, flipAt = at };
            Transitions[id] = follower;
            WriteFlip(calc.pixels, zone, follower, now);
        }
        OldFaces.Clear();
    }

    private static void PlanCanvasGroup(ZoneCalculator calc, int owner, int ownerKey, List<TileZone> zones,
        Dictionary<int, Transition> leads, float now)
    {
        var members = new HashSet<int>();
        foreach (TileZone zone in zones) members.Add(zone.id);
        var best = new Dictionary<int, (int distance, float dirX, float dirY)>();
        var open = new SortedDictionary<int, List<TileZone>>();
        void Offer(TileZone zone, int distance, float dirX, float dirY)
        {
            if (best.TryGetValue(zone.id, out var known) && known.distance <= distance) return;
            best[zone.id] = (distance, dirX, dirY);
            if (!open.TryGetValue(distance, out List<TileZone> list)) open[distance] = list = new List<TileZone>();
            list.Add(zone);
        }
        IMetaObject target = OwnerTable[owner];
        // 起点：挨着本来就归新主人(同样子)的块；没有就挨着新主人的宗主、同一帝国的块；再没有(或是自己变色)就是离都城最近的块
        foreach (bool related in new[] { false, true })
        {
            foreach (TileZone zone in zones)
            {
                float sumX = 0f, sumY = 0f;
                bool touches = false;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = zone.x + dx, ny = zone.y + dy;
                    if (nx < 0 || nx >= _zonesX || ny < 0 || ny >= _zonesY) continue;
                    int near = ny * _zonesX + nx;
                    if (members.Contains(near) || _zoneOwner[near] == 0) continue;
                    bool hit = related
                        ? Related(OwnerTable[_zoneOwner[near]], target)
                        : _zoneOwner[near] == owner && _zoneOwnerKey[near] == ownerKey;
                    if (!hit) continue;
                    touches = true;
                    sumX -= dx;
                    sumY -= dy;
                }
                if (!touches) continue;
                float length = Mathf.Sqrt(sumX * sumX + sumY * sumY);
                Offer(zone, 0, length < 0.01f ? 0f : sumX / length, length < 0.01f ? 0f : sumY / length);
            }
            if (best.Count > 0) break;
        }
        if (best.Count == 0)
        {
            TileZone seat = SeatOf(target);
            TileZone first = zones[0];
            if (seat != null)
            {
                int bestDistance = int.MaxValue;
                foreach (TileZone zone in zones)
                {
                    int d = (zone.x - seat.x) * (zone.x - seat.x) + (zone.y - seat.y) * (zone.y - seat.y);
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    first = zone;
                }
            }
            Offer(first, 0, 0f, 0f);
        }
        var seeds = new List<(TileZone zone, int distance)>();
        foreach (KeyValuePair<int, (int distance, float dirX, float dirY)> pair in best)
            seeds.Add((calc.getZone(pair.Key % _zonesX, pair.Key / _zonesX), pair.Value.distance));
        void Spread()
        {
            while (open.Count > 0)
            {
                int distance = 0;
                foreach (int key in open.Keys)
                {
                    distance = key;
                    break;
                }
                List<TileZone> ring = open[distance];
                open.Remove(distance);
                foreach (TileZone zone in ring)
                {
                    if (best[zone.id].distance != distance) continue;
                    foreach ((int dx, int dy) in Sides)
                    {
                        int nx = zone.x + dx, ny = zone.y + dy;
                        if (nx < 0 || nx >= _zonesX || ny < 0 || ny >= _zonesY || !members.Contains(ny * _zonesX + nx)) continue;
                        Offer(calc.getZone(nx, ny), distance + 1, dx, dy);
                    }
                }
            }
        }
        Spread();
        // 不连着的几片(海岛、飞地)：按离最近起点的直线距离接着排
        if (best.Count < members.Count)
        {
            foreach (TileZone zone in zones)
            {
                if (best.ContainsKey(zone.id)) continue;
                float nearest = float.MaxValue, dirX = 0f, dirY = 0f;
                foreach ((TileZone seed, int distance) in seeds)
                {
                    if (seed == null) continue;
                    float dx = zone.x - seed.x, dy = zone.y - seed.y, length = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance + length >= nearest) continue;
                    nearest = distance + length;
                    dirX = length > 0f ? dx / length : 0f;
                    dirY = length > 0f ? dy / length : 0f;
                }
                if (nearest < float.MaxValue) Offer(zone, Mathf.RoundToInt(nearest), dirX, dirY);
            }
            Spread();
        }
        int farthest = 0;
        foreach ((int distance, float _, float _) in best.Values) farthest = Mathf.Max(farthest, distance);
        float step = Mathf.Min(SweepStep, MaxSweepSeconds / Mathf.Max(1, farthest + 1));
        foreach (TileZone zone in zones)
        {
            if (!leads.TryGetValue(zone.id, out Transition transition)) continue;
            (int distance, float dirX, float dirY) = best.TryGetValue(zone.id, out var entry) ? entry : (farthest + 1, 0f, 0f);
            transition.flipAt = now + (distance + 0.3f * (FrontNoise(zone.x * 5, zone.y * 5) - 0.5f)) * step;
            Orient(transition, dirX, dirY, zone);
            transition.planned = true;
        }
    }

    private static bool Same(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            Color32 x = a[i], y = b[i];
            if (x.r != y.r || x.g != y.g || x.b != y.b || x.a != y.a) return false;
        }
        return true;
    }

    #endregion
}
