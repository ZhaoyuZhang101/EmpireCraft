using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

public class NameplateManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(UpdateOverlappingPosition)).Patch(
            AccessTools.Method(typeof(NameplateManager), nameof(NameplateManager.updateOverlappingPosition)),
            prefix: new HarmonyMethod(GetType(), nameof(UpdateOverlappingPosition))
        );
    }

    public static bool UpdateOverlappingPosition(NameplateManager __instance)
    {
        return false;
    }
}
