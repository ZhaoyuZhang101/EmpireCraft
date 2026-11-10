using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 议会表决诉求(用户 2026-11-10 "政党机制也完善下，总感觉现在就是个空壳"，问答选「议会表决诉求」)：
//   · 提案：有责任内阁的由执政一方提——首相党为主，联合政府的友党按议席轮到也能提(首相党权重加倍)；
//     还没有责任内阁(立宪君主国议会初开)的照旧由势力最大的派系提。以前一律由势力最大的派系提，
//     它若不在执政一方，执政党一条诉求也推不了；
//   · 表决：各党按对这条诉求的立场投票——立场 = 阶层利害、纲领与对立诉求(CourtIssues.Stance) + 理念远近
//     (诉求出自哪几种理念的纲领，离得越近越赞成)；执政联盟守党纪(不是强烈反对就投赞成)，在野党立场为正赞成、
//     为负反对、居中弃权；赞成票多过反对票即通过，随后照旧按月施行；
//   · 否决：这条诉求两年内不再提，政府半年内不再提案，提案的党势力 -2；
//   · 记录：每件议案的表决与结局记进 parliament_bills(至多 HistorySize 条)，诉求天平页列出
public static class ParliamentBillSystem
{
    public const int HistorySize = 10;
    public const int FailCooldownYears = 2;
    private const int FailPauseMonths = 6;
    private const double MonthTime = 5d;
    // 执政联盟的党员立场低于这个就倒戈投反对、低于 DisciplineAbstain 就弃权
    private const float DisciplineBreak = -15f, DisciplineAbstain = -5f;
    private const float IdeologyLeanWeight = 10f;

    public sealed class Tally
    {
        public int Yes, No, Abstain;
        public bool Passed;
        public bool GovernmentBill;
        public readonly List<(FixedFaction party, int seats, int vote, float stance)> Parties = new();
    }

    public static bool Applies(Empire empire) =>
        empire?.data?.constitutional_economy != null && !empire.isRekt() && ParliamentSystem.HasParliament(empire);

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    private static FixedFaction Find(Empire empire, string id) =>
        string.IsNullOrEmpty(id) ? null
            : empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()?.FirstOrDefault(faction => faction?.GetID() == id);

    private static double Now => World.world?.getCurWorldTime() ?? 0d;

    // 政府刚被否决过一案，半年内不再提
    public static bool Paused(Empire empire) => (State(empire)?.next_bill_at ?? -1d) > Now;

