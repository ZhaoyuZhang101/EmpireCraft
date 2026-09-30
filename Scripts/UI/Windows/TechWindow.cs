using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 文明科技窗口(按文化)：
//   概况 → 文化、所处时代、研究点、当前研究和进度
//   材料 → 已发现/未发现的材料，以及怎样发现
//   树   → 材料一列 + 各分支，连线表示需要什么
//   详情 → 选中节点的费用、前置、解锁内容(没装对应模组的会标出来)，可设为研究目标
public class TechWindow : AbstractWideWindow<TechWindow>
{
    private const float PanelWidth = 440f;
    private const float GraphHeight = 230f;
    private const int MaterialsPerRow = 7;

    private static string _pendingCulture = "";

    private readonly List<GameObject> _content = new();
    private AutoVertLayoutGroup _root;
    private TechGraphView _graph;
    private SimpleText _detailText;
    private string _culture = "";
    private string _selectedId = "";

    public static void Open(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        _pendingCulture = culture;
        ScrollWindow.showWindow(nameof(TechWindow));
    }

    public static void Open(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return;
        Open(CultureService.GetRealmCulture(kingdom));
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
        TechnologySystem.EnsureLoaded();
        if (!CultureService.IsValidCulture(_culture))
        {
            var empty = BeginSection(20f);
            AddLine(empty, LM.Get("label_none"));
            return;
        }
        if (string.IsNullOrEmpty(_selectedId))
        {
            CultureTechState state = TechnologySystem.GetState(_culture);
            _selectedId = !string.IsNullOrEmpty(state.current_tech)
                ? state.current_tech
                : TechnologySystem.Techs.FirstOrDefault(tech =>
                    TechnologySystem.GetTechStatus(_culture, tech) == TechNodeStatus.Available)?.id ?? "";
        }
        AddOverview();
        AddMaterials();
        AddLegend();
        AddGraph();
        AddDetail();
        AddCompatibility();
    }

