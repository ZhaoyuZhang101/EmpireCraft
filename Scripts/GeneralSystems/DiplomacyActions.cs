using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 外交界面的操作(用户 2026-10-29 问答定"全部开放"；上帝视角，不写"玩家")。a 是焦点国，b 是对方：
//   · 每项一个判定(能做返回 null，不能做返回原因的本地化键)与一个执行；执行前再判一次，执行都算神力所为(见 MembershipCooldown.ByGod)；
//   · 宣战照 WarGate.Forbidden 的顺序逐条给原因(ForbiddenReason)，再看停战、够不够得着(WarReach)，先挑战争目标；
//   · 停战、通好、保护约没有现成的公开入口，按各自系统的字段直接写(到期年限照各系统)；
//   · 好感修正记在 KingdomExtraData.god_opinion，由好感项 opinion_god_favor 读(见 EmpireCraftOpinionAddition)，双向各改一档。
public static class DiplomacyActions
{
    public sealed class Action
    {
        public string key;
        public Func<Kingdom, Kingdom, string> check;
        public Func<Kingdom, Kingdom, WarGoal, string> run;
    }

    public sealed class Group
    {
        public string titleKey;
        public bool withGoals;
        public List<Action> actions = new();
    }

    private const int OpinionStep = 20, OpinionMax = 100;

    public static readonly WarGoal[] Goals =
    {
        WarGoal.夺城, WarGoal.侵略, WarGoal.吞并, WarGoal.迫使附庸, WarGoal.赔款, WarGoal.劫掠, WarGoal.扶立, WarGoal.争法理,
        WarGoal.通商, WarGoal.改宗
    };

    public static string GoalName(WarGoal goal) => WarGoalSystem.Name(goal);

    private static Action Make(string key, Func<Kingdom, Kingdom, string> check, Func<Kingdom, Kingdom, WarGoal, string> run) =>
        new() { key = key, check = check, run = run };

