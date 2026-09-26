using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 制度窗口。排版顺序刻意按"玩家第一次打开时的疑问顺序"来：
//   这是什么 → 顶部一句话（制度属于文化，同文化共享）
//   我现在什么水平 → 文化 / 科技线 / 当前政体 / 文明等级 + 离下一级还差多少
//   我现在该干什么 → 行动提示（在研的是哪个、还有几项可推动）
//   这些颜色什么意思 → 图例（跟树上的节点用同一套色）
//   树 → 一个分支一列，车道标题上带该分支已达等级
//   这个节点到底怎样 → 详情卡（缺什么前置、施行后政体会不会变、支持反对多少）
public class InstitutionWindow : AbstractWideWindow<InstitutionWindow>
{
    // 换成真宽窗口(AbstractWideWindow)之后不用再迁就窄边框比例，直接给够宽度让树状图
    // 一次能看到更多节点，少一些"拖拽平移"才能看全的情况。
    private const float PanelWidth = 440f;
    private const float GraphHeight = 220f;
    private static readonly Vector2 WindowSize = new(480f, 380f);

    private readonly List<GameObject> _content = new();
    private AutoVertLayoutGroup _root;
    private Empire _empire;
    private InstitutionGraphView _graph;
    private AutoVertLayoutGroup _detailPanel;
    private IReadOnlyList<InstitutionNodeView> _lineViews = Array.Empty<InstitutionNodeView>();
    private IReadOnlyList<InstitutionNodeView> _foreignViews = Array.Empty<InstitutionNodeView>();
    private string _culture = "";
    private string _selectedId = "";

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 2f, new RectOffset(3, 3, 3, 3));
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        _selectedId = "";
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
        _detailPanel = null;
    }

    private bool IsUsable() =>
        _empire?.data != null && !_empire.isRekt() && _empire.CoreKingdom != null &&
        !_empire.CoreKingdom.isRekt() && _empire.CoreKingdom.GetRegime() != null;

    private void Rebuild()
    {
        Clear();
        // 帝国已归档 / 核心王国丢了的话，给一行说明而不是让窗口空着 —— 空内容会被
        // AutoLayoutWindow 顶成原版的"未找到/开发中"占位页，看起来像功能坏了。
        if (!IsUsable())
        {
            var empty = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
            _content.Add(empty.gameObject);
            empty.AddTextIntoVertLayout(LM.Get("label_none"), true, TextAnchor.MiddleCenter,
                new Vector2(PanelWidth, 12));
            return;
        }
        InstitutionSystem.MigrateEmpire(_empire);
        _culture = InstitutionSystem.GetPrimaryCulture(_empire);
        _lineViews = InstitutionSystem.BuildLineView(_empire);
        _foreignViews = InstitutionSystem.BuildForeignView(_empire);
        if (string.IsNullOrEmpty(_selectedId))
            _selectedId = _lineViews.FirstOrDefault(view =>
                              view.Status == InstitutionNodeStatus.Reforming)?.Node.id
                          ?? _lineViews.FirstOrDefault(view =>
                              view.Status == InstitutionNodeStatus.Available)?.Node.id
                          ?? _lineViews.FirstOrDefault()?.Node.id ?? "";
        AddStatusPanel();
        AddActionHint();
        AddSocialUnrest();
        AddConstitutionPanel();
        AddLegend();
        AddGraph();
        AddDetail();
        AddCultureRanking();
    }

    // ── 我现在什么水平 ──
    private void AddStatusPanel()
    {
        InstitutionCultureRanking ranking = InstitutionSystem.GetCultureRanking(_culture);
        const float height = 46f;
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));
        _content.Add(panel.gameObject);
        panel.AddTextIntoVertLayout(
            $"{_culture.GetCultureTranslate().ColorString("#F3C34A")}  ·  " +
            $"{LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(ranking.Line)).ColorString("#7FD8EA")}" +
            $"{LM.Get("institution_line_label")}  ·  " +
            $"{CompositeEmpireService.GetRegimeName(_empire.CoreKingdom.GetRegime().type)}",
            true, TextAnchor.MiddleCenter, new Vector2(PanelWidth - 8f, 11));
        panel.AddTextIntoVertLayout(BuildLevelLine(ranking), true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth - 8f, 11));
        panel.AddTextIntoVertLayout(LM.Get("institution_tagline").ColorString("#B8C6CC"), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth - 8f, 18));
        panel.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(PanelWidth, height));
    }

    // 文明等级这一行的关键是把"综合先进度"这个数字变得有意义：标出离下一级还差多少。
    private string BuildLevelLine(InstitutionCultureRanking ranking)
    {
        string level = $"{LM.Get("institution_civilization_level")} {ranking.LevelName.ColorString("#F3C34A")}";
        List<InstitutionCultureLevelConfig> levels = InstitutionDefinitionRegistry.Global.culture_levels
            .OrderBy(item => item.level).ToList();
        InstitutionCultureLevelConfig next = levels
            .FirstOrDefault(item => item.minimum_advancement > ranking.Advancement);
        if (next == null)
            return $"{level}  ·  " +
                   string.Format(LM.Get("institution_level_max"), ranking.Advancement);
        string nextName = LM.Get(next.name_key);
        if (string.IsNullOrWhiteSpace(nextName) || nextName == next.name_key)
            nextName = string.Format(LM.Get("institution_culture_level_format"), next.level);
        return $"{level}  ·  " + string.Format(LM.Get("institution_level_progress"),
            ranking.Advancement, next.minimum_advancement, nextName);
    }

    // ── 我现在该干什么 ──
    private void AddActionHint()
    {
        InstitutionReformState reform = _empire.data.institution_state?.active_reform;
        string text;
        if (reform != null)
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(reform.node_id);
            text = string.Format(LM.Get("institution_hint_reforming"),
                    InstitutionSystem.GetNodeName(node), GetStageName(reform.stage),
                    reform.progress.ToString("0"))
                .ColorString("#65D6C4");
        }
        else
        {
            int available = _lineViews.Count(view => view.Status == InstitutionNodeStatus.Available);
            text = available > 0
                ? string.Format(LM.Get("institution_hint_available"), available).ColorString("#F3C34A")
                : LM.Get("institution_hint_none").ColorString("#B8B8B8");
        }
        var row = _root.BeginVertGroup(pSpacing: 0, pAlignment: TextAnchor.UpperCenter);
        _content.Add(row.gameObject);
        row.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        if (reform != null)
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(reform.node_id);
            InstitutionReformEnvironment environment = InstitutionSystem.GetReformEnvironment(_empire, node);
            string environmentText = string.Format(LM.Get("institution_reform_environment"),
                environment.SameCultureCountries, environment.SameCultureEmpires,
                environment.AdvancedBorderEmpires, environment.SpeedMultiplier,
                environment.EffectiveMinimumYears);
            string color = environment.AdvancedBorderEmpires > 0 ? "#65D6C4" :
                environment.SameCultureEmpires > 1 ? "#F3C34A" : "#B8C6CC";
            row.AddTextIntoVertLayout(environmentText.ColorString(color), true, TextAnchor.MiddleCenter,
                new Vector2(PanelWidth, 12));
        }
    }

    private void AddSocialUnrest()
    {
        Dictionary<SocialClass, float> shares = InstitutionSystem.BuildClassShares(_empire);
        List<KeyValuePair<SocialClass, float>> tensions = InstitutionSystem.GetClassGrievances(_empire)
            .Where(pair => pair.Value >= 0.5f && shares.TryGetValue(pair.Key, out float share) && share > 0f)
            .OrderByDescending(pair => pair.Value).ToList();
        if (tensions.Count == 0) return;
        InstitutionSocialUnrestConfig config = InstitutionDefinitionRegistry.Global.social_unrest;
        var panel = _root.BeginVertGroup(pSpacing: 0, pAlignment: TextAnchor.UpperCenter);
        _content.Add(panel.gameObject);
        string entries = string.Join("  ·  ", tensions.Take(4).Select(pair =>
        {
            string color = pair.Value >= config.rebellion_threshold ? "#E05A4F" :
                pair.Value >= 50f ? "#E9A85B" : "#B8C6CC";
            return $"{LM.Get($"class_{pair.Key}")} {pair.Value:0}%".ColorString(color);
        }));
        panel.AddTextIntoVertLayout($"{LM.Get("institution_social_tension")}: {entries}", true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
        KeyValuePair<SocialClass, float> highest = tensions[0];
        panel.AddTextIntoVertLayout(
            string.Format(LM.Get("institution_social_tension_cause"), LM.Get($"class_{highest.Key}"),
                InstitutionSystem.GetSocialGrievanceCause(_empire, highest.Key)).ColorString("#D98C8C"),
            true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
    }

    private void AddConstitutionPanel()
    {
        ConstitutionalEconomyView view = ConstitutionalEconomySystem.GetView(_empire);
        ConstitutionConfig config = InstitutionDefinitionRegistry.Global.constitution;
        var section = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(section.gameObject);
        string status = view.constitutional ? LM.Get("constitution_status_enacted") :
            view.reforming ? LM.Get("constitution_status_reforming") :
            LM.Get("constitution_status_pending");
        section.AddTextIntoVertLayout($"{LM.Get("constitution_title")}  ·  {status}".ColorString("#7FD8EA"),
            true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        section.AddTextIntoVertLayout(string.Format(LM.Get("constitution_economy_line"),
                view.merchant_households, view.total_households,
                view.budding ? LM.Get("constitution_budding_yes") :
                    $"{view.budding_years}/{config.budding_years}"), true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth, 12));
        section.AddTextIntoVertLayout(string.Format(LM.Get("constitution_trade_line"),
                view.voyages, view.foreign_voyages, view.delivered_gold), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        section.AddTextIntoVertLayout(string.Format(LM.Get("constitution_politics_line"),
                view.culture_years, view.parliamentary_support, config.stable_culture_years), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        ParliamentView parliament = ParliamentSystem.GetView(_empire);
        if (parliament.Exists)
        {
            string government = parliament.PrimeMinister == null
                ? LM.Get("prime_minister_vacant")
                : string.Format(LM.Get("parliament_summary_line"), parliament.PrimeMinister.getName(),
                    parliament.PrimeMinisterFaction?.Name ?? LM.Get("label_none"),
                    LM.Get($"parliament_government_{parliament.GovernmentType}"),
                    parliament.GovernmentSeats, parliament.TotalSeats);
            section.AddTextIntoVertLayout(government.ColorString("#65D6C4"), true,
                TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        }
        if (parliament.Exists && view.parliamentary_support < config.support_threshold)
            section.AddTextIntoVertLayout(LM.Get("constitution_deadlock").ColorString("#D98C8C"), true,
                TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        if (view.reforming)
        {
            int years = InstitutionSystem.GetReformEnvironment(_empire).EffectiveMinimumYears;
            section.AddTextIntoVertLayout(string.Format(LM.Get("constitution_progress_line"),
                    LM.Get($"constitution_stage_{view.reform_stage}"), view.reform_progress, years), true,
                TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
            if (view.reform_stalled)
                section.AddTextIntoVertLayout(LM.Get("constitution_stalled").ColorString("#D98C8C"), true,
                    TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        }
        else if (!view.constitutional && string.IsNullOrEmpty(view.blocker))
        {
            section.AddButtonIntoVertLayout("constitution_start", LM.Get("constitution_start"),
                () => { if (ConstitutionalEconomySystem.StartReform(_empire)) Rebuild(); },
                SpriteTextureLoader.getSprite("ChineseCrown"), size: new Vector2(PanelWidth - 6f, 14));
        }
        else if (!view.constitutional)
        {
            string blocker = LM.Get(view.blocker);
            if (!string.IsNullOrEmpty(view.missing_institutions))
                blocker = string.Format(LM.Get("constitution_missing_nodes"), view.missing_institutions);
            section.AddTextIntoVertLayout(blocker.ColorString("#D98C8C"), true,
                TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        }
    }

    // ── 这些颜色什么意思 ──
    private void AddLegend()
    {
        var row = _root.BeginHoriGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleCenter,
            pSize: new Vector2(PanelWidth, 11));
        _content.Add(row.gameObject);
        AddLegendItem(row, InstitutionNodeStatus.Enacted, "institution_legend_enacted");
        AddLegendItem(row, InstitutionNodeStatus.Available, "institution_legend_available");
        AddLegendItem(row, InstitutionNodeStatus.Forceable, "institution_legend_forceable");
        AddLegendItem(row, InstitutionNodeStatus.Locked, "institution_legend_locked");
        AddLegendItem(row, InstitutionNodeStatus.ForeignReady, "institution_legend_foreign");
    }

    private static void AddLegendItem(AutoHoriLayoutGroup row, InstitutionNodeStatus status, string labelKey)
    {
        var swatchObject = new GameObject("LegendSwatch", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(LayoutElement));
        var rect = swatchObject.GetComponent<RectTransform>();
        rect.SetParent(row.transform, false);
        rect.sizeDelta = new Vector2(7f, 7f);
        Image image = swatchObject.GetComponent<Image>();
        image.sprite = SpriteTextureLoader.getSprite("ui/FactionFrame_dominate");
        image.type = Image.Type.Sliced;
        // 跟树上节点用同一份取色，不会两处各画一套
        image.color = InstitutionGraphView.GetLegendColor(status);
        image.raycastTarget = false;
        LayoutElement element = swatchObject.GetComponent<LayoutElement>();
        element.preferredWidth = 7f;
        element.preferredHeight = 7f;
        element.minWidth = 7f;
        element.minHeight = 7f;
        row.AddTextIntoHoriLayout(LM.Get(labelKey), true, TextAnchor.MiddleLeft, new Vector2(27, 10));
    }

    private void AddGraph()
    {
        var section = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(section.gameObject);
        var header = section.BeginHoriGroup(new Vector2(PanelWidth, 13), TextAnchor.MiddleCenter, 2);
        header.AddButtonIntoHoriLayout("window_back", LM.Get("window_back"),
            () => ScrollWindow.showWindow(nameof(EmpireWindow)), size: new Vector2(34, 11));
        header.AddTextIntoHoriLayout(LM.Get("institution_graph_hint").ColorString("#B8C6CC"), true,
            TextAnchor.MiddleLeft, new Vector2(PanelWidth - 78f, 11));
        header.AddButtonIntoHoriLayout("institution_graph_reset", LM.Get("institution_graph_reset"),
            () => _graph?.ResetView(), size: new Vector2(34, 11));
        _graph = InstitutionGraphView.Create(section.transform, new Vector2(PanelWidth, GraphHeight));
        _graph.Rebuild(_lineViews, _foreignViews, _selectedId, OnNodeSelected, BuildNodeTooltip, BuildNodeCard);
    }

    // 卡片上只放一眼就要知道的：状态(改革中带阶段和进度)，再加一行最关键的数字——
    // 能推动的看支持/反对，外来的看接触度，其余看施行后政体变化或所属分支。
    private InstitutionGraphView.NodeCardInfo BuildNodeCard(InstitutionNodeView view)
    {
        string statusHex = GetStatusHex(view.Status);
        string status = GetStatusName(view.Status).ColorString(statusHex);
        float progress = -1f;
        InstitutionReformState reform = _empire.data.institution_state?.active_reform;
        if (view.Status == InstitutionNodeStatus.Reforming && reform != null && reform.node_id == view.Node.id)
        {
            status = $"{GetStageName(reform.stage)} {reform.progress:0}%".ColorString(statusHex);
            progress = reform.progress / 100f;
        }

        string info;
        switch (view.Status)
        {
            case InstitutionNodeStatus.ForeignLocked:
            case InstitutionNodeStatus.ForeignContacting:
            case InstitutionNodeStatus.ForeignReady:
                info = $"{LM.Get("institution_exposure")} {view.Exposure:0}/{view.RequiredExposure:0}";
                break;
            case InstitutionNodeStatus.Available:
            case InstitutionNodeStatus.Forceable:
            case InstitutionNodeStatus.Reforming:
                info = $"{LM.Get("institution_support")} {view.Support.ToString("0").ColorString("#65D66E")}%  " +
                       $"{LM.Get("institution_opposition")} {view.Opposition.ToString("0").ColorString("#D98C8C")}%";
                break;
            default:
                info = InstitutionDefinitionRegistry.TryGetNodeRegime(view.Node, out RegimeType regime)
                    ? $"→ {CompositeEmpireService.GetRegimeName(regime)}".ColorString("#C9A7E8")
                    : GetBranchName(view.Node.branch);
                break;
        }
        return new InstitutionGraphView.NodeCardInfo(status, info, progress);
    }

    // 悬浮提示要跟点击后的详情卡说同一件事——直接复用同一套拼字逻辑，只是不放交互按钮。
    // TipButton 渲染的 Text 组件本来就开着富文本，颜色标签是保真的(之前判断它不保真是误判，
    // 真正的问题是 LM.Get 把没注册的整句话当 key 处理——用 LM.AddToCurrentLocale 注册成真
    // key 之后，标签也跟着一起原样存进去了)，所以这里照抄详情卡原来那一套配色。
    private (string title, string body) BuildNodeTooltip(InstitutionNodeView view)
    {
        bool foreign = view.Status is InstitutionNodeStatus.ForeignLocked
            or InstitutionNodeStatus.ForeignContacting or InstitutionNodeStatus.ForeignReady;

        string title = $"{InstitutionSystem.GetNodeName(view.Node).ColorString("#F3C34A")}  " +
                        $"{LM.Get("institution_advancement")} {view.Node.advancement}  ·  " +
                        $"{GetBranchName(view.Node.branch)}  ·  " +
                        $"{GetStatusName(view.Status).ColorString(GetStatusHex(view.Status))}";

        var lines = new List<string> { LM.Get(view.Node.description_key) };

        if (InstitutionDefinitionRegistry.TryGetNodeRegime(view.Node, out RegimeType nodeRegime))
        {
            bool isRoot = InstitutionDefinitionRegistry.GetRootNodes(view.Node.line).Contains(view.Node.id);
            string regimeName = CompositeEmpireService.GetRegimeName(nodeRegime);
            lines.Add((isRoot
                ? $"{LM.Get("institution_regime_initial")}: {regimeName}"
                : string.Format(LM.Get("institution_regime_after"), regimeName)).ColorString("#C9A7E8"));
        }

        List<string> unlockedSuccessionLaws = view.Node.effects_on_complete
            .Where(effect => effect.type == "unlock_succession_law")
            .Select(effect => Enum.TryParse(effect.value, true, out SuccessionLawType law)
                ? LM.Get($"option_succession_law{(int)law}")
                : null)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
        if (unlockedSuccessionLaws.Count > 0)
            lines.Add(string.Format(LM.Get("institution_unlocks_succession_law"),
                string.Join("、", unlockedSuccessionLaws)).ColorString("#C9A7E8"));

        // 这个节点给士兵发什么特质、特质具体加了多少属性——跟继承法解锁一样，不写在脸上
        // 玩家根本不知道研究这个到底换来了什么实际战斗力。
        List<string> grantedTraitLines = view.Node.effects_on_complete
            .Where(effect => effect.type == "grant_trait" && !string.IsNullOrWhiteSpace(effect.value))
            .Select(effect => FormatGrantedTrait(effect.value))
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();
        if (grantedTraitLines.Count > 0)
            lines.Add(string.Format(LM.Get("institution_grants_trait"),
                string.Join("、", grantedTraitLines)).ColorString("#E9A85B"));

        if (view.Node.politics.oppose_classes.Count > 0)
            lines.Add(string.Format(LM.Get("institution_long_term_harms"),
                string.Join("、", view.Node.politics.oppose_classes.Keys.Select(socialClass =>
                    LM.Get($"class_{socialClass}")))).ColorString("#D98C8C"));
        if (view.Node.politics.support_classes.Count > 0)
            lines.Add(string.Format(LM.Get("institution_long_term_benefits"),
                string.Join("、", view.Node.politics.support_classes.Keys.Select(socialClass =>
                    LM.Get($"class_{socialClass}")))).ColorString("#65D66E"));

        if (foreign)
        {
            lines.Add(
                $"{LM.Get("institution_absorbed_from").Replace("{0}", LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(view.Node.line)))}  ·  " +
                $"{LM.Get("institution_exposure")} {view.Exposure:0}/{view.RequiredExposure:0}  ·  " +
                $"{LM.Get("institution_contact_years")} {view.ContactYears}/{view.RequiredContactYears}");
            lines.Add(LM.Get("institution_foreign_hint").ColorString("#B8C6CC"));
            return (title, string.Join("\n", lines));
        }

        if (view.Status is InstitutionNodeStatus.Enacted or InstitutionNodeStatus.Absorbed)
            return (title, string.Join("\n", lines));

        // 制度本身是文化共有的(施行完成会同文化全体共享，见窗口顶部那句提示)，但只要还没
        // 施行完成，"支持/反对/是不是在改革中"这些数字算的都是当前查看的这一个帝国——
        // 同文化下如果还有其他帝国，它们各自的政治态势、甚至各自是否已经在推进同一个节点，
        // 都可能完全不一样，不会互相同步。这里把帝国名字钉在数字前面，切换帝国查看时
        // 才不会把"这个帝国的态势"误当成"整个文化的态势"。
        lines.Add(string.Format(LM.Get("institution_empire_scope_note"), _empire.GetEmpireName())
            .ColorString("#8FA0A8"));
        InstitutionReformEnvironment environment = InstitutionSystem.GetReformEnvironment(_empire, view.Node);
        string environmentColor = environment.AdvancedBorderEmpires > 0 ? "#65D6C4" :
            environment.SameCultureEmpires > 1 ? "#F3C34A" : "#B8C6CC";
        lines.Add(string.Format(LM.Get("institution_reform_environment"),
                environment.SameCultureCountries, environment.SameCultureEmpires,
                environment.AdvancedBorderEmpires, environment.SpeedMultiplier,
                environment.EffectiveMinimumYears).ColorString(environmentColor));
        lines.Add($"{LM.Get("institution_support")} {view.Support.ToString("0").ColorString("#65D66E")}%  ·  " +
                   $"{LM.Get("institution_opposition")} {view.Opposition.ToString("0").ColorString("#D98C8C")}%  ·  " +
                   $"{LM.Get("institution_minimum_years")} {environment.EffectiveMinimumYears}  ·  " +
                   $"{LM.Get("institution_mandate_required")} {view.Node.research.mandate_required}");

        // 支持/反对具体是哪个派系、哪个阶层——之前只报一个百分比看不出跟派系系统有什么关系，
        // 这里直接把节点配置里的 support_factions/oppose_factions(还有阶层)摊开显示。
        // FactionType 只是派系"类别"(尊王/诸侯/绥靖……)，不是玩家在这局游戏里实际见到的
        // 那个派系——同一类别在当前政体下已经有一个具体的 FixedFaction 了(比如"贵族寡头
        // 派")，能找到就显示它的真实名字，找不到(这个政体压根没配这个类别)才退回类别名。
        List<FixedFaction> playerFactions = _empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
                                             ?? new List<FixedFaction>();
        string factionLine = BuildWeightedGroupLine(view.Node.politics.support_factions,
            view.Node.politics.oppose_factions,
            faction => playerFactions.FirstOrDefault(f => f.Type == faction)?.Name ?? faction.ToString(),
            (faction, weight) => InstitutionSystem.GetFactionContribution(_empire, faction, weight));
        if (!string.IsNullOrEmpty(factionLine)) lines.Add(factionLine);
        Dictionary<SocialClass, float> classShares = InstitutionSystem.BuildClassShares(_empire);
        string classLine = BuildWeightedGroupLine(view.Node.politics.support_classes,
            view.Node.politics.oppose_classes, socialClass => LM.Get($"class_{socialClass}"),
            (socialClass, weight) => InstitutionSystem.GetClassContribution(_empire, socialClass, weight, classShares));
        if (!string.IsNullOrEmpty(classLine)) lines.Add(classLine);
        if (!string.IsNullOrEmpty(factionLine) || !string.IsNullOrEmpty(classLine))
            lines.Add(LM.Get("institution_contribution_hint").ColorString("#8FA0A8"));

        InstitutionReformState activeReform = _empire.data.institution_state?.active_reform;
        if (activeReform != null && activeReform.node_id == view.Node.id)
        {
            FixedFaction sponsor = InstitutionSystem.GetReformSponsor(_empire, activeReform);
            string sponsorName = sponsor?.Name ?? LM.Get("institution_reform_origin_ruler");
            lines.Add(string.Format(LM.Get("institution_reform_sponsor"), sponsorName)
                .ColorString("#F3C34A"));
        }
        if (view.Node.politics.oppose_factions.Count > 0 || view.Node.politics.oppose_classes.Count > 0)
            lines.Add(string.Format(LM.Get("institution_rebellion_reason_preview"),
                InstitutionSystem.GetResistanceReason(view.Node)).ColorString("#D98C8C"));

        // "任一满足"的一组显示成"A或B"
        List<string> missing = InstitutionDefinitionRegistry
            .GetMissingPrerequisites(view.Node, required => InstitutionSystem.IsEnacted(_culture, required))
            .Select(group => string.Join(LM.Get("institution_requires_any_separator"), group.Select(required =>
                InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(required)))))
            .ToList();
        if (missing.Count > 0)
            lines.Add(string.Format(LM.Get("institution_missing_requires"),
                string.Join("、", missing)).ColorString("#D98C8C"));

        switch (view.Status)
        {
            case InstitutionNodeStatus.Forceable:
                lines.Add(LM.Get(view.Reason).ColorString("#D98C8C"));
                break;
            case InstitutionNodeStatus.Reforming:
                InstitutionReformState reform = _empire.data.institution_state?.active_reform;
                if (reform != null)
                    lines.Add(
                        $"{GetStageName(reform.stage)}  {reform.progress:0.0}%  ·  {LM.Get("institution_radicalism")} {reform.radicalism:0}%"
                            .ColorString("#65D6C4"));
                break;
            default:
                if (missing.Count == 0 && !string.IsNullOrEmpty(view.Reason))
                    lines.Add(LM.Get(view.Reason).ColorString("#B8B8B8"));
                break;
        }

        return (title, string.Join("\n", lines));
    }

    // 把特质 id 换成"特质名(+伤害10 · +速度10)"这样能看懂的样子，直接读特质自己的
    // base_stats(跟 EmpireCraftActorTraitLibrary 里 lib.t.base_stats[...] 赋值的是同一份数据)，
    // 不用在这里重复抄一遍数值——数值改了这里自动跟着对。
    private static string FormatGrantedTrait(string traitId)
    {
        ActorTrait trait = AssetManager.traits?.get(traitId);
        string traitName = LM.Get(traitId);
        if (trait?.base_stats == null || !trait.base_stats.hasStats()) return traitName;
        string stats = string.Join(" · ", trait.base_stats.getList().Select(entry => $"+{StatLabel(entry.id)}{entry.value:0.#}"));
        return $"{traitName}({stats})";
    }

    private static string StatLabel(string statKey)
    {
        return statKey switch
        {
            "damage" => LM.Get("stat_damage"),
            "speed" => LM.Get("stat_speed"),
            "armor" => LM.Get("stat_armor"),
            "critical_chance" => LM.Get("stat_critical_chance"),
            "critical_damage_multiplier" => LM.Get("stat_critical_damage_multiplier"),
            _ => statKey
        };
    }

    // support/oppose_factions 和 support/oppose_classes 是同一种形状(Dictionary<T, float>)，
    // 拼成"支持: A+45.0 · B+12.0  反对: C+8.0"这一行的逻辑抽出来一份，两处共用。
    // 括号里的数不再是抽象的配置权重，而是按 CalculatePoliticalBalance 同一套公式单独算出来
    // 的"这一项实际贡献了多少分"，跟上面显示的支持度/反对度百分比是同一个刻度，直接能对上——
    // 比"权重×1.2"直观，缺点是多项加起来可能超过显示的 100% 上限(总分会被封顶，单项不会)。
    private static string BuildWeightedGroupLine<T>(Dictionary<T, float> support, Dictionary<T, float> oppose,
        Func<T, string> nameOf, Func<T, float, float> contributionOf)
    {
        string FormatGroup(Dictionary<T, float> group) => group == null || group.Count == 0
            ? ""
            : string.Join(" · ", group.Select(pair =>
                $"{nameOf(pair.Key)}{FormatContribution(contributionOf(pair.Key, pair.Value))}"));
        string supportText = FormatGroup(support);
        string opposeText = FormatGroup(oppose);
        if (string.IsNullOrEmpty(supportText) && string.IsNullOrEmpty(opposeText)) return "";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(supportText))
            parts.Add($"{LM.Get("institution_support")}: {supportText}".ColorString("#65D66E"));
        if (!string.IsNullOrEmpty(opposeText))
            parts.Add($"{LM.Get("institution_opposition")}: {opposeText}".ColorString("#D98C8C"));
        return string.Join("  ", parts);
    }

    public static string FormatContribution(float points) => (points >= 0 ? "+" : "") + points.ToString("0.0");

    private void OnNodeSelected(string nodeId)
    {
        _selectedId = nodeId ?? "";
        _graph?.SetSelected(_selectedId, _lineViews.Concat(_foreignViews).ToList());
        RefreshDetail();
    }

    private void AddDetail()
    {
        _detailPanel = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(_detailPanel.gameObject);
        RefreshDetail();
    }

    // 只重建详情区，不动整张图 —— 否则每点一个节点，拖拽和缩放都会被重置。
    // 名称/描述/政体变更/继承法解锁/支持反对/缺哪个前置这些信息，悬浮节点的 tooltip 已经
    // 说过一遍了(BuildNodeTooltip)；这里只留"点了能做什么"——按钮和锁住的原因，不再重复
    // 整段详情文字，不然一悬浮一点击，同一段话会在屏幕上出现两次。
    private void RefreshDetail()
    {
        if (_detailPanel == null) return;
        _detailPanel.transform.ClearChildren();
        InstitutionNodeView view = _lineViews.Concat(_foreignViews)
            .FirstOrDefault(item => item.Node.id == _selectedId);
        if (view == null) return;
        bool foreign = view.Status is InstitutionNodeStatus.ForeignLocked
            or InstitutionNodeStatus.ForeignContacting or InstitutionNodeStatus.ForeignReady;

        _detailPanel.AddTextIntoVertLayout(
            $"{InstitutionSystem.GetNodeName(view.Node).ColorString("#F3C34A")}  " +
            $"{GetStatusName(view.Status).ColorString(GetStatusHex(view.Status))}",
            true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));

        if (foreign)
        {
            _detailPanel.AddTextIntoVertLayout(LM.Get("institution_foreign_hint").ColorString("#B8C6CC"), true,
                TextAnchor.MiddleCenter, new Vector2(PanelWidth, 20));
            return;
        }

        if (view.Status is InstitutionNodeStatus.Enacted or InstitutionNodeStatus.Absorbed) return;

        // 上帝模式：跳过改革直接点亮（连同前置），或点亮本文化整条线
        var godRow = _detailPanel.BeginHoriGroup(new Vector2(PanelWidth, 14), TextAnchor.MiddleCenter, 4);
        godRow.AddButtonIntoHoriLayout("institution_force_enact", LM.Get("institution_force_enact"),
            () => ForceEnact(view.Node.id, false), size: new Vector2(110, 12));
        godRow.AddButtonIntoHoriLayout("institution_force_enact_all", LM.Get("institution_force_enact_all"),
            () => ForceEnact(view.Node.id, true), size: new Vector2(110, 12));

        InstitutionReformEnvironment environment = InstitutionSystem.GetReformEnvironment(_empire, view.Node);
        string environmentColor = environment.AdvancedBorderEmpires > 0 ? "#65D6C4" :
            environment.SameCultureEmpires > 1 ? "#F3C34A" : "#B8C6CC";
        _detailPanel.AddTextIntoVertLayout(string.Format(LM.Get("institution_reform_environment"),
                environment.SameCultureCountries, environment.SameCultureEmpires,
                environment.AdvancedBorderEmpires, environment.SpeedMultiplier,
                environment.EffectiveMinimumYears).ColorString(environmentColor), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));

        bool missingRequires = !InstitutionDefinitionRegistry.ArePrerequisitesMet(view.Node,
            required => InstitutionSystem.IsEnacted(_culture, required));

        switch (view.Status)
        {
            case InstitutionNodeStatus.Available:
                _detailPanel.AddButtonIntoVertLayout("institution_start_reform",
                    LM.Get("institution_start_reform"), () => StartReform(view.Node.id, false),
                    SpriteTextureLoader.getSprite("ChineseCrown"), size: new Vector2(PanelWidth - 6f, 14));
                break;
            case InstitutionNodeStatus.Forceable:
                _detailPanel.AddTextIntoVertLayout(LM.Get(view.Reason).ColorString("#D98C8C"), true,
                    TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
                _detailPanel.AddButtonIntoVertLayout("institution_force_reform",
                    LM.Get("institution_force_reform"), () => StartReform(view.Node.id, true),
                    SpriteTextureLoader.getSprite("ui/icons/iconWar"),
                    size: new Vector2(PanelWidth - 6f, 14));
                break;
            case InstitutionNodeStatus.Reforming:
                InstitutionReformState reform = _empire.data.institution_state?.active_reform;
                if (reform != null)
                {
                    FixedFaction sponsor = InstitutionSystem.GetReformSponsor(_empire, reform);
                    _detailPanel.AddTextIntoVertLayout(
                        $"{GetStageName(reform.stage)}  {reform.progress:0.0}%  ·  " +
                        $"{LM.Get("institution_radicalism")} {reform.radicalism:0}%",
                        true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
                    _detailPanel.AddTextIntoVertLayout(
                        string.Format(LM.Get("institution_reform_sponsor"),
                            sponsor?.Name ?? LM.Get("institution_reform_origin_ruler")),
                        true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
                }
                break;
            default:
                if (!missingRequires && !string.IsNullOrEmpty(view.Reason))
                    _detailPanel.AddTextIntoVertLayout(LM.Get(view.Reason).ColorString("#B8B8B8"), true,
                        TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
                break;
        }
    }

    private void ForceEnact(string nodeId, bool all)
    {
        if (all) InstitutionSystem.ForceEnactAll(_culture);
        else InstitutionSystem.ForceEnactNode(_culture, nodeId);
        _selectedId = nodeId;
        Rebuild();
    }

    // 不能叫 Start：Unity 会把它当成生命周期方法调用并报错 "Start() can not take parameters"
    private void StartReform(string nodeId, bool force)
    {
        if (!InstitutionSystem.StartReform(_empire, nodeId, force)) return;
        _selectedId = nodeId;
        Rebuild();
    }

    private void AddCultureRanking()
    {
        var section = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        _content.Add(section.gameObject);
        section.AddTextIntoVertLayout(LM.Get("institution_culture_ranking").ColorString("#7FD8EA"), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        section.AddTextIntoVertLayout(LM.Get("institution_culture_ranking_hint").ColorString("#B8C6CC"), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth, 16));
        foreach (IGrouping<int, InstitutionCultureRanking> group in InstitutionSystem.GetCultureRankings()
                     .GroupBy(item => item.Level).OrderByDescending(group => group.Key))
        {
            List<InstitutionCultureRanking> cultures = group.ToList();
            section.AddTextIntoVertLayout(cultures[0].LevelName.ColorString("#F3C34A"), true,
                TextAnchor.MiddleCenter, new Vector2(PanelWidth, 11));
            foreach (InstitutionCultureRanking culture in cultures)
            {
                var row = section.BeginHoriGroup(new Vector2(PanelWidth, 16), TextAnchor.MiddleCenter, 2);
                row.AddButtonIntoHoriLayout("institution_level_down", "-",
                    () => AdjustCultureLevel(culture.Culture, -1), size: new Vector2(16, 14), showTip: true);
                string mode = culture.Manual ? LM.Get("institution_level_manual") : LM.Get("institution_level_auto");
                row.AddTextIntoHoriLayout(
                    $"{culture.Culture.GetCultureTranslate()}  " +
                    $"[{LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(culture.Line))}]  " +
                    $"{culture.Advancement}  ({mode})", true, TextAnchor.MiddleLeft, new Vector2(PanelWidth - 66f, 14));
                row.AddButtonIntoHoriLayout("institution_level_auto", LM.Get("institution_level_auto_short"),
                    () => ResetCultureLevel(culture.Culture), size: new Vector2(28, 14), showTip: true);
                row.AddButtonIntoHoriLayout("institution_level_up", "+",
                    () => AdjustCultureLevel(culture.Culture, 1), size: new Vector2(16, 14), showTip: true);
            }
        }
    }

    private void AdjustCultureLevel(string culture, int direction)
    {
        InstitutionCultureRanking ranking = InstitutionSystem.GetCultureRanking(culture);
        int target = Math.Max(InstitutionSystem.GetMinimumCultureLevel(),
            Math.Min(InstitutionSystem.GetMaximumCultureLevel(), ranking.Level + direction));
        InstitutionSystem.SetCultureLevel(culture, target);
        Rebuild();
    }

    private void ResetCultureLevel(string culture)
    {
        InstitutionSystem.SetCultureLevel(culture, 0);
        Rebuild();
    }

    private static string GetBranchName(string branch)
    {
        // 新分支只需要在语言文件里补 institution_branch_<分支>，不需要改代码
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
            InstitutionNodeStatus.Available => LM.Get("institution_status_available"),
            InstitutionNodeStatus.Forceable => LM.Get("institution_status_force_available"),
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
            InstitutionNodeStatus.Forceable => "#D98C8C",
            InstitutionNodeStatus.ForeignReady => "#C9A7E8",
            InstitutionNodeStatus.ForeignContacting => "#A79BC4",
            _ => "#B8B8B8"
        };
    }

    private static string GetStageName(InstitutionReformStage stage)
    {
        return stage switch
        {
            InstitutionReformStage.Debate => LM.Get("institution_stage_Debate"),
            InstitutionReformStage.Legislation => LM.Get("institution_stage_Legislation"),
            InstitutionReformStage.Implementation => LM.Get("institution_stage_Implementation"),
            InstitutionReformStage.Consolidation => LM.Get("institution_stage_Consolidation"),
            _ => stage.ToString()
        };
    }
}
