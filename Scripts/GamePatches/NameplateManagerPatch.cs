using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace EmpireCraft.Scripts.GamePatches;
public class NameplateManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(update)).Patch(AccessTools.Method(typeof(NameplateManager), nameof(NameplateManager.update)),
        prefix: new HarmonyMethod(GetType(), nameof(update)));
        LogService.LogInfo("名牌管理Patch加载成功");
    }

    private static bool update(NameplateManager __instance)
    {
        __instance.prepare();
        MetaType currentMode = __instance.getCurrentMode();
        __instance.setMode(currentMode);
        if (currentMode == MetaType.None)
        {
            if (__instance.gameObject.activeSelf)
            {
                __instance.gameObject.SetActive(value: false);
            }
        }
        else
        {
            if (!__instance.gameObject.activeSelf)
            {
                __instance.gameObject.SetActive(value: true);
            }
            NameplateAsset nameplateAsset;
            if (ModClass.CURRENT_MAP_MOD != EmpireCraftMapMode.None)
            {
                nameplateAsset = EmpireCraftNamePlateLibrary.map_modes_nameplates[ModClass.CURRENT_MAP_MOD];
            } else
            {
                nameplateAsset = AssetManager.nameplates_library.map_modes_nameplates[currentMode];
            }
            nameplateAsset.action_main(__instance, nameplateAsset);
        }
        if (currentMode != MetaType.None)
        {
            __instance.updatePositions();
        }
        for (int i = 0; i < __instance.active.Count; i++)
        {
            NameplateText nameplateText = __instance.active[i];
            nameplateText.update(World.world.delta_time);
            nameplateText.checkActive();
        }
        __instance.checkTooltips();
        __instance.finale();
        return false;
    }
}
