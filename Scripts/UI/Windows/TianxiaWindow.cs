using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 天下窗口(见 TianxiaSystem、Layer/TianxiaMap)，「卷册」宽窗口(用户 2026-10-19："太丑了，能不能用卡片展示天下和法理并且给点信息")：
    //   · 左栏：天下名片(名号、标识、天命一句、改名) → 天下一览(各天下一行，城数，点了换看) → 规模(法理、城、诸家、历代共主)；
    //   · 右栏：页头(名号 · 天命周期) → 主指标(共主、据有、城数、周期) → 天命卡(共主君主头像、正统、开国气象、动荡缘由、官学、规则)
    //     → 天下诸家(两栏卡片：国色条、国号与共主标记、君主、城数/据有/国力/正统，点开帝国或国情窗口)
    //     → 构成法理(两栏卡片：法理名、所属帝国、城数/法理数/文化/立核，点开帝国法理窗口) → 来朝诸国 → 历代共主。
    public class TianxiaWindow : WideScrollWindow<TianxiaWindow>
    {
        private const int MaxHouses = 8, MaxRailTianxia = 16, MaxHistory = 20;

        private static EmpireCore _selected;
        private Dictionary<Kingdom, int> _held = new();
        private int _total;
        private Kingdom _holder;
        private List<EmpireCore> _leads = new();

        public static void Open(EmpireCore core)
        {
            _selected = TianxiaSystem.Lead(core);
            if (_selected == null) return;
            ScrollWindow.showWindow(nameof(TianxiaWindow));
        }

        protected override void Prepare()
        {
            _leads = TianxiaSystem.Leads().OrderByDescending(lead => TianxiaSystem.Cities(lead).Count).ToList();
            _selected = TianxiaSystem.Lead(_selected);
            if (_selected == null || !EmpireCoreManager.EmpireCores.ContainsKey(_selected.id)) _selected = _leads.FirstOrDefault();
            if (_selected == null) return;
            (_held, _total) = TianxiaSystem.Holdings(_selected);
            _holder = TianxiaSystem.Holder(_selected);
        }

        private void Select(EmpireCore lead)
        {
            _selected = lead;
            Refresh(resetScroll: true);
        }

        // ───────── 左栏 ─────────
        protected override void DrawRail(AutoVertLayoutGroup rail)
        {
            if (_selected == null)
            {
                rail.AddRailTitle(LM.Get("tianxia_label"));
                return;
            }
            EmpireCore lead = _selected;
            var identity = rail.BeginRailCard("FactionFrame_dominate");
            identity.AddRailText(TianxiaSystem.Name(lead).ColorString(TianxiaMap.HexOf(lead)), 10, 14f);
            string id = TianxiaSystem.TianxiaId(lead);
            if (id != "") identity.AddRailText(string.Format(LM.Get("tianxia_id_label"), id).ColorString(WideKit.Muted), 6, 9f);
            identity.AddRailText(TianxiaSystem.MandateText(lead).ColorString("#FF8A7A"), 6, 9f);
            identity.AddRailInput(LM.Get("tianxia_name_input"), TianxiaSystem.Name(lead), value =>
            {
                TianxiaSystem.Rename(lead, value);
                Refresh();
            });
            // 天下基名：普世之名、唯一合法政府国号所用(见 RealmNamingSystem)
            identity.AddRailInput(LM.Get("tianxia_base_input"), RealmNamingSystem.TianxiaBaseName(lead), value =>
            {
                RealmNamingSystem.SetTianxiaBase(lead, value);
                Refresh();
            });

            var menu = rail.BeginRailMenu(LM.Get("tianxia_section_all"));
            int index = 0;
            foreach (EmpireCore other in _leads.Take(MaxRailTianxia))
            {
                EmpireCore target = other;
                menu.AddRailOption(null, TianxiaSystem.Name(other).ColorString(TianxiaMap.HexOf(other)),
                    TianxiaSystem.Cities(other).Count.ToString(), other == lead, () => Select(target), index++);
            }

            var stats = rail.BeginRailCard();
            float rowWidth = UIHelper.RailWidth - 8f;
            stats.AddRailRow(rowWidth, LM.Get("tianxia_cores_count"), TianxiaSystem.Members(lead).Count.ToString(), WideKit.Cyan, 0);
            stats.AddRailRow(rowWidth, LM.Get("empire_core_cities_count"), _total.ToString(), WideKit.Green, 1);
            stats.AddRailRow(rowWidth, LM.Get("tianxia_houses_count"), _held.Count.ToString(), WideKit.Orange, 2);
            stats.AddRailRow(rowWidth, LM.Get("tianxia_dynasties_count"), (lead.mandate_history?.Count ?? 0).ToString(), WideKit.Value, 3);
        }

        // ───────── 右栏 ─────────
        protected override void DrawPage(AutoVertLayoutGroup page)
        {
            if (_selected == null)
            {
                page.AddEmptyCard(PageWidth, LM.Get("tianxia_label"), LM.Get("tianxia_none_hint"));
                return;
            }
            EmpireCore lead = _selected;
            int phase = TianxiaSystem.Phase(lead);
            page.AddPageHeader(PageWidth, TianxiaSystem.Name(lead), TianxiaSystem.PhaseName(phase));

            float share = _holder != null && _total > 0 && _held.TryGetValue(_holder, out int holderCities)
                ? holderCities / (float)_total : 0f;
            page.AddKpiRow(PageWidth,
                (LM.Get("tianxia_kpi_holder"), _holder != null ? _holder.GetKingdomName() : LM.Get("tianxia_vacant_short"),
                    _holder != null ? WideKit.Gold : WideKit.Muted),
                (LM.Get("tianxia_kpi_share"), _holder != null ? $"{share:P0}" : "—", WideKit.Grade(share, TianxiaSystem.ClaimShare, TianxiaSystem.KeepShare)),
                (LM.Get("empire_core_cities_count"), _total.ToString(), WideKit.Green),
                (LM.Get("tianxia_phase_label"), TianxiaSystem.PhaseName(phase), PhaseHex(phase)));

            DrawMandate(page, lead, phase);
            DrawHouses(page);
            DrawCores(page, lead);
            DrawTributaries(page, lead);
            DrawHistory(page, lead);
        }

        private static string PhaseHex(int phase) => phase switch
        {
            TianxiaSystem.Peace => WideKit.Green,
            TianxiaSystem.Unrest => WideKit.Orange,
            TianxiaSystem.Chaos => WideKit.Red,
            _ => WideKit.Muted
        };

        private void DrawMandate(AutoVertLayoutGroup page, EmpireCore lead, int phase)
        {
            var card = page.BeginCard(PageWidth, LM.Get("tianxia_mandate_short"), WideKit.Gold, TianxiaSystem.PhaseName(phase));
            Empire empire = _holder?.GetEmpire();
            if (_holder != null)
            {
                Actor ruler = _holder.king;
                bool living = ruler != null && ruler.isAlive();
                card.AddPersonRow(PageWidth, living ? ruler : null, LM.Get("tianxia_holder_mark"),
                    empire != null ? empire.GetEmpireFullName() : _holder.GetKingdomName(),
                    living ? ruler.getName() : null);
                int grace = UnificationSystem.GraceYearsLeft(_holder);
                card.AddMetricRow(PageWidth,
                    (LM.Get("empire_list_metric_legitimacy"), empire != null ? empire.Legitimacy.ToString() : "—",
                        empire != null ? WideKit.Grade(empire.Legitimacy, 70, 30) : WideKit.Muted),
                    (LM.Get("tianxia_metric_pressure"), empire != null ? $"{DynasticCycleSystem.GetPressure(empire):0}" : "—",
                        empire != null ? WideKit.Grade(DynasticCycleSystem.GetPressure(empire), 30, 60, false) : WideKit.Muted),
                    (LM.Get("tianxia_metric_grace"), grace >= 0 ? string.Format(LM.Get("tianxia_years_value"), grace) : "—",
                        grace >= 0 ? WideKit.Green : WideKit.Muted),
                    (LM.Get("tianxia_metric_tributaries"), TianxiaSystem.Tributaries(lead).Count.ToString(), WideKit.Cyan));
                if (phase == TianxiaSystem.Unrest && TianxiaSystem.ShakenReasons(lead) is List<string> reasons && reasons.Count > 0)
                    Note(card, string.Format(LM.Get("tianxia_shaken_reasons"), string.Join("、", reasons)), WideKit.Orange);
                PartyIdeology? school = CliqueSystem.OfficialSchool(empire);
                if (school != null)
                    Note(card, string.Format(LM.Get("tianxia_official_school"), PartySystem.GetIdeologyName(school.Value)), WideKit.Cyan);
            }
            else if (phase == TianxiaSystem.Minor)
                Note(card, string.Format(LM.Get("tianxia_minor_hint"), TianxiaSystem.MinMandateCities), WideKit.Muted);
            else
                Note(card, TianxiaSystem.MandateText(lead), WideKit.Muted);
            Note(card, string.Format(LM.Get("tianxia_rule_hint"), $"{TianxiaSystem.ClaimShare:P0}", $"{TianxiaSystem.SealClaimShare:P0}",
                $"{TianxiaSystem.KeepShare:P0}", TianxiaSystem.MinMandateCities), WideKit.Muted);
        }

        // 天下诸家：两栏卡片
        private void DrawHouses(AutoVertLayoutGroup page)
        {
            page.AddSectionHeader(PageWidth, string.Format(LM.Get("tianxia_section_houses_count"), _held.Count), WideKit.Cyan, false);
            List<KeyValuePair<Kingdom, int>> houses = _held.OrderByDescending(pair => pair.Value).Take(MaxHouses).ToList();
            if (houses.Count == 0)
            {
                page.AddEmptyCard(PageWidth, LM.Get("empire_core_none"));
                return;
            }
            page.AddCardGrid(PageWidth, houses, 2, (row, width, pair) =>
            {
                Kingdom kingdom = pair.Key;
                Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
                var card = row.BeginColumnCard(width);
                card.AddColorStrip(width, kingdom.getColor()?.getColorMain() ?? Color.gray);
                string name = empire != null ? empire.GetEmpireFullName() : kingdom.GetKingdomName();
                UIHelper.AddCardTitle(card, width, name, WideKit.Hex(kingdom.getColor()?._color_text ?? Color.white),
                    kingdom == _holder ? LM.Get("tianxia_holder_mark") : "", 0f);
                Actor ruler = kingdom.king;
                bool living = ruler != null && ruler.isAlive();
                card.AddPersonRow(width, living ? ruler : null, LM.Get(empire != null ? "tianxia_house_emperor" : "tianxia_house_king"),
                    living ? ruler.getName() : LM.Get("label_none"));
                float share = _total > 0 ? pair.Value / (float)_total : 0f;
                card.AddMetricRow(width,
                    (LM.Get("empire_list_metric_cities"), pair.Value.ToString(), WideKit.Green),
                    (LM.Get("tianxia_kpi_share"), $"{share:P0}", WideKit.Grade(share, TianxiaSystem.ClaimShare, 0.3f)),
                    (LM.Get("tianxia_metric_power"), UIHelper.ShortCount(WarSituation.Power(kingdom)), WideKit.Orange));
                UIHelper.MakeClickable(card.gameObject, () => OpenHouse(kingdom));
            });
            if (_held.Count > MaxHouses)
                Note(page, string.Format(LM.Get("tianxia_more_houses"), _held.Count - MaxHouses), WideKit.Muted);
        }

        // 构成：各帝国法理一张卡
        private void DrawCores(AutoVertLayoutGroup page, EmpireCore lead)
        {
            List<EmpireCore> cores = TianxiaSystem.Members(lead);
            page.AddSectionHeader(PageWidth, string.Format(LM.Get("tianxia_section_cores_count"), cores.Count), WideKit.Cyan, false);
            page.AddCardGrid(PageWidth, cores, 2, (row, width, core) =>
            {
                Empire owner = EmpireCoreManager.GetEmpires(core).FirstOrDefault(empire => !empire.IsArchived());
                var card = row.BeginColumnCard(width);
                card.AddColorStrip(width, owner?.getColor()?.getColorMain() ?? Color.gray);
                UIHelper.AddCardTitle(card, width, EmpireCoreManager.GetStatusDisplayName(core), WideKit.Gold,
                    core == lead && cores.Count > 1 ? LM.Get("tianxia_core_main") : "", 0f);
                string culture = CultureService.IsValidCulture(core.default_culture) ? core.default_culture.GetCultureTranslate() : "—";
                City capital = core.GetCoreCapital();
                Note(card, string.Format(LM.Get("tianxia_core_owner"),
                    owner != null ? owner.GetEmpireFullName() : LM.Get("tianxia_core_unclaimed"),
                    capital?.data != null && !capital.isRekt() ? capital.GetCityName() : "—"), WideKit.Soft, width);
                card.AddMetricRow(width,
                    (LM.Get("empire_list_metric_cities"), EmpireCoreManager.GetCities(core).Count.ToString(), WideKit.Green),
                    (LM.Get("tianxia_metric_titles"), EmpireCoreManager.GetTitles(core).Count.ToString(), WideKit.Cyan),
                    (LM.Get("tianxia_metric_culture"), culture, WideKit.Value),
                    (LM.Get("tianxia_metric_founded"), HistoryDateFormatter.GetYear(core.create_timestamp), WideKit.Muted));
                EmpireCore target = core;
                UIHelper.MakeClickable(card.gameObject, () =>
                {
                    EmpireCraftMetaTypeLibrary.selected_empireCore = target;
                    ScrollWindow.showWindow(nameof(EmpireCoreWindow));
                });
            });
        }

        private void DrawTributaries(AutoVertLayoutGroup page, EmpireCore lead)
        {
            List<Kingdom> tributaries = TianxiaSystem.Tributaries(lead);
            if (tributaries.Count == 0) return;
            var card = page.BeginCard(PageWidth, LM.Get("tianxia_section_tributaries"), WideKit.Cyan, tributaries.Count.ToString());
            int index = 0;
            foreach (Kingdom kingdom in tributaries)
            {
                Kingdom target = kingdom;
                card.AddListRow(PageWidth, index++, null, kingdom.GetKingdomName(),
                    string.Format(LM.Get("tianxia_tributary_detail"), kingdom.cities?.Count ?? 0), () => OpenHouse(target),
                    WideKit.Hex(kingdom.getColor()?._color_text ?? Color.white));
            }
        }

        private void DrawHistory(AutoVertLayoutGroup page, EmpireCore lead)
        {
            List<MandateRecord> history = lead.mandate_history ?? new List<MandateRecord>();
            var card = page.BeginCard(PageWidth, LM.Get("tianxia_section_history"), WideKit.Gold, history.Count.ToString());
            if (history.Count == 0)
            {
                Note(card, LM.Get("tianxia_mandate_never"), WideKit.Muted);
                return;
            }
            int index = 0;
            foreach (MandateRecord record in Enumerable.Reverse(history).Take(MaxHistory))
            {
                string to = record.lost_at < 0d ? LM.Get("tianxia_until_now") : HistoryDateFormatter.GetYear(record.lost_at);
                string from = HistoryDateFormatter.GetYear(record.claimed_at);
                string name = string.IsNullOrWhiteSpace(record.dynasty) ? record.kingdom_name : $"{record.kingdom_name}({record.dynasty})";
                card.AddListRow(PageWidth, index++, null, name, $"{from} — {to}", null,
                    record.lost_at < 0d ? WideKit.Gold : WideKit.Soft, 120f);
            }
        }

        private static void OpenHouse(Kingdom kingdom)
        {
            if (kingdom?.data == null || kingdom.isRekt()) return;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom != null && !empire.isRekt())
            {
                SelectedMetas.selected_kingdom = empire.CoreKingdom;
                EmpireCraftMetaTypeLibrary.selected_empire = empire;
                ScrollWindow.showWindow(nameof(EmpireWindow));
                return;
            }
            KingdomOverviewWindow.Open(kingdom);
        }

        // 卡片里一段灰字说明(自动换行)
        private static void Note(AutoVertLayoutGroup card, string text, string hex, float width = PageWidth)
        {
            SimpleText label = card.AddTextIntoVertLayout(text.ColorString(hex), true, TextAnchor.UpperLeft,
                new Vector2(width - 12f, 11f));
            label.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
            label.RefreshAutoHeight(11f, 2f);
        }
    }
}
