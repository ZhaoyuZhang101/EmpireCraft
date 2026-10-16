using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 原版几处报错的防护(玩家日志 2026-10-16)：
//   · 国家的颜色编号超出了国家颜色库(别的模组改过颜色库、旧存档等)：原版取颜色(Kingdom.getColor)下标越界，
//     地图图标、铭牌、军团、廷议、征粮……凡是要国家颜色的地方一路报错(日志里两千五百多条)。取色前把越界的编号换回库里的颜色；
//   · 存档时谋划(Plot.save)的目标已经没了数据(战争结束、国家灭亡、人死后对象被回收，谋划还指着它)：
//     原版取 target_xxx.data.id 空引用，整个存档失败("Error during saving")。存之前把这些失效的目标清掉；
//     还有别的引用失效(笔刷改地形删掉城市、国家)时只跳过这一条谋划，存档照常完成；
//   · 窗口左右切换(MetaSwitchManager.switchWindow)：打开下一个窗口时切换条被关掉、列表清空了，
//     原版接着刷新序号和名字时空引用。列表已经没了就不刷新。
public class VanillaGuardPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(VanillaGuardPatch));
        harmony.Patch(AccessTools.Method(typeof(Plot), nameof(Plot.save)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforePlotSave)),
            finalizer: new HarmonyMethod(GetType(), nameof(PlotSaveFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getColor)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeKingdomGetColor)));
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

    // 兜底：谋划存档还是空引用(笔刷改地形删掉城市、国家后，谋划里还有别的引用失效)时，只跳过这一条谋划，
    // 不让整个存档失败；每条谋划只记一次日志，写下各个目标的状态便于查是哪个引用空了
    private static readonly global::System.Collections.Generic.HashSet<long> ReportedPlots = new();

    private static global::System.Exception PlotSaveFinalizer(global::System.Exception __exception, Plot __instance)
    {
        if (__exception == null) return null;
        long id = __instance?.data?.id ?? -1L;
        if (ReportedPlots.Add(id))
        {
            string State(object target, object data) => target == null ? "无" : data == null ? "已失效" : "有";
            LogService.LogWarning($"[EmpireCraft][谋划存档] 跳过存档出错的谋划 id={id} 类型={AssetId(__instance)} " +
                                  $"数据={(__instance?.data == null ? "无" : "有")} " +
                                  $"目标人物={State(__instance?.target_actor, __instance?.target_actor?.data)} " +
                                  $"目标城市={State(__instance?.target_city, __instance?.target_city?.data)} " +
                                  $"目标国家={State(__instance?.target_kingdom, __instance?.target_kingdom?.data)} " +
                                  $"目标同盟={State(__instance?.target_alliance, __instance?.target_alliance?.data)} " +
                                  $"目标战争={State(__instance?.target_war, __instance?.target_war?.data)}: {__exception.Message}");
        }
        return null;
    }

    private static string AssetId(Plot plot)
    {
        try
        {
            return plot?.getAsset()?.id ?? "无";
        }
        catch
        {
            return "读不到";
        }
    }

    private static void BeforeKingdomGetColor(Kingdom __instance)
    {
        KingdomData data = __instance?.data;
        if (data == null || __instance._cached_color != null) return;
        var colors = AssetManager.kingdom_colors_library?.list;
        if (colors == null || colors.Count == 0 || data.color_id >= 0 && data.color_id < colors.Count) return;
        int repaired = global::System.Math.Abs(data.color_id) % colors.Count;
        LogService.LogWarning($"[EmpireCraft] 国家 {data.name}({data.id}) 的颜色编号 {data.color_id} 超出颜色库(共 {colors.Count} 种)，改用第 {repaired} 种");
        data.setColorID(repaired);
        if (data.original_color_id < 0 || data.original_color_id >= colors.Count) data.original_color_id = repaired;
    }

    private static bool BeforeUpdateShowingData(MetaSwitchManager __instance) =>
        Traverse.Create(__instance).Field("_list").GetValue() != null;
}
