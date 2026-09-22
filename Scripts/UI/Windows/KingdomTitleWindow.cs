using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class KingdomTitleWindow : AutoLayoutWindow<KingdomTitleWindow>
    {
        private static readonly Color SectionColor = new Color(0.7f, 0.9f, 1f);
        private static readonly Color CyanColor = new Color(0.35f, 0.85f, 1f);
        private static readonly Color GreenColor = new Color(0.25f, 0.9f, 0.55f);
        private static readonly Color GoldColor = new Color(1f, 0.78f, 0.28f);

        private enum ContentTab
        {
            Overview,
            Culture,
            NameHistory
        }

        public KingdomTitle title { get; set; }
        public TextInput titleNameInput;
        public TextInput provinceNameInput;
        private AutoVertLayoutGroup _topPart;
        private AutoVertLayoutGroup _content;
        private ContentTab _activeTab;
        private string _selectedCulture;

        // Phase 16.6：玩家在"文化地名历史"标签页里手动点了"添加文化"按钮、但还没输入
        // 名字的候选文化。CultureService.RecordTitleCulturalName/RecordProvinceCulturalName
        // 对空字符串直接无视（不会写进存档），所以这些还没起名的候选文化本身没有任何
        // 持久化记录可言——只能靠这个纯 UI 层的瞬时集合让它们在窗口关闭之前先"占个位置"，
        // 一旦玩家真正输入了名字，就会自动转成 title_name_history/province_name_history
        // 里的正式记录，不再需要靠这个集合撑着。跟 _selectedCulture 一样，只在切换选中
        // 头衔（OnNormalEnable）时才重置，RefreshContent 触发的重建不会清空它。
        private HashSet<string> _manualNameHistoryCultures = new HashSet<string>();

        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 80, 3);
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 80, 3);
            title = EmpireCraftMetaTypeLibrary.selected_kingdomTitle;
            Clear();
            if (title == null || title.isRekt() || title.data == null) return;
            title.checkActive();
            title.isBeenControlled();
            _activeTab = ContentTab.Overview;
            _selectedCulture = CultureService.GetEffectiveTitleCulture(title);
            _manualNameHistoryCultures = new HashSet<string>();
            InitialTabButtons();
            ActivateTab("kingdom_title_overview_tab");
            BuildContent();
        }

        private void Clear()
        {
            if (_topPart != null)
            {
                Destroy(_topPart.gameObject);
                _topPart = null;
            }
            if (_content != null)
            {
                Destroy(_content.gameObject);
                _content = null;
            }
            titleNameInput = null;
            provinceNameInput = null;
        }

        private void BuildContent()
        {
            BuildIdentityCard();

            _content = this.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter);
            AddChild(_content.gameObject);

            switch (_activeTab)
            {
                case ContentTab.Culture:
                    BuildCultureSection();
                    return;
                case ContentTab.NameHistory:
                    BuildNameHistorySection();
                    return;
            }

            BuildMetrics();
            List<Kingdom> administrations = FindCurrentAdministrations();
            BuildCurrentRelations(administrations);
            BuildJurisdictionHistory(administrations);
            BuildCityGrid();
        }

        private void InitialTabButtons()
        {
            if (ScrollWindowComponent.tabs._tabs.All(tab => tab.name != "kingdom_title_overview_tab"))
            {
                SimpleWindowTab overviewTab = Instantiate(SimpleWindowTab.Prefab);
                overviewTab.Setup("kingdom_title_overview_tab", ScrollWindowComponent,
                    action: _ => ShowTab(ContentTab.Overview),
                    sprite: SpriteTextureLoader.getSprite("ui/iconHistory"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(tab => tab.name != "kingdom_title_culture_tab"))
            {
                SimpleWindowTab cultureTab = Instantiate(SimpleWindowTab.Prefab);
                cultureTab.Setup("kingdom_title_culture_tab", ScrollWindowComponent,
                    action: _ => ShowTab(ContentTab.Culture),
                    sprite: SpriteTextureLoader.getSprite("ui/icons/iconOptions"));
            }
            if (ScrollWindowComponent.tabs._tabs.All(tab => tab.name != "kingdom_title_name_history_tab"))
            {
                SimpleWindowTab nameHistoryTab = Instantiate(SimpleWindowTab.Prefab);
                nameHistoryTab.Setup("kingdom_title_name_history_tab", ScrollWindowComponent,
                    action: _ => ShowTab(ContentTab.NameHistory),
                    sprite: SpriteTextureLoader.getSprite("ui/icons/iconCulture"));
            }
        }

        private void ShowTab(ContentTab tab)
        {
            _activeTab = tab;
            if (tab != ContentTab.Overview && !CultureService.IsValidCulture(_selectedCulture))
                _selectedCulture = CultureService.GetEffectiveTitleCulture(title);
            // 切换 Tab 时视为进入新的内容区域，滚动条回到顶部符合预期，不需要保留旧位置。
            RefreshContent(preserveScroll: false);
        }

        private void ActivateTab(string tabName)
        {
            WindowMetaTab tab = ScrollWindowComponent.tabs._tabs.FirstOrDefault(item => item.name == tabName);
            if (tab == null) return;
            ScrollWindowComponent.tabs.disableTabs();
            ScrollWindowComponent.tabs.enableTab(tab);
        }

        private void BuildIdentityCard()
        {
            _topPart = this.BeginVertGroup(pSize: new Vector2(205, 76), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            HoverMarqueeText.Attach(_topPart.AddTextIntoVertLayout(
                (title.data.name ?? LM.Get("label_none")).ColorString(pColor: GoldColor), true,
                TextAnchor.MiddleCenter, new Vector2(190, 16)));
            AddInputRow(_topPart, LM.Get("title_name"), title.data.name, NameChange, out titleNameInput);
            AddInputRow(_topPart, LM.Get("province_name"), title.data.province_name, ProvinceNameChange,
                out provinceNameInput);
            string capitalName = title.title_capital?.GetCityName() ?? LM.Get("label_none");
            _topPart.AddTextIntoVertLayout($"{LM.Get("kingdom_title_capital")}: {capitalName}", true,
                TextAnchor.MiddleCenter, new Vector2(190, 10));
            _topPart.transform.AddStretchBackground("clanFrame", new Vector2(205, 76));
            _topPart.gameObject.AdjustTopPart(transform.parent.transform, new Vector2(0, 1));
            RectTransform topRect = _topPart.GetComponent<RectTransform>();
            topRect.sizeDelta = new Vector2(205, 76);
            LayoutRebuilder.ForceRebuildLayoutImmediate(topRect);
        }

        private static void AddInputRow(AutoVertLayoutGroup parent, string label, string value,
            UnityEngine.Events.UnityAction<string> action, out TextInput input)
        {
            var row = parent.BeginHoriGroup(pSize: new Vector2(196, 19), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            var labelText = row.AddTextIntoHoriLayout(label, true, TextAnchor.MiddleRight, new Vector2(52, 15));
            labelText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            input = UnityEngine.Object.Instantiate(TextInput.Prefab, null);
            input.Setup(value ?? "", action);
            input.SetSize(new Vector2(138, 18));
            row.AddChild(input.gameObject);
        }

        private void BuildMetrics()
        {
            var metrics = _content.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleCenter);
            AddMetricCard(metrics, LM.Get("kingdom_title_cities"), title.city_list.Count.ToString(), CyanColor);
            AddMetricCard(metrics, LM.Get("i_population"), title.countPopulation().ToString(), GreenColor);
            AddMetricCard(metrics, LM.Get("kingdom_title_zones"), title.countZones().ToString(), GoldColor);
        }

        private static void AddMetricCard(AutoHoriLayoutGroup parent, string label, string value, Color color)
        {
            var card = parent.BeginVertGroup(pSize: new Vector2(62, 28), pSpacing: -2,
                pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout(value.ColorString(pColor: color), true, TextAnchor.MiddleCenter,
                new Vector2(30, 12));
            var labelText = card.AddTextIntoVertLayout(label, true, TextAnchor.MiddleCenter,
                new Vector2(58, 10));
            labelText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            card.transform.AddStretchBackground("FactionFrame", new Vector2(62, 28));
        }

        private void BuildCultureSection()
        {
            AddSectionTitle(LM.Get("kingdom_title_culture"));
            string effectiveCulture = CultureService.GetEffectiveTitleCulture(title);
            if (!CultureService.IsValidCulture(_selectedCulture)) _selectedCulture = effectiveCulture;
            string displayCulture = CultureService.IsValidCulture(effectiveCulture)
                ? effectiveCulture.GetCultureTranslate()
                : LM.Get("label_none");
            string source = CultureService.IsInheritingEmpireCulture(title)
                ? LM.Get("kingdom_title_inherits_empire_culture")
                : LM.Get("kingdom_title_explicit_culture");
            string lockState = title.data.culture_locked
                ? LM.Get("kingdom_title_culture_locked")
                : LM.Get("kingdom_title_culture_unlocked");

            var card = _content.BeginVertGroup(pSize: new Vector2(200, 64), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout($"{LM.Get("kingdom_title_effective_culture")}: {displayCulture}", true,
                TextAnchor.MiddleCenter, new Vector2(190, 12));
            card.AddTextIntoVertLayout($"{source} · {lockState}", true,
                TextAnchor.MiddleCenter, new Vector2(190, 10));
            string selectedDisplay = CultureService.IsValidCulture(_selectedCulture)
                ? _selectedCulture.GetCultureTranslate()
                : LM.Get("label_none");
            card.AddTextIntoVertLayout($"{LM.Get("kingdom_title_selected_culture")}: {selectedDisplay}", true,
                TextAnchor.MiddleCenter, new Vector2(190, 10));
            var actions = card.BeginHoriGroup(pSize: new Vector2(190, 16), pSpacing: 3,
                pAlignment: TextAnchor.MiddleCenter);
            actions.AddButtonIntoHoriLayout("set_title_culture", LM.Get("kingdom_title_set_default_culture"),
                SetSelectedTitleCulture, SpriteTextureLoader.getSprite("ui/changeOfficer"),
                size: new Vector2(61, 15), showTip: false);
            actions.AddButtonIntoHoriLayout("toggle_title_culture_lock", lockState, ToggleCultureLock,
                SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"), size: new Vector2(61, 15), showTip: false);
            actions.AddButtonIntoHoriLayout("clear_title_culture_override", LM.Get("kingdom_title_clear_override"),
                ClearCultureOverride, SpriteTextureLoader.getSprite("ui/changeOfficer"),
                size: new Vector2(61, 15), showTip: false);
            card.transform.AddStretchBackground("FactionFrame", new Vector2(200, 64));

            AddSectionTitle(LM.Get("kingdom_title_select_culture"));
            AutoGridLayoutGroup palette = _content.BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(48, 16), pSpacing: new Vector2(2, 2));
            foreach (string culture in CultureService.CultureKeys)
            {
                string cultureKey = culture;
                string label = culture.GetCultureTranslate();
                if (culture == _selectedCulture) label = label.ColorString(pColor: GoldColor);
                palette.AddButtonIntoGirdLayout($"select_culture_{culture}", label,
                    () => SelectCulture(cultureKey), size: new Vector2(48, 16));
            }

            AddSectionTitle(LM.Get("kingdom_title_city_cultures"));
            foreach (City city in title.city_list.Where(city => city != null && !city.isRekt())
                         .OrderByDescending(city => city == title.title_capital).ThenBy(city => city.GetCityName()))
            {
                AddCityCultureCard(city);
            }
        }

        private void AddCityCultureCard(City city)
        {
            Dictionary<string, float> shares = CultureService.GetCityCultureShares(city);
            string mainCulture = CultureService.GetMainCulture(city);
            string mainDisplay = CultureService.IsValidCulture(mainCulture)
                ? mainCulture.GetCultureTranslate()
                : LM.Get("label_none");
            string shareText = string.Join(" · ", shares.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key.GetCultureTranslate()} {pair.Value:0.#}%"));
            // 卡片实际内容：标题行(12) + 改名输入行(19) + 占比汇总行(11) + 每个文化一行(16×N)
            // + 底部"添加所选文化"按钮(15)，一共 4+N 个元素，元素之间还有 pSpacing=1 的间距，
            // 即 (4+N-1) 段空隙——加起来正好是 60+17×N。旧公式算出来的是 54+17×N，
            // 固定少算了 6，导致按钮和卡片背景框的下边缘对不上：背景框比实际内容矮一截，
            // 按钮视觉上探出框外，紧接着的下一张卡片又从这个偏矮的位置起画，看起来又挤
            // 又乱、也不对称。这里按元素实际高度重新算一遍，不再是拍脑袋的经验值。
            float cardHeight = 60 + shares.Count * 17;
            var card = _content.BeginVertGroup(pSize: new Vector2(200, cardHeight), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            HoverMarqueeText.Attach(card.AddTextIntoVertLayout(
                $"{city.GetCityName()} · {LM.Get("kingdom_title_main_culture")}: {mainDisplay}", true,
                TextAnchor.MiddleCenter, new Vector2(190, 12)));
            AddInputRow(card, LM.Get("kingdom_title_city_name"), city.data.name,
                value => CityNameChange(city, value), out _);
            HoverMarqueeText.Attach(card.AddTextIntoVertLayout(shareText, true,
                TextAnchor.MiddleCenter, new Vector2(190, 11)));
            foreach (KeyValuePair<string, float> share in shares.OrderByDescending(pair => pair.Value))
            {
                string cultureKey = share.Key;
                var row = card.BeginHoriGroup(pSize: new Vector2(190, 16), pSpacing: 2,
                    pAlignment: TextAnchor.MiddleCenter);
                HoverMarqueeText.Attach(row.AddTextIntoHoriLayout(
                    $"{cultureKey.GetCultureTranslate()} {share.Value:0.#}%", true,
                    TextAnchor.MiddleLeft, new Vector2(92, 14)));
                row.AddButtonIntoHoriLayout($"decrease_{city.id}_{cultureKey}", "-5%",
                    () => AdjustCityCulture(city, cultureKey, -5f), size: new Vector2(45, 14));
                row.AddButtonIntoHoriLayout($"increase_{city.id}_{cultureKey}", "+5%",
                    () => AdjustCityCulture(city, cultureKey, 5f), size: new Vector2(45, 14));
            }
            card.AddButtonIntoVertLayout($"add_selected_culture_{city.id}",
                LM.Get("kingdom_title_add_selected_culture"), () => AddSelectedCulture(city),
                SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"),
                size: new Vector2(150, 15), showTip: false);
            card.transform.AddStretchBackground(city == title.title_capital ? "FactionFrame_dominate" : "FactionFrame",
                new Vector2(200, cardHeight));
        }

        private void SelectCulture(string culture)
        {
            if (!CultureService.IsValidCulture(culture)) return;
            _selectedCulture = culture;
            RefreshContent();
        }

        private void SetSelectedTitleCulture()
        {
            if (!CultureService.SetTitleCulture(title, _selectedCulture, explicitCulture: true,
                    locked: title?.data?.culture_locked == true))
            {
                ActionLibrary.showWhisperTip("kingdom_title_culture_invalid");
                return;
            }
            RefreshContent();
        }

        private void ToggleCultureLock()
        {
            if (title?.data == null) return;
            CultureService.SetTitleCultureLock(title, !title.data.culture_locked);
            RefreshContent();
        }

        private void ClearCultureOverride()
        {
            if (title?.data == null) return;
            title.data.culture_explicit = false;
            CultureService.RefreshCultureDependents(title);
            RefreshContent();
        }

        private void AddSelectedCulture(City city)
        {
            if (!CultureService.IsValidCulture(_selectedCulture))
            {
                ActionLibrary.showWhisperTip("kingdom_title_culture_invalid");
                return;
            }
            float current = CultureService.GetCultureShare(city, _selectedCulture);
            CultureService.SetCityCultureShare(city, _selectedCulture, Math.Min(100f, current + 5f));
            // 城市/法理的主流文化不再在这里自动切换：城市主流文化要靠城主发起决议，
            // 法理主流文化要靠"文治"派系发起决议（见 TempFac_文化转化），这里只改占比。
            RefreshContent();
        }

        private void AdjustCityCulture(City city, string culture, float delta)
        {
            float current = CultureService.GetCultureShare(city, culture);
            CultureService.SetCityCultureShare(city, culture, Math.Max(0f, Math.Min(100f, current + delta)));
            RefreshContent();
        }

        // 第三个 Tab：按文化列出这个法理头衔/省份历史上用过的所有名字。
        // 之前头衔名称和省份名称是两份各自独立的列表，同一个文化会拆成两条互不相干的
        // 记录，可以只删头衔不删省份（或反过来），导致同一个文化的记录出现"一半有一半
        // 没有"的不一致状态。用户明确指出这里应该以文化为单位：一个文化一张卡片，
        // 头衔名称和省份名称放在同一张卡片里一起改，删除也是把这个文化的两条记录一起删掉。
        private void BuildNameHistorySection()
        {
            AddSectionTitle(LM.Get("kingdom_title_name_history"));
            _content.AddTextIntoVertLayout(LM.Get("kingdom_title_name_history_hint"), true,
                TextAnchor.MiddleCenter, new Vector2(190, 22));

            // 头衔/省份现在生效的文化，就算还没被记录进历史，也要在列表里露面——不然
            // 玩家永远看不到"现在这个名字属于哪个文化"，也没法从这里改名。第一次显示
            // 就直接把当前名字写进历史，而不是只临时展示。
            string effectiveCulture = CultureService.GetEffectiveTitleCulture(title);
            Dictionary<string, string> titleHistory = title.data.title_name_history;
            Dictionary<string, string> provinceHistory = title.data.province_name_history;
            // Phase 16.6：法理范围内每个文化现在大致占多少比重，现算现用（法理本身不
            // 持久化这份占比）。除了已经有名字记录的文化，占比明显大于零、但还没触发过
            // "文治"改名决议的文化也应该露面——标成"未成为主流文化"，玩家可以提前给它
            // 预设一个名字，等这个文化真的成为主流、决议实际执行的那一刻就会直接用上。
            Dictionary<string, float> shares = CultureService.GetTitleCultureShares(title);

            List<string> cultures = (titleHistory?.Keys ?? Enumerable.Empty<string>())
                .Concat(provinceHistory?.Keys ?? Enumerable.Empty<string>())
                .Concat(shares.Where(pair => pair.Value > 0.01f).Select(pair => pair.Key))
                .Concat(_manualNameHistoryCultures)
                .Where(CultureService.IsValidCulture)
                .Distinct()
                .ToList();
            if (CultureService.IsValidCulture(effectiveCulture) && !cultures.Contains(effectiveCulture))
                cultures.Add(effectiveCulture);
            cultures = cultures
                .OrderByDescending(culture => string.Equals(culture, effectiveCulture, StringComparison.Ordinal))
                .ThenByDescending(culture => shares.TryGetValue(culture, out float share) ? share : 0f)
                .ThenBy(culture => culture.GetCultureTranslate())
                .ToList();

            if (cultures.Count == 0)
            {
                _content.AddTextIntoVertLayout(LM.Get("label_none"), true, TextAnchor.MiddleCenter,
                    new Vector2(100, 12));
            }
            else
            {
                foreach (string culture in cultures)
                {
                    bool isCurrent = string.Equals(culture, effectiveCulture, StringComparison.Ordinal);
                    float sharePercent = shares.TryGetValue(culture, out float share) ? share : 0f;

                    // 只有"现在生效的这个文化"才用头衔/省份现在的真实名字兜底并顺便记录下来；
                    // 其它文化如果只在两份历史里的一份中有记录，另一份就留空，不能借用现在的
                    // 真实名字冒充——那是当前文化专属的，不该串到别的文化头上。候选/手动添加
                    // 的文化两边都留空，等玩家自己输入预设名字。
                    string titleName =
                        titleHistory != null && titleHistory.TryGetValue(culture, out string historicTitle)
                            ? historicTitle
                            : (isCurrent ? title.data.name : "");
                    if (isCurrent && (titleHistory == null || !titleHistory.ContainsKey(culture)) &&
                        !string.IsNullOrWhiteSpace(titleName))
                        CultureService.RecordTitleCulturalName(title, culture, titleName);

                    string provinceName = provinceHistory != null &&
                                           provinceHistory.TryGetValue(culture, out string historicProvince)
                        ? historicProvince
                        : (isCurrent ? title.data.province_name : "");
                    if (isCurrent && (provinceHistory == null || !provinceHistory.ContainsKey(culture)) &&
                        !string.IsNullOrWhiteSpace(provinceName))
                        CultureService.RecordProvinceCulturalName(title, culture, provinceName);

                    AddNameHistoryCard(culture, titleName, provinceName, isCurrent, sharePercent);
                }
            }

            // Phase 16.6：玩家也可以手动把一个到目前为止完全没出现过（占比为 0）的文化
            // 加进这个列表，直接复用 BuildCultureSection 里选择文化用的那一套调色板网格
            // 按钮样式——只列出还没在上面出现过的文化，点一下就临时加进列表，等玩家输入
            // 名字后自动转成正式记录。
            List<string> addableCultures = CultureService.CultureKeys.Where(c => !cultures.Contains(c)).ToList();
            if (addableCultures.Count > 0)
            {
                AddSectionTitle(LM.Get("kingdom_title_name_history_add_culture"));
                AutoGridLayoutGroup addGrid = _content.BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount,
                    pCellSize: new Vector2(48, 16), pSpacing: new Vector2(2, 2));
                foreach (string culture in addableCultures)
                {
                    string cultureKey = culture;
                    addGrid.AddButtonIntoGirdLayout($"add_name_history_{culture}", culture.GetCultureTranslate(),
                        () => AddManualNameHistoryCulture(cultureKey), size: new Vector2(48, 16));
                }
            }
        }

        private void AddManualNameHistoryCulture(string culture)
        {
            if (!CultureService.IsValidCulture(culture)) return;
            _manualNameHistoryCultures.Add(culture);
            RefreshContent();
        }

        private void AddNameHistoryCard(string culture, string titleName, string provinceName, bool isCurrent,
            float sharePercent)
        {
            const float cardHeight = 20 + 12 + 19 + 19 + 4;
            var card = _content.BeginVertGroup(pSize: new Vector2(200, cardHeight), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);

            var header = card.BeginHoriGroup(pSize: new Vector2(196, 18), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            string labelText = culture.GetCultureTranslate();
            if (isCurrent) labelText = labelText.ColorString(pColor: GoldColor);
            var label = header.AddTextIntoHoriLayout(labelText, true, TextAnchor.MiddleLeft,
                new Vector2(160, 16));
            label.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            header.AddButtonIntoHoriLayout($"delete_name_history_{culture}", "",
                () => DeleteNameHistoryForCulture(culture), SpriteTextureLoader.getSprite("ui/iconRemove"),
                size: new Vector2(18, 18), showTip: true);

            // 占比只是现算现用的参考值，真正的"主流文化"判定仍然只看 GetEffectiveTitleCulture——
            // 占比再高，只要"文治"改名决议还没实际触发，就还是"未成为主流文化"的候选状态。
            string shareLabel = $"{LM.Get("kingdom_title_culture_share")}: {sharePercent:0.#}%";
            if (!isCurrent)
                shareLabel += $" · {LM.Get("kingdom_title_not_mainstream_culture").ColorString(pColor: Color.gray)}";
            var shareText = card.AddTextIntoVertLayout(shareLabel, true, TextAnchor.MiddleCenter,
                new Vector2(190, 11));
            shareText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);

            AddInputRow(card, LM.Get("kingdom_title_name_history_title_section"), titleName,
                value => TitleHistoryNameChange(culture, value), out _);
            AddInputRow(card, LM.Get("kingdom_title_name_history_province_section"), provinceName,
                value => ProvinceHistoryNameChange(culture, value), out _);

            card.transform.AddStretchBackground("FactionFrame", new Vector2(200, cardHeight));
        }

        private void TitleHistoryNameChange(string culture, string value)
        {
            if (title?.data == null || string.IsNullOrWhiteSpace(value)) return;
            CultureService.RecordTitleCulturalName(title, culture, value);
            // 如果改的正是头衔现在生效的那个文化对应的名字，顺便同步一下头衔当前显示的名字。
            if (string.Equals(CultureService.GetEffectiveTitleCulture(title), culture, StringComparison.Ordinal))
                title.data.name = value;
            RefreshContent();
        }

        private void ProvinceHistoryNameChange(string culture, string value)
        {
            if (title?.data == null || string.IsNullOrWhiteSpace(value)) return;
            CultureService.RecordProvinceCulturalName(title, culture, value);
            if (string.Equals(CultureService.GetEffectiveTitleCulture(title), culture, StringComparison.Ordinal))
            {
                title.data.province_name = value;
                title.RefreshAdministrativeDivisionNames();
            }
            RefreshContent();
        }

        // 一张卡片对应一个文化，头衔名称和省份名称是同一个文化在这个法理下的完整记录，
        // 所以删除也是一起删——不再允许只删其中一个，避免同一个文化留下不完整的记录。
        private void DeleteNameHistoryForCulture(string culture)
        {
            if (title?.data == null) return;
            CultureService.RemoveTitleCulturalName(title, culture);
            CultureService.RemoveProvinceCulturalName(title, culture);
            _manualNameHistoryCultures.Remove(culture);
            RefreshContent();
        }

        // 每次点击“+5%/-5%”“绑定文化”等按钮都会 Clear()+BuildContent() 整个窗口内容，
        // 这会让承载内容的 ScrollRect 丢失当前滚动位置，表现为“每次新增/修改城市文化，
        // 整个窗口都被重置回顶部”。这里在重建前记录滚动位置，重建后立即恢复。
        private ScrollRect _cachedScrollRect;

        private void RefreshContent(bool preserveScroll = true)
        {
            ScrollRect scrollRect = preserveScroll ? GetOwningScrollRect() : null;
            float? savedAnchoredY = null;
            if (scrollRect != null && scrollRect.content != null)
                savedAnchoredY = scrollRect.content.anchoredPosition.y;

            Clear();
            if (title == null || title.isRekt() || title.data == null) return;
            BuildContent();

            if (scrollRect != null && savedAnchoredY.HasValue)
                RestoreScrollPosition(scrollRect, savedAnchoredY.Value);
        }

        private ScrollRect GetOwningScrollRect()
        {
            if (_cachedScrollRect == null)
                _cachedScrollRect = GetComponentInParent<ScrollRect>();
            return _cachedScrollRect;
        }

        private static void RestoreScrollPosition(ScrollRect scrollRect, float anchoredY)
        {
            if (scrollRect == null || scrollRect.content == null) return;
            // 强制立即完成布局重建，这样下面读取到的 content 高度才是重建后的真实高度，
            // 否则用旧高度换算出来的位置在下一帧又会被 ScrollRect 自己钳制掉。
            Canvas.ForceUpdateCanvases();
            RectTransform viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : scrollRect.GetComponent<RectTransform>();
            float viewportHeight = viewport != null ? viewport.rect.height : 0f;
            float maxY = Mathf.Max(0f, scrollRect.content.rect.height - viewportHeight);
            Vector2 pos = scrollRect.content.anchoredPosition;
            pos.y = Mathf.Clamp(anchoredY, 0f, maxY);
            scrollRect.content.anchoredPosition = pos;
        }

        private void BuildCurrentRelations(List<Kingdom> administrations)
        {
            AddSectionTitle(LM.Get("kingdom_title_current_relations"));
            var grid = _content.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(100, 62), pSpacing: new Vector2(2, 2));

            List<KingdomTitleHolderRelation> holders = ResolveCurrentHolders();
            if (holders.Count == 0)
            {
                AddHolderCard(grid, null, null, true);
            }
            else
            {
                foreach (KingdomTitleHolderRelation holder in holders)
                {
                    AddHolderCard(grid, holder.Holder, holder.Empire, holder.Unlanded);
                }
            }
            if (administrations.Count == 0)
            {
                AddKingdomCard(grid, LM.Get("kingdom_title_administration"), null,
                    LM.Get("kingdom_title_no_administration"));
            }
            else
            {
                foreach (Kingdom administration in administrations)
                {
                    // 直接按这个 administration 实际的 KingdomType 显示真实后缀（军/都护府/道/
                    // 羁縻州/国……），不再把非"军"一律显示成"道"——都护府现在是政体引擎真实
                    // 判定出来的国家类型，跟其他类型是平等的，见 GetKingdomTypeSuffixText。
                    AddKingdomCard(grid, LM.Get("kingdom_title_administration"), administration,
                        administration.GetKingdomTypeSuffixText());
                }
            }

            Kingdom controller = title.control_kingdom;
            if (controller != null && !controller.isRekt() && administrations.All(k => k != controller) &&
                controller != title.main_kingdom)
            {
                AddKingdomCard(grid, LM.Get("kingdom_title_controller"), controller,
                    LM.Get("title_been_controlled"));
            }
        }

        private List<KingdomTitleHolderRelation> ResolveCurrentHolders()
        {
            return KingdomTitleRelationResolver.ResolveCurrentHolders(title);
        }

        private List<Kingdom> FindCurrentAdministrations()
        {
            return KingdomTitleRelationResolver.FindCurrentAdministrations(title);
        }

        private void AddHolderCard(AutoGridLayoutGroup parent, Actor holder, Empire empire, bool unlanded)
        {
            var card = this.BeginVertGroup(pSize: new Vector2(100, 62), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            string heading = unlanded
                ? LM.Get("kingdom_title_unlanded_holder")
                : LM.Get("kingdom_title_holder");
            card.AddTextIntoVertLayout(heading.ColorString(pColor: CyanColor), true,
                TextAnchor.MiddleCenter, new Vector2(94, 11));
            var body = card.BeginHoriGroup(pSize: new Vector2(94, 43), pSpacing: 3,
                pAlignment: TextAnchor.MiddleCenter);
            AddAvatarSlot(body, holder?.id ?? -1L);
            var details = body.BeginVertGroup(pSize: new Vector2(58, 40), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft);
            string holderName = holder?.getName() ?? LM.Get("label_vacant");
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(holderName.ColorString(
                    pColor: holder == null ? Color.gray : GreenColor), true, TextAnchor.MiddleLeft,
                new Vector2(56, 12)));
            string status = holder == null ? LM.Get("label_vacant") :
                unlanded ? LM.Get("label_unlanded_fief") : LM.Get("label_landed_fief");
            details.AddTextIntoVertLayout(status, true, TextAnchor.MiddleLeft, new Vector2(56, 10));
            if (holder != null)
            {
                HoverMarqueeText.Attach(details.AddTextIntoVertLayout(FormatPeerageName(holder, empire), true,
                    TextAnchor.MiddleLeft, new Vector2(56, 10)));
            }
            parent.AddChild(card.gameObject);
            card.transform.AddStretchBackground(holder == null ? "FactionFrame" : "FactionFrame_dominate",
                new Vector2(100, 62));
        }

        private static string FormatPeerageName(Actor holder, Empire empire)
        {
            string peerageName = holder?.GetPeerageDisplayName() ?? "";
            string empireName = empire?.GetEmpireName() ?? "";
            if (string.IsNullOrWhiteSpace(empireName)) return peerageName;
            if (string.IsNullOrWhiteSpace(peerageName)) return empireName;
            return $"{empireName} · {peerageName}";
        }

        private void AddKingdomCard(AutoGridLayoutGroup parent, string heading, Kingdom kingdom, string status)
        {
            var card = this.BeginVertGroup(pSize: new Vector2(100, 62), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout(heading.ColorString(pColor: CyanColor), true,
                TextAnchor.MiddleCenter, new Vector2(94, 11));
            var body = card.BeginHoriGroup(pSize: new Vector2(94, 43), pSpacing: 3,
                pAlignment: TextAnchor.MiddleCenter);
            AddAvatarSlot(body, kingdom?.king?.id ?? -1L);
            var details = body.BeginVertGroup(pSize: new Vector2(58, 40), pSpacing: 0,
                pAlignment: TextAnchor.MiddleLeft);
            string kingdomName = kingdom?.GetKingdomFullName() ?? LM.Get("label_none");
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(kingdomName.ColorString(
                    pColor: kingdom == null ? Color.gray : GoldColor), true, TextAnchor.MiddleLeft,
                new Vector2(56, 12)));
            details.AddTextIntoVertLayout(status, true, TextAnchor.MiddleLeft, new Vector2(56, 10));
            if (kingdom != null)
            {
                details.AddButtonIntoVertLayout("open_kingdom", LM.Get("kingdom_title_details"), () =>
                    MetaType.Kingdom.getAsset().selectAndInspect(kingdom),
                    SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(48, 13), showTip: true);
            }
            parent.AddChild(card.gameObject);
            card.transform.AddStretchBackground(kingdom == null ? "FactionFrame" : "FactionFrame_dominate",
                new Vector2(100, 62));
        }

        private static void AddAvatarSlot(AutoHoriLayoutGroup parent, long actorId)
        {
            var avatarSlot = parent.BeginVertGroup(new Vector2(30, 40), pSpacing: 0,
                pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
            SimpleButton avatar = UIHelper.CreateAvatarView(actorId);
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
            avatarSlot.AddChild(avatar.gameObject);
            avatarSlot.transform.localPosition = Vector3.zero;
        }

        private void BuildJurisdictionHistory(List<Kingdom> administrations)
        {
            if (title.main_kingdom != null && !title.main_kingdom.isRekt())
                title.RecordJurisdiction(title.main_kingdom, KingdomTitle.JurisdictionHolder);
            foreach (Kingdom administration in administrations)
                title.RecordJurisdiction(administration, KingdomTitle.JurisdictionAdministration);

            AddSectionTitle(LM.Get("kingdom_title_history"));
            List<KingdomTitleJurisdictionRecord> records = title.data.jurisdiction_history?.Where(record =>
                    record != null).OrderByDescending(record => record.start_time).ToList()
                ?? new List<KingdomTitleJurisdictionRecord>();
            foreach (KingdomTitleJurisdictionRecord record in records)
            {
                AddHistoryCard(record);
            }

            bool founderAlreadyShown = records.Any(record => record.kingdom_id == title.data.founder_kingdom_id);
            if (!founderAlreadyShown && !string.IsNullOrWhiteSpace(title.data.founder_kingdom_name))
            {
                AddFounderCard();
            }
            if (records.Count == 0 && string.IsNullOrWhiteSpace(title.data.founder_kingdom_name))
            {
                _content.AddTextIntoVertLayout(LM.Get("label_none"), true, TextAnchor.MiddleCenter,
                    new Vector2(100, 12));
            }
        }

        private void AddHistoryCard(KingdomTitleJurisdictionRecord record)
        {
            Kingdom liveKingdom = World.world.kingdoms.get(record.kingdom_id);
            if (liveKingdom?.isRekt() == true) liveKingdom = null;
            bool current = record.end_time < 0;
            string relation = record.relation == KingdomTitle.JurisdictionAdministration
                ? LM.Get("kingdom_title_history_administration")
                : LM.Get("kingdom_title_history_holder");
            string kingdomName = liveKingdom?.GetKingdomFullName();
            if (string.IsNullOrWhiteSpace(kingdomName)) kingdomName = record.kingdom_name;
            if (string.IsNullOrWhiteSpace(kingdomName)) kingdomName = LM.Get("label_none");

            var card = _content.BeginHoriGroup(pSize: new Vector2(200, 36), pSpacing: 2,
                pAlignment: TextAnchor.MiddleCenter);
            var details = card.BeginVertGroup(pSize: new Vector2(176, 32), pSpacing: -1,
                pAlignment: TextAnchor.MiddleLeft);
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(
                $"{relation} · {kingdomName}".ColorString(pColor: current ? GoldColor : SectionColor), true,
                TextAnchor.MiddleLeft, new Vector2(172, 12)));
            details.AddTextIntoVertLayout(FormatPeriod(record.start_time, record.end_time), true,
                TextAnchor.MiddleLeft, new Vector2(172, 10));
            if (liveKingdom != null)
            {
                Kingdom target = liveKingdom;
                card.AddButtonIntoHoriLayout("open_history_kingdom", "", () =>
                    MetaType.Kingdom.getAsset().selectAndInspect(target),
                    SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(16, 16), showTip: true);
            }
            card.transform.AddStretchBackground(current ? "FactionFrame_dominate" : "FactionFrame",
                new Vector2(200, 36));
        }

        private void AddFounderCard()
        {
            var card = _content.BeginVertGroup(pSize: new Vector2(200, 30), pSpacing: -1,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(5, 5, 1, 1));
            HoverMarqueeText.Attach(card.AddTextIntoVertLayout(
                $"{LM.Get("kingdom_title_history_founder")} · {title.data.founder_kingdom_name}"
                    .ColorString(pColor: SectionColor), true, TextAnchor.MiddleLeft, new Vector2(188, 12)));
            string date = title.data.timestamp_established_time > 0
                ? Date.getDate(title.data.timestamp_established_time)
                : LM.Get("kingdom_title_unknown_date");
            card.AddTextIntoVertLayout(date, true, TextAnchor.MiddleLeft, new Vector2(188, 10));
            card.transform.AddStretchBackground("FactionFrame", new Vector2(200, 30));
        }

        private void BuildCityGrid()
        {
            AddSectionTitle(LM.Get("kingdom_title_cities"));
            var grid = _content.BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(48, 40), pSpacing: new Vector2(2, 2));
            foreach (City city in title.city_list.Where(city => city != null && !city.isRekt())
                         .OrderByDescending(city => city == title.title_capital).ThenBy(city => city.GetCityName()))
            {
                var card = this.BeginVertGroup(pSize: new Vector2(48, 40), pSpacing: 0,
                    pAlignment: TextAnchor.MiddleCenter);
                HoverMarqueeText.Attach(card.AddTextIntoVertLayout(city.GetCityName().ColorString(
                        pColor: city == title.title_capital ? GoldColor : SectionColor), true,
                    TextAnchor.MiddleCenter, new Vector2(44, 11)));
                string status = city == title.title_capital
                    ? LM.Get("kingdom_title_capital")
                    : city.kingdom?.GetKingdomName() ?? LM.Get("label_none");
                HoverMarqueeText.Attach(card.AddTextIntoVertLayout(status, true, TextAnchor.MiddleCenter,
                    new Vector2(44, 9)));
                City target = city;
                card.AddButtonIntoVertLayout("open_title_city", "", () =>
                    MetaType.City.getAsset().selectAndInspect(target),
                    SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(14, 14), showTip: true);
                grid.AddChild(card.gameObject);
                card.transform.AddStretchBackground(city == title.title_capital ? "FactionFrame_dominate" : "FactionFrame",
                    new Vector2(48, 40));
            }
        }

        private void AddSectionTitle(string text)
        {
            _content.AddTextIntoVertLayout(text.ColorString(pColor: SectionColor), true,
                TextAnchor.MiddleCenter, new Vector2(150, 13));
        }

        private static string FormatPeriod(double start, double end)
        {
            string startText = start > 0 ? Date.getDate(start) : LM.Get("kingdom_title_unknown_date");
            string endText = end < 0 ? LM.Get("kingdom_title_current") : Date.getDate(end);
            return $"{startText} - {endText}";
        }

        private void NameChange(string value)
        {
            if (title?.data == null) return;
            title.data.name = value;
        }

        private void ProvinceNameChange(string value)
        {
            if (title?.data == null) return;
            title.data.province_name = value;
            title.RefreshAdministrativeDivisionNames();
        }

        // 手动改城市名字：直接改名，同时把新名字记进这座城市当前主流文化的历史里，
        // 这样以后文化换来换去、又换回这个文化时，玩家手打的这个名字也不会丢。
        private void CityNameChange(City city, string value)
        {
            if (city?.data == null || string.IsNullOrWhiteSpace(value)) return;
            if (string.Equals(city.data.name, value, StringComparison.Ordinal)) return;
            city.setName(value);
            string mainCulture = CultureService.GetMainCulture(city);
            if (CultureService.IsValidCulture(mainCulture))
                CultureService.RecordCityCulturalName(city, mainCulture, value);
            RefreshContent();
        }
    }
}
