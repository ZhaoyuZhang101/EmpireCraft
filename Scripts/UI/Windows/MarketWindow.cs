using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 市场汇总(用户 2026-10-29 "我需要市场的总界面，比如我点击这个明市场进去的应该是明市场的汇总界面"
// "帝国模组的国家界面可以单独进入国家市场界面")。「卷册」宽窗口，一个共同市场一页汇总，数据都从 CommodityMarketSystem 的年度结算读：
//   · 左栏：市场名(领衔国 + 市场)、大国/小国市场 → 四页(总览、物资、成员、贸易) → 从哪国进来的(本国) → 各大市场(切换) → 去外交界面；
//   · 总览：主指标(成员、城、外贸、物资缺口、国力) → 本国在这个市场里(从国家进来时) → 最缺、富余两栏 → 主要成员 → 贸易伙伴；
//   · 物资：各物资的产量、需求、价格、缺口、进出口；
//   · 成员：各成员国的身份、城数、一年产出与需求(按底价折钱)、奢侈品满足，点一行看那国的外交界面；
//   · 贸易：进出口额、能通商的市场，与各市场的买卖，进口、出口最多的物资。
// 入口：市场图层点地块(见 MarketMap)；国情窗口左栏「市场」；外交界面市场页里的汇总按钮与各大市场。
public class MarketWindow : WideScrollWindow<MarketWindow>
{
    private enum Page { Overview, Goods, Members, Trade }

    private const int MemberPageSize = 30;
    private const float HalfWidth = UIHelper.PageColumnWidth;

    private static long _pendingLeader = -1L, _pendingFocus = -1L;

    private Page _page = Page.Overview;
    private long _leaderId = -1L, _focusId = -1L;
    private int _memberPage;
    private Kingdom _leader, _focus;
    private CommodityMarketSystem.Market _market;

    // 打开某国所在的市场；country 为真时把这国当作"本国"(总览里多一张本国在市场里的卡)
    public static void Open(Kingdom kingdom, bool country = true)
    {
        if (!Alive(kingdom)) return;
        Kingdom leader = CommodityMarketSystem.MarketOf(kingdom)?.leader;
        if (!Alive(leader)) leader = CommodityMarketSystem.RootOf(kingdom) ?? kingdom;
        _pendingLeader = leader.id;
        _pendingFocus = country ? kingdom.id : -1L;
        ScrollWindow.showWindow(nameof(MarketWindow));
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    protected override void OnOpen()
    {
        if (_pendingLeader >= 0)
        {
            _leaderId = _pendingLeader;
            _focusId = _pendingFocus;
            _page = Page.Overview;
        }
        _pendingLeader = _pendingFocus = -1L;
        _memberPage = 0;
    }

    protected override void Prepare()
    {
        _leader = World.world?.kingdoms?.get(_leaderId);
        _focus = World.world?.kingdoms?.get(_focusId);
        if (!Alive(_focus)) _focus = null;
        _market = Alive(_leader) ? CommodityMarketSystem.MarketOf(_leader) : null;
        if (_market != null && _market.leader != _leader) _market = null;
        if (_market == null && _focus != null) _market = CommodityMarketSystem.MarketOf(_focus);
        if (_market == null) _market = CommodityMarketSystem.AllMarkets.Where(market => Alive(market.leader))
            .OrderByDescending(market => market.power).FirstOrDefault();
        _leader = _market?.leader ?? _leader;
        _leaderId = _leader?.id ?? -1L;
    }

    private void Go(Page page)
    {
        _page = page;
        _memberPage = 0;
        Refresh(resetScroll: true);
    }

    private void Switch(Kingdom leader)
    {
        _leaderId = leader.id;
        _memberPage = 0;
        Refresh(resetScroll: true);
    }

    private string Title() => _leader == null ? LM.Get("market_window") : MarketMap.Name(_leader);

    private string Kind() => LM.Get(_market.great ? "diplomacy_market_great" : "diplomacy_market_minor");

    private static string PageKey(Page page) => "market_page_" + page.ToString().ToLowerInvariant();

    private static string Money(float value) => UIHelper.ShortCount((long)Mathf.Round(value));

    private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (_market == null)
        {
            rail.AddRailTitle(LM.Get("market_window"), LM.Get("diplomacy_market_pending"));
            return;
        }
        rail.AddRailTitle(Title(), $"{Kind()} · {string.Format(LM.Get("diplomacy_chip_count"), _market.members.Count)}");

        var pages = rail.BeginRailMenu();
        int index = 0;
        foreach (Page page in new[] { Page.Overview, Page.Goods, Page.Members, Page.Trade })
        {
            Page target = page;
            string count = page == Page.Members ? _market.members.Count.ToString() : null;
            pages.AddRailOption(null, LM.Get(PageKey(page)), count, _page == page, () => Go(target), index++);
        }

        if (_focus != null)
        {
            var country = rail.BeginRailMenu(LM.Get("market_rail_country"));
            Kingdom focus = _focus;
            country.AddRailOption(null, focus.GetKingdomName(), null, _market.members.Contains(focus), () =>
            {
                Kingdom leader = CommodityMarketSystem.MarketOf(focus)?.leader;
                if (Alive(leader)) _leaderId = leader.id;
                Go(Page.Overview);
            }, 0);
        }

        var markets = rail.BeginRailMenu(LM.Get("market_rail_markets"));
        index = 0;
        foreach (CommodityMarketSystem.Market other in CommodityMarketSystem.AllMarkets.Where(other => Alive(other.leader))
                     .OrderByDescending(other => other.power).Take(10))
        {
            Kingdom leader = other.leader;
            markets.AddRailOption(null, leader.GetKingdomName(), other.members.Count.ToString(), other == _market,
                () => Switch(leader), index++);
        }
        Kingdom diplomacy = _focus ?? _leader;
        rail.AddRailButton("market_open_diplomacy", LM.Get("market_open_diplomacy"), () => DiplomacyWindow.OpenMarket(diplomacy));
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (_market == null)
        {
            page.AddPageHeader(PageWidth, LM.Get("market_window"));
            page.AddEmptyCard(PageWidth, "diplomacy_market_pending", LM.Get("diplomacy_market_pending_hint"));
            return;
        }
        switch (_page)
        {
            case Page.Goods:
                DrawGoods(page);
                break;
            case Page.Members:
                DrawMembers(page);
                break;
            case Page.Trade:
                DrawTrade(page);
                break;
            default:
                DrawOverview(page);
                break;
        }
    }

    private void DrawOverview(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, Title(), Kind());
        float shortage = MarketMap.Shortage(_market);
        int level = MarketMap.Bucket(shortage);
        page.AddKpiRow(PageWidth,
            (LM.Get("diplomacy_market_members"), _market.members.Count.ToString(), WideKit.Cyan),
            (LM.Get("market_kpi_cities"), _market.cities.ToString(), WideKit.Value),
            (LM.Get("diplomacy_market_trade"), Money(_market.trade_value), WideKit.Green),
            (LM.Get("market_layer_shortage"), $"{Percent(shortage)} {LM.Get("market_short_level_" + level)}",
                level >= 3 ? WideKit.Red : level >= 1 ? WideKit.Orange : WideKit.Green),
            (LM.Get("market_kpi_power"), UIHelper.ShortCount((long)_market.power), WideKit.Gold));

        if (_focus != null && _market.members.Contains(_focus)) DrawCountry(page);

        var columns = page.BeginColumns(PageWidth);
        var shortCard = columns.BeginColumnCard(HalfWidth, "market_short_title", WideKit.Red);
        int index = 0;
        foreach (CommodityConfig good in CommodityMarketSystem.AllGoods
                     .Where(good => _market.shortage.TryGetValue(good.id, out float value) && value >= 0.05f)
                     .OrderByDescending(good => _market.shortage[good.id]).Take(8))
        {
            float value = _market.shortage[good.id];
            shortCard.AddBarRow(HalfWidth, index++, DiplomacyLedger.GoodName(good), value, Percent(value),
                value >= 0.5f ? WideKit.Red : WideKit.Orange, value >= 0.5f ? "#B5483A" : "#C9893A", "#2C2A26");
        }
        if (index == 0) shortCard.AddParagraph(HalfWidth, LM.Get("market_no_short"), 7, WideKit.Muted);
        var spareCard = columns.BeginColumnCard(HalfWidth, "market_surplus_title", WideKit.Green);
        index = 0;
        foreach ((CommodityConfig good, float spare) in CommodityMarketSystem.AllGoods
                     .Select(good => (good, Surplus(good.id))).Where(pair => pair.Item2 >= 1f)
                     .OrderByDescending(pair => pair.Item2 * pair.good.price).Take(8))
        {
            _market.exports.TryGetValue(good.id, out float sold);
            spareCard.AddValueRow(HalfWidth, index++, DiplomacyLedger.GoodName(good),
                sold > 0f ? string.Format(LM.Get("market_spare_sold"), Mathf.RoundToInt(spare), Mathf.RoundToInt(sold))
                    : Mathf.RoundToInt(spare).ToString(), WideKit.Green);
        }
        if (index == 0) spareCard.AddParagraph(HalfWidth, LM.Get("market_no_surplus"), 7, WideKit.Muted);

        var membersCard = page.BeginCard(PageWidth, "market_members_title", WideKit.Cyan, _market.members.Count.ToString());
        index = 0;
        foreach (Kingdom member in Members().Take(6)) MemberRow(membersCard, member, index++);
        if (_market.members.Count > 6)
            membersCard.AddListRow(PageWidth, index, null, LM.Get("market_page_members"),
                string.Format(LM.Get("diplomacy_more"), _market.members.Count - 6), () => Go(Page.Members), WideKit.Gold);

        var partners = page.BeginCard(PageWidth, "market_partners_title", WideKit.Gold);
        DrawPartners(partners, 5);
    }

    // 本国在这个市场里：身份、产出与需求占全市场几成、奢侈品满足、关税同盟
    private void DrawCountry(AutoVertLayoutGroup page)
    {
        var card = page.BeginCard(PageWidth, null, WideKit.Gold);
        card.AddParagraph(PageWidth, string.Format(LM.Get("market_country_title"), _focus.GetKingdomName()), 8, WideKit.Gold);
        _market.memberValue.TryGetValue(_focus.id, out var own);
        float made = _market.memberValue.Values.Sum(value => value.made), wanted = _market.memberValue.Values.Sum(value => value.wanted);
        float madeShare = made > 0f ? own.made / made : 0f, wantedShare = wanted > 0f ? own.wanted / wanted : 0f;
        card.AddValueRow(PageWidth, 0, LM.Get("market_country_role"), DiplomacyLedger.MarketRole(_focus, _market.leader), WideKit.Value);
        card.AddBarRow(PageWidth, 1, LM.Get("market_country_made"), madeShare, $"{Percent(madeShare)} · {Money(own.made)}",
            WideKit.Green, "#5C9A5A", "#2C2A26");
        card.AddBarRow(PageWidth, 2, LM.Get("market_country_wanted"), wantedShare, $"{Percent(wantedShare)} · {Money(own.wanted)}",
            WideKit.Orange, "#C9893A", "#2C2A26");
        float luxury = CommodityMarketSystem.LuxurySatisfaction(_focus);
        card.AddBarRow(PageWidth, 3, LM.Get("diplomacy_market_luxury"), luxury, Percent(luxury),
            luxury >= 0.6f ? WideKit.Green : luxury >= 0.3f ? WideKit.Orange : WideKit.Red, "#8FA0A8", "#2C2A26");
        Kingdom union = CommodityMarketSystem.UnionOf(_focus);
        card.AddValueRow(PageWidth, 4, LM.Get("diplomacy_union_title"),
            union != null ? string.Format(LM.Get("diplomacy_union_in"), union.GetKingdomName()) : LM.Get("label_none"),
            union != null ? WideKit.Gold : WideKit.Muted);
    }

    private float Surplus(string good)
    {
        _market.supply.TryGetValue(good, out float supply);
        _market.demand.TryGetValue(good, out float demand);
        return supply - demand;
    }

    private IEnumerable<Kingdom> Members() =>
        _market.members.Where(Alive).OrderByDescending(member => member == _market.leader)
            .ThenByDescending(member => _market.memberValue.TryGetValue(member.id, out var value) ? value.made : 0f);

    private void MemberRow(AutoVertLayoutGroup card, Kingdom member, int index)
    {
        _market.memberValue.TryGetValue(member.id, out var value);
        Kingdom target = member;
        string detail = string.Format(LM.Get("market_member_line"), DiplomacyLedger.MarketRole(member, _market.leader),
            member.cities?.Count ?? 0, Money(value.made), Money(value.wanted),
            Percent(CommodityMarketSystem.LuxurySatisfaction(member)));
        card.AddListRow(PageWidth, index, null, member.GetKingdomName(), detail, () => DiplomacyWindow.OpenMarket(target),
            member == _market.leader ? WideKit.Gold : member == _focus ? WideKit.Cyan : WideKit.Value, 96f);
    }

    private void DrawGoods(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Goods)), Title());
        var goods = page.BeginCard(PageWidth, "diplomacy_goods_title", WideKit.Gold, LM.Get("diplomacy_goods_hint"));
        goods.AddTableRow(PageWidth, -1, null, LM.Get("diplomacy_goods_name"), 64f,
            (LM.Get("diplomacy_goods_made"), ""), (LM.Get("diplomacy_goods_wanted"), ""), (LM.Get("diplomacy_goods_price"), ""),
            (LM.Get("diplomacy_goods_short"), ""), (LM.Get("diplomacy_goods_import"), ""), (LM.Get("diplomacy_goods_export"), ""));
        int index = 0;
        foreach (CommodityConfig good in CommodityMarketSystem.AllGoods)
        {
            _market.supply.TryGetValue(good.id, out float made);
            _market.demand.TryGetValue(good.id, out float wanted);
            _market.price.TryGetValue(good.id, out float price);
            _market.shortage.TryGetValue(good.id, out float shortageOf);
            _market.imports.TryGetValue(good.id, out float imported);
            _market.exports.TryGetValue(good.id, out float exported);
            if (made <= 0f && wanted <= 0f) continue;
            goods.AddTableRow(PageWidth, index++, null, DiplomacyLedger.GoodName(good), 64f,
                (Mathf.RoundToInt(made).ToString(), WideKit.Value), (Mathf.RoundToInt(wanted).ToString(), WideKit.Soft),
                ((price > 0f ? price : good.price).ToString("0.0"), WideKit.Gold),
                (Percent(shortageOf), shortageOf >= 0.5f ? WideKit.Red : shortageOf > 0.05f ? WideKit.Orange : WideKit.Green),
                (imported > 0f ? Mathf.RoundToInt(imported).ToString() : "—", WideKit.Cyan),
                (exported > 0f ? Mathf.RoundToInt(exported).ToString() : "—", WideKit.Green));
        }
    }

    private void DrawMembers(AutoVertLayoutGroup page)
    {
        List<Kingdom> members = Members().ToList();
        page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Members)), string.Format(LM.Get("diplomacy_chip_count"), members.Count));
        int pages = Mathf.Max(1, (members.Count + MemberPageSize - 1) / MemberPageSize);
        _memberPage = Mathf.Clamp(_memberPage, 0, pages - 1);
        var card = page.BeginCard(PageWidth, "market_members_all", WideKit.Cyan, LM.Get("market_members_hint"));
        int index = 0;
        foreach (Kingdom member in members.Skip(_memberPage * MemberPageSize).Take(MemberPageSize)) MemberRow(card, member, index++);
        if (pages > 1)
            page.AddPager(PageWidth, _memberPage, pages, LM.Get("diplomacy_page_format"), delta =>
            {
                _memberPage = Mathf.Clamp(_memberPage + delta, 0, pages - 1);
                Refresh(resetScroll: true);
            });
    }

    private void DrawTrade(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get(PageKey(Page.Trade)), Title());
        page.AddKpiRow(PageWidth,
            (LM.Get("market_trade_imports"), Money(_market.boughtFrom.Values.Sum()), WideKit.Cyan),
            (LM.Get("market_trade_exports"), Money(_market.soldTo.Values.Sum()), WideKit.Green),
            (LM.Get("market_trade_links"), _market.linked.Count.ToString(), WideKit.Value));
        var partners = page.BeginCard(PageWidth, "market_partners_title", WideKit.Gold);
        DrawPartners(partners, 40);

        var columns = page.BeginColumns(PageWidth);
        GoodsColumn(columns, "market_imports_title", WideKit.Cyan, _market.imports);
        GoodsColumn(columns, "market_exports_title", WideKit.Green, _market.exports);

        var links = page.BeginCard(PageWidth, "market_links_title", WideKit.Muted);
        int index = 0;
        foreach (CommodityMarketSystem.Market other in CommodityMarketSystem.AllMarkets
                     .Where(other => Alive(other.leader) && _market.linked.Contains(other.leader.id))
                     .OrderByDescending(other => other.power))
        {
            Kingdom leader = other.leader;
            links.AddListRow(PageWidth, index++, null, MarketMap.Name(leader),
                string.Format(LM.Get("diplomacy_markets_line"), other.members.Count, Money(other.trade_value),
                    LM.Get(other.great ? "diplomacy_market_great" : "diplomacy_market_minor")), () => Switch(leader), WideKit.Value, 110f);
        }
        if (index == 0) links.AddParagraph(PageWidth, LM.Get("market_no_links"), 7, WideKit.Muted);
    }

    private void GoodsColumn(NeoModLoader.General.UI.Window.Layout.AutoHoriLayoutGroup columns, string title, string hex,
        Dictionary<string, float> amounts)
    {
        var card = columns.BeginColumnCard(HalfWidth, title, hex);
        int index = 0;
        foreach (CommodityConfig good in CommodityMarketSystem.AllGoods
                     .Where(good => amounts.TryGetValue(good.id, out float value) && value >= 0.5f)
                     .OrderByDescending(good => amounts[good.id] * good.price).Take(10))
            card.AddValueRow(HalfWidth, index++, DiplomacyLedger.GoodName(good), Mathf.RoundToInt(amounts[good.id]).ToString(), hex);
        if (index == 0) card.AddParagraph(HalfWidth, LM.Get("market_trade_none"), 7, WideKit.Muted);
    }

    // 与各市场的买卖：买进付的钱、卖出收的钱，按合计排
    private void DrawPartners(AutoVertLayoutGroup card, int take)
    {
        int index = 0;
        foreach (long id in _market.boughtFrom.Keys.Union(_market.soldTo.Keys)
                     .OrderByDescending(id => (_market.boughtFrom.TryGetValue(id, out float b) ? b : 0f) +
                                              (_market.soldTo.TryGetValue(id, out float s) ? s : 0f)).Take(take))
        {
            Kingdom leader = World.world.kingdoms.get(id);
            if (!Alive(leader)) continue;
            _market.boughtFrom.TryGetValue(id, out float bought);
            _market.soldTo.TryGetValue(id, out float sold);
            card.AddListRow(PageWidth, index++, null, MarketMap.Name(leader),
                string.Format(LM.Get("market_partner_line"), Money(bought), Money(sold)), () => Switch(leader), WideKit.Value, 110f);
        }
        if (index == 0) card.AddParagraph(PageWidth, LM.Get("market_trade_none"), 7, WideKit.Muted);
    }
}
