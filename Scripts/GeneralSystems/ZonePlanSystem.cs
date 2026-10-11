using System;
using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 区块用途(城市规划)。城里每个区块可以划一种用途，划了用途的区块只许建对应的建筑：
//   农耕区 = 规划农田(见 FarmlandSystem，存在 farm_zones 里)，只许建风车；
//   工业区：矿场、伐木场、工厂、仓库；矿场、伐木场升到第 3 级以上必须在工业区(见 IndustryBuildingSystem)；
//   居住区：民居和水井、神庙、图书馆等配套；
//   商业区：市场、交易所、码头；建立住宅规划后，新住房集中在住宅区。
//   军事区：兵营、瞭望塔、训练场、炮台碉堡；
//   保护区：什么都不许建。
// 工业、住宅各为一个连片区域，只向相邻区块扩展；已有住房为工业和风车范围内的农田让位。
// 规划决议(国家一级，见 PlanPolicy)：国家定一个总方针，各城 AI 每月按方针补划一块，挑最合适的地块
// (靠矿脉、森林划工业，靠水、靠中心划商业，靠国境划军事，中心周围划居住)。旧地图或领土变动后收拢断开的规划。
public enum ZoneUse
{
    None = 0,
    Farm = 1,
    Industry = 2,
    Residential = 3,
    Commerce = 4,
    Military = 5,
    Reserve = 6
}

// 规划决议：重农抑商、农商并重、工业化(工业时代以后)、重商
public enum PlanPolicy
{
    Agrarian = 0,
    Balanced = 1,
    Industrial = 2,
    Mercantile = 3
}

public static partial class ZonePlanSystem
{
    // ---- 区块用途的读写 ----
    private static Dictionary<int, int> Uses(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.zone_uses ??= new Dictionary<int, int>();
        return data.zone_uses;
    }

    public static ZoneUse Get(City city, TileZone zone)
    {
        if (city?.data == null || zone?.city != city) return ZoneUse.None;
        if (FarmlandSystem.IsPlanned(city, zone)) return ZoneUse.Farm;
        return Uses(city).TryGetValue(zone.id, out int use) ? (ZoneUse)use : ZoneUse.None;
    }

    // 划定用途(None = 取消)。农耕区交给 FarmlandSystem(取消时退耕还草)
    public static bool Set(City city, TileZone zone, ZoneUse use)
    {
        if (city?.data == null || zone == null || zone.city != city) return false;
        InvalidatePlanning(city);
        NormalizeDistricts(city);
        if (use == ZoneUse.Farm && !FarmlandSystem.HasFarmArea(city, zone)) return false;
        if (!CanAddToDistrict(city, zone, use)) return false;
        ZoneUse old = Get(city, zone);
        if (old == use) return true;
        if (old == ZoneUse.Farm) FarmlandSystem.TogglePlanned(city, zone);
        else Uses(city).Remove(zone.id);
        if (use == ZoneUse.Farm) FarmlandSystem.TogglePlanned(city, zone);
        else if (use != ZoneUse.None) Uses(city)[zone.id] = (int)use;
        NormalizeDistricts(city);
        ClearHousing(city, zone, use);
        return true;
    }

    public static bool IsHousing(BuildingAsset asset) => asset != null &&
        (asset.type == "type_house" || asset.hasHousingSlots() && UseOf(asset) == ZoneUse.Residential);

    private static bool IsDistrict(ZoneUse use) => use == ZoneUse.Industry || use == ZoneUse.Residential;

    private static bool Touches(TileZone zone, City city, ZoneUse use)
    {
        if (zone?.neighbours == null) return false;
        foreach (TileZone neighbour in zone.neighbours)
            if (neighbour?.city == city && Get(city, neighbour) == use) return true;
        return false;
    }

    // 一个区域可以含多个相连的区块，但不能另起第二片；住宅可经过城市中心连通。
    public static bool CanAddToDistrict(City city, TileZone zone, ZoneUse use)
    {
        if (!IsDistrict(use)) return true;
        if (city?.data == null || zone?.city != city) return false;
        if (Get(city, zone) == use || Count(city, use) == 0 || Touches(zone, city, use)) return true;
        TileZone center = city.getTile()?.zone;
        if (use != ZoneUse.Residential || center?.city != city || !Touches(center, city, use) ||
            zone.neighbours == null) return false;
        foreach (TileZone neighbour in zone.neighbours)
            if (neighbour == center) return true;
        return false;
    }

