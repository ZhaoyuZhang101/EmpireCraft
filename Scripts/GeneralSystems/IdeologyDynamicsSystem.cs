using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 意识形态演变：政权再稳定，意识形态也不会一成不变。三条机制，每年由 ConstitutionalEconomySystem 调用：
//
// 一、意识形态疲劳(0~100)：立国理念宣传得越久越空洞(晚期苏联式的犬儒)。
//     每年 +3/+2/+1(意识形态强度高/中/低)；打仗时同仇敌忾 -5；颁布新宪法清零；新领导人的路线调整也会降低。
//     疲劳越高：国民教育越无力(见 IdeologyEducationSystem)，立国理念的信徒越容易改信别的理念(见 IdeologyPopulationSystem)，
//     民间异见越多(见 PublicOpinionSystem)。
//
// 二、领导人更替时的路线之争：新元首上台时按国情选路线——
//     · 改革派(经济衰退、民怨、疲劳越高越可能)：意识形态强度降一档，一部分立国理念信徒转向更温和的相邻理念，疲劳 -15；
//     · 保守派(民意动荡、外部思潮压力、战争越多越可能)：强度升一档，发起思想运动把一部分人拉回立国理念，疲劳 -30，但民怨 +5；
//     · 延续派：维持现状。
//     玩家在宪法页手定过意识形态强度的，路线之争不改强度(人口的转向照常)。
//
// 三、经济趋势驱动思潮：记录近十年的全国繁荣度，上升时自由主义更易传播，衰退时激进左右翼更易传播、民怨上升；
//     WarBox 的金融危机、国家违约直接算作严重衰退。
public static class IdeologyDynamicsSystem
{
    private const int HistoryYears = 11;
    private const int ProsperitySampleCities = 40;
    public const float GrowthThreshold = 0.05f;
    public const float DeclineThreshold = -0.05f;

