using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 帝国行政页顶部的立法机构卡。议会召开后按体制分四种版式，名字也取本国实际的机构：
//   · 君主立宪：皇帝统而不治 + 总理大臣，英式对席(政府席 | 在野席)；二元君主制时内阁对君主负责；
//   · 议会内阁制共和国：虚位元首 + 总理，半圆议席，执政联盟加金框，可倒阁；
//   · 总统制共和国(含半总统制)：总统兼元首与政府首脑，国会控制条标出一致/分立政府，任期固定；
//   · 代表大会制(一党领导、统一战线、民主集中制)：会堂方阵，领导党居中、协商党派分坐两侧，
//     没有执政/在野、组阁与倒阁。
// 下面再接一张"各省"卡：有地方选举的画各省执政党与得票，代表大会制画各省代表团，普选后画各省选区议席。
public partial class EmpireBeaurauWindow
{
    private enum LegislatureKind { Monarchy, ParliamentaryRepublic, Presidential, Congress }

    private static readonly string[] LegislatureOfficeKeys =
    {
        "npc", "peoples_assembly", "commune_congress", "congress", "bicameral_parliament", "parliament",
        "corporative_chamber", "grand_council"
    };
    private static readonly string[] ChamberOfficeKeys = { "house_of_lords", "house_of_commons" };
    private static readonly string[] ExecutiveOfficeKeys =
    {
        "state_council", "council_of_ministers", "executive_council", "syndicate_federation",
        "state_council_meeting", "leaders_office", "presidential_office", "cabinet"
    };

    private LegislatureKind GetLegislatureKind()
    {
        if (!RepublicSystem.IsRepublic(_empire)) return LegislatureKind.Monarchy;
        if (RepublicSystem.IsOneParty(_empire) || PartyBanSystem.UsesDemocraticCentralism(_empire))
            return LegislatureKind.Congress;
        return RepublicSystem.IsParliamentaryRepublic(_empire)
            ? LegislatureKind.ParliamentaryRepublic
            : LegislatureKind.Presidential;
    }

    private List<OfficeObject> CoreOffices()
    {
        var result = new List<OfficeObject>();
        List<long> ids = _empire.data.centerOffice?.CoreOffices;
        if (ids == null) return result;
        foreach (long id in ids)
            if (OfficeManager.Offices.TryGetValue(id, out OfficeObject office) && office != null) result.Add(office);
        return result;
    }

