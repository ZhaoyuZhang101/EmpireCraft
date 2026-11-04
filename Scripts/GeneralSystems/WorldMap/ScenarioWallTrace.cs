using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 从当前世界导入剧本时，把格子上的城墙描回折线(见 ScenarioWorldExport.Walls)：从端头走到底，剩下的是环；太短的零星墙不要。
// 只管几何，不碰世界，折线的点隔几格取一个，两头都留
internal static class ScenarioWallTrace
{
    private static readonly (int dx, int dy)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int dx, int dy)[] Diagonal = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

    public static List<List<(int x, int y)>> Trace(IEnumerable<(int x, int y)> tiles, int minTiles)
    {
        var all = new HashSet<(int x, int y)>(tiles);
        var left = new HashSet<(int x, int y)>(all);
        var lines = new List<List<(int x, int y)>>();
        List<(int x, int y)> ordered = all.OrderBy(cell => cell.y).ThenBy(cell => cell.x).ToList();
        foreach ((int x, int y) start in ordered.Where(cell => Neighbours(cell, all).Count() == 1))
            if (left.Contains(start)) Walk(start, left, lines, minTiles);
        foreach ((int x, int y) start in ordered)
            if (left.Contains(start)) Walk(start, left, lines, minTiles);
        return lines;
    }

    public static List<(int x, int y)> Sample(List<(int x, int y)> line, int every)
    {
        var points = new List<(int x, int y)>();
        for (int i = 0; i < line.Count; i += every) points.Add(line[i]);
        if (points[points.Count - 1] != line[line.Count - 1]) points.Add(line[line.Count - 1]);
        return points;
    }

    private static void Walk((int x, int y) start, HashSet<(int x, int y)> left, List<List<(int x, int y)>> lines, int minTiles)
    {
        var path = new List<(int x, int y)> { start };
        left.Remove(start);
        (int x, int y) current = start;
        while (true)
        {
            // 四邻优先，免得斜着抄近路把拐角那一格漏下
            (int x, int y) next = default;
            bool found = false;
            foreach ((int dx, int dy) in Orthogonal.Concat(Diagonal))
            {
                (int x, int y) cell = (current.x + dx, current.y + dy);
                if (!left.Contains(cell)) continue;
                next = cell;
                found = true;
                break;
            }
            if (!found) break;
            path.Add(next);
            left.Remove(next);
            current = next;
        }
        // 环：首尾挨着就接回起点
        if (path.Count >= 8 && global::System.Math.Abs(current.x - start.x) <= 1 && global::System.Math.Abs(current.y - start.y) <= 1) path.Add(start);
        if (path.Count >= minTiles) lines.Add(path);
    }

    private static IEnumerable<(int x, int y)> Neighbours((int x, int y) cell, HashSet<(int x, int y)> all) =>
        Orthogonal.Concat(Diagonal).Select(step => (cell.x + step.dx, cell.y + step.dy)).Where(all.Contains);
}
