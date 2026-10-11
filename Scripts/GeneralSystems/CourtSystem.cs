using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

public enum CourtCouncilKind { None, Cabinet, Electors, Senate, ThreeDepartments, Han, Zhou }

// 朝堂博弈：没有议会的帝国，派系诉求不再由最大派系直接推行，而是走奏议五步——
//   ① 上奏：派系花政治资本，把自己支持的一条议题递上去(独特议题只能本派上奏，公共议题谁都能上)；
//   ② 票拟：有议事会(内阁/选帝侯/元老院)的，由议程官(首辅/首席选帝侯/首席元老)决定递不递，
//      议程官所属派系反对时会压下，盟友或花资本疏通才能过；不在议事会又没有盟友的派系只能走言路，未必上达；
//   ③ 廷议：各派按立场表态，权重 = 派系势力(有议事会时一半看席位)，赞成率不足三成五即否决；
//   ④ 批红：皇帝按赞成率、圣眷、自己的立场与性格、方略裁决准奏/驳回/留中；幼主、傀儡由权臣或议程官代批；
//   ⑤ 施行：准奏后照原诉求逐月推进，议事会里支持的越多越快，反对派可以掣肘拖慢。
// 同一时间只办一件奏议。挑战皇权的诉求(索取皇位)、民间运动(开放党禁)、地方推动不走这条路。
// 有议会的帝国走议会与政党(ParliamentSystem)，不适用本系统。
public static partial class CourtSystem
{
    public const float ProposalCost = 15f;
    public const float LobbyCost = 10f;
    public const float DebateThreshold = 35f;
    private const int LogSize = 8;
    private const int StageMonths = 1;
    private const int ShelveMonths = 6;

    #region 查询

    public static bool Applies(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime != null && empire.data != null && !empire.isRekt() && !empire.IsArchived() &&
               !ParliamentSystem.HasParliament(empire);
    }

    public static CourtState State(Empire empire)
    {
        if (empire?.data == null) return null;
        CourtState state = empire.data.court ??= new CourtState();
        state.factions ??= new Dictionary<string, CourtFactionState>();
        state.log ??= new List<CourtLogEntry>();
        return state;
    }

    public static CourtFactionState FactionState(Empire empire, FixedFaction faction)
    {
        CourtState state = State(empire);
        if (state == null || faction == null) return new CourtFactionState();
        if (!state.factions.TryGetValue(faction.GetID(), out CourtFactionState data) || data == null)
            state.factions[faction.GetID()] = data = new CourtFactionState();
        data.cooldowns ??= new Dictionary<string, double>();
        return data;
    }

    public static List<FixedFaction> Factions(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()?.Where(faction => faction != null && !faction.Ban)
            .ToList() ?? new List<FixedFaction>();

    public static FixedFaction FindFaction(Empire empire, string id) =>
        string.IsNullOrEmpty(id) ? null : Factions(empire).FirstOrDefault(faction => faction.GetID() == id);

    public static CourtCouncilKind CouncilKind(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null) return CourtCouncilKind.None;
        return regime.type switch
        {
            RegimeType.Feudalism => CourtCouncilKind.Electors,
            RegimeType.ClassicalRepublic => CityStateService.HasSenate(empire) ? CourtCouncilKind.Senate : CourtCouncilKind.None,
            _ => Stage(empire) switch
            {
                HuaxiaCourtStage.Zhou => CourtCouncilKind.Zhou,
                HuaxiaCourtStage.Han => CourtCouncilKind.Han,
                HuaxiaCourtStage.Tang => CourtCouncilKind.ThreeDepartments,
                HuaxiaCourtStage.Ming => CourtCouncilKind.Cabinet,
                _ => regime.has_cabinet ? CourtCouncilKind.Cabinet : CourtCouncilKind.None
            }
        };
    }

