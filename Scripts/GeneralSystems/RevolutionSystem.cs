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

// 革命形势(用户 2026-11-24 "革命也没有那种历史上真实的革命的感觉""政权更替大多数情况下都是自发的")。
// 以前的革命是"某个值过线或掷中骰子 → 当场换政体，或者政党起兵、打下京城一帧建共和"，看不到矛盾怎么攒起来、
// 谁在组织、什么事点的火，打完也就完了。现在革命是一条有先后的过程，各个旧入口(民意革命浪潮、"建立共和"诉求、
// 废君压力、社会主义政变、现代革命)都要过这里的门(Ready)：
//   1. 形势：矛盾逐年积累(正统低落、阶层怨气、异见、街头抗争、国库亏空、连年镇压、暴君昏主、废君压力)，
//      一年至多涨 RiseStep，不会一夜之间从太平到革命；
//   2. 组织：领头的党(党禁之下是秘密结社)随形势与本理念的民心壮大，外国扶持能添一把(见"扶持革命党"诉求)；
//   3. 当局应对：形势成熟后朝廷要么让步(立宪、开选举，形势缓和、组织分化)，要么镇压(组织受创，但火上浇油，
//      还可能闹出血案)；君主的资质决定 AI 怎么选，制度窗口里也可以亲自选；
//   4. 引爆：形势成熟、组织够了，还得有一件事点火——国库告罄、总罢工、街头暴乱、血案、新君初立、正统崩溃，
//      或者一桩偶然的冲突；没有火星就一直僵着；
//   5. 起事：革命党在外省成立革命政府，与旧政府对峙(原来的革命战争)。邻国与列强按政体、理念站队，
//      出兵干涉或声援；久攻不下就议和——君主肯让步的立宪收场，不肯的各据一方；
//   6. 之后：革命成功只是开头。新政权进入激进期，清洗异己、旧党反扑，过了头就有人出来收拾残局(回摆)，
//      久乱之下强人更容易上台(见 RestorationSystem)；十来年后才算尘埃落定。事败的余党转入地下，以待再起。
// 和平的路(群众政治转型、议会三分之二逼宫)也不再是当年就换：先谈判一年，君主可能拒绝(见 BeginTalks)。
public static class RevolutionSystem
{
    public const int Calm = 0, Undercurrent = 1, Ripe = 2, Uprising = 3;
    public const float UndercurrentAt = 35f, RipeAt = 60f, OrgNeeded = 40f;
    private const float RiseStep = 8f, FallStep = 5f, OrgRise = 6f, OrgFall = 4f;
    private const float RipeHysteresis = 10f;
    public const int ResponseCooldownYears = 2;
    private const float MassacreChance = 0.25f;
    private const int TalksYears = 1;
    private const int StalemateYears = 6;
    private const int MaxInterventions = 3;
    private const float SympathizerChance = 0.25f, GuardianChance = 0.2f;
    private const float CloseIdeology = 30f, FarIdeology = 60f;
    private const int PostYears = 12, PostSettleYears = 5;

    private static readonly HashSet<PartyIdeology> RadicalIdeologies = new()
        { PartyIdeology.Socialism, PartyIdeology.Communism, PartyIdeology.Fascism, PartyIdeology.Anarchism };

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    public static float Tension(Empire empire) => State(empire)?.revolution_tension ?? 0f;
    public static float Organization(Empire empire) => State(empire)?.revolution_org ?? 0f;
    public static int Stage(Empire empire) => State(empire)?.revolution_stage ?? Calm;
    public static bool InAftermath(Empire empire) =>
        State(empire) is { post_revolution_since: >= 0d, post_revolution_phase: >= 1 and <= 3 };

