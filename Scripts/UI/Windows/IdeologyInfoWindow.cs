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
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 理念窗口(理念图层点城市、各窗口的"理念"一行打开)。自上而下：
//   理念选择(按四条路线分行) → 总览卡(图标/路线/光谱位置/简介/吸引与排斥的阶层)
//   → 世界数据块 → 世界理念构成条 → 信众最多的国家 → 该理念的政党 → 聚焦城市 → 文化理念路线树
public class IdeologyInfoWindow : AbstractWideWindow<IdeologyInfoWindow>
{
    private const float PanelWidth = 440f;
    private const float Gap = 4f;
    private const float ChipHeight = 12f;
    private static PartyIdeology? _pendingSelection;
    private static City _pendingCity;
    private readonly List<GameObject> _content = new();
    private AutoVertLayoutGroup _root;
    private PartyIdeology _selected = PartyIdeology.Conservatism;
    private City _focusedCity;
    private InstitutionGraphView _graph;

    public static void Open()
    {
        City capital = SelectedMetas.selected_kingdom?.capital;
        _pendingSelection = capital == null ? null : IdeologyPopulationSystem.GetDominant(capital);
        _pendingCity = capital;
        ScrollWindow.showWindow(nameof(IdeologyInfoWindow));
    }

    public static void Open(PartyIdeology ideology)
    {
        _pendingSelection = ideology;
        _pendingCity = null;
        ScrollWindow.showWindow(nameof(IdeologyInfoWindow));
    }