    private static void NormalizeDistricts(City city)
    {
        NormalizeDistrict(city, ZoneUse.Industry);
        NormalizeDistrict(city, ZoneUse.Residential);
    }

    // 旧地图、割让和手动改用途可能留下断开的规划；保留已有同类建筑最多的连片区域。
    private static void NormalizeDistrict(City city, ZoneUse use)
    {
        if (city?.zones == null) return;
        var members = new HashSet<TileZone>();
        foreach (TileZone zone in city.zones)
            if (zone?.city == city && Get(city, zone) == use) members.Add(zone);
        if (members.Count <= 1) return;
        TileZone center = city.getTile()?.zone;
        if (use == ZoneUse.Residential && center?.city == city && Touches(center, city, use)) members.Add(center);
        var unseen = new HashSet<TileZone>(members);
        HashSet<TileZone> best = null;
        float bestScore = float.MinValue;
        int bestId = int.MaxValue;
        foreach (TileZone seed in city.zones)
        {
            if (seed == null || !unseen.Remove(seed)) continue;
            var component = new HashSet<TileZone> { seed };
            var pending = new Queue<TileZone>();
            pending.Enqueue(seed);
            float score = 0f;
            int firstId = seed.id;
            while (pending.Count > 0)
            {
                TileZone zone = pending.Dequeue();
                firstId = Math.Min(firstId, zone.id);
                score += 100f + Score(city, zone, use);
                HashSet<Building> buildings = zone.getHashset(BuildingList.Civs);
                if (buildings != null)
                    foreach (Building building in buildings)
                        if (building?.asset != null && building.city == city && !building.isOnRemove() &&
                            (use == ZoneUse.Residential ? IsHousing(building.asset) : UseOf(building.asset) == use))
                            score += 1000f;
                if (zone.neighbours == null) continue;
                foreach (TileZone neighbour in zone.neighbours)
                    if (neighbour != null && unseen.Remove(neighbour))
                    {
                        component.Add(neighbour);
                        pending.Enqueue(neighbour);
                    }
            }
            if (score < bestScore || score == bestScore && firstId >= bestId) continue;
            best = component;
            bestScore = score;
            bestId = firstId;
        }
        if (best == null) return;
        foreach (TileZone zone in members)
            if (!best.Contains(zone) && Get(city, zone) == use) Uses(city).Remove(zone.id);
    }

    public static void OnFarmPlanned(City city, TileZone zone)
    {
        InvalidatePlanning(city);
        Uses(city).Remove(zone.id);
        NormalizeDistricts(city);
        ClearHousing(city, zone, ZoneUse.Farm);
    }

    // 先收集再拆除，避免原版移除建筑时改变正在遍历的集合；不拆其他配套或外国建筑。
    public static int ClearHousing(City city, TileZone zone, ZoneUse use)
    {
        if (zone?.city != city || zone.tiles == null || use != ZoneUse.Industry && use != ZoneUse.Farm) return 0;
        var houses = new HashSet<Building>();
        foreach (WorldTile tile in zone.tiles)
        {
            Building building = tile?.building;
            if (building?.asset == null || building.city != city || building.isOnRemove() || !IsHousing(building.asset)) continue;
            if (use == ZoneUse.Farm && (!FarmlandSystem.IsWithinWindmillRange(city, tile) ||
                tile.Type == null || !tile.Type.can_be_farm && !tile.Type.farm_field)) continue;
            houses.Add(building);
        }
        foreach (Building house in houses) house.startDestroyBuilding();
        return houses.Count;
    }

    public static int Count(City city, ZoneUse use)
    {
        if (city?.zones == null) return 0;
        int count = 0;
        foreach (TileZone zone in city.zones)
            if (zone != null && Get(city, zone) == use) count++;
        return count;
    }

    public static bool IsCenter(City city, TileZone zone) => zone != null && city?.getTile()?.zone == zone;

