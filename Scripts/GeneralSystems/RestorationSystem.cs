using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 复辟帝制(袁世凯洪宪帝制式)：本文化已废除君主制的现代共和国，大权独揽的元首可能图谋称帝——绝大多数以失败告终。
// 文化里君主制已被废除(见 RepublicSystem.FeatureAbolishMonarchy)，复辟通常只是一场闹剧；
// 只有护国起兵已平定、民间共和思想不超过一成五、正统未崩时，才有一丝站稳的可能(拿破仑三世式)：
// 成功则撤销本文化的"废除君主制"，共和国改回君主制。
//   · 条件：共和国；本文化已废君；元首在任满 MinTenureYears 年；执政理念偏保守/威权；
//           权力集中在元首(总统制或元首指定/党内推举，没有宪法的也算)；正统 ≥ 60；不在打仗；
//           满足后每年 YearlyChance 概率发起；失败过的国家 CooldownYears 年内不再发起。
//   · 筹备(PrepareYears 年)：组织筹安会、劝进，正统每年 -3；元首换人则作罢。
//   · 称帝：国号改为"某某帝国"、元首改称皇帝；正统 -25，各阶层民怨 +10；
//           各省按民间共和思想的比例宣布独立(护国运动，独立性强的、军府更积极)，与中央开战。
//   · 撤销帝制：称帝满一年且已有省份起兵、或共和思想超过三成，最迟 MaxReignYears 年，或称帝的元首身死，
//           被迫撤销帝制、恢复共和；正统再 -10。
// 每年由 ConstitutionalEconomySystem 的年度结算调用。
public static class RestorationSystem
{
    private const int MinTenureYears = 8;
    private const float YearlyChance = 0.03f;
    private const int PrepareYears = 2;
    private const int MaxReignYears = 3;
    private const int CooldownYears = 50;
    private const int MinMandate = 60;
    private const float SuccessRepublicanShare = 0.15f;
    private const int SuccessMinMandate = 30;
    private const float SuccessChance = 0.25f;

    public static bool IsProclaimed(Empire empire) =>
        empire?.data?.constitutional_economy?.restoration_stage == 2;

