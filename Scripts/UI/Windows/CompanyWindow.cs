using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 公司窗口(用户 2026-11-25 "像素的建筑附上公司的名字可以点开查看经营业务""然后再写个财富榜""可股票和股市")：卷册宽窗口，左栏四页——
//   · 公司：全名、性质(国企、私企、财阀旗下、外资)、东家、总部、声誉、资本、盈利、身价；经营业务(旗下各片厂区与出的货，点厂区进厂区小窗口)；
//     上市的看股价走势；大事记；可以当场收归国有、发卖民营；
//   · 本国公司：这个国家的公司一览(行业、性质、规模、身价)，各行业眼下的加价(垄断哄抬或价格战)，外国资本控着几成；
//   · 财富榜：天下公司按身价排、富豪按东家的宗族排(见 StockMarketSystem)；
//   · 股市：各国交易所的股指与走势，本国上市公司的股价、涨跌、市值。
// 入口：厂区小窗口里的公司一行(见 FactoryWindow)、国情窗口的产业卡(见 InstitutionWindow)。
public class CompanyWindow : WideScrollWindow<CompanyWindow>
{
    private enum Page { Company, Realm, Rich, Stocks }

    private static int _pendingCompany = -1;
    private static long _pendingRealm = -1L;
    private static int _pendingPage = -1;

    private Page _page = Page.Realm;
    private int _companyId = -1;
    private long _realmId = -1L;
    private CompanyData _company;
    private Empire _realm;

    public static void Open(CompanyData company)
    {
        if (company == null) return;
        _pendingCompany = company.id;
        _pendingRealm = CompanySystem.RealmOf(company)?.id ?? -1L;
        _pendingPage = (int)Page.Company;
        ScrollWindow.showWindow(nameof(CompanyWindow));
    }

    // page：1 本国公司、2 财富榜、3 股市
    public static void OpenRealm(Empire empire, int page = 1)
    {
        _pendingCompany = -1;
        _pendingRealm = empire?.id ?? -1L;
        _pendingPage = Mathf.Clamp(page, 1, 3);
        ScrollWindow.showWindow(nameof(CompanyWindow));
    }

    protected override void OnOpen()
    {
        if (_pendingPage < 0) return;
        _page = (Page)_pendingPage;
        _companyId = _pendingCompany;
        _realmId = _pendingRealm;
        _pendingPage = -1;
    }

    protected override void Prepare()
    {
        _company = CompanySystem.ById(_companyId);
        Empire realm = _realmId < 0 ? null : ModClass.EMPIRE_MANAGER?.get(_realmId);
        _realm = realm?.data == null || realm.isRekt() || realm.IsArchived() ? null : realm;
        if (_page == Page.Company && _company == null) _page = Page.Realm;
    }

    private void Go(Page page)
    {
        _page = page;
        Refresh(resetScroll: true);
    }

    private void Show(CompanyData company)
    {
        _companyId = company.id;
        if (CompanySystem.RealmOf(company) is { } realm) _realmId = realm.id;
        Go(Page.Company);
    }

    private void ShowRealm(Empire realm)
    {
        _realmId = realm.id;
        Go(Page.Realm);
    }

    private static string Money(float value) => UIHelper.ShortCount((long)Mathf.Round(value));

    private static string TradeName(CompanyData company) => CompanyNaming.SectorName(company.sector);

