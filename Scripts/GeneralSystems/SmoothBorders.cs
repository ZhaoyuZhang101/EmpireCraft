using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 平滑国界(模组设置「平滑国界」)：国界、城界不再沿 8×8 的地块方格走，而是逐个像素(地格)判定归属——
// 每格按周围 3×3 块地的城市归属做二次 B 样条加权(块心权重最大，越靠边越接近一半一半)，再叠一层按城市散列的
// 低频噪声，拐角变圆、斜边变顺、直边微微起伏；归属不同的相邻像素画边框。逻辑上的归属仍按地块(建城、
// 征税、占领都按块)，这里只改画法。国家、帝国图层按实际控制方画：被敌军占着的地直接涂占领方的颜色(不画斜纹)，
// 军团模式下逐格推进中已吃下的格子算推进方的(见 FrontSystem.Smooth)，国界跟着前线走；交战双方相接处的边框涂成战线的朱红
// (原来沿地块边画的战线在平滑模式下不画，见 CorpsMapRenderer)。
// 水面不归任何国家：领土只涂陆地，国界贴着海岸走，海岸本身不画边框(以前一块地里的水也算这国的，国界画在离岸几格的水上)。
// 悬停、选中的高亮：原版给那国的每块地叠一张整块的半透明方片(QuantumSpriteLibrary.colorZones)，平滑后仍是方块；
// 这里改为上色时把归它的像素提亮，形状与国界一致；高亮对象变了才重画相关的地块。
// ZoneCalculator.applyMetaColorsToZone 的前缀调用(PureMapPatch)：画好这块地的每个像素后跳过原版。
public static class SmoothBorders
{
    private const int ZoneSize = Config.CITY_ZONE_SIZE;
    private const float NoiseAmplitude = 0.28f, NoiseCell = 6f;
    private static readonly Color32 EmpireGold = new(232, 184, 64, 255), FrontRed = new(226, 64, 52, 255);

    public static bool Enabled { get; private set; }

    // 本次画过的像素：边框(地形浮雕、占领条纹不覆盖)、归别人的(城市平面图不画在上面)
    private static bool[] _outline = Array.Empty<bool>(), _foreign = Array.Empty<bool>();
    // 每格上次画的归属与不带高亮的底色(高亮开关时直接改这几格的颜色，不必重画地块)
    private static IMetaObject[] _owner = Array.Empty<IMetaObject>();
    private static Color32[] _base = Array.Empty<Color32>();
    private static bool[] _hasBase = Array.Empty<bool>();
    private static int _paintedZone = -1;
    // 每块地上次画时的归属：变了就让周围八块也重画(斜对角的块原版不会因此重画)
    private static readonly Dictionary<int, (object meta, int color)> LastMeta = new();

    public static void Set(bool on)
    {
        if (on == Enabled) return;
        Enabled = on;
        LastMeta.Clear();
        ResetTransitions();
        _layerFlip = null;
        ZoneFlipOverlay.Hide();
        World.world?.zone_calculator?.dirtyAndClear();
    }

    // 图层诊断用(见 LayerDiagnostics)：这一格上次画时记下的归属、边框、是否归别人，这块地是否正在翻牌
    internal static string Describe(int tileId, int zoneId)
    {
        if (!Enabled || tileId < 0 || tileId >= _owner.Length) return "平滑国界关";
        IMetaObject owner = _owner[tileId];
        string name = owner is Kingdom kingdom ? kingdom.name : owner is City city ? city.name : owner?.GetType().Name ?? "无";
        return $"记下归属 {name}，边框 {_outline[tileId]}，归别人 {_foreign[tileId]}，有底色 {_hasBase[tileId]}，" +
               $"翻牌中 {Transitions.ContainsKey(zoneId)}，有快照 {Snapshots.ContainsKey(zoneId)}";
    }

    public static bool IsOutline(int tileId) => Enabled && tileId >= 0 && tileId < _outline.Length && _outline[tileId];
    public static bool IsForeign(int tileId) => Enabled && tileId >= 0 && tileId < _foreign.Length && _foreign[tileId];

    #region 归属

    // 以当前块为中心 5×5 块的归属缓存(像素要看 3×3 块，边上的像素再看一圈)
    private static readonly IMetaObject[] Metas = new IMetaObject[25];
    // 正在逐格推进的块：吃下的格子归谁(推进方，或收复时的原主)
    private static readonly IMetaObject[] PushMetas = new IMetaObject[25];
    private static readonly TileZone[] Zones = new TileZone[25];
    // 城界是国家/帝国的几何底图：先确定属于哪座城，再映射政治归属，不能合并国家权重后另算一套边。
    private static readonly IMetaObject[] CityMetas = new IMetaObject[25];
    private static bool _cityGeometry, _controlGeometry;
    private static int _cx, _cy;

    // 图层本来的归属(原版按它算颜色、选中高亮)
    private static IMetaObject LayerMeta(MetaTypeAsset layer, int option, TileZone zone)
    {
        if (zone == null) return null;
        IMetaObject meta = layer.map_mode == MetaType.City ? zone.city
            : layer.map_mode == MetaType.Alliance ? AllianceMeta(zone, option)
            : layer.tile_get_metaobject?.Invoke(zone, option);
        // 帝国图层上没被哪个帝国(或同盟视图)覆盖的王国：模组按本国颜色补画(见 EmpireCraftMetaTypeLibrary)，这里也按本国归属
        if ((meta == null || !meta.isAlive()) && layer.map_mode == MetaTypeExtension.Empire &&
            zone.city?.kingdom is Kingdom kingdom && kingdom.isAlive() && !kingdom.wild)
            meta = kingdom;
        return meta != null && meta.isAlive() ? meta : null;
    }

    // 联盟图层(原版没有按地块取归属的函数，结盟的国家按联盟画、没结盟的按城画，见 MetaTypeLibrary.alliance.draw_zones)：
    // 同样的规则逐格平滑(用户 2026-10-17 "明明是平滑地图模式我选择联盟图层居然还是原版的")
    private static IMetaObject AllianceMeta(TileZone zone, int option)
    {
        City city = zone.city;
        if (city?.data == null || city.isRekt()) return null;
        Alliance alliance = zone.getAllianceOnZone(option);
        return alliance != null && alliance.isAlive() ? alliance : city;
    }

    // 平滑国界接手的图层：城市、联盟，以及原版按地块取归属的图层
    private static bool Smoothable(MetaTypeAsset layer) =>
        layer != null && (layer.map_mode == MetaType.City || layer.map_mode == MetaType.Alliance || layer.tile_get_metaobject != null);

    private static bool ControlLayer(MetaTypeAsset layer) =>
        layer.map_mode == MetaType.Kingdom || layer.map_mode == MetaTypeExtension.Empire;

    // 某国在国家/帝国图层上的归属：帝国图层取它所属的帝国
    private static IMetaObject RealmMeta(MetaTypeAsset layer, Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        if (layer.map_mode == MetaTypeExtension.Empire && kingdom.IsInEmpire() && kingdom.GetEmpire() is Empire empire &&
            !empire.IsArchived()) return empire;
        return kingdom;
    }

    // 实际控制方：国家、帝国图层上被敌军占着的地算占领方的
    private static IMetaObject RealMetaOf(MetaTypeAsset layer, int option, TileZone zone)
    {
        IMetaObject meta = LayerMeta(layer, option, zone);
        if (!ControlLayer(layer) || zone?.city?.kingdom == null) return meta;
        Kingdom owner = zone.city.kingdom;
        Kingdom occupier = zone.city.GetTileZoneOccupier(zone);
        if (occupier?.data == null || occupier.isRekt() || occupier == owner || occupier.isInWarOnSameSide(owner) ||
            !FrontSystem.Hostile(owner, occupier)) return meta;
        return RealmMeta(layer, occupier) ?? meta;
    }

    private static IMetaObject PushMetaOf(MetaTypeAsset layer, TileZone zone)
    {
        if (!ControlLayer(layer) || zone?.city?.kingdom == null || !FrontSystem.SmoothPushOn(zone, out Kingdom side, out bool retake))
            return null;
        return retake ? RealmMeta(layer, zone.city.kingdom) : RealmMeta(layer, side);
    }

