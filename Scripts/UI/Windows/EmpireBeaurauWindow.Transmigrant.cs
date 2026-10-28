using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 穿越政体的帝国行政页顶部(用户 2026-10-29 "临高政体的执委会和元老院呢")：
//   · 左栏：执委会主席(第五阶段起为元老院主席)，下面是元老院五百二十一席——执委会成员金框金色、原班元老青色、继承席位绿色，
//     悬停看各类席数；
//   · 右栏：执行委员会(降临时为临时执委会，第五阶段起为元老院内阁)，原著的核心元老一行一人、写当前阶段的职务，
//     落成了实体的亮色；
//   · 通栏：元老院与主席的规矩、席位构成、当前阶段与下一步；最后一行点开元老院名册(SenateWindow)。
public partial class EmpireBeaurauWindow
{
    private static readonly Color CommitteeSeat = new(0.95f, 0.76f, 0.29f, 1f);
    private static readonly Color FounderSeat = new(0.5f, 0.85f, 0.92f, 1f);
    private static readonly Color HeirSeat = new(0.45f, 0.8f, 0.5f, 1f);

    [Hotfixable]
    private void InitialTopPartInfoTransmigrant()
    {
        Kingdom core = _empire?.CoreKingdom;
        TransmigrantState state = TransmigrantSystem.Of(core);
        if (state == null)
        {
            InitialTopPartInfoNormal();
            return;
        }
        topSpace = null;
        List<SenatorSeat> seats = state.seats;
        List<SenatorSeat> committee = seats.Where(seat => TransmigrantSystem.IsCore(seat) && seat.generation <= 1)
            .OrderBy(seat => seat.seat == state.chairman_seat ? 0 : 1).ThenBy(seat => seat.seat).ToList();
        var (card, left, right, column) = BeginTopCard(LM.Get("transmigrant_bureau_title"),
            string.Format(LM.Get("transmigrant_senate_chip_seats"), seats.Count), "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;

        // 主席
        SenatorSeat chair = seats.FirstOrDefault(seat => seat.seat == state.chairman_seat);
        Actor ruler = _empire.Emperor;
        string chairTitle = LM.Get(DynastyNaming.RankKey(RegimeType.Transmigrant, 0, core));
        string termEnd = state.chairman_elected_at >= 0d
            ? HistoryDateFormatter.GetYear(state.chairman_elected_at + TransmigrantSystem.TermYears * 60d) : "";
        AddPersonCell(left, column, ruler, chairTitle, "#FFCC00", JoinDetail(
            chair == null ? "" : string.Format(LM.Get("transmigrant_senate_seat_no"), chair.seat).ColorString("#C8C2B4"),
            string.IsNullOrEmpty(termEnd) ? "" : string.Format(LM.Get("transmigrant_bureau_term"), Mathf.Max(1, state.chairman_terms),
                termEnd).ColorString("#8FA0A8")));

        // 元老院：执委会成员、原班、继承，各自坐在一起
        var committeeSeats = new HashSet<int>(committee.Select(seat => seat.seat));
        int founders = seats.Count(seat => seat.generation <= 1 && !committeeSeats.Contains(seat.seat));
        int heirs = seats.Count(seat => seat.generation > 1 && !committeeSeats.Contains(seat.seat));
        var chamberSeats = new List<HemicycleChart.Seat>();
        chamberSeats.AddRange(committee.Select(_ => new HemicycleChart.Seat { Color = CommitteeSeat, Governing = true }));
        chamberSeats.AddRange(Enumerable.Repeat(new HemicycleChart.Seat { Color = FounderSeat }, founders));
        chamberSeats.AddRange(Enumerable.Repeat(new HemicycleChart.Seat { Color = HeirSeat }, heirs));
        GameObject chamber = HemicycleChart.Create(left.transform, new Vector2(column, Mathf.Round(column * 0.5f)), chamberSeats);
        UIHelper.AttachTextTooltip(chamber, $"transmigrant_chamber_{_empire.id}", LM.Get("transmigrant_senate_window"),
            string.Format(LM.Get("transmigrant_bureau_chamber"), committee.Count, founders, heirs,
                seats.Count(seat => TransmigrantSystem.ActorOf(seat) != null)));

        // 执行委员会
        AddColumnLabel(right, column, LM.Get(CommitteeKey(state.stage)));
        foreach (SenatorSeat seat in committee)
        {
            Actor actor = TransmigrantSystem.ActorOf(seat);
            string post = TransmigrantSystem.PostOf(state, seat);
            if (seat.seat == state.chairman_seat) post = chairTitle;
            string line = TransmigrantSystem.FullName(seat).ColorString(actor != null ? "#F2EEE2" : "#C8C2B4") +
                          (string.IsNullOrEmpty(post) ? "" : " · " + post.ColorString(seat.seat == state.chairman_seat ? "#F3C34A" : "#7FD8EA"));
            SimpleText label = right.AddTextIntoVertLayout(line, true, TextAnchor.MiddleLeft, new Vector2(column, 10f));
            label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(label);
            if (actor != null) UIHelper.MakeClickable(label.gameObject, () => UIHelper.actorClick(actor));
        }
        if (committee.Count == 0) AddColumnLabel(right, column, LM.Get("transmigrant_bureau_committee_empty"));

        // 通栏
        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, LM.Get("transmigrant_bureau_rule").ColorString("#7FD8EA"));
        AddFooterLine(card, inner, string.Format(LM.Get("transmigrant_bureau_seats"), seats.Count(seat => seat.generation <= 1),
            seats.Count(seat => seat.generation > 1), seats.Count(seat => TransmigrantSystem.ActorOf(seat) != null))
            .ColorString("#8FA0A8"), 6);
        string next = TransmigrantSystem.NextStageNode(state.stage);
        string stage = LM.Get("institution_" + TransmigrantSystem.StageNode(state.stage));
        AddFooterLine(card, inner, (next == null
            ? string.Format(LM.Get("transmigrant_bureau_stage_final"), stage)
            : string.Format(LM.Get("transmigrant_bureau_stage"), stage, LM.Get("institution_" + next))).ColorString("#8FA0A8"), 6);
        SimpleText open = AddFooterLine(card, inner, LM.Get("transmigrant_bureau_open_roll").ColorString("#F3C34A"), 6);
        UIHelper.MakeClickable(open.gameObject, () => ScrollWindow.showWindow(nameof(SenateWindow)));
    }

