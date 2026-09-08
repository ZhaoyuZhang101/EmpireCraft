using System;

namespace EmpireCraft.Scripts.Layer;

public static class NationalPowerRules
{
    public const double PopulationWeight = 0.60d;
    public const double MilitaryWeight = 0.30d;
    public const double EconomyWeight = 0.10d;
    public const double TributaryMaximumShare = 0.20d;
    public const double SubmissionMaximumShare = 0.10d;

    public static double Calculate(int population, int military, int economy)
    {
        return Math.Max(0, population) * PopulationWeight +
               Math.Max(0, military) * MilitaryWeight +
               Math.Max(0, economy) * EconomyWeight;
    }

    public static bool IsWithinShare(double subjectPower, double empirePower, double maximumShare)
    {
        return empirePower > 0d && subjectPower >= 0d && maximumShare > 0d &&
               subjectPower <= empirePower * maximumShare;
    }
}
