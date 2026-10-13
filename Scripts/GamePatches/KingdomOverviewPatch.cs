using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 帝国图层上点国家铭牌：不属于帝国、也没被帝国覆盖的国家打开国情窗口(同点它的地块，见 EmpireCraftMetaTypeLibrary.inspectEmpire)，
// 别的图层、别处的链接照旧走原版王国界面
public class KingdomOverviewPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(KingdomOverviewPatch));
        harmony.Patch(AccessTools.Method(typeof(MetaTypeAsset), nameof(MetaTypeAsset.selectAndInspect)),
            prefix: new HarmonyMethod(typeof(KingdomOverviewPatch), nameof(BeforeSelectAndInspect)));
    }

    private static bool BeforeSelectAndInspect(MetaTypeAsset __instance, NanoObject pNewNanoObject, bool pFromNameplate)
    {
        if (!pFromNameplate || __instance.map_mode != MetaType.Kingdom || pNewNanoObject is not Kingdom kingdom) return true;
        if (World.world?.getCachedMapMetaAsset()?.map_mode != MetaTypeExtension.Empire) return true;
        if (!EmpireCraftMetaTypeLibrary.ShowsAsKingdom(kingdom)) return true;
        KingdomOverviewWindow.Open(kingdom);
        return false;
    }
}
