using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本编辑器的笔刷：形状照原版 BrushLibrary 的四组(圆 circ、方 sqr、菱 diamond、毛糙的一团 special)，
// 大小按刷的格子算，半径 0 就是一格(刷地块一格 0.2°，刷地形一格 0.1°，见 ScenarioCells)。
// 另管开局怎么读刷的格子：最细的笔刷点在哪一格都要算数，不能只认一块地的中心。
public static class ScenarioBrush
{
    public enum Shape { Circle, Square, Diamond, Rough }

    public const int ShapeCount = 4, DefaultSize = 4;
    public static readonly int[] Radii = { 0, 1, 2, 3, 5, 8, 12, 20, 40 };

    // 照原图归城的 c: 不往块内别的格子取：导入时一块地只认中心，中心留空的就是没城的地(见 ScenarioWorldExport)
    public static readonly Func<string, bool> NotExact = key => !key.StartsWith("c:", StringComparison.Ordinal);

    private static readonly Dictionary<(Shape, int), (int dx, int dy)[]> Covers = new();

    public static bool Inside(Shape shape, int dx, int dy, int radius)
    {
        if (dx == 0 && dy == 0) return true;
        switch (shape)
        {
            case Shape.Square:
                return Math.Abs(dx) <= radius && Math.Abs(dy) <= radius;
            case Shape.Diamond:
                return Math.Abs(dx) + Math.Abs(dy) <= radius;
            case Shape.Rough:
                float outer = radius + 0.5f, solid = outer * 0.72f, distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance <= solid) return true;
                return distance <= outer && Noise(dx, dy, radius) < (outer - distance) / (outer - solid);
            default:
                return dx * dx + dy * dy <= radius * radius + radius - 1;
        }
    }

    // 这个笔刷盖住哪些格子(相对落笔那一格)
    public static (int dx, int dy)[] Cover(Shape shape, int radius)
    {
        if (Covers.TryGetValue((shape, radius), out (int dx, int dy)[] cover)) return cover;
        var list = new List<(int dx, int dy)>();
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
            if (Inside(shape, dx, dy, radius)) list.Add((dx, dy));
        return Covers[(shape, radius)] = list.ToArray();
    }

    // 一块地开局取哪个刷的值：中心那格刷了就按它；没刷就看块内别的格子，取刷得最多的(一样多取排序靠前的，每次结果一样)。
    // 格子比一块地还大时块内没有别的格子，仍只认中心
    public static bool ZoneValue<T>(IReadOnlyDictionary<int, T> map, float lon, float lat, float side, float step, Func<T, bool> usable, out T value)
    {
        value = default;
        if (map == null || map.Count == 0) return false;
        if (map.TryGetValue(ScenarioCells.Key(lon, lat, step), out value)) return true;
        value = default;
        if (side <= step) return false;
        float half = side / 2f;
        int i0 = FirstCell(lon - half, step), i1 = FirstCell(lon + half, step) - 1;
        int j0 = FirstCell(lat - half, step), j1 = FirstCell(lat + half, step) - 1;
        Dictionary<T, int> counts = null;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            if (!map.TryGetValue(ScenarioCells.Pack(i, j), out T found) || usable != null && !usable(found)) continue;
            counts ??= new Dictionary<T, int>();
            counts[found] = counts.TryGetValue(found, out int count) ? count + 1 : 1;
        }
        if (counts == null) return false;
        int most = 0;
        string bestName = null;
        foreach (KeyValuePair<T, int> pair in counts)
        {
            string name = pair.Key.ToString();
            if (pair.Value < most || pair.Value == most && string.CompareOrdinal(name, bestName) >= 0) continue;
            most = pair.Value;
            bestName = name;
            value = pair.Key;
        }
        return true;
    }

    // 一块地盖住的格子(取法同 ZoneValue)，连中心那格
    public static void ZoneCells(float lon, float lat, float side, float step, ICollection<int> cells)
    {
        cells.Add(ScenarioCells.Key(lon, lat, step));
        if (side <= step) return;
        float half = side / 2f;
        int i0 = FirstCell(lon - half, step), i1 = FirstCell(lon + half, step) - 1;
        int j0 = FirstCell(lat - half, step), j1 = FirstCell(lat + half, step) - 1;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            cells.Add(ScenarioCells.Pack(i, j));
    }

    // 比例尺大的图一个地格盖好几个地形格，地格只取中心那点会漏掉最细笔刷点的那一格：把每个刷的格子算到它中心落在的地格
    public static Dictionary<int, string> TileOverrides(IReadOnlyDictionary<int, string> cells, float step, float west, float bottom, float degrees, int width, int height)
    {
        if (cells == null || cells.Count == 0 || degrees <= step) return null;
        var tiles = new Dictionary<int, string>();
        foreach (KeyValuePair<int, string> pair in cells)
        {
            ScenarioCells.Unpack(pair.Key, out int i, out int j);
            int x = (int)Math.Floor(((i + 0.5f) * step - west) / degrees), y = (int)Math.Floor(((j + 0.5f) * step - bottom) / degrees);
            if (x >= 0 && y >= 0 && x < width && y < height) tiles[y * width + x] = pair.Value;
        }
        return tiles;
    }

    // 中心不小于这个经纬度的第一格。地块的界正好压在格子中心上时，两边算出来的浮点数差一点点，先取整到 1/256 格，
    // 这格才不会两边都不要或都要
    private static int FirstCell(float at, float step) => (int)Math.Ceiling(Math.Round((at / step - 0.5f) * 256f) / 256f);

    private static float Noise(int x, int y, int salt)
    {
        unchecked
        {
            int hash = x * 73856093 ^ y * 19349663 ^ salt * 83492791;
            hash ^= hash >> 13;
            hash *= 1274126177;
            hash ^= hash >> 16;
            return (hash & 0xFFFF) / 65536f;
        }
    }
}
