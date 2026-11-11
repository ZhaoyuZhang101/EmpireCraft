using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝堂上的神力(用户 2026-11-11 "玩家也能自己主动发起提议""这里玩家为什么不能自己加砝码""和编辑")：
//   · 神力上奏：替某派把它的一条诉求递上去(不花政治资本)，照常走票拟、串联、廷议、批红；有议会的直接付表决；
//   · 改表态：廷议上某派的表态按神意改成赞成/观望/反对(诉求天平页点那一派的行)，已廷议过的当场重算赞成率；
//   · 神意砝码：批红准奏率另加祥瑞、万民称颂(加)或天象示警、舆情汹汹(减)，各三档；
//   · 施行快慢：施行中的奏议速度 ×0.5 ~ ×2；
//   · 改派系处境：政治资本、圣眷、怨望各加减(派系窗口的派务页)。
public static partial class CourtSystem
{
    public static readonly string[] DivinePositive = { "omen_good", "acclaim" };
    public static readonly string[] DivineNegative = { "omen_bad", "outcry" };
    public static readonly int[] DivineTiers = { 5, 10, 20 };
    public static readonly float[] DivineRates = { 1f, 1.5f, 2f, 0.5f };

    #region 神力上奏

    // 为什么递不上去(null 为递得上)
    public static string CannotGodPropose(Empire empire, FixedFaction faction, TemporaryFaction claim)
    {
        if (empire?.data == null || faction == null || claim == null) return "court_god_propose_invalid";
        if (faction.Ban) return "court_god_propose_banned";
        if (!claim.Active) return "agenda_status_inactive";
        if (claim.IsStarted()) return "agenda_status_running";
        if (claim.CountDown > 0) return "agenda_status_cooldown";
        if (empire.RunningTemporaryFaction?.IsStarted() == true) return "court_god_propose_running";
        if (CourtIssues.IsDirectAction(claim.type)) return "court_god_propose_direct";
        if (Applies(empire) && State(empire)?.memorial != null) return "court_god_propose_busy";
        if (!Applies(empire) && !ParliamentBillSystem.Applies(empire)) return "court_god_propose_invalid";
        if (!claim.CheckCondition() || !claim.CheckTarget()) return "court_god_propose_condition";
        if (!claim.CanAffordBudget()) return "court_god_propose_budget";
        return null;
    }

    public static bool GodPropose(Empire empire, FixedFaction faction, TemporaryFaction claim)
    {
        if (CannotGodPropose(empire, faction, claim) != null) return false;
        if (Applies(empire))
        {
            CourtState state = State(empire);
            double now = World.world.getCurWorldTime();
            // 下个月就票拟
            state.memorial = new CourtMemorial
            {
                claim = claim.type.ToString(), proposer = faction.GetID(), stage = 0, stage_time = now - StageMonths * 5d, proposed = now
            };
            Log(state, "court_log_god_propose", faction.Name, IssueName(claim.type, empire));
            return true;
        }
        ParliamentBillSystem.Propose(empire, faction, claim);
        return true;
    }

    #endregion

    #region 改表态、神意砝码、施行快慢

    // 当前奏议还能改表态与批红砝码(没批红准奏)
    public static bool CanEditDebate(Empire empire, out CourtMemorial memorial)
    {
        memorial = State(empire)?.memorial;
        return memorial != null && memorial.stage <= 3 && memorial.decision != "approve";
    }

    // 改某派的表态：原样 → 赞成 → 观望 → 反对 → 原样
    public static void CycleStance(Empire empire, string factionId)
    {
        if (!CanEditDebate(empire, out CourtMemorial memorial) || factionId == memorial.proposer) return;
        memorial.stance_overrides ??= new Dictionary<string, int>();
        if (!memorial.stance_overrides.TryGetValue(factionId, out int current)) memorial.stance_overrides[factionId] = 1;
        else if (current == 1) memorial.stance_overrides[factionId] = 0;
        else if (current == 0) memorial.stance_overrides[factionId] = -1;
        else memorial.stance_overrides.Remove(factionId);
        TemporaryFaction claim = FindClaim(empire, memorial);
        FixedFaction proposer = FindFaction(empire, memorial.proposer);
        if (claim != null && proposer != null) Retally(empire, memorial, claim, proposer);
    }

