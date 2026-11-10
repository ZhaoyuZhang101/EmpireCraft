using System;
using System.Collections.Generic;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 随机地图生成高原(用户 2026-11-10 "要考虑尤其随机地图也能用"；模组设置「随机地图生成高原」默认开)：
// 原版按高度定完格子、撒生物群系之前(见 GamePatches.PlateauPatch)，挑一两片成片的高地抬成高原，再铺外缘山脊、坡地、山口(PlateauShaper)：
//   · 挑地：离水(海、湖)至少 CoastGap 格的陆地，按周围 SmoothRadius 格的平均高度从高往低取，取到约占陆地 TargetShare 时连片，
//     最大的一片(大图再加第二片)够 MinShare 才做；一片里的山、山巅照留(高原里的山岭)，其余陆地(连同丘陵)抬成高原，
//     高原里被围住的小块低地一并抬起(不留一圈山围着的坑)；
//   · 哪一种：最高的那片原来平均高度到 AlpineHeight 的是高寒高原，否则黄土高原；第二片总是黄土高原(两片不全是牧区)；
//   · 特殊模板(蚁穴、棋盘、隔间)、去山模板、陆地太少的图不做。只在新开随机图时做一次，之后神力随便改。
public static class PlateauGenerator
{
    public static bool Enabled = true;

    private const float TargetShare = 0.06f, MinShare = 0.015f, AlpineHeight = 175f;
    private const int CoastGap = 10, SmoothRadius = 6, MinLand = 20_000, MinTiles = 400, SecondLand = 350_000, HoleMax = 1500;

