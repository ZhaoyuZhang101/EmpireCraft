using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 平滑国界(模组设置「平滑国界」)：国界、城界不再沿 8×8 的地块方格走，而是逐个像素(地格)判定归属——
// 每格按周围 3×3 块地的归属做二次 B 样条加权(块心权重最大，越靠边越接近一半一半)，再叠一层按国家散列的
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

    public static bool IsOutline(int tileId) => Enabled && tileId >= 0 && tileId < _outline.Length && _outline[tileId];
    public static bool IsForeign(int tileId) => Enabled && tileId >= 0 && tileId < _foreign.Length && _foreign[tileId];

    #region 归属

    // 以当前块为中心 5×5 块的归属缓存(像素要看 3×3 块，边上的像素再看一圈)
    private static readonly IMetaObject[] Metas = new IMetaObject[25];
    // 正在逐格推进的块：吃下的格子归谁(推进方，或收复时的原主)
    private static readonly IMetaObject[] PushMetas = new IMetaObject[25];
    private static readonly TileZone[] Zones = new TileZone[25];
    private static int _cx, _cy;

    // 图层本来的归属(原版按它算颜色、选中高亮)
    private static IMetaObject LayerMeta(MetaTypeAsset layer, int option, TileZone zone)
    {
        if (zone == null) return null;
        IMetaObject meta = layer.map_mode == MetaType.City ? (IMetaObject)zone.city : layer.tile_get_metaobject?.Invoke(zone, option);
        // 帝国图层上没被哪个帝国(或同盟视图)覆盖的王国：模组按本国颜色补画(见 EmpireCraftMetaTypeLibrary)，这里也按本国归属
        if ((meta == null || !meta.isAlive()) && layer.map_mode == MetaTypeExtension.Empire &&
            zone.city?.kingdom is Kingdom kingdom && kingdom.isAlive() && !kingdom.wild)
            meta = kingdom;
        return meta != null && meta.isAlive() ? meta : null;
    }

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
        if (occupier?.data == null || occupier.isRekt() || occupier == owner || occupier.isInWarOnSameSide(owner)) return meta;
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
        if (layer.map_mode != MetaType.City && layer.tile_get_metaobject == null) return false;
        ZoneCalculator calc = World.world.zone_calculator;
        int option = layer.getZoneOptionState();
        _cx = center.x;
        _cy = center.y;
        _hover = HoveredProvince(layer, option);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int index = (dy + 2) * 5 + dx + 2;
            TileZone zone = calc.getZone(_cx + dx, _cy + dy);
            Zones[index] = zone;
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

    // 地格 (x, y) 归谁：逐格推进中已吃下的归推进方；否则周围 3×3 块的归属按二次 B 样条加权，
    // 再加按国家散列的低频噪声，取最大
    private static IMetaObject OwnerAt(int x, int y)
    {
        if (IsWater(x, y)) return null;
        int zx = Mathf.FloorToInt((float)x / ZoneSize), zy = Mathf.FloorToInt((float)y / ZoneSize);
        int hx = zx - _cx + 2, hy = zy - _cy + 2;
        if (hx is >= 0 and < 5 && hy is >= 0 and < 5)
        {
            int here = hy * 5 + hx;
            if (PushMetas[here] != null && FrontSystem.SmoothCovers(Zones[here], x, y)) return PushMetas[here];
        }
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
                IMetaObject meta = gx is >= 0 and < 5 && gy is >= 0 and < 5 ? Shades[gy * 5 + gx] : null;
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
    private static readonly HashSet<int> CollectZones = new(), ActiveZones = new();
    private static int _collectFrame = -1;

    // QuantumSpriteLibrary.colorZones 的前缀：记下这一帧要高亮哪些地块(及其归属)，不画方片
    public static bool Highlight(IEnumerable<TileZone> zones)
    {
        if (!Enabled || World.world == null || zones == null) return false;
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        if (layer == null || layer.map_mode != MetaType.City && layer.tile_get_metaobject == null) return false;
        int frame = Time.frameCount;
        if (_collectFrame != frame)
        {
            FinishFrame();
            _collectFrame = frame;
        }
        int option = layer.getZoneOptionState();
        foreach (TileZone zone in zones)
        {
            if (zone == null || !CollectZones.Add(zone.id)) continue;
            IMetaObject meta = LayerMeta(layer, option, zone);
            if (meta != null) CollectMetas.Add(meta);
        }
        return true;
    }

    // 每帧(ModClass.Update)：上一帧以来没有高亮调用就熄灭
    public static void FrameTick()
    {
        TrackHover();
        SweepTick();
        int frame = Time.frameCount;
        if (_collectFrame == frame || _collectFrame == frame - 1) return;
        if (ActiveZones.Count == 0 && CollectZones.Count == 0) return;
        FinishFrame();
        _collectFrame = frame;
    }

    // 收齐一帧：高亮对象变了，新旧地块连同四周重画
    private static void FinishFrame()
    {
        bool same = CollectMetas.SetEquals(ActiveMetas) && CollectZones.SetEquals(ActiveZones);
        if (!same && World.world?.zone_calculator != null)
        {
            ZoneCalculator calc = World.world.zone_calculator;
            var touched = new HashSet<int>(ActiveZones);
            touched.UnionWith(CollectZones);
            ActiveMetas.Clear();
            ActiveMetas.UnionWith(CollectMetas);
            ActiveZones.Clear();
            ActiveZones.UnionWith(CollectZones);
            LitCache.Clear();
            // 高亮只是把底色提亮：相关地块(连同四周，平滑的边会伸进邻块)直接改色，当帧就变，不必重画
            Color32[] pixels = calc.pixels;
            var done = new HashSet<int>();
            bool changed = false;
            if (pixels != null && pixels.Length == _base.Length)
                foreach (int id in touched)
                {
                    TileZone zone = calc.getZoneByID(id);
                    if (zone == null) continue;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        TileZone near = calc.getZone(zone.x + dx, zone.y + dy);
                        if (near?.tiles == null || !done.Add(near.id)) continue;
                        foreach (WorldTile tile in near.tiles)
                        {
                            int pixel = tile.data.tile_id;
                            if (!_hasBase[pixel] || pixels[pixel].a == 0) continue;
                            pixels[pixel] = Lifted(pixel);
                            changed = true;
                        }
                    }
                }
            if (changed) calc.setDirty();
        }
        CollectMetas.Clear();
        CollectZones.Clear();
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
        // 内陆地块(周围 3×3 块同属一方、没有逐格推进)：平滑不会改变任何一格(隔两块的地权重不到百分之一，
        // 噪声翻不过来)，也画不出边框——不逐格算 B 样条与四邻
        bool uniform = true;
        for (int dy = 1; dy <= 3 && uniform; dy++)
        for (int dx = 1; dx <= 3; dx++)
        {
            int index = dy * 5 + dx;
            if (ReferenceEquals(Shades[index], Shades[12]) && PushMetas[index] == null) continue;
            uniform = false;
            break;
        }
        for (int gy = 0; gy < Window; gy++)
        for (int gx = 0; gx < Window; gx++)
            Owners[gy * Window + gx] = uniform ? IsWater(_ox + gx, _oy + gy) ? null : Shades[12] : OwnerAt(_ox + gx, _oy + gy);
        Color32[] pixels = calc.pixels;
        if (_outline.Length != pixels.Length)
        {
            _outline = new bool[pixels.Length];
            _foreign = new bool[pixels.Length];
            _owner = new IMetaObject[pixels.Length];
            _base = new Color32[pixels.Length];
            _hasBase = new bool[pixels.Length];
        }
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
        if (zone?.tiles == null || pixels == null || pixels.Length != _base.Length) return;
        if (!Enabled || _paintedZone != zone.id)
        {
            foreach (WorldTile tile in zone.tiles) _hasBase[tile.data.tile_id] = false;
            return;
        }
        _paintedZone = -1;
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile.data.tile_id;
            _base[id] = pixels[id];
            _hasBase[id] = true;
            pixels[id] = Lifted(id);
        }
        if (_pendingKey != 0L) Store(zone, _pendingKey);
        ApplyTransition(pixels, zone);
        _pendingKey = 0L;
    }

    // 悬停、选中的对象变了：属于新旧对象的格子按记下的底色重新提亮或还原
    public static void Retint(params object[] metas)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        Color32[] pixels = calc?.pixels;
        if (!Enabled || pixels == null || pixels.Length != _base.Length) return;
        LitCache.Clear();
        bool changed = false;
        for (int id = 0; id < pixels.Length; id++)
        {
            IMetaObject owner = _owner[id];
            if (owner == null || !_hasBase[id] || pixels[id].a == 0) continue;
            IMetaObject realm = ReferenceEquals(owner, _hover) && _hoverRealm != null ? _hoverRealm : owner;
            bool hit = false;
            foreach (object meta in metas)
                if (meta != null && (ReferenceEquals(meta, owner) || ReferenceEquals(meta, realm)))
                {
                    hit = true;
                    break;
                }
            if (!hit) continue;
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
    //   · 换主(归降、被占、城市归降、并入帝国)：从新主人那边(入侵方向、宗主或同一帝国那边)一圈圈翻过来，
    //     每块牌挨着新主人的那一边先翘起来，每圈约 SweepStep 秒，整片至多 MaxSweepSeconds 秒；
    //   · 自己变(归属没换、只是颜色变了，如国家换颜色、成了附庸换色)，或换主时接不上新主人：从都城那块地往外一圈圈翻；
    //   · 临时换色(帝国图层悬停某省显示本省颜色、移开换回)：要跟手——从鼠标处很快地翻过去(每秒 RippleSpeed 格，
    //     每张牌 HoverFlipSeconds 秒)，这个省连同四周的地块上色时优先；没翻完就移开，从眼下看到的那一面翻回去；
    //   · 换图层(国家、文化、宗教……或图层选项)：旧图留着，各地块照常分帧重画(镜头里的先画，见 ZonePaintBudget)，
    //     画好一块翻一块，从镜头中心往外一圈圈翻，约 LayerWaveSeconds 秒翻到镜头边上；镜头外的直接换。
    // 一次换主的地块要分好几帧才画完(上色分帧，见 ZonePaintBudget)：等这批画完(至多 MaxPlanWait 秒)再统一排一道，
    // 之后才画到的块接着这道往下排。逐格推进吃下的地(FrontSystem.Smooth)本身就是渐变，直接显示。新旧一样的块不翻。
    private const float SweepStep = 0.18f, MaxSweepSeconds = 4f, RippleSpeed = 600f, RippleGroupSeconds = 0.15f, HoverFlipSeconds = 0.18f,
        StaleSeconds = 6f, MaxPlanWait = 0.4f, FlipSeconds = 0.32f, LayerWaveSeconds = 0.9f, LayerFlipSeconds = 6f,
        SlotShade = 0.55f;

    private sealed class ShownState
    {
        public IMetaObject meta;
        public int colorKey;
    }

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
    }

    private static readonly Dictionary<int, ShownState> Shown = new();
    private static readonly Dictionary<int, (int inside, int outside, int meta, int shade)> NativeShown = new();
    private static readonly HashSet<int> PendingSweep = new();
    private static readonly Dictionary<int, Transition> Transitions = new();

    // 正在扫的一道扫描(按变成谁分)：起扫时刻、每块用时、各块排第几圈
    private sealed class SweepPlan
    {
        public float start, step, end;
        public readonly Dictionary<int, int> distances = new();
    }

    private static readonly Dictionary<(IMetaObject meta, int color), SweepPlan> Plans = new();
    private static float _pendingSince;
    private static MetaTypeAsset _sweepLayer;
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
        Transitions.Clear();
        Plans.Clear();
        ExpandedUnits.Clear();
        _layerFlip = null;
    }

    // 真实归属；顺带看这块地换主了没有，换了就记下它原来的样子、排进扫描
    private static IMetaObject MetaOf(MetaTypeAsset layer, int option, TileZone zone)
    {
        IMetaObject real = RealMetaOf(layer, option, zone);
        if (zone == null || !Enabled) return real;
        if (!ReferenceEquals(layer, _sweepLayer) || option != _sweepOption)
        {
            bool first = _sweepLayer == null;
            _sweepLayer = layer;
            _sweepOption = option;
            ResetTransitions();
            // 换图层整图都要重画：不再因为"本块归属变了"让四周八块跟着重画，否则每块要多画好几遍
            LastMeta.Clear();
            if (!first) StartLayerFlip();
        }
        int realKey = ColorKey(SafeColor(real));
        if (Shown.TryGetValue(zone.id, out ShownState shown) && ReferenceEquals(shown.meta, real) && shown.colorKey == realKey)
            return real;
        bool known = shown != null;
        Shown[zone.id] = new ShownState { meta = real, colorKey = realKey };
        if (!known) return real;
        Queue(zone, zone.x != _cx || zone.y != _cy, ReferenceEquals(shown.meta, real));
        ExpandUnit(layer, option, zone, shown.meta, real);
        return real;
    }

    // 记下这块地原来的样子、排进扫描。不是正在画的这块(画邻块时顺带发现它变了)：让原版下一轮重画它，
    // 否则它等不到新样子，渐变作废后一直留着旧颜色
    private static void Queue(TileZone zone, bool repaint, bool self)
    {
        Transition transition = Capture(zone);
        if (transition == null) return;
        transition.self = self;
        Transitions[zone.id] = transition;
        if (PendingSweep.Count == 0) _pendingSince = Time.unscaledTime;
        PendingSweep.Add(zone.id);
        if (repaint) zone.resetRenderHelpers();
    }

    // 换色的单位是整个国家、整个帝国(并入帝国、成了附庸、国家换颜色……)：一块地换了，同一国(涉及帝国就是整个帝国)
    // 其余要变的地块也当场记下原样、排进同一道扫描，整片从新主人那边一次扫过去。以前要等原版一块块画到才各自排进来，
    // 画得晚的城只能就地接上，看起来一座城一座城地变。同一国 0.15 秒内只查一次
    private const float ExpandCooldown = 0.15f;
    private static readonly Dictionary<long, float> ExpandedUnits = new();

    private static void ExpandUnit(MetaTypeAsset layer, int option, TileZone zone, IMetaObject before, IMetaObject after)
    {
        float now = Time.unscaledTime;
        var kingdoms = new List<Kingdom>();
        void Add(Kingdom kingdom)
        {
            if (kingdom?.cities == null || kingdom.isRekt()) return;
            if (ExpandedUnits.TryGetValue(kingdom.id, out float at) && now >= at && now - at < ExpandCooldown) return;
            ExpandedUnits[kingdom.id] = now;
            kingdoms.Add(kingdom);
        }
        void AddRealm(IMetaObject meta)
        {
            if (meta is Kingdom kingdom) Add(kingdom);
            else if (meta is Empire empire && !empire.isRekt() && empire.kingdoms_list != null)
                foreach (Kingdom member in empire.kingdoms_list) Add(member);
        }
        Add(zone.city?.kingdom);
        AddRealm(before);
        AddRealm(after);
        foreach (Kingdom kingdom in kingdoms)
        foreach (City city in kingdom.cities)
        {
            if (city?.zones == null) continue;
            foreach (TileZone other in city.zones)
            {
                if (other == null || other == zone || PendingSweep.Contains(other.id) ||
                    !Shown.TryGetValue(other.id, out ShownState shown)) continue;
                IMetaObject real = RealMetaOf(layer, option, other);
                int key = ColorKey(SafeColor(real));
                if (ReferenceEquals(shown.meta, real) && shown.colorKey == key) continue;
                Shown[other.id] = new ShownState { meta = real, colorKey = key };
                Queue(other, true, ReferenceEquals(shown.meta, real));
            }
        }
    }

    // 原版把这块地清空了(不再归任何对象，如城没了)：忘掉记下的底色、渐变与缓存，免得之后高亮、渐变又把旧颜色写回去
    public static void Cleared(TileZone zone)
    {
        if (zone?.tiles == null) return;
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile.data.tile_id;
            if (id < 0 || id >= _hasBase.Length) continue;
            _hasBase[id] = false;
            _owner[id] = null;
        }
        Transitions.Remove(zone.id);
        PendingSweep.Remove(zone.id);
        _layerFlip?.seen.Remove(zone.id);
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
        if (zone?.tiles == null || _base.Length == 0) return null;
        if (Transitions.TryGetValue(zone.id, out Transition running) && running.oldPixels?.Length == zone.tiles.Length)
        {
            // 正在翻(或等着翻)的块又要变：旧样子是眼下看到的那一面，不是底下压暗的缝
            Color32[] face = running.phase >= 2 && running.newPixels != null ? running.newPixels : running.oldPixels;
            return new Transition { oldPixels = (Color32[])face.Clone(), created = Time.unscaledTime };
        }
        var old = new Color32[zone.tiles.Length];
        for (int i = 0; i < zone.tiles.Length; i++)
        {
            int id = zone.tiles[i].data.tile_id;
            if (id < 0 || id >= _base.Length || !_hasBase[id]) return null;
            old[i] = _base[id];
        }
        return new Transition { oldPixels = old, created = Time.unscaledTime };
    }

    // 逐格推进吃下的块：推进本身已经是渐变，换主时直接显示。已经画好新样子、还在按旧样子显示的(等着扫描)，
    // 当场换成新样子——以前直接丢掉渐变，这块地就一直留着旧颜色(它已经画过，原版不会再重画)
    public static void Settle(TileZone zone)
    {
        if (zone == null) return;
        Shown.Remove(zone.id);
        PendingSweep.Remove(zone.id);
        if (!Transitions.TryGetValue(zone.id, out Transition transition)) return;
        Transitions.Remove(zone.id);
        ZoneCalculator calc = World.world?.zone_calculator;
        Color32[] pixels = calc?.pixels;
        if (transition.newPixels == null || zone.tiles == null || pixels == null || pixels.Length != _base.Length ||
            transition.newPixels.Length != zone.tiles.Length) return;
        for (int i = 0; i < zone.tiles.Length; i++)
        {
            int id = zone.tiles[i].data.tile_id;
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

    // 这块地刚画好新样子：记下来；新旧一样就不翻
    private static void ApplyTransition(Color32[] pixels, TileZone zone)
    {
        if (!Transitions.TryGetValue(zone.id, out Transition transition)) return;
        var fresh = new Color32[zone.tiles.Length];
        bool same = transition.oldPixels.Length == fresh.Length;
        for (int i = 0; i < zone.tiles.Length; i++)
        {
            fresh[i] = _base[zone.tiles[i].data.tile_id];
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
            return;
        }
        transition.newPixels = fresh;
        transition.phase = -1;
        WriteFlip(pixels, zone, transition, Time.unscaledTime);
    }

    // 按翻到哪一步写底下的色块：没翻是旧样子，翻着是压暗的旧(前半)、新(后半)样子，翻完是新样子；步子变了才写
    private static bool WriteFlip(Color32[] pixels, TileZone zone, Transition transition, float now)
    {
        int phase = !transition.planned || transition.newPixels == null || now < transition.flipAt ? 0
            : now >= transition.flipAt + transition.duration ? 3 : now < transition.flipAt + transition.duration / 2f ? 1 : 2;
        if (phase == transition.phase) return false;
        transition.phase = phase;
        Color32[] source = phase <= 1 ? transition.oldPixels : transition.newPixels;
        bool slot = phase is 1 or 2;
        for (int i = 0; i < zone.tiles.Length && i < source.Length; i++)
        {
            int id = zone.tiles[i].data.tile_id;
            _base[id] = slot ? Slot(source[i]) : source[i];
            pixels[id] = Lifted(id);
        }
        return true;
    }

    // 换图层：从镜头中心往外一圈圈翻(这次重画的地块各翻一次)
    private sealed class LayerFlip
    {
        public float start, speed, originX, originY;
        public Rect view;
        public readonly HashSet<int> seen = new();
    }

    private static LayerFlip _layerFlip;

    private static void StartLayerFlip()
    {
        Rect view = ZoneFlipOverlay.CameraView();
        float half = Mathf.Sqrt(view.width * view.width + view.height * view.height) / 2f;
        _layerFlip = new LayerFlip
        {
            start = Time.unscaledTime, speed = Mathf.Max(40f, half / LayerWaveSeconds), originX = view.center.x, originY = view.center.y,
            view = Rect.MinMaxRect(view.xMin - ZoneSize, view.yMin - ZoneSize, view.xMax + ZoneSize, view.yMax + ZoneSize)
        };
    }

    // 换图层后这块地头一次重画：记下旧样子，排好何时翻(画晚了的当场翻)；镜头外的不翻
    private static void CaptureLayerFlip(TileZone zone)
    {
        LayerFlip flip = _layerFlip;
        if (flip == null) return;
        float now = Time.unscaledTime;
        if (now - flip.start > LayerFlipSeconds)
        {
            _layerFlip = null;
            return;
        }
        if (!flip.seen.Add(zone.id) || Transitions.ContainsKey(zone.id)) return;
        float cx = zone.x * ZoneSize + ZoneSize / 2f, cy = zone.y * ZoneSize + ZoneSize / 2f;
        if (!flip.view.Contains(new Vector2(cx, cy))) return;
        Transition transition = Capture(zone);
        if (transition == null) return;
        float dx = cx - flip.originX, dy = cy - flip.originY;
        transition.planned = true;
        transition.flipAt = Mathf.Max(now, flip.start + Mathf.Sqrt(dx * dx + dy * dy) / flip.speed + (FrontNoise(zone.x * 7, zone.y * 7) - 0.5f) * 0.08f);
        Orient(transition, dx, dy, zone);
        Transitions[zone.id] = transition;
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
        if (PendingSweep.Count > 0 && (!PendingUnpainted() || now - _pendingSince >= MaxPlanWait)) PlanSweeps();
        if (Plans.Count > 0)
            foreach ((IMetaObject, int) key in Plans.Where(pair => now > pair.Value.end).Select(pair => pair.Key).ToList())
                Plans.Remove(key);
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
    // 输入：两种颜色、周围 5×5 块的归属及其颜色与所属帝国、图层与选项、战线开关、宣战议和、本城平面图的指纹、纯净地图与规划视图；
    // 周围有逐格推进的地块不缓存；缓存三十秒过期(地形之类没算进去的变化至多三十秒后补上)
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
    // 宣战、议和的次数(战线朱红边要变)
    private static int _warEpoch;

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
                Add(meta == null ? 0 : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(meta));
                Add(ColorIndex(meta));
                // 帝国金边看各国属于哪个帝国
                if (meta is Kingdom kingdom) Add(kingdom.GetEmpireID());
            }
            Add((CorpsSystem.Enabled ? 1 : 0) | (PureMapMode.Enabled ? 2 : 0) | (ZonePlanSystem.SteadyOverlay ? 4 : 0) |
                (PureMapPatch.DetailShown ? 8 : 0) | (PureMapPatch.Relief ? 16 : 0));
            Add(_warEpoch);
            Add(PureMapPatch.StampOf(zone.city));
            return key == 0L ? 1L : key;
        }
    }

    // 颜色编号：颜色表里找不到(编号失效，原版 getColor 会抛异常)的记 -2，不能让一块地因此画不出来
    private static int ColorIndex(IMetaObject meta)
    {
        if (meta == null) return -1;
        try
        {
            return (CustomColorSystem.Of(meta) ?? meta.getColor())?.index_id ?? -1;
        }
        catch
        {
            return -2;
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
            for (int i = 0; i < zone.tiles.Length; i++)
            {
                int id = zone.tiles[i].data.tile_id;
                _owner[id] = shot.owners[i];
                _outline[id] = shot.outline[i];
                _foreign[id] = shot.foreign[i];
                _base[id] = shot.pixels[i];
                _hasBase[id] = true;
                pixels[id] = Lifted(id);
            }
            Remember(zone, Metas[12]);
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
        for (int i = 0; i < count; i++)
        {
            int id = zone.tiles[i].data.tile_id;
            shot.pixels[i] = _base[id];
            shot.owners[i] = _owner[id];
            shot.outline[i] = _outline[id];
            shot.foreign[i] = _foreign[id];
        }
    }

    public static void ClearCache() => Snapshots.Clear();

    #endregion

    private static Color32 Lifted(int id)
    {
        Color32 color = _base[id];
        IMetaObject owner = _owner[id];
        if (_outline[id] || owner == null || color.a == 0) return color;
        return Lit(owner) ? Color32.Lerp(color, new Color32(255, 255, 255, color.a), HighlightLift) : color;
    }

    // 悬停的、选中的(原版选中后边框一闪一闪，这里改成常亮；选中变了原版会整图重画)。每帧按归属记一次，不逐格查；
    // 悬停时单独显示本省颜色的那个省算它所属的帝国
    private static readonly Dictionary<IMetaObject, bool> LitCache = new();
    private static int _litFrame = -1;

    private static bool Lit(IMetaObject owner)
    {
        if (_litFrame != Time.frameCount)
        {
            _litFrame = Time.frameCount;
            LitCache.Clear();
        }
        if (LitCache.TryGetValue(owner, out bool lit)) return lit;
        IMetaObject realm = ReferenceEquals(owner, _hover) && _hoverRealm != null ? _hoverRealm : owner;
        ZoneCalculator calc = World.world?.zone_calculator;
        lit = ActiveMetas.Count > 0 && (ActiveMetas.Contains(owner) || ActiveMetas.Contains(realm)) ||
              calc != null && calc._cached_check_animation && calc.shouldShowSelectionFor(realm);
        LitCache[owner] = lit;
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
        Kingdom a = owner is Kingdom ka ? ka : (owner as Empire)?.CoreKingdom;
        Kingdom b = other is Kingdom kb ? kb : (other as Empire)?.CoreKingdom;
        hostile = a != null && b != null && FrontSystem.Hostile(a, b);
        Hostility[(owner, other)] = hostile;
        return hostile;
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

    private static void ColorsFrom(ColorAsset color, bool clearInside, ref Color32 inside, ref Color32 outside)
    {
        if (color == null) return;
        inside = clearInside ? new Color32(0, 0, 0, 0) : color.getColorBorderInsideAlpha32();
        outside = color.getColorMainSecond32();
        if (!PureMapMode.Enabled) return;
        if (inside.a > 0) inside.a = byte.MaxValue;
        if (outside.a > 0) outside.a = byte.MaxValue;
    }

    // 宣战、议和后战线(边框朱红)要变：双方各城的地块重画(WarPatch)
    public static void RefreshWar(War war)
    {
        _warEpoch++;
        if (!Enabled || war == null) return;
        foreach (Kingdom kingdom in war.getAttackers().Concat(war.getDefenders()))
            foreach (City city in kingdom?.cities ?? new List<City>())
                foreach (TileZone zone in city?.zones ?? new List<TileZone>())
                    zone?.resetRenderHelpers();
    }

    // 这块地的归属或颜色变了：周围八块(连斜对角)也重画，平滑的边要跟着变
    private static void Remember(TileZone zone, IMetaObject meta)
    {
        int color = ColorIndex(meta);
        if (LastMeta.TryGetValue(zone.id, out var last) && ReferenceEquals(last.meta, meta) && last.color == color) return;
        bool first = !LastMeta.ContainsKey(zone.id);
        LastMeta[zone.id] = (meta, color);
        if (first) return;
        ZoneCalculator calc = World.world.zone_calculator;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (dx != 0 || dy != 0) calc.getZone(zone.x + dx, zone.y + dy)?.resetRenderHelpers();
    }

    #endregion
}