    // 官制品级里主席下面的执委会一层：核心元老(主席除外)一人一张卡，职务随阶段(同顶部名单)，席号；
    // 还没落成实体的卡压暗，落成了的点一下看人物
    private IEnumerable<GameObject> CommitteeNodes(TransmigrantState state) =>
        state.seats.Where(seat => TransmigrantSystem.IsCore(seat) && seat.generation <= 1 && seat.seat != state.chairman_seat)
            .OrderBy(seat => seat.seat).Select(seat => SafeNode(TransmigrantSystem.FullName(seat), () => BuildCommitteeNode(state, seat)));

    private GameObject BuildCommitteeNode(TransmigrantState state, SenatorSeat seat)
    {
        Actor actor = TransmigrantSystem.ActorOf(seat);
        string post = TransmigrantSystem.PostOf(state, seat);
        string name = TransmigrantSystem.FullName(seat);
        string seatNo = string.Format(LM.Get("transmigrant_senate_seat_no"), seat.seat);
        string title = string.IsNullOrEmpty(post) ? LM.Get("transmigrant_senate_core_title") : post;
        string tip = string.Join("\n", new[] { title, name, seatNo });
        return BuildNode(title, OfficeTitleHex, actor, name.ColorString(actor != null ? "#F2EEE2" : "#C8C2B4"),
            seatNo.ColorString("#65D6C4"), "", $"senator_{_empire.id}_{seat.seat}", tip, null, highlight: false,
            onClick: actor == null ? null : () => UIHelper.actorClick(actor));
    }

    // 执委会的叫法随阶段：降临时临时执委会，第一届起执行委员会，第五阶段起元老院内阁
    private static string CommitteeKey(int stage) => stage switch
    {
        <= 1 => "transmigrant_bureau_committee_provisional",
        >= 5 => "transmigrant_bureau_committee_cabinet",
        _ => "transmigrant_bureau_committee"
    };
}
