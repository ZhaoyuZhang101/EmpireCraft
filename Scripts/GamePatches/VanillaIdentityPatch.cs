using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

public class VanillaIdentityPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(VanillaIdentityPatch));
        harmony.Patch(AccessTools.Method(typeof(ActorManager), nameof(ActorManager.checkNewSpecies)),
            prefix: new HarmonyMethod(typeof(VanillaIdentityPatch), nameof(BeforeCheckNewSpecies)),
            postfix: new HarmonyMethod(typeof(VanillaIdentityPatch), nameof(AfterCheckNewSpecies)));
        harmony.Patch(AccessTools.Method(typeof(Plot), nameof(Plot.updateProgressTarget)),
            prefix: new HarmonyMethod(typeof(VanillaIdentityPatch), nameof(BeforePlotProgress)));
    }

    // 远处生成的新居民/征召单位也复用该物种已有亚种；动物仍按原版就近寻找。
    public static void BeforeCheckNewSpecies(ActorAsset pAsset, ref bool pGlobalSearch)
    {
        if (pAsset?.civ == true) pGlobalSearch = true;
    }

    public static void AfterCheckNewSpecies(Actor pActor) => VanillaIdentitySystem.TryReuseLanguage(pActor);

    public static bool BeforePlotProgress(Plot __instance, Actor pActor, ref bool __result)
    {
        string id = __instance?.getAsset()?.id;
        if (!VanillaIdentitySystem.BlocksPlot(id) &&
            !(id == "new_language" && (pActor?.hasLanguage() == true || VanillaIdentitySystem.TryReuseLanguage(pActor)))) return true;
        // 旧档正在进行的决议直接取消，避免留下成功通知或执行完结动作。
        __instance.finishPlot(PlotState.Cancelled, pActor);
        __result = true;
        return false;
    }
}
