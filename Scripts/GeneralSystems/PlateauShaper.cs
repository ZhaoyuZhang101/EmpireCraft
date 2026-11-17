using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 高原外缘(用户 2026-11-10 问答定"山脊+坡地，山口通到底"；"高原和平原的接壤处默认生成山地""要生成的自然点，看起来像是地势自然形成的"；
// "至少得让国家能通行吧""玩家可以通过擦除山地来留口")。生成地图时铺一次(随机图见 PlateauGenerator，世界地图见 WorldMapGenerator)：
//   高原面 → 外缘山脊(在高原一侧，宽 2~7.5 格，大小两层噪声起伏，夹着山巅，高寒高原的山顶多半积雪；有山嘴伸进高原)
//          → 坡地(平原一侧 2~4.5 格，铺成丘陵：能走、行军慢；里头零星几座孤山) → 平原；
//   山脊只沿"挨着能走的地"的边铺：外面本来就是厚山(喜马拉雅、太行)的地方不再加；荒漠、沙地也不算(蒙古高原上的戈壁、
//   黄土高原西北的腾格里都是高原连着的荒漠，不在高原与荒漠之间起山)；山脊至少两格宽，八方向都翻不过去；
//   山口：给定的路线(世界地图按史实，见 WorldMaps/plateau_passes.json)沿最省的路开出 3~5 格宽的谷道，从高原一直通到平原
//         (路线朝高原那头没落在高原上的——川藏、滇藏的终点在横断山里——接着找到最近的高原格，Enter 为假的路线不接)，
//         谷道里不铺山脊、不铺坡地，原有的山改成丘陵；一片高原没有路线经过的，自动挑 2~5 处(挑山最薄的边，彼此隔得远)，
//         所以每片高原至少有路通到外面——军队、政令教化、城市扩张、修路、商路都从山口过(TerrainSystem 照常按格子判通不通)；
//   高原里的河(窄水面)算高原里面，不沿河岸围山；流出高原的河在山脊处断开(山口处照留)。不到 MinRegion 格的高原碎块不围；
//   高原里被山围住、连不到外面大片平地的小块谷地(不到 PocketMax 格，如雅鲁藏布江谷地)不围山脊，照留作河谷耕地；
//   铺完再查一遍：哪块高原(连同挨着的谷地)走不到外面的平地，就沿最省的路挖一道山口接出去(Unseal)，保证各国都走得进去。
// 之后不再重铺：神力擦掉山就是新山口。
// 不切碎、不围死(群友 2026-11-17 "画好一整片高原，下一秒就被切成三份，路都修不通""高原边角宜居的地方被山脉围住""出不去进不来"
// "那里有一块丘陵的话就不在那里生成山脉边界")：
//   · 窄缝不算外面：能走的外面里窄于 2×SeamReach 格的(两笔之间留下的缝、高原与海或山夹着的窄条)不对着它起山脊、也不铺坡地；
//   · 细处山脊变窄：高原本身窄的地方(细颈、窄条)山脊至多铺到"本地半宽 - RidgeKeep"，中间总留一条高原面；山嘴只伸进宽处；
//   · 丘陵那边不起山：高原边上离原有的丘陵(成片 HillPatch 格以上)比离平地还近的格不起山脊、不伸山嘴，丘陵就是天然的坡与口子；
//   · 铺完核对(Connect)：铺之前四邻连着的能走的地，铺完还得连着(同 TerrainSystem.Crossable 四邻的口径)；被山脊、山嘴围出来的
//     那一块(不到 TinyPocket 格的高原碎格填成山)沿最省的路把自己铺的山挖开 3 格宽的口子，接回原来连着的那一大片。
// 神力画高原(用户 2026-11-10 "高原地形的神力呢""玩家可以自己放的""放的时候边缘也要自动铺设自然山体")：每画完一笔，
// 只在新画那一片附近重算一遍(ShapePainted，账见 PlateauPaintLedger)：算的时候把以前画高原铺的外缘看成没铺，只给画出来的高原格起山脊、
// 只在画出来的高原外面铺坡地，所以接着往外画，旧的外缘自己退掉、新边上再起；生成地图时铺的外缘、原有的高原不动。
public static class PlateauShaper
{
    // 神力画高原这一笔要重算的范围与账(格号都是图上的 y*宽+x；Scan 窗口比 Apply 窗口每边多出外缘能波及的距离)
    public sealed class Painting
    {
        public int ScanX0, ScanY0, ScanX1, ScanY1, ApplyX0, ApplyY0, ApplyX1, ApplyY1;
        // 以前画高原铺的外缘：这格铺之前的地面格(没铺过为 null，按现在的算)
        public Func<int, TileType> Base;
        // 画出来的高原格(只有它们起山脊)；缺口(铺好的山脊被擦掉的格：不算外面，背后不再起山)；不再铺坡地的格；以前开的山口
        public Func<int, bool> Painted, Opening, Keep, Pass;
        // 这格该是这样的外缘(记账再改)；这格不该有外缘(以前铺过的还原)
        public Action<int, TileType, TopTileType> Put;
        public Action<int> Clear;
        // 这一笔新开的山口谷道
        public readonly List<int> Carved = new();
    }

    public readonly struct Route
    {
        public readonly string Name;
        public readonly Vector2Int From, To;
        // To 那头要通进高原(没落在高原上就接到最近的高原格)；关中东出潼关这类只是别被坡地堵上的路线为假
        public readonly bool Enter;

        public Route(string name, Vector2Int from, Vector2Int to, bool enter = true)
        {
            Name = name;
            From = from;
            To = to;
            Enter = enter;
        }
    }

    // 山脊宽(格)：至少 MinRidge(两格，八方向翻不过)，细噪声加到 RidgeSpread、粗噪声(几十格一个起伏)再加 RidgeSwell；
    // 坡地同理；山嘴最多伸进高原 SpurReach；坡地里孤山的门槛
    private const float MinRidge = 2f, RidgeSpread = 4f, RidgeSwell = 1.5f, MinSlope = 2f, SlopeSpread = 2.5f, SpurReach = 7f,
        KnobLevel = 0.72f;
    // 山口谷道半径(格)：1.2~2.1，即宽 3~5 格
    private const float PassRadius = 1.2f, PassSpread = 0.9f;
    private const int MinRegion = 120, PocketMax = 1500, SealedMin = 30, UnsealSearch = 80;
    // 5×5 里陆地占四成以上的水算河
    private const float NarrowLand = 0.4f;
    private const float Inf = 1e9f, Diagonal = 1.41421356f;
    // 找路的窗口最多多少格(路线两头外扩)；自动山口往里、往外各找多远
    private const int MaxWindow = 600_000, AutoSearch = 16, OuterSearch = 40, EnterSearch = 60;
    // 窄缝：离外面以外(高原、水、山、荒漠)不到 SeamReach 格就够不着的能走的地算窄缝(窄于约 2×SeamReach 格)；
    // 细处山脊：本地半宽减 RidgeKeep 封顶(不低于 MinRidge)，山嘴只在本地半宽减 2×RidgeKeep 以内；
    // 丘陵：成片 HillPatch 格以上才算，离丘陵不比离平地远 HillMargin 格以上的不起山
    private const float SeamReach = 5f, RidgeKeep = 2f, HillMargin = 1f;
    private const int HillPatch = 4;
    // 本地半宽的档(格)：最宽算到 20(山脊带加山嘴最多伸进 14.5 格)
    private static readonly float[] ThickLevels = { 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 12f, 14f, 17f, 20f };
    // 铺完核对：不到 TinyPocket 格的碎块不挖口子(高原碎格填成山)；挖一处最多看 DigSearch 格；穿过自己铺的山一格的代价
    private const int TinyPocket = 12, DigSearch = 250_000;
    private const float DigCost = 5f;

    private static int _w, _h;
    private static WorldTile[] _tiles;
    private static bool[] _region, _water, _narrow, _lava, _block, _walk, _corridor, _opened;
    private static float[] _dIn, _dOut;
    // 高原本地半宽、到丘陵的距离(没有成片丘陵为 null)；窄缝(不铺坡地)；人画的高原格(窄缝、丘陵两条只对它放宽)
    private static float[] _thick, _dHill;
    private static bool[] _thin, _lenient;
    private static Func<int, bool> _drawn;
    // 铺什么(null 为不铺：画高原时还原)、顶层、哪一类(Ridge/Slope/Opened)
    private static TileType[] _plan;
    private static TopTileType[] _planTop;
    private static byte[] _kind;
    private const byte KindNone = 0, KindRidge = 1, KindSlope = 2, KindOpened = 3;
    private static int[] _comp;
    private static List<int> _sizes;
    private static int _seed;
    // 窗口在图上的左下角(整图为 0)；画高原时的账、只算画出来的格、不当外面的缺口、不铺坡地的格、各片的情况
    private static int _ox, _oy;
    private static Painting _paint;
    private static TileType[] _main;
    private static bool[] _eligible, _noSource, _noSlope;
    private static bool[] _compPainted, _compOld;
    private static List<int> _carved;
    // 画高原时自动山口离 Apply 窗口边至少几格(谷道整条落在窗口里)
    private const int PaintInset = 8;

    // painted：图上哪格是人刷的(剧本编辑器刷的地形，见 EarthTerrain.PaintedTile)；只有人画的高原才按窄缝、丘陵放宽，
    // 地球底图与随机图照旧只留史实山口、自动山口
    public static int Shape(IList<Route> routes, int seed, Func<int, bool> painted = null)
    {
        MapBox world = World.world;
        if (world == null || PlateauTerrain.Alpine == null || PlateauTerrain.Loess == null) return 0;
        _ox = _oy = 0;
        _paint = null;
        _drawn = painted;
        _w = MapBox.width;
        _h = MapBox.height;
        _seed = seed;
        int n = _w * _h;
        if (n <= 0) return 0;
        try
        {
            if (!Scan(world, n)) return 0;
            int passes = 0, auto = 0;
            var passed = new HashSet<int>();
            if (routes != null)
                foreach (Route route in routes)
                    if (Carve(route.From, route.Enter ? IntoRegion(route.To) : route.To, passed)) passes++;
                    else LogService.LogInfo($"[EmpireCraft][高原] 山口「{route.Name}」找不到路，没开");
            for (int c = 0; c < _sizes.Count; c++)
            {
                if (_sizes[c] < MinRegion || passed.Contains(c)) continue;
                auto += AutoPasses(c, passed);
            }
            (int ridge, int slope, int opened, int linked) = Apply();
            (int unsealed, int dug) = Unseal();
            LogService.LogInfo($"[EmpireCraft][高原] 外缘：山脊 {ridge} 格、坡地 {slope} 格、山口里开山 {opened} 格；路线山口 {passes} 条、" +
                               $"自动山口 {auto} 条；被外缘围住的接回 {linked} 处；围死的高原小块挖通 {unsealed} 处({dug} 格)");
            return ridge + slope + opened + dug;
        }
        finally
        {
            Release();
        }
    }

    private static void Release()
    {
        _tiles = null;
        _region = _water = _narrow = _lava = _block = _walk = _corridor = _opened = null;
        _dIn = _dOut = null;
        _thick = _dHill = null;
        _thin = _lenient = null;
        _drawn = null;
        _plan = null;
        _planTop = null;
        _kind = null;
        _comp = null;
        _sizes = null;
        _main = null;
        _eligible = _noSource = _noSlope = _compPainted = _compOld = null;
        _carved = null;
        _paint = null;
        _ox = _oy = 0;
    }

    // 神力画完一笔(见 PlateauPaintLedger)：只在 Scan 窗口里算、只改 Apply 窗口里的格。画出来的一片高原(没连着原有高原)
    // 窗口里还没有通到外面的山口，就在新画的边上挑 1~5 处；最后照样查一遍围死的(不碰窗口边的)挖通
    public static int ShapePainted(Painting paint, int seed)
    {
        MapBox world = World.world;
        if (world == null || paint == null || PlateauTerrain.Alpine == null || PlateauTerrain.Loess == null) return 0;
        int x0 = Math.Max(0, paint.ScanX0), y0 = Math.Max(0, paint.ScanY0);
        int x1 = Math.Min(MapBox.width - 1, paint.ScanX1), y1 = Math.Min(MapBox.height - 1, paint.ScanY1);
        if (x1 - x0 < 3 || y1 - y0 < 3) return 0;
        _ox = x0;
        _oy = y0;
        _w = x1 - x0 + 1;
        _h = y1 - y0 + 1;
        _seed = seed;
        _paint = paint;
        _drawn = null;
        _carved = new List<int>();
        try
        {
            if (!Scan(world, _w * _h)) return 0;
            var passed = new HashSet<int>();
            float ridgeBand = MinRidge + RidgeSpread;
            // 以前开的山口还通着的(谷道里的高原格还在山脊带上)：这片算有路
            for (int i = 0; i < _region.Length; i++)
                if (_corridor[i] && _region[i] && _dIn[i] <= ridgeBand) passed.Add(_comp[i]);
            int auto = 0;
            for (int c = 0; c < _sizes.Count; c++)
            {
                if (_sizes[c] < MinRegion || passed.Contains(c) || !_compPainted[c] || _compOld[c]) continue;
                auto += AutoPasses(c, passed);
            }
            (int ridge, int slope, int opened, int linked) = Apply();
            (int unsealed, int dug) = Unseal();
            foreach (int i in _carved) paint.Carved.Add(Global(i));
            if (ridge + slope + opened + dug > 0)
                LogService.LogInfo($"[EmpireCraft][高原] 神力画高原：山脊 {ridge} 格、坡地 {slope} 格、山口里开山 {opened} 格；" +
                                   $"自动山口 {auto} 条；被外缘围住的接回 {linked} 处；围死的挖通 {unsealed} 处({dug} 格)");
            return ridge + slope + opened + dug;
        }
        finally
        {
            Release();
        }
    }

    // 窗口里的格号 → 图上的格号
    private static int Global(int i) => (i / _w + _oy) * MapBox.width + i % _w + _ox;

    private static bool InApply(int x, int y, int inset = 0)
    {
        if (_paint == null) return true;
        int wx = x + _ox, wy = y + _oy;
        return wx >= _paint.ApplyX0 + inset && wx <= _paint.ApplyX1 - inset && wy >= _paint.ApplyY0 + inset && wy <= _paint.ApplyY1 - inset;
    }

    // 写格：整图生成直接改；画高原时交给账(记下原样，以后能退)
    private static void Put(int i, WorldTile tile, TileType main, TopTileType top)
    {
        if (_paint == null) Set(tile, main, top);
        else _paint.Put(Global(i), main, top);
    }

    private static bool Scan(MapBox world, int n)
    {
        _tiles = new WorldTile[n];
        _main = new TileType[n];
        var plateau = new bool[n];
        _water = new bool[n];
        _lava = new bool[n];
        _block = new bool[n];
        _walk = new bool[n];
        bool painting = _paint != null;
        _eligible = painting ? new bool[n] : null;
        _noSource = painting ? new bool[n] : null;
        _noSlope = painting ? new bool[n] : null;
        bool any = false;
        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            int i = y * _w + x;
            WorldTile tile = world.GetTileSimple(x + _ox, y + _oy);
            _tiles[i] = tile;
            TileType main = tile?.main_type;
            if (painting && tile != null)
            {
                // 以前画高原铺的外缘按铺之前的样子算
                int g = Global(i);
                main = _paint.Base(g) ?? main;
                _noSource[i] = _paint.Opening(g);
                _noSlope[i] = _noSource[i] || _paint.Keep(g);
            }
            _main[i] = main;
            if (main == null) continue;
            plateau[i] = PlateauTerrain.IsPlateau(main);
            if (painting) _eligible[i] = plateau[i] && _paint.Painted(Global(i));
            any |= painting ? _eligible[i] : plateau[i];
            _lava[i] = main.lava;
            _water[i] = main.liquid && !main.lava;
            _block[i] = main.block || main.mountains || main.summit;
            _walk[i] = !_water[i] && !_lava[i] && !_block[i];
        }
        if (!any) return false;
        _narrow = Narrow();
        // 高原连同挨着它的窄水面(河)：三轮，两格宽的河也并得进来
        _region = (bool[])plateau.Clone();
        var add = new List<int>();
        for (int round = 0; round < 3; round++)
        {
            add.Clear();
            for (int i = 0; i < n; i++)
                if (_narrow[i] && !_region[i] && Touches(_region, i)) add.Add(i);
            if (add.Count == 0) break;
            foreach (int i in add) _region[i] = true;
        }
        _comp = Label(_region, out _sizes);
        // 画高原时各片的情况：有没有画出来的格、有没有原有的高原格、碰没碰窗口边(碰边的不知道外头多大，不当碎块)
        var border = new bool[_sizes.Count];
        if (painting)
        {
            _compPainted = new bool[_sizes.Count];
            _compOld = new bool[_sizes.Count];
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x, c = _comp[i];
                if (c < 0) continue;
                if (_eligible[i]) _compPainted[c] = true;
                else if (plateau[i]) _compOld[c] = true;
                if (x == 0 || y == 0 || x == _w - 1 || y == _h - 1) border[c] = true;
            }
        }
        // 太小的碎块不围山，也不算高原一侧
        for (int i = 0; i < n; i++)
            if (_region[i] && _sizes[_comp[i]] < MinRegion && !border[_comp[i]]) _region[i] = false;
        var passable = new bool[n];
        for (int i = 0; i < n; i++) passable[i] = !_region[i] && _walk[i] && !Desert(_tiles[i]) && !(painting && _noSource[i]);
        // 被山、高原围住的小块谷地不算外面(不对着它铺山脊)
        int[] pocket = Label(passable, out List<int> pocketSizes);
        var open = new bool[pocketSizes.Count];
        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            int i = y * _w + x;
            if (pocket[i] < 0) continue;
            if (pocketSizes[pocket[i]] > PocketMax || x == 0 || y == 0 || x == _w - 1 || y == _h - 1) open[pocket[i]] = true;
        }
        for (int i = 0; i < n; i++)
            if (passable[i] && !open[pocket[i]]) passable[i] = false;
        _lenient = new bool[n];
        bool anyLenient = false;
        for (int i = 0; i < n; i++)
        {
            _lenient[i] = _region[i] && (painting ? _eligible[i] : _drawn != null && _drawn(Global(i)));
            anyLenient |= _lenient[i];
        }
        _thin = anyLenient ? Thin(passable) : new bool[n];
        _dIn = Distance(passable);
        _dHill = anyLenient ? Hills(passable) : null;
        _thick = Thickness();
        if (painting)
        {
            // 坡地只铺在画出来的高原外面
            var painted = new bool[n];
            for (int i = 0; i < n; i++) painted[i] = _region[i] && _eligible[i];
            _dOut = Distance(painted);
        }
        else _dOut = Distance(_region);
        _corridor = new bool[n];
        _opened = new bool[n];
        // 以前开的山口照留(谷道里不起山脊、不铺坡地)
        if (painting)
            for (int i = 0; i < n; i++)
                if (_paint.Pass(Global(i)))
                {
                    _corridor[i] = true;
                    if (_block[i]) _opened[i] = true;
                }
        return true;
    }

    // 窄缝：外面能走的地里，离外面以外不到 SeamReach 格的地方连成的窄条(圆盘放不进去的)。从外面里去掉(不对着它起山脊)，记下来不铺坡地
    private static bool[] Thin(bool[] passable)
    {
        int n = passable.Length;
        var wall = new bool[n];
        for (int i = 0; i < n; i++) wall[i] = !passable[i];
        float[] toWall = Distance(wall);
        var core = new bool[n];
        bool anyCore = false;
        for (int i = 0; i < n; i++)
            if (passable[i] && toWall[i] >= SeamReach)
            {
                core[i] = true;
                anyCore = true;
            }
        float[] toCore = anyCore ? Distance(core) : null;
        // 只管挨着人画的高原的窄条(地球底图的河谷、山间窄地照旧)
        float[] toDrawn = Distance(_lenient);
        var thin = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (!passable[i] || toCore != null && toCore[i] <= SeamReach + 0.01f || toDrawn[i] > 2f * SeamReach + 2f) continue;
            thin[i] = true;
            passable[i] = false;
        }
        return thin;
    }

    // 成片的原有丘陵(不是这回铺的坡地：画高原时以前铺的坡地按铺之前算)：到它的距离；没有为 null
    private static float[] Hills(bool[] passable)
    {
        int n = passable.Length;
        var hills = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++)
            if (passable[i] && ReferenceEquals(_main[i], TileLibrary.hills))
            {
                hills[i] = true;
                any = true;
            }
        if (!any) return null;
        int[] patch = Label(hills, out List<int> sizes);
        any = false;
        for (int i = 0; i < n; i++)
            if (hills[i] && sizes[patch[i]] < HillPatch) hills[i] = false;
            else any |= hills[i];
        return any ? Distance(hills) : null;
    }

    // 高原本地半宽：含这格的、整个落在高原(连同窄河)里的最大圆盘的半径，按 ThickLevels 分档(开运算：腐蚀到 t 再外扩 t)
    private static float[] Thickness()
    {
        int n = _region.Length;
        var outside = new bool[n];
        for (int i = 0; i < n; i++) outside[i] = !_region[i];
        float[] toOutside = Distance(outside);
        var thick = new float[n];
        var center = new bool[n];
        for (int i = 0; i < n; i++)
            if (_region[i]) thick[i] = 1f;
        foreach (float t in ThickLevels)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                center[i] = _region[i] && toOutside[i] >= t;
                any |= center[i];
            }
            if (!any) break;
            float[] reach = Distance(center);
            for (int i = 0; i < n; i++)
                if (_region[i] && reach[i] <= t + 0.01f) thick[i] = t;
        }
        return thick;
    }

    // 离成片丘陵不比离平地远 HillMargin 格以上：不起山(丘陵就是坡、就是口子)
    private static bool HillSide(int i) => _dHill != null && _lenient[i] && _dHill[i] <= _dIn[i] + HillMargin;

    // 荒漠、沙地(地表是荒漠，或地面是沙)：不对着它铺山脊
    private static bool Desert(WorldTile tile)
    {
        TileTypeBase type = tile?.Type;
        return type != null && (type.biome_id == "biome_desert" || type.sand || tile.main_type != null && tile.main_type.sand);
    }

    // 窄水面：5×5 里陆地占 NarrowLand 以上
    private static bool[] Narrow()
    {
        int n = _w * _h;
        var sum = new int[(_w + 1) * (_h + 1)];
        for (int y = 0; y < _h; y++)
        {
            int row = 0;
            for (int x = 0; x < _w; x++)
            {
                if (!_water[y * _w + x]) row++;
                sum[(y + 1) * (_w + 1) + x + 1] = sum[y * (_w + 1) + x + 1] + row;
            }
        }
        var narrow = new bool[n];
        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            int i = y * _w + x;
            if (!_water[i]) continue;
            int x0 = Math.Max(0, x - 2), y0 = Math.Max(0, y - 2), x1 = Math.Min(_w, x + 3), y1 = Math.Min(_h, y + 3);
            int land = sum[y1 * (_w + 1) + x1] - sum[y0 * (_w + 1) + x1] - sum[y1 * (_w + 1) + x0] + sum[y0 * (_w + 1) + x0];
            narrow[i] = land >= (x1 - x0) * (y1 - y0) * NarrowLand;
        }
        return narrow;
    }

    private static bool Touches(bool[] mask, int i)
    {
        int x = i % _w, y = i / _w;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < _w && ny < _h && mask[ny * _w + nx]) return true;
        }
        return false;
    }

    // 八方向连片
    private static int[] Label(bool[] mask, out List<int> sizes)
    {
        int n = mask.Length;
        var comp = new int[n];
        for (int i = 0; i < n; i++) comp[i] = -1;
        sizes = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < n; start++)
        {
            if (!mask[start] || comp[start] >= 0) continue;
            int id = sizes.Count, size = 0;
            comp[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                size++;
                int x = i % _w, y = i / _w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                    int j = ny * _w + nx;
                    if (!mask[j] || comp[j] >= 0) continue;
                    comp[j] = id;
                    stack.Push(j);
                }
            }
            sizes.Add(size);
        }
        return comp;
    }

    // 到最近的源格的距离(两遍倒角，近似直线距离)
    private static float[] Distance(bool[] source)
    {
        int n = _w * _h;
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = source[i] ? 0f : Inf;
        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            int i = y * _w + x;
            float v = d[i];
            if (v == 0f) continue;
            if (x > 0) v = Math.Min(v, d[i - 1] + 1f);
            if (y > 0)
            {
                v = Math.Min(v, d[i - _w] + 1f);
                if (x > 0) v = Math.Min(v, d[i - _w - 1] + Diagonal);
                if (x < _w - 1) v = Math.Min(v, d[i - _w + 1] + Diagonal);
            }
            d[i] = v;
        }
        for (int y = _h - 1; y >= 0; y--)
        for (int x = _w - 1; x >= 0; x--)
        {
            int i = y * _w + x;
            float v = d[i];
            if (v == 0f) continue;
            if (x < _w - 1) v = Math.Min(v, d[i + 1] + 1f);
            if (y < _h - 1)
            {
                v = Math.Min(v, d[i + _w] + 1f);
                if (x < _w - 1) v = Math.Min(v, d[i + _w + 1] + Diagonal);
                if (x > 0) v = Math.Min(v, d[i + _w - 1] + Diagonal);
            }
            d[i] = v;
        }
        return d;
    }

    // 噪声都按图上坐标取(画高原分窗口重算时同一格结果不变)
    private static float RidgeWidth(int x, int y)
    {
        x += _ox;
        y += _oy;
        return MinRidge + RidgeSpread * Noise.Fbm(x / 13f, y / 13f, _seed) + RidgeSwell * Noise.Value(x / 41f, y / 41f, _seed + 7);
    }

    private static float SlopeWidth(int x, int y) => MinSlope + SlopeSpread * Noise.Fbm((x + _ox) / 9f, (y + _oy) / 9f, _seed + 17);

    // 路线朝高原那头：落在高原上就是它，否则接到 EnterSearch 格内最近的高原格(不是水)
    private static Vector2Int IntoRegion(Vector2Int to)
    {
        int x = Mathf.Clamp(to.x, 1, _w - 2), y = Mathf.Clamp(to.y, 1, _h - 2);
        if (_region[y * _w + x]) return to;
        int? hit = Nearest(x, y, i => _region[i] && !_water[i], EnterSearch);
        return hit == null ? to : new Vector2Int(hit.Value % _w, hit.Value / _w);
    }

    // 找路：一格的代价(走不了为 Inf)
    private static float StepCost(int i)
    {
        if (_lava[i]) return Inf;
        if (_water[i]) return _narrow[i] ? 4f : Inf;
        if (_block[i]) return 8f;
        TileType main = _main[i];
        return main != null && main.rocks ? 1.6f : 1f;
    }

    // 沿最省的路开一条山口；谷道经过的高原外缘(山脊带)记给那一片高原(passed)
    private static bool Carve(Vector2Int from, Vector2Int to, HashSet<int> passed)
    {
        int fx = Mathf.Clamp(from.x, 1, _w - 2), fy = Mathf.Clamp(from.y, 1, _h - 2);
        int tx = Mathf.Clamp(to.x, 1, _w - 2), ty = Mathf.Clamp(to.y, 1, _h - 2);
        if (!Snap(ref fx, ref fy) || !Snap(ref tx, ref ty)) return false;
        if (fx == tx && fy == ty) return false;
        List<int> path = FindPath(fx, fy, tx, ty);
        if (path == null) return false;
        float ridgeBand = MinRidge + RidgeSpread;
        foreach (int k in path)
        {
            int px = k % _w, py = k / _w;
            float radius = PassRadius + PassSpread * Noise.Fbm((px + _ox) / 5f, (py + _oy) / 5f, _seed + 91);
            int r = Mathf.CeilToInt(radius);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int qx = px + dx, qy = py + dy;
                if (qx < 0 || qy < 0 || qx >= _w || qy >= _h) continue;
                int j = qy * _w + qx;
                if (!_corridor[j]) _carved?.Add(j);
                _corridor[j] = true;
                if (_block[j]) _opened[j] = true;
                if (_region[j] && _dIn[j] <= ridgeBand) passed.Add(_comp[j]);
            }
        }
        return true;
    }

    // 两格之间最省的一条路(八方向，代价见 StepCost)，经过的格(图上编号，从终点到起点)；走不通为 null
    private static List<int> FindPath(int fx, int fy, int tx, int ty)
    {
        int margin = 8 + (Math.Abs(fx - tx) + Math.Abs(fy - ty)) / 4;
        int x0 = Math.Max(0, Math.Min(fx, tx) - margin), x1 = Math.Min(_w - 1, Math.Max(fx, tx) + margin);
        int y0 = Math.Max(0, Math.Min(fy, ty) - margin), y1 = Math.Min(_h - 1, Math.Max(fy, ty) + margin);
        int ww = x1 - x0 + 1, wh = y1 - y0 + 1;
        if (ww * wh > MaxWindow) return null;
        var cost = new float[ww * wh];
        var prev = new int[ww * wh];
        for (int k = 0; k < cost.Length; k++)
        {
            cost[k] = Inf;
            prev[k] = -1;
        }
        int start = (fy - y0) * ww + (fx - x0), goal = (ty - y0) * ww + (tx - x0);
        var heap = new WorldMapZones.MinHeap();
        cost[start] = 0f;
        heap.Push(0f, start);
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int k);
            if (at > cost[k]) continue;
            if (k == goal) break;
            int lx = k % ww, ly = k / ww;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = lx + dx, ny = ly + dy;
                if (nx < 0 || ny < 0 || nx >= ww || ny >= wh) continue;
                float step = StepCost((ny + y0) * _w + nx + x0);
                if (step >= Inf) continue;
                float total = at + step * (dx != 0 && dy != 0 ? Diagonal : 1f);
                int nk = ny * ww + nx;
                if (total >= cost[nk]) continue;
                cost[nk] = total;
                prev[nk] = k;
                heap.Push(total, nk);
            }
        }
        if (cost[goal] >= Inf) return null;
        var path = new List<int>();
        for (int k = goal; k >= 0; k = prev[k])
        {
            path.Add((k / ww + y0) * _w + k % ww + x0);
            if (k == start) break;
        }
        return path;
    }

    // 铺完之后：高原格所在的那一片能走的地(八方向)里，高原以外能走的格不到 SealedMin 的算围死了(只剩山口接不出去的高原小块)，
    // 沿最省的路挖到 UnsealSearch 格内最近的、连着外面大片平地的那一片，路上的山改成丘陵(宽 3 格左右)
    private static (int unsealed, int dug) Unseal()
    {
        int n = _w * _h, unsealed = 0, dug = 0;
        for (int round = 0; round < 8; round++)
        {
            for (int i = 0; i < n; i++)
            {
                TileType main = _tiles[i]?.main_type;
                if (main == null) continue;
                _block[i] = main.block || main.mountains || main.summit;
                _walk[i] = !_water[i] && !_lava[i] && !_block[i];
            }
            int[] part = Label(_walk, out List<int> sizes);
            var plateauIn = new int[sizes.Count];
            var lowIn = new int[sizes.Count];
            var first = new int[sizes.Count];
            // 画高原时只管有画出来的格、不碰窗口边的(碰边的可能从窗口外通出去)
            var painted = new bool[sizes.Count];
            var border = new bool[sizes.Count];
            for (int c = 0; c < first.Length; c++) first[c] = -1;
            for (int i = 0; i < n; i++)
            {
                int c = part[i];
                if (c < 0) continue;
                if (PlateauTerrain.IsPlateau(_tiles[i].main_type))
                {
                    plateauIn[c]++;
                    if (first[c] < 0) first[c] = i;
                }
                else lowIn[c]++;
                if (_paint == null) continue;
                if (_eligible[i]) painted[c] = true;
                int x = i % _w, y = i / _w;
                if (x == 0 || y == 0 || x == _w - 1 || y == _h - 1) border[c] = true;
            }
            bool any = false;
            for (int c = 0; c < sizes.Count; c++)
            {
                if (plateauIn[c] < SealedMin || lowIn[c] >= SealedMin) continue;
                if (_paint != null && (!painted[c] || border[c])) continue;
                int start = first[c];
                int? to = Nearest(start % _w, start / _w, i => part[i] >= 0 && part[i] != c && lowIn[part[i]] >= SealedMin, UnsealSearch);
                if (to == null) continue;
                List<int> path = FindPath(start % _w, start / _w, to.Value % _w, to.Value / _w);
                if (path == null) continue;
                foreach (int k in path)
                {
                    int px = k % _w, py = k / _w;
                    // 画高原时挖出的路记作山口，以后重算不再在路上起山脊
                    _carved?.Add(k);
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx != 0 && dy != 0) continue;
                        int qx = px + dx, qy = py + dy;
                        if (qx < 0 || qy < 0 || qx >= _w || qy >= _h) continue;
                        int j = qy * _w + qx;
                        if (!_block[j]) continue;
                        Put(j, _tiles[j], TileLibrary.hills, null);
                        _block[j] = false;
                        // 两旁挖开的也记作山口(以前只记路当中一格，下一笔重算又把两旁铺回山，只剩斜着一格宽，四邻走不过)
                        _carved?.Add(j);
                        dug++;
                    }
                }
                unsealed++;
                any = true;
            }
            if (!any) break;
        }
        return (unsealed, dug);
    }

    // 路线一头落在水里、山里的，挪到附近能走的格
    private static bool Snap(ref int x, ref int y)
    {
        if (_walk[y * _w + x]) return true;
        for (int r = 1; r <= 6; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            int nx = x + dx, ny = y + dy;
            if (nx < 1 || ny < 1 || nx >= _w - 1 || ny >= _h - 1 || !_walk[ny * _w + nx]) continue;
            x = nx;
            y = ny;
            return true;
        }
        return false;
    }

    // 一片高原没有路线经过：挑 2~5 处山口(沿边每 160 格一处)，先挑外面能走的地最近(山最薄)的边，之后挑离已挑的最远的；
    // 画高原时只在画出来、离 Apply 窗口边够远的边上挑，1~5 处
    private static int AutoPasses(int comp, HashSet<int> passed)
    {
        var edge = new List<int>();
        for (int i = 0; i < _region.Length; i++)
            if (_region[i] && _comp[i] == comp && _dIn[i] <= 3f && PassSite(i)) edge.Add(i);
        if (edge.Count == 0)
        {
            // 四周全是厚山、海：挑离能走的地最近的边
            float best = Inf;
            int pick = -1;
            bool[] outside = OutsideLand();
            for (int i = 0; i < _region.Length; i++)
                if (_region[i] && _comp[i] == comp && _dIn[i] < best && PassSite(i) && Touches(outside, i))
                {
                    best = _dIn[i];
                    pick = i;
                }
            if (pick < 0 || best >= Inf) return 0;
            edge.Add(pick);
        }
        int want = Mathf.Clamp(Mathf.RoundToInt(edge.Count / 160f), _paint == null ? 2 : 1, 5);
        var chosen = new List<int>();
        var rng = new global::System.Random(_seed ^ (comp * 7919));
        // 第一处：山最薄的边里随便挑一处
        float thinnest = Inf;
        foreach (int i in edge) thinnest = Math.Min(thinnest, _dIn[i]);
        var thin = edge.FindAll(i => _dIn[i] <= thinnest + 0.5f);
        chosen.Add(thin[rng.Next(thin.Count)]);
        while (chosen.Count < want)
        {
            int pick = -1;
            float far = 0f;
            foreach (int i in edge)
            {
                float near = Inf;
                foreach (int c in chosen) near = Math.Min(near, Sq(i, c));
                float score = Mathf.Sqrt(near) - 4f * _dIn[i];
                if (score > far)
                {
                    far = score;
                    pick = i;
                }
            }
            // 边太短，挑不出隔得开的了
            if (pick < 0 || far < 24f) break;
            chosen.Add(pick);
        }
        int made = 0;
        float inner = MinRidge + RidgeSpread + 2f, outer = MinSlope + SlopeSpread + 2f;
        foreach (int c in chosen)
        {
            int cx = c % _w, cy = c / _w;
            int from = Nearest(cx, cy, i => _region[i] && _comp[i] == comp && _dIn[i] >= inner) ??
                       Nearest(cx, cy, i => _region[i] && _comp[i] == comp && !_water[i]) ?? c;
            int? to = Nearest(cx, cy, i => !_region[i] && _walk[i] && _dOut[i] >= outer, OuterSearch) ??
                      Nearest(cx, cy, i => !_region[i] && _walk[i], OuterSearch);
            if (to == null) continue;
            if (Carve(new Vector2Int(from % _w, from / _w), new Vector2Int(to.Value % _w, to.Value / _w), passed)) made++;
        }
        return made;
    }

    private static bool PassSite(int i) => _paint == null || _eligible[i] && InApply(i % _w, i / _w, PaintInset);

    // 高原外面的陆地(山也算)
    private static bool[] OutsideLand()
    {
        var mask = new bool[_region.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = !_region[i] && !_water[i] && !_lava[i];
        return mask;
    }

    private static float Sq(int a, int b)
    {
        int dx = a % _w - b % _w, dy = a / _w - b / _w;
        return dx * dx + dy * dy;
    }

    private static int? Nearest(int cx, int cy, Func<int, bool> ok, int reach = AutoSearch)
    {
        for (int r = 0; r <= reach; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            int x = cx + dx, y = cy + dy;
            if (x < 1 || y < 1 || x >= _w - 1 || y >= _h - 1) continue;
            int i = y * _w + x;
            if (ok(i)) return i;
        }
        return null;
    }

    // 先算每格该铺什么(Plan)，再核对没把哪块地围死(Connect，围住的挖口子)，最后写格：画高原时算下来不是外缘的、以前铺过的还原
    private static (int ridge, int slope, int opened, int linked) Apply()
    {
        int n = _w * _h;
        _plan = new TileType[n];
        _planTop = new TopTileType[n];
        _kind = new byte[n];
        for (int y = 1; y < _h - 1; y++)
        for (int x = 1; x < _w - 1; x++)
        {
            int i = y * _w + x;
            if (_tiles[i] == null || !InApply(x, y)) continue;
            Plan(i, x, y, _tiles[i]);
        }
        int linked = Connect();
        int ridge = 0, slope = 0, opened = 0;
        for (int y = 1; y < _h - 1; y++)
        for (int x = 1; x < _w - 1; x++)
        {
            int i = y * _w + x;
            WorldTile tile = _tiles[i];
            if (tile == null || !InApply(x, y)) continue;
            if (_plan[i] == null)
            {
                _paint?.Clear(Global(i));
                continue;
            }
            Put(i, tile, _plan[i], _planTop[i]);
            switch (_kind[i])
            {
                case KindRidge: ridge++; break;
                case KindSlope: slope++; break;
                case KindOpened: opened++; break;
            }
        }
        return (ridge, slope, opened, linked);
    }

    private static void Mark(int i, TileType main, TopTileType top, byte kind)
    {
        _plan[i] = main;
        _planTop[i] = top;
        _kind[i] = kind;
    }

    // 一格该不该是外缘，该就记下铺什么
    private static void Plan(int i, int x, int y, WorldTile tile)
    {
        TileType mountains = TileLibrary.mountains, summit = TileLibrary.summit, hills = TileLibrary.hills;
        TopTileType snowBlock = TopTileLibrary.snow_block, snowSummit = TopTileLibrary.snow_summit;
        int wx = x + _ox, wy = y + _oy;
        if (_corridor[i])
        {
            if (_opened[i]) Mark(i, hills, null, KindOpened);
            return;
        }
        if (_region[i])
        {
            // 画高原时只给画出来的高原格起山脊；丘陵那边不起
            if (_paint != null && !_eligible[i] || HillSide(i)) return;
            // 高原窄的地方山脊收窄，中间留一条高原面(细颈、窄条不被两边的山脊合拢切断)
            float width = Math.Min(RidgeWidth(x, y), Math.Max(MinRidge, _thick[i] - RidgeKeep));
            bool alpine = ReferenceEquals(_main[i], PlateauTerrain.Alpine) || _water[i] && NearAlpine(x, y);
            bool snowy = alpine && Noise.Fbm(wx / 6f, wy / 6f, _seed + 71) > 0.45f;
            if (_dIn[i] <= width)
            {
                // 宽水面(湖)只在最外两格封住，余下照留
                if (_water[i] && !_narrow[i] && _dIn[i] > MinRidge) return;
                bool peak = _dIn[i] >= 0.8f && _dIn[i] <= width - 0.5f && Noise.Fbm(wx / 4f, wy / 4f, _seed + 31) > 0.62f;
                Mark(i, peak ? summit : mountains, snowy ? (peak ? snowSummit : snowBlock) : null, KindRidge);
                return;
            }
            // 山嘴只伸进宽处
            if (!_water[i] && _dIn[i] < _thick[i] - 2f * RidgeKeep && Spur(wx, wy, _dIn[i], width))
                Mark(i, mountains, snowy ? snowBlock : null, KindRidge);
            return;
        }
        // 荒漠一侧不铺坡地(同山脊，见 Desert)；窄缝里不铺；画高原时缺口、玩家改过的坡地不铺
        if (_walk[i] && !_thin[i] && SoftLand(_main[i]) && !Desert(tile) && (_noSlope == null || !_noSlope[i]))
        {
            float width = SlopeWidth(x, y);
            if (_dOut[i] > width) return;
            // 坡地靠里那半边偶有孤山(不连成墙，坡地照样走得通)
            bool knob = _dOut[i] <= width * 0.6f && Noise.Fbm(wx / 5f, wy / 5f, _seed + 61) > KnobLevel;
            Mark(i, knob ? mountains : hills, null, KindSlope);
        }
    }

    // 这格最后是什么地：铺之前(画高原时以前铺的外缘按铺之前算)、铺之后；Apply 窗口外、图边一圈不改，照现在的
    private static bool Planned(int i)
    {
        int x = i % _w, y = i / _w;
        return x >= 1 && y >= 1 && x < _w - 1 && y < _h - 1 && _tiles[i] != null && InApply(x, y);
    }

    private static TileType Before(int i) => Planned(i) ? _main[i] : _tiles[i]?.main_type;
    private static TileType After(int i) => Planned(i) ? _plan[i] ?? _main[i] : _tiles[i]?.main_type;

    private static bool Walkable(TileType type) =>
        type != null && !type.liquid && !type.lava && !type.block && !type.mountains && !type.summit;

    // 自己铺的山(山脊、山嘴、坡地里的孤山)：挖得动
    private static bool Diggable(int i) => _plan[i] != null && (_plan[i].block || _plan[i].mountains || _plan[i].summit) && Planned(i);

    // 铺完核对(群友 2026-11-17 "出不去进不来")：铺之前四邻连着的能走的地(同 TerrainSystem.Crossable 按四邻判通不通)，铺之后被
    // 自己铺的山隔成了几块的，小块(不到 TinyPocket 格)是高原碎格就填成山，其余沿最省的路把山挖开 3 格宽接回最大那块。
    // 画高原时碰着窗口边的几块当作在窗口外连着。返回接回几处
    private static int Connect()
    {
        int n = _w * _h;
        var pre = new bool[n];
        var post = new bool[n];
        for (int i = 0; i < n; i++)
        {
            pre[i] = Walkable(Before(i));
            post[i] = Walkable(After(i));
        }
        int[] preComp = Label4(pre, out List<int> preSizes);
        int[] postComp = Label4(post, out List<int> postSizes);
        int pieces = postSizes.Count;
        var parent = new int[pieces];
        for (int c = 0; c < pieces; c++) parent[c] = c;
        int Find(int c)
        {
            while (parent[c] != c) c = parent[c] = parent[parent[c]];
            return c;
        }
        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) parent[a] = b;
        }
        // 画高原时碰着窗口边的块：到窗口外去找(Escapes)
        var border = new bool[pieces];
        if (_paint != null)
            for (int i = 0; i < n; i++)
            {
                int x = i % _w, y = i / _w;
                if (postComp[i] >= 0 && (x == 0 || y == 0 || x == _w - 1 || y == _h - 1)) border[postComp[i]] = true;
            }
        // 铺之前的每一片：铺之后落在它里面最大的那块为主
        var anchor = new int[preSizes.Count];
        for (int p = 0; p < anchor.Length; p++) anchor[p] = -1;
        var pairs = new HashSet<long>();
        for (int i = 0; i < n; i++)
        {
            if (!pre[i] || !post[i]) continue;
            int p = preComp[i], q = postComp[i];
            if (anchor[p] < 0 || postSizes[q] > postSizes[anchor[p]]) anchor[p] = q;
            pairs.Add((long)p * pieces + q);
        }
        // 各块的格(按块排好)
        var start = new int[pieces + 1];
        for (int i = 0; i < n; i++)
            if (postComp[i] >= 0) start[postComp[i] + 1]++;
        for (int c = 0; c < pieces; c++) start[c + 1] += start[c];
        var cells = new int[start[pieces]];
        var fill = (int[])start.Clone();
        for (int i = 0; i < n; i++)
            if (postComp[i] >= 0) cells[fill[postComp[i]]++] = i;

        var order = new List<long>(pairs);
        // 大块先接
        order.Sort((a, b) => postSizes[(int)(b % pieces)].CompareTo(postSizes[(int)(a % pieces)]));
        int linked = 0;
        foreach (long key in order)
        {
            int p = (int)(key / pieces), q = (int)(key % pieces);
            int main = anchor[p];
            if (main < 0 || Find(q) == Find(main)) continue;
            if (border[q] && Escapes(cells, start[q], start[q + 1], postComp, Find, Find(main)))
            {
                Union(q, main);
                continue;
            }
            if (postSizes[q] < TinyPocket)
            {
                if (FillPocket(cells, start[q], start[q + 1]))
                    for (int k = start[q]; k < start[q + 1]; k++) postComp[cells[k]] = -1;
                continue;
            }
            List<int> path = DigPath(cells, start[q], start[q + 1], postComp, Find, Find(main));
            if (path == null) continue;
            foreach (int k in path)
            {
                if (postComp[k] >= 0) Union(postComp[k], q);
                int kx = k % _w, ky = k / _w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int qx = kx + dx, qy = ky + dy;
                    if (qx < 0 || qy < 0 || qx >= _w || qy >= _h) continue;
                    int j = qy * _w + qx;
                    if (!Diggable(j)) continue;
                    // 高原上的还原成高原面，坡地里的孤山改丘陵；记作山口(画高原时以后重算照留)
                    if (_region[j]) Mark(j, null, null, KindNone);
                    else Mark(j, TileLibrary.hills, null, KindSlope);
                    _corridor[j] = true;
                    _carved?.Add(j);
                    // 挖开的格算这一块的(后面接别的块时能从这里走)
                    postComp[j] = q;
                }
            }
            Union(q, main);
            linked++;
        }
        return linked;
    }

    // 画高原时碰着窗口边的一块：出窗口(按图上现在的格)四邻走，走得回窗口里目标那一片(或走过 EscapeCap 格，当作连着外面大片)就算连着
    private const int EscapeCap = 200_000;

    private static bool Escapes(int[] cells, int from, int to, int[] postComp, Func<int, int> find, int goal)
    {
        MapBox world = World.world;
        int mw = MapBox.width, mh = MapBox.height;
        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        for (int k = from; k < to; k++)
        {
            int g = Global(cells[k]);
            if (seen.Add(g)) queue.Enqueue(g);
        }
        while (queue.Count > 0)
        {
            if (seen.Count > EscapeCap) return true;
            int g = queue.Dequeue(), gx = g % mw, gy = g / mw;
            int lx = gx - _ox, ly = gy - _oy;
            if (lx >= 0 && ly >= 0 && lx < _w && ly < _h)
            {
                int i = ly * _w + lx;
                if (postComp[i] >= 0 && find(postComp[i]) == goal) return true;
            }
            for (int d = 0; d < 4; d++)
            {
                int nx = gx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = gy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
                int ng = ny * mw + nx;
                if (seen.Contains(ng)) continue;
                int jx = nx - _ox, jy = ny - _oy;
                bool walk = jx >= 0 && jy >= 0 && jx < _w && jy < _h
                    ? Walkable(After(jy * _w + jx))
                    : Walkable(world.GetTileSimple(nx, ny)?.main_type);
                if (!walk) continue;
                seen.Add(ng);
                queue.Enqueue(ng);
            }
        }
        return false;
    }

    // 被围住的高原碎格(整块都是这回能铺的高原格)填成山
    private static bool FillPocket(int[] cells, int from, int to)
    {
        for (int k = from; k < to; k++)
        {
            int i = cells[k];
            if (!_region[i] || _water[i] || !Planned(i) || _paint != null && !_eligible[i] || _corridor[i]) return false;
        }
        for (int k = from; k < to; k++)
        {
            int i = cells[k];
            bool snowy = ReferenceEquals(_main[i], PlateauTerrain.Alpine) &&
                         Noise.Fbm((i % _w + _ox) / 6f, (i / _w + _oy) / 6f, _seed + 71) > 0.45f;
            Mark(i, TileLibrary.mountains, snowy ? TopTileLibrary.snow_block : null, KindRidge);
        }
        return true;
    }

    // 从这一块出发(四邻)，走能走的格代价 1、挖自己铺的山代价 DigCost，到目标那一片(并查集的根为 goal)最省的一条路；
    // 经过的格从终点到起点；找不到为 null
    private static List<int> DigPath(int[] cells, int from, int to, int[] postComp, Func<int, int> find, int goal)
    {
        var cost = new Dictionary<int, float>();
        var prev = new Dictionary<int, int>();
        var heap = new WorldMapZones.MinHeap();
        for (int k = from; k < to; k++)
        {
            cost[cells[k]] = 0f;
            heap.Push(0f, cells[k]);
        }
        int pops = 0;
        while (heap.Count > 0 && pops++ < DigSearch)
        {
            heap.Pop(out float at, out int i);
            if (at > cost[i]) continue;
            if (postComp[i] >= 0 && find(postComp[i]) == goal)
            {
                var path = new List<int>();
                for (int k = i; ; k = prev[k])
                {
                    path.Add(k);
                    if (!prev.ContainsKey(k)) break;
                }
                return path;
            }
            int x = i % _w, y = i / _w;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                int j = ny * _w + nx;
                float step = postComp[j] >= 0 ? 1f : Diggable(j) ? DigCost : Inf;
                if (step >= Inf) continue;
                float total = at + step;
                if (cost.TryGetValue(j, out float old) && total >= old) continue;
                cost[j] = total;
                prev[j] = i;
                heap.Push(total, j);
            }
        }
        return null;
    }

    // 四邻连片
    private static int[] Label4(bool[] mask, out List<int> sizes)
    {
        int n = mask.Length;
        var comp = new int[n];
        for (int i = 0; i < n; i++) comp[i] = -1;
        sizes = new List<int>();
        var stack = new Stack<int>();
        for (int s = 0; s < n; s++)
        {
            if (!mask[s] || comp[s] >= 0) continue;
            int id = sizes.Count, size = 0;
            comp[s] = id;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                size++;
                int x = i % _w, y = i / _w;
                if (x > 0) Visit(i - 1);
                if (x < _w - 1) Visit(i + 1);
                if (y > 0) Visit(i - _w);
                if (y < _h - 1) Visit(i + _w);
            }
            sizes.Add(size);

            void Visit(int j)
            {
                if (!mask[j] || comp[j] >= 0) return;
                comp[j] = id;
                stack.Push(j);
            }
        }
        return comp;
    }

    // 山嘴：山脊往高原里伸的几道细长山梁(脊状噪声的峰线)；x、y 是图上坐标
    private static bool Spur(int x, int y, float dIn, float width)
    {
        if (dIn <= width || dIn > width + SpurReach * Noise.Fbm(x / 13f, y / 13f, _seed + 43)) return false;
        float v = Noise.Value(x / 7f, y / 7f, _seed + 53);
        return 1f - Math.Abs(2f * v - 1f) > 0.86f;
    }

    private static bool NearAlpine(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < _w && ny < _h && ReferenceEquals(_main[ny * _w + nx], PlateauTerrain.Alpine)) return true;
        }
        return false;
    }

    // 坡地只铺在平地上(土、草)：沙滩、丘陵、山、高原、水不动
    private static bool SoftLand(TileType main) =>
        main != null && main.ground && !main.rocks && !main.block && !main.liquid && !main.lava && !main.sand &&
        !PlateauTerrain.IsPlateau(main) && main.id != "sand";

    private static void Set(WorldTile tile, TileType main, TopTileType top)
    {
        if (main == null) return;
        tile.setTileTypes(main, top);
        tile.Height = main.height_min;
    }

    // 平滑的值噪声与分形叠加(0~1)：同一种子同一处结果不变
    public static class Noise
    {
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        public static float Value(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float x, float y, int seed) =>
            Value(x, y, seed) * 0.6f + Value(x * 2.13f, y * 2.13f, seed + 1) * 0.28f + Value(x * 4.37f, y * 4.37f, seed + 2) * 0.12f;
    }
}