    // 议事会：内阁 / 选帝侯 / 元老；周、汉、唐是中央各官职的长官
    public static List<Actor> Council(Empire empire) => CouncilKind(empire) switch
    {
        CourtCouncilKind.None => new List<Actor>(),
        CourtCouncilKind.Zhou or CourtCouncilKind.Han or CourtCouncilKind.ThreeDepartments => CentralHolders(empire),
        _ => empire.GetCabinetMembers()
    };

    // 议程官：首辅 / 中书令 / 丞相 / 首席选帝侯 / 首席元老；周天子临朝，诸臣直陈，没有议程官
    public static Actor AgendaSetter(Empire empire) => CouncilKind(empire) switch
    {
        CourtCouncilKind.Zhou => null,
        CourtCouncilKind.Han => DepartmentHead(empire, ChancellorOffice),
        CourtCouncilKind.ThreeDepartments => DepartmentHead(empire, ZhongshuOffice),
        _ => Council(empire).FirstOrDefault()
    };

    public static bool IsBoycotting(CourtFactionState state) => state != null && state.grievance >= 60f;

    // 廷议权重：派系势力；有议事会时一半看议事会席位
    public static Dictionary<FixedFaction, float> Weights(Empire empire, List<FixedFaction> factions)
    {
        List<Actor> council = Council(empire);
        var result = new Dictionary<FixedFaction, float>();
        foreach (FixedFaction faction in factions)
        {
            float ratio = Mathf.Max(0, faction.CentralRatio);
            if (council.Count == 0) { result[faction] = ratio; continue; }
            int seats = council.Count(actor => actor.GetFaction() == faction);
            result[faction] = ratio * 0.5f + 100f * seats / council.Count * 0.5f;
        }
        return result;
    }

    public static TemporaryFaction FindClaim(Empire empire, CourtMemorial memorial)
    {
        FixedFaction proposer = FindFaction(empire, memorial?.proposer);
        if (proposer?.TemporaryFactions == null || !Enum.TryParse(memorial.claim, out TemporaryFactionType type)) return null;
        return proposer.TemporaryFactions.FirstOrDefault(claim => claim != null && claim.type == type);
    }

    public static bool IsMemorialClaim(Empire empire, TemporaryFaction claim)
    {
        CourtMemorial memorial = State(empire)?.memorial;
        return memorial != null && claim != null && claim.factionID == memorial.proposer &&
               claim.type.ToString() == memorial.claim;
    }

    public static string IssueName(string claim) =>
        Enum.TryParse(claim, out TemporaryFactionType type) ? IssueName(type) : claim;

    public static string IssueName(TemporaryFactionType type) => TranslateHelper.GetTemporaryFactionClaimText(type);

    // 施行速度：议事会里支持的比例越高越快，反对派掣肘拖慢
    public static float ProgressMultiplier(Empire empire, TemporaryFaction claim)
    {
        if (!Applies(empire) || !IsMemorialClaim(empire, claim)) return 1f;
        CourtState state = State(empire);
        List<Actor> council = Council(empire);
        float councilSupport = council.Count == 0
            ? Mathf.Clamp01(state.memorial.support / 100f)
            : council.Count(actor => Supports(state.memorial, actor.GetFaction())) / (float)council.Count;
        float obstruction = state.memorial.stances.Where(pair => pair.Value < 0)
            .Sum(pair => state.factions.TryGetValue(pair.Key, out CourtFactionState data) ? data.obstruction : 0f);
        return (0.6f + 0.9f * councilSupport) * (1f - Mathf.Min(0.6f, obstruction));
    }

    private static bool Supports(CourtMemorial memorial, FixedFaction faction) =>
        faction != null && memorial.stances.TryGetValue(faction.GetID(), out int stance) && stance > 0;

    #endregion

    #region 每月推进

