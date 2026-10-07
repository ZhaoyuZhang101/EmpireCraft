using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 无小人模式：盖房、造船、打造装备等从城市仓库里拿东西(原版和模组都走 City.takeResource /
// City.spendResourcesForBuildingAsset)，用的是百姓的货，由城市国库按价付钱(见 PopulationEconomySystem.ChargePublicUse)。
// 按拿之前和拿之后仓库里的差额算实际用掉的数量。
public class PublicMaterialPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private static readonly string[] BuildingMaterials = { "wood", "stone", "common_metals" };
    // 正在 spendResourcesForBuildingAsset 里(里面可能再调 takeResource，避免重复算)
    [ThreadStatic] private static int _spending;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(PublicMaterialPatch));
        var take = AccessTools.Method(typeof(City), "takeResource", new[] { typeof(string), typeof(int) });
        if (take != null)
            harmony.Patch(take, prefix: new HarmonyMethod(GetType(), nameof(BeforeTake)),
                postfix: new HarmonyMethod(GetType(), nameof(AfterTake)));
        var spend = AccessTools.Method(typeof(City), "spendResourcesForBuildingAsset");
        if (spend != null)
            harmony.Patch(spend, prefix: new HarmonyMethod(GetType(), nameof(BeforeSpend)),
                finalizer: new HarmonyMethod(GetType(), nameof(AfterSpend)));
        LogService.LogInfo($"[EmpireCraft][公家用料] takeResource={(take != null)} spendResourcesForBuildingAsset={(spend != null)}");
    }

    private static bool Tracking => CityPopulationSystem.AbstractPopulationEnabled && _spending == 0 &&
                                    !PopulationEconomySystem.InPrivateUse;

    public static void BeforeTake(City __instance, string __0, out int __state)
    {
        __state = -1;
        if (!Tracking || __instance == null || string.IsNullOrEmpty(__0) || PopulationEconomySystem.IsFood(__0)) return;
        try
        {
            __state = __instance.getResourcesAmount(__0);
        }
        catch
        {
            __state = -1;
        }
    }

    public static void AfterTake(City __instance, string __0, int __state)
    {
        if (__state < 0 || __instance == null) return;
        try
        {
            int used = __state - __instance.getResourcesAmount(__0);
            if (used > 0) PopulationEconomySystem.ChargePublicUse(__instance, __0, used);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][公家用料] 结算失败: {exception.Message}");
        }
    }

    public static void BeforeSpend(City __instance, out Dictionary<string, int> __state)
    {
        __state = null;
        if (!Tracking || __instance == null) return;
        try
        {
            __state = new Dictionary<string, int>();
            foreach (string id in BuildingMaterials) __state[id] = __instance.getResourcesAmount(id);
        }
        catch
        {
            __state = null;
        }
        if (__state != null) _spending++;
    }

    // 用 finalizer：原方法抛错也要把计数减回来
    public static Exception AfterSpend(Exception __exception, City __instance, Dictionary<string, int> __state)
    {
        if (__state == null) return __exception;
        _spending--;
        if (__exception != null) return __exception;
        try
        {
            foreach (KeyValuePair<string, int> pair in __state)
            {
                int used = pair.Value - __instance.getResourcesAmount(pair.Key);
                if (used > 0) PopulationEconomySystem.ChargePublicUse(__instance, pair.Key, used);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][公家用料] 结算失败: {exception.Message}");
        }
        return null;
    }
}