    // 有"革命形势"可言的国家：本文化已能废除君主制的君主国，或已经是共和国。穿越政权、别国的附从不算
    public static bool Applies(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived() || State(empire) == null) return false;
        if (TransmigrantSystem.Governs(empire) || WarlordEraSystem.Dependent(empire.CoreKingdom)) return false;
        return RepublicSystem.IsRepublic(empire) ? !RepublicSystem.IsTransitioning(empire) : RepublicSystem.CanAbolish(empire);
    }

    // 革命能不能在今年爆发：形势成熟、组织够了、今年有事点火
    public static bool Ready(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !Applies(empire)) return false;
        return state.revolution_stage >= Ripe && !string.IsNullOrEmpty(state.revolution_spark) &&
               state.revolution_spark_at >= 0d && World.world.getCurWorldTime() >= state.revolution_spark_at &&
               Date.getYearsSince(state.revolution_spark_at) < 1;
    }

    #region 年度

    // 每年(ConstitutionalEconomySystem，先于民意结算)
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        double now = World.world.getCurWorldTime();
        if (state.post_revolution_since >= 0d && state.post_revolution_phase is >= 1 and <= 3) Aftermath(empire, state, now);
        if (state.abdication_talks_since >= 0d) ResolveTalks(empire, state, now);
        if (!Applies(empire))
        {
            state.revolution_tension = Mathf.Max(0f, state.revolution_tension - FallStep);
            state.revolution_org = Mathf.Max(0f, state.revolution_org - OrgFall);
            state.revolution_stage = Calm;
            return;
        }
        if (RepublicSystem.HasActiveRevolutionWar(empire))
        {
            WarYear(empire, state);
            return;
        }

        float target = Target(empire, state, out string cause);
        state.revolution_cause = cause;
        state.revolution_tension = target > state.revolution_tension
            ? Mathf.Min(target, state.revolution_tension + RiseStep)
            : Mathf.Max(target, state.revolution_tension - FallStep);

        FixedFaction party = Movement(empire, state, out PartyIdeology ideology);
        state.revolution_party_id = party?.GetID() ?? "";
        state.revolution_ideology = (int)ideology;
        UpdateOrganization(empire, state, party, ideology);

        int previous = Mathf.Min(state.revolution_stage, Ripe);
        float tension = state.revolution_tension;
        int stage = tension >= RipeAt && state.revolution_org >= OrgNeeded ? Ripe
            : previous == Ripe && tension >= RipeAt - RipeHysteresis && state.revolution_org >= OrgNeeded - RipeHysteresis ? Ripe
            : tension >= UndercurrentAt ? Undercurrent : Calm;
        state.revolution_stage = stage;
        string country = empire.GetEmpireFullName();
        string movement = MovementName(empire, party, ideology);
        if (stage > previous)
        {
            if (stage == Ripe) EventRecorder.Record(empire, string.Format(LM.Get("revolution_ripe_history"), country, movement));
            else EventRecorder.RecordRoutine(empire, string.Format(LM.Get("revolution_undercurrent_history"), country, movement));
        }
        else if (stage == Calm && previous > Calm)
        {
            state.revolution_underground = false;
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("revolution_eased_history"), country));
        }
        if (stage < Ripe) return;

        // 当局应对：两年里亲自应对过的不再由 AI 重复
        if (state.revolution_response_at < 0d || Date.getYearsSince(state.revolution_response_at) >= ResponseCooldownYears)
            Respond(empire, state, AiRepresses(empire, state), false);
        if (state.revolution_stage < Ripe) return;

        string spark = FindSpark(empire, state);
        if (spark == null) return;
        state.revolution_spark = spark;
        state.revolution_spark_at = now;
        EventRecorder.Record(empire, string.Format(LM.Get("revolution_spark_" + spark + "_history"), country, movement));
        Ignite(empire, state, ideology);
    }

    // 形势该到多高(0~100)，连同最大的那条成因(界面显示)
    private static float Target(Empire empire, ConstitutionalEconomyState state, out string cause)
    {
        var parts = new List<(string key, float value)>();
        parts.Add(("legitimacy", Mathf.Clamp01((55f - empire.Legitimacy) / 55f) * 30f));
        Dictionary<SocialClass, float> shares = InstitutionSystem.BuildClassShares(empire);
        float grievance = 0f;
        foreach (var pair in InstitutionSystem.GetClassGrievances(empire))
            if (shares.TryGetValue(pair.Key, out float share) && share >= 0.05f) grievance = Mathf.Max(grievance, pair.Value);
        parts.Add(("grievance", Mathf.Clamp01(grievance / 100f) * 25f));
        parts.Add(("dissent", Mathf.Clamp01(state.opinion_dissent) * 15f));
        parts.Add(("street", Mathf.Clamp(state.street_unrest_stage, 0, 3) / 3f * 10f));
        if (state.bankrupt_since >= 0d) parts.Add(("bankrupt", 8f));
        // 恐慌与萧条(见 EconomicCycleSystem)
        if (EconomicCycleSystem.InCrisis(empire)) parts.Add(("depression", state.cycle_phase == EconomicCycleSystem.Panic ? 10f : 8f));
        parts.Add(("repression", Mathf.Min(10f, state.repression_years * 4f)));
        bool monarchy = !RepublicSystem.IsRepublic(empire);
        if (monarchy)
        {
            parts.Add(("ruler", RulerQualitySystem.Of(empire.Emperor) switch
            {
                RulerQualitySystem.Grade.Tyrant => 8f,
                RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => 4f,
                _ => 0f
            }));
            parts.Add(("abolition", Mathf.Clamp01(RepublicSystem.GetAbolitionPressure(empire) / 100f) * 10f));
        }
        if (state.revolution_underground) parts.Add(("underground", 5f));
        float total = parts.Sum(part => part.value);
        // 让步之后的几年里缓和；明君在位人心稍安
        if (state.revolution_response == "concede" && state.revolution_response_at >= 0d &&
            Date.getYearsSince(state.revolution_response_at) < 5) total -= 10f;
        if (monarchy && RulerQualitySystem.Of(empire.Emperor) == RulerQualitySystem.Grade.Wise) total -= 5f;
        cause = parts.OrderByDescending(part => part.value).First().key;
        return Mathf.Clamp(total, 0f, 100f);
    }

    // 领头的党与它的纲领。君主国：非保守派里本理念民心最盛的合法政党；共和国：不在台上、与立国理念相去甚远的合法政党。
    // 没有这样的党(党禁之下、还没有政党、起事失败被取缔)：秘密结社，纲领取民心最盛的那一种
    private static FixedFaction Movement(Empire empire, ConstitutionalEconomyState state, out PartyIdeology ideology)
    {
        bool republic = RepublicSystem.IsRepublic(empire);
        PartyIdeology ruling = IdeologyFamilies.StateIdeology(empire);
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        FixedFaction best = PartySystem.GetParties(empire)
            .Where(party => party != null && party.IsParty && !party.Ban && (republic
                ? party != governing && PartySystem.IdeologyDistance(party.Ideology, ruling) >= CloseIdeology
                : party.Ideology != PartyIdeology.Conservatism))
            .OrderByDescending(party => IdeologyPopulationSystem.GetEmpireShare(empire, party.Ideology))
            .ThenByDescending(party => party.GetLeader() != null).FirstOrDefault();
        if (best != null)
        {
            ideology = best.Ideology;
            return best;
        }
        // 事败转入地下的沿用原来的纲领
        if (state.revolution_underground && state.revolution_ideology >= 0 &&
            Enum.IsDefined(typeof(PartyIdeology), state.revolution_ideology))
        {
            ideology = (PartyIdeology)state.revolution_ideology;
            return null;
        }
        ideology = republic
            ? Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
                .Where(candidate => PartySystem.IdeologyDistance(candidate, ruling) >= CloseIdeology)
                .OrderByDescending(candidate => IdeologyPopulationSystem.GetEmpireShare(empire, candidate)).First()
            : RepublicSystem.ReformIdeology(empire);
        return null;
    }

    public static string MovementName(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return "";
        FixedFaction party = string.IsNullOrEmpty(state.revolution_party_id) ? null
            : PartySystem.GetParties(empire).FirstOrDefault(candidate => candidate.GetID() == state.revolution_party_id);
        PartyIdeology ideology = Enum.IsDefined(typeof(PartyIdeology), state.revolution_ideology)
            ? (PartyIdeology)state.revolution_ideology : RepublicSystem.ReformIdeology(empire);
        return MovementName(empire, party, ideology);
    }

    private static string MovementName(Empire empire, FixedFaction party, PartyIdeology ideology) =>
        party != null ? party.Name : string.Format(LM.Get("revolution_society_name"), PartySystem.GetIdeologyName(ideology));

    private static void UpdateOrganization(Empire empire, ConstitutionalEconomyState state, FixedFaction party,
        PartyIdeology ideology)
    {
        float share = Mathf.Clamp01(IdeologyPopulationSystem.GetEmpireShare(empire, ideology));
        float mobilization = 0f;
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)))
            if (PartySystem.GetAffinity(ideology, socialClass) >= 40f)
                mobilization = Mathf.Max(mobilization, SocialCrisisSystem.GetMobilization(empire, socialClass));
        float target = share * 60f + state.revolution_tension * 0.4f + mobilization * 0.15f +
                       (party?.GetLeader() != null ? 8f : 0f);
        if (state.revolution_tension < UndercurrentAt) target *= 0.5f;
        target = Mathf.Clamp(target, 0f, 100f);
        // 没有合法的党，只能在地下串联，长得慢
        float rise = party != null ? OrgRise : OrgRise * 0.5f;
        state.revolution_org = target > state.revolution_org
            ? Mathf.Min(target, state.revolution_org + rise)
            : Mathf.Max(target, state.revolution_org - OrgFall);
    }

    // 民意的革命浪潮攒够了年头(PublicOpinionSystem)：不再直接起事，给形势再添一把火；形势没成熟时只是一场骚乱
    public static void OnOpinionWave(Empire empire, PartyIdeology preferred)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        state.revolution_tension = Mathf.Min(100f, state.revolution_tension + 10f);
        if (state.revolution_stage < Ripe)
            EventRecorder.RecordRoutine(empire, string.Format(LM.Get("public_opinion_riot_log"), empire.GetEmpireName(),
                PartySystem.GetIdeologyName(preferred)));
    }

    // 外来的震动(战败、冷战落败，见 WorldOrderSystem)：形势陡增
    public static void Shock(Empire empire, float amount)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        state.revolution_tension = Mathf.Clamp(state.revolution_tension + amount, 0f, 100f);
    }

    // 外国扶持(见"扶持革命党"诉求)
    public static void ForeignSupport(Empire target, float amount)
    {
        ConstitutionalEconomyState state = State(target);
        if (state == null || !Applies(target)) return;
        state.revolution_org = Mathf.Clamp(state.revolution_org + amount, 0f, 100f);
    }

    #endregion

    #region 当局应对

    // 君主：暴君强压到底，明君、庸弱、冲龄、昏聩让步，其余只在正统很低时让步；共和国照街头抗争的口径
    private static bool AiRepresses(Empire empire, ConstitutionalEconomyState state)
    {
        if (RepublicSystem.IsRepublic(empire)) return HarshRuleSystem.AiChoosesRepression(empire, state);
        return !MonarchConcedes(empire);
    }

    private static bool MonarchConcedes(Empire empire)
    {
        Actor ruler = empire?.Emperor;
        if (ruler == null) return true;
        return RulerQualitySystem.Of(ruler) switch
        {
            RulerQualitySystem.Grade.Wise or RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Minor or
                RulerQualitySystem.Grade.Fatuous => true,
            RulerQualitySystem.Grade.Tyrant => false,
            _ => empire.Legitimacy < 35
        };
    }

    // 能否亲自应对：返回不能的原因(语言键)，能则 null
    public static string CanPlayerRespond(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !Applies(empire) || state.revolution_stage < Undercurrent || state.revolution_stage >= Uprising)
            return "revolution_response_no_need";
        if (state.revolution_response_at >= 0d && Date.getYearsSince(state.revolution_response_at) < ResponseCooldownYears)
            return "revolution_response_cooldown";
        return null;
    }

    public static string PlayerRespond(Empire empire, bool repress)
    {
        string reason = CanPlayerRespond(empire);
        if (reason != null) return reason;
        Respond(empire, State(empire), repress, true);
        return null;
    }

    private static void Respond(Empire empire, ConstitutionalEconomyState state, bool repress, bool byPlayer)
    {
        double now = World.world.getCurWorldTime();
        state.revolution_response_at = now;
        state.revolution_response = repress ? "repress" : "concede";
        state.revolution_response_by_player = byPlayer;
        string country = empire.GetEmpireFullName();
        string movement = MovementName(empire);
        PartyIdeology ideology = Enum.IsDefined(typeof(PartyIdeology), state.revolution_ideology)
            ? (PartyIdeology)state.revolution_ideology : RepublicSystem.ReformIdeology(empire);
        if (repress)
        {
            // 军中同情革命的多，镇压就不得力
            bool halfhearted = IdeologyPopulationSystem.GetMilitaryShare(empire, ideology) > 0.4f;
            state.revolution_org = Mathf.Max(0f, state.revolution_org - (halfhearted ? 8f : 18f));
            state.revolution_tension = Mathf.Min(100f, state.revolution_tension + 6f);
            state.repression_years++;
            state.last_repression = now;
            if (UnityEngine.Random.value < MassacreChance)
            {
                // 闹出血案：火上浇油，今年就可能点着
                state.revolution_tension = Mathf.Min(100f, state.revolution_tension + 10f);
                state.revolution_massacre_at = now;
                empire.AddMandate(-3);
                EventRecorder.Record(empire, string.Format(LM.Get("revolution_massacre_history"), country, movement));
            }
            else
                EventRecorder.Record(empire, string.Format(LM.Get(halfhearted
                    ? "revolution_repress_halfhearted_history" : "revolution_repress_history"), country, movement));
        }
        else
        {
            state.revolution_tension = Mathf.Max(0f, state.revolution_tension - 18f);
            state.revolution_org = Mathf.Max(0f, state.revolution_org - 6f);
            state.repression_years = 0;
            string measure = Concession(empire, state);
            EventRecorder.Record(empire, string.Format(LM.Get("revolution_concede_history"), country, movement, measure));
        }
        if (state.revolution_tension < RipeAt - RipeHysteresis || state.revolution_org < OrgNeeded - RipeHysteresis)
            state.revolution_stage = state.revolution_tension >= UndercurrentAt ? Undercurrent : Calm;
    }

    // 让步让出了什么：君主国许立宪(已立宪的提前大选)，共和国顺应民意或提前大选；都不成就只是许诺改革
    private static string Concession(Empire empire, ConstitutionalEconomyState state)
    {
        if (!RepublicSystem.IsRepublic(empire))
        {
            if (!state.constitutional_monarchy && ConstitutionalEconomySystem.ConcedeConstitutionalMonarchy(empire))
                return LM.Get("revolution_concession_constitution");
        }
        else if (PublicOpinionSystem.AcceptPreferred(empire)) return LM.Get("revolution_concession_opinion");
        if (ParliamentSystem.HasParliament(empire) && ParliamentSystem.CanCallSnapElection(state))
        {
            state.last_parliament_election = -1d;
            return LM.Get("revolution_concession_election");
        }
        return LM.Get("revolution_concession_promise");
    }

    #endregion

    #region 引爆与起事

    // 今年有没有一件事点火(返回语言键后缀)；没有返回 null
    private static string FindSpark(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.revolution_massacre_at >= 0d && Date.getYearsSince(state.revolution_massacre_at) < 1) return "massacre";
        if (state.bankrupt_since >= 0d) return "bankrupt";
        // 市面崩盘的那一年(见 EconomicCycleSystem)
        if (state.cycle_phase == EconomicCycleSystem.Panic) return "crash";
        if (state.general_strike) return "strike";
        // 民意到了"革命浪潮"一档(见 PublicOpinionSystem)
        if (state.opinion_level >= 3) return "wave";
        if (state.street_unrest_stage >= HarshRuleSystem.Riots) return "riot";
        if (empire.Legitimacy < 20) return "collapse";
        if (!RepublicSystem.IsRepublic(empire))
        {
            bool newReign = state.reign_ruler_since >= 0d && Date.getYearsSince(state.reign_ruler_since) < 1;
            if (newReign || RulerQualitySystem.Of(empire.Emperor) == RulerQualitySystem.Grade.Minor) return "succession";
        }
        // 一桩偶然的冲突：形势越紧越容易擦枪走火
        float chance = 0.04f + Mathf.Max(0f, state.revolution_tension - RipeAt) / 150f;
        return UnityEngine.Random.value < chance ? "incident" : null;
    }

    // 点着了：君主国走建立共和(够票的先谈判，不然起兵)，共和国走夺权、垮台、提前大选或现代革命(见 PublicOpinionSystem.Revolt)
    private static void Ignite(Empire empire, ConstitutionalEconomyState state, PartyIdeology ideology)
    {
        PublicOpinionSystem.Revolt(empire, state, ideology);
        if (RepublicSystem.HasActiveRevolutionWar(empire))
        {
            BeginUprising(empire, state);
            return;
        }
        // 没打起来(和平转了、让步了，或者找不到起事的地方)：这把火烧过去了
        state.revolution_tension = Mathf.Max(0f, state.revolution_tension - 8f);
    }

    private static War RevolutionWar(Empire empire) => empire?.CoreKingdom?.getWars()
        ?.FirstOrDefault(war => war != null && !war.hasEnded() && RepublicSystem.IsRevolutionWar(war));

    private static void BeginUprising(Empire empire, ConstitutionalEconomyState state)
    {
        state.revolution_stage = Uprising;
        state.revolution_interventions = 0;
        War war = RevolutionWar(empire);
        Kingdom rebel = war?.getMainAttacker();
        if (rebel == null) return;
        EventRecorder.Record(empire, string.Format(LM.Get("revolution_government_history"), MovementName(empire),
            rebel.capital?.GetCityName() ?? rebel.GetKingdomName(), empire.GetEmpireFullName()));
        Intervene(empire, state, war);
    }

    // 革命战争进行中的每一年：列强站队；久攻不下就议和
    private static void WarYear(Empire empire, ConstitutionalEconomyState state)
    {
        War war = RevolutionWar(empire);
        if (war == null) return;
        // 各地互保同盟另立(见 RepublicSystem.TryRegionalRepublicCoalition)有自己的收场，这里只记阶段
        if (war.GetOrCreate().republic_regional_coalition)
        {
            state.revolution_stage = Uprising;
            return;
        }
        if (state.revolution_stage != Uprising)
        {
            BeginUprising(empire, state);
            return;
        }
        Intervene(empire, state, war);
        if (war.getAge() < StalemateYears) return;
        Kingdom rebel = war.getMainAttacker();
        if (rebel == null || rebel.isRekt() || (rebel.cities?.Count ?? 0) == 0) return;
        // 僵局：结果在停战结算里定(见 OnPeace)
        World.world.wars.endWar(war, WarWinner.Peace);
    }

    // 外国干涉：与革命党理念相近的现代国家声援，君主国(君主国的革命)或与旧政府理念相近的(共和国的革命)出兵助剿。
    // 只找够得着的(接壤，或宣战可达)、眼下没在打仗的、有点分量的国家；每年每边至多一家，总共至多 MaxInterventions 家
    private static void Intervene(Empire empire, ConstitutionalEconomyState state, War war)
    {
        if (state.revolution_interventions >= MaxInterventions || war == null || war.hasEnded()) return;
        Kingdom core = empire.CoreKingdom;
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        PartyIdeology cause = snapshot.republic_revolution_ideology;
        bool monarchyRevolution = !snapshot.republic_modern_revolution;
        PartyIdeology ruling = IdeologyFamilies.StateIdeology(empire);
        int weight = Mathf.Max(2, empire.AllCities().Count() / 4);
        bool helped = false, opposed = false;
        foreach (Kingdom other in World.world.kingdoms.list.ToList())
        {
            if (state.revolution_interventions >= MaxInterventions) break;
            if (other?.data == null || other.isRekt() || !other.hasKing() || other.wild || other == core ||
                empire.kingdoms_hashset.Contains(other) || other.IsInEmpire() && !other.IsEmpire() ||
                WarlordEraSystem.Dependent(other) || FeudalVassalService.GetOverlord(other) != null ||
                (other.cities?.Count ?? 0) < weight || other.getWars().Any(active => active != null && !active.hasEnded()) ||
                war.hasKingdom(other)) continue;
            bool near = empire.IsNeighbourWith(other);
            if (!near && !WarReach.MayDeclare(other, core, WarTypeLibrary.normal)) continue;
            RegimeType? regime = other.GetRegime()?.type;
            bool otherMonarchy = RegimeManager.IsMonarchy(regime);
            PartyIdeology theirs = IdeologyFamilies.StateIdeology(other);
            bool sympathizer = !otherMonarchy && regime == RegimeType.Modern &&
                               PartySystem.IdeologyDistance(theirs, cause) <= CloseIdeology;
            bool guardian = !sympathizer && (monarchyRevolution
                ? otherMonarchy && !other.isEnemy(core)
                : PartySystem.IdeologyDistance(theirs, ruling) <= CloseIdeology &&
                  PartySystem.IdeologyDistance(theirs, cause) >= FarIdeology);
            float reach = near ? 1f : 0.5f;
            if (sympathizer && !helped && UnityEngine.Random.value < SympathizerChance * reach)
            {
                war.joinAttackers(other);
                if (!war.hasKingdom(other)) continue;
                helped = true;
                state.revolution_interventions++;
                EventRecorder.Record(empire, string.Format(LM.Get("revolution_foreign_support_history"),
                    other.GetKingdomFullName(), MovementName(empire)));
            }
            else if (guardian && !opposed && UnityEngine.Random.value < GuardianChance * reach)
            {
                war.joinDefenders(other);
                if (!war.hasKingdom(other)) continue;
                opposed = true;
                state.revolution_interventions++;
                EventRecorder.Record(empire, string.Format(LM.Get("revolution_foreign_intervention_history"),
                    other.GetKingdomFullName(), empire.GetEmpireFullName()));
            }
        }
    }

    // 停战(僵局、强制停战)：君主肯让步的立宪收场、革命军归建；不肯的各据一方，起事的那一块自立为革命政权
    public static void OnPeace(Empire empire, War war, FixedFaction party, PartyIdeology ideology, bool splitRealm)
    {
        ConstitutionalEconomyState state = State(empire);
        Kingdom rebel = war?.getMainAttacker();
        if (state == null || rebel == null || rebel.isRekt()) return;
        string country = empire.GetEmpireFullName();
        string movement = party?.Name ?? MovementName(empire);
        state.revolution_stage = Calm;
        if (!RepublicSystem.IsRepublic(empire) && MonarchConcedes(empire) && !state.constitutional_monarchy &&
            ConstitutionalEconomySystem.ConcedeConstitutionalMonarchy(empire))
        {
            if (splitRealm && !rebel.isRekt()) empire.join(rebel, pForce: true, pLegitimacyTransfer: true);
            state.revolution_tension = Mathf.Max(0f, state.revolution_tension - 30f);
            state.revolution_org *= 0.5f;
            state.revolution_response = "concede";
            state.revolution_response_at = World.world.getCurWorldTime();
            EventRecorder.Record(empire, string.Format(LM.Get("revolution_compromise_history"), country, movement));
            return;
        }
        state.revolution_tension = Mathf.Max(0f, state.revolution_tension - 15f);
        state.revolution_org *= 0.5f;
        string seat = rebel.capital?.GetCityName() ?? rebel.GetKingdomName();
        // 自立的那一块是革命政权：眼下还挂着起事的名号，停战结算完才清(见 WarPatch)，这里只记一笔——
        // 日后它组建政府时算革命建立的政府(见 ModernStateFormationSystem.FormGovernment)，还是君主政体的由近代化那边改过去
        // (见 StateModernization.TryRevolution)
        rebel.GetOrCreate().revolutionary_seat = true;
        EventRecorder.Record(empire, string.Format(LM.Get("revolution_partition_history"), country, movement, seat));
    }

    // 革命党兵败：党被取缔(RepublicSystem)，余党转入地下，组织留下四成
    public static void OnDefeat(Empire empire, PartyIdeology ideology)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        state.revolution_stage = Calm;
        state.revolution_org = Mathf.Max(10f, state.revolution_org * 0.4f);
        state.revolution_tension = Mathf.Max(0f, state.revolution_tension - 20f);
        state.revolution_underground = true;
        state.revolution_ideology = (int)ideology;
        state.revolution_party_id = "";
        EventRecorder.Record(empire, string.Format(LM.Get("revolution_underground_history"), empire.GetEmpireFullName(),
            PartySystem.GetIdeologyName(ideology)));
    }

    #endregion

    #region 逊位谈判

    // 和平改制(群众政治转型 route = "mass"、议会三分之二且握有军队 route = "coup")：先谈一年。
    // 已经在谈的返回 false(免得各个入口年年重提)
    public static bool BeginTalks(Empire empire, FixedFaction party, string route)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || party == null || state.abdication_talks_since >= 0d) return false;
        state.abdication_talks_since = World.world.getCurWorldTime();
        state.abdication_party_id = party.GetID();
        state.abdication_route = route;
        EventRecorder.Record(empire, string.Format(LM.Get("republic_abdication_talks_history"), party.Name,
            empire.Emperor?.getName() ?? LM.Get("label_none"), empire.GetEmpireFullName()), party.GetLeader());
        return true;
    }

    public static bool InTalks(Empire empire) => State(empire)?.abdication_talks_since >= 0d;

    private static void ResolveTalks(Empire empire, ConstitutionalEconomyState state, double now)
    {
        if (now >= state.abdication_talks_since && Date.getYearsSince(state.abdication_talks_since) < TalksYears) return;
        string route = state.abdication_route;
        FixedFaction party = PartySystem.GetParties(empire)
            .FirstOrDefault(candidate => candidate.GetID() == state.abdication_party_id);
        state.abdication_talks_since = -1d;
        state.abdication_party_id = "";
        state.abdication_route = "";
        if (party == null || RepublicSystem.IsRepublic(empire)) return;
        bool coup = route == "coup";
        bool stillHolds = coup ? RepublicSystem.CanAbdicate(empire, party) : RepublicSystem.CanMassPoliticsTransition(empire, party);
        string monarch = empire.Emperor?.getName() ?? LM.Get("label_none");
        if (!stillHolds)
        {
            EventRecorder.Record(empire, string.Format(LM.Get("republic_abdication_lapsed_history"), party.Name, monarch),
                party.GetLeader());
            return;
        }
        // 军队站在逼宫的一边，由不得君主；只凭选票的，君主可以翻脸
        if (coup || MonarchConcedes(empire))
        {
            RepublicSystem.CompleteAbdication(empire, party, coup);
            return;
        }
        state.revolution_tension = Mathf.Min(100f, state.revolution_tension + 30f);
        state.revolution_org = Mathf.Min(100f, state.revolution_org + 15f);
        empire.AddMandate(-5);
        EventRecorder.Record(empire, string.Format(LM.Get("republic_abdication_refused_history"), monarch, party.Name),
            empire.Emperor);
    }

    #endregion

    #region 革命之后

    // 革命成功(RepublicSystem.ResolveRevolutionWar)
    public static void OnVictory(Empire empire, PartyIdeology ideology, Actor leader, War war)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return;
        float radical = 25f + state.revolution_tension * 0.3f + (war?.getAge() ?? 0f) * 6f +
                        state.revolution_interventions * 8f + (RadicalIdeologies.Contains(ideology) ? 20f : 0f);
        state.post_revolution_since = World.world.getCurWorldTime();
        state.post_revolution_phase = 1;
        state.post_revolution_radical = Mathf.Clamp(radical, 20f, 95f);
        state.post_revolution_purges = 0;
        state.post_revolution_leader_id = leader == null || leader.isRekt() ? -1L : leader.id;
        state.post_revolution_counter_at = -1d;
        state.revolution_stage = Calm;
        state.revolution_tension = Mathf.Min(state.revolution_tension, 20f);
        state.revolution_org = 0f;
        state.revolution_underground = false;
        state.revolution_spark = "";
        state.revolution_interventions = 0;
    }

    public static string AftermathPhaseName(Empire empire) =>
        LM.Get("post_revolution_phase_" + Mathf.Clamp(State(empire)?.post_revolution_phase ?? 0, 0, 4));

    // 强人上台的机会(RestorationSystem)：久乱思定，回摆之后更甚
    public static float StrongmanFactor(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || state.post_revolution_since < 0d || state.post_revolution_phase is < 1 or > 3) return 1f;
        if (Date.getYearsSince(state.post_revolution_since) < 3) return 1f;
        return state.post_revolution_phase == 2 ? 4f : state.post_revolution_radical >= 50f ? 2.5f : 1f;
    }

    private static void Aftermath(Empire empire, ConstitutionalEconomyState state, double now)
    {
        if (!RepublicSystem.IsRepublic(empire))
        {
            // 复辟了：这一页翻过去
            state.post_revolution_phase = 4;
            return;
        }
        int years = Date.getYearsSince(state.post_revolution_since);
        Kingdom core = empire.CoreKingdom;
        PartyIdeology founding = state.republic_ideology;
        string country = empire.GetEmpireFullName();
        float radical = state.post_revolution_radical;
        if (core.hasEnemies()) radical += 6f;
        if (RadicalIdeologies.Contains(founding)) radical += 2f;
        if (!core.hasEnemies() && empire.Legitimacy >= 50) radical -= 5f;
        if (years >= 3) radical -= 3f;
        if (state.post_revolution_phase == 2) radical = Mathf.Min(radical, 45f);
        state.post_revolution_radical = radical = Mathf.Clamp(radical, 0f, 100f);

        if (state.post_revolution_phase == 1)
        {
            // 清洗异己
            if (radical >= 55f && UnityEngine.Random.value < 0.25f + radical / 400f) Purge(empire, state, founding);
            // 旧党反扑：保守的民心越多越容易，四年里至多一次
            bool rested = state.post_revolution_counter_at < 0d || Date.getYearsSince(state.post_revolution_counter_at) >= 4;
            float counter = 0.1f + 0.3f * IdeologyPopulationSystem.GetEmpireShare(empire, PartyIdeology.Conservatism);
            if (rested && years >= 1 && UnityEngine.Random.value < counter && RepublicSystem.StartCounterRevolution(empire))
            {
                state.post_revolution_counter_at = now;
                state.post_revolution_radical = Mathf.Min(100f, state.post_revolution_radical + 15f);
            }
            // 回摆：清洗过了头，人心厌乱，有人出来收拾残局
            if (state.post_revolution_radical >= 70f && state.post_revolution_purges >= 2 &&
                UnityEngine.Random.value < 0.2f + (state.post_revolution_purges - 2) * 0.1f)
            {
                Thermidor(empire, state);
                return;
            }
        }
        bool settled = years >= PostYears || years >= PostSettleYears && state.post_revolution_radical < 25f;
        if (!settled) return;
        state.post_revolution_phase = 4;
        EventRecorder.Record(empire, string.Format(LM.Get("post_revolution_settled_history"), country));
    }

    // 清洗：取缔与立国理念相去最远的在野党；没有政党可取缔就清算旧人
    private static void Purge(Empire empire, ConstitutionalEconomyState state, PartyIdeology founding)
    {
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        FixedFaction target = PartySystem.GetParties(empire)
            .Where(party => party != null && party.IsParty && !party.Ban && party != governing &&
                            party.GetID() != state.one_party_id && party.Ideology != founding)
            .OrderByDescending(party => PartySystem.IdeologyDistance(party.Ideology, founding)).FirstOrDefault();
        state.post_revolution_purges++;
        state.post_revolution_radical = Mathf.Min(100f, state.post_revolution_radical + 5f);
        empire.AddMandate(-2);
        string country = empire.GetEmpireFullName();
        if (target == null)
        {
            EventRecorder.Record(empire, string.Format(LM.Get("post_revolution_purge_old_history"), country));
            return;
        }
        string name = target.Name;
        target.BanFaction();
        EventRecorder.Record(empire, string.Format(LM.Get("post_revolution_purge_history"), country, name));
    }

    private static void Thermidor(Empire empire, ConstitutionalEconomyState state)
    {
        state.post_revolution_phase = 2;
        state.post_revolution_radical = 30f;
        state.revolution_tension = Mathf.Max(0f, state.revolution_tension - 10f);
        empire.AddMandate(5);
        Actor head = empire.Emperor;
        string fallen = head?.getName() ?? LM.Get("label_none");
        // 领着革命的那个人还在台上：换一个本党里威望最高的别人
        Actor successor = null;
        if (head != null && head.id == state.post_revolution_leader_id)
        {
            FixedFaction governing = PartySystem.GetGovernmentParty(empire) ?? empire.CoreKingdom.GetRegime()?.GetDominateFaction();
            successor = governing?.AllMembers?
                .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && actor.id != head.id)
                .OrderByDescending(actor => actor.renown).FirstOrDefault();
            if (successor != null) empire.InstallHeadOfState(successor);
        }
        EventRecorder.Record(empire, successor != null
            ? string.Format(LM.Get("post_revolution_thermidor_history"), empire.GetEmpireFullName(), successor.getName(), fallen)
            : string.Format(LM.Get("post_revolution_thermidor_plain_history"), empire.GetEmpireFullName()), successor);
    }

    #endregion
}
