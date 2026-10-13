using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 行政控制力(每国每年一次，分帧轮到哪国算哪国)：
//   · 控制力(0~1)：城离最近的治所(本国都城；帝国还有各成员国的都城，算远 4 块地)沿本国陆地走几块地。
//     时代越新管得越远(满控半径 10 + 2×时代 块地，律令制再远一成五，分封、封建近一成)，超出部分每多走半径的两倍降到一成；
//     陆路不通的飞地按直线距离的 1.4 倍再加 8 块计，最多八成，隔海最多六成五(工业时代起八成)；
//   · 效果：控制力低的城自然稳定度下降(满控不减，最低扣 20)；
//     边地易手：两国太平时，交界处没有建筑、没划用途的边地归"控制力×离城心近"更强的一方(要强出两成五，山地要强出六成)，
//       每国每年每城最多得失 3 块，不把城切成两截；被一座城三面以上围住的无主空地(多是山地)并进这座城；
//     飞地易帜：陆路不通的飞地城(不是都城)控制力不到 0.45 时，每年有概率改投接壤最长的邻国(双方都不在打仗)。
public static class AdministrativeControlSystem
{
    private const int MemberSeatOffset = 4;
    private const float ExclaveFactor = 1.4f;
    private const int ExclavePenalty = 8;
    private const float ExclaveCap = 0.8f, OverseasCap = 0.65f, ModernOverseasCap = 0.8f;
    private const float MinControl = 0.1f;
    private const float StabilityPenalty = 20f;
    private const float PullScale = 6f;
    private const float DriftMargin = 1.25f, RoughDriftMargin = 1.6f;
    private const int DriftPerCity = 3;
    private const int DriftPerPass = 60;
    private const int HolesPerCity = 4;
    private const float DefectThreshold = 0.45f;
    private const float DefectChance = 0.5f;

    private static readonly Queue<long> Pending = new();
    private static int _cursor;

    public static void ResetWorldState()
    {
        Pending.Clear();
        _cursor = 0;
    }

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static bool Valid(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                                  !AncientWarfareCompatibility.Owns(kingdom);

    private static bool Valid(City city) => city?.data != null && !city.isRekt() && city.zones != null &&
                                            city.zones.Count > 0 && Valid(city.kingdom) &&
                                            !AncientWarfareCompatibility.OwnsObject(city);

    // 尚未算过的城当作满控
    public static float Get(City city)
    {
        if (city?.data == null) return 1f;
        float control = city.GetOrCreate().admin_control;
        return control < 0f ? 1f : control;
    }

    public static bool Known(City city) => city?.data != null && city.GetOrCreate().admin_control >= 0f;

    public static float StabilityLoss(City city) => Known(city) ? (1f - Get(city)) * StabilityPenalty : 0f;

    public static string Describe(City city)
    {
        if (!Known(city)) return null;
        var data = city.GetOrCreate();
        string where = data.admin_overseas ? LM.Get("admin_control_overseas")
            : data.admin_exclave ? LM.Get("admin_control_exclave")
            : string.Format(LM.Get("admin_control_distance"), data.admin_distance);
        return $"{data.admin_control * 100f:0}% · {where}";
    }

    // 每帧最多处理一国；同一国一年一次
    public static void Tick()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (!SimulationFrameBudget.HasTime) return;
        if (Pending.Count == 0) Refill();
        if (Pending.Count == 0) return;
        Kingdom realm = World.world.kingdoms.get(Pending.Dequeue());
        if (!Valid(realm) || Realm(realm) != realm) return;
        var data = realm.GetOrCreate();
        if (data.admin_pass_at >= 0d && World.world.getCurWorldTime() >= data.admin_pass_at &&
            Date.getYearsSince(data.admin_pass_at) < 1) return;
        try
        {
            using (FrameProfiler.Measure("行政控制力"))
            {
                data.admin_pass_at = World.world.getCurWorldTime();
                List<City> cities = Cities(realm);
                Compute(realm, cities);
                if (!realm.hasEnemies()) Defect(realm, cities);
                Drift(realm, cities);
            }
        }
        catch (Exception error)
        {
            LogService.LogWarning($"[EmpireCraft][行政控制力] {realm.id}: {error.Message}");
        }
    }

    // 一次排进最多 16 国，轮着来
    private static void Refill()
    {
        var list = World.world.kingdoms.list;
        if (list == null || list.Count == 0) return;
        if (_cursor >= list.Count) _cursor = 0;
        for (int scanned = 0; scanned < list.Count && Pending.Count < 16; scanned++)
        {
            Kingdom kingdom = list[_cursor];
            _cursor = (_cursor + 1) % list.Count;
            if (Valid(kingdom) && Realm(kingdom) == kingdom && kingdom.hasCities()) Pending.Enqueue(kingdom.id);
        }
    }

    private static List<City> Cities(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        IEnumerable<Kingdom> members = empire?.kingdoms_list?.Where(Valid) ?? new[] { realm };
        var result = new List<City>();
        foreach (Kingdom member in members.Distinct())
            if (member.cities != null)
                result.AddRange(member.cities.Where(Valid));
        return result;
    }

