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
public static class WarGate
{
    public static bool CanDeclare(Kingdom attacker, Kingdom target)
    {
        if (attacker?.data == null || target?.data == null || attacker == target || attacker.isRekt() || !target.isAlive())
            return false;
        if (NationalSentimentSystem.InSameUnitedFront(attacker, target)) return false;
        if (EmpirePartitionSystem.InAmity(attacker, target)) return false;
        if (TribalFissionService.Bound(attacker, target)) return false;
        // 跨文明通好之国互不侵犯(见 GreatPowerDiplomacy)
        if (GreatPowerDiplomacy.Bound(attacker, target)) return false;
        if (attacker.IsInSameEmpire(target)) return false;
        double lastWarEnded = World.world?.diplomacy?.getRelation(attacker, target)?.data?.timestamp_last_war_ended ?? -1d;
        if (lastWarEnded >= 0d && Date.getYearsSince(lastWarEnded) < SimGlobals.m.minimum_years_between_wars) return false;
        Empire own = attacker.GetEmpire(), theirs = target.GetEmpire();
        if (own != null && (target.GetGivenAllianceEmpire() == own || target.GetTakenAllianceEmpire() == own)) return false;
        if (theirs != null && (attacker.GetGivenAllianceEmpire() == theirs || attacker.GetTakenAllianceEmpire() == theirs)) return false;
        if (attacker.hasAlliance() && target.hasAlliance() && attacker.getAlliance() == target.getAlliance()) return false;
        return FeudalVassalService.CanDeclareExternalWar(attacker, target, includeImperialMembers: attacker.IsEmpire());
    }
}
