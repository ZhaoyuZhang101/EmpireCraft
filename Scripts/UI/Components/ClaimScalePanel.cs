using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国的「诉求天平」页(用户 2026-11-10 "推动诉求也要加天平页面""这样才能知道成功还是没成功")：
//   ① 当前奏议：四步各自的读数(票拟上达的把握、廷议赞成率对三成五、批红准奏率、施行进度)，再逐项列出下一步的砝码；
//   ② 正在施行、不走奏议的诉求(议会执政党、直接行动、地方推上来的)：进度与每月速度的各项倍数；
//   ③ 地方推动：成员国君主推本派诉求，每月加几点(按政绩分档)、名望够不够、还要几个月；
//   ④ 往期奏议：每件成没成、卡在哪一步、廷议与批红的读数，悬停看当时各派表态与砝码。
public static class ClaimScalePanel
{
    private const float CardPadding = 12f;

    public static void Render(AutoVertLayoutGroup page, Empire empire, float width)
    {
        if (page == null || empire?.data == null || empire.isRekt()) return;
        bool court = CourtSystem.Applies(empire);
        var intro = page.BeginCard(width, "claim_scale_title", WideKit.Gold, null, "ui/icons/iconKingdom");
        intro.AddParagraph(width, LM.Get(court ? "claim_scale_intro_court" : "claim_scale_intro_other"), 7, WideKit.Soft);
        if (court) RenderMemorial(page, empire, width);
        else if (ParliamentBillSystem.Applies(empire)) RenderNextBill(page, empire, width);
        RenderRunning(page, empire, width, court);
        RenderLocalPushes(page, empire, width);
        if (court) RenderHistory(page, empire, width);
        else if (ParliamentBillSystem.Applies(empire)) RenderBills(page, empire, width);
    }

    #region 议会表决(见 ParliamentBillSystem)

    // 执政表现与下一件议案的预估：由谁提、提哪一条、各党怎么投
    private static void RenderNextBill(AutoVertLayoutGroup page, Empire empire, float width)
    {
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        float approval = PartyFinanceSystem.Approval(empire);
        int seats = state.parliament_seats?.Count ?? 0;
        (FixedFaction sponsor, TemporaryFaction claim, ParliamentBillSystem.Tally tally) = ParliamentBillSystem.Preview(empire);
        var card = page.BeginCard(width, "claim_scale_bill_next", WideKit.Gold,
            sponsor == null ? null : string.Format(LM.Get("claim_scale_bill_sponsor"), sponsor.Name), "ui/icons/iconKingdom");
        card.AddMetricRow(width,
            (LM.Get("claim_scale_approval"), Mathf.RoundToInt(approval).ToString(), approval >= 50f ? WideKit.Green : approval >= 35f ? WideKit.Gold : WideKit.Red),
            (LM.Get("claim_scale_government_seats"), $"{state.government_seats}/{seats}", state.government_seats * 2 > seats ? WideKit.Green : WideKit.Orange),
            (LM.Get("claim_scale_bill_pause"), ParliamentBillSystem.Paused(empire) ? LM.Get("claim_scale_bill_paused") : LM.Get("claim_scale_bill_ready"),
                ParliamentBillSystem.Paused(empire) ? WideKit.Orange : WideKit.Green));
        if (state.government_approval_parts != null && state.government_approval_parts.Count > 0)
            card.AddParagraph(width, string.Format(LM.Get("claim_scale_approval_parts"), string.Join("  ", state.government_approval_parts
                .Where(pair => Mathf.Abs(pair.Value) >= 0.5f)
                .Select(pair => $"{LM.Get(pair.Key)} {Points(pair.Value / 100f)}"))), 6, WideKit.Muted);
        card.AddParagraph(width, LM.Get("claim_scale_bill_rule"), 6, WideKit.Muted);
        if (claim == null || tally == null)
        {
            card.AddParagraph(width, LM.Get("claim_scale_bill_none"), 7, WideKit.Muted);
            return;
        }
        Heading(card, width, "claim_scale_bill_preview", string.Format(LM.Get("claim_scale_bill_tally"),
            CourtSystem.IssueName(claim.type, empire), tally.Yes, tally.No, tally.Abstain,
            LM.Get(tally.Passed ? "claim_scale_bill_would_pass" : "claim_scale_bill_would_fail")));
        int index = 0;
        foreach ((FixedFaction party, int partySeats, int vote, float stance) in tally.Parties.OrderByDescending(item => item.seats))
        {
            string mark = LM.Get(vote > 0 ? "claim_scale_vote_yes" : vote < 0 ? "claim_scale_vote_no" : "claim_scale_vote_abstain");
            string detail = string.Format(LM.Get("claim_scale_vote_detail"), partySeats, Mathf.RoundToInt(stance),
                Mathf.RoundToInt(PartyRelations.Relation(party, sponsor)));
            card.AddValueRow(width, index++, party.Name, $"{mark} · {detail}", vote > 0 ? WideKit.Green : vote < 0 ? WideKit.Red : WideKit.Muted,
                labelWidth: (width - CardPadding) * 0.4f);
        }
    }

