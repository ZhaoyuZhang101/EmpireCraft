using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 工会与工潮(用户 2026-11-24 第四批里的"工会与福利")。以前"罢工"只有装了 WarBox 才有。现在工业时代起：
//   · 工会声势(0~100)逐年向目标靠拢：劳工在人口里占得多、言论宽松、有合法的左翼政党(在台上更甚)、工会合法、
//     市面萧条都让它涨；取缔工会、言论严管、劳动立法与工钱已经谈下来、福利发得出都让它落，统制经济下工会官办、落得更多；
//   · 工潮：声势过三成五，劳工怨气大、萧条或物价飞涨时每年有机会闹起罢工潮(工厂减产一成五)；
//     声势过六成五且怨气极深即总罢工(减产四成，也是革命形势的一颗火星，见 RevolutionSystem.FindSpark)；
//   · 收场(次年见分晓；国情窗口里可以当年就亲自定)：让步——涨一档工钱(至多三档，满了改立一档劳动法)，劳工怨气大减、
//     商人不满；弹压——工会声势大跌、劳工怨气更深、革命形势陡增，总罢工时有机会酿成血案；自由派当局不插手，
//     劳资自己谈，一半谈成一半拖散；
//   · 工钱与劳动法是长久的：劳工怨气低、内需旺(消费品好卖)，工厂产值每档少一点。
// 诉求"劳动立法""承认工会""取缔工会"改的也是这里的状态。数值都是模组自己的规则。
public static class LabourSystem
{
    public const int NoStrike = 0, StrikeWave = 1, GeneralStrike = 2;
    public const int Banned = -1, Tolerated = 0, Legal = 1;
    public const int MaxLabourLaw = 2, MaxWage = 3;
    public const float StrikeAt = 35f, GeneralAt = 65f;
    private const float MinLabourShare = 0.04f, Approach = 0.15f;
    private const float StrikeChanceScale = 0.5f, GeneralStrikeShare = 0.3f, StrikeWaveShare = 0.12f;
    private const float MassacreChance = 0.25f;
    public const string Concede = "concede", Repress = "repress", Fade = "fade";

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;
    private static ConstitutionalEconomyState State(Kingdom kingdom) => kingdom?.GetEmpire()?.data?.constitutional_economy;

