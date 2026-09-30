using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 民意与理念压力(参考文明五"美丽新世界"的意识形态压力与民意)。
//
// 理念压力：执政理念不同的外国会向本国输出思潮。国力越强、科技时代越领先，压力越大；
//   只沿着接触传播(领土接壤或正在交战，交战时宣传的说服力减半)。压力会让本国百姓更快倒向该理念。
// 民意：每年对照"百姓各自信奉的理念"和"执政党的理念"：
//   · 信奉的理念跟执政理念相近(光谱距离 ≤45)的算支持；
//   · 相距很远(>90)的算异见，中间的算半个；
//   · 外来压力、阶层怨气会继续推高异见。
//   异见越多民意越差：满意 → 异见 → 抵制 → 革命浪潮。
// 后果：抵制起正统值每年下降、倾向另一理念的阶层怨气上升、城市忠诚下降；
//   革命浪潮持续两年就爆发革命——君主国走建立共和(和平退位或革命战争)，
//   一党制要么被军方支持的新党夺权、要么垮台恢复多党选举，多党制提前大选。
// 只在开放党禁(有政党政治)的国家生效；关闭科技树不影响这里，时代差按 0 计。
public static class PublicOpinionSystem
{
    public const int Content = 0;
    public const int Dissidents = 1;
    public const int CivilResistance = 2;
    public const int RevolutionaryWave = 3;

    private const float SupportDistance = 45f;
    private const float FarDistance = 90f;
    private const float BasePressure = 10f;
    private const int WaveYearsToRevolt = 2;

    public static int GetLevel(Empire empire) => State(empire)?.opinion_level ?? Content;

    public static string GetLevelName(int level) => LM.Get($"public_opinion_level_{Mathf.Clamp(level, 0, 3)}");

    public static bool TryGetPreferred(Empire empire, out PartyIdeology ideology)
    {
        ideology = default;
        string value = State(empire)?.opinion_preferred;
        return !string.IsNullOrEmpty(value) && Enum.TryParse(value, out ideology);
    }

    public static float GetPressure(Empire empire, PartyIdeology ideology) =>
        State(empire)?.ideology_pressure?.Where(source => source.ideology == ideology).Sum(source => source.amount) ?? 0f;

    // 给人口转化用：按压力大小随机挑一个外来理念；没有压力返回 null
    public static PartyIdeology? PickPressured(Empire empire)
    {
        List<IdeologyPressureSource> sources = State(empire)?.ideology_pressure;
        if (sources == null || sources.Count == 0) return null;
        float total = sources.Sum(source => source.amount);
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (IdeologyPressureSource source in sources)
            if ((roll -= source.amount) <= 0f) return source.ideology;
        return sources[sources.Count - 1].ideology;
    }

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    private static PartyIdeology? GetGoverning(Empire empire)
    {
        FixedFaction party = PartySystem.GetGovernmentParty(empire);
        if (party != null) return party.Ideology;
        return RepublicSystem.IsRepublic(empire) ? State(empire)?.republic_ideology : null;
    }

