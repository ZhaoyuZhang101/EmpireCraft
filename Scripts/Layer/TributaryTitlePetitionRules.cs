using System;

namespace EmpireCraft.Scripts.Layer;

public static class TributaryTitlePetitionRules
{
    // 朝贡国君请封花的名望(以前 300；名望开销按身份年入的新名望重定(用户 2026-11-10 "名望开销也可以调整"；稳态皇帝八九百、一品大员五百上下、封君五百、五品一两百))
    public const int PetitionInfluenceCost = 200;

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
