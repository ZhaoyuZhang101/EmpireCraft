using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CultureShareRules
{
    public const float AssimilatedThreshold = 80f;
    public const float TitleConversionThreshold = 70f;
    // 城市层面：挑战文化必须形成稳定多数，并明显领先当前官方主流文化。
    public const float CityCultureShiftThreshold = 55f;
    public const float CityCultureShiftLeadMargin = 15f;
    public const int CityCultureShiftStableYears = 5;
    // 新生儿采用出生城市官方文化的概率。70% 是制度教育提供的基础值，官方文化相对
    // 最强竞争文化的领先越明显，概率越接近 100%；未触发时保留父母继承出的文化。
    public const float NewbornLocalCultureBaseChance = 0.70f;
    public const float NewbornLocalCultureMaxChance = 1f;
    public const float NewbornLocalCultureChance = NewbornLocalCultureBaseChance;
    // 人口迁徙只会缓慢改变城市中的文化影响力。和平治理下，官方文化依靠制度、教育和公共生活
    // 始终保留多数地位；只有异文化征服才能直接更换官方文化并打破这种惯性。
    public const float PopulationPressurePerYear = 1f;
    public const float OfficialCultureInstitutionalFloor = 55f;
    public const float ForeignOccupationCultureShock = 30f;
    public const float ForeignOccupationCultureTarget = 60f;
    // 王国/帝国法理层面：某文化需要成为法理辖下至少这个比例的城市的主流文化（且是首都的主流文化）
    // 才允许“文治”派系发起“改变法理主流文化”的决议。
    public const float TitleCultureDominanceRatio = 2f / 3f;
    private const float Epsilon = 0.0001f;

    public static bool IsAssimilated(float share)
    {
        return share > AssimilatedThreshold;
    }

    public static float CalculateNewbornLocalCultureChance(float officialShare, float strongestOtherShare)
    {
        officialShare = Math.Max(0f, Math.Min(100f, officialShare));
        strongestOtherShare = Math.Max(0f, Math.Min(100f, strongestOtherShare));
        float leadRatio = Math.Max(0f, officialShare - strongestOtherShare) / 100f;
        return NewbornLocalCultureBaseChance +
               (NewbornLocalCultureMaxChance - NewbornLocalCultureBaseChance) * leadRatio;
    }

    public static void SetShare(Dictionary<string, float> shares, string culture, float percentage)
    {
        if (shares == null || string.IsNullOrWhiteSpace(culture)) return;
        percentage = Math.Max(0f, Math.Min(100f, percentage));
        float oldValue = shares.TryGetValue(culture, out float current) ? current : 0f;
        float oldOthers = Math.Max(0f, 100f - oldValue);
        float newOthers = 100f - percentage;
        foreach (string key in shares.Keys.Where(key => key != culture).ToList())
        {
            shares[key] = oldOthers <= Epsilon ? 0f : shares[key] * newOthers / oldOthers;
        }
        shares[culture] = percentage;
    }

    public static void Normalize(Dictionary<string, float> shares, Func<string, bool> isValidCulture)
    {
        if (shares == null) return;
        foreach (string key in shares.Keys.ToList())
        {
            float value = shares[key];
            if (!isValidCulture(key) || float.IsNaN(value) || float.IsInfinity(value) || value <= Epsilon)
                shares.Remove(key);
            else
                shares[key] = Math.Max(0f, value);
        }
        float total = shares.Values.Sum();
        if (total <= Epsilon) return;
        foreach (string key in shares.Keys.ToList()) shares[key] = shares[key] * 100f / total;
        string largest = shares.OrderByDescending(pair => pair.Value).First().Key;
        shares[largest] += 100f - shares.Values.Sum();
    }

    public static string FindUnanimousConversionCulture(IEnumerable<IDictionary<string, float>> cityShares,
        string currentCulture, float threshold)
    {
        List<IDictionary<string, float>> cities = cityShares?.Where(shares => shares != null && shares.Count > 0).ToList()
                                                   ?? new List<IDictionary<string, float>>();
        if (cities.Count == 0) return "";
        string candidate = cities[0].OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
        if (candidate == currentCulture) return "";
        return cities.All(shares => shares.TryGetValue(candidate, out float value) && value > threshold)
            ? candidate
            : "";
    }
}