    public static readonly List<Group> Groups = new()
    {
        new Group
        {
            titleKey = "diplomacy_group_war", withGoals = true,
            actions =
            {
                Make("diplomacy_act_declare", CanDeclare, (a, b, goal) =>
                {
                    War war = World.world.diplomacy.startWar(a, b, WarTypeLibrary.normal);
                    if (war == null) return "diplomacy_reason_failed";
                    WarGoalSystem.Preset(war, goal);
                    return null;
                }),
                Make("diplomacy_act_peace", (a, b) => a.isInWarWith(b) ? null : "diplomacy_reason_not_at_war", (a, b, _) =>
                {
                    EndWars(a, b);
                    return null;
                }),
                Make("diplomacy_act_truce", (a, b) => null, (a, b, _) =>
                {
                    EndWars(a, b);
                    DiplomacyRelation relation = World.world.diplomacy.getRelation(a, b);
                    if (relation?.data != null) relation.data.timestamp_last_war_ended = World.world.getCurWorldTime();
                    return null;
                })
            }
        },
        new Group
        {
            titleKey = "diplomacy_group_alliance",
            actions =
            {
                Make("diplomacy_act_ally", (a, b) =>
                {
                    if (DiplomacyLedger.Allied(a, b)) return "diplomacy_reason_already";
                    if (a.isInWarWith(b)) return "diplomacy_reason_at_war";
                    if (ModAllianceService.IsVanillaAllianceBanned()) return "diplomacy_reason_alliance_banned";
                    if (a.IsEmpire() || a.IsInEmpire() || b.IsEmpire() || b.IsInEmpire()) return "diplomacy_reason_empire_alliance";
                    return null;
                }, (a, b, _) => World.world.alliances.forceAlliance(a, b) ? null : "diplomacy_reason_failed"),
                Make("diplomacy_act_leave_alliance", (a, b) => a.hasAlliance() ? null : "diplomacy_reason_no_alliance", (a, b, _) =>
                {
                    Alliance alliance = a.getAlliance();
                    alliance.leave(a);
                    if (alliance.kingdoms_list.Count < 2) World.world.alliances.dissolveAlliance(alliance);
                    return null;
                }),
                Make("diplomacy_act_marry", (a, b) =>
                {
                    if (RoyalMarriageSystem.Allied(a, b)) return "diplomacy_reason_already";
                    return a.isInWarWith(b) ? "diplomacy_reason_at_war" : null;
                }, (a, b, _) => RoyalMarriageSystem.ArrangeHeqin(a, b) != null ? null : "diplomacy_reason_no_match"),
                Make("diplomacy_act_pact", (a, b) =>
                {
                    if (GreatPowerDiplomacy.Bound(a, b)) return "diplomacy_reason_already";
                    if (a.GetEmpire() == null || b.GetEmpire() == null) return "diplomacy_reason_not_empire_both";
                    if (a.IsInSameEmpire(b)) return "diplomacy_reason_same_empire";
                    return a.isInWarWith(b) ? "diplomacy_reason_at_war" : null;
                }, (a, b, _) =>
                {
                    Empire ea = a.GetEmpire(), eb = b.GetEmpire();
                    double now = World.world.getCurWorldTime();
                    (ea.data.diplomatic_pacts ??= new Dictionary<long, double>())[eb.id] = now;
                    (eb.data.diplomatic_pacts ??= new Dictionary<long, double>())[ea.id] = now;
                    string text = string.Format(LM.Get("diplomacy_pact_history"), ea.GetEmpireName(), eb.GetEmpireName());
                    ea.RecordHistory(directContent: text, kingdomId: eb.CoreKingdom?.id ?? -1L);
                    eb.RecordHistory(directContent: text, kingdomId: ea.CoreKingdom?.id ?? -1L);
                    TranslateHelper.LogEventMessage(text, a);
                    return null;
                }),
                Make("diplomacy_act_protect", (a, b) =>
                {
                    if (HolyRomanSystem.ProtectedBy(b, a)) return "diplomacy_reason_already";
                    return a.isInWarWith(b) ? "diplomacy_reason_at_war" : null;
                }, (a, b, _) =>
                {
                    var data = b.GetOrCreate();
                    data.hre_protector_id = a.id;
                    data.hre_protected_at = World.world.getCurWorldTime();
                    data.hre_protected_from_id = -1L;
                    TranslateHelper.LogEventMessage(string.Format(LM.Get("diplomacy_protect_history"), a.GetKingdomFullName(),
                        b.GetKingdomFullName()), a);
                    return null;
                })
            }
        },
        new Group
        {
            titleKey = "diplomacy_group_subjects",
            actions =
            {
                Make("diplomacy_act_tribute", (a, b) =>
                {
                    Empire eb = b.GetEmpire();
                    if (eb == null || eb.CoreKingdom != b) return "diplomacy_reason_not_empire";
                    if (a.GetTakenAllianceEmpire() == eb) return "diplomacy_reason_already";
                    return a.IsInEmpire() ? "diplomacy_reason_in_empire" : null;
                }, (a, b, _) => TributaryRelations.Submit(b, a) ? null : "diplomacy_reason_failed"),
                Make("diplomacy_act_end_tribute", (a, b) =>
                    TributaryOf(a, b) || TributaryOf(b, a) ? null : "diplomacy_reason_no_tribute", (a, b, _) =>
                {
                    (TributaryOf(a, b) ? a : b).RemoveTakenAlliance();
                    return null;
                }),
                Make("diplomacy_act_vassalize", (a, b) =>
                {
                    if (FeudalVassalService.GetOverlord(b) == a) return "diplomacy_reason_already";
                    if (b.IsEmpire() || b.IsInEmpire()) return "diplomacy_reason_in_empire";
                    return FeudalVassalService.IsInChain(a, b) ? "diplomacy_reason_chain" : null;
                }, (a, b, _) => FeudalVassalService.ForceBind(a, b) ? null : "diplomacy_reason_failed"),
                Make("diplomacy_act_release_vassal", (a, b) =>
                    FeudalVassalService.GetOverlord(b) == a || FeudalVassalService.GetOverlord(a) == b ? null : "diplomacy_reason_no_vassal",
                    (a, b, _) =>
                    {
                        FeudalVassalService.Break(FeudalVassalService.GetOverlord(b) == a ? b : a);
                        return null;
                    }),
                Make("diplomacy_act_colonize", CanColonize, (a, b, _) => Colonize(a, b, false)),
                Make("diplomacy_act_charter", CanColonize, (a, b, _) => Colonize(a, b, true)),
                Make("diplomacy_act_free_colony", (a, b) =>
                    ColonySystem.MetropoleOf(b) == a || ColonySystem.MetropoleOf(a) == b ? null : "diplomacy_reason_no_colony",
                    (a, b, _) =>
                    {
                        Kingdom colony = ColonySystem.MetropoleOf(b) == a ? b : a;
                        Kingdom metropole = ColonySystem.MetropoleOf(colony);
                        ColonySystem.Release(colony, string.Format(LM.Get("diplomacy_colony_freed_history"),
                            metropole?.GetKingdomFullName() ?? "", colony.GetKingdomFullName()));
                        return null;
                    }),
                Make("diplomacy_act_rotate_governor", (a, b) =>
                    ColonySystem.MetropoleOf(b) == a && ColonySystem.Of(b)?.kind == Data.ColonyState.Governorate
                        ? null : "diplomacy_reason_no_governorate",
                    (a, b, _) => ColonySystem.RotateGovernor(b) ? null : "diplomacy_reason_no_candidate"),
                Make("diplomacy_act_nationalize", (a, b) =>
                    ColonySystem.MetropoleOf(b) == a && ColonySystem.Of(b)?.kind == Data.ColonyState.Company
                        ? null : "diplomacy_reason_no_company",
                    (a, b, _) => ColonySystem.NationalizeNow(b) ? null : "diplomacy_reason_failed")
            }
        },
        new Group
        {
            titleKey = "diplomacy_group_market",
            actions =
            {
                Make("diplomacy_act_union_in", (a, b) =>
                {
                    if (CommodityMarketSystem.UnionOf(b) == a) return "diplomacy_reason_already";
                    if (a.isInWarWith(b)) return "diplomacy_reason_at_war";
                    return DiplomacyLedger.SameMarket(a, b) ? "diplomacy_reason_same_market" : null;
                }, (a, b, _) => CommodityMarketSystem.JoinUnion(b, a)),
                Make("diplomacy_act_union_out", (a, b) =>
                    CommodityMarketSystem.UnionOf(b) == a || CommodityMarketSystem.UnionOf(a) == b ? null : "diplomacy_reason_no_union",
                    (a, b, _) => CommodityMarketSystem.LeaveUnion(CommodityMarketSystem.UnionOf(b) == a ? b : a))
            }
        },
        new Group
        {
            titleKey = "diplomacy_group_opinion",
            actions =
            {
                Make("diplomacy_act_opinion_up", (a, b) => null, (a, b, _) => ShiftOpinion(a, b, OpinionStep)),
                Make("diplomacy_act_opinion_down", (a, b) => null, (a, b, _) => ShiftOpinion(a, b, -OpinionStep)),
                Make("diplomacy_act_opinion_reset", (a, b) => GodOpinion(a, b) != 0 || GodOpinion(b, a) != 0 ? null : "diplomacy_reason_no_god_opinion",
                    (a, b, _) =>
                    {
                        a.GetOrCreate().god_opinion?.Remove(b.id);
                        b.GetOrCreate().god_opinion?.Remove(a.id);
                        return null;
                    })
            }
        }
    };