    private static void RenderBills(AutoVertLayoutGroup page, Empire empire, float width)
    {
        List<ParliamentBillRecord> bills = empire.data.constitutional_economy.parliament_bills ?? new List<ParliamentBillRecord>();
        var card = page.BeginCard(width, "claim_scale_bills", WideKit.Gold,
            string.Format(LM.Get("claim_scale_bills_header"), bills.Count(bill => bill.outcome == "completed"), bills.Count), "ui/icons/iconBooks");
        if (bills.Count == 0)
        {
            card.AddParagraph(width, LM.Get("claim_scale_bills_none"), 7, WideKit.Muted);
            return;
        }
        for (int i = 0; i < bills.Count; i++)
        {
            ParliamentBillRecord bill = bills[i];
            int years = Date.getYearsSince(bill.proposed);
            string when = years <= 0 ? LM.Get("court_log_this_year") : string.Format(LM.Get("court_log_years_ago"), years);
            bool good = bill.outcome is "completed" or "running";
            string outcome = LM.Get("claim_scale_bill_outcome_" + (string.IsNullOrEmpty(bill.outcome) ? "rejected" : bill.outcome));
            string text = $"{CourtSystem.IssueName(bill.claim, empire)}({bill.sponsor_name}) — " +
                          outcome.ColorString(good ? WideKit.Green : WideKit.Red) +
                          ("  " + string.Format(LM.Get("claim_scale_bill_votes"), bill.yes, bill.no, bill.abstain)).ColorString(WideKit.Muted);
            var row = card.AddTimelineRow(width, i, when, text, WideKit.Soft);
            var lines = bill.votes.OrderByDescending(pair => bill.seats.TryGetValue(pair.Key, out int s) ? s : 0).Select(pair =>
            {
                string name = bill.names.TryGetValue(pair.Key, out string found) ? found : pair.Key;
                int partySeats = bill.seats.TryGetValue(pair.Key, out int s) ? s : 0;
                return $"{name} {partySeats} · {LM.Get(pair.Value > 0 ? "claim_scale_vote_yes" : pair.Value < 0 ? "claim_scale_vote_no" : "claim_scale_vote_abstain")}";
            });
            Tip(row.gameObject, $"claim_scale_bill_{empire.id}_{i}", CourtSystem.IssueName(bill.claim, empire), string.Join("\n", lines));
        }
    }

    #endregion

    #region 当前奏议

