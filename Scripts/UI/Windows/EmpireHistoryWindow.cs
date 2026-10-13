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
        // 横屏主从布局：左栏固定的历代名单(分页)，右侧是选中这一位的详情与记事
        private AutoVertLayoutGroup _rail;
        private const float ContentWidth = UIHelper.PageMainWidth;
        protected override void Init()
        {
            UIHelper.FixWideWindowScrollArea(BackgroundTransform);
            _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
            _rail = UIHelper.CreateWideWindowRail(BackgroundTransform, UIHelper.RailWidth, UIHelper.RailInset);
        }
        public void Clear()
        {
            _generation++;
            UIHelper.ClearRail(_rail);
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
            int pages = Mathf.Max(1, (_histories.Count + PageSize - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            if (_expandedHistory == null || !_histories.Contains(_expandedHistory))
                _expandedHistory = _histories.Skip(_page * PageSize).FirstOrDefault();
            DrawRail(pages);
            var parent = _root.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 6));
            UIHelper.FixPanelWidth(parent.transform, ContentWidth);
            _groups["empire_history_records"] = parent.gameObject;
            if (_expandedHistory == null)
            {
                parent.AddPageHeader(ContentWidth, "EmpireHistoryWindow Title");
                return;
            }
            AddRulerDetail(parent, _expandedHistory);
            AddExpandedHistoryDetails(parent, _expandedHistory);
            StartCoroutine(RefreshScroll(_generation, restorePosition));
        }

        private IEnumerator RefreshScroll(int generation, Vector3? restorePosition)
        {
            yield return CoroutineHelper.wait_for_next_frame;
            if (generation != _generation || !gameObject.activeInHierarchy) yield break;
            ScrollWindow window = ScrollWindowComponent;
            if (window?.transform_content == null) yield break;
            for (int pass = 0; pass < 3; pass++) LayoutRebuilder.ForceRebuildLayoutImmediate(window.transform_content);
            if (restorePosition.HasValue) window.transform_content.localPosition = restorePosition.Value;
            else if (window.scrollRect != null) window.scrollRect.verticalNormalizedPosition = 1f;
            window.scrollRect?.StopMovement();
        }

        // 左栏历代名单(固定不随滚动)：国号与总数 → 本页十二位(选中的高亮) → 翻页
        private void DrawRail(int pages)
        {
            UIHelper.ClearRail(_rail);
            if (_rail == null) return;
            var list = _rail.BeginRailCard("FactionFrame_dominate");
            list.AddRailText((_empire?.GetEmpireFullName() ?? LM.Get("label_none")).ColorString("#F3C34A"), 9, 13f);
            list.AddRailText(string.Format(LM.Get("lineage_chip_total"), _histories.Count).ColorString("#8FA0A8"), 6, 9f);
            int start = _page * PageSize;
            int end = Mathf.Min(_histories.Count, start + PageSize);
            for (int i = start; i < end; i++) AddRulerRow(list, _histories[i], i - start);
            if (pages <= 1) return;
            float inner = UIHelper.RailWidth - 8f;
            var pager = list.BeginHoriGroup(new Vector2(inner, 14f), TextAnchor.MiddleCenter, 2, new RectOffset(0, 0, 0, 0));
            pager.AddButtonIntoHoriLayout("lineage_previous", "‹", () =>
            {
                if (_page > 0) { _page--; _expandedHistory = null; ShowPersonalHistory(); }
            }, size: new Vector2(20f, 13f));
            pager.AddTextIntoHoriLayout($"{_page + 1}/{pages}".ColorString("#C8C2B4"), true, TextAnchor.MiddleCenter,
                new Vector2(inner - 48f, 13f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            pager.AddButtonIntoHoriLayout("lineage_next", "›", () =>
            {
                if (_page + 1 < pages) { _page++; _expandedHistory = null; ShowPersonalHistory(); }
            }, size: new Vector2(20f, 13f));
        }

        private void AddRulerRow(AutoVertLayoutGroup list, EmpireCraftHistory history, int index)
        {
            bool selected = ReferenceEquals(_expandedHistory, history);
            float inner = UIHelper.RailWidth - 8f;
            var row = list.BeginHoriGroup(new Vector2(inner, 17f), TextAnchor.MiddleLeft, 2, new RectOffset(3, 3, 0, 0));
            if (selected || index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 17f));
            string name = string.IsNullOrWhiteSpace(history.emperor) ? LM.Get("waiting_for_naming") : history.emperor;
            var nameText = row.AddTextIntoHoriLayout(name.ColorString(selected ? "#F3C34A" : "#F2EEE2"), true,
                TextAnchor.MiddleLeft, new Vector2(inner - 42f, 15f));
            nameText.UseFixedFontSize(selected ? 8 : 7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(nameText);
            row.AddTextIntoHoriLayout($"{ReignYears(history)}{LM.Get("Year")}".ColorString("#8FA0A8"), true,
                TextAnchor.MiddleRight, new Vector2(34f, 15f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            UIHelper.MakeClickable(row.gameObject, () =>
            {
                if (ReferenceEquals(_expandedHistory, history)) return;
                _expandedHistory = history;
                ShowPersonalHistory();
            });
        }

        private int ReignYears(EmpireCraftHistory history) => ReferenceEquals(_empire?.data?.currentHistory, history)
            ? _empire.GetEmperorYear()
            : Mathf.Max(1, history.total_time);

        // 右侧详情：页头(姓名 · 年号 · 在位) + 人物卡(头像、姓名、称号)
        private void AddRulerDetail(AutoVertLayoutGroup parent, EmpireCraftHistory history)
        {
            string emperorName = string.IsNullOrWhiteSpace(history.emperor) ? LM.Get("waiting_for_naming") : history.emperor;
            string eraName = string.IsNullOrWhiteSpace(history.year_name) ? LM.Get("waiting_for_naming") : history.year_name;
            parent.AddPageHeader(ContentWidth, emperorName, $"{LM.Get("year_name")}: {eraName}",
                $"{ReignYears(history)}{LM.Get("Year")}");
            var card = parent.BeginCard(ContentWidth);
            var row = card.BeginHoriGroup(new Vector2(ContentWidth - 12f, 34f), TextAnchor.MiddleLeft, 6, new RectOffset(0, 0, 0, 0));
            row.transform.localPosition = Vector3.zero;
            Actor actor = history.id > 0 ? World.world.units.get(history.id) : null;
            PersonalClanIdentity identity = FindHistoryIdentity(history.id, history.emperor);
            bool isAlive = identity?.is_alive ?? (actor != null && actor.isAlive());
            var avatarLayout = row.BeginVertGroup(new Vector2(30, 30), pSpacing: 0,
                pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
            var avatar = UIHelper.CreateAvatarView(history.id, actor == null ? null : () => UIHelper.actorClick(actor),
                pIsAlive: isAlive, pIdentity: identity);
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
            avatarLayout.AddChild(avatar.gameObject);
            avatarLayout.transform.localPosition = Vector3.zero;
            var details = row.BeginVertGroup(new Vector2(ContentWidth - 12f - 36f, 32f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
                pPadding: new RectOffset(0, 0, 0, 0));
            details.transform.localPosition = Vector3.zero;
            Color empireColor = _empire?.getColor()._color_text ?? new Color(0.55f, 0.85f, 1f);
            details.AddTextIntoVertLayout(emperorName.ColorString(pColor: empireColor), true, TextAnchor.MiddleLeft,
                new Vector2(ContentWidth - 50f, 15f)).UseFixedFontSize(11, HorizontalWrapMode.Overflow);
            string title = HistoryTitle(history);
            if (!string.IsNullOrEmpty(title))
            {
                var titleText = details.AddTextIntoVertLayout(title.ColorString("#F3C34A"), true, TextAnchor.MiddleLeft,
                    new Vector2(ContentWidth - 50f, 10f));
                titleText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
                HoverMarqueeText.Attach(titleText);
            }
            details.AddTextIntoVertLayout(
                $"{LM.Get("year_name")}: {eraName}  ·  {ReignYears(history)}{LM.Get("Year")}".ColorString("#40E6CC"), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 50f, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        }

        // 称号：共和国元首是"国家全称 + 职务"；君主有谥号时给庙号谥号
        private static string HistoryTitle(EmpireCraftHistory history)
        {
            if (history.is_republic)
                return (string.IsNullOrWhiteSpace(history.empire_full_name) ? history.empire_name : history.empire_full_name) +
                       (history.office_title ?? "");
            if (string.IsNullOrEmpty(history.shihao_name)) return "";
            string shi = PosthumousNameGenerator.ShiTitle(history);
            return string.IsNullOrEmpty(history.miaohao_name)
                ? shi
                : history.empire_name + LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix) + " · " + shi;
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

        private PersonalClanIdentity FindHistoryIdentity(long actorId, string actorName = null)
        {
            if (actorId > 0 && _actorIdentities.TryGetValue(actorId, out var identity)) return identity;
            if (!string.IsNullOrWhiteSpace(actorName) && _legacyIdentities.TryGetValue(actorName, out identity)) return identity;
            return null;
        }

        private void AddExpandedHistoryDetails(AutoVertLayoutGroup parent, EmpireCraftHistory history)
        {
            var events = history.descriptions?.Where(d => d != null).ToList();
            if (events == null || events.Count == 0) return;
            parent.AddSectionHeader(ContentWidth, "empire_history_events", "#B3E6FF", true, "ui/iconHistory");
            var detailSpace = parent.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 0));
            UIHelper.FixPanelWidth(detailSpace.transform, ContentWidth);
            int pages = Mathf.Max(1, (events.Count + EventPageSize - 1) / EventPageSize);
            int page = _eventPages.TryGetValue(history, out int saved) ? Mathf.Clamp(saved, 0, pages - 1) : 0;
            if (pages > 1)
            {
                var navigation = detailSpace.BeginHoriGroup(new Vector2(ContentWidth, 18f), TextAnchor.MiddleCenter, 8f,
                    new RectOffset(0, 0, 0, 0));
                navigation.AddButtonIntoHoriLayout("lineage_previous", LM.Get("lineage_previous"), () =>
                {
                    if (page == 0) return;
                    _eventPages[history] = page - 1; ShowPersonalHistory();
                }, size: new Vector2(62f, 16f));
                navigation.AddTextIntoHoriLayout(string.Format(LM.Get("history_event_page"), page + 1, pages, events.Count),
                    true, TextAnchor.MiddleCenter, new Vector2(150f, 16f));
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
                ListHistoryDescriptions(lastDesc, events[i], detailSpace, ContentWidth);
                lastDesc = events[i];
            }
        }
        public static void ListHistoryDescriptions(HistoryDescription lastDesc, HistoryDescription desc, AutoVertLayoutGroup parent,
            float width = UIHelper.WidePanelWidth)
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
                var cityChangeText = parent.AddTextIntoVertLayout(content, false, TextAnchor.MiddleCenter, new Vector2(width, 16));
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
