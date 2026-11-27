using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 公司窗口(用户 2026-11-25 "像素的建筑附上公司的名字可以点开查看经营业务""然后再写个财富榜""可股票和股市"；
// 2026-11-28 "现在公司能被清晰的被玩家看到吗""每个国家和城市有哪些公司""以及他们的份额""势力""资本主义的发展主要依赖于他们"，
// 看过参考稿 Tasks/ui-mockups/company-landscape.png 后"照这个做")：卷册宽窗口，左栏七页——
//   · 公司：全名、性质、东家、总部、声誉、资本、盈利、身价；行业与各城的份额；经营业务；股价；大事记；收归国有、入股、皇商特许；
//     特许公司另一套：治下各城、特许状(续、撤)、股东与分红、收归国有、董事会、股价；
//   · 产业格局：本国各行业的份额条(谁占几成)、集中程度(竞争、寡头、一家独大、官办为主、行会、皇商)、加价或价格战；
//   · 势力：财阀(占工业几成、旗下公司、献金、游说、在朝、大而不倒、拆分)、特许公司、皇商、外国资本、国家资本；
//   · 城市：各城有几家、头号是谁占几成；选中一城看各家的产值份额、厂区、用工；
//   · 公司名录、财富榜、股市(特许公司标「特许」)。
// 古代(商号的年代)界面里叫"商号"、"官办"(见 CompanyRules.forms)。
// 入口：国情窗口产业卡的「公司格局」、厂区小窗口里的公司一行、地图上点公司的厂区、城市窗口的「工商」一行(只看)。
public class CompanyWindow : WideScrollWindow<CompanyWindow>
{
    private enum Page { Company, Realm, Rich, Stocks, Industry, Influence, Cities }

    private static int _pendingCompany = -1;
    private static long _pendingRealm = -1L;
    private static long _pendingCity = -1L;
    private static int _pendingPage = -1;

    private Page _page = Page.Industry;
    private int _companyId = -1;
    private long _realmId = -1L;
    private long _cityId = -1L;
    private CompanyData _company;
    private Empire _realm;

    // 份额条的配色：头几家各一色，余下的灰
    private static readonly string[] Palette = { "#F3C34A", "#7FD8EA", "#65D66E", "#E8935A", "#B48CE0" };
    private const string Rest = "#5A6458";

    public static void Open(CompanyData company)
    {
        if (company == null) return;
        _pendingCompany = company.id;
        _pendingRealm = CompanySystem.RealmOf(company)?.id ?? -1L;
        _pendingCity = -1L;
        _pendingPage = (int)Page.Company;
        ScrollWindow.showWindow(nameof(CompanyWindow));
    }

    // page：1 公司名录、2 财富榜、3 股市、4 产业格局、5 势力、6 城市
    public static void OpenRealm(Empire empire, int page = 4)
    {
        _pendingCompany = -1;
        _pendingRealm = empire?.id ?? -1L;
        _pendingCity = -1L;
        _pendingPage = Mathf.Clamp(page, 1, 6);
        ScrollWindow.showWindow(nameof(CompanyWindow));
    }

    public static void OpenCity(City city)
    {
        if (city?.kingdom == null) return;
        _pendingCompany = -1;
        _pendingRealm = city.kingdom.GetEmpire()?.id ?? -1L;
        _pendingCity = city.id;
        _pendingPage = (int)Page.Cities;
        ScrollWindow.showWindow(nameof(CompanyWindow));
    }

    protected override void OnOpen()
    {
        if (_pendingPage < 0) return;
        _page = (Page)_pendingPage;
        _companyId = _pendingCompany;
        _realmId = _pendingRealm;
        _cityId = _pendingCity;
        _pendingPage = -1;
    }

    protected override void Prepare()
    {
        _company = CompanySystem.ById(_companyId);
        Empire realm = _realmId < 0 ? null : ModClass.EMPIRE_MANAGER?.get(_realmId);
        _realm = realm?.data == null || realm.isRekt() || realm.IsArchived() ? null : realm;
        if (_page == Page.Company && (_company == null || CompanySystem.RealmOf(_company) == null)) _page = Page.Industry;
    }

    private void Go(Page page)
    {
        _page = page;
        Refresh(resetScroll: true);
    }

    private void Show(CompanyData company)
    {
        if (company == null) return;
        _companyId = company.id;
        if (CompanySystem.RealmOf(company) is { } realm) _realmId = realm.id;
        Go(Page.Company);
    }

    private void ShowRealm(Empire realm, Page page = Page.Industry)
    {
        _realmId = realm.id;
        Go(page);
    }

    private static string Money(float value) => UIHelper.ShortCount((long)Mathf.Round(value));

    private static string Pct(float share) => Mathf.RoundToInt(share * 100f) + "%";

    private static string TradeName(CompanyData company) => CompanyNaming.SectorName(company.sector);

    // 古代(商号的年代)换个叫法：有"键_shop"的用它
    private bool ShopEra => _realm != null && CompanySystem.RealmForm(_realm).guild;

    private string Word(string key)
    {
        if (!ShopEra) return LM.Get(key);
        string shop = LM.Get(key + "_shop");
        return string.IsNullOrEmpty(shop) || shop == key + "_shop" ? LM.Get(key) : shop;
    }

    private static (string text, string hex) Tag(CompanyData company)
    {
        if (CompanySystem.IsCharter(company)) return (LM.Get("company_tag_charter"), WideKit.Gold);
        bool shop = CompanyRules.FormOf(company).guild;
        if (company.state) return (LM.Get(shop ? "company_tag_state_shop" : "company_tag_state"), WideKit.Cyan);
        if (company.foreign_empire_id >= 0) return (LM.Get("company_tag_foreign"), WideKit.Orange);
        if (company.group_clan_id >= 0) return (LM.Get("company_tag_group"), WideKit.Gold);
        if (CompanySystem.Licensed(company)) return (LM.Get("company_tag_licensed"), WideKit.Gold);
        return (LM.Get(shop ? "company_tag_shop" : "company_tag_private"), WideKit.Value);
    }

    private static List<CompanyData> Own(Empire realm) =>
        CompanySystem.All.Where(company => !CompanySystem.IsCharter(company) && CompanySystem.RealmOf(company) == realm).ToList();

    private static List<CompanyData> Charters(Empire realm) =>
        CompanySystem.All.Where(company => CompanySystem.IsCharter(company) && CompanySystem.RealmOf(company) == realm).ToList();

    // 谁说了算：财阀旗下的并作一家，外资按控股国并
    private static long Controller(CompanyData company) =>
        company.group_clan_id >= 0 ? company.group_clan_id + 1_000_000_000L
        : company.foreign_empire_id >= 0 ? -company.foreign_empire_id - 2L : -1_000_000_000L - company.id;

    private sealed class Trade
    {
        public string sector;
        public List<CompanyData> firms;
        public int total;
        public float value;
    }

    private static List<Trade> Trades(IEnumerable<CompanyData> own) => own.GroupBy(company => company.sector)
        .Select(group => new Trade
        {
            sector = group.Key, firms = group.OrderByDescending(company => company.scale).ToList(),
            total = group.Sum(company => company.scale), value = group.Sum(company => company.value)
        })
        .Where(trade => trade.total > 0).OrderByDescending(trade => trade.total).ToList();

    private void Toast(string error)
    {
        if (error != null) WorldTip.showNow(LM.Get(error), false, "top", 3f);
        Refresh();
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        rail.AddRailTitle(Word("company_window"), _realm?.GetEmpireFullName() ?? LM.Get("company_window_world"));
        var pages = rail.BeginRailMenu();
        int index = 0;
        if (_company != null)
            pages.AddRailOption(null, _company.short_name, null, _page == Page.Company, () => Go(Page.Company), index++);
        int own = _realm == null ? 0 : Own(_realm).Count;
        pages.AddRailOption(null, LM.Get("company_page_landscape"), null, _page == Page.Industry, () => Go(Page.Industry), index++);
        pages.AddRailOption(null, LM.Get("company_page_influence"), null, _page == Page.Influence, () => Go(Page.Influence), index++);
        pages.AddRailOption(null, LM.Get("company_page_cities"), null, _page == Page.Cities, () => Go(Page.Cities), index++);
        pages.AddRailOption(null, Word("company_page_realm"), _realm == null ? null : own.ToString(), _page == Page.Realm,
            () => Go(Page.Realm), index++);
        pages.AddRailOption(null, LM.Get("company_page_rich"), null, _page == Page.Rich, () => Go(Page.Rich), index++);
        pages.AddRailOption(null, LM.Get("company_page_stocks"), StockMarketSystem.Exchanges.Count.ToString(), _page == Page.Stocks,
            () => Go(Page.Stocks), index);
    }

    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        switch (_page)
        {
            case Page.Company:
                if (CompanySystem.IsCharter(_company)) DrawCharter(page);
                else DrawCompany(page);
                break;
            case Page.Realm:
                DrawRealm(page);
                break;
            case Page.Rich:
                DrawRich(page);
                break;
            case Page.Stocks:
                DrawStocks(page);
                break;
            case Page.Influence:
                DrawInfluence(page);
                break;
            case Page.Cities:
                DrawCities(page);
                break;
            default:
                DrawIndustry(page);
                break;
        }
    }

    private bool NoRealm(AutoVertLayoutGroup page, string titleKey)
    {
        if (_realm != null) return false;
        page.AddPageHeader(PageWidth, LM.Get(titleKey));
        page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
        return true;
    }

    // ───────── 产业格局 ─────────
    private void DrawIndustry(AutoVertLayoutGroup page)
    {
        if (NoRealm(page, "company_page_landscape")) return;
        List<CompanyData> own = Own(_realm);
        page.AddPageHeader(PageWidth, _realm.GetEmpireFullName(), LM.Get("company_page_landscape"),
            string.Format(LM.Get("company_form_chip"), CompanyRules.Label(CompanySystem.RealmForm(_realm))));
        if (own.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
            return;
        }
        int total = own.Sum(company => company.scale);
        float Share(Func<CompanyData, bool> pick) => total <= 0 ? 0f : own.Where(pick).Sum(company => company.scale) / (float)total;
        List<Trade> trades = Trades(own);
        if (ShopEra)
            page.AddKpiRow(PageWidth,
                (Word("company_kpi_count"), own.Count.ToString(), WideKit.Gold),
                (Word("company_kpi_state"), Pct(Share(company => company.state)), WideKit.Cyan),
                (LM.Get("company_kpi_guilds"), trades.Count(trade => CompanySystem.HasGuild(_realm, trade.sector)).ToString(), WideKit.Value),
                (LM.Get("company_kpi_licensed"), own.Count(CompanySystem.Licensed).ToString(), WideKit.Gold));
        else
            page.AddKpiRow(PageWidth,
                (Word("company_kpi_count"), own.Count.ToString(), WideKit.Gold),
                (LM.Get("company_kpi_listed"), own.Count(company => company.listed).ToString(), WideKit.Value),
                (Word("company_kpi_state"), Pct(Share(company => company.state)), WideKit.Cyan),
                (LM.Get("company_kpi_group"), Pct(Share(company => company.group_clan_id >= 0)), WideKit.Gold),
                (LM.Get("company_kpi_foreign"), Pct(Share(company => company.foreign_empire_id >= 0)), WideKit.Orange));
        foreach (Trade trade in trades.Take(12))
        {
            var card = page.BeginCard(PageWidth, CompanyNaming.SectorName(trade.sector), WideKit.Gold,
                string.Format(Word("company_trade_header"), trade.firms.Count, Money(trade.value)));
            var parts = new List<(float share, string hex)>();
            for (int i = 0; i < trade.firms.Count; i++)
                parts.Add((trade.firms[i].scale / (float)trade.total, i < Palette.Length ? Palette[i] : Rest));
            card.AddStackBar(PageWidth, parts);
            card.AddParagraph(PageWidth, TradeTags(trade), 6, WideKit.Soft);
            int row = 0;
            foreach (CompanyData company in trade.firms.Take(Palette.Length))
            {
                CompanyData target = company;
                (string text, string hex) tag = Tag(company);
                UIHelper.MakeClickable(card.AddTableRow(PageWidth, row, null, "■".ColorString(Palette[row]) + " " + company.short_name, 120f,
                    (tag.text, tag.hex), (Pct(company.scale / (float)trade.total), WideKit.Value),
                    (Money(CompanySystem.Worth(company)), WideKit.Gold)).gameObject, () => Show(target));
                row++;
            }
            if (trade.firms.Count > Palette.Length)
                card.AddParagraph(PageWidth, string.Format(LM.Get("diplomacy_more"), trade.firms.Count - Palette.Length), 6, WideKit.Muted);
        }
        if (trades.Count > 12) page.AddParagraph(PageWidth, string.Format(LM.Get("diplomacy_more"), trades.Count - 12), 6, WideKit.Muted);
    }

    // 一个行业的集中程度与行情
    private string TradeTags(Trade trade)
    {
        var tags = new List<string>();
        float stateShare = trade.firms.Where(company => company.state).Sum(company => company.scale) / (float)trade.total;
        IGrouping<long, CompanyData> lead = trade.firms.Where(company => !company.state).GroupBy(Controller)
            .OrderByDescending(group => group.Sum(company => company.scale)).FirstOrDefault();
        float top = lead == null ? 0f : lead.Sum(company => company.scale) / (float)trade.total;
        bool guild = CompanySystem.HasGuild(_realm, trade.sector);
        if (stateShare >= 0.5f) tags.Add(Word("company_trade_state_led").ColorString(WideKit.Cyan));
        else if (top >= CompanyRules.Numbers.gouge_from) tags.Add(LM.Get("company_trade_monopoly").ColorString(WideKit.Orange));
        else if (trade.firms.Count >= 2 && (trade.firms[0].scale + trade.firms[1].scale) / (float)trade.total >= 0.6f)
            tags.Add(LM.Get("company_trade_oligopoly").ColorString(WideKit.Gold));
        else tags.Add(LM.Get("company_trade_competitive").ColorString(WideKit.Green));
        if (guild) tags.Add(string.Format(LM.Get("company_trade_guild"), Pct(CompanyRules.Guild.guild_markup)));
        if (trade.firms.FirstOrDefault(CompanySystem.Licensed) is { } licensee)
            tags.Add(string.Format(LM.Get("company_trade_licensed"), licensee.short_name).ColorString(WideKit.Gold));
        float gouge = CompanySystem.Markup(_realm.id, trade.sector);
        if (!guild && gouge >= 0.05f) tags.Add(string.Format(LM.Get("company_trade_gouging"), Pct(gouge)).ColorString(WideKit.Orange));
        else if (gouge < 0f) tags.Add(string.Format(LM.Get("company_trade_price_war"), Pct(-gouge)).ColorString(WideKit.Cyan));
        if (CompanySystem.Strategic.Contains(trade.sector)) tags.Add(LM.Get("company_trade_strategic"));
        return string.Join("  ·  ", tags);
    }

    // ───────── 势力 ─────────
    private void DrawInfluence(AutoVertLayoutGroup page)
    {
        if (NoRealm(page, "company_page_influence")) return;
        page.AddPageHeader(PageWidth, _realm.GetEmpireFullName(), LM.Get("company_page_influence"));
        bool any = false;
        List<CompanyData> own = Own(_realm);
        int total = own.Sum(company => company.scale);

        // 财阀
        foreach (ConglomerateData house in ConglomerateSystem.Of(_realm).OrderByDescending(item => item.share))
        {
            any = true;
            List<CompanyData> held = CompanySystem.GroupCompanies(_realm, house.clan_id);
            var card = page.BeginCard(PageWidth, house.name, WideKit.Gold, string.Format(LM.Get("company_house_header"), Pct(house.share)));
            float lobby = Mathf.Min(0.6f, ConglomerateSystem.Monopoly(_realm) * (ConglomerateSystem.InOffice(_realm) ? 1.5f : 1f) * 1.2f);
            card.AddMetricRow(PageWidth,
                (LM.Get("company_house_firms"), held.Count.ToString(), WideKit.Value),
                (LM.Get("company_worth"), Money(held.Sum(CompanySystem.Worth)), WideKit.Gold),
                (LM.Get("company_house_donation"), Money(house.share * CompanyRules.Houses.donation_scale), WideKit.Value),
                (LM.Get("company_house_lobby"), "−" + Pct(lobby), WideKit.Orange));
            int row = 0;
            string party = PartySystem.GetParties(_realm)?.FirstOrDefault(item => item != null && item.GetID() == house.party_id)?.Name;
            if (!string.IsNullOrEmpty(party))
                card.AddListRow(PageWidth, row++, null, LM.Get("company_house_party"), party, null, WideKit.Soft, 80f);
            if (ParliamentSystem.GetHeadOfGovernment(_realm)?.GetSpecificClan()?.id == house.clan_id)
                card.AddListRow(PageWidth, row++, null, LM.Get("company_house_office"), LM.Get("company_house_office_yes"), null, WideKit.Orange, 80f);
            if (house.share >= ConglomerateSystem.BailoutShare)
                card.AddListRow(PageWidth, row++, null, LM.Get("company_house_too_big"), LM.Get("company_house_too_big_hint"), null, WideKit.Red, 80f);
            if (held.Count > 0)
                card.AddParagraph(PageWidth, LM.Get("company_house_holdings") + "：" +
                                             string.Join("、", held.OrderByDescending(company => company.scale).Take(6).Select(company => company.short_name)) +
                                             (held.Count > 6 ? " " + string.Format(LM.Get("diplomacy_more"), held.Count - 6) : ""), 6, WideKit.Soft);
            if (ConglomerateSystem.CanBreak(_realm) && ConglomerateSystem.Largest(_realm) == house)
                card.AddButtonRow(PageWidth, ("company_house_break", LM.Get("company_house_break"), () =>
                {
                    ConglomerateSystem.Break(_realm);
                    Refresh();
                }));
        }

        // 特许公司
        foreach (CompanyData charter in Charters(_realm))
        {
            any = true;
            DrawCharterCard(page, charter);
        }

        // 皇商(商号的年代)
        List<CompanyData> licensed = own.Where(CompanySystem.Licensed).ToList();
        if (licensed.Count > 0)
        {
            any = true;
            var card = page.BeginCard(PageWidth, "company_card_licensed", WideKit.Gold, LM.Get("company_licensed_hint"));
            int row = 0;
            foreach (CompanyData company in licensed.OrderByDescending(company => company.scale))
            {
                CompanyData target = company;
                int trade = own.Where(other => other.sector == company.sector).Sum(other => other.scale);
                float left = Mathf.Max(0f, CompanyRules.Guild.license_years - Date.getYearsSince(company.licensed_at));
                UIHelper.MakeClickable(card.AddTableRow(PageWidth, row++, null, company.short_name, 120f,
                    (TradeName(company), WideKit.Soft), (Pct(trade <= 0 ? 0f : company.scale / (float)trade), WideKit.Value),
                    (string.Format(LM.Get("company_years_left"), Mathf.CeilToInt(left)), WideKit.Gold)).gameObject, () => Show(target));
            }
        }

        // 外国资本
        ConstitutionalEconomyState state = _realm.data?.constitutional_economy;
        if (state?.foreign_control != null && state.foreign_control.Count > 0)
        {
            any = true;
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
            capital.AddParagraph(PageWidth, string.Format(LM.Get("company_foreign_marks"), Pct(CompanySystem.InfluenceAt), Pct(CompanySystem.ControlAt)),
                6, WideKit.Muted);
        }

        // 国家资本：国企、官办，国家入了股的
        List<CompanyData> stateFirms = own.Where(company => company.state).OrderByDescending(company => company.scale).ToList();
        List<CompanyData> stakes = own.Where(company => !company.state && company.state_share > 0f).OrderByDescending(company => company.state_share).ToList();
        if (stateFirms.Count > 0 || stakes.Count > 0)
        {
            any = true;
            float stateShare = total <= 0 ? 0f : stateFirms.Sum(company => company.scale) / (float)total;
            var card = page.BeginCard(PageWidth, Word("company_card_state_capital"), WideKit.Cyan,
                string.Format(LM.Get("company_state_capital_header"), Pct(stateShare)));
            int row = 0;
            foreach (CompanyData company in stateFirms.Take(6))
            {
                CompanyData target = company;
                UIHelper.MakeClickable(card.AddTableRow(PageWidth, row++, null, company.short_name, 120f,
                    (TradeName(company), WideKit.Soft), (Tag(company).text, WideKit.Cyan), (Money(CompanySystem.Worth(company)), WideKit.Gold)).gameObject,
                    () => Show(target));
            }
            foreach (CompanyData company in stakes.Take(6))
            {
                CompanyData target = company;
                UIHelper.MakeClickable(card.AddTableRow(PageWidth, row++, null, company.short_name, 120f,
                    (TradeName(company), WideKit.Soft), (string.Format(LM.Get("company_stake_line"), Pct(company.state_share)), WideKit.Cyan),
                    (Money(CompanySystem.Worth(company)), WideKit.Gold)).gameObject, () => Show(target));
            }
        }
        if (!any) page.AddEmptyCard(PageWidth, "company_influence_none", Word("company_influence_none_hint"));
    }

    private void DrawCharterCard(AutoVertLayoutGroup page, CompanyData company)
    {
        Kingdom colony = CompanySystem.CharterColony(company);
        if (colony == null) return;
        string header = LM.Get("company_tag_charter") +
                        (company.listed ? "  ·  " + string.Format(LM.Get("company_charter_price"), company.price.ToString("0.0")) : "");
        var card = page.BeginCard(PageWidth, company.name, WideKit.Gold, header);
        card.AddMetricRow(PageWidth,
            (LM.Get("company_charter_cities"), colony.cities.Count.ToString(), WideKit.Value),
            (LM.Get("company_charter_troops"), UIHelper.ShortCount(CompanySystem.CharterTroops(company)), WideKit.Value),
            (LM.Get("company_charter_dividend"), Money(company.profit), WideKit.Green),
            (LM.Get("company_charter_years"), Mathf.CeilToInt(CompanySystem.CharterYearsLeft(colony)).ToString(), WideKit.Gold));
        int row = 0;
        if (colony.king != null && !colony.king.isRekt())
            card.AddListRow(PageWidth, row++, null, LM.Get("company_charter_governor"), colony.king.getName(), null, WideKit.Soft, 80f);
        card.AddListRow(PageWidth, row++, null, LM.Get("company_charter_treasury_cut"), Pct(company.state_share), null, WideKit.Cyan, 80f);
        card.AddBarRow(PageWidth, row, LM.Get("company_charter_nationalize"), colony.cities.Count / (float)Mathf.Max(1, CompanySystem.NationalizeCities),
            $"{colony.cities.Count}/{CompanySystem.NationalizeCities}", WideKit.Value, WideKit.Orange, "#3A4038", null, 80f, 120f);
        int id = company.id;
        card.AddButtonRow(PageWidth,
            ("company_charter_renew", LM.Get("company_charter_renew"), () => Toast(CompanySystem.RenewCharter(CompanySystem.ById(id)))),
            ("company_charter_revoke", LM.Get("company_charter_revoke"), () => Toast(CompanySystem.RevokeCharter(CompanySystem.ById(id)))),
            ("company_charter_open", LM.Get("company_charter_open"), () => Show(CompanySystem.ById(id))));
    }

    // ───────── 城市 ─────────
    private sealed class CityBook
    {
        public City city;
        public float total;
        public readonly Dictionary<CompanyData, (float value, int sites, int jobs)> firms = new();
    }

    private static CityBook BookOf(City city)
    {
        var book = new CityBook { city = city };
        foreach (FactorySite site in FactorySystem.InCity(city))
        {
            CompanyData company = CompanySystem.Of(site);
            float value = site.units.Sum(FactorySystem.ValueOf);
            book.total += value;
            if (company == null) continue;
            book.firms.TryGetValue(company, out var sum);
            book.firms[company] = (sum.value + value, sum.sites + 1, sum.jobs + site.units.Sum(FactorySystem.JobsOf));
        }
        return book;
    }

    private void DrawCities(AutoVertLayoutGroup page)
    {
        if (NoRealm(page, "company_page_cities")) return;
        page.AddPageHeader(PageWidth, _realm.GetEmpireFullName(), LM.Get("company_page_cities"));
        List<CityBook> books = ClaimPolicySystem.Cities(_realm).Where(city => city?.data != null && !city.isRekt()).Select(BookOf)
            .Where(book => book.firms.Count > 0).OrderByDescending(book => book.total).ToList();
        if (books.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
            return;
        }
        CityBook chosen = books.FirstOrDefault(book => book.city.id == _cityId) ?? books[0];
        _cityId = chosen.city.id;

        // 选中的城
        var detail = page.BeginCard(PageWidth, chosen.city.GetCityName(), WideKit.Gold,
            string.Format(Word("company_city_header"), chosen.firms.Count, Money(chosen.total)));
        List<KeyValuePair<CompanyData, (float value, int sites, int jobs)>> ranked = chosen.firms.OrderByDescending(pair => pair.Value.value).ToList();
        var parts = new List<(float share, string hex)>();
        for (int i = 0; i < ranked.Count; i++)
            parts.Add((chosen.total <= 0f ? 0f : ranked[i].Value.value / chosen.total, i < Palette.Length ? Palette[i] : Rest));
        detail.AddStackBar(PageWidth, parts);
        detail.AddTableRow(PageWidth, -1, null, LM.Get("company_col_name"), 110f,
            (LM.Get("company_col_kind"), ""), (LM.Get("company_col_city_share"), ""), (LM.Get("company_col_sites"), ""), (LM.Get("company_col_jobs"), ""));
        int row = 0;
        foreach (KeyValuePair<CompanyData, (float value, int sites, int jobs)> pair in ranked.Take(12))
        {
            CompanyData target = pair.Key;
            (string text, string hex) tag = Tag(pair.Key);
            string mark = row < Palette.Length ? "■".ColorString(Palette[row]) + " " : "";
            UIHelper.MakeClickable(detail.AddTableRow(PageWidth, row, null, mark + pair.Key.short_name, 110f,
                (tag.text, tag.hex), (Pct(chosen.total <= 0f ? 0f : pair.Value.value / chosen.total), WideKit.Value),
                (pair.Value.sites.ToString(), WideKit.Soft), (pair.Value.jobs.ToString(), WideKit.Soft)).gameObject, () => Show(target));
            row++;
        }
        if (ranked.Count > 12) detail.AddParagraph(PageWidth, string.Format(LM.Get("diplomacy_more"), ranked.Count - 12), 6, WideKit.Muted);

        // 各城一览
        var list = page.BeginCard(PageWidth, "company_card_city_list", WideKit.Cyan, LM.Get("company_city_list_hint"));
        list.AddTableRow(PageWidth, -1, null, LM.Get("company_col_city"), 90f,
            (Word("company_col_count"), ""), (LM.Get("company_col_top"), ""), (LM.Get("company_col_top_share"), ""));
        row = 0;
        foreach (CityBook book in books.Take(40))
        {
            KeyValuePair<CompanyData, (float value, int sites, int jobs)> top = book.firms.OrderByDescending(pair => pair.Value.value).First();
            long cityId = book.city.id;
            UIHelper.MakeClickable(list.AddTableRow(PageWidth, row++, null, (book == chosen ? "▶ " : "") + book.city.GetCityName(), 90f,
                (book.firms.Count.ToString(), WideKit.Value), (top.Key.short_name, Tag(top.Key).hex),
                (Pct(book.total <= 0f ? 0f : top.Value.value / book.total), WideKit.Gold)).gameObject, () =>
            {
                _cityId = cityId;
                Refresh(resetScroll: true);
            });
        }
        if (books.Count > 40) list.AddParagraph(PageWidth, string.Format(LM.Get("diplomacy_more"), books.Count - 40), 6, WideKit.Muted);
    }

    // ───────── 公司 ─────────
    private void DrawCompany(AutoVertLayoutGroup page)
    {
        CompanyData company = _company;
        Empire realm = CompanySystem.RealmOf(company);
        City home = CompanySystem.Home(company);
        CompanyForm form = CompanyRules.FormOf(company);
        page.AddPageHeader(PageWidth, company.name, CompanySystem.OwnershipName(company), TradeName(company), CompanyRules.Label(form));
        var overview = page.BeginCard(PageWidth, "company_card_overview", WideKit.Gold);
        // 名号可改(地图上标的简称跟着换；改过的不随改组换名)
        int renameId = company.id;
        TextInput nameInput = Instantiate(TextInput.Prefab, null);
        nameInput.Setup(company.name, text =>
        {
            CompanySystem.Rename(CompanySystem.ById(renameId), text);
            Refresh();
        });
        nameInput.SetSize(new Vector2(PageWidth - 16f, 18f));
        Text inputText = nameInput.input.textComponent;
        if (inputText != null)
        {
            inputText.resizeTextMinSize = 6;
            inputText.resizeTextMaxSize = 10;
        }
        nameInput.input.characterLimit = 16;
        overview.AddChild(nameInput.gameObject);
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
            ConglomerateSystem.Of(realm).FirstOrDefault(item => item.clan_id == company.group_clan_id) is { } house)
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_group"), house.name, () => ShowRealm(realm, Page.Influence), WideKit.Gold, 70f);
        if (company.state_share > 0f)
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_stake"),
                Mathf.RoundToInt(company.state_share * 100f) + "%", null, WideKit.Cyan, 70f);
        if (CompanySystem.Licensed(company))
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_tag_licensed"),
                string.Format(LM.Get("company_years_left"),
                    Mathf.CeilToInt(Mathf.Max(0f, CompanyRules.Guild.license_years - Date.getYearsSince(company.licensed_at)))), null, WideKit.Gold, 70f);
        if (CompanySystem.ForeignOwner(company) is { } foreign)
        {
            CompanyData parent = CompanySystem.ById(company.foreign_company_id);
            overview.AddListRow(PageWidth, row++, null, LM.Get("company_foreign"),
                foreign.GetEmpireFullName() + (parent == null ? "" : " · " + parent.name),
                parent == null ? null : () => Show(parent), WideKit.Orange, 70f);
        }
        float gouge = realm == null ? 0f : CompanySystem.Markup(realm.id, company.sector);
        if (gouge != 0f)
            overview.AddParagraph(PageWidth, string.Format(LM.Get(form.guild ? "company_guild_line" : gouge > 0f ? "company_gouging_line" : "company_price_war_line"),
                TradeName(company), Mathf.RoundToInt(Mathf.Abs(gouge) * 100f)), 6, gouge > 0f ? WideKit.Orange : WideKit.Cyan);
        if (realm != null)
        {
            int id = company.id;
            overview.AddButtonRow(PageWidth,
                ("company_toggle_state", LM.Get(company.state ? "enterprise_to_private" : "enterprise_to_state"), () => ToggleState(id)),
                ("company_view_landscape", LM.Get("company_page_landscape"), () => ShowRealm(realm)));
            // 国库入股、减持(一回一成，见 CompanySystem.BuyStake)；商号的年代不入股
            if (!company.state && company.foreign_empire_id < 0 && form.stake)
                overview.AddButtonRow(PageWidth,
                    ("company_stake_buy", LM.Get("company_stake_buy"), () => Toast(CompanySystem.BuyStake(CompanySystem.ById(id)))),
                    ("company_stake_sell", LM.Get("company_stake_sell"), () => Toast(CompanySystem.SellStake(CompanySystem.ById(id)))));
            // 皇商特许(商号的年代)
            if (!company.state && form.license)
                overview.AddButtonRow(PageWidth, CompanySystem.Licensed(company)
                    ? ("company_license_revoke", LM.Get("company_license_revoke"), () => Toast(CompanySystem.RevokeLicense(CompanySystem.ById(id))))
                    : ("company_license_grant", LM.Get("company_license_grant"), () => Toast(CompanySystem.GrantLicense(CompanySystem.ById(id)))));
        }

        List<FactorySite> sites = CompanySystem.Sites(company);
        DrawShares(page, company, realm, sites);

        // 经营业务：旗下各片厂区与出的货
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

        DrawStock(page, company);
        DrawNotes(page, company);
    }

    // 份额：在本国这一行里占几成、排第几；在各城的厂区产值里占几成
    private void DrawShares(AutoVertLayoutGroup page, CompanyData company, Empire realm, List<FactorySite> sites)
    {
        if (realm == null) return;
        List<CompanyData> peers = Own(realm).Where(other => other.sector == company.sector).OrderByDescending(other => other.scale).ToList();
        int total = peers.Sum(other => other.scale);
        if (total <= 0) return;
        var card = page.BeginCard(PageWidth, "company_card_shares", WideKit.Gold);
        int rank = peers.IndexOf(company) + 1, row = 0;
        card.AddBarRow(PageWidth, row++, string.Format(LM.Get("company_share_trade"), TradeName(company)), company.scale / (float)total,
            $"{Pct(company.scale / (float)total)} · " + string.Format(LM.Get("company_share_rank"), rank, peers.Count),
            WideKit.Gold, WideKit.Gold, "#3A4038", () => ShowRealm(realm), 80f, 110f);
        foreach (City city in sites.Select(FactorySystem.CityOf).Where(city => city != null).Distinct().Take(6))
        {
            CityBook book = BookOf(city);
            float mine = book.firms.TryGetValue(company, out var held) ? held.value : 0f;
            long cityId = city.id;
            card.AddBarRow(PageWidth, row++, city.GetCityName(), book.total <= 0f ? 0f : mine / book.total,
                Pct(book.total <= 0f ? 0f : mine / book.total), WideKit.Value, WideKit.Cyan, "#3A4038", () =>
                {
                    _cityId = cityId;
                    Go(Page.Cities);
                }, 80f, 110f);
        }
    }

    private void DrawStock(AutoVertLayoutGroup page, CompanyData company)
    {
        if (!company.listed) return;
        IList<float> prices = company.prices ?? new List<float>();
        float change = StockMarketSystem.Change(prices);
        var stock = page.BeginCard(PageWidth, "company_card_stock", WideKit.Gold,
            string.Format(LM.Get("company_stock_header"), company.price.ToString("0.0"), StockMarketSystem.ChangeText(change),
                Money(company.price * StockMarketSystem.Shares)));
        if (prices.Count >= 2) stock.AddColumnChart(PageWidth, prices, prices.Max() * 1.1f, change >= 0f ? "#65D66E" : "#E05A4F", 34f);
        else stock.AddParagraph(PageWidth, LM.Get("company_stock_new"), 6, WideKit.Muted);
    }

    private static void DrawNotes(AutoVertLayoutGroup page, CompanyData company)
    {
        if (company.notes == null || company.notes.Count == 0) return;
        var notes = page.BeginCard(PageWidth, "company_card_notes", WideKit.Soft);
        foreach (CompanyNote note in company.notes.AsEnumerable().Reverse())
            notes.AddParagraph(PageWidth, string.Format(LM.Get("company_note_line"), Date.getYearsSince(note.at), note.text), 6, WideKit.Soft);
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

    // ───────── 特许公司 ─────────
    private void DrawCharter(AutoVertLayoutGroup page)
    {
        CompanyData company = _company;
        Kingdom colony = CompanySystem.CharterColony(company);
        Kingdom metropole = ColonySystem.MetropoleOf(colony);
        if (colony == null || metropole == null)
        {
            page.AddPageHeader(PageWidth, company.name);
            page.AddEmptyCard(PageWidth, "company_charter_gone", LM.Get("company_charter_gone_hint"));
            return;
        }
        int id = company.id;
        page.AddPageHeader(PageWidth, company.name, LM.Get("company_kind_charter"),
            string.Format(LM.Get("company_charter_metropole"), metropole.GetKingdomFullName()));
        page.AddKpiRow(PageWidth,
            (LM.Get(company.listed ? "stock_col_cap" : "company_worth"), Money(CompanySystem.Worth(company)), WideKit.Gold),
            (LM.Get("company_charter_dividend"), Money(company.profit), WideKit.Green),
            (LM.Get("company_charter_cities"), colony.cities.Count.ToString(), WideKit.Value),
            (LM.Get("company_charter_troops"), UIHelper.ShortCount(CompanySystem.CharterTroops(company)), WideKit.Value));

        // 治下(代替厂区)
        var lands = page.BeginCard(PageWidth, "company_card_charter_lands", WideKit.Cyan,
            string.Format(LM.Get("company_charter_lands_header"), colony.cities.Count, colony.capital?.GetCityName() ?? ""));
        lands.AddTableRow(PageWidth, -1, null, LM.Get("company_col_city"), 110f,
            (LM.Get("company_col_people"), ""), (LM.Get("company_col_role"), ""));
        int row = 0;
        foreach (City city in colony.cities.Where(city => city?.data != null && !city.isRekt())
                     .OrderByDescending(city => city == colony.capital).ThenByDescending(city => city.getPopulationPeople()).Take(12))
            lands.AddTableRow(PageWidth, row++, null, city.GetCityName(), 110f,
                (UIHelper.ShortCount(city.getPopulationPeople()), WideKit.Value),
                (LM.Get(city == colony.capital ? "company_charter_seat" : "company_charter_post"), city == colony.capital ? WideKit.Gold : WideKit.Soft));

        // 特许状
        float left = CompanySystem.CharterYearsLeft(colony);
        ColonyState colonyState = ColonySystem.Of(colony);
        var charter = page.BeginCard(PageWidth, "company_card_charter", WideKit.Gold,
            string.Format(LM.Get("company_years_left"), Mathf.CeilToInt(left)));
        charter.AddParagraph(PageWidth, string.Format(LM.Get("company_charter_text"), metropole.GetKingdomFullName(), colony.GetKingdomFullName(),
            CompanyRules.Charter.years), 6, WideKit.Soft);
        charter.AddListRow(PageWidth, 0, null, LM.Get("company_charter_renewals"), (colonyState?.charter_renewals ?? 0).ToString(), null, WideKit.Soft, 80f);
        charter.AddListRow(PageWidth, 1, null, LM.Get("company_charter_fee"), Money(CompanySystem.RenewFee(colony)), null, WideKit.Soft, 80f);
        charter.AddButtonRow(PageWidth,
            ("company_charter_renew", string.Format(LM.Get("company_charter_renew_years"), CompanyRules.Charter.years),
                () => Toast(CompanySystem.RenewCharter(CompanySystem.ById(id)))),
            ("company_charter_revoke", LM.Get("company_charter_revoke_full"), () => Toast(CompanySystem.RevokeCharter(CompanySystem.ById(id)))));

        // 股东与分红
        var holders = page.BeginCard(PageWidth, "company_card_charter_holders", WideKit.Gold,
            string.Format(LM.Get("company_charter_dividend_header"), Money(company.profit)));
        List<CompanyHolder> holderList = company.holders ?? new List<CompanyHolder>();
        var parts = new List<(float share, string hex)> { (company.state_share, "#7FD8EA") };
        for (int i = 0; i < holderList.Count; i++) parts.Add((holderList[i].share, i == 0 ? "#F3C34A" : "#65D66E"));
        float retail = CompanySystem.RetailShare(company);
        parts.Add((retail, "#8FA0A8"));
        holders.AddStackBar(PageWidth, parts);
        row = 0;
        holders.AddBarRow(PageWidth, row++, LM.Get("company_holder_treasury"), company.state_share,
            $"{Pct(company.state_share)} · {Money(company.profit * company.state_share)}", WideKit.Cyan, "#7FD8EA", "#3A4038", null, 80f, 110f);
        for (int i = 0; i < holderList.Count; i++)
            holders.AddBarRow(PageWidth, row++, string.Format(LM.Get("company_owner_family"), holderList[i].owner), holderList[i].share,
                $"{Pct(holderList[i].share)} · {Money(company.profit * holderList[i].share)}", WideKit.Value, i == 0 ? "#F3C34A" : "#65D66E", "#3A4038",
                null, 80f, 110f);
        holders.AddBarRow(PageWidth, row, LM.Get("company_holder_retail"), retail,
            $"{Pct(retail)} · {Money(company.profit * retail)}", WideKit.Soft, "#8FA0A8", "#3A4038", null, 80f, 110f);
        holders.AddParagraph(PageWidth, LM.Get("company_charter_dividend_hint"), 6, WideKit.Muted);

        // 收归国有
        var nationalize = page.BeginCard(PageWidth, "company_card_charter_nationalize", WideKit.Orange, LM.Get("company_charter_nationalize_hint"));
        nationalize.AddBarRow(PageWidth, 0, LM.Get("company_charter_nationalize"), colony.cities.Count / (float)Mathf.Max(1, CompanySystem.NationalizeCities),
            $"{colony.cities.Count}/{CompanySystem.NationalizeCities}", WideKit.Value, WideKit.Orange, "#3A4038", null, 80f, 120f);
        long balance = colony.GetTreasuryBalance();
        nationalize.AddListRow(PageWidth, 1, null, LM.Get("company_charter_debt"),
            LM.Get(balance < 0 ? "company_charter_debt_yes" : "company_charter_debt_no"), null, balance < 0 ? WideKit.Red : WideKit.Green, 80f);
        nationalize.AddButtonRow(PageWidth, ("company_charter_buyout", string.Format(LM.Get("company_charter_buyout"), Money(CompanySystem.BuyOutCost(company))),
            () => Toast(CompanySystem.BuyOutCharter(CompanySystem.ById(id)))));

        // 董事会：总督与大股东
        var board = page.BeginCard(PageWidth, "company_card_charter_board", WideKit.Soft);
        row = 0;
        if (colony.king != null && !colony.king.isRekt())
            board.AddListRow(PageWidth, row++, null, LM.Get("company_charter_governor"), colony.king.getName(), null, WideKit.Gold, 80f);
        foreach (CompanyHolder holder in holderList)
            board.AddListRow(PageWidth, row++, null, LM.Get("company_charter_director"),
                string.Format(LM.Get("company_owner_family"), holder.owner) + " · " + Pct(holder.share), null, WideKit.Soft, 80f);
        board.AddListRow(PageWidth, row, null, LM.Get("company_charter_autonomy"),
            Mathf.RoundToInt(colonyState?.autonomy ?? 0f) + "/100", null, (colonyState?.autonomy ?? 0f) >= 70f ? WideKit.Red : WideKit.Soft, 80f);

        DrawStock(page, company);
        DrawNotes(page, company);
    }

    // ───────── 公司名录 ─────────
    private void DrawRealm(AutoVertLayoutGroup page)
    {
        if (NoRealm(page, "company_page_realm")) return;
        List<CompanyData> own = CompanySystem.All.Where(company => CompanySystem.RealmOf(company) == _realm)
            .OrderByDescending(CompanySystem.Worth).ToList();
        page.AddPageHeader(PageWidth, _realm.GetEmpireFullName(), string.Format(Word("company_count"), own.Count));
        if (own.Count == 0)
        {
            page.AddEmptyCard(PageWidth, "company_none", LM.Get("company_none_hint"));
            return;
        }
        var list = page.BeginCard(PageWidth, "company_card_list", WideKit.Gold);
        list.AddTableRow(PageWidth, -1, null, LM.Get("company_col_name"), 110f,
            (LM.Get("company_col_trade"), ""), (LM.Get("company_col_kind"), ""), (LM.Get("company_col_scale"), ""), (LM.Get("company_worth"), ""));
        int index = 0;
        foreach (CompanyData company in own.Take(40))
        {
            CompanyData target = company;
            float gouge = CompanySystem.Markup(_realm.id, company.sector);
            (string text, string hex) tag = Tag(company);
            UIHelper.MakeClickable(list.AddTableRow(PageWidth, index++, null, company.short_name, 110f,
                (TradeName(company) + (gouge >= 0.05f ? "▲" : gouge < 0f ? "▼" : ""), gouge >= 0.05f ? WideKit.Orange : WideKit.Soft),
                (tag.text, tag.hex), (company.scale.ToString(), WideKit.Value), (Money(CompanySystem.Worth(company)), WideKit.Gold)).gameObject,
                () => Show(target));
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
            (string text, string hex) tag = Tag(company);
            string trade = CompanySystem.IsCharter(company) ? tag.text : TradeName(company);
            UIHelper.MakeClickable(firms.AddTableRow(PageWidth, index, null, $"{index + 1}. {company.short_name}", 120f,
                (CompanySystem.RealmOf(company)?.GetEmpireName() ?? "", WideKit.Soft), (trade, tag.hex),
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
            if (realm != null) UIHelper.MakeClickable(row.gameObject, () => ShowRealm(realm, Page.Influence));
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
                string name = company.short_name + (CompanySystem.IsCharter(company) ? " " + LM.Get("company_tag_charter").ColorString(WideKit.Gold) : "");
                UIHelper.MakeClickable(card.AddTableRow(PageWidth, index++, null, name, 120f,
                    (company.price.ToString("0.0"), WideKit.Value),
                    (StockMarketSystem.ChangeText(move), move >= 0f ? WideKit.Green : WideKit.Red),
                    (Money(company.price * StockMarketSystem.Shares), WideKit.Gold)).gameObject, () => Show(target));
            }
            if (index == 0) card.AddParagraph(PageWidth, LM.Get("stock_no_listed"), 6, WideKit.Muted);
        }
    }
}
