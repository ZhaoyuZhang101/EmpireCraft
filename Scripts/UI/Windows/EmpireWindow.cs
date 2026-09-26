using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
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
    public class EmpireWindow : AutoLayoutWindow<EmpireWindow>
    {
        // The ruler card is 68 units high with a 72-unit frame. Keep the scroll content just
        // below it; a larger shared padding creates a conspicuous hole on short tabs.
        private const int ContentTopPadding = 76;
        private TextInput _empireNameInput;
        private Empire _empire;
        private Empire _textInputEmpire;
        private bool _textInputInitialized;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private int _renderGeneration;

        private Dictionary<string, Text> _infosTrans = new Dictionary<string, Text>();

        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, ContentTopPadding, 3);
            _empireNameInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
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
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_controlled_kingdoms"))
            {
                var kingdomsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                kingdomsWindowTab.Setup("empire_controlled_kingdoms", this.ScrollWindowComponent, action:ShowKingdomListHelp,
                    sprite: SpriteTextureLoader.getSprite("SplitAllUnderHeaven"));
            }

            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "past_emperors"))
            {
                var pastEmperorsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                pastEmperorsWindowTab.Setup("past_emperors", this.ScrollWindowComponent, action:ShowEmperors, sprite:SpriteTextureLoader.getSprite("ui/iconHistory"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_bureau"))
            {
                var bureauWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                bureauWindowTab.Setup("empire_bureau", this.ScrollWindowComponent, action:ShowBureau, sprite:SpriteTextureLoader.getSprite("ChineseCrown"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_setting"))
            {
                var settingWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                settingWindowTab.Setup("empire_setting", this.ScrollWindowComponent, action:OpenEmpireSettingWindow);
            }
            if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "empire_institutions"))
            {
                var institutionTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                institutionTab.Setup("empire_institutions", this.ScrollWindowComponent,
                    action: OpenInstitutionWindow, sprite: SpriteTextureLoader.getSprite("ChineseCrown"));
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
            var topSpace = this.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter,
                pSize: new Vector2(196, 68), pPadding: new RectOffset(0, 6, 0, 0));
            topSpace.transform.AddStretchBackground("clanFrame", new Vector2(208, 72));
            
            //左侧信息栏
            var leftPart = topSpace.BeginVertGroup(new Vector2(50, 56), pSpacing: 0,
                pAlignment:TextAnchor.MiddleCenter);
            leftPart.AddTextIntoVertLayout($"{LM.Get("empire_clan")}: {(_empire.EmpireSpecificClan?.name??""+ " " + LM.Get("Clan")).ColorString(_empire.EmpireSpecificClan?.color??"#FFFFFF")}", size:new Vector2(50, 10));
            leftPart.AddTextIntoVertLayout($"{"format_past_emperor".LocalFormat(_empire?.data?.history_emperrors?.Count??0)}", size:new Vector2(50, 10));
            leftPart.AddTextIntoVertLayout($"{LM.Get("i_population")}: {_empire.CountPopulation()}/{_empire.countMaxPopulation()}", size:new Vector2(50, 10));
            leftPart.AddTextIntoVertLayout($"{LM.Get("national_power")}: {_empire.GetNationalPower():0.##}", size:new Vector2(50, 10));
            // 原来放在下面"帝国文化特点"面板里的两条基础信息(当前制度/统治文化)，挪到这里跟
            // 其它基础数据放一起，不用滚到下面才看得到。
            leftPart.AddTextIntoVertLayout(
                $"{LM.Get("composite_empire_current_system")}: {CompositeEmpireService.GetRegimeName(_empire.CoreKingdom.GetRegime().type)}",
                size:new Vector2(50, 10));

            // 人物区固定为 hori(继任者, vert(皇帝, 权臣), 皇后)。
            var avatarRow = topSpace.BeginHoriGroup(pSpacing: 0, pAlignment: TextAnchor.MiddleCenter,
                pSize: new Vector2(92, 62));
            avatarRow.AddActorViewIntoHoriLayout(_empire.CoreKingdom.GetHeir(),
                description:LM.Get("empire_heir").ColorString(pColor:new Color(0.8f,0.0f,1f)));
            
            //中央信息栏
            var centerPart = avatarRow.BeginVertGroup(new Vector2(30, 60), pSpacing:0,
                pAlignment:TextAnchor.MiddleCenter);
            string emperorTitle = LM.Get(_empire.Emperor?.isSexFemale() == true ? "actor_emperor_G" : "actor_emperor_B")
                .ColorString(pColor:new Color(1,0.8f,0));
            if (_empire.data.is_been_controlled)
            {
                emperorTitle += "\n" + LM.Get("powerful_minister_status_puppet").ColorString(pColor:new Color(0.65f,0.75f,0.85f));
            }
            centerPart.AddActorViewIntoVertLayout(_empire.Emperor, description:emperorTitle);
            if (ParliamentSystem.HasParliament(_empire))
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
            
            Actor lover = _empire.Emperor?.lover;
            avatarRow.AddActorViewIntoHoriLayout(lover,
                description:LM.Get(lover?.isSexFemale() == false ? "empire_lover_B" : "empire_lover_G")
                    .ColorString(pColor:new Color(1f,0.1f,0.5f)));
            
            //右侧信息栏
            var rightPart = topSpace.BeginVertGroup(new Vector2(50, 46), pSpacing: 0,
                pAlignment:TextAnchor.MiddleCenter);
            rightPart.AddTextIntoVertLayout($"{LM.Get("official_students_num")}: " +
                                            $"{_empire.GetMembersWithTrait("jingshi").Count.ToString().ColorString("#E16A54")}/" +
                                            $"{_empire.GetMembersWithTrait("gongshi").Count.ToString().ColorString("#CB9DF0")}/" +
                                            $"{_empire.GetMembersWithTrait("juren").Count.ToString()}".ColorString("#A2D2DF"), size:new Vector2(50, 10));
            rightPart.AddTextIntoVertLayout($"{_empire.GetYearNameWithTime().ColorString(pColor:_empire.getColor()._color_text)}", size:new Vector2(50, 10));
            rightPart.AddTextIntoVertLayout($"{LM.Get("i_age")}: {_empire.CoreKingdom.getAge()}", size:new Vector2(50, 10));
            rightPart.AddTextIntoVertLayout(
                $"{LM.Get("composite_empire_ruling_culture")}: {GetCultureDisplayName(CultureService.GetRealmCulture(_empire.CoreKingdom))}",
                size:new Vector2(50, 10));
            
            topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 1));
            RectTransform topRect = topSpace.GetComponent<RectTransform>();
            topRect.sizeDelta = new Vector2(196, 68);
            LayoutRebuilder.ForceRebuildLayoutImmediate(topRect);
            _empireNameInput?.transform.SetAsLastSibling();
            
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
            string text = _empire.GetEmpireName();
            this.transform.parent.transform.parent.GenerateTextInput(offset:new Vector2(0, 152), default_text:text, input:_empireNameInput);
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
                _empireNameInput.input.text = _empire.GetEmpireName();
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
                    var reformLabel = parent.AddTextIntoVertLayout(reformText.ColorString("#65D6C4"), true,
                        TextAnchor.MiddleCenter, new Vector2(160, 22));
                    // 节点名字长短不一，单行 12 高度经常被挤成看不清的缩字——固定字号+换行，
                    // 留够两行的高度，宁可换行也不缩字（跟制度倾向那行同款做法）。
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
            int rowCount = (classes.Count + 1) / 2;
            float panelHeight = 4 + 12 + 10 + rowCount * 21 + (rowCount + 1);

            var panel = parent.BeginVertGroup(new Vector2(196, panelHeight), pSpacing: 1,
                pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));
            var title = panel.AddTextIntoVertLayout(LM.Get("empire_class_alignment").ColorString("#7FD8EA"), true,
                TextAnchor.MiddleCenter, new Vector2(190, 12));
            title.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            var hint = panel.AddTextIntoVertLayout(LM.Get("empire_class_alignment_hint").ColorString("#8FA0A8"),
                true, TextAnchor.MiddleCenter, new Vector2(190, 10));
            hint.UseFixedFontSize(6, HorizontalWrapMode.Overflow);

            for (int index = 0; index < classes.Count; index += 2)
            {
                var row = panel.BeginHoriGroup(new Vector2(190, 21), TextAnchor.MiddleCenter, 2);
                for (int column = 0; column < 2 && index + column < classes.Count; column++)
                    AddClassAlignmentCell(row, classes[index + column], classShares, factions);
            }
            panel.transform.AddStretchBackground("regimeFrame", new Vector2(196, panelHeight));
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

            var cell = row.BeginVertGroup(new Vector2(94, 20), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(3, 2, 0, 0));
            var heading = cell.AddTextIntoVertLayout(
                $"{socialClass.ToTranslate()}  {population:0}%".ColorString("#F3C34A"), true,
                TextAnchor.MiddleLeft, new Vector2(89, 9));
            heading.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var valueLabel = cell.AddTextIntoVertLayout(preference.ColorString(preferenceColor), true,
                TextAnchor.MiddleLeft, new Vector2(89, 9));
            valueLabel.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(valueLabel);
            cell.transform.AddStretchBackground("FactionFrame", new Vector2(94, 20));
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
            var panel = parent.BeginVertGroup(new Vector2(196, height), pSpacing: 1,
                pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));

            string titleState = composite
                ? CompositeEmpireService.GetStageName(empire)
                : adoptionInProgress
                    ? LM.Get("composite_empire_adoption_plotting")
                    : LM.Get("composite_empire_stage_None");
            panel.AddTextIntoVertLayout(
                $"{LM.Get("composite_empire_identity_title")} · {titleState}".ColorString(
                    pColor: composite ? new Color(0.25f, 0.9f, 0.8f) : new Color(0.85f, 0.75f, 0.35f)),
                true, TextAnchor.MiddleCenter, new Vector2(190, 12));

            if (composite)
            {
                string ruling = GetCultureDisplayName(CompositeEmpireService.GetRulingCulture(empire));
                string institution = GetCultureDisplayName(CompositeEmpireService.GetInstitutionalCulture(empire));
                string externalIdentity = GetCultureDisplayName(empire.data.external_identity_culture);
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_ruling_culture")}: {ruling.ColorString("#F3C34A")}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_court_culture")}: {institution.ColorString("#65D6C4")}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_court_system")}: {CompositeEmpireService.GetInstitutionalRegimeName(empire)}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                if (hasCulturalName)
                {
                    string nameCulture = GetCultureDisplayName(empire.data.composite_cultural_name_culture);
                    panel.AddTextIntoVertLayout(
                        $"{LM.Get("composite_empire_cultural_name")}: {empire.data.composite_cultural_name.ColorString("#F3C34A")} ({nameCulture})",
                        true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                }
                panel.AddTextIntoVertLayout(
                    LM.Get("composite_empire_regional_systems"),
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(CompositeEmpireService.GetRegionalInstitutionSummary(empire),
                    true, TextAnchor.MiddleCenter, new Vector2(190, 30));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_military_tradition")}: {CompositeEmpireService.GetMilitaryTraditionName(empire).ColorString("#E9A85B")}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_external_identity")}: {externalIdentity}  |  {LM.Get("composite_empire_integration")}: {empire.data.composite_integration}%",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_dual_legitimacy")}: {empire.data.central_plains_legitimacy}/{empire.data.ruling_tradition_legitimacy}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
            }
            else if (adoption != null)
            {
                string ruling = GetCultureDisplayName(adoption.RulingCulture);
                string institution = adoption.HasInstitutionalTarget
                    ? GetCultureDisplayName(adoption.InstitutionalCulture)
                    : LM.Get("label_none");
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_ruling_culture")}: {ruling.ColorString("#F3C34A")}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_current_system")}: {CompositeEmpireService.GetMilitaryTraditionName(empire)}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_candidate_culture")}: {institution.ColorString("#65D6C4")}",
                    true, TextAnchor.MiddleCenter, new Vector2(190, 10));

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
                panel.AddTextIntoVertLayout(requirement, true, TextAnchor.MiddleCenter, new Vector2(190, 20));

                if (adoptionInProgress)
                {
                    panel.AddTextIntoVertLayout(LM.Get("composite_empire_adoption_plotting").ColorString("#65D6C4"),
                        true, TextAnchor.MiddleCenter, new Vector2(190, 11));
                }
                else if (adoption.IsEligible)
                {
                    panel.AddButtonIntoVertLayout("start_composite_empire_adoption",
                        LM.Get("composite_empire_adoption_start"), () => StartCompositeEmpireAdoption(empire),
                        SpriteTextureLoader.getSprite("ChineseCrown"), size: new Vector2(188, 15), showTip: false);
                }
            }

            panel.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(196, height));
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
            Clear();
            InitialTopPartInfo();
            var parent = CommonInitial("past_emperors");
            parent.AddComponent<EmpireCraftStatsRow>();
            EmpireCraftStatsRow statsRow = parent.GetComponent<EmpireCraftStatsRow>();
            
            string text = "";
            var groups = new List<string>();
            foreach (var h in _empire.data.history)
            {
                var s = string.IsNullOrEmpty(h.royal_surname) ? "" : h.royal_surname;
                var g = string.IsNullOrEmpty(s) ? h.empire_name : string.Join("·", s, h.empire_name);
                if (!groups.Contains(g)) groups.Add(g);
            }
            foreach (var g in groups)
            {
                parent.AddTextIntoVertLayout(g, true, TextAnchor.MiddleCenter);
                foreach (var h in _empire.data.history)
                {
                    var s = string.IsNullOrEmpty(h.royal_surname) ? "" : h.royal_surname;
                    var k = string.IsNullOrEmpty(s) ? h.empire_name : string.Join("·", s, h.empire_name);
                    if (k == g)
                    {
                        ListPastEmperor(statsRow, h);
                    }
                }
            }
            text = _empire.GetEmpireName() + (_empire.data.has_year_name?_empire.data.year_name:"") + LM.Get("emperor");
            statsRow.IShowStatsRow("current_emperor", _empire.Emperor?.name??"无" , _empire.getColor().color_text, pIconPath: "iconKings", action: () => OpenHistoryWindow(_empire.data.currentHistory));
            if (_empire.Emperor != null)
            {
                statsRow.IShowStatsRow("title_name", text, _empire.getColor().color_text);
            }
            StartCoroutine(ShowStatsRowsAndRefresh(statsRow));
        }
        //显示个人历史
        public void ShowPersonalHistory()
        {
            Clear();
            InitialTopPartInfo();
            var currentHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (currentHistory == null) return;

            var parent = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
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
            AddReignInfoColumn(reignCard, LM.Get("emperor"), emperorName, _empire.getColor()._color_text, new Vector2(82, 30), 11);
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

        private IEnumerator ShowStatsRowsAndRefresh(EmpireCraftStatsRow statsRow)
        {
            if (statsRow == null) yield break;
            yield return statsRow.showRows();
            yield return RefreshScrollLayout(resetToTop: true);
        }

        private IEnumerator RefreshScrollLayout(bool resetToTop)
        {
            // Several panels and the vanilla stats rows are activated over multiple frames.
            // Rebuild only after that work finishes so the ScrollRect sees their final height.
            yield return CoroutineHelper.wait_for_next_frame;
            if (ContentTransform == null) yield break;

            Canvas.ForceUpdateCanvases();
            RectTransform contentRect = ContentTransform.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                float preferredHeight = LayoutUtility.GetPreferredHeight(contentRect);
                if (preferredHeight > 0f)
                    contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            }

            ScrollRect scrollRect = ScrollWindowComponent?.scrollRect;
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
            this.DORestart();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, ContentTopPadding, 3);
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
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, ContentTopPadding, 3);
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            if (_empire == null || _empire.isRekt())
            {
                Clear();
                return;
            }
            SyncEmpireNameInput();
            InitialTabButtons();
            StartCoroutine(ShowKingdomList());
        }

        public override void OnNormalDisable()
        {
            base.OnNormalDisable();
            Clear();
        }

        public void ListPastEmperor(EmpireCraftStatsRow statsRow, EmpireCraftHistory history)
        {
            if (string.IsNullOrEmpty(history.emperor))
            {
                return;
            }
            var text1 = history.empire_name + (_empire.data.has_year_name?history.year_name:"") + LM.Get("emperor");
            var text2 = "";
            if (_empire.data.has_year_name)
            {
                if (!string.IsNullOrEmpty(history.miaohao_name))
                {
                    text2 =
                        history.empire_name + LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix) + "-" +
                        history.empire_name + LM.Get(history.shihao_name) + LM.Get("emperor_suffix");
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
            statsRow.IShowStatsRow("empty", "=======================================================================================", "#ffffff");
        }

        public void OpenHistoryWindow(EmpireCraftHistory history)
        {
            ConfigData.CURRENT_SELECTED_HISTORY = history;
            ShowPersonalHistory();
        }

        public void name_change(string name)
        {
            if (_empire != null)
            {
                _empire.SetEmpireName(name);
            }
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
            var content = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter,
                pPadding: new RectOffset(3, 3, 0, 8));
            SimpleText title = Instantiate(SimpleText.Prefab, null);
            string empirePersonalHistory = LM.Get(titleName);
            title.Setup($"{empirePersonalHistory}", TextAnchor.MiddleCenter, new Vector2(40, 15));
            title.background.enabled = false;
            content.AddChild(title.gameObject);

            AddIntoGroup(titleName, content.gameObject);
            return content;
        }
    }
}
