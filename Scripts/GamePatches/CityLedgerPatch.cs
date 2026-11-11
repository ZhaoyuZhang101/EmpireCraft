using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 建筑台账接到原版上(见 CityLedger)：原版与模组里按座数、按住房位问城市的地方，把台账里的数加进去——
//   · City.countBuildingsType / countBuildingsOfID / countBuildings / hasBuildingType：实体与台账相加；
//   · City.updateCityStatus：住房位加上台账里的(排在 NoCommonersPatch 之前，它接着按户数算空房)；
//   · City.recalculateMaxHouses：许盖的民居数加上台账里的(无小人模式那份在 NoCommonersPatch.AfterRecalculateMaxHouses 里一起算)；
//   · City.hasSuitableFood / getRandomSuitableFood：仓库实体没有了，粮在账面库存里(settlement_supplies)，留着的实体人物照样吃得到；
//   · Docks.update：原版"城里只剩码头一座建筑就自毁"，台账里还有建筑时不算只剩它一座。
// 取数、扣货、入库(getResourcesAmount / takeResource / addResourcesToRandomStockpile)早就接在账面库存上了，见 StateSettlementPatch。
public class CityLedgerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CityLedgerPatch));
        Patch(harmony, typeof(City), nameof(City.countBuildingsType), postfix: nameof(AfterCountType));
        Patch(harmony, typeof(City), nameof(City.countBuildingsOfID), postfix: nameof(AfterCountId));
        Patch(harmony, typeof(City), nameof(City.countBuildings), postfix: nameof(AfterCountAll));
        Patch(harmony, typeof(City), "hasBuildingType", postfix: nameof(AfterHasType));
        Patch(harmony, typeof(City), "updateCityStatus", postfix: nameof(AfterStatus), priority: Priority.First);
        Patch(harmony, typeof(City), "recalculateMaxHouses", postfix: nameof(AfterMaxHouses), priority: Priority.First);
        Patch(harmony, typeof(City), nameof(City.hasSuitableFood), postfix: nameof(AfterHasSuitableFood));
        Patch(harmony, typeof(City), nameof(City.getRandomSuitableFood), postfix: nameof(AfterSuitableFood));
        Patch(harmony, typeof(Docks), nameof(Docks.update), prefix: nameof(BeforeDocksUpdate));
        Patch(harmony, typeof(ai.behaviours.CityBehCheckAttackZone), nameof(ai.behaviours.CityBehCheckAttackZone.execute),
            postfix: nameof(AfterAttackZone));
    }

    // 原版出兵挑目标地块：取目标城随便一座建筑脚下那块地；目标城一座建筑实体都没有时，拿的是自己城的地(兵就不出门了)。
    // 建筑进了台账以后常有这样的城：改指目标城的城心那块地
    public static void AfterAttackZone(City pCity)
    {
        City target = pCity?.target_attack_city;
        if (target == null || target.isRekt() || target.buildings == null || target.buildings.Count > 0 || !target.hasZones()) return;
        TileZone zone = pCity.target_attack_zone;
        if (zone != null && zone.city == target) return;
        pCity.target_attack_zone = target.getTile()?.zone ?? target.zones.GetRandom();
    }

    private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null, int priority = Priority.Normal)
    {
        try
        {
            harmony.Patch(AccessTools.Method(type, method),
                prefix: prefix == null ? null : new HarmonyMethod(typeof(CityLedgerPatch), prefix) { priority = priority },
                postfix: postfix == null ? null : new HarmonyMethod(typeof(CityLedgerPatch), postfix) { priority = priority });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][建筑台账] 补丁 {type.Name}.{method} 失败: {exception.Message}");
        }
    }

    public static void AfterCountType(City __instance, string pBuildingTypeID, bool pCountOnlyFinished, ref int __result)
    {
        if (!CityLedger.Any) return;
        __result += CityLedger.CountType(__instance, pBuildingTypeID);
        // 连在建的一起数时(原版拿它比上限)，台账里的工地也算一座
        if (!pCountOnlyFinished && CityLedger.Pending(__instance) is BuildingAsset pending && pending.type == pBuildingTypeID) __result++;
    }

    public static void AfterCountId(City __instance, string pBuildingID, ref int __result)
    {
        if (CityLedger.Any) __result += CityLedger.CountId(__instance, pBuildingID);
    }

    public static void AfterCountAll(City __instance, ref int __result)
    {
        if (CityLedger.Any) __result += CityLedger.Total(__instance);
    }

    // 限定哪座岛的问法(码头、渡海)只认实体：台账里的建筑没有位置
    public static void AfterHasType(City __instance, string pBuildingTypeID, TileIsland pLimitIsland, ref bool __result)
    {
        if (__result || pLimitIsland != null || !CityLedger.Any) return;
        __result = CityLedger.CountType(__instance, pBuildingTypeID) > 0;
    }

    public static void AfterStatus(City __instance)
    {
        if (!CityLedger.Any || __instance?.status == null) return;
        int extra = CityLedger.HousingSlots(__instance);
        if (extra <= 0) return;
        CityStatus status = __instance.status;
        status.housing_total += extra;
        status.housing_occupied = Math.Min(status.population, status.housing_total);
        status.housing_free = status.housing_total - status.housing_occupied;
    }

    // 无小人模式(连同手动城区)在 NoCommonersPatch.AfterRecalculateMaxHouses 里整个重算，那里自己加台账的数
    public static void AfterMaxHouses(City __instance)
    {
        if (!CityLedger.Any || __instance?.status == null || CityPopulationSystem.AbstractPopulationEnabled ||
            CityZoningSystem.Manual(__instance) || DebugConfig.isOn(DebugOption.CityUnlimitedHouses)) return;
        __instance.status.houses_max += CityLedger.MaxHouses(__instance);
    }

    private static Dictionary<string, int> Stock(City city) =>
        city?.data == null ? null : city.GetOrCreate().population?.settlement_supplies;

    public static void AfterHasSuitableFood(City __instance, Subspecies pSubspecies, ref bool __result)
    {
        if (__result || pSubspecies == null) return;
        Dictionary<string, int> stock = Stock(__instance);
        if (stock == null || stock.Count == 0) return;
        foreach (string food in pSubspecies.getAllowedFoodByDiet())
            if (stock.TryGetValue(food, out int have) && have > 0)
            {
                __result = true;
                return;
            }
    }

    // 同原版：合口味的里头挑存得最多的
    public static void AfterSuitableFood(City __instance, Subspecies pSubspecies, ref ResourceAsset __result)
    {
        if (__result != null || pSubspecies == null) return;
        Dictionary<string, int> stock = Stock(__instance);
        if (stock == null || stock.Count == 0) return;
        string best = null;
        int most = 0;
        foreach (string food in pSubspecies.getAllowedFoodByDiet())
            if (stock.TryGetValue(food, out int have) && have > most)
            {
                most = have;
                best = food;
            }
        if (best != null && AssetManager.resources.dict.TryGetValue(best, out ResourceAsset asset)) __result = asset;
    }

    public static bool BeforeDocksUpdate(Docks __instance)
    {
        Building building = __instance?.building;
        if (building == null || !CityLedger.Any || !building.hasCity()) return true;
        City city = building.city;
        if (city?.buildings == null || city.buildings.Count != 1 || CityLedger.Total(city) <= 0) return true;
        // 码头落在陆地上照原版拆；只是"城里只剩它一座"这条不算
        return __instance.ifStayingOnGround();
    }
}
