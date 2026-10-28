using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 外交界面(用户 2026-10-28 "国与国之间的外交关系界面，能够详细地查看和操作他们之间的外交关系"；方案 Docs/plan-diplomacy-window.md，
// 10-29 问答定操作按钮全部开放、往来大事全部列出；市场窗口并进来)。「卷册」宽窗口，数据都从 DiplomacyLedger 读：
//   · 左栏(固定)：焦点国 → 五页(总览、双边、操作、市场、体系图) → 总览页的筛选与搜索 / 双边页的对方与互换；
//   · 总览：主指标(盟友、交战、附属、态度最好最坏) → 与每一国一行(态度、关系标签)，点一行进双边页；
//   · 双边：两国的关系与条约(生效、到期) → 态度及其构成(双向) → 力量对比(本国 / 连同附属) → 往来大事(全部，翻页)；
//   · 操作：上帝视角的按钮，一行一项，能做的金字可点，不能的灰字写原因(见 DiplomacyActions)；宣战先挑目标；
//   · 市场：焦点国所在的共同市场——领衔、成员、外贸、奢侈品，各物资的产量需求价格缺口进出口，关税同盟的签与退，各大市场一览；
//   · 体系图：焦点国所在的宗主一系(附庸、陪臣、殖民地、帝国成员、朝贡国)的树，所在天下的各家占比。
// 入口：帝国页的「外交」权能点地图上的国家；国情窗口左栏的「外交」。
public class DiplomacyWindow : WideScrollWindow<DiplomacyWindow>
{
    private enum Page { Overview, Bilateral, Actions, Market, System }

    private enum Filter { All, Neighbours, Tianxia, Empire, Market, War }

    private const int ListPageSize = 40, HistoryPageSize = 18;
    private const float HalfWidth = UIHelper.PageColumnWidth;

    private static long _pendingFocus = -1L, _pendingOther = -1L;
    private static bool _pendingMarket;

    private Page _page = Page.Overview;
    private Filter _filter = Filter.All;
    private string _search = "";
    private int _listPage, _historyPage;
    private bool _bloc;
    private WarGoal _goal = WarGoal.夺城;
    private long _focusId = -1L, _otherId = -1L;
    private string _notice = "";

    private Kingdom _focus, _other;
    private List<Kingdom> _rows = new();

    public static void Open(Kingdom focus, Kingdom other = null)
    {
        if (!Alive(focus)) return;
        _pendingFocus = focus.id;
        _pendingOther = Alive(other) && other != focus ? other.id : -1L;
        ScrollWindow.showWindow(nameof(DiplomacyWindow));
    }

    // 市场图层点地块：以这国为焦点直接打开市场页(见 MarketMap)
    public static void OpenMarket(Kingdom focus)
    {
        if (!Alive(focus)) return;
        _pendingMarket = true;
        Open(focus);
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    protected override void OnOpen()
    {
        if (_pendingFocus >= 0)
        {
            _focusId = _pendingFocus;
            _otherId = _pendingOther;
            _page = _pendingMarket ? Page.Market : _otherId >= 0 ? Page.Bilateral : Page.Overview;
        }
        _pendingFocus = _pendingOther = -1L;
        _pendingMarket = false;
        _filter = Filter.All;
        _search = "";
        _listPage = _historyPage = 0;
        _notice = "";
    }

    // ───────── 数据 ─────────
    protected override void Prepare()
    {
        _focus = World.world?.kingdoms?.get(_focusId);
        if (!Alive(_focus)) _focus = SelectedMetas.selected_kingdom;
        if (!Alive(_focus)) _focus = DiplomacyLedger.Realms().OrderByDescending(kingdom => kingdom.cities.Count).FirstOrDefault();
        _focusId = _focus?.id ?? -1L;
        _other = World.world?.kingdoms?.get(_otherId);
        if (!Alive(_other) || _other == _focus) _other = null;
        _rows = Alive(_focus) ? Rows() : new List<Kingdom>();
        _listPage = Mathf.Clamp(_listPage, 0, Mathf.Max(0, (_rows.Count - 1) / ListPageSize));
    }

    private List<Kingdom> Rows()
    {
        IEnumerable<Kingdom> rows = DiplomacyLedger.Realms().Where(kingdom => kingdom != _focus);
        rows = _filter switch
        {
            Filter.Neighbours => rows.Where(kingdom => DiplomacyLedger.Neighbours(_focus, kingdom)),
            Filter.Tianxia => rows.Where(kingdom => DiplomacyLedger.SameTianxia(_focus, kingdom)),
            Filter.Empire => rows.Where(kingdom => DiplomacyLedger.SameBloc(_focus, kingdom)),
            Filter.Market => rows.Where(kingdom => DiplomacyLedger.SameMarket(_focus, kingdom)),
            Filter.War => rows.Where(kingdom => _focus.isEnemy(kingdom)),
            _ => rows
        };
        if (_search.Length > 0) rows = rows.Where(kingdom => kingdom.GetKingdomFullName().Contains(_search));
        // 有关系的在前，再按态度
        return rows.OrderByDescending(kingdom => DiplomacyLedger.Ties(_focus, kingdom).Count > 0)
            .ThenByDescending(kingdom => DiplomacyLedger.Opinion(_focus, kingdom)).ToList();
    }

    private void Go(Page page, Kingdom other = null)
    {
        _page = page;
        if (other != null) _otherId = other.id;
        _historyPage = 0;
        _notice = "";
        Refresh(resetScroll: true);
    }

    private void Focus(Kingdom kingdom)
    {
        if (!Alive(kingdom)) return;
        Kingdom previous = _focus;
        _focusId = kingdom.id;
        _otherId = previous != null && previous != kingdom ? previous.id : -1L;
        _listPage = 0;
        _notice = "";
        Refresh(resetScroll: true);
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (!Alive(_focus))
        {
            rail.AddRailTitle(LM.Get("diplomacy_window"), LM.Get("diplomacy_pick_hint"));
            return;
        }
        rail.AddRailTitle(_focus.GetKingdomFullName(), DiplomacyLedger.Standing(_focus));

        var pages = rail.BeginRailMenu();
        int index = 0;
        foreach (Page page in new[] { Page.Overview, Page.Bilateral, Page.Actions, Page.Market, Page.System })
        {
            Page target = page;
            string count = page == Page.Overview ? _rows.Count.ToString() : null;
            pages.AddRailOption(null, LM.Get(PageKey(page)), count, _page == page, () => Go(target), index++);
        }

        if (_page == Page.Overview)
        {
            var filters = rail.BeginRailMenu(LM.Get("diplomacy_filter"));
            index = 0;
            foreach (Filter filter in new[] { Filter.All, Filter.Neighbours, Filter.Tianxia, Filter.Empire, Filter.Market, Filter.War })
            {
                Filter target = filter;
                filters.AddRailOption(null, LM.Get("diplomacy_filter_" + filter.ToString().ToLowerInvariant()), null,
                    _filter == filter, () =>
                    {
                        _filter = target;
                        _listPage = 0;
                        Refresh(resetScroll: true);
                    }, index++);
            }
            var search = rail.BeginRailCard();
            search.AddRailInput(LM.Get("diplomacy_search"), _search, text =>
            {
                string value = (text ?? "").Trim();
                if (value == _search) return;
                _search = value;
                _listPage = 0;
                if (gameObject.activeInHierarchy) StartCoroutine(RefreshNextFrame());
            });
        }
        else if (_page is Page.Bilateral or Page.Actions && _other != null)
        {
            var pair = rail.BeginRailMenu(LM.Get("diplomacy_counterpart"));
            pair.AddRailOption(null, _other.GetKingdomFullName(), null, true, () => Go(Page.Bilateral), 0);
            rail.AddRailButton("diplomacy_swap", LM.Get("diplomacy_swap"), () => Focus(_other));
        }
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh(resetScroll: true);
    }

    private static string PageKey(Page page) => "diplomacy_page_" + page.ToString().ToLowerInvariant();

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (!Alive(_focus))
        {
            page.AddPageHeader(PageWidth, LM.Get("diplomacy_window"));
            page.AddEmptyCard(PageWidth, "diplomacy_empty", LM.Get("diplomacy_pick_hint"));
            return;
        }
        if (!string.IsNullOrEmpty(_notice))
            page.AddParagraph(PageWidth, _notice, 7, WideKit.Orange);
        switch (_page)
        {
            case Page.Bilateral:
                DrawBilateral(page);
                break;
            case Page.Actions:
                DrawActions(page);
                break;
            case Page.Market:
                DrawMarket(page);
                break;
            case Page.System:
                DrawSystem(page);
                break;
            default:
                DrawOverview(page);
                break;
        }
    }

    private static string Name(Kingdom kingdom) =>
        Alive(kingdom) ? kingdom.GetKingdomFullName().ColorString(kingdom.getColor().color_text) : LM.Get("label_none");

    private static string OpinionText(int value) => (value > 0 ? "+" + value : value.ToString()).ColorString(OpinionHex(value));

    private static string OpinionHex(int value) => value >= 50 ? WideKit.Green : value >= 0 ? WideKit.Value : value > -50 ? WideKit.Orange : WideKit.Red;

    // 总览
    private void DrawOverview(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Overview)), string.Format(LM.Get("diplomacy_chip_count"), _rows.Count));
        List<Kingdom> all = DiplomacyLedger.Realms().Where(kingdom => kingdom != _focus).ToList();
        int allies = all.Count(kingdom => DiplomacyLedger.Allied(_focus, kingdom));
        int wars = all.Count(kingdom => _focus.isEnemy(kingdom));
        int dependents = DiplomacyLedger.Dependents(_focus).Count;
        Kingdom best = all.OrderByDescending(kingdom => DiplomacyLedger.Opinion(_focus, kingdom)).FirstOrDefault();
        Kingdom worst = all.OrderBy(kingdom => DiplomacyLedger.Opinion(_focus, kingdom)).FirstOrDefault();
        page.AddKpiRow(PageWidth,
            (LM.Get("diplomacy_kpi_allies"), allies.ToString(), WideKit.Green),
            (LM.Get("diplomacy_kpi_wars"), wars.ToString(), wars > 0 ? WideKit.Red : WideKit.Value),
            (LM.Get("diplomacy_kpi_dependents"), dependents.ToString(), WideKit.Cyan),
            (LM.Get("diplomacy_kpi_best"), best == null ? "—" : best.GetKingdomName(), WideKit.Gold));
        if (worst != null && DiplomacyLedger.Opinion(_focus, worst) < 0)
            page.AddParagraph(PageWidth, string.Format(LM.Get("diplomacy_worst_line"), Name(worst),
                OpinionText(DiplomacyLedger.Opinion(_focus, worst))), 7, WideKit.Muted);

        if (_rows.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "diplomacy_empty_rows");
            return;
        }
        int pages = (_rows.Count + ListPageSize - 1) / ListPageSize;
        var card = page.BeginCard(PageWidth, "diplomacy_relations_title", WideKit.Gold, LM.Get("diplomacy_relations_hint"));
        card.AddPager(PageWidth - 12f, _listPage, pages, LM.Get("diplomacy_page_format"), target =>
        {
            _listPage = target;
            Refresh(resetScroll: true);
        });
        int index = 0;
        foreach (Kingdom kingdom in _rows.Skip(_listPage * ListPageSize).Take(ListPageSize))
        {
            Kingdom target = kingdom;
            int opinion = DiplomacyLedger.Opinion(_focus, kingdom);
            string tags = string.Join(" · ", DiplomacyLedger.Ties(_focus, kingdom).Select(tie => tie.text.ColorString(tie.hex)));
            string detail = $"{OpinionText(opinion)}  {tags}";
            card.AddListRow(PageWidth, index++, null, Name(kingdom), detail, () => Go(Page.Bilateral, target), WideKit.Value, 96f);
        }
    }

    // 双边
    private void DrawBilateral(AutoVertLayoutGroup page)
    {
        if (_other == null)
        {
            page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Bilateral)));
            page.AddEmptyCard(PageWidth, "diplomacy_pick_other", LM.Get("diplomacy_pick_other_hint"));
            return;
        }
        page.AddPageHeader(PageWidth, $"{_focus.GetKingdomName()} ⇄ {_other.GetKingdomName()}",
            LM.Get(_focus.isEnemy(_other) ? "diplomacy_chip_war" : "diplomacy_chip_peace"));

        // 关系与条约
        List<DiplomacyLedger.Tie> ties = DiplomacyLedger.Ties(_focus, _other);
        var treaties = page.BeginCard(PageWidth, "diplomacy_ties_title", WideKit.Cyan, ties.Count.ToString());
        if (ties.Count == 0) treaties.AddParagraph(PageWidth, LM.Get("diplomacy_no_ties"), 7, WideKit.Muted);
        int index = 0;
        foreach (DiplomacyLedger.Tie tie in ties)
            treaties.AddValueRow(PageWidth, index++, tie.text, DiplomacyLedger.TermText(tie), tie.hex);

        // 态度构成(双向)
        var columns = page.BeginColumns(PageWidth);
        DrawOpinion(columns, _focus, _other);
        DrawOpinion(columns, _other, _focus);

        // 力量对比
        var power = page.BeginCard(PageWidth, "diplomacy_power_title", WideKit.Gold);
        power.AddChipRow(PageWidth - 12f,
            (LM.Get("diplomacy_power_own"), !_bloc, () => SetBloc(false)),
            (LM.Get("diplomacy_power_bloc"), _bloc, () => SetBloc(true)));
        DiplomacyLedger.Strength a = DiplomacyLedger.StrengthOf(_focus, _bloc), b = DiplomacyLedger.StrengthOf(_other, _bloc);
        power.AddVersusRow(PageWidth, 0, _focus.GetKingdomName(), "", _other.GetKingdomName(), WideKit.Gold, WideKit.Gold);
        index = 1;
        foreach ((string key, float left, float right) in new[]
                 {
                     ("national_power", a.power, b.power), ("diplomacy_power_army", a.army, b.army),
                     ("diplomacy_power_navy", a.navy, b.navy), ("diplomacy_power_cities", a.cities, b.cities),
                     ("i_population", a.population, b.population), ("diplomacy_power_money", a.money, b.money)
                 })
            power.AddVersusRow(PageWidth, index++, UIHelper.ShortCount((long)left), LM.Get(key), UIHelper.ShortCount((long)right),
                left >= right ? WideKit.Green : WideKit.Value, right >= left ? WideKit.Green : WideKit.Value);

        // 往来大事
        List<(double at, string text)> history = DiplomacyLedger.History(_focus, _other);
        var chronicle = page.BeginCard(PageWidth, "diplomacy_history_title", WideKit.Muted, history.Count.ToString());
        if (history.Count == 0)
        {
            chronicle.AddParagraph(PageWidth, LM.Get("diplomacy_no_history"), 7, WideKit.Muted);
            return;
        }
        int pages = (history.Count + HistoryPageSize - 1) / HistoryPageSize;
        _historyPage = Mathf.Clamp(_historyPage, 0, pages - 1);
        chronicle.AddPager(PageWidth - 12f, _historyPage, pages, LM.Get("diplomacy_page_format"), target =>
        {
            _historyPage = target;
            Refresh();
        });
        index = 0;
        foreach ((double at, string text) in history.Skip(_historyPage * HistoryPageSize).Take(HistoryPageSize))
            chronicle.AddTimelineRow(PageWidth, index++, HistoryDateFormatter.GetYear(at), text);
    }

    private void SetBloc(bool bloc)
    {
        if (_bloc == bloc) return;
        _bloc = bloc;
        Refresh();
    }

    private void DrawOpinion(AutoHoriLayoutGroup columns, Kingdom from, Kingdom to)
    {
        int total = DiplomacyLedger.Opinion(from, to);
        var card = columns.BeginColumnCard(HalfWidth, string.Format(LM.Get("diplomacy_opinion_title"), from.GetKingdomName()),
            WideKit.Gold, OpinionText(total));
        List<(string label, int value)> parts = DiplomacyLedger.OpinionParts(from, to);
        if (parts.Count == 0) card.AddParagraph(HalfWidth, LM.Get("diplomacy_no_opinion_parts"), 6, WideKit.Muted);
        int index = 0;
        foreach ((string label, int value) in parts)
            card.AddValueRow(HalfWidth, index++, label, value > 0 ? "+" + value : value.ToString(), OpinionHex(value * 4));
    }

    // 操作
    private void DrawActions(AutoVertLayoutGroup page)
    {
        if (_other == null)
        {
            page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Actions)));
            page.AddEmptyCard(PageWidth, "diplomacy_pick_other", LM.Get("diplomacy_pick_other_hint"));
            return;
        }
        page.AddPageHeader(PageWidth, $"{_focus.GetKingdomName()} → {_other.GetKingdomName()}", LM.Get("diplomacy_actions_hint"));
        foreach (DiplomacyActions.Group group in DiplomacyActions.Groups)
        {
            var card = page.BeginCard(PageWidth, group.titleKey, WideKit.Gold);
            if (group.withGoals)
            {
                // 宣战先挑目标
                var chips = card.AddChipRow(PageWidth - 12f);
                foreach (WarGoal goal in DiplomacyActions.Goals)
                {
                    WarGoal target = goal;
                    chips.AddChip(DiplomacyActions.GoalName(goal), _goal == goal, () =>
                    {
                        _goal = target;
                        Refresh();
                    });
                }
            }
            int index = 0;
            foreach (DiplomacyActions.Action action in group.actions)
            {
                string reason = DiplomacyActions.Check(action, _focus, _other);
                bool can = reason == null;
                DiplomacyActions.Action chosen = action;
                card.AddListRow(PageWidth, index++, null, LM.Get(action.key),
                    can ? LM.Get(action.key + "_desc").ColorString(WideKit.Soft) : LM.Get(reason).ColorString(WideKit.Muted),
                    can ? () => Act(chosen) : null, can ? WideKit.Gold : WideKit.Muted, 96f);
            }
        }
    }

    private void Act(DiplomacyActions.Action action)
    {
        string failure = DiplomacyActions.Do(action, _focus, _other, _goal);
        _notice = failure == null
            ? string.Format(LM.Get("diplomacy_done"), LM.Get(action.key))
            : string.Format(LM.Get("diplomacy_failed"), LM.Get(action.key), LM.Get(failure));
        Refresh();
    }

    // 市场
    private void DrawMarket(AutoVertLayoutGroup page)
    {
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(_focus);
        page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Market)),
            market == null ? LM.Get("diplomacy_market_pending") : LM.Get(market.great ? "diplomacy_market_great" : "diplomacy_market_minor"));
        if (market == null)
        {
            page.AddEmptyCard(PageWidth, "diplomacy_market_pending", LM.Get("diplomacy_market_pending_hint"));
            return;
        }
        float shortage = market.shortage.Count == 0 ? 0f : market.shortage.Values.Average();
        page.AddKpiRow(PageWidth,
            (LM.Get("diplomacy_market_leader"), market.leader?.GetKingdomName() ?? "—", WideKit.Gold),
            (LM.Get("diplomacy_market_members"), market.members.Count.ToString(), WideKit.Cyan),
            (LM.Get("diplomacy_market_trade"), UIHelper.ShortCount((long)market.trade_value), WideKit.Green),
            (LM.Get("diplomacy_market_luxury"), $"{Mathf.RoundToInt(CommodityMarketSystem.LuxurySatisfaction(_focus) * 100f)}%",
                WideKit.Value));
        // 整个市场的汇总另有窗口(见 MarketWindow)
        var summary = page.BeginCard(PageWidth);
        Kingdom country = _focus;
        summary.AddListRow(PageWidth, 0, null, string.Format(LM.Get("diplomacy_open_market_window"),
                EmpireCraft.Scripts.Layer.MarketMap.Name(market.leader)), "", () => MarketWindow.Open(country), WideKit.Gold,
            PageWidth - 20f);

        // 成员与关税同盟
        var columns = page.BeginColumns(PageWidth);
        var members = columns.BeginColumnCard(HalfWidth, "diplomacy_market_members_title", WideKit.Cyan, market.members.Count.ToString());
        int index = 0;
        foreach (Kingdom member in market.members.Where(Alive).Take(14))
        {
            Kingdom target = member;
            members.AddListRow(HalfWidth, index++, null, member.GetKingdomName(), DiplomacyLedger.MarketRole(member, market.leader),
                () => Go(Page.Bilateral, target), member == market.leader ? WideKit.Gold : WideKit.Value, 64f);
        }
        if (market.members.Count > 14)
            members.AddParagraph(HalfWidth, string.Format(LM.Get("diplomacy_more"), market.members.Count - 14), 6, WideKit.Muted);
        var union = columns.BeginColumnCard(HalfWidth, "diplomacy_union_title", WideKit.Gold);
        Kingdom unionLeader = CommodityMarketSystem.UnionOf(_focus);
        index = 0;
        if (unionLeader != null)
        {
            union.AddParagraph(HalfWidth, string.Format(LM.Get("diplomacy_union_in"), Name(unionLeader)), 7, WideKit.Soft);
            union.AddListRow(HalfWidth, index, null, LM.Get("diplomacy_union_leave"), "", () => UnionAct(CommodityMarketSystem.LeaveUnion(_focus)),
                WideKit.Gold, HalfWidth - 20f);
        }
        else
        {
            union.AddParagraph(HalfWidth, LM.Get("diplomacy_union_none"), 7, WideKit.Muted);
            foreach (CommodityMarketSystem.Market other in CommodityMarketSystem.AllMarkets
                         .Where(other => other != market && other.great && Alive(other.leader))
                         .OrderByDescending(other => other.power).Take(8))
            {
                Kingdom leader = other.leader;
                union.AddListRow(HalfWidth, index++, null, string.Format(LM.Get("diplomacy_union_join"), leader.GetKingdomName()), "",
                    () => UnionAct(CommodityMarketSystem.JoinUnion(_focus, leader)), WideKit.Value, HalfWidth - 20f);
            }
        }

        // 各物资
        var goods = page.BeginCard(PageWidth, "diplomacy_goods_title", WideKit.Gold, LM.Get("diplomacy_goods_hint"));
        goods.AddTableRow(PageWidth, -1, null, LM.Get("diplomacy_goods_name"), 64f,
            (LM.Get("diplomacy_goods_made"), ""), (LM.Get("diplomacy_goods_wanted"), ""), (LM.Get("diplomacy_goods_price"), ""),
            (LM.Get("diplomacy_goods_short"), ""), (LM.Get("diplomacy_goods_import"), ""), (LM.Get("diplomacy_goods_export"), ""));
        index = 0;
        foreach (CommodityConfig good in CommodityMarketSystem.AllGoods)
        {
            market.supply.TryGetValue(good.id, out float made);
            market.demand.TryGetValue(good.id, out float wanted);
            market.price.TryGetValue(good.id, out float price);
            market.shortage.TryGetValue(good.id, out float shortageOf);
            market.imports.TryGetValue(good.id, out float imported);
            market.exports.TryGetValue(good.id, out float exported);
            if (made <= 0f && wanted <= 0f) continue;
            goods.AddTableRow(PageWidth, index++, null, DiplomacyLedger.GoodName(good), 64f,
                (Mathf.RoundToInt(made).ToString(), WideKit.Value), (Mathf.RoundToInt(wanted).ToString(), WideKit.Soft),
                ((price > 0f ? price : good.price).ToString("0.0"), WideKit.Gold),
                ($"{Mathf.RoundToInt(shortageOf * 100f)}%", shortageOf >= 0.5f ? WideKit.Red : shortageOf > 0.05f ? WideKit.Orange : WideKit.Green),
                (imported > 0f ? Mathf.RoundToInt(imported).ToString() : "—", WideKit.Cyan),
                (exported > 0f ? Mathf.RoundToInt(exported).ToString() : "—", WideKit.Green));
        }

        // 各大市场
        var markets = page.BeginCard(PageWidth, "diplomacy_markets_title", WideKit.Muted);
        index = 0;
        foreach (CommodityMarketSystem.Market other in CommodityMarketSystem.AllMarkets.Where(other => Alive(other.leader))
                     .OrderByDescending(other => other.power).Take(10))
        {
            Kingdom leader = other.leader;
            markets.AddListRow(PageWidth, index++, null, leader.GetKingdomName(),
                string.Format(LM.Get("diplomacy_markets_line"), other.members.Count, UIHelper.ShortCount((long)other.trade_value),
                    LM.Get(other.great ? "diplomacy_market_great" : "diplomacy_market_minor")),
                () => MarketWindow.Open(leader, false), other == market ? WideKit.Gold : WideKit.Value, 96f);
        }
    }

    private void UnionAct(string failure)
    {
        _notice = failure == null ? LM.Get("diplomacy_union_done") : LM.Get(failure);
        Refresh();
    }

    // 体系图
    private void DrawSystem(AutoVertLayoutGroup page)
    {
        Kingdom root = DiplomacyLedger.TopOf(_focus);
        page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.System)), root == null ? "" : root.GetKingdomName());
        var tree = page.BeginCard(PageWidth, "diplomacy_tree_title", WideKit.Gold);
        int index = 0;
        foreach ((Kingdom kingdom, int depth, string role) in DiplomacyLedger.Tree(root))
        {
            Kingdom target = kingdom;
            string indent = depth == 0 ? "" : new string('　', depth - 1) + "└ ";
            tree.AddListRow(PageWidth, index++, null, indent + kingdom.GetKingdomName(), role,
                () => Focus(target), kingdom == _focus ? WideKit.Gold : depth == 0 ? WideKit.Cyan : WideKit.Value, 130f);
        }

        // 天下
        EmpireCore lead = _focus.capital == null ? null : TianxiaSystem.LeadOf(_focus.capital);
        if (lead == null) return;
        (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
        Kingdom holder = TianxiaSystem.Holder(lead);
        var realm = page.BeginCard(PageWidth, TianxiaSystem.Name(lead), WideKit.Cyan,
            holder == null ? LM.Get("diplomacy_tianxia_vacant") : string.Format(LM.Get("diplomacy_tianxia_holder"), holder.GetKingdomName()));
        index = 0;
        foreach (KeyValuePair<Kingdom, int> pair in held.Where(pair => Alive(pair.Key)).OrderByDescending(pair => pair.Value).Take(12))
        {
            Kingdom target = pair.Key;
            float share = total == 0 ? 0f : pair.Value / (float)total;
            realm.AddBarRow(PageWidth, index++, pair.Key.GetKingdomName(), share,
                $"{pair.Value}/{total} · {Mathf.RoundToInt(share * 100f)}%", WideKit.Value,
                pair.Key == holder ? WideKit.Gold : WideKit.Cyan, "#3A4038", () => Focus(target), 80f, 150f);
        }
    }
}
