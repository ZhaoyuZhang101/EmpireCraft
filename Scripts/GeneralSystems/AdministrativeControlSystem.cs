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
//   · 控制力(0~1)：城离最近的治所(本国都城；帝国还有各成员国的都城，起步算 4)沿本国领土走过去的路程。
//     路程按地形阻力算(用户 2026-10-16："按照地形和距离来判断辐射范围"，见 TerrainResistance)：平原一块地算 1，
//     山地 4、过河 +2、翻长城 +6，有路减半——山后、长城外的城鞭长莫及，驿道通到的地方管得远；
//     港口之间在本国水师辐射内(见 StraitReach)可走海路(上船 2，航程每 16 格 +1)，隔海的城最多六成五(工业时代起八成)。
//     时代越新管得越远(满控路程 10 + 2×时代，律令制再远一成五，分封、封建近一成)，超出部分每多走满控路程的两倍降到一成；
//     陆路海路都不通的飞地按直线距离的 1.4 倍再加 8 计，最多八成；
//   · 效果：控制力低的城自然稳定度下降(满控不减，最低扣 20)；赋税收不齐(满控全收，最低收一半，见 TreasurySystem)；
//     征兵也少从这样的城里抽(见 CorpsSystem.Mobilize、CityPopulationSystem.DrawKingdomCity)；地图上见政令图层；
//     领土不在这里变动：扩张、边界侵蚀都按原版(侵蚀受原版"边界侵蚀"开关管、只在交战双方之间，见 CityExpansionPatch)。
//     以前这里的"边地易手"(太平时互相侵蚀)、"被围空地并入"(把山地、隔水的地并进来)不守原版规矩，已删；
//     旧存档里因此留下的山地、隔水地和飞地，读档后交给原版的"断开的地放弃"清掉(见 CityExpansionPatch.CheckAbandonOnLoad)；
//     飞地易帜：陆路不通的飞地城(不是都城)控制力不到 0.45 时，每年有概率改投接壤最长的邻国(双方都不在打仗)。
public static class AdministrativeControlSystem
{
    private const int MemberSeatOffset = 4;
    private const float ExclaveFactor = 1.4f;
    private const int ExclavePenalty = 8;
    private const float ExclaveCap = 0.8f, OverseasCap = 0.65f, ModernOverseasCap = 0.8f;
    private const float MinControl = 0.1f;
    private const float StabilityPenalty = 20f;
    private const float DefectThreshold = 0.45f;
    private const float DefectChance = 0.5f;
    // 海路：上船 2，航程每 16 格 +1
    private const float SeaEmbark = 2f, SeaTilesPerCost = 16f;
    // 地块阻力算不出(城里的水面)时按过河算
    private const float WaterStep = 1f + TerrainResistance.RiverCrossing;
    // 鞭长莫及：赋税、征兵按 0.5 + 0.5 × 控制力
    private const float MinYield = 0.5f;

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

    // 赋税、征兵能收上来几成(满控全收，最低一半)
    public static float Yield(City city) => Known(city) ? MinYield + (1f - MinYield) * Get(city) : 1f;

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
        if (Config.game_loaded && !SmoothLoader.isLoading()) EmpireCraft.Scripts.GamePatches.CityExpansionPatch.CheckAbandonOnLoad();
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
        // 港口(本国水师辐射内互通海路)：城心块 → 港口城、所在水域
        float radius = StraitReach.Radius(realm);
        var ports = new Dictionary<TileZone, (City city, HashSet<int> basins)>();
        if (radius > 0f)
            foreach (City city in cities)
            {
                if (NavySystem.Port(city) == null) continue;
                TileZone zone = CenterZone(city);
                var basins = new HashSet<int>(SeaRegionSystem.RegionsAround(city).Select(region => region.Basin));
                if (zone != null && basins.Count > 0) ports[zone] = (city, basins);
            }
        // 多源最短路(按地形阻力)：都城从 0 起、成员国都城从 4 起；记下路上有没有走过海路
        var distance = new Dictionary<TileZone, (float cost, bool sea)>();
        var heap = new MinHeap<(TileZone zone, bool sea)>();
        foreach ((TileZone zone, int offset) in seats) heap.Push(offset, (zone, false));
        while (heap.Count > 0)
        {
            (float cost, (TileZone zone, bool sea)) = heap.Pop();
            if (distance.ContainsKey(zone)) continue;
            distance[zone] = (cost, sea);
            foreach (TileZone neighbour in zone.neighbours ?? Array.Empty<TileZone>())
            {
                if (neighbour == null || !zones.Contains(neighbour) || distance.ContainsKey(neighbour)) continue;
                float step = TerrainResistance.Step(neighbour);
                heap.Push(cost + (step < 0f ? WaterStep : step), (neighbour, sea));
            }
            if (!ports.TryGetValue(zone, out var from)) continue;
            foreach (KeyValuePair<TileZone, (City city, HashSet<int> basins)> to in ports)
            {
                if (to.Key == zone || distance.ContainsKey(to.Key) || !from.basins.Overlaps(to.Value.basins)) continue;
                float span = (from.city.city_center - to.Value.city.city_center).magnitude;
                if (span <= radius) heap.Push(cost + SeaEmbark + span / SeaTilesPerCost, (to.Key, true));
            }
        }
        float reach = Reach(realm);
        bool modern = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm)) >= 7;
        foreach (City city in cities)
        {
            var data = city.GetOrCreate();
            TileZone center = CenterZone(city);
            if (center == null) continue;
            if (distance.TryGetValue(center, out var reached))
            {
                // 走海路才到、又与各治所不在同一块陆地上的，是隔海的城
                bool across = reached.sea && !seats.Any(seat => seat.zone.centerTile != null && center.centerTile != null &&
                                                                  seat.zone.centerTile.isSameIsland(center.centerTile));
                data.admin_distance = Mathf.RoundToInt(reached.cost);
                data.admin_exclave = false;
                data.admin_overseas = across;
                float control = Control(reached.cost, reach);
                data.admin_control = across ? Mathf.Min(modern ? ModernOverseasCap : OverseasCap, control) : control;
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

    #region 飞地易帜

    private static void Defect(Kingdom realm, List<City> cities)
    {
        foreach (City city in cities)
        {
            if (!Valid(city)) continue;
            var data = city.GetOrCreate();
            if (!data.admin_exclave || data.admin_control >= DefectThreshold || city.kingdom.capital == city ||
                city.kingdom.hasEnemies() || city.GetOrCreate().OccupiedStatus?.Count > 0 ||
                UnificationSystem.InGrace(city.kingdom)) continue;
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