    private AutoVertLayoutGroup BeginSection(float height, string frame = null)
    {
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(4, 4, 3, 3));
        _content.Add(panel.gameObject);
        if (frame != null) panel.transform.AddStretchBackground(frame, new Vector2(PanelWidth, height));
        return panel;
    }

    private static SimpleText AddLine(AutoVertLayoutGroup panel, string text, float height = 11f, int fontSize = 7)
    {
        SimpleText line = panel.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth - 10f, height));
        line.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        return line;
    }

    // ── 概况 ──
    private void AddOverview()
    {
        CultureTechState state = TechnologySystem.GetState(_culture);
        int techCount = TechnologySystem.Techs.Count();
        int materialCount = TechnologySystem.Materials.Count();
        string colorHex = "#" + ColorUtility.ToHtmlStringRGB(_culture.GetCultureColor());
        float bonus = TechnologySystem.GetResearchBonus(_culture);

        var panel = BeginSection(76f, "FactionFrame_dominate");
        var tags = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 13), TextAnchor.MiddleCenter, 6);
        AddTag(tags, TechGraphView.Icon("ui/icons/iconCulture"), _culture.GetCultureTranslate(), colorHex);
        AddTag(tags, TechGraphView.Icon("ui/icons/iconKnowledge"), TechnologySystem.GetEraName(_culture), "#F3C34A");
        AddTag(tags, TechGraphView.Icon("ui/icons/iconBooks"),
            string.Format(LM.Get("tech_points_per_year"), state.last_yearly_points.ToString("0.#"),
                (bonus * 100f).ToString("0")), "#7FD8EA");

        AddLine(panel, string.Format(LM.Get("tech_overview_counts"),
            state.researched_techs.Count, techCount, state.discovered_materials.Count, materialCount));

        // 科技点储备与来源明细；年产太低就是停滞
        string Income(string key) => (state.last_income != null && state.last_income.TryGetValue(key, out float v)
            ? v : 0f).ToString("0.#");
        string bankLine = string.Format(LM.Get("tech_bank_line"), state.research_bank.ToString("0"),
            Income("books_written"), Income("books_read"), Income("war"), Income("schools"), Income("basis"));
        if (TechnologySystem.IsStagnant(_culture))
            bankLine += "  " + LM.Get("tech_stagnant").ColorString("#E05A4F");
        bankLine += "  " + string.Format(LM.Get("tech_funding_line"),
            TechnologySystem.Config.research.gold_per_point.ToString("0.#"), TechnologySystem.GetCultureTreasury(_culture));
        if (state.last_unfunded > 0.5f)
            bankLine += "  " + LM.Get("tech_unfunded").ColorString("#E9A85B");
        AddLine(panel, bankLine.ColorString("#B8C6CC"), 11f, 6);

        var researchRow = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 11), TextAnchor.MiddleCenter, 4);
        if (TechnologySystem.TryGetTech(state.current_tech, out TechNodeConfig current))
        {
            float cost = TechnologySystem.GetCost(_culture, current);
            float done = TechnologySystem.GetProgress(_culture, current.id);
            string eta = state.last_yearly_points > 0.01f
                ? string.Format(LM.Get("tech_eta_years"),
                    Mathf.CeilToInt(Math.Max(0f, cost - done) / state.last_yearly_points))
                : LM.Get("tech_eta_unknown");
            string target = !string.IsNullOrEmpty(state.player_target) && state.player_target != current.id
                ? "  " + string.Format(LM.Get("tech_player_target_line"),
                    TechnologySystem.GetTechName(state.player_target)).ColorString("#C9A7E8")
                : "";
            AddRowLabel(researchRow, string.Format(LM.Get("tech_current_research"),
                TechnologySystem.GetTechName(current.id).ColorString("#9EF2FF")) + target, 230f, TextAnchor.MiddleRight);
            AddProgressBar(researchRow.transform, new Vector2(100f, 5f), done / Math.Max(1f, cost),
                new Color(0.40f, 0.84f, 0.77f));
            AddRowLabel(researchRow, $"{done:0}/{cost:0} {eta}", 90f, TextAnchor.MiddleLeft);
        }
        else
        {
            AddRowLabel(researchRow, LM.Get("tech_no_research"), PanelWidth - 20f, TextAnchor.MiddleCenter);
        }

        var buttons = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 13), TextAnchor.MiddleCenter, 4);
        buttons.AddButtonIntoHoriLayout("institution_graph_reset", LM.Get("institution_graph_reset"),
            () => _graph?.ResetView(), size: new Vector2(50, 11));
        buttons.AddButtonIntoHoriLayout("tech_auto_research",
            LM.Get(state.auto_research ? "tech_auto_research_on" : "tech_auto_research_off"),
            () =>
            {
                TechnologySystem.SetAutoResearch(_culture, !state.auto_research);
                Rebuild();
            }, size: new Vector2(64, 11));
        buttons.AddButtonIntoHoriLayout("tech_clear_target", LM.Get("tech_clear_target"),
            () =>
            {
                TechnologySystem.SetPlayerTarget(_culture, "");
                Rebuild();
            }, size: new Vector2(70, 11));
        buttons.AddButtonIntoHoriLayout("tech_modernize", LM.Get("tech_modernize"),
            () =>
            {
                int count = TechnologySystem.ModernizeNow(_culture);
                WorldTip.showNow(string.Format(LM.Get("tech_modernize_result"), count), false, "top", 3f);
                Rebuild();
            }, size: new Vector2(60, 11));
        buttons.AddButtonIntoHoriLayout("tech_force_all", LM.Get("tech_force_all"),
            () =>
            {
                TechnologySystem.ForceResearchAll(_culture);
                Rebuild();
            }, size: new Vector2(70, 11));
        buttons.AddButtonIntoHoriLayout("tech_reset_culture", LM.Get("tech_reset_culture"),
            () =>
            {
                TechnologySystem.ResetCulture(_culture);
                _selectedId = "";
                Rebuild();
            }, size: new Vector2(60, 11));
    }

    // ── 材料 ──
    private void AddMaterials()
    {
        List<TechMaterialConfig> materials = TechnologySystem.Materials.OrderBy(m => m.tier).ToList();
        int rows = Math.Max(1, (materials.Count + MaterialsPerRow - 1) / MaterialsPerRow);
        var panel = BeginSection(14f + rows * 14f, "FactionFrame");
        AddLine(panel, LM.Get("tech_materials_title").ColorString("#7FD8EA"), 10f);
        float chipWidth = (PanelWidth - 12f) / MaterialsPerRow - 3f;
        for (int row = 0; row < rows; row++)
        {
            var line = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 12), TextAnchor.MiddleCenter, 3);
            foreach (TechMaterialConfig material in materials.Skip(row * MaterialsPerRow).Take(MaterialsPerRow))
            {
                bool found = TechnologySystem.HasMaterial(_culture, material.id);
                var chip = line.BeginHoriGroup(new Vector2(chipWidth, 12), TextAnchor.MiddleLeft, 2);
                UIHelper.AddInsetBackground(chip, new Vector2(chipWidth, 12));
                UIHelper.AddLayoutIcon(chip.transform, GetMaterialIcon(material), 8f);
                string name = TechnologySystem.GetMaterialName(material.id);
                AddRowLabel(chip, found ? name.ColorString("#FFD273") : name.ColorString("#8A8F99"), chipWidth - 12f,
                    TextAnchor.MiddleLeft, 6);
                string body = (found ? LM.Get("tech_status_discovered") : LM.Get("tech_status_undiscovered")) + "\n" +
                              TechnologySystem.DescribeMaterialCondition(material);
                UIHelper.AttachTextTooltip(chip.gameObject, $"tech_mat_chip_{material.id}", name, body);
                string id = TechGraphView.MaterialPrefix + material.id;
                // 按钮要有能接收射线的图形才点得动；没有就补一张透明底图
                Image hit = chip.gameObject.GetComponent<Image>();
                if (hit == null)
                {
                    hit = chip.gameObject.AddComponent<Image>();
                    hit.color = new Color(0f, 0f, 0f, 0f);
                }
                hit.raycastTarget = true;
                Button button = chip.gameObject.GetComponent<Button>() ?? chip.gameObject.AddComponent<Button>();
                button.onClick.AddListener(() => OnNodeSelected(id));
            }
        }
    }

    private static Sprite GetMaterialIcon(TechMaterialConfig material)
    {
        foreach (TechResourceRequirement requirement in material.resources)
        {
            Sprite sprite = AssetManager.resources.get(requirement.id)?.getSpriteIcon();
            if (sprite != null) return sprite;
        }
        return TechGraphView.Icon("ui/icons/iconResStone");
    }

    // ── 图例 ──
    private void AddLegend()
    {
        var panel = BeginSection(14f);
        var row = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 11), TextAnchor.MiddleCenter, 6);
        foreach ((TechNodeStatus status, string key) in new[]
                 {
                     (TechNodeStatus.Researched, "tech_status_researched"),
                     (TechNodeStatus.Researching, "tech_status_researching"),
                     (TechNodeStatus.Available, "tech_status_available"),
                     (TechNodeStatus.Locked, "tech_status_locked"),
                     (TechNodeStatus.MaterialDiscovered, "tech_status_discovered")
                 })
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(TechGraphView.GetStatusColor(status));
            AddRowLabel(row, "■ ".ColorString(hex) + LM.Get(key), 66f, TextAnchor.MiddleCenter, 6);
        }
    }

    // ── 树 ──
    private void AddGraph()
    {
        var section = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(section.gameObject);
        _graph = TechGraphView.Create(section.transform, new Vector2(PanelWidth, GraphHeight));
        RefreshGraph();
    }

    private void RefreshGraph()
    {
        if (_graph == null) return;
        var nodes = new List<TechGraphView.NodeInfo>();
        CultureTechState state = TechnologySystem.GetState(_culture);
        foreach (TechMaterialConfig material in TechnologySystem.Materials)
        {
            TechNodeStatus status = TechnologySystem.GetMaterialStatus(_culture, material);
            string name = TechnologySystem.GetMaterialName(material.id);
            string condition = TechnologySystem.DescribeMaterialCondition(material);
            nodes.Add(new TechGraphView.NodeInfo(TechGraphView.MaterialPrefix + material.id,
                TechnologySystem.MaterialBranch, material.tier, name,
                GetStatusName(status).ColorString(GetStatusHex(status)), LM.Get("tech_material_label"), status, -1f,
                GetMaterialIcon(material), material.requires_techs.ToList(), name,
                condition + "\n" + LM.Get($"tech_mat_{material.id}_desc")));
        }
        foreach (TechNodeConfig tech in TechnologySystem.Techs)
        {
            TechNodeStatus status = TechnologySystem.GetTechStatus(_culture, tech);
            float cost = TechnologySystem.GetCost(_culture, tech);
            // 研究中的，以及靠加速事件预存了进度的，都画进度条
            float stored = TechnologySystem.GetProgress(_culture, tech.id);
            float progress = status != TechNodeStatus.Researched && (status == TechNodeStatus.Researching || stored > 0f)
                ? stored / Math.Max(1f, cost)
                : -1f;
            string info = status == TechNodeStatus.Researched
                ? TechnologySystem.GetBranchName(tech.branch)
                : $"{LM.Get("tech_cost")} {cost:0}";
            if (state.boosted.Contains(tech.id) && status != TechNodeStatus.Researched)
                info = LM.Get("tech_boost_mark").ColorString("#FFD273") + info;
            if (state.player_target == tech.id) info = "★ " + info;
            var requires = tech.requires_techs.Concat(tech.requires_materials.Select(m => TechGraphView.MaterialPrefix + m))
                .ToList();
            (string title, string body) = BuildTechTooltip(tech, status);
            nodes.Add(new TechGraphView.NodeInfo(tech.id, tech.branch, tech.tier, TechnologySystem.GetTechName(tech.id),
                GetStatusName(status).ColorString(GetStatusHex(status)), info, status, progress,
                TechGraphView.GetBranchIcon(tech.branch), requires, title, body));
        }
        List<string> lanes = new List<string> { TechnologySystem.MaterialBranch };
        lanes.AddRange(TechnologySystem.Config.branches.Where(b => b != TechnologySystem.MaterialBranch));
        _graph.Rebuild(nodes, lanes, _selectedId, OnNodeSelected);
    }

    private void OnNodeSelected(string id)
    {
        _selectedId = id ?? "";
        _graph?.SetSelected(_selectedId);
        RefreshDetail();
    }

    // ── 详情 ──
    private void AddDetail()
    {
        var panel = BeginSection(96f, "FactionFrame");
        _detailText = panel.AddTextIntoVertLayout("", true, TextAnchor.UpperCenter, new Vector2(PanelWidth - 10f, 78),
            mode: HorizontalWrapMode.Wrap);
        _detailText.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        var buttons = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 13), TextAnchor.MiddleCenter, 4);
        buttons.AddButtonIntoHoriLayout("tech_invest", LM.Get("tech_invest"), () =>
        {
            if (!TechnologySystem.Invest(_culture, _selectedId))
                WorldTip.showNow(LM.Get("tech_invest_failed"), false, "top", 3f);
            Rebuild();
        }, size: new Vector2(90, 11));
        buttons.AddButtonIntoHoriLayout("tech_set_target", LM.Get("tech_set_target"), () =>
        {
            if (TechnologySystem.TryGetTech(_selectedId, out _)) TechnologySystem.SetPlayerTarget(_culture, _selectedId);
            Rebuild();
        }, size: new Vector2(90, 11));
        buttons.AddButtonIntoHoriLayout("tech_force_research", LM.Get("tech_force_research"), () =>
        {
            if (_selectedId.StartsWith(TechGraphView.MaterialPrefix, StringComparison.Ordinal))
                TechnologySystem.ForceDiscover(_culture, _selectedId.Substring(TechGraphView.MaterialPrefix.Length));
            else TechnologySystem.ForceResearch(_culture, _selectedId);
            Rebuild();
        }, size: new Vector2(90, 11));
        // 上帝模式：回退选中的技术(连同后续技术)或遗忘选中的材料
        var rollback = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 13), TextAnchor.MiddleCenter, 4);
        rollback.AddButtonIntoHoriLayout("tech_force_revoke", LM.Get("tech_force_revoke"), () =>
        {
            int count = _selectedId.StartsWith(TechGraphView.MaterialPrefix, StringComparison.Ordinal)
                ? TechnologySystem.ForceForget(_culture, _selectedId.Substring(TechGraphView.MaterialPrefix.Length))
                : TechnologySystem.ForceRevoke(_culture, _selectedId);
            if (count <= 0) WorldTip.showNow(LM.Get("tech_force_revoke_nothing"), false, "top", 3f);
            Rebuild();
        }, size: new Vector2(186, 11));
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        if (_detailText == null) return;
        if (_selectedId.StartsWith(TechGraphView.MaterialPrefix, StringComparison.Ordinal))
        {
            string id = _selectedId.Substring(TechGraphView.MaterialPrefix.Length);
            if (!TechnologySystem.TryGetMaterial(id, out TechMaterialConfig material))
            {
                _detailText.text.text = "";
                return;
            }
            TechNodeStatus status = TechnologySystem.GetMaterialStatus(_culture, material);
            var users = TechnologySystem.Techs.Where(t => t.requires_materials.Contains(id))
                .Select(t => TechnologySystem.GetTechName(t.id)).ToList();
            _detailText.text.text =
                $"{TechnologySystem.GetMaterialName(id).ColorString("#FFD273")}  ·  {LM.Get("tech_material_label")}  ·  " +
                $"{GetStatusName(status).ColorString(GetStatusHex(status))}\n" +
                $"{LM.Get($"tech_mat_{id}_desc")}\n{TechnologySystem.DescribeMaterialCondition(material)}\n" +
                (users.Count > 0 ? string.Format(LM.Get("tech_material_used_by"), string.Join("、", users)) : "");
            return;
        }
        if (!TechnologySystem.TryGetTech(_selectedId, out TechNodeConfig tech))
        {
            _detailText.text.text = LM.Get("tech_select_hint");
            return;
        }
        (string title, string body) = BuildTechTooltip(tech, TechnologySystem.GetTechStatus(_culture, tech));
        _detailText.text.text = title + "\n" + body;
    }

    private (string title, string body) BuildTechTooltip(TechNodeConfig tech, TechNodeStatus status)
    {
        string title = $"{TechnologySystem.GetTechName(tech.id).ColorString("#F3C34A")}  ·  " +
                       $"{TechnologySystem.GetBranchName(tech.branch)}  ·  " +
                       $"{GetStatusName(status).ColorString(GetStatusHex(status))}";
        var lines = new List<string> { TechnologySystem.GetTechDescription(tech.id) };
        float cost = TechnologySystem.GetCost(_culture, tech);
        string costLine = $"{LM.Get("tech_cost")} {cost:0}";
        float stored = TechnologySystem.GetProgress(_culture, tech.id);
        if (stored > 0f && status != TechNodeStatus.Researched) costLine += $"  ({LM.Get("tech_progress_stored")} {stored:0})";
        float size = TechnologySystem.GetSizeCostFactor(_culture) - 1f;
        if (size > 0.005f)
            costLine += "  " + string.Format(LM.Get("tech_cost_size"), (size * 100f).ToString("0")).ColorString("#D98C8C");
        int knowing = TechnologySystem.CountContactsKnowing(_culture, tech.id);
        if (knowing > 0)
            costLine += "  " + string.Format(LM.Get("tech_cost_diffusion"), knowing,
                (TechnologySystem.GetDiffusionDiscount(_culture, tech.id) * 100f).ToString("0")).ColorString("#9EF2FF");
        lines.Add(costLine);
        List<(LandmarkBookConfig book, float fraction)> bookBoosts = LandmarkBookSystem.BooksForTech(tech.id).ToList();
        if (bookBoosts.Count > 0 && status != TechNodeStatus.Researched)
        {
            List<string> known = TechnologySystem.GetState(_culture).known_books ?? new List<string>();
            lines.Add(LM.Get("tech_book_boosts") + string.Join("  ", bookBoosts.Select(pair =>
                Mark(known.Contains(pair.book.id)) + "《" + LandmarkBookSystem.GetTitle(pair.book.id, _culture) + "》+" +
                (pair.fraction * 100f).ToString("0") + "%")));
        }
        if (tech.boosts.Count > 0 && status != TechNodeStatus.Researched)
        {
            bool fired = TechnologySystem.GetState(_culture).boosted.Contains(tech.id);
            string boosts = string.Join(LM.Get("tech_or"), tech.boosts.Select(TechnologySystem.DescribeBoost));
            lines.Add((fired ? LM.Get("tech_boost_done") : LM.Get("tech_boost_pending")) + boosts);
        }
        var requires = tech.requires_techs
            .Select(id => Mark(TechnologySystem.HasTech(_culture, id)) + TechnologySystem.GetTechName(id))
            .Concat(tech.requires_materials.Select(id =>
                Mark(TechnologySystem.HasMaterial(_culture, id)) + TechnologySystem.GetMaterialName(id)))
            .ToList();
        // 文化制度的约束：文明等级、需要推行的制度
        int needLevel = TechnologySystem.GetRequiredCultureLevel(tech.tier);
        if (TechnologySystem.Config.culture_level_max_tier.Count > 0)
            requires.Add(Mark(tech.tier <= TechnologySystem.GetMaxTierForCulture(_culture)) +
                         string.Format(LM.Get("tech_requires_culture_level"), needLevel));
        requires.AddRange(tech.requires_institutions.Select(requirement =>
            Mark(TechnologySystem.IsInstitutionRequirementMet(_culture, requirement)) +
            TechnologySystem.DescribeFeature(new TechInstitutionLink
                { feature = requirement.feature, min_value = requirement.min_value })));
        if (requires.Count > 0) lines.Add(LM.Get("tech_requires") + string.Join("  ", requires));
        var unlocks = TechnologySystem.DescribeUnlocks(tech)
            .Select(unlock => unlock.present ? unlock.label : (unlock.label + LM.Get("tech_not_installed")).ColorString("#8A8F99"))
            .ToList();
        foreach ((TechInstitutionLink link, bool gate, float push) in TechnologySystem.GetInstitutionEffects(tech.id))
        {
            string feature = TechnologySystem.DescribeFeature(link);
            unlocks.Add(gate
                ? string.Format(LM.Get("tech_enables_institution"), feature).ColorString("#C9A7E8")
                : string.Format(LM.Get("tech_drives_institution"), feature, (push * 100f).ToString("0"))
                    .ColorString("#C9A7E8"));
        }
        if (tech.research_bonus > 0f)
            unlocks.Add(string.Format(LM.Get("tech_research_bonus"), (tech.research_bonus * 100f).ToString("0")));
        if (unlocks.Count > 0) lines.Add(LM.Get("tech_unlocks") + string.Join("、", unlocks));
        return (title, string.Join("\n", lines));
    }

    private static string Mark(bool ok) => ok ? "✔".ColorString("#9EF29E") : "✘".ColorString("#FF8A7A");

    // ── 兼容性 ──
    private void AddCompatibility()
    {
        var panel = BeginSection(26f);
        string modern = TechnologySystem.ModernModDetected
            ? LM.Get("tech_compat_detected").ColorString("#9EF29E")
            : LM.Get("tech_compat_missing").ColorString("#8A8F99");
        string box = TechnologySystem.ModernBoxDetected
            ? LM.Get("tech_compat_detected").ColorString("#9EF29E")
            : LM.Get("tech_compat_missing").ColorString("#8A8F99");
        string war = TechnologySystem.WarBoxDetected
            ? LM.Get("tech_compat_detected").ColorString("#9EF29E")
            : LM.Get("tech_compat_missing").ColorString("#8A8F99");
        AddLine(panel, string.Format(LM.Get("tech_compat_line"), modern, box, war), 10f, 6);
        AddLine(panel, LM.Get("tech_compat_hint").ColorString("#A8B8BE"), 10f, 6);
    }

    // ── 小部件 ──
    private static void AddTag(AutoHoriLayoutGroup row, Sprite icon, string text, string hex)
    {
        var tag = row.BeginHoriGroup(new Vector2(135f, 13f), TextAnchor.MiddleCenter, 2);
        UIHelper.AddLayoutIcon(tag.transform, icon, 10f);
        AddRowLabel(tag, text.ColorString(hex), 120f, TextAnchor.MiddleLeft, 8);
    }

    private static SimpleText AddRowLabel(AutoHoriLayoutGroup row, string text, float width, TextAnchor anchor,
        int fontSize = 7)
    {
        SimpleText label = row.AddTextIntoHoriLayout(text, true, anchor, new Vector2(width, 11));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        return label;
    }

    private static void AddProgressBar(Transform parent, Vector2 size, float progress, Color fillColor)
    {
        var trackObject = new GameObject("ProgressBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var trackRect = trackObject.GetComponent<RectTransform>();
        trackRect.SetParent(parent, false);
        trackRect.sizeDelta = size;
        Image track = trackObject.GetComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.5f);
        track.raycastTarget = false;
        LayoutElement element = trackObject.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = element.minHeight = size.y;

        var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.SetParent(trackRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.GetComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;
    }

    private static string GetStatusName(TechNodeStatus status) => LM.Get(status switch
    {
        TechNodeStatus.Researched => "tech_status_researched",
        TechNodeStatus.Researching => "tech_status_researching",
        TechNodeStatus.Available => "tech_status_available",
        TechNodeStatus.MaterialDiscovered => "tech_status_discovered",
        TechNodeStatus.MaterialUndiscovered => "tech_status_undiscovered",
        _ => "tech_status_locked"
    });

    private static string GetStatusHex(TechNodeStatus status) =>
        "#" + ColorUtility.ToHtmlStringRGB(TechGraphView.GetStatusColor(status));
}