    // ---- 用途与建筑 ----
    // 建筑属于哪种用途(决定新建时优先挑哪种区块)
    public static ZoneUse UseOf(BuildingAsset asset)
    {
        if (asset == null) return ZoneUse.None;
        string type = asset.type ?? "";
        switch (type)
        {
            case "type_windmill":
            case AnimalHusbandrySystem.PastureType:
                return ZoneUse.Farm;
            case "type_mine":
            case "type_stockpile":
            case IndustryBuildingSystem.LumberType:
            case AnimalHusbandrySystem.SlaughterhouseType:
                return ZoneUse.Industry;
            case "type_market":
            case "type_stock_exchange":
                return ZoneUse.Commerce;
            case "type_barracks":
            case "type_watch_tower":
            case "type_training_dummies":
            case "type_artillerybunker":
            case "type_cannonbattery":
            case "type_gatlingnest":
                return ZoneUse.Military;
        }
        if (TechnologySystem.IsFactory(asset)) return ZoneUse.Industry;
        if (asset.hasHousingSlots()) return ZoneUse.Residential;
        return ZoneUse.None;
    }

    public static bool Allows(ZoneUse use, BuildingAsset asset)
    {
        if (use == ZoneUse.None || asset == null) return true;
        if (use == ZoneUse.Reserve) return false;
        string type = asset.type ?? "";
        switch (use)
        {
            case ZoneUse.Farm:
                return type == "type_windmill" || type == AnimalHusbandrySystem.PastureType;
            case ZoneUse.Industry:
                return type == "type_mine" || type == IndustryBuildingSystem.LumberType || type == "type_stockpile" ||
                       type == AnimalHusbandrySystem.SlaughterhouseType || TechnologySystem.IsFactory(asset);
            case ZoneUse.Residential:
                return asset.hasHousingSlots() || type == "type_well" || type == "type_bonfire" || type == "type_statue" ||
                       type == "type_temple" || type == "type_library" || type == "type_university" ||
                       type == "type_garden" || type == "type_fountain" || type == "type_hall";
            case ZoneUse.Commerce:
                return asset.hasHousingSlots() || asset.docks || type == "type_market" || type == "type_stock_exchange" ||
                       type == "type_stockpile" || type == "type_docks" || type == "type_well" || type == "type_fountain";
            case ZoneUse.Military:
                return UseOf(asset) == ZoneUse.Military;
        }
        return true;
    }

    // 工业和住房受连片规划约束；其他建筑再按区块用途和城市中心规则判断。
    public static bool CanBuildAt(City city, WorldTile tile, BuildingAsset asset)
    {
        if (!CanBuildOnTile(city, tile, asset)) return false;
        // 住房和厂房的地基也必须符合规划，防止中心在区外、边缘占田，造成反复拆建。
        BuildingFundament foundation = asset?.fundament;
        if (tile == null || foundation == null || World.world == null ||
            !IsHousing(asset) && UseOf(asset) != ZoneUse.Industry) return true;
        for (int x = tile.x - foundation.left; x <= tile.x + foundation.right; x++)
            for (int y = tile.y - foundation.bottom; y <= tile.y + foundation.top; y++)
            {
                WorldTile covered = World.world.GetTile(x, y);
                if (covered == null || !CanBuildOnTile(city, covered, asset)) return false;
            }
        return true;
    }

    private static bool CanBuildOnTile(City city, WorldTile tile, BuildingAsset asset)
    {
        TileZone zone = tile?.zone;
        if (city?.data == null || zone == null || asset == null) return true;
        ZoneUse use = Get(city, zone);
        bool housing = IsHousing(asset);
        if (use == ZoneUse.Industry && housing) return false;
        if (use == ZoneUse.Farm)
        {
            bool inRange = FarmlandSystem.IsWithinWindmillRange(city, tile);
            if (inRange && housing) return false;
            if (!inRange || !FarmlandSystem.IsProtected(city)) use = ZoneUse.None;
        }
        // 用途规划一旦建立，满了也不能回退到全城乱建；城市中心也不例外。
        if (UseOf(asset) == ZoneUse.Industry && Count(city, ZoneUse.Industry) > 0 && use != ZoneUse.Industry) return false;
        if (housing && Count(city, ZoneUse.Residential) > 0 && use != ZoneUse.Residential &&
            !(IsCenter(city, zone) && Touches(zone, city, ZoneUse.Residential))) return false;
        if (IsCenter(city, zone) || use == ZoneUse.None) return true;
        return Allows(use, asset);
    }

