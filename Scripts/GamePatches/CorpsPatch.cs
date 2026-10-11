using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 军团模式(见 CorpsSystem)：
//   · 原版计兵数(City.countWarriors，Kingdom.countTotalWarriors 由它汇总)把驻在本城的军团折算进去，
//     战争 AI、投降、叛乱等按兵数判断的规则照常可用；
//   · 城市易手只由战线结算(FrontSystem)决定：实体单位走进城不再累积原版攻城点数；
//   · 鼠标悬停在兵牌或交战标记上时，原版地图提示(单位、城市、王国)让路，只显示兵牌详情；
//     这时点一下(原版"点一下查看"的同一时机与判定：没拿神力或拿着查看类神力、不是拖地图)打开军事页，不再选中脚下的单位或地块。
public class CorpsPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CorpsPatch));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.countWarriors)),
            postfix: new HarmonyMethod(typeof(CorpsPatch), nameof(AfterCountWarriors)));
        harmony.Patch(AccessTools.Method(typeof(City), "updateConquest"),
            prefix: new HarmonyMethod(typeof(CorpsPatch), nameof(BeforeConquest)));
        harmony.Patch(AccessTools.Method(typeof(CursorTooltipHelper), nameof(CursorTooltipHelper.update)),
            prefix: new HarmonyMethod(typeof(CorpsPatch), nameof(BeforeCursorTooltip)));
        harmony.Patch(AccessTools.Method(typeof(PlayerControl), "checkClickTouchInspectSelect"),
            prefix: new HarmonyMethod(typeof(CorpsPatch), nameof(BeforeInspectClick)));
    }

    // 判定照抄原版 checkClickTouchInspectSelect 的前半段：任何一条不满足就交还原版(原版也会就此返回)
    private static bool BeforeInspectClick(PlayerControl __instance, ref bool __result)
    {
        if (!UI.Components.CorpsMapRenderer.Hovering || !__instance.canInspectWithMainTouch()) return true;
        if (!DebugConfig.isOn(DebugOption.InspectObjectsOnClick) || MoveCamera.camera_drag_activated ||
            __instance._over_ui_timeout > 0f || __instance.already_used_zoom || __instance.already_used_camera_drag ||
            __instance.isActionHappening() || __instance.inspect_timer_click > 0.15f || MoveCamera.inSpectatorMode() ||
            World.world.getCurSessionTime() - __instance._last_time_touched_ui < 0.2 ||
            Toolbox.DistVec2Float(__instance._origin_touch, __instance._current_touch) >= 20f)
            return true;
        if (!UI.Components.CorpsMapRenderer.TryClick()) return true;
        __result = true;
        return false;
    }

    private static bool BeforeCursorTooltip() => !UI.Components.CorpsMapRenderer.Hovering;

    private static void AfterCountWarriors(City __instance, ref int __result)
    {
        if (!CorpsSystem.Enabled) return;
        __result += CorpsSystem.EquivalentAt(__instance);
    }

    private static bool BeforeConquest(City __instance, Actor pActor)
    {
        if (!CorpsSystem.Enabled) return true;
        return Compatibility.AncientWarfareCompatibility.OwnsObject(__instance) ||
               Compatibility.AncientWarfareCompatibility.OwnsObject(pActor);
    }
}
