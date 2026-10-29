using System;
using System.Collections;
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

// 天下宗族(「卷册」宽窗口，用户 2026-10-22 定，示意图 Tasks/ui-mockups/clan-wide.png ①)：
//   · 左栏：天下宗族(现存几族 · 族人几何) → 筛选(全部宗族、帝王之家、世家、已绝嗣) → 按物种(有宗族的物种，带族数) → 搜索；
//   · 右栏：页头(筛选 · 物种 · 族数) → 主指标(最大宗族、世家、帝王之家) → 按姓归并分节(分了支的姓各一节，本宗在前；
//     其余单独的宗族一节)，每节两栏宗族卡(族色条、族名与门第、宗子头像与官职、在世/世代/田产/居官)，整卡点开宗族总览；每页 12 族。
// 各族的人数、物种、门第在打开窗口时算一遍(族多时逐族取族人要些工夫)，筛选翻页只用算好的。
public class SpecificClanListWindow : WideScrollWindow<SpecificClanListWindow>
{
    private enum Filter { All, Ruling, Gentry, Extinct }

    private sealed class Entry
    {
        public SpecificClan clan;
        public int living;
        public string species;
        public Empire ruling;
        public long group;
    }

    private const int PerPage = 12, RailSpecies = 6;

    private Filter _filter = Filter.All;
    // 空 = 全部物种
    private string _species = "";
    private string _search = "";
    private int _page;
    private readonly List<Entry> _entries = new();
    private List<Entry> _shown = new();
    // 本次筛出来的各姓有几族(分节用)、有没有分了支的姓
    private readonly Dictionary<long, int> _groupSize = new();
    private bool _anyMulti;

    // 每次打开(含从宗族窗口返回)重算各族数据；筛选、页码保留
    protected override void OnOpen() => Collect();

