using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 军团页：帝国各国的军团(兵力、组织度、任务)、正在交战的战线与军团纪事(见 CorpsSystem、FrontSystem)
    public partial class EmpireWindow
    {
        private static readonly Color StrengthColor = new Color32(101, 214, 110, 255);
        private static readonly Color OrgColor = new Color32(127, 216, 234, 255);
        private static readonly Color OrgLowColor = new Color32(232, 88, 79, 255);

        // 点地图兵牌/交战标记打开军团页时要突出的军与城；窗口关上时清掉
        private static HashSet<long> _militaryFocusCorps;
        private static long _militaryFocusCity = -1L;

        // 直接打开某帝国的军团页(地图兵牌、交战标记点击时用)
        public static void OpenMilitary(Empire empire, IEnumerable<long> focusCorps, long focusCity)
        {
            if (empire?.CoreKingdom == null || empire.isRekt()) return;
            SelectedMetas.selected_kingdom = empire.CoreKingdom;
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            _militaryFocusCorps = focusCorps == null ? null : new HashSet<long>(focusCorps);
            _militaryFocusCity = focusCity;
            _pendingTab = "empire_military";
            ScrollWindow.showWindow(nameof(EmpireWindow));
        }

        private static void ClearMilitaryFocus()
        {
            _militaryFocusCorps = null;
            _militaryFocusCity = -1L;
        }

        public void ShowMilitary(WindowMetaTab pArg0)
        {
            Clear();
            Kingdom core = _empire?.CoreKingdom;
            if (core == null) return;
            if (!CorpsSystem.Enabled)
            {
                AddMilitaryDisabledHint(BeginPage("empire_military"), MainWidth);
                return;
            }
            var realms = _empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt())
                .OrderBy(kingdom => kingdom == core ? 0 : 1).ToList();
            var parent = BeginPage("empire_military", MilitaryChips(realms));
            BuildMilitaryPage(parent, realms, MainWidth, HalfWidth, _militaryFocusCorps, _militaryFocusCity);
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }

        internal static string[] MilitaryChips(List<Kingdom> realms)
        {
            var all = realms.SelectMany(CorpsSystem.Of).ToList();
            return new[]
            {
                string.Format(LM.Get("corps_chip_count"), all.Count),
                CorpsSystem.ShortStrength(all.Sum(corps => corps.strength))
            };
        }

        internal static void AddMilitaryDisabledHint(AutoVertLayoutGroup page, float width) =>
            page.AddTextIntoVertLayout(LM.Get("corps_disabled_hint").ColorString("#8FA0A8"), true,
                TextAnchor.UpperLeft, new Vector2(width - 4f, 20f)).UseFixedFontSize(7, HorizontalWrapMode.Wrap);

        // 军团页内容(帝国窗口与独立王国的军事窗口共用)：主指标 → 各国军团表 → 战线 | 军团纪事 → 规则说明。
        // focusCorps / focusCity：从地图点进来时要突出的军与交战城，所在国、那几支军、那处交战排到最前
        internal static void BuildMilitaryPage(AutoVertLayoutGroup parent, List<Kingdom> realms, float mainWidth,
            float halfWidth, HashSet<long> focusCorps = null, long focusCity = -1L)
        {
            if (focusCorps is { Count: > 0 })
                realms = realms.OrderBy(kingdom => CorpsSystem.Of(kingdom).Any(corps => focusCorps.Contains(corps.id)) ? 0 : 1)
                    .ToList();
            var all = realms.SelectMany(kingdom => CorpsSystem.Of(kingdom).Select(corps => (kingdom, corps))).ToList();
            float strength = all.Sum(entry => entry.corps.strength);
            var battles = FrontSystem.Battles.Values.Where(battle => realms.Any(kingdom =>
                kingdom.id == battle.attacker_id || kingdom.id == battle.defender_id)).ToList();

            var kpis = parent.BeginColumns(mainWidth);
            float kpiWidth = (mainWidth - 3 * UIHelper.CardGap) / 4f;
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("corps_kpi_count"), all.Count.ToString());
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("corps_kpi_strength"), CorpsSystem.ShortStrength(strength), "#65D66E");
            float org = all.Count == 0 ? 0f : all.Average(entry => entry.corps.org);
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("corps_kpi_org"), Mathf.RoundToInt(org).ToString(),
                org < 40f ? "#E8584F" : "#7FD8EA");
            UIHelper.AddKpiTile(kpis, kpiWidth, LM.Get("corps_kpi_battles"), battles.Count.ToString(),
                battles.Count > 0 ? "#E8584F" : "#8FA0A8");

            var table = parent.BeginCard(mainWidth, "corps_table_title", "#F3C34A",
                string.Format(LM.Get("corps_table_header"), all.Count(entry => entry.corps.general_id >= 0)),
                "ui/icons/iconWar");
            float inner = mainWidth - 12f;
            AddCorpsHeader(table, inner);
            foreach (Kingdom kingdom in realms)
            {
                List<CorpsData> list = CorpsSystem.Of(kingdom);
                if (list.Count == 0) continue;
                if (realms.Count > 1)
                    table.AddTextIntoVertLayout(kingdom.GetKingdomName().ColorString("#C8C2B4"), true, TextAnchor.MiddleLeft,
                        new Vector2(inner, 11f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
                foreach (CorpsData corps in list.OrderBy(corps => focusCorps?.Contains(corps.id) == true ? 0 : 1)
                             .ThenBy(corps => corps.militia).ThenBy(corps => corps.serial))
                    AddCorpsRow(table, inner, corps, focusCorps?.Contains(corps.id) == true);
            }
            if (all.Count == 0)
                table.AddTextIntoVertLayout(LM.Get("corps_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                    new Vector2(inner, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);

            var columns = parent.BeginColumns(mainWidth);
            var front = columns.BeginColumnCard(halfWidth, "corps_front_title", "#E8584F",
                string.Format(LM.Get("corps_front_header"), battles.Count), "ui/icons/iconWar");
            float half = halfWidth - 12f;
            foreach (FrontSystem.BattleView battle in battles.OrderBy(battle => battle.city_id == focusCity ? 0 : 1)
                         .ThenByDescending(battle => battle.attack + battle.defend))
                AddBattleRow(front, half, battle, realms, battle.city_id == focusCity);
            if (battles.Count == 0)
                front.AddTextIntoVertLayout(LM.Get("corps_front_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                    new Vector2(half, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);

            var log = columns.BeginColumnCard(halfWidth, "corps_log_title", "#7FD8EA", null, "ui/icons/iconBook");
            var entries = realms.SelectMany(FrontSystem.LogOf).OrderByDescending(entry => entry.time).Take(12).ToList();
            foreach ((double time, string text) in entries)
            {
                string when = Date.getYearsSince(time) <= 0 ? LM.Get("court_log_this_year")
                    : string.Format(LM.Get("court_log_years_ago"), Date.getYearsSince(time));
                log.AddTextIntoVertLayout($"{when.ColorString("#8FA0A8")} {text}", true, TextAnchor.UpperLeft,
                    new Vector2(half, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            }
            if (entries.Count == 0)
                log.AddTextIntoVertLayout(LM.Get("corps_log_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                    new Vector2(half, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);

            var hint = parent.AddTextIntoVertLayout(LM.Get("corps_rules_hint").ColorString("#8FA0A8"), true,
                TextAnchor.UpperLeft, new Vector2(mainWidth - 4f, 30f));
            hint.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        }

        // 从地图点进来的那几行垫一层淡金底
        private static void AddRowHighlight(Transform row)
        {
            var go = new GameObject("FocusHighlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(LayoutElement));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(row, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-2f, 0f);
            rect.offsetMax = new Vector2(2f, 0f);
            var image = go.GetComponent<Image>();
            image.color = new Color32(243, 195, 74, 46);
            image.raycastTarget = false;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            rect.SetAsFirstSibling();
        }

        // 列宽：番号 | 驻地 | 兵力 | 组织度 | 任务
        private static readonly float[] CorpsColumns = { 0.16f, 0.18f, 0.27f, 0.17f, 0.22f };

        private static void AddCorpsHeader(AutoVertLayoutGroup card, float width)
        {
            var row = card.BeginHoriGroup(new Vector2(width, 10f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            string[] keys = { "corps_col_name", "corps_col_home", "corps_col_strength", "corps_col_org", "corps_col_task" };
            for (int i = 0; i < keys.Length; i++)
                row.AddTextIntoHoriLayout(LM.Get(keys[i]).ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                    new Vector2(width * CorpsColumns[i] - 2f, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        }

        private static void AddCorpsRow(AutoVertLayoutGroup card, float width, CorpsData corps, bool focus = false)
        {
            var row = card.BeginHoriGroup(new Vector2(width, 12f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            if (focus) AddRowHighlight(row.transform);
            float W(int i) => width * CorpsColumns[i] - 2f;
            Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
            string name = CorpsSystem.Name(corps);
            if (focus) name = name.ColorString("#F3C34A");
            if (general?.data != null && !general.isRekt()) name += $" · {general.getName()}".ColorString("#C8C2B4");
            var nameText = row.AddTextIntoHoriLayout(name, true, TextAnchor.MiddleLeft, new Vector2(W(0), 12f));
            nameText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(nameText);
            row.AddTextIntoHoriLayout((CorpsSystem.Home(corps)?.GetCityName() ?? "—").ColorString("#C8C2B4"), true,
                TextAnchor.MiddleLeft, new Vector2(W(1), 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var strength = row.BeginHoriGroup(new Vector2(W(2), 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            UIHelper.AddShareBar(strength.transform, new Vector2(W(2) * 0.45f, 5f),
                corps.full <= 0f ? 0f : corps.strength / corps.full, StrengthColor);
            strength.AddTextIntoHoriLayout($"{CorpsSystem.ShortStrength(corps.strength)}/{CorpsSystem.ShortStrength(corps.full)}",
                true, TextAnchor.MiddleLeft, new Vector2(W(2) * 0.55f - 3f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var org = row.BeginHoriGroup(new Vector2(W(3), 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            UIHelper.AddShareBar(org.transform, new Vector2(W(3) * 0.6f, 5f), corps.org / 100f,
                corps.org < 30f ? OrgLowColor : OrgColor);
            org.AddTextIntoHoriLayout(Mathf.RoundToInt(corps.org).ToString(), true, TextAnchor.MiddleLeft,
                new Vector2(W(3) * 0.4f - 3f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            row.AddTextIntoHoriLayout(TaskText(corps), true, TextAnchor.MiddleLeft, new Vector2(W(4), 12f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        }

        private static string TaskText(CorpsData corps)
        {
            string hex = corps.task switch
            {
                "attack" => "#FFB3A8",
                "defend" => "#A8CCFF",
                "march" => "#E6DCA8",
                "retreat" => "#E8584F",
                _ => "#C9E6A8"
            };
            string text = LM.Get("corps_task_" + corps.task);
            City sector = corps.sector_city_id >= 0 ? World.world.cities.get(corps.sector_city_id) : null;
            if (sector?.data != null && !sector.isRekt()) text += " · " + sector.GetCityName();
            return text.ColorString(hex);
        }

        private static void AddBattleRow(AutoVertLayoutGroup card, float width, FrontSystem.BattleView battle,
            List<Kingdom> realms, bool focus = false)
        {
            City city = World.world.cities.get(battle.city_id);
            if (city?.data == null || city.isRekt()) return;
            Kingdom attacker = World.world.kingdoms.get(battle.attacker_id);
            Kingdom defender = World.world.kingdoms.get(battle.defender_id);
            bool ours = realms.Any(kingdom => kingdom.id == battle.attacker_id);
            string title = $"{city.GetCityName()}  {(attacker?.GetKingdomName() ?? "?").ColorString("#FFB3A8")} → " +
                           $"{(defender?.GetKingdomName() ?? "?").ColorString("#A8CCFF")}";
            var titleText = card.AddTextIntoVertLayout(title, true, TextAnchor.MiddleLeft, new Vector2(width, 10f));
            titleText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            if (focus) AddRowHighlight(titleText.transform);
            var row = card.BeginHoriGroup(new Vector2(width, 10f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            UIHelper.AddShareBar(row.transform, new Vector2(width * 0.4f, 5f), battle.ratio,
                ours ? StrengthColor : OrgLowColor);
            int occupied = city.zones?.Count(zone => FrontSystem.Hostile(city.kingdom, FrontSystem.Controller(zone))) ?? 0;
            string detail = string.Format(LM.Get("corps_battle_detail"), Mathf.RoundToInt(battle.ratio * 100f),
                CorpsSystem.ShortStrength(battle.attack), CorpsSystem.ShortStrength(battle.defend), occupied,
                city.zones?.Count ?? 0);
            row.AddTextIntoHoriLayout(detail.ColorString("#C8C2B4"), true, TextAnchor.MiddleLeft,
                new Vector2(width * 0.6f - 3f, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        }
    }
}