    private static void RenderMemorial(AutoVertLayoutGroup page, Empire empire, float width)
    {
        CourtState state = CourtSystem.State(empire);
        CourtMemorial memorial = state?.memorial;
        if (memorial == null)
        {
            page.AddEmptyCard(width, "claim_scale_memorial", "claim_scale_no_memorial");
            return;
        }
        TemporaryFaction claim = CourtSystem.FindClaim(empire, memorial);
        FixedFaction proposer = CourtSystem.FindFaction(empire, memorial.proposer);
        string issue = CourtSystem.IssueName(memorial.claim, empire);
        var card = page.BeginCard(width, string.Format(LM.Get("court_memorial_title"), issue), WideKit.Gold,
            string.Format(LM.Get("court_memorial_header"), proposer?.Name ?? "", LM.Get("court_status_" + memorial.stage)),
            "ui/icons/iconKingdom");
        if (claim == null || proposer == null)
        {
            card.AddParagraph(width, LM.Get("claim_scale_void"), 7, WideKit.Muted);
            return;
        }

        // 廷议：已议过的照当时的表态，没议的按眼下预估
        List<(string name, int stance, float weight)> stances = memorial.stage >= 2 && memorial.stances.Count > 0
            ? memorial.stances.Select(pair => (CourtSystem.FindFaction(empire, pair.Key)?.Name ?? pair.Key, pair.Value,
                memorial.weights.TryGetValue(pair.Key, out float weight) ? weight : 0f)).ToList()
            : CourtSystem.DebateStances(empire, claim, proposer).Select(item => (item.faction.Name, item.stance, item.weight)).ToList();
        float support = memorial.stage >= 2 && memorial.support >= 0f ? memorial.support : Share(stances);
        bool contest = CourtSystem.CanContest(empire, proposer);
        // 批红：议过的用实际赞成率，没议的拿预估的赞成率估
        var preview = memorial.stage >= 2 ? memorial : new CourtMemorial { support = support, bid = memorial.bid };
        List<CourtScaleFactor> approval = CourtSystem.ApprovalFactors(empire, preview, claim.type, proposer);
        float chance = CourtSystem.Chance(approval);
        List<CourtScaleFactor> draft = memorial.stage == 0 ? CourtSystem.DraftOutlook(empire, memorial, claim, proposer) : null;
        float pass = draft?.Aggregate(1f, (product, factor) => product * factor.value) ?? 1f;

        // 主指标：四步各一格
        string draftText = memorial.stage == 0 ? Percent(pass) : LM.Get("claim_scale_passed");
        string debateText = $"{Mathf.RoundToInt(support)}%/{CourtSystem.DebateThreshold:0}%";
        string decideText = memorial.stage >= 4 ? LM.Get("claim_scale_approved") : Percent(chance);
        string runText = memorial.stage >= 4 ? Percent(claim.progressMax <= 0f ? 0f : claim.progress / claim.progressMax) : "—";
        card.AddMetricRow(width,
            (LM.Get("claim_scale_step_draft"), draftText, memorial.stage == 0 ? Hex(pass) : WideKit.Green),
            (LM.Get("claim_scale_step_debate"), debateText,
                support >= CourtSystem.DebateThreshold ? WideKit.Green : contest ? WideKit.Orange : WideKit.Red),
            (LM.Get("claim_scale_step_decide"), decideText, memorial.stage >= 4 ? WideKit.Green : Hex(chance)),
            (LM.Get("claim_scale_step_run"), runText, memorial.stage >= 4 ? WideKit.Cyan : WideKit.Muted));

        if (draft != null)
        {
            Heading(card, width, "claim_scale_draft_heading", Percent(pass));
            for (int i = 0; i < draft.Count; i++)
                card.AddValueRow(width, i, FactorLabel(draft[i]), Percent(draft[i].value), Hex(draft[i].value), labelWidth: (width - CardPadding) * 0.7f);
        }

        if (memorial.stage <= 2 || memorial.stage == 3)
        {
            Heading(card, width, memorial.stage >= 2 ? "claim_scale_debate_done" : "claim_scale_debate_heading",
                string.Format(LM.Get("claim_scale_debate_value"), Mathf.RoundToInt(support), CourtSystem.DebateThreshold));
            DrawStances(card, width, stances);
            if (support < CourtSystem.DebateThreshold)
                card.AddParagraph(width, LM.Get(contest ? "claim_scale_debate_contest" : "claim_scale_debate_fail"), 6,
                    contest ? WideKit.Orange : WideKit.Red);
        }

        if (memorial.stage < 4)
        {
            Heading(card, width, memorial.stage >= 2 ? "claim_scale_decide_heading" : "claim_scale_decide_preview", Percent(chance));
            DrawApproval(card, width, approval);
            (Actor decider, string role) = CourtSystem.Decider(empire);
            card.AddParagraph(width, string.Format(LM.Get("claim_scale_decider"), LM.Get("court_decider_" + role),
                decider?.getName() ?? LM.Get("label_none")), 6, WideKit.Muted);
            if (memorial.stage == 3 && memorial.roll >= 0f)
                card.AddParagraph(width, RollText(memorial.approval, memorial.roll, memorial.decision), 6, WideKit.Soft);
        }
        else
        {
            if (memorial.roll >= 0f)
                card.AddParagraph(width, RollText(memorial.approval, memorial.roll, "approve"), 6, WideKit.Soft);
            DrawExecution(card, width, claim);
        }
    }

    private static float Share(List<(string name, int stance, float weight)> stances)
    {
        float support = stances.Where(item => item.stance > 0).Sum(item => item.weight);
        float opposition = stances.Where(item => item.stance < 0).Sum(item => item.weight);
        return support + opposition <= 0f ? 50f : 100f * support / (support + opposition);
    }

