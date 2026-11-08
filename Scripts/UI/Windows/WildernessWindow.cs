using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 无人区(「卷册」宽窗口，组件见 WideScrollWindow / WideKit；用户 2026-11-08 "无人区没法自定义命名, 也没有无人区的界面")：
// 地图上点一片无人区(那个图层自己不接这一下时，见 HookClicks)打开这一片；营建页「无人区」按钮打开名册。
//   · 左栏(固定)：名字、类型、名字怎么来的 → 命名(输入框，留空恢复自动起名，见 WildernessSystem.Rename) → 地块、面积、归属、中立
//     → 查看(本片 / 名册) → 去处(定位、设为或取消永久中立)；
//   · 右栏「本片」：页头 → 主指标(地块、面积、分属几国、挂几个法理) → 分属 | 辖属城池(点开国情、城) → 法理 → 通行 | 开发；
//   · 右栏「名册」：按类型筛，全图各片按大小排，一页 ListPageSize 片，点一片切到它。
public class WildernessWindow : WideScrollWindow<WildernessWindow>
{
    private const float HalfWidth = UIHelper.PageColumnWidth;
    private const int ListPageSize = 18, CityRows = 8;
    private static int _pendingId = -1;

    // 状态(返回时还原)：看哪一片、本片还是名册、名册的类型筛选与页码
    private int _regionId = -1;
    private bool _list;
    private WildKind _kind = WildKind.None;
    private int _listPage;

    // 每次重画前算(Prepare)
    private WildRegion _region;
    private int _zones, _neutral, _unowned, _untitled, _developing, _passable, _developed;
    private float _devSum;
    private readonly List<(Kingdom kingdom, int zones)> _realms = new();
    private readonly List<(City city, int zones)> _cities = new();
    private readonly List<(KingdomTitle title, int zones)> _titles = new();
    private readonly List<(string key, int zones)> _passages = new();
    private readonly List<WildRegion> _listed = new();

    public static void Open(WildRegion region)
    {
        if (region == null) return;
        _pendingId = region.Id;
        ScrollWindow.showWindow(nameof(WildernessWindow));
    }

    // 点到的是一片无人区就打开它(地块归了城的不算)
    public static bool TryOpen(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (!WildernessSystem.Enabled || zone == null || zone.city != null) return false;
        WildRegion region = WildernessSystem.RegionOf(zone);
        if (region == null || region.Kind == WildKind.None) return false;
        Open(region);
        return true;
    }

    // 各图层点地块：原来的处理没接(点到的不是城、国……返回 false)且点的是无人区时打开无人区窗口。
    // 图层都建好以后(MainTab.Init)挂一次；同一图层不重复挂
    private static readonly HashSet<MetaTypeAsset> Hooked = new();

    public static void HookClicks()
    {
        if (AssetManager.meta_type_library?.list == null) return;
        foreach (MetaTypeAsset asset in AssetManager.meta_type_library.list)
        {
            if (asset?.click_action_zone == null || !Hooked.Add(asset)) continue;
            MetaZoneClickAction original = asset.click_action_zone;
            asset.click_action_zone = new MetaZoneClickAction((tile, power) => original(tile, power) || TryOpen(tile));
        }
    }

    protected override void OnOpen()
    {
        // 点地图打开那一片；按钮打开名册(编号每个世界从头排，不沿用上回看的那片)
        _list = _pendingId < 0;
        _regionId = _pendingId;
        _pendingId = -1;
        _listPage = 0;
    }