    public static void Generate()
    {
        if (!Enabled || World.world == null || PlateauTerrain.Alpine == null || PlateauTerrain.Loess == null) return;
        MapGenTemplate template = MapGenerator.template;
        if (template == null || template.special_anthill || template.special_checkerboard || template.special_cubicles ||
            template.values == null || template.values.remove_mountains) return;
        int w = MapBox.width, h = MapBox.height, n = w * h;
        if (n <= 0) return;
        var tiles = new WorldTile[n];
        var land = new bool[n];
        var wet = new bool[n];
        var heights = new float[n];
        int landCount = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            WorldTile tile = World.world.GetTileSimple(x, y);
            tiles[i] = tile;
            TileType main = tile?.main_type;
            if (main == null) continue;
            wet[i] = main.liquid;
            land[i] = !main.liquid;
            heights[i] = tile.Height;
            if (land[i]) landCount++;
        }
        if (landCount < MinLand) return;
        float[] toWater = Chamfer(wet, w, h);
        float[] smooth = BoxMean(heights, w, h, SmoothRadius);
        var candidates = new List<float>();
        for (int i = 0; i < n; i++)
            if (land[i] && toWater[i] >= CoastGap) candidates.Add(smooth[i]);
        if (candidates.Count == 0) return;
        candidates.Sort((a, b) => b.CompareTo(a));
        int minSize = Math.Max(MinTiles, (int)(landCount * MinShare));
        int want = landCount >= SecondLand ? 2 : 1;
        List<List<int>> picked = null;
        foreach (float scale in new[] { 1.5f, 2.5f })
        {
            float threshold = candidates[Math.Min(candidates.Count - 1, (int)(landCount * TargetShare * scale))];
            var mask = new bool[n];
            for (int i = 0; i < n; i++) mask[i] = land[i] && toWater[i] >= CoastGap && smooth[i] >= threshold;
            picked = Pieces(mask, w, h, minSize, want);
            if (picked.Count > 0) break;
        }
        if (picked == null || picked.Count == 0) return;
        int made = 0;
        for (int k = 0; k < picked.Count; k++)
        {
            List<int> piece = picked[k];
            float mean = 0f;
            foreach (int i in piece) mean += heights[i];
            mean /= piece.Count;
            TileType kind = k == 0 && mean >= AlpineHeight ? PlateauTerrain.Alpine : PlateauTerrain.Loess;
            var inside = new bool[n];
            foreach (int i in piece) inside[i] = true;
            FillHoles(inside, land, w, h);
            for (int i = 0; i < n; i++)
            {
                if (!inside[i] || !land[i]) continue;
                TileType main = tiles[i].main_type;
                if (main == null || main.block || main.mountains || main.summit || main.lava) continue;
                tiles[i].setTileTypes(kind, null);
                tiles[i].Height = kind.height_min;
                made++;
            }
            LogService.LogInfo($"[EmpireCraft][高原] 随机图第 {k + 1} 片高原：{(kind == PlateauTerrain.Alpine ? "高寒" : "黄土")}，原平均高度 {mean:0}，{piece.Count} 格");
        }
        if (made > 0) PlateauShaper.Shape(null, Randy.randomInt(1, 1 << 30));
    }

    // 连片(八方向)，取最大的 want 片，每片至少 minSize 格
    private static List<List<int>> Pieces(bool[] mask, int w, int h, int minSize, int want)
    {
        int n = mask.Length;
        var seen = new bool[n];
        var pieces = new List<List<int>>();
        var stack = new Stack<int>();
        for (int start = 0; start < n; start++)
        {
            if (!mask[start] || seen[start]) continue;
            var piece = new List<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                piece.Add(i);
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (!mask[j] || seen[j]) continue;
                    seen[j] = true;
                    stack.Push(j);
                }
            }
            if (piece.Count >= minSize) pieces.Add(piece);
        }
        pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
        if (pieces.Count > want) pieces.RemoveRange(want, pieces.Count - want);
        return pieces;
    }

    // 被这片高原围住(四方向连片碰不到图边、不到 HoleMax 格)的地并进来
    private static void FillHoles(bool[] inside, bool[] land, int w, int h)
    {
        int n = inside.Length;
        var seen = new bool[n];
        var stack = new Stack<int>();
        var part = new List<int>();
        for (int start = 0; start < n; start++)
        {
            if (inside[start] || seen[start]) continue;
            part.Clear();
            bool open = false;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                part.Add(i);
                int x = i % w, y = i / w;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) open = true;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            if (!open && part.Count <= HoleMax)
                foreach (int i in part)
                    if (land[i]) inside[i] = true;
        }

        void Visit(int j)
        {
            if (inside[j] || seen[j]) return;
            seen[j] = true;
            stack.Push(j);
        }
    }

    private static float[] Chamfer(bool[] source, int w, int h)
    {
        const float inf = 1e9f, diagonal = 1.41421356f;
        int n = w * h;
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = source[i] ? 0f : inf;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            float v = d[i];
            if (x > 0) v = Math.Min(v, d[i - 1] + 1f);
            if (y > 0)
            {
                v = Math.Min(v, d[i - w] + 1f);
                if (x > 0) v = Math.Min(v, d[i - w - 1] + diagonal);
                if (x < w - 1) v = Math.Min(v, d[i - w + 1] + diagonal);
            }
            d[i] = v;
        }
        for (int y = h - 1; y >= 0; y--)
        for (int x = w - 1; x >= 0; x--)
        {
            int i = y * w + x;
            float v = d[i];
            if (x < w - 1) v = Math.Min(v, d[i + 1] + 1f);
            if (y < h - 1)
            {
                v = Math.Min(v, d[i + w] + 1f);
                if (x < w - 1) v = Math.Min(v, d[i + w + 1] + diagonal);
                if (x > 0) v = Math.Min(v, d[i + w - 1] + diagonal);
            }
            d[i] = v;
        }
        return d;
    }

    // 周围 (2r+1)² 格的平均(积分图)
    private static float[] BoxMean(float[] values, int w, int h, int r)
    {
        var sum = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double row = 0d;
            for (int x = 0; x < w; x++)
            {
                row += values[y * w + x];
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + row;
            }
        }
        var mean = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r), x1 = Math.Min(w, x + r + 1), y1 = Math.Min(h, y + r + 1);
            double total = sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
            mean[y * w + x] = (float)(total / ((x1 - x0) * (y1 - y0)));
        }
        return mean;
    }
}
