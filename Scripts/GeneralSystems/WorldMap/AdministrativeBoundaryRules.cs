using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 经纬度面数据采用 GeoJSON MultiPolygon 的坐标层级；每个 polygon 的第一圈是外界，后续圈为孔洞。
internal static class AdministrativeBoundaryRules
{
    public static bool Bounds(List<List<List<float[]>>> polygons, out float west, out float south, out float east, out float north)
    {
        west = south = float.PositiveInfinity;
        east = north = float.NegativeInfinity;
        if (polygons != null)
            foreach (List<List<float[]>> polygon in polygons)
                foreach (List<float[]> ring in polygon ?? new List<List<float[]>>())
                    foreach (float[] point in ring ?? new List<float[]>())
                    {
                        if (point == null || point.Length < 2) continue;
                        west = Math.Min(west, point[0]); east = Math.Max(east, point[0]);
                        south = Math.Min(south, point[1]); north = Math.Max(north, point[1]);
                    }
        return west <= east && south <= north;
    }

    public static bool Contains(List<List<List<float[]>>> polygons, float lon, float lat)
    {
        if (polygons == null) return false;
        foreach (List<List<float[]>> polygon in polygons)
        {
            if (polygon == null || polygon.Count == 0 || !InRing(polygon[0], lon, lat)) continue;
            bool hole = false;
            for (int i = 1; i < polygon.Count; i++)
                if (InRing(polygon[i], lon, lat)) { hole = true; break; }
            if (!hole) return true;
        }
        return false;
    }

    private static bool InRing(List<float[]> ring, float x, float y)
    {
        if (ring == null || ring.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            float[] a = ring[j], b = ring[i];
            if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;
            double cross = (double)(x - a[0]) * (b[1] - a[1]) - (double)(y - a[1]) * (b[0] - a[0]);
            if (Math.Abs(cross) < 0.0000001 && x >= Math.Min(a[0], b[0]) && x <= Math.Max(a[0], b[0]) &&
                y >= Math.Min(a[1], b[1]) && y <= Math.Max(a[1], b[1])) return true;
            if ((a[1] > y) != (b[1] > y) && x < (double)(b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0])
                inside = !inside;
        }
        return inside;
    }

}
