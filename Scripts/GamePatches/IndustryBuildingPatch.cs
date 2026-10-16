using System;
using System.Collections.Generic;
using ai.behaviours;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 矿场伐木场分级的原版接入点(城市规划撤掉后留下的部分，用户 2026-10-17 "之前的城市规划的那一套和图层都得取消掉了")：
//   - 规划农田(见 FarmlandSystem)：风车范围里不盖民居，田受保护时只许风车、牧场、仓库(原城市规划里农田那条)；
//   - 伐木场挑树多的地块建(建在林子边上，不挑规划农田)；
//   - 拆除中的建筑不能升级，矿场伐木场的高等级要有工厂(见 IndustryBuildingSystem.CanUpgradeHere)；
//   - 矿场、伐木场、屠宰场的额外配额，森林多的城多建伐木场，游牧国多建牧场(City.getLimitOfBuildingsType)。
public class IndustryBuildingPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        MineralResourceSystem.Init();
        ImperialSealSystem.Init();
        IndustryBuildingSystem.Init();
        var harmony = new Harmony(nameof(IndustryBuildingPatch));
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.isGoodTileForBuilding)),
                postfix: new HarmonyMethod(typeof(IndustryBuildingPatch), nameof(AfterIsGoodTile)));
            harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.tryToBuildInZones)),
                prefix: new HarmonyMethod(typeof(IndustryBuildingPatch), nameof(BeforeTryToBuildInZones)));
            harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.upgradeBuilding)),
                prefix: new HarmonyMethod(typeof(IndustryBuildingPatch), nameof(BeforeUpgradeBuilding)));
            harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getLimitOfBuildingsType)),
                postfix: new HarmonyMethod(typeof(IndustryBuildingPatch), nameof(AfterLimitOfBuildingsType)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 矿场伐木场补丁未生效: {exception.Message}");
        }
    }

    public static void AfterIsGoodTile(WorldTile pTile, BuildingAsset pAsset, City pCity, ref bool __result)
    {
        if (!__result || pTile?.zone == null || pAsset == null || pCity?.data == null ||
            !FarmlandSystem.IsPlanned(pCity, pTile.zone) || !FarmlandSystem.IsWithinWindmillRange(pCity, pTile)) return;
        string type = pAsset.type ?? "";
        if (pAsset.hasHousingSlots() || type == "type_house")
        {
            __result = false;
            return;
        }
        if (FarmlandSystem.IsProtected(pCity) && type != "type_windmill" && type != AnimalHusbandrySystem.PastureType &&
            type != "type_stockpile")
            __result = false;
    }

    [ThreadStatic] private static bool _preferring;

    // 伐木场：挑树最多的几块地建(建在林子边上)；挑的几块都建不下就照原版
    public static bool BeforeTryToBuildInZones(List<TileZone> pList, BuildingAsset pBuildingAsset, City pCity,
        bool pForceCenterZone, ref WorldTile __result)
    {
        if (_preferring || pForceCenterZone || pList == null || pCity?.data == null ||
            !IndustryBuildingSystem.IsLumber(pBuildingAsset)) return true;
        var woods = new List<TileZone>();
        foreach (TileZone zone in pList)
            if (zone != null && !FarmlandSystem.IsPlanned(pCity, zone) && (zone.getHashset(BuildingList.Trees)?.Count ?? 0) >= 6)
                woods.Add(zone);
        if (woods.Count == 0) return true;
        woods.Sort((a, b) => (b.getHashset(BuildingList.Trees)?.Count ?? 0)
            .CompareTo(a.getHashset(BuildingList.Trees)?.Count ?? 0));
        if (woods.Count > 4) woods.RemoveRange(4, woods.Count - 4);
        WorldTile tile;
        _preferring = true;
        try
        {
            tile = CityBehBuild.tryToBuildInZones(woods, pBuildingAsset, pCity);
        }
        finally
        {
            _preferring = false;
        }
        if (tile == null) return true;
        __result = tile;
        return false;
    }

    public static bool BeforeUpgradeBuilding(Building pBuilding, City pCity, ref bool __result)
    {
        if (pBuilding?.asset == null || pCity == null) return true;
        if (!pBuilding.isOnRemove() && IndustryBuildingSystem.CanUpgradeHere(pBuilding, pCity)) return true;
        __result = false;
        return false;
    }

    public static void AfterLimitOfBuildingsType(City __instance, BuildOrder pElement, ref int __result)
    {
        using var timing = FrameProfiler.Measure("建造钩子·类型配额");
        if (__result <= 0 || pElement == null) return;
        BuildingAsset asset = pElement.getBuildingAsset(__instance);
        // 矿场的额外配额只给有模组矿藏的城(没矿的城保留原版的那一座就够，免得白建一排矿场)
        if (IndustryBuildingSystem.IsMine(asset) && !MineralResourceSystem.HasAnyDeposit(__instance)) { }
        else if (IndustryBuildingSystem.IsMine(asset) || IndustryBuildingSystem.IsLumber(asset) ||
                 asset?.type == AnimalHusbandrySystem.SlaughterhouseType)
            __result += IndustryBuildingSystem.ExtraLimit(__instance);
        // 森林多的城多建伐木场
        if (IndustryBuildingSystem.IsLumber(asset)) __result += IndustryBuildingSystem.ForestExtraLimit(__instance);
        else if (asset?.type == AnimalHusbandrySystem.PastureType && AnimalHusbandrySystem.IsNomadic(__instance.kingdom))
            __result += 2;
    }
}