    public static float GetFatigue(Empire empire) => empire?.data?.constitutional_economy?.ideology_fatigue ?? 0f;
    public static float GetTrend(Empire empire) => empire?.data?.constitutional_economy?.economic_trend ?? 0f;

    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null || empire.isRekt() || empire.IsArchived()) return;
        bool crisisStarted = UpdateEconomicTrend(empire, state);
        UpdateLiberationEnd(empire, state);
        ConstitutionData constitution = ConstitutionSystem.Get(empire);
        if (constitution == null)
        {
            state.ideology_fatigue = 0f;
            return;
        }
        UpdateFatigue(empire, state, constitution);
        UpdateLine(empire, state, constitution);
        UpdateSpeechPressure(empire, state, constitution);
        UpdateSuppression(empire, state, constitution, crisisStarted);
    }

    #region 思想解放期与压不住的思想

    // 思想解放期(五四、明治启蒙、1848 年民族之春、公开性)：言论改为宽松、颁布新宪法(革命、改制)、
    // 或被压抑的思想爆发时开启，持续 LiberationYears 年，效果随年数线性减弱。期间(见 IdeologyPopulationSystem)：
    //   · 人们不再一味随大流：本城多数派对个人的影响减半，改为按自身阶层自由选择；
    //   · 三十岁以下的年轻人接受新思想的概率最多 ×3；
    //   · 各种理念书籍的影响力与理念著作的出版量最多翻倍(见 SpeechFreedomSystem)。
    //
    // 压不住的思想(赫鲁晓夫解冻、1989、布拉格之春)：言论严格或意识形态强度高时，不信立国理念的人并没有被改变，
    // 只是不敢说——每年按他们的人口比例累积"被压抑的思想"(疲劳越高积累越快)；言论与强度都放松后每年消散一成。
    // 积累到 30 以上时，遇到领导人更替、金融危机、革命浪潮或改革派上台就一下子爆发：
    // 立国理念的表面信徒中最多三成改信被压制的理念或新思潮，并开启思想解放期。压得越久越狠，爆发越大。
    public const float LiberationYears = 18f;
    private const float BurstThreshold = 30f;
    private const float MaxBurstShare = 0.3f;

    // 解放期强度：刚开始为 1，LiberationYears 年后为 0
    public static float GetLiberation(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || state.liberation_started < 0d) return 0f;
        float years = Date.getYearsSince(state.liberation_started);
        return Mathf.Clamp01(1f - years / LiberationYears);
    }

    public static float GetSuppressed(Empire empire) => empire?.data?.constitutional_economy?.suppressed_thought ?? 0f;

    private static void StartLiberation(Empire empire, ConstitutionalEconomyState state, string reasonKey)
    {
        bool already = GetLiberation(empire) > 0f;
        state.liberation_started = World.world.getCurWorldTime();
        state.liberation_announced_end = false;
        if (already) return;
        EventRecorder.Record(empire, string.Format(LM.Get("liberation_start_history"), empire.GetEmpireFullName(),
            LM.Get(reasonKey)));
    }

    private static void UpdateLiberationEnd(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.liberation_announced_end || state.liberation_started < 0d || GetLiberation(empire) > 0f) return;
        state.liberation_announced_end = true;
        EventRecorder.Record(empire, string.Format(LM.Get("liberation_end_history"), empire.GetEmpireFullName()));
    }

    private static void UpdateSuppression(Empire empire, ConstitutionalEconomyState state, ConstitutionData constitution,
        bool crisisStarted)
    {
        ConstitutionSpeech speech = constitution.clauses.speech;
        // 言论改为宽松：思想解放
        if (!string.IsNullOrEmpty(state.last_speech) && state.last_speech != speech.ToString() &&
            speech == ConstitutionSpeech.Free)
            StartLiberation(empire, state, "liberation_reason_speech");
        state.last_speech = speech.ToString();

        bool strict = speech == ConstitutionSpeech.Strict;
        bool high = constitution.clauses.ideology_intensity == ConstitutionIdeologyIntensity.High;
        if (strict || high)
        {
            Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
            int total = counts.Values.Sum();
            float dissenters = total == 0 ? 0f
                : 1f - (counts.TryGetValue(constitution.clauses.founding_ideology, out int own) ? own : 0) / (float)total;
            float gain = dissenters * 10f * (strict && high ? 1.5f : 1f) * (1f + state.ideology_fatigue / 100f);
            state.suppressed_thought = Mathf.Clamp(state.suppressed_thought + gain, 0f, 100f);
        }
        else
        {
            state.suppressed_thought *= 0.9f;
        }
        if (crisisStarted) TryBurst(empire, state, constitution, "liberation_reason_crisis");
        else if (state.opinion_level >= PublicOpinionSystem.RevolutionaryWave)
            TryBurst(empire, state, constitution, "liberation_reason_wave");
    }

    // 被压抑的思想爆发；没积累到门槛时什么也不发生
    private static bool TryBurst(Empire empire, ConstitutionalEconomyState state, ConstitutionData constitution,
        string reasonKey)
    {
        if (state.suppressed_thought < BurstThreshold) return false;
        PartyIdeology founding = constitution.clauses.founding_ideology;
        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
        int total = counts.Values.Sum();
        float share = MaxBurstShare * Mathf.Clamp01(state.suppressed_thought / 100f);
        int changed = 0;
        foreach (City city in empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
                     .SelectMany(kingdom => kingdom.cities).Distinct().ToList())
        {
            if (city?.units == null || city.isRekt()) continue;
            string culture = CultureService.GetMainCulture(city);
            // 爆发允许产生科技基础已经成熟的新思潮，不局限于已经占有人口的旧派别。
            List<PartyIdeology> targets = IdeologyPopulationSystem.GetAvailableIdeologies(culture, empire, true)
                .Where(ideology => ideology != founding).ToList();
            if (targets.Count == 0) continue;
            Dictionary<PartyIdeology, int> local = IdeologyPopulationSystem.GetCityCounts(city);
            List<PartyIdeology> emerging = targets.Where(ideology => !local.ContainsKey(ideology)).ToList();
            List<Actor> conformists = city.units.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                    actor.isAdult() &&
                    !actor.isKing() && !actor.isCityLeader() && actor.GetFaction()?.IsParty != true &&
                    IdeologyPopulationSystem.Get(actor) == founding)
                .ToList();
            int quota = conformists.Count == 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(conformists.Count * share));
            foreach (Actor actor in conformists.OrderBy(_ => UnityEngine.Random.value).Take(quota))
            {
                // 优先给尚未出现的思想一个传播起点；之后按阶级亲和度选择，旧多数派最多加权一倍。
                PartyIdeology chosen = IdeologyPopulationSystem.WeightedChoice(
                    emerging.Count > 0 ? emerging : targets, actor, ideology =>
                        1f + (total > 0 && counts.TryGetValue(ideology, out int supporters) ? supporters / (float)total : 0f));
                emerging.Remove(chosen);
                IdeologyPopulationSystem.Set(actor, chosen);
                IdeologyPopulationSystem.RegisterIdea(culture, chosen);
                changed++;
            }
            if (quota > 0) LayerCityCache.Invalidate(city);
        }
        if (changed == 0) return false;
        state.suppressed_thought = 0f;
        EventRecorder.Record(empire, string.Format(LM.Get("thought_burst_history"), empire.GetEmpireFullName(),
            LM.Get(reasonKey), changed));
        StartLiberation(empire, state, reasonKey);
        return true;
    }

    #endregion

    #region 言论压力

    // 政府不会无缘无故改变言论自由，只会迫于压力。每年累积"言论压力"(正 = 要求放开，负 = 要求收紧)，
    // 越过 ±50 就改一档(宽松 ↔ 一般 ↔ 严格)并归零；没有压力时每年向 0 回落两成。
    //   放开的压力：示威(民众上街要求权利)、民怨深重、自由主义信众成为社会中坚、经济繁荣(中产阶层要求发言权)、
    //              民间有异见但尚未失控(当局以让步换稳定)、改革派上台；
    //   收紧的压力：革命浪潮(当局镇压)、全国总罢工、战争、金融危机、外国思潮大举渗透、保守派上台。
    // 已经是最宽松/最严格时，同方向的压力不再累积。玩家在宪法页手定过言论自由的不改。
    private const float SpeechThreshold = 50f;

    private static void UpdateSpeechPressure(Empire empire, ConstitutionalEconomyState state, ConstitutionData constitution)
    {
        if (constitution.provisional || ConstitutionSystem.IsPlayerLocked(empire, ConstitutionSystem.ClauseSpeech))
        {
            state.speech_pressure = 0f;
            return;
        }
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        float grievance = grievances.Count == 0 ? 0f : grievances.Values.Average() / 100f;
        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
        int total = counts.Values.Sum();
        float liberalShare = total == 0 ? 0f
            : counts.Where(pair => IdeologyFamilies.IsLiberal(pair.Key)).Sum(pair => pair.Value) / (float)total;
        float hostile = state.ideology_pressure?.Sum(source => source.amount) ?? 0f;
        bool atWar = empire.CoreKingdom.getWars().Any(war => war != null && !war.hasEnded());

        float loosen = state.demonstration_share * 40f +
                       (grievance > 0.5f ? (grievance - 0.5f) * 30f : 0f) +
                       Mathf.Max(0f, liberalShare - 0.35f) * 60f +
                       (state.economic_trend >= GrowthThreshold ? 5f : 0f) +
                       (state.opinion_level == PublicOpinionSystem.Dissidents ? 6f : 0f);
        float tighten = (state.opinion_level >= PublicOpinionSystem.RevolutionaryWave ? 20f : 0f) +
                        (state.general_strike ? 15f : 0f) +
                        (atWar ? 6f : 0f) +
                        (state.economic_crisis ? 10f : 0f) +
                        Mathf.Min(15f, Mathf.Max(0f, hostile - 40f) / 4f);
        float delta = loosen - tighten;
        ConstitutionSpeech speech = constitution.clauses.speech;
        if (speech == ConstitutionSpeech.Free && delta > 0f || speech == ConstitutionSpeech.Strict && delta < 0f) delta = 0f;
        float pressure = state.speech_pressure * 0.8f + delta;
        state.speech_pressure = Mathf.Clamp(pressure, -100f, 100f);
        if (Mathf.Abs(state.speech_pressure) < SpeechThreshold) return;

        bool looser = state.speech_pressure > 0f;
        ConstitutionSpeech next = looser
            ? speech == ConstitutionSpeech.Strict ? ConstitutionSpeech.Limited : ConstitutionSpeech.Free
            : speech == ConstitutionSpeech.Free ? ConstitutionSpeech.Limited : ConstitutionSpeech.Strict;
        state.speech_pressure = 0f;
        if (!ConstitutionSystem.SetSpeechByPressure(empire, next)) return;
        EventRecorder.Record(empire, string.Format(LM.Get(looser ? "speech_loosened_history" : "speech_tightened_history"),
            empire.GetEmpireFullName(), ConstitutionSystem.ValueText(ConstitutionSystem.ClauseSpeech, next.ToString())));
    }

    #endregion

    #region 经济趋势

    // 返回今年是否新爆发了金融危机(压不住的思想的触发条件之一)
    private static bool UpdateEconomicTrend(Empire empire, ConstitutionalEconomyState state)
    {
        List<City> cities = empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.cities).Where(city => city != null && !city.isRekt()).ToList();
        if (cities.Count == 0) return false;
        float prosperity = cities.OrderBy(_ => UnityEngine.Random.value).Take(ProsperitySampleCities)
            .Average(IdeologyPopulationSystem.CityProsperity);
        state.prosperity_history ??= new List<float>();
        state.prosperity_history.Add(prosperity);
        while (state.prosperity_history.Count > HistoryYears) state.prosperity_history.RemoveAt(0);
        float trend = state.prosperity_history.Count < 2 ? 0f : prosperity - state.prosperity_history[0];
        // WarBox：金融危机、国家违约 = 严重衰退；高失业同样拖累
        bool crisis = Compatibility.WarBoxCompatibility.TryGetFinance(empire.CoreKingdom, out bool inCrisis,
            out float unemployment) && inCrisis;
        if (crisis) trend = Mathf.Min(trend, -0.15f);
        else if (unemployment > 0.15f) trend = Mathf.Min(trend, -0.05f - (unemployment - 0.15f));
        bool started = crisis && !state.economic_crisis;
        if (started)
            EventRecorder.Record(empire, string.Format(LM.Get("ideology_crisis_history"), empire.GetEmpireFullName()));
        state.economic_crisis = crisis;
        state.economic_trend = trend;
        // 衰退积累民怨，繁荣消解民怨
        float delta = trend <= DeclineThreshold ? 2f : trend >= GrowthThreshold ? -1f : 0f;
        if (delta != 0f) AdjustGrievances(empire, delta);
        return started;
    }

    #endregion

    #region 意识形态疲劳

    private static void UpdateFatigue(Empire empire, ConstitutionalEconomyState state, ConstitutionData constitution)
    {
        if (state.ideology_constitution_number != constitution.number)
        {
            // 新宪法、新国体：一切重新开始；不是第一部宪法(或旧存档首次记录)的，开启思想解放期
            bool replaced = state.ideology_constitution_number > 0;
            state.ideology_constitution_number = constitution.number;
            state.ideology_fatigue = 0f;
            if (replaced) StartLiberation(empire, state, "liberation_reason_constitution");
            return;
        }
        float growth = constitution.clauses.ideology_intensity switch
        {
            ConstitutionIdeologyIntensity.High => 3f,
            ConstitutionIdeologyIntensity.Low => 1f,
            _ => 2f
        };
        bool atWar = empire.kingdoms_list.Any(kingdom => kingdom != null && !kingdom.isRekt() &&
                                                         kingdom.getWars().Any(war => war != null && !war.hasEnded()));
        if (atWar) growth -= 5f;
        // 言论自由：宽松时思想交锋让理念保持活力，严格时宣传显得空洞
        growth += SpeechFreedomSystem.FatigueModifier(empire);
        state.ideology_fatigue = Mathf.Clamp(state.ideology_fatigue + growth, 0f, 100f);
    }

    #endregion

    #region 路线之争

    private enum Line { Reform, Hardline, Continuity }

    private static void UpdateLine(Empire empire, ConstitutionalEconomyState state, ConstitutionData constitution)
    {
        Actor head = empire.Emperor ?? empire.CoreKingdom.king;
        if (head == null || head.isRekt()) return;
        if (state.ideology_line_head_id == head.id) return;
        bool first = state.ideology_line_head_id < 0;
        state.ideology_line_head_id = head.id;
        if (first || constitution.provisional) return;

        // 领导人更替：被压抑的思想趁机爆发；压不住了，新领导层只能走改革路线
        bool burst = TryBurst(empire, state, constitution, "liberation_reason_succession");
        Line line = burst ? Line.Reform : ChooseLine(empire, state);
        state.ideology_line = line.ToString();
        if (line == Line.Continuity) return;
        PartyIdeology founding = constitution.clauses.founding_ideology;
        ConstitutionIdeologyIntensity intensity = constitution.clauses.ideology_intensity;
        if (line == Line.Reform)
        {
            ConstitutionIdeologyIntensity lower = intensity == ConstitutionIdeologyIntensity.High
                ? ConstitutionIdeologyIntensity.Medium
                : ConstitutionIdeologyIntensity.Low;
            ConstitutionSystem.SetIdeologyIntensityByLine(empire, lower);
            PartyIdeology? moderate = ModerateNeighbour(empire, founding);
            if (moderate.HasValue) Convert(empire, founding, moderate.Value, 0.05f);
            state.ideology_fatigue = Mathf.Max(0f, state.ideology_fatigue - 15f);
            state.speech_pressure = Mathf.Clamp(state.speech_pressure + 30f, -100f, 100f);
            EventRecorder.Record(empire, string.Format(LM.Get("ideology_line_reform_history"),
                empire.GetEmpireFullName(), head.getName(),
                moderate.HasValue ? PartySystem.GetIdeologyName(moderate.Value) : LM.Get("label_none")));
        }
        else
        {
            ConstitutionIdeologyIntensity higher = intensity == ConstitutionIdeologyIntensity.Low
                ? ConstitutionIdeologyIntensity.Medium
                : ConstitutionIdeologyIntensity.High;
            ConstitutionSystem.SetIdeologyIntensityByLine(empire, higher);
            Convert(empire, null, founding, 0.03f);
            state.ideology_fatigue = Mathf.Max(0f, state.ideology_fatigue - 30f);
            state.speech_pressure = Mathf.Clamp(state.speech_pressure - 30f, -100f, 100f);
            AdjustGrievances(empire, 5f);
            EventRecorder.Record(empire, string.Format(LM.Get("ideology_line_hardline_history"),
                empire.GetEmpireFullName(), head.getName(), PartySystem.GetIdeologyName(founding)));
        }
    }

    private static Line ChooseLine(Empire empire, ConstitutionalEconomyState state)
    {
        IReadOnlyDictionary<SocialClass, float> grievances = InstitutionSystem.GetClassGrievances(empire);
        float grievance = grievances.Count == 0 ? 0f : grievances.Values.Average() / 100f;
        float reform = 1f + grievance * 2f + Mathf.Max(0f, -state.economic_trend) * 5f +
                       state.ideology_fatigue / 50f + (state.economic_crisis ? 1f : 0f);
        float hostile = state.ideology_pressure?.Sum(source => source.amount) ?? 0f;
        bool atWar = empire.CoreKingdom.getWars().Any(war => war != null && !war.hasEnded());
        float hardline = 1f + (state.opinion_level >= 2 ? 1f : 0f) + Mathf.Min(1f, hostile / 100f) +
                         (atWar ? 0.5f : 0f);
        const float continuity = 1.5f;
        float roll = UnityEngine.Random.value * (reform + hardline + continuity);
        if (roll < reform) return Line.Reform;
        return roll < reform + hardline ? Line.Hardline : Line.Continuity;
    }

    // 比立国理念更温和(离中间派更近)、又与它最接近的已解锁理念，如共产主义 → 社会主义
    private static PartyIdeology? ModerateNeighbour(Empire empire, PartyIdeology founding)
    {
        float own = PartySystem.IdeologyDistance(founding, PartyIdeology.Centrism);
        if (own <= 0f) return null;
        return PartySystem.UnlockedIdeologies(empire)
            .Where(ideology => ideology != founding &&
                               PartySystem.IdeologyDistance(ideology, PartyIdeology.Centrism) < own)
            .OrderBy(ideology => PartySystem.IdeologyDistance(ideology, founding))
            .Cast<PartyIdeology?>().FirstOrDefault();
    }

    // 每座城把一部分居民(from 为空时为所有非目标理念者)转为 to；党员与统治者不在此列
    private static void Convert(Empire empire, PartyIdeology? from, PartyIdeology to, float share)
    {
        foreach (City city in empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
                     .SelectMany(kingdom => kingdom.cities).ToList())
        {
            if (city?.units == null || city.isRekt()) continue;
            List<Actor> candidates = city.units.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                    !actor.isKing() && !actor.isCityLeader() && actor.GetFaction()?.IsParty != true &&
                    IdeologyPopulationSystem.Get(actor) != to &&
                    (!from.HasValue || IdeologyPopulationSystem.Get(actor) == from.Value))
                .ToList();
            int quota = Mathf.RoundToInt(candidates.Count * share);
            if (quota <= 0) continue;
            foreach (Actor actor in candidates.OrderBy(_ => UnityEngine.Random.value).Take(quota))
                IdeologyPopulationSystem.Set(actor, to);
            LayerCityCache.Invalidate(city);
        }
    }

    #endregion

    private static void AdjustGrievances(Empire empire, float delta)
    {
        InstitutionEmpireState institutions = empire.data?.institution_state;
        if (institutions == null) return;
        institutions.class_grievances ??= new Dictionary<SocialClass, float>();
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
        {
            institutions.class_grievances.TryGetValue(socialClass, out float current);
            institutions.class_grievances[socialClass] = Mathf.Clamp(current + delta, 0f, 100f);
        }
    }
}
