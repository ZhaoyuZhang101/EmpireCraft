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
//   商业区：市场、交易所、码头、仓库，也可以建民居(前店后宅)；
//   军事区：兵营、瞭望塔、训练场、炮台碉堡；
//   保护区：什么都不许建。
// 城市中心所在区块不受限制；没划用途的区块照原版自由建。规划前已经建在区块里的建筑不拆。
// 规划决议(国家一级，见 PlanPolicy)：国家定一个总方针，各城 AI 每月按方针补划一块，挑最合适的地块
// (靠矿脉、森林划工业，靠水、靠中心划商业，靠国境划军事，中心周围划居住)；神力手动划的不会被 AI 改掉。
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

public static class ZonePlanSystem
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
        if (city?.data == null || zone == null) return ZoneUse.None;
        if (FarmlandSystem.IsPlanned(city, zone)) return ZoneUse.Farm;
        return Uses(city).TryGetValue(zone.id, out int use) ? (ZoneUse)use : ZoneUse.None;
    }

    // 划定用途(None = 取消)。农耕区交给 FarmlandSystem(取消时退耕还草)
    public static void Set(City city, TileZone zone, ZoneUse use)
    {
        if (city?.data == null || zone == null || zone.city != city) return;
        ZoneUse old = Get(city, zone);
        if (old == use) return;
        if (old == ZoneUse.Farm) FarmlandSystem.TogglePlanned(city, zone);
        else Uses(city).Remove(zone.id);
        if (use == ZoneUse.Farm) FarmlandSystem.TogglePlanned(city, zone);
        else if (use != ZoneUse.None) Uses(city)[zone.id] = (int)use;
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

    // 城市在 pTile 所在区块能不能建 pAsset(城市中心区块、没划用途的区块不限)
    public static bool CanBuildAt(City city, WorldTile tile, BuildingAsset asset)
    {
        TileZone zone = tile?.zone;
        if (city?.data == null || zone == null || asset == null || IsCenter(city, zone)) return true;
        ZoneUse use = Get(city, zone);
        if (use == ZoneUse.None) return true;
        // 不设耕地红线时规划农田不受保护，照样可以盖房
        if (use == ZoneUse.Farm && !FarmlandSystem.IsProtected(city)) return true;
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

    // 各用途的目标区块比例(占非中心区块)；每种最多 MaxZonesPerUse 块
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

    private static readonly ZoneUse[] AutoUses = { ZoneUse.Industry, ZoneUse.Military, ZoneUse.Commerce, ZoneUse.Residential };

    // ---- 每月各城补划(分帧) ----
    private static readonly Queue<City> Pending = new();
    private static double _lastPass = -1d;
    private const double SliceBudgetMs = 1.5d;

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || Config.paused || !Config.game_loaded) return;
        double now = world.getCurWorldTime();
        if (Pending.Count == 0)
        {
            if (_lastPass >= 0d && now >= _lastPass && Date.getMonthsSince(_lastPass) < 1) return;
            _lastPass = now;
            foreach (City city in world.cities)
                if (city?.data != null && !city.isRekt()) Pending.Enqueue(city);
        }
        Stopwatch watch = Stopwatch.StartNew();
        while (Pending.Count > 0 && watch.Elapsed.TotalMilliseconds < SliceBudgetMs)
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
        if (city.zones == null || city.zones.Count < 4) return;
        // 已经不归本城的区块(割让、被占)清掉
        Dictionary<int, int> uses = Uses(city);
        if (uses.Count > 0)
        {
            var owned = new HashSet<int>();
            foreach (TileZone zone in city.zones)
                if (zone != null) owned.Add(zone.id);
            var stale = new List<int>();
            foreach (int id in uses.Keys)
                if (!owned.Contains(id)) stale.Add(id);
            foreach (int id in stale) uses.Remove(id);
        }
        PlanPolicy policy = PolicyOf(city.kingdom);
        int usable = city.zones.Count - 1;
        foreach (ZoneUse use in AutoUses)
        {
            int target = Mathf.Min(MaxZonesPerUse, Mathf.RoundToInt(usable * TargetShare(policy, use)));
            // 有矿场、伐木场的城至少一块工业区(矿场所在的区块)，升级高等级矿场要用
            if (use == ZoneUse.Industry && target < 1 && IndustryBuildingSystem.CountIndustry(city) > 0) target = 1;
            if (target <= 0 || Count(city, use) >= target) continue;
            TileZone best = null;
            float bestScore = 0f;
            foreach (TileZone zone in city.zones)
            {
                if (zone?.tiles == null || IsCenter(city, zone) || Get(city, zone) != ZoneUse.None) continue;
                float score = Score(city, zone, use);
                if (score <= bestScore) continue;
                bestScore = score;
                best = zone;
            }
            if (best != null) uses[best.id] = (int)use;
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
                else conflicting++;
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
