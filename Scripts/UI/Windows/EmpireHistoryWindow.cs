using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine.UI;
using NeoModLoader.api;
namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireHistoryWindow : AbstractWideWindow<EmpireHistoryWindow>
    {
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private AutoVertLayoutGroup _root;
        private ScrollWindow ScrollWindowComponent => GetComponent<ScrollWindow>();
        private const int PageSize = 12;
        private const int EventPageSize = 30;
        private readonly List<EmpireCraftHistory> _histories = new();
        private readonly Dictionary<long, PersonalClanIdentity> _actorIdentities = new();
        private readonly Dictionary<string, PersonalClanIdentity> _legacyIdentities = new();
        private readonly Dictionary<EmpireCraftHistory, int> _eventPages = new();
        private int _page;
        private int _generation;
        private Empire _empire;
        private EmpireCraftHistory _expandedHistory;
        protected override void Init()
        {
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
        }
        public void Clear()
        {
            _generation++;
            foreach (var container in _groups)
            {
                if (container.Value == null) continue;
                container.Value.SetActive(false);
                GameObject.Destroy(container.Value);
            }
            _groups.Clear();
        }
        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            _histories.Clear();
            _histories.AddRange(GetDisplayHistories());
            _eventPages.Clear();
            _expandedHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            int selected = _histories.IndexOf(_expandedHistory);
            _page = selected < 0 ? 0 : selected / PageSize;
            BuildIdentityIndex();
            ShowPersonalHistory();
        }
        public void ShowPersonalHistory(Vector3? restorePosition = null)
        {
            Clear();
            var parent = _root.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            UIHelper.UseWidePanel(parent.transform);
            _groups["empire_history_records"] = parent.gameObject;
            int pages = Mathf.Max(1, (_histories.Count + PageSize - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var navigation = parent.BeginHoriGroup(new Vector2(UIHelper.WidePanelWidth, 18f), TextAnchor.MiddleCenter, 8f);
            navigation.AddButtonIntoHoriLayout("lineage_previous", LM.Get("lineage_previous"), () =>
            {
                if (_page > 0) { _page--; _expandedHistory = null; ShowPersonalHistory(); }
            }, size: new Vector2(62f, 16f));
            navigation.AddTextIntoHoriLayout(string.Format(LM.Get("lineage_page"), _page + 1, pages, _histories.Count),
                true, TextAnchor.MiddleCenter, new Vector2(230f, 16f));
            navigation.AddButtonIntoHoriLayout("lineage_next", LM.Get("lineage_next"), () =>
            {
                if (_page + 1 < pages) { _page++; _expandedHistory = null; ShowPersonalHistory(); }
            }, size: new Vector2(62f, 16f));
            StartCoroutine(RenderPage(parent, _generation, restorePosition));
        }

        private IEnumerator RenderPage(AutoVertLayoutGroup parent, int generation, Vector3? restorePosition)
        {
            int end = Mathf.Min(_histories.Count, (_page + 1) * PageSize);
            for (int i = _page * PageSize; i < end; i++)
            {
                if (generation != _generation || parent == null || !gameObject.activeInHierarchy) yield break;
                AddEmperorHistoryCard(parent, _histories[i]);
                yield return CoroutineHelper.wait_for_next_frame;
            }
            if (generation != _generation || parent == null || !gameObject.activeInHierarchy) yield break;
            ScrollWindow window = ScrollWindowComponent;
            if (window?.transform_content == null) yield break;
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.transform_content);
            if (restorePosition.HasValue) window.transform_content.localPosition = restorePosition.Value;
            else if (window.scrollRect != null) window.scrollRect.verticalNormalizedPosition = 1f;
            window.scrollRect?.StopMovement();
        }

        // 只收集这些君主所需的身份，遍历一次人口库，不为每张头像反复遍历全库。
        private void BuildIdentityIndex()
        {
            _actorIdentities.Clear();
            _legacyIdentities.Clear();
            var actors = new HashSet<long>(_histories.Where(h => h.id > 0).Select(h => h.id));
            var names = new HashSet<string>(_histories.Where(h => !string.IsNullOrWhiteSpace(h.emperor)).Select(h => h.emperor));
            foreach (PersonalClanIdentity identity in SpecificClanManager._globalPersonLookup.Values)
            {
                if (identity == null) continue;
                if (actors.Contains(identity.actor_id) && !_actorIdentities.ContainsKey(identity.actor_id))
                    _actorIdentities[identity.actor_id] = identity;
                if (identity.is_alive || identity.actor_id > 0 || identity.name == null || !names.Contains(identity.name)) continue;
                // 多个同名旧身份保持歧义，不能随意挑一个头像。
                if (_legacyIdentities.ContainsKey(identity.name)) _legacyIdentities[identity.name] = null;
                else _legacyIdentities[identity.name] = identity;
            }
        }

        public override void OnNormalDisable()
        {
            base.OnNormalDisable();
            Clear();
            _histories.Clear();
            _actorIdentities.Clear();
            _legacyIdentities.Clear();
            _eventPages.Clear();
        }

        private List<EmpireCraftHistory> GetDisplayHistories()
        {
            var result = new List<EmpireCraftHistory>();
            var seen = new HashSet<EmpireCraftHistory>();
            EmpireCraftHistory currentHistory = _empire?.data?.currentHistory;
            if (currentHistory != null)
            {
                result.Add(currentHistory);
                seen.Add(currentHistory);
            }
            if (_empire?.data?.history != null)
            {
                for (int i = _empire.data.history.Count - 1; i >= 0; i--)
                {
                    EmpireCraftHistory history = _empire.data.history[i];
                    if (history != null && seen.Add(history)) result.Add(history);
                }
            }
            EmpireCraftHistory selectedHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (selectedHistory != null && seen.Add(selectedHistory)) result.Add(selectedHistory);
            return result;
        }

        private void AddEmperorHistoryCard(AutoVertLayoutGroup parent, EmpireCraftHistory history)
        {
            bool expanded = object.ReferenceEquals(_expandedHistory, history);
            var card = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(UIHelper.WidePanelWidth, 34));
            Actor actor = history.id > 0 ? World.world.units.get(history.id) : null;
            PersonalClanIdentity identity = FindHistoryIdentity(history.id, history.emperor);
            bool isAlive = identity?.is_alive ?? (actor != null && actor.isAlive());
            var avatarLayout = card.BeginVertGroup(new Vector2(28, 28), pSpacing: 0,
                pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
            var avatar = UIHelper.CreateAvatarView(history.id, actor == null ? null : () => UIHelper.actorClick(actor),
                pIsAlive: isAlive, pIdentity: identity);
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(28, 28);
            avatarLayout.AddChild(avatar.gameObject);
            avatarLayout.transform.localPosition = Vector3.zero;

            var details = card.BeginVertGroup(new Vector2(UIHelper.WidePanelWidth - 52f, 30), pSpacing: -2, pAlignment: TextAnchor.MiddleLeft,
                pPadding: new RectOffset(0, 0, 1, 1));
            Color empireColor = _empire?.getColor()._color_text ?? new Color(0.55f, 0.85f, 1f);
            string emperorName = string.IsNullOrWhiteSpace(history.emperor) ? LM.Get("waiting_for_naming") : history.emperor;
            var nameText = details.AddTextIntoVertLayout(emperorName.ColorString(pColor: empireColor), true,
                TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 54f, 15));
            nameText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            string eraName = string.IsNullOrWhiteSpace(history.year_name) ? LM.Get("waiting_for_naming") : history.year_name;
            int reignYears = object.ReferenceEquals(_empire?.data?.currentHistory, history)
                ? _empire.GetEmperorYear()
                : Mathf.Max(1, history.total_time);
            var reignText = details.AddTextIntoVertLayout(
                $"{LM.Get("year_name")}: {eraName.ColorString(pColor: new Color(1f, 0.78f, 0.2f))}  ·  {reignYears}{LM.Get("Year")}".ColorString(pColor: new Color(0.25f, 0.9f, 0.8f)),
                true, TextAnchor.MiddleLeft, new Vector2(UIHelper.WidePanelWidth - 54f, 12));
            reignText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            details.transform.localPosition = Vector3.zero;
            var stateText = card.AddTextIntoHoriLayout((expanded ? "−" : "+").ColorString(pColor: expanded
                    ? new Color(1f, 0.78f, 0.2f)
                    : new Color(0.65f, 0.82f, 1f)),
                true, TextAnchor.MiddleCenter, new Vector2(14, 20));
            stateText.UseFixedFontSize(10, HorizontalWrapMode.Overflow);
            card.transform.AddStretchBackground(expanded ? "FactionFrame_dominate" : "clanFrame", new Vector2(UIHelper.WidePanelWidth, 34));
            AddHistoryCardClickLayer(card, history);

            if (!expanded) return;
            AddExpandedHistoryDetails(parent, history);
        }

        private PersonalClanIdentity FindHistoryIdentity(long actorId, string actorName = null)
        {
            if (actorId > 0 && _actorIdentities.TryGetValue(actorId, out var identity)) return identity;
            if (!string.IsNullOrWhiteSpace(actorName) && _legacyIdentities.TryGetValue(actorName, out identity)) return identity;
            return null;
        }

        private void AddHistoryCardClickLayer(AutoHoriLayoutGroup card, EmpireCraftHistory history)
        {
            var overlay = new GameObject("EmpireHistoryCardClick", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            overlay.transform.SetParent(card.transform, false);
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = Color.clear;
            overlayImage.raycastTarget = true;
            var overlayLayout = overlay.GetComponent<LayoutElement>();
            overlayLayout.ignoreLayout = true;
            var overlayButton = overlay.GetComponent<Button>();
            overlayButton.targetGraphic = overlayImage;
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(() =>
            {
                ToggleExpandedHistory(history);
            });
            overlay.transform.SetAsLastSibling();
        }

        private void ToggleExpandedHistory(EmpireCraftHistory history)
        {
            RectTransform content = ScrollWindowComponent?.transform_content;
            Vector3 previousScrollPosition = content == null ? Vector3.zero : content.localPosition;
            _expandedHistory = object.ReferenceEquals(_expandedHistory, history) ? null : history;
            ShowPersonalHistory(content == null ? (Vector3?)null : previousScrollPosition);
        }

        private void AddExpandedHistoryDetails(AutoVertLayoutGroup parent, EmpireCraftHistory history)
        {
            var events = history.descriptions?.Where(d => d != null).ToList();
            if (events == null || events.Count == 0) return;
            var detailSpace = parent.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            UIHelper.UseWidePanel(detailSpace.transform);
            int pages = Mathf.Max(1, (events.Count + EventPageSize - 1) / EventPageSize);
            int page = _eventPages.TryGetValue(history, out int saved) ? Mathf.Clamp(saved, 0, pages - 1) : 0;
            if (pages > 1)
            {
                var navigation = detailSpace.BeginHoriGroup(new Vector2(UIHelper.WidePanelWidth, 18f), TextAnchor.MiddleCenter, 8f);
                navigation.AddButtonIntoHoriLayout("lineage_previous", LM.Get("lineage_previous"), () =>
                {
                    if (page == 0) return;
                    _eventPages[history] = page - 1; ShowPersonalHistory();
                }, size: new Vector2(62f, 16f));
                navigation.AddTextIntoHoriLayout(string.Format(LM.Get("history_event_page"), page + 1, pages, events.Count),
                    true, TextAnchor.MiddleCenter, new Vector2(230f, 16f));
                navigation.AddButtonIntoHoriLayout("lineage_next", LM.Get("lineage_next"), () =>
                {
                    if (page + 1 >= pages) return;
                    _eventPages[history] = page + 1; ShowPersonalHistory();
                }, size: new Vector2(62f, 16f));
            }
            int start = page * EventPageSize;
            HistoryDescription lastDesc = start > 0 ? events[start - 1] : new HistoryDescription
            {
                cities = history.initial_cities ?? new List<string>(), description = "", time = ""
            };
            for (int i = start; i < Mathf.Min(events.Count, start + EventPageSize); i++)
            {
                ListHistoryDescriptions(lastDesc, events[i], detailSpace);
                lastDesc = events[i];
            }
        }
        public static void ListHistoryDescriptions(HistoryDescription lastDesc, HistoryDescription desc, AutoVertLayoutGroup parent)
        {
            var previousCities = new HashSet<string>(lastDesc.cities ?? new List<string>());
            var currentCities = desc.cities ?? new List<string>();
            var currentCitySet = new HashSet<string>(currentCities);
            if (lastDesc.time != desc.time)
            {
                // Era changes are quiet timeline markers, so they deliberately have no card background.
                var eraText = parent.AddTextIntoVertLayout(desc.time.ColorString(pColor:new Color(0.35f, 0.82f, 1.0f)), true,
                    TextAnchor.MiddleCenter, new Vector2(130, 18));
                eraText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
                string content = "";
                string com = "";
                int init = 0;
                foreach (var city in currentCities)
                {
                    init++;
                    com = init<currentCities.Count? ", ": "";
                    if (!previousCities.Contains(city))
                    {
                        content+= city.ColorString(pColor:new Color(0.4f, 0.8f, 0.4f)) +$"({LM.Get("obtain_city")})"+ com;
                    }
                    else
                    {
                        content += city +　com;
                    }
                }
                
                foreach (var lCity in previousCities)
                {
                    com = string.IsNullOrEmpty(content) ? "" : ", ";
                    if (!currentCitySet.Contains(lCity))
                    {
                        content += com + lCity.ColorString(pColor:new Color(1.0f, 0.3f, 0.3f)) +$"({LM.Get("lost_city")})";
                    }
                }
                var cityChangeText = parent.AddTextIntoVertLayout(content, false, TextAnchor.MiddleCenter, new Vector2(UIHelper.WidePanelWidth, 16));
                cityChangeText.UseFixedFontSize(8, HorizontalWrapMode.Wrap);
                cityChangeText.RefreshAutoHeight(16, 4);
            }
            Actor actor = desc.actor_id > 0 ? World.world.units.get(desc.actor_id) : null;
            Kingdom kingdom = actor == null && desc.kingdom_id > 0 ? World.world.kingdoms.get(desc.kingdom_id) : null;
            string monthDay = HistoryDateFormatter.GetMonthDay(desc.timestamp);
            var markers = new List<HistoryEventRowMarker>();
            if (actor != null)
            {
                markers.Add(new HistoryEventRowMarker(24f, eventCard =>
                {
                    var avatarLayout = eventCard.BeginVertGroup(new Vector2(24, 24), pSpacing: 0,
                        pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
                    var avatar = UIHelper.CreateAvatarView(actor.id, () => UIHelper.actorClick(actor),
                        pIsAlive: actor.isAlive());
                    avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
                    avatarLayout.AddChild(avatar.gameObject);
                    avatarLayout.transform.localPosition = Vector3.zero;
                }));
            }
            else if (kingdom != null)
            {
                markers.Add(new HistoryEventRowMarker(24f, eventCard =>
                {
                    KingdomBanner banner = Instantiate(Resources.Load<KingdomBanner>("ui/PrefabBannerKingdom"),
                        eventCard.transform);
                    banner.enable_default_click = true;
                    banner.load(kingdom);
                    banner.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
                    eventCard.AddChild(banner.gameObject);
                }));
            }
            HistoryEventRow.Add(parent, monthDay.ColorString(pColor: new Color(0.25f, 0.9f, 0.8f)),
                desc.description, markers);
        }
    }
}
