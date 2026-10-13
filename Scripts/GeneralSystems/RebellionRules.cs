using System;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class RebellionRules
{
    public const int LocalGraceYears = 10;

    public static int GraceYearsRemaining(bool member, double joinedAt, double now, int yearsSince)
    {
        if (!member || joinedAt < 0d || double.IsNaN(joinedAt) || double.IsInfinity(joinedAt) || now < joinedAt)
            return 0;
        return Math.Max(0, LocalGraceYears - Math.Max(0, yearsSince));
    }

    // 高正统不抹掉饥荒、兼并或民族冲突，但会降低这些矛盾升级为武装起事的概率：正统 50 以下不打折，90 时约四成，最低两成五。
    public static float LegitimacyFactor(int legitimacy) =>
        legitimacy < 0 ? 1f : Math.Max(0.25f, Math.Min(1f, (115 - Math.Max(0, Math.Min(100, legitimacy))) / 65f));
}
