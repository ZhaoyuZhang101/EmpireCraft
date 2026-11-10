using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 党产与竞选、民调与执政表现(用户 2026-11-10 "政党总感觉是空壳"，问答选「党产与竞选」)：
//   · 党产：每年党员每人缴 2，支持本党的阶层捐款(商人最肯出钱，贵族地主次之，工农很少)，有议会的按议席每席补 3；
//     开销是组织维持(每名党员 1，另 5)。党产封顶 FundsCap；
//   · 竞选：大选前各党拿出党产的一半拉票，全国与各选区得票乘 1 + 0.2 ×(本党花费 ÷ 各党平均 − 1)，夹在 0.85~1.2(按存档估各党年入十到四十五、议席补贴三十上下)；
//   · 地方：地方选举赢下的行政区越多，全国得票越多(多乘 0.15 × 所占比例)——地方资助打压从此也影响大选；
//   · 执政表现(0~100，新政府 55 起)：议案办成 +4、被否决 -6、半途作罢 -3；国库亏空每年 -6；民意每差一档每年 -3；
//     每年向 50 回落五分之一。首相党得票乘 0.75 + 0.5 × 表现/100，执政联盟的友党一半效果；
//   · 民调：每年按眼下的选情估一次各党得票率，记最近十年。
public static class PartyFinanceSystem
{
    public const float FundsCap = 2000f;
    private const float DuesPerMember = 2f, UpkeepPerMember = 1f, UpkeepBase = 5f, SubsidyPerSeat = 3f, DonationScale = 20f;
    private const float CampaignShare = 0.5f;
    private const int PollYears = 10;
    public const float FreshApproval = 55f;

    private static readonly Dictionary<SocialClass, float> DonationWeights = new()
    {
        { SocialClass.Merchant, 1f }, { SocialClass.Noble, 0.8f }, { SocialClass.Landlord, 0.8f },
        { SocialClass.SlaveOwner, 0.6f }, { SocialClass.Officer, 0.3f }, { SocialClass.Citizen, 0.3f },
        { SocialClass.Army, 0.2f }, { SocialClass.Banner, 0.2f }, { SocialClass.Labour, 0.15f },
        { SocialClass.Peasant, 0.1f }, { SocialClass.Guoren, 0.1f }
    };

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    #region 党产

