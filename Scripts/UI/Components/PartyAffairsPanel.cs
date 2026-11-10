using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 派系窗口的「党务」页(用户 2026-11-10 "政党总感觉是空壳")：党产与收支、民调走势、执政表现(执政党)、
// 党内(党魁与党代会、党纪、一翼、分裂倍数)、对各党的好感
public static class PartyAffairsPanel
{
    private const float CardPadding = 12f;

    public static void Render(AutoVertLayoutGroup page, Empire empire, FixedFaction party, float width)
    {
        if (page == null || party == null) return;
        if (!party.IsParty || empire == null)
        {
            page.AddEmptyCard(width, "party_affairs_title", LM.Get("party_affairs_not_party"));
            return;
        }
        RenderFunds(page, empire, party, width);
        RenderPolls(page, empire, party, width);
        RenderInner(page, empire, party, width);
        RenderRelations(page, empire, party, width);
    }

    private static void RenderFunds(AutoVertLayoutGroup page, Empire empire, FixedFaction party, float width)
    {
        var parts = new Dictionary<string, float>();
        float income = PartyFinanceSystem.Income(empire, party, parts);
        var card = page.BeginCard(width, "party_affairs_funds", WideKit.Gold, null, "ui/icons/iconMoney");
        bool campaign = party.CampaignAt >= 0d && Date.getYearsSince(party.CampaignAt) < 1;
        card.AddMetricRow(width,
            (LM.Get("party_affairs_funds_now"), $"{Mathf.RoundToInt(party.PartyFunds)}/{PartyFinanceSystem.FundsCap:0}", WideKit.Gold),
            (LM.Get("party_affairs_income"), Signed(income), income >= 0f ? WideKit.Green : WideKit.Red),
            (LM.Get("party_affairs_campaign"), party.CampaignAt < 0d ? "—" : Mathf.RoundToInt(party.CampaignSpend).ToString(),
                campaign ? WideKit.Cyan : WideKit.Muted));
        int index = 0;
        foreach (KeyValuePair<string, float> pair in parts)
            card.AddValueRow(width, index++, LM.Get(pair.Key), Signed(pair.Value), pair.Value >= 0f ? WideKit.Green : WideKit.Red);
        card.AddParagraph(width, string.Format(LM.Get("party_affairs_vote_factors"),
            PartyFinanceSystem.CampaignFactor(empire, party).ToString("0.00"), PartyFinanceSystem.ProvinceFactor(empire, party).ToString("0.00"),
            PartyFinanceSystem.ApprovalFactor(empire, party).ToString("0.00")), 6, WideKit.Muted);
    }

    private static void RenderPolls(AutoVertLayoutGroup page, Empire empire, FixedFaction party, float width)
    {
        List<float> polls = party.PollHistory ?? new List<float>();
        string latest = polls.Count == 0 ? "—" : $"{polls[polls.Count - 1]:0.#}%";
        var card = page.BeginCard(width, "party_affairs_polls", WideKit.Gold, latest, "ui/icons/iconPopulation");
        if (polls.Count == 0)
        {
            card.AddParagraph(width, LM.Get("party_affairs_polls_none"), 7, WideKit.Muted);
        }
        else
        {
            card.AddColumnChart(width, polls, Mathf.Max(20f, polls.Max() * 1.2f), WideKit.Cyan);
            float vote = empire.data?.constitutional_economy?.vote_shares != null &&
                         empire.data.constitutional_economy.vote_shares.TryGetValue(party.GetID(), out float share) ? share * 100f : -1f;
            if (vote >= 0f)
                card.AddParagraph(width, string.Format(LM.Get("party_affairs_polls_vs_election"), $"{vote:0.#}%", latest), 6, WideKit.Muted);
        }
        // 执政党：执政表现
        if (!ParliamentSystem.IsGoverningFaction(empire, party.GetID())) return;
        float approval = PartyFinanceSystem.Approval(empire);
        card.AddBarRow(width, 0, LM.Get("claim_scale_approval"), approval / 100f, Mathf.RoundToInt(approval).ToString(),
            approval >= 50f ? WideKit.Green : approval >= 35f ? WideKit.Gold : WideKit.Red, WideKit.Gold, "#3A4048",
            labelWidth: 60f, barWidth: (width - CardPadding) * 0.4f);
    }

