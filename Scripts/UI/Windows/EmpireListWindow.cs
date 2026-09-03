using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;
using UnityEngine.Events;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine.UI;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.UI.Windows
{
    public class EmpireListWindow : AutoLayoutWindow<EmpireListWindow>
    {
        AutoVertLayoutGroup TopLayout;
        ListPool<GameObject> ListPool;
        protected override void Init()
        {
            ListPool = new ListPool<GameObject>();
            TopLayout = this.BeginVertGroup(pSpacing: 5, pPadding: new RectOffset(3, 3, 80, 3));
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            Clear();
            ShowTop();
        }

        public void Clear()
        {
            if (ListPool == null) return;
            float deleteTime = 0.2f;
            foreach (GameObject go in ListPool) 
            {
                go.SetActive(false);
                Destroy(go, deleteTime);
                deleteTime += 0.2f;
            }
            ListPool.Clear();
        }


        public void ShowTop() 
        {
            var toolbar = TopLayout.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.MiddleCenter);
            toolbar.AddButtonIntoVertLayout("purge_recent_100_years", LM.Get("purge_recent_100_years"), () =>
            {
                ModClass.EMPIRE_MANAGER.PurgeArchivedOlderThanYears(100);
                Clear();
                ShowTop();
            }, size: new Vector2(60, 12));
            ListPool.Add(toolbar.gameObject);

            var cultureGroups = new Dictionary<string, List<Empire>>();
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                if (empire == null || empire.data == null) continue;
                string culture = GetEmpireCulture(empire);
                if (!cultureGroups.TryGetValue(culture, out List<Empire> empires))
                {
                    empires = new List<Empire>();
                    cultureGroups[culture] = empires;
                }
                empires.Add(empire);
            }

            foreach (var cultureGroup in cultureGroups.OrderBy(group => GetCultureDisplayName(group.Key)))
            {
                AddCultureTimeline(cultureGroup.Key, cultureGroup.Value);
            }
        }

        private void AddCultureTimeline(string culture, List<Empire> empires)
        {
            var section = TopLayout.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
            ListPool.Add(section.gameObject);
            string cultureName = GetCultureDisplayName(culture);
            var title = section.AddTextIntoVertLayout(cultureName.ColorString(pColor: new Color(0.45f, 0.85f, 1f)), true,
                TextAnchor.MiddleCenter, new Vector2(196, 16));
            title.UseFixedFontSize(10, HorizontalWrapMode.Overflow);

            List<Empire> aliveEmpires = empires.Where(empire => !empire.IsArchived())
                .OrderBy(empire => empire.data.timestamp_established_time).ToList();
            List<Empire> archivedEmpires = empires.Where(empire => empire.IsArchived())
                .OrderBy(empire => empire.data.timestamp_established_time).ToList();
            AddTimelineState(section, LM.Get("empire_list_current"), aliveEmpires, true);
            AddTimelineState(section, LM.Get("empire_list_archived"), archivedEmpires, false);
        }

        private void AddTimelineState(AutoVertLayoutGroup parent, string stateName, List<Empire> empires, bool alive)
        {
            if (empires.Count == 0) return;
            var stateText = parent.AddTextIntoVertLayout(stateName.ColorString(pColor: alive
                    ? new Color(0.35f, 0.9f, 0.55f)
                    : new Color(0.65f, 0.72f, 0.78f)),
                true, TextAnchor.MiddleLeft, new Vector2(190, 11));
            stateText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            foreach (Empire empire in empires)
            {
                AddEmpireTimelineCard(parent, empire, alive);
            }
        }

        private void AddEmpireTimelineCard(AutoVertLayoutGroup parent, Empire empire, bool alive)
        {
            var card = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(196, 36));
            var timeColumn = this.BeginVertGroup(new Vector2(38, 32), pSpacing: -2, pAlignment: TextAnchor.MiddleCenter,
                pPadding: new RectOffset(0, 0, 1, 1));
            string foundedAt = empire.data.timestamp_established_time > 0
                ? Date.getDate(empire.data.timestamp_established_time)
                : "";
            int yearEnd = foundedAt.IndexOf('年');
            string foundedYear = yearEnd >= 0 ? foundedAt.Substring(0, yearEnd + 1) : foundedAt;
            var foundedText = timeColumn.AddTextIntoVertLayout(foundedYear.ColorString(pColor: new Color(1f, 0.78f, 0.2f)), true,
                TextAnchor.MiddleCenter, new Vector2(36, 16));
            foundedText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            var stateText = timeColumn.AddTextIntoVertLayout((alive ? LM.Get("empire_list_current") : LM.Get("empire_list_archived")).ColorString(
                pColor: alive ? new Color(0.35f, 0.9f, 0.55f) : new Color(0.65f, 0.72f, 0.78f)), true,
                TextAnchor.MiddleCenter, new Vector2(36, 10));
            stateText.UseFixedFontSize(5, HorizontalWrapMode.Overflow);
            timeColumn.transform.localPosition = Vector3.zero;
            card.AddChild(timeColumn.gameObject);

            var details = this.BeginVertGroup(new Vector2(126, 32), pSpacing: -2, pAlignment: TextAnchor.MiddleLeft,
                pPadding: new RectOffset(0, 0, 1, 1));
            string name = empire.GetEmpireFullName();
            var nameText = details.AddTextIntoVertLayout(name.ColorString(pColor: empire.getColor()._color_text), true,
                TextAnchor.MiddleLeft, new Vector2(122, 16));
            nameText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            int duration = alive && empire.data.timestamp_established_time > 0
                ? Mathf.Max(1, Date.getYearsSince(empire.data.timestamp_established_time) + 1)
                : GetEmpireRecordedDuration(empire);
            var durationText = details.AddTextIntoVertLayout($"{LM.Get("empire_core_history_duration")}: {duration}{LM.Get("Year")}", true,
                TextAnchor.MiddleLeft, new Vector2(122, 11));
            durationText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            details.transform.localPosition = Vector3.zero;
            card.AddChild(details.gameObject);

            EmpireCraftHistory history = GetRepresentativeHistory(empire);
            long actorId = history?.id ?? empire.data.emperor;
            PersonalClanIdentity identity = FindPersonByActorId(actorId);
            Actor actor = actorId > 0 ? World.world.units.get(actorId) : null;
            bool isAlive = identity?.is_alive ?? (actor != null && actor.isAlive());
            var avatarLayout = this.BeginVertGroup(new Vector2(28, 28), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter,
                pPadding: new RectOffset(0, 0, 0, 0));
            var avatar = UIHelper.CreateAvatarView(actorId, actor == null ? null : () => UIHelper.actorClick(actor), pIsAlive: isAlive);
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(28, 28);
            avatarLayout.AddChild(avatar.gameObject);
            avatarLayout.transform.localPosition = Vector3.zero;
            card.AddChild(avatarLayout.gameObject);
            card.transform.AddStretchBackground(alive ? "FactionFrame_dominate" : "clanFrame", new Vector2(196, 36));
            AddTimelineCardClickLayer(card, empire);
        }

        private static int GetEmpireRecordedDuration(Empire empire)
        {
            int duration = 0;
            foreach (EmpireCraftHistory history in empire.data.history ?? new List<EmpireCraftHistory>())
            {
                duration += history?.total_time ?? 0;
            }
            return Mathf.Max(1, duration);
        }

        private string GetEmpireCulture(Empire empire)
        {
            try
            {
                string activeCulture = empire.GetCulture();
                if (!string.IsNullOrWhiteSpace(activeCulture)) return activeCulture;
            }
            catch
            {
                // Archived empires no longer have a core kingdom, so fall through to their last emperor.
            }
            EmpireCraftHistory history = GetRepresentativeHistory(empire);
            PersonalClanIdentity identity = FindPersonByActorId(history?.id ?? empire.data.emperor);
            return string.IsNullOrWhiteSpace(identity?.culture) ? "unknown" : identity.culture;
        }

        private static string GetCultureDisplayName(string culture)
        {
            if (string.IsNullOrWhiteSpace(culture) || culture == "unknown") return LM.Get("empire_list_culture_unknown");
            return OnomasticsRule.ALL_CULTURE_TRANSLATE.ContainsKey(culture) ? culture.GetCultureTranslate() : culture;
        }

        private static EmpireCraftHistory GetRepresentativeHistory(Empire empire)
        {
            return empire.data.currentHistory ?? empire.data.history?.LastOrDefault();
        }

        private static PersonalClanIdentity FindPersonByActorId(long actorId)
        {
            if (actorId <= 0) return null;
            foreach (PersonalClanIdentity identity in SpecificClanManager._globalPersonLookup.Values)
            {
                if (identity.actor_id == actorId) return identity;
            }
            return null;
        }

        private void AddTimelineCardClickLayer(AutoHoriLayoutGroup card, Empire empire)
        {
            var overlay = new GameObject("EmpireTimelineCardClick", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            overlay.transform.SetParent(card.transform, false);
            var rect = overlay.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = overlay.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            overlay.GetComponent<LayoutElement>().ignoreLayout = true;
            var button = overlay.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OpenEmpireHistory(empire));
            overlay.transform.SetAsLastSibling();
        }

        private void OpenEmpireHistory(Empire empire)
        {
            if (empire.CoreKingdom != null) SelectedMetas.selected_kingdom = empire.CoreKingdom;
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            ConfigData.CURRENT_SELECTED_HISTORY = GetRepresentativeHistory(empire);
            ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
        }
    }
}
