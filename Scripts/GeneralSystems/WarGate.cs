using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诉求开战的统一闸门(复查 2026-10-17：对外扩张、游牧扩张、劫掠、迫使朝贡、扩张地盘以前直接 newWar/startWar，
// 不看停战年限、岁币、朝贡、同盟——收岁币的帝国能立刻打送岁币的、劫掠能打到自己的朝贡国与盟友)：
//   · 民族统一战线、分治的兄弟之国、共主与共事部落互不征伐(同计划里的 IsValidModWarTarget)；
//   · 停战未满年限不打；
//   · 岁币、朝贡两头都不打(送的不打收的，收的也不打送的)；
//   · 同一同盟不打；
//   · 最后看封建附庸能否对外开战(FeudalVassalService.CanDeclareExternalWar)。
// 独立王国走原版挑宣战对象(见 WarTargetPatch)，兵力自有比较，只用 Forbidden 避开这些不该打的关系。
public static class WarGate
{
    public static bool CanDeclare(Kingdom attacker, Kingdom target)
    {
        if (attacker?.data == null || target?.data == null || attacker == target || attacker.isRekt() || !target.isAlive())
            return false;
        if (Forbidden(attacker, target)) return false;
        double lastWarEnded = World.world?.diplomacy?.getRelation(attacker, target)?.data?.timestamp_last_war_ended ?? -1d;
        if (lastWarEnded >= 0d && Date.getYearsSince(lastWarEnded) < SimGlobals.m.minimum_years_between_wars) return false;
        return FeudalVassalService.CanDeclareExternalWar(attacker, target, includeImperialMembers: attacker.IsEmpire());
    }

    // 不论强弱都不打的关系：统一战线、兄弟之国、共主与共事部落、跨文明通好、扶立、同一帝国、岁币与朝贡、同盟、
    // 姻亲、封建上下级、保护约、同一位君主
    public static bool Forbidden(Kingdom attacker, Kingdom target)
    {
        if (attacker == null || target == null || attacker == target) return true;
        if (NationalSentimentSystem.InSameUnitedFront(attacker, target)) return true;
        if (EmpirePartitionSystem.InAmity(attacker, target)) return true;
        if (TribalFissionService.Bound(attacker, target)) return true;
        // 跨文明通好之国互不侵犯(见 GreatPowerDiplomacy)；扶立之君与扶立者二十年互不开战(见 WarGoalSystem)
        if (GreatPowerDiplomacy.Bound(attacker, target) || WarGoalSystem.Installed(attacker, target)) return true;
        if (attacker.IsInSameEmpire(target)) return true;
        Empire own = attacker.GetEmpire(), theirs = target.GetEmpire();
        if (own != null && (target.GetGivenAllianceEmpire() == own || target.GetTakenAllianceEmpire() == own)) return true;
        if (theirs != null && (attacker.GetGivenAllianceEmpire() == theirs || attacker.GetTakenAllianceEmpire() == theirs)) return true;
        if (attacker.hasAlliance() && target.hasAlliance() && attacker.getAlliance() == target.getAlliance()) return true;
        if (RoyalMarriageSystem.Allied(attacker, target)) return true;
        if (FeudalVassalService.IsInChain(attacker, target) || FeudalVassalService.IsInChain(target, attacker)) return true;
        // 保护约三十年：保护人与被保护者互不攻伐(见 HolyRomanSystem.Decline)
        if (HolyRomanSystem.ProtectedBy(attacker, target) || HolyRomanSystem.ProtectedBy(target, attacker)) return true;
        return attacker.king != null && attacker.king == target.king;
    }
}
