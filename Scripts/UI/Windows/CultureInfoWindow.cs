using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
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

// 模组文化窗口：点击文化图层（铭牌、地块）或任何原版文化链接都会打开这里（见 CultureWindowRedirectPatch）。
//   概况  → 文化名、所属科技线、文明等级与排名、制度决定的政体
//   分布  → 城市、人口、王国、帝国（帝国可点击跳转）
//   制度  → 立宪所需的制度基础是否具备、已掌握/吸收的制度数
//   科技树 → 该文化整条科技线的掌握情况（不针对某个帝国，所以不显示支持/反对）
public class CultureInfoWindow : AbstractWideWindow<CultureInfoWindow>
{
    private const float PanelWidth = 440f;
    private const float GraphHeight = 220f;

    private static string _pendingCulture = "";

    private readonly List<GameObject> _content = new();
    private AutoVertLayoutGroup _root;
    private InstitutionGraphView _graph;
    private SimpleText _detailText;
    private IReadOnlyList<InstitutionNodeView> _lineViews = Array.Empty<InstitutionNodeView>();
    private IReadOnlyList<InstitutionNodeView> _foreignViews = Array.Empty<InstitutionNodeView>();
    private string _culture = "";
    private string _selectedId = "";

    public static void Open(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        _pendingCulture = culture;
        ScrollWindow.showWindow(nameof(CultureInfoWindow));
    }

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 2f, new RectOffset(3, 3, 3, 3));
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        if (!string.Equals(_culture, _pendingCulture, StringComparison.Ordinal)) _selectedId = "";
        _culture = _pendingCulture;
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
        _detailText = null;
    }

    private void Rebuild()
    {
        Clear();
        if (!CultureService.IsValidCulture(_culture))
        {
            var empty = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
            _content.Add(empty.gameObject);
            empty.AddTextIntoVertLayout(LM.Get("label_none"), true, TextAnchor.MiddleCenter,
                new Vector2(PanelWidth, 12));
            return;
        }
        _lineViews = InstitutionSystem.BuildCultureLineView(_culture);
        _foreignViews = InstitutionSystem.BuildCultureForeignView(_culture);
        if (string.IsNullOrEmpty(_selectedId))
            _selectedId = _lineViews.FirstOrDefault(view => view.Status == InstitutionNodeStatus.Reforming)?.Node.id
                          ?? _lineViews.FirstOrDefault(view => view.Status == InstitutionNodeStatus.Available)?.Node.id
                          ?? _lineViews.FirstOrDefault()?.Node.id ?? "";
        AddOverview();
        AddDistribution();
        AddInstitutionSummary();
        AddGraph();
    }

    private AutoVertLayoutGroup BeginSection(float height)
    {
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));
        _content.Add(panel.gameObject);
        return panel;
    }

    private void AddLine(AutoVertLayoutGroup panel, string text, float height = 11f) =>
        panel.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter, new Vector2(PanelWidth - 8f, height));

    // ── 概况 ──
    private void AddOverview()
    {
        InstitutionCultureRanking ranking = InstitutionSystem.GetCultureRanking(_culture);
        List<InstitutionCultureRanking> rankings = InstitutionSystem.GetCultureRankings().ToList();
        int rank = rankings.FindIndex(item => item.Culture == _culture) + 1;
        string colorHex = "#" + ColorUtility.ToHtmlStringRGB(_culture.GetCultureColor());
        string regime = InstitutionSystem.TryResolveCultureRegime(_culture, out RegimeType regimeType)
            ? CompositeEmpireService.GetRegimeName(regimeType)
            : LM.Get("label_none");

        var panel = BeginSection(48f);
        AddLine(panel, $"{_culture.GetCultureTranslate().ColorString(colorHex)}  ·  " +
                       $"{LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(ranking.Line)).ColorString("#7FD8EA")}" +
                       $"{LM.Get("institution_line_label")}", 12f);
        AddLine(panel, string.Format(LM.Get("culture_window_level_line"), ranking.LevelName.ColorString("#F3C34A"),
            ranking.Advancement, rank > 0 ? rank : rankings.Count + 1, Math.Max(1, rankings.Count)));
        AddLine(panel, string.Format(LM.Get("culture_window_regime_line"), regime.ColorString("#C9A7E8")));

        var buttons = panel.BeginHoriGroup(new Vector2(PanelWidth - 8f, 13), TextAnchor.MiddleCenter, 4);
        buttons.AddButtonIntoHoriLayout("culture_window_open_vanilla", LM.Get("culture_window_open_vanilla"),
            () => CultureWindowRedirectPatch.OpenVanilla(_culture), size: new Vector2(70, 11));
        buttons.AddButtonIntoHoriLayout("institution_graph_reset", LM.Get("institution_graph_reset"),
            () => _graph?.ResetView(), size: new Vector2(40, 11));
        panel.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(PanelWidth, 48f));
    }

    // ── 分布 ──
    private void AddDistribution()
    {
        int cities = 0;
        float people = 0f;
        float worldPeople = 0f;
        foreach (City city in World.world?.cities ?? Enumerable.Empty<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            int population = city.getPopulationPeople();
            worldPeople += population;
            people += population * CultureService.GetCultureShare(city, _culture) / 100f;
            if (string.Equals(CultureService.GetMainCulture(city, initialize: false), _culture, StringComparison.Ordinal))
                cities++;
        }
        List<Kingdom> kingdoms = (World.world?.kingdoms ?? Enumerable.Empty<Kingdom>())
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.cities?.Count > 0 &&
                              string.Equals(CultureService.GetRealmCulture(kingdom), _culture, StringComparison.Ordinal))
            .ToList();
        List<Empire> empires = (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
            .Where(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt() &&
                             string.Equals(CultureService.GetEmpireDefaultCulture(empire), _culture, StringComparison.Ordinal))
            .ToList();

        float height = 36f + (empires.Count > 0 ? 16f : 0f);
        var panel = BeginSection(height);
        AddLine(panel, LM.Get("culture_window_distribution").ColorString("#7FD8EA"), 12f);
        float share = worldPeople <= 0f ? 0f : people / worldPeople * 100f;
        AddLine(panel, string.Format(LM.Get("culture_window_distribution_line"), cities, (int)people, share,
            kingdoms.Count, empires.Count));
        if (empires.Count > 0)
        {
            var row = panel.BeginHoriGroup(new Vector2(PanelWidth - 8f, 14), TextAnchor.MiddleCenter, 3);
            foreach (Empire empire in empires.OrderByDescending(item => item.CountPopulation()).Take(5))
            {
                Empire target = empire;
                row.AddButtonIntoHoriLayout($"culture_window_empire_{empire.id}", empire.GetEmpireName(),
                    () => target.SelectAndInspect(), size: new Vector2(80, 12));
            }
        }
        panel.transform.AddStretchBackground("FactionFrame", new Vector2(PanelWidth, height));
    }

    // ── 制度概况 ──
    private void AddInstitutionSummary()
    {
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(_culture);
        int enacted = _lineViews.Count(view => view.Status is InstitutionNodeStatus.Enacted);
        int absorbed = state?.absorbed_node_ids.Count ?? 0;
        string line = InstitutionSystem.GetCultureLine(_culture);
        List<string> features = InstitutionDefinitionRegistry.GetConstitutionRequiredFeatures(line)
            .Select(feature =>
            {
                string key = $"institution_feature_{feature}";
                string name = LM.Get(key);
                if (string.IsNullOrWhiteSpace(name) || name == key) name = feature;
                return InstitutionSystem.HasFeature(_culture, feature)
                    ? $"✔{name}".ColorString("#65D66E")
                    : $"✘{name}".ColorString("#D98C8C");
            }).ToList();

        var panel = BeginSection(36f);
        AddLine(panel, string.Format(LM.Get("culture_window_institution_line"), enacted, _lineViews.Count, absorbed));
        AddLine(panel, $"{LM.Get("culture_window_constitution_basis")}: {string.Join("  ", features)}");
        panel.transform.AddStretchBackground("FactionFrame", new Vector2(PanelWidth, 36f));
    }

    // ── 科技树 ──
    private void AddGraph()
    {
        var section = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(section.gameObject);
        section.AddTextIntoVertLayout(LM.Get("culture_window_tree_hint").ColorString("#B8C6CC"), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
        _graph = InstitutionGraphView.Create(section.transform, new Vector2(PanelWidth, GraphHeight));
        RefreshGraph();
        // 上帝模式：强制点亮选中节点（连同前置）/ 点亮整条线
        var actions = section.BeginHoriGroup(new Vector2(PanelWidth, 14), TextAnchor.MiddleCenter, 4);
        actions.AddButtonIntoHoriLayout("institution_force_enact", LM.Get("institution_force_enact"),
            () => ForceEnact(false), size: new Vector2(110, 12));
        actions.AddButtonIntoHoriLayout("institution_force_enact_all", LM.Get("institution_force_enact_all"),
            () => ForceEnact(true), size: new Vector2(110, 12));
        _detailText = section.AddTextIntoVertLayout("", true, TextAnchor.UpperCenter, new Vector2(PanelWidth, 40),
            mode: HorizontalWrapMode.Wrap);
        RefreshDetail();
    }

    private void ForceEnact(bool all)
    {
        if (!CultureService.IsValidCulture(_culture)) return;
        if (all) InstitutionSystem.ForceEnactAll(_culture);
        else if (!string.IsNullOrEmpty(_selectedId)) InstitutionSystem.ForceEnactNode(_culture, _selectedId);
        Rebuild();
    }

    private void RefreshGraph() =>
        _graph?.Rebuild(_lineViews, _foreignViews, _selectedId, OnNodeSelected, BuildTooltip, BuildCard);

    private void OnNodeSelected(string nodeId)
    {
        _selectedId = nodeId ?? "";
        RefreshGraph();
        RefreshDetail();
    }

    private InstitutionNodeView FindView(string nodeId) =>
        _lineViews.FirstOrDefault(view => view.Node.id == nodeId) ??
        _foreignViews.FirstOrDefault(view => view.Node.id == nodeId);

    private void RefreshDetail()
    {
        if (_detailText == null) return;
        InstitutionNodeView view = FindView(_selectedId);
        if (view == null)
        {
            _detailText.text.text = "";
            return;
        }
        (string title, string body) = BuildTooltip(view);
        _detailText.text.text = title + "\n" + body;
    }

    private static InstitutionGraphView.NodeCardInfo BuildCard(InstitutionNodeView view)
    {
        string status = GetStatusName(view.Status).ColorString(GetStatusHex(view.Status));
        string info = view.Status is InstitutionNodeStatus.ForeignLocked or InstitutionNodeStatus.ForeignContacting
            or InstitutionNodeStatus.ForeignReady
            ? $"{LM.Get("institution_exposure")} {view.Exposure:0}/{view.RequiredExposure:0}"
            : InstitutionDefinitionRegistry.TryGetNodeRegime(view.Node, out RegimeType regime)
                ? $"→ {CompositeEmpireService.GetRegimeName(regime)}".ColorString("#C9A7E8")
                : GetBranchName(view.Node.branch);
        return new InstitutionGraphView.NodeCardInfo(status, info, -1f);
    }

    private static (string title, string body) BuildTooltip(InstitutionNodeView view)
    {
        string title = $"{InstitutionSystem.GetNodeName(view.Node).ColorString("#F3C34A")}  " +
                       $"{LM.Get("institution_advancement")} {view.Node.advancement}  ·  " +
                       $"{GetBranchName(view.Node.branch)}  ·  " +
                       $"{GetStatusName(view.Status).ColorString(GetStatusHex(view.Status))}";
        var lines = new List<string> { LM.Get(view.Node.description_key) };
        if (!string.IsNullOrEmpty(view.Reason)) lines.Add(LM.Get(view.Reason).ColorString("#D98C8C"));
        if (view.Status is InstitutionNodeStatus.ForeignLocked or InstitutionNodeStatus.ForeignContacting
            or InstitutionNodeStatus.ForeignReady)
            lines.Add($"{LM.Get("institution_absorbed_from").Replace("{0}", LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(view.Node.line)))}  ·  " +
                      $"{LM.Get("institution_contact_years")} {view.ContactYears}/{view.RequiredContactYears}");
        if (view.Node.politics.support_classes.Count > 0)
            lines.Add(string.Format(LM.Get("institution_long_term_benefits"),
                string.Join("、", view.Node.politics.support_classes.Keys.Select(item => LM.Get($"class_{item}"))))
                .ColorString("#65D66E"));
        if (view.Node.politics.oppose_classes.Count > 0)
            lines.Add(string.Format(LM.Get("institution_long_term_harms"),
                string.Join("、", view.Node.politics.oppose_classes.Keys.Select(item => LM.Get($"class_{item}"))))
                .ColorString("#D98C8C"));
        return (title, string.Join("\n", lines));
    }

    private static string GetBranchName(string branch)
    {
        string key = $"institution_branch_{branch}";
        string value = LM.Get(key);
        return string.IsNullOrWhiteSpace(value) || value == key ? branch : value;
    }

    private static string GetStatusName(InstitutionNodeStatus status)
    {
        return status switch
        {
            InstitutionNodeStatus.Enacted => LM.Get("institution_status_enacted"),
            InstitutionNodeStatus.Absorbed => LM.Get("institution_status_absorbed"),
            InstitutionNodeStatus.Reforming => LM.Get("institution_status_reforming"),
            InstitutionNodeStatus.Available => LM.Get("culture_window_status_researchable"),
            InstitutionNodeStatus.ForeignLocked => LM.Get("institution_status_foreign_locked"),
            InstitutionNodeStatus.ForeignContacting => LM.Get("institution_status_foreign_contacting"),
            InstitutionNodeStatus.ForeignReady => LM.Get("institution_status_foreign_ready"),
            _ => LM.Get("institution_status_locked")
        };
    }

    private static string GetStatusHex(InstitutionNodeStatus status)
    {
        return status switch
        {
            InstitutionNodeStatus.Enacted => "#65D66E",
            InstitutionNodeStatus.Absorbed => "#F3C34A",
            InstitutionNodeStatus.Reforming => "#65D6C4",
            InstitutionNodeStatus.Available => "#F3C34A",
            InstitutionNodeStatus.ForeignReady => "#C9A7E8",
            InstitutionNodeStatus.ForeignContacting => "#A79BC4",
            _ => "#B8B8B8"
        };
    }
}
