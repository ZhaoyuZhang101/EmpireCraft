using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市精细层：城郊过渡带(用户 2026-10-16："城市区和背景的地形不存在什么过渡"，演示版看过说"可以，移植进游戏")。
// 从城边(精细图上画了城的格子：街坊、城墙、大道、新城区)往外量格数，加一点噪声，边不跟着格子走：
//   · 一格多以内：城根下的土院子，稀稀拉拉几户人家；
//   · 三格多以内：菜园(一垄垄)、果园(一行行树)、空院；
//   · 六格半以内：一小块一小块的条田(见 FieldColor，越远越稀，块间留空地)、一丛丛树林；
//   · 地面颜色从城边的土黄绿慢慢过渡回国色。
// 只画在本城或无主的陆地上；画在村镇、田块、小路之前，它们照旧压在上面。
public partial class PureMapPatch
{
    private const int FringeReach = 7;
    private static readonly Color32 FringeSoft = Rgb(178, 192, 128), FringeVegA = Rgb(104, 156, 86), FringeVegB = Rgb(146, 188, 106),
        FringeOrchard = Rgb(164, 192, 118), FringeOrchardTree = Rgb(64, 112, 58), FringeWood = Rgb(72, 120, 64), FringeWoodLit = Rgb(96, 146, 80);

    // 二维值噪声(0~1)
    private static float DetailNoise2(float x, float y, float cell, long seed)
    {
        float fx = x / cell, fy = y / cell;
        int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
        float sx = fx - ix, sy = fy - iy;
        sx = sx * sx * (3f - 2f * sx);
        sy = sy * sy * (3f - 2f * sy);
        float a = DetailHash(seed, ix, iy), b = DetailHash(seed, ix + 1, iy), c = DetailHash(seed, ix, iy + 1), d = DetailHash(seed, ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
    }

    // 城郊的条田：每 16×12 像素一格，格里一块八到十二像素宽、五到八像素高的田(位置随机，四周留空地)；
    // 格子正中离城边三格半到六格半才种，越远越稀
    private static bool FringeField(int[] dist, int w, int h, int px, int py, long seed, out bool vertical)
    {
        const int cw = 16, ch = 12;
        int s = DetailScale, shift = py / ch * 5, col = (px + shift) / cw, row = py / ch, ix = (px + shift) % cw, iy = py % ch;
        vertical = DetailHash(seed, col, row, 605) < 0.4f;
        int fw = 8 + (int)(DetailHash(seed, col, row, 607) * 5), fh = 5 + (int)(DetailHash(seed, col, row, 608) * 4);
        int ox = 1 + (int)(DetailHash(seed, col, row, 609) * (cw - fw - 1)), oy = 1 + (int)(DetailHash(seed, col, row, 610) * (ch - fh - 1));
        if (ix < ox || ix >= ox + fw || iy < oy || iy >= oy + fh) return false;
        int mx = col * cw - shift + cw / 2, my = row * ch + ch / 2;
        int tx = Mathf.Clamp(mx / s, 0, w - 1), ty = Mathf.Clamp(my / s, 0, h - 1);
        int baseDist = dist[ty * w + tx];
        if (baseDist <= 0 || baseDist > FringeReach) return false;
        float d = baseDist - 0.5f + (DetailNoise2(mx, my, 7f, seed + 600) - 0.5f) * 2.2f;
        return d >= 3.4f && d <= 6.5f && DetailHash(seed, col, row, 604) <= 1f - (d - 3.4f) / 3.1f * 0.75f;
    }

    private static void DrawFringe(Canvas c, DetailStyle style, Color32 tint, long seed)
    {
        int s = DetailScale, w = c.W, h = c.H;
        // 每格离城边几格(四邻广度优先，至多 FringeReach)
        var dist = new int[w * h];
        var queue = new Queue<int>();
        for (int ty = 0; ty < h; ty++)
        for (int tx = 0; tx < w; tx++)
        {
            int i = ty * w + tx;
            byte kind = c.Kind(tx * s + s / 2, ty * s + s / 2);
            bool city = kind is not (KEmpty or KWater or KOff or KPath);
            dist[i] = city ? 0 : int.MaxValue;
            if (city) queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w, d = dist[i];
            if (d >= FringeReach) continue;
            void Visit(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                int j = ny * w + nx;
                if (dist[j] <= d + 1) return;
                dist[j] = d + 1;
                queue.Enqueue(j);
            }
            Visit(x + 1, y);
            Visit(x - 1, y);
            Visit(x, y + 1);
            Visit(x, y - 1);
        }
        Color32 yard = Lerp(tint, Rgb(212, 198, 164), 0.7f);
        for (int py = 0; py < c.PH; py++)
        for (int px = 0; px < c.PW; px++)
        {
            if (c.Kind(px, py) != KEmpty || !c.Land(px, py)) continue;
            int baseDist = dist[py / s * w + px / s];
            if (baseDist <= 0 || baseDist > FringeReach) continue;
            float d = baseDist - 0.5f + (DetailNoise2(px, py, 7f, seed + 600) - 0.5f) * 2.2f;
            // 条田：按格子定一块田的大小、位置与种不种(看格子正中离城边多远)，块里的像素整块画，不随每个像素的远近参差
            if (d >= 3.4f && FringeField(dist, w, h, px, py, seed, out bool vertical))
            {
                c.Put(px, py, FieldColor(tint, px, py, vertical), KEmpty);
                continue;
            }
            if (d > 6.5f) continue;
            // 地色往城边过渡：城边是土黄绿，往外慢慢回到国色
            float k = Mathf.Max(0f, 1f - d / 6.5f);
            Color32 ground = Lerp(tint, FringeSoft, 0.78f * k * k * (3f - 2f * k));
            c.Put(px, py, ground, KEmpty);
            if (d < 1.2f)
            {
                c.Put(px, py, (px * 7 + py * 3) % 11 == 0 ? Darker(yard, 0.92f) : yard, KEmpty);
                continue;
            }
            if (d < 3.4f)
            {
                // 菜园、果园、空院：一畦畦错开的小块，块间留一道埂
                const int cw = 7, ch = 6;
                int shift = py / ch * 3, col = (px + shift) / cw, row = py / ch;
                if ((px + shift) % cw == 0 || py % ch == 0)
                {
                    c.Put(px, py, Darker(ground, 0.9f), KEmpty);
                    continue;
                }
                float r = DetailHash(seed, col, row, 601);
                if (r < 0.5f) c.Put(px, py, py % 2 == 0 ? FringeVegA : FringeVegB, KEmpty);
                else if (r < 0.78f) c.Put(px, py, px % 3 == 1 && py % 3 == 1 ? FringeOrchardTree : FringeOrchard, KEmpty);
                else if (r < 0.9f) c.Put(px, py, yard, KEmpty);
                continue;
            }
            // 树林、田块(越远越稀)
            if (DetailNoise2(px, py, 9f, seed + 602) > 0.66f)
            {
                c.Put(px, py, DetailHash(seed, px, py, 603) < 0.5f ? FringeWood : FringeWoodLit, KEmpty);
                continue;
            }
        }
        // 城根下几户人家
        for (int py = 2; py < c.PH; py += 6)
        for (int px = 2; px < c.PW; px += 7)
        {
            int d = dist[Mathf.Min(h - 1, py / s) * w + Mathf.Min(w - 1, px / s)];
            if (d < 1 || d > 2 || DetailHash(seed, px, py, 606) > 0.55f * (1f - (d - 1) / 1.5f)) continue;
            SmallHouse(c, style, px, py, seed + px * 31L + py, DetailHash(seed, px, py, 607) < 0.4f);
        }
    }
}
