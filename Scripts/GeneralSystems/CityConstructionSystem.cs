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
//   - 首都、首府优先：建设力 ×1.6 / ×1.3，更倾向于升级，民居上限更高。
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
        float rate = Mathf.Min(MaxPointsPerSecond,
            (BasePointsPerSecond + CityPopulationSystem.Households(city) * PointsPerHousehold) * (1f + Wealth(city)) *
            (1f + SeatBonus(city)));
        Kingdom kingdom = city.kingdom;
        bool funded = kingdom != null && !kingdom.wild && kingdom.GetMoney() >= FundingThreshold;
        if (funded) rate *= FundingBonus;
        float points = rate * seconds + data.construction_carry;
        int whole = Mathf.FloorToInt(points);
        data.construction_carry = points - whole;
        int used = Advance(city, whole);
        if (funded && used > 0) kingdom.SubMoney(Mathf.CeilToInt(used / PointsPerFundingGold));
        if (!HasConstruction(city)) StartNext(city);
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
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][城市建设] 开工失败({city.data?.name}): {exception.Message}");
        }
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
        Kingdom kingdom = city.kingdom;
        int fromCity = Mathf.Min(cost, Mathf.Max(0, city.GetMoney()));
        int fromState = cost - fromCity;
        if (fromState > 0 && (kingdom == null || kingdom.wild || kingdom.GetMoney() < fromState)) return false;
        if (!CityBehBuild.upgradeBuilding(building, city)) return false;
        if (fromCity > 0) city.SubMoney(fromCity);
        if (fromState > 0) kingdom.SubMoney(fromState);
        return true;
    }
}