    private static string KindHex(CompanyData company) =>
        company.state ? WideKit.Cyan : company.foreign_empire_id >= 0 ? WideKit.Orange : company.group_clan_id >= 0 ? WideKit.Gold : WideKit.Value;

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        rail.AddRailTitle(LM.Get("company_window"), _realm?.GetEmpireFullName() ?? LM.Get("company_window_world"));
        var pages = rail.BeginRailMenu();
        int index = 0;
        if (_company != null)
            pages.AddRailOption(null, _company.short_name, null, _page == Page.Company, () => Go(Page.Company), index++);
        pages.AddRailOption(null, LM.Get("company_page_realm"), _realm == null ? null
            : CompanySystem.All.Count(company => CompanySystem.RealmOf(company) == _realm).ToString(), _page == Page.Realm, () => Go(Page.Realm), index++);
        pages.AddRailOption(null, LM.Get("company_page_rich"), null, _page == Page.Rich, () => Go(Page.Rich), index++);
        pages.AddRailOption(null, LM.Get("company_page_stocks"), StockMarketSystem.Exchanges.Count.ToString(), _page == Page.Stocks,
            () => Go(Page.Stocks), index);
    }

    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        switch (_page)
        {
            case Page.Company:
                DrawCompany(page);
                break;
            case Page.Rich:
                DrawRich(page);
                break;
            case Page.Stocks:
                DrawStocks(page);
                break;
            default:
                DrawRealm(page);
                break;
        }
    }

    // ───────── 公司 ─────────
    private void DrawCompany(AutoVertLayoutGroup page)
    {
        CompanyData company = _company;
        Empire realm = CompanySystem.RealmOf(company);
        City home = CompanySystem.Home(company);
        page.AddPageHeader(PageWidth, company.name, CompanySystem.OwnershipName(company), TradeName(company));
        var overview = page.BeginCard(PageWidth, "company_card_overview", WideKit.Gold);
        overview.AddMetricRow(PageWidth,
            (LM.Get("company_reputation"), Mathf.RoundToInt(company.reputation).ToString(),
                company.reputation >= 60f ? WideKit.Green : company.reputation < 30f ? WideKit.Red : WideKit.Value),
            (LM.Get("company_capital"), company.state ? "—" : Money(company.capital), company.capital < 0f ? WideKit.Red : WideKit.Value),
            (LM.Get("company_profit"), Money(company.profit), company.profit < 0f ? WideKit.Red : WideKit.Green),
            (LM.Get("company_worth"), Money(CompanySystem.Worth(company)), WideKit.Gold));
        int row = 0;
        overview.AddListRow(PageWidth, row++, null, LM.Get("company_home"),
            home == null ? "" : $"{home.GetCityName()} · {realm?.GetEmpireFullName() ?? ""}", null, WideKit.Soft, 70f);
        overview.AddListRow(PageWidth, row++, null, LM.Get("company_founded"),
            string.Format(LM.Get("company_founded_years"), Date.getYearsSince(company.founded)), null, WideKit.Soft, 70f);
        if (!company.state && !string.IsNullOrEmpty(company.owner))
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_owner"),
                string.Format(LM.Get("company_owner_family"), company.owner), null, WideKit.Soft, 70f);
        if (company.group_clan_id >= 0 && realm != null &&
            ConglomerateSystem.Of(realm).FirstOrDefault(house => house.clan_id == company.group_clan_id) is { } house)
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_group"), house.name, null, WideKit.Gold, 70f);
        if (CompanySystem.ForeignOwner(company) is { } foreign)
        {
            CompanyData parent = CompanySystem.ById(company.foreign_company_id);
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_foreign"),
                foreign.GetEmpireFullName() + (parent == null ? "" : " · " + parent.name),
                parent == null ? null : () => Show(parent), WideKit.Orange, 70f);
        }
        float gouge = realm == null ? 0f : CompanySystem.Markup(realm.id, company.sector);
        if (gouge != 0f)
            overview.AddParagraph(PageWidth, string.Format(LM.Get(gouge > 0f ? "company_gouging_line" : "company_price_war_line"),
                TradeName(company), Mathf.RoundToInt(Mathf.Abs(gouge) * 100f)), 6, gouge > 0f ? WideKit.Orange : WideKit.Cyan);
        if (realm != null)
        {
            int id = company.id;
            overview.AddButtonRow(PageWidth,
                ("company_toggle_state", LM.Get(company.state ? "enterprise_to_private" : "enterprise_to_state"), () => ToggleState(id)),
                ("company_view_realm", LM.Get("company_page_realm"), () => ShowRealm(realm)));
        }

        // 经营业务：旗下各片厂区与出的货
        List<FactorySite> sites = CompanySystem.Sites(company);
        var business = page.BeginCard(PageWidth, "company_card_business", WideKit.Cyan,
            string.Format(LM.Get("company_business_header"), sites.Count, sites.Sum(site => site.units.Sum(FactorySystem.JobsOf)),
                Mathf.RoundToInt(company.value)));
        row = 0;
        foreach (FactorySite site in sites.OrderByDescending(site => site.units.Sum(FactorySystem.ValueOf)))
        {
            FactorySite target = site;
            City city = FactorySystem.CityOf(site);
            string units = string.Join("、", site.units.GroupBy(unit => unit.type)
                .Select(group => FactorySystem.TypeName(group.Key) + (group.Count() > 1 ? "×" + group.Count() : "")));
            business.AddListRow(PageWidth, row++, FactoryWindow.IconOf(site.units.FirstOrDefault()?.type ?? ""),
                city?.GetCityName() ?? "", units, () => FactoryWindow.Open(target), WideKit.Value, 70f);
        }
        var products = new Dictionary<string, float>();
        foreach (FactorySite site in sites)
            foreach (FactoryUnit unit in site.units)
                if (CommodityMarketSystem.Table.factories.TryGetValue(unit.type, out CommodityRecipe recipe))
                    foreach (KeyValuePair<string, float> output in recipe.outputs)
                        products[output.Key] = (products.TryGetValue(output.Key, out float sum) ? sum : 0f) + output.Value * Mathf.Max(1, unit.level);
        if (products.Count > 0)
            business.AddParagraph(PageWidth, LM.Get("company_products") + "：" + string.Join("、", products.OrderByDescending(pair => pair.Value)
                .Select(pair => (CommodityMarketSystem.TryGetGood(pair.Key, out CommodityConfig good) ? DiplomacyLedger.GoodName(good) : pair.Key) +
                                " " + pair.Value.ToString("0.#"))), 6, WideKit.Soft);

        if (company.listed)
        {
            IList<float> prices = company.prices ?? new List<float>();
            float change = StockMarketSystem.Change(prices);
            var stock = page.BeginCard(PageWidth, "company_card_stock", WideKit.Gold,
                string.Format(LM.Get("company_stock_header"), company.price.ToString("0.0"), StockMarketSystem.ChangeText(change),
                    Money(company.price * StockMarketSystem.Shares)));
            if (prices.Count >= 2) stock.AddColumnChart(PageWidth, prices, prices.Max() * 1.1f, change >= 0f ? "#65D66E" : "#E05A4F", 34f);
            else stock.AddParagraph(PageWidth, LM.Get("company_stock_new"), 6, WideKit.Muted);
        }

        if (company.notes != null && company.notes.Count > 0)
        {
            var notes = page.BeginCard(PageWidth, "company_card_notes", WideKit.Soft);
            foreach (CompanyNote note in company.notes.AsEnumerable().Reverse())
                notes.AddParagraph(PageWidth, string.Format(LM.Get("company_note_line"), Date.getYearsSince(note.at), note.text), 6, WideKit.Soft);
        }
    }

    // 收归国有、发卖民营：旗下各片厂区一起改(见 CompanySystem.Reassign)
    private void ToggleState(int companyId)
    {
        CompanyData company = CompanySystem.ById(companyId);
        if (company == null) return;
        bool toState = !company.state;
        FactorySite first = null;
        foreach (FactorySite site in CompanySystem.Sites(company))
        {
            site.state = toState;
            CompanySystem.Reassign(site);
            first ??= site;
        }
        if (CompanySystem.Of(first) is { } now) _companyId = now.id;
        Refresh();
    }

    // ───────── 本国公司 ─────────
    private void DrawRealm(AutoVertLayoutGroup page)
    {
        if (_realm == null)
        {
            page.AddPageHeader(PageWidth, LM.Get("company_page_realm"));
            page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
            return;
        }
        List<CompanyData> own = CompanySystem.All.Where(company => CompanySystem.RealmOf(company) == _realm)
            .OrderByDescending(CompanySystem.Worth).ToList();
        page.AddPageHeader(PageWidth, _realm.GetEmpireFullName(), string.Format(LM.Get("company_count"), own.Count));
        if (own.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
            return;
        }
        ConstitutionalEconomyState state = _realm.data?.constitutional_economy;
        if (state?.foreign_control != null && state.foreign_control.Count > 0)
        {
            var capital = page.BeginCard(PageWidth, "company_card_foreign", WideKit.Orange, LM.Get("company_foreign_hint"));
            int at = 0;
            foreach (KeyValuePair<long, float> pair in state.foreign_control.OrderByDescending(pair => pair.Value))
            {
                Empire foreign = ModClass.EMPIRE_MANAGER?.get(pair.Key);
                if (foreign?.data == null) continue;
                capital.AddBarRow(PageWidth, at++, foreign.GetEmpireFullName(), pair.Value,
                    LM.Get(pair.Value >= CompanySystem.ControlAt ? "company_foreign_master"
                        : pair.Value >= CompanySystem.InfluenceAt ? "company_foreign_beholden" : "company_foreign_minor"),
                    WideKit.Value, pair.Value >= CompanySystem.InfluenceAt ? WideKit.Red : WideKit.Orange, "#3A4038", null, 150f, 80f);
            }
        }
        var list = page.BeginCard(PageWidth, "company_card_list", WideKit.Gold);
        list.AddTableRow(PageWidth, -1, null, LM.Get("company_col_name"), 110f,
            (LM.Get("company_col_trade"), ""), (LM.Get("company_col_kind"), ""), (LM.Get("company_col_scale"), ""), (LM.Get("company_worth"), ""));
        int index = 0;
        foreach (CompanyData company in own.Take(40))
        {
            CompanyData target = company;
            float gouge = CompanySystem.Markup(_realm.id, company.sector);
            UIHelper.MakeClickable(list.AddTableRow(PageWidth, index++, null, company.short_name, 110f,
                (TradeName(company) + (gouge >= 0.05f ? "▲" : gouge < 0f ? "▼" : ""), gouge >= 0.05f ? WideKit.Orange : WideKit.Soft),
                (LM.Get(company.state ? "company_tag_state" : company.foreign_empire_id >= 0 ? "company_tag_foreign"
                    : company.group_clan_id >= 0 ? "company_tag_group" : "company_tag_private"), KindHex(company)),
                (company.scale.ToString(), WideKit.Value), (Money(CompanySystem.Worth(company)), WideKit.Gold)).gameObject, () => Show(target));
        }
        if (own.Count > 40) list.AddParagraph(PageWidth, string.Format(LM.Get("diplomacy_more"), own.Count - 40), 6, WideKit.Muted);
    }

    // ───────── 财富榜 ─────────
    private void DrawRich(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("company_page_rich"));
        List<CompanyData> top = StockMarketSystem.TopCompanies(12);
        if (top.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
            return;
        }
        var firms = page.BeginCard(PageWidth, "company_rich_firms", WideKit.Gold);
        firms.AddTableRow(PageWidth, -1, null, LM.Get("company_col_name"), 120f,
            (LM.Get("company_col_realm"), ""), (LM.Get("company_col_trade"), ""), (LM.Get("company_worth"), ""));
        int index = 0;
        foreach (CompanyData company in top)
        {
            CompanyData target = company;
            UIHelper.MakeClickable(firms.AddTableRow(PageWidth, index, null, $"{index + 1}. {company.short_name}", 120f,
                (CompanySystem.RealmOf(company)?.GetEmpireName() ?? "", WideKit.Soft), (TradeName(company), KindHex(company)),
                (Money(CompanySystem.Worth(company)), WideKit.Gold)).gameObject, () => Show(target));
            index++;
        }
        List<StockMarketSystem.Fortune> rich = StockMarketSystem.RichList(12);
        var people = page.BeginCard(PageWidth, "company_rich_families", WideKit.Gold, LM.Get("company_rich_families_hint"));
        people.AddTableRow(PageWidth, -1, null, LM.Get("company_col_family"), 120f,
            (LM.Get("company_col_realm"), ""), (LM.Get("company_col_firms"), ""), (LM.Get("company_col_wealth"), ""));
        index = 0;
        foreach (StockMarketSystem.Fortune fortune in rich)
        {
            Empire realm = fortune.realm;
            var row = people.AddTableRow(PageWidth, index, null,
                $"{index + 1}. " + string.Format(LM.Get("company_owner_family"), fortune.name) + (fortune.group ? " ★" : ""), 120f,
                (realm?.GetEmpireName() ?? "", WideKit.Soft), (fortune.companies.ToString(), WideKit.Value),
                (Money(fortune.wealth), WideKit.Gold));
            if (realm != null) UIHelper.MakeClickable(row.gameObject, () => ShowRealm(realm));
            index++;
        }
    }

    // ───────── 股市 ─────────
    private void DrawStocks(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("company_page_stocks"));
        if (StockMarketSystem.Exchanges.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "stock_none", LM.Get("stock_none_hint"));
            return;
        }
        StockExchangeData mine = StockMarketSystem.ExchangeOf(_realm);
        foreach (StockExchangeData exchange in StockMarketSystem.Exchanges.OrderByDescending(item => item == mine).ThenByDescending(item => item.index).Take(8))
        {
            Empire realm = ModClass.EMPIRE_MANAGER?.get(exchange.empire_id);
            if (realm?.data == null || realm.isRekt() || realm.IsArchived()) continue;
            IList<float> history = exchange.history ?? new List<float>();
            float change = StockMarketSystem.Change(history);
            var card = page.BeginCard(PageWidth, string.Format(LM.Get("stock_exchange_title"), realm.GetEmpireFullName()),
                exchange == mine ? WideKit.Gold : WideKit.Cyan,
                string.Format(LM.Get("stock_exchange_header"), Mathf.RoundToInt(exchange.index), StockMarketSystem.ChangeText(change),
                    Date.getYearsSince(exchange.since)));
            if (exchange != mine)
            {
                card.AddListRow(PageWidth, 0, null, LM.Get("stock_view_exchange"), "", () =>
                {
                    _realmId = realm.id;
                    Go(Page.Stocks);
                }, WideKit.Value, PageWidth - 20f);
                continue;
            }
            if (history.Count >= 2) card.AddColumnChart(PageWidth, history, history.Max() * 1.1f, change >= 0f ? "#65D66E" : "#E05A4F", 34f, new[] { 100f });
            card.AddTableRow(PageWidth, -1, null, LM.Get("company_col_name"), 120f,
                (LM.Get("stock_col_price"), ""), (LM.Get("stock_col_change"), ""), (LM.Get("stock_col_cap"), ""));
            int index = 0;
            foreach (CompanyData company in CompanySystem.All.Where(company => company.listed && CompanySystem.RealmOf(company) == realm)
                         .OrderByDescending(company => company.price).Take(30))
            {
                CompanyData target = company;
                float move = StockMarketSystem.Change(company.prices);
                UIHelper.MakeClickable(card.AddTableRow(PageWidth, index++, null, company.short_name, 120f,
                    (company.price.ToString("0.0"), WideKit.Value),
                    (StockMarketSystem.ChangeText(move), move >= 0f ? WideKit.Green : WideKit.Red),
                    (Money(company.price * StockMarketSystem.Shares), WideKit.Gold)).gameObject, () => Show(target));
            }
            if (index == 0) card.AddParagraph(PageWidth, LM.Get("stock_no_listed"), 6, WideKit.Muted);
        }
    }
}
