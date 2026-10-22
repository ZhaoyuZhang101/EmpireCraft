using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 宗族总览：页头(开族始祖 · 立族之年) → 主指标(在世/历代、门第、田产、居官) → 门第 | 世系 → 门第走势 → 田产 | 族人分布 → 显赫族人
public partial class SpecificClanWindow
{
    private const int OverviewRows = 5, NotableCount = 6;
    // 门第走势上的横线：世家线(30)与丙、乙、甲等的分界
    private static readonly float[] PedigreeGuides = { GentrySystem.EnterAt, 40f, 55f, 70f };

    private void DrawOverview(AutoVertLayoutGroup page)
    {
        PersonalClanIdentity founder = SpecificClanManager.getPerson(_sc.founder);
        page.AddPageHeader(PageWidth, LM.Get("clan_page_overview"),
            founder == null ? null : string.Format(LM.Get("clan_overview_founder_chip"), founder.name),
            Year(_sc.established_timestamp));

        List<ClanLandHoldingView> holdings = LandEconomySystem.GetSpecificClanHoldings(_sc);
        float share = holdings.Sum(holding => holding.Share);
        int officials = _living.Count(OnOffice);
        (string standing, string standingHex) = _standing;
        page.AddKpiRow(PageWidth,
            (string.Format(LM.Get("clan_kpi_living"), UIHelper.ShortCount(_people.Count)), UIHelper.ShortCount(_living.Count),
                _living.Count > 0 ? WideKit.Green : WideKit.Red),
            (LM.Get("clan_kpi_pedigree"), _sc.gentry_assessed ? Mathf.RoundToInt(GentrySystem.Pedigree(_sc)).ToString() : "—",
                standingHex),
            (string.Format(LM.Get("clan_kpi_land"), share.ToString("0.#")), string.Format(LM.Get("clan_kpi_cities"), holdings.Count),
                WideKit.Cyan),
            (LM.Get("clan_kpi_officials"), string.Format(LM.Get("clan_kpi_people"), officials), WideKit.Gold));

        var top = page.BeginColumns(PageWidth);
        DrawPedigreeCard(top.BeginColumnCard(UIHelper.PageColumnWidth, LM.Get("clan_card_pedigree"), WideKit.Gold,
            _sc.gentry && _sc.gentry_since >= 0d ? string.Format(LM.Get("clan_card_pedigree_since"), Year(_sc.gentry_since)) : null,
            "ui/icons/iconKingdom"), standing, standingHex);
        DrawLineageCard(top.BeginColumnCard(UIHelper.PageColumnWidth, LM.Get("clan_card_lineage"), WideKit.Gold,
            LM.Get("clan_card_lineage_more"), "ui/icons/iconFamily"));

        DrawTrendCard(page);

        var middle = page.BeginColumns(PageWidth);
        var land = middle.BeginColumnCard(UIHelper.PageColumnWidth, LM.Get("clan_card_land"), WideKit.Gold,
            string.Format(LM.Get("clan_kpi_cities"), holdings.Count), "ui/icons/iconMoney");
        if (holdings.Count == 0) land.AddParagraph(UIHelper.PageColumnWidth, LM.Get("clan_overview_no_land"), 7, WideKit.Muted);
        int index = 0;
        foreach (ClanLandHoldingView holding in holdings.OrderByDescending(holding => holding.Share).Take(OverviewRows))
            land.AddValueRow(UIHelper.PageColumnWidth, index++, holding.CityName,
                $"{holding.Share:0.#}% · " + string.Format(LM.Get("clan_overview_city_rank"), holding.Rank),
                holding.Rank == 1 ? WideKit.Gold : WideKit.Green);

        var spread = middle.BeginColumnCard(UIHelper.PageColumnWidth, LM.Get("clan_card_spread"), WideKit.Gold, null,
            "ui/icons/iconCity");
        List<(string city, int count)> byCity = _living.Select(CityName).Where(city => !string.IsNullOrEmpty(city))
            .GroupBy(city => city).Select(group => (group.Key, group.Count())).OrderByDescending(item => item.Item2)
            .Take(OverviewRows).ToList();
        if (byCity.Count == 0) spread.AddParagraph(UIHelper.PageColumnWidth, LM.Get("label_none"), 7, WideKit.Muted);
        index = 0;
        foreach ((string city, int count) in byCity)
            spread.AddValueRow(UIHelper.PageColumnWidth, index++, city, string.Format(LM.Get("clan_kpi_people"), count), WideKit.Cyan);

        List<PersonalClanIdentity> notables = _living.OrderByDescending(OnOffice).ThenByDescending(Renown)
            .ThenBy(person => person.generation).Take(NotableCount).ToList();
        if (notables.Count == 0) return;
        var card = page.BeginCard(PageWidth, LM.Get("clan_card_notables"), WideKit.Gold, LM.Get("clan_card_notables_hint"),
            "ui/icons/iconKings");
        DrawTiles(card, notables.Select(person => (ClanRelation.NONE, person)), 2, Page.Biography);
    }

    // 族人现在在哪座城(虚拟族人按并入时所在的城)
    private static string CityName(PersonalClanIdentity person)
    {
        City city = person.is_virtual
            ? person.virtual_city_id > 0 ? World.world.cities.get(person.virtual_city_id) : null
            : person._actor?.city;
        return city?.data == null || city.isRekt() ? null : city.GetCityName();
    }

