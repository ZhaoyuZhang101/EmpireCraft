using System;
using System.Collections.Generic;
using ai.behaviours;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 城市规划与矿场伐木场分级的原版接入点：
//   - 选址：划了用途的区块只许建对应的建筑(CityBehBuild.isGoodTileForBuilding)；
//   - 新建时优先建在对应用途的区块里(CityBehBuild.tryToBuildInZones)；
//   - 矿场、伐木场第 3 级起只能在工业区升级(CityBehBuild.upgradeBuilding)；
//   - 每块工业区多许建一座矿场、伐木场(City.getLimitOfBuildingsType)。
public class ZonePlanPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        MineralResourceSystem.Init();
        IndustryBuildingSystem.Init();
        var harmony = new Harmony(nameof(ZonePlanPatch));
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.isGoodTileForBuilding)),
                postfix: new HarmonyMethod(typeof(ZonePlanPatch), nameof(AfterIsGoodTile)));
            harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.tryToBuildInZones)),
                prefix: new HarmonyMethod(typeof(ZonePlanPatch), nameof(BeforeTryToBuildInZones)));
            harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.upgradeBuilding)),
                prefix: new HarmonyMethod(typeof(ZonePlanPatch), nameof(BeforeUpgradeBuilding)));
            harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getLimitOfBuildingsType)),
                postfix: new HarmonyMethod(typeof(ZonePlanPatch), nameof(AfterLimitOfBuildingsType)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 城市规划补丁未生效: {exception.Message}");
        }
    }

    public static void AfterIsGoodTile(WorldTile pTile, BuildingAsset pAsset, City pCity, ref bool __result)
    {
        if (__result && !ZonePlanSystem.CanBuildAt(pCity, pTile, pAsset)) __result = false;
    }

    [ThreadStatic] private static bool _preferring;

    public static bool BeforeTryToBuildInZones(List<TileZone> pList, BuildingAsset pBuildingAsset, City pCity,
        bool pForceCenterZone, ref WorldTile __result)
    {
        if (_preferring || pForceCenterZone || pList == null || pBuildingAsset == null || pCity?.data == null ||
            pBuildingAsset.docks) return true;
        ZoneUse use = ZonePlanSystem.UseOf(pBuildingAsset);
        if (use == ZoneUse.None) return true;
        List<TileZone> preferred = null;
        foreach (TileZone zone in pList)
        {
            if (zone == null || ZonePlanSystem.Get(pCity, zone) != use) continue;
            (preferred ??= new List<TileZone>()).Add(zone);
        }
        if (preferred == null) return true;
        WorldTile tile;
        _preferring = true;
        try
        {
            tile = CityBehBuild.tryToBuildInZones(preferred, pBuildingAsset, pCity);
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
        if (pBuilding?.asset == null || pCity == null || IndustryBuildingSystem.CanUpgradeHere(pBuilding, pCity)) return true;
        __result = false;
        return false;
    }

    public static void AfterLimitOfBuildingsType(City __instance, BuildOrder pElement, ref int __result)
    {
        if (__result <= 0 || pElement == null) return;
        BuildingAsset asset = pElement.getBuildingAsset(__instance);
        if (IndustryBuildingSystem.IsMine(asset) || IndustryBuildingSystem.IsLumber(asset) ||
            asset?.type == AnimalHusbandrySystem.SlaughterhouseType)
            __result += IndustryBuildingSystem.ExtraLimit(__instance);
        else if (asset?.type == AnimalHusbandrySystem.PastureType && AnimalHusbandrySystem.IsNomadic(__instance.kingdom))
            __result += 2;
    }
}
