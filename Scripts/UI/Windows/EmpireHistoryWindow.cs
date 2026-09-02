using System.Collections.Generic;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.General;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine.UI;
namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireHistoryWindow : AutoLayoutWindow<EmpireHistoryWindow>
    {
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>();
        private Empire _empire;
        private EmpireCraftHistory _expandedHistory;
        protected override void Init()
        {
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 3, 3);
        }
        public void Clear()
        {
            foreach (var container in _groups)
            {
                GameObject.Destroy(container.Value);
            }
            _groups.Clear();
        }
        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            layout.spacing = 3;
            layout.padding = new RectOffset(3, 3, 3, 3);
            _empire = EmpireCraftMetaTypeLibrary.selected_empire;
            _expandedHistory = null;
            Clear();
            ShowPersonalHistory();
        }
        public void ShowPersonalHistory()
        {
            Clear();
            var parent = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            _groups["empire_history_records"] = parent.gameObject;
            foreach (EmpireCraftHistory history in GetDisplayHistories())
            {
                AddEmperorHistoryCard(parent, history);
            }
        }

        private List<EmpireCraftHistory> GetDisplayHistories()
        {
            var result = new List<EmpireCraftHistory>();
            EmpireCraftHistory currentHistory = _empire?.data?.currentHistory;
            if (currentHistory != null)
            {
                result.Add(currentHistory);
            }
            if (_empire?.data?.history != null)
            {
                for (int i = _empire.data.history.Count - 1; i >= 0; i--)
                {
                    EmpireCraftHistory history = _empire.data.history[i];
                    if (history != null && !result.Contains(history)) result.Add(history);
                }
            }
            EmpireCraftHistory selectedHistory = ConfigData.CURRENT_SELECTED_HISTORY;
            if (selectedHistory != null && !result.Contains(selectedHistory)) result.Add(selectedHistory);
            return result;
        }

        private void AddEmperorHistoryCard(AutoVertLayoutGroup parent, EmpireCraftHistory history)
        {
            bool expanded = object.ReferenceEquals(_expandedHistory, history);
            var card = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(196, 34));
            Actor actor = history.id > 0 ? World.world.units.get(history.id) : null;
            var avatar = UIHelper.CreateAvatarView(history.id, actor == null ? null : () => UIHelper.actorClick(actor),
                pIsAlive: actor != null && actor.isAlive());
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(28, 28);
            card.AddChild(avatar.gameObject);

            var details = card.BeginVertGroup(new Vector2(146, 30), pSpacing: -2, pAlignment: TextAnchor.MiddleLeft,
                pPadding: new RectOffset(0, 0, 1, 1));
            Color empireColor = _empire?.getColor()._color_text ?? new Color(0.55f, 0.85f, 1f);
            string emperorName = string.IsNullOrWhiteSpace(history.emperor) ? LM.Get("waiting_for_naming") : history.emperor;
            var nameText = details.AddTextIntoVertLayout(emperorName.ColorString(pColor: empireColor), true,
                TextAnchor.MiddleLeft, new Vector2(142, 15));
            nameText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            string eraName = string.IsNullOrWhiteSpace(history.year_name) ? LM.Get("waiting_for_naming") : history.year_name;
            int reignYears = object.ReferenceEquals(_empire?.data?.currentHistory, history)
                ? _empire.GetEmperorYear()
                : Mathf.Max(1, history.total_time);
            var reignText = details.AddTextIntoVertLayout(
                $"{LM.Get("year_name")}: {eraName.ColorString(pColor: new Color(1f, 0.78f, 0.2f))}  ·  {reignYears}{LM.Get("Year")}".ColorString(pColor: new Color(0.25f, 0.9f, 0.8f)),
                true, TextAnchor.MiddleLeft, new Vector2(142, 12));
            reignText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var stateText = card.AddTextIntoHoriLayout((expanded ? "−" : "+").ColorString(pColor: expanded
                    ? new Color(1f, 0.78f, 0.2f)
                    : new Color(0.65f, 0.82f, 1f)),
                true, TextAnchor.MiddleCenter, new Vector2(14, 20));
            stateText.UseFixedFontSize(10, HorizontalWrapMode.Overflow);
            card.transform.AddStretchBackground(expanded ? "FactionFrame_dominate" : "clanFrame", new Vector2(196, 34));
            AddHistoryCardClickLayer(card, history);

            if (!expanded) return;
            AddExpandedHistoryDetails(parent, history);
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
                _expandedHistory = object.ReferenceEquals(_expandedHistory, history) ? null : history;
                ShowPersonalHistory();
            });
            overlay.transform.SetAsLastSibling();
        }

        private void AddExpandedHistoryDetails(AutoVertLayoutGroup parent, EmpireCraftHistory history)
        {
            if (history.descriptions == null || history.descriptions.Count == 0) return;
            var detailSpace = parent.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            HistoryDescription lastDesc = new HistoryDescription()
            {
                cities = history.initial_cities != null ? new List<string>(history.initial_cities) : new List<string>(),
                description = "",
                time = ""
            };
            foreach (HistoryDescription description in history.descriptions)
            {
                ListHistoryDescriptions(lastDesc, description, detailSpace);
                lastDesc = description;
            }
        }
        public static void ListHistoryDescriptions(HistoryDescription lastDesc, HistoryDescription desc, AutoVertLayoutGroup parent)
        {
            if (lastDesc.time != desc.time)
            {
                // Era changes are quiet timeline markers, so they deliberately have no card background.
                var eraText = parent.AddTextIntoVertLayout(desc.time.ColorString(pColor:new Color(0.35f, 0.82f, 1.0f)), true,
                    TextAnchor.MiddleCenter, new Vector2(130, 18));
                eraText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
                string content = "";
                string com = "";
                int init = 0;
                foreach (var city in desc.cities)
                {
                    init++;
                    com = init<desc.cities.Count? ", ": "";
                    if (!lastDesc.cities.Contains(city))
                    {
                        content+= city.ColorString(pColor:new Color(0.4f, 0.8f, 0.4f)) +$"({LM.Get("obtain_city")})"+ com;
                    }
                    else
                    {
                        content += city +　com;
                    }
                }
                
                foreach (var lCity in lastDesc.cities)
                {
                    com = string.IsNullOrEmpty(content) ? "" : ", ";
                    if (!desc.cities.Contains(lCity))
                    {
                        content += com + lCity.ColorString(pColor:new Color(1.0f, 0.3f, 0.3f)) +$"({LM.Get("lost_city")})";
                    }
                }
                var cityChangeText = parent.AddTextIntoVertLayout(content, false, TextAnchor.MiddleCenter, new Vector2(196, 16));
                cityChangeText.UseFixedFontSize(8, HorizontalWrapMode.Wrap);
                cityChangeText.RefreshAutoHeight(16, 4);
            }
            Actor actor = desc.actor_id > 0 ? World.world.units.get(desc.actor_id) : null;
            Kingdom kingdom = actor == null && desc.kingdom_id > 0 ? World.world.kingdoms.get(desc.kingdom_id) : null;
            bool hasMarker = actor != null || kingdom != null;
            var eventCard = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(196, 28));
            string date = desc.timestamp >= 0 ? Date.getDate(desc.timestamp) : "";
            int yearEnd = date.IndexOf('年');
            string monthDay = yearEnd >= 0 ? date.Substring(yearEnd + 1) : date;
            var dateText = eventCard.AddTextIntoHoriLayout(monthDay.ColorString(pColor: new Color(0.25f, 0.9f, 0.8f)), true, TextAnchor.MiddleCenter, new Vector2(38, 22));
            dateText.UseFixedFontSize(8);
            var contentText = eventCard.AddTextIntoHoriLayout(desc.description, true, TextAnchor.MiddleLeft, new Vector2(hasMarker ? 126 : 152, 22));
            contentText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            if (actor != null)
            {
                var avatarLayout = eventCard.BeginVertGroup(new Vector2(24, 24), pSpacing: 0,
                    pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
                var avatar = UIHelper.CreateAvatarView(actor.id, () => UIHelper.actorClick(actor), pIsAlive: actor.isAlive());
                avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
                avatarLayout.AddChild(avatar.gameObject);
                avatarLayout.transform.localPosition = Vector3.zero;
                avatarLayout.transform.SetAsLastSibling();
            }
            else if (kingdom != null)
            {
                KingdomBanner banner = Instantiate(Resources.Load<KingdomBanner>("ui/PrefabBannerKingdom"), eventCard.transform);
                banner.enable_default_click = true;
                banner.load(kingdom);
                banner.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
                eventCard.AddChild(banner.gameObject);
                banner.transform.SetAsLastSibling();
            }
            eventCard.transform.AddStretchBackground("clanFrame", new Vector2(196, 28));
        }
    }
}
