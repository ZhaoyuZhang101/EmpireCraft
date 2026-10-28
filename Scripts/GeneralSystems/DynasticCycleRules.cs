using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 王朝积弊只能延缓(用户 2026-10-26："积弊无法削减，仅可延缓增长"；"积弊上限 100，对应自然增长下满额所需 200 年")：
//   王朝存续期间只涨不降。以前的减项(治世每年消 1.2/0.6/0.3、中兴 -0.6、开国气象 -0.3、明君 -0.3)改为给当年的增长打折，
//   每年至少涨 MinGrowth，再乘王朝年岁的快慢(开国气象里不涨、立国头 80 年二成、160 年内四成五，见 MaladySystem.Pace)。
//   只有改朝换代才清(清空、减半、保留三档，见 DynasticCycleSystem.OnDynastyChanged)。
//   折扣随国祚递减(用户 10-29 群友反馈"国祚 300 多年了积弊还是这么低"，选"折扣递减 + 旧档补底")：百年以后治世、明君、
//   中兴的折扣逐渐失效，三百年时只剩两成(FadeShare)——老王朝再贤明的君主、再太平的年景也压不住沉积；
//   旧档补底：积弊低于同岁数最少该有的(AgeFloor：明君治世年年最慢的增长累到这个岁数，打 FloorShare)，每年另补 CatchUpPerYear，
//   补到为止(以前治世会往回降、旧档读进来只补了一部分，几百年的王朝积弊才二十几)。
//   按常数估算(不计六弊、派系等推高)：中主、平常治理约 240 年过危机线 60、三百年上下满额；
//   治世中主约 280 年过 60、350 年满额；明君治世为主约 300 年过 60、四百年前后满额；昏君迭出、早逝、更替频繁约 85 年过 60、110 年满额。
internal static class DynasticCycleRules
{
    public const float CrisisPressure = 60f;
    // 满额：到 99.5 就算(逐年累加的浮点到不了整 100)
    public const float SaturatedPressure = 99.5f;
    // 年岁分档：百年、两百年、三百年
    private const int FirstBand = 100, SecondBand = 200, ThirdBand = 300;
    // 打折：治世五折、明君(含开国雄主、中兴之主)六折、中兴之主力挽再七折；打折后每年至少涨 MinGrowth(乘年岁快慢之前)
    public const float GoodGovernanceSlowdown = 0.5f, WiseRulerSlowdown = 0.6f, RestorerSlowdown = 0.7f, MinGrowth = 0.05f;
    // 折扣递减：百年到三百年之间折扣失效到 FadeShare
    private const float FadeShare = 0.8f;
    // 补底：同岁数最少该有的积弊 = 明君治世最慢增长累计 × FloorShare；每年至多补 CatchUpPerYear
    private const float FloorShare = 0.85f;
    public const float CatchUpPerYear = 2f;
    private const int FloorYears = 1000, GraceYears = 10;
    // 加速因素(用户 2026-10-26)：君主早逝(前任在位不满 10 年或卒年不满 30)一次 +2.5；近 20 年换了 3 位以上君主，每年另加 0.3；
    // 禅让篡位(兵变禅让、权臣外戚代立)一次 +5
    public const int PrematureReignYears = 10, PrematureAge = 30, ChurnYears = 20, ChurnSuccessions = 3;
    public const float PrematureStrain = 2.5f, ChurnStrain = 0.3f, UsurpationStrain = 5f;
    // 满额民变：一次起事的城数 1 + 满额年数/10，至多 4；民怨 80 以上再 +1
    private const float SaturatedWaveYears = 10f, BoilingGrievance = 80f;
    private const int MaxSaturatedWaves = 4;
    private static float Bound(float value) => Math.Max(0f, Math.Min(100f, value));

    // 当年的增长：打折之后、乘王朝年岁快慢之前，不小于 MinGrowth。
    // extra：君主资质(庸昏暴)、六条朝政积弊、派系失衡、连年饥荒各系统的推高(只取正，见 DynasticCycleSystem.Update)
    public static float AnnualGrowth(int age, float burden, bool bankrupt, bool atWar, double corruption,
        bool interregnum, bool restorer, bool wise, float extra, float settledOtherBurden = -1f)
    {
        // 土地兼并、冗官、宗室膨胀随王朝年岁累积：百年内每年 0.3，两百年内 0.45，三百年内 0.6，之后 0.8
        float friction = Friction(age);
        double rate = CorruptionRules.Bound(corruption);
        // 即时腐败的民怨已包含在 burden 内；积弊只另计多年腐败的缓慢沉积。
        float otherBurden = settledOtherBurden >= 0f ? Bound(settledOtherBurden)
            : Math.Max(0f, Bound(burden) - (float)rate * 40f);
        float growth = friction + Math.Max(0f, otherBurden - 35f) * 0.008f +
                       (float)Math.Max(0d, rate - 0.25d) * 0.24f + Math.Max(0f, extra);
        if (bankrupt) growth += 0.15f;
        if (interregnum) growth += 0.2f;
        float factor = 1f;
        // 治世(不亏空、无战事、苛政 30 以下、腐败两成五以下、有君在位)只能延缓；三样折扣都随国祚递减(见 Slowdown)
        if (!bankrupt && !atWar && burden <= 30f && corruption <= 0.25d && !interregnum) factor *= Slowdown(GoodGovernanceSlowdown, age);
        if (wise && !interregnum) factor *= Slowdown(WiseRulerSlowdown, age);
        if (restorer && !bankrupt && burden < 40f && !interregnum) factor *= Slowdown(RestorerSlowdown, age);
        return Math.Max(MinGrowth, growth * factor);
    }