    protected override void Prepare()
    {
        _region = WildernessSystem.ById(_regionId);
        _zones = _neutral = _unowned = _untitled = _developing = _passable = _developed = 0;
        _devSum = 0f;
        _realms.Clear();
        _cities.Clear();
        _titles.Clear();
        _passages.Clear();
        _listed.Clear();
        if (_region == null) _list = true;
        else Tally(_region);
        if (!_list) return;
        _listed.AddRange(WildernessSystem.Regions.Where(region => region != null && region.Kind != WildKind.None &&
                                                                  (_kind == WildKind.None || region.Kind == _kind))
            .OrderByDescending(region => region.Zones.Count).ThenBy(region => region.Id));
        int pages = Mathf.Max(1, (_listed.Count + ListPageSize - 1) / ListPageSize);
        _listPage = Mathf.Clamp(_listPage, 0, pages - 1);
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;
    private static bool Alive(City city) => city?.data != null && !city.isRekt();

    // 一片各块：归哪国(含碎块并进的，见 WildernessSystem.OwnerOf)、跟着哪座城、挂哪个法理、军队怎么走、开发到哪
    private void Tally(WildRegion region)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return;
        var realms = new Dictionary<Kingdom, int>();
        var cities = new Dictionary<City, int>();
        var titles = new Dictionary<KingdomTitle, int>();
        var passages = new Dictionary<string, int>();
        bool develops = WildernessDevelopmentSystem.Develops(region.Kind);
        foreach (int id in region.Zones)
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone == null) continue;
            _zones++;
            if (WildernessSystem.IsNeutral(zone)) _neutral++;
            else if (WildernessSystem.OwnerOf(zone) is Kingdom owner && Alive(owner)) Add(realms, owner);
            else _unowned++;
            if (WildernessSystem.OriginOf(zone) is City origin && Alive(origin)) Add(cities, origin);
            if (WildernessSystem.TitleOf(zone) is KingdomTitle title) Add(titles, title);
            else _untitled++;
            if (WildernessPassage.TipKey(zone) is string passage) Add(passages, passage);
            if (!develops) continue;
            float dev = WildernessDevelopmentSystem.DevelopmentOf(zone);
            _developing++;
            _devSum += dev;
            if (dev >= WildernessDevelopmentSystem.Full) _developed++;
            else if (dev >= WildernessDevelopmentSystem.Half) _passable++;
        }
        _realms.AddRange(realms.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id).Select(pair => (pair.Key, pair.Value)));
        _cities.AddRange(cities.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id).Select(pair => (pair.Key, pair.Value)));
        _titles.AddRange(titles.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id).Select(pair => (pair.Key, pair.Value)));
        _passages.AddRange(passages.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value)));
    }

    private static void Add<TKey>(Dictionary<TKey, int> counts, TKey key)
    {
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        WildRegion region = _region;
        float inner = UIHelper.RailWidth - 8f;
        if (region != null)
        {
            var identity = rail.BeginRailCard("FactionFrame_dominate");
            identity.AddRailText(WildernessSystem.DisplayName(region).ColorString(RegionHex(region)), 10, 26f, wrap: true);
            identity.AddRailText(KindName(region.Kind).ColorString(WideKit.Cyan), 7, 10f);
            identity.AddRailText(NamingNote(region).ColorString(WideKit.Muted), 6, 9f);

            // 命名：输入框里是现在的名字(还没起名的空着、灰字写类型)；改了回车生效，留空恢复自动起名
            var naming = rail.BeginRailMenu(LM.Get("wild_window_rename"));
            var line = naming.BeginHoriGroup(new Vector2(inner, 18f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
            TextInput input = Object.Instantiate(TextInput.Prefab, null);
            input.Setup(region.Name ?? "", text =>
            {
                string name = (text ?? "").Trim();
                bool same = name == (region.Name ?? "") && (region.CustomName || name.Length > 0);
                if (!same && WildernessSystem.Rename(region, name))
                {
                    Refresh();
                    return;
                }
                input.input.SetTextWithoutNotify(region.Name ?? "");
            });
            input.SetSize(new Vector2(inner, 18f));
            if (input.input.textComponent != null)
            {
                input.input.textComponent.resizeTextMinSize = 6;
                input.input.textComponent.resizeTextMaxSize = 10;
            }
            input.input.characterLimit = WildernessSystem.MaxNameLength;
            if (input.input.placeholder is Text placeholder) placeholder.text = KindName(region.Kind);
            line.AddChild(input.gameObject);
            naming.AddRailText(LM.Get("wild_window_rename_hint").ColorString(WideKit.Muted), 6, 18f, wrap: true);

            var stats = rail.BeginRailCard();
            int row = 0;
            stats.AddRailRow(inner, LM.Get("wild_window_kpi_zones"), string.Format(LM.Get("wild_tooltip_zones_value"), _zones),
                WideKit.Value, row++);
            if (WorldMapScale.ZoneKm > 0f)
                stats.AddRailRow(inner, LM.Get("wild_window_kpi_area"), Area(_zones), WideKit.Value, row++);
            stats.AddRailRow(inner, LM.Get("wild_tooltip_owner"), OwnerSummary(), _realms.Count > 0 ? WideKit.Gold : WideKit.Muted, row++);
            if (_neutral > 0)
                stats.AddRailRow(inner, LM.Get("wild_tooltip_neutral"),
                    _neutral >= _zones ? LM.Get("wild_window_neutral_all") : string.Format(LM.Get("wild_tooltip_zones_value"), _neutral),
                    "#BFD8E6", row);
        }

        // 查看：本片 / 名册
        var view = rail.BeginRailMenu(LM.Get("wild_window_view"));
        if (region != null)
            view.AddRailOption(null, LM.Get("wild_window_tab_region"), null, !_list, () =>
            {
                _list = false;
                Refresh(resetScroll: true);
            }, 0);
        view.AddRailOption(null, LM.Get("wild_window_tab_list"), WildernessSystem.Regions.Count.ToString(), _list, () =>
        {
            _list = true;
            Refresh(resetScroll: true);
        }, 1);

        if (region == null) return;
        // 去处：定位(关窗、镜头移到这一片中间)、整片设为 / 取消永久中立(同神力「中立区」)
        var links = rail.BeginRailMenu(LM.Get("kov_links"));
        links.AddRailLink(SpriteTextureLoader.getSprite("ui/icons/iconKingdomZones"), LM.Get("wild_window_locate"), () => Locate(region));
        bool allNeutral = _zones > 0 && _neutral >= _zones;
        links.AddRailLink(SpriteTextureLoader.getSprite("ui/icons/godpowers/wild_neutral"),
            LM.Get(allNeutral ? "wild_window_neutral_off" : "wild_window_neutral_on"), () =>
            {
                WildernessSystem.SetNeutral(region, !allNeutral);
                Refresh();
            });
    }

    private static void Locate(WildRegion region)
    {
        TileZone center = region.Center ?? (region.Zones.Count > 0 ? World.world?.zone_calculator?.getZoneByID(region.Zones[0]) : null);
        WorldTile tile = center?.centerTile;
        if (tile == null || MoveCamera.instance == null) return;
        ScrollWindow.hideAllEvent();
        MoveCamera.instance.focusOn(tile.posV3);
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (_list)
        {
            DrawList(page);
            return;
        }
        WildRegion region = _region;
        page.AddPageHeader(PageWidth, "wild_window_page", KindName(region.Kind), OwnerSummary());
        var kpis = new List<(string, string, string)>
        {
            (LM.Get("wild_window_kpi_zones"), _zones.ToString(), WideKit.Green)
        };
        if (WorldMapScale.ZoneKm > 0f) kpis.Add((LM.Get("wild_window_kpi_area"), Area(_zones), WideKit.Cyan));
        kpis.Add((LM.Get("wild_window_kpi_realms"), string.Format(LM.Get("wild_window_realms_value"), _realms.Count), WideKit.Gold));
        kpis.Add((LM.Get("wild_window_kpi_titles"), _titles.Count.ToString(), WideKit.Orange));
        page.AddKpiRow(PageWidth, kpis.ToArray());

        var columns = page.BeginColumns(PageWidth);
        DrawRealms(columns.BeginColumnCard(HalfWidth, "wild_window_card_realms", WideKit.Gold,
            string.Format(LM.Get("wild_window_realms_value"), _realms.Count), "ui/icons/iconKingdom"));
        DrawCities(columns.BeginColumnCard(HalfWidth, "wild_window_card_cities", WideKit.Cyan, _cities.Count.ToString(), "ui/icons/iconCity"));
        DrawTitles(page);
        bool develops = _developing > 0;
        if (develops)
        {
            var lower = page.BeginColumns(PageWidth);
            DrawPassage(lower.BeginColumnCard(HalfWidth, "wild_tooltip_passage", "#BFD8E6"), HalfWidth);
            DrawDevelopment(lower.BeginColumnCard(HalfWidth, "wild_tooltip_development", "#9FD38A"));
        }
        else DrawPassage(page.BeginCard(PageWidth, "wild_tooltip_passage", "#BFD8E6"), PageWidth);
    }

    // 分属：各国占几块(条按占这一片的比例)，再列永久中立、无主的块数；点一国打开国情
    private void DrawRealms(AutoVertLayoutGroup card)
    {
        int index = 0;
        foreach ((Kingdom kingdom, int zones) in _realms)
        {
            Kingdom target = kingdom;
            card.AddBarRow(HalfWidth, index++, kingdom.GetKingdomName(), Share(zones), Zones(zones), WideKit.Value, KingdomHex(kingdom),
                "#2A2F33", () => KingdomOverviewWindow.Open(target), 52f, 40f);
        }
        if (_neutral > 0)
            card.AddBarRow(HalfWidth, index++, LM.Get("wild_tooltip_neutral"), Share(_neutral), Zones(_neutral), "#BFD8E6", "#BFD8E6",
                "#2A2F33", null, 52f, 40f);
        if (_unowned > 0)
            card.AddBarRow(HalfWidth, index++, LM.Get("wild_window_unowned"), Share(_unowned), Zones(_unowned), WideKit.Muted, WideKit.Muted,
                "#2A2F33", null, 52f, 40f);
        if (index == 0) card.AddListRow(HalfWidth, 0, null, LM.Get("wild_window_unowned"), "", titleWidth: 120f);
    }

    // 辖属城池：行政上各块跟着哪座城(城归谁，它辖下的无人区就归谁，见 WildernessSystem.OriginOf)；点开城
    private void DrawCities(AutoVertLayoutGroup card)
    {
        if (_cities.Count == 0)
        {
            card.AddListRow(HalfWidth, 0, null, LM.Get("label_none"), "", titleWidth: 120f);
            return;
        }
        int shown = Mathf.Min(_cities.Count, CityRows);
        for (int i = 0; i < shown; i++)
        {
            City city = _cities[i].city;
            card.AddValueRow(HalfWidth, i, city.GetCityName(), Zones(_cities[i].zones), WideKit.Value,
                () => MetaType.City.getAsset().selectAndInspect(city), 80f);
        }
        if (_cities.Count > shown)
            card.AddListRow(HalfWidth, shown, null, string.Format(LM.Get("wild_window_more_cities"), _cities.Count - shown), "",
                titleHex: WideKit.Muted, titleWidth: 120f);
    }

    // 法理：挂了哪几个法理、各几块、法理归谁(没人取得的写明)；没挂法理的块数单列
    private void DrawTitles(AutoVertLayoutGroup page)
    {
        var card = page.BeginCard(PageWidth, "wild_tooltip_title_line", "#D9C79A", _titles.Count.ToString(), "ui/icons/iconWorldLaws");
        int index = 0;
        foreach ((KingdomTitle title, int zones) in _titles)
        {
            Kingdom holder = TitleHolder(title);
            string detail = holder != null
                ? string.Format(LM.Get("wild_window_title_held"), zones, holder.GetKingdomName())
                : string.Format(LM.Get("wild_window_title_free"), zones);
            card.AddListRow(PageWidth, index++, null, title.data?.name ?? "?", detail, null, "#D9C79A", 110f);
        }
        if (_untitled > 0)
            card.AddListRow(PageWidth, index, null, LM.Get("wild_tooltip_title_none"), Zones(_untitled), null, WideKit.Muted, 110f);
    }

    // 法理归谁：这一片里挂这个法理的随便哪块问一次(同一法理各块一样，见 WildernessSystem.HolderOf)
    private Kingdom TitleHolder(KingdomTitle title)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null || _region == null) return null;
        foreach (int id in _region.Zones)
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone != null && WildernessSystem.TitleOf(zone) == title) return WildernessSystem.HolderOf(zone);
        }
        return null;
    }

    // 通行：军队在各块怎么走(有路、小径、丘陵、游牧能走、过不去)各几块；开发过半能走的块算在开发里
    private void DrawPassage(AutoVertLayoutGroup card, float width)
    {
        if (_passages.Count == 0)
        {
            card.AddListRow(width, 0, null, LM.Get("wild_window_passage_open"), "", titleWidth: width - 30f);
            return;
        }
        for (int i = 0; i < _passages.Count; i++)
            card.AddValueRow(width, i, LM.Get(_passages[i].key), Zones(_passages[i].zones), "#BFD8E6", null, (width - 20f) * 0.72f);
    }

    // 开发(雨林、大泽，见 WildernessDevelopmentSystem)：平均开发度、过半能走的、开发完的
    private void DrawDevelopment(AutoVertLayoutGroup card)
    {
        float average = _developing > 0 ? _devSum / _developing : 0f;
        card.AddMetricRow(HalfWidth,
            (LM.Get("wild_window_dev_average"), $"{Mathf.FloorToInt(average)}%", WideKit.Green),
            (LM.Get("wild_window_dev_passable"), _passable.ToString(), WideKit.Value),
            (LM.Get("wild_window_dev_done"), _developed.ToString(), WideKit.Gold));
    }

    // 名册：类型筛选(只列这张图上有的类型) → 各片按大小排、一页 ListPageSize 片 → 翻页
    private void DrawList(AutoVertLayoutGroup page)
    {
        int total = WildernessSystem.Regions.Count(region => region != null && region.Kind != WildKind.None);
        page.AddPageHeader(PageWidth, "wild_window_tab_list", string.Format(LM.Get("wild_window_list_count"), total));
        if (total == 0)
        {
            page.AddEmptyCard(PageWidth, "wild_window_list_empty", LM.Get("wild_window_list_empty_hint"));
            return;
        }
        var chips = new List<(string, bool, UnityEngine.Events.UnityAction)>
        {
            (LM.Get("wild_window_all"), _kind == WildKind.None, () => SetKind(WildKind.None))
        };
        foreach (WildKind kind in WildernessSystem.Regions.Where(region => region != null && region.Kind != WildKind.None)
                     .Select(region => region.Kind).Distinct().OrderBy(kind => (int)kind))
        {
            WildKind pick = kind;
            chips.Add((KindName(kind), _kind == kind, () => SetKind(pick)));
        }
        page.AddChipRow(PageWidth, chips.ToArray());

        var card = page.BeginCard(PageWidth, _kind == WildKind.None ? "wild_window_tab_list" : KindName(_kind), WideKit.Gold,
            string.Format(LM.Get("wild_window_list_count"), _listed.Count));
        int pages = Mathf.Max(1, (_listed.Count + ListPageSize - 1) / ListPageSize);
        int start = _listPage * ListPageSize;
        for (int i = start; i < Mathf.Min(_listed.Count, start + ListPageSize); i++)
        {
            WildRegion region = _listed[i];
            string detail = string.Format(LM.Get("wild_window_list_row"), KindName(region.Kind), region.Zones.Count, OwnerSummary(region));
            card.AddListRow(PageWidth, i - start, null, WildernessSystem.DisplayName(region), detail, () =>
            {
                _regionId = region.Id;
                _list = false;
                Refresh(resetScroll: true);
            }, RegionHex(region), 120f);
        }
        page.AddPager(PageWidth, _listPage, pages, LM.Get("wild_window_pager"), go =>
        {
            _listPage = go;
            Refresh(resetScroll: true);
        });
    }

    private void SetKind(WildKind kind)
    {
        _kind = kind;
        _listPage = 0;
        Refresh(resetScroll: true);
    }

    // ───────── 文字 ─────────
    private static string KindName(WildKind kind) => WildernessNaming.KindName(kind) ?? "";

    private float Share(int zones) => _zones <= 0 ? 0f : (float)zones / _zones;

    private static string Zones(int zones) => string.Format(LM.Get("wild_tooltip_zones_value"), zones);

    private static string Area(int zones) =>
        string.Format(LM.Get("wild_window_area_value"), UIHelper.ShortCount(zones * WorldMapScale.ZoneKm * WorldMapScale.ZoneKm));

    // 名字怎么来的：改过的、按哪个文化起的、附近还没有文明只标类型的
    private static string NamingNote(WildRegion region)
    {
        if (region.CustomName) return LM.Get("wild_window_named_custom");
        if (string.IsNullOrEmpty(region.Name)) return LM.Get("wild_window_named_none");
        if (string.IsNullOrEmpty(region.Culture)) return LM.Get("wild_window_named_auto");
        string culture = OnomasticsRule.ALL_CULTURE_TRANSLATE.ContainsKey(region.Culture) ? region.Culture.GetCultureTranslate() : region.Culture;
        return string.Format(LM.Get("wild_window_named_culture"), culture);
    }

    // 本片归属一句话：一国写国名，几国写"某国等 N 国"，没有国家的写永久中立或无主
    private string OwnerSummary() => Summary(_realms.Select(pair => pair.kingdom).ToList(), _neutral > 0 && _neutral >= _zones);

    private static string OwnerSummary(WildRegion region)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return "";
        var counts = new Dictionary<Kingdom, int>();
        int neutral = 0, zones = 0;
        foreach (int id in region.Zones)
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone == null) continue;
            zones++;
            if (WildernessSystem.IsNeutral(zone)) neutral++;
            else if (WildernessSystem.OwnerOf(zone) is Kingdom owner && Alive(owner)) Add(counts, owner);
        }
        return Summary(counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id).Select(pair => pair.Key).ToList(),
            zones > 0 && neutral >= zones);
    }

    private static string Summary(List<Kingdom> realms, bool neutral)
    {
        if (realms.Count == 0) return LM.Get(neutral ? "wild_tooltip_neutral" : "wild_window_unowned");
        if (realms.Count == 1) return realms[0].GetKingdomName();
        return string.Format(LM.Get("wild_window_realms_more"), realms[0].GetKingdomName(), realms.Count);
    }

    private static string RegionHex(WildRegion region)
    {
        Color32 color = WildernessOverlay.ColorOf(region);
        // 底色偏暗的(山脉、熔岩)提亮一点，左栏深底上看得清
        return WideKit.Hex(Color.Lerp(color, Color.white, 0.35f));
    }

    private static string KingdomHex(Kingdom kingdom)
    {
        ColorAsset color = CustomColorSystem.Of(kingdom) ?? kingdom.getColor();
        return color == null ? WideKit.Gold : WideKit.Hex(color.getColorMain());
    }
}
