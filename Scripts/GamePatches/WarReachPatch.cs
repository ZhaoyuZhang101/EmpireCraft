using System;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 开战总入口过"够得着"这道闸(见 WarReach)：原版与本模组的 startWar、本模组直接调的 newWar 都在这里拦，
// 拦下时返回空(原版 startWar 本来就会返回空，各处都判过)
public class WarReachPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(WarReachPatch));
        harmony.Patch(AccessTools.Method(typeof(DiplomacyManager), nameof(DiplomacyManager.startWar)),
            prefix: new HarmonyMethod(typeof(WarReachPatch), nameof(BeforeStartWar)),
            postfix: new HarmonyMethod(typeof(WarReachPatch), nameof(AfterStartWar)));
        harmony.Patch(AccessTools.Method(typeof(WarManager), nameof(WarManager.newWar)),
            prefix: new HarmonyMethod(typeof(WarReachPatch), nameof(BeforeNewWar)));
    }

    // startWar 已经放行的这一次，里面调的 newWar 不再算一遍(两个前缀以前各算一次)
    private static bool _passed;

    public static bool BeforeStartWar(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, ref War __result)
    {
        _passed = Allow(pAttacker, pDefender, pAsset, ref __result);
        return _passed;
    }

    public static void AfterStartWar() => _passed = false;

    public static bool BeforeNewWar(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pType, ref War __result)
    {
        if (_passed)
        {
            _passed = false;
            return true;
        }
        return Allow(pAttacker, pDefender, pType, ref __result);
    }

    private static bool Allow(Kingdom attacker, Kingdom defender, WarTypeAsset type, ref War result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(attacker)) return true;
        try
        {
            if (WarReach.MayDeclare(attacker, defender, type)) return true;
        }
        catch (Exception)
        {
            return true;
        }
        result = null;
        return false;
    }
}