    private static bool Valid(Kingdom a, Kingdom b) =>
        a?.data != null && b?.data != null && !a.isRekt() && !b.isRekt() && a != b;

    public static string Check(Action action, Kingdom a, Kingdom b) => !Valid(a, b) ? "diplomacy_reason_invalid" : action.check(a, b);

    public static string Do(Action action, Kingdom a, Kingdom b, WarGoal goal)
    {
        string reason = Check(action, a, b);
        if (reason != null) return reason;
        return MembershipCooldown.ByGod(() => action.run(a, b, goal));
    }

    // ───────── 宣战 ─────────
    private static string CanDeclare(Kingdom a, Kingdom b)
    {
        if (a.isInWarWith(b)) return "diplomacy_reason_at_war";
        string forbidden = ForbiddenReason(a, b);
        if (forbidden != null) return forbidden;
        double ended = World.world.diplomacy?.getRelation(a, b)?.data?.timestamp_last_war_ended ?? -1d;
        if (ended > 0d && Date.getYearsSince(ended) < SimGlobals.m.minimum_years_between_wars) return "diplomacy_reason_truce";
        return WarReach.MayDeclare(a, b, WarTypeLibrary.normal) ? null : "diplomacy_reason_unreachable";
    }

    // 不论强弱都不打的关系，与 WarGate.Forbidden 同一顺序，逐条给原因
    public static string ForbiddenReason(Kingdom a, Kingdom b)
    {
        if (NationalSentimentSystem.InSameUnitedFront(a, b)) return "diplomacy_reason_united_front";
        if (EmpirePartitionSystem.InAmity(a, b)) return "diplomacy_reason_amity";
        if (TribalFissionService.Bound(a, b)) return "diplomacy_reason_tribal";
        if (GreatPowerDiplomacy.Bound(a, b)) return "diplomacy_reason_pact";
        if (WarGoalSystem.Installed(a, b)) return "diplomacy_reason_installed";
        if (a.IsInSameEmpire(b)) return "diplomacy_reason_same_empire";
        Empire own = a.GetEmpire(), theirs = b.GetEmpire();
        if (own != null && (b.GetGivenAllianceEmpire() == own || b.GetTakenAllianceEmpire() == own) ||
            theirs != null && (a.GetGivenAllianceEmpire() == theirs || a.GetTakenAllianceEmpire() == theirs))
            return "diplomacy_reason_tributary";
        if (DiplomacyLedger.Allied(a, b)) return "diplomacy_reason_allied";
        if (RoyalMarriageSystem.Allied(a, b)) return "diplomacy_reason_married";
        if (FeudalVassalService.IsInChain(a, b) || FeudalVassalService.IsInChain(b, a)) return "diplomacy_reason_chain";
        if (ColonySystem.Bound(a, b)) return "diplomacy_reason_colony";
        if (TransmigrantSystem.DisguiseForbids(a, b)) return "diplomacy_reason_disguise";
        if (HolyRomanSystem.ProtectedBy(a, b) || HolyRomanSystem.ProtectedBy(b, a)) return "diplomacy_reason_protected";
        if (MembershipCooldown.TruceForbids(a, b)) return "diplomacy_reason_god_truce";
        if (a.king != null && a.king == b.king) return "diplomacy_reason_same_ruler";
        return WarGate.Forbidden(a, b) ? "diplomacy_reason_forbidden" : null;
    }

