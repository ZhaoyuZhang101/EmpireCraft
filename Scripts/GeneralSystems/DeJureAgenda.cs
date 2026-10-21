using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

// 先收法理再扩张(世界法则 empirecraft_law_dejure_first，默认开)：
// 玩家 2026-10-21 建议"版图按照法理统一后再考虑扩张，要不然真的很乱"，用户选"优先打法理"。
// 挑宣战目标时(独立王国见 WarTargetPatch，帝国与成员见 EmpireCraftPlotsAddition.GetWarTarget，派系"扩张地盘")，
// 占着本国(帝国按整个帝国算)法理城的邻国排在前面；开打后战争目标定为"收复"，议和归还这些城(见 WarGoalSystem.Claimed)。
// 只调先后：没有能打的法理目标时照常扩张，不会因此不打仗
public static class DeJureAgenda
{
    public static bool Enabled => EmpireCraftWorldLawLibrary.empirecraft_law_dejure_first?.isEnabled() == true;

    // target 占着 initiator 一方法理的城
    public static bool HasClaimOn(Kingdom initiator, Kingdom target) =>
        Enabled && initiator?.data != null && target?.data != null && initiator != target &&
        WarGoalSystem.Claimed(initiator, target.cities).Count > 0;
}
