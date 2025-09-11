using System.Linq;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class KingdomWindowPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(OnEnable)).Patch(
            AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.OnEnable)),
            prefix: new HarmonyMethod(GetType(), nameof(OnEnable))
        );     
    }

    public static void OnEnable(KingdomWindow __instance)
    {
        Transform space = __instance.tabs.transform.Find("space (1)");
        if (space != null)
        {
            GameObject.Destroy(space.gameObject);
        }
        if (__instance.tabs._tabs.All(p => p.name != "regime"))
        {
            SimpleWindowTab simpleWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            simpleWindowTab.Setup("regime", __instance.scroll_window, action:(_) => ShowRegime(), sprite:SpriteTextureLoader.getSprite("ui/regime"));
        }
    }
    private static void ShowRegime()
    {
        ScrollWindow.showWindow(nameof(RegimeWindow));
        LogService.LogInfo($"开启RegimeWindow");
    }
}