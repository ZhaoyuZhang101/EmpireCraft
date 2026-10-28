using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 元老院名册(「卷册」宽窗口，组件见 WideScrollWindow / WideKit；席位见 TransmigrantSystem)：
//   · 左栏(固定)：国号、阶段与降临年 → 核心元老 / 全部席位 / 原班 / 继承 → 按原著分组(临时执委会时的分组，取人数最多的几组)；
//   · 右栏：页头(页名 · 席数) → 主指标(席位、原班、继承、在朝) →
//     核心页：主席卡(头像、届数、任满年)与阶段卡(当前阶段、下一步与城数门槛)，核心元老两栏卡片；
//     其余页：一张卡一行一席(席位号与姓名、职务、年龄、第几代、分组)，每页 PageSize 席，卡顶翻页；
//   · 有实体的元老整卡/整行点开此人；世上没有穿越政权时显示空状态。
public class SenateWindow : WideScrollWindow<SenateWindow>
{
    private enum View { Core, All, Founders, Heirs }

    private const int PageSize = 60;
    private const int RailGroupRows = 8;
    private const int LastStage = 6;

    private View _view = View.Core;
    // 空 = 全部分组
    private string _group = "";
    private int _page;

    private Kingdom _kingdom;
    private TransmigrantState _state;
    private List<SenatorSeat> _shown = new();
    private List<(string group, int count)> _groups = new();

    protected override void OnOpen()
    {
        _view = View.Core;
        _group = "";
        _page = 0;
    }

    // ───────── 数据(每次重画前取一遍) ─────────
    protected override void Prepare()
    {
        _kingdom = TransmigrantSystem.Active();
        _state = TransmigrantSystem.Of(_kingdom);
        List<SenatorSeat> scoped = Scope(_state?.seats ?? new List<SenatorSeat>(), _view).ToList();
        _groups = scoped.Select(TransmigrantSystem.GroupOf).Where(group => !string.IsNullOrEmpty(group))
            .GroupBy(group => group).Select(group => (group.Key, group.Count()))
            .OrderByDescending(group => group.Item2).Take(RailGroupRows).ToList();
        if (!string.IsNullOrEmpty(_group) && _groups.All(group => group.group != _group)) _group = "";
        _shown = (string.IsNullOrEmpty(_group) ? scoped : scoped.Where(seat => TransmigrantSystem.GroupOf(seat) == _group))
            .OrderBy(seat => seat.seat).ToList();
        _page = Mathf.Clamp(_page, 0, PageCount - 1);
    }

    private static IEnumerable<SenatorSeat> Scope(List<SenatorSeat> seats, View view) => view switch
    {
        View.Core => seats.Where(TransmigrantSystem.IsCore),
        View.Founders => seats.Where(seat => seat.generation <= 1),
        View.Heirs => seats.Where(seat => seat.generation > 1),
        _ => seats
    };

    private int PageCount => Mathf.Max(1, (_shown.Count + PageSize - 1) / PageSize);

    private static string ViewName(View view) => LM.Get(view switch
    {
        View.Core => "transmigrant_senate_view_core",
        View.Founders => "transmigrant_senate_view_founders",
        View.Heirs => "transmigrant_senate_view_heirs",
        _ => "transmigrant_senate_view_all"
    });

    private static string StageName(int stage) => LM.Get("institution_" + TransmigrantSystem.StageNode(stage));

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (_state == null)
        {
            rail.AddRailTitle(LM.Get("transmigrant_senate_window"), LM.Get("transmigrant_senate_none"));
            return;
        }
        rail.AddRailTitle(_kingdom.GetKingdomFullName(), string.Format(LM.Get("transmigrant_senate_rail_summary"),
            StageName(_state.stage), HistoryDateFormatter.GetYear(_state.landed_at)));

        List<SenatorSeat> seats = _state.seats;
        var views = rail.BeginRailMenu();
        int index = 0;
        foreach (View view in new[] { View.Core, View.All, View.Founders, View.Heirs })
        {
            View target = view;
            views.AddRailOption(null, ViewName(view), Scope(seats, view).Count().ToString(), _view == view,
                () => SetView(target), index++);
        }