    public static void Tick(Empire empire)
    {
        CourtState state = State(empire);
        if (state == null || World.world == null) return;
        double now = World.world.getCurWorldTime();
        EnsureBureau(empire);
        YearlyIfDue(empire, state, now);
        state.last_month = now;
        CourtMemorial memorial = state.memorial;
        if (memorial == null)
        {
            if (empire.RunningTemporaryFaction?.IsStarted() != true) TryPropose(empire, state, now);
            return;
        }
        TemporaryFaction claim = FindClaim(empire, memorial);
        FixedFaction proposer = FindFaction(empire, memorial.proposer);
        if (claim == null || proposer == null)
        {
            Close(empire, state, "court_log_void", IssueName(memorial.claim));
            return;
        }
        if (memorial.stage == 4)
        {
            MonitorExecution(empire, state, memorial, claim, proposer, now);
            return;
        }
        int wait = memorial.stage == 3 ? ShelveMonths : StageMonths;
        if (memorial.stage_time >= 0d && Date.getMonthsSince(memorial.stage_time) < wait) return;
        switch (memorial.stage)
        {
            case 0:
                Draft(empire, state, memorial, claim, proposer, now);
                break;
            case 1:
                Debate(empire, state, memorial, claim, proposer, now);
                break;
            case 2:
                Decide(empire, state, memorial, claim, proposer, now, null);
                break;
            case 3:
                memorial.stage = 2;
                memorial.stage_time = now;
                break;
        }
    }

    private static void TryPropose(Empire empire, CourtState state, double now)
    {
        ClaimAgendaContext context = ClaimAgendaSystem.BuildContext(empire);
        var candidates = new List<(FixedFaction faction, TemporaryFaction claim, float score)>();
        foreach (FixedFaction faction in Factions(empire))
        {
            CourtFactionState data = FactionState(empire, faction);
            if (data.capital < ProposalCost || IsBoycotting(data) || faction.TemporaryFactions == null) continue;
            foreach (TemporaryFaction claim in faction.TemporaryFactions)
            {
                if (claim == null || !claim.Active || claim.CountDown > 0 || claim.IsStarted() ||
                    CourtIssues.IsDirectAction(claim.type) || claim.CanProceedOutsideGovernment) continue;
                if (data.cooldowns.TryGetValue(claim.type.ToString(), out double until) && now < until) continue;
                // 国库出不起施行经费的不上奏
                if (!claim.CanAffordBudget()) continue;
                float stance = CourtIssues.Stance(faction, claim.type);
                if (stance < CourtIssues.SupportThreshold) continue;
                ClaimAgendaView view = ClaimAgendaSystem.Evaluate(context, faction, claim);
                if (!view.CanPropose) continue;
                float score = view.Score + stance + faction.CentralRatio * 0.3f + (data.favor - 50f) * 0.2f +
                              Random.Range(0f, 6f);
                candidates.Add((faction, claim, score));
            }
        }
        foreach ((FixedFaction faction, TemporaryFaction claim, float _) in candidates.OrderByDescending(item => item.score))
        {
            if (!claim.CheckCondition() || !claim.CheckTarget()) continue;
            if (CourtIssues.Stance(faction, claim) < CourtIssues.SupportThreshold) continue;
            CourtFactionState data = FactionState(empire, faction);
            data.capital -= ProposalCost;
            state.memorial = new CourtMemorial
            {
                claim = claim.type.ToString(), proposer = faction.GetID(), stage = 0, stage_time = now
            };
            Log(state, "court_log_propose", faction.Name, IssueName(claim.type));
            return;
        }
    }

