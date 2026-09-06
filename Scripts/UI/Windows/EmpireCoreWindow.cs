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
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireCoreWindow : AutoLayoutWindow<EmpireCoreWindow>
    {
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private readonly Dictionary<long, bool> _historyExpandedStates = new Dictionary<long, bool>();
        private TextInput _nameInput;
        private EmpireCore _core;
        private AutoVertLayoutGroup _topPart;

        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 95, 3);
            _nameInput = Object.Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
            _nameInput.Setup("", ChangeName);
        }

        public void Clear()
        {
            foreach (var container in _groups)
            {
                Destroy(container.Value);
            }
            _groups.Clear();
            if (_topPart != null)
            {
                Destroy(_topPart.gameObject);
                _topPart = null;
            }
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 95, 3);
            _core = EmpireCraftMetaTypeLibrary.selected_empireCore;
            Clear();
            if (_core == null) return;
            InitialTextInput();
            ShowOverview();
        }

        private void InitialTextInput()
        {
            string text = EmpireCoreManager.GetDisplayName(_core);
            this.transform.parent.transform.parent.GenerateTextInput(offset: new Vector2(0, 152), default_text: text, input: _nameInput);
        }

        private AutoVertLayoutGroup CommonInitial(string titleName)
        {
            var container = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            SimpleText title = Object.Instantiate(SimpleText.Prefab, null);
            title.Setup(LM.Get(titleName), TextAnchor.MiddleCenter, new Vector2(60, 15));
            title.background.enabled = false;
            container.AddChild(title.gameObject);
            var content = this.BeginVertGroup(pSpacing: 3);
            container.AddChild(content.gameObject);
            if (!_groups.ContainsKey(titleName)) _groups.Add(titleName, container.gameObject);
            return content;
        }

        private void ShowOverview()
        {
            _topPart = this.BeginVertGroup(pSpacing: 5, pAlignment: TextAnchor.UpperCenter);

            var summary = _topPart.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleCenter);
            summary.AddTextIntoVertLayout(EmpireCoreManager.GetDisplayName(_core).ColorString(pColor: new Color(1f, 0.78f, 0.28f)),
                true, TextAnchor.MiddleCenter, new Vector2(150, 16));
            summary.AddTextIntoVertLayout($"{LM.Get("empire_core_plate_name")}: {EmpireCoreManager.GetPlateName(_core)}",
                true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            string foundingEmpire = EmpireCoreManager.GetFoundingEmpireName(_core);
            string founderText = string.IsNullOrWhiteSpace(foundingEmpire) ? LM.Get("empire_core_none") : foundingEmpire;
            summary.AddTextIntoVertLayout($"{LM.Get("empire_core_founding_empire")}: {founderText}",
                true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            summary.AddTextIntoVertLayout($"{LM.Get("empire_core_created_time")}: {Date.getDate(_core.create_timestamp)}",
                true, TextAnchor.MiddleCenter, new Vector2(150, 10));
            summary.transform.AddStretchBackground("clanFrame", new Vector2(205, 56));

            var empires = EmpireCoreManager.GetEmpires(_core);
            var metrics = _topPart.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleCenter);
            AddMetricCard(metrics, LM.Get("empire_core_titles_count"), EmpireCoreManager.GetTitles(_core).Count.ToString(), new Color(0.45f, 0.82f, 1f));
            AddMetricCard(metrics, LM.Get("empire_core_cities_count"), EmpireCoreManager.GetCities(_core).Count.ToString(), new Color(0.45f, 0.95f, 0.55f));
            AddMetricCard(metrics, LM.Get("empire_core_current_empires"), empires.Count.ToString(), new Color(1f, 0.72f, 0.32f));
            _topPart.gameObject.AdjustTopPart(transform.parent.transform, new Vector2(0, 1));

            var parent = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            _groups["EmpireCoreWindowContent"] = parent.gameObject;
            AddSectionTitle(parent, LM.Get("empire_core_current_empires"));

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
            var titleGrid = parent.BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount,
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

        private void AddMetricCard(AutoHoriLayoutGroup parent, string label, string value, Color color)
        {
            var card = parent.BeginVertGroup(pSize: new Vector2(62, 28), pSpacing: -2, pAlignment: TextAnchor.MiddleCenter);
            card.AddTextIntoVertLayout(value.ColorString(pColor: color), true, TextAnchor.MiddleCenter, new Vector2(30, 12));
            card.AddTextIntoVertLayout(label, true, TextAnchor.MiddleCenter, new Vector2(58, 10));
            card.transform.AddStretchBackground("FactionFrame", new Vector2(62, 28));
        }

        private void AddSectionTitle(AutoVertLayoutGroup parent, string title)
        {
            parent.AddTextIntoVertLayout(title.ColorString(pColor: new Color(0.7f, 0.9f, 1f)), true, TextAnchor.MiddleCenter,
                new Vector2(130, 13));
        }

        private void AddEmpireCard(AutoVertLayoutGroup parent, Empire empire)
        {
            var card = parent.BeginHoriGroup(pSize: new Vector2(200, 42), pSpacing: 2, pAlignment: TextAnchor.MiddleCenter);
            var details = card.BeginVertGroup(pSize: new Vector2(175, 38), pSpacing: 1, pAlignment: TextAnchor.MiddleLeft);
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout(empire.GetEmpireFullName().ColorString(empire.getColor().color_text), true,
                TextAnchor.MiddleLeft, new Vector2(170, 12)));
            HoverMarqueeText.Attach(details.AddTextIntoVertLayout($"{LM.Get("empire_core_royal_surname")}: {empire.EmpireSpecificClan?.name ?? LM.Get("empire_core_none")}",
                true, TextAnchor.MiddleLeft, new Vector2(170, 10)));
            details.AddTextIntoVertLayout($"{LM.Get("i_population")}: {empire.CountPopulation()}  |  {LM.Get("label_mandate")}: {empire.Mandate}",
                true, TextAnchor.MiddleLeft, new Vector2(140, 10));
            card.AddButtonIntoHoriLayout("open_empire", "", () =>
            {
                EmpireCraftMetaTypeLibrary.selected_empire = empire;
                ScrollWindow.showWindow(nameof(EmpireWindow));
            }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(16, 16), showTip: true);
            card.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(200, 42));
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
            foreach (long empireId in _core.empire_history_ids)
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
                var historyCard = parent.BeginHoriGroup(pSize: new Vector2(200, 42), pSpacing: 2,
                    pAlignment: TextAnchor.MiddleCenter);
                var historyDetails = historyCard.BeginVertGroup(pSize: new Vector2(175, 38), pSpacing: 1,
                    pAlignment: TextAnchor.MiddleLeft);
                Color empireColor = liveEmpire?.getColor()._color_main ?? new Color(0.68f, 0.88f, 1f);
                HoverMarqueeText.Attach(historyDetails.AddTextIntoVertLayout(empireName.ColorString(pColor: empireColor), true,
                    TextAnchor.MiddleLeft, new Vector2(170, 12)));
                HoverMarqueeText.Attach(historyDetails.AddTextIntoVertLayout($"{LM.Get("empire_core_royal_surname")}: {royalSurnames}",
                    true, TextAnchor.MiddleLeft, new Vector2(170, 10)));
                historyDetails.AddTextIntoVertLayout($"{LM.Get("empire_core_history_duration")}: {duration}{LM.Get("Year")}  |  {stateText}", true,
                    TextAnchor.MiddleLeft, new Vector2(170, 10));
                historyCard.AddButtonIntoHoriLayout("empire_core_history_card", "", () =>
                {
                    _historyExpandedStates[empireId] = !_historyExpandedStates.TryGetValue(empireId, out bool expanded) || !expanded;
                    RefreshWindow();
                }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(16, 16), showTip: true);
                historyCard.transform.AddStretchBackground(exists ? "FactionFrame_dominate" : "FactionFrame", new Vector2(200, 42));

                if (!_historyExpandedStates.TryGetValue(empireId, out bool isExpanded) || !isExpanded)
                {
                    continue;
                }

                parent.AddTextIntoVertLayout(LM.Get("empire_core_history_emperors"), true, TextAnchor.MiddleCenter, new Vector2(100, 10));
                foreach (var history in histories)
                {
                    if (history == null) continue;
                    var emperorCard = parent.BeginHoriGroup(pSize: new Vector2(200, 26), pSpacing: 2,
                        pAlignment: TextAnchor.MiddleCenter);
                    var emperorDetails = emperorCard.BeginVertGroup(pSize: new Vector2(175, 24), pSpacing: -2,
                        pAlignment: TextAnchor.MiddleLeft);
                    string titleText = BuildEmperorTitleText(history);
                    string displayName = string.IsNullOrEmpty(titleText)
                        ? history.emperor
                        : $"{history.emperor}  |  {titleText}";
                    emperorDetails.AddTextIntoVertLayout(displayName.ColorString(pColor: new Color(0.82f, 0.9f, 1f)), true,
                        TextAnchor.MiddleLeft, new Vector2(170, 11));
                    string reignText = $"{history.year_name}  ·  {GetReignDuration(history, liveEmpire)}{LM.Get("Year")}";
                    emperorDetails.AddTextIntoVertLayout(reignText, true, TextAnchor.MiddleLeft, new Vector2(170, 9));
                    emperorCard.AddButtonIntoHoriLayout("open_history", "", () =>
                    {
                        ConfigData.CURRENT_SELECTED_HISTORY = history;
                        EmpireCraftMetaTypeLibrary.selected_empire = liveEmpire ?? currentEmpires.FirstOrDefault();
                        ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
                    }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(14, 14), showTip: true);
                    emperorCard.transform.AddStretchBackground("clanFrame", new Vector2(200, 26));
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
            Empire activeEmpire = ModClass.EMPIRE_MANAGER.get(empireId);
            if (activeEmpire != null && !activeEmpire.isRekt() && activeEmpire.data?.history != null)
            {
                result.AddRange(activeEmpire.data.history.Where(h => h != null));
            }

            if (ModClass.ALL_HISTORY_DATA.TryGetValue(empireId, out var archivedHistories) && archivedHistories != null)
            {
                foreach (var history in archivedHistories)
                {
                    if (history == null) continue;
                    if (result.All(h => h.id != history.id))
                    {
                        result.Add(history);
                    }
                }
            }

            if (activeEmpire != null && !activeEmpire.isRekt() && activeEmpire.data?.currentHistory != null &&
                result.All(h => h.id != activeEmpire.data.currentHistory.id))
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