    private static OfficeObject FindOffice(List<OfficeObject> offices, IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            OfficeObject office = offices.FirstOrDefault(item => item.pre == $"ideology_office_{key}_full");
            if (office != null) return office;
        }
        return null;
    }

    private static string LegislatureName(LegislatureKind kind, List<OfficeObject> offices)
    {
        List<OfficeObject> chambers = ChamberOfficeKeys
            .Select(key => offices.FirstOrDefault(item => item.pre == $"ideology_office_{key}_full"))
            .Where(item => item != null).ToList();
        if (chambers.Count > 0) return string.Join("·", chambers.Select(item => item.GetOfficeName(null)));
        OfficeObject office = FindOffice(offices, LegislatureOfficeKeys);
        if (office != null) return office.GetOfficeName(null);
        return LM.Get(kind switch
        {
            LegislatureKind.Monarchy => "legislature_imperial_diet",
            LegislatureKind.Congress => "legislature_peoples_congress",
            _ => "parliament_title"
        });
    }

    private string HeadTitle()
    {
        string title = RepublicSystem.GetHeadOfStateTitle(_empire);
        return string.IsNullOrWhiteSpace(title) ? LM.Get("legislature_head_of_state") : title;
    }

    private string TermsText(Actor actor)
    {
        int served = actor == null ? 0 : ParliamentSystem.GetServedTerms(_empire, actor);
        if (served <= 0) return "";
        int limit = ConstitutionSystem.GetClauses(_empire)?.max_terms ?? 0;
        return limit > 0
            ? string.Format(LM.Get("legislature_terms_of_max"), served, limit)
            : string.Format(LM.Get("legislature_terms"), served);
    }

    private static string PartyOf(Actor actor)
    {
        string name = Serving(actor) ? actor.GetFaction()?.Name : null;
        return string.IsNullOrWhiteSpace(name) ? LM.Get("label_none").ColorString("#8FA0A8") : name.ColorString("#C9A7E8");
    }

    private static string GovernmentDetail(ParliamentView view)
    {
        string government = string.IsNullOrWhiteSpace(view.GovernmentType)
            ? LM.Get("prime_minister_vacant")
            : string.Format(LM.Get("prime_minister_status"), LM.Get($"parliament_government_{view.GovernmentType}"),
                view.GovernmentSeats, view.TotalSeats);
        string party = view.PrimeMinisterFaction == null
            ? LM.Get("label_none").ColorString("#8FA0A8")
            : view.PrimeMinisterFaction.Name.ColorString(pColor: GoverningColor);
        return $"{party} · {government.ColorString("#C8C2B4")}";
    }

    private static string JoinDetail(params string[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    [Hotfixable]
    private void InitialTopPartInfoParliament()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        topSpace = null;
        ParliamentView view = ParliamentSystem.GetView(_empire);
        LegislatureKind kind = GetLegislatureKind();
        List<OfficeObject> offices = CoreOffices();
        string name = LegislatureName(kind, offices);
        // 政党按理念从左到右排(议席图、各省得票条共用同一套颜色)
        List<ParliamentFactionView> ordered = view.Factions
            .OrderBy(faction => PartySystem.GetPosition(faction.Ideology).x)
            .ThenByDescending(faction => faction.Seats).ToList();
        Dictionary<string, Color> colors = PartyColors(ordered);
        switch (kind)
        {
            case LegislatureKind.Congress:
                BuildCongressCard(view, name, offices, colors);
                break;
            case LegislatureKind.Presidential:
                BuildPresidentialCard(view, name, colors);
                break;
            case LegislatureKind.ParliamentaryRepublic:
                BuildParliamentaryCard(view, name, ordered, colors);
                break;
            default:
                BuildMonarchyCard(view, name, colors);
                break;
        }
        AddProvinceCard(view, kind, colors);
    }

    #region 四种版式

    // ① 代表大会制：主席(元首) + 政府机构主官 + 立法机构主官；会堂方阵；领导党 | 协商党派
    private void BuildCongressCard(ParliamentView view, string name, List<OfficeObject> offices,
        Dictionary<string, Color> colors)
    {
        var (card, left, right, column) = BeginTopCard(name,
            string.Format(LM.Get("legislature_header_congress"), view.Term, view.TotalSeats, view.YearsUntilElection),
            "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;
        ParliamentFactionView lead = view.Factions.FirstOrDefault(faction => faction.Governing) ?? view.Factions.FirstOrDefault();

        Actor head = _empire.Emperor;
        string headDetail = Serving(head) && lead != null && head.GetFaction() == lead.Faction
            ? string.Format(LM.Get("legislature_party_leader"), lead.Faction.Name).ColorString("#C9A7E8")
            : PartyOf(head);
        AddPersonCell(left, column, head, $"{HeadTitle()} · {LM.Get("legislature_head_of_state")}", "#FFCC00", headDetail);
        OfficeObject executive = FindOffice(offices, ExecutiveOfficeKeys);
        if (executive != null)
            AddPersonCell(left, column, executive.GetActor(), executive.GetOfficeName(null), "#59D9FF",
                JoinDetail(executive.GetName(null).ColorString("#C8C2B4"), PartyOf(executive.GetActor())));
        OfficeObject legislature = FindOffice(offices, LegislatureOfficeKeys);
        if (legislature != null)
            AddPersonCell(left, column, legislature.GetActor(), legislature.GetOfficeName(null), "#C9A7E8",
                JoinDetail(legislature.GetName(null).ColorString("#C8C2B4"), PartyOf(legislature.GetActor())));

        // 会堂方阵：领导党居中，其余党派按人数轮流分到两侧
        var center = new List<HemicycleChart.Seat>();
        var sideLeft = new List<HemicycleChart.Seat>();
        var sideRight = new List<HemicycleChart.Seat>();
        foreach (ParliamentFactionView faction in view.Factions)
        {
            List<HemicycleChart.Seat> seats = SeatsOf(view, faction, colors).ToList();
            if (faction == lead) center.AddRange(seats);
            else (sideLeft.Count <= sideRight.Count ? sideLeft : sideRight).AddRange(seats);
        }
        float hallHeight = Mathf.Max(70f, (executive != null ? 1 : 0) * 33f + (legislature != null ? 1 : 0) * 33f + 33f);
        GameObject hall = LegislatureCharts.CreateHall(right.transform, new Vector2(column, hallHeight), center, sideLeft, sideRight);
        UIHelper.AttachTextTooltip(hall, $"legislature_hall_{_empire.id}", name,
            string.Join("\n", view.Factions.Select(faction => DelegateLine(view, faction, colors, faction == lead, true))) +
            "\n" + LM.Get("legislature_hall_legend").ColorString("#8FA0A8"));

        // 领导党 | 参加政治协商(或其他)的党派
        var columns = card.BeginColumns(inner);
        var leadColumn = TopColumn(columns, column);
        var otherColumn = TopColumn(columns, column);
        AddColumnLabel(leadColumn, column, LM.Get("legislature_leading_party"));
        if (lead != null) AddPartyLine(leadColumn, column, lead, DelegateLine(view, lead, colors, true, false));
        List<FixedFaction> consultative = PartyBanSystem.GetConsultativeParties(_empire);
        List<ParliamentFactionView> others = view.Factions.Where(faction => faction != lead).ToList();
        if (others.Count > 0)
        {
            SimpleText label = AddColumnLabel(otherColumn, column,
                LM.Get(consultative.Count > 0 ? "legislature_consultative_parties" : "legislature_other_parties"));
            if (consultative.Count > 0) AttachConsultationTooltip(label.gameObject, consultative);
            foreach (ParliamentFactionView faction in others)
                AddPartyLine(otherColumn, column, faction, DelegateLine(view, faction, colors, false, false));
        }

        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, AgendaLine(view.Agenda, true));
        AddHowHint(card, inner, "legislature_how_hint_congress", name,
            string.Format(LM.Get("legislature_how_congress"), view.TotalSeats, ParliamentSystem.TermYears(_empire)));
    }

    // ② 议会内阁制共和国：虚位元首 + 总理；半圆议席；执政/在野与反对党领袖；可倒阁
    private void BuildParliamentaryCard(ParliamentView view, string name, List<ParliamentFactionView> ordered,
        Dictionary<string, Color> colors)
    {
        var (card, left, right, column) = BeginTopCard(name,
            string.Format(LM.Get("legislature_header_parliament"), view.Term, view.YearsUntilElection),
            "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;
        Actor head = _empire.Emperor;
        AddPersonCell(left, column, head, $"{HeadTitle()} · {LM.Get("legislature_ceremonial_head")}", "#FFCC00",
            string.Format(LM.Get("legislature_elected_by_parliament"), TermsText(head)).TrimEnd(' ', '·')
                .ColorString("#C8C2B4"));
        AddPersonCell(left, column, view.PrimeMinister, LM.Get("legislature_premier"), "#59D9FF", GovernmentDetail(view));
        AddHemicycle(left, column, view, ordered, colors, name);

        AddColumnLabel(right, column, LM.Get("parliament_seat_distribution"));
        foreach (ParliamentFactionView faction in view.Factions)
            AddPartyLine(right, column, faction, FactionSeatLine(faction, colors, false, "parliament_governing_mark"));
        Actor opposition = RepublicSystem.GetOppositionLeader(_empire);
        if (Serving(opposition))
        {
            AddColumnLabel(right, column, LM.Get("republic_opposition_leader"));
            AddPersonCell(right, column, opposition, opposition.GetFaction()?.Name ?? "", "#E9A85B", "");
        }

        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, AgendaLine(view.Agenda, false));
        AddFooterLine(card, inner, LM.Get("legislature_confidence").ColorString("#E9A85B"));
        AddConsultationLine(card, inner);
        AddHowHint(card, inner, "parliament_how_hint", name,
            string.Format(LM.Get("legislature_how_republic"), view.TotalSeats, ParliamentSystem.TermYears(_empire)) +
            "\n" + LM.Get("parliament_politics_body") +
            (view.ResponsibleGovernment ? "\n" + LM.Get("parliament_responsible_government_body") : ""));
    }

    // ③ 总统制：总统大卡；国会控制条与一致/分立政府；任期固定
    private void BuildPresidentialCard(ParliamentView view, string name, Dictionary<string, Color> colors)
    {
        var (card, left, right, column) = BeginTopCard(name,
            string.Format(LM.Get("legislature_header_presidential"), view.Term, view.YearsUntilElection),
            "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;
        Actor president = _empire.Emperor;
        FixedFaction presidentParty = Serving(president) ? president.GetFaction() : null;

        var box = left.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperLeft, pPadding: new RectOffset(4, 4, 4, 4));
        UIHelper.FixPanelWidth(box.transform, column);
        UIHelper.AddInsetBackground(box, new Vector2(column, 40f));
        AddPersonCell(box, column - 8f, president, $"{HeadTitle()} · {LM.Get("legislature_head_of_both")}", "#FFCC00",
            JoinDetail(PartyOf(president), TermsText(president).ColorString("#C8C2B4")));
        AddColumnLabel(box, column - 8f, LM.Get("legislature_fixed_term").ColorString("#E9A85B"));
        int limit = ConstitutionSystem.GetClauses(_empire)?.max_terms ?? 0;
        if (Serving(president) && limit > 0 && ParliamentSystem.GetServedTerms(_empire, president) >= limit)
            AddColumnLabel(box, column - 8f, LM.Get("legislature_last_term"));

        // 国会控制：总统党在最左，其余按议席多少排
        List<ParliamentFactionView> bySeats = view.Factions
            .OrderByDescending(faction => faction.Faction == presidentParty ? 1 : 0)
            .ThenByDescending(faction => faction.Seats).ToList();
        AddColumnLabel(right, column, LM.Get("legislature_control"));
        GameObject bar = LegislatureCharts.CreateControlBar(right.transform, new Vector2(column, 10f),
            bySeats.Select(faction => (colors[faction.Faction.GetID()], faction.Seats)).ToList());
        UIHelper.AttachTextTooltip(bar, $"legislature_bar_{_empire.id}", name,
            string.Join("\n", bySeats.Select(faction =>
                FactionSeatLine(faction, colors, true, faction.Faction == presidentParty ? "legislature_president_party_mark" : null))) +
            "\n" + string.Format(LM.Get("parliament_majority_note"), view.TotalSeats / 2 + 1, view.TotalSeats) +
            "\n" + LM.Get("legislature_bar_legend").ColorString("#8FA0A8"));
        SimpleText status = AddColumnLabel(right, column, ControlStatus(view, presidentParty));
        status.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        foreach (ParliamentFactionView faction in bySeats)
            AddPartyLine(right, column, faction, FactionSeatLine(faction, colors, false,
                faction.Faction == presidentParty ? "legislature_president_party_mark" : null));

        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, AgendaLine(view.Agenda, false));
        AddConsultationLine(card, inner);
        AddHowHint(card, inner, "legislature_how_hint_presidential", name,
            string.Format(LM.Get("legislature_how_presidential"), view.TotalSeats, ParliamentSystem.TermYears(_empire)));
    }

    // 一致政府(总统党过半) / 分立政府(另一党过半) / 无党过半
    private static string ControlStatus(ParliamentView view, FixedFaction presidentParty)
    {
        int total = Math.Max(1, view.TotalSeats);
        ParliamentFactionView own = view.Factions.FirstOrDefault(faction => faction.Faction == presidentParty);
        int ownSeats = own?.Seats ?? 0;
        if (ownSeats * 2 > total)
            return $"{LM.Get("legislature_unified").ColorString("#65D66E")} " +
                   string.Format(LM.Get("legislature_control_president"), ownSeats, total).ColorString("#8FA0A8");
        ParliamentFactionView rival = view.Factions.FirstOrDefault(faction => faction != own && faction.Seats * 2 > total);
        if (rival != null)
            return $"{LM.Get("legislature_divided").ColorString("#E05A4F")} " +
                   string.Format(LM.Get("legislature_control_opposition"), rival.Faction.Name, rival.Seats, total)
                       .ColorString("#8FA0A8");
        return $"{LM.Get("legislature_no_majority").ColorString("#E9A85B")} " +
               string.Format(LM.Get("legislature_control_none"), ownSeats, total).ColorString("#8FA0A8");
    }

    // ④ 君主立宪：君主 + 总理大臣 + 在野领袖；英式对席；责任内阁或二元君主制
    private void BuildMonarchyCard(ParliamentView view, string name, Dictionary<string, Color> colors)
    {
        var (card, left, right, column) = BeginTopCard(name,
            string.Format(LM.Get("legislature_header_parliament"), view.Term, view.YearsUntilElection),
            "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;
        bool responsible = view.ResponsibleGovernment &&
                           ConstitutionSystem.GetClauses(_empire)?.power_center != ConstitutionPowerCenter.DualMonarchy;
        Actor monarch = _empire.Emperor;
        string monarchTitle = LM.Get(monarch?.isSexFemale() == true ? "actor_emperor_G" : "actor_emperor_B") + " · " +
                              LM.Get(responsible ? "legislature_monarch_reigns" : "legislature_monarch_rules");
        AddPersonCell(left, column, monarch, monarchTitle, "#FFCC00",
            LM.Get("legislature_monarch_hereditary").ColorString("#C8C2B4"));
        AddPersonCell(left, column, view.PrimeMinister, LM.Get("prime_minister_title"), "#59D9FF", GovernmentDetail(view));
        Actor opposition = RepublicSystem.GetOppositionLeader(_empire);
        if (Serving(opposition))
            AddPersonCell(left, column, opposition, LM.Get("legislature_opposition_leader"), "#E9A85B", PartyOf(opposition));

        // 对席：政府席(执政一方) | 在野席，各按议席多少排
        var labels = right.BeginHoriGroup(new Vector2(column, 9f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
        labels.AddTextIntoHoriLayout(LM.Get("legislature_government_benches").ColorString("#F3C34A"), true,
            TextAnchor.MiddleLeft, new Vector2(column / 2f, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        labels.AddTextIntoHoriLayout(LM.Get("legislature_opposition_benches").ColorString("#8FA0A8"), true,
            TextAnchor.MiddleRight, new Vector2(column / 2f, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        List<ParliamentFactionView> bySeats = view.Factions.OrderByDescending(faction => faction.Seats).ToList();
        var government = bySeats.Where(faction => faction.Governing).SelectMany(faction => SeatsOf(view, faction, colors)).ToList();
        var benchOpposition = bySeats.Where(faction => !faction.Governing).SelectMany(faction => SeatsOf(view, faction, colors)).ToList();
        GameObject benches = LegislatureCharts.CreateBenches(right.transform, new Vector2(column, 58f), government, benchOpposition);
        UIHelper.AttachTextTooltip(benches, $"legislature_benches_{_empire.id}", name,
            string.Join("\n", bySeats.Select(faction => FactionSeatLine(faction, colors, true, "parliament_governing_mark"))) +
            "\n" + string.Format(LM.Get("parliament_majority_note"), view.TotalSeats / 2 + 1, view.TotalSeats) +
            "\n" + LM.Get("legislature_bench_legend").ColorString("#8FA0A8"));
        foreach (ParliamentFactionView faction in bySeats)
            AddPartyLine(right, column, faction, FactionSeatLine(faction, colors, false, "parliament_governing_mark"));

        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, AgendaLine(view.Agenda, false));
        AddFooterLine(card, inner, LM.Get(responsible ? "legislature_confidence" : "legislature_dual_monarchy")
            .ColorString("#E9A85B"));
        AddConsultationLine(card, inner);
        if (ParliamentSystem.KeepsCabinet(_empire))
        {
            int electors = _empire.GetCabinetMembers().Count(actor => actor != null && !actor.isRekt());
            AddFooterLine(card, inner, string.Format(LM.Get("parliament_electoral_college_note"), electors)
                .ColorString("#8FA0A8"));
        }
        AddHowHint(card, inner, "parliament_how_hint", name,
            string.Format(LM.Get("parliament_how_body"), view.TotalSeats, ParliamentSystem.TermYears(_empire)) +
            "\n" + LM.Get("parliament_politics_body") +
            (view.ResponsibleGovernment ? "\n" + LM.Get("parliament_responsible_government_body") : ""));
    }

    #endregion

    #region 各省

    // 各省卡：有地方选举的(多党制)画各省执政党与得票；代表大会制画各省代表团；只有普选选区的画各省议席。
    // 点省份打开该省的政体窗口(地方政治在那里，可资助/打压)
    private void AddProvinceCard(ParliamentView view, LegislatureKind kind, Dictionary<string, Color> colors)
    {
        bool local = ProvincialPoliticsSystem.HasLocalElections(_empire);
        Dictionary<long, List<ParliamentSeatView>> districts = view.Seats.Where(seat => seat.District > 0)
            .GroupBy(seat => seat.District).ToDictionary(group => group.Key, group => group.ToList());
        if (!local && districts.Count == 0) return;
        List<Kingdom> provinces = _empire.kingdoms_hashset
            .Where(kingdom => ProvincialPoliticsSystem.IsProvince(_empire, kingdom) ||
                              kingdom == _empire.CoreKingdom && districts.ContainsKey(kingdom.id))
            .Where(kingdom => kingdom != null && !kingdom.isRekt())
            .OrderByDescending(kingdom => kingdom == _empire.CoreKingdom ? 1 : 0)
            .ThenByDescending(kingdom => kingdom.getPopulationPeople()).ToList();
        if (provinces.Count == 0) return;

        string title, header;
        if (kind == LegislatureKind.Congress)
        {
            title = LM.Get("legislature_delegations");
            header = LM.Get("legislature_delegations_header");
        }
        else if (local)
        {
            title = LM.Get("legislature_local_title");
            int years = ProvincialPoliticsSystem.YearsUntilLocalElection(_empire);
            header = string.Format(LM.Get("legislature_local_header"),
                Mathf.RoundToInt(ProvincialPoliticsSystem.CoalitionProvinceShare(_empire) * 100f),
                years < 0 ? "" : string.Format(LM.Get("legislature_local_next"), years));
        }
        else
        {
            title = LM.Get("legislature_districts");
            header = LM.Get("legislature_districts_header");
        }
        var card = _root.BeginCard(_contentWidth, title, "#F3C34A", header, "ui/icons/iconCity");
        pool.Add(card.gameObject);
        float inner = _contentWidth - 12f;
        int perRow = Mathf.Max(2, Mathf.FloorToInt((inner + UIHelper.CardGap) / (92f + UIHelper.CardGap)));
        float tileWidth = Mathf.Floor((inner - UIHelper.CardGap * (perRow - 1)) / perRow);
        AutoHoriLayoutGroup row = null;
        for (int i = 0; i < provinces.Count; i++)
        {
            if (i % perRow == 0) row = card.BeginColumns(inner);
            districts.TryGetValue(provinces[i].id, out List<ParliamentSeatView> seats);
            AddProvinceTile(row, tileWidth, provinces[i], seats, kind, local, colors);
        }
        AddFooterSpacer(card, inner);
        SimpleText hint = AddFooterLine(card, inner, LM.Get(local ? "legislature_local_hint" : "legislature_province_hint")
            .ColorString("#8FA0A8"), 6);
        if (local)
            UIHelper.AttachTextTooltip(hint.gameObject, $"legislature_local_help_{_empire.id}",
                LM.Get("local_politics_title"), LM.Get("local_politics_help_body"));
    }

    private void AddProvinceTile(AutoHoriLayoutGroup row, float width, Kingdom province, List<ParliamentSeatView> seats,
        LegislatureKind kind, bool local, Dictionary<string, Color> colors)
    {
        var tile = row.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperLeft, pPadding: new RectOffset(3, 3, 2, 3));
        UIHelper.FixPanelWidth(tile.transform, width);
        UIHelper.AddInsetBackground(tile, new Vector2(width, 30f));
        float inner = width - 6f;
        bool core = province == _empire.CoreKingdom;
        int seatCount = seats?.Count ?? 0;

        // 第一行：省名 | 议席(代表)数
        var head = tile.BeginHoriGroup(new Vector2(inner, 10f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
        string seatText = seatCount <= 0 ? ""
            : string.Format(LM.Get(kind == LegislatureKind.Congress ? "legislature_delegates_short" : "legislature_district_seats"),
                seatCount);
        float seatWidth = Mathf.Ceil(UIHelper.EstimateTextWidth(seatText, 6)) + 2f;
        SimpleText nameText = head.AddTextIntoHoriLayout(province.GetKingdomName().ColorString("#F3C34A"), true,
            TextAnchor.MiddleLeft, new Vector2(inner - seatWidth, 10f));
        nameText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(nameText);
        if (seatWidth > 2f)
            head.AddTextIntoHoriLayout(seatText.ColorString("#8FA0A8"), true, TextAnchor.MiddleRight,
                new Vector2(seatWidth, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        // 第二行：得票条(地方选举) 或 本省议席构成
        List<(FixedFaction party, float share)> shares = local && !core
            ? ProvincialPoliticsSystem.GetShares(_empire, province)
            : new List<(FixedFaction party, float share)>();
        var segments = shares.Count > 0
            ? shares.Select(item => (PartyColor(item.party, colors), Mathf.RoundToInt(item.share * 1000f))).ToList()
            : (seats ?? new List<ParliamentSeatView>()).Where(seat => seat.Faction != null)
                .GroupBy(seat => seat.Faction).OrderByDescending(group => group.Count())
                .Select(group => (PartyColor(group.Key, colors), group.Count())).ToList();
        if (segments.Count > 0)
            LegislatureCharts.CreateControlBar(tile.transform, new Vector2(inner, 5f), segments, majorityLine: false);

        // 第三行：本省执政党(地方选举) / 中央直辖 / 本省第一大党
        string status;
        FixedFaction governing = local && !core ? ProvincialPoliticsSystem.GetGoverningParty(_empire, province) : null;
        if (core) status = LM.Get("legislature_province_direct").ColorString("#7FD8EA");
        else if (governing != null)
            status = string.Format(LM.Get("legislature_province_governing"), governing.Name)
                         .ColorString(ProvincialPoliticsSystem.IsOppositionHeld(_empire, province) ? "#E9A85B" : "#65D66E") +
                     (ProvincialPoliticsSystem.IsOppositionHeld(_empire, province)
                         ? LM.Get("legislature_province_resisting").ColorString("#E9A85B") : "");
        else if (local) status = LM.Get("legislature_province_no_election").ColorString("#8FA0A8");
        else
        {
            var top = (seats ?? new List<ParliamentSeatView>()).Where(seat => seat.Faction != null)
                .GroupBy(seat => seat.Faction).OrderByDescending(group => group.Count()).FirstOrDefault();
            status = top == null ? "" : $"{top.Key.Name} {top.Count()}/{seatCount}".ColorString("#C8C2B4");
        }
        if (!string.IsNullOrEmpty(status))
        {
            SimpleText statusText = tile.AddTextIntoVertLayout(status, true, TextAnchor.MiddleLeft, new Vector2(inner, 9f));
            statusText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(statusText);
        }

        // 悬停：省长、各党得票与本省议席；点击：打开本省政体窗口(地方政治)
        var tip = new List<string>();
        Actor governor = province.king;
        if (governor?.data != null && !governor.isRekt())
            tip.Add(string.Format(LM.Get("legislature_province_governor"), governor.getName()));
        foreach ((FixedFaction party, float share) in shares)
            tip.Add($"{party.Name} {share * 100f:0}%" + (party == governing ? LM.Get("parliament_governing_mark") : ""));
        if (seatCount > 0)
            tip.AddRange(seats.Where(seat => seat.Faction != null).GroupBy(seat => seat.Faction)
                .OrderByDescending(group => group.Count())
                .Select(group => string.Format(LM.Get("legislature_province_seat_line"), group.Key.Name, group.Count())));
        if (tip.Count > 0)
            UIHelper.AttachTextTooltip(tile.gameObject, $"legislature_province_{province.id}", province.GetKingdomName(),
                string.Join("\n", tip));
        Kingdom target = province;
        UIHelper.MakeClickable(tile.gameObject, () =>
        {
            if (target == null || target.isRekt()) return;
            SelectedMetas.selected_kingdom = target;
            ScrollWindow.showWindow(nameof(RegimeWindow));
        });
    }

    private static Color PartyColor(FixedFaction party, Dictionary<string, Color> colors)
    {
        if (party != null && colors.TryGetValue(party.GetID(), out Color color)) return color;
        Color fallback = party == null ? Color.gray
            : EmpireCraftNamePlateLibrary.GetIdeologyColorAsset(PartySystem.LeaningOf(party))?.getColorBanner() ?? Color.gray;
        fallback.a = 1f;
        return fallback;
    }

    #endregion

    #region 共用小件

    private static IEnumerable<HemicycleChart.Seat> SeatsOf(ParliamentView view, ParliamentFactionView faction,
        Dictionary<string, Color> colors) =>
        view.Seats.Where(seat => seat.Faction == faction.Faction).Select(seat => new HemicycleChart.Seat
        {
            Color = colors[faction.Faction.GetID()], Governing = faction.Governing, Vacant = seat.Representative == null
        });

    private void AddHemicycle(AutoVertLayoutGroup column, float width, ParliamentView view,
        List<ParliamentFactionView> ordered, Dictionary<string, Color> colors, string name)
    {
        var seats = ordered.SelectMany(faction => SeatsOf(view, faction, colors)).ToList();
        GameObject chart = HemicycleChart.Create(column.transform, new Vector2(width, Mathf.Round(width * 0.5f)), seats);
        UIHelper.AttachTextTooltip(chart, $"parliament_hemicycle_{_empire.id}", name,
            string.Join("\n", ordered.Select(faction => FactionSeatLine(faction, colors, true, "parliament_governing_mark"))) +
            "\n" + string.Format(LM.Get("parliament_majority_note"), view.TotalSeats / 2 + 1, view.TotalSeats) +
            (view.NextSeatCount != view.TotalSeats
                ? "\n" + string.Format(LM.Get("parliament_next_seat_count"), view.NextSeatCount) : "") +
            "\n" + LM.Get("parliament_hemicycle_legend").ColorString("#8FA0A8"));
    }

    private static void AddPartyLine(AutoVertLayoutGroup column, float width, ParliamentFactionView faction, string text)
    {
        var line = column.BeginHoriGroup(new Vector2(width, 11f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
        UIHelper.AddLayoutIcon(line.transform, SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(faction.Ideology)), 9f);
        SimpleText label = line.AddTextIntoHoriLayout(text, true, TextAnchor.MiddleLeft, new Vector2(width - 11f, 11f));
        label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(label);
    }

    // 代表大会："劳工党 24名代表 69%"(领导党) / "宪友会 3名 社会自由主义"
    private static string DelegateLine(ParliamentView view, ParliamentFactionView faction, Dictionary<string, Color> colors,
        bool lead, bool withIdeology)
    {
        string name = faction.Faction.Name.ColorString(pColor: colors[faction.Faction.GetID()]);
        // 党禁前的议会里是古代派系：按帝国的文化线叫(见 CultureDoctrines)
        string ideology = PartySystem.GetIdeologyName(faction.Ideology, CultureDoctrines.CultureOf(faction.Faction))
            .ColorString("#8FA0A8");
        if (lead)
            return $"{name} {string.Format(LM.Get("legislature_delegates"), faction.Seats)} " +
                   $"{faction.Seats * 100 / Math.Max(1, view.TotalSeats)}%".ColorString("#A8B8BE") +
                   (withIdeology ? " " + ideology : "");
        return $"{name} {string.Format(LM.Get("legislature_delegates_short"), faction.Seats)} {ideology}";
    }

    private static void AddFooterSpacer(AutoVertLayoutGroup card, float inner) =>
        card.AddTextIntoVertLayout("", false, TextAnchor.MiddleCenter, new Vector2(inner, 2f));

    private static SimpleText AddFooterLine(AutoVertLayoutGroup card, float inner, string text, int fontSize = 7)
    {
        SimpleText line = card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(inner, 10f));
        line.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(line);
        return line;
    }

    private void AddConsultationLine(AutoVertLayoutGroup card, float inner)
    {
        List<FixedFaction> consultative = PartyBanSystem.GetConsultativeParties(_empire);
        if (consultative.Count == 0) return;
        SimpleText line = AddFooterLine(card, inner,
            string.Format(LM.Get("consultation_line"), string.Join("、", consultative.Select(party => party.Name)))
                .ColorString("#7FD8EA"));
        AttachConsultationTooltip(line.gameObject, consultative);
    }

    private void AttachConsultationTooltip(GameObject target, List<FixedFaction> consultative)
    {
        UIHelper.AttachTextTooltip(target, $"consultation_{_empire.id}", LM.Get("consultation_title"),
            string.Format(LM.Get("consultation_body"),
                string.Join("、", consultative.Select(party => $"{party.Name}[{PartySystem.GetIdeologyName(party.Ideology)}]")),
                PartyBanSystem.ConsultationLegitimacy, Mathf.RoundToInt(PartyBanSystem.ConsultationDissent * 100f)));
    }

    private void AddHowHint(AutoVertLayoutGroup card, float inner, string hintKey, string title, string body)
    {
        SimpleText hint = AddFooterLine(card, inner, LM.Get(hintKey).ColorString("#8FA0A8"), 6);
        UIHelper.AttachTextTooltip(hint.gameObject, $"{hintKey}_{_empire.id}", title, body);
    }

    // "民主党·执政 4席 +1 得票32%"；markKey 为空时不标
    private static string FactionSeatLine(ParliamentFactionView faction, Dictionary<string, Color> colors,
        bool withIdeology, string markKey)
    {
        string name = faction.Faction.Name.ColorString(pColor: colors[faction.Faction.GetID()]);
        bool marked = markKey == "parliament_governing_mark" ? faction.Governing : !string.IsNullOrEmpty(markKey);
        if (marked) name += LM.Get(markKey).ColorString("#F3C34A");
        string line = $"{name} {string.Format(LM.Get("parliament_seats_short"), faction.Seats)}";
        if (faction.PreviousSeats >= 0 && faction.PreviousSeats != faction.Seats)
        {
            int delta = faction.Seats - faction.PreviousSeats;
            line += " " + (delta > 0 ? $"+{delta}".ColorString("#65D66E") : $"{delta}".ColorString("#E05A4F"));
        }
        if (faction.VoteShare >= 0f)
            line += " " + string.Format(LM.Get("parliament_vote_share"), (faction.VoteShare * 100f).ToString("0"))
                .ColorString("#A8B8BE");
        if (withIdeology)
            line += " " + $"[{PartySystem.GetIdeologyName(faction.Ideology, CultureDoctrines.CultureOf(faction.Faction))}]"
                .ColorString("#C9A7E8");
        return line;
    }

    private static string AgendaLine(GovernmentAgenda agenda, bool congress)
    {
        if (agenda == null)
            return LM.Get(congress ? "congress_agenda_none" : "government_agenda_none").ColorString("#8FA0A8");
        string text = string.Format(LM.Get(congress ? "congress_agenda_label" : "government_agenda_label"),
            LM.Get($"constitution_clause_{agenda.clause}"),
            ConstitutionSystem.ValueText(agenda.clause, agenda.target), agenda.progress.ToString("0"));
        if (agenda.stalled) text += LM.Get("government_agenda_stalled").ColorString("#E9A85B");
        return text.ColorString("#7FD8EA");
    }

    #endregion
}