    // ② 票拟
    private static void Draft(Empire empire, CourtState state, CourtMemorial memorial, TemporaryFaction claim,
        FixedFaction proposer, double now)
    {
        CourtFactionState data = FactionState(empire, proposer);
        List<Actor> council = Council(empire);
        Actor setter = AgendaSetter(empire);
        if (council.Count > 0 && CouncilKind(empire) != CourtCouncilKind.Zhou)
        {
            // 不在议事会又没有盟友坐席：走言路，未必上达
            bool seated = council.Any(actor => actor.GetFaction() == proposer ||
                                               (!string.IsNullOrEmpty(data.ally) && actor.GetFaction()?.GetID() == data.ally));
            if (!seated && Random.value > 0.6f)
            {
                Reject(empire, state, memorial, proposer, "court_log_unheard", 4f, 1);
                return;
            }
            FixedFaction setterFaction = setter?.GetFaction();
            if (setterFaction != null && setterFaction != proposer && setterFaction.GetID() != data.ally &&
                CourtIssues.StanceLevel(setterFaction, claim) < 0)
            {
                // 议程官反对：花资本疏通，半数能过
                if (data.capital >= LobbyCost && Random.value < 0.5f)
                {
                    data.capital -= LobbyCost;
                    Log(state, "court_log_lobbied", proposer.Name, setter.getName(), IssueName(claim.type));
                }
                else
                {
                    Reject(empire, state, memorial, proposer, "court_log_blocked", 6f, 2, setter.getName());
                    return;
                }
            }
            memorial.drafted_by_ally = setterFaction != null && setterFaction.GetID() == data.ally;
        }
        memorial.stage = 1;
        memorial.stage_time = now;
    }

    // ③ 廷议
    private static void Debate(Empire empire, CourtState state, CourtMemorial memorial, TemporaryFaction claim,
        FixedFaction proposer, double now)
    {
        List<FixedFaction> factions = Factions(empire);
        Dictionary<FixedFaction, float> weights = Weights(empire, factions);
        CourtFactionState proposerData = FactionState(empire, proposer);
        memorial.stances.Clear();
        memorial.weights.Clear();
        float support = 0f, opposition = 0f;
        foreach (FixedFaction faction in factions)
        {
            CourtFactionState data = FactionState(empire, faction);
            int stance = faction == proposer ? 1 : CourtIssues.StanceLevel(faction, claim);
            if (faction != proposer && faction.GetID() == proposerData.ally && stance == 0) stance = 1;
            if (faction != proposer && IsBoycotting(data)) stance = -1;
            memorial.stances[faction.GetID()] = stance;
            memorial.weights[faction.GetID()] = weights[faction];
            if (stance > 0) support += weights[faction];
            else if (stance < 0) opposition += weights[faction];
        }
        memorial.support = support + opposition <= 0f ? 50f : 100f * support / (support + opposition);
        if (memorial.support < DebateThreshold)
        {
            Reject(empire, state, memorial, proposer, "court_log_debate_failed", 5f, 2,
                memorial.support.ToString("0"));
            return;
        }
        Log(state, "court_log_debated", IssueName(claim.type), memorial.support.ToString("0"));
        memorial.stage = 2;
        memorial.stage_time = now;
    }

