using System;
using System.Collections.Generic;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城市建设(无小人模式)：没有建筑工以后，施工以城市为单位推进，不再看有几个工人。
//   - 建设力(每秒施工点) = (3 + 户数 × 0.05) × (1 + 富庶度)，最多 30；国家国库充裕时国家拨款，再快一半，
//     每 10 点施工花国库 1 金；
//   - 一座建完马上开下一座，不等原版的建造计时器；
//   - 建什么仍由原版城市规划决定(资源够不够、要不要、科技解锁了没有)，但越富庶的城越倾向于先把已有建筑
//     升级到科技允许的最高一级；升级除了原版的资源外还要花钱，先花城市国库，不够由国家国库出；
//   - 越富庶的城，民居上限越高，房屋越密集(见 NoCommonersPatch.AfterRecalculateMaxHouses)；
//   - 首都、首府优先：建设力 ×1.6 / ×1.3，更倾向于升级，民居上限更高；
//   - 民居跟上科技：每月按本文化掌握的技术直接升级民居，不看人口(见 UpgradeHousing)；
//   - 修路：没有修路工，城市每月自己把建筑之间的路铺上(见 BuildRoads)；
//   - 清理废墟：没有清洁工，城里的废墟每月花钱请人拆(见 ClearRuins)，先花城市国库，不够由国家出；
//     拆下来的木头、石头回到仓库。没钱就留着，废墟占着地方盖不了新房。
// 富庶度 = 经济繁荣度(商人、市民多、高级民居)与城市国库的综合，0~1。
public static class CityConstructionSystem
{
    private const float BasePointsPerSecond = 3f;
    private const float PointsPerHousehold = 0.05f;
    private const float MaxPointsPerSecond = 30f;
    private const int FundingThreshold = 500;
    private const float FundingBonus = 1.5f;
    private const float PointsPerFundingGold = 10f;
    // 城市国库达到这个数算"钱多"(富庶度里国库那一半拉满)
    private const float RichCityMoney = 1000f;
    // 升级的花费：基础 + 升级后建筑施工量的一半
    private const int UpgradeBaseCost = 10;

    // 首都、首府优先：建设力更高、更倾向于升级、房屋更密集
    private const float CapitalBonus = 0.6f;
    private const float ProvincialSeatBonus = 0.3f;

