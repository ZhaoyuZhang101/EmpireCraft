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
//
// 扩张上限(用户 2026-10-16："扩张的毫无节制""远的扩张不到的就可以用羁縻统治，或者属国朝贡或者诸侯等，参考朝鲜和明朝的关系")：
//   · 控制范围：满控路程除了时代，还看本国已采纳的科技(驿传、工业化、电报、摩托化、航空)、政体(律令远、封建近)
//     和君主(雄主、正统高管得远，昏君、正统低收缩)；
//   · 硬限：30 年内新打下(含割让)的城，控制力不到三成(过度扩张时门槛更高)且没在打仗的，由当地首领自立，
//     按政体文化转为间接统治——施行羁縻的帝国设羁縻州(仍在帝国内，自治、少纳税)；封建、分封或不是帝国的立为附庸诸侯；
//     其余帝国收为朝贡国(如朝鲜之于明)。每国每年至多放出一片(最多 6 城)；
//   · 早期生产力只够这样统治(用户 2026-10-16："早期之所以是周朝制度是因为生产力只能允许这么统治")：
//     直辖容量起点很低，青铜时代只能直辖十几城，再大就只能分封——周制、封建帝国里的诸侯(周制的郡除外)
//     不算直辖，也不因路远失稳；青铜时代及以前，或周制天下，管不到的新征服之地封为诸侯(帝国成员)；
//   · 软限：直辖容量 = 4 + 4×时代 + 制度科技(官僚文书、驿传、考选取士、电报、计算机)，按政体、君主折算；
//     羁縻地区、分封诸侯不算直辖。直辖城数超过容量即过度扩张：远方城市稳定度再降，硬限门槛提高，
//     开战不再谋割地吞并、只迫其称臣(见 WarGoalSystem.Ensure)。
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
    // 扩张上限：新征服之地控制力不到三成转间接统治(过度扩张时门槛最多再加两成)；30 年内算新征服；一次最多放出 6 城
    private const float HardLimit = 0.3f, OverextendedHardLimit = 0.2f;
    private const float RecentConquestYears = 30f;
    private const int ReleaseClusterMax = 6;
    private const float OverextensionPenalty = 15f;
    // 开战估算路程：直线距离按平均地形阻力折算
    private const float EstimateTerrain = 1.4f;

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

    // 羁縻地区本地首领自治，不因鞭长莫及而失稳；过度扩张时远方再扣(满控扣 3，控制力越低扣得越多，最多 18)
    public static float StabilityLoss(City city)
    {
        if (!Known(city) || IsIndirect(city.kingdom)) return 0f;
        float control = Get(city);
        float loss = (1f - control) * StabilityPenalty;
        Kingdom realm = Realm(city.kingdom);
        float over = realm?.data == null ? 0f : realm.GetOrCreate().admin_overextension;
        return loss + over * OverextensionPenalty * (1.2f - control);
    }

    // 间接统治：羁縻地区；周制、封建帝国里的诸侯(周制直辖的郡、帝国核心除外)
    private static bool IsIndirect(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || kingdom == empire.CoreKingdom) return false;
        if (JimiSystem.IsJimiAdministration(kingdom) || JimiSystem.IsJimiRegion(kingdom, empire)) return true;
        RegimeType? regime = empire.CoreKingdom?.GetRegime()?.type;
        return regime == RegimeType.Feudalism ||
               regime == RegimeType.ZhouFeudalism && kingdom.GetKingdomType() != KingdomType.ZhouFeudalism_jun;
    }

    // 青铜时代及以前(时代 ≤ 3)
    private const int EarlyEraTier = 3;

    // 城易主时记下(CityPatch 攻城结算、WarGoalSystem 割地调用)
    public static void MarkConquered(City city, Kingdom receiver)
    {
        if (city?.data == null || receiver?.data == null) return;
        var data = city.GetOrCreate();
        data.conquered_at = World.world.getCurWorldTime();
        data.conquered_by = Realm(receiver).id;
    }

    // 开战定目标用：对方都城是否远在本国控制范围之外(按本国离它最近的城的路程加直线距离估算)，或本国已过度扩张
    public static bool BeyondReach(Kingdom attacker, City target)
    {
        Kingdom realm = Realm(attacker);
        if (!Valid(realm) || target?.data == null || target.isRekt()) return false;
        if (realm.GetOrCreate().admin_overextension > 0f) return true;
        TileZone goal = CenterZone(target);
        if (goal == null) return false;
        float best = float.MaxValue;
        foreach (City city in Cities(realm))
        {
            if (!Known(city)) continue;
            TileZone from = CenterZone(city);
            if (from == null) continue;
            float cost = city.GetOrCreate().admin_distance +
                         Vector2.Distance(new Vector2(from.x, from.y), new Vector2(goal.x, goal.y)) * EstimateTerrain;
            if (cost < best) best = cost;
        }
        return best < float.MaxValue && Control(best, Reach(realm)) < HardLimit;
    }

    public static string DescribeCapacity(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        if (!Valid(realm)) return null;
        var data = realm.GetOrCreate();
        if (data.admin_capacity <= 0) return null;
        string text = string.Format(LM.Get("admin_capacity_line"), data.admin_direct_cities, data.admin_capacity);
        return data.admin_overextension > 0f
            ? text + "  " + string.Format(LM.Get("admin_overextended"), data.admin_overextension * 100f)
            : text;
    }

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
                UpdateCapacity(realm, cities);
                if (!realm.hasEnemies()) Defect(realm, cities);
                ReleaseOverreach(realm, cities);
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

    // 满控路程：时代 + 本国已采纳的交通通信科技，按政体、君主折算
    private static readonly (string tech, float reach)[] ReachTechs =
        { ("postal_roads", 2f), ("industrialization", 2f), ("telegraph", 3f), ("motorization", 2f), ("aviation", 2f) };
    // 直辖容量：时代 + 本国已采纳的治理科技
    private static readonly (string tech, float cities)[] CapacityTechs =
        { ("bureaucracy", 4f), ("postal_roads", 4f), ("civil_examination", 4f), ("telegraph", 8f), ("computing", 6f) };

    private static float Reach(Kingdom realm)
    {
        int tier = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm));
        float reach = 10f + 2f * tier;
        foreach ((string tech, float bonus) in ReachTechs)
            if (TechnologySystem.RealmHasTech(realm, tech)) reach += bonus;
        reach *= realm.GetRegime()?.type switch
        {
            RegimeType.LvLing => 1.15f,
            RegimeType.Feudalism or RegimeType.ZhouFeudalism => 0.9f,
            _ => 1f
        };
        return reach * RulerFactor(realm);
    }

    public static int Capacity(Kingdom realm)
    {
        int tier = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm));
        float capacity = 4f + 4f * tier;
        foreach ((string tech, float bonus) in CapacityTechs)
            if (TechnologySystem.RealmHasTech(realm, tech)) capacity += bonus;
        // 分封、封建不缩容量：诸侯本就不算直辖，是生产力不够时的统治办法
        if (realm.GetRegime()?.type == RegimeType.LvLing) capacity *= 1.2f;
        return Mathf.Max(3, Mathf.RoundToInt(capacity * RulerFactor(realm)));
    }

    // 君主：雄主 ×1.15、平庸 ×1、懦弱放纵 ×0.9、昏聩 ×0.85、暴君 ×0.95；帝国再按正统 0.85~1.15 折算
    private static float RulerFactor(Kingdom realm)
    {
        float factor = RulerQualitySystem.Of(realm.king) switch
        {
            RulerQualitySystem.Grade.Wise => 1.15f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Dissolute => 0.9f,
            RulerQualitySystem.Grade.Fatuous => 0.85f,
            RulerQualitySystem.Grade.Tyrant => 0.95f,
            _ => 1f
        };
        Empire empire = realm.GetEmpire();
        if (empire?.data != null && realm.IsEmpire())
            factor *= 0.85f + 0.3f * Mathf.Clamp01(MonarchyLegitimacy.Get(empire) / 100f);
        return factor;
    }

    private static void UpdateCapacity(Kingdom realm, List<City> cities)
    {
        var data = realm.GetOrCreate();
        data.admin_capacity = Capacity(realm);
        data.admin_direct_cities = cities.Count(city => !IsIndirect(city.kingdom));
        data.admin_overextension = Mathf.Clamp01((float)data.admin_direct_cities / data.admin_capacity - 1f);
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

    #region 扩张上限

    private static bool Overreached(Kingdom realm, City city, float limit)
    {
        if (!Valid(city) || city.kingdom.capital == city) return false;
        var data = city.GetOrCreate();
        return data.conquered_at >= 0d && data.conquered_by == realm.id && data.admin_control >= 0f &&
               data.admin_control < limit && World.world.getCurWorldTime() >= data.conquered_at &&
               Date.getYearsSince(data.conquered_at) < RecentConquestYears &&
               !(data.OccupiedStatus?.Count > 0) && !city.kingdom.hasEnemies() && !IsIndirect(city.kingdom);
    }

    // 管不到的新征服之地：由当地首领自立，按政体文化转为羁縻州、附庸诸侯或朝贡国
    private static void ReleaseOverreach(Kingdom realm, List<City> cities)
    {
        float limit = HardLimit + OverextendedHardLimit * realm.GetOrCreate().admin_overextension;
        City seed = cities.Where(city => Overreached(realm, city, limit))
            .OrderBy(city => city.GetOrCreate().admin_control).ThenBy(city => city.id).FirstOrDefault();
        if (seed == null) return;
        Kingdom former = seed.kingdom;
        // 连成一片的同属一国的新征服城一起放出
        var cluster = new List<City> { seed };
        var seen = new HashSet<City> { seed };
        for (int i = 0; i < cluster.Count && cluster.Count < ReleaseClusterMax; i++)
            foreach (City next in (IEnumerable<City>)cluster[i].neighbours_cities ?? Array.Empty<City>())
            {
                if (cluster.Count >= ReleaseClusterMax) break;
                if (next == null || !seen.Add(next) || next.kingdom != former || !Overreached(realm, next, limit)) continue;
                cluster.Add(next);
            }
        if (!seed.hasLeader()) CityPopulationSystem.EnsureLeader(seed);
        Actor leader = LocalLeader(seed);
        if (leader == null) return;
        Kingdom local;
        try
        {
            local = seed.makeOwnKingdom(leader);
        }
        catch (Exception error)
        {
            LogService.LogWarning($"[EmpireCraft][扩张上限] {seed.data.name} 自立失败: {error.Message}");
            return;
        }
        if (local == null || local.isRekt() || seed.kingdom != local) return;
        foreach (City city in cluster.Skip(1))
            if (Valid(city) && city.kingdom == former) city.joinAnotherKingdom(local);
        foreach (City city in cluster)
        {
            var data = city.GetOrCreate();
            data.conquered_at = -1d;
            data.conquered_by = -1L;
            data.admin_control = -1f;
        }
        string form = Subordinate(realm, local);
        EventRecorder.Record(realm, string.Format(LM.Get("admin_overreach_" + form + "_history"),
            realm.GetKingdomName(), local.GetKingdomName(), cluster.Count));
        LogService.LogInfo($"[EmpireCraft][扩张上限] {realm.GetKingdomName()} 管不到 {seed.data.name} 等 {cluster.Count} 城，转为 {form}");
    }

    private static Actor LocalLeader(City city)
    {
        Actor current = city.leader;
        if (current != null && !current.isRekt() && current.isAlive() && current.isAdult() && !current.isKing() &&
            current.isUnitFitToRule()) return current;
        return (city.units ?? new List<Actor>()).ToList()
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.isKing() &&
                            actor.isUnitFitToRule())
            .OrderByDescending(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
            .ThenByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
    }

    // 周制天下或青铜时代及以前的帝国 → 分封诸侯(帝国成员)；施行羁縻的帝国 → 羁縻州；
    // 封建政体或不是帝国 → 附庸诸侯；其余帝国 → 朝贡国
    private static string Subordinate(Kingdom realm, Kingdom local)
    {
        Empire empire = realm.IsEmpire() ? realm.GetEmpire() : null;
        if (empire != null && (realm.GetRegime()?.type == RegimeType.ZhouFeudalism ||
                               TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(realm)) <= EarlyEraTier))
        {
            if (local.hasAlliance()) local.getAlliance().leave(local);
            empire.join(local, pForce: true);
            if (local.GetEmpire() == empire)
            {
                local.GetOrCreate().last_kingdom_status_ts = -1d;
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(local);
                return "enfeoff";
            }
        }
        if (empire != null && JimiSystem.Enacted(empire))
        {
            if (local.hasAlliance()) local.getAlliance().leave(local);
            empire.join(local, pForce: true);
            if (local.GetEmpire() == empire)
            {
                var data = local.GetOrCreate();
                data.jimi_mode = (int)JimiMode.Jimi;
                data.last_kingdom_status_ts = -1d;
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(local);
                return "jimi";
            }
        }
        RegimeType? regime = realm.GetRegime()?.type;
        bool feudal = regime == RegimeType.Feudalism || regime == RegimeType.ZhouFeudalism;
        if (empire != null && !feudal)
        {
            local.JoinTakenAlliance(empire, pForce: true);
            if (local.HasTakenAlliance()) return "tributary";
        }
        return FeudalVassalService.ForceBind(realm, local) ? "vassal" : "independent";
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