    private static bool Fill(MetaTypeAsset layer, TileZone center)
    {
        if (!Smoothable(layer)) return false;
        ZoneCalculator calc = World.world.zone_calculator;
        int option = layer.getZoneOptionState();
        _cx = center.x;
        _cy = center.y;
        _controlGeometry = ControlLayer(layer);
        _cityGeometry = _controlGeometry || layer.map_mode == MetaType.City;
        _hover = HoveredProvince(layer, option);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int index = (dy + 2) * 5 + dx + 2;
            TileZone zone = calc.getZone(_cx + dx, _cy + dy);
            Zones[index] = zone;
            CityMetas[index] = zone?.city is City city && city.isAlive() ? city : null;
            Metas[index] = MetaOf(layer, option, zone);
            Shades[index] = ShadeOf(zone, Metas[index]);
            PushMetas[index] = PushMetaOf(layer, zone);
        }
        return true;
    }

    // 帝国图层悬停某省：原版把这个省的地块整块换成本省颜色(见 EmpireCraftMetaTypeLibrary)，而各省都算同一个帝国，
    // 平滑时分不出省界，颜色按地块一块块换，边上是锯齿、弯进弯出的格子颜色不对。悬停时把这个省当作单独的一方(Shades)：
    // 省界和国界一样按 B 样条加噪声平滑，相邻地块里划进来的格子一起换色
    private static readonly IMetaObject[] Shades = new IMetaObject[25];
    private static Kingdom _hover;
    private static IMetaObject _hoverRealm;

    private static Kingdom HoveredProvince(MetaTypeAsset layer, int option)
    {
        if (layer.map_mode != MetaTypeExtension.Empire || option != 0) return null;
        Kingdom kingdom = World.world.getMouseTilePosCachedFrame()?.zone_city?.kingdom;
        return kingdom != null && kingdom.isAlive() && kingdom.IsInEmpire() ? kingdom : null;
    }

    // 悬停的省、刚移开的省：它们的地块(连同四周，平滑的边伸进邻块)上色优先，鼠标一动就跟上(见 ZonePaintBudget)
    private static Kingdom _hoverNow, _hoverBefore;
    private static float _hoverChangedAt = -10f;
    private const float HoverUrgentSeconds = 1f;

    private static void TrackHover()
    {
        if (!Enabled || World.world == null) return;
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        Kingdom hover = layer == null ? null : HoveredProvince(layer, layer.getZoneOptionState());
        if (hover == _hoverNow) return;
        _hoverBefore = _hoverNow;
        _hoverNow = hover;
        _hoverChangedAt = Time.unscaledTime;
    }

    public static bool HoverUrgent(TileZone zone)
    {
        if (!Enabled || zone == null || Time.unscaledTime - _hoverChangedAt > HoverUrgentSeconds) return false;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Kingdom kingdom = (dx == 0 && dy == 0 ? zone : World.world.zone_calculator.getZone(zone.x + dx, zone.y + dy))?.city?.kingdom;
            if (kingdom != null && (kingdom == _hoverNow || kingdom == _hoverBefore)) return true;
        }
        return false;
    }

    private static IMetaObject ShadeOf(TileZone zone, IMetaObject real)
    {
        if (_hover == null || real == null || zone?.city?.kingdom != _hover || !(real is Empire empire) ||
            _hover.GetEmpire() != empire) return real;
        _hoverRealm = real;
        return _hover;
    }

    private static readonly IMetaObject[] Candidates = new IMetaObject[9];
    private static readonly float[] Weights = new float[9];

    // 城界只算一遍。国家/帝国的和平国界是城市轮廓的并集；临时控制只在该城的轮廓内计算。
    private static IMetaObject OwnerAt(int x, int y)
    {
        if (IsWater(x, y)) return null;
        if (!_cityGeometry) return SmoothedOwnerAt(x, y, false);
        City city = SmoothedOwnerAt(x, y, true) as City;
        if (city == null || !_controlGeometry) return city;
        int zx = Mathf.FloorToInt((float)x / ZoneSize), zy = Mathf.FloorToInt((float)y / ZoneSize);
        int hx = zx - _cx + 2, hy = zy - _cy + 2;
        if (hx is >= 0 and < 5 && hy is >= 0 and < 5)
        {
            int here = hy * 5 + hx;
            if (ReferenceEquals(CityMetas[here], city) && PushMetas[here] != null &&
                FrontSystem.SmoothCovers(Zones[here], x, y)) return PushMetas[here];
        }
        return SmoothedOwnerAt(x, y, false, city);
    }

    private static IMetaObject SmoothedOwnerAt(int x, int y, bool cities, City within = null)
    {
        int zx = Mathf.FloorToInt((float)x / ZoneSize), zy = Mathf.FloorToInt((float)y / ZoneSize);
        float tx = (x + 0.5f) / ZoneSize - zx - 0.5f, ty = (y + 0.5f) / ZoneSize - zy - 0.5f;
        float ax0 = 0.5f * (0.5f - tx) * (0.5f - tx), ax1 = 0.75f - tx * tx, ax2 = 0.5f * (0.5f + tx) * (0.5f + tx);
        float ay0 = 0.5f * (0.5f - ty) * (0.5f - ty), ay1 = 0.75f - ty * ty, ay2 = 0.5f * (0.5f + ty) * (0.5f + ty);
        int count = 0;
        for (int j = -1; j <= 1; j++)
        {
            float wy = j < 0 ? ay0 : j == 0 ? ay1 : ay2;
            for (int i = -1; i <= 1; i++)
            {
                int gx = zx + i - _cx + 2, gy = zy + j - _cy + 2;
                bool inWindow = gx is >= 0 and < 5 && gy is >= 0 and < 5;
                int index = inWindow ? gy * 5 + gx : -1;
                if (within != null && (!inWindow || !ReferenceEquals(CityMetas[index], within))) continue;
                IMetaObject meta = inWindow ? cities ? CityMetas[index] : Shades[index] : null;
                float w = wy * (i < 0 ? ax0 : i == 0 ? ax1 : ax2);
                int k = 0;
                while (k < count && !ReferenceEquals(Candidates[k], meta)) k++;
                if (k == count)
                {
                    Candidates[count] = meta;
                    Weights[count] = 0f;
                    count++;
                }
                Weights[k] += w;
            }
        }
        if (count == 1) return Candidates[0];
        IMetaObject best = null;
        float bestScore = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            float score = Weights[k] + NoiseAmplitude * (Noise(x, y, Candidates[k]?.GetHashCode() ?? 0) - 0.5f);
            if (score <= bestScore) continue;
            bestScore = score;
            best = Candidates[k];
        }
        return best;
    }

    private static bool IsWater(int x, int y)
    {
        WorldTile tile = World.world.GetTile(x, y);
        TileTypeBase type = tile?.Type;
        return type != null && (type.ocean || type.liquid && !type.lava);
    }

    // 逐格占领推进线边缘的起伏(见 FrontSystem.Smooth)
    public static float FrontNoise(int x, int y) => Noise(x, y, 7919);

    // 低频值噪声(0~1)：格点间距 NoiseCell 个像素，双线性插值
    private static float Noise(int x, int y, int seed)
    {
        float fx = x / NoiseCell, fy = y / NoiseCell;
        int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
        float sx = fx - ix, sy = fy - iy;
        sx = sx * sx * (3f - 2f * sx);
        sy = sy * sy * (3f - 2f * sy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    #endregion

    #region 高亮

    private const float HighlightLift = 0.32f;
    private static readonly HashSet<IMetaObject> CollectMetas = new(), ActiveMetas = new();
    // 两组轮换(收齐一帧后互换)，不每次重建
    private static HashSet<int> CollectZones = new(), ActiveZones = new();
    private static int _collectFrame = -1;

    // 图层归属只看地块所属的城(城市图层；国家、帝国图层的"按城池归属"那一档，见 EnclaveFill：补洞只补没有城的地块)
    private static bool CityKeyed(MetaTypeAsset layer, int option) =>
        layer.map_mode == MetaType.City || option == 0 && ControlLayer(layer);

    // QuantumSpriteLibrary.colorZones 的前缀：记下这一帧要高亮哪些地块(及其归属)，不画方片。
    // 高亮的地块按城成串传进来：归属只看城的图层上，同一座城的地块只取一次归属
    public static bool Highlight(IEnumerable<TileZone> zones)
    {
        if (!Enabled || World.world == null || zones == null) return false;
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        if (!Smoothable(layer)) return false;
        int frame = Time.frameCount;
        if (_collectFrame != frame)
        {
            FinishFrame();
            _collectFrame = frame;
        }
        int option = layer.getZoneOptionState();
        bool byCity = CityKeyed(layer, option);
        City lastCity = null;
        IMetaObject lastMeta = null, lastAdded = null;
        foreach (TileZone zone in zones)
        {
            if (zone == null || !CollectZones.Add(zone.id)) continue;
            City city = zone.city;
            IMetaObject meta;
            if (byCity && city != null)
            {
                if (!ReferenceEquals(city, lastCity))
                {
                    lastCity = city;
                    lastMeta = LayerMeta(layer, option, zone);
                }
                meta = lastMeta;
            }
            else meta = LayerMeta(layer, option, zone);
            if (meta == null || ReferenceEquals(meta, lastAdded)) continue;
            CollectMetas.Add(meta);
            lastAdded = meta;
        }
        return true;
    }

    // 每帧(ModClass.Update)：上一帧以来没有高亮调用就熄灭
    public static void FrameTick()
    {
        // 初次进图：读档完、地图画得出来时来一遍全图换色(读档时分帧画上的颜色可能还不全、不对)
        if (_entryPending && Enabled && Config.game_loaded && !SmoothLoader.isLoading() &&
            World.world?.getCachedMapMetaAsset() != null && World.world.zone_calculator?._mode_asset != null)
        {
            _entryPending = false;
            RepaintAll("进图");
        }
        TrackHover();
        SweepTick();
        int frame = Time.frameCount;
        if (_collectFrame == frame || _collectFrame == frame - 1) return;
        if (ActiveZones.Count == 0 && CollectZones.Count == 0) return;
        FinishFrame();
        _collectFrame = frame;
    }

    // 收齐一帧：高亮对象变了，新旧地块连同四周重画
    // (性能日志"精灵·highlight_cursor_zones"一次 50~88 毫秒：大国几千块地，以前每次新建两个 HashSet、逐格浮点插值、
    // 逐格查一次帧号与字典；现在用按地块编号记轮次的数组去重，提亮查表，同一归属连成串只查一次)
    private static void FinishFrame()
    {
        bool same = CollectMetas.SetEquals(ActiveMetas) && CollectZones.SetEquals(ActiveZones);
        if (!same && World.world?.zone_calculator != null)
        {
            using var timing = FrameProfiler.Measure("平滑国界·高亮重算");
            ZoneCalculator calc = World.world.zone_calculator;
            ActiveMetas.Clear();
            ActiveMetas.UnionWith(CollectMetas);
            // 新旧两组互换：before 是旧的高亮地块，下面一起改色后随 CollectZones 清空
            HashSet<int> before = ActiveZones;
            ActiveZones = CollectZones;
            CollectZones = before;
            ResetLit();
            // 高亮只是把底色提亮：相关地块(连同四周，平滑的边会伸进邻块)直接改色，当帧就变，不必重画
            Color32[] pixels = calc.pixels;
            bool changed = false;
            if (pixels != null && pixels.Length == _base.Length)
            {
                BeginLift();
                int stamp = NextMark(calc);
                foreach (int id in before) changed |= LiftAround(calc, pixels, id, stamp);
                foreach (int id in ActiveZones) changed |= LiftAround(calc, pixels, id, stamp);
            }
            if (changed) calc.setDirty();
        }
        CollectMetas.Clear();
        CollectZones.Clear();
    }

    // 这块地连同四周八块按当前高亮重新提亮或还原(这一轮已经改过的块跳过)
    private static bool LiftAround(ZoneCalculator calc, Color32[] pixels, int zoneId, int stamp)
    {
        if (!calc._zones_dict_id.TryGetValue(zoneId, out TileZone zone) || zone == null) return false;
        bool changed = false;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            TileZone near = calc.getZone(zone.x + dx, zone.y + dy);
            if (near?.tiles == null || near.id < 0 || near.id >= _zoneMark.Length || _zoneMark[near.id] == stamp) continue;
            _zoneMark[near.id] = stamp;
            int[] ids = PixelIds(near);
            for (int i = 0; i < ids.Length; i++)
            {
                int pixel = ids[i];
                if (pixel < 0 || !_hasBase[pixel] || pixels[pixel].a == 0) continue;
                pixels[pixel] = Lifted(pixel);
                changed = true;
            }
        }
        return changed;
    }

    // 按地块编号记轮次(代替每轮新建 HashSet)
    private static int[] _zoneMark = Array.Empty<int>();
    private static int _markStamp;

    private static int NextMark(ZoneCalculator calc)
    {
        int count = calc.zones?.Count ?? 0;
        if (_zoneMark.Length < count) _zoneMark = new int[count];
        if (_markStamp == int.MaxValue)
        {
            Array.Clear(_zoneMark, 0, _zoneMark.Length);
            _markStamp = 0;
        }
        return ++_markStamp;
    }

    // 每块地各格的像素编号(按 zone.id 存，第一次用到时建；地块的格子数组换了就重建)：
    // 逐格循环里不再每格 tiles[i].data.tile_id 两次间接取值
    private static int[][] _pixelIds = Array.Empty<int[]>();
    private static WorldTile[][] _pixelIdsOf = Array.Empty<WorldTile[]>();

    private static int[] PixelIds(TileZone zone)
    {
        WorldTile[] tiles = zone.tiles;
        int index = zone.id;
        if (index < 0) return BuildPixelIds(tiles);
        if (index >= _pixelIds.Length)
        {
            int size = Mathf.Max(index + 1, World.world?.zone_calculator?.zones?.Count ?? 0);
            Array.Resize(ref _pixelIds, size);
            Array.Resize(ref _pixelIdsOf, size);
        }
        int[] ids = _pixelIds[index];
        if (ids != null && ReferenceEquals(_pixelIdsOf[index], tiles)) return ids;
        ids = BuildPixelIds(tiles);
        _pixelIds[index] = ids;
        _pixelIdsOf[index] = tiles;
        return ids;
    }

    private static int[] BuildPixelIds(WorldTile[] tiles)
    {
        var ids = new int[tiles.Length];
        for (int i = 0; i < tiles.Length; i++) ids[i] = tiles[i]?.data?.tile_id ?? -1;
        return ids;
    }

    #endregion

    #region 上色

    // 这块地连外面一圈(10×10 格)的归属，画时查表，不对每格和它的四邻反复计算
    private const int Window = ZoneSize + 2;
    private static readonly IMetaObject[] Owners = new IMetaObject[Window * Window];
    private static int _ox, _oy;

    private static IMetaObject OwnerCached(int x, int y)
    {
        int gx = x - _ox, gy = y - _oy;
        return gx is >= 0 and < Window && gy is >= 0 and < Window ? Owners[gy * Window + gx] : OwnerAt(x, y);
    }

    private static void EnsurePixelState(Color32[] pixels)
    {
        if (pixels == null || _outline.Length == pixels.Length) return;
        _outline = new bool[pixels.Length];
        _foreign = new bool[pixels.Length];
        _owner = new IMetaObject[pixels.Length];
        _base = new Color32[pixels.Length];
        _hasBase = new bool[pixels.Length];
    }

    private static bool UniformWindow()
    {
        // 国家按城市轮廓映射，外圈也可能有另一座城市；检查整个采样窗口才能安全跳过几何计算。
        if (_controlGeometry)
        {
            for (int i = 0; i < 25; i++)
                if (!ReferenceEquals(Shades[i], Shades[12]) || PushMetas[i] != null ||
                    Shades[12] != null && CityMetas[i] == null) return false;
            return true;
        }
        for (int dy = 1; dy <= 3; dy++)
        for (int dx = 1; dx <= 3; dx++)
        {
            int index = dy * 5 + dx;
            if (!ReferenceEquals(Shades[index], Shades[12]) || PushMetas[index] != null) return false;
        }
        return true;
    }

    // 画这块地；返回 false 时(图层不支持、地图未就绪)照原版画
    public static bool Paint(ZoneCalculator calc, TileZone zone, Color32 inside, Color32 outside)
    {
        if (!Enabled || zone?.tiles == null || calc?.pixels == null || World.world == null) return false;
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        if (layer == null || !Fill(layer, zone)) return false;
        // 同样的上色输入画过：直接拷回(见"上色缓存")
        TrackNative(zone, LayerMeta(layer, layer.getZoneOptionState(), zone), inside, outside, ShadeKey());
        CaptureLayerFlip(zone);
        long key = KeyOf(layer, zone, inside, outside);
        if (key != 0L && Restore(calc.pixels, zone, key))
        {
            ApplyTransition(calc.pixels, zone);
            return true;
        }
        _pendingKey = key;
        _ox = zone.x * ZoneSize - 1;
        _oy = zone.y * ZoneSize - 1;
        // 整个采样窗口同属一方、没有逐格推进：平滑不会改变国色，也画不出边框，跳过逐格计算。
        bool uniform = UniformWindow();
        for (int gy = 0; gy < Window; gy++)
        for (int gx = 0; gx < Window; gx++)
            Owners[gy * Window + gx] = uniform ? IsWater(_ox + gx, _oy + gy) ? null : Shades[12] : OwnerAt(_ox + gx, _oy + gy);
        Color32[] pixels = calc.pixels;
        EnsurePixelState(pixels);
        // 本块的归属、原版这次给本块的颜色是按谁画的(悬停的省按本省)
        IMetaObject own = Shades[12];
        IMetaObject native = ShadeOf(zone, LayerMeta(layer, layer.getZoneOptionState(), zone));
        Remember(zone, own);
        bool clearInside = inside.a == 0;
        bool goldLayer = layer.map_mode == MetaType.Kingdom;
        bool frontLayer = CorpsSystem.Enabled && (layer.map_mode == MetaType.Kingdom || layer.map_mode == MetaTypeExtension.Empire);
        Hostility.Clear();
        int width = MapBox.width, height = MapBox.height;
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile.data.tile_id;
            int x = tile.x, y = tile.y;
            IMetaObject owner = OwnerCached(x, y);
            bool foreign = !ReferenceEquals(owner, own) && !(ReferenceEquals(owner, _hover) && ReferenceEquals(Metas[12], _hoverRealm)) &&
                           !(ReferenceEquals(own, _hover) && ReferenceEquals(owner, _hoverRealm));
            bool edge = !uniform && owner != null &&
                        (Differs(owner, x + 1, y, width, height) || Differs(owner, x - 1, y, width, height) ||
                         Differs(owner, x, y + 1, width, height) || Differs(owner, x, y - 1, width, height));
            _foreign[id] = foreign;
            _outline[id] = edge;
            _owner[id] = owner;
            Color32 colorInside = inside, colorOutside = outside;
            if (!ReferenceEquals(owner, native)) ColorsOf(owner, clearInside, ref colorInside, ref colorOutside);
            Color32 color = edge ? colorOutside : colorInside;
            if (owner == null && !foreign) color = new Color32(0, 0, 0, 0);
            // 战线：边框紧挨着交战敌国的领土
            if (edge && frontLayer && (AtWar(owner, x + 1, y, width, height) || AtWar(owner, x - 1, y, width, height) ||
                                       AtWar(owner, x, y + 1, width, height) || AtWar(owner, x, y - 1, width, height)))
            {
                pixels[id] = FrontRed;
                continue;
            }
            // 纯净地图的国家图层：帝国与外界的边界描金(原版只沿地块边描，见 PureMapPatch.AfterApplyColors)
            if (edge && goldLayer && EmpireOf(owner) is Empire empire &&
                (EmpireDiffers(empire, x + 1, y, width, height) || EmpireDiffers(empire, x - 1, y, width, height) ||
                 EmpireDiffers(empire, x, y + 1, width, height) || EmpireDiffers(empire, x, y - 1, width, height)))
                color = EmpireGold;
            pixels[id] = color;
        }
        _paintedZone = zone.id;
        calc.setDirty();
        return true;
    }

    // 这块地画完(地形调色、城市平面图之后)：记下底色，再按当前高亮提亮；不是这里逐像素画的(原版画的)就作废底色
    public static void Finish(Color32[] pixels, TileZone zone)
    {
        if (Enabled && zone?.tiles != null && pixels != null && _paintedZone != zone.id &&
            Transitions.TryGetValue(zone.id, out Transition raw) && raw.raw)
            ApplyRaw(pixels, zone, raw);
        if (zone?.tiles == null || pixels == null || pixels.Length != _base.Length) return;
        int[] ids = PixelIds(zone);
        if (!Enabled || _paintedZone != zone.id)
        {
            foreach (int id in ids)
                if (id >= 0) _hasBase[id] = false;
            return;
        }
        _paintedZone = -1;
        BeginLift();
        foreach (int id in ids)
        {
            if (id < 0) continue;
            _base[id] = pixels[id];
            _hasBase[id] = true;
            pixels[id] = Lifted(id);
        }
        if (_pendingKey != 0L) Store(zone, _pendingKey);
        ApplyTransition(pixels, zone);
        _pendingKey = 0L;
    }

    // 悬停、选中的对象变了：属于新旧对象的格子按记下的底色重新提亮或还原
    // (整张图逐格扫，帧率体检里藏着 35~50 毫秒的尖峰：相邻的格大多同一归属，连成串只判一次是否命中)
    public static void Retint(params object[] metas)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        Color32[] pixels = calc?.pixels;
        if (!Enabled || pixels == null || pixels.Length != _base.Length) return;
        using var timing = FrameProfiler.Measure("平滑国界·高亮重算");
        ResetLit();
        BeginLift();
        bool changed = false;
        IMetaObject runOwner = null;
        bool runHit = false;
        for (int id = 0; id < pixels.Length; id++)
        {
            IMetaObject owner = _owner[id];
            if (owner == null || !_hasBase[id] || pixels[id].a == 0) continue;
            if (!ReferenceEquals(owner, runOwner))
            {
                runOwner = owner;
                IMetaObject realm = ReferenceEquals(owner, _hover) && _hoverRealm != null ? _hoverRealm : owner;
                runHit = false;
                foreach (object meta in metas)
                    if (meta != null && (ReferenceEquals(meta, owner) || ReferenceEquals(meta, realm)))
                    {
                        runHit = true;
                        break;
                    }
            }
            if (!runHit) continue;
            pixels[id] = Lifted(id);
            changed = true;
        }
        if (changed) calc.setDirty();
    }

    #region 换色渐变

    // 一块地的样子一下子变了：不当帧整块换，先记下它原来的样子，新样子照常画一次，然后这块地像一张牌一样原地翻过去
    // (用户 2026-10-16 看过演示版定下："翻转方向从即将要转移的势力那边延伸，如果是自己变就从中间")：
    // 翻到一半之前牌面是旧样子，过了一半是新样子；牌画在色块上面一层(见 ZoneFlipOverlay)，翻的时候底下的色块压暗(牌翘起来的缝)。
    // 何时翻、朝哪边翻：
    //   · 换主(城换了主人、城市归降、并入帝国)：从新主人那边(宗主或同一帝国那边)一圈圈翻过来，
    //     每块牌挨着新主人的那一边先翘起来，每圈约 SweepStep 秒，整片至多 MaxSweepSeconds 秒；
    //   · 自己变(归属没换、只是颜色变了，如国家换颜色、成了附庸换色)，或换主时接不上新主人：从都城那块地往外一圈圈翻；
    //   · 临时换色(帝国图层悬停某省显示本省颜色、移开换回)：要跟手——从鼠标处很快地翻过去(每秒 RippleSpeed 格，
    //     每张牌 HoverFlipSeconds 秒)，这个省连同四周的地块上色时优先；没翻完就移开，从眼下看到的那一面翻回去；
    //   · 换图层(国家、文化、宗教……或图层选项)：先算齐再一起翻——旧图留着，镜头里的地块分帧把新图层全部画好
    //     (这期间镜头外的不画，算力都给镜头里，见 ZonePaintBudget)，画齐了才从镜头中心一道波整体翻开；镜头外的随后补画，直接换。
    // 一次换主：头一块发现变了就把镜头里同一批变了的地块一起找出来(见 ScanView)，记下旧样子、优先重画，
    // 等这批都画好(至多 MaxPlanWait 秒)再统一排；之后才画到的块接着这道往下排。挨着换主地块的邻块(平滑的边伸进它们里面)
    // 跟着那块翻到一半时一起换(Follow)，新颜色不先从边上渗出来。战线不翻：被敌军占着、收复(只是谁占着变了)直接换，
    // 逐格推进吃下的地(FrontSystem.Smooth)本身就是渐变，也直接显示。新旧一样的块不翻。
    private const float SweepStep = 0.18f, MaxSweepSeconds = 4f, RippleSpeed = 600f, RippleGroupSeconds = 0.15f, HoverFlipSeconds = 0.18f,
        StaleSeconds = 6f, MaxPlanWait = 1f, FlipSeconds = 0.32f, SlotShade = 0.55f;

    private sealed class ShownState
    {
        public IMetaObject meta;
        public int colorKey;
        // 不算占领的归属(城属于谁、并入哪个帝国)与它的颜色：只有这个变了才翻牌，战线上被占、收复直接换
        public IMetaObject owner;
        public int ownerKey;
    }

    // 这块地眼下的样子(实际控制方)与不算占领的归属
    private static ShownState StateOf(MetaTypeAsset layer, int option, TileZone zone, IMetaObject real)
    {
        IMetaObject owner = LayerMeta(layer, option, zone);
        return new ShownState
        {
            meta = real, colorKey = ColorKey(SafeColor(real)), owner = owner,
            ownerKey = ReferenceEquals(owner, real) ? ColorKey(SafeColor(real)) : ColorKey(SafeColor(owner))
        };
    }

    // 纯粹的政权变更、整体变色(城换了主、并入帝国、国家换颜色……)：不算占领的归属或它的颜色变了。
    // 战线上被占、收复只是谁占着变了，不翻牌(用户 2026-10-16："只有帝国变色和城市归属等纯粹的政权变更和整体变色才有翻转动画，而战线没有")
    private static bool PoliticalChange(ShownState before, ShownState after) =>
        !ReferenceEquals(before.owner, after.owner) || before.ownerKey != after.ownerKey;

    private sealed class Transition
    {
        public Color32[] oldPixels, newPixels;
        // 排好了何时翻；归属没换、只是颜色变了(自己变)
        public bool planned, self;
        public float created;
        // 何时开始翻、翻多久；绕哪条轴(上下翻)；哪一边先翘起来(+1 坐标大的那边)
        public float flipAt, lift = 1f, duration = FlipSeconds;
        public bool vertical;
        // 画到哪一步：0 旧样子，1 翻前半(底下压暗的旧样子)，2 翻后半(底下压暗的新样子)，3 翻完
        public int phase = -1;
        // 这块地是原版画的(没有平滑国界的底色)：翻的时候直接写色块
        public bool raw;
        // 跟着邻块换(挨着换主的地块，只是平滑的边变了)：不画牌，到 flipAt(邻块翻到一半)直接换成新样子
        public bool follow;
    }

    private static readonly Dictionary<int, ShownState> Shown = new();
    private static readonly Dictionary<int, (int inside, int outside, int meta, int shade)> NativeShown = new();
    private static readonly HashSet<int> PendingSweep = new();
    // 等着跟邻块一起换的地块(见 Follow)
    private static readonly HashSet<int> PendingFollow = new();
    private static readonly Dictionary<int, Transition> Transitions = new();

    // 正在扫的一道扫描(按变成谁分)：起扫时刻、每块用时、各块排第几圈
    private sealed class SweepPlan
    {
        public float start, step, end;
        public readonly Dictionary<int, int> distances = new();
    }

    private static readonly Dictionary<(IMetaObject meta, int color), SweepPlan> Plans = new();
    private static readonly List<(IMetaObject meta, int color)> ExpiredPlans = new();
    private static float _pendingSince;
    private static MetaTypeAsset _sweepLayer;
    private static object _transitionWorld;
    private static int _sweepOption = int.MinValue;
    private static float _rippleStart = -10f;
    private static int _rippleX, _rippleY;

    private static int ColorKey(ColorAsset color)
    {
        if (color == null) return 0;
        Color32 a = color.getColorBorderInsideAlpha32(), b = color.getColorMainSecond32();
        unchecked
        {
            return ((a.r << 24) | (a.g << 16) | (a.b << 8) | a.a) * 31 + ((b.r << 24) | (b.g << 16) | (b.b << 8) | b.a);
        }
    }

    private static int Rgb(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;

    private static void ResetTransitions()
    {
        Shown.Clear();
        NativeShown.Clear();
        PendingSweep.Clear();
        PendingFollow.Clear();
        Transitions.Clear();
        Plans.Clear();
        _scannedAt = -10f;
        _layerFlip = null;
    }

    // 新世界也可能复用同一个 MapBox；读档/重建时显式清掉旧画面与未结束的换色。
    public static void ResetWorldState()
    {
        ResetTransitions();
        _transitionWorld = null;
        _sweepLayer = null;
        _sweepOption = int.MinValue;
        LastMeta.Clear();
        Snapshots.Clear();
        Array.Clear(_hasBase, 0, _hasBase.Length);
        Array.Clear(_owner, 0, _owner.Length);
        Array.Clear(_outline, 0, _outline.Length);
        Array.Clear(_foreign, 0, _foreign.Length);
        _pixelIds = Array.Empty<int[]>();
        _pixelIdsOf = Array.Empty<WorldTile[]>();
        _zoneMark = Array.Empty<int>();
        _paintedZone = _restoredZone = -1;
        _pendingKey = 0L;
        _entryPending = true;
        ZoneFlipOverlay.Hide();
    }

    // 真实归属；顺带看这块地换主了没有，换了就记下它原来的样子、排进扫描
    private static IMetaObject MetaOf(MetaTypeAsset layer, int option, TileZone zone)
    {
        IMetaObject real = RealMetaOf(layer, option, zone);
        if (zone == null || !Enabled) return real;
        CheckLayer(layer, option);
        int realKey = ColorKey(SafeColor(real));
        if (Shown.TryGetValue(zone.id, out ShownState shown) && ReferenceEquals(shown.meta, real) && shown.colorKey == realKey)
            return real;
        bool known = shown != null;
        ShownState state = StateOf(layer, option, zone, real);
        Shown[zone.id] = state;
        if (!known || !PoliticalChange(shown, state)) return real;
        Queue(zone, zone.x != _cx || zone.y != _cy, ReferenceEquals(shown.owner, state.owner));
        ScanView(layer, option);
        return real;
    }

    // 换了图层(或图层选项)：之前的换色作废，整张图翻牌换成新图层
    private static void CheckLayer(MetaTypeAsset layer, int option)
    {
        if (ReferenceEquals(layer, _sweepLayer) && option == _sweepOption) return;
        bool first = _sweepLayer == null;
        _sweepLayer = layer;
        _sweepOption = option;
        if (!first)
        {
            RepaintAll("换图层", invalidate: false);
            return;
        }
        ResetTransitions();
        LastMeta.Clear();
    }

    // 在原版决定清图之前统一观察图层，所有模式都走同一次全图换色，不等某块地开始上色才发现切换。
    public static bool PrepareLayerSwitch(ZoneCalculator calc)
    {
        if (!Enabled || calc?.pixels == null || World.world == null) return false;
        EnsurePixelState(calc.pixels);
        if (!ReferenceEquals(_transitionWorld, World.world))
        {
            ResetWorldState();
            _transitionWorld = World.world;
        }
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        if (layer == null)
        {
            if (_sweepLayer != null) ResetTransitions();
            _sweepLayer = null;
            _sweepOption = int.MinValue;
            return false;
        }
        int option = layer.getZoneOptionState();
        bool changed = _sweepLayer != null && (!ReferenceEquals(layer, _sweepLayer) || option != _sweepOption);
        CheckLayer(layer, option);
        return changed;
    }

    // 原版画法的图层(没有逐格归属的，如教化、政令这类热力图层)：上色前记下旧样子，画完(Finish)再翻
    public static void CaptureRaw(TileZone zone)
    {
        if (!Enabled || zone?.tiles == null || World.world == null) return;
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        if (layer == null) return;
        EnsurePixelState(World.world.zone_calculator?.pixels);
        CheckLayer(layer, layer.getZoneOptionState());
        Transition transition = CaptureLayerFlip(zone);
        if (transition != null) transition.raw = true;
    }

    // 记下这块地原来的样子、排进扫描。不是正在画的这块(画邻块时顺带发现它变了)：让原版下一轮重画它，
    // 否则它等不到新样子，渐变作废后一直留着旧颜色。
    // 镜头外(外扩一块)的地块不翻(同换图层)：看不见，直接画成新样子——大国整片换色时不必给几千块地记旧样子、逐帧写翻牌
    private static void Queue(TileZone zone, bool repaint, bool self)
    {
        if (!InFlipView(zone))
        {
            Transitions.Remove(zone.id);
            PendingSweep.Remove(zone.id);
            if (repaint) zone.resetRenderHelpers();
            return;
        }
        Transition transition = Capture(zone);
        if (transition == null) return;
        transition.self = self;
        Transitions[zone.id] = transition;
        PendingFollow.Remove(zone.id);
        if (PendingSweep.Count == 0 && PendingFollow.Count == 0) _pendingSince = Time.unscaledTime;
        PendingSweep.Add(zone.id);
        if (repaint) zone.resetRenderHelpers();
        Follow(zone);
    }

    // 挨着换主地块的邻块：一格归谁看周围 3×3 块，换主那块的新主人会伸进邻块的边上。邻块一重画(见 Remember)，
    // 新颜色就先从边上渗出来、国界先挪了位置，牌却还没翻(大片换色一道扫描要几秒)。邻块也先记下旧样子，
    // 等挨着的换主地块翻到一半时直接换成新样子(不画牌)；新旧一样的(内陆的邻块)画好时就丢掉
    private static void Follow(TileZone zone)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            TileZone near = calc.getZone(zone.x + dx, zone.y + dy);
            // 只跟画过的块(原版不画的空地、海上不归谁的块不会重画，等不到新样子)
            if (near == null || Transitions.ContainsKey(near.id) || !LastMeta.ContainsKey(near.id) || !InFlipView(near)) continue;
            Transition transition = Capture(near);
            if (transition == null) continue;
            transition.follow = true;
            Transitions[near.id] = transition;
            PendingFollow.Add(near.id);
            near.resetRenderHelpers();
        }
    }

    private static int _flipViewFrame = -1;
    private static Rect _flipView;

    // 镜头外扩一块地(格坐标，每帧取一次镜头)
    private static Rect FlipView()
    {
        int frame = Time.frameCount;
        if (_flipViewFrame == frame) return _flipView;
        _flipViewFrame = frame;
        Rect view = ZoneFlipOverlay.CameraView();
        _flipView = Rect.MinMaxRect(view.xMin - ZoneSize, view.yMin - ZoneSize, view.xMax + ZoneSize, view.yMax + ZoneSize);
        return _flipView;
    }

    // 这块地在镜头里(镜头外扩一块地)
    private static bool InFlipView(TileZone zone)
    {
        Rect view = FlipView();
        float x0 = zone.x * ZoneSize, y0 = zone.y * ZoneSize;
        return x0 + ZoneSize >= view.xMin && x0 <= view.xMax && y0 + ZoneSize >= view.yMin && y0 <= view.yMax;
    }

    // 一次政权变更往往连带一大片地块(帝国解散、成员离开、并入帝国、国家换颜色……)，原版却按国、按城一块块重画，
    // 画到哪块才发现哪块变了，看起来零零碎碎地翻(用户 2026-10-21 "新旧颜色东一块西一块")。头一块发现变了，
    // 就把镜头里(外扩一块)所有归属或颜色变了的地块一起找出来：记下旧样子、让它们优先重画(见 Awaiting)，整批画好再统一排(见 PlanSweeps)。
    // 以前只查同一国(涉及帝国查帝国成员)的城，帝国解散后找不到原来的成员，成员各自零零碎碎地翻。
    // 只比记下的样子与眼下的实际归属，不画(同一座城的地块只取一次归属)；镜头外的看不见，照常按重画指纹重画、直接换。
    // 镜头拉远时一次要比几千块，ScanCooldown 秒内只扫一次(之内发现的变化自己排进来，或等下一次扫)
    private const float ScanCooldown = 0.2f;
    private static float _scannedAt = -10f;
    private static readonly Dictionary<City, IMetaObject> ScanCityMetas = new();

    private static void ScanView(MetaTypeAsset layer, int option)
    {
        float now = Time.unscaledTime;
        if (now >= _scannedAt && now - _scannedAt < ScanCooldown) return;
        _scannedAt = now;
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null || layer == null) return;
        using var timing = FrameProfiler.Measure("平滑国界·换主找齐");
        Rect view = FlipView();
        int x0 = Mathf.Max(0, Mathf.FloorToInt(view.xMin / ZoneSize)), x1 = Mathf.Min(calc.zones_total_x - 1, Mathf.FloorToInt(view.xMax / ZoneSize));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(view.yMin / ZoneSize)), y1 = Mathf.Min(calc.zones_total_y - 1, Mathf.FloorToInt(view.yMax / ZoneSize));
        bool byCity = CityKeyed(layer, option), control = ControlLayer(layer);
        ScanCityMetas.Clear();
        IMetaObject lastMeta = null;
        int lastKey = 0;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            TileZone zone = calc.getZone(x, y);
            // 正在画的这块自己会查(见 MetaOf)，这里再让它重画一遍就白画了
            // 没画过的块(原版不画的空地等)屏幕上没有东西可翻，等它画的时候自己查
            if (zone == null || x == _cx && y == _cy || PendingSweep.Contains(zone.id) || !LastMeta.ContainsKey(zone.id) ||
                !Shown.TryGetValue(zone.id, out ShownState shown)) continue;
            City city = zone.city;
            IMetaObject real;
            if (byCity && city != null && !(control && city.kingdom != null && city.HasOccupiedZones()))
            {
                if (!ScanCityMetas.TryGetValue(city, out real)) ScanCityMetas[city] = real = RealMetaOf(layer, option, zone);
            }
            else real = RealMetaOf(layer, option, zone);
            if (!ReferenceEquals(real, lastMeta) || lastMeta == null)
            {
                lastMeta = real;
                lastKey = ColorKey(SafeColor(real));
            }
            if (ReferenceEquals(shown.meta, real) && shown.colorKey == lastKey) continue;
            ShownState state = StateOf(layer, option, zone, real);
            Shown[zone.id] = state;
            // 只是战线上被占、收复：不翻，原版重画时直接换
            if (!PoliticalChange(shown, state)) continue;
            Queue(zone, true, ReferenceEquals(shown.owner, state.owner));
        }
        ScanCityMetas.Clear();
    }

    // 原版把这块地清空了(不再归任何对象，如城没了)：忘掉记下的底色、渐变与缓存，免得之后高亮、渐变又把旧颜色写回去
    public static void Cleared(TileZone zone)
    {
        if (zone?.tiles == null) return;
        // 新图层不覆盖的地块也参与同一道全图换色；清理旧归属时不能把这段退场动画一并删掉。
        bool layerClear = _layerFlip?.seen.Contains(zone.id) == true &&
                          Transitions.TryGetValue(zone.id, out Transition clearing) && clearing.raw && clearing.newPixels != null;
        foreach (int id in PixelIds(zone))
        {
            if (id < 0 || id >= _hasBase.Length) continue;
            _hasBase[id] = false;
            _owner[id] = null;
        }
        if (!layerClear) Transitions.Remove(zone.id);
        PendingSweep.Remove(zone.id);
        PendingFollow.Remove(zone.id);
        if (!layerClear) _layerFlip?.seen.Remove(zone.id);
        Shown.Remove(zone.id);
        NativeShown.Remove(zone.id);
        LastMeta.Remove(zone.id);
        Snapshots.Remove(zone.id);
    }

    // 本色变了而归属的颜色没变(=临时换色，如悬停某省)：从鼠标处起一圈波纹
    private static void TrackNative(TileZone zone, IMetaObject meta, Color32 inside, Color32 outside, int shade)
    {
        int metaKey = ColorKey(SafeColor(meta)) ^ (meta == null ? 0 : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(meta));
        var now = (Rgb(inside), Rgb(outside), metaKey, shade);
        bool known = NativeShown.TryGetValue(zone.id, out var last);
        NativeShown[zone.id] = now;
        if (!known || last.meta != metaKey || last.inside == now.Item1 && last.outside == now.Item2 && last.shade == shade) return;
        Transition transition = Capture(zone);
        if (transition == null) return;
        float time = Time.unscaledTime;
        if (time - _rippleStart > RippleGroupSeconds)
        {
            WorldTile mouse = World.world?.getMouseTilePosCachedFrame();
            _rippleStart = time;
            _rippleX = mouse?.x ?? zone.x * ZoneSize + ZoneSize / 2;
            _rippleY = mouse?.y ?? zone.y * ZoneSize + ZoneSize / 2;
        }
        float dx = zone.x * ZoneSize + ZoneSize / 2f - _rippleX, dy = zone.y * ZoneSize + ZoneSize / 2f - _rippleY;
        transition.planned = true;
        transition.flipAt = _rippleStart + Mathf.Sqrt(dx * dx + dy * dy) / RippleSpeed;
        transition.duration = HoverFlipSeconds;
        Orient(transition, dx, dy, zone);
        Transitions[zone.id] = transition;
        PendingSweep.Remove(zone.id);
        PendingFollow.Remove(zone.id);
    }

    // 周围 3×3 块显示成谁(悬停的省)
    private static int ShadeKey()
    {
        unchecked
        {
            int key = 17;
            for (int dy = 1; dy <= 3; dy++)
            for (int dx = 1; dx <= 3; dx++)
            {
                IMetaObject meta = Shades[dy * 5 + dx];
                key = key * 31 + (meta == null ? 0 : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(meta));
            }
            return key;
        }
    }

    // 记下这块地眼下画着的样子(还没画过的块不做渐变)
    private static Transition Capture(TileZone zone)
    {
        if (zone?.tiles == null) return null;
        if (Transitions.TryGetValue(zone.id, out Transition running) && running.oldPixels?.Length == zone.tiles.Length)
        {
            // 正在翻(或等着翻)的块又要变：旧样子是眼下看到的那一面，不是底下压暗的缝
            Color32[] face = running.phase >= 2 && running.newPixels != null ? running.newPixels : running.oldPixels;
            return new Transition { oldPixels = (Color32[])face.Clone(), created = Time.unscaledTime };
        }
        Color32[] shown = World.world?.zone_calculator?.pixels;
        int[] ids = PixelIds(zone);
        var old = new Color32[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            if (id >= 0 && id < _base.Length && _hasBase[id]) old[i] = _base[id];
            else if (shown != null && id >= 0 && id < shown.Length) old[i] = shown[id];
            else return null;
        }
        return new Transition { oldPixels = old, created = Time.unscaledTime };
    }

    // 逐格推进吃下的块：推进本身已经是渐变，换主时直接显示。已经画好新样子、还在按旧样子显示的(等着扫描)，
    // 当场换成新样子——以前直接丢掉渐变，这块地就一直留着旧颜色(它已经画过，原版不会再重画)
    public static void Settle(TileZone zone)
    {
        MetaTypeAsset layer = World.world?.getCachedMapMetaAsset();
        // 战线推进只处理国家/帝国的显示；不能撤销城市图层的状态或写回别的图层留下的颜色。
        if (zone == null || layer == null || !ControlLayer(layer) || !ReferenceEquals(layer, _sweepLayer)) return;
        Shown.Remove(zone.id);
        PendingSweep.Remove(zone.id);
        PendingFollow.Remove(zone.id);
        if (!Transitions.TryGetValue(zone.id, out Transition transition)) return;
        Transitions.Remove(zone.id);
        ZoneCalculator calc = World.world?.zone_calculator;
        Color32[] pixels = calc?.pixels;
        if (transition.newPixels == null || zone.tiles == null || pixels == null || pixels.Length != _base.Length ||
            transition.newPixels.Length != zone.tiles.Length) return;
        int[] ids = PixelIds(zone);
        BeginLift();
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            if (id < 0) continue;
            _base[id] = transition.newPixels[i];
            pixels[id] = Lifted(id);
        }
        calc.setDirty();
    }

    // 朝哪边翻：绕和来向垂直的轴翻，挨着来向的那一边先翘起来；没有来向(从块心起)随块定
    private static void Orient(Transition transition, float dirX, float dirY, TileZone zone)
    {
        if (Mathf.Abs(dirX) < 0.01f && Mathf.Abs(dirY) < 0.01f)
        {
            transition.vertical = (zone.x + zone.y) % 2 == 0;
            transition.lift = zone.x % 2 == 0 ? 1f : -1f;
            return;
        }
        transition.vertical = Mathf.Abs(dirY) > Mathf.Abs(dirX);
        transition.lift = -Mathf.Sign(transition.vertical ? dirY : dirX);
    }

    private static Color32 Slot(Color32 color) =>
        new((byte)(color.r * SlotShade), (byte)(color.g * SlotShade), (byte)(color.b * SlotShade), color.a);

    // 原版画法的块刚画好：新样子取色块上的颜色
    private static void ApplyRaw(Color32[] pixels, TileZone zone, Transition transition)
    {
        int[] ids = PixelIds(zone);
        var fresh = new Color32[ids.Length];
        bool same = transition.oldPixels.Length == fresh.Length;
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            fresh[i] = id >= 0 && id < pixels.Length ? pixels[id] : default;
            if (!same) continue;
            Color32 a = fresh[i], b = transition.oldPixels[i];
            same = a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }
        if (same)
        {
            Transitions.Remove(zone.id);
            return;
        }
        transition.newPixels = fresh;
        transition.phase = -1;
        WriteFlip(pixels, zone, transition, Time.unscaledTime);
    }

    // 这块地刚画好新样子：记下来；新旧一样就不翻
    private static void ApplyTransition(Color32[] pixels, TileZone zone)
    {
        if (!Transitions.TryGetValue(zone.id, out Transition transition)) return;
        int[] ids = PixelIds(zone);
        var fresh = new Color32[ids.Length];
        bool same = transition.oldPixels.Length == fresh.Length;
        for (int i = 0; i < ids.Length; i++)
        {
            fresh[i] = _base[ids[i]];
            if (same)
            {
                Color32 a = fresh[i], b = transition.oldPixels[i];
                same = a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            }
        }
        if (same)
        {
            Transitions.Remove(zone.id);
            PendingSweep.Remove(zone.id);
            PendingFollow.Remove(zone.id);
            return;
        }
        transition.newPixels = fresh;
        transition.phase = -1;
        WriteFlip(pixels, zone, transition, Time.unscaledTime);
    }

    // 按翻到哪一步写底下的色块：没翻是旧样子，翻着是压暗的旧(前半)、新(后半)样子，翻完是新样子；步子变了才写。
    // 跟着邻块换的(follow)不翻，到时候直接换成新样子
    private static bool WriteFlip(Color32[] pixels, TileZone zone, Transition transition, float now)
    {
        int phase = !transition.planned || transition.newPixels == null || now < transition.flipAt ? 0
            : transition.follow || now >= transition.flipAt + transition.duration ? 3
            : now < transition.flipAt + transition.duration / 2f ? 1 : 2;
        if (phase == transition.phase) return false;
        transition.phase = phase;
        Color32[] source = phase <= 1 ? transition.oldPixels : transition.newPixels;
        bool slot = phase is 1 or 2;
        int[] ids = PixelIds(zone);
        BeginLift();
        for (int i = 0; i < ids.Length && i < source.Length; i++)
        {
            int id = ids[i];
            if (id < 0 || id >= pixels.Length) continue;
            if (transition.raw || id >= _base.Length)
            {
                pixels[id] = slot ? Slot(source[i]) : source[i];
                continue;
            }
            _base[id] = slot ? Slot(source[i]) : source[i];
            pixels[id] = Lifted(id);
        }
        return true;
    }

    // 全图换色(换图层、初次进图都走 RepaintAll)：
    //   用户 2026-10-16："我切换图层的时候不能用全图翻转吗"；10-21："每次切换图层的时候都先检测，然后一次性从中间也好、从左往右也好转换"；
    //   10-22："读取颜色这一步会有些迟钝"、"写个公共方法，每次切换层级只用调用这一个方法"、"初次进入地图也要来一遍"、
    //   "一次性算完颜色会造成卡顿"，选"边算边翻"。
    //   · 起翻：调用那一刻就从镜头中心起一道波(满速约 LayerRegionSeconds 秒到镜头边上)，旧图留着；
    //   · 边算：镜头里(外扩一块；算色期间镜头移到哪也并进来)的地块由近到远分帧画新样子——只画离波心不过"算色前沿"的，
    //     一轮里前沿以内全画完了前沿就往外推(推得越来越快)，预算用完就停在那里下一帧接着画(见 ZonePaintBudget)；
    //     原版巡检跟着模拟步走、不一定每帧一轮，算色期间没巡检的那一帧这里补跑一轮；镜头外的这时一概不画；
    //   · 那道波只推到已经算好的那一圈(前沿以内没有一块因预算延后)，算得快就是满速，算得慢就跟着算色走，不出现一块块乱翻；
    //   · 补画：镜头里算齐后镜头外的按放宽的预算补画、直接换(看不见)；补画期间进了镜头的块也翻。
    // 以前(10-21)先算齐再起翻：镜头里四千块要等原版巡检二十多轮，切到帝国图层空等两秒多。
    // 教化、政令这类原版画法的图层一样(见 CaptureRaw)。
    private const float LayerComputeMaxSeconds = 4f, LayerRegionSeconds = 0.2f, LayerBackfillMaxSeconds = 12f,
        LayerFlipMaxSeconds = 20f, FrontierStartZones = 3f, FrontierMaxZones = 5f;

    private sealed class LayerFlip
    {
        public string layer, reason;
        public float start, computedAt = -1f, backfilledAt = -1f, lastTick, lastPlanned;
        public int startFrame, startRound, rounds, computedFrame, late;
        // 要一起翻的范围(格)：调用时的镜头外扩一块，算色期间镜头移到哪并到哪
        public Rect view;
        public readonly HashSet<int> seen = new();
        // 记下了旧样子、等那道波推过来的块
        public readonly List<int> waiting = new();
        // 那道波：中心、满速(每秒几格)、眼下推到多远；算色前沿与每次外推多少、已算好的半径、范围里最远的一块有多远
        public float cx, cy, speed, wave, frontier, band, ready, reach;
        public bool Computing => computedAt < 0f;
        public bool Backfilling => computedAt >= 0f && backfilledAt < 0f;
    }

    private static LayerFlip _layerFlip;
    // 初次进图：读档完、地图画得出来时来一遍全图换色(见 FrameTick)
    private static bool _entryPending;

    // 还在算镜头里的颜色(镜头里的由近到远画、镜头外的不画)；算齐后还在补画镜头外(见 ZonePaintBudget)
    public static bool LayerPrecomputing => Enabled && _layerFlip != null && _layerFlip.Computing;
    public static bool LayerBackfilling => Enabled && _layerFlip != null && _layerFlip.Backfilling;

    // 这块地在全图换色要一起翻的范围里(块心在内)
    public static bool InLayerView(TileZone zone)
    {
        LayerFlip flip = _layerFlip;
        return flip != null && zone != null &&
               flip.view.Contains(new Vector2(zone.x * ZoneSize + ZoneSize / 2f, zone.y * ZoneSize + ZoneSize / 2f));
    }

    // 算色期间这块地轮到了没有(离波心不过算色前沿)；distance 是它离波心多远
    public static bool LayerDue(TileZone zone, out float distance)
    {
        LayerFlip flip = _layerFlip;
        distance = 0f;
        if (flip == null || zone == null) return true;
        distance = WaveDistance(flip, zone);
        return distance <= flip.frontier;
    }

    private static float WaveDistance(LayerFlip flip, TileZone zone)
    {
        float dx = zone.x * ZoneSize + ZoneSize / 2f - flip.cx, dy = zone.y * ZoneSize + ZoneSize / 2f - flip.cy;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // 有地块等着画新样子好一起翻：原版不一定每帧巡检一轮，这期间每帧都巡检(见 ZonePaintBudget.BeforeRedraw、LayerTick)
    public static bool WantsEveryRound =>
        Enabled && (_layerFlip != null && (_layerFlip.Computing || _layerFlip.Backfilling) ||
                    Transitions.Count > 0 && (PendingSweep.Count > 0 || PendingFollow.Count > 0));

    // 全图换色：旧图留着，从镜头中心由近到远分帧算新颜色，一道波从中心推开、只推到已经算好的那一圈；镜头外的随后补画、直接换。
    // 换图层(CheckLayer)、初次进图(FrameTick)都调这一个；invalidate：让全图各块下一轮重画
    // (换图层时原版清图那一步已经让各块重画了，见 ZonePaintBudget.BeforeClearAll)
    public static void RepaintAll(string reason, bool invalidate = true)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        MetaTypeAsset layer = World.world?.getCachedMapMetaAsset();
        if (!Enabled || calc?.pixels == null || layer == null) return;
        EnsurePixelState(calc.pixels);
        ResetTransitions();
        // 整图都要重画：不再因为"本块归属变了"让四周八块跟着重画，否则每块要多画好几遍
        LastMeta.Clear();
        if (invalidate)
        {
            calc.setDrawnZonesDirty();
            foreach (TileZone zone in calc._current_drawn_zones) zone.resetRenderHelpers();
            calc.clearAllRedrawTimers();
        }
        float now = Time.unscaledTime;
        Rect camera = ZoneFlipOverlay.CameraView();
        var flip = new LayerFlip
        {
            layer = layer.id, reason = reason, start = now, lastTick = now, lastPlanned = now, startFrame = Time.frameCount,
            startRound = ZonePaintBudget.Round, view = FlipView(), cx = camera.center.x, cy = camera.center.y
        };
        flip.reach = Reach(flip);
        flip.speed = Mathf.Max(40f, flip.reach / LayerRegionSeconds);
        flip.band = flip.frontier = FrontierStartZones * ZoneSize;
        _layerFlip = flip;
        ZonePaintBudget.ResetLayerPending();
    }

    // 范围里离波心最远的一块有多远(范围的四角)
    private static float Reach(LayerFlip flip)
    {
        float dx = Mathf.Max(Mathf.Abs(flip.view.xMin - flip.cx), Mathf.Abs(flip.view.xMax - flip.cx));
        float dy = Mathf.Max(Mathf.Abs(flip.view.yMin - flip.cy), Mathf.Abs(flip.view.yMax - flip.cy));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // 全图换色后这块地头一次重画：记下旧样子，等那道波推过来；算齐后才进镜头(镜头在动)、头一次画上新样子的当场翻；不在范围里的直接换
    private static Transition CaptureLayerFlip(TileZone zone)
    {
        LayerFlip flip = _layerFlip;
        if (flip == null || Transitions.ContainsKey(zone.id)) return null;
        if (!InLayerView(zone) && !(!flip.Computing && zone.visible && ZonePaintBudget.FirstLayerPaint(zone))) return null;
        if (!flip.seen.Add(zone.id)) return null;
        Transition transition = Capture(zone);
        if (transition == null) return null;
        Transitions[zone.id] = transition;
        if (flip.Computing) flip.waiting.Add(zone.id);
        else
        {
            flip.late++;
            Schedule(transition, zone, flip.cx, flip.cy, flip.start, flip.speed, Time.unscaledTime);
        }
        return transition;
    }

    private static void Schedule(Transition transition, TileZone zone, float cx, float cy, float at, float speed, float now)
    {
        float dx = zone.x * ZoneSize + ZoneSize / 2f - cx, dy = zone.y * ZoneSize + ZoneSize / 2f - cy;
        transition.planned = true;
        transition.flipAt = Mathf.Max(now, at + Mathf.Sqrt(dx * dx + dy * dy) / speed + (FrontNoise(zone.x * 7, zone.y * 7) - 0.5f) * 0.06f);
        Orient(transition, dx, dy, zone);
    }

    // 原版巡检完一轮(见 ZonePaintBudget.AfterRedraw)：deferredInView 是这一轮前沿以内因预算没画的块(nearest 是其中离波心最近的)，
    // beyond 是范围里还在前沿以外、没轮到的块，pending 是换色以来延后过、还没画上新样子的块。
    // 前沿以内一块不差就把前沿往外推；范围里一块不差就是算齐了，转去补画镜头外
    internal static void LayerRoundEnd(int round, bool modeCurrent, int deferredInView, float nearest, int beyond, int pending)
    {
        LayerFlip flip = _layerFlip;
        if (!Enabled || flip == null || round <= flip.startRound || !modeCurrent) return;
        float now = Time.unscaledTime;
        if (flip.Computing)
        {
            flip.rounds++;
            // 算色期间镜头移到哪，那里也要画齐、一起翻
            Rect view = FlipView();
            flip.view = Rect.MinMaxRect(Mathf.Min(flip.view.xMin, view.xMin), Mathf.Min(flip.view.yMin, view.yMin),
                Mathf.Max(flip.view.xMax, view.xMax), Mathf.Max(flip.view.yMax, view.yMax));
            flip.reach = Reach(flip);
            if (deferredInView > 0)
            {
                // 预算用完了：离波心最近的那块没画的以内都算好了，前沿不动、下一帧接着画
                flip.ready = Mathf.Max(flip.ready, nearest - 0.01f);
                return;
            }
            flip.ready = Mathf.Max(flip.ready, flip.frontier);
            if (beyond > 0 && flip.frontier < flip.reach && now - flip.start < LayerComputeMaxSeconds)
            {
                // 前沿以内都画完了、预算还有富余：往外推，下一圈宽一些(至多 FrontierMaxZones 块：太厚的话一圈里没有远近之分，
                // 波要等整圈画完才动，一顿一顿的)
                flip.frontier += flip.band;
                flip.band = Mathf.Min(flip.band * 1.3f, FrontierMaxZones * ZoneSize);
                return;
            }
            Computed(flip, now, beyond);
            return;
        }
        if (!flip.Backfilling || pending > 0 && now - flip.computedAt < LayerBackfillMaxSeconds) return;
        flip.backfilledAt = now;
        string state = pending > 0 ? $"补画超过 {LayerBackfillMaxSeconds:0} 秒还剩 {pending} 块没画(照常排着画)" : "补画完";
        LogService.LogInfo($"[EmpireCraft][全图换色] {flip.layer}：镜头外{state}，算齐后 {Time.frameCount - flip.computedFrame} 帧、" +
                           $"{(now - flip.computedAt) * 1000f:0} 毫秒；算齐后才画到、在镜头里接着翻的 {flip.late} 块");
    }

    // 镜头里算齐了(或算满 LayerComputeMaxSeconds 秒)：波不再等算色，满速推完；没画好的块画好时接着翻
    private static void Computed(LayerFlip flip, float now, int unpainted)
    {
        flip.computedAt = now;
        flip.computedFrame = Time.frameCount;
        flip.ready = float.MaxValue;
        string ready = unpainted > 0 ? $"；算满 {LayerComputeMaxSeconds} 秒还有 {unpainted} 块没轮到(画好时接着翻)" : "";
        LogService.LogInfo($"[EmpireCraft][全图换色] {flip.layer}({flip.reason})：镜头里 {flip.seen.Count} 块边算边翻，算齐用了 " +
                           $"{Time.frameCount - flip.startFrame} 帧、巡检 {flip.rounds} 轮、{(now - flip.start) * 1000f:0} 毫秒{ready}");
    }

    // 每帧：算色期间原版没巡检就补一轮；那道波往外推(不超过已算好的那一圈)，推过的、画好了的块翻；到时收尾
    private static void LayerTick(float now)
    {
        LayerFlip flip = _layerFlip;
        if (flip == null) return;
        ZoneCalculator calc = World.world?.zone_calculator;
        if (flip.Computing)
        {
            if (now - flip.start >= LayerComputeMaxSeconds + 0.5f) Computed(flip, now, 0);
            else if (calc != null && Config.game_loaded && !SmoothLoader.isLoading()) ZonePaintBudget.ForceRound(calc);
        }
        float dt = Mathf.Clamp(now - flip.lastTick, 0f, 0.1f);
        flip.lastTick = now;
        flip.wave = Mathf.Min(flip.wave + flip.speed * dt, flip.ready);
        if (flip.waiting.Count > 0 && calc != null)
        {
            for (int i = flip.waiting.Count - 1; i >= 0; i--)
            {
                int id = flip.waiting[i];
                TileZone zone = calc.getZoneByID(id);
                if (zone == null || !Transitions.TryGetValue(id, out Transition transition) || transition.planned)
                {
                    flip.waiting.RemoveAt(i);
                    continue;
                }
                if (transition.newPixels == null || WaveDistance(flip, zone) > flip.wave) continue;
                transition.planned = true;
                transition.flipAt = now + FrontNoise(zone.x * 7, zone.y * 7) * 0.04f;
                Orient(transition, zone.x * ZoneSize + ZoneSize / 2f - flip.cx, zone.y * ZoneSize + ZoneSize / 2f - flip.cy, zone);
                flip.waiting.RemoveAt(i);
                flip.lastPlanned = now;
            }
        }
        // 补画完、那道波也翻完了就收掉(之后的重画不再当成换图层来翻)
        if (now - flip.start >= LayerFlipMaxSeconds ||
            flip.backfilledAt >= 0f && flip.waiting.Count == 0 && now - flip.lastPlanned > FlipSeconds + 0.5f) _layerFlip = null;
    }

    // 每帧：给新换主的块排好何时翻；正在翻的块画成牌、底下压暗，翻完的收掉
    private static void SweepTick()
    {
        if (!Enabled || World.world?.zone_calculator == null)
        {
            ZoneFlipOverlay.Hide();
            return;
        }
        float now = Time.unscaledTime;
        // 这次换色的地块都画好新样子(或等满 MaxPlanWait)再统一排：没画好的地块上色时优先(见 Awaiting)，
        // 排好前仍显示旧样子，整片一起翻过去，不剩几块等一下才变
        if ((PendingSweep.Count > 0 || PendingFollow.Count > 0) && (!PendingUnpainted() || now - _pendingSince >= MaxPlanWait))
            PlanSweeps();
        LayerTick(now);
        if (Plans.Count > 0)
        {
            ExpiredPlans.Clear();
            foreach (KeyValuePair<(IMetaObject meta, int color), SweepPlan> pair in Plans)
                if (now > pair.Value.end) ExpiredPlans.Add(pair.Key);
            foreach ((IMetaObject, int) key in ExpiredPlans) Plans.Remove(key);
        }
        if (Transitions.Count == 0)
        {
            ZoneFlipOverlay.Hide(false);
            return;
        }
        ZoneCalculator calc = World.world.zone_calculator;
        Color32[] pixels = calc.pixels;
        if (pixels == null || pixels.Length != _base.Length) return;
        long started = global::System.Diagnostics.Stopwatch.GetTimestamp();
        List<int> done = null;
        bool wrote = false;
        ZoneFlipOverlay.Begin();
        foreach (KeyValuePair<int, Transition> pair in Transitions)
        {
            Transition transition = pair.Value;
            TileZone zone = calc.getZoneByID(pair.Key);
            if (zone?.tiles == null || transition.newPixels == null && now - transition.created > StaleSeconds)
            {
                // 一直没画到：作废，并让原版重画它(直接显示新样子)，不留旧颜色
                zone?.resetRenderHelpers();
                (done ??= new List<int>()).Add(pair.Key);
                continue;
            }
            // 画好了新样子却一直没排上翻(那一方没等到起翻就换了图层之类)：当场翻，不留旧样子
            if (!transition.planned && transition.newPixels != null && now - transition.created > StaleSeconds)
            {
                transition.planned = true;
                transition.flipAt = now;
            }
            if (WriteFlip(pixels, zone, transition, now)) wrote = true;
            if (transition.phase == 3)
            {
                (done ??= new List<int>()).Add(pair.Key);
                continue;
            }
            if (transition.phase is 1 or 2)
                ZoneFlipOverlay.Card(zone, transition.oldPixels, transition.newPixels, (now - transition.flipAt) / transition.duration,
                    transition.vertical, transition.lift);
        }
        ZoneFlipOverlay.End();
        if (done != null)
            foreach (int id in done) Transitions.Remove(id);
        if (wrote) calc.setDirty();
        FrameProfiler.Record("平滑国界·换色翻牌", global::System.Diagnostics.Stopwatch.GetTimestamp() - started);
    }

    private static bool PendingUnpainted()
    {
        foreach (int id in PendingSweep)
            if (Transitions.TryGetValue(id, out Transition transition) && transition.newPixels == null) return true;
        foreach (int id in PendingFollow)
            if (Transitions.TryGetValue(id, out Transition transition) && transition.newPixels == null) return true;
        return false;
    }

    // 这块地正在换色、还没画出新样子：上色时优先(见 ZonePaintBudget)
    public static bool Awaiting(TileZone zone) =>
        zone != null && Transitions.TryGetValue(zone.id, out Transition transition) && transition.newPixels == null;

    private static void PlanSweeps()
    {
        ZoneCalculator calc = World.world.zone_calculator;
        MetaTypeAsset layer = _sweepLayer;
        if (layer == null)
        {
            PendingSweep.Clear();
            PendingFollow.Clear();
            return;
        }
        // 按变成谁(新主人、新颜色)分组，各组从挨着新主人的块起一圈圈往里翻；整组都是自己变的从都城那块往外
        var groups = new Dictionary<(IMetaObject meta, int color), List<TileZone>>();
        var others = new HashSet<(IMetaObject meta, int color)>();
        foreach (int id in PendingSweep)
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone == null || !Transitions.ContainsKey(id)) continue;
            IMetaObject real = RealMetaOf(layer, _sweepOption, zone);
            var key = (real, ColorKey(SafeColor(real)));
            if (!groups.TryGetValue(key, out List<TileZone> list)) groups[key] = list = new List<TileZone>();
            list.Add(zone);
            if (!Transitions[id].self) others.Add(key);
        }
        PendingSweep.Clear();
        float now = Time.unscaledTime;
        foreach (KeyValuePair<(IMetaObject meta, int color), List<TileZone>> group in groups)
        {
            List<TileZone> zones = group.Value;
            var members = new HashSet<int>(zones.Select(zone => zone.id));
            // 同一次换主、晚几帧才画到的块：接着正在扫的那道往下排(扫过了的从眼下的前沿起)
            bool extend = Plans.TryGetValue(group.Key, out SweepPlan plan) && now <= plan.end;
            if (!extend) plan = new SweepPlan { start = now, step = SweepStep };
            int floor = extend ? Mathf.Max(0, Mathf.CeilToInt((now - plan.start) / plan.step)) : 0;
            var best = new Dictionary<int, (int distance, float dirX, float dirY)>();
            var open = new SortedDictionary<int, List<TileZone>>();
            void Offer(TileZone zone, int distance, float dirX, float dirY)
            {
                if (best.TryGetValue(zone.id, out var known) && known.distance <= distance) return;
                best[zone.id] = (distance, dirX, dirY);
                if (!open.TryGetValue(distance, out List<TileZone> list)) open[distance] = list = new List<TileZone>();
                list.Add(zone);
            }
            if (extend)
                foreach (TileZone zone in zones)
                foreach ((int dx, int dy) in Sides)
                {
                    TileZone from = calc.getZone(zone.x - dx, zone.y - dy);
                    if (from != null && plan.distances.TryGetValue(from.id, out int distance))
                        Offer(zone, Mathf.Max(floor, distance + 1), dx, dy);
                }
            // 起点：挨着本来就归新主人(同色)的块；没有就挨着新主人的宗主、同一帝国的块；再没有(或是自己变)就是离都城最近的块(从块心往外)
            if (best.Count == 0 && others.Contains(group.Key))
                foreach (Func<ShownState, bool> test in new Func<ShownState, bool>[]
                         {
                             shown => ReferenceEquals(shown.meta, group.Key.meta) && shown.colorKey == group.Key.color,
                             shown => Related(shown.meta, group.Key.meta)
                         })
                {
                    foreach (TileZone zone in zones)
                    {
                        (float dirX, float dirY, bool touches) = Facing(calc, zone, members, test);
                        if (touches) Offer(zone, floor, dirX, dirY);
                    }
                    if (best.Count > 0) break;
                }
            if (best.Count == 0)
            {
                TileZone seat = SeatOf(group.Key.meta);
                TileZone first = seat == null ? zones[0]
                    : zones.OrderBy(zone => (zone.x - seat.x) * (zone.x - seat.x) + (zone.y - seat.y) * (zone.y - seat.y)).First();
                Offer(first, floor, 0f, 0f);
            }
            var seeds = best.Select(pair => (zone: calc.getZoneByID(pair.Key), pair.Value.distance))
                .Where(entry => entry.zone != null).ToList();
            // 由近及远一圈圈排(起点圈数不一，按圈数从小到大展开)
            void Spread()
            {
                while (open.Count > 0)
                {
                    int distance = open.Keys.First();
                    List<TileZone> ring = open[distance];
                    open.Remove(distance);
                    foreach (TileZone zone in ring)
                    {
                        if (best[zone.id].distance != distance) continue;
                        foreach ((int dx, int dy) in Sides)
                        {
                            TileZone neighbour = calc.getZone(zone.x + dx, zone.y + dy);
                            if (neighbour != null && members.Contains(neighbour.id)) Offer(neighbour, distance + 1, dx, dy);
                        }
                    }
                }
            }
            Spread();
            // 不连着的几片(海岛、飞地、被别国隔开的)：按离最近起点的直线距离接着排，隔多远就晚几圈，
            // 和主体一起扫完(以前排在整道扫描最后，主体变完了还剩几块等一下才变)
            if (best.Count < members.Count && seeds.Count > 0)
            {
                foreach (TileZone zone in zones)
                {
                    if (best.ContainsKey(zone.id)) continue;
                    float nearest = float.MaxValue, dirX = 0f, dirY = 0f;
                    foreach ((TileZone seed, int distance) in seeds)
                    {
                        float dx = zone.x - seed.x, dy = zone.y - seed.y, length = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance + length >= nearest) continue;
                        nearest = distance + length;
                        dirX = length > 0f ? dx / length : 0f;
                        dirY = length > 0f ? dy / length : 0f;
                    }
                    Offer(zone, Mathf.Max(floor, Mathf.RoundToInt(nearest)), dirX, dirY);
                }
                Spread();
            }
            int farthest = best.Count == 0 ? floor : best.Values.Max(entry => entry.distance);
            if (!extend) plan.step = Mathf.Min(SweepStep, MaxSweepSeconds / Mathf.Max(1, farthest + 1));
            foreach (TileZone zone in zones)
            {
                if (!Transitions.TryGetValue(zone.id, out Transition transition)) continue;
                (int distance, float dirX, float dirY) = best.TryGetValue(zone.id, out var entry) ? entry : (farthest + 1, 0f, 0f);
                transition.flipAt = plan.start + (distance + 0.3f * (FrontNoise(zone.x * 5, zone.y * 5) - 0.5f)) * plan.step;
                Orient(transition, dirX, dirY, zone);
                transition.planned = true;
                plan.distances[zone.id] = distance;
            }
            plan.end = Mathf.Max(plan.end, plan.start + plan.step * (farthest + 3f));
            Plans[group.Key] = plan;
        }
        PlanFollowers(calc, now);
    }

    // 跟着邻块换的块：挨着的换主地块里最早翻到一半的那一刻换；挨着的都不用翻了(新旧一样)就当场换
    private static void PlanFollowers(ZoneCalculator calc, float now)
    {
        foreach (int id in PendingFollow)
        {
            if (!Transitions.TryGetValue(id, out Transition follower) || !follower.follow) continue;
            TileZone zone = calc.getZoneByID(id);
            float at = float.MaxValue;
            if (zone != null)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    TileZone near = dx == 0 && dy == 0 ? null : calc.getZone(zone.x + dx, zone.y + dy);
                    if (near != null && Transitions.TryGetValue(near.id, out Transition lead) && !lead.follow && lead.planned)
                        at = Mathf.Min(at, lead.flipAt + lead.duration / 2f);
                }
            follower.flipAt = at == float.MaxValue ? now : at;
            follower.planned = true;
        }
        PendingFollow.Clear();
    }

    private static readonly (int dx, int dy)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    // 这块挨着的新主人那边在哪个方向(扫描从那边来)
    private static (float dirX, float dirY, bool touches) Facing(ZoneCalculator calc, TileZone zone, HashSet<int> members,
        Func<ShownState, bool> test)
    {
        float sumX = 0f, sumY = 0f;
        bool touches = false;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            TileZone neighbour = calc.getZone(zone.x + dx, zone.y + dy);
            if (neighbour == null || members.Contains(neighbour.id) || !Shown.TryGetValue(neighbour.id, out ShownState shown) ||
                !test(shown)) continue;
            touches = true;
            sumX -= dx;
            sumY -= dy;
        }
        float length = Mathf.Sqrt(sumX * sumX + sumY * sumY);
        return length < 0.01f ? (0f, 0f, touches) : (sumX / length, sumY / length, touches);
    }

    // 宗主、附庸、同一帝国
    private static bool Related(IMetaObject shown, IMetaObject target)
    {
        if (shown == null || target == null) return false;
        Kingdom a = shown as Kingdom ?? (shown as Empire)?.CoreKingdom, b = target as Kingdom ?? (target as Empire)?.CoreKingdom;
        if (a == null || b == null || a.isRekt() || b.isRekt()) return false;
        if (FeudalVassalService.GetOverlord(b) == a || FeudalVassalService.GetOverlord(a) == b) return true;
        Empire empire = a.GetEmpire();
        return empire != null && empire == b.GetEmpire();
    }

    private static TileZone SeatOf(IMetaObject meta)
    {
        Kingdom kingdom = meta as Kingdom ?? (meta as Empire)?.CoreKingdom;
        return kingdom?.capital?.getTile()?.zone;
    }

    #endregion

    #region 上色缓存

    // 帝国图层上悬停帝国里的成员国时，它的地块换成本国颜色、移开又换回帝国颜色；平滑国界下邻块跟着变，
    // 每次都要整块重画(逐格平滑、地形、城市平面图)，大国一换就是几百块地，鼠标一动卡一下。
    // 同样的输入画出同样的像素：每块地记下最近画过的两版，再遇到同样的输入直接拷回，当帧就好、几乎不花时间，画面不变。
    // 输入：两种颜色、周围 5×5 块的归属及其颜色与所属帝国、图层与选项、战线开关、这几方两两是否交战、本城平面图的指纹、纯净地图与规划视图；
    // 周围有逐格推进的地块不缓存；缓存三十秒过期(没算进去的变化至多三十秒后补上；地形变了当场丢掉那块的，见 ForgetSnapshot)
    private const float CacheSeconds = 30f;
    private const int MaxCached = 12000;

    private sealed class Snapshot
    {
        public long key;
        public float at;
        public Color32[] pixels;
        public IMetaObject[] owners;
        public bool[] outline, foreign;
    }

    private static readonly Dictionary<int, Snapshot[]> Snapshots = new();
    private static long _pendingKey;
    private static int _restoredZone = -1;

    // 这块地刚从缓存拷回(地形调色、城市平面图已经在里面了，PureMapPatch 不再画)
    public static bool Restored(TileZone zone)
    {
        if (zone == null || _restoredZone != zone.id) return false;
        _restoredZone = -1;
        return true;
    }

    private static long KeyOf(MetaTypeAsset layer, TileZone zone, Color32 inside, Color32 outside)
    {
        unchecked
        {
            long key = -3750763034362895579L;
            void Add(long value) => key = (key ^ value) * 1099511628211L;
            Add(zone.id);
            Add(inside.r | inside.g << 8 | inside.b << 16 | inside.a << 24);
            Add(outside.r | outside.g << 8 | outside.b << 16 | outside.a << 24);
            Add(layer.GetHashCode());
            Add(layer.getZoneOptionState());
            for (int i = 0; i < 25; i++)
            {
                if (PushMetas[i] != null) return 0L;
                IMetaObject meta = Shades[i];
                IMetaObject city = CityMetas[i];
                Add(city == null ? 0 : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(city));
                Add(meta == null ? 0 : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(meta));
                Add(ColorKey(SafeColor(meta)));
                // 帝国金边看各国属于哪个帝国
                if (meta is Kingdom kingdom) Add(kingdom.GetEmpireID());
            }
            Add((CorpsSystem.Enabled ? 1 : 0) | (PureMapMode.Enabled ? 2 : 0) |
                (PureMapPatch.DetailShown ? 8 : 0) | (PureMapPatch.Relief ? 16 : 0) |
                (PureMapPatch.CitiesShown ? 32 : 0));
            Add(CorpsSystem.Enabled && ControlLayer(layer) ? FrontSignature() : 0);
            Add(PureMapPatch.CitiesShown ? PureMapPatch.StampOf(zone.city) : 0);
            return key == 0L ? 1L : key;
        }
    }

    // 战线朱红只画在周围 5×5 块里交战的两方之间：缓存按这几方两两是否交战(且真打起来)算，
    // 以前记全局的宣战议和次数，任何一处宣战、议和全图的上色缓存一起作废，每块都得从头画
    // (性能日志里"纯净地图·地块上色"每帧都顶着预算，排在后面的地块迟迟轮不到，见 ZonePaintBudget)
    private static readonly List<IMetaObject> FrontParties = new();

    private static int FrontSignature()
    {
        FrontParties.Clear();
        for (int i = 0; i < 25; i++)
        {
            IMetaObject meta = Shades[i];
            if (meta != null && !FrontParties.Contains(meta)) FrontParties.Add(meta);
        }
        if (FrontParties.Count < 2) return 0;
        unchecked
        {
            int signature = 17;
            for (int a = 0; a < FrontParties.Count; a++)
            for (int b = a + 1; b < FrontParties.Count; b++)
                signature = signature * 3 + (AtWarMeta(FrontParties[a], FrontParties[b]) ? 1 : 0) +
                            (AtWarMeta(FrontParties[b], FrontParties[a]) ? 2 : 0);
            return signature;
        }
    }

    private static bool Restore(Color32[] pixels, TileZone zone, long key)
    {
        if (!Snapshots.TryGetValue(zone.id, out Snapshot[] slots) || pixels.Length != _base.Length) return false;
        float now = Time.unscaledTime;
        foreach (Snapshot shot in slots)
        {
            if (shot == null || shot.key != key || now - shot.at > CacheSeconds || now < shot.at ||
                shot.pixels.Length != zone.tiles.Length) continue;
            int[] ids = PixelIds(zone);
            BeginLift();
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                if (id < 0) continue;
                _owner[id] = shot.owners[i];
                _outline[id] = shot.outline[i];
                _foreign[id] = shot.foreign[i];
                _base[id] = shot.pixels[i];
                _hasBase[id] = true;
                pixels[id] = Lifted(id);
            }
            // 记下画成谁要同重画一样按显示的一方(悬停的省按本省)：以前记实际归属，悬停的省从缓存拷回时
            // 记下的没变，四邻不跟着重画，省界留在邻块上成了残线(图层诊断"多余的边框"，修过仍在)
            Remember(zone, Shades[12]);
            _restoredZone = zone.id;
            World.world.zone_calculator.setDirty();
            return true;
        }
        return false;
    }

    private static void Store(TileZone zone, long key)
    {
        if (Snapshots.Count > MaxCached) Snapshots.Clear();
        if (!Snapshots.TryGetValue(zone.id, out Snapshot[] slots)) Snapshots[zone.id] = slots = new Snapshot[2];
        // 换掉同样输入的旧版，否则换掉较旧的一版
        int slot = slots[0] == null || slots[0].key == key ? 0 : slots[1] == null || slots[1].key == key ? 1 :
            slots[0].at <= slots[1].at ? 0 : 1;
        Snapshot shot = slots[slot] ??= new Snapshot();
        int count = zone.tiles.Length;
        if (shot.pixels == null || shot.pixels.Length != count)
        {
            shot.pixels = new Color32[count];
            shot.owners = new IMetaObject[count];
            shot.outline = new bool[count];
            shot.foreign = new bool[count];
        }
        shot.key = key;
        shot.at = Time.unscaledTime;
        int[] ids = PixelIds(zone);
        for (int i = 0; i < count; i++)
        {
            int id = ids[i];
            if (id < 0) continue;
            shot.pixels[i] = _base[id];
            shot.owners[i] = _owner[id];
            shot.outline[i] = _outline[id];
            shot.foreign[i] = _foreign[id];
        }
    }

    public static void ClearCache() => Snapshots.Clear();

    // 这块地的地形变了(见 ZonePaintBudget.TerrainChanged)：缓存的输入不含地形，丢掉，下次重画按眼下的地形画
    public static void ForgetSnapshot(TileZone zone)
    {
        if (zone != null && Snapshots.Count > 0) Snapshots.Remove(zone.id);
    }

    // 这一格记为边框，四邻的陆地格记下的归属却都同它一样：残留的边框(国界早该没了，见 LayerDiagnostics)
    public static bool StaleOutline(WorldTile tile)
    {
        int id = tile?.data?.tile_id ?? -1;
        if (!Enabled || id < 0 || id >= _outline.Length || !_outline[id] || !_hasBase[id]) return false;
        IMetaObject owner = _owner[id];
        if (owner == null) return false;
        foreach (WorldTile next in new[] { tile.tile_up, tile.tile_down, tile.tile_left, tile.tile_right })
        {
            if (next?.data == null) return false;
            int other = next.data.tile_id;
            if (IsWater(next.x, next.y)) continue;
            if (other < 0 || other >= _owner.Length || !ReferenceEquals(_owner[other], owner)) return false;
        }
        return true;
    }

    // 归属/战线变化或残留边框：丢掉本块与周围八块的缓存，包含平滑轮廓伸入的斜对角。
    // 重画指纹(用户 2026-10-19 转玩家反馈"平滑模式下色块不刷新"，问答选"补全重画指纹")：原版决定一块地要不要重画
    // (TileZone.checkShouldReRender)只比归属对象与调色板编号(城市图层连编号都不比)，自定义色、附庸描边色、被谁占着、
    // 所属帝国(金边)变了它都看不出来，那片地停在旧颜色。别的游戏的做法是画的时候比对全部输入的指纹，不靠定时扫描事后补：
    // 这里把实际控制方、实际颜色值、所属帝国折进原版比对的那个数(见 ZonePaintBudget.BeforeCheck)，变了下一轮巡检就重画、照常翻牌
    // 每轮巡检每块地都要算一次指纹：按帧记下——各方的样子(AppearanceKeys)；归属只看城的图层上(见 CityKeyed)，
    // 原版按城成串巡检，同一座城的地块只取一次归属，城里没有被占的地块时也不逐块查谁占着(结果与逐块算一样)
    private static int _fingerprintFrame = -1, _fingerprintOption;
    private static MetaTypeAsset _fingerprintLayer;
    private static bool _fingerprintSmooth, _fingerprintByCity;
    private static City _fingerprintCity;
    private static IMetaObject _fingerprintCityMeta, _fingerprintMeta;
    private static bool _fingerprintCityOccupied;
    private static int _fingerprintKey, _fingerprintAppearance;
    private static readonly Dictionary<IMetaObject, int> AppearanceKeys = new();
    // 刚算过指纹的那块地(原版巡检时先算指纹、紧接着决定画不画，见 ZonePaintBudget.AfterCheck)：实际控制方与它的样子
    private static int _fpZone = -1, _fpAppearance;
    private static IMetaObject _fpReal;

    internal static int Fingerprint(TileZone zone)
    {
        _fpZone = -1;
        if (zone == null || World.world == null) return 0;
        int frame = Time.frameCount;
        if (_fingerprintFrame != frame)
        {
            _fingerprintFrame = frame;
            // 按原版这一轮实际在画的图层算(换图层那一帧原版还按旧图层画一轮，setMode 在这之后)：
            // 以前取已经换好的新图层，旧图层这一轮里每块地的指纹都变了，五千多块全按旧图层白画一遍
            _fingerprintLayer = World.world.zone_calculator?._mode_asset ?? World.world.getCachedMapMetaAsset();
            _fingerprintOption = _fingerprintLayer?.getZoneOptionState() ?? 0;
            _fingerprintSmooth = _fingerprintLayer != null && Smoothable(_fingerprintLayer);
            _fingerprintByCity = _fingerprintSmooth && CityKeyed(_fingerprintLayer, _fingerprintOption);
            _fingerprintCity = null;
            _fingerprintCityMeta = _fingerprintMeta = null;
            _fingerprintKey = _fingerprintAppearance = 0;
            AppearanceKeys.Clear();
        }
        if (!_fingerprintSmooth) return 0;
        IMetaObject real;
        City city = zone.city;
        if (_fingerprintByCity && city != null)
        {
            if (!ReferenceEquals(city, _fingerprintCity))
            {
                _fingerprintCity = city;
                _fingerprintCityMeta = LayerMeta(_fingerprintLayer, _fingerprintOption, zone);
                _fingerprintCityOccupied = ControlLayer(_fingerprintLayer) && city.kingdom != null && city.HasOccupiedZones();
            }
            real = _fingerprintCityOccupied ? RealMetaOf(_fingerprintLayer, _fingerprintOption, zone) : _fingerprintCityMeta;
        }
        else real = RealMetaOf(_fingerprintLayer, _fingerprintOption, zone);
        _fpZone = zone.id;
        _fpReal = real;
        _fpAppearance = 0;
        if (real == null) return 0;
        if (ReferenceEquals(real, _fingerprintMeta))
        {
            _fpAppearance = _fingerprintAppearance;
            return _fingerprintKey;
        }
        if (!AppearanceKeys.TryGetValue(real, out int key)) AppearanceKeys[real] = key = AppearanceKey(real);
        _fingerprintMeta = real;
        _fpAppearance = _fingerprintAppearance = key;
        unchecked
        {
            return _fingerprintKey = global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(real) * 31 + key;
        }
    }

    // 刚巡检的这块地自身的归属或颜色变了(上次画成的与眼下的实际控制方对不上；还没画过的也算)：
    // 这种不画就一直是错色(帝国解散、成员离开、城换了主……)，上色排队时优先(见 ZonePaintBudget)；
    // 只是四邻变了、城市平面图变了、全图重画之类的重画不算
    internal static bool OwnChanged(TileZone zone)
    {
        if (!Enabled || zone == null || zone.id != _fpZone) return false;
        if (!LastMeta.TryGetValue(zone.id, out var last)) return _fpReal != null;
        return !ReferenceEquals(last.meta, _fpReal) || last.color != _fpAppearance;
    }

    // 一方画出来的样子：颜色(内、外框)与所属帝国(国家图层的帝国金边)
    private static int AppearanceKey(IMetaObject meta)
    {
        int key = ColorKey(SafeColor(meta));
        return meta is Kingdom kingdom ? unchecked(key * 31 + (int)kingdom.GetEmpireID()) : key;
    }

    // 图层诊断用(见 LayerDiagnostics)：色块没刷新——这块地上次画成的归属或颜色与眼下的实际归属对不上，原版却没来重画
    // (重画指纹漏了的变动)；正在翻牌、排着翻的不算，悬停省份临时换成本省颜色的不算。
    // 还没画过的块：眼下有归属，色块却是空的也算
    internal static bool StaleOwner(MetaTypeAsset layer, TileZone zone, Color32[] pixels, out string detail)
    {
        detail = null;
        if (!Enabled || layer == null || zone?.tiles == null || !Smoothable(layer) || Transitions.ContainsKey(zone.id) ||
            PendingSweep.Contains(zone.id)) return false;
        IMetaObject real = RealMetaOf(layer, layer.getZoneOptionState(), zone);
        if (!LastMeta.TryGetValue(zone.id, out var last))
        {
            if (real == null || pixels == null || !ControlLayer(layer) && layer.map_mode != MetaType.City) return false;
            foreach (WorldTile tile in zone.tiles)
            {
                int id = tile?.data?.tile_id ?? -1;
                if (id < 0 || id >= pixels.Length || IsWater(tile.x, tile.y)) continue;
                if (pixels[id].a != 0) return false;
                detail = $"没画过，眼下归 {NameOf(real)}";
                return true;
            }
            return false;
        }
        if (ReferenceEquals(last.meta, real) && last.color == AppearanceKey(real)) return false;
        if (real is Empire && zone.city?.kingdom is Kingdom province && ReferenceEquals(last.meta, province)) return false;
        detail = $"画成 {NameOf(last.meta as IMetaObject)}，眼下归 {NameOf(real)}" +
                 (ReferenceEquals(last.meta, real) ? "(颜色变了)" : "");
        return true;
    }

    // 诊断用：记下的旧主人可能已经灭亡(原版 name 取 data.name，data 没了就空引用)
    private static string NameOf(IMetaObject meta)
    {
        try
        {
            return meta switch
            {
                null => "无",
                Kingdom kingdom => kingdom.data == null ? "已灭之国" : kingdom.name,
                City city => city.data == null ? "已毁之城" : city.name,
                Empire empire => empire.data == null ? "已亡帝国" : empire.GetEmpireName(),
                _ => meta.GetType().Name
            };
        }
        catch (Exception)
        {
            return meta.GetType().Name;
        }
    }

    public static void Repaint(TileZone zone)
    {
        if (zone == null) return;
        Snapshots.Remove(zone.id);
        zone.resetRenderHelpers();
        foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
        {
            if (next == null) continue;
            Snapshots.Remove(next.id);
            next.resetRenderHelpers();
        }
    }

    #endregion

    // 提亮查表：表就是用 Color32.Lerp(c, 白, HighlightLift) 逐个通道值算出来的，与逐格插值的结果一模一样
    // (Lerp 各通道独立；透明度插向自己，不变)，逐格不再做浮点插值
    private static readonly byte[] LiftTable = BuildLiftTable();

    private static byte[] BuildLiftTable()
    {
        var table = new byte[256];
        var white = new Color32(255, 255, 255, 255);
        for (int v = 0; v < 256; v++) table[v] = Color32.Lerp(new Color32((byte)v, (byte)v, (byte)v, 255), white, HighlightLift).r;
        return table;
    }

    // 调用前这一轮先 BeginLift()(新的一帧作废按归属记下的提亮与否)
    private static Color32 Lifted(int id)
    {
        Color32 color = _base[id];
        IMetaObject owner = _owner[id];
        if (_outline[id] || owner == null || color.a == 0) return color;
        return Lit(owner) ? new Color32(LiftTable[color.r], LiftTable[color.g], LiftTable[color.b], color.a) : color;
    }

    // 悬停的、选中的(原版选中后边框一闪一闪，这里改成常亮；选中变了原版会整图重画)。每帧按归属记一次，不逐格查；
    // 悬停时单独显示本省颜色的那个省算它所属的帝国。
    // 相邻的格大多同一归属：上一次查的归属与结果记下(_runOwner)，同一轮(_litEpoch 没变)直接用，连字典也不查
    private static readonly Dictionary<IMetaObject, bool> LitCache = new();
    private static int _litFrame = -1, _litEpoch, _runEpoch = -1;
    private static IMetaObject _runOwner;
    private static bool _runLit;

    // 逐格提亮的循环开头调一次：帧号只在这里取(不逐格取)，新的一帧作废记下的结果
    private static void BeginLift()
    {
        int frame = Time.frameCount;
        if (_litFrame == frame) return;
        _litFrame = frame;
        ResetLit();
    }

    // 高亮对象变了：作废记下的结果
    private static void ResetLit()
    {
        LitCache.Clear();
        _litEpoch++;
    }

    private static bool Lit(IMetaObject owner)
    {
        if (_runEpoch == _litEpoch && ReferenceEquals(owner, _runOwner)) return _runLit;
        if (!LitCache.TryGetValue(owner, out bool lit))
        {
            IMetaObject realm = ReferenceEquals(owner, _hover) && _hoverRealm != null ? _hoverRealm : owner;
            ZoneCalculator calc = World.world?.zone_calculator;
            lit = ActiveMetas.Count > 0 && (ActiveMetas.Contains(owner) || ActiveMetas.Contains(realm)) ||
                  calc != null && calc._cached_check_animation && calc.shouldShowSelectionFor(realm);
            LitCache[owner] = lit;
        }
        _runOwner = owner;
        _runLit = lit;
        _runEpoch = _litEpoch;
        return lit;
    }

    // 相邻像素归属不同(水面不算：海岸本身不画边框)
    private static bool Differs(IMetaObject owner, int x, int y, int width, int height) =>
        x >= 0 && y >= 0 && x < width && y < height && !IsWater(x, y) && !ReferenceEquals(OwnerCached(x, y), owner);

    // 一次上色里两国是否交战(按国家或帝国核心算，结果在这块地里缓存)
    private static readonly Dictionary<(IMetaObject, IMetaObject), bool> Hostility = new();

    private static bool AtWar(IMetaObject owner, int x, int y, int width, int height)
    {
        if (owner == null || x < 0 || y < 0 || x >= width || y >= height || IsWater(x, y)) return false;
        IMetaObject other = OwnerCached(x, y);
        if (other == null || ReferenceEquals(other, owner)) return false;
        if (Hostility.TryGetValue((owner, other), out bool hostile)) return hostile;
        hostile = AtWarMeta(owner, other);
        Hostility[(owner, other)] = hostile;
        return hostile;
    }

    // 两方(国家或帝国，帝国按核心算)交战且本月真打起来了
    private static bool AtWarMeta(IMetaObject owner, IMetaObject other)
    {
        Kingdom a = owner is Kingdom ka ? ka : (owner as Empire)?.CoreKingdom;
        Kingdom b = other is Kingdom kb ? kb : (other as Empire)?.CoreKingdom;
        return a != null && b != null && FrontSystem.Hostile(a, b) && FrontSystem.HasFront(a, b);
    }

    // 描金的范围(同 PureMapPatch.AfterApplyColors)：战国时的周室势力圈(加粗的那种)；纯净地图下所属的帝国
    private static Empire EmpireOf(IMetaObject meta)
    {
        if (meta is not Kingdom kingdom) return null;
        Empire sphere = ZhouEraSystem.SphereOf(kingdom);
        if (sphere != null && ZhouEraSystem.Era(sphere) >= ZhouEraSystem.Warring) return sphere;
        return PureMapMode.Enabled ? kingdom.GetEmpire() : null;
    }

    private static bool EmpireDiffers(Empire empire, int x, int y, int width, int height) =>
        x >= 0 && y >= 0 && x < width && y < height && EmpireOf(OwnerCached(x, y)) != empire;

    // 归邻国(邻城)的像素用它的颜色：自定义色、纯净地图不透明，与原版/PureMapPatch 的规则一致
    private static void ColorsOf(IMetaObject owner, bool clearInside, ref Color32 inside, ref Color32 outside)
    {
        if (owner == null)
        {
            inside = outside = new Color32(0, 0, 0, 0);
            return;
        }
        ColorsFrom(SafeColor(owner), clearInside, ref inside, ref outside);
    }

    private static ColorAsset SafeColor(IMetaObject meta)
    {
        if (meta == null) return null;
        try
        {
            return CustomColorSystem.Of(meta) ?? meta.getColor();
        }
        catch
        {
            return null;
        }
    }

    // 城市精细层的底色也从当前图层、当前选项和实际控制方取，不能固定拿国家色或选项 0。
    internal static ColorAsset DisplayColor(TileZone zone)
    {
        MetaTypeAsset layer = World.world?.getCachedMapMetaAsset();
        if (zone == null || !Smoothable(layer)) return null;
        int option = layer.getZoneOptionState();
        IMetaObject meta = RealMetaOf(layer, option, zone);
        Kingdom hover = HoveredProvince(layer, option);
        if (meta is Empire empire && hover == zone.city?.kingdom && hover != null && hover.GetEmpire() == empire)
            meta = hover;
        return SafeColor(meta);
    }

    private static void ColorsFrom(ColorAsset color, bool clearInside, ref Color32 inside, ref Color32 outside)
    {
        if (color == null) return;
        inside = clearInside ? new Color32(0, 0, 0, 0) : color.getColorBorderInsideAlpha32();
        outside = color.getColorMainSecond32();
        if (!PureMapMode.Enabled) return;
        if (inside.a > 0) inside.a = byte.MaxValue;
        if (outside.a > 0) outside.a = byte.MaxValue;
    }

    // 宣战、议和后战线(边框朱红)要变：双方各城的地块重画(WarPatch)；上色缓存按周围几方是否交战取(见 FrontSignature)，
    // 不挨着敌方的地块照样从缓存拷回
    public static void RefreshWar(War war)
    {
        if (!Enabled || war == null) return;
        foreach (Kingdom kingdom in war.getAttackers().Concat(war.getDefenders()))
            foreach (City city in kingdom?.cities ?? new List<City>())
                foreach (TileZone zone in city?.zones ?? new List<TileZone>())
                    zone?.resetRenderHelpers();
    }

    // 本月真打起来的两方变了(见 FrontSystem.HasFront)：涉及的国家(连同所在帝国的成员)各城重画
    // (只重画挨着别国的地块并丢掉它们的上色缓存，不让全图缓存作废)
    public static void RefreshFronts(ICollection<long> realms)
    {
        if (!Enabled || realms == null || realms.Count == 0 || World.world?.kingdoms == null) return;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() ||
                !realms.Contains(kingdom.id) && !realms.Contains(FrontSystem.Realm(kingdom).id)) continue;
            foreach (City city in kingdom.cities ?? new List<City>())
                foreach (TileZone zone in city?.zones ?? new List<TileZone>())
                {
                    if (zone?.neighbours_all == null) continue;
                    bool border = false;
                    foreach (TileZone next in zone.neighbours_all)
                        if (next?.city?.kingdom != null && next.city.kingdom != kingdom) { border = true; break; }
                    if (!border) continue;
                    Snapshots.Remove(zone.id);
                    zone.resetRenderHelpers();
                }
        }
    }

    // 这块地的归属或颜色变了：周围两圈(5×5，连斜对角)也重画，平滑的边要跟着变。
    // 一格归谁看它所在地块周围 3×3 块，判边框还要看外面一圈格(落在邻块里，归属又看邻块周围 3×3)，
    // 所以隔一块的地块边上的边框也跟着这块变；以前只重画周围八块，隔一块的那圈留着旧边框(图层诊断"多余的边框")
    private const int RememberRadius = 2;

    private static void Remember(TileZone zone, IMetaObject meta)
    {
        // 自定义色可能共用同一个调色板编号，必须比较实际色值；所属帝国变了四邻的金边也要重画
        int color = AppearanceKey(meta);
        if (LastMeta.TryGetValue(zone.id, out var last) && ReferenceEquals(last.meta, meta) && last.color == color) return;
        bool first = !LastMeta.ContainsKey(zone.id);
        LastMeta[zone.id] = (meta, color);
        if (first) return;
        ZoneCalculator calc = World.world.zone_calculator;
        for (int dy = -RememberRadius; dy <= RememberRadius; dy++)
        for (int dx = -RememberRadius; dx <= RememberRadius; dx++)
            if (dx != 0 || dy != 0) calc.getZone(zone.x + dx, zone.y + dy)?.resetRenderHelpers();
    }

    #endregion
}
