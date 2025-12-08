using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.GamePatches;

public class BuildingPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {

        new Harmony(nameof(UpdateStats)).Patch(
            AccessTools.Method(typeof(Building), nameof(Building.updateStats)),
            postfix: new HarmonyMethod(GetType(), nameof(UpdateStats))
        );
    }

    public static void UpdateStats(Building __instance)
    {
        if (!__instance.hasKingdom()) return;
        if (__instance.kingdom.IsInEmpire())
        {
            Empire empire = __instance.kingdom.GetEmpire();
            __instance.stats["health"] += empire.data.建设_addition*5;
        }
        
    }
}