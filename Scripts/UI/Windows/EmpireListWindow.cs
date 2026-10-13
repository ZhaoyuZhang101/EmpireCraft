using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Window.Layout;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using UnityEngine;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 帝国列表(「卷册」宽窗口，组件见 WideScrollWindow / WideKit)：
    //   · 左栏(固定)：天下帝国概览 → 现存 / 已亡 / 全部 → 按文化筛选(文化纹章) → 清理百年前的历史帝国；
    //   · 右栏：页头(当前筛选 · 国数) → 主指标(辖城、人口、最久立国) → 按文化分节：
    //     现存帝国两栏卡片(国色条、国号与政体、元首头像与年号、城/人口/正统/立国，整卡点开帝国窗口)，
    //     已亡帝国收在一张卡里一行一国(立国年、存续、末代君主，点开史书)；
    //   · 没有帝国时显示空状态与称帝提示。
    // 图标在 GameResources/ui/icons/empirelist/(Tools/IconForge/imperial_forge.py 的 EMPIRELIST)。
    public class EmpireListWindow : WideScrollWindow<EmpireListWindow>
    {
        private enum Filter { Alive, Archived, All }

        private const string IconPath = "ui/icons/empirelist/";
        private const int RailCultureRows = 8;

        private Filter _filter = Filter.Alive;
        // 空 = 全部文化
        private string _culture = "";
        private List<Empire> _alive = new(), _archived = new();
        private List<IGrouping<string, Empire>> _groups = new();

        // ───────── 数据(每次重画前取一遍) ─────────
        protected override void Prepare()
        {
            List<Empire> all = ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null).ToList();
            _alive = all.Where(empire => !empire.IsArchived()).ToList();
            _archived = all.Where(empire => empire.IsArchived()).ToList();
            List<Empire> byFilter = _filter switch
            {
                Filter.Alive => _alive,
                Filter.Archived => _archived,
                _ => all
            };
            _groups = byFilter.GroupBy(GetEmpireCulture)
                .OrderByDescending(group => group.Count(empire => !empire.IsArchived()))
                .ThenByDescending(group => group.Count())
                .ThenBy(group => GetCultureDisplayName(group.Key)).ToList();
            if (!string.IsNullOrEmpty(_culture) && _groups.All(group => group.Key != _culture)) _culture = "";
        }

        // ───────── 左栏 ─────────
        protected override void DrawRail(AutoVertLayoutGroup rail)
        {
            rail.AddRailTitle(LM.Get("empire_list_rail_title"),
                string.Format(LM.Get("empire_list_rail_summary"), _alive.Count, _archived.Count));

            var filters = rail.BeginRailMenu();
            filters.AddRailOption(Icon("filter_alive"), LM.Get("empire_list_current"), _alive.Count.ToString(),
                _filter == Filter.Alive, () => SetFilter(Filter.Alive), 0);
            filters.AddRailOption(Icon("filter_archived"), LM.Get("empire_list_archived"), _archived.Count.ToString(),
                _filter == Filter.Archived, () => SetFilter(Filter.Archived), 1);
            filters.AddRailOption(Icon("filter_all"), LM.Get("empire_list_all"), (_alive.Count + _archived.Count).ToString(),
                _filter == Filter.All, () => SetFilter(Filter.All), 2);

            if (_groups.Count > 1)
            {
                var cultures = rail.BeginRailMenu(LM.Get("empire_list_cultures"));
                cultures.AddRailOption(null, LM.Get("empire_list_all_cultures"), _groups.Sum(group => group.Count()).ToString(),
                    string.IsNullOrEmpty(_culture), () => SetCulture(""), 0);
                int index = 1;
                foreach (var group in _groups.Take(RailCultureRows))
                {
                    string culture = group.Key;
                    cultures.AddRailOption(CultureIcons.Get(culture), GetCultureDisplayName(culture), group.Count().ToString(),
                        _culture == culture, () => SetCulture(culture), index++);
                }
            }

            if (_filter != Filter.Alive && _archived.Count > 0)
                rail.AddRailButton("purge_recent_100_years", LM.Get("purge_recent_100_years"), () =>
                {
                    ModClass.EMPIRE_MANAGER.PurgeArchivedOlderThanYears(100);
                    Refresh();
                });
        }

        private void SetFilter(Filter filter)
        {
            if (_filter == filter) return;
            _filter = filter;
            _culture = "";
            Refresh(resetScroll: true);
        }

        private void SetCulture(string culture)
        {
            if (_culture == culture) return;
            _culture = culture;
            Refresh(resetScroll: true);
        }

        // ───────── 右栏 ─────────
        protected override void DrawPage(AutoVertLayoutGroup page)
        {
            var groups = string.IsNullOrEmpty(_culture) ? _groups : _groups.Where(group => group.Key == _culture).ToList();
            List<Empire> shown = groups.SelectMany(group => group).ToList();
            List<Empire> living = shown.Where(empire => !empire.IsArchived()).ToList();
            string title = _filter switch
            {
                Filter.Alive => LM.Get("empire_list_current"),
                Filter.Archived => LM.Get("empire_list_archived"),
                _ => LM.Get("empire_list_window")
            };
            if (!string.IsNullOrEmpty(_culture)) title = GetCultureDisplayName(_culture) + " · " + title;
            page.AddPageHeader(PageWidth, title, string.Format(LM.Get("empire_list_chip_count"), shown.Count));

            if (shown.Count == 0)
            {
                bool archivedOnly = _filter == Filter.Archived;
                page.AddEmptyCard(PageWidth, archivedOnly ? "empire_list_empty_archived" : "empire_list_empty",
                    archivedOnly ? null : LM.Get("empire_list_empty_hint"));
                return;
            }
            if (living.Count > 0)
            {
                Empire oldest = living.OrderByDescending(Years).First();
                page.AddKpiRow(PageWidth,
                    (LM.Get("empire_list_kpi_cities"), living.Sum(empire => empire.countCities()).ToString(), WideKit.Green),
                    (LM.Get("empire_list_kpi_population"),
                        UIHelper.ShortCount(living.Sum(empire => (double)empire.CountPopulation())), WideKit.Cyan),
                    (LM.Get("empire_list_kpi_oldest"), $"{Years(oldest)}{LM.Get("Year")}", WideKit.Gold));
            }

            foreach (var group in groups)
            {
                page.AddSectionHeader(PageWidth, $"{GetCultureDisplayName(group.Key)}  ×{group.Count()}", WideKit.Cyan, false,
                    "ui/icons/cultures/" + group.Key);
                List<Empire> current = group.Where(empire => !empire.IsArchived())
                    .OrderByDescending(empire => empire.countCities()).ToList();
                page.AddCardGrid(PageWidth, current, 2, AddAliveCard);
                List<Empire> fallen = group.Where(empire => empire.IsArchived())
                    .OrderByDescending(empire => empire.data.timestamp_established_time).ToList();
                if (fallen.Count > 0) AddArchivedCard(page, fallen);
            }
        }

        // 现存帝国卡：国色条 → 国号(国色)与政体 → 元首头像、称号姓名、年号在位 → 城/人口/正统/立国
        private void AddAliveCard(AutoHoriLayoutGroup row, float width, Empire empire)
        {
            var card = row.BeginColumnCard(width);
            card.AddColorStrip(width, empire.getColor()?.getColorMain() ?? Color.gray);
            UIHelper.AddCardTitle(card, width, empire.GetEmpireFullName(),
                WideKit.Hex(empire.getColor()?._color_text ?? Color.white), RegimeLabel(empire), 0f);

            bool republic = RepublicSystem.IsRepublic(empire);
            Actor head = empire.Emperor;
            bool living = head != null && head.isAlive();
            string reign = null;
            if (living)
            {
                reign = string.Format(LM.Get("empire_list_reign"), empire.GetEmperorYear());
                if (!republic && !string.IsNullOrWhiteSpace(empire.data.year_name))
                    reign = $"{LM.Get("year_name")} {empire.data.year_name} · {reign}";
            }
            card.AddPersonRow(width, living ? head : null,
                republic ? RepublicSystem.GetHeadOfStateTitle(empire) : LM.Get("emperor"),
                living ? head.getName() : LM.Get("label_none"), reign);

            int legitimacy = empire.Legitimacy;
            card.AddMetricRow(width,
                (LM.Get("empire_list_metric_cities"), empire.countCities().ToString(), WideKit.Green),
                (LM.Get("empire_list_metric_population"), UIHelper.ShortCount(empire.CountPopulation()), WideKit.Cyan),
                (LM.Get("empire_list_metric_legitimacy"), legitimacy.ToString(), WideKit.Grade(legitimacy, 70, 40)),
                (LM.Get("empire_list_metric_years"), $"{Years(empire)}{LM.Get("Year")}", WideKit.Value));
            UIHelper.MakeClickable(card.gameObject, () => OpenEmpire(empire));
        }

        // 已亡帝国：一张卡，一行一国，点开史书
        private void AddArchivedCard(AutoVertLayoutGroup page, List<Empire> fallen)
        {
            var card = page.BeginCard(PageWidth, "empire_list_archived", WideKit.Muted, fallen.Count.ToString(),
                IconPath + "filter_archived");
            Sprite tablet = Icon("filter_archived");
            for (int i = 0; i < fallen.Count; i++)
            {
                Empire empire = fallen[i];
                EmpireCraftHistory last = GetRepresentativeHistory(empire);
                string founded = empire.data.timestamp_established_time > 0
                    ? HistoryDateFormatter.GetYear(empire.data.timestamp_established_time) : "";
                string line = $"{founded} · {LM.Get("empire_core_history_duration")} {GetEmpireRecordedDuration(empire)}{LM.Get("Year")}";
                if (!string.IsNullOrWhiteSpace(last?.emperor))
                    line += $" · {LM.Get("empire_list_last_ruler")} {last.emperor}";
                card.AddListRow(PageWidth, i, tablet, empire.GetEmpireFullName(), line, () => OpenEmpireHistory(empire));
            }
        }

        private static Sprite Icon(string name) => SpriteTextureLoader.getSprite(IconPath + name);

        private static string RegimeLabel(Empire empire)
        {
            if (WarlordEraSystem.IsProvisionalGovernment(empire)) return LM.Get("empire_list_regime_provisional");
            if (RepublicSystem.IsRepublic(empire)) return LM.Get("empire_list_regime_republic");
            if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.Modern) return LM.Get("empire_list_regime_modern");
            return LM.Get("empire_list_regime_monarchy");
        }

        // ───────── 数据 ─────────
        private static int Years(Empire empire) => empire.data.timestamp_established_time > 0
            ? Mathf.Max(1, Date.getYearsSince(empire.data.timestamp_established_time) + 1) : 1;

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
