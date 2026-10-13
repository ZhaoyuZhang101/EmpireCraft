using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 谋划开战的进度满了，大军还没动员好、陈兵边境时不宣战(见 WarReadinessSystem)
public class WarReadinessPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(WarReadinessPatch));
        harmony.Patch(AccessTools.Method(typeof(Plot), nameof(Plot.updateProgressTarget)),
            prefix: new HarmonyMethod(typeof(WarReadinessPatch), nameof(BeforeProgress)));
    }

    private static bool BeforeProgress(Plot __instance, ref bool __result)
    {
        if (!WarReadinessSystem.Hold(__instance)) return true;
        // 等集结期间照样算"有人在推进"，否则原版会把久未推进的谋划当作被遗忘取消掉(PlotManager)
        __instance.last_update_progress = World.world.getCurWorldTime();
        __result = false;
        return false;
    }
}
