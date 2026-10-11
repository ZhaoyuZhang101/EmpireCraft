using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class InstitutionRules
{
    public static float Clamp100(float value) => Math.Max(0f, Math.Min(100f, value));

    public static float CalculateAnnualProgress(float baseProgress, float support, float opposition)
    {
        float multiplier = 0.65f + Clamp100(support) * 0.0075f - Clamp100(opposition) * 0.005f;
        return Math.Max(0.5f, baseProgress * multiplier);
    }

    public static float CalculateDurationScaledAnnualProgress(float baseProgress, float support, float opposition,
        int environmentalDurationYears)
    {
        float politicalMultiplier = 0.65f + Clamp100(support) * 0.0075f - Clamp100(opposition) * 0.005f;
        float nodePace = Math.Max(0.65f, Math.Min(1.25f, baseProgress / 8f));
        float duration = Math.Max(1, environmentalDurationYears);
        return Math.Max(0.25f, 100f / duration * politicalMultiplier * nodePace);
    }

    public static float CalculateAnnualRadicalism(float support, float opposition, bool replacesVestedInterest,
        bool forced)
    {
        float change = Math.Max(-2f, (Clamp100(opposition) - Clamp100(support)) * 0.08f);
        if (replacesVestedInterest) change += 1.5f;
        if (forced) change += 0.5f;
        return change;
    }

    public static InstitutionReformStage ResolveStage(float progress)
    {
        if (progress < 25f) return InstitutionReformStage.Debate;
        if (progress < 50f) return InstitutionReformStage.Legislation;
        if (progress < 75f) return InstitutionReformStage.Implementation;
        return InstitutionReformStage.Consolidation;
    }

    // 文明等级：按累计制度分（各分支已掌握节点的最高等级之和）落到全局门槛表上。
    public static InstitutionCultureLevelConfig ResolveLevel(IEnumerable<InstitutionCultureLevelConfig> levels,
        int advancement)
    {
        List<InstitutionCultureLevelConfig> list = (levels ?? Enumerable.Empty<InstitutionCultureLevelConfig>())
            .Where(level => level != null).ToList();
        if (list.Count == 0) list = InstitutionConfigNormalizer.DefaultCultureLevels();
        return list.Where(level => advancement >= level.minimum_advancement)
                   .OrderByDescending(level => level.minimum_advancement)
                   .ThenByDescending(level => level.level)
                   .FirstOrDefault()
               ?? list.OrderBy(level => level.level).First();
    }

    public static InstitutionCultureLevelConfig ResolveManualLevel(
        IEnumerable<InstitutionCultureLevelConfig> levels, int manualLevel)
    {
        List<InstitutionCultureLevelConfig> list = (levels ?? Enumerable.Empty<InstitutionCultureLevelConfig>())
            .Where(level => level != null).ToList();
        if (list.Count == 0) list = InstitutionConfigNormalizer.DefaultCultureLevels();
        return list.Where(level => level.level <= manualLevel).OrderByDescending(level => level.level)
                   .FirstOrDefault()
               ?? list.OrderBy(level => level.level).First();
    }

    // 吸收的等级门槛：节点等级必须高于本文化当前文明等级（"只有比自己先进的制度才值得照搬"）。
    // maxTierGap > 0 时再额外限制领先幅度，0 表示不限制。
    public static bool IsTierAbsorbable(int nodeAdvancement, int cultureLevel, int maxTierGap)
    {
        if (nodeAdvancement <= cultureLevel) return false;
        return maxTierGap <= 0 || nodeAdvancement - cultureLevel <= maxTierGap;
    }

    public static bool IsExposureSatisfied(float exposure, int contactYears, float minimumExposure,
        int minimumContactYears)
    {
        return exposure >= minimumExposure && contactYears >= minimumContactYears;
    }
}
