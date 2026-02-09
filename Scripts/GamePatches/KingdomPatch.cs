using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using EmpireCraft.Scripts.Layer;
using UnityEngine;
using System;

namespace EmpireCraft.Scripts.GamePatches;
public class KingdomPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(countTotalWarriors)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.countTotalWarriors)),
            prefix: new HarmonyMethod(GetType(), nameof(countTotalWarriors)));
        new Harmony(nameof(getPopulationPeople)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getPopulationPeople)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationPeople)));
        LogService.LogInfo("Kingdom warriors/population cache patch loaded");
    }
    public static bool countTotalWarriors(Kingdom __instance, ref int __result)
    {
        var ed = KingdomExtension.GetOrCreate(__instance);
        if (ed != null && ed.last_cached_timestamp > 0)
        {
            __result = ed.cached_warriors;
            return false;
        }
        return true;
    }
    public static bool getPopulationPeople(Kingdom __instance, ref int __result)
    {
        var ed = KingdomExtension.GetOrCreate(__instance);
        if (ed != null && ed.last_cached_timestamp > 0)
        {
            __result = ed.cached_population;
            return false;
        }
        return true;
    }
}