    private void DrawPedigreeCard(AutoVertLayoutGroup card, string standing, string standingHex)
    {
        float width = UIHelper.PageColumnWidth;
        int index = 0;
        card.AddValueRow(width, index++, LM.Get("clan_pedigree_status"), standing, standingHex);
        if (_sc.gentry_assessed)
        {
            card.AddValueRow(width, index++, LM.Get("clan_pedigree_seat"), GentrySystem.JunWang(_sc), WideKit.Gold);
            float pedigree = GentrySystem.Pedigree(_sc);
            ColorUtility.TryParseHtmlString(WideKit.Orange, out Color orange);
            UIHelper.AddBarRow(card, width - 12f, LM.Get("clan_pedigree_score").ColorString(WideKit.Muted),
                Mathf.RoundToInt(pedigree).ToString().ColorString(WideKit.Orange), pedigree / 100f, orange);
        }
        Empire patron = _sc.gentry_patron_empire_id >= 0 ? ModClass.EMPIRE_MANAGER?.get(_sc.gentry_patron_empire_id) : null;
        if (patron?.data != null && !patron.isRekt())
            card.AddValueRow(width, index++, LM.Get("clan_pedigree_patron"), patron.GetEmpireName(), WideKit.Value);
        if (_sc.HasHistoryEmpire())
            card.AddValueRow(width, index++, LM.Get("clan_pedigree_empire"), _sc.empire_name, WideKit.Red);
        if (!_sc.gentry_assessed)
            card.AddParagraph(width, LM.Get("clan_pedigree_unassessed_hint"), 6, WideKit.Muted);
    }

    // 世系：出自哪一支、分出了哪些支；点了到支派页看
    private void DrawLineageCard(AutoVertLayoutGroup card)
    {
        float width = UIHelper.PageColumnWidth;
        int index = 0;
        SpecificClan parent = _sc.parent_clan_id >= 0 ? SpecificClanManager.Get(_sc.parent_clan_id) : null;
        card.AddValueRow(width, index++, LM.Get("clan_lineage_from"),
            parent == null ? LM.Get("clan_lineage_root") : BranchName(parent), "#C9A7E8",
            parent == null ? null : () => ShowBranch(parent));
        List<SpecificClan> subs = SpecificClanManager._specificClans.ToList()
            .Where(clan => clan != null && clan.parent_clan_id == _sc.id).OrderBy(clan => clan.established_timestamp).ToList();
        foreach (SpecificClan sub in subs.Take(OverviewRows - 1))
        {
            SpecificClan target = sub;
            int living = sub.LivingPeople.Count;
            card.AddValueRow(width, index++, BranchName(sub),
                living > 0 ? $"{Reason(sub)} · " + string.Format(LM.Get("clan_kpi_people"), living) : LM.Get("clan_branch_extinct"),
                living > 0 ? WideKit.Soft : WideKit.Muted, () => ShowBranch(target));
        }
        if (subs.Count > OverviewRows - 1)
            card.AddValueRow(width, index, string.Format(LM.Get("clan_lineage_more"), subs.Count - (OverviewRows - 1)), "",
                WideKit.Muted, () => Go(Page.Branches));
        else if (subs.Count == 0)
            card.AddParagraph(width, LM.Get("clan_lineage_no_branch"), 6, WideKit.Muted);
        UIHelper.MakeClickable(card.gameObject, () => Go(Page.Branches));
    }

    private void ShowBranch(SpecificClan clan)
    {
        _branch = clan;
        _page = Page.Branches;
        Refresh(resetScroll: true);
    }

    // 门第走势：每年一柱(近 80 年)，横线是世家线与各等分界
    private void DrawTrendCard(AutoVertLayoutGroup page)
    {
        List<int> years = _sc.pedigree_years ?? new List<int>();
        List<int> values = _sc.pedigree_values ?? new List<int>();
        int count = Mathf.Min(years.Count, values.Count);
        // 没评过门第、或门第不到 20 的寒门(不记走势，见 GentrySystem.TrendFloor)，不画这张卡
        if (count == 0 && (!_sc.gentry_assessed || !_sc.gentry && GentrySystem.Pedigree(_sc) < GentrySystem.TrendFloor)) return;
        var card = page.BeginCard(PageWidth, LM.Get("clan_card_trend"), WideKit.Gold,
            count == 0 ? null : string.Format(LM.Get("clan_card_trend_span"), count), "ui/icons/iconMoneyFlow");
        if (count < 2)
        {
            card.AddParagraph(PageWidth, LM.Get("clan_trend_waiting"), 7, WideKit.Muted);
            return;
        }
        List<float> series = values.Take(count).Select(value => (float)value).ToList();
        card.AddColumnChart(PageWidth, series, 100f, WideKit.Orange, 36f, PedigreeGuides);
        int high = series.IndexOf(series.Max()), low = series.IndexOf(series.Min());
        card.AddParagraph(PageWidth, string.Format(LM.Get("clan_trend_summary"), years[0], years[count - 1],
            values[high], years[high], values[low], years[low]), 6, WideKit.Muted);
    }
}
