using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GamePatches;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城的辖区按村镇划(用户 2026-10-17 "城市应该按照涵盖几个村几个镇……来算"，选"城的辖区按村镇划")：
//   · 一座城的地分成几片：城区(城心与城市平面图里建成的地块)一片，其余每块地归离它最近的村镇(见 RuralSettlementSystem，
//     废墟也算)；离城区比离哪个村镇都近的归城区；
//   · 打仗按片占、按片收复(见 FrontSystem.Advance、Retake 与平滑国界的 Complete)：拿下一片里的一块地，那一片就一并到手；
//     城区那片就是主城池，一破整城投降；
//   · 没有村镇的城(没开纯净地图、游牧营地等)照旧一块地一片。
// 按城缓存，城的地块数或镇村有变、或过了一个月(FrontSystem.MonthTick 清掉)就重算。
public static class CityDistricts
{
    private const int CoreKey = -1;
    private static readonly Dictionary<long, (int stamp, Dictionary<int, int> keys)> Cache = new();

    public static void Reset() => Cache.Clear();

    private static int Stamp(City city)
    {
        unchecked
        {
            int hash = city.zones.Count * 7919 + RuralSettlementSystem.Revision * 31 + (PureMapPatch.BuiltZones(city)?.Count ?? 0);
            foreach (TileZone zone in city.zones)
                if (zone != null) hash ^= zone.id * 92821;
            return hash;
        }
    }

    private static Dictionary<int, int> Keys(City city)
    {
        if (city?.zones == null || city.data == null) return null;
        int stamp = Stamp(city);
        if (Cache.TryGetValue(city.data.id, out var cached) && cached.stamp == stamp) return cached.keys;
        var keys = new Dictionary<int, int>();
        var seats = new List<(int key, TileZone zone)>();
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
        {
            TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
            if (zone != null && zone.city == city) seats.Add((settlement.id, zone));
        }
        if (seats.Count > 0)
        {
            var core = new HashSet<int>(PureMapPatch.BuiltZones(city) ?? new HashSet<int>());
            TileZone center = city.getTile()?.zone;
            if (center != null) core.Add(center.id);
            List<TileZone> coreZones = city.zones.Where(zone => zone != null && core.Contains(zone.id)).ToList();
            foreach (TileZone zone in city.zones)
            {
                if (zone == null) continue;
                if (core.Contains(zone.id))
                {
                    keys[zone.id] = CoreKey;
                    continue;
                }
                int best = CoreKey;
                float bestDistance = coreZones.Count == 0 ? float.MaxValue : coreZones.Min(other => Distance(zone, other));
                foreach ((int key, TileZone seat) in seats)
                {
                    float d = Distance(zone, seat);
                    if (d > bestDistance) continue;
                    bestDistance = d;
                    best = key;
                }
                keys[zone.id] = best;
            }
        }
        Cache[city.data.id] = (stamp, keys);
        return keys;
    }

    private static float Distance(TileZone a, TileZone b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

    // 没有村镇的城：一块地一片(以地块编号区分)
    private static int KeyOf(City city, TileZone zone, Dictionary<int, int> keys) =>
        keys != null && keys.TryGetValue(zone.id, out int key) ? key : int.MinValue + zone.id;

    // 这块地所在的那一片(本城的地块)
    public static List<TileZone> ZonesOf(City city, TileZone zone)
    {
        Dictionary<int, int> keys = Keys(city);
        if (zone == null || keys == null || keys.Count == 0) return zone == null ? new List<TileZone>() : new List<TileZone> { zone };
        int key = KeyOf(city, zone, keys);
        return city.zones.Where(other => other != null && KeyOf(city, other, keys) == key).ToList();
    }

    public static bool SameDistrict(City city, TileZone a, TileZone b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        Dictionary<int, int> keys = Keys(city);
        return keys != null && keys.Count > 0 && KeyOf(city, a, keys) == KeyOf(city, b, keys);
    }

    // 还有几片里有 open 的地块(守方"城中最后一片"不丢用)
    public static int Count(City city, Func<TileZone, bool> open)
    {
        Dictionary<int, int> keys = Keys(city);
        var seen = new HashSet<int>();
        foreach (TileZone zone in city.zones)
            if (zone != null && open(zone)) seen.Add(KeyOf(city, zone, keys));
        return seen.Count;
    }

    // 这个村镇那一片的地块(划归别的城用，见 SettlementAssignPower)
    public static List<TileZone> ZonesOfSettlement(City city, RuralSettlement settlement)
    {
        Dictionary<int, int> keys = Keys(city);
        if (keys == null || settlement == null) return new List<TileZone>();
        return city.zones.Where(zone => zone != null && keys.TryGetValue(zone.id, out int key) && key == settlement.id).ToList();
    }
}
