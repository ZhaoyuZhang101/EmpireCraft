using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using NeoModLoader.General.UI.Prefabs;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Data;

using UnityEngine.UI;
using EmpireCraft.Scripts.HelperFunc;

using NeoModLoader.General;
using System.Collections;
using DG.Tweening;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 宽窗口(480×380)：帝国窗口是内容最多的窗口，窄窗里财政、派系、行政区都要排长队
    public class EmpireWindow : AbstractWideWindow<EmpireWindow>
    {
        private AutoVertLayoutGroup _root;
        private ScrollWindow TabsWindow => GetComponent<ScrollWindow>();
        // The ruler card is 68 units high with a 72-unit frame. Keep the scroll content just
        // below it; a larger shared padding creates a conspicuous hole on short tabs.
        private TextInput _empireNameInput;
        private Empire _empire;
        private Empire _textInputEmpire;
        private bool _textInputInitialized;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private int _renderGeneration;
        private const int LineagePageSize = 12;
        private readonly List<(string group, EmpireCraftHistory history)> _lineage = new();
        private int _lineagePage;

        private Dictionary<string, Text> _infosTrans = new Dictionary<string, Text>();

        protected override void Init()
        {
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
            // 宽窗口脚本挂在窗口根上：输入框挂窗口根，随窗口显示/隐藏。
            // 旧写法 parent.parent 在宽窗口层级里是常驻 Canvas 根，启动时编辑按钮会挂在屏幕中央。
            // 窗口根带布局组件：输入框必须脱离布局并锚点居中，否则被拉成贯穿窗口的细长条
            _empireNameInput = Instantiate(TextInput.Prefab, this.transform);
            var inputElement = _empireNameInput.gameObject.GetComponent<LayoutElement>()
                               ?? _empireNameInput.gameObject.AddComponent<LayoutElement>();
            inputElement.ignoreLayout = true;
            var inputRect = (RectTransform)_empireNameInput.transform;
            inputRect.anchorMin = inputRect.anchorMax = inputRect.pivot = new Vector2(0.5f, 0.5f);
            _empireNameInput.Setup("", name_change);
        }
        public void Clear()
        {
            _renderGeneration++;
            foreach (var container in _groups)
            {
                if (container.Value == null) continue;
                container.Value.SetActive(false);
                Destroy(container.Value);
            }
            _groups.Clear();
        }
        private void InitialTabButtons()
        {
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_finance"))
            {
                var financeTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                financeTab.Setup("empire_finance", TabsWindow, action: ShowFinance,
                    sprite: SpriteTextureLoader.getSprite("ui/icons/iconMoney"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_controlled_kingdoms"))
            {
                var kingdomsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                kingdomsWindowTab.Setup("empire_controlled_kingdoms", this.TabsWindow, action:ShowKingdomListHelp,
                    sprite: SpriteTextureLoader.getSprite("SplitAllUnderHeaven"));
            }

            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_dependencies"))
            {
                var dependenciesTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                dependenciesTab.Setup("empire_dependencies", this.TabsWindow, action: ShowDependencies,
                    sprite: SpriteTextureLoader.getSprite("TabDependencies"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "past_emperors"))
            {
                var pastEmperorsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                pastEmperorsWindowTab.Setup("past_emperors", this.TabsWindow, action:ShowEmperors, sprite:SpriteTextureLoader.getSprite("TabDynasty"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_bureau"))
            {
                var bureauWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                bureauWindowTab.Setup("empire_bureau", this.TabsWindow, action:ShowBureau, sprite:SpriteTextureLoader.getSprite("TabBureau"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_setting"))
            {
                var settingWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                settingWindowTab.Setup("empire_setting", this.TabsWindow, action:OpenEmpireSettingWindow,
                    sprite: SpriteTextureLoader.getSprite("TabSetting"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_constitution"))
            {
                var constitutionTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                constitutionTab.Setup("empire_constitution", this.TabsWindow, action: ShowConstitution,
                    sprite: SpriteTextureLoader.getSprite("TabConstitution"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_institutions"))
            {
                var institutionTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                institutionTab.Setup("empire_institutions", this.TabsWindow,
                    action: OpenInstitutionWindow, sprite: SpriteTextureLoader.getSprite("TabInstitutions"));
            }
        }

        private void ShowKingdomListHelp(WindowMetaTab pArg0)
        {
            StartCoroutine(ShowKingdomList());
        }
        private void OpenEmpireSettingWindow(WindowMetaTab pArg0)
        {
            ScrollWindow.showWindow(nameof(EmpireSettingWindow));
        }

        private void OpenInstitutionWindow(WindowMetaTab pArg0)
        {
            ScrollWindow.showWindow(nameof(InstitutionWindow));
        }

        public void ShowTopPart()
        {
            InitialTextInput();
            InitialTopPartInfo();
        }
        //顶部信息栏
        private void InitialTopPartInfo()
        {
            //总容器
            var topSpace = _root.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter,
                pSize: new Vector2(UIHelper.WidePanelWidth, 78), pPadding: new RectOffset(8, 8, 4, 4));
            // 一建好就登记：后面任何一项绘制出错，Clear() 也能把它删掉，
            // 否则每次刷新都会多留一块残缺的顶部栏，越叠越多
            AddIntoGroup("top_space", topSpace.gameObject);
            try
            {
                DrawTopPartInfo(topSpace);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口顶部信息绘制失败({_empire?.data?.name}): {exception}");
            }
        }

        private void DrawTopPartInfo(AutoHoriLayoutGroup topSpace)
        {
            topSpace.transform.AddStretchBackground("clanFrame", new Vector2(UIHelper.WidePanelWidth, 78));

            //左侧信息栏：国号大字 + 政体·文化 + 皇室/执政党 + 传承
            var leftPart = topSpace.BeginVertGroup(new Vector2(156, 64), pSpacing: 0,
                pAlignment:TextAnchor.MiddleLeft, pPadding:new RectOffset(5, 5, 3, 3));
            UIHelper.AddInsetBackground(leftPart, new Vector2(156, 64));
            bool republic = RepublicSystem.IsRepublic(_empire);
            leftPart.AddTextIntoVertLayout(_empire.GetEmpireFullName().ColorString("#F3C34A"), true,
                TextAnchor.MiddleLeft, new Vector2(146, 16)).UseFixedFontSize(11, HorizontalWrapMode.Overflow);
            leftPart.AddTextIntoVertLayout(
                ($"{CompositeEmpireService.GetRegimeName(_empire.CoreKingdom.GetRegime().type)} · " +
                 $"{GetCultureDisplayName(CultureService.GetRealmCulture(_empire.CoreKingdom))}").ColorString("#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(146, 10)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            string rulerLine;
            if (republic)
            {
                // 共和国：没有皇室，改为执政党
                FixedFaction governing = ParliamentSystem.GetGoverningFaction(_empire) ??
                                         _empire.CoreKingdom.GetRegime()?.GetDominateFaction();
                rulerLine = $"{LM.Get("republic_ruling_party").ColorString("#8FA0A8")}: {(governing?.Name ?? LM.Get("label_none")).ColorString("#C9A7E8")}";
            }
            else
                rulerLine = $"{LM.Get("empire_clan").ColorString("#8FA0A8")}: {(_empire.EmpireSpecificClan?.name??""+ " " + LM.Get("Clan")).ColorString(_empire.EmpireSpecificClan?.color??"#FFFFFF")}";
            leftPart.AddTextIntoVertLayout(rulerLine, true, TextAnchor.MiddleLeft, new Vector2(146, 12))
                .UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            string tenureLine = republic
                ? string.Format(LM.Get("republic_years_format"), RepublicSystem.GetRepublicYears(_empire))
                : "format_past_emperor".LocalFormat(_empire?.data?.history_emperrors?.Count??0);
            leftPart.AddTextIntoVertLayout(tenureLine, true, TextAnchor.MiddleLeft, new Vector2(146, 12))
                .UseFixedFontSize(8, HorizontalWrapMode.Overflow);

            // 人物区固定为 hori(继任者, vert(皇帝, 权臣), 皇后)。
            var avatarRow = topSpace.BeginHoriGroup(pSpacing: 0, pAlignment: TextAnchor.MiddleCenter,
                pSize: new Vector2(92, 62));
            // 共和国没有储君、皇后这些位置
            if (!republic)
                avatarRow.AddActorViewIntoHoriLayout(_empire.CoreKingdom.GetHeir(),
                    description:LM.Get("empire_heir").ColorString(pColor:new Color(0.8f,0.0f,1f)));

            //中央信息栏
            var centerPart = avatarRow.BeginVertGroup(new Vector2(30, 60), pSpacing:0,
                pAlignment:TextAnchor.MiddleCenter);
            string emperorTitle = (republic
                    ? GeneralSystems.RepublicSystem.GetHeadOfStateTitle(_empire)
                    : LM.Get(_empire.Emperor?.isSexFemale() == true ? "actor_emperor_G" : "actor_emperor_B"))
                .ColorString(pColor:new Color(1,0.8f,0));
            if (_empire.data.is_been_controlled)
            {
                emperorTitle += "\n" + LM.Get("powerful_minister_status_puppet").ColorString(pColor:new Color(0.65f,0.75f,0.85f));
            }
            centerPart.AddActorViewIntoVertLayout(_empire.Emperor, description:emperorTitle);
            if (republic)
            {
                // 共和国元首就是执政党领袖，下面改放反对党领袖
                centerPart.AddActorViewIntoVertLayout(RepublicSystem.GetOppositionLeader(_empire),
                    description:LM.Get("republic_opposition_leader").ColorString(pColor:new Color(0.35f,0.85f,1f)));
            }
            else if (ParliamentSystem.HasParliament(_empire))
            {
                // 议会存续期间，权臣的位置改由议会选出的总理大臣占据
                AddPrimeMinisterView(centerPart);
            }
            else
            {
                Actor powerfulMinister = _empire.GetPowerfulMinister();
                string powerfulMinisterDescription = LM.Get(_empire.data.powerful_minister_is_empress_dowager
                        ? "empress_dowager_title" : "powerful_minister_title")
                    .ColorString(pColor:new Color(1f,0.55f,0.1f));
                string powerfulMinisterStatus = _empire.GetPowerfulMinisterStatusText();
                if (!string.IsNullOrWhiteSpace(powerfulMinisterStatus))
                {
                    powerfulMinisterDescription += "\n" +
                        powerfulMinisterStatus.ColorString(pColor:new Color(0.1f,1f,0.8f));
                }
                centerPart.AddActorViewIntoVertLayout(powerfulMinister, description:powerfulMinisterDescription);
            }

            if (!republic)
            {
                Actor lover = _empire.Emperor?.lover;
                avatarRow.AddActorViewIntoHoriLayout(lover,
                    description:LM.Get(lover?.isSexFemale() == false ? "empire_lover_B" : "empire_lover_G")
                        .ColorString(pColor:new Color(1f,0.1f,0.5f)));
            }

            //右侧信息栏：纪年 + 2×2 关键指标格
            var rightPart = topSpace.BeginVertGroup(new Vector2(156, 64), pSpacing: 1,
                pAlignment:TextAnchor.UpperCenter, pPadding:new RectOffset(5, 5, 3, 3));
            UIHelper.AddInsetBackground(rightPart, new Vector2(156, 64));
            // 共和国显示其选定的纪年方式。
            string yearText = republic
                ? RepublicSystem.GetCalendarText(_empire)
                : _empire.GetYearNameWithTime();
            rightPart.AddTextIntoVertLayout(yearText.ColorString(pColor:_empire.getColor()._color_text), true,
                TextAnchor.MiddleCenter, new Vector2(146, 12)).UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            var metricsRow1 = rightPart.BeginHoriGroup(new Vector2(146, 22), TextAnchor.MiddleCenter, 2);
            UIHelper.AddMetricCell(metricsRow1, 72, LM.Get("i_population"), $"{_empire.CountPopulation()}/{_empire.countMaxPopulation()}");
            UIHelper.AddMetricCell(metricsRow1, 72, LM.Get("national_power"), $"{_empire.GetNationalPower():0.##}");
            var metricsRow2 = rightPart.BeginHoriGroup(new Vector2(146, 22), TextAnchor.MiddleCenter, 2);
            UIHelper.AddMetricCell(metricsRow2, 72, LM.Get("i_age"), _empire.CoreKingdom.getAge().ToString());
            UIHelper.AddMetricCell(metricsRow2, 72, LM.Get("official_students_num"),
                $"{_empire.GetMembersWithTrait("jingshi").Count.ToString().ColorString("#E16A54")}/" +
                $"{_empire.GetMembersWithTrait("gongshi").Count.ToString().ColorString("#CB9DF0")}/" +
                $"{_empire.GetMembersWithTrait("juren").Count.ToString().ColorString("#A2D2DF")}");

            RectTransform topRect = topSpace.GetComponent<RectTransform>();
            topRect.sizeDelta = new Vector2(UIHelper.WidePanelWidth, 78);
            _empireNameInput?.transform.SetAsLastSibling();
            // 窗口开着时国号变了也跟着刷新；玩家正在输入时不打断
            if (_empireNameInput != null && !_empireNameInput.input.isFocused &&
                _empireNameInput.input.text != _empire.GetEmpireFullName())
                _empireNameInput.input.text = _empire.GetEmpireFullName();

            AddIntoGroup("top_space", topSpace.gameObject);
        }


        private void AddPrimeMinisterView(AutoVertLayoutGroup parent)
        {
            ParliamentView parliament = ParliamentSystem.GetView(_empire);
            string description = LM.Get("prime_minister_title").ColorString(pColor:new Color(0.35f,0.85f,1f));
            if (parliament.PrimeMinister != null && !string.IsNullOrWhiteSpace(parliament.GovernmentType))
            {
                description += "\n" + string.Format(LM.Get("prime_minister_status"),
                        LM.Get($"parliament_government_{parliament.GovernmentType}"),
                        parliament.GovernmentSeats, parliament.TotalSeats)
                    .ColorString(pColor:new Color(0.1f,1f,0.8f));
            }
            else
            {
                description += "\n" + LM.Get("prime_minister_vacant").ColorString(pColor:Color.gray);
            }
            parent.AddActorViewIntoVertLayout(parliament.PrimeMinister, description:description);
        }

        private void InitialTextInput()
        {
            string text = _empire.GetEmpireFullName();
            this.transform.GenerateTextInput(offset:new Vector2(0, 218), default_text:text, input:_empireNameInput);
        }

        // Initialize the text input once. Re-opening the same empire must not overwrite
        // what the player is currently typing; switching to another empire should sync it.
        private void SyncEmpireNameInput(bool forceValueSync = false)
        {
            if (_empireNameInput == null || _empire == null) return;

            bool empireChanged = _textInputEmpire != _empire;

            if (!_textInputInitialized)
            {
                InitialTextInput();
                _textInputInitialized = true;
                _textInputEmpire = _empire;
                return;
            }

            if (forceValueSync || empireChanged)
            {
                _empireNameInput.input.text = _empire.GetEmpireFullName();
                _textInputEmpire = _empire;
            }

            _empireNameInput.transform.SetAsLastSibling();
        }

        //显示势力范围
        public IEnumerator ShowKingdomList()
        {
            Clear();
            if (_empire?.CoreKingdom == null || _empire.CoreKingdom.isRekt()) yield break;
            int renderGeneration = _renderGeneration;
            Empire renderedEmpire = _empire;
            InitialTopPartInfo();
            var parent = CommonInitial("empire_controlled_kingdoms");
            if (parent == null) yield break;
            yield return CoroutineHelper.wait_for_next_frame;
            if (!IsCurrentRender(renderGeneration, renderedEmpire, parent)) yield break;
            try
            {
                // 派系卡片只说"支持/反对几项"，具体现在到底在推哪一项、推到百分之多少，
                // 得单独报一行——不然玩家看着一堆"倾向"数字，却不知道眼下真正在发生什么。
                InstitutionReformState activeReform = renderedEmpire.data.institution_state?.active_reform;
                if (activeReform != null)
                {
                    InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(activeReform.node_id);
                    string reformText = string.Format(LM.Get("empire_active_reform"),
                        InstitutionSystem.GetNodeName(node), activeReform.progress.ToString("0.0"));
                    var reformCard = parent.BeginCard("empire_active_reform_card", "#65D6C4", null, "ui/icons/iconKnowledge");
                    var reformLabel = reformCard.AddTextIntoVertLayout(reformText.ColorString("#65D6C4"), true,
                        TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 20f, 22));
                    reformLabel.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
                }
                UIHelper.InitialEmpireFactionSpace(parent.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter),
                    renderedEmpire.CoreKingdom);
                AddClassAlignmentOverview(parent, renderedEmpire);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国派系界面绘制失败，继续显示行政区: {exception}");
            }
            yield return CoroutineHelper.wait_for_next_frame;
            if (!IsCurrentRender(renderGeneration, renderedEmpire, parent)) yield break;
            AddCompositeEmpireStatus(parent, renderedEmpire);
            yield return RefreshScrollLayout(resetToTop: true);
        }

        private void AddClassAlignmentOverview(AutoVertLayoutGroup parent, Empire empire)
        {
            if (parent == null || empire?.CoreKingdom == null) return;
            List<FixedFaction> factions = empire.CoreKingdom.GetRegime()?.GetPlayerFactions()
                ?.Where(faction => faction != null && !faction.Ban).ToList() ?? new List<FixedFaction>();
            FactionClassSystem.EnsureEmpireProfiles(empire, factions);
            Dictionary<SocialClass, float> classShares = InstitutionSystem.BuildClassShares(empire);
            // 占比四舍五入后为 0% 的阶层不显示；面板高度按实际行数计算，避免最后一行被挤出框外。
            List<SocialClass> classes = Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
                .Where(socialClass => classShares.TryGetValue(socialClass, out float share) && share * 100f >= 0.5f)
                .ToList();
            int rowCount = (classes.Count + 2) / 3;
            float panelHeight = 4 + 16 + 10 + rowCount * 21 + (rowCount + 1);

            var panel = parent.BeginVertGroup(new Vector2(UIHelper.WidePanelWidth, panelHeight), pSpacing: 1,
                pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));
            UIHelper.AddCardTitle(panel, UIHelper.WidePanelWidth, LM.Get("empire_class_alignment"), "#7FD8EA", null,
                "ui/icons/iconPopulation20");
            var hint = panel.AddTextIntoVertLayout(LM.Get("empire_class_alignment_hint").ColorString("#8FA0A8"),
                true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
            hint.UseFixedFontSize(6, HorizontalWrapMode.Overflow);

            for (int index = 0; index < classes.Count; index += 3)
            {
                var row = panel.BeginHoriGroup(new Vector2(UIHelper.WidePanelWidth - 12f, 21), TextAnchor.MiddleCenter, 2);
                for (int column = 0; column < 3 && index + column < classes.Count; column++)
                    AddClassAlignmentCell(row, classes[index + column], classShares, factions);
            }
            panel.transform.AddStretchBackground("regimeFrame", new Vector2(UIHelper.WidePanelWidth, panelHeight));
        }

        private static void AddClassAlignmentCell(AutoHoriLayoutGroup row, SocialClass socialClass,
            Dictionary<SocialClass, float> classShares, List<FixedFaction> factions)
        {
            float population = classShares.TryGetValue(socialClass, out float share) ? share * 100f : 0f;
            FixedFaction preferred = factions.OrderByDescending(faction =>
                    faction.ClassSupport.TryGetValue(socialClass, out float support) ? support : 0f)
                .FirstOrDefault();
            float allegiance = preferred != null && preferred.ClassSupport.TryGetValue(socialClass, out float value)
                ? value
                : 0f;
            string preference = preferred == null
                ? LM.Get("empire_class_unaligned")
                : string.Format(LM.Get("empire_class_alignment_value"), preferred.Name, allegiance);
            string preferenceColor = allegiance >= 50f ? "#65D66E" : "#B8C6CC";

            var cell = row.BeginVertGroup(new Vector2(138, 20), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(3, 2, 0, 0));
            var heading = cell.AddTextIntoVertLayout(
                $"{socialClass.ToTranslate()}  {population:0}%".ColorString("#F3C34A"), true,
                TextAnchor.MiddleLeft, new Vector2(133, 9));
            heading.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var valueLabel = cell.AddTextIntoVertLayout(preference.ColorString(preferenceColor), true,
                TextAnchor.MiddleLeft, new Vector2(133, 9));
            valueLabel.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(valueLabel);
            cell.transform.AddStretchBackground("FactionFrame", new Vector2(138, 20));
        }

        private void AddCompositeEmpireStatus(AutoVertLayoutGroup parent, Empire empire)
        {
            if (parent == null || empire?.data == null) return;

            bool composite = CompositeEmpireService.IsComposite(empire);
            Actor emperor = empire.Emperor;
            PlotAsset adoptionPlot = AssetManager.plots_library?.basic_plots?
                .Find(plot => plot?.id == "adopt_central_plains_institutions");
            bool adoptionInProgress = adoptionPlot != null && emperor?.plot?.isSameType(adoptionPlot) == true;
            CompositeEmpireService.AdoptionStatus adoption = composite
                ? null
                : CompositeEmpireService.GetAdoptionStatus(empire);
            // 只有游牧统治传统的帝国才可能走上"复合帝国"这条路——其它帝国这块面板本来就用不上，
            // 与其显示一句"仅具有游牧统治传统的帝国可以..."占地方，不如这块面板直接不显示；
            // 统治文化/当前制度这两条基础信息已经挪到顶部信息栏，不会因为面板不显示就看不到。
            bool rulingCultureIsNomadic = composite || (adoption != null &&
                OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(adoption.RulingCulture, out Setting rulingSetting) &&
                rulingSetting.regime == RegimeType.YouMu);
            if (!rulingCultureIsNomadic) return;
            bool hasCulturalName = composite && empire.data.composite_cultural_name_adopted &&
                                   !string.IsNullOrWhiteSpace(empire.data.composite_cultural_name);
            float height = composite ? hasCulturalName ? 166f : 154f :
                adoption?.HasInstitutionalTarget == true ? 104f : 82f;
            var panel = parent.BeginVertGroup(new Vector2(UIHelper.WidePanelWidth, height), pSpacing: 1,
                pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));

            string titleState = composite
                ? CompositeEmpireService.GetStageName(empire)
                : adoptionInProgress
                    ? LM.Get("composite_empire_adoption_plotting")
                    : LM.Get("composite_empire_stage_None");
            panel.AddTextIntoVertLayout(
                $"{LM.Get("composite_empire_identity_title")} · {titleState}".ColorString(
                    pColor: composite ? new Color(0.25f, 0.9f, 0.8f) : new Color(0.85f, 0.75f, 0.35f)),
                true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 12));

            if (composite)
            {
                string ruling = GetCultureDisplayName(CompositeEmpireService.GetRulingCulture(empire));
                string institution = GetCultureDisplayName(CompositeEmpireService.GetInstitutionalCulture(empire));
                string externalIdentity = GetCultureDisplayName(empire.data.external_identity_culture);
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_ruling_culture")}: {ruling.ColorString("#F3C34A")}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_court_culture")}: {institution.ColorString("#65D6C4")}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_court_system")}: {CompositeEmpireService.GetInstitutionalRegimeName(empire)}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                if (hasCulturalName)
                {
                    string nameCulture = GetCultureDisplayName(empire.data.composite_cultural_name_culture);
                    panel.AddTextIntoVertLayout(
                        $"{LM.Get("composite_empire_cultural_name")}: {empire.data.composite_cultural_name.ColorString("#F3C34A")} ({nameCulture})",
                        true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                }
                panel.AddTextIntoVertLayout(
                    LM.Get("composite_empire_regional_systems"),
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(CompositeEmpireService.GetRegionalInstitutionSummary(empire),
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 30));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_military_tradition")}: {CompositeEmpireService.GetMilitaryTraditionName(empire).ColorString("#E9A85B")}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_external_identity")}: {externalIdentity}  |  {LM.Get("composite_empire_integration")}: {empire.data.composite_integration}%",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_dual_legitimacy")}: {empire.data.central_plains_legitimacy}/{empire.data.ruling_tradition_legitimacy}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
            }
            else if (adoption != null)
            {
                string ruling = GetCultureDisplayName(adoption.RulingCulture);
                string institution = adoption.HasInstitutionalTarget
                    ? GetCultureDisplayName(adoption.InstitutionalCulture)
                    : LM.Get("label_none");
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_ruling_culture")}: {ruling.ColorString("#F3C34A")}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_current_system")}: {CompositeEmpireService.GetMilitaryTraditionName(empire)}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_candidate_culture")}: {institution.ColorString("#65D6C4")}",
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 10));

                // 走到这里已经确认过统治文化是游牧传统了(见方法开头的 rulingCultureIsNomadic
                // 提前返回)，"不是游牧传统"这条分支不会再触发，删掉。
                string requirement;
                if (!adoption.HasInstitutionalTarget)
                {
                    requirement = LM.Get("composite_empire_adoption_no_core");
                }
                else if (!adoption.HasDistinctCultures)
                {
                    requirement = LM.Get("composite_empire_adoption_same_culture");
                }
                else
                {
                    int requiredTitles = (adoption.TotalTitles + 1) / 2;
                    string controlled = adoption.ControlledTitles.ToString().ColorString(
                        adoption.HasRequiredControl ? "#65D66E" : "#FF6B6B");
                    string mandate = adoption.Mandate.ToString().ColorString(
                        adoption.HasRequiredMandate ? "#65D66E" : "#FF6B6B");
                    requirement = string.Format(LM.Get("composite_empire_adoption_title_progress"),
                                      controlled, adoption.TotalTitles, requiredTitles) + "\n" +
                                  string.Format(LM.Get("composite_empire_adoption_mandate_progress"), mandate);
                }
                panel.AddTextIntoVertLayout(requirement, true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 20));

                if (adoptionInProgress)
                {
                    panel.AddTextIntoVertLayout(LM.Get("composite_empire_adoption_plotting").ColorString("#65D6C4"),
                        true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 11));
                }
                else if (adoption.IsEligible)
                {
                    panel.AddButtonIntoVertLayout("start_composite_empire_adoption",
                        LM.Get("composite_empire_adoption_start"), () => StartCompositeEmpireAdoption(empire),
                        SpriteTextureLoader.getSprite("ChineseCrown"), size: new Vector2(188, 15), showTip: false);
                }
            }

            panel.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(UIHelper.WidePanelWidth, height));
        }

        private static string GetCultureDisplayName(string culture)
        {
            return CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : LM.Get("label_none");
        }

        private void StartCompositeEmpireAdoption(Empire empire)
        {
            Actor emperor = empire?.Emperor;
            PlotAsset plot = AssetManager.plots_library?.basic_plots?
                .Find(asset => asset?.id == "adopt_central_plains_institutions");
            if (emperor == null || plot?.try_to_start_advanced == null ||
                !CompositeEmpireService.CanAdoptCentralInstitutions(emperor)) return;
            if (plot.try_to_start_advanced(emperor, plot, true))
            {
                TranslateHelper.LogCompositeEmpireAdoptionStarted(empire,
                    CompositeEmpireService.GetAdoptionStatus(empire));
                StartCoroutine(ShowKingdomList());
            }
        }
        //显示君主世系
        public void ShowEmperors(WindowMetaTab pArg0)
        {
            _lineage.Clear();
            _lineagePage = 0;
            // 一次分组，保留朝代首次出现的顺序；不再每个朝代重扫完整历史。
            var groups = new Dictionary<string, List<EmpireCraftHistory>>();
            var order = new List<string>();
            foreach (EmpireCraftHistory history in _empire.data.history)
            {
                if (history == null || string.IsNullOrEmpty(history.emperor)) continue;
                string group = HistoryGroupKey(history) ?? "";
                if (!groups.TryGetValue(group, out var entries))
                {
                    groups[group] = entries = new List<EmpireCraftHistory>();
                    order.Add(group);
                }
                entries.Add(history);
            }
            foreach (string group in order)
                foreach (EmpireCraftHistory history in groups[group]) _lineage.Add((group, history));
            ShowLineagePage();
        }

        private void ShowLineagePage()
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("past_emperors");
            int pages = Math.Max(1, (_lineage.Count + LineagePageSize - 1) / LineagePageSize);
            _lineagePage = Math.Max(0, Math.Min(pages - 1, _lineagePage));
            var navigation = parent.BeginHoriGroup(new Vector2(UIHelper.WidePanelWidth - 12f, 18f),
                TextAnchor.MiddleCenter, 8f);
            navigation.AddButtonIntoHoriLayout("lineage_previous", LM.Get("lineage_previous"), () =>
            {
                if (_lineagePage > 0) { _lineagePage--; ShowLineagePage(); }
            }, size: new Vector2(62f, 16f));
            navigation.AddTextIntoHoriLayout(string.Format(LM.Get("lineage_page"), _lineagePage + 1,
                pages, _lineage.Count), true, TextAnchor.MiddleCenter, new Vector2(230f, 16f));
            navigation.AddButtonIntoHoriLayout("lineage_next", LM.Get("lineage_next"), () =>
            {
                if (_lineagePage + 1 < pages) { _lineagePage++; ShowLineagePage(); }
            }, size: new Vector2(62f, 16f));

            bool republicNow = RepublicSystem.IsRepublic(_empire);
            string reignTitle = republicNow
                ? _empire.GetEmpireFullName() + RepublicSystem.GetHeadOfStateTitle(_empire)
                : _empire.GetEmpireName() + (_empire.data.has_year_name ? _empire.data.year_name : "") + LM.Get("emperor");
            var current = parent.BeginCard(republicNow ? "current_head_of_state" : "current_emperor",
                headerValue: _empire.Emperor != null ? reignTitle : null, icons: "ui/icons/iconKings");
            EmpireCraftStatsRow currentRows = current.AddComponent<EmpireCraftStatsRow>();
            currentRows.WideLayout = true;
            currentRows.IShowStatsRow(republicNow ? "current_head_of_state" : "current_emperor",
                _empire.Emperor?.name ?? LM.Get("label_none"), _empire.getColor().color_text,
                pIconPath: "iconKings", action: () => OpenHistoryWindow(_empire.data.currentHistory));
            StartCoroutine(ShowStatsRowsAndRefresh(currentRows));
            StartCoroutine(RenderLineagePage(parent, _renderGeneration, _empire));
        }

        private IEnumerator RenderLineagePage(AutoVertLayoutGroup parent, int generation, Empire empire)
        {
            string lastGroup = null;
            int end = Math.Min(_lineage.Count, (_lineagePage + 1) * LineagePageSize);
            for (int i = _lineagePage * LineagePageSize; i < end; i++)
            {
                if (!IsCurrentRender(generation, empire, parent)) yield break;
                var entry = _lineage[i];
                if (entry.group != lastGroup)
                {
                    parent.AddSectionHeader(entry.group == RepublicGroupKey ? RepublicGroupTitle() : entry.group,
                        "#F3C34A", false, "ui/icons/iconCrown");
                    lastGroup = entry.group;
                }
                var card = parent.BeginCard();
                EmpireCraftStatsRow rows = card.AddComponent<EmpireCraftStatsRow>();
                rows.WideLayout = true;
                ListPastEmperor(rows, entry.history);
                yield return rows.showRows();
                yield return CoroutineHelper.wait_for_next_frame;
            }
            if (IsCurrentRender(generation, empire, parent)) yield return RefreshScrollLayout(true);
        }
        public void ShowFinance(WindowMetaTab pArg0)
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("empire_finance");
            Kingdom core = _empire?.CoreKingdom;
            if (!TreasurySystem.Enabled(core)) return;
            string color = _empire.getColor().color_text;
            TreasuryReport report = TreasurySystem.Report(core);
            // 宽窗口排版：左栏国库存量、右栏月度流量并排对照；全国合并口径压轴全宽
            var columns = parent.BeginColumns();
            var treasuryCard = columns.BeginColumnCard("fiscal_section_treasury", headerValue: core.GetKingdomName(),
                icons: "ui/icons/iconMoney");
            treasuryCard.AddHeroStat(UIHelper.CardColumnWidth, LM.Get("label_national_treasury"),
                MoneyDisplay.Format(core.GetTreasuryBalance()));
            EmpireCraftStatsRow treasuryRows = treasuryCard.AddComponent<EmpireCraftStatsRow>();
            treasuryRows.WideLayout = true;
            treasuryRows.IShowStatsRow("fiscal_available", MoneyDisplay.Format(StateSettlementSystem.DiscretionaryBalance(core)), color);
            treasuryRows.IShowStatsRow("fiscal_safety_reserve", MoneyDisplay.Format(TreasurySystem.SafetyReserve(core)), "#E6A166");
            treasuryRows.IShowStatsRow("fiscal_cash_balance", MoneyDisplay.Format(report.cash_balance),
                report.cash_balance < 0 ? "#FF6666" : "#65D66E");

            var monthlyCard = columns.BeginColumnCard("fiscal_section_monthly",
                headerValue: $"{LM.Get("fiscal_recorded_months")}: {report.months}", icons: "ui/icons/iconClock");
            monthlyCard.AddHeroStat(UIHelper.CardColumnWidth, LM.Get("fiscal_operating_balance"),
                MoneyDisplay.Format(report.operating_balance), report.operating_balance < 0 ? "#FF6666" : "#65D66E");
            EmpireCraftStatsRow monthlyRows = monthlyCard.AddComponent<EmpireCraftStatsRow>();
            monthlyRows.WideLayout = true;
            monthlyRows.IShowStatsRow("fiscal_income", MoneyDisplay.Format(report.income), "#65D66E");
            monthlyRows.IShowStatsRow("fiscal_expense", MoneyDisplay.Format(report.expense), "#E9A85B");
            foreach (var item in TreasurySystem.Details(report))
                monthlyRows.IShowStatsRow(item.key, item.value, color, pIconPath: "iconMoney");

            TreasuryReport realm = TreasurySystem.Consolidated(core);
            var realmCard = parent.BeginCard("fiscal_section_realm", headerValue: TreasurySystem.FlowText(realm),
                icons: "ui/icons/iconKingdom");
            realmCard.AddHeroStat(UIHelper.WidePanelWidth, LM.Get("fiscal_realm_operating_balance"),
                MoneyDisplay.Format(realm.operating_balance), realm.operating_balance < 0 ? "#FF6666" : "#65D66E");
            EmpireCraftStatsRow realmRows = realmCard.AddComponent<EmpireCraftStatsRow>();
            realmRows.WideLayout = true;
            realmRows.IShowStatsRow("fiscal_realm_cash_balance", MoneyDisplay.Format(realm.cash_balance),
                realm.cash_balance < 0 ? "#FF6666" : "#65D66E");
            realmRows.IShowStatsRow("fiscal_realm_debt_repayment", MoneyDisplay.Format(realm.debt_repayment), "#E9A85B");
            var hint = parent.AddTextIntoVertLayout(LM.Get("fiscal_account_hint").ColorString("#8FA0A8"), true,
                TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 16f, 26f));
            hint.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
            StartCoroutine(ShowCardRows(treasuryRows, monthlyRows, realmRows));
        }

        // 属国页签：朝贡国与核心王国的封建附庸(陪臣缩进一层)，显示控制等级与年贡，点行打开对应国家窗口
        public void ShowDependencies(WindowMetaTab pArg0)
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("empire_dependencies");
            var tributaries = World.world.kingdoms.list
                .Where(k => k != null && !k.isRekt() && k.GetTakenAllianceEmpire() == _empire)
                .OrderByDescending(TributeEstimate).ToList();
            Kingdom core = _empire?.CoreKingdom;
            var vassals = core != null && !core.isRekt()
                ? FeudalVassalService.GetDirectVassals(core).ToList() : new List<Kingdom>();
            // 宽窗口排版：朝贡国与封建附庸左右并排对照，标题条右侧直接给数量
            var columns = parent.BeginColumns();
            var tributaryCard = columns.BeginColumnCard("empire_tributaries", "#7FD8EA", tributaries.Count.ToString(), "ui/icons/iconKings");
            EmpireCraftStatsRow tributaryRows = tributaryCard.AddComponent<EmpireCraftStatsRow>();
            tributaryRows.WideLayout = true;
            foreach (Kingdom tributary in tributaries)
                AddDependencyRow(tributaryRows, tributary, string.Format(LM.Get("dependency_tribute_value"),
                    MoneyDisplay.Format(TributeEstimate(tributary))));
            if (tributaries.Count == 0) AddCardEmptyNote(tributaryCard);

            var vassalCard = columns.BeginColumnCard("empire_feudal_vassals", "#7FD8EA", vassals.Count.ToString(), "ui/icons/iconCrown");
            EmpireCraftStatsRow vassalRows = vassalCard.AddComponent<EmpireCraftStatsRow>();
            vassalRows.WideLayout = true;
            int shown = 0;
            foreach (Kingdom vassal in vassals) shown += AddVassalRows(vassalRows, vassal, 0);
            if (shown == 0) AddCardEmptyNote(vassalCard);
            StartCoroutine(ShowCardRows(tributaryRows, vassalRows));
        }

        private static void AddCardEmptyNote(AutoVertLayoutGroup card)
        {
            card.AddTextIntoVertLayout(LM.Get("label_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleCenter,
                new Vector2(UIHelper.CardColumnWidth - 8f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        }

        // 封建附庸一行(陪臣缩进)，接着递归它自己的附庸；层级上限与 FeudalVassalService 的三层一致
        private int AddVassalRows(EmpireCraftStatsRow rows, Kingdom vassal, int depth)
        {
            if (vassal == null || vassal.isRekt() || depth > 2) return 0;
            string value = string.Format(LM.Get("dependency_vassal_value"),
                Math.Min(3, Math.Max(1, vassal.GetOrCreate().feudal_vassal_level)),
                MoneyDisplay.Format(FeudalVassalService.YearlyTribute(vassal)));
            AddDependencyRow(rows, vassal, value,
                (depth > 0 ? new string('　', depth) + "└" : "") + vassal.GetKingdomName());
            int count = 1;
            foreach (Kingdom sub in FeudalVassalService.GetDirectVassals(vassal))
                count += AddVassalRows(rows, sub, depth + 1);
            return count;
        }

        private void AddDependencyRow(EmpireCraftStatsRow rows, Kingdom kingdom, string value, string name = null)
        {
            rows.IShowStatsRow(name ?? kingdom.GetKingdomName(), value, kingdom.getColor().color_text,
                pIconPath: "iconKings", action: () =>
                {
                    if (kingdom == null || kingdom.isRekt()) return;
                    SelectedMetas.selected_kingdom = kingdom;
                    ScrollWindow.showWindow("kingdom");
                });
        }

        // 朝贡年贡估计(与 StartToTaken 同口径：户数的一半；财政下实缴受国库限制，这里是名义值)
        private static long TributeEstimate(Kingdom k) =>
            (CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(k) : k.units?.Count ?? 0) / 2;

        //显示宪法：条款可点击修改(见 ConstitutionSystem.CycleByPlayer)，手定的条款不再随制度自动变化
        public void ShowConstitution(WindowMetaTab pArg0)
        {
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("empire_constitution");
            string color = _empire.getColor().color_text;
            ConstitutionData constitution = ConstitutionSystem.Get(_empire);
            if (constitution == null)
            {
                var noneCard = parent.BeginCard("empire_constitution", icons: "ui/icons/iconWorldLaws");
                EmpireCraftStatsRow noneRows = noneCard.AddComponent<EmpireCraftStatsRow>();
                noneRows.WideLayout = true;
                noneRows.IShowStatsRow("empire_constitution", LM.Get("constitution_none"), color);
                StartCoroutine(ShowCardRows(noneRows));
                return;
            }
            // 宪法卡：标题条直接是《宪法名》，右侧是颁行年数与修订号；条款行可点击循环修改
            string age = string.Format(LM.Get(constitution.provisional ? "constitution_age_provisional" : "constitution_age"),
                Mathf.Max(0, Date.getYearsSince(constitution.promulgated_at)), constitution.number);
            var clauseCard = parent.BeginCard($"《{constitution.name}》", headerValue: age, icons: "ui/icons/iconWorldLaws");
            var clauseHint = clauseCard.AddTextIntoVertLayout(
                LM.Get(constitution.provisional ? "constitution_hint_provisional" : "constitution_hint").ColorString("#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 16f, 12f));
            clauseHint.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
            EmpireCraftStatsRow clauseRows = clauseCard.AddComponent<EmpireCraftStatsRow>();
            clauseRows.WideLayout = true;
            foreach (string row in ConstitutionSystem.EditableRows)
            {
                string value = ConstitutionSystem.CurrentValue(constitution.clauses, row);
                bool editable = ConstitutionSystem.Options(_empire, row).Count > 0;
                string text = ConstitutionSystem.ValueText(row, value) +
                              (ConstitutionSystem.IsPlayerLocked(_empire, row) ? LM.Get("constitution_locked_mark") : "") +
                              (editable ? "" : LM.Get("constitution_readonly_mark"));
                string clauseRow = row;
                clauseRows.IShowStatsRow($"constitution_clause_{row}", text, editable ? color : "#9A9A9A",
                    action: editable
                        ? () =>
                        {
                            if (ConstitutionSystem.CycleByPlayer(_empire, clauseRow)) ShowConstitution(pArg0);
                        }
                        : null);
            }

            // 两栏：左看国家状态，右看思想与路线
            var columns = parent.BeginColumns();
            var stateCard = columns.BeginColumnCard("constitution_section_state", icons: "ui/icons/iconCity");
            EmpireCraftStatsRow stateRows = stateCard.AddComponent<EmpireCraftStatsRow>();
            stateRows.WideLayout = true;
            float nationalSentiment = NationalSentimentSystem.GetEmpire(_empire);
            stateCard.AddHeroStat(UIHelper.CardColumnWidth, LM.Get("national_sentiment"), $"{nationalSentiment:0}%",
                nationalSentiment >= 60f ? "#FF8A7A" : nationalSentiment >= 30f ? "#FFD34E" : "#F3C34A");
            if (CityPopulationSystem.AbstractPopulationEnabled && GranarySystem.Enabled(_empire.CoreKingdom))
                stateRows.IShowStatsRow("granary", $"{GranarySystem.Stock(_empire.CoreKingdom):0}", color);
            EmpireCore frontCore = EmpireCoreManager.Get(_empire);
            if (frontCore?.warlord_parent_core_id > 0) frontCore = EmpireCoreManager.Get(frontCore.warlord_parent_core_id) ?? frontCore;
            if (NationalSentimentSystem.InUnitedFront(frontCore))
                stateRows.IShowStatsRow("nation_united_front", LM.Get("nation_united_front_active"), "#8FE7FF");

            // 意识形态演变(见 IdeologyDynamicsSystem)：疲劳度、经济趋势、现行路线
            var dynamicsCard = columns.BeginColumnCard("constitution_section_dynamics", icons: "ui/icons/iconKnowledge");
            EmpireCraftStatsRow dynamicsRows = dynamicsCard.AddComponent<EmpireCraftStatsRow>();
            dynamicsRows.WideLayout = true;
            ConstitutionalEconomyState dynamics = _empire.data?.constitutional_economy;
            if (dynamics != null)
            {
                float fatigue = dynamics.ideology_fatigue;
                dynamicsCard.AddHeroStat(UIHelper.CardColumnWidth, LM.Get("ideology_fatigue"), $"{fatigue:0}%",
                    fatigue >= 60f ? "#FF8A7A" : fatigue >= 30f ? "#FFD34E" : "#7EE6A8");
                string trendKey = dynamics.economic_crisis ? "economic_trend_crisis"
                    : dynamics.economic_trend >= IdeologyDynamicsSystem.GrowthThreshold ? "economic_trend_growth"
                    : dynamics.economic_trend <= IdeologyDynamicsSystem.DeclineThreshold ? "economic_trend_decline"
                    : "economic_trend_stable";
                dynamicsRows.IShowStatsRow("economic_trend", LM.Get(trendKey),
                    trendKey is "economic_trend_crisis" or "economic_trend_decline" ? "#FF8A7A"
                    : trendKey == "economic_trend_growth" ? "#7EE6A8" : color);
                float speechPressure = dynamics.speech_pressure;
                dynamicsRows.IShowStatsRow("speech_pressure",
                    Mathf.Abs(speechPressure) < 10f ? LM.Get("speech_pressure_none")
                    : string.Format(LM.Get(speechPressure > 0f ? "speech_pressure_loosen" : "speech_pressure_tighten"),
                        Mathf.RoundToInt(Mathf.Abs(speechPressure) * 2f)),
                    speechPressure > 0f ? "#8FE7FF" : speechPressure < 0f ? "#FF8A7A" : color);
                float liberation = IdeologyDynamicsSystem.GetLiberation(_empire);
                if (liberation > 0f)
                    dynamicsRows.IShowStatsRow("thought_liberation", string.Format(LM.Get("thought_liberation_value"),
                        Mathf.CeilToInt(liberation * IdeologyDynamicsSystem.LiberationYears)), "#8FE7FF");
                if (dynamics.suppressed_thought >= 1f)
                    dynamicsRows.IShowStatsRow("suppressed_thought", $"{dynamics.suppressed_thought:0}%",
                        dynamics.suppressed_thought >= 30f ? "#FF8A7A" : "#FFD34E");
                if (!string.IsNullOrEmpty(dynamics.ideology_line))
                    dynamicsRows.IShowStatsRow("ideology_line", LM.Get($"ideology_line_{dynamics.ideology_line}"), color);
            }
            else AddCardEmptyNote(dynamicsCard);

            // 修宪记录卡：标题条右侧给总次数，最近十条倒序
            List<ConstitutionAmendmentRecord> amendments = constitution.amendments ?? new List<ConstitutionAmendmentRecord>();
            var amendmentCard = parent.BeginCard("constitution_section_amendments",
                headerValue: $"{LM.Get("constitution_amendments")}: {amendments.Count}",
                icons: new[] { "ui/iconHistory", "ui/icons/iconKnowledge" });
            EmpireCraftStatsRow amendmentRows = amendmentCard.AddComponent<EmpireCraftStatsRow>();
            amendmentRows.WideLayout = true;
            foreach (ConstitutionAmendmentRecord record in Enumerable.Reverse(amendments).Take(10))
            {
                string when = string.Format(LM.Get("constitution_years_ago"), Mathf.Max(0, Date.getYearsSince(record.time)));
                string by = LM.Get(record.by switch
                {
                    "player" => "constitution_by_player",
                    "line" => "constitution_by_line",
                    "pressure" => "constitution_by_pressure",
                    _ => "constitution_by_sync"
                });
                amendmentRows.IShowStatsRow($"constitution_clause_{record.clause}",
                    $"{ConstitutionSystem.ValueText(record.clause, record.from)} → {ConstitutionSystem.ValueText(record.clause, record.to)}（{when}·{by}）",
                    "#C8C8C8");
            }
            if (amendments.Count == 0)
                amendmentCard.AddTextIntoVertLayout(LM.Get("label_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleCenter,
                    new Vector2(UIHelper.WidePanelWidth - 8f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);

            // 条款说明：静态参考文字，小字浅色收在页尾
            parent.AddSectionHeader("constitution_section_notes", "#9A948A");
            foreach (string hintKey in new[] { "constitution_ideology_intensity_hint", "constitution_speech_hint",
                "constitution_nation_hint", "constitution_civil_service_hint", "constitution_farmland_hint", "constitution_grain_hint" })
            {
                var note = parent.AddTextIntoVertLayout(LM.Get(hintKey).ColorString("#9A948A"),
                    true, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 16f, 18f));
                note.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
            }
            StartCoroutine(ShowCardRows(clauseRows, stateRows, dynamicsRows, amendmentRows));
        }

        //显示个人历史
        public void ShowPersonalHistory()
        {
            Clear();
            InitialTopPartInfo();
            var currentHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (currentHistory == null) return;

            var parent = _root.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            UIHelper.UseWidePanel(parent.transform);
            AddIntoGroup("empire_reign_history", parent.gameObject);
            string eraName = string.IsNullOrWhiteSpace(currentHistory.year_name)
                ? LM.Get("waiting_for_naming")
                : currentHistory.year_name;
            string emperorName = string.IsNullOrWhiteSpace(currentHistory.emperor)
                ? _empire.Emperor?.getName() ?? ""
                : currentHistory.emperor;
            int reignYears = currentHistory == _empire.data.currentHistory
                ? _empire.GetEmperorYear()
                : Mathf.Max(1, currentHistory.total_time);

            var reignCard = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter,
                pSize: new Vector2(196, 34));
            AddReignInfoColumn(reignCard, LM.Get("year_name"), eraName, new Color(1f, 0.78f, 0.2f), new Vector2(52, 30), 9);
            AddReignInfoColumn(reignCard, IsRepublicRecord(currentHistory) ? OfficeTitle(currentHistory) : LM.Get("emperor"),
                emperorName, _empire.getColor()._color_text, new Vector2(82, 30), 11);
            AddReignInfoColumn(reignCard, LM.Get("empire_reign_duration"), $"{reignYears}{LM.Get("Year")}",
                new Color(0.25f, 0.9f, 0.8f), new Vector2(52, 30), 9);
            reignCard.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(196, 34));

            if (currentHistory.descriptions != null)
            {
                HistoryDescription lasDes = new HistoryDescription()
                {
                    cities = currentHistory.initial_cities != null ? new List<string>(currentHistory.initial_cities) : new List<string>(),
                    description = "",
                    time = ""
                };
                foreach (var d in currentHistory.descriptions)
                {
                    EmpireHistoryWindow.ListHistoryDescriptions(lasDes, d, parent);
                    lasDes = d;
                }
            }
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }

        // 多张卡片各自的行依次显示完，再刷新一次滚动区
        private IEnumerator ShowCardRows(params EmpireCraftStatsRow[] cards)
        {
            int generation = _renderGeneration;
            foreach (EmpireCraftStatsRow rows in cards)
                if (rows != null && rows.gameObject.activeInHierarchy) yield return rows.showRows();
            if (generation == _renderGeneration) yield return RefreshScrollLayout(resetToTop: true);
        }

        private IEnumerator ShowStatsRowsAndRefresh(EmpireCraftStatsRow statsRow)
        {
            if (statsRow == null) yield break;
            int generation = _renderGeneration;
            yield return statsRow.showRows();
            if (generation == _renderGeneration && statsRow != null && statsRow.gameObject.activeInHierarchy)
                yield return RefreshScrollLayout(resetToTop: true);
        }

        private IEnumerator RefreshScrollLayout(bool resetToTop)
        {
            // Several panels and the vanilla stats rows are activated over multiple frames.
            // Rebuild only after that work finishes so the ScrollRect sees their final height.
            yield return CoroutineHelper.wait_for_next_frame;
            if (ContentTransform == null) yield break;

            RectTransform contentRect = ContentTransform.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                float preferredHeight = LayoutUtility.GetPreferredHeight(contentRect);
                if (preferredHeight > 0f)
                    contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
            }

            ScrollRect scrollRect = TabsWindow?.scrollRect;
            if (scrollRect == null) yield break;
            scrollRect.vertical = true;
            scrollRect.StopMovement();
            if (resetToTop) scrollRect.verticalNormalizedPosition = 1f;
        }

        // Keep the ruler prominent while the era and reign length remain readable context.
        private static void AddReignInfoColumn(AutoHoriLayoutGroup parent, string label, string value, Color valueColor,
            Vector2 size, int valueFontSize)
        {
            var column = parent.BeginVertGroup(size, pSpacing: -2, pAlignment: TextAnchor.MiddleCenter,
                pPadding: new RectOffset(0, 0, 1, 1));
            var labelText = column.AddTextIntoVertLayout(label.ColorString(pColor: new Color(0.65f, 0.72f, 0.72f)), true,
                TextAnchor.MiddleCenter, new Vector2(size.x - 2, 8));
            labelText.UseFixedFontSize(5, HorizontalWrapMode.Overflow);
            var valueText = column.AddTextIntoVertLayout(value.ColorString(pColor: valueColor), true,
                TextAnchor.MiddleCenter, new Vector2(size.x - 2, 18));
            valueText.UseFixedFontSize(valueFontSize, HorizontalWrapMode.Overflow);
        }
        //显示行政窗口
        public void ShowBureau(WindowMetaTab pArg0)
        {

            ScrollWindow.showWindow(nameof(EmpireBeaurauWindow));
        }

        public IEnumerator ShowKingdoms(AutoVertLayoutGroup parent, int renderGeneration, Empire renderedEmpire)
        {
            foreach (var e in renderedEmpire.kingdoms_list.ToList())
            {
                if (!IsCurrentRender(renderGeneration, renderedEmpire, parent)) yield break;
                if (e == null || e.isRekt()) continue;
                PrepareKingdom(e, parent);
                yield return CoroutineHelper.wait_for_next_frame;
            }
        }

        private bool IsCurrentRender(int generation, Empire renderedEmpire, AutoVertLayoutGroup parent)
        {
            return generation == _renderGeneration && renderedEmpire != null && renderedEmpire == _empire &&
                   !renderedEmpire.isRekt() && parent != null && parent.gameObject != null;
        }

        public void PrepareKingdom(Kingdom e, AutoVertLayoutGroup parent)
        {
            if (e == null || e.isRekt() || parent == null) return;
            GameObject kingdomListElement = PrefabHelper.FindPrefabByName("list_element_kingdom");
            if (kingdomListElement == null) return;
            GameObject inst = GameObject.Instantiate(kingdomListElement);
            KingdomListElement kl = inst.GetComponent<KingdomListElement>();
            if (kl == null)
            {
                Destroy(inst);
                return;
            }
            kl.kingdomName.text = e.GetKingdomFullName();
            kl.textAge._text.text = e.getAge().ToString();
            kl.textPopulation._text.text = e.countUnits().ToString();
            kl.textArmy._text.text = e.countTotalWarriors().ToString();
            kl.textCities._text.text = e.countCities().ToString();
            kl.textZones._text.text = e.countZones().ToString();
            kl.avatarLoader.load(e.king);
            kl.meta_object = e;
            kl.loadBanner();
            inst.name = "list_element_kingdom";
            inst.SetActive(true);
            parent.AddChild(inst);
        }

        public override void OnFirstEnable()
        {
            base.OnFirstEnable();
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            if (_empire == null || _empire.isRekt())
            {
                Clear();
                return;
            }
            SyncEmpireNameInput(forceValueSync: true);
            InitialTabButtons();
        }
        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            if (_empire == null || _empire.isRekt())
            {
                Clear();
                return;
            }
            // 国号可能在窗口关闭期间变了(军阀时期改称中央、临时政府等)，每次打开都按当前完整国号刷新
            SyncEmpireNameInput(forceValueSync: true);
            InitialTabButtons();
            StartCoroutine(ShowKingdomList());
        }

        public override void OnNormalDisable()
        {
            base.OnNormalDisable();
            Clear();
            _lineage.Clear();
        }

        private const string RepublicGroupKey = "\u0001republic";

        private string HistoryGroupKey(EmpireCraftHistory h)
        {
            if (IsRepublicRecord(h)) return RepublicGroupKey;
            var s = string.IsNullOrEmpty(h.royal_surname) ? "" : h.royal_surname;
            return string.IsNullOrEmpty(s) ? h.empire_name : string.Join("·", s, h.empire_name);
        }

        // 共和组的标题：现在仍是共和国就用现国号，否则用最后一任元首时的国号
        private string RepublicGroupTitle()
        {
            string name = RepublicSystem.IsRepublic(_empire)
                ? _empire.GetEmpireFullName()
                : _empire.data.history.LastOrDefault(IsRepublicRecord)?.empire_full_name ?? "";
            return string.Format(LM.Get("republic_history_group"), name);
        }

        // 旧存档的记录没有 is_republic 标记：任期内最早一条记载晚于建立共和的，算共和国元首
        private bool IsRepublicRecord(EmpireCraftHistory h)
        {
            if (h == null) return false;
            if (h.is_republic) return true;
            var state = _empire.data?.constitutional_economy;
            if (state == null || !state.is_republic || state.republic_since < 0d) return false;
            if (h == _empire.data.currentHistory) return true;
            double first = h.descriptions?.Where(d => d != null && d.timestamp >= 0d)
                .Select(d => d.timestamp).DefaultIfEmpty(-1d).Min() ?? -1d;
            return first >= 0d && first >= state.republic_since;
        }

        private string OfficeTitle(EmpireCraftHistory h) =>
            !string.IsNullOrWhiteSpace(h.office_title) ? h.office_title : RepublicSystem.GetHeadOfStateTitle(_empire);

        public void ListPastEmperor(EmpireCraftStatsRow statsRow, EmpireCraftHistory history)
        {
            if (string.IsNullOrEmpty(history.emperor))
            {
                return;
            }
            // 共和国元首：称号为"国家全称 + 职务"(如绥民国总统)，没有庙号谥号
            if (IsRepublicRecord(history))
            {
                string fullName = string.IsNullOrWhiteSpace(history.empire_full_name)
                    ? history.empire_name
                    : history.empire_full_name;
                statsRow.IShowStatsRow("past_head_of_state", history.emperor + $"(在任 {history.total_time}{LM.Get("Year")})", _empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(history));
                statsRow.IShowStatsRow("title_name", fullName + OfficeTitle(history), _empire.getColor().color_text);

                return;
            }
            var text1 = history.empire_name + (_empire.data.has_year_name?history.year_name:"") + LM.Get("emperor");
            var text2 = "";
            if (_empire.data.has_year_name)
            {
                if (!string.IsNullOrEmpty(history.shihao_name))
                {
                    string shi = history.empire_name + LM.Get(history.shihao_name) + LM.Get("emperor_suffix");
                    text2 = string.IsNullOrEmpty(history.miaohao_name)
                        ? shi
                        : history.empire_name + LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix) + "-" + shi;
                }
                else
                {
                    text2 = LM.Get("waiting_for_naming");
                }
            }
            statsRow.IShowStatsRow("past_emperor", history.emperor + $"(在位 {history.total_time}{LM.Get("Year")})", _empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(history));
            statsRow.IShowStatsRow("title_name", text1, _empire.getColor().color_text);
            if (_empire.data.has_year_name)
            {
                statsRow.IShowStatsRow("post_humous_name", text2, _empire.getColor().color_text);
            }

        }

        public void OpenHistoryWindow(EmpireCraftHistory history)
        {
            ConfigData.CURRENT_SELECTED_HISTORY = history;
            ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
        }

        // 名称框显示与提示框、地图铭牌相同的完整国号；只有玩家真的改了名才生效
        public void name_change(string name)
        {
            if (_empire?.CoreKingdom == null) return;
            name = name?.Trim() ?? "";
            if (name.Length == 0 || name == _empire.GetEmpireFullName()) return;
            // 现代政权、军阀时期改称的政权(中央、临时政府)：国号不是"名号 + 类型"拼出来的，按玩家输入的完整国号定名
            if (_empire.CoreKingdom.GetRegime()?.type == RegimeType.Modern ||
                _empire.GetEmpireFullName() != _empire.GetBaseEmpireFullName())
            {
                _empire.CoreKingdom.SetCustomCountryName(name);
                _empire.CoreKingdom.SetCustomCountrySuffix("");
                return;
            }
            _empire.SetEmpireName(name);
        }

        public void AddIntoGroup(string title, GameObject obj)
        {
            if (_groups.TryGetValue(title, out GameObject previous) && previous != null && previous != obj)
            {
                previous.SetActive(false);
                Destroy(previous);
            }
            _groups[title] = obj;
        }

        private AutoVertLayoutGroup CommonInitial(string titleName)
        {
            // Keep one auto-sized group directly under the ScrollRect content. The old nested
            // group was created under the root and then reparented, so delayed stats rows did not
            // reliably propagate their final preferred height back to the scroll area.
            var content = _root.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter,
                pPadding: new RectOffset(3, 3, 0, 8));
            UIHelper.UseWidePanel(content.transform);
            SimpleText title = Instantiate(SimpleText.Prefab, null);
            string empirePersonalHistory = LM.Get(titleName);
            title.Setup($"{empirePersonalHistory}", TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 12f, 18f));
            title.background.enabled = false;
            title.UseFixedFontSize(10, HorizontalWrapMode.Wrap);
            content.AddChild(title.gameObject);
            // 页标题下一条细暗线，把标题和正文分开(与分节标题的内嵌条同一视觉家族)
            content.AddTextIntoVertLayout("", false, TextAnchor.MiddleCenter,
                new Vector2(UIHelper.WidePanelWidth - 12f, 3f));

            AddIntoGroup(titleName, content.gameObject);
            return content;
        }
    }
}