    private static void RenderInner(AutoVertLayoutGroup page, Empire empire, FixedFaction party, float width)
    {
        Actor leader = party.GetLeader();
        bool elected = leader != null && party.ElectedLeaderId == leader.id && party.LeaderElectedAt >= 0d;
        string leaderText = leader == null ? LM.Get("label_none")
            : elected ? string.Format(LM.Get("party_affairs_leader_elected"), leader.getName(), Date.getYearsSince(party.LeaderElectedAt))
            : string.Format(LM.Get("party_affairs_leader_interim"), leader.getName());
        int nextCongress = elected ? Math.Max(0, PartyInnerSystem.CongressYears - Date.getYearsSince(party.LeaderElectedAt)) : 0;
        var card = page.BeginCard(width, "party_affairs_inner", WideKit.Gold, leaderText, "ui/icons/iconKings");
        string wingName = Enum.TryParse(party.WingIdeology, out PartyIdeology wing) ? PartySystem.GetIdeologyName(wing) : LM.Get("label_none");
        float split = PartyInnerSystem.SplitFactor(party);
        card.AddMetricRow(width,
            (LM.Get("party_affairs_discipline"), Mathf.RoundToInt(party.Discipline).ToString(), party.Discipline >= 60f ? WideKit.Green : WideKit.Value),
            (LM.Get("party_affairs_wing"), party.WingShare <= 0f ? LM.Get("label_none") : $"{wingName} {party.WingShare * 100f:0}%",
                party.WingShare >= 0.3f ? WideKit.Red : party.WingShare >= 0.2f ? WideKit.Orange : WideKit.Value),
            (LM.Get("party_affairs_split"), $"×{split:0.00}", split > 1.5f ? WideKit.Red : WideKit.Value));
        card.AddValueRow(width, 0, LM.Get("party_affairs_next_congress"),
            elected ? string.Format(LM.Get("party_affairs_years"), nextCongress) : LM.Get("party_affairs_congress_due"));
        card.AddValueRow(width, 1, LM.Get("party_affairs_seat_loss"), party.LastSeatLoss <= 0f ? "—" : $"{party.LastSeatLoss * 100f:0}%",
            party.LastSeatLoss >= 0.3f ? WideKit.Red : WideKit.Value);
        card.AddParagraph(width, LM.Get("party_affairs_inner_rule"), 6, WideKit.Muted);
    }

    private static void RenderRelations(AutoVertLayoutGroup page, Empire empire, FixedFaction party, float width)
    {
        List<FixedFaction> others = PartySystem.GetParties(empire).Where(other => other != party && !other.Ban).ToList();
        var card = page.BeginCard(width, "party_affairs_relations", WideKit.Gold, null, "ui/icons/iconFamily");
        if (others.Count == 0)
        {
            card.AddParagraph(width, LM.Get("party_affairs_relations_none"), 7, WideKit.Muted);
            return;
        }
        int index = 0;
        foreach (FixedFaction other in others.OrderByDescending(other => PartyRelations.Relation(party, other)))
        {
            float relation = PartyRelations.Relation(party, other);
            bool partner = ParliamentSystem.IsGoverningFaction(empire, party.GetID()) && ParliamentSystem.IsGoverningFaction(empire, other.GetID());
            string value = $"{Mathf.RoundToInt(relation)}" + (partner ? " · " + LM.Get("party_affairs_partner") : "") +
                           (PartyRelations.RefusesCoalition(party, other) ? " · " + LM.Get("party_affairs_refuses") : "");
            card.AddBarRow(width, index++, other.Name, (relation + 100f) / 200f, value,
                relation >= 40f ? WideKit.Green : relation <= -30f ? WideKit.Red : WideKit.Soft,
                relation >= 0f ? WideKit.Green : WideKit.Red, "#3A4048", labelWidth: 70f, barWidth: (width - CardPadding) * 0.35f);
        }
        card.AddParagraph(width, LM.Get("party_affairs_relations_rule"), 6, WideKit.Muted);
    }

    private static string Signed(float value)
    {
        int rounded = Mathf.RoundToInt(value);
        return rounded > 0 ? $"+{rounded}" : rounded.ToString();
    }
}