    public static void Open(City city)
    {
        if (city == null || city.isRekt()) return;
        _pendingSelection = IdeologyPopulationSystem.GetDominant(city);
        _pendingCity = city;
        ScrollWindow.showWindow(nameof(IdeologyInfoWindow));
    }

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        if (_pendingSelection.HasValue) _selected = _pendingSelection.Value;
        _focusedCity = _pendingCity;
        _pendingSelection = null;
        _pendingCity = null;
        Rebuild();
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        Clear();
    }

    private void Clear()
    {
        foreach (GameObject item in _content)
            if (item != null) Destroy(item);
        _content.Clear();
        _graph = null;
    }

    // 一次扫描得到全世界各理念人数和主理念城市数
    private sealed class WorldStats
    {
        public readonly Dictionary<PartyIdeology, int> People = new();
        public readonly Dictionary<PartyIdeology, int> DominantCities = new();
        public int Total;
    }

    private static WorldStats ScanWorld()
    {
        var stats = new WorldStats();
        foreach (City city in World.world?.cities ?? Enumerable.Empty<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetCityCounts(city);
            int cityTotal = counts.Values.Sum();
            if (cityTotal <= 0) continue;
            foreach (KeyValuePair<PartyIdeology, int> pair in counts)
                stats.People[pair.Key] = (stats.People.TryGetValue(pair.Key, out int value) ? value : 0) + pair.Value;
            stats.Total += cityTotal;
            PartyIdeology dominant = IdeologyPopulationSystem.GetDominant(city);
            stats.DominantCities[dominant] = (stats.DominantCities.TryGetValue(dominant, out int cities) ? cities : 0) + 1;
        }
        return stats;
    }

    private void Rebuild()
    {
        Clear();
        WorldStats world = ScanWorld();
        AddSelector();
        AddOverviewCard();
        AddWorldChips(world);
        AddDistributionBar(world);
        AddRealmsCard();
        AddPartiesCard();
        AddFocusedCity();
        AddCultureTree();
    }

    #region 理念选择

    private void AddSelector()
    {
        IdeologyRoute[] routes = Enum.GetValues(typeof(IdeologyRoute)).Cast<IdeologyRoute>().ToArray();
        float height = 6f + routes.Length * 15f;
        var card = Card(new Vector2(PanelWidth, height), "FactionFrame");
        foreach (IdeologyRoute route in routes)
        {
            var row = card.BeginHoriGroup(new Vector2(PanelWidth - 10f, 14f), TextAnchor.MiddleLeft, 3);
            Label(row, PartySystem.GetRouteName(route).ColorString("#7FD8EA"), 34f, 7, TextAnchor.MiddleRight);
            foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
                         .Where(ideology => PartySystem.RouteOf(ideology) == route))
            {
                PartyIdeology target = ideology;
                bool selected = ideology == _selected;
                var chip = row.BeginHoriGroup(new Vector2(92f, 13f), TextAnchor.MiddleLeft, 2);
                UIHelper.AddInsetBackground(chip, new Vector2(92f, 13f));
                UIHelper.AddLayoutIcon(chip.transform, SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(ideology)), 10f);
                var button = chip.AddButtonIntoHoriLayout($"ideology_select_{ideology}",
                    PartySystem.GetIdeologyName(ideology).ColorString(selected ? "#F3C34A" : Hex(ideology)),
                    () => { _selected = target; Rebuild(); }, size: new Vector2(78f, 11f));
                button.Background.enabled = selected;
            }
        }
    }

    #endregion

    #region 总览卡

    private void AddOverviewCard()
    {
        Vector2 position = PartySystem.GetPosition(_selected);
        List<(SocialClass socialClass, float affinity)> affinities = Enum.GetValues(typeof(SocialClass))
            .Cast<SocialClass>().Select(socialClass => (socialClass, PartySystem.GetAffinity(_selected, socialClass)))
            .OrderByDescending(pair => pair.Item2).ToList();
        const float height = 74f;
        var card = Card(new Vector2(PanelWidth, height), "FactionFrame_dominate");

        var head = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, 20f), TextAnchor.MiddleLeft, 4);
        UIHelper.AddLayoutIcon(head.transform, SpriteTextureLoader.getSprite(IdeologyTraitIcons.Path(_selected)), 18f);
        Label(head, PartySystem.GetIdeologyName(_selected).ColorString(Hex(_selected)), 130f, 11, TextAnchor.MiddleLeft, 20f);
        Label(head, $"{LM.Get("ideology_window_route")} {PartySystem.GetRouteName(PartySystem.RouteOf(_selected))}"
            .ColorString("#7FD8EA"), 90f, 7, TextAnchor.MiddleLeft, 20f);
        Label(head, string.Format(LM.Get("ideology_window_compass"), EconomicLabel(position.x), AuthorityLabel(position.y))
            .ColorString("#C9A7E8"), 180f, 7, TextAnchor.MiddleRight, 20f);

        var description = card.AddTextIntoVertLayout(Description(_selected).ColorString("#D8D2C0"), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth - 16f, 18f));
        description.UseFixedFontSize(7, HorizontalWrapMode.Wrap);

        AddClassRow(card, LM.Get("ideology_window_attracts"), affinities.Where(pair => pair.affinity >= 30f).Take(4),
            "#65D66E");
        AddClassRow(card, LM.Get("ideology_window_repels"),
            affinities.Where(pair => pair.affinity <= 0f).OrderBy(pair => pair.affinity).Take(4), "#D98C8C");
    }

    private static void AddClassRow(AutoVertLayoutGroup card, string label,
        IEnumerable<(SocialClass socialClass, float affinity)> classes, string hex)
    {
        var row = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, ChipHeight), TextAnchor.MiddleLeft, 3);
        Label(row, label.ColorString("#A8B8BE"), 60f, 7, TextAnchor.MiddleRight);
        List<(SocialClass socialClass, float affinity)> list = classes.ToList();
        if (list.Count == 0)
        {
            Label(row, LM.Get("label_none").ColorString("#8FA0A8"), 80f, 7, TextAnchor.MiddleLeft);
            return;
        }
        foreach ((SocialClass socialClass, float affinity) in list)
        {
            var chip = row.BeginHoriGroup(new Vector2(84f, ChipHeight), TextAnchor.MiddleCenter, 0);
            UIHelper.AddInsetBackground(chip, new Vector2(84f, ChipHeight));
            Label(chip, $"{LM.Get($"class_{socialClass}")} {affinity:+0;-0}".ColorString(hex), 80f, 7,
                TextAnchor.MiddleCenter);
        }
    }

    private static string EconomicLabel(float x) =>
        LM.Get(x <= -60f ? "ideology_axis_far_left" : x < -20f ? "ideology_axis_left" :
            x >= 60f ? "ideology_axis_far_right" : x > 20f ? "ideology_axis_right" : "ideology_axis_center");

    private static string AuthorityLabel(float y) =>
        LM.Get(y >= 60f ? "ideology_axis_libertarian" : y > 20f ? "ideology_axis_liberal" :
            y <= -60f ? "ideology_axis_totalitarian" : y < -20f ? "ideology_axis_authoritarian" : "ideology_axis_moderate");

    private static string Description(PartyIdeology ideology)
    {
        string snake = string.Concat(ideology.ToString().Select((ch, index) =>
            index > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
        string key = $"institution_ideology_{snake}_desc";
        string text = LM.Get(key);
        return string.IsNullOrWhiteSpace(text) || text == key ? "" : text;
    }

    #endregion

    #region 世界数据

    private void AddWorldChips(WorldStats world)
    {
        int people = world.People.TryGetValue(_selected, out int count) ? count : 0;
        int cities = world.DominantCities.TryGetValue(_selected, out int dominant) ? dominant : 0;
        List<Empire> empires = LiveEmpires();
        int parties = empires.Sum(empire => PartySystem.GetParties(empire).Count(party => party.Ideology == _selected));
        int governing = empires.Count(empire => PartySystem.GetGovernmentParty(empire)?.Ideology == _selected);
        List<string> cultures = OnomasticsRule.ALL_CULTURE_RULE.Keys.ToList();
        int researched = cultures.Count(culture => PartySystem.IsResearched(culture, _selected));
        List<string> stateCultures = cultures.Where(culture =>
            InstitutionSystem.GetOrCreateCultureState(culture)?.state_ideology == _selected.ToString()).ToList();

        var card = Card(new Vector2(PanelWidth, 3 * (ChipHeight + 2f) + 8f), "FactionFrame");
        AddChipRow(card,
            ("ui/icons/iconPopulation", LM.Get("ideology_window_believers"), $"{people} · {Percent(people, world.Total)}"),
            ("ui/icons/iconCity", LM.Get("ideology_window_dominant_cities"), cities.ToString()),
            ("ui/icons/iconKingdom", LM.Get("ideology_window_governing"), governing.ToString()));
        AddChipRow(card,
            ("ui/icons/iconPlot", LM.Get("ideology_window_parties"), parties.ToString()),
            ("ui/icons/iconBooks", LM.Get("ideology_window_researched"), $"{researched}/{cultures.Count}"),
            ("ui/icons/iconCulture", LM.Get("ideology_window_state_cultures"), stateCultures.Count.ToString()));
        var row = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, ChipHeight), TextAnchor.MiddleCenter, 3);
        Label(row, (stateCultures.Count == 0
                ? LM.Get("ideology_window_no_state_culture")
                : string.Format(LM.Get("ideology_window_state_culture_list"),
                    string.Join("、", stateCultures.Select(culture => culture.GetCultureTranslate()))))
            .ColorString("#A8B8BE"), PanelWidth - 16f, 7, TextAnchor.MiddleCenter);
    }

    private static void AddChipRow(AutoVertLayoutGroup card, params (string icon, string label, string value)[] chips)
    {
        float width = (PanelWidth - 12f - (chips.Length - 1) * 3f) / chips.Length;
        var row = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, ChipHeight), TextAnchor.MiddleCenter, 3);
        foreach ((string icon, string label, string value) in chips)
        {
            var chip = row.BeginHoriGroup(new Vector2(width, ChipHeight), TextAnchor.MiddleLeft, 2);
            UIHelper.AddInsetBackground(chip, new Vector2(width, ChipHeight));
            UIHelper.AddLayoutIcon(chip.transform, UIHelper.FirstSprite(icon), 9f);
            Label(chip, label.ColorString("#A8B8BE"), width - 60f, 7, TextAnchor.MiddleLeft);
            Label(chip, value.ColorString("#F2EEE2"), 44f, 7, TextAnchor.MiddleRight);
        }
    }

    // 全世界理念构成：13 色堆叠条，选中的理念那一段加亮边
    private void AddDistributionBar(WorldStats world)
    {
        var card = Card(new Vector2(PanelWidth, 30f), "FactionFrame");
        var title = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, 10f), TextAnchor.MiddleCenter, 0);
        Label(title, LM.Get("ideology_window_world_mix").ColorString("#7FD8EA"), PanelWidth - 16f, 7, TextAnchor.MiddleCenter);
        const float barWidth = PanelWidth - 20f;
        var bar = card.BeginHoriGroup(new Vector2(barWidth, 9f), TextAnchor.MiddleLeft, 0);
        if (world.Total <= 0)
        {
            Label(bar, LM.Get("ideology_tooltip_none").ColorString("#8FA0A8"), barWidth, 7, TextAnchor.MiddleCenter);
            return;
        }
        foreach (KeyValuePair<PartyIdeology, int> pair in world.People.Where(pair => pair.Value > 0)
                     .OrderBy(pair => PartySystem.RouteOf(pair.Key)).ThenBy(pair => pair.Key))
        {
            float width = Mathf.Max(1f, barWidth * pair.Value / world.Total);
            bool selected = pair.Key == _selected;
            AddBarSegment(bar.transform, width, selected ? 9f : 6f, Color32(pair.Key), selected);
        }
    }

    private static void AddBarSegment(Transform parent, float width, float height, Color color, bool outlined)
    {
        var segment = new GameObject("Segment", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = segment.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, height);
        Image image = segment.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (outlined)
        {
            Outline outline = segment.AddComponent<Outline>();
            outline.effectColor = new Color(0.95f, 0.76f, 0.29f);
            outline.effectDistance = new Vector2(1f, 1f);
        }
        LayoutElement element = segment.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = width;
        element.preferredHeight = element.minHeight = height;
    }

    #endregion

    #region 国家与政党

    private void AddRealmsCard()
    {
        List<(Empire empire, int supporters, int total)> realms = LiveEmpires()
            .Select(empire =>
            {
                Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
                return (empire, supporters: counts.TryGetValue(_selected, out int count) ? count : 0,
                    total: counts.Values.Sum());
            })
            .Where(item => item.supporters > 0).OrderByDescending(item => item.supporters).Take(6).ToList();
        float height = 16f + Mathf.Max(1, realms.Count) * 14f;
        var card = Card(new Vector2(PanelWidth, height), "FactionFrame");
        CardTitle(card, "ui/icons/iconKingdom", LM.Get("ideology_window_realms"));
        if (realms.Count == 0)
        {
            Note(card, LM.Get("ideology_window_no_realms"));
            return;
        }
        foreach (var item in realms)
        {
            Empire target = item.empire;
            var row = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, 13f), TextAnchor.MiddleLeft, 3);
            UIHelper.AddInsetBackground(row, new Vector2(PanelWidth - 12f, 13f));
            row.AddButtonIntoHoriLayout($"ideology_empire_{target.id}", target.GetEmpireName(),
                () => target.SelectAndInspect(), size: new Vector2(96f, 11f));
            Label(row, string.Format(LM.Get("ideology_window_realm_share"), item.supporters, item.total,
                    Percent(item.supporters, item.total),
                    $"{100f * IdeologyPopulationSystem.GetMilitaryShare(target, _selected):0.#}%")
                .ColorString("#D8D2C0"), 170f, 7, TextAnchor.MiddleLeft);
            FixedFaction governing = PartySystem.GetGovernmentParty(target);
            Label(row, (governing == null
                    ? LM.Get("ideology_window_no_government")
                    : $"{LM.Get("republic_ruling_party")} {governing.Name}").ColorString(governing == null
                    ? "#8FA0A8" : Hex(governing.Ideology)), 150f, 7, TextAnchor.MiddleRight);
        }
    }

    private void AddPartiesCard()
    {
        List<(Empire empire, FixedFaction party)> parties = LiveEmpires()
            .SelectMany(empire => PartySystem.GetParties(empire).Where(party => party.Ideology == _selected)
                .Select(party => (empire, party)))
            .OrderByDescending(item => item.party.CentralRatio).Take(8).ToList();
        float height = 16f + Mathf.Max(1, parties.Count) * 13f;
        var card = Card(new Vector2(PanelWidth, height), "FactionFrame");
        CardTitle(card, "ui/icons/iconPlot", LM.Get("ideology_window_party_list"));
        if (parties.Count == 0)
        {
            Note(card, LM.Get("ideology_window_no_parties"));
            return;
        }
        foreach ((Empire empire, FixedFaction party) in parties)
        {
            var row = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, 12f), TextAnchor.MiddleLeft, 3);
            UIHelper.AddInsetBackground(row, new Vector2(PanelWidth - 12f, 12f));
            bool governing = PartySystem.GetGovernmentParty(empire) == party;
            Label(row, (party.Name + (governing ? $" · {LM.Get("empire_faction_dominant_short")}" : ""))
                .ColorString(governing ? "#F3C34A" : Hex(_selected)), 150f, 7, TextAnchor.MiddleLeft);
            Label(row, empire.GetEmpireName().ColorString("#A8B8BE"), 110f, 7, TextAnchor.MiddleLeft);
            Label(row, string.Format(LM.Get("ideology_window_party_line"), party.Count, party.CentralRatio)
                .ColorString("#D8D2C0"), 150f, 7, TextAnchor.MiddleRight);
        }
    }

    #endregion

    #region 聚焦城市与路线树

    private void AddFocusedCity()
    {
        if (_focusedCity == null || _focusedCity.isRekt()) return;
        Dictionary<PartyIdeology, int> local = IdeologyPopulationSystem.GetCityCounts(_focusedCity);
        int total = local.Values.Sum();
        var card = Card(new Vector2(PanelWidth, 32f), "FactionFrame");
        CardTitle(card, "ui/icons/iconCity", _focusedCity.GetCityFullName());
        var bar = card.BeginHoriGroup(new Vector2(PanelWidth - 20f, 9f), TextAnchor.MiddleLeft, 0);
        if (total <= 0)
        {
            Label(bar, LM.Get("ideology_tooltip_none").ColorString("#8FA0A8"), PanelWidth - 20f, 7, TextAnchor.MiddleCenter);
            return;
        }
        foreach (KeyValuePair<PartyIdeology, int> pair in local.Where(pair => pair.Value > 0)
                     .OrderByDescending(pair => pair.Value))
            AddBarSegment(bar.transform, Mathf.Max(1f, (PanelWidth - 20f) * pair.Value / total),
                pair.Key == _selected ? 9f : 6f, Color32(pair.Key), pair.Key == _selected);
    }

    private void AddCultureTree()
    {
        Empire lead = LiveEmpires().OrderByDescending(empire =>
            IdeologyPopulationSystem.GetEmpireCounts(empire).TryGetValue(_selected, out int count) ? count : 0).FirstOrDefault();
        City referenceCity = _focusedCity != null && !_focusedCity.isRekt()
            ? _focusedCity
            : World.world?.cities?.FirstOrDefault(city => city != null && !city.isRekt() &&
                                                          IdeologyPopulationSystem.GetDominant(city) == _selected);
        string culture = lead != null ? InstitutionSystem.GetPrimaryCulture(lead)
            : referenceCity == null ? "" : CultureService.GetMainCulture(referenceCity);
        if (!CultureService.IsValidCulture(culture)) return;
        var treeSection = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(treeSection.gameObject);
        var row = treeSection.BeginHoriGroup(new Vector2(PanelWidth, 14), TextAnchor.MiddleCenter, 4);
        UIHelper.AddLayoutIcon(row.transform, UIHelper.FirstSprite("ui/icons/iconBooks"), 10f);
        Label(row, $"{culture.GetCultureTranslate()} · {LM.Get("ideology_population_tree")}".ColorString("#7FD8EA"),
            300f, 8, TextAnchor.MiddleLeft, 12f);
        row.AddButtonIntoHoriLayout("ideology_open_culture", culture.GetCultureTranslate(),
            () => CultureInfoWindow.Open(culture), size: new Vector2(80, 12));
        IReadOnlyList<InstitutionNodeView> path = InstitutionSystem.BuildCultureLineView(culture)
            .Where(view => view.Node.branch == IdeologyInstitutionPaths.Branch(_selected)).ToList();
        _graph = InstitutionGraphView.Create(treeSection.transform, new Vector2(PanelWidth, 175f));
        _graph.Rebuild(path, Array.Empty<InstitutionNodeView>(), "", _ => { });
    }

    #endregion

    #region 部件

    private AutoVertLayoutGroup Card(Vector2 size, string frame)
    {
        var card = _root.BeginVertGroup(size, pSpacing: 2, pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(6, 6, 4, 4));
        card.transform.AddStretchBackground(frame, size);
        _content.Add(card.gameObject);
        return card;
    }

    private static void CardTitle(AutoVertLayoutGroup card, string icon, string text)
    {
        var row = card.BeginHoriGroup(new Vector2(PanelWidth - 12f, 12f), TextAnchor.MiddleLeft, 3);
        UIHelper.AddLayoutIcon(row.transform, UIHelper.FirstSprite(icon), 10f);
        Label(row, text.ColorString("#7FD8EA"), PanelWidth - 30f, 8, TextAnchor.MiddleLeft, 12f);
    }

    private static void Note(AutoVertLayoutGroup card, string text)
    {
        var note = card.AddTextIntoVertLayout(text.ColorString("#8FA0A8"), true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth - 16f, 11f));
        note.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
    }

    private static SimpleText Label(AutoHoriLayoutGroup row, string text, float width, int fontSize,
        TextAnchor anchor, float height = ChipHeight)
    {
        SimpleText label = row.AddTextIntoHoriLayout(text, true, anchor, new Vector2(width, height));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        return label;
    }

    private static List<Empire> LiveEmpires() =>
        (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
        .Where(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt()).ToList();

    private static string Percent(int part, int total) => total <= 0 ? "0%" : $"{100f * part / total:0.#}%";

    private static Color Color32(PartyIdeology ideology)
    {
        ColorAsset color = EmpireCraftNamePlateLibrary.GetIdeologyColorAsset(ideology);
        return color == null ? new Color(0.5f, 0.85f, 0.92f) : color.getColorBanner();
    }

    private static string Hex(PartyIdeology ideology) => "#" + ColorUtility.ToHtmlStringRGB(Color32(ideology));

    #endregion
}
