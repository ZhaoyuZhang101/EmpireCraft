using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 派系的抽象社会基础。具体人物仍负责领导与官场活动；阶层支持则代表农民、商人、官僚等
// 社会集团整体把多少政治期待投向该派系。一个阶层在各活跃派系间的支持份额总和约为 100%。
public static class FactionClassSystem
{
    private const float AnnualAlignmentRate = 0.25f;
    private const float AnnualFavorDecay = 0.05f;

    public static void EnsureProfile(FixedFaction faction)
    {
        if (faction == null) return;
        faction.ClassAffinities ??= new Dictionary<SocialClass, float>();
        faction.ClassFavor ??= new Dictionary<SocialClass, float>();
        faction.ClassSupport ??= new Dictionary<SocialClass, float>();
        faction.ClassFavorSources ??= new Dictionary<SocialClass, string>();
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
        {
            if (!faction.ClassAffinities.ContainsKey(socialClass))
                faction.ClassAffinities[socialClass] = GetDefaultAffinity(faction.Type, socialClass);
            if (!faction.ClassFavor.ContainsKey(socialClass)) faction.ClassFavor[socialClass] = 50f;
            if (!faction.ClassSupport.ContainsKey(socialClass)) faction.ClassSupport[socialClass] = 0f;
            if (!faction.ClassFavorSources.ContainsKey(socialClass)) faction.ClassFavorSources[socialClass] = "";
            faction.ClassAffinities[socialClass] = Clamp(faction.ClassAffinities[socialClass], -100f, 100f);
            faction.ClassFavor[socialClass] = Clamp(faction.ClassFavor[socialClass], 0f, 100f);
            faction.ClassSupport[socialClass] = Clamp(faction.ClassSupport[socialClass], 0f, 100f);
            faction.ClassFavorSources[socialClass] ??= "";
        }
    }

    public static void UpdateEmpire(Empire empire, IEnumerable<FixedFaction> configuredFactions)
    {
        List<FixedFaction> factions = configuredFactions?.Where(faction => faction != null).ToList() ?? new();
        if (empire == null || factions.Count == 0) return;
        EnsureEmpireProfiles(empire, factions);
        List<FixedFaction> active = factions.Where(faction => !faction.Ban).ToList();
        if (active.Count == 0) return;

        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
        {
            var scores = new Dictionary<FixedFaction, float>();
            foreach (FixedFaction faction in active)
            {
                float favor = faction.ClassFavor[socialClass];
                favor += (50f - favor) * AnnualFavorDecay;
                faction.ClassFavor[socialClass] = Clamp(favor, 0f, 100f);
                // 先天定位决定长期基本盘，近期好感决定同一阶层在相近派系间向谁移动。
                scores[faction] = Math.Max(1f, 50f + faction.ClassAffinities[socialClass] * 0.45f +
                                                 (faction.ClassFavor[socialClass] - 50f) * 0.8f);
            }
            float scoreTotal = scores.Values.Sum();
            float currentTotal = active.Sum(faction => faction.ClassSupport[socialClass]);
            foreach (FixedFaction faction in factions.Where(faction => faction.Ban))
                faction.ClassSupport[socialClass] = 0f;
            foreach (FixedFaction faction in active)
            {
                float target = scoreTotal > 0f ? scores[faction] / scoreTotal * 100f : 100f / active.Count;
                faction.ClassSupport[socialClass] = currentTotal <= 0.1f
                    ? target
                    : faction.ClassSupport[socialClass] +
                      (target - faction.ClassSupport[socialClass]) * AnnualAlignmentRate;
            }
            float normalizedTotal = active.Sum(faction => faction.ClassSupport[socialClass]);
            if (normalizedTotal > 0f)
                foreach (FixedFaction faction in active)
                    faction.ClassSupport[socialClass] = faction.ClassSupport[socialClass] / normalizedTotal * 100f;
        }

        RecalculateClassPower(empire, factions);
    }

