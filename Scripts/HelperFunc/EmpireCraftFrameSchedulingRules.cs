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
    public const double SpareFrameThresholdMilliseconds = 18d;
    public const double ModerateFrameThresholdMilliseconds = 24d;

    public static double ResolveBudgetMilliseconds(bool enabled, int population, double frameMilliseconds)
    {
        return ResolveBudgetMilliseconds(enabled, false, population, frameMilliseconds);
    }

    public static double ResolveBudgetMilliseconds(bool enabled, bool adaptiveThroughput,
        int population, double frameMilliseconds)
    {
        double budget = !enabled ? NormalBudgetMilliseconds :
            population >= ExtremePopulationThreshold ? ExtremePopulationBudgetMilliseconds :
            population >= HighPopulationThreshold ? HighPopulationBudgetMilliseconds : NormalBudgetMilliseconds;
        if (frameMilliseconds > 33d) budget *= 0.5d;
        else if (frameMilliseconds > 22d) budget *= 0.75d;

        if (adaptiveThroughput && frameMilliseconds <= ModerateFrameThresholdMilliseconds)
        {
            double spareCapacityBudget = frameMilliseconds <= SpareFrameThresholdMilliseconds
                ? population >= ExtremePopulationThreshold ? 3.5d :
                    population >= HighPopulationThreshold ? 4d : 3d
                : population >= HighPopulationThreshold ? 2.25d : 2.5d;
            budget = Math.Max(budget, spareCapacityBudget);
        }
        return Math.Max(MinimumBudgetMilliseconds, budget);
    }

    public static int ResolveMaximumKingdoms(bool enabled, int population)
    {
        return ResolveMaximumKingdoms(enabled, false, population);
    }

    public static int ResolveMaximumKingdoms(bool enabled, bool adaptiveThroughput, int population)
    {
        int maximum = !enabled ? 64 : population >= ExtremePopulationThreshold ? 8 :
            population >= HighPopulationThreshold ? 16 : 32;
        if (!adaptiveThroughput) return maximum;
        if (population >= ExtremePopulationThreshold) return 48;
        if (population >= HighPopulationThreshold) return 64;
        return 96;
    }

    public static int ResolveMaximumEmpires(bool adaptiveThroughput, int population)
    {
        if (!adaptiveThroughput) return 1;
        if (population >= ExtremePopulationThreshold) return 8;
        if (population >= HighPopulationThreshold) return 12;
        return 16;
    }

    public static int ResolveMaximumTitles(bool enabled, bool adaptiveThroughput, int population)
    {
        return ResolveMaximumTitles(enabled, adaptiveThroughput, population, 0d);
    }

    public static int ResolveMaximumTitles(bool enabled, bool adaptiveThroughput, int population,
        double frameMilliseconds)
    {
        if (!adaptiveThroughput || frameMilliseconds > ModerateFrameThresholdMilliseconds)
            return ResolveMaximumKingdoms(enabled, population);
        if (population >= ExtremePopulationThreshold) return 32;
        if (population >= HighPopulationThreshold) return 48;
        return 64;
    }

    public static bool CanContinue(int processed, int minimum, int maximum,
        double elapsedMilliseconds, double budgetMilliseconds)
    {
        if (processed < minimum) return true;
        if (processed >= maximum) return false;
        return elapsedMilliseconds < budgetMilliseconds;
    }
}
