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

// 经济周期与危机(用户 2026-11-24 "现代之后就很无聊了"，五批里的第四批)。
// 以前"金融危机"只有装了 WarBox 才有，模组自己的经济从工业时代起一路平着走。现在工业时代起市面有兴衰：
//   1. 景气热度逐年积累(自由贸易、金融霸权、私有化、财阀放贷都更快；保护关税、闭关、国有化、打仗更慢)，
//      复苏 → 繁荣(热度 40) → 过热(70)；
//   2. 过热的年份有机会崩盘(热度越高越容易)；通商的伙伴崩了也会传过来(同一个市场几乎躲不掉，通商的市场看热度)；
//      大战打输的国家直接陷入萧条(见 WorldOrderSystem.OnWorldWarEnded)；
//   3. 恐慌一年、萧条三到六年：税收少、工厂开工不足、消费品与奢侈品卖不动(价格跟着跌，见 CommodityMarketSystem.Book)，
//      劳工、市民、商人怨气上涨，执政表现下跌，革命形势多一条成因；
//   4. 当局有四条对策(AI 按执政理念挑，国情窗口里也能亲自定)：听其自然、兴办工程以工代赈(花钱，萧条短、怨气轻)、
//      紧缩(停发福利省钱，萧条长、怨气重)、高筑关税(改保护关税，工厂好过一点，容易招来关税战，见 TradeWarSystem)；
//   5. 大国过半同时陷入危机即世界性大萧条：各国复苏更慢、更容易被传染。
// 统制经济(国有化的社会主义国家；社会主义、共产主义立国的建政即国有化)不随行情起落；穿越政权自成一套。数值都是模组自己的规则。
public static class EconomicCycleSystem
{
    public const int None = 0, Recovery = 1, Boom = 2, Overheat = 3, Panic = 4, Depression = 5;
    public const int IndustrialEra = 7;
    public const float BoomAt = 40f, OverheatAt = 70f;
    private const float HeatBase = 4f, HeatFloor = 1f;
    // 过热时每年崩盘的机会：底数 + 超出过热线的部分(热度满时合计三成六)
    private const float CrashBase = 0.12f, CrashPerHeat = 0.8f;
    private const float ContagionSame = 0.9f, ContagionLinked = 0.35f, WorldDepressionContagion = 1.5f;
    private const int MinDepressionYears = 3, MaxDepressionYears = 6;
    public const int ResponseCooldownYears = 2;
    private const float StimulusShortenChance = 0.5f, AusterityProlongChance = 0.2f;
    public const string Laissez = "laissez", Stimulus = "stimulus", Austerity = "austerity", Tariff = "tariff";

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;
    private static ConstitutionalEconomyState State(Kingdom kingdom) => kingdom?.GetEmpire()?.data?.constitutional_economy;

    public static int Phase(Empire empire) => State(empire)?.cycle_phase ?? None;
    public static bool InCrisis(Empire empire) => Phase(empire) is Panic or Depression;

