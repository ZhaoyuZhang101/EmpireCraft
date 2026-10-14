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
    public partial class EmpireWindow : AbstractWideWindow<EmpireWindow>
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
        // 横屏骨架：左栏国家档案常驻，右栏是当前页签内容(见 UIHelper 卷册组件)
        private const float RailWidth = UIHelper.RailWidth;
        private const float MainWidth = UIHelper.PageMainWidth;
        private const float HalfWidth = UIHelper.PageColumnWidth;
        // 固定左栏：挂在窗口背景上不随滚动，滚动区右移让出 RailInset
        private const float RailInset = UIHelper.RailInset;
        private AutoVertLayoutGroup _rail;
        private readonly List<(string group, EmpireCraftHistory history)> _lineage = new();
        private int _lineagePage;

        private Dictionary<string, Text> _infosTrans = new Dictionary<string, Text>();

        protected override void Init()
        {
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
            _rail = UIHelper.CreateWideWindowRail(BackgroundTransform, RailWidth, RailInset);
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
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_papacy"))
            {
                var papacyTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                papacyTab.Setup("empire_papacy", this.TabsWindow, action: ShowPapacy,
                    sprite: SpriteTextureLoader.getSprite("TabPapacy"));
            }
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_diet"))
            {
                var dietTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                dietTab.Setup("empire_diet", this.TabsWindow, action: ShowDiet,
                    sprite: SpriteTextureLoader.getSprite("TabDiet"));
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
            if (TabsWindow.tabs._tabs.All(p => p.name != "empire_military"))
            {
                var militaryTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
                militaryTab.Setup("empire_military", this.TabsWindow, action: ShowMilitary,
                    sprite: SpriteTextureLoader.getSprite("TabMilitary"));
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
        }

        // 横屏骨架：左栏国家档案固定在窗口左侧(不随滚动)，右侧滚动区是当前页。返回右侧页面，页签内容都往里放；
        // chips 是页头右侧的状态摘要
        private AutoVertLayoutGroup BeginPage(string title, params string[] chips)
        {
            RedrawRail();
            var main = _root.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 6));
            UIHelper.FixPanelWidth(main.transform, MainWidth);
            // 一建好就登记：后面任何一项绘制出错，Clear() 也能把它删掉
            AddIntoGroup("page", main.gameObject);
            AddPageHeader(main, title, chips);
            SyncNameInputText();
            return main;
        }

        private void RedrawRail()
        {
            if (_rail == null || _empire?.CoreKingdom == null) return;
            UIHelper.ClearRail(_rail);
            try
            {
                DrawRail(_rail);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口档案栏绘制失败({_empire?.data?.name}): {exception}");
            }
        }

        private static void AddPageHeader(AutoVertLayoutGroup main, string title, string[] chips) =>
            main.AddPageHeader(MainWidth, title, chips);

        // 窗口开着时国号变了也跟着刷新；玩家正在输入时不打断
        private void SyncNameInputText()
        {
            _empireNameInput?.transform.SetAsLastSibling();
            if (_empireNameInput != null && !_empireNameInput.input.isFocused &&
                _empireNameInput.input.text != _empire.GetEmpireFullName())
                _empireNameInput.input.text = _empire.GetEmpireFullName();
        }

        // 左栏国家档案(所有页签共用)：身份(国号/政体文化/纪年) → 人物(元首与身边要人) → 关键数据
        private void DrawRail(AutoVertLayoutGroup rail)
        {
            bool republic = RepublicSystem.IsRepublic(_empire);
            float inner = RailWidth - 10f;

            var identity = rail.BeginVertGroup(pSpacing: 0, pAlignment: TextAnchor.UpperLeft, pPadding: new RectOffset(5, 5, 4, 5));
            UIHelper.FixPanelWidth(identity.transform, RailWidth);
            UIHelper.AddCardFrame(identity.transform, "FactionFrame_dominate");
            string fullName = _empire.GetEmpireFullName();
            float nameHeight = UIHelper.EstimateTextWidth(fullName, 10) <= inner ? 14f : 26f;
            identity.AddTextIntoVertLayout(fullName.ColorString("#F3C34A"), true, TextAnchor.UpperLeft,
                new Vector2(inner, nameHeight)).UseFixedFontSize(10, HorizontalWrapMode.Wrap);
            var regimeLine = identity.AddTextIntoVertLayout(
                ($"{CompositeEmpireService.GetRegimeName(_empire.CoreKingdom.GetRegime().type)} · " +
                 $"{GetCultureDisplayName(CultureService.GetRealmCulture(_empire.CoreKingdom))}").ColorString("#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(inner, 9f));
            regimeLine.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(regimeLine);
            // 共和国显示其选定的纪年方式
            string yearText = republic ? RepublicSystem.GetCalendarText(_empire) : _empire.GetYearNameWithTime();
            identity.AddTextIntoVertLayout(yearText.ColorString(pColor: _empire.getColor()._color_text), true,
                TextAnchor.MiddleLeft, new Vector2(inner, 11f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            // 君主国再标历代君主数(共和国的纪年行已经是"共和 N 年"，不重复)
            if (!republic)
                identity.AddTextIntoVertLayout("format_past_emperor".LocalFormat(_empire?.data?.history_emperrors?.Count ?? 0)
                    .ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft, new Vector2(inner, 9f))
                    .UseFixedFontSize(6, HorizontalWrapMode.Overflow);

            // 人物：元首 + 身边要人(共和国是反对党领袖；君主国是储君、权臣或议会总理、皇后)
            var people = rail.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperLeft, pPadding: new RectOffset(4, 4, 4, 4));
            UIHelper.FixPanelWidth(people.transform, RailWidth);
            UIHelper.AddCardFrame(people.transform);
            string rulerRole = republic
                ? RepublicSystem.GetHeadOfStateTitle(_empire)
                : _empire.CoreKingdom.GetRegime()?.type == RegimeType.ClassicalRepublic
                    ? LM.Get("ClassicalRepublic_officiallevel_0")
                    : LM.Get(_empire.Emperor?.isSexFemale() == true ? "actor_emperor_G" : "actor_emperor_B");
            if (_empire.data.is_been_controlled) rulerRole += " · " + LM.Get("powerful_minister_status_puppet");
            int peopleRows = 0;
            if (AddPersonRow(people, _empire.Emperor, rulerRole, "#FFCC00", true)) peopleRows++;
            if (republic)
            {
                // 一党领导、统一战线、民主集中制没有反对党(其他党派参加政治协商)
                bool congress = RepublicSystem.IsOneParty(_empire) || PartyBanSystem.UsesDemocraticCentralism(_empire);
                if (!congress && AddPersonRow(people, RepublicSystem.GetOppositionLeader(_empire),
                        LM.Get("republic_opposition_leader"), "#59D9FF")) peopleRows++;
            }
            else
            {
                if (AddPersonRow(people, _empire.CoreKingdom.GetHeir(), LM.Get("empire_heir"), "#CC66FF")) peopleRows++;
                if (ParliamentSystem.HasParliament(_empire))
                {
                    // 议会存续期间，权臣的位置改由议会选出的总理大臣占据
                    ParliamentView parliament = ParliamentSystem.GetView(_empire);
                    string role = LM.Get("prime_minister_title");
                    if (parliament.PrimeMinister != null && !string.IsNullOrWhiteSpace(parliament.GovernmentType))
                        role += " · " + string.Format(LM.Get("prime_minister_status"),
                            LM.Get($"parliament_government_{parliament.GovernmentType}"),
                            parliament.GovernmentSeats, parliament.TotalSeats);
                    if (AddPersonRow(people, parliament.PrimeMinister, role, "#59D9FF")) peopleRows++;
                }
                else
                {
                    string role = LM.Get(_empire.data.powerful_minister_is_empress_dowager
                        ? "empress_dowager_title" : "powerful_minister_title");
                    string status = _empire.GetPowerfulMinisterStatusText();
                    if (!string.IsNullOrWhiteSpace(status)) role += " · " + status;
                    if (AddPersonRow(people, _empire.GetPowerfulMinister(), role, "#FF8C1A")) peopleRows++;
                }
                Actor lover = _empire.Emperor?.lover;
                if (AddPersonRow(people, lover, LM.Get(lover?.isSexFemale() == false ? "empire_lover_B" : "empire_lover_G"), "#FF4D94")) peopleRows++;
            }


            // 关键数据(紧凑行：一行一项、不折行，左栏才放得下)
            var stats = rail.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperLeft, pPadding: new RectOffset(4, 4, 4, 4));
            UIHelper.FixPanelWidth(stats.transform, RailWidth);
            UIHelper.AddCardFrame(stats.transform);
            float rowWidth = RailWidth - 8f;
            int statRows = 0;
            void Stat(string label, string value, string hex) => AddRailRow(stats, rowWidth, label, value, hex, statRows++);
            if (republic)
            {
                FixedFaction governing = ParliamentSystem.GetGoverningFaction(_empire) ??
                                         _empire.CoreKingdom.GetRegime()?.GetDominateFaction();
                Stat(LM.Get("republic_ruling_party"), governing?.Name ?? LM.Get("label_none"), "#C9A7E8");
            }
            else
            {
                Stat(LM.Get("empire_clan"), _empire.EmpireSpecificClan?.name ?? LM.Get("label_none"),
                    _empire.EmpireSpecificClan?.color ?? "#FFFFFF");
            }
            Stat(LM.Get("i_population"), $"{ShortCount(_empire.CountPopulation())}/{ShortCount(_empire.countMaxPopulation())}", "#F2EEE2");
            // 战国周室：连同离开帝国的诸侯的"天下"城与人口
            if (ZhouEraSystem.DetachedVassals(_empire).Count > 0)
                Stat(LM.Get("zhou_sphere_rail"), string.Format(LM.Get("zhou_sphere_rail_value"),
                    ZhouEraSystem.SphereCities(_empire), ShortCount(ZhouEraSystem.SpherePopulation(_empire))), "#E8B840");
            Stat(LM.Get("national_power"), ShortCount(_empire.GetNationalPower()), "#F2EEE2");
            Stat(LM.Get("i_age"), _empire.CoreKingdom.getAge().ToString(), "#F2EEE2");
            Stat(LM.Get("official_students_num"),
                $"{_empire.GetMembersWithTrait("jingshi").Count.ToString().ColorString("#E16A54")}/" +
                $"{_empire.GetMembersWithTrait("gongshi").Count.ToString().ColorString("#CB9DF0")}/" +
                $"{_empire.GetMembersWithTrait("juren").Count.ToString().ColorString("#A2D2DF")}", "#F2EEE2");
            float sentiment = NationalSentimentSystem.GetEmpire(_empire);
            Stat(LM.Get("national_sentiment"), $"{sentiment:0}%", sentiment >= 60f ? "#FF8A7A" : sentiment >= 30f ? "#FFD34E" : "#F2EEE2");
            if (TreasurySystem.Enabled(_empire.CoreKingdom))
                Stat(LM.Get("label_national_treasury"), MoneyDisplay.Format(_empire.CoreKingdom.GetTreasuryBalance()), "#F3C34A");

            // 国势：按重要程度排好，左栏剩多少高度放多少项(至少放法统一项)
            var stateItems = new List<(string label, string value, string hex)>
            {
                (LM.Get(_empire.LegitimacyLabelKey), _empire.Legitimacy.ToString(), "#FFCF55")
            };
            double corruption = CorruptionSystem.GetRate(_empire);
            stateItems.Add((LM.Get("empire_rail_corruption"), $"{corruption:P0}", corruption >= 0.5d ? "#FF6666" : "#F2EEE2"));
            int provinces = _empire.kingdoms_list.Count(kingdom => kingdom != null && !kingdom.isRekt());
            int cities = _empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt())
                .Sum(kingdom => kingdom.cities?.Count ?? 0);
            stateItems.Add((LM.Get("empire_rail_territory"), string.Format(LM.Get("empire_rail_territory_value"), cities, provinces), "#F2EEE2"));
            Kingdom core = _empire.CoreKingdom;
            string capital = core.capital?.GetCityName();
            if (!string.IsNullOrWhiteSpace(capital)) stateItems.Add((LM.Get("empire_rail_capital"), capital, "#F2EEE2"));
            int wars = core.getWars().Count(war => war != null && war.isAlive() && !war.hasEnded());
            stateItems.Add((LM.Get("empire_rail_wars"),
                wars > 0 ? string.Format(LM.Get("empire_rail_wars_value"), wars) : LM.Get("empire_rail_peace"),
                wars > 0 ? "#FF6666" : "#65D66E"));
            float burden = HarshRuleSystem.GetBurden(_empire);
            if (burden >= 1f)
                stateItems.Add((LM.Get("label_harsh_rule"), $"{burden:0}", burden >= 60f ? "#FF6666" : burden >= 30f ? "#E9A85B" : "#F2EEE2"));
            int streetStage = HarshRuleSystem.GetStreetStage(_empire);
            if (ModernLegitimacy.Applies(_empire) && streetStage > 0)
                stateItems.Add((LM.Get("label_street_unrest"), HarshRuleSystem.GetStageName(streetStage), streetStage >= 3 ? "#FF6666" : "#E9A85B"));
            if (MonarchyLegitimacy.Applies(_empire))
            {
                stateItems.Add((LM.Get("empire_rail_dynasty_age"), DynasticCycleSystem.GetAge(_empire).ToString(), "#F2EEE2"));
                stateItems.Add((LM.Get("label_dynastic_strain"), $"{DynasticCycleSystem.GetPressure(_empire):0}",
                    DynasticCycleSystem.InCrisis(_empire) ? "#FF6666" : "#F2EEE2"));
            }
            // 以下按重要程度往后排，左栏放不下的自然截掉
            stateItems.Add((LM.Get("empire_rail_warriors"), ShortCount(_empire.countWarriors()), "#F2EEE2"));
            if (TreasurySystem.Enabled(core))
            {
                long balance = TreasurySystem.Report(core).operating_balance;
                stateItems.Add((LM.Get("empire_rail_balance"), (balance > 0 ? "+" : "") + MoneyDisplay.Format(balance),
                    balance < 0 ? "#FF6666" : "#65D66E"));
            }
            int tributaries = World.world.kingdoms.list.Count(k => k != null && !k.isRekt() && k.GetTakenAllianceEmpire() == _empire);
            int vassals = FeudalVassalService.GetDirectVassals(core).Count();
            if (tributaries + vassals > 0)
                stateItems.Add((LM.Get("empire_rail_dependencies"),
                    string.Format(LM.Get("empire_rail_dependencies_value"), tributaries, vassals), "#F2EEE2"));
            if (republic)
            {
                stateItems.Add((LM.Get("empire_rail_ideology"),
                    PartySystem.GetIdeologyName(IdeologyFamilies.StateIdeology(_empire)), "#C9A7E8"));
                float autocracy = PartyBanSystem.GetAutocracy(_empire);
                stateItems.Add((LM.Get("empire_rail_autocracy"), $"{autocracy:0}",
                    autocracy >= 60f ? "#FF6666" : autocracy >= 30f ? "#E9A85B" : "#F2EEE2"));
            }
            if (ParliamentSystem.HasParliament(_empire))
                stateItems.Add((LM.Get("empire_rail_next_election"),
                    string.Format(LM.Get("empire_rail_years_later"), ParliamentSystem.GetView(_empire).YearsUntilElection),
                    "#F2EEE2"));
            if (GranarySystem.Enabled(core))
                stateItems.Add((LM.Get("empire_rail_granary"), $"{GranarySystem.Stock(core):0}", "#F2EEE2"));
            string alliance = core.getAlliance()?.data?.name;
            if (!string.IsNullOrWhiteSpace(alliance)) stateItems.Add((LM.Get("empire_rail_alliance"), alliance, "#7FD8EA"));
            // 左栏可用高度 = 窗口高 - 上下边距；前三张卡按固定行高估算
            float available = ((RectTransform)BackgroundTransform).sizeDelta.y - (UIHelper.WideWindowTopMargin + 3f) - 10f;
            float used = (9f + nameHeight + 9f + 11f + (republic ? 0f : 9f))
                         + (8f + peopleRows * 32f + Mathf.Max(0, peopleRows - 1) * 2f)
                         + (8f + statRows * RailRowHeight + Mathf.Max(0, statRows - 1))
                         + 3 * 4f;
            int fit = Mathf.Clamp((int)((available - used - (8f + 18f)) / (RailRowHeight + 1f)), 1, stateItems.Count);
            var state = rail.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperLeft, pPadding: new RectOffset(4, 4, 4, 4));
            UIHelper.FixPanelWidth(state.transform, RailWidth);
            UIHelper.AddCardFrame(state.transform);
            UIHelper.AddCardTitle(state, RailWidth + 4f, LM.Get("empire_rail_state"), "#F3C34A", null, 0f, "ui/icons/iconKingdom");
            // 国势是左栏最后一张卡：底边拉到窗口底，放不满时卡里留空
            state.gameObject.AddComponent<FillToWindowBottom>().Background = (RectTransform)BackgroundTransform;
            for (int index = 0; index < fit; index++)
                AddRailRow(state, rowWidth, stateItems[index].label, stateItems[index].value, stateItems[index].hex, index);
        }

        private const float RailRowHeight = UIHelper.RailRowHeight;
        private static void AddRailRow(AutoVertLayoutGroup card, float width, string label, string value, string hex, int index) =>
            card.AddRailRow(width, label, value, hex, index);

        // 人物行：头像(可点开人物) + 职位 / 姓名；空缺的要人不占行(元首除外)。
        // 头像沿用原版头像组写法(AddActorViewIntoHoriLayout，与旧顶栏相同)，各分组位置归零
        private static bool AddPersonRow(AutoVertLayoutGroup people, Actor actor, string role, string roleHex, bool always = false)
        {
            bool alive = actor?.data != null && !actor.isRekt();
            if (!alive && !always) return false;
            float inner = RailWidth - 8f;
            var row = people.BeginHoriGroup(new Vector2(inner, 32f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            row.transform.localPosition = Vector3.zero;
            row.AddActorViewIntoHoriLayout(alive ? actor : null);
            var text = row.BeginVertGroup(new Vector2(inner - 34f, 28f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
                pPadding: new RectOffset(0, 0, 0, 0));
            text.transform.localPosition = Vector3.zero;
            text.AddTextIntoVertLayout(role.ColorString(roleHex), true, TextAnchor.MiddleLeft, new Vector2(inner - 34f, 10f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            text.AddTextIntoVertLayout((alive ? actor.getName() : LM.Get("label_none")).ColorString(alive ? "#F2EEE2" : "#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(inner - 34f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            return true;
        }

        private static string ShortCount(double value) => UIHelper.ShortCount(value);

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
            // 推行中的改革放到页头右侧：一眼知道眼下真正在发生什么
            string reformChip = null;
            InstitutionReformState activeReform = renderedEmpire.data.institution_state?.active_reform;
            if (activeReform != null)
                reformChip = string.Format(LM.Get("empire_reform_chip"),
                    InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(activeReform.node_id)),
                    activeReform.progress.ToString("0.0"));
            var parent = BeginPage("empire_controlled_kingdoms", reformChip);
            if (parent == null) yield break;
            yield return CoroutineHelper.wait_for_next_frame;
            if (!IsCurrentRender(renderGeneration, renderedEmpire, parent)) yield break;
            try
            {
                UIHelper.InitialEmpireFactionOverview(parent.BeginHoriGroup(pAlignment: TextAnchor.UpperLeft,
                    pPadding: new RectOffset(0, 0, 0, 0)), renderedEmpire.CoreKingdom, MainWidth);
                // 朝局：当前奏议、各派形势、朝局纪事(没有议会的帝国)
                CourtPanel.Render(parent, renderedEmpire, MainWidth, () => StartCoroutine(ShowKingdomList()));
                // 朝政积弊(见 MaladySystem)
                MaladyPanel.Render(parent, renderedEmpire, MainWidth);
                // 周制：天下大势(春秋战国)
                ZhouPanel.Render(parent, renderedEmpire, MainWidth, () => StartCoroutine(ShowKingdomList()));
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
            const int perRow = 4;
            float inner = MainWidth - 12f;
            float cellWidth = (inner - (perRow - 1) * 2f) / perRow;
            var card = parent.BeginCard(MainWidth, "empire_class_alignment", "#7FD8EA", null, "ui/icons/iconPopulation20");
            var hint = card.AddTextIntoVertLayout(LM.Get("empire_class_alignment_hint").ColorString("#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(inner, 9));
            hint.UseFixedFontSize(5, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(hint);
            for (int index = 0; index < classes.Count; index += perRow)
            {
                var row = card.BeginHoriGroup(new Vector2(inner, 21), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
                for (int column = 0; column < perRow && index + column < classes.Count; column++)
                    AddClassAlignmentCell(row, classes[index + column], classShares, factions, cellWidth);
            }
        }

        private static void AddClassAlignmentCell(AutoHoriLayoutGroup row, SocialClass socialClass,
            Dictionary<SocialClass, float> classShares, List<FixedFaction> factions, float width)
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

            var cell = row.BeginVertGroup(new Vector2(width, 20), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(3, 2, 0, 0));
            UIHelper.AddInsetBackground(cell, new Vector2(width, 20));
            var heading = cell.AddTextIntoVertLayout(
                $"{socialClass.ToTranslate()}  {population:0}%".ColorString("#F3C34A"), true,
                TextAnchor.MiddleLeft, new Vector2(width - 5, 9));
            heading.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var valueLabel = cell.AddTextIntoVertLayout(preference.ColorString(preferenceColor), true,
                TextAnchor.MiddleLeft, new Vector2(width - 5, 9));
            valueLabel.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(valueLabel);
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
            var panel = parent.BeginVertGroup(new Vector2(MainWidth, height), pSpacing: 1,
                pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));

            string titleState = composite
                ? CompositeEmpireService.GetStageName(empire)
                : adoptionInProgress
                    ? LM.Get("composite_empire_adoption_plotting")
                    : LM.Get("composite_empire_stage_None");
            panel.AddTextIntoVertLayout(
                $"{LM.Get("composite_empire_identity_title")} · {titleState}".ColorString(
                    pColor: composite ? new Color(0.25f, 0.9f, 0.8f) : new Color(0.85f, 0.75f, 0.35f)),
                true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 12));

            if (composite)
            {
                string ruling = GetCultureDisplayName(CompositeEmpireService.GetRulingCulture(empire));
                string institution = GetCultureDisplayName(CompositeEmpireService.GetInstitutionalCulture(empire));
                string externalIdentity = GetCultureDisplayName(empire.data.external_identity_culture);
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_ruling_culture")}: {ruling.ColorString("#F3C34A")}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_court_culture")}: {institution.ColorString("#65D6C4")}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_court_system")}: {CompositeEmpireService.GetInstitutionalRegimeName(empire)}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                if (hasCulturalName)
                {
                    string nameCulture = GetCultureDisplayName(empire.data.composite_cultural_name_culture);
                    panel.AddTextIntoVertLayout(
                        $"{LM.Get("composite_empire_cultural_name")}: {empire.data.composite_cultural_name.ColorString("#F3C34A")} ({nameCulture})",
                        true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                }
                panel.AddTextIntoVertLayout(
                    LM.Get("composite_empire_regional_systems"),
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(CompositeEmpireService.GetRegionalInstitutionSummary(empire),
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 30));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_military_tradition")}: {CompositeEmpireService.GetMilitaryTraditionName(empire).ColorString("#E9A85B")}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_external_identity")}: {externalIdentity}  |  {LM.Get("composite_empire_integration")}: {empire.data.composite_integration}%",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_dual_legitimacy")}: {empire.data.central_plains_legitimacy}/{empire.data.ruling_tradition_legitimacy}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
            }
            else if (adoption != null)
            {
                string ruling = GetCultureDisplayName(adoption.RulingCulture);
                string institution = adoption.HasInstitutionalTarget
                    ? GetCultureDisplayName(adoption.InstitutionalCulture)
                    : LM.Get("label_none");
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_ruling_culture")}: {ruling.ColorString("#F3C34A")}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_current_system")}: {CompositeEmpireService.GetMilitaryTraditionName(empire)}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));
                panel.AddTextIntoVertLayout(
                    $"{LM.Get("composite_empire_candidate_culture")}: {institution.ColorString("#65D6C4")}",
                    true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 10));

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
                panel.AddTextIntoVertLayout(requirement, true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 20));

                if (adoptionInProgress)
                {
                    panel.AddTextIntoVertLayout(LM.Get("composite_empire_adoption_plotting").ColorString("#65D6C4"),
                        true, TextAnchor.MiddleCenter, new Vector2(MainWidth - 12f, 11));
                }
                else if (adoption.IsEligible)
                {
                    panel.AddButtonIntoVertLayout("start_composite_empire_adoption",
                        LM.Get("composite_empire_adoption_start"), () => StartCompositeEmpireAdoption(empire),
                        SpriteTextureLoader.getSprite("ChineseCrown"), size: new Vector2(MainWidth - 20f, 15), showTip: false);
                }
            }

            panel.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(MainWidth, height));
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
            int pages = Math.Max(1, (_lineage.Count + LineagePageSize - 1) / LineagePageSize);
            _lineagePage = Math.Max(0, Math.Min(pages - 1, _lineagePage));
            var parent = BeginPage("past_emperors", string.Format(LM.Get("lineage_chip_total"), _lineage.Count));

            // 在位者卡：标题条是称号，内容是头像 + 姓名 + 在位年数，点"个人史"打开本朝记事
            bool republicNow = RepublicSystem.IsRepublic(_empire);
            string reignTitle = republicNow
                ? _empire.GetEmpireFullName() + RepublicSystem.GetHeadOfStateTitle(_empire)
                : _empire.GetEmpireName() + (_empire.data.has_year_name ? _empire.data.year_name : "") + LM.Get("emperor");
            var current = parent.BeginCard(MainWidth, republicNow ? "current_head_of_state" : "current_emperor", "#F3C34A",
                _empire.Emperor != null ? reignTitle : null, "ui/icons/iconKings");
            var currentRow = current.BeginHoriGroup(new Vector2(MainWidth - 12f, 32f), TextAnchor.MiddleLeft, 6, new RectOffset(0, 0, 0, 0));
            Actor ruler = _empire.Emperor;
            currentRow.transform.localPosition = Vector3.zero;
            currentRow.AddActorViewIntoHoriLayout(ruler?.data != null && !ruler.isRekt() ? ruler : null);
            var rulerText = currentRow.BeginVertGroup(new Vector2(MainWidth - 12f - 36f - 60f, 30f), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(0, 0, 0, 0));
            rulerText.transform.localPosition = Vector3.zero;
            rulerText.AddTextIntoVertLayout((ruler?.getName() ?? LM.Get("label_none")).ColorString("#F2EEE2"), true,
                TextAnchor.MiddleLeft, new Vector2(MainWidth - 108f, 16f)).UseFixedFontSize(11, HorizontalWrapMode.Overflow);
            rulerText.AddTextIntoVertLayout(string.Format(LM.Get("lineage_current_tenure"), _empire.GetEmperorYear()).ColorString("#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(MainWidth - 108f, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            currentRow.AddButtonIntoHoriLayout("lineage_personal_history", LM.Get("lineage_personal_history"),
                () => OpenHistoryWindow(_empire.data.currentHistory), size: new Vector2(54f, 14f));

            // 翻页
            var navigation = parent.BeginHoriGroup(new Vector2(MainWidth, 16f), TextAnchor.MiddleCenter, 8f, new RectOffset(0, 0, 0, 0));
            navigation.AddButtonIntoHoriLayout("lineage_previous", LM.Get("lineage_previous"), () =>
            {
                if (_lineagePage > 0) { _lineagePage--; ShowLineagePage(); }
            }, size: new Vector2(56f, 14f));
            navigation.AddTextIntoHoriLayout(string.Format(LM.Get("lineage_page"), _lineagePage + 1,
                pages, _lineage.Count).ColorString("#C8C2B4"), true, TextAnchor.MiddleCenter, new Vector2(150f, 14f))
                .UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            navigation.AddButtonIntoHoriLayout("lineage_next", LM.Get("lineage_next"), () =>
            {
                if (_lineagePage + 1 < pages) { _lineagePage++; ShowLineagePage(); }
            }, size: new Vector2(56f, 14f));
            StartCoroutine(RenderLineagePage(parent, _renderGeneration, _empire));
        }

        private const int LineageColumns = 3;
        private const float LineageTileHeight = 36f;

        private IEnumerator RenderLineagePage(AutoVertLayoutGroup parent, int generation, Empire empire)
        {
            int start = _lineagePage * LineagePageSize;
            int end = Math.Min(_lineage.Count, start + LineagePageSize);
            float tileWidth = (MainWidth - (LineageColumns - 1) * UIHelper.CardGap) / LineageColumns;
            int index = start;
            while (index < end)
            {
                if (!IsCurrentRender(generation, empire, parent)) yield break;
                string group = _lineage[index].group;
                parent.AddSectionHeader(MainWidth, group == RepublicGroupKey ? RepublicGroupTitle() : group,
                    "#F3C34A", false, "ui/icons/iconCrown");
                // 同一朝代的元首排成三列网格
                AutoHoriLayoutGroup row = null;
                int inRow = 0;
                while (index < end && _lineage[index].group == group)
                {
                    if (row == null || inRow == LineageColumns)
                    {
                        row = parent.BeginHoriGroup(new Vector2(MainWidth, LineageTileHeight), TextAnchor.UpperLeft,
                            UIHelper.CardGap, new RectOffset(0, 0, 0, 0));
                        inRow = 0;
                    }
                    AddLineageTile(row, _lineage[index].history, tileWidth);
                    inRow++;
                    index++;
                }
                yield return CoroutineHelper.wait_for_next_frame;
            }
            if (IsCurrentRender(generation, empire, parent)) yield return RefreshScrollLayout(true);
        }

        // 元首格：姓名 / 在位年数 / 称号(谥号或职务)，点开个人史
        private void AddLineageTile(AutoHoriLayoutGroup row, EmpireCraftHistory history, float width)
        {
            (string reign, string title) = LineageTexts(history);
            var tile = row.BeginVertGroup(new Vector2(width, LineageTileHeight), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
                pPadding: new RectOffset(6, 4, 3, 3));
            var nameRow = tile.BeginHoriGroup(new Vector2(width - 10f, 12f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            UIHelper.AddLayoutIcon(nameRow.transform, SpriteTextureLoader.getSprite("ui/icons/iconKings"), 9f);
            nameRow.AddTextIntoHoriLayout(history.emperor.ColorString("#F2EEE2"), true, TextAnchor.MiddleLeft,
                new Vector2(width - 22f, 11f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            tile.AddTextIntoVertLayout(reign.ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft, new Vector2(width - 10f, 9f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var titleLabel = tile.AddTextIntoVertLayout(title.ColorString(_empire.getColor().color_text), true,
                TextAnchor.MiddleLeft, new Vector2(width - 10f, 9f));
            titleLabel.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(titleLabel);
            tile.transform.AddStretchBackground("FactionFrame", new Vector2(width, LineageTileHeight));
            UIHelper.MakeClickable(tile.gameObject, () => OpenHistoryWindow(history));
        }

        // (在位/在任年数, 称号)：共和国元首是"国家全称 + 职务"；君主有年号时显示庙号谥号
        private (string reign, string title) LineageTexts(EmpireCraftHistory history)
        {
            if (IsRepublicRecord(history))
            {
                string fullName = string.IsNullOrWhiteSpace(history.empire_full_name) ? history.empire_name : history.empire_full_name;
                return (string.Format(LM.Get("lineage_term_years"), history.total_time), fullName + OfficeTitle(history));
            }
            string title = history.empire_name + (_empire.data.has_year_name ? history.year_name : "") + LM.Get("emperor");
            if (_empire.data.has_year_name)
            {
                if (!string.IsNullOrEmpty(history.shihao_name))
                {
                    string shi = PosthumousNameGenerator.ShiTitle(history, _empire);
                    title = string.IsNullOrEmpty(history.miaohao_name)
                        ? shi
                        : history.empire_name + LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix) + "-" + shi;
                }
                else title = LM.Get("waiting_for_naming");
            }
            return (string.Format(LM.Get("lineage_reign_years"), history.total_time), title);
        }

        public void ShowFinance(WindowMetaTab pArg0)
        {
            Clear();
            Kingdom core = _empire?.CoreKingdom;
            if (!TreasurySystem.Enabled(core))
            {
                BeginPage("empire_finance");
                return;
            }
            TreasuryReport report = TreasurySystem.Report(core);
            TreasuryReport realm = TreasurySystem.Consolidated(core);
            var parent = BeginPage("empire_finance", core.GetKingdomName(),
                string.Format(LM.Get("fiscal_chip_months"), report.months));

            var kpis = parent.BeginColumns(MainWidth);
            float kpiWidth = (MainWidth - 2 * UIHelper.CardGap) / 3f;
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("label_national_treasury"), MoneyDisplay.Format(core.GetTreasuryBalance()));
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("fiscal_operating_balance"), MoneyDisplay.Format(report.operating_balance),
                report.operating_balance < 0 ? "#FF6666" : "#65D66E");
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("fiscal_realm_operating_balance"), MoneyDisplay.Format(realm.operating_balance),
                realm.operating_balance < 0 ? "#FF6666" : "#65D66E");

            AddReconciliation(parent, core, report);
            AddTreasuryChart(parent, TreasurySystem.MonthlyFlows(core));
            AddTaxCollection(parent, _empire);
            AddStateDebt(parent, core);

            // 构成里列了央地拨款，标题的合计也算上它，和下面的条目对得上
            var columns = parent.BeginColumns(MainWidth);
            var incomeCard = columns.BeginColumnCard(HalfWidth, "fiscal_income_breakdown", "#65D66E",
                MoneyDisplay.Format(report.income + report.transfer_in), "ui/icons/iconMoney");
            AddBreakdown(incomeCard, TreasuryRules.SplitTaxTransfers(report.income_sources, report.tax_transfer_sources),
                new Color(0.4f, 0.84f, 0.43f));
            var expenseCard = columns.BeginColumnCard(HalfWidth, "fiscal_expense_breakdown", "#E9A85B",
                MoneyDisplay.Format(report.expense + report.transfer_out), "ui/icons/iconMoney");
            AddBreakdown(expenseCard, report.expense_sources, new Color(0.91f, 0.66f, 0.36f));

            var realmCard = parent.BeginCard(MainWidth, "fiscal_section_realm", "#F3C34A", LM.Get("fiscal_realm_scope"),
                "ui/icons/iconKingdom");
            var cells = realmCard.BeginHoriGroup(new Vector2(MainWidth - 12f, 22f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            float cellWidth = (MainWidth - 12f - 8f) / 5f;
            UIHelper.AddMetricCell(cells, cellWidth, LM.Get("fiscal_income"), MoneyDisplay.Format(realm.income), "#65D66E");
            UIHelper.AddMetricCell(cells, cellWidth, LM.Get("fiscal_expense"), MoneyDisplay.Format(realm.expense), "#E9A85B");
            UIHelper.AddMetricCell(cells, cellWidth, LM.Get("fiscal_realm_cash_balance"), MoneyDisplay.Format(realm.cash_balance),
                realm.cash_balance < 0 ? "#FF6666" : "#65D66E");
            UIHelper.AddMetricCell(cells, cellWidth, LM.Get("fiscal_realm_debt_repayment"), MoneyDisplay.Format(realm.debt_repayment), "#E9A85B");
            // 全境的战争开支(各省自付的也算，见 TreasuryCategory.War)
            UIHelper.AddMetricCell(cells, cellWidth, LM.Get("fiscal_source_War"), MoneyDisplay.Format(realm.war),
                realm.war > 0 ? "#FF6666" : "#8FA0A8");

            // 小字：国库(与悬停提示里的一致)、可支配(国库扣除预留)、安全储备、现金结余、税收转移与记账说明
            string footnote = $"{LM.Get("label_treasury")} {MoneyDisplay.Format(core.GetTreasuryBalance())}  ·  " +
                              $"{LM.Get("fiscal_available")} {MoneyDisplay.Format(StateSettlementSystem.DiscretionaryBalance(core))}  ·  " +
                              $"{LM.Get("fiscal_safety_reserve")} {MoneyDisplay.Format(TreasurySystem.SafetyReserve(core))}  ·  " +
                              $"{LM.Get("fiscal_cash_balance")} {MoneyDisplay.Format(report.cash_balance)}\n" +
                              $"{TreasurySystem.TransferText(report)}\n{LM.Get("fiscal_account_hint")}";
            var hint = parent.AddTextIntoVertLayout(footnote.ColorString("#8FA0A8"), true, TextAnchor.UpperLeft,
                new Vector2(MainWidth - 4f, 30f));
            hint.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            StartCoroutine(RefreshScrollLayout(resetToTop: !_financeKeepScroll));
            _financeKeepScroll = false;
            StartCoroutine(RefreshFinanceLater(pArg0, _renderGeneration));
        }

        // 财政页开着时，游戏时间走了(没暂停)才按最新账目重画；打开窗口游戏多半暂停着，账目不变就不重画。滚动位置不动
        private const float FinanceRefreshSeconds = 3f;
        private bool _financeKeepScroll;

        private IEnumerator RefreshFinanceLater(WindowMetaTab tab, int generation)
        {
            double drawnAt = World.world?.getCurWorldTime() ?? 0d;
            while (true)
            {
                yield return new WaitForSecondsRealtime(FinanceRefreshSeconds);
                if (generation != _renderGeneration || !gameObject.activeInHierarchy) yield break;
                if ((World.world?.getCurWorldTime() ?? drawnAt) != drawnAt) break;
            }
            _financeKeepScroll = true;
            ShowFinance(tab);
        }

        // 近 12 个月收支图：流入、流出各取一年合计最大的四类上色，其余并为"其他"；悬停某月看当月明细
        private static readonly Color[] InflowColors =
            { new(0.40f, 0.84f, 0.43f), new(0.25f, 0.69f, 0.60f), new(0.62f, 0.88f, 0.94f), new(0.71f, 0.89f, 0.42f) };
        private static readonly Color[] OutflowColors =
            { new(0.91f, 0.66f, 0.36f), new(0.88f, 0.35f, 0.31f), new(0.79f, 0.65f, 0.91f), new(0.85f, 0.55f, 0.69f) };
        private static readonly Color OtherFlow = new(0.55f, 0.58f, 0.52f);
        private static readonly Color NetLine = new(0.95f, 0.76f, 0.29f);

        private void AddTreasuryChart(AutoVertLayoutGroup parent, List<TreasuryMonth> months)
        {
            List<TreasuryMonth> recorded = months.Where(month => month.has_data).ToList();
            string header = recorded.Count == 0 ? null : string.Format(LM.Get("fiscal_chart_avg"),
                SignedMoney(recorded.Sum(month => month.In - month.Out) / recorded.Count));
            var card = parent.BeginCard(MainWidth, "fiscal_chart_title", "#F3C34A", header, "ui/icons/iconMoney");
            float inner = MainWidth - 12f;
            if (recorded.Count == 0)
            {
                AddCardEmptyNote(card, MainWidth);
                return;
            }
            List<string> TopKeys(Func<TreasuryMonth, Dictionary<string, long>> pick) => months
                .SelectMany(month => pick(month)).GroupBy(pair => pair.Key)
                .Select(group => (key: group.Key, total: group.Sum(pair => pair.Value)))
                .Where(item => item.total > 0).OrderByDescending(item => item.total).Take(4).Select(item => item.key).ToList();
            List<string> inKeys = TopKeys(month => month.inflow);
            List<string> outKeys = TopKeys(month => month.outflow);
            List<(Color, long)> Stack(Dictionary<string, long> flows, List<string> keys, Color[] palette)
            {
                var stack = keys.Select((key, index) =>
                    (palette[index], flows.TryGetValue(key, out long amount) ? amount : 0L)).ToList();
                long other = flows.Where(pair => !keys.Contains(pair.Key)).Sum(pair => pair.Value);
                if (other > 0) stack.Add((OtherFlow, other));
                return stack;
            }
            var chartColumns = months.Select(month => new TreasuryChart.Column
            {
                Up = Stack(month.inflow, inKeys, InflowColors),
                Down = Stack(month.outflow, outKeys, OutflowColors),
                Net = month.In - month.Out,
                HasData = month.has_data
            }).ToList();
            var (_, hitAreas) = TreasuryChart.Create(card.transform, new Vector2(inner, 72f), chartColumns, NetLine);
            for (int i = 0; i < months.Count && i < hitAreas.Count; i++)
            {
                TreasuryMonth month = months[i];
                string title = MonthLabel(month);
                string body = !month.has_data ? LM.Get("fiscal_chart_no_record")
                    : $"{LM.Get("fiscal_chart_in")} {MoneyDisplay.Format(month.In)}".ColorString("#65D66E") + "\n" +
                      FlowLines(month.inflow) +
                      $"{LM.Get("fiscal_chart_out")} {MoneyDisplay.Format(month.Out)}".ColorString("#E9A85B") + "\n" +
                      FlowLines(month.outflow) +
                      $"{LM.Get("fiscal_chart_net")} {SignedMoney(month.In - month.Out)}"
                          .ColorString(month.In - month.Out < 0 ? "#FF6666" : "#F3C34A");
                UIHelper.AttachTextTooltip(hitAreas[i], $"fiscal_month_{i}", title, body);
            }

            // 月份(本月标金)
            var labels = card.BeginHoriGroup(new Vector2(inner, 8f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
            float columnWidth = inner / months.Count;
            foreach (TreasuryMonth month in months)
                labels.AddTextIntoHoriLayout(MonthLabel(month).ColorString(month.months_ago == 0 ? "#F3C34A" : "#8FA0A8"), true,
                    TextAnchor.MiddleCenter, new Vector2(columnWidth, 8f)).UseFixedFontSize(5, HorizontalWrapMode.Overflow);

            // 图例：流入一行、流出一行(末尾标出金线)
            AddChartLegend(card, inner, LM.Get("fiscal_chart_in"), inKeys, InflowColors,
                months.Any(month => month.inflow.Keys.Any(key => !inKeys.Contains(key))), null);
            AddChartLegend(card, inner, LM.Get("fiscal_chart_out"), outKeys, OutflowColors,
                months.Any(month => month.outflow.Keys.Any(key => !outKeys.Contains(key))), LM.Get("fiscal_chart_net_legend"));
        }

        // 国库对账：账期初的国库 + 其间全部收入(含央地拨款、举债) − 全部支出(含还本付息) = 现在的国库。
        // 每笔进出都记了账，所以这一行总是对得上；"经常收支"不含举债、还本、建设等，和国库变动本来就不相等
        private static void AddReconciliation(AutoVertLayoutGroup parent, Kingdom core, TreasuryReport report)
        {
            long balance = core.GetTreasuryBalance();
            long inflow = report.income + report.transfer_in, outflow = report.expense + report.transfer_out;
            var card = parent.BeginCard(MainWidth, "fiscal_reconcile_title", "#F3C34A",
                string.Format(LM.Get("fiscal_chip_months"), report.months), "ui/icons/iconMoney");
            card.AddMetricRow(MainWidth,
                (LM.Get("fiscal_reconcile_opening"), MoneyDisplay.Format(balance - report.cash_balance), "#F2EEE2"),
                (LM.Get("fiscal_reconcile_in"), "+" + MoneyDisplay.Format(inflow), "#65D66E"),
                (LM.Get("fiscal_reconcile_out"), "-" + MoneyDisplay.Format(outflow), "#E9A85B"),
                (LM.Get("fiscal_reconcile_closing"), MoneyDisplay.Format(balance), "#F3C34A"));
        }

        // 国债(见 StateDebtSystem)：本金、平均年息、一年的利息、相当于几年经常收入；一年内违约过标红
        private static void AddStateDebt(AutoVertLayoutGroup parent, Kingdom core)
        {
            long debt = StateDebtSystem.Outstanding(core);
            if (debt <= 0L) return;
            float rate = StateDebtSystem.AverageRate(core);
            long income = StateDebtSystem.AnnualStableIncome(core);
            bool defaulted = StateDebtSystem.DefaultedRecently(core);
            var card = parent.BeginCard(MainWidth, "state_debt", defaulted ? "#E66B66" : "#E9A85B",
                LM.Get(defaulted ? "fiscal_debt_defaulted" : "fiscal_debt_servicing"), "ui/icons/iconMoney");
            card.AddMetricRow(MainWidth,
                (LM.Get("fiscal_debt_principal"), MoneyDisplay.Format(debt), "#E9A85B"),
                (LM.Get("fiscal_debt_rate"), rate.ToString("0.0%"), "#F2EEE2"),
                (LM.Get("fiscal_debt_interest"), MoneyDisplay.Format((long)(debt * (double)rate)), "#E9A85B"),
                (LM.Get("fiscal_debt_years"), income > 0 ? (debt / (double)income).ToString("0.0") : "-",
                    debt > income * 2 ? "#FF6666" : "#F2EEE2"));
            var note = card.AddTextIntoVertLayout(LM.Get("fiscal_debt_hint").ColorString("#8FA0A8"), true, TextAnchor.UpperLeft,
                new Vector2(MainWidth - 16f, 16f));
            note.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        }

        // 税赋征收(见 TaxErosionSystem)：上一年应征、实收、征收率，各环节的流失
        private static void AddTaxCollection(AutoVertLayoutGroup parent, Empire empire)
        {
            var (assessed, hidden, fraud, stolen, withheld) = TaxErosionSystem.Ledger(empire);
            if (assessed <= 0) return;
            long collected = Math.Max(0L, assessed - hidden - fraud - stolen);
            float rate = collected / (float)assessed;
            string rateHex = rate >= 0.85f ? "#65D66E" : rate >= 0.6f ? "#F3C34A" : "#FF6666";
            var card = parent.BeginCard(MainWidth, "fiscal_collection_title", "#F3C34A",
                string.Format(LM.Get("fiscal_collection_rate"), rate.ToString("P0")), "ui/icons/iconMoney");
            card.AddMetricRow(MainWidth,
                (LM.Get("fiscal_collection_assessed"), MoneyDisplay.Format(assessed), "#F2EEE2"),
                (LM.Get("fiscal_collection_collected"), MoneyDisplay.Format(collected), rateHex),
                (LM.Get("fiscal_collection_withheld"), MoneyDisplay.Format(withheld), withheld > 0 ? "#E9A85B" : "#8FA0A8"));
            int index = 0;
            foreach ((string key, long value) in new[] { ("fiscal_collection_hidden", hidden), ("fiscal_collection_fraud", fraud),
                         ("fiscal_collection_stolen", stolen) })
            {
                if (value <= 0) continue;
                card.AddListRow(MainWidth, index++, null, LM.Get(key),
                    $"-{MoneyDisplay.Format(value)}  ({value / (float)assessed:P0})", titleHex: "#E9A85B", titleWidth: 90f);
            }
            if (index == 0) card.AddListRow(MainWidth, 0, null, LM.Get("fiscal_collection_clean"), "", titleWidth: 220f);
        }

        private static string MonthLabel(TreasuryMonth month) => month.months_ago == 0
            ? LM.Get("fiscal_chart_current")
            : string.Format(LM.Get("fiscal_chart_month"), month.month);

        private static string SignedMoney(long value) => (value > 0 ? "+" : "") + MoneyDisplay.Format(value);

        private static string FlowLines(Dictionary<string, long> flows) => string.Concat(flows
            .Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value)
            .Select(pair => $"  {UIHelper.LocalizeOrRaw("fiscal_source_" + pair.Key)} {MoneyDisplay.Format(pair.Value)}\n"
                .ColorString("#C8C2B4")));

        private static void AddChartLegend(AutoVertLayoutGroup card, float inner, string title, List<string> keys,
            Color[] palette, bool other, string netLegend)
        {
            var row = card.BeginHoriGroup(new Vector2(inner, 9f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            row.AddTextIntoHoriLayout(title.ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                new Vector2(UIHelper.EstimateTextWidth(title, 6) + 2f, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var items = keys.Select((key, index) => (palette[index], UIHelper.LocalizeOrRaw("fiscal_source_" + key))).ToList();
            if (other) items.Add((OtherFlow, LM.Get("fiscal_chart_other")));
            if (netLegend != null) items.Add((NetLine, netLegend));
            foreach ((Color color, string name) in items)
            {
                var swatch = new GameObject("Swatch", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                    typeof(LayoutElement));
                swatch.transform.SetParent(row.transform, false);
                ((RectTransform)swatch.transform).sizeDelta = new Vector2(5f, 5f);
                swatch.GetComponent<Image>().color = color;
                swatch.GetComponent<Image>().raycastTarget = false;
                LayoutElement element = swatch.GetComponent<LayoutElement>();
                element.preferredWidth = element.minWidth = 5f;
                element.preferredHeight = element.minHeight = 5f;
                row.AddTextIntoHoriLayout(name.ColorString("#C8C2B4"), true, TextAnchor.MiddleLeft,
                    new Vector2(UIHelper.EstimateTextWidth(name, 6) + 2f, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            }
        }

        // 收支构成：按金额取前六项画条形，条长相对最大一项
        private static void AddBreakdown(AutoVertLayoutGroup card, Dictionary<string, long> sources, Color color)
        {
            float inner = HalfWidth - 12f;
            var items = (sources ?? new Dictionary<string, long>()).Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value).Take(6).ToList();
            if (items.Count == 0)
            {
                AddCardEmptyNote(card, HalfWidth);
                return;
            }
            float max = items[0].Value;
            foreach (KeyValuePair<string, long> item in items)
                UIHelper.AddBarRow(card, inner, UIHelper.LocalizeOrRaw("fiscal_source_" + item.Key),
                    MoneyDisplay.Format(item.Value), item.Value / max, color);
        }

        // 属国页签：朝贡国与核心王国的封建附庸(陪臣缩进一层)，显示控制等级与年贡，点行打开对应国家窗口
        public void ShowDependencies(WindowMetaTab pArg0)
        {
            Clear();
            var tributaries = World.world.kingdoms.list
                .Where(k => k != null && !k.isRekt() && k.GetTakenAllianceEmpire() == _empire)
                .OrderByDescending(TributeEstimate).ToList();
            Kingdom core = _empire?.CoreKingdom;
            var vassals = core != null && !core.isRekt()
                ? FeudalVassalService.GetDirectVassals(core).ToList() : new List<Kingdom>();
            long totalTribute = tributaries.Sum(TributeEstimate) + vassals.Sum(FeudalVassalService.YearlyTribute);
            // 春秋战国：周室诸侯(战国时已离开帝国、仍奉王室)单列，按国力排
            List<Kingdom> zhou = ZhouEraSystem.Era(_empire) >= ZhouEraSystem.Spring
                ? ZhouEraSystem.Vassals(_empire).OrderByDescending(ZhouEraSystem.Power).ToList()
                : new List<Kingdom>();
            var parent = BeginPage("empire_dependencies",
                zhou.Count > 0 ? string.Format(LM.Get("dependency_chip_zhou"), zhou.Count) : null,
                string.Format(LM.Get("dependency_chip_tributaries"), tributaries.Count),
                string.Format(LM.Get("dependency_chip_vassals"), vassals.Count),
                string.Format(LM.Get("dependency_chip_total"), MoneyDisplay.Format(totalTribute)));
            EmpireCraftStatsRow zhouRows = null;
            if (zhou.Count > 0)
            {
                var zhouCard = parent.BeginCard(MainWidth, "zhou_vassal_states", "#F3C34A", LM.Get("zhou_vassal_sort"),
                    "ui/icons/iconKingdom");
                zhouRows = zhouCard.AddComponent<EmpireCraftStatsRow>();
                zhouRows.WideLayout = true;
                foreach (Kingdom state in zhou)
                {
                    string value = string.Format(LM.Get("zhou_vassal_value"), ZhouPanel.Rank(state),
                        state.cities?.Count ?? 0, state.countTotalWarriors());
                    if (ZhouEraSystem.IsKing(state)) value += " " + LM.Get("zhou_tag_king");
                    if (ZhouEraSystem.IsReformed(state)) value += " " + LM.Get("zhou_tag_reform");
                    AddDependencyRow(zhouRows, state, value);
                }
            }
            // 横屏排版：朝贡国与封建附庸左右并排对照，标题条右侧给排序口径
            var columns = parent.BeginColumns(MainWidth);
            var tributaryCard = columns.BeginColumnCard(HalfWidth, "empire_tributaries", "#7FD8EA",
                LM.Get("dependency_sort_tribute"), "ui/icons/iconKings");
            EmpireCraftStatsRow tributaryRows = tributaryCard.AddComponent<EmpireCraftStatsRow>();
            tributaryRows.WideLayout = true;
            foreach (Kingdom tributary in tributaries)
                AddDependencyRow(tributaryRows, tributary, string.Format(LM.Get("dependency_tribute_value"),
                    MoneyDisplay.Format(TributeEstimate(tributary))));
            if (tributaries.Count == 0) AddCardEmptyNote(tributaryCard, HalfWidth);

            var vassalCard = columns.BeginColumnCard(HalfWidth, "empire_feudal_vassals", "#7FD8EA",
                LM.Get("dependency_sort_vassal"), "ui/icons/iconCrown");
            EmpireCraftStatsRow vassalRows = vassalCard.AddComponent<EmpireCraftStatsRow>();
            vassalRows.WideLayout = true;
            int shown = 0;
            foreach (Kingdom vassal in vassals) shown += AddVassalRows(vassalRows, vassal, 0);
            if (shown == 0) AddCardEmptyNote(vassalCard, HalfWidth);
            StartCoroutine(ShowCardRows(zhouRows, tributaryRows, vassalRows));
        }

        private static void AddCardEmptyNote(AutoVertLayoutGroup card, float width)
        {
            card.AddTextIntoVertLayout(LM.Get("label_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleCenter,
                new Vector2(width - 12f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
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
            // 正在打仗的国家行首换成战争图标，点图标看战争详情(见 WarBadge)；点行打开国家窗口
            KeyValueField field = rows.IShowStatsRow(name ?? kingdom.GetKingdomName(), value, kingdom.getColor().color_text,
                pIconPath: WarBadge.IconFor(kingdom, "iconKings"), action: () =>
                {
                    if (kingdom == null || kingdom.isRekt()) return;
                    SelectedMetas.selected_kingdom = kingdom;
                    ScrollWindow.showWindow("kingdom");
                });
            WarBadge.Bind(field, kingdom);
        }

        // 朝贡年贡估计(与 StartToTaken 同口径：户数的一半；财政下实缴受国库限制，这里是名义值)
        private static long TributeEstimate(Kingdom k) =>
            (CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(k) : k.units?.Count ?? 0) / 2;

        //显示宪法：条款可点击修改(见 ConstitutionSystem.CycleByPlayer)，手定的条款不再随制度自动变化
        public void ShowConstitution(WindowMetaTab pArg0)
        {
            Clear();
            string color = _empire.getColor().color_text;
            ConstitutionData constitution = ConstitutionSystem.Get(_empire);
            if (constitution == null)
            {
                var none = BeginPage("empire_constitution");
                none.AddTextIntoVertLayout(LM.Get("constitution_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleCenter,
                    new Vector2(MainWidth, 14f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
                StartCoroutine(RefreshScrollLayout(resetToTop: true));
                return;
            }
            string age = string.Format(LM.Get(constitution.provisional ? "constitution_age_provisional" : "constitution_age"),
                Mathf.Max(0, Date.getYearsSince(constitution.promulgated_at)), constitution.number);
            var parent = BeginPage($"《{constitution.name}》", age);
            var columns = parent.BeginColumns(MainWidth);

            // 左栏：条款(点值循环修改；手定的条款不再随制度自动变化)
            var clauseCard = columns.BeginColumnCard(HalfWidth, "constitution_section_clauses", "#F3C34A",
                LM.Get("constitution_click_hint"), "ui/icons/iconWorldLaws");
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

            // 右栏：民族情绪 / 思想疲劳两块主指标 + 思想与路线(含国家状态)
            var right = columns.BeginColumn(HalfWidth);
            ConstitutionalEconomyState dynamics = _empire.data?.constitutional_economy;
            var kpis = right.BeginColumns(HalfWidth);
            float kpiWidth = (HalfWidth - UIHelper.CardGap) / 2f;
            float sentiment = NationalSentimentSystem.GetEmpire(_empire);
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("national_sentiment"), $"{sentiment:0}%",
                sentiment >= 60f ? "#FF8A7A" : sentiment >= 30f ? "#FFD34E" : "#F3C34A");
            float fatigue = dynamics?.ideology_fatigue ?? 0f;
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("ideology_fatigue"), $"{fatigue:0}%",
                fatigue >= 60f ? "#FF8A7A" : fatigue >= 30f ? "#FFD34E" : "#7EE6A8");
            var dynamicsCard = right.BeginCard(HalfWidth, "constitution_section_dynamics", "#F3C34A", null, "ui/icons/iconKnowledge");
            EmpireCraftStatsRow dynamicsRows = dynamicsCard.AddComponent<EmpireCraftStatsRow>();
            dynamicsRows.WideLayout = true;
            if (dynamics != null)
            {
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
            if (CityPopulationSystem.AbstractPopulationEnabled && GranarySystem.Enabled(_empire.CoreKingdom))
                dynamicsRows.IShowStatsRow("granary", $"{GranarySystem.Stock(_empire.CoreKingdom):0}", color);
            EmpireCore frontCore = EmpireCoreManager.Get(_empire);
            if (frontCore?.warlord_parent_core_id > 0) frontCore = EmpireCoreManager.Get(frontCore.warlord_parent_core_id) ?? frontCore;
            if (NationalSentimentSystem.InUnitedFront(frontCore))
                dynamicsRows.IShowStatsRow("nation_united_front", LM.Get("nation_united_front_active"), "#8FE7FF");

            // 修宪记录：最近六次排成三列格，标题条给总次数
            List<ConstitutionAmendmentRecord> amendments = constitution.amendments ?? new List<ConstitutionAmendmentRecord>();
            var amendmentCard = parent.BeginCard(MainWidth, "constitution_section_amendments", "#F3C34A",
                $"{LM.Get("constitution_amendments")}: {amendments.Count}", "ui/iconHistory", "ui/icons/iconKnowledge");
            float tileWidth = (MainWidth - 12f - 2 * 3f) / 3f;
            AutoHoriLayoutGroup amendmentRow = null;
            int inRow = 0;
            foreach (ConstitutionAmendmentRecord record in Enumerable.Reverse(amendments).Take(6))
            {
                if (amendmentRow == null || inRow == 3)
                {
                    amendmentRow = amendmentCard.BeginHoriGroup(new Vector2(MainWidth - 12f, 30f), TextAnchor.UpperLeft, 3,
                        new RectOffset(0, 0, 0, 0));
                    inRow = 0;
                }
                string when = string.Format(LM.Get("constitution_years_ago"), Mathf.Max(0, Date.getYearsSince(record.time)));
                string by = LM.Get(record.by switch
                {
                    "player" => "constitution_by_player",
                    "line" => "constitution_by_line",
                    "pressure" => "constitution_by_pressure",
                    _ => "constitution_by_sync"
                });
                var tile = amendmentRow.BeginVertGroup(new Vector2(tileWidth, 30f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
                    pPadding: new RectOffset(4, 3, 1, 1));
                UIHelper.AddInsetBackground(tile, new Vector2(tileWidth, 30f));
                var clause = tile.AddTextIntoVertLayout(UIHelper.LocalizeOrRaw($"constitution_clause_{record.clause}"), true,
                    TextAnchor.MiddleLeft, new Vector2(tileWidth - 7f, 9f));
                clause.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
                var change = tile.AddTextIntoVertLayout(
                    $"{ConstitutionSystem.ValueText(record.clause, record.from)} → {ConstitutionSystem.ValueText(record.clause, record.to)}"
                        .ColorString("#F3C34A"), true, TextAnchor.MiddleLeft, new Vector2(tileWidth - 7f, 10f));
                change.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
                HoverMarqueeText.Attach(change);
                tile.AddTextIntoVertLayout($"{when} · {by}".ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                    new Vector2(tileWidth - 7f, 8f)).UseFixedFontSize(5, HorizontalWrapMode.Overflow);
                inRow++;
            }
            if (amendments.Count == 0) AddCardEmptyNote(amendmentCard, MainWidth);

            // 条款说明：静态参考文字，小字浅色收在页尾
            parent.AddSectionHeader(MainWidth, "constitution_section_notes", "#9A948A");
            foreach (string hintKey in new[] { "constitution_ideology_intensity_hint", "constitution_speech_hint",
                "constitution_nation_hint", "constitution_civil_service_hint", "constitution_farmland_hint", "constitution_grain_hint" })
            {
                var note = parent.AddTextIntoVertLayout(LM.Get(hintKey).ColorString("#9A948A"),
                    true, TextAnchor.MiddleLeft, new Vector2(MainWidth - 4f, 18f));
                note.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            }
            StartCoroutine(ShowCardRows(clauseRows, dynamicsRows));
        }

        //显示个人历史
        public void ShowPersonalHistory()
        {
            Clear();
            var currentHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (currentHistory == null) return;
            var parent = BeginPage("empire_reign_history");
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
                pSize: new Vector2(MainWidth, 34));
            AddReignInfoColumn(reignCard, LM.Get("year_name"), eraName, new Color(1f, 0.78f, 0.2f), new Vector2(80, 30), 9);
            AddReignInfoColumn(reignCard, IsRepublicRecord(currentHistory) ? OfficeTitle(currentHistory) : LM.Get("emperor"),
                emperorName, _empire.getColor()._color_text, new Vector2(140, 30), 11);
            AddReignInfoColumn(reignCard, LM.Get("empire_reign_duration"), $"{reignYears}{LM.Get("Year")}",
                new Color(0.25f, 0.9f, 0.8f), new Vector2(80, 30), 9);
            reignCard.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(MainWidth, 34));

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
                    EmpireHistoryWindow.ListHistoryDescriptions(lasDes, d, parent, MainWidth);
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
                // 卡片/栏层层套着自适应高度：子级的新高度要到下一轮布局才被父级读到，连续重建几次一帧内收敛
                for (int pass = 0; pass < 3; pass++)
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
        // 官僚页签：官制、议会/内阁、虚拟爵位直接画在右栏里(原来的独立官僚窗口并进来了)
        public void ShowBureau(WindowMetaTab pArg0)
        {
            Clear();
            if (_empire?.CoreKingdom == null || _empire.CoreKingdom.isRekt()) return;
            var parent = BeginPage("empire_bureau");
            try
            {
                EmpireBeaurauWindow.Instance?.RenderInto(parent, _empire, MainWidth - 6f);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口官僚页绘制失败({_empire?.data?.name}): {exception}");
            }
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
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
        // 打开窗口时直接切到的页签(只用一次)，如地图兵牌点进来的军团页
        private static string _pendingTab;

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            string pendingTab = _pendingTab;
            _pendingTab = null;
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
            StartCoroutine(pendingTab == null ? ShowKingdomList() : OpenTabNextFrame(pendingTab));
        }

        // 等页签按钮自己的初始动作做完，再按页签按钮的方式切过去(页签高亮一起换)
        private IEnumerator OpenTabNextFrame(string tabName)
        {
            Clear();
            yield return CoroutineHelper.wait_for_next_frame;
            WindowMetaTab tab = TabsWindow.tabs._tabs.FirstOrDefault(item => item.name == tabName);
            if (tab != null) tab.doAction();
            else yield return ShowKingdomList();
        }

        public override void OnNormalDisable()
        {
            base.OnNormalDisable();
            ClearMilitaryFocus();
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

    }
}