        if (_groups.Count > 1)
        {
            var groups = rail.BeginRailMenu(LM.Get("transmigrant_senate_by_group"));
            groups.AddRailOption(null, LM.Get("talent_all"), _groups.Sum(group => group.count).ToString(),
                string.IsNullOrEmpty(_group), () => SetGroup(""), 0);
            index = 1;
            foreach ((string group, int count) in _groups)
            {
                string key = group;
                groups.AddRailOption(null, group, count.ToString(), _group == key, () => SetGroup(key), index++);
            }
        }
    }

    private void SetView(View view)
    {
        if (_view == view) return;
        _view = view;
        _group = "";
        _page = 0;
        Refresh(resetScroll: true);
    }

    private void SetGroup(string group)
    {
        if (_group == group) return;
        _group = group;
        _page = 0;
        Refresh(resetScroll: true);
    }

    private void Turn(int delta)
    {
        int page = Mathf.Clamp(_page + delta, 0, PageCount - 1);
        if (page == _page) return;
        _page = page;
        Refresh(resetScroll: true);
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (_state == null)
        {
            page.AddPageHeader(PageWidth, LM.Get("transmigrant_senate_window"));
            page.AddEmptyCard(PageWidth, "transmigrant_senate_empty", LM.Get("transmigrant_senate_empty_hint"));
            return;
        }
        page.AddPageHeader(PageWidth, ViewName(_view), string.Format(LM.Get("transmigrant_senate_chip_seats"), _shown.Count));

        List<SenatorSeat> seats = _state.seats;
        page.AddKpiRow(PageWidth,
            (LM.Get("transmigrant_senate_kpi_seats"), seats.Count.ToString(), WideKit.Gold),
            (LM.Get("transmigrant_senate_kpi_founders"), seats.Count(seat => seat.generation <= 1).ToString(), WideKit.Green),
            (LM.Get("transmigrant_senate_kpi_heirs"), seats.Count(seat => seat.generation > 1).ToString(), WideKit.Cyan),
            (LM.Get("transmigrant_senate_kpi_seated"), seats.Count(seat => TransmigrantSystem.ActorOf(seat) != null).ToString(),
                WideKit.Value));

        if (_view == View.Core)
        {
            DrawLeadership(page);
            if (_shown.Count == 0) return;
            page.AddSectionHeader(PageWidth, "transmigrant_senate_core_title", WideKit.Gold);
            page.AddCardGrid(PageWidth, _shown, 2, AddSeatCard);
            return;
        }
        DrawList(page);
    }

    // 主席卡 + 阶段卡
    private void DrawLeadership(AutoVertLayoutGroup page)
    {
        float half = (PageWidth - UIHelper.CardGap) / 2f;
        var columns = page.BeginColumns(PageWidth);

        SenatorSeat chair = _state.seats.FirstOrDefault(seat => seat.seat == _state.chairman_seat);
        Actor ruler = chair != null ? TransmigrantSystem.ActorOf(chair) : null;
        string title = LM.Get(_state.stage >= 5 ? "Transmigrant_officiallevel_0_cabinet" : "Transmigrant_officiallevel_0");
        var chairCard = columns.BeginColumnCard(half, title, WideKit.Gold);
        string post = chair == null ? "" : TransmigrantSystem.PostOf(_state, chair);
        chairCard.AddPersonRow(half, ruler,
            chair == null ? "" : string.Format(LM.Get("transmigrant_senate_seat_no"), chair.seat),
            chair == null ? LM.Get("transmigrant_senate_vacant") : TransmigrantSystem.FullName(chair),
            string.IsNullOrEmpty(post) ? null : post);
        bool elected = chair != null && _state.chairman_elected_at >= 0d;
        chairCard.AddMetricRow(half,
            (LM.Get("transmigrant_senate_metric_elected"),
                elected ? HistoryDateFormatter.GetYear(_state.chairman_elected_at) : "—", WideKit.Value),
            (LM.Get("transmigrant_senate_metric_terms"), elected ? Mathf.Max(1, _state.chairman_terms).ToString() : "—", WideKit.Cyan),
            (LM.Get("transmigrant_senate_metric_term_end"),
                elected ? HistoryDateFormatter.GetYear(_state.chairman_elected_at + TransmigrantSystem.TermYears * 60d) : "—",
                WideKit.Value));
        if (ruler != null) UIHelper.MakeClickable(chairCard.gameObject, () => UIHelper.actorClick(ruler));

        var stageCard = columns.BeginColumnCard(half, "transmigrant_senate_stage_title", WideKit.Cyan,
            $"{Mathf.Min(_state.stage, LastStage)}/{LastStage}");
        AddLine(stageCard, half, StageName(_state.stage).ColorString(WideKit.Gold), 9, 13f);
        string next = TransmigrantSystem.NextStageNode(_state.stage);
        if (next == null)
        {
            AddLine(stageCard, half, LM.Get("transmigrant_senate_stage_final").ColorString(WideKit.Muted), 7, 22f);
            return;
        }
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(next);
        AddLine(stageCard, half, string.Format(LM.Get("transmigrant_senate_stage_next"), LM.Get("institution_" + next))
            .ColorString(WideKit.Soft), 7, 11f);
        int cities = _kingdom.cities.Count, required = node?.requires_cities ?? 0;
        stageCard.AddMetricRow(half,
            (LM.Get("transmigrant_senate_metric_cities"), required > 0 ? $"{cities}/{required}" : cities.ToString(),
                required > 0 && cities < required ? WideKit.Orange : WideKit.Green),
            (LM.Get("transmigrant_senate_metric_stage"), InstitutionSystem.IsEnacted(TransmigrantSystem.Culture, next)
                ? LM.Get("transmigrant_senate_stage_done") : LM.Get("transmigrant_senate_stage_pending"), WideKit.Cyan));
    }

    private static void AddLine(AutoVertLayoutGroup card, float width, string text, int size, float height) =>
        card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(width - 12f, height),
            mode: HorizontalWrapMode.Wrap).UseFixedFontSize(size, HorizontalWrapMode.Wrap);

    // 核心元老卡：头像、席位号、姓名、职务 → 年龄 / 第几代 / 分组
    private void AddSeatCard(AutoHoriLayoutGroup row, float width, SenatorSeat seat)
    {
        Actor actor = TransmigrantSystem.ActorOf(seat);
        var card = row.BeginColumnCard(width);
        string name = TransmigrantSystem.FullName(seat);
        if (seat.seat == _state.chairman_seat) name = name.ColorString(WideKit.Gold) + " ★".ColorString(WideKit.Orange);
        card.AddPersonRow(width, actor, string.Format(LM.Get("transmigrant_senate_seat_no"), seat.seat), name, PostText(seat));
        string group = TransmigrantSystem.GroupOf(seat);
        card.AddMetricRow(width,
            (LM.Get("talent_metric_age"), TransmigrantSystem.AgeOf(seat).ToString(), WideKit.Value),
            (LM.Get("transmigrant_senate_metric_generation"), GenerationText(seat), WideKit.Cyan),
            (LM.Get("transmigrant_senate_metric_group"), string.IsNullOrEmpty(group) ? "—" : group, WideKit.Soft));
        if (actor != null) UIHelper.MakeClickable(card.gameObject, () => UIHelper.actorClick(actor));
    }

    // 席位列表：一张卡，卡顶翻页，一行一席
    private void DrawList(AutoVertLayoutGroup page)
    {
        if (_shown.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "transmigrant_senate_empty_view");
            return;
        }
        int pages = PageCount;
        var card = page.BeginCard(PageWidth, "transmigrant_senate_list_title", WideKit.Gold,
            string.Format(LM.Get("transmigrant_senate_page"), _page + 1, pages));
        if (pages > 1)
            card.AddButtonRow(PageWidth,
                ("transmigrant_senate_prev", LM.Get("transmigrant_senate_prev"), () => Turn(-1)),
                ("transmigrant_senate_next", LM.Get("transmigrant_senate_next"), () => Turn(1)));
        int index = 0;
        foreach (SenatorSeat seat in _shown.Skip(_page * PageSize).Take(PageSize))
        {
            Actor actor = TransmigrantSystem.ActorOf(seat);
            string group = TransmigrantSystem.GroupOf(seat);
            string detail = string.Format(LM.Get("transmigrant_senate_line"), PostText(seat), TransmigrantSystem.AgeOf(seat),
                GenerationText(seat), string.IsNullOrEmpty(group) ? "" : " · " + group);
            string hex = seat.seat == _state.chairman_seat ? WideKit.Gold : actor != null ? WideKit.Value : WideKit.Soft;
            card.AddListRow(PageWidth, index++, null, $"{seat.seat}. {TransmigrantSystem.FullName(seat)}", detail,
                actor == null ? null : () => UIHelper.actorClick(actor), hex, 84f);
        }
    }

    private string PostText(SenatorSeat seat)
    {
        string post = TransmigrantSystem.PostOf(_state, seat);
        return string.IsNullOrEmpty(post) ? LM.Get("transmigrant_senator") : post;
    }

    private static string GenerationText(SenatorSeat seat) => seat.generation <= 1
        ? LM.Get("transmigrant_senate_generation_founder")
        : string.Format(LM.Get("transmigrant_senate_generation_n"), seat.generation);
}
