using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 长城作行政区的天然界线(用户 2026-10-30 截图：盛唐的丰州羁縻州——丰州、灵州两城都在秦长城北边——辖地跨过长城一路伸到长安北面，
// "这个行政区都跨长城了要不还是让长城成为天然屏障吧""你要知道下面就是长安呢")。病根在开局按州府锚点分地：关内道的延、庆、鄜、坊诸州
// 锚点在墙南，分到关内道的地块归"道里离它最近的城"，道里只有墙北的两座城，陕北整片隔着墙划给了灵州。
// 剧本这一时代的长城、边墙(格坐标的折线，见 WorldMapScenario.WallLines)随存档留着(SaveData.world_map_walls)，开局与之后都照它分两边：
//   · 地块：城心到地块中心的连线穿过某道墙奇数次(凹进去的墙一出一进不算)，这块地就在墙后，城不领(铺辖地、锚点分地、铺满，见 WorldMapZones)；
//   · 城与城：连线穿墙奇数次，且从这道墙没接上的端头绕过去比直线远 DetourRatio 倍以上，才算隔着长城——墙头上擦过去的不算
//     (天水与兰州擦着秦长城西头、科隆与美因茨擦着日耳曼边墙北头)；
//   · 压在墙上的点(离那段墙不到 OnWall 格：傍着长城的城)那段墙不算；接上的几道墙(阴山接燕辽、明内长城接外边)算一道；
//   · 长城关门不算缺口：只按折线判，关城筑在哪都一样；只认剧本的长城，随机地图、神力画的城墙不算。
// 用的地方：开局铺地(WorldMapZones)、设行政区与按法理划行政区(WorldMapScenario.Divide/Reorganize)、行政区重组
// (AdministrationReorganizationSystem)、按法理补齐行政区(Empire.AutoEstablishTerritorialDivisions)、国内划界(InternalBoundarySystem)：
// 同一王国法理里隔着长城的城分设行政区(行政区共用这个法理)，城只归长城这一侧的行政区；无人区跟着这一侧的城(WildernessSystem.OriginOf)。
public static class FrontierWalls
{
    private const float OnWall = 2f, JoinTiles = 4f, DetourRatio = 1.3f;

    // 一道连着的墙：各段，没接上的端头(绕墙走的地方)，外框
    public sealed class Line
    {
        public readonly List<(Vector2 a, Vector2 b)> segments = new();
        public readonly List<Vector2> ends = new();
        public float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
    }

    private static List<Line> _lines = new();
    // 存档：各道折线 [x0, y0, x1, y1, …](格)
    private static List<float[]> _saved;
    // 旧档没存长城：头一回用到时按存档的时代补算(见 WorldMapWilderness.ScenarioWalls)
    private static bool _fallback;

    public static void ResetWorldState()
    {
        _lines = new List<Line>();
        _saved = null;
        _fallback = false;
    }

    // 剧本摆好时记下这一时代的长城(格坐标折线)
    public static void Set(List<List<Vector2>> polylines)
    {
        _fallback = false;
        _saved = polylines?.Where(line => line != null && line.Count >= 2)
            .Select(line => line.SelectMany(point => new[] { point.x, point.y }).ToArray()).ToList();
        if (_saved != null && _saved.Count == 0) _saved = null;
        _lines = Build(polylines);
    }

    public static void Load(List<float[]> saved)
    {
        ResetWorldState();
        if (saved == null)
        {
            _fallback = true;
            return;
        }
        Set(saved.Where(flat => flat != null && flat.Length >= 4)
            .Select(flat => Enumerable.Range(0, flat.Length / 2).Select(i => new Vector2(flat[i * 2], flat[i * 2 + 1])).ToList())
            .ToList());
    }

    public static List<float[]> Save()
    {
        Ensure();
        return _saved;
    }

    private static void Ensure()
    {
        if (!_fallback) return;
        _fallback = false;
        List<List<Vector2>> lines = WorldMapWilderness.ScenarioWalls();
        if (lines != null) Set(lines);
    }

    public static bool Any
    {
        get
        {
            Ensure();
            return _lines.Count > 0;
        }
    }

    // 折线 → 一道道连着的墙：端头离别的折线不到 JoinTiles 的接成一道，没接上的端头记下
    public static List<Line> Build(IEnumerable<List<Vector2>> polylines)
    {
        List<List<Vector2>> lines = polylines?.Where(line => line != null && line.Count >= 2).ToList() ?? new List<List<Vector2>>();
        int count = lines.Count;
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        bool Near(Vector2 point, List<Vector2> line)
        {
            for (int k = 1; k < line.Count; k++)
                if (Distance(point, line[k - 1], line[k]) < JoinTiles) return true;
            return false;
        }
        for (int i = 0; i < count; i++)
        for (int j = 0; j < count; j++)
            if (i != j && (Near(lines[i][0], lines[j]) || Near(lines[i][lines[i].Count - 1], lines[j]))) parent[Find(i)] = Find(j);
        var result = new List<Line>();
        foreach (IGrouping<int, int> group in Enumerable.Range(0, count).GroupBy(Find))
        {
            var wall = new Line();
            foreach (int i in group)
            {
                List<Vector2> line = lines[i];
                for (int k = 1; k < line.Count; k++)
                {
                    Vector2 a = line[k - 1], b = line[k];
                    wall.segments.Add((a, b));
                    wall.minX = Mathf.Min(wall.minX, Mathf.Min(a.x, b.x));
                    wall.maxX = Mathf.Max(wall.maxX, Mathf.Max(a.x, b.x));
                    wall.minY = Mathf.Min(wall.minY, Mathf.Min(a.y, b.y));
                    wall.maxY = Mathf.Max(wall.maxY, Mathf.Max(a.y, b.y));
                }
                foreach (Vector2 end in new[] { line[0], line[line.Count - 1] })
                    if (!group.Any(j => j != i && Near(end, lines[j]))) wall.ends.Add(end);
            }
            result.Add(wall);
        }
        return result;
    }

    // 从 p 看 q 在墙后：连线穿过某道墙奇数次(p 压着的那段不算)
    public static bool Behind(List<Line> lines, Vector2 p, Vector2 q)
    {
        foreach (Line line in lines)
            if (Crossings(line, p, q, false) % 2 == 1) return true;
        return false;
    }

    // 两城隔着长城：连线穿过某道墙奇数次(两头压着的那段不算)，且从没接上的端头绕过去比直线远 DetourRatio 倍以上
    public static bool Separates(List<Line> lines, Vector2 p, Vector2 q)
    {
        foreach (Line line in lines)
        {
            if (Crossings(line, p, q, true) % 2 == 0) continue;
            float straight = Vector2.Distance(p, q), detour = float.PositiveInfinity;
            foreach (Vector2 end in line.ends) detour = Mathf.Min(detour, Vector2.Distance(p, end) + Vector2.Distance(end, q));
            if (detour >= straight * DetourRatio) return true;
        }
        return false;
    }

    private static int Crossings(Line line, Vector2 p, Vector2 q, bool both)
    {
        float minX = Mathf.Min(p.x, q.x), maxX = Mathf.Max(p.x, q.x), minY = Mathf.Min(p.y, q.y), maxY = Mathf.Max(p.y, q.y);
        if (maxX < line.minX || minX > line.maxX || maxY < line.minY || minY > line.maxY) return 0;
        int count = 0;
        foreach ((Vector2 a, Vector2 b) in line.segments)
        {
            if (Mathf.Max(a.x, b.x) < minX || Mathf.Min(a.x, b.x) > maxX || Mathf.Max(a.y, b.y) < minY || Mathf.Min(a.y, b.y) > maxY) continue;
            if (!Crosses(p, q, a, b) || Distance(p, a, b) < OnWall || both && Distance(q, a, b) < OnWall) continue;
            count++;
        }
        return count;
    }

    private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
        Side(c, d, a) > 0f != Side(c, d, b) > 0f && Side(a, b, c) > 0f != Side(a, b, d) > 0f;

    private static float Side(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);

    // 点到线段的距离
    private static float Distance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float length = ab.sqrMagnitude;
        float t = length <= 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
        return Vector2.Distance(p, a + ab * t);
    }

    #region 进世界后

    private static Vector2? Seat(City city)
    {
        WorldTile tile = city?.data == null || city.isRekt() ? null : city.getTile();
        return tile == null ? null : new Vector2(tile.x, tile.y);
    }

    // 两城隔着长城(见 Separates)
    public static bool Separates(City a, City b)
    {
        if (a == null || b == null || a == b || !Any) return false;
        Vector2? p = Seat(a), q = Seat(b);
        return p != null && q != null && Separates(_lines, p.Value, q.Value);
    }

    // 这块地在城的墙后(见 Behind)
    public static bool Behind(City city, TileZone zone)
    {
        if (zone?.centerTile == null || !Any) return false;
        Vector2? p = Seat(city);
        return p != null && Behind(_lines, p.Value, new Vector2(zone.centerTile.x, zone.centerTile.y));
    }

    // 一组城按长城分开(组里任两城都不隔墙)：照给的先后，每座城进头一个跟组里各城都不隔墙的组，都隔着就另起一组。
    // 头一组是排在最前的城那一组(法理首府排最前，它那一组接着用这个法理的名)
    public static List<List<City>> Split(IEnumerable<City> cities)
    {
        var groups = new List<List<City>>();
        foreach (City city in cities ?? Enumerable.Empty<City>())
        {
            if (city == null) continue;
            List<City> home = Any ? groups.FirstOrDefault(group => group.All(other => !Separates(city, other))) : groups.FirstOrDefault();
            if (home == null) groups.Add(home = new List<City>());
            home.Add(city);
        }
        return groups;
    }

    #endregion
}
