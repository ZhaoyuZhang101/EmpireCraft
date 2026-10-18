using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝局危机只放大已经罢朝的政治冲突；低正统本身不凭空制造反对派。
public static class CourtCrisisRules
{
    public static float GrievanceChange(int legitimacy, bool boycotting, float favor) =>
        legitimacy < 30 && boycotting ? 6f + Math.Max(0, 30 - legitimacy) / 10f : favor >= 60f ? -8f : -4f;

    public static int AdvanceYears(int legitimacy, float boycottShare, int years) =>
        legitimacy < 30 && boycottShare >= 40f ? Math.Min(10, Math.Max(0, years) + 1) : Math.Max(0, years - 2);

    public static bool CanCoalition(int legitimacy, int years, float power, float grievance, float favor, float capital) =>
        legitimacy < 30 && years >= 2 && power >= 40f && grievance >= 70f && favor <= 35f && capital >= 60f;
}