    // ④ 批红。godDecision 不为空时直接按它办(神力按钮)
    private static void Decide(Empire empire, CourtState state, CourtMemorial memorial, TemporaryFaction claim,
        FixedFaction proposer, double now, string godDecision)
    {
        (Actor decider, string role) = Decider(empire);
        memorial.decider = role;
        // 已准奏、只是在等别的诉求办完的，不再重新裁决
        string decision = godDecision ?? (memorial.decision == "approve" ? "approve" : null);
        if (decision == null)
        {
            float approve = ApprovalChance(empire, memorial, claim.type, proposer);
            decision = Random.value < approve ? "approve"
                : !memorial.shelved_once && Random.value < 0.35f ? "shelve" : "reject";
        }
        string who = decider?.getName() ?? LM.Get("court_decider_" + role);
        // 三省六部：皇帝画敕后门下省审核，门下侍中所属派系反对时可能封驳，奏议退回重议(只封驳一次；神力直批不受封驳)
        if (decision == "approve" && godDecision == null && !memorial.reviewed &&
            CouncilKind(empire) == CourtCouncilKind.ThreeDepartments)
        {
            memorial.reviewed = true;
            Actor reviewer = DepartmentHead(empire, MenxiaOffice);
            FixedFaction reviewerFaction = reviewer?.GetFaction();
            CourtFactionState proposerState = FactionState(empire, proposer);
            if (reviewerFaction != null && reviewerFaction != proposer && reviewerFaction.GetID() != proposerState.ally &&
                CourtIssues.StanceLevel(reviewerFaction, claim) < 0 && Random.value < 0.6f)
            {
                memorial.decision = "";
                memorial.stage = 2;
                memorial.stage_time = now;
                proposerState.grievance = Mathf.Min(100f, proposerState.grievance + 3f);
                Log(state, "court_log_fengbo", reviewer.getName(), IssueName(claim.type));
                return;
            }
        }
        switch (decision)
        {
            case "approve":
                if (empire.RunningTemporaryFaction?.IsStarted() == true)
                {
                    memorial.decision = decision;
                    return;
                }
                claim.pusherType = MetaType.None;
                // 准奏了但国库拿不出施行经费：先留中待国库充裕，再拿不出就作罢
                if (!claim.CanAffordBudget())
                {
                    if (!memorial.shelved_once)
                    {
                        memorial.decision = "approve";
                        memorial.shelved_once = true;
                        memorial.stage = 3;
                        memorial.stage_time = now;
                        Log(state, "court_log_no_funds", IssueName(claim.type), claim.Budget);
                    }
                    else
                    {
                        FactionState(empire, proposer).cooldowns[memorial.claim] = now + 12 * 5d;
                        Close(empire, state, "court_log_no_funds_dropped", IssueName(claim.type));
                    }
                    return;
                }
                claim.Start();
                if (!claim.IsStarted())
                {
                    Close(empire, state, "court_log_start_failed", IssueName(claim.type));
                    return;
                }
                memorial.decision = decision;
                memorial.stage = 4;
                memorial.stage_time = now;
                AdjustAfterRuling(empire, memorial, proposer, true);
                Log(state, "court_log_approved", who, proposer.Name, IssueName(claim.type));
                break;
            case "shelve":
                memorial.decision = decision;
                memorial.shelved_once = true;
                memorial.stage = 3;
                memorial.stage_time = now;
                FactionState(empire, proposer).grievance += 3f;
                Log(state, "court_log_shelved", who, IssueName(claim.type));
                break;
            default:
                AdjustAfterRuling(empire, memorial, proposer, false);
                Reject(empire, state, memorial, proposer, "court_log_rejected", 10f, 3, who);
                break;
        }
    }

    // 神力：直接替批红者裁决(准奏 / 驳回 / 留中)；上奏、票拟、廷议阶段也可以直接批
    public static bool GodDecide(Empire empire, string decision)
    {
        CourtState state = State(empire);
        CourtMemorial memorial = state?.memorial;
        if (memorial == null || memorial.stage >= 4) return false;
        TemporaryFaction claim = FindClaim(empire, memorial);
        FixedFaction proposer = FindFaction(empire, memorial.proposer);
        if (claim == null || proposer == null) return false;
        if (memorial.stances.Count == 0)
            foreach (FixedFaction faction in Factions(empire))
                memorial.stances[faction.GetID()] = faction == proposer ? 1 : CourtIssues.StanceLevel(faction, claim);
        if (decision == "shelve")
        {
            memorial.shelved_once = true;
            memorial.stage = 3;
            memorial.stage_time = World.world.getCurWorldTime();
            Log(state, "court_log_shelved", LM.Get("court_decider_emperor"), IssueName(claim.type));
            return true;
        }
        Decide(empire, state, memorial, claim, proposer, World.world.getCurWorldTime(), decision);
        return true;
    }

    // 批红者：成年、未被挟制的皇帝亲批；否则权臣代批，再没有就由议程官代批
    public static (Actor decider, string role) Decider(Empire empire)
    {
        Actor emperor = empire?.Emperor;
        bool emperorRules = emperor?.data != null && !emperor.isRekt() && emperor.isAlive() && emperor.isAdult() &&
                            !empire.data.is_been_controlled;
        if (emperorRules) return (emperor, "emperor");
        Actor minister = empire?.GetPowerfulMinister();
        if (minister != null) return (minister, "minister");
        Actor setter = AgendaSetter(empire);
        return setter != null ? (setter, "regent") : (null, "regent");
    }

