using ai;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 抽象地图模式(见 AbstractMapMode)：
//   · 国家色块完全不透明：原版近看时国家内部画成透明、只留边框，内部颜色也是半透明的；
//     这里任何缩放都填满，颜色不透明(透明的清除色不动，否则空白处会变成白块)；
//   · 人物要去哪里直接到位、不走路(地图上也不画人物)：原版的行为照常完成(回家、上任、相会生子)，不再寻路
public class AbstractMapPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(AbstractMapPatch));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "shouldBeClearColor"),
            prefix: new HarmonyMethod(typeof(AbstractMapPatch), nameof(BeforeShouldBeClearColor)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            prefix: new HarmonyMethod(typeof(AbstractMapPatch), nameof(BeforeApplyColors)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZoneFull"),
            prefix: new HarmonyMethod(typeof(AbstractMapPatch), nameof(BeforeApplyColorsFull)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "colorZone"),
            prefix: new HarmonyMethod(typeof(AbstractMapPatch), nameof(BeforeColorZone)));
        harmony.Patch(AccessTools.Method(typeof(ActorMove), nameof(ActorMove.goTo)),
            prefix: new HarmonyMethod(typeof(AbstractMapPatch), nameof(BeforeGoTo)));
    }

    private static bool BeforeShouldBeClearColor(ref bool __result)
    {
        if (!AbstractMapMode.Enabled) return true;
        __result = false;
        return false;
    }

    private static void Opaque(ref Color32 color)
    {
        if (color.a > 0) color.a = byte.MaxValue;
    }

    private static void BeforeApplyColors(ref Color32 pColorInside, ref Color32 pColorOutside)
    {
        if (!AbstractMapMode.Enabled) return;
        Opaque(ref pColorInside);
        Opaque(ref pColorOutside);
    }

    private static void BeforeApplyColorsFull(ref Color32 pColor)
    {
        if (AbstractMapMode.Enabled) Opaque(ref pColor);
    }

    private static void BeforeColorZone(ref Color32 pColor)
    {
        if (AbstractMapMode.Enabled) Opaque(ref pColor);
    }

    private static bool BeforeGoTo(Actor pActor, WorldTile pTileTarget, ref ExecuteEvent __result)
    {
        if (!AbstractMapMode.Enabled || pActor?.asset == null || pTileTarget == null || pActor.current_tile == null)
            return true;
        if (pActor.asset.is_boat && !pTileTarget.isGoodForBoat())
        {
            __result = ExecuteEvent.False;
            return false;
        }
        pActor.clearOldPath();
        pActor.setCurrentTilePosition(pTileTarget);
        pActor.dirty_current_tile = true;
        __result = ExecuteEvent.True;
        return false;
    }
}
