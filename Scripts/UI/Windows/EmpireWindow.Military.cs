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
            // 组建新军(见 CorpsSystem.RaiseManual)：每国一行，点驻地就在那里组建一军
            foreach (Kingdom kingdom in realms) AddRaiseRow(table, inner, kingdom, realms.Count > 1);
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

            AddNavyCard(parent, realms, mainWidth);

            var hint = parent.AddTextIntoVertLayout(LM.Get("corps_rules_hint").ColorString("#8FA0A8"), true,
                TextAnchor.UpperLeft, new Vector2(mainWidth - 4f, 30f));
            hint.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        }

        // 列宽：舰队 | 舰种 | 战舰+运输 | 组织度 | 任务
        private static readonly float[] NavyColumns = { 0.26f, 0.18f, 0.18f, 0.18f, 0.2f };

        // 海军卡(见 NavySystem)：各舰队、制海海区数、被封锁的港口；没有港口也没有舰队的内陆国不画
        private static void AddNavyCard(AutoVertLayoutGroup parent, List<Kingdom> realms, float width)
        {
            var fleets = realms.SelectMany(kingdom => NavySystem.Of(kingdom).Select(fleet => (kingdom, fleet)))
                .Where(entry => entry.fleet.warships + entry.fleet.transports > 0).ToList();
            var ports = realms.SelectMany(NavySystem.Ports).ToList();
            if (fleets.Count == 0 && ports.Count == 0) return;
            int controlled = SeaRegionSystem.All.Count(region => NavySystem.Controller(region) is Kingdom owner &&
                                                                 realms.Contains(owner));
            var card = parent.BeginCard(width, "navy_card_title", "#7FD8EA",
                string.Format(LM.Get("navy_card_header"), fleets.Count, fleets.Sum(entry => entry.fleet.warships),
                    fleets.Sum(entry => entry.fleet.transports), controlled,
                    realms.Count > 0 ? Mathf.RoundToInt(StraitReach.Radius(realms[0])) : 0), "ui/icons/iconBoat");
            float inner = width - 12f;
            float W(int i) => inner * NavyColumns[i] - 2f;
            var header = card.BeginHoriGroup(new Vector2(inner, 10f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            string[] keys = { "navy_col_fleet", "navy_col_tier", "navy_col_ships", "corps_col_org", "corps_col_task" };
            for (int i = 0; i < keys.Length; i++)
                header.AddTextIntoHoriLayout(LM.Get(keys[i]).ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                    new Vector2(W(i), 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            foreach ((Kingdom kingdom, FleetData fleet) in fleets.OrderByDescending(entry => entry.fleet.warships))
            {
                var row = card.BeginHoriGroup(new Vector2(inner, 12f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
                string name = NavySystem.Name(fleet);
                if (realms.Count > 1) name = $"{kingdom.GetKingdomName()} · {name}";
                var nameText = row.AddTextIntoHoriLayout(name, true, TextAnchor.MiddleLeft, new Vector2(W(0), 12f));
                nameText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
                HoverMarqueeText.Attach(nameText);
                row.AddTextIntoHoriLayout(LM.Get(NavySystem.TierKey(kingdom)).ColorString("#F3C34A"), true,
                    TextAnchor.MiddleLeft, new Vector2(W(1), 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
                row.AddTextIntoHoriLayout($"{fleet.warships}" + $" + {fleet.transports}".ColorString("#C8C2B4"), true,
                    TextAnchor.MiddleLeft, new Vector2(W(2), 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
                var org = row.BeginHoriGroup(new Vector2(W(3), 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
                UIHelper.AddShareBar(org.transform, new Vector2(W(3) * 0.6f, 5f), fleet.org / 100f,
                    fleet.org < 30f ? OrgLowColor : OrgColor);
                org.AddTextIntoHoriLayout(Mathf.RoundToInt(fleet.org).ToString(), true, TextAnchor.MiddleLeft,
                    new Vector2(W(3) * 0.4f - 3f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
                string hex = fleet.task switch
                {
                    "escort" or "blockade" => "#FFB3A8",
                    "patrol" => "#A8CCFF",
                    "retreat" => "#E8584F",
                    _ => "#C9E6A8"
                };
                row.AddTextIntoHoriLayout(LM.Get("navy_task_" + fleet.task).ColorString(hex), true, TextAnchor.MiddleLeft,
                    new Vector2(W(4), 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            }
            var blockaded = ports.Where(NavySystem.IsBlockaded).Select(city => city.GetCityName()).ToList();
            if (blockaded.Count > 0)
                card.AddTextIntoVertLayout(string.Format(LM.Get("navy_blockaded"), string.Join("、", blockaded)).ColorString("#E8584F"),
                    true, TextAnchor.MiddleLeft, new Vector2(inner, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            card.AddTextIntoVertLayout(LM.Get("navy_rules_hint").ColorString("#8FA0A8"), true, TextAnchor.UpperLeft,
                new Vector2(inner, 26f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
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
        // 军号 | 兵种(可换装时带"换装"按钮) | 驻地 | 兵力 | 组织度 | 任务
        private static readonly float[] CorpsColumns = { 0.15f, 0.19f, 0.13f, 0.21f, 0.13f, 0.19f };

        private static void AddCorpsHeader(AutoVertLayoutGroup card, float width)
        {
            var row = card.BeginHoriGroup(new Vector2(width, 10f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            string[] keys = { "corps_col_name", "corps_col_arm", "corps_col_home", "corps_col_strength", "corps_col_org", "corps_col_task" };
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
            AddArmCell(row, W(1), corps);
            row.AddTextIntoHoriLayout((CorpsSystem.Home(corps)?.GetCityName() ?? "—").ColorString("#C8C2B4"), true,
                TextAnchor.MiddleLeft, new Vector2(W(2), 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var strength = row.BeginHoriGroup(new Vector2(W(3), 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            UIHelper.AddShareBar(strength.transform, new Vector2(W(3) * 0.45f, 5f),
                corps.full <= 0f ? 0f : corps.strength / corps.full, StrengthColor);
            strength.AddTextIntoHoriLayout($"{CorpsSystem.ShortStrength(corps.strength)}/{CorpsSystem.ShortStrength(corps.full)}",
                true, TextAnchor.MiddleLeft, new Vector2(W(3) * 0.55f - 3f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            var org = row.BeginHoriGroup(new Vector2(W(4), 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            UIHelper.AddShareBar(org.transform, new Vector2(W(4) * 0.6f, 5f), corps.org / 100f,
                corps.org < 30f ? OrgLowColor : OrgColor);
            org.AddTextIntoHoriLayout(Mathf.RoundToInt(corps.org).ToString(), true, TextAnchor.MiddleLeft,
                new Vector2(W(4) * 0.4f - 3f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            if (!corps.manual)
            {
                row.AddTextIntoHoriLayout(TaskText(corps), true, TextAnchor.MiddleLeft, new Vector2(W(5), 12f))
                    .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
                return;
            }
            // 手动组建的军：任务后带"解散"按钮(交战中的不能解散)
            const float DisbandWidth = 22f;
            var cell = row.BeginHoriGroup(new Vector2(W(5), 12f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            var task = cell.AddTextIntoHoriLayout(TaskText(corps), true, TextAnchor.MiddleLeft, new Vector2(W(5) - DisbandWidth - 2f, 12f));
            task.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            Kingdom owner = World.world.kingdoms.get(corps.kingdom_id);
            AdvancedButton disband = null;
            disband = cell.AddButtonIntoHoriLayout($"corps_disband_{corps.id}", LM.Get("corps_manual_disband"), () =>
            {
                if (!CorpsSystem.DisbandManual(owner, corps) || disband == null) return;
                task.text.text = LM.Get("corps_manual_disbanded").ColorString("#8FA0A8");
                disband.gameObject.SetActive(false);
            }, size: new Vector2(DisbandWidth, 10f));
            if (corps.task is "attack" or "defend") disband.Button.interactable = false;
        }

        private const int RaiseSeats = 5;

        // 组建新军一行：国名(多国时)、几座兵源最多的城做按钮；组建后按钮改字、不再可点
        private static void AddRaiseRow(AutoVertLayoutGroup card, float width, Kingdom kingdom, bool named)
        {
            List<City> seats = CorpsSystem.ManualSeats(kingdom, RaiseSeats);
            if (seats.Count == 0) return;
            var row = card.BeginHoriGroup(new Vector2(width, 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 2, 0));
            string label = string.Format(LM.Get(named ? "corps_manual_row_named" : "corps_manual_row"), kingdom.GetKingdomName(),
                CorpsSystem.ManualCount(kingdom), CorpsSystem.ManualLimit(kingdom));
            row.AddTextIntoHoriLayout(label.ColorString("#E6DCA8"), true, TextAnchor.MiddleLeft, new Vector2(width * 0.3f, 12f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            float buttonWidth = Mathf.Min(64f, (width * 0.7f - 3f * seats.Count) / seats.Count);
            foreach (City city in seats)
            {
                City seat = city;
                string blocker = CorpsSystem.ManualBlocker(kingdom, seat);
                AdvancedButton button = null;
                button = row.AddButtonIntoHoriLayout($"corps_raise_{kingdom.id}_{seat.id}", seat.GetCityName(), () =>
                {
                    CorpsData raised = CorpsSystem.RaiseManual(kingdom, seat);
                    if (raised == null || button == null) return;
                    button.Text.text = string.Format(LM.Get("corps_manual_done"), CorpsSystem.Name(raised));
                    button.Button.interactable = false;
                }, size: new Vector2(buttonWidth, 10f));
                if (blocker != null) button.Button.interactable = false;
                UIHelper.AttachTextTooltip(button.gameObject, $"corps_raise_{kingdom.id}_{seat.id}", LM.Get("corps_manual_title"),
                    string.Format(LM.Get("corps_manual_tip"), seat.GetCityName(),
                        CorpsSystem.ShortStrength(CorpsSystem.ManualSize(kingdom, seat)),
                        MoneyDisplay.Format(CorpsSystem.ManualCost(kingdom, seat)), MoneyDisplay.Format(kingdom.GetMoney())) +
                    (blocker == null ? "" : "\n" + LM.Get(blocker).ColorString("#E8584F")));
            }
        }

        // 兵种格：兵种名；本文化有更新的装备时带"换装"按钮(花本国国库，见 CorpsArmSystem.Refit)，换完就地改字、按钮消失
        private static void AddArmCell(AutoHoriLayoutGroup row, float width, CorpsData corps)
        {
            var cell = row.BeginHoriGroup(new Vector2(width, 12f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            bool refit = CorpsArmSystem.CanRefit(corps);
            float buttonWidth = refit ? 22f : 0f;
            var text = cell.AddTextIntoHoriLayout(CorpsArmSystem.Name(CorpsArmSystem.Of(corps)).ColorString("#E6DCA8"), true,
                TextAnchor.MiddleLeft, new Vector2(width - buttonWidth - (refit ? 2f : 0f), 12f));
            text.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            if (!refit) return;
            Kingdom kingdom = World.world.kingdoms.get(corps.kingdom_id);
            int cost = CorpsArmSystem.RefitCost(corps);
            string target = CorpsArmSystem.Name(CorpsArmSystem.UpgradeOf(corps));
            AdvancedButton button = null;
            button = cell.AddButtonIntoHoriLayout($"corps_refit_{corps.id}", LM.Get("corps_refit"), () =>
            {
                if (!CorpsArmSystem.Refit(kingdom, corps)) return;
                text.text.text = CorpsArmSystem.Name(CorpsArmSystem.Of(corps)).ColorString("#E6DCA8");
                if (button != null) button.gameObject.SetActive(false);
            }, size: new Vector2(buttonWidth, 10f));
            UIHelper.AttachTextTooltip(button.gameObject, $"corps_refit_{corps.id}", LM.Get("corps_refit"),
                string.Format(LM.Get("corps_refit_tip"), target, MoneyDisplay.Format(cost),
                    MoneyDisplay.Format(kingdom?.GetMoney() ?? 0)));
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
