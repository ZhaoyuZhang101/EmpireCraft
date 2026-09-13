using System;

namespace EmpireCraft.Scripts.Layer;

public struct CityValueSnapshot
{
    public int Total;
    public int Population;
    public int Wealth;
    public int Proximity;
    public bool IsIsolated;
    public float DistanceToCore;
}

public static class CityValueRules
{
    public static CityValueSnapshot Evaluate(int population, int wealth, float distanceToCore, float coreRadius,
        bool isIsolated)
    {
        int populationScore = ScaleLogarithmic(population, 500, 45);
        int wealthScore = ScaleLogarithmic(wealth, 500, 30);
        float reach = Math.Max(24f, coreRadius * 1.35f);
        int proximityScore = RoundToInt(25f * (1f - Clamp01(Math.Max(0f, distanceToCore) / reach)));
        int total = populationScore + wealthScore + proximityScore;
        if (isIsolated) total = RoundToInt(total * 0.85f);
        return new CityValueSnapshot
        {
            Total = Math.Max(0, Math.Min(100, total)),
            Population = populationScore,
            Wealth = wealthScore,
            Proximity = proximityScore,
            IsIsolated = isIsolated,
            DistanceToCore = Math.Max(0f, distanceToCore)
        };
    }

    public static int GetIsolationRebellionLoyaltyThreshold(CityValueSnapshot value)
    {
        if (!value.IsIsolated) return 0;
        return Math.Max(8, Math.Min(38, 8 + (60 - value.Total) / 2));
    }

    public static bool CanRebelWhileContent(CityValueSnapshot value)
    {
        return value.IsIsolated && value.Total <= 45;
    }

    public static float GetExclaveDisengagementChance(CityValueSnapshot value, int rebelCityCount)
    {
        if (!value.IsIsolated || value.Total > 60) return 0f;
        float chance = 0.12f + (60 - value.Total) * 0.009f;
        if (rebelCityCount > 1) chance *= 0.65f;
        return Math.Max(0f, Math.Min(0.6f, chance));
    }

    private static int ScaleLogarithmic(int value, int reference, int maximum)
    {
        if (value <= 0 || reference <= 0 || maximum <= 0) return 0;
        double share = Math.Log(value + 1d) / Math.Log(reference + 1d);
        return RoundToInt(maximum * Clamp01((float)share));
    }

    private static int RoundToInt(float value)
    {
        return (int)Math.Floor(value + 0.5f);
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
    }
}