    // 提案的一方：责任内阁下按议席在执政联盟里抽(首相党加倍)，否则势力最大的派系
    public static FixedFaction Sponsor(Empire empire, Regime regime)
    {
        FixedFaction dominate = regime?.GetDominateFaction();
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !ParliamentSystem.HasResponsibleGovernment(empire)) return dominate;
        FixedFaction leader = PartySystem.GetGovernmentParty(empire);
        var weighted = new List<(FixedFaction party, float weight)>();
        foreach (string id in ParliamentSystem.GetCoalitionIds(empire).Distinct())
        {
            FixedFaction party = Find(empire, id);
            if (party == null || party.Ban || party.TemporaryFactions == null) continue;
            float weight = Seats(state, id) * (party == leader ? 2f : 1f);
            if (weight > 0f) weighted.Add((party, weight));
        }
        if (weighted.Count == 0) return leader ?? dominate;
        float roll = Random.value * weighted.Sum(item => item.weight);
        foreach ((FixedFaction party, float weight) in weighted)
            if ((roll -= weight) <= 0f) return party;
        return weighted[weighted.Count - 1].party;
    }

    private static int Seats(ConstitutionalEconomyState state, string id) =>
        state?.parliament_seats?.Count(seat => seat.faction_id == id) ?? 0;

    // 议员立场：诉求立场 + 理念远近
    public static float Stance(FixedFaction party, TemporaryFaction claim)
    {
        float score = CourtIssues.Stance(party, claim);
        if (party?.IsParty == true && claim != null) score += IdeologyLean(party.Ideology, claim.type);
        return Mathf.Clamp(score, -30f, 30f);
    }

    // 诉求出自哪几种理念的纲领：离其中最近的那一种越近越赞成(相距 0 加 10，100 不加，200 减 10)
    public static float IdeologyLean(PartyIdeology ideology, TemporaryFactionType issue)
    {
        float best = float.MinValue;
        foreach (PartyIdeology other in Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>())
        {
            List<TemporaryFactionType> claims = PartySystem.ClaimsOf(other);
            if (claims == null || !claims.Contains(issue)) continue;
            best = Mathf.Max(best, 1f - PartySystem.IdeologyDistance(ideology, other) / 100f);
        }
        return best == float.MinValue ? 0f : Mathf.Clamp(best, -1f, 1f) * IdeologyLeanWeight;
    }

    // 各党怎么投：提案的党赞成；执政联盟守党纪；在野党按立场
    public static Tally Count(Empire empire, TemporaryFaction claim, FixedFaction sponsor)
    {
        var tally = new Tally();
        ConstitutionalEconomyState state = State(empire);
        if (state?.parliament_seats == null || claim == null)
        {
            tally.Passed = true;
            return tally;
        }
        var coalition = new HashSet<string>(ParliamentSystem.GetCoalitionIds(empire));
        tally.GovernmentBill = sponsor != null && coalition.Contains(sponsor.GetID());
        foreach (IGrouping<string, ParliamentSeat> group in state.parliament_seats.GroupBy(seat => seat.faction_id))
        {
            FixedFaction party = Find(empire, group.Key);
            if (party == null) continue;
            int seats = group.Count();
            float stance = Stance(party, claim);
            int vote = party == sponsor ? 1
                : tally.GovernmentBill && coalition.Contains(group.Key)
                    ? stance <= DisciplineBreak ? -1 : stance <= DisciplineAbstain ? 0 : 1
                    : CourtIssues.StanceLevel(stance);
            vote = PartyRelations.AdjustVote(empire, party, sponsor, vote, stance);
            tally.Parties.Add((party, seats, vote, stance));
            if (vote > 0) tally.Yes += seats;
            else if (vote < 0) tally.No += seats;
            else tally.Abstain += seats;
        }
        tally.Passed = tally.Parties.Count == 0 || tally.Yes > tally.No;
        return tally;
    }

    // 提案并表决：通过就开跑；否决记冷却。返回是否开跑
    public static bool Propose(Empire empire, FixedFaction sponsor, TemporaryFaction claim)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || sponsor == null || claim == null) return false;
        Tally tally = Count(empire, claim, sponsor);
        var record = new ParliamentBillRecord
        {
            claim = claim.type.ToString(), sponsor = sponsor.GetID(), sponsor_name = sponsor.Name, proposed = Now,
            yes = tally.Yes, no = tally.No, abstain = tally.Abstain, passed = tally.Passed, government = tally.GovernmentBill
        };
        foreach ((FixedFaction party, int seats, int vote, float stance) in tally.Parties)
        {
            string id = party.GetID();
            record.votes[id] = vote;
            record.seats[id] = seats;
            record.names[id] = party.Name;
        }
        string issue = CourtSystem.IssueName(claim.type, empire);
        if (tally.Passed)
        {
            claim.pusherType = MetaType.None;
            claim.Start();
            record.outcome = claim.IsStarted() ? "running" : "failed";
            if (record.outcome == "failed") record.closed = Now;
            Archive(state, record);
            if (claim.IsStarted())
                PartyRelations.AfterBill(empire, sponsor, tally);
            Log(empire, "parliament_bill_passed_log", sponsor.Name, issue, tally.Yes, tally.No, tally.Abstain);
            return claim.IsStarted();
        }
        record.outcome = "rejected";
        record.closed = Now;
        claim.CountDown = Math.Max(claim.CountDown, FailCooldownYears);
        state.next_bill_at = Now + FailPauseMonths * MonthTime;
        empire.CoreKingdom?.TryIncreaseFactionRatio(sponsor, -2);
        Archive(state, record);
        PartyRelations.AfterBill(empire, sponsor, tally);
        PartyFinanceSystem.OnBillDefeated(empire, sponsor, tally.GovernmentBill);
        Log(empire, "parliament_bill_rejected_log", sponsor.Name, issue, tally.Yes, tally.No, tally.Abstain);
        return false;
    }

    // 这件诉求是不是表决通过、正在施行的议案(朝政检查不要把它当成"未经许可开跑"叫停)
    public static bool IsPassedBill(Empire empire, TemporaryFaction claim)
    {
        List<ParliamentBillRecord> bills = State(empire)?.parliament_bills;
        return claim != null && bills != null && bills.Any(bill => bill.outcome == "running" && bill.sponsor == claim.factionID &&
                                                                   bill.claim == claim.type.ToString());
    }

    // 每月：施行中的议案办完了就记结局
    public static void Tick(Empire empire)
    {
        List<ParliamentBillRecord> bills = State(empire)?.parliament_bills;
        if (bills == null) return;
        foreach (ParliamentBillRecord bill in bills.Where(bill => bill.outcome == "running"))
        {
            TemporaryFaction claim = Find(empire, bill.sponsor)?.TemporaryFactions?
                .FirstOrDefault(item => item != null && item.type.ToString() == bill.claim);
            if (claim?.IsStarted() == true) continue;
            bill.outcome = claim?.LastRunCompleted == true ? "completed" : "aborted";
            bill.closed = Now;
            if (bill.government) PartyFinanceSystem.OnBillFinished(empire, Find(empire, bill.sponsor), bill.outcome == "completed");
        }
    }

    // 下一件议案的预估(诉求天平页)：照眼下由谁提、提哪一条、表决会怎样
    public static (FixedFaction sponsor, TemporaryFaction claim, Tally tally) Preview(Empire empire)
    {
        if (!Applies(empire)) return (null, null, null);
        Regime regime = empire.CoreKingdom?.GetRegime();
        FixedFaction leader = ParliamentSystem.HasResponsibleGovernment(empire) ? PartySystem.GetGovernmentParty(empire) : regime?.GetDominateFaction();
        if (leader?.TemporaryFactions == null) return (leader, null, null);
        ClaimAgendaContext context = ClaimAgendaSystem.BuildContext(empire);
        TemporaryFaction claim = leader.TemporaryFactions
            .Where(tf => tf != null && tf.Active && tf.CountDown <= 0 && !tf.IsStarted())
            .Select(tf => (claim: tf, view: ClaimAgendaSystem.Evaluate(context, leader, tf)))
            .Where(entry => entry.view.CanPropose)
            .OrderByDescending(entry => entry.view.Score)
            .Select(entry => entry.claim)
            .FirstOrDefault(tf => tf.CheckCondition() && tf.CheckTarget());
        return (leader, claim, claim == null ? null : Count(empire, claim, leader));
    }

    private static void Archive(ConstitutionalEconomyState state, ParliamentBillRecord record)
    {
        state.parliament_bills ??= new List<ParliamentBillRecord>();
        state.parliament_bills.Insert(0, record);
        if (state.parliament_bills.Count > HistorySize)
            state.parliament_bills.RemoveRange(HistorySize, state.parliament_bills.Count - HistorySize);
    }

    private static void Log(Empire empire, string key, params object[] args)
    {
        string text;
        try
        {
            text = string.Format(LM.Get(key), args);
        }
        catch (FormatException)
        {
            text = LM.Get(key);
        }
        EventRecorder.RecordRoutine(empire, $"{empire.GetEmpireFullName()}：{text}", null);
    }
}