    // 称帝期间的国号与元首称号
    public static string ImperialName(Empire empire) =>
        string.Format(LM.Get("restoration_empire_name"), empire.GetEmpireName());

    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null || empire.isRekt() || empire.IsArchived()) return;
        Actor head = empire.Emperor ?? empire.CoreKingdom.king;
        TrackTenure(state, head);
        switch (state.restoration_stage)
        {
            case 0:
                if (CanAttempt(empire, state, head) && UnityEngine.Random.value < YearlyChance) Prepare(empire, state, head);
                break;
            case 1:
                UpdatePreparation(empire, state, head);
                break;
            case 2:
                UpdateReign(empire, state, head);
                break;
        }
    }

    private static void TrackTenure(ConstitutionalEconomyState state, Actor head)
    {
        long id = head == null || head.isRekt() ? -1L : head.id;
        if (state.tenure_head_id == id) return;
        state.tenure_head_id = id;
        state.tenure_since = World.world.getCurWorldTime();
    }

    private static bool CanAttempt(Empire empire, ConstitutionalEconomyState state, Actor head)
    {
        if (head == null || head.isRekt() || !state.is_republic || RepublicSystem.IsTransitioning(empire)) return false;
        if (state.restoration_failed_at > 0d && Date.getYearsSince(state.restoration_failed_at) < CooldownYears)
            return false;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (InstitutionSystem.GetFeature(culture, RepublicSystem.FeatureAbolishMonarchy) <= 0f) return false;
        if (state.tenure_since < 0d || Date.getYearsSince(state.tenure_since) < MinTenureYears) return false;
        if (empire.Mandate < MinMandate) return false;
        if (empire.CoreKingdom.getWars().Any(war => war != null && !war.hasEnded())) return false;
        PartyIdeology ideology = IdeologyFamilies.StateIdeology(empire);
        if (!IdeologyFamilies.IsTraditional(ideology) && !IdeologyFamilies.IsAuthoritarian(ideology) &&
            ideology != PartyIdeology.ConservativeLiberalism) return false;
        ConstitutionClauses clauses = ConstitutionSystem.GetClauses(empire);
        return clauses == null || clauses.power_center == ConstitutionPowerCenter.Presidential ||
               clauses.head_selection is ConstitutionHeadSelection.Designated or ConstitutionHeadSelection.PartyNomination;
    }

    private static void Prepare(Empire empire, ConstitutionalEconomyState state, Actor head)
    {
        state.restoration_stage = 1;
        state.restoration_started = World.world.getCurWorldTime();
        state.restoration_head_id = head.id;
        EventRecorder.Record(empire, string.Format(LM.Get("restoration_prepare_history"),
            empire.GetEmpireFullName(), head.getName()), head);
    }

    private static void UpdatePreparation(Empire empire, ConstitutionalEconomyState state, Actor head)
    {
        if (head == null || head.isRekt() || head.id != state.restoration_head_id)
        {
            // 发起的元首不在了，筹备作罢
            Reset(state, cooldown: false);
            EventRecorder.Record(empire, string.Format(LM.Get("restoration_abandoned_history"),
                empire.GetEmpireFullName()));
            return;
        }
        empire.AddMandate(-3);
        if (Date.getYearsSince(state.restoration_started) >= PrepareYears) Proclaim(empire, state, head);
    }

    private static void Proclaim(Empire empire, ConstitutionalEconomyState state, Actor head)
    {
        string republicName = empire.GetEmpireFullName();
        state.restoration_stage = 2;
        state.restoration_started = World.world.getCurWorldTime();
        empire.AddMandate(-25);
        AdjustGrievances(empire, 10f);
        EventRecorder.Record(empire, string.Format(LM.Get("restoration_proclaim_history"),
            head.getName(), republicName, ImperialName(empire)), head);

        // 护国运动：各省按共和思想的比例宣布独立，独立性强的、军府更积极
        float republican = RepublicanShare(empire);
        Kingdom core = empire.CoreKingdom;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member == null || member.isRekt() || member == core || !member.hasKing() ||
                member.IsFactionRebelling() || member.IsLocalRebelling()) continue;
            bool autonomous = member.GetKingdomType() == Regimes.KingdomType.LvLing_jiedushi ||
                              member.GetRegime()?.IsAllowArmy() == true;
            float chance = Mathf.Min(0.9f, (0.3f + republican) * (autonomous ? 1.5f : 1f));
            if (UnityEngine.Random.value >= chance) continue;
            empire.leave(member);
            War war = DiplomacyHelpers.wars.newWar(member, core, WarTypeLibrary.normal);
            war?.SetEmpireWarType(EmpireWarType.地方独立);
            EventRecorder.Record(empire, actor: member.king, logKingdom: member, text: string.Format(
                LM.Get("restoration_uprising_history"), member.GetKingdomFullName(), ImperialName(empire)));
        }
    }

    private static void UpdateReign(Empire empire, ConstitutionalEconomyState state, Actor head)
    {
        empire.AddMandate(-5);
        float years = Date.getYearsSince(state.restoration_started);
        bool headGone = head == null || head.isRekt() || head.id != state.restoration_head_id;
        bool uprising = empire.CoreKingdom.getWars().Any(war => war != null && !war.hasEnded() &&
                                                                war.GetEmpireWarType() == EmpireWarType.地方独立);
        float republican = RepublicanShare(empire);
        // 一丝成功的可能(拿破仑三世式)：称帝满一年，护国起兵已平定，民间共和思想不超过一成五，
        // 正统未崩，每年 SuccessChance 的概率帝制站稳脚跟
        if (!headGone && years >= 1 && !uprising && republican <= SuccessRepublicanShare &&
            empire.Mandate >= SuccessMinMandate && UnityEngine.Random.value < SuccessChance)
        {
            Succeed(empire, state, head);
            return;
        }
        if (!headGone && years < MaxReignYears && (years < 1 || !uprising && republican <= 0.3f)) return;
        string imperialName = ImperialName(empire);
        Reset(state, cooldown: true);
        empire.AddMandate(-10);
        EventRecorder.Record(empire, string.Format(LM.Get(headGone ? "restoration_end_death_history" : "restoration_end_history"),
            imperialName, empire.GetEmpireFullName()));
    }

    // 帝制站稳：撤销本文化的"废除君主制"，共和国改回君主制，称帝的元首正式登基
    private static void Succeed(Empire empire, ConstitutionalEconomyState state, Actor head)
    {
        string imperialName = ImperialName(empire);
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        Reset(state, cooldown: false);
        InstitutionSystem.RevokeAbolishMonarchy(culture);
        RepublicSystem.RestoreByStrongman(empire, head);
        empire.AddMandate(10);
        EventRecorder.Record(empire, string.Format(LM.Get("restoration_success_history"), imperialName,
            head.getName(), culture.GetCultureTranslate(), empire.GetEmpireFullName()), head);
    }

    private static void Reset(ConstitutionalEconomyState state, bool cooldown)
    {
        state.restoration_stage = 0;
        state.restoration_started = -1d;
        state.restoration_head_id = -1L;
        if (cooldown) state.restoration_failed_at = World.world.getCurWorldTime();
    }

    // 民间的共和思想：保守、威权、保守自由主义、中间派以外的理念信众占比
    private static float RepublicanShare(Empire empire)
    {
        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
        int total = counts.Values.Sum();
        if (total == 0) return 0f;
        int republican = counts.Where(pair => !IdeologyFamilies.IsTraditional(pair.Key) &&
                                              !IdeologyFamilies.IsAuthoritarian(pair.Key) &&
                                              pair.Key is not (PartyIdeology.ConservativeLiberalism or PartyIdeology.Centrism))
            .Sum(pair => pair.Value);
        return (float)republican / total;
    }

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