    #region 年度结算

    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null || World.world == null) return;
        PartyIdeology? governingValue = GetGoverning(empire);
        if (!PartySystem.IsActive(empire) || !governingValue.HasValue)
        {
            Reset(state);
            return;
        }
        PartyIdeology governing = governingValue.Value;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);

        state.ideology_pressure = ComputePressure(empire, governing, culture);

        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
        int total = counts.Values.Sum();
        if (total == 0)
        {
            Reset(state);
            return;
        }
        float support = 0f, far = 0f, middle = 0f;
        foreach (KeyValuePair<PartyIdeology, int> pair in counts)
        {
            float share = (float)pair.Value / total;
            float distance = PartySystem.IdeologyDistance(pair.Key, governing);
            if (distance <= SupportDistance) support += share;
            else if (distance > FarDistance) far += share;
            else middle += share;
        }
        float hostilePressure = state.ideology_pressure
            .Where(source => PartySystem.IdeologyDistance(source.ideology, governing) > SupportDistance)
            .Sum(source => source.amount);
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        float grievance = grievances.Count == 0 ? 0f : grievances.Values.Average() / 100f;
        float dissent = Mathf.Clamp01(far + middle * 0.5f + Mathf.Min(0.25f, hostilePressure / 200f) +
                                      grievance * 0.15f);
        state.opinion_support = support;
        state.opinion_dissent = dissent;

        PartyIdeology? preferred = PickPreferred(counts, total, state.ideology_pressure, governing, culture);
        state.opinion_preferred = preferred?.ToString() ?? "";

        int previous = state.opinion_level;
        int level = dissent < 0.3f ? Content : dissent < 0.45f ? Dissidents : dissent < 0.6f ? CivilResistance
            : preferred.HasValue ? RevolutionaryWave : CivilResistance;
        state.opinion_level = level;
        if (level > previous && level >= CivilResistance)
            Announce(empire, string.Format(LM.Get($"public_opinion_log_{level}"), empire.GetEmpireName(),
                preferred.HasValue ? PartySystem.GetIdeologyName(preferred.Value) : ""));

        ApplyConsequences(empire, state, level, preferred);

        state.opinion_wave_years = level == RevolutionaryWave ? state.opinion_wave_years + 1 : 0;
        if (state.opinion_wave_years >= WaveYearsToRevolt && preferred.HasValue)
        {
            state.opinion_wave_years = 0;
            Revolt(empire, state, preferred.Value);
        }
    }

    private static void Reset(ConstitutionalEconomyState state)
    {
        state.opinion_level = Content;
        state.opinion_wave_years = 0;
        state.opinion_support = 0f;
        state.opinion_dissent = 0f;
        state.opinion_preferred = "";
        state.ideology_pressure?.Clear();
    }

    // 外国压力：只算执政理念与本国不同、且跟本国接壤或交战的帝国
    private static List<IdeologyPressureSource> ComputePressure(Empire empire, PartyIdeology governing, string culture)
    {
        var result = new List<IdeologyPressureSource>();
        var borders = new HashSet<Empire>();
        foreach (Kingdom kingdom in empire.kingdoms_hashset.Where(k => k != null && !k.isRekt()))
        foreach (City city in kingdom.cities.Where(c => c != null && !c.isRekt()))
        foreach (City neighbour in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
        {
            Empire other = neighbour?.kingdom?.GetEmpire();
            if (other != null && other != empire && !other.isRekt() && !other.IsArchived()) borders.Add(other);
        }
        double ownPower = Math.Max(1d, empire.CoreKingdom.GetNationalPower());
        int ownEra = TechnologySystem.GetEraTier(culture);
        foreach (Empire source in borders)
        {
            if (source.CoreKingdom == null || !PartySystem.IsActive(source)) continue;
            PartyIdeology? ideology = GetGoverning(source);
            if (!ideology.HasValue || ideology.Value == governing) continue;
            float ratio = Mathf.Clamp((float)(source.CoreKingdom.GetNationalPower() / ownPower), 0.25f, 4f);
            int eraGap = TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(source)) - ownEra;
            float amount = BasePressure * ratio * (1f + 0.25f * Math.Max(0, eraGap));
            if (empire.CoreKingdom.isEnemy(source.CoreKingdom)) amount *= 0.5f;
            result.Add(new IdeologyPressureSource
            {
                empire_id = source.getID(),
                empire_name = source.GetEmpireName(),
                ideology = ideology.Value,
                amount = amount
            });
        }
        return result.OrderByDescending(source => source.amount).ToList();
    }

    // 百姓最想要的理念：信众占比 + 外来压力；必须比执政理念的信众更多，且本文化有相应的技术基础
    private static PartyIdeology? PickPreferred(Dictionary<PartyIdeology, int> counts, int total,
        List<IdeologyPressureSource> pressure, PartyIdeology governing, string culture)
    {
        float governingShare = counts.TryGetValue(governing, out int own) ? (float)own / total : 0f;
        PartyIdeology? best = null;
        float bestScore = governingShare;
        foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>())
        {
            if (ideology == governing) continue;
            if (!TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology))) continue;
            float score = (counts.TryGetValue(ideology, out int count) ? (float)count / total : 0f) +
                          pressure.Where(source => source.ideology == ideology).Sum(source => source.amount) / 200f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = ideology;
        }
        return best;
    }

    private static void ApplyConsequences(Empire empire, ConstitutionalEconomyState state, int level,
        PartyIdeology? preferred)
    {
        if (level < CivilResistance) return;
        empire.AddMandate(level == RevolutionaryWave ? -2 : -1);
        // 不信任案：有议会、不是一党制，距上次大选满一年时，抵制 25% / 革命浪潮 50% 的概率内阁倒台、提前大选
        if (ParliamentSystem.HasParliament(empire) && !RepublicSystem.IsOneParty(empire) &&
            state.last_parliament_election >= 0d && Date.getYearsSince(state.last_parliament_election) >= 1 &&
            UnityEngine.Random.value < 0.25f * (level - 1))
        {
            state.last_parliament_election = -1d;
            Announce(empire, string.Format(LM.Get("public_opinion_no_confidence_log"), empire.GetEmpireName()));
        }
        if (!preferred.HasValue) return;
        InstitutionEmpireState institutions = empire.data.institution_state;
        if (institutions == null) return;
        institutions.class_grievances ??= new Dictionary<SocialClass, float>();
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
        {
            if (PartySystem.GetAffinity(preferred.Value, socialClass) < 30f) continue;
            institutions.class_grievances.TryGetValue(socialClass, out float current);
            institutions.class_grievances[socialClass] = Mathf.Min(100f, current + 3f * level);
        }
    }

    #endregion

    #region 革命

    private static void Revolt(Empire empire, ConstitutionalEconomyState state, PartyIdeology preferred)
    {
        FixedFaction party = PartySystem.GetParties(empire)
                                 .FirstOrDefault(candidate => candidate.Ideology == preferred && !candidate.Ban)
                             ?? PartySystem.PlayerFoundParty(empire, preferred);
        string ideologyName = PartySystem.GetIdeologyName(preferred);
        if (party == null)
        {
            // 民怨沸腾却没有组织起来的政党：只有骚乱，没有政权更迭
            Announce(empire, string.Format(LM.Get("public_opinion_riot_log"), empire.GetEmpireName(), ideologyName));
            return;
        }

        if (!RepublicSystem.IsRepublic(empire))
        {
            // 君主国：建立共和(够票就和平退位，不够就革命战争)；保守主义本身拥护君主，不走这条路
            if (preferred == PartyIdeology.Conservatism) return;
            Announce(empire, string.Format(LM.Get("public_opinion_revolution_log"), empire.GetEmpireName(),
                ideologyName));
            RepublicSystem.PushRepublic(empire, party);
            return;
        }

        bool authoritarian = PartySystem.GetPosition(preferred).y <= -50f;
        if (authoritarian && RepublicSystem.CanCoup(empire, party))
        {
            // 军方站在这一边：新党夺权，建立一党制(原先的一党制先解除，再由新党关闭党禁)
            if (RepublicSystem.IsOneParty(empire)) PartyBanSystem.Open(empire, "party_ban_reopened_core_history");
            Announce(empire, string.Format(LM.Get("public_opinion_seizure_log"), empire.GetEmpireName(),
                party.Name));
            PartyBanSystem.Close(empire, party, null, "party_ban_one_party_history");
            return;
        }
        if (RepublicSystem.IsOneParty(empire))
        {
            // 一党制垮台，恢复多党选举
            Announce(empire, string.Format(LM.Get("public_opinion_collapse_log"), empire.GetEmpireName()));
            PartyBanSystem.Open(empire, "party_ban_reopened_core_history");
            return;
        }
        // 多党制：提前大选，让选票说话
        state.last_parliament_election = -1d;
        Announce(empire, string.Format(LM.Get("public_opinion_election_log"), empire.GetEmpireName(), ideologyName));
    }

    // 玩家顺应民意：理念路线切到百姓想要的理念所在路线
    public static bool AcceptPreferred(Empire empire)
    {
        if (!TryGetPreferred(empire, out PartyIdeology preferred)) return false;
        return PartySystem.SwitchRoute(InstitutionSystem.GetPrimaryCulture(empire), PartySystem.RouteOf(preferred));
    }

    // 玩家下令提前大选(有议会、不是一党制时)
    public static bool CallSnapElection(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !ParliamentSystem.HasParliament(empire) || RepublicSystem.IsOneParty(empire)) return false;
        state.last_parliament_election = -1d;
        ParliamentSystem.Update(empire);
        return true;
    }

    private static void Announce(Empire empire, string text)
    {
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        EmpireCraft.Scripts.System.HistoryRecordSystem.RecordHistory(empire, directContent: text,
            kingdomId: empire.CoreKingdom?.id ?? -1L);
    }

    #endregion
}
