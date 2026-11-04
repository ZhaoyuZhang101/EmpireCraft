using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

// 文化的统一倾向：分裂的天下里这一家有多想归于一统(0~1)。文化配置 setting.unification_drive，没写时 strong_unification 算 1、否则 0。
// 这是"想不想"，不是"能不能"，作用于：
//   · 问鼎门槛：久乱之后按倾向放宽(见 UnificationSystem.Bars)；
//   · 开战挑目标：同样合法的邻国里先打同一天下的(见 EmpireCraftPlotsAddition.GetWarTarget、WarTargetPatch)；
//   · 朝堂议程：谋求统一更急(见 ClaimAgendaSystem.UnificationUrgency)；
//   · 共主收拾残局：每年动手的机会更大(见 UnificationSystem.Pacify)。
// 不给兵、不给地、不多开仗：军饷、停战、姻亲、盟约这些开战闸门照旧，打不过的、打不起的照样不打。
public static class UnificationDrive
{
    public const float AgendaUrgency = 8f, PacifyBoost = 1f, PacifyChanceCap = 0.7f;

    public static float Of(Kingdom kingdom) =>
        kingdom?.data == null || kingdom.isRekt() ? 0f : CultureService.GetRealmCulture(kingdom).GetUnificationDrive();

    // 这一家要先打这个邻国：有统一倾向，对方在同一个天下
    public static bool Prefers(Kingdom initiator, Kingdom target) =>
        Of(initiator) > 0f && UnificationSystem.SameRealm(initiator, target);

    public static float PacifyChance(Kingdom holder, float chance)
    {
        float boosted = chance * (1f + PacifyBoost * Of(holder));
        return boosted > PacifyChanceCap ? PacifyChanceCap : boosted;
    }
}
