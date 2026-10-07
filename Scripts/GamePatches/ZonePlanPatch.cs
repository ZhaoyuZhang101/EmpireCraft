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
//   - 工业和住房集中在现有连片区域，选址失败请求相邻扩展；其他建筑优先匹配用途；
//   - 升级后的地基仍遵守用途规划，拆除中的建筑不能升级(CityBehBuild.upgradeBuilding)；
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
            if (zone == null || !ZonePlanSystem.IsPreferredZone(pCity, zone, use)) continue;
            (preferred ??= new List<TileZone>()).Add(zone);
        }
        // 伐木场：工业区里没地方(或还没有工业区)时，挑树最多的几块空区块建(建在林子边上)，不干等工业区扩地
        bool woodsFallback = false;
        if (preferred == null && IndustryBuildingSystem.IsLumber(pBuildingAsset))
        {
            var woods = new List<TileZone>();
            foreach (TileZone zone in pList)
                if (zone != null && ZonePlanSystem.Get(pCity, zone) == ZoneUse.None &&
                    (zone.getHashset(BuildingList.Trees)?.Count ?? 0) >= 6) woods.Add(zone);
            woods.Sort((a, b) => (b.getHashset(BuildingList.Trees)?.Count ?? 0)
                .CompareTo(a.getHashset(BuildingList.Trees)?.Count ?? 0));
            if (woods.Count > 4) woods.RemoveRange(4, woods.Count - 4);
            if (woods.Count > 0)
            {
                preferred = woods;
                woodsFallback = true;
            }
        }
        bool clustered = use == ZoneUse.Industry || use == ZoneUse.Residential;
        if (preferred == null)
        {
            if (!clustered || ZonePlanSystem.Count(pCity, use) == 0) return true;
            ZonePlanSystem.RequestSpace(pCity, use);
            __result = null;
            return false;
        }
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
        if (tile == null && (!clustered || woodsFallback)) return true;
        if (tile == null) ZonePlanSystem.RequestSpace(pCity, use);
        __result = tile;
        return false;
    }

    public static bool BeforeUpgradeBuilding(Building pBuilding, City pCity, ref bool __result)
    {
        if (pBuilding?.asset == null || pCity == null) return true;
        if (!pBuilding.isOnRemove() && IndustryBuildingSystem.CanUpgradeHere(pBuilding, pCity))
        {
            BuildingAsset target = AssetManager.buildings.get(pBuilding.asset.upgrade_to ?? "");
            if (target == null || ZonePlanSystem.CanBuildAt(pCity, pBuilding.current_tile, target)) return true;
            ZoneUse use = ZonePlanSystem.UseOf(target);
            if (ZonePlanSystem.IsPreferredZone(pCity, pBuilding.current_tile?.zone, use))
                ZonePlanSystem.RequestSpace(pCity, use);
        }
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
        if (IndustryBuildingSystem.IsLumber(asset)) __result += IndustryBuildingSystem.ForestExtraLimit(__instance);
        // 森林多的城多建伐木场
        if (IndustryBuildingSystem.IsLumber(asset)) __result += IndustryBuildingSystem.ForestExtraLimit(__instance);
        else if (asset?.type == AnimalHusbandrySystem.PastureType && AnimalHusbandrySystem.IsNomadic(__instance.kingdom))
            __result += 2;
    }
}