    // 折扣随国祚递减：百年内照旧，三百年时折扣只剩 1 - FadeShare
    private static float Slowdown(float factor, int age) =>
        factor + (1f - factor) * Math.Max(0f, Math.Min(1f, (age - FirstBand) / (float)(ThirdBand - FirstBand))) * FadeShare;

    private static float Friction(int age) => age < FirstBand ? 0.3f : age < SecondBand ? 0.45f : age < ThirdBand ? 0.6f : 0.8f;

    // 同岁数最少该有的积弊(旧档补底，见 DynasticCycleSystem.Update)
    private static float[] _floor;

    public static float AgeFloor(int age)
    {
        if (_floor == null)
        {
            var floor = new float[FloorYears + 1];
            float sum = 0f;
            for (int year = 0; year <= FloorYears; year++)
            {
                if (year >= GraceYears)
                {
                    float least = Math.Max(MinGrowth, Friction(year) * Slowdown(GoodGovernanceSlowdown, year) *
                                                      Slowdown(WiseRulerSlowdown, year));
                    sum = Math.Min(100f, sum + least * MaladySystem.PaceAt(year));
                }
                floor[year] = sum * FloorShare;
            }
            _floor = floor;
        }
        return _floor[Math.Max(0, Math.Min(age, FloorYears))];
    }

    public static float Advance(float pressure, float change) => Bound(Bound(pressure) + change);

    // 改朝换代的承接：清空留四分之一(至多 20)、减半、保留原样(见 DynasticCycleSystem.Transition)
    public static float Carry(float pressure, DynasticCycleSystem.Transition transition) => transition switch
    {
        DynasticCycleSystem.Transition.Clear => Math.Min(20f, Bound(pressure) * 0.25f),
        DynasticCycleSystem.Transition.Halve => Bound(pressure) * 0.5f,
        _ => Bound(pressure)
    };

    // 六条朝政积弊的承接：清空各留两成、减半、保留原样
    public static float MaladyShare(DynasticCycleSystem.Transition transition) => transition switch
    {
        DynasticCycleSystem.Transition.Clear => 0.2f,
        DynasticCycleSystem.Transition.Halve => 0.5f,
        _ => 1f
    };

    // 旧档(还没有王朝起点的)只温和补入部分长期积弊，不在加载时追补几百次年度结算：
    // 按"中主、平常治理"同岁数自然该有的积弊(按年岁快慢折成满速年数，九十个满速年满额)，治世打六折，苛政轻的再减，至多 50
    public static float LegacyPressure(int age, float burden, bool goodGovernance)
    {
        float paced = 0.2f * Math.Max(0, Math.Min(age, 80) - 10) + 0.45f * Math.Max(0, Math.Min(age, 160) - 80) +
                      Math.Max(0, age - 160);
        float natural = Math.Min(100f, paced / 90f * 100f);
        float weight = (goodGovernance ? 0.6f : 1f) * (0.5f + 0.5f * Math.Min(1f, Bound(burden) / 40f));
        return Math.Min(50f, natural * weight);
    }

    public static int LegitimacyPenalty(float pressure) => -(int)Math.Round(Bound(pressure) * 0.4f,
        MidpointRounding.AwayFromZero);
    public static int HarshBurden(float pressure) => (int)Math.Round(Math.Max(0f, Bound(pressure) - 25f) * 0.4f,
        MidpointRounding.AwayFromZero);

    // 民变一次起事的城数(用户 2026-10-26)：积弊满额后按满额年数 1 + 年数/10(至多 4)；未满额的危局照旧——积弊 80、苛政 75 以上两城；
    // 民怨(PopularGrievance)80 以上再 +1
    public static int UprisingWaves(float pressure, float burden, float saturatedYears, float grievance)
    {
        int waves = pressure >= SaturatedPressure
            ? Math.Min(MaxSaturatedWaves, 1 + (int)(Math.Max(0f, saturatedYears) / SaturatedWaveYears))
            : pressure >= 80f && burden >= 75f ? 2 : 1;
        return grievance >= BoilingGrievance ? waves + 1 : waves;
    }
}
