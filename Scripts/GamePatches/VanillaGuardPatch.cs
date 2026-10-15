using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 原版两处空引用的防护(玩家日志 2026-10-16)：
//   · 存档时谋划(Plot.save)的目标已经没了数据(战争结束、国家灭亡、人死后对象被回收，谋划还指着它)：
//     原版取 target_xxx.data.id 空引用，整个存档失败("Error during saving")。存之前把这些失效的目标清掉；
//   · 窗口左右切换(MetaSwitchManager.switchWindow)：打开下一个窗口时切换条被关掉、列表清空了，
//     原版接着刷新序号和名字时空引用。列表已经没了就不刷新。
public class VanillaGuardPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(VanillaGuardPatch));
        harmony.Patch(AccessTools.Method(typeof(Plot), nameof(Plot.save)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforePlotSave)));
        var update = AccessTools.Method(typeof(MetaSwitchManager), "updateShowingData");
        if (update != null) harmony.Patch(update, prefix: new HarmonyMethod(GetType(), nameof(BeforeUpdateShowingData)));
    }

    private static void BeforePlotSave(Plot __instance)
    {
        if (__instance.target_actor != null && __instance.target_actor.data == null) __instance.target_actor = null;
        if (__instance.target_city != null && __instance.target_city.data == null) __instance.target_city = null;
        if (__instance.target_kingdom != null && __instance.target_kingdom.data == null) __instance.target_kingdom = null;
        if (__instance.target_alliance != null && __instance.target_alliance.data == null) __instance.target_alliance = null;
        if (__instance.target_war != null && __instance.target_war.data == null) __instance.target_war = null;
    }

    private static bool BeforeUpdateShowingData(MetaSwitchManager __instance) =>
        Traverse.Create(__instance).Field("_list").GetValue() != null;
}