    #region 控制力

    public static TileZone CenterZone(City city)
    {
        TileZone zone = city.getTile()?.zone;
        if (zone != null && zone.city == city) return zone;
        WorldTile tile = World.world.GetTile((int)city.city_center.x, (int)city.city_center.y - 2);
        if (tile?.zone != null && tile.zone.city == city) return tile.zone;
        return city.zones.Count > 0 ? city.zones[0] : null;
    }

    private static float Reach(Kingdom realm)
    {
        int tier = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm));
        float reach = 10f + 2f * tier;
        return realm.GetRegime()?.type switch
        {
            RegimeType.LvLing => reach * 1.15f,
            RegimeType.Feudalism or RegimeType.ZhouFeudalism => reach * 0.9f,
            _ => reach
        };
    }

    private static float Control(float distance, float reach) =>
        distance <= reach ? 1f : Mathf.Max(MinControl, 1f - (distance - reach) / (2f * reach));

    private static void Compute(Kingdom realm, List<City> cities)
    {
        var seats = new List<(TileZone zone, int offset)>();
        foreach (City city in cities)
        {
            Kingdom owner = city.kingdom;
            if (owner.capital != city) continue;
            TileZone zone = CenterZone(city);
            if (zone != null) seats.Add((zone, owner == realm ? 0 : MemberSeatOffset));
        }
        if (seats.Count == 0) return;
        var zones = new HashSet<TileZone>();
        foreach (City city in cities)
            foreach (TileZone zone in city.zones)
                if (zone != null) zones.Add(zone);
        // 多源最短路：治所按起步距离排好，逐层外推(都城从 0 起、成员国都城从 4 起)
        var distance = new Dictionary<TileZone, int>();
        var buckets = new SortedDictionary<int, List<TileZone>>();
        foreach ((TileZone zone, int offset) in seats)
        {
            if (!buckets.TryGetValue(offset, out var bucket)) buckets[offset] = bucket = new List<TileZone>();
            bucket.Add(zone);
        }
        while (buckets.Count > 0)
        {
            int depth = buckets.Keys.First();
            List<TileZone> layer = buckets[depth];
            buckets.Remove(depth);
            var next = new List<TileZone>();
            foreach (TileZone zone in layer)
            {
                if (distance.ContainsKey(zone)) continue;
                distance[zone] = depth;
                foreach (TileZone neighbour in zone.neighbours ?? Array.Empty<TileZone>())
                    if (neighbour != null && zones.Contains(neighbour) && !distance.ContainsKey(neighbour)) next.Add(neighbour);
            }
            if (next.Count == 0) continue;
            if (buckets.TryGetValue(depth + 1, out var existing)) existing.AddRange(next);
            else buckets[depth + 1] = next;
        }
        float reach = Reach(realm);
        bool modern = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm)) >= 7;
        foreach (City city in cities)
        {
            var data = city.GetOrCreate();
            TileZone center = CenterZone(city);
            if (center == null) continue;
            if (distance.TryGetValue(center, out int steps))
            {
                data.admin_distance = steps;
                data.admin_exclave = false;
                data.admin_overseas = false;
                data.admin_control = Control(steps, reach);
                continue;
            }
            // 陆路不通：按到最近治所的直线距离折算
            float best = float.MaxValue;
            bool overseas = true;
            foreach ((TileZone seat, int offset) in seats)
            {
                float straight = Vector2.Distance(new Vector2(seat.x, seat.y), new Vector2(center.x, center.y)) + offset;
                if (straight < best) best = straight;
                if (seat.centerTile != null && center.centerTile != null && seat.centerTile.isSameIsland(center.centerTile))
                    overseas = false;
            }
            int far = Mathf.RoundToInt(best * ExclaveFactor) + ExclavePenalty;
            data.admin_distance = far;
            data.admin_exclave = true;
            data.admin_overseas = overseas;
            float cap = overseas ? modern ? ModernOverseasCap : OverseasCap : ExclaveCap;
            data.admin_control = Mathf.Min(cap, Control(far, reach));
        }
    }

    #endregion

    #region 边地易手

    private static float Pull(City city, TileZone zone)
    {
        if (zone.centerTile == null) return 0f;
        float dx = (zone.centerTile.x - city.city_center.x) / 8f, dy = (zone.centerTile.y - city.city_center.y + 2f) / 8f;
        return Get(city) / (1f + Mathf.Sqrt(dx * dx + dy * dy) / PullScale);
    }

    private static void Drift(Kingdom realm, List<City> cities)
    {
        var gained = new Dictionary<City, int>();
        var lost = new Dictionary<City, int>();
        int moved = 0;
        foreach (City city in cities)
        {
            if (moved >= DriftPerPass) break;
            if (!Valid(city)) continue;
            int holes = 0;
            foreach (TileZone border in city.border_zones.ToList())
            {
                if (border?.city != city) continue;
                foreach (TileZone zone in border.neighbours ?? Array.Empty<TileZone>())
                {
                    if (zone == null || zone.city == city) continue;
                    if (zone.city == null)
                    {
                        if (holes < HolesPerCity && FillHole(city, zone)) holes++;
                        continue;
                    }
                    gained.TryGetValue(city, out int got);
                    if (got >= DriftPerCity) break;
                    City other = zone.city;
                    lost.TryGetValue(other, out int gone);
                    if (gone >= DriftPerCity || !CanTake(city, other, zone)) continue;
                    city.addZone(zone);
                    if (zone.city != city) continue;
                    gained[city] = got + 1;
                    lost[other] = gone + 1;
                    moved++;
                }
            }
        }
    }

    private static bool CanTake(City city, City other, TileZone zone)
    {
        if (!Valid(other) || Realm(other.kingdom) == Realm(city.kingdom) ||
            FrontSystem.Hostile(city.kingdom, other.kingdom)) return false;
        if (CityExpansionPatchAtLimit(city) || other.zones.Count <= 3 || zone.tiles_with_ground <= 0) return false;
        if (zone.hasAnyBuildingsInSet(BuildingList.Civs) || CenterZone(other) == zone) return false;
        if (ZonePlanSystem.Get(other, zone) != ZoneUse.None) return false;
        if (other.GetTileZoneOccupier(zone) != null || city.GetTileZoneOccupier(zone) != null) return false;
        float margin = EmpireCraft.Scripts.GamePatches.CityExpansionPatch.Passable(zone) ? DriftMargin : RoughDriftMargin;
        if (Pull(city, zone) < Pull(other, zone) * margin) return false;
        return StaysWhole(other, zone);
    }

    private static bool CityExpansionPatchAtLimit(City city) => EmpireCraft.Scripts.GamePatches.CityExpansionPatch.AtLimit(city);

    // 拿走这块后原城的其余地块仍连成一片
    private static bool StaysWhole(City city, TileZone removed)
    {
        var touching = (removed.neighbours ?? Array.Empty<TileZone>()).Where(zone => zone?.city == city).ToList();
        if (touching.Count <= 1) return true;
        var seen = new HashSet<TileZone> { removed, touching[0] };
        var stack = new Stack<TileZone>();
        stack.Push(touching[0]);
        while (stack.Count > 0)
        {
            TileZone zone = stack.Pop();
            foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                if (next != null && next.city == city && seen.Add(next)) stack.Push(next);
        }
        return touching.All(seen.Contains);
    }

    // 被这座城三面以上围住、其余是本国或无主的空地并进来
    private static bool FillHole(City city, TileZone zone)
    {
        if (zone.tiles_with_ground <= 0 || zone.neighbours == null) return false;
        int own = 0;
        Kingdom realm = Realm(city.kingdom);
        foreach (TileZone next in zone.neighbours)
        {
            if (next?.city == city) own++;
            else if (next?.city != null && Realm(next.city.kingdom) != realm) return false;
        }
        if (own < 3) return false;
        city.addZone(zone);
        return zone.city == city;
    }

    #endregion

    #region 飞地易帜

    private static void Defect(Kingdom realm, List<City> cities)
    {
        foreach (City city in cities)
        {
            if (!Valid(city)) continue;
            var data = city.GetOrCreate();
            if (!data.admin_exclave || data.admin_control >= DefectThreshold || city.kingdom.capital == city ||
                city.kingdom.hasEnemies() || city.GetOrCreate().OccupiedStatus?.Count > 0) continue;
            if (UnityEngine.Random.value >= (DefectThreshold - data.admin_control) * DefectChance) continue;
            Kingdom receiver = Neighbour(city, realm);
            if (receiver == null) continue;
            Kingdom former = city.kingdom;
            city.joinAnotherKingdom(receiver);
            if (city.kingdom != receiver) continue;
            data.admin_control = -1f;
            if (former != realm && former.cities.Count == 0 && former.GetEmpire() == realm.GetEmpire())
                former.GetEmpire()?.leave(former);
            EventRecorder.Record(realm, string.Format(LM.Get("admin_control_defect_history"),
                city.data.name, realm.data.name, receiver.data.name));
            return; // 每国每年至多一座
        }
    }

    // 和这座城接壤最长、不在打仗的邻国
    private static Kingdom Neighbour(City city, Kingdom realm)
    {
        var contacts = new Dictionary<Kingdom, int>();
        foreach (TileZone zone in city.border_zones)
            foreach (TileZone next in zone?.neighbours ?? Array.Empty<TileZone>())
            {
                Kingdom neighbour = next?.city?.kingdom;
                if (!Valid(neighbour) || Realm(neighbour) == realm || neighbour.hasEnemies() ||
                    AncientWarfareCompatibility.OwnsObject(next.city)) continue;
                contacts.TryGetValue(neighbour, out int border);
                contacts[neighbour] = border + 1;
            }
        return contacts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id)
            .Select(pair => pair.Key).FirstOrDefault();
    }

    #endregion
}