    public static bool Applies(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived() || State(empire) == null) return false;
        if (TransmigrantSystem.Governs(empire)) return false;
        return TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) >= EconomicCycleSystem.IndustrialEra;
    }

    #region 年度

    // 每年(ConstitutionalEconomySystem，在 WarBox 罢工统计之后、苛政与革命形势之前)
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        float labour = 0f;
        bool applies = Applies(empire) &&
                       InstitutionSystem.BuildClassShares(empire).TryGetValue(SocialClass.Labour, out labour) && labour >= MinLabourShare;
        if (!applies)
        {
            state.union_strength = Mathf.Max(0f, state.union_strength - 5f);
            state.strike_stage = NoStrike;
            return;
        }
        state.union_strength += (Target(empire, state, labour) - state.union_strength) * Approach;
        state.union_strength = Mathf.Clamp(state.union_strength, 0f, 100f);
        if (state.strike_stage > NoStrike)
        {
            // 去年闹起来的工潮今年见分晓
            Resolve(empire, state, AiResponse(empire, state), false);
            return;
        }
        TryStrike(empire, state);
    }

    private static float Target(Empire empire, ConstitutionalEconomyState state, float labour)
    {
        float target = Mathf.Min(45f, labour * 200f);
        target += ConstitutionSystem.GetSpeech(empire) switch
        {
            ConstitutionSpeech.Free => 15f,
            ConstitutionSpeech.Strict => -15f,
            _ => 0f
        };
        if (PartySystem.GetParties(empire).Any(party => party != null && party.IsParty && !party.Ban &&
                                                        IdeologyFamilies.IsLabourMovement(party.Ideology))) target += 10f;
        if (PartySystem.GetGovernmentParty(empire) is { } governing && IdeologyFamilies.IsLabourMovement(governing.Ideology)) target += 10f;
        target += state.union_status switch
        {
            Legal => 20f,
            Banned => -30f,
            _ => 0f
        };
        if (EconomicCycleSystem.InCrisis(empire)) target += 10f;
        // 统制经济下工会是官办的，闹不起来
        if (EconomicCycleSystem.Planned(empire)) target -= 30f;
        target -= state.labour_law * 4f + state.wage_level * 3f;
        if (state.welfare_funded) target -= state.welfare_level * 3f;
        return Mathf.Clamp(target, 0f, 100f);
    }

    private static float Grievance(Empire empire) =>
        InstitutionSystem.GetClassGrievances(empire).TryGetValue(SocialClass.Labour, out float value) ? value : 0f;

    private static void TryStrike(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.union_strength < StrikeAt) return;
        float grievance = Grievance(empire);
        bool crisis = EconomicCycleSystem.InCrisis(empire);
        if (grievance < 45f && !crisis && CommodityMarketSystem.PriceLevel(empire.CoreKingdom) < 1.3f) return;
        float chance = (state.union_strength - 25f) / 100f * StrikeChanceScale * (crisis ? 1.5f : 1f);
        // 刚闹完一场，各方都要喘口气；福利发得出，日子过得去
        if (state.strike_resolved_at >= 0d && Date.getYearsSince(state.strike_resolved_at) < 2) chance *= 0.3f;
        if (state.welfare_funded) chance *= 1f - 0.12f * state.welfare_level;
        if (UnityEngine.Random.value >= chance) return;
        bool general = state.union_strength >= GeneralAt && grievance >= 65f;
        state.strike_stage = general ? GeneralStrike : StrikeWave;
        state.strike_since = World.world.getCurWorldTime();
        state.strike_response_by_player = false;
        MarkStrike(state);
        string text = string.Format(LM.Get(general ? "labour_general_strike_history" : "labour_strike_wave_history"), empire.GetEmpireFullName());
        if (general) EventRecorder.Record(empire, text);
        else EventRecorder.RecordRoutine(empire, text);
    }

    // 民意、革命形势读的那两个数(见 PublicOpinionSystem、RevolutionSystem.FindSpark)
    private static void MarkStrike(ConstitutionalEconomyState state)
    {
        bool general = state.strike_stage >= GeneralStrike;
        state.strike_share = Mathf.Max(state.strike_share, general ? GeneralStrikeShare : StrikeWaveShare);
        if (general) state.general_strike = true;
    }

    private static string AiResponse(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.union_status == Banned) return Repress;
        PartyIdeology ideology = IdeologyFamilies.StateIdeology(empire);
        if (IdeologyFamilies.IsLeft(ideology) || ideology is PartyIdeology.SocialLiberalism or PartyIdeology.Centrism or
                PartyIdeology.ReligiousDemocracy) return Concede;
        if (IdeologyFamilies.IsAuthoritarian(ideology) || ideology == PartyIdeology.Conservatism) return Repress;
        // 自由派当局不插手：劳资自己谈
        return UnityEngine.Random.value < 0.5f ? Concede : Fade;
    }

    private static void Resolve(Empire empire, ConstitutionalEconomyState state, string kind, bool byPlayer)
    {
        bool general = state.strike_stage >= GeneralStrike;
        string name = empire.GetEmpireFullName();
        string text;
        switch (kind)
        {
            case Concede:
                bool wage = state.wage_level < MaxWage;
                if (wage) state.wage_level++;
                else if (state.labour_law < MaxLabourLaw) state.labour_law++;
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, -15f);
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 6f);
                state.union_strength = Mathf.Min(100f, state.union_strength + 5f);
                PartyFinanceSystem.Adjust(empire, "approval_strike", 2f);
                text = string.Format(LM.Get(wage ? "labour_strike_conceded_wage_history" : "labour_strike_conceded_law_history"), name);
                break;
            case Repress:
                state.union_strength = Mathf.Max(0f, state.union_strength - 15f);
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 10f);
                state.repression_years++;
                RevolutionSystem.Shock(empire, general ? 10f : 5f);
                PartyFinanceSystem.Adjust(empire, "approval_strike", -3f);
                bool massacre = general && UnityEngine.Random.value < MassacreChance;
                if (massacre) state.revolution_massacre_at = World.world.getCurWorldTime();
                text = string.Format(LM.Get(massacre ? "labour_strike_massacre_history" : "labour_strike_repressed_history"), name);
                break;
            default:
                kind = Fade;
                state.union_strength = Mathf.Max(0f, state.union_strength - 5f);
                ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 3f);
                text = string.Format(LM.Get("labour_strike_faded_history"), name);
                break;
        }
        state.strike_stage = NoStrike;
        state.strike_outcome = kind;
        state.strike_resolved_at = World.world.getCurWorldTime();
        state.strike_response_by_player = byPlayer;
        if (general) state.general_strike = false;
        if (general || kind == Repress) EventRecorder.Record(empire, text);
        else EventRecorder.RecordRoutine(empire, text);
    }

    #endregion

    #region 应对与国策

    // 能否亲自应对工潮：返回不能的原因(语言键)，能则 null
    public static string CanPlayerRespond(Empire empire) =>
        State(empire) is { } state && state.strike_stage > NoStrike ? null : "labour_response_no_need";

    public static string PlayerRespond(Empire empire, bool repress)
    {
        string reason = CanPlayerRespond(empire);
        if (reason != null) return reason;
        Resolve(empire, State(empire), repress ? Repress : Concede, true);
        return null;
    }

    public static int UnionStatus(Empire empire) => State(empire)?.union_status ?? Tolerated;

    // 承认或取缔工会(见诉求"承认工会""取缔工会")
    public static void SetUnionStatus(Empire empire, int status)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        status = Mathf.Clamp(status, Banned, Legal);
        if (state.union_status == status) return;
        bool legalized = status > state.union_status;
        state.union_status = status;
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, legalized ? -8f : 10f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, legalized ? 5f : -5f);
        if (status == Banned) state.union_strength = Mathf.Max(0f, state.union_strength - 20f);
    }

    public static bool RaiseLabourLaw(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || state.labour_law >= MaxLabourLaw) return false;
        state.labour_law++;
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, -10f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 6f);
        return true;
    }

    public static string UnionStatusName(int status) =>
        LM.Get(status >= Legal ? "labour_union_legal" : status <= Banned ? "labour_union_banned" : "labour_union_tolerated");

    #endregion

    #region 效果

    // 工厂产值(见 FactorySystem.Produce)：工钱、劳动法每档少一点，闹工潮的年份减产
    public static float OutputFactor(City city)
    {
        ConstitutionalEconomyState state = State(city?.kingdom);
        if (state == null) return 1f;
        float factor = 1f - 0.03f * state.wage_level - 0.04f * state.labour_law;
        if (state.strike_stage >= GeneralStrike) factor *= 0.6f;
        else if (state.strike_stage == StrikeWave) factor *= 0.85f;
        return Mathf.Max(0.3f, factor);
    }

    // 内需(见 CommodityMarketSystem.Book)：工钱高了，消费品好卖
    public static float DemandFactor(Kingdom kingdom) => 1f + 0.05f * (State(kingdom)?.wage_level ?? 0);

    // 阶层怨气的目标值加减(见 InstitutionSystem)
    public static float GrievanceShift(Empire empire, SocialClass socialClass)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return 0f;
        if (socialClass == SocialClass.Labour)
            return -4f * state.wage_level - 5f * state.labour_law +
                   (state.union_status <= Banned ? 6f : state.union_status >= Legal ? -3f : 0f);
        if (socialClass == SocialClass.Merchant)
            return 2f * state.wage_level + 2f * state.labour_law + (state.union_status >= Legal ? 3f : 0f);
        return 0f;
    }

    #endregion
}