    // ---- 规划决议 ----
    // 手动定下的决议这么多年内 AI 不改；AI 每隔这么多年重新评估一次
    private const double ManualLockYears = 30d;
    private const double ReviewYears = 5d;

    public static bool CanAdopt(Kingdom kingdom, PlanPolicy policy) =>
        policy != PlanPolicy.Industrial || IsIndustrialAge(kingdom);

    public static bool IsIndustrialAge(Kingdom kingdom) =>
        kingdom != null && (ModernStability.IsModern(kingdom) ||
                            TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(kingdom)) >= 7);

    public static PlanPolicy PolicyOf(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.wild || kingdom.isRekt()) return PlanPolicy.Balanced;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        double now = World.world.getCurWorldTime();
        bool locked = data.plan_policy_locked_at >= 0d && now >= data.plan_policy_locked_at &&
                      Date.getYearsSince(data.plan_policy_locked_at) < ManualLockYears;
        bool fresh = data.plan_policy >= 0 && data.plan_policy_reviewed >= 0d && now >= data.plan_policy_reviewed &&
                     Date.getYearsSince(data.plan_policy_reviewed) < ReviewYears;
        if (!locked && !fresh)
        {
            PlanPolicy chosen = ChoosePolicy(kingdom);
            PlanPolicy? previous = data.plan_policy >= 0 ? (PlanPolicy)data.plan_policy : null;
            data.plan_policy = (int)chosen;
            data.plan_policy_reviewed = now;
            if (previous.HasValue && previous.Value != chosen) AnnouncePolicy(kingdom, chosen);
        }
        PlanPolicy policy = (PlanPolicy)Mathf.Clamp(data.plan_policy, 0, 3);
        return CanAdopt(kingdom, policy) ? policy : PlanPolicy.Balanced;
    }

    // 神力：换成下一个可选的决议，锁定 ManualLockYears 年
    public static PlanPolicy CyclePolicy(Kingdom kingdom)
    {
        PlanPolicy current = PolicyOf(kingdom);
        PlanPolicy next = current;
        for (int i = 1; i <= 4; i++)
        {
            next = (PlanPolicy)(((int)current + i) % 4);
            if (CanAdopt(kingdom, next)) break;
        }
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        data.plan_policy = (int)next;
        double now = World.world.getCurWorldTime();
        data.plan_policy_reviewed = now;
        data.plan_policy_locked_at = now;
        AnnouncePolicy(kingdom, next);
        return next;
    }

    // AI：闹饥荒就重农；工业时代、国库宽裕就工业化；沿海城多就重商；铁器时代以后农商并重；更早重农抑商
    private static PlanPolicy ChoosePolicy(Kingdom kingdom)
    {
        int cities = 0, hungry = 0, coastal = 0;
        foreach (City city in kingdom.getCities())
        {
            if (city?.data == null) continue;
            cities++;
            if (CityPopulationSystem.Get(city)?.last_food_shortage > 0f) hungry++;
            if (city.hasBuildingType("type_docks")) coastal++;
        }
        if (cities == 0) return PlanPolicy.Balanced;
        if (hungry * 3 >= cities) return PlanPolicy.Agrarian;
        if (IsIndustrialAge(kingdom) && kingdom.GetMoney() >= 300) return PlanPolicy.Industrial;
        int era = TechnologySystem.GetEraTier(TechnologySystem.GetCultureOf(kingdom));
        if (era >= 5 && coastal * 2 >= cities) return PlanPolicy.Mercantile;
        return era >= 4 ? PlanPolicy.Balanced : PlanPolicy.Agrarian;
    }

    private static void AnnouncePolicy(Kingdom kingdom, PlanPolicy policy)
    {
        try
        {
            TranslateHelper.LogEventMessage(string.Format(LM.Get("plan_policy_adopted"), kingdom.GetKingdomName(),
                PolicyName(policy)), kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][规划决议] 记录失败: {exception.Message}");
        }
    }

    public static string PolicyName(PlanPolicy policy) => LM.Get("plan_policy_" + policy.ToString().ToLowerInvariant());
    public static string UseName(ZoneUse use) => LM.Get("zone_use_" + use.ToString().ToLowerInvariant());

    // 决议对耕地红线的影响(乘在红线比例上)
    public static float FarmFactor(Kingdom kingdom) => PolicyOf(kingdom) switch
    {
        PlanPolicy.Agrarian => 1.4f,
        PlanPolicy.Industrial => 0.7f,
        PlanPolicy.Mercantile => 0.85f,
        _ => 1f
    };

    // 各用途的常规目标区块比例(占非中心区块)；连片工业和住宅满了可继续按需扩展。
    private const int MaxZonesPerUse = 8;

    private static float TargetShare(PlanPolicy policy, ZoneUse use) => (policy, use) switch
    {
        (PlanPolicy.Agrarian, ZoneUse.Industry) => 0.06f,
        (PlanPolicy.Agrarian, ZoneUse.Commerce) => 0.03f,
        (PlanPolicy.Agrarian, ZoneUse.Residential) => 0.2f,
        (PlanPolicy.Agrarian, ZoneUse.Military) => 0.04f,
        (PlanPolicy.Balanced, ZoneUse.Industry) => 0.1f,
        (PlanPolicy.Balanced, ZoneUse.Commerce) => 0.08f,
        (PlanPolicy.Balanced, ZoneUse.Residential) => 0.22f,
        (PlanPolicy.Balanced, ZoneUse.Military) => 0.04f,
        (PlanPolicy.Industrial, ZoneUse.Industry) => 0.2f,
        (PlanPolicy.Industrial, ZoneUse.Commerce) => 0.08f,
        (PlanPolicy.Industrial, ZoneUse.Residential) => 0.25f,
        (PlanPolicy.Industrial, ZoneUse.Military) => 0.05f,
        (PlanPolicy.Mercantile, ZoneUse.Industry) => 0.08f,
        (PlanPolicy.Mercantile, ZoneUse.Commerce) => 0.16f,
        (PlanPolicy.Mercantile, ZoneUse.Residential) => 0.22f,
        (PlanPolicy.Mercantile, ZoneUse.Military) => 0.03f,
        _ => 0f
    };

    private static readonly ZoneUse[] AutoUses = { ZoneUse.Industry, ZoneUse.Residential, ZoneUse.Military, ZoneUse.Commerce };

    public static void RequestSpace(City city, ZoneUse use)
    {
        if (city?.data != null && IsDistrict(use)) city.GetOrCreate().district_space_requests |= 1 << (int)use;
    }

    public static bool IsPreferredZone(City city, TileZone zone, ZoneUse use) =>
        Get(city, zone) == use || use == ZoneUse.Residential && IsCenter(city, zone) &&
        Touches(zone, city, ZoneUse.Residential);

    // ---- 每月各城补划(分帧) ----
    private static readonly Queue<City> Pending = new();
    private static double _lastPass = -1d;
    private const double SliceBudgetMs = 1.5d;

    public static void ResetWorldState()
    {
        ResetContinuation();
        Pending.Clear();
        _lastPass = -1d;
        Flashes.Clear();
        _overlayTimer = 0f;
    }

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || Config.paused || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (CityPopulationSystem.AbstractPopulationEnabled) { TickContinuation(world); return; }
        if (_planningCity != null) Pending.Enqueue(_planningCity);
        CancelContinuation();
        double now = world.getCurWorldTime();
        if (Pending.Count == 0)
        {
            if (_lastPass >= 0d && now >= _lastPass && Date.getMonthsSince(_lastPass) < 1) return;
            _lastPass = now;
            foreach (City city in world.cities)
                if (city?.data != null && !city.isRekt()) Pending.Enqueue(city);
        }
        Stopwatch watch = Stopwatch.StartNew();
        while (Pending.Count > 0 && watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime)
        {
            City city = Pending.Dequeue();
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
            try
            {
                PlanCity(city);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][城市规划] 规划失败({city.data?.name}): {exception.Message}");
            }
        }
    }

    private static void PlanCity(City city)
    {
        if (city.zones == null) return;
        // 已经不归本城的区块(割让、被占)清掉
        Dictionary<int, int> uses = Uses(city);
        if (uses.Count > 0)
        {
            var owned = new HashSet<int>();
            foreach (TileZone zone in city.zones)
                if (zone?.city == city) owned.Add(zone.id);
            var stale = new List<int>();
            foreach (int id in uses.Keys)
                if (!owned.Contains(id)) stale.Add(id);
            foreach (int id in stale) uses.Remove(id);
        }
        NormalizeDistricts(city);
        foreach (TileZone zone in city.zones)
            if (zone?.city == city) ClearHousing(city, zone, Get(city, zone));
        if (city.zones.Count < 2) return;
        PlanPolicy policy = PolicyOf(city.kingdom);
        int usable = city.zones.Count - 1;
        foreach (ZoneUse use in AutoUses)
        {
            int target = Mathf.Min(MaxZonesPerUse, Mathf.RoundToInt(usable * TargetShare(policy, use)));
            // 即使城很小，已有厂房或仓库也需要一个集中的工业区。
            if (use == ZoneUse.Industry && target < 1 && HasIndustryInCity(city)) target = 1;
            if (use == ZoneUse.Residential && target < 1) target = 1;
            int current = Count(city, use);
            bool needsSpace = IsDistrict(use) && (city.GetOrCreate().district_space_requests & 1 << (int)use) != 0;
            if (needsSpace) target = Mathf.Max(target, Mathf.Min(usable, current + 1));
            if (target <= 0 || current >= target) continue;
            TileZone best = null;
            float bestScore = 0f;
            foreach (TileZone zone in city.zones)
            {
                if (zone?.city != city || zone.tiles == null || IsCenter(city, zone) || Get(city, zone) != ZoneUse.None ||
                    !CanAddToDistrict(city, zone, use)) continue;
                float score = Score(city, zone, use);
                if (score <= 0f) continue;
                if (IsDistrict(use))
                {
                    // 相邻边越多越紧凑，避免区域长成细长条。
                    int neighbours = 0;
                    if (zone.neighbours != null)
                        foreach (TileZone neighbour in zone.neighbours)
                            if (neighbour?.city == city && (Get(city, neighbour) == use ||
                                use == ZoneUse.Residential && IsCenter(city, neighbour))) neighbours++;
                    score += neighbours * 12f;
                }
                if (score <= bestScore) continue;
                bestScore = score;
                best = zone;
            }
            if (best != null && Set(city, best, use)) city.GetOrCreate().district_space_requests &= ~(1 << (int)use);
        }
    }

    // 地块适合做某种用途的程度(> 0 才会划)
    private static float Score(City city, TileZone zone, ZoneUse use)
    {
        WorldTile center = city.getTile();
        float distance = center == null || zone.centerTile == null
            ? 0f
            : Mathf.Sqrt(Toolbox.SquaredDistTile(center, zone.centerTile));
        int fitting = 0, conflicting = 0;
        HashSet<Building> civs = zone.getHashset(BuildingList.Civs);
        if (civs != null)
            foreach (Building building in civs)
            {
                if (building?.asset == null || building.city != city) continue;
                if (Allows(use, building.asset)) fitting++;
                else if (use != ZoneUse.Industry || !IsHousing(building.asset)) conflicting++;
            }
        int water = 0, buildable = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.Type == null) continue;
            if (tile.Type.liquid) water++;
            else buildable++;
        }
        if (buildable < 16) return 0f;
        float score = 1f + fitting * 3f - conflicting * 2f;
        switch (use)
        {
            case ZoneUse.Industry:
                score += Count(zone, BuildingList.Minerals) * 2f + Count(zone, BuildingList.Trees) * 0.3f;
                if (HasIndustryBuilding(zone, city)) score += 20f;
                score += distance * 0.02f;
                break;
            case ZoneUse.Commerce:
                score += Mathf.Min(water, 12) * 0.8f;
                score += Mathf.Max(0f, 10f - distance * 0.1f);
                break;
            case ZoneUse.Residential:
                score += Mathf.Max(0f, 20f - distance * 0.25f);
                break;
            case ZoneUse.Military:
                score += BorderScore(city, zone) * 6f;
                break;
        }
        return Mathf.Max(0f, score);
    }

    private static int Count(TileZone zone, BuildingList list) => zone.getHashset(list)?.Count ?? 0;

    private static bool HasIndustryBuilding(TileZone zone, City city)
    {
        HashSet<Building> civs = zone.getHashset(BuildingList.Civs);
        if (civs == null) return false;
        foreach (Building building in civs)
            if (building?.asset != null && building.city == city && UseOf(building.asset) == ZoneUse.Industry) return true;
        return false;
    }

    private static bool HasIndustryInCity(City city)
    {
        if (city?.buildings == null) return false;
        foreach (Building building in city.buildings)
            if (building?.asset != null && building.city == city && !building.isOnRemove() &&
                UseOf(building.asset) == ZoneUse.Industry) return true;
        return false;
    }

    // 挨着别国城市区块的边数
    private static int BorderScore(City city, TileZone zone)
    {
        int border = 0;
        if (zone.neighbours_all == null) return 0;
        foreach (TileZone neighbour in zone.neighbours_all)
        {
            City other = neighbour?.city;
            if (other != null && other != city && other.kingdom != city.kingdom) border++;
        }
        return border;
    }

    // ---- 地图显示：选中城市规划类神力时，屏幕内划了用途的区块按颜色高亮 ----
    public static readonly HashSet<string> PlanningPowers = new()
    {
        "farm_planning", "zone_industry", "zone_residential", "zone_commerce", "zone_military", "zone_reserve"
    };

    public static Color ColorOf(ZoneUse use) => use switch
    {
        ZoneUse.Farm => new Color(0.45f, 0.85f, 0.35f),
        ZoneUse.Industry => new Color(0.75f, 0.45f, 0.2f),
        ZoneUse.Residential => new Color(0.95f, 0.85f, 0.4f),
        ZoneUse.Commerce => new Color(0.35f, 0.6f, 0.95f),
        ZoneUse.Military => new Color(0.9f, 0.25f, 0.25f),
        ZoneUse.Reserve => new Color(0.2f, 0.55f, 0.3f),
        _ => new Color(0.6f, 0.6f, 0.6f)
    };

    private static readonly Dictionary<TileZone, ZoneFlash> Flashes = new();
    private static float _overlayTimer;
    private const float OverlayInterval = 1f;
    private const int MaxOverlayZones = 300;

    public static void TickOverlay()
    {
        MapBox world = World.world;
        if (world == null || !Config.game_loaded) return;
        GodPower power = world.selected_power;
        if (power == null || !PlanningPowers.Contains(power.id))
        {
            if (Flashes.Count > 0) Flashes.Clear();
            return;
        }
        _overlayTimer -= Time.unscaledDeltaTime;
        if (_overlayTimer > 0f) return;
        _overlayTimer = OverlayInterval;
        Camera camera = Camera.main;
        if (camera == null) return;
        Vector3 min = camera.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 max = camera.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));
        int shown = 0;
        foreach (City city in world.cities)
        {
            if (city?.zones == null || city.isRekt()) continue;
            foreach (TileZone zone in city.zones)
            {
                if (shown >= MaxOverlayZones) return;
                WorldTile center = zone?.centerTile;
                if (center == null || center.x < min.x - 8 || center.x > max.x + 8 || center.y < min.y - 8 ||
                    center.y > max.y + 8) continue;
                ZoneUse use = Get(city, zone);
                if (use == ZoneUse.None) continue;
                shown++;
                Color color = ColorOf(use);
                if (Flashes.TryGetValue(zone, out ZoneFlash flash) && flash != null && flash.gameObject.activeSelf)
                {
                    flash.start(color, 0.35f);
                    continue;
                }
                flash = EffectsLibrary.spawn("fx_zone_highlight", center, null, null, 0.35f) as ZoneFlash;
                if (flash == null) continue;
                flash.start(color, 0.35f);
                Flashes[zone] = flash;
            }
        }
    }
}
