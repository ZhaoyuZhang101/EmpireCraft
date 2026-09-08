using System;

namespace EmpireCraft.Scripts.Layer;

public static class TributaryTitlePetitionRules
{
    public const int PetitionInfluenceCost = 300;

    public static bool CanPetition(bool isTributaryOfEmpire, bool hasMainTitle, int overlordOpinion)
    {
        return CanPetition(isTributaryOfEmpire, hasMainTitle, overlordOpinion, int.MaxValue);
    }

    public static bool CanPetition(bool isTributaryOfEmpire, bool hasMainTitle, int overlordOpinion,
        int rulerInfluence)
    {
        return isTributaryOfEmpire && !hasMainTitle && overlordOpinion >= 0 &&
               rulerInfluence >= PetitionInfluenceCost;
    }

    public static bool ControlsRequiredTerritory(bool controlsTitleCapital, int controlledCities,
        int titleCities, double controlledRate)
    {
        if (!controlsTitleCapital || titleCities <= 0 || controlledCities <= 0) return false;
        int requiredCities = (int)Math.Ceiling(titleCities * controlledRate);
        return controlledCities >= requiredCities;
    }
}
