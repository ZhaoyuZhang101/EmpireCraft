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
            prefix: new HarmonyMethod(typeof(WarReachPatch), nameof(BeforeStartWar)));
        harmony.Patch(AccessTools.Method(typeof(WarManager), nameof(WarManager.newWar)),
            prefix: new HarmonyMethod(typeof(WarReachPatch), nameof(BeforeNewWar)));
    }

    public static bool BeforeStartWar(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, ref War __result) =>
        Allow(pAttacker, pDefender, pAsset, ref __result);

    public static bool BeforeNewWar(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pType, ref War __result) =>
        Allow(pAttacker, pDefender, pType, ref __result);

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