    private static int Era(Empire empire) => TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire));

    // 统制经济：国有化的社会主义国家，不随行情起落
    public static bool Planned(Empire empire) =>
        State(empire)?.nationalization >= 1 && IdeologyFamilies.IsSocialist(WarlordEraSystem.GovernmentIdeology(empire));

    public static bool Applies(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived() || State(empire) == null) return false;
        if (TransmigrantSystem.Governs(empire) || Planned(empire)) return false;
        return Era(empire) >= IndustrialEra;
    }

    #region 年度

    // 每年(ConstitutionalEconomySystem，先于革命形势与民意结算)
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        double now = World.world.getCurWorldTime();
        // 社会主义、共产主义立国的国家建政即国有化，是统制经济(见 ModernPolicySystem.EnsureFounding)
        ModernPolicySystem.EnsureFounding(empire, state);
        if (!Applies(empire))
        {
            if (state.cycle_phase == None) return;
            state.cycle_phase = None;
            state.cycle_heat = 0f;
            state.cycle_depression_years = 0;
            state.cycle_response = "";
            return;
        }
        if (state.cycle_phase == None)
        {
            state.cycle_phase = Recovery;
            state.cycle_phase_since = now;
            state.cycle_heat = UnityEngine.Random.Range(10f, 35f);
        }
        switch (state.cycle_phase)
        {
            case Panic:
                EnterDepression(empire, state, now);
                break;
            case Depression:
                DepressionYear(empire, state, now);
                break;
            default:
                Grow(empire, state, now);
                break;
        }
    }

    private static void Grow(Empire empire, ConstitutionalEconomyState state, double now)
    {
        Empire source = ContagionSource(empire, state);
        if (source != null)
        {
            Crash(empire, state, "contagion", source);
            return;
        }
        Kingdom core = empire.CoreKingdom;
        float gain = HeatBase;
        gain += PolicyOf(core) switch
        {
            TradePolicy.Free => 2f,
            TradePolicy.Protective => -1f,
            TradePolicy.Closed => -3f,
            _ => 0f
        };
        gain += state.finance_policy > 0 ? 3f : state.finance_policy < 0 ? -2f : 0f;
        gain += state.nationalization > 0 ? -2f : state.nationalization < 0 ? 1.5f : 0f;
        if (CorpsSystem.IsAtWar(core)) gain -= 2f;
        gain += ConglomerateSystem.Monopoly(empire) * 3f;
        gain = Mathf.Max(HeatFloor, gain);
        if (WorldOrderSystem.WorldDepression) gain *= 0.5f;
        state.cycle_heat = Mathf.Clamp(state.cycle_heat + gain, 0f, 100f);

        if (state.cycle_phase == Recovery && state.cycle_heat >= BoomAt) Enter(state, Boom, now);
        if (state.cycle_phase == Boom && state.cycle_heat >= OverheatAt)
        {
            Enter(state, Overheat, now);
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("economic_cycle_overheat_history"), empire.GetEmpireFullName()));
            return;
        }
        if (state.cycle_phase != Overheat) return;
        float chance = CrashBase + (state.cycle_heat - OverheatAt) / 100f * CrashPerHeat;
        if (UnityEngine.Random.value < chance) Crash(empire, state, "bubble", null);
    }

    private static void Enter(ConstitutionalEconomyState state, int phase, double now)
    {
        state.cycle_phase = phase;
        state.cycle_phase_since = now;
    }

    private static TradePolicy PolicyOf(Kingdom core) =>
        CommodityMarketSystem.PolicyOf(CommodityMarketSystem.MarketOf(core)?.leader ?? core);

    // 通商的伙伴里今年有没有崩盘的、会不会传过来
    private static Empire ContagionSource(Empire empire, ConstitutionalEconomyState state)
    {
        Kingdom core = empire.CoreKingdom;
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(core);
        if (market == null) return null;
        float guard = PolicyOf(core) switch
        {
            TradePolicy.Closed => 0f,
            TradePolicy.Protective => 0.5f,
            _ => 1f
        };
        if (guard <= 0f) return null;
        if (WorldOrderSystem.WorldDepression) guard *= WorldDepressionContagion;
        foreach (Kingdom member in market.members)
        {
            Empire other = member?.GetEmpire();
            if (other == null || other == empire || !JustCrashed(other)) continue;
            if (UnityEngine.Random.value < ContagionSame * Mathf.Min(1f, guard)) return other;
        }
        float exposure = 0.5f + state.cycle_heat / 100f;
        foreach (long leaderId in market.linked)
        {
            Kingdom leader = World.world.kingdoms.get(leaderId);
            if (leader == null || leader.isRekt() || MarketSystem.Embargoed(core, leader)) continue;
            Empire other = leader.GetEmpire();
            if (other == null || other == empire || !JustCrashed(other)) continue;
            float weight = CommodityMarketSystem.MarketOf(leader)?.great == true ? 1.3f : 1f;
            if (UnityEngine.Random.value < ContagionLinked * exposure * weight * guard) return other;
        }
        return null;
    }

    private static bool JustCrashed(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        return state != null && state.cycle_phase == Panic && state.cycle_phase_since >= 0d &&
               World.world.getCurWorldTime() >= state.cycle_phase_since && Date.getYearsSince(state.cycle_phase_since) < 1;
    }

    private static void Crash(Empire empire, ConstitutionalEconomyState state, string cause, Empire source)
    {
        double now = World.world.getCurWorldTime();
        float heat = state.cycle_heat;
        Enter(state, Panic, now);
        state.cycle_heat = 10f;
        state.cycle_cause = cause;
        state.cycle_contagion_from = source?.id ?? -1L;
        state.cycle_crises++;
        state.cycle_last_crisis = now;
        state.cycle_response = "";
        state.cycle_response_by_player = false;
        int years = UnityEngine.Random.Range(MinDepressionYears, MaxDepressionYears + 1);
        if (heat >= 85f) years++;
        if (cause == "contagion") years--;
        if (WorldOrderSystem.WorldDepression) years++;
        state.cycle_depression_years = Mathf.Clamp(years, 2, MaxDepressionYears + 2);

        ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 10f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 8f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Citizen, 6f);
        PartyFinanceSystem.Adjust(empire, "approval_economic_crisis", -10f);
        RevolutionSystem.Shock(empire, 6f);
        // 大而不倒：国库出手救最大的财阀，救不起它就垮台(见 ConglomerateSystem)
        ConglomerateSystem.OnCrash(empire, state);
        string name = empire.GetEmpireFullName();
        EventRecorder.Record(empire, cause switch
        {
            "contagion" => string.Format(LM.Get("economic_cycle_contagion_history"), name, source?.GetEmpireFullName() ?? ""),
            "war" => string.Format(LM.Get("economic_cycle_war_history"), name),
            _ => string.Format(LM.Get("economic_cycle_crash_history"), name)
        });
    }

    // 外来的打击(大战打输，见 WorldOrderSystem.OnWorldWarEnded)：直接陷入恐慌
    public static void Shock(Empire empire, string cause)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !Applies(empire) || state.cycle_phase is Panic or Depression) return;
        if (state.cycle_phase == None) state.cycle_phase = Recovery;
        Crash(empire, state, cause, null);
    }

    private static void EnterDepression(Empire empire, ConstitutionalEconomyState state, double now)
    {
        Enter(state, Depression, now);
        if (string.IsNullOrEmpty(state.cycle_response)) Respond(empire, state, AiResponse(empire, state), false);
    }

    private static void DepressionYear(Empire empire, ConstitutionalEconomyState state, double now)
    {
        Kingdom core = empire.CoreKingdom;
        if (string.IsNullOrEmpty(state.cycle_response)) Respond(empire, state, AiResponse(empire, state), false);
        switch (state.cycle_response)
        {
            case Stimulus:
                int cost = StimulusCost(empire);
                if (core.GetMoney() >= cost)
                {
                    core.SubMoney(cost, TreasuryCategory.Construction);
                    if (state.cycle_depression_years > 1 && UnityEngine.Random.value < StimulusShortenChance) state.cycle_depression_years--;
                }
                else
                {
                    // 国库见底，工程停办
                    state.cycle_response = Laissez;
                    EventRecorder.RecordRoutine(empire, string.Format(LM.Get("economic_cycle_stimulus_stopped_history"), empire.GetEmpireFullName()));
                }
                break;
            case Austerity:
                if (UnityEngine.Random.value < AusterityProlongChance) state.cycle_depression_years++;
                break;
        }
        PartyFinanceSystem.Adjust(empire, "approval_economic_crisis", state.cycle_response == Stimulus ? -2f : -4f);
        if (--state.cycle_depression_years > 0) return;
        Enter(state, Recovery, now);
        state.cycle_heat = 15f;
        state.cycle_response = "";
        state.cycle_cause = "";
        state.cycle_contagion_from = -1L;
        EventRecorder.RecordRoutine(empire, string.Format(LM.Get("economic_cycle_recovery_history"), empire.GetEmpireFullName()));
    }

    #endregion

    #region 对策

    // 兴办工程一年的花费：照二档福利的开支
    public static int StimulusCost(Empire empire) => Math.Max(10, ConstitutionalEconomySystem.GetWelfareAnnualCost(empire, 2));

    private static string AiResponse(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.bankrupt_since >= 0d) return Austerity;
        bool rich = empire.CoreKingdom.GetMoney() >= StimulusCost(empire) * 2;
        PartyIdeology ideology = IdeologyFamilies.StateIdeology(empire);
        float roll = UnityEngine.Random.value;
        if (IdeologyFamilies.IsLeft(ideology) || ideology is PartyIdeology.SocialLiberalism or PartyIdeology.Centrism or
                PartyIdeology.ReligiousDemocracy)
            return rich ? Stimulus : Laissez;
        if (IdeologyFamilies.IsAuthoritarian(ideology)) return rich && roll < 0.6f ? Stimulus : Tariff;
        if (ideology == PartyIdeology.Conservatism) return roll < 0.5f ? Tariff : Austerity;
        if (ideology == PartyIdeology.Libertarianism) return Laissez;
        return roll < 0.5f ? Austerity : Laissez;
    }

    // 能否亲自定对策：返回不能的原因(语言键)，能则 null
    public static string CanPlayerRespond(Empire empire, string kind)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !InCrisis(empire)) return "economic_cycle_response_no_need";
        if (state.cycle_response == kind) return "economic_cycle_response_same";
        if (state.cycle_response_by_player && state.cycle_response_at >= 0d &&
            Date.getYearsSince(state.cycle_response_at) < ResponseCooldownYears) return "economic_cycle_response_cooldown";
        if (kind == Stimulus && empire.CoreKingdom.GetMoney() < StimulusCost(empire)) return "economic_cycle_response_no_money";
        return null;
    }

    public static string PlayerRespond(Empire empire, string kind)
    {
        string reason = CanPlayerRespond(empire, kind);
        if (reason != null) return reason;
        Respond(empire, State(empire), kind, true);
        return null;
    }

    // 定下对策(诉求"救市纾困""关税壁垒"也走这里)
    public static void Respond(Empire empire, ConstitutionalEconomyState state, string kind, bool byPlayer)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        state.cycle_response = kind;
        state.cycle_response_at = World.world.getCurWorldTime();
        state.cycle_response_by_player = byPlayer;
        if (kind == Tariff)
        {
            Kingdom leader = CommodityMarketSystem.MarketOf(empire.CoreKingdom)?.leader ?? empire.CoreKingdom;
            // 改不成(和约逼开了港、制度定了锁国)就只剩个名头
            if (CommodityMarketSystem.SetPolicy(leader, TradePolicy.Protective) == null) TradeWarSystem.Provoke(empire, "slump");
        }
        if (kind == Stimulus)
        {
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, -6f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -3f);
        }
        else if (kind == Austerity)
        {
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 5f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Citizen, 3f);
        }
        EventRecorder.RecordRoutine(empire, string.Format(LM.Get("economic_cycle_response_" + kind + "_history"), empire.GetEmpireFullName()));
    }

    // 紧缩：当年的福利停发(见 ConstitutionalEconomySystem.UpdateWelfare)
    public static bool WelfareSuspended(Empire empire) => State(empire) is { } state && state.cycle_phase == Depression &&
                                                          state.cycle_response == Austerity;

    #endregion

    #region 效果

    // 税收(见 PopulationEconomySystem.PayTaxes)：繁荣多收一点，恐慌、萧条少收
    public static float TaxFactor(City city) => State(city?.kingdom)?.cycle_phase switch
    {
        Boom => 1.05f,
        Overheat => 1.1f,
        Panic => 0.75f,
        Depression => 0.85f,
        _ => 1f
    };

    // 工厂开工(见 FactorySystem.Produce)：萧条开工不足；高筑关税护着本国工厂，好过一点
    public static float FactoryFactor(City city)
    {
        ConstitutionalEconomyState state = State(city?.kingdom);
        if (state == null) return 1f;
        float guard = state.cycle_response == Tariff ? 0.05f : 0f;
        return state.cycle_phase switch
        {
            Overheat => 1.05f,
            Panic => 0.75f + guard,
            Depression => 0.85f + guard,
            _ => 1f
        };
    }

    // 消费品、奢侈品的销路(见 CommodityMarketSystem.Book)：萧条时没人买，价格跟着跌
    public static float DemandFactor(Kingdom kingdom) => State(kingdom)?.cycle_phase switch
    {
        Boom => 1.05f,
        Overheat => 1.15f,
        Panic => 0.6f,
        Depression => 0.75f,
        _ => 1f
    };

    // 阶层怨气的目标值加减(见 InstitutionSystem)：失业、破产
    public static float GrievanceShift(Empire empire, SocialClass socialClass)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || state.cycle_phase == None) return 0f;
        if (state.cycle_phase is Boom or Overheat)
            return socialClass is SocialClass.Merchant or SocialClass.Citizen or SocialClass.Labour ? -3f : 0f;
        if (state.cycle_phase is not (Panic or Depression)) return 0f;
        float shift = socialClass switch
        {
            SocialClass.Labour => 14f,
            SocialClass.Citizen => 9f,
            SocialClass.Merchant => 8f,
            SocialClass.Peasant => 4f,
            _ => 0f
        };
        if (shift <= 0f) return 0f;
        return state.cycle_response switch
        {
            // 以工代赈：失业的有活干；商人照旧难熬
            Stimulus => socialClass == SocialClass.Merchant ? shift : shift * 0.5f,
            // 紧缩：健全财政合商人的意，底下的人更苦
            Austerity => socialClass == SocialClass.Merchant ? shift * 0.5f : shift * 1.3f,
            _ => shift
        };
    }

    public static string PhaseName(int phase) => LM.Get("economic_cycle_phase_" + Mathf.Clamp(phase, 0, 5));

    #endregion

    #region 世界性大萧条

    // 每年(WorldOrderSystem.Scan)：大国过半同时陷入危机
    internal static void WorldScan(WorldOrderData data, List<Empire> greats, double now)
    {
        int crisis = greats.Count(InCrisis);
        if (data.depression_since < 0d)
        {
            if (greats.Count < 2 || crisis < 2 || crisis * 2 < greats.Count) return;
            data.depression_since = now;
            WorldOrderSystem.Record(LM.Get("world_depression_history"), greats[0], greats);
            return;
        }
        if (crisis > 0) return;
        int years = Date.getYearsSince(data.depression_since);
        data.depression_since = -1d;
        data.depression_ended = now;
        WorldOrderSystem.Record(string.Format(LM.Get("world_depression_ended_history"), years), greats.FirstOrDefault(), greats);
    }

    #endregion
}
