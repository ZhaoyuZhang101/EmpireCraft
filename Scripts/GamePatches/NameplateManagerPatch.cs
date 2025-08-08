using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameLibrary;
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
            if (ModClass.CURRENT_MAP_MOD != ModMapMode.None)
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
        foreach (var nameplateText in __instance.active)
        {
            nameplateText.update(World.world.delta_time);
            nameplateText.checkActive();
        }
        __instance.checkTooltips();
        __instance.finale();
        return false;
    }
}
