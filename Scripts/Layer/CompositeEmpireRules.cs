using System;

namespace EmpireCraft.Scripts.Layer;

public enum CompositeEmpireIntegrationStage
{
    None = 0,
    DualAdministration = 1,
    CompositeEmpire = 2,
    IntegratedDynasty = 3
}

public static class CompositeEmpireRules
{
    public const int InitialIntegration = 35;
    public const int CompositeThreshold = 50;
    public const int IntegratedThreshold = 80;
    public const int PreservedEliteIntegrationCap = 69;

    public static CompositeEmpireIntegrationStage GetStage(int integration)
    {
        if (integration >= IntegratedThreshold) return CompositeEmpireIntegrationStage.IntegratedDynasty;
        if (integration >= CompositeThreshold) return CompositeEmpireIntegrationStage.CompositeEmpire;
        if (integration > 0) return CompositeEmpireIntegrationStage.DualAdministration;
        return CompositeEmpireIntegrationStage.None;
    }

    public static int GetAnnualIntegrationDelta(float institutionalShare, float rulingShare,
        bool emperorUsesInstitutionalCulture, bool emperorUsesRulingCulture)
    {
        int delta = institutionalShare >= 0.67f ? 2 : institutionalShare >= 0.5f ? 1 : 0;
        if (emperorUsesInstitutionalCulture) delta += 3;
        if (emperorUsesRulingCulture) delta -= 1;
        if (rulingShare >= 0.2f) delta -= 1;
        return Math.Max(-2, Math.Min(5, delta));
    }

    public static int CapIntegration(int value, bool rulingEliteStillExists)
    {
        int cap = rulingEliteStillExists ? PreservedEliteIntegrationCap : 100;
        return Math.Max(0, Math.Min(cap, value));
    }

    public static int GetCentralLegitimacyDelta(bool usesInstitutionalRegime, float controlledCoreShare,
        float institutionalPopulationShare)
    {
        int delta = usesInstitutionalRegime ? 1 : -2;
        delta += controlledCoreShare >= 0.67f ? 1 : controlledCoreShare < 0.5f ? -2 : 0;
        delta += institutionalPopulationShare >= 0.5f ? 1 : 0;
        return Math.Max(-4, Math.Min(3, delta));
    }

    public static int GetRulingLegitimacyDelta(bool emperorUsesRulingCulture, float rulingPopulationShare,
        bool preservesMilitaryTradition)
    {
        int delta = emperorUsesRulingCulture ? 1 : -2;
        delta += rulingPopulationShare >= 0.08f ? 1 : rulingPopulationShare < 0.03f ? -1 : 0;
        if (preservesMilitaryTradition) delta += 1;
        return Math.Max(-4, Math.Min(3, delta));
    }
}
