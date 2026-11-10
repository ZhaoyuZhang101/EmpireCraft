using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems;

// 神力画高原的账(用户 2026-11-10 "放的时候边缘也要自动铺设自然山体")：每画完一笔(见 PlateauBrush)在新画的那片附近重算外缘
// (PlateauShaper.ShapePainted)。账上记：
//   · 画出来的高原格(从别的地形画成高原的；原本就是高原、只换了一种高原的不算，不在原有高原里起山)；
//   · 画高原铺的外缘(山脊、坡地、山口里开的山)：每格铺之前的样子，重算时当没铺过算，算下来不该有的还原；
//   · 玩家改过的外缘格：铺好的山脊被画成别的(擦出缺口)记作缺口——不算外面，背后不再起山，也不铺坡地；铺好的坡地被改了不再铺；
//   · 山口：这一片高原开过的谷道，以后重算照留。
// 只在这一局里记(换图、读档清空)；读档后接着画，旧的外缘不再退，照样在新画的边上起山脊。
public static class PlateauPaintLedger
{
    private readonly struct Placed
    {
        public readonly TileType BeforeMain;
        public readonly TopTileType BeforeTop;
        public readonly int BeforeHeight;
        public readonly TileType After;

        public Placed(TileType beforeMain, TopTileType beforeTop, int beforeHeight, TileType after)
        {
            BeforeMain = beforeMain;
            BeforeTop = beforeTop;
            BeforeHeight = beforeHeight;
            After = after;
        }

        public Placed With(TileType after) => new(BeforeMain, BeforeTop, BeforeHeight, after);
    }

    private static readonly HashSet<int> Painted = new(), Openings = new(), Kept = new(), Passes = new();
    private static readonly Dictionary<int, Placed> Rim = new();
    private static int _width, _height;

    // 改格子(运行时换成原版改地形，压到的建筑照原版拆；回归测试里直接设格)
    public static Action<WorldTile, TileType, TopTileType> Writer;

    // 外缘起伏的种子：固定，同一处每次重算结果一样
    public const int Seed = 20261111;
    // Apply 窗口：这一笔画出来的格外扩多少(外缘、山嘴最远波及 RidgeWidth+SpurReach 约 15 格)；Scan 窗口再外扩多少(算距离要看得够远)
    private const int Reach = 18, Context = 30;

    public static int PaintedCount => Painted.Count;
    public static int RimCount => Rim.Count;

    public static void Reset()
    {
        Painted.Clear();
        Openings.Clear();
        Kept.Clear();
        Passes.Clear();
        Rim.Clear();
        _width = _height = 0;
    }

    // 这一笔从别的地形画成高原的格(图上格号 y*宽+x)；返回改了几格
    public static int Finish(ICollection<int> grown)
    {
        MapBox world = World.world;
        if (world == null || grown == null || grown.Count == 0) return 0;
        int w = MapBox.width, h = MapBox.height;
        if (w != _width || h != _height)
        {
            Reset();
            _width = w;
            _height = h;
        }
        Settle(world, w);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        foreach (int g in grown)
        {
            if (g < 0 || g >= w * h) continue;
            int x = g % w, y = g / w;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
            Painted.Add(g);
            // 画回高原：不再是缺口、不再挡坡地，原来的山口那一段也让给高原
            Openings.Remove(g);
            Kept.Remove(g);
            Passes.Remove(g);
        }
        if (x1 < 0) return 0;
        var paint = new PlateauShaper.Painting
        {
            ApplyX0 = x0 - Reach, ApplyY0 = y0 - Reach, ApplyX1 = x1 + Reach, ApplyY1 = y1 + Reach,
            ScanX0 = x0 - Reach - Context, ScanY0 = y0 - Reach - Context, ScanX1 = x1 + Reach + Context, ScanY1 = y1 + Reach + Context,
            Base = g => Rim.TryGetValue(g, out Placed p) ? p.BeforeMain : null,
            Painted = Painted.Contains,
            Opening = Openings.Contains,
            Keep = Kept.Contains,
            Pass = Passes.Contains,
            Put = (g, main, top) => Put(world, w, g, main, top),
            Clear = g => Clear(world, w, g)
        };
        int changed = PlateauShaper.ShapePainted(paint, Seed);
        foreach (int g in paint.Carved) Passes.Add(g);
        return changed;
    }

    // 清账：铺下的外缘格被改过(地面格不是铺的那样了；积雪这类顶层变化不算)的不再归外缘管
    private static void Settle(MapBox world, int w)
    {
        List<int> gone = null;
        foreach (KeyValuePair<int, Placed> pair in Rim)
        {
            WorldTile tile = world.GetTileSimple(pair.Key % w, pair.Key / w);
            if (tile == null || ReferenceEquals(tile.main_type, pair.Value.After)) continue;
            (gone ??= new List<int>()).Add(pair.Key);
            if (PlateauTerrain.IsPlateau(tile.main_type)) continue;
            if (PlateauTerrain.IsPlateau(pair.Value.BeforeMain)) Openings.Add(pair.Key);
            else Kept.Add(pair.Key);
        }
        if (gone != null)
            foreach (int g in gone) Rim.Remove(g);
    }

    private static void Put(MapBox world, int w, int g, TileType main, TopTileType top)
    {
        WorldTile tile = world.GetTileSimple(g % w, g / w);
        if (tile == null || main == null) return;
        if (Rim.TryGetValue(g, out Placed placed))
        {
            if (ReferenceEquals(placed.After, main) && ReferenceEquals(tile.main_type, main)) return;
            Rim[g] = placed.With(main);
        }
        else
        {
            if (ReferenceEquals(tile.main_type, main)) return;
            Rim[g] = new Placed(tile.main_type, tile.top_type, tile.Height, main);
        }
        Write(tile, main, top);
        tile.Height = main.height_min;
    }

    private static void Clear(MapBox world, int w, int g)
    {
        if (!Rim.TryGetValue(g, out Placed placed)) return;
        Rim.Remove(g);
        WorldTile tile = world.GetTileSimple(g % w, g / w);
        if (tile == null || placed.BeforeMain == null) return;
        Write(tile, placed.BeforeMain, placed.BeforeTop);
        tile.Height = placed.BeforeHeight;
    }

    private static void Write(WorldTile tile, TileType main, TopTileType top)
    {
        if (Writer != null) Writer(tile, main, top);
        else tile.setTileTypes(main, top);
    }
}