    private void Collect()
    {
        _entries.Clear();
        // 在位皇室按始祖宗族记一次(同一始祖的各支都算这家)
        var ruling = new Dictionary<SpecificClan, Empire>();
        if (ModClass.EMPIRE_MANAGER != null)
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.EmpireSpecificClan == null) continue;
                SpecificClan root = empire.EmpireSpecificClan.GetRootClan();
                if (root != null && !ruling.ContainsKey(root)) ruling[root] = empire;
            }
        foreach (SpecificClan clan in SpecificClanManager._specificClans.ToList())
        {
            if (clan == null) continue;
            List<PersonalClanIdentity> living = clan.LivingPeople;
            PersonalClanIdentity sample = living.FirstOrDefault(person => !string.IsNullOrEmpty(person.species)) ??
                                          clan.SnapshotPeople().FirstOrDefault(person => person != null && !string.IsNullOrEmpty(person.species));
            if (sample == null) continue;
            SpecificClan clanRoot = clan.GetRootClan();
            ruling.TryGetValue(clanRoot ?? clan, out Empire empire);
            _entries.Add(new Entry
            {
                clan = clan, living = living.Count, species = sample.species, ruling = empire,
                group = clan.SurnameRoot()?.id ?? clan.id
            });
        }
    }

    // ───────── 数据(每次重画前按筛选取一遍) ─────────
    protected override void Prepare()
    {
        IEnumerable<Entry> list = _entries.Where(entry => _species.Length == 0 || entry.species == _species);
        list = _filter switch
        {
            Filter.Ruling => list.Where(entry => entry.ruling != null),
            Filter.Gentry => list.Where(entry => entry.clan.gentry && entry.living > 0),
            Filter.Extinct => list.Where(entry => entry.living == 0),
            _ => list.Where(entry => entry.living > 0)
        };
        if (_search.Length > 0) list = list.Where(entry => Matches(entry.clan, _search));
        // 按姓归并：分了支的姓各占一组(人多的姓在前，组里本宗在前、再按分出先后)，单独的宗族排在最后
        List<List<Entry>> groups = list.GroupBy(entry => entry.group).Select(group => group.ToList()).ToList();
        _shown = groups.Where(group => group.Count > 1).OrderByDescending(group => group.Sum(entry => entry.living))
            .SelectMany(group => group.OrderBy(entry => entry.clan.id == entry.group ? 0 : 1)
                .ThenBy(entry => entry.clan.established_timestamp))
            .Concat(groups.Where(group => group.Count == 1).SelectMany(group => group)
                .OrderByDescending(entry => entry.living).ThenBy(entry => entry.clan.established_timestamp))
            .ToList();
        _groupSize.Clear();
        foreach (Entry entry in _shown)
            _groupSize[entry.group] = _groupSize.TryGetValue(entry.group, out int size) ? size + 1 : 1;
        _anyMulti = _groupSize.Values.Any(size => size > 1);
        _page = Mathf.Clamp(_page, 0, Mathf.Max(0, Pages - 1));
    }

    private int Pages => Mathf.Max(1, Mathf.CeilToInt(_shown.Count / (float)PerPage));

    private static bool Matches(SpecificClan clan, string text)
    {
        bool Has(string value) => !string.IsNullOrEmpty(value) && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        if (Has(clan.GetDisplayName()) || Has(clan.name) || Has(clan.branch_label) || Has(clan.empire_name) ||
            clan.gentry_assessed && Has(GentrySystem.JunWang(clan))) return true;
        return clan.SnapshotPeople().Any(person => person != null && (Has(person.name) || Has(person.cityName) || Has(person.kingdomName)));
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        List<Entry> alive = _entries.Where(entry => entry.living > 0).ToList();
        rail.AddRailTitle(LM.Get("clan_list_title"), string.Format(LM.Get("clan_list_summary"), alive.Count,
            UIHelper.ShortCount(alive.Sum(entry => entry.living))));

        var filters = rail.BeginRailMenu();
        int index = 0;
        foreach ((Filter filter, string key, string icon, int count) in new[]
                 {
                     (Filter.All, "clan_list_all", "iconFamilyList", alive.Count),
                     (Filter.Ruling, "clan_list_ruling", "iconCrown", _entries.Count(entry => entry.ruling != null)),
                     (Filter.Gentry, "clan_list_gentry", "iconKingdom", alive.Count(entry => entry.clan.gentry)),
                     (Filter.Extinct, "clan_list_extinct", "iconFamilyDestroyed", _entries.Count(entry => entry.living == 0))
                 })
        {
            Filter target = filter;
            filters.AddRailOption(SpriteTextureLoader.getSprite("ui/icons/" + icon), LM.Get(key), count.ToString(), _filter == filter,
                () => SetFilter(target), index++);
        }

        List<(string species, int count)> species = _entries.Where(entry => entry.living > 0).GroupBy(entry => entry.species)
            .Select(group => (group.Key, group.Count())).OrderByDescending(item => item.Item2).ToList();
        if (species.Count > 1)
        {
            var menu = rail.BeginRailMenu(LM.Get("clan_list_species"));
            menu.AddRailOption(null, LM.Get("clan_list_all_species"), alive.Count.ToString(), _species.Length == 0,
                () => SetSpecies(""), 0);
            index = 1;
            foreach ((string id, int count) in species.Take(RailSpecies))
            {
                ActorAsset asset = AssetManager.actor_library.get(id);
                string target = id;
                menu.AddRailOption(asset?.getSpriteIcon(), asset != null ? LM.Get(asset.getLocaleID()) : id, count.ToString(),
                    _species == id, () => SetSpecies(target), index++);
            }
        }

        var search = rail.BeginRailCard();
        search.AddRailInput(LM.Get("clan_list_search"), _search, text =>
        {
            string value = (text ?? "").Trim();
            if (value == _search) return;
            _search = value;
            _page = 0;
            // 下一帧再重画，不在输入框自己的事件里把它拆掉
            if (gameObject.activeInHierarchy) StartCoroutine(RefreshNextFrame());
        });
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh(resetScroll: true);
    }

    private void SetFilter(Filter filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        _page = 0;
        Refresh(resetScroll: true);
    }

    private void SetSpecies(string species)
    {
        if (_species == species) return;
        _species = species;
        _page = 0;
        Refresh(resetScroll: true);
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        string speciesName = _species.Length == 0 ? null
            : AssetManager.actor_library.get(_species) is ActorAsset asset ? LM.Get(asset.getLocaleID()) : _species;
        page.AddPageHeader(PageWidth, LM.Get(_search.Length > 0 ? "title_search_result" : FilterKey(_filter)), speciesName,
            string.Format(LM.Get("clan_list_count"), _shown.Count));

        List<Entry> alive = _entries.Where(entry => entry.living > 0 && (_species.Length == 0 || entry.species == _species)).ToList();
        Entry largest = alive.OrderByDescending(entry => entry.living).FirstOrDefault();
        page.AddKpiRow(PageWidth,
            (largest == null ? LM.Get("clan_list_largest") : string.Format(LM.Get("clan_list_largest_named"), largest.clan.GetDisplayName()),
                largest == null ? "—" : string.Format(LM.Get("clan_kpi_people"), UIHelper.ShortCount(largest.living)), WideKit.Gold),
            (LM.Get("clan_list_gentry"), string.Format(LM.Get("clan_list_count"), alive.Count(entry => entry.clan.gentry)), WideKit.Orange),
            (LM.Get("clan_list_ruling"), string.Format(LM.Get("clan_list_count"), alive.Count(entry => entry.ruling != null)), WideKit.Red));

        if (_shown.Count == 0)
        {
            page.AddEmptyCard(PageWidth, LM.Get("clan_list_none"));
            return;
        }
        List<Entry> slice = _shown.Skip(_page * PerPage).Take(PerPage).ToList();
        long currentGroup = long.MinValue;
        bool othersHeader = false;
        var row = (AutoHoriLayoutGroup)null;
        int inRow = 0;
        foreach (Entry entry in slice)
        {
            int groupSize = _groupSize.TryGetValue(entry.group, out int size) ? size : 1;
            if (groupSize > 1 && entry.group != currentGroup)
            {
                currentGroup = entry.group;
                SpecificClan root = SpecificClanManager.Get(entry.group) ?? entry.clan;
                page.AddSectionHeader(PageWidth, string.Format(LM.Get("clan_group_header"), root.GetDisplayName(), groupSize),
                    WideKit.Cyan, false, "ui/icons/iconFamily");
                row = null;
            }
            else if (groupSize == 1 && !othersHeader)
            {
                othersHeader = true;
                currentGroup = long.MinValue;
                if (_anyMulti)
                    page.AddSectionHeader(PageWidth, LM.Get("clan_group_others"), WideKit.Cyan, false, "ui/icons/iconFamilyList");
                row = null;
            }
            if (row == null || inRow == 2)
            {
                row = page.BeginColumns(PageWidth);
                inRow = 0;
            }
            DrawClanCard(row, UIHelper.PageColumnWidth, entry);
            inRow++;
        }
        page.AddPager(PageWidth, _page, Pages, LM.Get("clan_pager"), target =>
        {
            _page = target;
            Refresh(resetScroll: true);
        });
    }

    private static string FilterKey(Filter filter) => filter switch
    {
        Filter.Ruling => "clan_list_ruling",
        Filter.Gentry => "clan_list_gentry",
        Filter.Extinct => "clan_list_extinct",
        _ => "clan_list_all"
    };

    // 宗族卡：族色条、族名(右边门第)、宗子行、在世/世代/田产/居官；整卡点开宗族总览
    private static void DrawClanCard(AutoHoriLayoutGroup row, float width, Entry entry)
    {
        SpecificClan clan = entry.clan;
        var card = row.BeginColumnCard(width);
        if (ColorUtility.TryParseHtmlString(SpecificClanWindow.ClanHex(clan), out Color color)) card.AddColorStrip(width, color);
        (string standing, string standingHex) = SpecificClanWindow.Standing(clan, entry.ruling);
        UIHelper.AddCardTitle(card, width, clan.GetDisplayName(), WideKit.Gold, standing.ColorString(standingHex), 0f);

        PersonalClanIdentity heir = SpecificClanWindow.Heir(clan) ?? SpecificClanManager.getPerson(clan.founder);
        string caption = entry.living > 0 ? LM.Get("clan_list_heir") : LM.Get("clan_list_last");
        if (clan.gentry_assessed) caption += " · " + string.Format(LM.Get("clan_list_seat"), GentrySystem.JunWang(clan));
        if (heir != null) card.AddIdentityRow(width, heir, caption, SpecificClanWindow.Post(heir), () => SpecificClanWindow.ShowClan(clan));

        List<PersonalClanIdentity> people = clan.SnapshotPeople().Where(person => person != null).ToList();
        PersonalClanIdentity founder = SpecificClanManager.getPerson(clan.founder);
        int generations = people.Count == 0 ? 0 : people.Max(person =>
            founder != null && founder.generation > 0 && person.generation >= founder.generation
                ? person.generation - founder.generation + 1 : Mathf.Max(1, person.generation));
        int land = LandEconomySystem.GetSpecificClanHoldings(clan).Count;
        int officials = clan.LivingPeople.Count(SpecificClanWindow.OnOffice);
        card.AddMetricRow(width,
            (LM.Get("clan_list_living"), UIHelper.ShortCount(entry.living), entry.living > 0 ? WideKit.Green : WideKit.Red),
            (LM.Get("clan_list_generations"), generations.ToString(), WideKit.Value),
            (LM.Get("clan_list_land"), string.Format(LM.Get("clan_kpi_cities"), land), WideKit.Cyan),
            (LM.Get("clan_list_officials"), officials.ToString(), WideKit.Gold));
        UIHelper.MakeClickable(card.gameObject, () => SpecificClanWindow.ShowClan(clan));
    }
}