    // 首都 0.6，首府(帝国内藩属王国/行省的首都)0.3，其余 0
    public static float SeatBonus(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.wild || city != kingdom.capital) return 0f;
        return kingdom.IsInEmpire() && !kingdom.IsEmpire() ? ProvincialSeatBonus : CapitalBonus;
    }

    // 富庶度(0~1)
    public static float Wealth(City city)
    {
        if (city == null) return 0f;
        float prosperity = 0f;
        try
        {
            prosperity = Mathf.Clamp01(IdeologyPopulationSystem.CityProsperity(city));
        }
        catch
        {
            // 算不出繁荣度按 0
        }
        float treasury = Mathf.Clamp01(city.GetMoney() / RichCityMoney);
        return Mathf.Clamp01(prosperity * 0.6f + treasury * 0.4f);
    }

    // 每月随人口经济结算一次：按经过的秒数推进施工，空闲时开下一座(或升级)
    public static void Settle(City city, CityPopulationData data, float seconds)
    {
        if (city?.data == null || city.isRekt() || data == null || seconds <= 0f) return;
        StateSettlementPatch.FlushSupplies(city);
        float rate = Mathf.Min(MaxPointsPerSecond,
            (BasePointsPerSecond + CityPopulationSystem.Households(city) * PointsPerHousehold) * (1f + Wealth(city)) *
            (1f + SeatBonus(city)));
        Kingdom kingdom = city.kingdom;
        bool funded = kingdom != null && !kingdom.wild && StateSettlementSystem.DiscretionaryFunds(kingdom) >= FundingThreshold;
        // 国家不拨款时，民间投资池充裕也能出资加快施工
        bool privatelyFunded = !funded && EnterpriseSystem.Pool(city) >= EnterpriseSystem.PrivateFundingThreshold;
        if (funded || privatelyFunded) rate *= FundingBonus;
        float points = rate * seconds + data.construction_carry;
        int whole = Mathf.FloorToInt(points);
        data.construction_carry = points - whole;
        int used = Advance(city, whole);
        if (funded && used > 0)
        {
            int grant = Mathf.Min(StateSettlementSystem.DiscretionaryFunds(kingdom), Mathf.CeilToInt(used / PointsPerFundingGold));
            if (grant > 0)
            {
                kingdom.SubMoney(grant, TreasuryCategory.Construction);
                EnterpriseSystem.ReturnToLocal(city, grant);
            }
        }
        else if (privatelyFunded && used > 0) EnterpriseSystem.SpendPool(city, Mathf.CeilToInt(used / PointsPerFundingGold));
        ClearRuins(city, rate);
        BuildRoads(city, rate);
        UpgradeHousing(city, rate);
        if (!HasConstruction(city)) StartNext(city);
    }

    // ---- 民居跟上科技 ----
    // 原版升级民居要求城里人口够多(按户计)，无小人模式下城市户数少，民居永远停在平房。这里每月直接按科技
    // 升级民居：本文化掌握了上一级民居的技术就能升(见 TechnologySystem.CanBuild)，不看人口。
    // 每月最多 2 + 建设力/5 座，先升等级最低的；花钱同普通升级，还要按等级花建材(见 HousingMaterials)，建材不够就等。
    private static void UpgradeHousing(City city, float rate)
    {
        if (city.buildings == null) return;
        int limit = 2 + Mathf.FloorToInt(rate / 5f);
        var houses = new List<Building>();
        foreach (Building building in city.buildings)
        {
            if (building?.asset == null || !building.asset.hasHousingSlots() || !building.canBeUpgraded()) continue;
            BuildingAsset target = AssetManager.buildings.get(building.asset.upgrade_to ?? "");
            if (target == null || !TechnologySystem.CanBuild(city, target.id)) continue;
            houses.Add(building);
        }
        if (houses.Count == 0) return;
        houses.Sort((a, b) => a.asset.upgrade_level.CompareTo(b.asset.upgrade_level));
        Kingdom kingdom = city.kingdom;
        foreach (Building house in houses)
        {
            if (limit <= 0) break;
            BuildingAsset target = AssetManager.buildings.get(house.asset.upgrade_to);
            // 建材按房屋等级：木屋用木头，砖石房用石头，楼房用铁(金属)加石头；取原版造价和等级造价的大者。
            // 仓库里不够就先不升(建材靠伐木场、矿场和市场)
            (int wood, int stone, int metal) need = HousingMaterials(target);
            if (city.getResourcesAmount("wood") < need.wood || city.getResourcesAmount("stone") < need.stone ||
                city.getResourcesAmount("common_metals") < need.metal) continue;
            int cost = UpgradeBaseCost + target.construction_progress_needed / 2;
            if (!EnterpriseSystem.CanPay(city, cost, privateFirst: true)) break;
            if (!house.upgradeBuilding()) continue;
            // 民居是百姓自己的房子：建材是私用，不由国库向百姓买
            using (PopulationEconomySystem.PrivateUse())
            {
                if (need.wood > 0) city.takeResource("wood", need.wood);
                if (need.stone > 0) city.takeResource("stone", need.stone);
                if (need.metal > 0) city.takeResource("common_metals", need.metal);
            }
            EnterpriseSystem.PayPrivateFirst(city, cost);
            limit--;
        }
    }

    // 升到 target 这一级民居要的建材(木头, 石头, 金属)：
    //   1~2 级木屋：木头 4 + 2×等级；3~5 级砖石房：石头 4 + 2×等级、木头 2；6 级以上楼房：金属 2 + 等级、石头 4 + 等级。
    // 与原版造价逐项取大者
    private static (int wood, int stone, int metal) HousingMaterials(BuildingAsset target)
    {
        int level = Mathf.Max(1, target.upgrade_level + 1);
        int wood, stone, metal;
        if (level <= 2) { wood = 4 + 2 * level; stone = 0; metal = 0; }
        else if (level <= 5) { wood = 2; stone = 4 + 2 * level; metal = 0; }
        else { wood = 0; stone = 4 + level; metal = 2 + level; }
        ConstructionCost cost = target.cost;
        if (cost != null)
        {
            wood = Mathf.Max(wood, cost.wood);
            stone = Mathf.Max(stone, cost.stone);
            metal = Mathf.Max(metal, cost.common_metals);
        }
        return (wood, stone, metal);
    }

    // ---- 修路 ----
    // 没有修路工：每月替城市铺路。原版在建筑落成时排好"要修的路"(相邻建筑之间连一条)，由修路工一格格铺；
    // 这里每月挑几座建筑重新排一次连路，再把排好的路直接铺上(每月最多 RoadTilesPerMonth + 建设力 格)
    private const int RoadTilesPerMonth = 20;
    // 诊断计数(每年随人口诊断记一次后清零)：修路规划次数、规划出的路格、铺下的路格、清掉的废墟、因没钱没清的废墟
    public static int StatRoadPlans, StatRoadPlanned, StatRoadBuilt, StatRuinsCleared, StatRuinsNoMoney;
    // 自己规划失败在哪一步(定位“规划 0 格”)：没有可连的建筑、寻路失败、路太长、路格全被过滤
    public static int StatRoadNoTarget, StatRoadPathFail, StatRoadTooLong, StatRoadFiltered;

    public static string TakeStats()
    {
        string text = $"修路规划 {StatRoadPlans} 次/规划 {StatRoadPlanned} 格/铺设 {StatRoadBuilt} 格" +
                      $"(无目标 {StatRoadNoTarget}/寻路失败 {StatRoadPathFail}/过长 {StatRoadTooLong}/全过滤 {StatRoadFiltered})，" +
                      $"清废墟 {StatRuinsCleared} 座/没钱没清 {StatRuinsNoMoney} 座";
        StatRoadPlans = StatRoadPlanned = StatRoadBuilt = StatRuinsCleared = StatRuinsNoMoney = 0;
        StatRoadNoTarget = StatRoadPathFail = StatRoadTooLong = StatRoadFiltered = 0;
        return text;
    }
    private const int RoadPlansPerMonth = 3;
    private const int MaxRoadPath = 60;
    private const int MaxRoadReach = 40;
    private static readonly List<WorldTile> RoadPath = new();

    private static WorldTile Door(Building building) => building.door_tile ?? building.current_tile;

    // 自己规划连路(不依赖原版“20 格以内、路网岛”那几个条件)：从这座建筑门口修到最近一座还没连上同一片路网的建筑
    private static void PlanRoad(City city, Building building)
    {
        WorldTile from = Door(building);
        if (from?.Type == null || from.Type.liquid) { StatRoadNoTarget++; return; }
        Building target = null;
        int best = MaxRoadReach * MaxRoadReach;
        foreach (Building other in city.buildings)
        {
            if (other == building || other?.asset == null || !other.asset.build_road_to || other.isUnderConstruction()) continue;
            WorldTile door = Door(other);
            if (door == null || door.Type == null || door.Type.liquid || !door.isSameIsland(from)) continue;
            if (from.road_island != null && from.road_island == door.road_island) continue;
            int dx = door.x - from.x, dy = door.y - from.y, distance = dx * dx + dy * dy;
            if (distance >= best || distance == 0) continue;
            best = distance;
            target = other;
        }
        if (target == null) { StatRoadNoTarget++; return; }
        WorldTile to = Door(target);
        RoadPath.Clear();
        World.world.pathfinding_param.resetParam();
        World.world.pathfinding_param.roads = true;
        World.world.calcPath(from, to, RoadPath);
        if (RoadPath.Count == 0) { StatRoadPathFail++; return; }
        if (RoadPath.Count > MaxRoadPath) { StatRoadTooLong++; return; }
        var tiles = new List<WorldTile>();
        foreach (WorldTile tile in RoadPath)
        {
            if (tile?.Type == null || tile.Type.liquid || tile.Type.road || tile.Type.block || tile.building != null) continue;
            tiles.Add(tile);
        }
        if (tiles.Count == 0) { StatRoadFiltered++; return; }
        city.addRoads(tiles);
    }

    private static void BuildRoads(City city, float rate)
    {
        if (city.buildings == null || city.buildings.Count < 2) return;
        try
        {
            for (int i = 0; i < RoadPlansPerMonth && city.road_tiles_to_build.Count == 0; i++)
            {
                Building building = city.buildings[UnityEngine.Random.Range(0, city.buildings.Count)];
                if (building?.asset == null || !building.asset.build_road_to || building.isUnderConstruction()) continue;
                int before = city.road_tiles_to_build.Count;
                CityBehBuild.makeRoadsBuildings(city, building);
                // 原版只连 20 格以内的最近建筑，实测无小人模式下几乎规划不出路；这时自己规划(见 PlanRoad)
                if (city.road_tiles_to_build.Count == before) PlanRoad(city, building);
                StatRoadPlans++;
                StatRoadPlanned += city.road_tiles_to_build.Count - before;
            }
            int limit = RoadTilesPerMonth + Mathf.FloorToInt(rate);
            for (int i = 0; i < limit; i++)
            {
                WorldTile tile = city.getRoadTileToBuild(null);
                if (tile == null) break;
                if (tile.Type == null || tile.Type.liquid || tile.Type.road)
                {
                    city.road_tiles_to_build.Remove(tile);
                    continue;
                }
                MapAction.createRoadTile(tile);
                city.road_tiles_to_build.Remove(tile);
                if (tile.Type != null && tile.Type.road) StatRoadBuilt++;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][城市建设] 修路失败({city.data?.name}): {exception.Message}");
        }
    }

    // ---- 清理废墟 ----
    // 每月最多拆 2 + 建设力/5 座；每座花 RuinBaseCost + 占地格数 金
    private const int RuinBaseCost = 2;

    private static void ClearRuins(City city, float rate)
    {
        if (city.zones == null) return;
        int limit = 2 + Mathf.FloorToInt(rate / 5f);
        Kingdom kingdom = city.kingdom;
        var ruins = new List<Building>();
        foreach (TileZone zone in city.zones)
        {
            HashSet<Building> set = zone?.getHashset(BuildingList.Ruins);
            if (set == null) continue;
            foreach (Building ruin in set)
            {
                if (ruins.Count >= limit) break;
                if (ruin?.asset != null && ruin.isRuin()) ruins.Add(ruin);
            }
            if (ruins.Count >= limit) break;
        }
        int cleared = 0;
        foreach (Building ruin in ruins)
        {
            BuildingFundament fundament = ruin.asset.fundament;
            int cost = RuinBaseCost + (fundament == null ? 1 : Mathf.Max(1, fundament.width * fundament.height / 4));
            // 公家先出，不够由民间集资(工钱都付给本城的人)
            if (!EnterpriseSystem.PayPublicFirst(city, cost) && !EnterpriseSystem.PayPrivateFirst(city, cost))
            {
                StatRuinsNoMoney += ruins.Count - cleared;
                break;
            }
            StatRuinsCleared++;
            try
            {
                if (ruin.asset.cost.wood > 0) city.addResourcesToRandomStockpile("wood", 1);
                if (ruin.asset.cost.stone > 0) city.addResourcesToRandomStockpile("stone", 1);
            }
            catch
            {
                // 没有仓库就不回收
            }
            ruin.startDestroyBuilding();
            cleared++;
        }
        if (LastRuinsCleared.Count > (World.world?.cities?.Count ?? 0) * 2) LastRuinsCleared.Clear();
        LastRuinsCleared[city] = cleared;
    }

    // 上月拆了几座废墟(城市面板用)
    public static readonly Dictionary<City, int> LastRuinsCleared = new();

    public static int CountRuins(City city)
    {
        int count = 0;
        if (city?.zones == null) return 0;
        foreach (TileZone zone in city.zones) count += zone?.getHashset(BuildingList.Ruins)?.Count ?? 0;
        return count;
    }

    private static bool HasConstruction(City city)
    {
        if (city.buildings == null) return false;
        foreach (Building building in city.buildings)
            if (building?.asset != null && building.isUnderConstruction()) return true;
        return false;
    }

    // 推进在建建筑，返回用掉的施工点；建完的建筑从"在建"里清掉
    private static int Advance(City city, int points)
    {
        if (points <= 0 || city.buildings == null) return 0;
        int used = 0;
        try
        {
            foreach (Building building in new List<Building>(city.buildings))
            {
                if (used >= points) break;
                if (building?.asset == null || !building.isUnderConstruction()) continue;
                int needed = Mathf.Max(1, building.asset.construction_progress_needed + 1 - building.getConstructionProgress());
                int share = Mathf.Min(needed, points - used);
                bool done = building.updateBuild(share);
                used += share;
                if (done && city.under_construction_building == building) city.under_construction_building = null;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][城市建设] 推进施工失败({city.data?.name}): {exception.Message}");
        }
        return used;
    }

    // 开下一座：越富庶越倾向于升级已有建筑，否则交给原版城市规划新建
    private static void StartNext(City city)
    {
        try
        {
            city.under_construction_building = null;
            if (UnityEngine.Random.value < 0.3f + 0.6f * Wealth(city) + SeatBonus(city) && TryUpgrade(city)) return;
            CityBehBuild.buildTick(city);
            EnterpriseSystem.OnConstructionStarted(city, city.under_construction_building);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][城市建设] 开工失败({city.data?.name}): {exception.Message}");
        }
    }

    public static void StartSettlement(City city)
    {
        if (city == null || city.isRekt() || HasConstruction(city)) return;
        // 新城没有可升级建筑，正常建造顺序先安置居民和农业，后续按月施工。
        StartNext(city);
    }

    // 升级：在原版允许的升级订单里(资源够、科技已解锁)挑一个，花原版资源外再花钱(城市国库优先，不够国家出)
    private static bool TryUpgrade(City city)
    {
        CityBuildOrderAsset orders = AssetManager.city_build_orders.get(city.getActorAsset()?.build_order_template_id ?? "");
        if (orders?.list == null) return false;
        var candidates = new List<BuildOrder>();
        foreach (BuildOrder order in orders.list)
        {
            if (order == null || !order.upgrade) continue;
            if (!CityBehBuild.canUseBuildAsset(order, city) || !CityBehBuild.hasResourcesForBuildAsset(order, city)) continue;
            if (!TechnologyPatch.CanUseBuildOrder(order, city)) continue;
            candidates.Add(order);
        }
        if (candidates.Count == 0) return false;
        BuildOrder chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        List<Building> buildings = city.getBuildingListOfID(chosen.getBuildingAsset(city).id);
        if (buildings == null || buildings.Count == 0) return false;
        Building building = buildings[UnityEngine.Random.Range(0, buildings.Count)];
        if (building == null || !building.canBeUpgraded()) return false;
        BuildingAsset target = AssetManager.buildings.get(building.asset.upgrade_to);
        if (target == null) return false;
        int cost = UpgradeBaseCost + target.construction_progress_needed / 2;
        bool privateOwned = building.asset.hasHousingSlots() ||
                            EnterpriseSystem.IsEnterprise(building.asset) && !EnterpriseSystem.IsPublic(city, building);
        if (!EnterpriseSystem.CanPay(city, cost, privateOwned)) return false;
        if (!CityBehBuild.upgradeBuilding(building, city)) return false;
        if (privateOwned) EnterpriseSystem.PayPrivateFirst(city, cost);
        else EnterpriseSystem.PayPublicFirst(city, cost);
        return true;
    }
}
