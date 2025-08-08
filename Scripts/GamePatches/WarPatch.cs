using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using static UnityEngine.UI.CanvasScaler;

namespace EmpireCraft.Scripts.GamePatches;
public class WarPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(start_new_war)).Patch(
            AccessTools.Method(typeof(DiplomacyManager), nameof(DiplomacyManager.startWar)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(start_new_war))
        );
        new Harmony(nameof(end_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(end_war))
        );
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(War), nameof(War.Dispose)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(removeData))
        );
        LogService.LogInfo("战争补丁加载成功");
    }
    public static void removeData(War __instance)
    {
        __instance.RemoveExtraData<War, WarExtraData>();
    }

    public static void start_new_war(DiplomacyManager __instance, Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, bool pLog, ref War __result)
    {
        // todo: 开启新战争时触发
    }

    public static bool end_war(WarManager __instance, War pWar, WarWinner pWinner = WarWinner.Nobody)
    {
        //todo: 战争结束时触发
        return true;
    }
}
