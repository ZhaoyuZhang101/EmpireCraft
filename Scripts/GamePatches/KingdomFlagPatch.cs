using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 国旗：原版旗帜(铭牌、提示框、各窗口里的 KingdomBanner)按国家颜色给底、纹两层染色；有国旗的国家换成国旗的圆形徽章(见 KingdomFlagSystem)
public class KingdomFlagPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(KingdomFlagPatch)).Patch(
            AccessTools.Method(typeof(KingdomBanner), "setupBanner"),
            postfix: new HarmonyMethod(GetType(), nameof(AfterSetup)));
    }

    public static void AfterSetup(KingdomBanner __instance)
    {
        Kingdom kingdom = __instance?.meta_object;
        Sprite flag = KingdomFlagSystem.BadgeOf(kingdom);
        if (flag == null || __instance.part_background == null) return;
        __instance.part_background.sprite = flag;
        __instance.part_background.color = Color.white;
        if (__instance.part_icon != null) __instance.part_icon.color = Color.clear;
    }
}
