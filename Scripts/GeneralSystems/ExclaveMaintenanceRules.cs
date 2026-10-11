using System;

namespace EmpireCraft.Scripts.GeneralSystems;

internal static class ExclaveMaintenanceRules
{
    // 和按户计的仓库、税收保持同一货币尺度；距离和面积增加后勤费用。
    internal static int AnnualCost(int zones, double households, double distance, bool overseas) => zones <= 0 ? 0 :
        (int)Math.Min(1000000d, Math.Ceiling((4d + zones * .2d + Math.Max(0d, households) * .02d +
                                          Math.Max(0d, distance) / 64d) * (overseas ? 1.5d : 1d)));

    internal static bool FiscalPressure(int treasury, int cost, double domesticBalance) => cost > 0 &&
        (treasury < cost || treasury <= Math.Max(20L, (long)cost * 2) && domesticBalance <= cost);

    internal static bool ShouldRelease(int weakYears, bool hasYearOfEvidence, bool atWar) =>
        hasYearOfEvidence && weakYears >= 2 && !atWar;
}
