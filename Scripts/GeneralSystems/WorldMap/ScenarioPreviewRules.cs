using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本编辑器预览与改动的规则部分(不碰世界和界面，见 ScenarioWilderness.Preview、ScenarioEditorScreen.RenderFill、ScenarioEditing)：
//   · 输入框没改就不动数据：框里显示的是当前语言那一段，存的是"简体|英文|繁体"，直接比较总不相等；
//   · 地图边长(格)取整到区块；
//   · 从城心和刷的地块一起往外铺(同 WorldMapZones.Fill：各城走得远近、地块数有上限，不进无人区、不过长城)，铺不到的陆地开局补小国；
//   · 平滑地块：一格看周围四块地，按远近给权重，归权重最大的那一家，方块的台阶磨成斜线；
//   · 长城压着哪些地块；
//   · 擦掉地块回到导入时的归属。
internal static class ScenarioPreviewRules
{
    public const int Unclaimed = -1, Filler = -2;
    // 八方向(同 WorldMapZones.Neighbours)：前四个正走，后四个斜走
    private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public static string KeepIfSame(string raw, string input, Func<string, string> shown)
    {
        if (input == null) return raw;
        string text = input.Trim();
        string current = raw == null ? "" : (shown(raw) ?? "").Trim();
        if (text == current) return raw;
        return text.Length == 0 ? null : text;
    }

    public static int ChunksOf(int tiles, int chunkTiles, int min, int max)
    {
        int chunks = (int)Math.Round(tiles / (double)chunkTiles, MidpointRounding.AwayFromZero);
        return Math.Max(min, Math.Min(max, chunks));
    }

    // owner：城心、刷的地块已填好(城的下标)，其余为 Unclaimed；cost 为走进这块地的代价(0 过不去，水面)；
    // wild、wall 进不去；budget、cap 为各城走得多远、领几块(已有的地算在内)。
    // 铺完：压着长城的地块归挨着的城；还没人领的陆地记 Filler
    public static void Spread(int width, int height, int[] owner, int[] cost, bool[] wild, bool[] wall, int[] budget, int[] cap)
    {
        int n = width * height;
        var dist = new int[n];
        var from = new int[n];
        var counts = new int[budget.Length];
        int reach = 0;
        foreach (int value in budget) reach = Math.Max(reach, value);
        var buckets = new List<int>[reach + 1];
        for (int z = 0; z < n; z++)
        {
            dist[z] = int.MaxValue;
            from[z] = -1;
            if (owner[z] < 0) continue;
            dist[z] = 0;
            from[z] = owner[z];
            counts[owner[z]]++;
            (buckets[0] ??= new List<int>()).Add(z);
        }
        for (int d = 0; d <= reach; d++)
        {
            List<int> bucket = buckets[d];
            if (bucket == null) continue;
            for (int k = 0; k < bucket.Count; k++)
            {
                int z = bucket[k];
                if (dist[z] != d) continue;
                int s = from[z];
                if (owner[z] < 0)
                {
                    if (counts[s] >= cap[s]) continue;
                    owner[z] = s;
                    counts[s]++;
                }
                else if (owner[z] != s) continue;
                int x = z % width, y = z / width;
                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = x + Dx[dir], ny = y + Dy[dir];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int next = ny * width + nx;
                    if (owner[next] >= 0 || cost[next] == 0 || wild[next] || wall[next]) continue;
                    int step = cost[next];
                    if (dir >= 4)
                    {
                        // 斜走：两边都走不过去就是穿缝(长城的斜线、水角)，不算连着；代价乘 1.4(同 WorldMapZones.Step)
                        int side = y * width + nx, other = ny * width + x;
                        if (Closed(side, cost, wild, wall) && Closed(other, cost, wild, wall)) continue;
                        step = step * 14 / 10;
                    }
                    int nd = d + step;
                    if (nd > budget[s] || nd >= dist[next]) continue;
                    dist[next] = nd;
                    from[next] = s;
                    (buckets[nd] ??= new List<int>()).Add(next);
                }
            }
        }
        for (int z = 0; z < n; z++)
        {
            if (owner[z] != Unclaimed || cost[z] == 0 || wild[z]) continue;
            int near = -1;
            if (wall[z])
            {
                int x = z % width, y = z / width;
                for (int dir = 0; dir < 4 && near < 0; dir++)
                {
                    int nx = x + Dx[dir], ny = y + Dy[dir];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int next = ny * width + nx;
                    if (!wall[next] && owner[next] >= 0) near = owner[next];
                }
            }
            owner[z] = near >= 0 ? near : Filler;
        }
    }

    private static bool Closed(int zone, int[] cost, bool[] wild, bool[] wall) => cost[zone] == 0 || wild[zone] || wall[zone];

    // 平滑地块：zones、weights 为周围四块地和权重(块号 -1 或类别为 skip 的不算)；同一类的权重加起来，取最大的一类，
    // 返回这一类里权重最大的那块；都不算返回 -1
    public static int Vote(int[] zones, float[] weights, int[] classes, int skip)
    {
        float best = -1f;
        int pick = -1;
        for (int i = 0; i < zones.Length; i++)
        {
            int zone = zones[i];
            if (zone < 0 || classes[zone] == skip) continue;
            float sum = 0f, top = -1f;
            int representative = zone;
            for (int j = 0; j < zones.Length; j++)
            {
                if (zones[j] < 0 || classes[zones[j]] != classes[zone]) continue;
                sum += weights[j];
                if (weights[j] <= top) continue;
                top = weights[j];
                representative = zones[j];
            }
            if (sum > best + 1e-5f || Math.Abs(sum - best) <= 1e-5f && representative < pick)
            {
                best = sum;
                pick = representative;
            }
        }
        return pick;
    }

    // 一道长城压着哪些地块(至多八邻相连，地块四邻走不过去)；points 为 [经度, 纬度]，不到两个点的不算
    public static void MarkWall(IList<float[]> points, float west, float bottom, float side, int width, int height, bool[] wall)
    {
        if (points == null || points.Count < 2 || side <= 0f) return;
        for (int p = 1; p < points.Count; p++)
        {
            float[] a = points[p - 1], b = points[p];
            if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;
            float ax = (a[0] - west) / side, ay = (a[1] - bottom) / side, bx = (b[0] - west) / side, by = (b[1] - bottom) / side;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) * 2f));
            for (int s = 0; s <= steps; s++)
            {
                int x = (int)Math.Floor(ax + (bx - ax) * s / steps), y = (int)Math.Floor(ay + (by - ay) * s / steps);
                if (x >= 0 && y >= 0 && x < width && y < height) wall[y * width + x] = true;
            }
        }
    }

    // 擦掉这一格回到什么：导入时的归属(归的城还在才算)；没有为 null
    public static string BaseValue(IReadOnlyDictionary<int, string> baseline, int cell, ISet<string> cities)
    {
        if (baseline == null || !baseline.TryGetValue(cell, out string key)) return null;
        return key.StartsWith("c:", StringComparison.Ordinal) && !cities.Contains(key.Substring(2)) ? null : key;
    }
}
