using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

public class SuccessionToolPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(FindNextHeir)).Patch(
            AccessTools.Method(typeof(SuccessionTool), nameof(SuccessionTool.findNextHeir)),
            prefix: new HarmonyMethod(GetType(), nameof(FindNextHeir)));
    }

    public static bool FindNextHeir(Kingdom pKingdom, Actor pExculdeActor, ref Actor __result)
    {
        __result = pKingdom.GetHeir();
        return false;
    }
}