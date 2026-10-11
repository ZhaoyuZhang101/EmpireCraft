using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.services;
using NeoModLoader.General.UI.Window;
using NeoModLoader.api;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 宽窗口(480×380)：法理头衔网格与历代帝国名录窄窗里挤不下
    public class EmpireCoreWindow : AbstractWideWindow<EmpireCoreWindow>
    {
        private AutoVertLayoutGroup _root;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private readonly Dictionary<long, bool> _historyExpandedStates = new Dictionary<long, bool>();
        private readonly Dictionary<long, int> _historyPages = new();
        private const int HistoryPageSize = 12;
        private TextInput _nameInput;
        private EmpireCore _core;
        private AutoVertLayoutGroup _topPart;

        protected override void Init()
        {
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
            // 宽窗口脚本挂在窗口根上：输入框挂窗口根，随窗口显示/隐藏(同 EmpireWindow 的修复)
            _nameInput = Object.Instantiate(TextInput.Prefab, this.transform);
            _nameInput.Setup("", ChangeName);
        }

        public void Clear()
        {
            foreach (var container in _groups)
            {
                if (container.Value == null) continue;
                container.Value.SetActive(false);
                Destroy(container.Value);
            }
            _groups.Clear();
            if (_topPart != null)
            {
                _topPart.gameObject.SetActive(false);
                Destroy(_topPart.gameObject);
                _topPart = null;
            }
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
            _core = EmpireCraftMetaTypeLibrary.selected_empireCore;
            Clear();
            if (_core == null) return;
            InitialTextInput();
            ShowOverview();
        }

        private void InitialTextInput()
        {
            string text = EmpireCoreManager.GetDisplayName(_core);
            // 宽窗口(380 高)：改名输入框放在标题栏上方
            this.transform.GenerateTextInput(offset: new Vector2(0, 218), default_text: text, input: _nameInput);
        }

        private AutoVertLayoutGroup CommonInitial(string titleName)
        {
            var container = _root.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            SimpleText title = Object.Instantiate(SimpleText.Prefab, null);
            title.Setup(LM.Get(titleName), TextAnchor.MiddleCenter, new Vector2(60, 15));
            title.background.enabled = false;
            container.AddChild(title.gameObject);
            var content = _root.BeginVertGroup(pSpacing: 3);
            container.AddChild(content.gameObject);
            if (!_groups.ContainsKey(titleName)) _groups.Add(titleName, container.gameObject);
            return content;
        }

        private void ShowOverview()
        {
            _topPart = _root.BeginVertGroup(pSpacing: 5, pAlignment: TextAnchor.UpperCenter);

            var summary = _topPart.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleCenter);
            summary.AddTextIntoVertLayout(EmpireCoreManager.GetStatusDisplayName(_core).ColorString(pColor: new Color(1f, 0.78f, 0.28f)),
                true, TextAnchor.MiddleCenter, new Vector2(150, 16));
            summary.AddTextIntoVertLayout($"{LM.Get("empire_core_plate_name")}: {EmpireCoreManager.GetPlateName(_core)}",
                true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            string foundingEmpire = EmpireCoreManager.GetFoundingEmpireName(_core);
            string founderText = string.IsNullOrWhiteSpace(foundingEmpire) ? LM.Get("empire_core_none") : foundingEmpire;
            summary.AddTextIntoVertLayout($"{LM.Get("empire_core_founding_empire")}: {founderText}",
                true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            summary.AddTextIntoVertLayout($"{LM.Get("empire_core_created_time")}: {Date.getDate(_core.create_timestamp)}",
                true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            Empire legitimate = EmpireCoreManager.GetLegitimateEmpire(_core);
            if (legitimate != null)
                summary.AddTextIntoVertLayout($"{LM.Get("empire_core_legitimate_empire")}: {legitimate.GetEmpireFullName()}",
                    true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            summary.transform.AddStretchBackground("clanFrame", new Vector2(205, legitimate == null ? 56 : 68));

            var empires = EmpireCoreManager.GetEmpires(_core);
            var metrics = _topPart.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleCenter);
            AddMetricCard(metrics, LM.Get("empire_core_titles_count"), EmpireCoreManager.GetTitles(_core).Count.ToString(), new Color(0.45f, 0.82f, 1f));
            AddMetricCard(metrics, LM.Get("empire_core_cities_count"), EmpireCoreManager.GetCities(_core).Count.ToString(), new Color(0.45f, 0.95f, 0.55f));
            AddMetricCard(metrics, LM.Get("empire_core_current_empires"), empires.Count.ToString(), new Color(1f, 0.72f, 0.32f));

            var parent = _root.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            _groups["EmpireCoreWindowContent"] = parent.gameObject;
            AddSectionTitle(parent, LM.Get("empire_core_current_empires"));

            AddEmpireCultureEditor(parent);

            if (empires.Count == 0)
            {
                parent.AddTextIntoVertLayout(LM.Get("empire_core_none"), true, TextAnchor.MiddleCenter, new Vector2(80, 10));
            }
            else
            {
                foreach (var empire in empires)
                {
                    AddEmpireCard(parent, empire);
                }
            }

            AddSectionTitle(parent, LM.Get("all_titles"));
            var titleGrid = parent.BeginGridGroup(8, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(48, 34), pSpacing: new Vector2(2, 2));
            foreach (var title in EmpireCoreManager.GetTitles(_core))
            {
                if (title == null || title.isRekt()) continue;
                AddTitleCard(titleGrid, title);
            }

            AddSectionTitle(parent, LM.Get("empire_core_history_collection"));
            if ((_core?.empire_history_ids?.Count ?? 0) == 0)
            {
                parent.AddTextIntoVertLayout(LM.Get("empire_core_none"), true, TextAnchor.MiddleCenter, new Vector2(80, 10));
                return;
            }
            ShowHistoryCards(parent, empires);
        }

        private void AddEmpireCultureEditor(AutoVertLayoutGroup parent)
        {
            string culture = _core.default_culture;
            if (!CultureService.IsValidCulture(culture))
            {
                culture = CultureService.GetRealmCulture(EmpireCoreManager.GetEmpires(_core).FirstOrDefault()?.CoreKingdom ??
                    _core.GetCoreCapital()?.kingdom);
                _core.default_culture = culture;
            }
            var card = parent.BeginVertGroup(pSize: new Vector2(UIHelper.WidePanelWidth, 48), pSpacing: 1,
                pAlignment: TextAnchor.MiddleCenter);
            string display = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : LM.Get("label_none");
            card.AddTextIntoVertLayout($"{LM.Get("kingdom_title_effective_culture")}: {display}", true,
                TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth - 10f, 11));
            TextInput input = Object.Instantiate(TextInput.Prefab, null);
            input.Setup(culture ?? "", value => ChangeCulture(value, input));
            input.SetSize(new Vector2(UIHelper.WidePanelWidth - 10f, 18));
            card.AddChild(input.gameObject);
            string lockText = _core.culture_locked
                ? LM.Get("kingdom_title_culture_locked")
                : LM.Get("kingdom_title_culture_unlocked");
            card.AddButtonIntoVertLayout("toggle_empire_core_culture_lock", lockText, () =>
            {
                // 之前这里只改了 _core.culture_locked 这一个字段，帝国锁定文化后
                // 下属的王国法理完全不会跟着锁定。现在改用 CultureService 的入口，
                // 让锁定状态正确级联到帝国旗下所有王国法理。
                CultureService.SetEmpireCultureLock(_core, !_core.culture_locked);
                RefreshWindow();
            }, SpriteTextureLoader.getSprite("ui/icons/iconArrowUP"), size: new Vector2(UIHelper.WidePanelWidth - 10f, 14), showTip: false);
            card.transform.AddStretchBackground("FactionFrame", new Vector2(UIHelper.WidePanelWidth, 48));
        }

        private void ChangeCulture(string value, TextInput input)
        {
            if (!CultureService.SetEmpireDefaultCulture(_core, value?.Trim(), _core.culture_locked))
            {
                ActionLibrary.showWhisperTip("kingdom_title_culture_invalid");
                input.input.text = _core.default_culture ?? "";
                return;
            }
            RefreshWindow();
        }

        private void AddMetricCard(AutoHoriLayoutGroup parent, string label, string value, Color color)
        {
            var card = parent.BeginVertGroup(pSize: new Vector2(140, 28), pSpacing: -2, pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout(value.ColorString(pColor: color), true, TextAnchor.MiddleCenter, new Vector2(130, 12));
            card.AddTextIntoVertLayout(label, true, TextAnchor.MiddleCenter, new Vector2(130, 10));
            card.transform.AddStretchBackground("FactionFrame", new Vector2(140, 28));
        }

        private void AddSectionTitle(AutoVertLayoutGroup parent, string title)
        {
            parent.AddTextIntoVertLayout(title.ColorString(pColor: new Color(0.7f, 0.9f, 1f)), true, TextAnchor.MiddleCenter,
                new Vector2(130, 13));
        }

        private void AddEmpireCard(AutoVertLayoutGroup parent, Empire empire)
        {
            bool composite = CompositeEmpireService.IsComposite(empire);
            float cardHeight = composite ? 72f : 42f;
            var card = parent.BeginHoriGroup(pSize: new Vector2(UIHelper.WidePanelWidth, cardHeight), pSpacing: 2, pAlignment: TextAnchor.MiddleCenter);
            var details = card.BeginVertGroup(pSize: new Vector2(UIHelper.WidePanelWidth - 25f, cardHeight - 4f), pSpacing: 1, pAlignment: TextAnchor.MiddleLeft);
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(empire.GetEmpireFullName().ColorString(empire.getColor().color_text), true,
                TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 12)));
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout($"{LM.Get("empire_core_royal_surname")}: {empire.EmpireSpecificClan?.name ?? LM.Get("empire_core_none")}",
                true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 10)));
            details.AddTextIntoVertLayout($"{LM.Get("i_population")}: {empire.CountPopulation()}  |  {LM.Get(empire.LegitimacyLabelKey)}: {empire.Legitimacy}",
                true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 60f, 10));
            if (composite)
            {
                string ruling = CompositeEmpireService.GetRulingCulture(empire);
                string institution = CompositeEmpireService.GetInstitutionalCulture(empire);
                ruling = CultureService.IsValidCulture(ruling) ? ruling.GetCultureTranslate() : ruling;
                institution = CultureService.IsValidCulture(institution) ? institution.GetCultureTranslate() : institution;
                details.AddTextIntoVertLayout($"{LM.Get("composite_empire_ruling_culture")}: {ruling}  |  {LM.Get("composite_empire_institutional_culture")}: {institution}",
                    true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 10));
                details.AddTextIntoVertLayout($"{LM.Get("composite_empire_military_tradition")}: {CompositeEmpireService.GetMilitaryTraditionName(empire)}  |  {CompositeEmpireService.GetStageName(empire)} {empire.data.composite_integration}%",
                    true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 10));
                details.AddTextIntoVertLayout($"{LM.Get("composite_empire_dual_legitimacy")}: {empire.data.central_plains_legitimacy}/{empire.data.ruling_tradition_legitimacy}",
                    true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 10));
            }
            card.AddButtonIntoHoriLayout("open_empire", "", () =>
            {
                EmpireCraftMetaTypeLibrary.selected_empire = empire;
                ScrollWindow.showWindow(nameof(EmpireWindow));
            }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(16, 16), showTip: true);
            card.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(UIHelper.WidePanelWidth, cardHeight));
        }

        private void AddTitleCard(AutoGridLayoutGroup parent, KingdomTitle title)
        {
            var card = parent.BeginVertGroup(pSize: new Vector2(48, 34), pSpacing: 2, pAlignment: TextAnchor.MiddleCenter);
            HoverMarqueeText.Attach(card.AddTextIntoVertLayout(title.data.name.ColorString(pColor: new Color(0.82f, 0.9f, 1f)), true,
                TextAnchor.MiddleCenter, new Vector2(44, 12)));
            card.AddButtonIntoVertLayout("open_title", "", () =>
            {
                EmpireCraftMetaTypeLibrary.selected_kingdomTitle = title;
                ScrollWindow.showWindow(nameof(KingdomTitleWindow));
            }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(14, 14), showTip: true);
            card.transform.AddStretchBackground("FactionFrame", new Vector2(48, 34));
        }

        private void ShowHistoryCards(AutoVertLayoutGroup parent, List<Empire> currentEmpires)
        {
            foreach (long empireId in _core.empire_history_ids.Distinct())
            {
                var histories = GetHistoriesForEmpire(empireId);
                if (histories.Count == 0) continue;

                Empire liveEmpire = currentEmpires.FirstOrDefault(e => e != null && e.id == empireId);
                Empire storedEmpire = ModClass.EMPIRE_MANAGER.get(empireId);
                EmpireCraftHistory lastHistory = histories.LastOrDefault();
                string empireName = liveEmpire?.GetEmpireFullName() ?? EmpireHistoryDisplay.FullName(
                    lastHistory?.empire_full_name, lastHistory?.empire_name, storedEmpire?.data?.name,
                    string.IsNullOrWhiteSpace(storedEmpire?.data?.name) ? null : storedEmpire.GetEmpireName(), LM.Get("empire_core_none"));
                string royalSurnames = string.Join(" / ", histories.Select(h => h.royal_surname)
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                if (string.IsNullOrWhiteSpace(royalSurnames))
                    royalSurnames = storedEmpire?.EmpireSpecificClan?.name ?? LM.Get("empire_core_none");
                int duration = histories.Sum(h => GetReignDuration(h, liveEmpire));
                bool exists = liveEmpire != null && !liveEmpire.isRekt() && !liveEmpire.IsArchived();
                string stateText = exists ? LM.Get("empire_core_history_exists") : LM.Get("empire_core_history_gone");
                var historyCard = parent.BeginHoriGroup(pSize: new Vector2(UIHelper.WidePanelWidth, 42), pSpacing: 2,
                    pAlignment: TextAnchor.MiddleCenter);
                var historyDetails = historyCard.BeginVertGroup(pSize: new Vector2(UIHelper.WidePanelWidth - 25f, 38), pSpacing: 1,
                    pAlignment: TextAnchor.MiddleLeft);
                Color empireColor = liveEmpire?.getColor()._color_main ?? new Color(0.68f, 0.88f, 1f);
                HoverMarqueeText.Attach(historyDetails.AddTextIntoVertLayout(empireName.ColorString(pColor: empireColor), true,
                    TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 12)));
                HoverMarqueeText.Attach(historyDetails.AddTextIntoVertLayout($"{LM.Get("empire_core_royal_surname")}: {royalSurnames}",
                    true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 10)));
                historyDetails.AddTextIntoVertLayout($"{LM.Get("empire_core_history_duration")}: {duration}{LM.Get("Year")}  |  {stateText}", true,
                    TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 10));
                historyCard.AddButtonIntoHoriLayout("empire_core_history_card", "", () =>
                {
                    _historyExpandedStates[empireId] = !_historyExpandedStates.TryGetValue(empireId, out bool expanded) || !expanded;
                    RefreshWindow();
                }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(16, 16), showTip: true);
                historyCard.transform.AddStretchBackground(exists ? "FactionFrame_dominate" : "FactionFrame", new Vector2(UIHelper.WidePanelWidth, 42));

                if (!_historyExpandedStates.TryGetValue(empireId, out bool isExpanded) || !isExpanded)
                {
                    continue;
                }

                parent.AddTextIntoVertLayout(LM.Get("empire_core_history_emperors"), true, TextAnchor.MiddleCenter, new Vector2(100, 10));
                int page = _historyPages.TryGetValue(empireId, out int savedPage) ? savedPage : 0;
                int pages = global::System.Math.Max(1, (histories.Count + HistoryPageSize - 1) / HistoryPageSize);
                page = global::System.Math.Max(0, global::System.Math.Min(page, pages - 1));
                if (pages > 1)
                {
                    int currentPage = page;
                    var navigation = parent.BeginHoriGroup(new Vector2(UIHelper.WidePanelWidth, 18f),
                        TextAnchor.MiddleCenter, 8f);
                    navigation.AddButtonIntoHoriLayout("lineage_previous", LM.Get("lineage_previous"), () =>
                    {
                        if (currentPage == 0) return;
                        _historyPages[empireId] = currentPage - 1; RefreshWindow();
                    }, size: new Vector2(62f, 16f));
                    navigation.AddTextIntoHoriLayout(string.Format(LM.Get("lineage_page"), page + 1, pages, histories.Count),
                        true, TextAnchor.MiddleCenter, new Vector2(230f, 16f));
                    navigation.AddButtonIntoHoriLayout("lineage_next", LM.Get("lineage_next"), () =>
                    {
                        if (currentPage + 1 >= pages) return;
                        _historyPages[empireId] = currentPage + 1; RefreshWindow();
                    }, size: new Vector2(62f, 16f));
                }
                foreach (var history in histories.Skip(page * HistoryPageSize).Take(HistoryPageSize))
                {
                    if (history == null) continue;
                    var emperorCard = parent.BeginHoriGroup(pSize: new Vector2(UIHelper.WidePanelWidth, 26), pSpacing: 2,
                        pAlignment: TextAnchor.MiddleCenter);
                    var emperorDetails = emperorCard.BeginVertGroup(pSize: new Vector2(UIHelper.WidePanelWidth - 25f, 24), pSpacing: -2,
                        pAlignment: TextAnchor.MiddleLeft);
                    string titleText = BuildEmperorTitleText(history);
                    string displayName = string.IsNullOrEmpty(titleText)
                        ? history.emperor
                        : $"{history.emperor}  |  {titleText}";
                    emperorDetails.AddTextIntoVertLayout(displayName.ColorString(pColor: new Color(0.82f, 0.9f, 1f)), true,
                        TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 11));
                    string reignText = $"{history.year_name}  ·  {GetReignDuration(history, liveEmpire)}{LM.Get("Year")}";
                    emperorDetails.AddTextIntoVertLayout(reignText, true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 32f, 9));
                    emperorCard.AddButtonIntoHoriLayout("open_history", "", () =>
                    {
                        ConfigData.CURRENT_SELECTED_HISTORY = history;
                        EmpireCraftMetaTypeLibrary.selected_empire = liveEmpire ?? currentEmpires.FirstOrDefault();
                        ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
                    }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(14, 14), showTip: true);
                    emperorCard.transform.AddStretchBackground("clanFrame", new Vector2(UIHelper.WidePanelWidth, 26));
                }
            }
        }

        private static int GetReignDuration(EmpireCraftHistory history, Empire liveEmpire)
        {
            return history != null && liveEmpire?.data?.currentHistory == history
                ? Date.getYearsSince(liveEmpire.data.newEmperor_timestamp) : history?.total_time ?? 0;
        }

        private List<EmpireCraftHistory> GetHistoriesForEmpire(long empireId)
        {
            var result = new List<EmpireCraftHistory>();
            var seen = new HashSet<long>();
            Empire activeEmpire = ModClass.EMPIRE_MANAGER.get(empireId);
            if (activeEmpire != null && !activeEmpire.isRekt() && activeEmpire.data?.history != null)
            {
                foreach (var history in activeEmpire.data.history)
                    if (history != null)
                    {
                        result.Add(history);
                        seen.Add(history.id);
                    }
            }

            if (ModClass.ALL_HISTORY_DATA.TryGetValue(empireId, out var archivedHistories) && archivedHistories != null)
            {
                foreach (var history in archivedHistories)
                {
                    if (history == null) continue;
                    if (seen.Add(history.id))
                    {
                        result.Add(history);
                    }
                }
            }

            if (activeEmpire != null && !activeEmpire.isRekt() && activeEmpire.data?.currentHistory != null &&
                seen.Add(activeEmpire.data.currentHistory.id))
                result.Add(activeEmpire.data.currentHistory);
            return result;
        }

        private string BuildEmperorTitleText(EmpireCraftHistory history)
        {
            string titleText = "";

            if (!string.IsNullOrEmpty(history?.miaohao_name))
            {
                titleText += $"{history.empire_name}{LM.Get(history.miaohao_name)}{LM.Get(history.miaohao_suffix)}";
            }

            if (!string.IsNullOrEmpty(history?.shihao_name))
            {
                if (!string.IsNullOrEmpty(titleText))
                {
                    titleText += " / ";
                }
                titleText += $"{history.empire_name}{LM.Get(history.shihao_name)}{LM.Get("emperor_suffix")}";
            }

            return titleText;
        }

        private void RefreshWindow()
        {
            Clear();
            if (_core == null) return;
            InitialTextInput();
            ShowOverview();
        }

        private void ChangeName(string value)
        {
            if (_core == null) return;
            _core.name = value;
        }
    }
}
