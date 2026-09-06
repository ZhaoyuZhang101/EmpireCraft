using System;

namespace EmpireCraft.Scripts.HelperFunc;

public static class EmpireCraftFrameSchedulingRules
{
    public const int HighPopulationThreshold = 5000;
    public const int ExtremePopulationThreshold = 20000;
    public const double MinimumBudgetMilliseconds = 0.5d;
    public const double NormalBudgetMilliseconds = 2.0d;
    public const double HighPopulationBudgetMilliseconds = 1.25d;
    public const double ExtremePopulationBudgetMilliseconds = 0.75d;

    public static double ResolveBudgetMilliseconds(bool enabled, int population, double frameMilliseconds)
    {
        if (!enabled) return NormalBudgetMilliseconds;
        double budget = population >= ExtremePopulationThreshold
            ? ExtremePopulationBudgetMilliseconds
            : population >= HighPopulationThreshold
                ? HighPopulationBudgetMilliseconds
                : NormalBudgetMilliseconds;
        if (frameMilliseconds > 33d) budget *= 0.5d;
        else if (frameMilliseconds > 22d) budget *= 0.75d;
        return Math.Max(MinimumBudgetMilliseconds, budget);
    }

    public static int ResolveMaximumKingdoms(bool enabled, int population)
    {
        if (!enabled) return 64;
        if (population >= ExtremePopulationThreshold) return 8;
        if (population >= HighPopulationThreshold) return 16;
        return 32;
    }

    public static bool CanContinue(int processed, int minimum, int maximum,
        double elapsedMilliseconds, double budgetMilliseconds)
    {
        if (processed < minimum) return true;
        if (processed >= maximum) return false;
        return elapsedMilliseconds < budgetMilliseconds;
    }
}
