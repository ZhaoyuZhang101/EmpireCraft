using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
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

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireCoreWindow : AutoLayoutWindow<EmpireCoreWindow>
    {
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private readonly Dictionary<long, bool> _historyExpandedStates = new Dictionary<long, bool>();
        private TextInput _nameInput;
        private EmpireCore _core;

        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
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
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 60, 3);
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
            var parent = CommonInitial("EmpireCoreWindowTitle");
            parent.AddTextIntoVertLayout($"{LM.Get("empire_core_display_name")}: {EmpireCoreManager.GetDisplayName(_core)}", true, TextAnchor.MiddleCenter, new Vector2(120, 12));
            parent.AddTextIntoVertLayout($"{LM.Get("empire_core_plate_name")}: {EmpireCoreManager.GetPlateName(_core)}", true, TextAnchor.MiddleCenter, new Vector2(120, 12));
            parent.AddTextIntoVertLayout($"{LM.Get("empire_core_created_time")}: {Date.getDate(_core.create_timestamp)}", true, TextAnchor.MiddleCenter, new Vector2(120, 12));

            string foundingEmpire = EmpireCoreManager.GetFoundingEmpireName(_core);
            if (!string.IsNullOrWhiteSpace(foundingEmpire))
            {
                parent.AddTextIntoVertLayout($"{LM.Get("empire_core_founding_empire")}: {foundingEmpire}", true, TextAnchor.MiddleCenter, new Vector2(120, 12));
            }

            parent.AddTextIntoVertLayout($"{LM.Get("empire_core_titles_count")}: {EmpireCoreManager.GetTitles(_core).Count}", true, TextAnchor.MiddleCenter, new Vector2(120, 12));
            parent.AddTextIntoVertLayout($"{LM.Get("empire_core_cities_count")}: {EmpireCoreManager.GetCities(_core).Count}", true, TextAnchor.MiddleCenter, new Vector2(120, 12));

            var empires = EmpireCoreManager.GetEmpires(_core);
            parent.AddTextIntoVertLayout(LM.Get("empire_core_current_empires"), true, TextAnchor.MiddleCenter, new Vector2(80, 12));
            if (empires.Count == 0)
            {
                parent.AddTextIntoVertLayout(LM.Get("empire_core_none"), true, TextAnchor.MiddleCenter, new Vector2(80, 10));
            }
            else
            {
                foreach (var empire in empires)
                {
                    parent.AddButtonIntoVertLayout("open_empire", empire.GetEmpireName(), () =>
                    {
                        EmpireCraftMetaTypeLibrary.selected_empire = empire;
                        ScrollWindow.showWindow(nameof(EmpireWindow));
                    }, size: new Vector2(80, 12));
                }
            }

            parent.AddTextIntoVertLayout(LM.Get("all_titles"), true, TextAnchor.MiddleCenter, new Vector2(80, 12));
            foreach (var title in EmpireCoreManager.GetTitles(_core))
            {
                if (title == null || title.isRekt()) continue;
                parent.AddButtonIntoVertLayout("open_title", title.data.name, () =>
                {
                    EmpireCraftMetaTypeLibrary.selected_kingdomTitle = title;
                    ScrollWindow.showWindow(nameof(KingdomTitleWindow));
                }, size: new Vector2(80, 12));
            }

            parent.AddTextIntoVertLayout(LM.Get("empire_core_history_collection"), true, TextAnchor.MiddleCenter, new Vector2(100, 12));
            if ((_core?.empire_history_ids?.Count ?? 0) == 0)
            {
                parent.AddTextIntoVertLayout(LM.Get("empire_core_none"), true, TextAnchor.MiddleCenter, new Vector2(80, 10));
                return;
            }
            ShowHistoryCards(parent, empires);
        }

        private void ShowHistoryCards(AutoVertLayoutGroup parent, List<Empire> currentEmpires)
        {
            foreach (long empireId in _core.empire_history_ids)
            {
                var histories = GetHistoriesForEmpire(empireId);
                if (histories.Count == 0) continue;

                Empire liveEmpire = currentEmpires.FirstOrDefault(e => e != null && e.id == empireId);
                EmpireCraftHistory firstHistory = histories.FirstOrDefault();
                string empireName = firstHistory?.empire_name ?? LM.Get("empire_core_none");
                int duration = histories.Sum(h => h?.total_time ?? 0);
                bool exists = liveEmpire != null && !liveEmpire.isRekt() && !liveEmpire.IsArchived();
                string stateText = exists ? LM.Get("empire_core_history_exists") : LM.Get("empire_core_history_gone");
                string header = $"{empireName} ({duration}{LM.Get("Year")})";
                string subText = $"{LM.Get("empire_core_history_duration")}: {duration}{LM.Get("Year")} | {stateText}";

                parent.AddButtonIntoVertLayout("empire_core_history_card", header, () =>
                {
                    _historyExpandedStates[empireId] = !_historyExpandedStates.TryGetValue(empireId, out bool expanded) || !expanded;
                    RefreshWindow();
                }, size: new Vector2(120, 14));
                parent.AddTextIntoVertLayout(subText, true, TextAnchor.MiddleCenter, new Vector2(120, 10));

                if (!_historyExpandedStates.TryGetValue(empireId, out bool isExpanded) || !isExpanded)
                {
                    continue;
                }

                parent.AddTextIntoVertLayout(LM.Get("empire_core_history_emperors"), true, TextAnchor.MiddleCenter, new Vector2(100, 10));
                foreach (var history in histories)
                {
                    if (history == null) continue;
                    string emperorLine = BuildEmperorHistoryText(history);
                    parent.AddButtonIntoVertLayout("open_history", emperorLine, () =>
                    {
                        ConfigData.CURRENT_SELECTED_HISTORY = history;
                        EmpireCraftMetaTypeLibrary.selected_empire = liveEmpire ?? currentEmpires.FirstOrDefault();
                        ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
                    }, size: new Vector2(120, 12));
                }
            }
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

            return result.OrderByDescending(h => h?.total_time ?? 0).ToList();
        }

        private string BuildEmperorHistoryText(EmpireCraftHistory history)
        {
            string emperor = history?.emperor ?? "";
            string yearName = history?.year_name ?? "";
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

            string suffix = string.IsNullOrEmpty(titleText) ? "" : $" | {titleText}";
            return $"{emperor} ({history?.total_time ?? 0}{LM.Get("Year")}) | {yearName}{suffix}";
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