    public static float ApprovalChance(Empire empire, CourtMemorial memorial, TemporaryFactionType issue,
        FixedFaction proposer)
    {
        CourtState state = State(empire);
        CourtFactionState data = FactionState(empire, proposer);
        float support = Mathf.Clamp(memorial.support < 0f ? 50f : memorial.support, 0f, 100f) / 100f;
        (Actor decider, string role) = Decider(empire);
        if (role != "emperor")
        {
            FixedFaction deciderFaction = decider?.GetFaction();
            float own = deciderFaction == proposer ? 0.35f
                : deciderFaction == null ? 0f : CourtIssues.StanceLevel(deciderFaction, issue) * 0.2f;
            return Mathf.Clamp(0.2f + support * 0.5f + own, 0.05f, 0.95f);
        }
        float authority = Mathf.Clamp01(empire.Legitimacy / 100f);
        float chance = 0.1f + (0.75f - 0.35f * authority) * support + (data.favor - 50f) / 150f +
                       EmperorStance(empire.Emperor, issue) * 0.2f;
        // 秦汉相权：丞相所属派系的立场牵制皇帝的制可
        if (CouncilKind(empire) == CourtCouncilKind.Han)
        {
            FixedFaction chancellorFaction = DepartmentHead(empire, ChancellorOffice)?.GetFaction();
            if (chancellorFaction == proposer) chance += 0.15f;
            else if (chancellorFaction != null) chance += CourtIssues.StanceLevel(chancellorFaction, issue) * 0.15f;
        }
        switch (state.strategy)
        {
            case "balance" when state.strategy_target == proposer.GetID():
                chance -= 0.15f;
                break;
            case "favorite":
                chance += state.strategy_target == proposer.GetID() ? 0.25f : -0.05f;
                break;
            case "suppress" when state.strategy_target == proposer.GetID():
                chance -= 0.35f;
                break;
        }
        return Mathf.Clamp(chance, 0.05f, 0.95f);
    }

    // 皇帝本人的立场：所属派系的立场，加上性格(好大喜功、贪财、多疑……)
    public static float EmperorStance(Actor emperor, TemporaryFactionType issue)
    {
        if (emperor?.data == null) return 0f;
        float stance = emperor.GetFaction() == null ? 0f : CourtIssues.StanceLevel(emperor.GetFaction(), issue);
        bool expansion = issue is TemporaryFactionType.对外扩张 or TemporaryFactionType.迫使朝贡 or
            TemporaryFactionType.扩张地盘 or TemporaryFactionType.谋求统一;
        bool appeasement = issue is TemporaryFactionType.提供岁币 or TemporaryFactionType.割让城池;
        bool devolution = issue is TemporaryFactionType.分封 or TemporaryFactionType.确认诸侯特权 or
            TemporaryFactionType.允许诸侯自由开战 or TemporaryFactionType.颁布金玺诏书;
        bool centralise = issue is TemporaryFactionType.削藩 or TemporaryFactionType.夺取诸侯开战权 or
            TemporaryFactionType.恢复世袭皇权 or TemporaryFactionType.颁布帝国治安令;
        if (emperor.hasTrait("ambitious")) stance += expansion ? 1f : appeasement ? -1f : 0f;
        if (emperor.hasTrait("greedy")) stance += issue == TemporaryFactionType.提高赋税 ? 1f :
            issue is TemporaryFactionType.降低赋税 ? -1f : issue == TemporaryFactionType.赈济灾荒 ? -0.5f : 0f;
        if (emperor.hasTrait("content")) stance += appeasement ? 0.5f : expansion ? -0.5f : 0f;
        if (emperor.hasTrait("paranoid")) stance += devolution ? -1f : centralise ? 0.5f :
            issue == TemporaryFactionType.整顿吏治 ? 0.5f : 0f;
        stance += devolution ? -0.5f : centralise ? 0.5f : 0f;
        return Mathf.Clamp(stance, -1.5f, 1.5f);
    }