    // 每年(PartySystem.Update 过了年度门槛后)：结党产、执政表现、民调
    public static void Yearly(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !PartySystem.IsActive(empire)) return;
        foreach (FixedFaction party in PartySystem.GetParties(empire).Where(party => !party.Ban))
            SettleFunds(empire, state, party);
        UpdateApproval(empire, state);
        TakePoll(empire);
    }

    public static float Income(Empire empire, FixedFaction party, Dictionary<string, float> parts = null)
    {
        int members = party.AllMembers.Count(actor => actor.isAlive());
        float dues = members * DuesPerMember;
        float donations = 0f;
        if (party.ClassSupport != null)
            foreach (KeyValuePair<SocialClass, float> pair in party.ClassSupport)
                donations += Mathf.Max(0f, pair.Value) / 100f * (DonationWeights.TryGetValue(pair.Key, out float weight) ? weight : 0f) *
                             DonationScale;
        ConstitutionalEconomyState state = State(empire);
        int seats = ParliamentSystem.HasParliament(empire) ? state?.parliament_seats?.Count(seat => seat.faction_id == party.GetID()) ?? 0 : 0;
        float subsidy = seats * SubsidyPerSeat;
        float upkeep = members * UpkeepPerMember + UpkeepBase;
        if (parts != null)
        {
            parts["party_income_dues"] = dues;
            parts["party_income_donations"] = donations;
            parts["party_income_subsidy"] = subsidy;
            parts["party_income_upkeep"] = -upkeep;
        }
        return dues + donations + subsidy - upkeep;
    }

    private static void SettleFunds(Empire empire, ConstitutionalEconomyState state, FixedFaction party)
    {
        party.PartyIncomeParts ??= new Dictionary<string, float>();
        party.PartyIncomeParts.Clear();
        float income = Income(empire, party, party.PartyIncomeParts);
        party.PartyIncome = income;
        party.PartyFunds = Mathf.Clamp(party.PartyFunds + income, 0f, FundsCap);
    }

    #endregion

    #region 竞选与得票

    // 大选前(ParliamentSystem.HoldGeneralElection)：各党拿出党产的一半拉票
    public static void Campaign(Empire empire, List<FixedFaction> parties)
    {
        double now = World.world?.getCurWorldTime() ?? 0d;
        foreach (FixedFaction party in parties ?? new List<FixedFaction>())
        {
            float spend = Mathf.Floor(party.PartyFunds * CampaignShare);
            party.PartyFunds -= spend;
            party.CampaignSpend = spend;
            party.CampaignAt = now;
        }
    }

    // 竞选得票倍数：只算最近一届(一年内)的竞选；各党都没花就不变
    public static float CampaignFactor(Empire empire, FixedFaction party)
    {
        if (party == null || party.CampaignAt < 0d || Date.getYearsSince(party.CampaignAt) >= 1) return 1f;
        List<FixedFaction> rivals = PartySystem.GetParties(empire)
            .Where(other => !other.Ban && other.CampaignAt >= 0d && Date.getYearsSince(other.CampaignAt) < 1).ToList();
        float average = rivals.Count == 0 ? 0f : rivals.Average(other => other.CampaignSpend);
        if (average <= 0f) return 1f;
        return Mathf.Clamp(1f + 0.2f * (party.CampaignSpend / average - 1f), 0.85f, 1.2f);
    }

    // 地方执政带来的全国得票：赢下的行政区占全部的比例 × 0.15
    public static float ProvinceFactor(Empire empire, FixedFaction party)
    {
        Dictionary<long, ProvincePolitics> provinces = State(empire)?.provinces;
        if (party == null || provinces == null || provinces.Count == 0) return 1f;
        int held = provinces.Values.Count(province => province?.governing_party_id == party.GetID());
        return 1f + 0.15f * held / provinces.Count;
    }

    // 执政表现带来的得票：首相党 0.75 + 0.5 × 表现/100，执政联盟的友党一半效果
    public static float ApprovalFactor(Empire empire, FixedFaction party)
    {
        ConstitutionalEconomyState state = State(empire);
        if (party == null || state == null || !ParliamentSystem.HasParliament(empire) || RepublicSystem.IsOneParty(empire)) return 1f;
        float approval = Approval(empire);
        float full = 0.75f + 0.5f * approval / 100f;
        if (party.GetID() == state.prime_minister_faction_id) return full;
        return ParliamentSystem.IsGoverningFaction(empire, party.GetID()) ? 1f + (full - 1f) * 0.5f : 1f;
    }

    // 竞选与执政表现，ApplyOpinionSwing 里乘到各党得票上(地方选举也算)；地方执政只进全国大选与选区(见 PartySystem.CountVotes)，
    // 不进地方选举，免得赢了几个省就越选越稳
    public static float VoteFactor(Empire empire, FixedFaction party) =>
        CampaignFactor(empire, party) * ApprovalFactor(empire, party);

    // 普选分区：这个行政区的地方执政党在本区多得一成
    public static float DistrictFactor(Empire empire, FixedFaction party, long districtId)
    {
        Dictionary<long, ProvincePolitics> provinces = State(empire)?.provinces;
        return party != null && provinces != null && provinces.TryGetValue(districtId, out ProvincePolitics province) &&
               province?.governing_party_id == party.GetID() ? 1.1f : 1f;
    }

    #endregion

    #region 执政表现

    public static float Approval(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return FreshApproval;
        EnsureApproval(state);
        return state.government_approval;
    }

    // 换了首相党就从 55 重来
    private static void EnsureApproval(ConstitutionalEconomyState state)
    {
        string party = state.prime_minister_faction_id ?? "";
        if (state.government_approval >= 0f && state.government_approval_party == party) return;
        state.government_approval = FreshApproval;
        state.government_approval_party = party;
        state.government_approval_parts ??= new Dictionary<string, float>();
        state.government_approval_parts.Clear();
    }

    public static void Adjust(Empire empire, string key, float delta)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !ParliamentSystem.HasParliament(empire)) return;
        EnsureApproval(state);
        state.government_approval = Mathf.Clamp(state.government_approval + delta, 0f, 100f);
        state.government_approval_parts ??= new Dictionary<string, float>();
        state.government_approval_parts[key] = (state.government_approval_parts.TryGetValue(key, out float sum) ? sum : 0f) + delta;
    }

    public static void OnBillDefeated(Empire empire, FixedFaction sponsor, bool governmentBill)
    {
        if (governmentBill) Adjust(empire, "approval_bill_defeated", -6f);
    }

    public static void OnBillFinished(Empire empire, FixedFaction sponsor, bool completed) =>
        Adjust(empire, completed ? "approval_bill_done" : "approval_bill_aborted", completed ? 4f : -3f);

    private static void UpdateApproval(Empire empire, ConstitutionalEconomyState state)
    {
        if (!ParliamentSystem.HasParliament(empire)) return;
        EnsureApproval(state);
        // 去年的加减项清掉，今年重记
        state.government_approval_parts?.Clear();
        float drift = (50f - state.government_approval) * 0.2f;
        Adjust(empire, "approval_drift", drift);
        if (state.bankrupt_since >= 0d) Adjust(empire, "approval_bankrupt", -6f);
        int opinion = PublicOpinionSystem.GetLevel(empire);
        if (opinion > 0) Adjust(empire, "approval_opinion", -3f * opinion);
    }

    #endregion

    #region 民调

    // 每年按眼下的选情估一次各党得票率(不改议席)
    private static void TakePoll(Empire empire)
    {
        List<FixedFaction> parties = PartySystem.GetParties(empire).Where(party => !party.Ban &&
            PartyBanSystem.IsAllowedParty(empire, party)).ToList();
        if (parties.Count == 0) return;
        Dictionary<FixedFaction, float> votes = PartySystem.CountVotes(empire, parties);
        float total = votes.Values.Sum();
        foreach (FixedFaction party in parties)
        {
            party.PollHistory ??= new List<float>();
            party.PollHistory.Add(total <= 0f ? 0f : 100f * votes[party] / total);
            if (party.PollHistory.Count > PollYears) party.PollHistory.RemoveRange(0, party.PollHistory.Count - PollYears);
        }
    }

    #endregion
}
