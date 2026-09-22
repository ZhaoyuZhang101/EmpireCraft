using System;

namespace EmpireCraft.Scripts.Layer;

public static class ImperialLegitimacyRules
{
    public static bool CanChallenge(double kingdomPower, double empirePower,
        int sameCultureEmpireCount, bool isIndependent, bool sharesLandBorder)
    {
        return isIndependent && sharesLandBorder && sameCultureEmpireCount == 1 && kingdomPower > empirePower;
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
