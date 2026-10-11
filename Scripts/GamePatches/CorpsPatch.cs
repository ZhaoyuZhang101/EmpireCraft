using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 军团模式(见 CorpsSystem)：
//   · 原版计兵数(City.countWarriors，Kingdom.countTotalWarriors 由它汇总)把驻在本城的军团折算进去，
//     战争 AI、投降、叛乱等按兵数判断的规则照常可用；
//   · 城市易手只由战线结算(FrontSystem)决定：实体单位走进城不再累积原版攻城点数。
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
    }

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
