using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 帝国的颜色、旗帜就是核心王国的颜色、旗帜(Empire.updateColor(CoreKingdom.getColor()))。
// 所以"更改帝国颜色"直接借用原版"自定义王国"窗口编辑核心王国，再把新颜色同步回帝国和沿用帝国颜色的成员国。
// 玩家从王国窗口直接改核心王国的颜色时也会走到这里，同样保持帝国一致。
public class EmpireColorPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(EmpireColorPatch)).Patch(
            AccessTools.Method(typeof(KingdomCustomizeWindow), "onBannerChange"),
            postfix: new HarmonyMethod(typeof(EmpireColorPatch), nameof(AfterBannerChange)));
    }

    public static void Open(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt()) return;
        SelectedMetas.selected_kingdom = core;
        ScrollWindow.showWindow("kingdom_customize");
    }

    private static void AfterBannerChange(KingdomCustomizeWindow __instance)
    {
        Kingdom kingdom = SelectedMetas.selected_kingdom;
        Empire empire = kingdom?.GetEmpire();
        bool isCore = empire != null && !empire.isRekt() && empire.CoreKingdom == kingdom;
        __instance.title?.setKeyAndUpdate(isCore ? "customize_empire" : "customize_kingdom");
        if (isCore) SyncFromCore(empire);
    }

    public static void SyncFromCore(Empire empire)
    {
        ColorAsset next = empire.CoreKingdom.getColor();
        ColorAsset old = empire.getColor();
        if (next == null || old == next) return;
        empire.updateColor(next);
        foreach (Kingdom member in empire.kingdoms_list)
        {
            if (member == null || member.isRekt() || member == empire.CoreKingdom) continue;
            if (member.getColor() == old) member.updateColor(next);
        }
        World.world.zone_calculator.dirtyAndClear();
    }
}
