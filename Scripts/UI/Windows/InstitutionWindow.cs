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
    private PartyIdeology _selectedIdeology = PartyIdeology.Conservatism;

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
        _selectedIdeology = PartySystem.GetGovernmentParty(_empire)?.Ideology ?? PartyIdeology.Conservatism;
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
        AddOverviewCards();
        AddSocialUnrest();
        AddPublicOpinion();
        AddLegend();
        AddGraph();
        AddDetail();
        AddCultureRanking();
    }

    // ── 我现在什么水平 ──
    // 顶部总览卡：第一行三枚带图标的"标签"(文化 / 科技线 / 政体)，第二行文明等级 + 一根到下一级的进度条，
    // 最后一行小字点出"制度属于文化"。原来三行都是自动放大的大字，头重脚轻，把下面的内容挤到很后面。
    private void AddStatusPanel()
    {
        InstitutionCultureRanking ranking = InstitutionSystem.GetCultureRanking(_culture);
        const float height = 50f;
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 2,
            pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(6, 6, 4, 4));
        _content.Add(panel.gameObject);

        var tags = panel.BeginHoriGroup(new Vector2(PanelWidth - 12f, 12), TextAnchor.MiddleCenter, 6);
        AddTag(tags, Icon("ui/icons/iconCulture"), _culture.GetCultureTranslate(), "#F3C34A");
        AddTag(tags, Icon("ui/icons/iconTech", "ui/icons/iconKnowledge"),
            LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(ranking.Line)) + LM.Get("institution_line_label"),
            "#7FD8EA");
        AddTag(tags, Icon("ui/icons/iconCrown", "ui/icons/iconKingdom"),
            CompositeEmpireService.GetRegimeName(_empire.CoreKingdom.GetRegime().type), "#E6E0CF");

        var levelRow = panel.BeginHoriGroup(new Vector2(PanelWidth - 12f, 11), TextAnchor.MiddleCenter, 4);
        (string levelText, float levelProgress) = BuildLevelLine(ranking);
        AddLabel(levelRow, levelText, 190f, 7, TextAnchor.MiddleRight);
        AddProgressBar(levelRow.transform, new Vector2(120f, 5f), levelProgress, new Color(0.95f, 0.76f, 0.29f));

        var tagline = panel.AddTextIntoVertLayout(LM.Get("institution_tagline").ColorString("#9FB0B6"), true,
            TextAnchor.MiddleCenter, new Vector2(PanelWidth - 12f, 9));
        tagline.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        panel.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(PanelWidth, height));
    }

    // 文明等级这一行的关键是把"综合先进度"这个数字变得有意义：标出离下一级还差多少，
    // 顺带返回一个 0~1 的进度给旁边的进度条用(已满级返回 1)。
    private (string text, float progress) BuildLevelLine(InstitutionCultureRanking ranking)
    {
        string level = $"{LM.Get("institution_civilization_level")} {ranking.LevelName.ColorString("#F3C34A")}";
        List<InstitutionCultureLevelConfig> levels = InstitutionDefinitionRegistry.Global.culture_levels
            .OrderBy(item => item.level).ToList();
        InstitutionCultureLevelConfig next = levels
            .FirstOrDefault(item => item.minimum_advancement > ranking.Advancement);
        if (next == null)
            return ($"{level}  ·  " + string.Format(LM.Get("institution_level_max"), ranking.Advancement), 1f);
        string nextName = LM.Get(next.name_key);
        if (string.IsNullOrWhiteSpace(nextName) || nextName == next.name_key)
            nextName = string.Format(LM.Get("institution_culture_level_format"), next.level);
        float progress = next.minimum_advancement > 0
            ? Mathf.Clamp01((float)ranking.Advancement / next.minimum_advancement)
            : 0f;
        return ($"{level}  ·  " + string.Format(LM.Get("institution_level_progress"),
            ranking.Advancement, next.minimum_advancement, nextName), progress);
    }

    // ── 我现在该干什么 ──
    // 制度改革和君主立宪并排两张卡片，每张卡片：图标标题 + 状态 → 主进度 → 一格一格的数据小块。
    // 两张卡片等高，高度取两边内容里更高的那个。
    private const float CardGap = 4f;
    private static float CardWidth => (PanelWidth - CardGap) / 2f;
    private const float CardHeaderHeight = 12f;
    private const float CardLineHeight = 11f;
    private const float ChipHeight = 12f;
    private const float CardSpacing = 2f;

    private void AddOverviewCards()
    {
        InstitutionReformState reform = _empire.data.institution_state?.active_reform;
        InstitutionNodeConfig reformNode = reform != null ? InstitutionDefinitionRegistry.Get(reform.node_id) : null;
        InstitutionReformEnvironment environment = InstitutionSystem.GetReformEnvironment(_empire, reformNode);
        ConstitutionalEconomyView constitution = ConstitutionalEconomySystem.GetView(_empire);
        ConstitutionConfig constitutionConfig = InstitutionDefinitionRegistry.Global.constitution;
        ParliamentView parliament = ParliamentSystem.GetView(_empire);

        List<string> constitutionNotes = BuildConstitutionNotes(constitution, constitutionConfig, parliament,
            out bool showStartButton);
        bool isRepublic = RepublicSystem.IsRepublic(_empire);
        if (isRepublic) showStartButton = false;
        if (RepublicSystem.IsTransitioning(_empire))
        {
            showStartButton = false;
            ConstitutionalEconomyState transition = _empire.data.constitutional_economy;
            int yearsRemaining = Math.Max(0, 3 - Date.getYearsSince(transition.republic_transition_started));
            constitutionNotes.Add(string.Format(LM.Get("republic_transition_progress"),
                LM.Get(transition.republic_transition_stage == 1
                    ? "republic_provisional_stage" : "republic_drafting_stage"), yearsRemaining)
                .ColorString("#65D6C4"));
        }
        else if (RepublicSystem.CanAbolish(_empire))
            constitutionNotes.Add(LM.Get("republic_abolition_available").ColorString("#65D6C4"));
        // 强制萌芽只在还没萌芽时出现；已萌芽才可能出现"发起制宪"按钮，两者不会同时显示
        bool showForceBudding = !isRepublic && !constitution.budding && !constitution.constitutional;
        bool showForceConstitution = !isRepublic && ConstitutionalEconomySystem.CanForceConstitution(_empire);
        int reformLines = reform != null ? 2 : 1; // 改革中：名称行 + 进度条行
        float reformHeight = CardHeaderHeight + reformLines * CardLineHeight + 3 * ChipHeight;
        float constitutionHeight = CardHeaderHeight + 4 * ChipHeight + constitutionNotes.Count * CardLineHeight +
                                   (showStartButton ? 14f : 0f) + (showForceBudding ? 14f : 0f) +
                                   (showForceConstitution ? 14f : 0f);
        int reformItems = 1 + reformLines + 3;
        int constitutionItems = 1 + 4 + constitutionNotes.Count + (showStartButton ? 1 : 0) +
                                (showForceBudding ? 1 : 0) + (showForceConstitution ? 1 : 0);
        float height = Mathf.Max(reformHeight + reformItems * CardSpacing,
            constitutionHeight + constitutionItems * CardSpacing) + 10f;

        var row = _root.BeginHoriGroup(new Vector2(PanelWidth, height), TextAnchor.UpperCenter, CardGap);
        _content.Add(row.gameObject);

        // 左：制度改革
        var reformCard = BeginCard(row, height);
        string reformStatus = reform != null
            ? LM.Get("institution_status_reforming").ColorString("#65D6C4")
            : LM.Get("label_none").ColorString("#8FA0A8");
        AddCardHeader(reformCard, Icon("ui/icons/iconKnowledge", "ui/icons/iconTech"),
            LM.Get("institution_card_reform"), reformStatus);
        if (reform != null)
        {
            AddCardLine(reformCard, $"{InstitutionSystem.GetNodeName(reformNode).ColorString("#F3C34A")}  ·  " +
                                    $"{GetStageName(reform.stage)} {reform.progress:0}%".ColorString("#65D6C4"));
            var barRow = reformCard.BeginHoriGroup(new Vector2(CardWidth - 12f, CardLineHeight),
                TextAnchor.MiddleCenter, 0);
            AddProgressBar(barRow.transform, new Vector2(CardWidth - 24f, 5f), reform.progress / 100f,
                new Color(0.40f, 0.84f, 0.77f));
        }
        else
        {
            int available = _lineViews.Count(view => view.Status == InstitutionNodeStatus.Available);
            AddCardLine(reformCard, available > 0
                ? string.Format(LM.Get("institution_hint_available"), available).ColorString("#F3C34A")
                : LM.Get("institution_hint_none").ColorString("#B8B8B8"));
        }
        string speedColor = environment.AdvancedBorderEmpires > 0 ? "#65D6C4" :
            environment.SameCultureEmpires > 1 ? "#F3C34A" : "#E6E0CF";
        AddChipRow(reformCard,
            (Icon("ui/icons/iconCulture"), LM.Get("institution_env_same_culture"),
                environment.SameCultureCountries.ToString()),
            (Icon("ui/icons/iconKingdom"), LM.Get("institution_env_empires"),
                environment.SameCultureEmpires.ToString()));
        AddChipRow(reformCard,
            (Icon("ui/icons/iconDiplomacy", "ui/icons/iconAlliance"), LM.Get("institution_env_advanced"),
                environment.AdvancedBorderEmpires.ToString()),
            (Icon("ui/icons/iconClockX2", "ui/icons/iconClock"), LM.Get("institution_env_speed"),
                $"×{environment.SpeedMultiplier:0.00}".ColorString(speedColor)));
        AddChipRow(reformCard,
            (Icon("ui/icons/iconClock"), LM.Get("institution_env_years"),
                environment.EffectiveMinimumYears.ToString()),
            (null, null, null));

        // 右：君主立宪
        var constitutionCard = BeginCard(row, height);
        ConstitutionalEconomyState economyState = _empire.data.constitutional_economy;
        string constitutionStatus = economyState?.is_republic == true
            ? RepublicSystem.IsTransitioning(_empire)
                ? LM.Get(economyState.republic_transition_stage == 1
                    ? "republic_provisional_stage" : "republic_drafting_stage").ColorString("#65D6C4")
                : string.Format(LM.Get("republic_status"), PartySystem.GetIdeologyName(economyState.republic_ideology))
                .ColorString("#C9A7E8")
            : constitution.constitutional
            ? LM.Get("constitution_status_enacted").ColorString("#65D66E")
            : constitution.reforming
                ? LM.Get("constitution_status_reforming").ColorString("#65D6C4")
                : LM.Get("constitution_status_pending").ColorString("#8FA0A8");
        AddCardHeader(constitutionCard, Icon("ui/icons/iconCrown", "ui/icons/iconKings"),
            LM.Get("constitution_title"), constitutionStatus);
        string budding = constitution.budding
            ? LM.Get("constitution_budding_yes").ColorString("#65D66E")
            : $"{constitution.budding_years}/{constitutionConfig.budding_years}";
        string supportColor = constitution.parliamentary_support >= constitutionConfig.support_threshold
            ? "#65D66E" : "#D98C8C";
        AddChipRow(constitutionCard,
            (Icon("ui/icons/iconPopulation", "ui/icons/iconCitizen"), LM.Get("constitution_stat_merchants"),
                $"{constitution.merchant_households}/{constitution.total_households}"),
            (Icon("ui/icons/iconGold", "ui/icons/iconMoney"), LM.Get("constitution_stat_budding"), budding));
        AddChipRow(constitutionCard,
            (Icon("ui/icons/iconBoat"), LM.Get("constitution_stat_voyages"), constitution.voyages.ToString()),
            (Icon("ui/icons/iconDiplomacy", "ui/icons/iconAlliance"), LM.Get("constitution_stat_foreign"),
                constitution.foreign_voyages.ToString()));
        AddChipRow(constitutionCard,
            (Icon("ui/icons/iconMoney", "ui/icons/iconGold"), LM.Get("constitution_stat_gold"),
                constitution.delivered_gold.ToString()),
            (Icon("ui/icons/iconClock"), LM.Get("constitution_stat_stability"),
                $"{constitution.culture_years}/{constitutionConfig.stable_culture_years}"));
        AddChipRow(constitutionCard,
            // 议会召开前还没有议会：这个数是"支持制宪的派系占中央份额的比例"，不能叫议会支持
            (Icon("ui/icons/iconLoyalty", "ui/icons/iconKingdom"),
                LM.Get(parliament.Exists ? "constitution_stat_support" : "constitution_stat_faction_support"),
                $"{constitution.parliamentary_support:0}%".ColorString(supportColor)),
            (null, null, null));
        foreach (string note in constitutionNotes) AddCardLine(constitutionCard, note);
        if (showStartButton)
            constitutionCard.AddButtonIntoVertLayout("constitution_start", LM.Get("constitution_start"),
                () => { if (ConstitutionalEconomySystem.StartReform(_empire)) Rebuild(); },
                SpriteTextureLoader.getSprite("ChineseCrown"), size: new Vector2(CardWidth - 16f, 12));
        if (showForceBudding)
            constitutionCard.AddButtonIntoVertLayout("constitution_force_budding",
                LM.Get("constitution_force_budding"),
                () => { if (ConstitutionalEconomySystem.ForceBudding(_empire)) Rebuild(); },
                Icon("ui/icons/iconGold", "ui/icons/iconMoney"), size: new Vector2(CardWidth - 16f, 12));
        if (showForceConstitution)
            constitutionCard.AddButtonIntoVertLayout("constitution_force_enact",
                LM.Get("constitution_force_enact"),
                () => { if (ConstitutionalEconomySystem.ForceConstitution(_empire)) Rebuild(); },
                Icon("ui/icons/iconCrown", "ui/icons/iconKings"), size: new Vector2(CardWidth - 16f, 12));
    }

    // 立宪卡片数据块下面的几行说明(内阁、僵局、制宪进度、卡在哪一步)，先算出来好定卡片高度。
    private List<string> BuildConstitutionNotes(ConstitutionalEconomyView view, ConstitutionConfig config,
        ParliamentView parliament, out bool showStartButton)
    {
        var notes = new List<string>();
        showStartButton = false;
        CultureInstitutionState cultureConstitution = ConstitutionalEconomySystem.GetCultureConstitution(_empire);
        if (cultureConstitution?.constitutional_monarchy == true)
            notes.Add(string.Format(LM.Get("constitution_culture_pioneer_line"),
                cultureConstitution.constitutional_monarchy_pioneer_name).ColorString("#65D66E"));
        if (!parliament.Exists && !view.constitutional)
        {
            // 还没有议会时，把"哪些派系算支持制宪"摊开：数字是按这些派系的中央占比加出来的
            List<FixedFaction> factions = _empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
                ?.Where(faction => faction != null && !faction.Ban).ToList() ?? new List<FixedFaction>();
            string Names(bool pro) => string.Join("、", factions
                .Where(faction => ConstitutionalEconomySystem.IsConstitutionalist(faction, view.budding) == pro)
                .Select(faction => $"{faction.Name}{faction.CentralRatio:0}%"));
            string pro = Names(true), anti = Names(false);
            if (factions.Count > 0)
                notes.Add(string.Format(LM.Get("constitution_faction_stance_line"),
                    string.IsNullOrEmpty(pro) ? LM.Get("label_none") : pro,
                    string.IsNullOrEmpty(anti) ? LM.Get("label_none") : anti).ColorString("#B8C6CC"));
        }
        if (parliament.Exists)
        {
            string government = parliament.PrimeMinister == null
                ? LM.Get("prime_minister_vacant")
                : string.Format(LM.Get("parliament_summary_line"), parliament.PrimeMinister.getName(),
                    parliament.PrimeMinisterFaction?.Name ?? LM.Get("label_none"),
                    LM.Get($"parliament_government_{parliament.GovernmentType}"),
                    parliament.GovernmentSeats, parliament.TotalSeats);
            notes.Add(government.ColorString("#65D6C4"));
            if (view.parliamentary_support < config.support_threshold)
                notes.Add(LM.Get("constitution_deadlock").ColorString("#D98C8C"));
        }
        if (view.reforming)
        {
            int years = InstitutionSystem.GetReformEnvironment(_empire).EffectiveMinimumYears;
            notes.Add(string.Format(LM.Get("constitution_progress_line"),
                LM.Get($"constitution_stage_{view.reform_stage}"), view.reform_progress, years).ColorString("#65D6C4"));
            if (view.reform_stalled) notes.Add(LM.Get("constitution_stalled").ColorString("#D98C8C"));
        }
        else if (!view.constitutional && string.IsNullOrEmpty(view.blocker))
        {
            showStartButton = true;
        }
        else if (!view.constitutional)
        {
            string blocker = LM.Get(view.blocker);
            if (!string.IsNullOrEmpty(view.missing_institutions))
                blocker = string.Format(LM.Get("constitution_missing_nodes"), view.missing_institutions);
            notes.Add(blocker.ColorString("#D98C8C"));
        }
        return notes;
    }

    // 阶层怨气：只在真有怨气时出现，做成一条带警告图标的红框横条，比夹在中间的两行红字醒目。
    // ── 民意与选举 ──
    // 只在开放党禁后显示：民意等级、支持/异见、百姓想要的理念、外国理念压力、下次大选，以及两个应对按钮
    private void AddPublicOpinion()
    {
        ConstitutionalEconomyState state = _empire.data.constitutional_economy;
        if (state == null || !PartySystem.IsActive(_empire)) return;
        int level = PublicOpinionSystem.GetLevel(_empire);
        string levelColor = level switch { 0 => "#9EF29E", 1 => "#E9D35B", 2 => "#E9A85B", _ => "#E05A4F" };
        bool hasPreferred = PublicOpinionSystem.TryGetPreferred(_empire, out PartyIdeology preferred);
        ParliamentView parliament = ParliamentSystem.GetView(_empire);
        List<IdeologyPressureSource> pressure = state.ideology_pressure ?? new List<IdeologyPressureSource>();

        const float height = 84f;
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(6, 6, 4, 4));
        _content.Add(panel.gameObject);

        var head = panel.BeginHoriGroup(new Vector2(PanelWidth - 12f, 12), TextAnchor.MiddleCenter, 3);
        AddIcon(head.transform, Icon("ui/icons/iconReligion", "ui/icons/iconPopulation"), 10f);
        string headText = $"{LM.Get("public_opinion_title")}: " +
                          PublicOpinionSystem.GetLevelName(level).ColorString(levelColor) + "   " +
                          string.Format(LM.Get("public_opinion_support_dissent"),
                              (state.opinion_support * 100f).ToString("0"), (state.opinion_dissent * 100f).ToString("0"));
        if (hasPreferred)
            headText += "   " + string.Format(LM.Get("public_opinion_preferred"),
                PartySystem.GetIdeologyName(preferred)).ColorString("#C9A7E8");
        AddLabel(head, headText, PanelWidth - 30f, 7, TextAnchor.MiddleLeft, 12f);

        string pressureText = pressure.Count == 0
            ? LM.Get("public_opinion_no_pressure")
            : LM.Get("public_opinion_pressure") + string.Join("  ·  ", pressure.Take(3).Select(source =>
                $"{source.empire_name}({PartySystem.GetIdeologyName(source.ideology)}) {source.amount:0}"));
        panel.AddTextIntoVertLayout(pressureText.ColorString("#B8C6CC"), true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth - 12f, 10)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        string electionText = parliament.Exists
            ? RepublicSystem.IsOneParty(_empire)
                ? LM.Get("public_opinion_one_party")
                : string.Format(LM.Get("public_opinion_next_election"), parliament.YearsUntilElection)
            : LM.Get("public_opinion_no_parliament");
        if (level >= PublicOpinionSystem.RevolutionaryWave)
            electionText += "  " + string.Format(LM.Get("public_opinion_wave_years"),
                state.opinion_wave_years, 2).ColorString("#E05A4F");
        panel.AddTextIntoVertLayout(electionText, true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth - 12f, 10)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        var buttons = panel.BeginHoriGroup(new Vector2(PanelWidth - 12f, 13), TextAnchor.MiddleCenter, 4);
        if (hasPreferred)
            buttons.AddButtonIntoHoriLayout("public_opinion_accept", LM.Get("public_opinion_accept"), () =>
            {
                PublicOpinionSystem.AcceptPreferred(_empire);
                Rebuild();
            }, size: new Vector2(90, 11));
        if (parliament.Exists && !RepublicSystem.IsOneParty(_empire))
            buttons.AddButtonIntoHoriLayout("public_opinion_snap_election", LM.Get("public_opinion_snap_election"), () =>
            {
                PublicOpinionSystem.CallSnapElection(_empire);
                Rebuild();
            }, size: new Vector2(90, 11));
        AddPartyBanControls(panel);
        panel.transform.AddStretchBackground("FactionFrame", new Vector2(PanelWidth, height));
    }

    // 党禁状态、选举制度、独裁度/重开压力，以及手动开关党禁、切换选举制度的按钮
    private void AddPartyBanControls(AutoVertLayoutGroup panel)
    {
        string mode = PartyBanSystem.GetMode(_empire);
        string modeColor = mode == PartyBanSystem.ModeOpen ? "#9EF29E" : "#E9A85B";
        string electoral = !PartyBanSystem.CanChooseElectoralSystem(_empire)
            ? LM.Get("electoral_restricted")
            : LM.Get(PartyBanSystem.UsesDemocraticCentralism(_empire)
                ? "electoral_democratic_centralism" : "electoral_universal_suffrage");
        string metric = mode == PartyBanSystem.ModeOpen
            ? string.Format(LM.Get("party_ban_autocracy"), PartyBanSystem.GetAutocracy(_empire).ToString("0"))
            : string.Format(LM.Get("party_ban_reopen_pressure"),
                PartyBanSystem.GetReopenPressure(_empire).ToString("0"),
                (PartyBanSystem.GetCoreSupport(_empire).support * 100f).ToString("0"));
        string status = $"{LM.Get("party_ban_title")}: {LM.Get($"party_ban_mode_{mode}").ColorString(modeColor)}   " +
                        $"{LM.Get("electoral_title")}: {electoral}   {metric.ColorString("#B8C6CC")}";
        float abolition = RepublicSystem.GetAbolitionPressure(_empire);
        if (abolition > 0f)
            status += "   " + string.Format(LM.Get("republic_abolition_pressure"), abolition.ToString("0"))
                .ColorString(abolition >= 50f ? "#E05A4F" : "#E9A85B");
        panel.AddTextIntoVertLayout(status, true, TextAnchor.MiddleCenter, new Vector2(PanelWidth - 12f, 10))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        var buttons = panel.BeginHoriGroup(new Vector2(PanelWidth - 12f, 13), TextAnchor.MiddleCenter, 4);
        if (PartyBanSystem.IsClosed(_empire))
            buttons.AddButtonIntoHoriLayout("party_ban_open", LM.Get("party_ban_open"), () =>
            {
                PartyBanSystem.OpenManually(_empire);
                Rebuild();
            }, size: new Vector2(90, 11));
        else if (PartyBanSystem.CanCloseManually(_empire, out _, out _))
            buttons.AddButtonIntoHoriLayout("party_ban_close", LM.Get("party_ban_close"), () =>
            {
                PartyBanSystem.CloseManually(_empire);
                Rebuild();
            }, size: new Vector2(90, 11));
        if (PartyBanSystem.CanChooseElectoralSystem(_empire))
            buttons.AddButtonIntoHoriLayout("electoral_toggle", LM.Get(PartyBanSystem.UsesDemocraticCentralism(_empire)
                ? "electoral_switch_to_suffrage" : "electoral_switch_to_centralism"), () =>
            {
                PartyBanSystem.ToggleElectoralSystem(_empire);
                Rebuild();
            }, size: new Vector2(90, 11));
    }

    private void AddSocialUnrest()
    {
        Dictionary<SocialClass, float> shares = InstitutionSystem.BuildClassShares(_empire);
        List<KeyValuePair<SocialClass, float>> tensions = InstitutionSystem.GetClassGrievances(_empire)
            .Where(pair => pair.Value >= 0.5f && shares.TryGetValue(pair.Key, out float share) && share > 0f)
            .OrderByDescending(pair => pair.Value).ToList();
        if (tensions.Count == 0) return;
        InstitutionSocialUnrestConfig config = InstitutionDefinitionRegistry.Global.social_unrest;
        const float height = 30f;
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(6, 6, 4, 4));
        _content.Add(panel.gameObject);
        string entries = string.Join("  ·  ", tensions.Take(4).Select(pair =>
        {
            string color = pair.Value >= config.rebellion_threshold ? "#E05A4F" :
                pair.Value >= 50f ? "#E9A85B" : "#B8C6CC";
            return $"{LM.Get($"class_{pair.Key}")} {pair.Value:0}%".ColorString(color);
        }));
        var head = panel.BeginHoriGroup(new Vector2(PanelWidth - 12f, 11), TextAnchor.MiddleCenter, 3);
        AddIcon(head.transform, Icon("ui/icons/iconWarning", "ui/icons/iconWar"), 9f);
        AddLabel(head, $"{LM.Get("institution_social_tension").ColorString("#E9A85B")}:  {entries}",
            PanelWidth - 30f, 7, TextAnchor.MiddleLeft);
        KeyValuePair<SocialClass, float> highest = tensions[0];
        var cause = panel.AddTextIntoVertLayout(
            string.Format(LM.Get("institution_social_tension_cause"), LM.Get($"class_{highest.Key}"),
                InstitutionSystem.GetSocialGrievanceCause(_empire, highest.Key)).ColorString("#D98C8C"),
            true, TextAnchor.MiddleCenter, new Vector2(PanelWidth - 12f, 10));
        cause.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        panel.transform.AddStretchBackground("FactionFrame", new Vector2(PanelWidth, height));
    }

    // ── 卡片小部件 ──

    private static Sprite Icon(params string[] paths) => UIHelper.FirstSprite(paths);

    private static void AddIcon(Transform parent, Sprite sprite, float size) =>
        UIHelper.AddLayoutIcon(parent, sprite, size);

    private static SimpleText AddLabel(AutoHoriLayoutGroup row, string text, float width, int fontSize,
        TextAnchor anchor, float height = 10f)
    {
        SimpleText label = row.AddTextIntoHoriLayout(text, true, anchor, new Vector2(width, height));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        return label;
    }

    // 顶部总览里的"图标 + 文字"标签
    private static void AddTag(AutoHoriLayoutGroup row, Sprite icon, string text, string hex)
    {
        var tag = row.BeginHoriGroup(new Vector2(120f, 12f), TextAnchor.MiddleCenter, 2);
        AddIcon(tag.transform, icon, 10f);
        AddLabel(tag, text.ColorString(hex), 104f, 8, TextAnchor.MiddleLeft, 12f);
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

    private static AutoVertLayoutGroup BeginCard(AutoHoriLayoutGroup row, float height)
    {
        var card = row.BeginVertGroup(new Vector2(CardWidth, height), pSpacing: CardSpacing,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(6, 6, 5, 5));
        card.transform.AddStretchBackground("FactionFrame", new Vector2(CardWidth, height));
        return card;
    }

    // 卡片标题行：左边图标 + 标题，右边状态
    private static void AddCardHeader(AutoVertLayoutGroup card, Sprite icon, string title, string status)
    {
        var header = card.BeginHoriGroup(new Vector2(CardWidth - 12f, CardHeaderHeight), TextAnchor.MiddleLeft, 3);
        AddIcon(header.transform, icon, 10f);
        float statusWidth = 60f;
        AddLabel(header, title.ColorString("#7FD8EA"), CardWidth - 12f - 13f - statusWidth - 6f, 8,
            TextAnchor.MiddleLeft, CardHeaderHeight);
        AddLabel(header, status, statusWidth, 7, TextAnchor.MiddleRight, CardHeaderHeight);
    }

    private static void AddCardLine(AutoVertLayoutGroup card, string text)
    {
        SimpleText line = card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter,
            new Vector2(CardWidth - 12f, CardLineHeight));
        line.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
    }

    // 一行两个数据小块；小块用 SimpleText 自带的深色内嵌底(windowInnerSliced)，跟族谱信息栏同一套样式。
    // 第二个传 (null, null, null) 表示这一行只有一个。
    private static void AddChipRow(AutoVertLayoutGroup card, (Sprite icon, string label, string value) left,
        (Sprite icon, string label, string value) right)
    {
        // 两边各留几像素，别让右侧那块的数字压到卡片边框上
        float chipWidth = (CardWidth - 12f - 3f) / 2f - 4f;
        var row = card.BeginHoriGroup(new Vector2(CardWidth - 12f, ChipHeight), TextAnchor.MiddleLeft, 3);
        AddChip(row, left, chipWidth);
        if (right.label != null) AddChip(row, right, chipWidth);
    }

    private static void AddChip(AutoHoriLayoutGroup row, (Sprite icon, string label, string value) chip, float width)
    {
        var box = row.BeginHoriGroup(new Vector2(width, ChipHeight), TextAnchor.MiddleLeft, 2);
        UIHelper.AddInsetBackground(box, new Vector2(width, ChipHeight));
        AddIcon(box.transform, chip.icon, 8f);
        AddLabel(box, chip.label.ColorString("#A8B8BE"), width - 44f, 6, TextAnchor.MiddleLeft, ChipHeight);
        AddLabel(box, chip.value ?? "", 30f, 7, TextAnchor.MiddleRight, ChipHeight);
    }

    // ── 这些颜色什么意思 ──
    // 理念路线：四条路线的按钮，当前生效的高亮；没有已研究理念的路线点不动
    private void AddIdeologyRouteRow()
    {
        IdeologyRoute[] routes = Enum.GetValues(typeof(IdeologyRoute)).Cast<IdeologyRoute>().ToArray();
        if (!routes.Any(route => PartySystem.HasResearchedRoute(_culture, route))) return;
        IdeologyRoute? active = PartySystem.GetActiveRoute(_culture);
        var row = _root.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleCenter,
            pSize: new Vector2(PanelWidth, 13));
        _content.Add(row.gameObject);
        var label = row.AddTextIntoHoriLayout(LM.Get("ideology_route_label").ColorString("#7FD8EA"), true,
            TextAnchor.MiddleRight, new Vector2(60, 12));
        label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        foreach (IdeologyRoute route in routes)
        {
            bool researched = PartySystem.HasResearchedRoute(_culture, route);
            bool selected = active == route;
            string hex = selected ? "#F3C34A" : researched ? "#E6E0CF" : "#6F7B80";
            IdeologyRoute target = route;
            var button = row.AddButtonIntoHoriLayout($"ideology_route_{route}",
                PartySystem.GetRouteName(route).ColorString(hex), () =>
                {
                    if (PartySystem.SwitchRoute(_culture, target)) Rebuild();
                }, size: new Vector2(46, 11), showTip: true);
            button.Background.enabled = selected;
        }
    }

    private void AddLegend()
    {
        var row = _root.BeginHoriGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleCenter,
            pSize: new Vector2(PanelWidth, 11));
        _content.Add(row.gameObject);
        AddLegendItem(row, InstitutionNodeStatus.Enacted, "institution_legend_enacted");
        AddLegendItem(row, InstitutionNodeStatus.Available, "institution_legend_available");
        AddLegendItem(row, InstitutionNodeStatus.Forceable, "institution_legend_forceable");
        AddLegendItem(row, InstitutionNodeStatus.Locked, "institution_legend_locked");
        AddLegendItem(row, InstitutionNodeStatus.Superseded, "institution_legend_superseded");
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
        var ideologyRow = section.BeginHoriGroup(new Vector2(PanelWidth, 14), TextAnchor.MiddleCenter, 4);
        ideologyRow.AddButtonIntoHoriLayout("ideology_previous", "◀", () => CycleIdeology(-1),
            size: new Vector2(18, 12));
        ideologyRow.AddTextIntoHoriLayout(PartySystem.GetIdeologyName(_selectedIdeology), true,
            TextAnchor.MiddleCenter, new Vector2(150, 12));
        ideologyRow.AddButtonIntoHoriLayout("ideology_next", "▶", () => CycleIdeology(1),
            size: new Vector2(18, 12));
        PartyIdeology? governingIdeology = PartySystem.GetGovernmentParty(_empire)?.Ideology;
        section.AddTextIntoVertLayout(string.Format(LM.Get("ideology_route_view_status"),
                PartySystem.GetIdeologyName(_selectedIdeology),
                governingIdeology.HasValue ? PartySystem.GetIdeologyName(governingIdeology.Value) : LM.Get("label_none")),
            true, TextAnchor.MiddleCenter, new Vector2(PanelWidth, 12));
        var header = section.BeginHoriGroup(new Vector2(PanelWidth, 13), TextAnchor.MiddleCenter, 2);
        header.AddButtonIntoHoriLayout("window_back", LM.Get("window_back"),
            () => ScrollWindow.showWindow(nameof(EmpireWindow)), size: new Vector2(34, 11));
        header.AddTextIntoHoriLayout(LM.Get("institution_graph_hint").ColorString("#B8C6CC"), true,
            TextAnchor.MiddleLeft, new Vector2(PanelWidth - 78f, 11));
        header.AddButtonIntoHoriLayout("institution_graph_reset", LM.Get("institution_graph_reset"),
            () => _graph?.ResetView(), size: new Vector2(34, 11));
        _graph = InstitutionGraphView.Create(section.transform, new Vector2(PanelWidth, GraphHeight));
        _graph.Rebuild(IdeologyInstitutionPaths.Visible(_lineViews, _selectedIdeology),
            IdeologyInstitutionPaths.Visible(_foreignViews, _selectedIdeology), _selectedId,
            OnNodeSelected, BuildNodeTooltip, BuildNodeCard);
    }

    private void CycleIdeology(int direction)
    {
        PartyIdeology[] values = Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>().ToArray();
        _selectedIdeology = values[(Array.IndexOf(values, _selectedIdeology) + direction + values.Length) % values.Length];
        _selectedId = _lineViews.FirstOrDefault(view => view.Node.branch ==
            IdeologyInstitutionPaths.Branch(_selectedIdeology))?.Node.id ?? "";
        Rebuild();
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
        string techLine = TechnologySystem.DescribeInstitutionTechLine(_culture, view.Node);
        if (techLine != null) lines.Add(techLine.ColorString("#9EF2FF"));

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

        if (view.Status is InstitutionNodeStatus.Enacted or InstitutionNodeStatus.Absorbed
            or InstitutionNodeStatus.Superseded) return;

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
        if (IdeologyInstitutionPaths.TryGetIdeology(branch, out PartyIdeology ideology))
            return PartySystem.GetIdeologyName(ideology);
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
            InstitutionNodeStatus.Superseded => LM.Get("institution_status_superseded"),
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
            InstitutionNodeStatus.Superseded => "#8CA8C8",
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