    public static bool StanceOverridden(CourtMemorial memorial, string factionId) =>
        memorial?.stance_overrides != null && memorial.stance_overrides.ContainsKey(factionId);

    public static int DivineApproval(CourtMemorial memorial, string key) =>
        Mathf.RoundToInt((memorial?.god_factors?.FirstOrDefault(factor => factor.key == "court_scale_divine_" + key)?.value ?? 0f) * 100f);

    // 神意砝码：没有 → 一档 → 二档 → 三档 → 去掉
    public static void CycleDivineApproval(Empire empire, string key)
    {
        if (!CanEditDebate(empire, out CourtMemorial memorial)) return;
        memorial.god_factors ??= new List<CourtScaleFactor>();
        string full = "court_scale_divine_" + key;
        int sign = DivineNegative.Contains(key) ? -1 : 1;
        int current = Mathf.Abs(DivineApproval(memorial, key));
        memorial.god_factors.RemoveAll(factor => factor.key == full);
        int index = global::System.Array.IndexOf(DivineTiers, current);
        int next = current == 0 ? DivineTiers[0] : index >= 0 && index + 1 < DivineTiers.Length ? DivineTiers[index + 1] : 0;
        if (next > 0) memorial.god_factors.Add(new CourtScaleFactor { key = full, value = sign * next / 100f });
    }

    public static void CycleRate(Empire empire)
    {
        CourtMemorial memorial = State(empire)?.memorial;
        if (memorial == null) return;
        int index = global::System.Array.FindIndex(DivineRates, rate => Mathf.Abs(rate - memorial.rate_factor) < 0.001f);
        memorial.rate_factor = DivineRates[(index + 1) % DivineRates.Length];
    }

    #endregion

    #region 改派系处境

    // field：capital 政治资本 / favor 圣眷 / grievance 怨望
    public static void AdjustFaction(Empire empire, FixedFaction faction, string field, float delta)
    {
        if (empire == null || faction == null) return;
        CourtFactionState data = FactionState(empire, faction);
        switch (field)
        {
            case "capital": data.capital = Mathf.Clamp(data.capital + delta, 0f, 100f); break;
            case "favor": data.favor = Mathf.Clamp(data.favor + delta, 0f, 100f); break;
            case "grievance": data.grievance = Mathf.Clamp(data.grievance + delta, 0f, 100f); break;
        }
    }

    #endregion

    #region 强制推进(用户 2026-11-11 "如果有正在推的议题也应该允许玩家强制推进")

    // 神力推进一步：不等时日，照常结算眼下这一步(上奏→票拟、票拟→廷议、廷议→批红按天平掷、留中→再批)；施行中的加三成进度
    public static bool GodAdvance(Empire empire)
    {
        CourtState state = State(empire);
        CourtMemorial memorial = state?.memorial;
        if (memorial == null) return false;
        TemporaryFaction claim = FindClaim(empire, memorial);
        FixedFaction proposer = FindFaction(empire, memorial.proposer);
        if (claim == null || proposer == null) return false;
        double now = World.world.getCurWorldTime();
        switch (memorial.stage)
        {
            case 0:
                Draft(empire, state, memorial, claim, proposer, now);
                return true;
            case 1:
                Debate(empire, state, memorial, claim, proposer, now);
                return true;
            case 2:
                Decide(empire, state, memorial, claim, proposer, now, null);
                return true;
            case 3:
                memorial.stage = 2;
                Decide(empire, state, memorial, claim, proposer, now, null);
                return true;
            default:
                return GodHasten(claim, HastenShare);
        }
    }

    public const float HastenShare = 0.3f;

    // 施行中的诉求(朝堂、议会、政党、地方推的都算)：加 share 成进度，推满当场施行
    public static bool GodHasten(TemporaryFaction claim, float share)
    {
        if (claim == null || !claim.IsStarted()) return false;
        claim.progress = Mathf.Min(claim.progressMax, claim.progress + claim.progressMax * share);
        if (claim.progress >= claim.progressMax) claim.RunExecute();
        return true;
    }

    #endregion
}
