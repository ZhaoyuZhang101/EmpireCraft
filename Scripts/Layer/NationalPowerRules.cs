using System;

namespace EmpireCraft.Scripts.Layer;

public static class NationalPowerRules
{
    // 经济不再单列(用户 2026-11-09"国力也可以把经济占比去掉了，因为经济也可以影响人口和军队")：
    // 国库丰盈本来就会变成更多的人口与更能动员的军队，再单记一项等于把同一件事算两遍。
    // 原来经济那 0.10 按原比例还给人口与军力(2:1)——两者的相对权重不变，国力的整体量级也和以前一致，
    // 各处按国力比值判定的门槛(归附、殖民、朝贡上限、称帝路线)不受影响。
    public const double PopulationWeight = 2d / 3d;
    public const double MilitaryWeight = 1d / 3d;
    public const double EconomyWeight = 0d;
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
