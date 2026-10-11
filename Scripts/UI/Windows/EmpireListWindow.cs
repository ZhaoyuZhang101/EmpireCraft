using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using UnityEngine;
using EmpireCraft.Scripts.UI.Components;
using UnityEngine.UI;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 帝国列表：顶部概览与筛选(现存 / 历史 / 全部)，按文化分组(文化纹章盾作组标题)。
    //   · 现存帝国：帝国色带、国名、元首与政体、城市 / 人口 / 正统 / 立国年数，右侧元首头像；点开帝国窗口；
    //   · 历史帝国：紧凑一行(牌位图标、国名、存续年数、末代君主)，点开史书；
    //   · 没有帝国时显示空状态与称帝提示。
    // 图标在 GameResources/ui/icons/empirelist/(Tools/IconForge/imperial_forge.py 的 EMPIRELIST)。
    public class EmpireListWindow : AutoLayoutWindow<EmpireListWindow>
    {
        private enum Filter { Alive, Archived, All }

        private const string IconPath = "ui/icons/empirelist/";
        private const float Width = 196f;
        private static readonly Color Gold = new(1f, 0.82f, 0.38f);
        private static readonly Color Muted = new(0.68f, 0.72f, 0.74f);
        private static readonly Color Live = new(0.42f, 0.9f, 0.58f);

        private AutoVertLayoutGroup _top;
        private readonly List<GameObject> _rows = new();
        private Filter _filter = Filter.Alive;

        protected override void Init()
        {
            _top = this.BeginVertGroup(pSpacing: 4, pPadding: new RectOffset(3, 3, 80, 3));
        }

        public override void OnNormalEnable()
        {
            base.OnNormalEnable();
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (GameObject go in _rows)
            {
                go.SetActive(false);
                Destroy(go);
            }
            _rows.Clear();

            List<Empire> all = ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null).ToList();
            List<Empire> alive = all.Where(empire => !empire.IsArchived()).ToList();
            List<Empire> archived = all.Where(empire => empire.IsArchived()).ToList();
            AddSummary(alive.Count, archived.Count);

            List<Empire> shown = _filter switch
            {
                Filter.Alive => alive,
                Filter.Archived => archived,
                _ => all
            };
            if (shown.Count == 0)
            {
                AddEmptyState();
                return;
            }
            foreach (var group in shown.GroupBy(GetEmpireCulture)
                         .OrderByDescending(group => group.Count(empire => !empire.IsArchived()))
                         .ThenBy(group => GetCultureDisplayName(group.Key)))
            {
                AddCultureHeader(group.Key, group.Count());
                foreach (Empire empire in group.Where(e => !e.IsArchived()).OrderByDescending(e => e.countCities()))
                    AddAliveCard(empire);
                foreach (Empire empire in group.Where(e => e.IsArchived())
                             .OrderByDescending(e => e.data.timestamp_established_time))
                    AddArchivedRow(empire);
            }
            if (_filter != Filter.Alive && archived.Count > 0)
            {
                var toolbar = _top.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter);
                toolbar.AddButtonIntoVertLayout("purge_recent_100_years", LM.Get("purge_recent_100_years"), () =>
                {
                    ModClass.EMPIRE_MANAGER.PurgeArchivedOlderThanYears(100);
                    Rebuild();
                }, size: new Vector2(80, 14));
                _rows.Add(toolbar.gameObject);
            }
        }

        // ───────── 顶部概览与筛选 ─────────
        private void AddSummary(int aliveCount, int archivedCount)
        {
            var bar = _top.BeginHoriGroup(pSpacing: 4, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(Width, 24));
            bar.transform.AddStretchBackground("regimeFrame", new Vector2(Width, 24));
            AddFilterButton(bar, Filter.Alive, "filter_alive", LM.Get("empire_list_current"), aliveCount);
            AddFilterButton(bar, Filter.Archived, "filter_archived", LM.Get("empire_list_archived"), archivedCount);
            AddFilterButton(bar, Filter.All, "filter_all", LM.Get("empire_list_all"), aliveCount + archivedCount);
            _rows.Add(bar.gameObject);
        }

        private void AddFilterButton(AutoHoriLayoutGroup bar, Filter filter, string icon, string label, int count)
        {
            bool active = _filter == filter;
            var cell = bar.BeginHoriGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(62, 20));
            var button = cell.AddButtonIntoHoriLayout("empire_list_filter_" + icon, "", () =>
            {
                _filter = filter;
                Rebuild();
            }, SpriteTextureLoader.getSprite(IconPath + icon), size: new Vector2(16, 16));
            button.Background.enabled = false;
            var text = cell.AddTextIntoHoriLayout($"{label} {count}".ColorString(pColor: active ? Gold : Muted), true,
                TextAnchor.MiddleLeft, new Vector2(44, 16));
            text.UseFixedFontSize(active ? 7 : 6, HorizontalWrapMode.Overflow);
            bar.AddChild(cell.gameObject);
        }

        private void AddEmptyState()
        {
            var box = _top.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.MiddleCenter);
            var icon = box.AddButtonIntoVertLayout("empire_list_empty", "", null,
                SpriteTextureLoader.getSprite(IconPath + "filter_alive"), size: new Vector2(40, 40));
            icon.Background.enabled = false;
            var title = box.AddTextIntoVertLayout(LM.Get(_filter == Filter.Archived
                    ? "empire_list_empty_archived" : "empire_list_empty").ColorString(pColor: Gold), true,
                TextAnchor.MiddleCenter, new Vector2(Width, 16));
            title.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            if (_filter != Filter.Archived)
            {
                var hint = box.AddTextIntoVertLayout(LM.Get("empire_list_empty_hint").ColorString(pColor: Muted), true,
                    TextAnchor.MiddleCenter, new Vector2(Width - 10, 30), mode: HorizontalWrapMode.Wrap);
                hint.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            }
            _rows.Add(box.gameObject);
        }

        // ───────── 文化分组标题 ─────────
        private void AddCultureHeader(string culture, int count)
        {
            var header = _top.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(Width, 18));
            var icon = header.AddButtonIntoHoriLayout("empire_list_culture_" + culture, "", null,
                CultureIcons.Get(culture), size: new Vector2(16, 16));
            icon.Background.enabled = false;
            var text = header.AddTextIntoHoriLayout(
                $"{GetCultureDisplayName(culture)}".ColorString(pColor: new Color(0.55f, 0.85f, 1f)) +
                $"  ×{count}".ColorString(pColor: Muted), true, TextAnchor.MiddleLeft, new Vector2(Width - 22, 16));
            text.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
            _rows.Add(header.gameObject);
        }

        // ───────── 现存帝国卡片 ─────────
        private void AddAliveCard(Empire empire)
        {
            var card = _top.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(Width, 46),
                pPadding: new RectOffset(4, 4, 3, 3));
            card.transform.AddStretchBackground("FactionFrame_dominate", new Vector2(Width, 46));

            AddColorStrip(card, empire.getColor()?.getColorMain() ?? Color.gray, 38);

            var details = this.BeginVertGroup(new Vector2(150, 40), pSpacing: -1, pAlignment: TextAnchor.MiddleLeft);
            var name = details.AddTextIntoVertLayout(empire.GetEmpireFullName().ColorString(
                pColor: empire.getColor()?._color_text ?? Color.white), true, TextAnchor.MiddleLeft, new Vector2(148, 13));
            name.UseFixedFontSize(9, HorizontalWrapMode.Overflow);

            bool republic = RepublicSystem.IsRepublic(empire);
            var headLine = details.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(148, 11));
            AddIcon(headLine, republic ? "head_republic" : "head_monarch", 10);
            string headTitle = republic ? RepublicSystem.GetHeadOfStateTitle(empire) : LM.Get("emperor");
            string headName = empire.Emperor != null && empire.Emperor.isAlive() ? empire.Emperor.getName() : LM.Get("label_none");
            AddSmallText(headLine, $"{headTitle} {headName}".ColorString(pColor: Gold) + " · " +
                                   RegimeLabel(empire).ColorString(pColor: Muted), 134);
            details.AddChild(headLine.gameObject);

            var stats = details.BeginHoriGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(148, 11));
            AddStat(stats, "stat_cities", empire.countCities().ToString());
            AddStat(stats, "stat_population", Compact(empire.CountPopulation()));
            AddStat(stats, "stat_mandate", empire.Mandate.ToString());
            int years = empire.data.timestamp_established_time > 0
                ? Mathf.Max(1, Date.getYearsSince(empire.data.timestamp_established_time) + 1) : 1;
            AddStat(stats, "stat_years", years + LM.Get("Year"));
            details.AddChild(stats.gameObject);
            details.transform.localPosition = Vector3.zero;
            card.AddChild(details.gameObject);

            Actor head = empire.Emperor;
            var avatar = UIHelper.CreateAvatarView(head?.data?.id ?? -1L, head == null ? null : () => UIHelper.actorClick(head),
                pIsAlive: head != null && head.isAlive());
            avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
            card.AddChild(avatar.gameObject);

            AddClickLayer(card, () => OpenEmpire(empire));
            _rows.Add(card.gameObject);
        }

        // ───────── 历史帝国(紧凑一行) ─────────
        private void AddArchivedRow(Empire empire)
        {
            var row = _top.BeginHoriGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(Width, 24),
                pPadding: new RectOffset(4, 4, 2, 2));
            row.transform.AddStretchBackground("clanFrame", new Vector2(Width, 24));
            AddIcon(row, "filter_archived", 16);
            var details = this.BeginVertGroup(new Vector2(168, 20), pSpacing: -2, pAlignment: TextAnchor.MiddleLeft);
            var name = details.AddTextIntoVertLayout(empire.GetEmpireFullName().ColorString(pColor: Muted), true,
                TextAnchor.MiddleLeft, new Vector2(166, 11));
            name.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
            EmpireCraftHistory last = GetRepresentativeHistory(empire);
            string founded = empire.data.timestamp_established_time > 0
                ? HistoryDateFormatter.GetYear(empire.data.timestamp_established_time) : "";
            string line = $"{founded} · {LM.Get("empire_core_history_duration")} {GetEmpireRecordedDuration(empire)}{LM.Get("Year")}";
            if (!string.IsNullOrWhiteSpace(last?.emperor))
                line += $" · {LM.Get("empire_list_last_ruler")} {last.emperor}";
            var sub = details.AddTextIntoVertLayout(line.ColorString(pColor: new Color(0.55f, 0.58f, 0.6f)), true,
                TextAnchor.MiddleLeft, new Vector2(166, 9));
            sub.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            details.transform.localPosition = Vector3.zero;
            row.AddChild(details.gameObject);
            AddClickLayer(row, () => OpenEmpireHistory(empire));
            _rows.Add(row.gameObject);
        }

        // ───────── 小部件 ─────────
        private static void AddColorStrip(AutoHoriLayoutGroup parent, Color color, float height)
        {
            var strip = new GameObject("EmpireColorStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            strip.transform.SetParent(parent.transform, false);
            strip.GetComponent<Image>().color = color;
            var layout = strip.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 3;
            layout.minHeight = layout.preferredHeight = height;
        }

        private static void AddIcon(AutoHoriLayoutGroup parent, string icon, float size)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent.transform, false);
            var image = go.GetComponent<Image>();
            image.sprite = SpriteTextureLoader.getSprite(IconPath + icon);
            image.preserveAspect = true;
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = size;
            layout.minHeight = layout.preferredHeight = size;
        }

        private static void AddSmallText(AutoHoriLayoutGroup parent, string text, float width)
        {
            var t = parent.AddTextIntoHoriLayout(text, true, TextAnchor.MiddleLeft, new Vector2(width, 10));
            t.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        }

        private static void AddStat(AutoHoriLayoutGroup parent, string icon, string value)
        {
            AddIcon(parent, icon, 10);
            AddSmallText(parent, value, 25);
        }

        private static string Compact(int value) =>
            value >= 10000 ? $"{value / 1000f:0.#}k" : value.ToString();

        private static string RegimeLabel(Empire empire)
        {
            if (WarlordEraSystem.IsProvisionalGovernment(empire)) return LM.Get("empire_list_regime_provisional");
            if (RepublicSystem.IsRepublic(empire)) return LM.Get("empire_list_regime_republic");
            if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.Modern) return LM.Get("empire_list_regime_modern");
            return LM.Get("empire_list_regime_monarchy");
        }

        private static void AddClickLayer(AutoHoriLayoutGroup card, UnityEngine.Events.UnityAction action)
        {
            var overlay = new GameObject("EmpireListCardClick", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
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
            button.onClick.AddListener(action);
            overlay.transform.SetAsLastSibling();
        }

        // ───────── 数据 ─────────
        private static int GetEmpireRecordedDuration(Empire empire)
        {
            int duration = 0;
            foreach (EmpireCraftHistory history in empire.data.history ?? new List<EmpireCraftHistory>())
                duration += history?.total_time ?? 0;
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
                // 已亡帝国没有核心王国，退回末代君主的文化
            }
            EmpireCraftHistory history = GetRepresentativeHistory(empire);
            PersonalClanIdentity identity = FindPersonByActorId(history?.id ?? empire.data.emperor, history?.emperor);
            return string.IsNullOrWhiteSpace(identity?.culture) ? "unknown" : identity.culture;
        }

        private static string GetCultureDisplayName(string culture)
        {
            if (string.IsNullOrWhiteSpace(culture) || culture == "unknown") return LM.Get("empire_list_culture_unknown");
            return OnomasticsRule.ALL_CULTURE_TRANSLATE.ContainsKey(culture) ? culture.GetCultureTranslate() : culture;
        }

        private static EmpireCraftHistory GetRepresentativeHistory(Empire empire) =>
            empire.data.currentHistory ?? empire.data.history?.LastOrDefault();

        private static PersonalClanIdentity FindPersonByActorId(long actorId, string actorName = null)
        {
            if (actorId > 0)
            {
                foreach (PersonalClanIdentity identity in SpecificClanManager._globalPersonLookup.Values)
                    if (identity.actor_id == actorId) return identity;
            }
            if (string.IsNullOrWhiteSpace(actorName)) return null;
            List<PersonalClanIdentity> legacyMatches = SpecificClanManager._globalPersonLookup.Values
                .Where(identity => identity != null && !identity.is_alive && identity.actor_id <= 0 &&
                                   identity.name == actorName).Take(2).ToList();
            return legacyMatches.Count == 1 ? legacyMatches[0] : null;
        }

        private static void OpenEmpire(Empire empire)
        {
            if (empire.CoreKingdom == null) return;
            SelectedMetas.selected_kingdom = empire.CoreKingdom;
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            ScrollWindow.showWindow(nameof(EmpireWindow));
        }

        private static void OpenEmpireHistory(Empire empire)
        {
            if (empire.CoreKingdom != null) SelectedMetas.selected_kingdom = empire.CoreKingdom;
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            ConfigData.CURRENT_SELECTED_HISTORY = GetRepresentativeHistory(empire);
            ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
        }
    }
}