    // 各派表态：赞成在前、观望居中、反对在后，权重大的在前
    private static void DrawStances(AutoVertLayoutGroup card, float width, List<(string name, int stance, float weight)> stances)
    {
        int index = 0;
        foreach ((string name, int stance, float weight) in stances.OrderByDescending(item => item.stance).ThenByDescending(item => item.weight))
        {
            string mark = LM.Get(stance > 0 ? "court_stance_support" : stance < 0 ? "court_stance_oppose" : "court_stance_neutral");
            card.AddValueRow(width, index++, name, $"{mark} · {weight:0.#}",
                stance > 0 ? WideKit.Green : stance < 0 ? WideKit.Red : WideKit.Muted);
        }
    }

    // 准奏率各项(百分点)，末行是合计
    private static void DrawApproval(AutoVertLayoutGroup card, float width, List<CourtScaleFactor> factors)
    {
        float labelWidth = (width - CardPadding) * 0.75f;
        int index = 0;
        foreach (CourtScaleFactor factor in factors)
        {
            if (Mathf.Abs(factor.value) < 0.005f && factor.key != "court_scale_base") continue;
            card.AddValueRow(width, index++, FactorLabel(factor), Points(factor.value),
                factor.value > 0f ? WideKit.Green : WideKit.Red, labelWidth: labelWidth);
        }
        float sum = factors.Sum(factor => factor.value);
        card.AddValueRow(width, index, LM.Get("claim_scale_decide_total"),
            string.Format(LM.Get("claim_scale_decide_total_value"), Mathf.RoundToInt(sum * 100f), Percent(CourtSystem.Chance(factors))),
            WideKit.Gold, labelWidth: labelWidth);
    }

    // 施行：进度、每月几点(各项倍数)、照这样还要几个月
    private static void DrawExecution(AutoVertLayoutGroup card, float width, TemporaryFaction claim)
    {
        var factors = new List<CourtScaleFactor>();
        float rate = claim.MonthlyRate(factors, out bool blocked);
        float left = Mathf.Max(0f, claim.progressMax - claim.progress);
        string months = blocked ? LM.Get("claim_scale_run_blocked")
            : rate <= 0f ? "—" : string.Format(LM.Get("claim_scale_run_months"), Mathf.CeilToInt(left / rate));
        Heading(card, width, "claim_scale_run_heading",
            string.Format(LM.Get("claim_scale_run_value"), Mathf.RoundToInt(claim.progress), Mathf.RoundToInt(claim.progressMax), rate.ToString("0.00"), months));
        card.AddBarRow(width, 1, LM.Get("claim_scale_step_run"), claim.progressMax <= 0f ? 0f : claim.progress / claim.progressMax,
            months, blocked ? WideKit.Red : WideKit.Cyan, WideKit.Cyan, "#3A4048", barWidth: (width - CardPadding) * 0.45f);
        float labelWidth = (width - CardPadding) * 0.75f;
        for (int i = 0; i < factors.Count; i++)
            card.AddValueRow(width, i, FactorLabel(factors[i]), $"×{factors[i].value:0.00}",
                factors[i].value >= 1f ? WideKit.Green : WideKit.Red, labelWidth: labelWidth);
    }

    #endregion

    #region 不走奏议的诉求、地方推动

    private static void RenderRunning(AutoVertLayoutGroup page, Empire empire, float width, bool court)
    {
        TemporaryFaction run = empire.RunningTemporaryFaction;
        if (run?.IsStarted() != true || court && CourtSystem.IsMemorialClaim(empire, run))
        {
            if (!court) page.AddEmptyCard(width, "claim_scale_running", "claim_scale_no_running");
            return;
        }
        FixedFaction faction = CourtSystem.FindFaction(empire, run.factionID);
        string how = run.IsLocallyPushed ? string.Format(LM.Get("claim_scale_running_local"), run.GetKingdom()?.data?.name ?? "")
            : court ? LM.Get("claim_scale_running_direct") : LM.Get("claim_scale_running_government");
        var card = page.BeginCard(width, string.Format(LM.Get("claim_scale_running_title"), CourtSystem.IssueName(run.type, empire)),
            WideKit.Gold, $"{faction?.Name ?? ""} · {how}", "ui/icons/iconKingdom");
        DrawExecution(card, width, run);
    }

