using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

public class NameplateManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(updateOverlappingPosition)).Patch(
            AccessTools.Method(typeof(NameplateManager), nameof(NameplateManager.updateOverlappingPosition)),
            prefix: new HarmonyMethod(GetType(), nameof(updateOverlappingPosition))
        );
    }

    public static bool updateOverlappingPosition(NameplateManager __instance)
    {
        if (__instance == null)
        {
            return false;
        }

        return true;
    }
}
