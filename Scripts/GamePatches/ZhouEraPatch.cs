using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 春秋战国(见 ZhouEraSystem)：城破时判断是不是某国都城——周室诸侯的都城被另一诸侯攻下即灭其国，
// 战国时王畿被攻下即周亡
public class ZhouEraPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(ZhouEraPatch)).Patch(AccessTools.Method(typeof(City), nameof(City.joinAnotherKingdom)),
            prefix: new HarmonyMethod(typeof(ZhouEraPatch), nameof(Before)),
            postfix: new HarmonyMethod(typeof(ZhouEraPatch), nameof(After)));
    }

    private static void Before(City __instance, out (Kingdom owner, bool capital) __state)
    {
        Kingdom owner = __instance?.kingdom;
        __state = (owner, owner?.data != null && owner.capital == __instance);
    }

    private static void After(City __instance, Kingdom pNewSetKingdom, bool pCaptured, (Kingdom owner, bool capital) __state)
    {
        if (!pCaptured || !__state.capital || __state.owner?.data == null || __instance?.kingdom == null ||
            __instance.kingdom == __state.owner || __instance.kingdom != pNewSetKingdom) return;
        var empire = ZhouEraSystem.SphereOf(__state.owner);
        if (empire == null) return;
        ZhouEraSystem.QueueCapitalTaken(__instance, __state.owner, pNewSetKingdom, empire.CoreKingdom == __state.owner);
    }
}