    private static void EndWars(Kingdom a, Kingdom b)
    {
        foreach (War war in World.world.wars.list.Where(war => war != null && !war.hasEnded() && war.isInWarWith(a, b)).ToList())
            World.world.wars.endWar(war, WarWinner.Peace);
    }

    // ───────── 宗属 ─────────
    private static bool TributaryOf(Kingdom subject, Kingdom lord)
    {
        Empire empire = lord.GetEmpire();
        return empire != null && empire.CoreKingdom == lord && subject.GetTakenAllianceEmpire() == empire;
    }

    private static string CanColonize(Kingdom a, Kingdom b)
    {
        if (ColonySystem.MetropoleOf(b) == a) return "diplomacy_reason_already";
        if (ColonySystem.IsColony(b)) return "diplomacy_reason_already_colony";
        if (ColonySystem.IsColony(a)) return "diplomacy_reason_colony_cannot";
        if (b.IsEmpire() || b.IsInEmpire()) return "diplomacy_reason_in_empire";
        return FeudalVassalService.IsInChain(a, b) ? "diplomacy_reason_chain" : null;
    }

    private static string Colonize(Kingdom a, Kingdom b, bool company)
    {
        if (FeudalVassalService.GetOverlord(b) != null) FeudalVassalService.Break(b);
        if (b.HasTakenAlliance()) b.RemoveTakenAlliance();
        if (CommodityMarketSystem.UnionOf(b) != null) CommodityMarketSystem.LeaveUnion(b);
        ColonySystem.Annex(a, b, company);
        return ColonySystem.MetropoleOf(b) == a ? null : "diplomacy_reason_failed";
    }

