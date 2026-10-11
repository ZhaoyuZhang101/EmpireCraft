using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 战争中的平民保护(见 MassacreSystem)：
//   原版只在"同物种且不仇外"时才放过敌国平民，异族平民在战争里会被士兵杀光，弱小民族因此很快被灭绝。
//   这里改为：交战双方的士兵一律不攻击对方平民，平民也不去攻击敌军——征服只需击败军队、攻下城池
//   (原版占城只看守军士兵，平民本来就不影响占领)。
//   例外：仇外的军队仍会屠戮异族平民(原版逻辑)，屠戮记入被害城市，引发持久的仇恨与叛乱；
//   开启"愤怒平民"世界法则时完全按原版。
public class CivilianProtectionPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CivilianProtectionPatch));
        harmony.Patch(AccessTools.Method(typeof(BaseSimObject), "canAttackTarget"),
            postfix: new HarmonyMethod(typeof(CivilianProtectionPatch), nameof(AfterCanAttack)));
        harmony.Patch(AccessTools.Method(typeof(Actor), "getHit"),
            prefix: new HarmonyMethod(typeof(CivilianProtectionPatch), nameof(BeforeHit)),
            postfix: new HarmonyMethod(typeof(CivilianProtectionPatch), nameof(AfterHit)));
    }

    private static void AfterCanAttack(BaseSimObject __instance, BaseSimObject pTarget, bool pCheckForFactions,
        ref bool __result)
    {
        if (!__result || !pCheckForFactions || pTarget == null || !__instance.isActor() || !pTarget.isActor()) return;
        if (WorldLawLibrary.world_law_angry_civilians.isEnabled()) return;
        Actor attacker = __instance.a;
        Actor target = pTarget.a;
        if (!MassacreSystem.IsWartimeCivilianEncounter(attacker, target)) return;
        // 古代战争模组接管的单位按它自己的规则
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(attacker) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(target)) return;
        // 仇外军队屠戮异族平民(原版允许的情形保持不变)
        if (MassacreSystem.WillMassacre(attacker, target)) return;
        __result = false;
    }

    private static void BeforeHit(Actor __instance, out bool __state)
    {
        __state = __instance != null && __instance.isAlive() && __instance.getHealth() > 0;
    }

    private static void AfterHit(Actor __instance, BaseSimObject pAttacker, bool __state)
    {
        if (!__state || __instance == null || pAttacker == null || !pAttacker.isActor()) return;
        if (__instance.isAlive() && __instance.getHealth() > 0) return;
        MassacreSystem.OnCivilianKilled(__instance, pAttacker.a);
    }
}
