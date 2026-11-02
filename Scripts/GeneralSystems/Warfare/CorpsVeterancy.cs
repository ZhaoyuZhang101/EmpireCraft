using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 老兵经验(借鉴钢铁雄心的部队经验，用户 2026-11-02 "先做第一批")：每支军 0~100。
//   · 涨：每月交战涨一点，打得越好涨得越多(见 FrontSystem.ResolveCity)；
//   · 落：补新兵按人头冲淡(见 CorpsSystem.Reinforce)，太平时每月回落一点，一代人不打仗就成了新军；武备废弛回落加倍；
//   · 效果：战力至多 +30%，交战掉组织度至多少两成五；组成"老练、精锐、百战之师"三档，升到百战之师记入军报。
// 新军从 0 起，旧存档的军也是 0，所以不改变开局的强弱；只有打过仗的才强。
public static class CorpsVeterancy
{
    private const float MaxCombat = 0.3f, MaxOrgSaving = 0.25f;
    private const float BaseGain = 0.8f, PerformanceGain = 2.2f;
    private const float PeaceDecay = 0.12f;
    private static readonly float[] TierFloors = { 20f, 50f, 80f };

    private static float Share(CorpsData corps) => Mathf.Clamp01((corps?.veterancy ?? 0f) / 100f);

    public static float CombatFactor(CorpsData corps) => 1f + MaxCombat * Share(corps);

    public static float OrgLossFactor(CorpsData corps) => 1f - MaxOrgSaving * Share(corps);

    // 0 新军、1 老练、2 精锐、3 百战之师
    public static int Tier(CorpsData corps)
    {
        float value = corps?.veterancy ?? 0f;
        int tier = 0;
        while (tier < TierFloors.Length && value >= TierFloors[tier]) tier++;
        return tier;
    }

    public static string TierName(CorpsData corps) => LM.Get("corps_veterancy_" + Tier(corps));

    public static string TierHex(CorpsData corps) => Tier(corps) switch
    {
        3 => "#F3C34A",
        2 => "#E6DCA8",
        1 => "#C9E6A8",
        _ => "#8FA0A8"
    };

    // 一月交战后；performance：本方这一仗打得如何(0 惨败 ~ 1 大胜)
    public static void Gain(Kingdom kingdom, CorpsData corps, float performance)
    {
        if (corps == null) return;
        int before = Tier(corps);
        corps.veterancy = Mathf.Min(100f, corps.veterancy + BaseGain + PerformanceGain * Mathf.Clamp01(performance));
        if (before >= TierFloors.Length || Tier(corps) < TierFloors.Length) return;
        string text = string.Format(LM.Get("corps_log_veteran"), CorpsSystem.Name(corps));
        FrontSystem.Log(kingdom, text);
        WarChronicle.RecordFor(kingdom, WarChronicle.Commander, text);
    }

    // 补进 added 个新兵：经验按人头摊薄
    public static void Dilute(CorpsData corps, float before, float added)
    {
        if (corps == null || added <= 0f || before + added <= 0f) return;
        corps.veterancy *= Mathf.Max(0f, before) / (before + added);
    }

    // 太平时每月回落(见 CorpsSystem.Recover)；手上在打仗的不落
    public static void Monthly(Kingdom kingdom, List<CorpsData> list)
    {
        if (list == null || CorpsSystem.IsAtWar(kingdom)) return;
        float decay = PeaceDecay * (MaladySystem.OrgCeiling(kingdom) < 100f ? 2f : 1f);
        foreach (CorpsData corps in list)
            if (corps.veterancy > 0f) corps.veterancy = Mathf.Max(0f, corps.veterancy - decay);
    }
}