    private static void AdjustAfterRuling(Empire empire, CourtMemorial memorial, FixedFaction proposer, bool approved)
    {
        CourtFactionState proposerData = FactionState(empire, proposer);
        if (approved) proposerData.favor = Mathf.Min(100f, proposerData.favor + 2f);
        else proposerData.favor = Mathf.Max(0f, proposerData.favor - 2f);
        foreach (KeyValuePair<string, int> pair in memorial.stances)
        {
            if (pair.Key == proposer.GetID() || !FactionExists(empire, pair.Key)) continue;
            CourtFactionState data = State(empire).factions.TryGetValue(pair.Key, out CourtFactionState found) ? found : null;
            if (data == null) continue;
            if (approved && pair.Value < 0) data.grievance = Mathf.Min(100f, data.grievance + 4f);
            if (approved && pair.Value > 0) data.favor = Mathf.Min(100f, data.favor + 1f);
        }
    }

    private static bool FactionExists(Empire empire, string id) => FindFaction(empire, id) != null;

    // ⑤ 施行：诉求结束后结算
    private static void MonitorExecution(Empire empire, CourtState state, CourtMemorial memorial, TemporaryFaction claim,
        FixedFaction proposer, double now)
    {
        if (claim.IsStarted()) return;
        CourtFactionState data = FactionState(empire, proposer);
        data.cooldowns[memorial.claim] = now + 2 * 12 * 5d;
        if (claim.LastRunCompleted)
        {
            data.favor = Mathf.Min(100f, data.favor + 3f);
            empire.CoreKingdom.TryIncreaseFactionRatio(proposer, 2);
            foreach (KeyValuePair<string, int> pair in memorial.stances.Where(pair => pair.Value < 0))
                if (state.factions.TryGetValue(pair.Key, out CourtFactionState opposed))
                    opposed.grievance = Mathf.Min(100f, opposed.grievance + 6f);
            string outcome = claim.CompletionOutcome ?? ClaimTuning.Outcome(claim.type);
            if (string.IsNullOrWhiteSpace(outcome))
                Close(empire, state, "court_log_completed", IssueName(claim.type), proposer.Name);
            else
                Close(empire, state, "court_log_completed_outcome", IssueName(claim.type), proposer.Name, outcome);
        }
        else
        {
            data.grievance = Mathf.Min(100f, data.grievance + 4f);
            Close(empire, state, "court_log_aborted", IssueName(claim.type));
        }
    }

    private static void Reject(Empire empire, CourtState state, CourtMemorial memorial, FixedFaction proposer,
        string logKey, float grievance, int cooldownYears, params object[] extra)
    {
        CourtFactionState data = FactionState(empire, proposer);
        data.grievance = Mathf.Min(100f, data.grievance + grievance);
        data.cooldowns[memorial.claim] = World.world.getCurWorldTime() + cooldownYears * 12 * 5d;
        var args = new List<object> { proposer.Name, IssueName(memorial.claim) };
        args.AddRange(extra);
        Close(empire, state, logKey, args.ToArray());
    }

    private static void Close(Empire empire, CourtState state, string logKey, params object[] args)
    {
        Log(state, logKey, args);
        state.memorial = null;
    }

    public static void Log(CourtState state, string key, params object[] args)
    {
        if (state == null) return;
        string text;
        try
        {
            text = args.Length == 0 ? LM.Get(key) : string.Format(LM.Get(key), args);
        }
        catch (FormatException)
        {
            text = LM.Get(key);
        }
        state.log.Insert(0, new CourtLogEntry { time = World.world?.getCurWorldTime() ?? 0d, text = text });
        if (state.log.Count > LogSize) state.log.RemoveRange(LogSize, state.log.Count - LogSize);
    }

    #endregion
}