    private static void RenderLocalPushes(AutoVertLayoutGroup page, Empire empire, float width)
    {
        var pushes = new List<(Kingdom kingdom, TemporaryPushProgress push)>();
        foreach (Kingdom kingdom in empire.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom) continue;
            TemporaryPushProgress push = kingdom.GetOrCreate().PushProgress;
            if (push?.StartedToPushTf == true) pushes.Add((kingdom, push));
        }
        var card = page.BeginCard(width, "claim_scale_local", WideKit.Gold,
            string.Format(LM.Get("claim_scale_local_header"), pushes.Count, TemporaryPushProgress.InfluenceCostPerMonth), "ui/icons/iconKingdom");
        card.AddParagraph(width, LM.Get("claim_scale_local_rule"), 6, WideKit.Muted);
        if (pushes.Count == 0)
        {
            card.AddParagraph(width, LM.Get("claim_scale_local_none"), 7, WideKit.Muted);
            return;
        }
        bool busy = empire.RunningTemporaryFaction?.IsStarted() == true;
        for (int i = 0; i < pushes.Count; i++)
        {
            (Kingdom kingdom, TemporaryPushProgress push) = pushes[i];
            (int add, int performance, bool affordable, int months) = push.Outlook();
            Actor pusher = push.GetPusher();
            string value = push.Progress >= 100
                ? LM.Get(busy ? "claim_scale_local_waiting" : "claim_scale_local_ready")
                : !affordable ? LM.Get("claim_scale_local_broke")
                : string.Format(LM.Get(months < 0 ? "claim_scale_local_value_short" : "claim_scale_local_value"), add,
                    pusher?.renown ?? 0, months);
            string label = $"{kingdom.data.name} · {CourtSystem.IssueName(push.TfType, empire)}";
            var row = card.AddBarRow(width, i, label, push.Progress / 100f, value,
                !affordable || months < 0 ? WideKit.Red : WideKit.Soft, WideKit.Gold, "#3A4048",
                labelWidth: (width - CardPadding) * 0.36f, barWidth: (width - CardPadding) * 0.24f);
            Tip(row.gameObject, $"claim_scale_local_{kingdom.id}", label,
                string.Format(LM.Get("claim_scale_local_tip"), pusher?.getName() ?? "", performance, add, push.Progress));
        }
    }

    #endregion

    #region 往期奏议

    private static void RenderHistory(AutoVertLayoutGroup page, Empire empire, float width)
    {
        List<CourtMemorialRecord> history = CourtSystem.State(empire)?.history ?? new List<CourtMemorialRecord>();
        var card = page.BeginCard(width, "claim_scale_history", WideKit.Gold,
            string.Format(LM.Get("claim_scale_history_header"), history.Count(Succeeded), history.Count), "ui/icons/iconBooks");
        if (history.Count == 0)
        {
            card.AddParagraph(width, LM.Get("claim_scale_history_none"), 7, WideKit.Muted);
            return;
        }
        for (int i = 0; i < history.Count; i++)
        {
            CourtMemorialRecord record = history[i];
            int years = Date.getYearsSince(record.closed);
            string when = years <= 0 ? LM.Get("court_log_this_year") : string.Format(LM.Get("court_log_years_ago"), years);
            string outcome = LM.Get("claim_scale_outcome_" + OutcomeKind(record.outcome));
            string text = $"{CourtSystem.IssueName(record.claim, empire)}({record.proposer_name}) — " +
                          outcome.ColorString(Succeeded(record) ? WideKit.Green : OutcomeKind(record.outcome) == "void" ? WideKit.Muted : WideKit.Red) +
                          ReadingText(record);
            var row = card.AddTimelineRow(width, i, when, text, WideKit.Soft);
            Tip(row.gameObject, $"claim_scale_history_{empire.id}_{i}", CourtSystem.IssueName(record.claim, empire), RecordTip(record));
        }
    }

    private static bool Succeeded(CourtMemorialRecord record) => record.outcome is "court_log_completed" or "court_log_completed_outcome";

    private static string OutcomeKind(string logKey) => logKey switch
    {
        "court_log_completed" or "court_log_completed_outcome" => "done",
        "court_log_aborted" => "aborted",
        "court_log_rejected" => "rejected",
        "court_log_debate_failed" => "debate",
        "court_log_unheard" => "unheard",
        "court_log_blocked" => "blocked",
        "court_log_no_funds_dropped" => "funds",
        "court_log_start_failed" => "failed",
        _ => "void"
    };

    // 往期一行后面接读数：廷议赞成率、批红准奏率与掷出的数
    private static string ReadingText(CourtMemorialRecord record)
    {
        var parts = new List<string>();
        if (record.support >= 0f && record.stances.Count > 0)
            parts.Add(string.Format(LM.Get("claim_scale_reading_debate"), Mathf.RoundToInt(record.support), CourtSystem.DebateThreshold));
        if (record.approval >= 0f && record.roll >= 0f)
            parts.Add(string.Format(LM.Get("claim_scale_reading_decide"), Percent(record.approval), Mathf.RoundToInt(record.roll * 100f)));
        else if (!string.IsNullOrEmpty(record.decision) && record.approval < 0f && record.stage >= 2)
            parts.Add(LM.Get("claim_scale_reading_divine"));
        return parts.Count == 0 ? "" : ("  " + string.Join(" · ", parts)).ColorString(WideKit.Muted);
    }

    // 悬停：纪事原文、各派表态与权重、批红的各项砝码
    private static string RecordTip(CourtMemorialRecord record)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(record.text)) lines.Add(record.text);
        if (record.proposed >= 0d)
            lines.Add(string.Format(LM.Get("claim_scale_tip_span"), Date.getYearsSince(record.proposed) - Date.getYearsSince(record.closed)));
        if (record.stances.Count > 0)
        {
            lines.Add(LM.Get("claim_scale_tip_debate").ColorString(WideKit.Gold));
            foreach (KeyValuePair<string, int> pair in record.stances.OrderByDescending(pair => pair.Value))
            {
                string name = record.names.TryGetValue(pair.Key, out string found) ? found : pair.Key;
                float weight = record.weights.TryGetValue(pair.Key, out float value) ? value : 0f;
                string mark = LM.Get(pair.Value > 0 ? "court_stance_support" : pair.Value < 0 ? "court_stance_oppose" : "court_stance_neutral");
                lines.Add($"  {name} {mark} {weight:0.#}");
            }
        }
        if (record.factors.Count > 0)
        {
            lines.Add(LM.Get("claim_scale_tip_decide").ColorString(WideKit.Gold));
            foreach (CourtScaleFactor factor in record.factors.Where(factor => Mathf.Abs(factor.value) >= 0.005f || factor.key == "court_scale_base"))
                lines.Add($"  {FactorLabel(factor)} {Points(factor.value)}");
            if (record.roll >= 0f) lines.Add(RollText(record.approval, record.roll, record.decision));
            if (!string.IsNullOrEmpty(record.decider_name)) lines.Add(string.Format(LM.Get("claim_scale_tip_decider"), record.decider_name));
        }
        return string.Join("\n", lines);
    }

    #endregion

    #region 小件

    private static string FactorLabel(CourtScaleFactor factor)
    {
        string format = LM.Get(factor.key);
        if (string.IsNullOrEmpty(factor.arg)) return format;
        try
        {
            return string.Format(format, factor.arg.Split('|').Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private static string RollText(float approval, float roll, string decision) =>
        string.Format(LM.Get("claim_scale_roll"), Percent(approval), Mathf.RoundToInt(roll * 100f),
            LM.Get("claim_scale_decision_" + (string.IsNullOrEmpty(decision) ? "reject" : decision)));

    private static string Percent(float share) => $"{Mathf.RoundToInt(share * 100f)}%";

    private static string Points(float value)
    {
        int points = Mathf.RoundToInt(value * 100f);
        return points > 0 ? $"+{points}" : points.ToString();
    }

    private static string Hex(float chance) => chance >= 0.6f ? WideKit.Green : chance >= 0.3f ? WideKit.Gold : WideKit.Red;

    // 小标题：左金色说明、右读数
    private static void Heading(AutoVertLayoutGroup card, float width, string key, string value)
    {
        float inner = width - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 13f), TextAnchor.MiddleLeft, 2, new RectOffset(1, 3, 2, 0));
        var left = row.AddTextIntoHoriLayout(LM.Get(key).ColorString(WideKit.Gold), true, TextAnchor.MiddleLeft,
            new Vector2(inner * 0.6f, 12f));
        left.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        var right = row.AddTextIntoHoriLayout((value ?? "").ColorString(WideKit.Value), true, TextAnchor.MiddleRight,
            new Vector2(inner * 0.4f - 6f, 12f));
        right.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(right);
    }

    // 悬停说明：没有图形的组补一张透明图接收指针
    private static void Tip(GameObject target, string key, string title, string body)
    {
        if (target == null || string.IsNullOrWhiteSpace(body)) return;
        if (target.GetComponent<Graphic>() == null)
        {
            Image hitArea = target.AddComponent<Image>();
            hitArea.color = Color.clear;
        }
        UIHelper.AttachTextTooltip(target, key, title, body);
    }

    #endregion
}
