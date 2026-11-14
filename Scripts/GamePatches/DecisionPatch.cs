using EmpireCraft.Scripts.GeneralSystems.Decisions;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 模组决议由 DecisionSystem 运行，原版的谋划对象只当牌子：
//   · 立项(newPlot 之后)：收进 DecisionSystem；
//   · 原版人物 AI 推进谋划(updateProgressTarget、updateProgress)：模组决议的一概跳过，进度由 DecisionSystem 每月写上去；
//     照样记"有人在推进"，免得原版当作被遗忘取消掉。原版自己的谋划(开战、结盟、创教……)不受影响。
public class DecisionPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(DecisionPatch));
        harmony.Patch(AccessTools.Method(typeof(PlotManager), nameof(PlotManager.newPlot)),
            postfix: new HarmonyMethod(typeof(DecisionPatch), nameof(AfterNewPlot)));
        harmony.Patch(AccessTools.Method(typeof(Plot), nameof(Plot.updateProgressTarget)),
            prefix: new HarmonyMethod(typeof(DecisionPatch), nameof(BeforeProgressTarget)) { priority = Priority.First });
        var progress = AccessTools.Method(typeof(Plot), "updateProgress");
        if (progress != null)
            harmony.Patch(progress, prefix: new HarmonyMethod(typeof(DecisionPatch), nameof(BeforeProgress)) { priority = Priority.First });
    }

    private static void AfterNewPlot(Plot __result, Actor pAuthor, PlotAsset pAsset) => DecisionSystem.OnPlotCreated(__result, pAuthor, pAsset);

    private static bool BeforeProgressTarget(Plot __instance, ref bool __result)
    {
        if (!DecisionSystem.Manages(__instance?.getAsset())) return true;
        __instance.last_update_progress = World.world.getCurWorldTime();
        __result = false;
        return false;
    }

    private static bool BeforeProgress(Plot __instance) => !DecisionSystem.Manages(__instance?.getAsset());
}
