using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 三百年是普通治理下的危机窗口，不是到期销毁王朝的倒计时。
internal static class DynasticCycleRules
{
    public const int TypicalCrisisYears = 300;
    public const float CrisisPressure = 60f;
    private static float Bound(float value) => Math.Max(0f, Math.Min(100f, value));

    public static float AnnualChange(int age, float burden, bool bankrupt, bool atWar, double corruption,
        bool interregnum, bool restorer, bool founding, float settledOtherBurden = -1f)
    {
        float friction = age < TypicalCrisisYears / 3 ? 0.05f
            : age < TypicalCrisisYears * 2 / 3 ? 0.25f : age < TypicalCrisisYears ? 0.45f : 0.6f;
        double rate = CorruptionRules.Bound(corruption);
        // 即时腐败的民怨已包含在 burden 内；积弊只另计多年腐败的缓慢沉积。
        float otherBurden = settledOtherBurden >= 0f ? Bound(settledOtherBurden)
            : Math.Max(0f, Bound(burden) - (float)rate * 40f);
        float change = friction + Math.Max(0f, otherBurden - 35f) * 0.008f +
            (float)Math.Max(0d, rate - 0.25d) * 0.24f;
        if (bankrupt) change += 0.15f;
        if (interregnum) change += 0.2f;
        if (!bankrupt && !atWar && burden <= 20f && corruption <= 0.25d && !interregnum) change -= 1.2f;
        if (restorer && !bankrupt && burden < 40f && !interregnum) change -= 0.6f;
        if (founding && !bankrupt && burden < 50f && !interregnum) change -= 0.3f;
        return change;
    }

    public static float Advance(float pressure, float change) => Bound(Bound(pressure) + change);
    // 旧档只温和补入部分长期积弊，不在加载时追补几百次年度结算。
    public static float LegacyPressure(int age, float burden, bool goodGovernance) => goodGovernance ? 0f
        : Math.Min(30f, Math.Max(0, age - 100) * 0.15f) * Math.Min(1f, Bound(burden) / 40f);
    public static int LegitimacyPenalty(float pressure) => -(int)Math.Round(Bound(pressure) * 0.4f,
        MidpointRounding.AwayFromZero);
    public static int HarshBurden(float pressure) => (int)Math.Round(Math.Max(0f, Bound(pressure) - 25f) * 0.4f,
        MidpointRounding.AwayFromZero);
    public static int UprisingWaves(float pressure, float burden) => pressure >= 80f && burden >= 75f ? 2 : 1;
}