    public static void EnsureEmpireProfiles(Empire empire, IEnumerable<FixedFaction> configuredFactions)
    {
        List<FixedFaction> factions = configuredFactions?.Where(faction => faction != null).ToList() ?? new();
        if (empire == null || factions.Count == 0) return;
        foreach (FixedFaction faction in factions) EnsureProfile(faction);
        List<FixedFaction> active = factions.Where(faction => !faction.Ban).ToList();
        if (active.Count == 0) return;
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
        {
            if (active.Sum(faction => faction.ClassSupport[socialClass]) > 0.1f) continue;
            Dictionary<FixedFaction, float> scores = active.ToDictionary(faction => faction,
                faction => Math.Max(1f, 50f + faction.ClassAffinities[socialClass] * 0.45f +
                                         (faction.ClassFavor[socialClass] - 50f) * 0.8f));
            float total = scores.Values.Sum();
            foreach (FixedFaction faction in active)
                faction.ClassSupport[socialClass] = scores[faction] / total * 100f;
        }
        RecalculateClassPower(empire, factions);
    }

    private static void RecalculateClassPower(Empire empire, IEnumerable<FixedFaction> factions)
    {
        Dictionary<SocialClass, float> populationShares = InstitutionSystem.BuildClassShares(empire);
        foreach (FixedFaction faction in factions)
        {
            faction.ClassPower = faction.Ban ? 0f : populationShares.Sum(pair =>
                pair.Value * faction.ClassSupport[pair.Key]);
        }
    }

    public static float GetClassPowerContribution(Empire empire, FixedFaction faction, SocialClass socialClass)
    {
        if (empire == null || faction == null) return 0f;
        EnsureProfile(faction);
        Dictionary<SocialClass, float> shares = InstitutionSystem.BuildClassShares(empire);
        float population = shares.TryGetValue(socialClass, out float share) ? share : 0f;
        return population * faction.ClassSupport[socialClass];
    }

    public static void ModifyFavor(FixedFaction faction, SocialClass socialClass, float amount, string source)
    {
        if (faction == null || Math.Abs(amount) < 0.01f) return;
        EnsureProfile(faction);
        faction.ClassFavor[socialClass] = Clamp(faction.ClassFavor[socialClass] + amount, 0f, 100f);
        faction.ClassFavorSources[socialClass] = source ?? "";
    }

    public static void ApplyInstitutionOutcome(Empire empire, InstitutionNodeConfig node, FixedFaction sponsor)
    {
        if (empire == null || node?.politics == null) return;
        string source = $"institution:{node.id}";
        foreach (FixedFaction faction in empire.CoreKingdom?.GetRegime()?.GetPlayerFactions() ??
                 Enumerable.Empty<FixedFaction>())
        {
            float supportRole = node.politics.support_factions.TryGetValue(faction.Type, out float support)
                ? Math.Max(0.25f, support)
                : 0f;
            float opposeRole = node.politics.oppose_factions.TryGetValue(faction.Type, out float oppose)
                ? Math.Max(0.25f, oppose)
                : 0f;
            if (faction == sponsor) supportRole = Math.Max(1.25f, supportRole + 0.35f);
            if (supportRole <= 0f && opposeRole <= 0f) continue;

            foreach (KeyValuePair<SocialClass, float> pair in node.politics.support_classes)
                ModifyFavor(faction, pair.Key, pair.Value * (supportRole * 10f - opposeRole * 6f), source);
            foreach (KeyValuePair<SocialClass, float> pair in node.politics.oppose_classes)
                ModifyFavor(faction, pair.Key, pair.Value * (opposeRole * 9f - supportRole * 8f), source);
        }
    }

    public static void ApplyClaimOutcome(Empire empire, string factionId, TemporaryFactionType claim)
    {
        FixedFaction faction = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.FirstOrDefault(candidate => candidate?.GetID() == factionId);
        if (faction == null) return;
        string source = $"claim:{claim}";
        foreach (KeyValuePair<SocialClass, float> pair in GetClaimClassEffects(claim))
            ModifyFavor(faction, pair.Key, pair.Value, source);
    }

    public static float GetClaimClassEffect(TemporaryFactionType claim, SocialClass socialClass)
    {
        Dictionary<SocialClass, float> effects = GetClaimClassEffects(claim);
        return effects.TryGetValue(socialClass, out float value) ? value : 0f;
    }

