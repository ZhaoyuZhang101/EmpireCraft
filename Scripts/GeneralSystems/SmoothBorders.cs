using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
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
        Shown.Clear();
        PendingSweep.Clear();
        RevealAt.Clear();
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
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int index = (dy + 2) * 5 + dx + 2;
            TileZone zone = calc.getZone(_cx + dx, _cy + dy);
            Zones[index] = zone;
            Metas[index] = MetaOf(layer, option, zone, out ShownColors[index]);
            PushMetas[index] = PushMetaOf(layer, zone);
        }
        return true;
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
                IMetaObject meta = gx is >= 0 and < 5 && gy is >= 0 and < 5 ? Metas[gy * 5 + gx] : null;
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
        long key = KeyOf(layer, zone, inside, outside);
        if (key != 0L && Restore(calc.pixels, zone, key)) return true;
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
            if (ReferenceEquals(Metas[index], Metas[12]) && PushMetas[index] == null) continue;
            uniform = false;
            break;
        }
        for (int gy = 0; gy < Window; gy++)
        for (int gx = 0; gx < Window; gx++)
            Owners[gy * Window + gx] = uniform ? IsWater(_ox + gx, _oy + gy) ? null : Metas[12] : OwnerAt(_ox + gx, _oy + gy);
        Color32[] pixels = calc.pixels;
        if (_outline.Length != pixels.Length)
        {
            _outline = new bool[pixels.Length];
            _foreign = new bool[pixels.Length];
            _owner = new IMetaObject[pixels.Length];
            _base = new Color32[pixels.Length];
            _hasBase = new bool[pixels.Length];
        }
        IMetaObject own = Metas[12];
        IMetaObject native = LayerMeta(layer, layer.getZoneOptionState(), zone);
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
            bool foreign = !ReferenceEquals(owner, own);
            bool edge = !uniform && owner != null &&
                        (Differs(owner, x + 1, y, width, height) || Differs(owner, x - 1, y, width, height) ||
                         Differs(owner, x, y + 1, width, height) || Differs(owner, x, y - 1, width, height));
            _foreign[id] = foreign;
            _outline[id] = edge;
            _owner[id] = owner;
            Color32 colorInside = inside, colorOutside = outside;
            // 这格所在的块还在换色渐变里(显示旧的归属或旧颜色)：归这块的格子照旧颜色画
            int shownIndex = (Mathf.FloorToInt((float)y / ZoneSize) - _cy + 2) * 5 + Mathf.FloorToInt((float)x / ZoneSize) - _cx + 2;
            ColorAsset shownColor = shownIndex is >= 0 and < 25 && ReferenceEquals(owner, Metas[shownIndex]) ? ShownColors[shownIndex] : null;
            if (shownColor != null) ColorsFrom(shownColor, clearInside, ref colorInside, ref colorOutside);
            else if (!ReferenceEquals(owner, native)) ColorsOf(owner, clearInside, ref colorInside, ref colorOutside);
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
        _pendingKey = 0L;
    }

    // 悬停、选中的对象变了：属于新旧对象的格子按记下的底色重新提亮或还原
    public static void Retint(params object[] metas)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        Color32[] pixels = calc?.pixels;
        if (!Enabled || pixels == null || pixels.Length != _base.Length) return;
        bool changed = false;
        for (int id = 0; id < pixels.Length; id++)
        {
            IMetaObject owner = _owner[id];
            if (owner == null || !_hasBase[id] || pixels[id].a == 0) continue;
            bool hit = false;
            foreach (object meta in metas)
                if (meta != null && ReferenceEquals(meta, owner))
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

    // 一片地一下子换了主(归降、被占、并入帝国、成了附庸换了颜色)：不一次全变，先照旧显示，
    // 再从新主人那边(入侵方向、宗主方向；接不上就从都城)一块一块变过去，每往里一块晚 SweepStep 秒；
    // 重画也就分散到这几秒里，不挤在一帧。逐格推进吃下的地(FrontSystem.Smooth)本身就是渐变，不再排队
    private const float SweepStep = 0.12f, SweepJitter = 0.06f, MaxSweepSeconds = 4f;

    private sealed class ShownState
    {
        public IMetaObject meta;
        public ColorAsset color;
        public int colorKey;
    }

    private static readonly ColorAsset[] ShownColors = new ColorAsset[25];
    private static readonly Dictionary<int, ShownState> Shown = new();
    private static readonly HashSet<int> PendingSweep = new();
    private static readonly Dictionary<int, float> RevealAt = new();
    private static MetaTypeAsset _sweepLayer;
    private static int _sweepOption = int.MinValue;

    private static int ColorKey(ColorAsset color)
    {
        if (color == null) return 0;
        Color32 a = color.getColorBorderInsideAlpha32(), b = color.getColorMainSecond32();
        unchecked
        {
            return ((a.r << 24) | (a.g << 16) | (a.b << 8) | a.a) * 31 + ((b.r << 24) | (b.g << 16) | (b.b << 8) | b.a);
        }
    }

    // 显示中的归属：变了的块先照旧(shownColor 是要用的旧颜色，没在渐变里为 null)
    private static IMetaObject MetaOf(MetaTypeAsset layer, int option, TileZone zone, out ColorAsset shownColor)
    {
        shownColor = null;
        IMetaObject real = RealMetaOf(layer, option, zone);
        if (zone == null || !Enabled) return real;
        if (!ReferenceEquals(layer, _sweepLayer) || option != _sweepOption)
        {
            // 换了图层：重新记，不做渐变
            _sweepLayer = layer;
            _sweepOption = option;
            Shown.Clear();
            PendingSweep.Clear();
            RevealAt.Clear();
        }
        ColorAsset realColor = SafeColor(real);
        int realKey = ColorKey(realColor);
        if (!Shown.TryGetValue(zone.id, out ShownState shown))
        {
            Shown[zone.id] = new ShownState { meta = real, color = realColor, colorKey = realKey };
            return real;
        }
        if (ReferenceEquals(shown.meta, real) && shown.colorKey == realKey)
        {
            PendingSweep.Remove(zone.id);
            RevealAt.Remove(zone.id);
            return real;
        }
        if (!RevealAt.ContainsKey(zone.id)) PendingSweep.Add(zone.id);
        shownColor = shown.color;
        return shown.meta;
    }

    // 逐格推进吃下的块：推进本身已经是渐变，换主时直接显示
    public static void Settle(TileZone zone)
    {
        if (zone == null) return;
        Shown.Remove(zone.id);
        PendingSweep.Remove(zone.id);
        RevealAt.Remove(zone.id);
    }

    // 每帧：给新变的块排好变色时间；到点的块换成新主人并重画(连同四周)
    private static void SweepTick()
    {
        if (!Enabled || World.world?.zone_calculator == null) return;
        if (PendingSweep.Count > 0) PlanSweeps();
        if (RevealAt.Count == 0) return;
        float now = Time.unscaledTime;
        List<int> due = null;
        foreach (KeyValuePair<int, float> pair in RevealAt)
            if (pair.Value <= now) (due ??= new List<int>()).Add(pair.Key);
        if (due == null) return;
        ZoneCalculator calc = World.world.zone_calculator;
        foreach (int id in due)
        {
            RevealAt.Remove(id);
            Shown.Remove(id);
            TileZone zone = calc.getZoneByID(id);
            if (zone == null) continue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                calc.getZone(zone.x + dx, zone.y + dy)?.resetRenderHelpers();
        }
    }

    private static void PlanSweeps()
    {
        ZoneCalculator calc = World.world.zone_calculator;
        MetaTypeAsset layer = _sweepLayer;
        if (layer == null)
        {
            PendingSweep.Clear();
            return;
        }
        // 按变成谁(新主人、新颜色)分组，各组从挨着新主人的块起一圈圈往里排
        var groups = new Dictionary<(IMetaObject meta, int color), List<TileZone>>();
        foreach (int id in PendingSweep)
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone == null) continue;
            IMetaObject real = RealMetaOf(layer, _sweepOption, zone);
            var key = (real, ColorKey(SafeColor(real)));
            if (!groups.TryGetValue(key, out List<TileZone> list)) groups[key] = list = new List<TileZone>();
            list.Add(zone);
        }
        PendingSweep.Clear();
        float now = Time.unscaledTime;
        foreach (KeyValuePair<(IMetaObject meta, int color), List<TileZone>> group in groups)
        {
            List<TileZone> zones = group.Value;
            var members = new HashSet<int>(zones.Select(zone => zone.id));
            var distance = new Dictionary<int, int>();
            var queue = new Queue<TileZone>();
            // 起点：挨着已经显示成新主人(同色)的块；没有就挨着新主人的宗主、同一帝国的块；再没有就是离都城最近的块
            foreach (TileZone zone in zones)
                if (Touches(calc, zone, members, shown => ReferenceEquals(shown.meta, group.Key.meta) && shown.colorKey == group.Key.color))
                {
                    distance[zone.id] = 0;
                    queue.Enqueue(zone);
                }
            if (queue.Count == 0)
                foreach (TileZone zone in zones)
                    if (Touches(calc, zone, members, shown => Related(shown.meta, group.Key.meta)))
                    {
                        distance[zone.id] = 0;
                        queue.Enqueue(zone);
                    }
            if (queue.Count == 0)
            {
                TileZone seat = SeatOf(group.Key.meta);
                TileZone start = seat == null ? zones[0]
                    : zones.OrderBy(zone => (zone.x - seat.x) * (zone.x - seat.x) + (zone.y - seat.y) * (zone.y - seat.y)).First();
                distance[start.id] = 0;
                queue.Enqueue(start);
            }
            while (queue.Count > 0)
            {
                TileZone zone = queue.Dequeue();
                int next = distance[zone.id] + 1;
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    TileZone neighbour = calc.getZone(zone.x + dx, zone.y + dy);
                    if (neighbour == null || !members.Contains(neighbour.id) || distance.ContainsKey(neighbour.id)) continue;
                    distance[neighbour.id] = next;
                    queue.Enqueue(neighbour);
                }
            }
            int farthest = distance.Count == 0 ? 0 : distance.Values.Max();
            float step = farthest <= 0 ? SweepStep : Mathf.Min(SweepStep, MaxSweepSeconds / farthest);
            foreach (TileZone zone in zones)
            {
                int d = distance.TryGetValue(zone.id, out int value) ? value : farthest + 1;
                RevealAt[zone.id] = now + d * step + UnityEngine.Random.value * SweepJitter;
            }
        }
    }

    private static bool Touches(ZoneCalculator calc, TileZone zone, HashSet<int> members, Func<ShownState, bool> test)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            TileZone neighbour = calc.getZone(zone.x + dx, zone.y + dy);
            if (neighbour == null || members.Contains(neighbour.id) || !Shown.TryGetValue(neighbour.id, out ShownState shown)) continue;
            if (test(shown)) return true;
        }
        return false;
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
                IMetaObject meta = Metas[i];
                Add(meta == null ? 0 : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(meta));
                Add(ColorIndex(meta));
                Add(ShownColors[i] == null ? 0 : ColorKey(ShownColors[i]));
                // 帝国金边看各国属于哪个帝国
                if (meta is Kingdom kingdom) Add(kingdom.GetEmpireID());
            }
            Add((CorpsSystem.Enabled ? 1 : 0) | (PureMapMode.Enabled ? 2 : 0) | (ZonePlanSystem.SteadyOverlay ? 4 : 0));
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
        // 悬停的、选中的(原版选中后边框一闪一闪，这里改成常亮；选中变了原版会整图重画)
        ZoneCalculator calc = World.world?.zone_calculator;
        bool lit = ActiveMetas.Count > 0 && ActiveMetas.Contains(owner) ||
                   calc != null && calc._cached_check_animation && calc.shouldShowSelectionFor(owner);
        return lit ? Color32.Lerp(color, new Color32(255, 255, 255, color.a), HighlightLift) : color;
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
