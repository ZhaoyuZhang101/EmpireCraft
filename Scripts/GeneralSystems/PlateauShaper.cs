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

    private static int _w, _h;
    private static WorldTile[] _tiles;
    private static bool[] _region, _water, _narrow, _lava, _block, _walk, _corridor, _opened;
    private static float[] _dIn, _dOut;
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

    public static int Shape(IList<Route> routes, int seed)
    {
        MapBox world = World.world;
        if (world == null || PlateauTerrain.Alpine == null || PlateauTerrain.Loess == null) return 0;
        _ox = _oy = 0;
        _paint = null;
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
            (int ridge, int slope, int opened) = Apply();
            (int unsealed, int dug) = Unseal();
            LogService.LogInfo($"[EmpireCraft][高原] 外缘：山脊 {ridge} 格、坡地 {slope} 格、山口里开山 {opened} 格；路线山口 {passes} 条、" +
                               $"自动山口 {auto} 条；围死的高原小块挖通 {unsealed} 处({dug} 格)");
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
            (int ridge, int slope, int opened) = Apply();
            (int unsealed, int dug) = Unseal();
            foreach (int i in _carved) paint.Carved.Add(Global(i));
            if (ridge + slope + opened + dug > 0)
                LogService.LogInfo($"[EmpireCraft][高原] 神力画高原：山脊 {ridge} 格、坡地 {slope} 格、山口里开山 {opened} 格；" +
                                   $"自动山口 {auto} 条；围死的挖通 {unsealed} 处({dug} 格)");
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
        _dIn = Distance(passable);
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

    private static (int ridge, int slope, int opened) Apply()
    {
        int ridge = 0, slope = 0, opened = 0;
        for (int y = 1; y < _h - 1; y++)
        for (int x = 1; x < _w - 1; x++)
        {
            int i = y * _w + x;
            WorldTile tile = _tiles[i];
            if (tile == null || !InApply(x, y)) continue;
            // 画高原时这格算下来不是外缘：以前铺过的还原
            if (!Rim(i, x, y, tile, ref ridge, ref slope, ref opened)) _paint?.Clear(Global(i));
        }
        return (ridge, slope, opened);
    }

    // 一格该不该是外缘，该就铺上(返回真)
    private static bool Rim(int i, int x, int y, WorldTile tile, ref int ridge, ref int slope, ref int opened)
    {
        TileType mountains = TileLibrary.mountains, summit = TileLibrary.summit, hills = TileLibrary.hills;
        TopTileType snowBlock = TopTileLibrary.snow_block, snowSummit = TopTileLibrary.snow_summit;
        int wx = x + _ox, wy = y + _oy;
        if (_corridor[i])
        {
            if (!_opened[i]) return false;
            Put(i, tile, hills, null);
            opened++;
            return true;
        }
        if (_region[i])
        {
            // 画高原时只给画出来的高原格起山脊
            if (_paint != null && !_eligible[i]) return false;
            float width = RidgeWidth(x, y);
            bool alpine = ReferenceEquals(_main[i], PlateauTerrain.Alpine) || _water[i] && NearAlpine(x, y);
            bool snowy = alpine && Noise.Fbm(wx / 6f, wy / 6f, _seed + 71) > 0.45f;
            if (_dIn[i] <= width)
            {
                // 宽水面(湖)只在最外两格封住，余下照留
                if (_water[i] && !_narrow[i] && _dIn[i] > MinRidge) return false;
                bool peak = _dIn[i] >= 0.8f && _dIn[i] <= width - 0.5f && Noise.Fbm(wx / 4f, wy / 4f, _seed + 31) > 0.62f;
                Put(i, tile, peak ? summit : mountains, snowy ? (peak ? snowSummit : snowBlock) : null);
                ridge++;
                return true;
            }
            if (!_water[i] && Spur(wx, wy, _dIn[i], width))
            {
                Put(i, tile, mountains, snowy ? snowBlock : null);
                ridge++;
                return true;
            }
            return false;
        }
        // 荒漠一侧不铺坡地(同山脊，见 Desert)；画高原时缺口、玩家改过的坡地不铺
        if (_walk[i] && SoftLand(_main[i]) && !Desert(tile) && (_noSlope == null || !_noSlope[i]))
        {
            float width = SlopeWidth(x, y);
            if (_dOut[i] > width) return false;
            // 坡地靠里那半边偶有孤山(不连成墙，坡地照样走得通)
            bool knob = _dOut[i] <= width * 0.6f && Noise.Fbm(wx / 5f, wy / 5f, _seed + 61) > KnobLevel;
            Put(i, tile, knob ? mountains : hills, null);
            slope++;
            return true;
        }
        return false;
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
