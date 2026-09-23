using System;

namespace EmpireCraft.Scripts.Layer;

public static class ImperialLegitimacyRules
{
    // 国力压过帝国本身即可发起；不接壤时要压过 1.5 倍(远方大国也能问鼎，不再被一城小帝国永远占着位置)
    public const double DistantChallengeRatio = 1.5d;

    public static bool CanChallenge(double kingdomPower, double empirePower,
        int sameCultureEmpireCount, bool isIndependent, bool sharesLandBorder)
    {
        if (!isIndependent || sameCultureEmpireCount != 1 || kingdomPower <= empirePower) return false;
        return sharesLandBorder || kingdomPower >= empirePower * DistantChallengeRatio;
    }

    public static int RequiredOccupiedZones(int totalZones)
    {
        return totalZones <= 0 ? int.MaxValue : (int)Math.Ceiling(totalZones / 3d);
    }

    public static bool HasOccupiedThird(int occupiedZones, int totalZones)
    {
        int required = RequiredOccupiedZones(totalZones);
        return required != int.MaxValue && occupiedZones >= required;
    }
}