    public static string GetFavorSourceText(FixedFaction faction, SocialClass socialClass)
    {
        EnsureProfile(faction);
        string source = faction.ClassFavorSources[socialClass];
        if (string.IsNullOrWhiteSpace(source)) return LM.Get("faction_class_source_inherited");
        if (source.StartsWith("institution:", StringComparison.Ordinal))
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(source.Substring("institution:".Length));
            return node == null
                ? LM.Get("faction_class_source_inherited")
                : string.Format(LM.Get("faction_class_source_institution"), InstitutionSystem.GetNodeName(node));
        }
        if (source.StartsWith("claim:", StringComparison.Ordinal) &&
            Enum.TryParse(source.Substring("claim:".Length), out TemporaryFactionType claim))
            return string.Format(LM.Get("faction_class_source_claim"), LM.Get(claim.ToString()));
        return LM.Get("faction_class_source_inherited");
    }

    public static string GetStratumKey(SocialClass socialClass) => socialClass switch
    {
        SocialClass.Peasant or SocialClass.Labour => "faction_class_group_common",
        SocialClass.Merchant or SocialClass.Officer => "faction_class_group_middle",
        SocialClass.Noble or SocialClass.Landlord => "faction_class_group_elite",
        SocialClass.Army => "faction_class_group_military",
        _ => "faction_class_group_common"
    };

    private static Dictionary<SocialClass, float> GetClaimClassEffects(TemporaryFactionType claim)
    {
        return claim switch
        {
            TemporaryFactionType.降低赋税 => Effects((SocialClass.Peasant, 14), (SocialClass.Merchant, 8),
                (SocialClass.Officer, -4)),
            TemporaryFactionType.提高赋税 => Effects((SocialClass.Officer, 10), (SocialClass.Army, 6),
                (SocialClass.Peasant, -14), (SocialClass.Merchant, -8)),
            TemporaryFactionType.提高福利 => Effects((SocialClass.Labour, 16), (SocialClass.Peasant, 12),
                (SocialClass.Merchant, -6), (SocialClass.Noble, -5)),
            TemporaryFactionType.拓展金融霸权 => Effects((SocialClass.Merchant, 16),
                (SocialClass.Officer, 5), (SocialClass.Peasant, -6)),
            TemporaryFactionType.缩减金融霸权 => Effects((SocialClass.Peasant, 9),
                (SocialClass.Labour, 9), (SocialClass.Merchant, -16)),
            TemporaryFactionType.开科取士 => Effects((SocialClass.Officer, 16), (SocialClass.Peasant, 8),
                (SocialClass.Noble, -12)),
            TemporaryFactionType.削藩 or TemporaryFactionType.夺取诸侯开战权 or
                TemporaryFactionType.颁布帝国治安令 => Effects((SocialClass.Officer, 12),
                    (SocialClass.Peasant, 5), (SocialClass.Noble, -15)),
            TemporaryFactionType.分封 or TemporaryFactionType.允许诸侯自由开战 or
                TemporaryFactionType.确认诸侯特权 => Effects((SocialClass.Noble, 16),
                    (SocialClass.Army, 6), (SocialClass.Officer, -10), (SocialClass.Peasant, -5)),
            TemporaryFactionType.对外扩张 or TemporaryFactionType.游牧扩张 or
                TemporaryFactionType.劫掠 or TemporaryFactionType.迫使朝贡 => Effects((SocialClass.Army, 16),
                    (SocialClass.Noble, 5), (SocialClass.Peasant, -8), (SocialClass.Merchant, -6)),
            TemporaryFactionType.提供岁币 or TemporaryFactionType.割让城池 => Effects(
                (SocialClass.Merchant, 8), (SocialClass.Peasant, 6), (SocialClass.Army, -14)),
            TemporaryFactionType.开放移民 => Effects((SocialClass.Merchant, 9), (SocialClass.Officer, 5),
                (SocialClass.Labour, -6)),
            TemporaryFactionType.清除移民 => Effects((SocialClass.Labour, 9),
                (SocialClass.Merchant, -7), (SocialClass.Officer, -4)),
            TemporaryFactionType.加强神权 or TemporaryFactionType.确立国教 or
                TemporaryFactionType.划地给教廷 or TemporaryFactionType.恢复圣地 => Effects(
                    (SocialClass.Noble, 8), (SocialClass.Peasant, 7), (SocialClass.Merchant, -7)),
            TemporaryFactionType.自由信仰 or TemporaryFactionType.渴望共和 => Effects(
                (SocialClass.Merchant, 12), (SocialClass.Officer, 8), (SocialClass.Noble, -8)),
            TemporaryFactionType.输出革命 or TemporaryFactionType.扶持革命党 => Effects(
                (SocialClass.Labour, 15), (SocialClass.Peasant, 12), (SocialClass.Noble, -15)),
            TemporaryFactionType.禁党 => Effects((SocialClass.Officer, 5), (SocialClass.Army, 5),
                (SocialClass.Labour, -10), (SocialClass.Merchant, -8)),
            TemporaryFactionType.供养宗室 or TemporaryFactionType.恢复世袭皇权 or
                TemporaryFactionType.转世袭 => Effects((SocialClass.Noble, 15),
                    (SocialClass.Officer, -6), (SocialClass.Peasant, -5)),
            TemporaryFactionType.国_打击贪腐 => Effects((SocialClass.Peasant, 8),
                (SocialClass.Merchant, 8), (SocialClass.Officer, 6), (SocialClass.Noble, -7)),
            _ => new Dictionary<SocialClass, float>()
        };
    }

    private static Dictionary<SocialClass, float> Effects(params (SocialClass socialClass, float amount)[] values) =>
        values.ToDictionary(value => value.socialClass, value => value.amount);

    private static float GetDefaultAffinity(FactionType faction, SocialClass socialClass)
    {
        Dictionary<SocialClass, float> profile = faction switch
        {
            FactionType.尊王 => Affinities((SocialClass.Officer, 85), (SocialClass.Peasant, 25),
                (SocialClass.Noble, -25)),
            FactionType.诸侯 => Affinities((SocialClass.Noble, 95), (SocialClass.Army, 45),
                (SocialClass.Landlord, 70), (SocialClass.Officer, -45), (SocialClass.Peasant, -25)),
            FactionType.中央 => Affinities((SocialClass.Officer, 90), (SocialClass.Army, 45),
                (SocialClass.Merchant, 30), (SocialClass.Noble, -55)),
            FactionType.自治 => Affinities((SocialClass.Merchant, 75), (SocialClass.Noble, 65),
                (SocialClass.Landlord, 55), (SocialClass.Officer, 20)),
            FactionType.攘夷 => Affinities((SocialClass.Army, 95), (SocialClass.Noble, 35),
                (SocialClass.Peasant, 20), (SocialClass.Merchant, -35)),
            FactionType.绥靖 => Affinities((SocialClass.Officer, 80), (SocialClass.Merchant, 65),
                (SocialClass.Peasant, 25), (SocialClass.Army, -50)),
            FactionType.血脉 => Affinities((SocialClass.Noble, 100), (SocialClass.Officer, 20)),
            FactionType.僭主 => Affinities((SocialClass.Army, 90), (SocialClass.Labour, 30),
                (SocialClass.Noble, -35)),
            FactionType.神权 => Affinities((SocialClass.Peasant, 60), (SocialClass.Noble, 55),
                (SocialClass.Officer, 45), (SocialClass.Merchant, -30)),
            FactionType.共和 => Affinities((SocialClass.Merchant, 95), (SocialClass.Officer, 65),
                (SocialClass.Landlord, 35), (SocialClass.Noble, -45)),
            FactionType.民主 => Affinities((SocialClass.Merchant, 70), (SocialClass.Labour, 65),
                (SocialClass.Peasant, 55), (SocialClass.Noble, -55)),
            FactionType.革命 => Affinities((SocialClass.Labour, 100), (SocialClass.Peasant, 85),
                (SocialClass.Army, 30), (SocialClass.Noble, -100), (SocialClass.Landlord, -90),
                (SocialClass.Merchant, -45)),
            FactionType.融入 => Affinities((SocialClass.Merchant, 65), (SocialClass.Officer, 65),
                (SocialClass.Labour, 30)),
            FactionType.同化 => Affinities((SocialClass.Army, 55), (SocialClass.Officer, 50),
                (SocialClass.Noble, 30)),
            FactionType.共产 => Affinities((SocialClass.Labour, 100), (SocialClass.Peasant, 90),
                (SocialClass.Officer, 30), (SocialClass.Noble, -100), (SocialClass.Landlord, -100),
                (SocialClass.Merchant, -80)),
            FactionType.原始 => Affinities((SocialClass.Peasant, 45), (SocialClass.Noble, 20)),
            _ => new Dictionary<SocialClass, float>()
        };
        return profile.TryGetValue(socialClass, out float affinity) ? affinity : 0f;
    }

    private static Dictionary<SocialClass, float> Affinities(
        params (SocialClass socialClass, float affinity)[] values) =>
        values.ToDictionary(value => value.socialClass, value => value.affinity);

    private static float Clamp(float value, float minimum, float maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));
}