    // ───────── 好感修正 ─────────
    // 好感项 opinion_god_favor(见 EmpireCraftOpinionAddition)：main 对 target 的神力修正
    // ───────── 剧本 ─────────
    // 剧本开局按史实套外交关系(世界地图各时代，见 WorldMapScenario；用户 10-29 "新的市场系统和外交关系系统记得适配所有剧本")。
    // kind：war(a 向 b 宣战，goal 定目标)、truce(停战)、alliance(结盟)、marriage(和亲联姻)、pact(通好)、protect(a 保护 b)、
    // tribute(a 向帝国 b 称臣纳贡)、vassal(a 立 b 为附庸)、union(b 并入 a 的市场)、friend/feud(双方旧谊/宿怨，value 默认 ±30)。
    // 走外交界面同一套操作与判定(做不了的返回原因键，不硬套)；成了返回 null
    public static readonly Dictionary<string, string> ScenarioKinds = new()
    {
        ["war"] = "diplomacy_act_declare", ["truce"] = "diplomacy_act_truce", ["alliance"] = "diplomacy_act_ally",
        ["marriage"] = "diplomacy_act_marry", ["pact"] = "diplomacy_act_pact", ["protect"] = "diplomacy_act_protect",
        ["tribute"] = "diplomacy_act_tribute", ["vassal"] = "diplomacy_act_vassalize", ["union"] = "diplomacy_act_union_in"
    };

    private const int HistoryOpinionDefault = 30, HistoryOpinionMax = 60;
    private const float HistoryFadeYears = 60f;

    public static string ApplyScenario(string kind, Kingdom a, Kingdom b, WarGoal goal = WarGoal.夺城, int value = 0)
    {
        if (!Valid(a, b)) return "diplomacy_reason_invalid";
        if (kind is "friend" or "feud")
        {
            int amount = value != 0 ? Mathf.Abs(value) : HistoryOpinionDefault;
            if (kind == "feud") amount = -amount;
            double now = World.world.getCurWorldTime();
            foreach ((Kingdom main, Kingdom target) in new[] { (a, b), (b, a) })
            {
                var data = main.GetOrCreate();
                data.history_opinion ??= new Dictionary<long, int>();
                data.history_opinion[target.id] = Mathf.Clamp(amount, -HistoryOpinionMax, HistoryOpinionMax);
                data.history_opinion_at = now;
            }
            return null;
        }
        if (!ScenarioKinds.TryGetValue(kind ?? "", out string key)) return "diplomacy_reason_invalid";
        Action action = Groups.SelectMany(group => group.actions).FirstOrDefault(item => item.key == key);
        return action == null ? "diplomacy_reason_invalid" : Do(action, a, b, goal);
    }

    // 旧谊、宿怨：开局的值从开局起 HistoryFadeYears 年里线性淡去
    public static int HistoryOpinion(Kingdom main, Kingdom target)
    {
        if (main?.data == null || target == null) return 0;
        var data = main.GetOrCreate();
        if (data.history_opinion == null || !data.history_opinion.TryGetValue(target.id, out int value)) return 0;
        float years = data.history_opinion_at < 0d ? 0f : Date.getYearsSince(data.history_opinion_at);
        return Mathf.RoundToInt(value * Mathf.Clamp01(1f - years / HistoryFadeYears));
    }

    public static int GodOpinion(Kingdom main, Kingdom target) =>
        main?.data != null && target != null && main.GetOrCreate().god_opinion is { } map && map.TryGetValue(target.id, out int value)
            ? value : 0;

    private static string ShiftOpinion(Kingdom a, Kingdom b, int step)
    {
        foreach ((Kingdom main, Kingdom target) in new[] { (a, b), (b, a) })
        {
            var map = main.GetOrCreate().god_opinion ??= new Dictionary<long, int>();
            int value = Mathf.Clamp(GodOpinion(main, target) + step, -OpinionMax, OpinionMax);
            if (value == 0) map.Remove(target.id);
            else map[target.id] = value;
        }
        return null;
    }
}
