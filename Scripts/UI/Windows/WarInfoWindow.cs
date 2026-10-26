using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 战争窗口(用户 2026-10-27 定稿示意图 Tasks/ui-mockups/war-wide.png ①~④)：原版战争链接、战争徽标、战争列表都打开这里
    // (见 WarWindowRedirectPatch)。
    //   · 左栏：战争名、类型与标签(夺城、统一天下……)、宣战年与第几年、攻守双方主国、四页目、原版战争窗口；
    //   · 概况：兵力、伤亡、失城、战局；战争目标(目标城逐座进度、主城池、拿到会怎样、此刻议和会怎样、强制停战)；两军对比；
    //   · 战线：这场仗双方交战的城(胜率、地形、城墙、登陆、本月推进)，海战；
    //   · 双方：参战国(怎么卷进来的、城、兵；中途退出的、亡于此战的)，双方主力军团；
    //   · 战报：宣战、参战退出、攻城失城、统帅阵亡、兵败、收场(见 WarChronicle)，按类筛。
    public class WarInfoWindow : WideScrollWindow<WarInfoWindow>
    {
        public enum Page { Overview, Fronts, Sides, Chronicle }

        private const int MaxTargets = 6, MaxBattles = 10, MaxCorps = 8, MaxParties = 10;
        private const float ColumnWidth = UIHelper.PageColumnWidth;

        private static War _pending;
        private static Page _pendingPage;
        private War _war;
        private Page _page;
        private int _logKind = -1;

        public static void Open(War war, Page page = Page.Overview)
        {
            if (!WarDisplay.Alive(war)) return;
            _pending = war;
            _pendingPage = page;
            ScrollWindow.showWindow(nameof(WarInfoWindow));
        }

        protected override void OnOpen()
        {
            if (_pending == null) return;
            if (_pending != _war) _logKind = -1;
            _war = _pending;
            _page = _pendingPage;
            _pending = null;
        }

        private void Go(Page page)
        {
            _page = page;
            Refresh(resetScroll: true);
        }

        private List<WarChronicleEntry> Log() => _war?.GetOrCreate().chronicle ?? new List<WarChronicleEntry>();

        // ───────── 左栏 ─────────
        protected override void DrawRail(AutoVertLayoutGroup rail)
        {
            if (!WarDisplay.Alive(_war))
            {
                rail.AddRailTitle(LM.Get("WarInfoWindow Title"));
                return;
            }
            War war = _war;
            float cardWidth = UIHelper.RailWidth + 4f;
            var head = rail.BeginRailCard("FactionFrame_dominate");
            head.AddRailText(war.data.name.ColorString(WideKit.Gold), 9, 24f);
            string kinds = WarCatalog.Kind(war).ColorString(WarCatalog.Color(war));
            foreach (string tag in WarCatalog.Tags(war)) kinds += " · " + tag.ColorString(WideKit.Cyan);
            head.AddRailText(kinds, 7, 10f);
            head.AddRailText((war.hasEnded()
                ? string.Format(LM.Get("war_rail_ended"), war.getYearStarted(), war.getYearEnded())
                : WarDisplay.Started(war)).ColorString(WideKit.Muted), 6, 9f);

            var sides = rail.BeginRailCard();
            AddSide(sides, cardWidth, war.main_attacker, true, WarDisplay.Side(war, true).Count);
            sides.AddRailText(LM.Get("war_rail_versus").ColorString(WideKit.Muted), 6, 9f);
            AddSide(sides, cardWidth, war.main_defender, false, WarDisplay.Side(war, false).Count);

            var menu = rail.BeginRailMenu(LM.Get("war_rail_group"));
            int index = 0;
            menu.AddRailOption(Icon("iconWar"), LM.Get("war_page_overview"), null, _page == Page.Overview, () => Go(Page.Overview), index++);
            menu.AddRailOption(Icon("iconArmy", "iconWar"), LM.Get("war_page_fronts"), WarDisplay.Battles(war).Count.ToString(),
                _page == Page.Fronts, () => Go(Page.Fronts), index++);
            menu.AddRailOption(Icon("iconKingdom"), LM.Get("war_page_sides"),
                (WarDisplay.Side(war, true).Count + WarDisplay.Side(war, false).Count).ToString(), _page == Page.Sides,
                () => Go(Page.Sides), index++);
            menu.AddRailOption(Icon("iconWorldLog", "iconClock"), LM.Get("war_page_chronicle"), Log().Count.ToString(),
                _page == Page.Chronicle, () => Go(Page.Chronicle), index++);

            rail.AddRailButton("war_open_vanilla", LM.Get("war_open_vanilla"), () => WarWindowRedirectPatch.OpenVanilla(war));
        }

        private static void AddSide(AutoVertLayoutGroup card, float cardWidth, Kingdom kingdom, bool attackers, int count)
        {
            string caption = string.Format(LM.Get(attackers ? "war_rail_attackers" : "war_rail_defenders"), count);
            card.AddRailText(caption.ColorString(WideKit.Muted), 6, 9f);
            string name = kingdom?.data != null ? kingdom.GetKingdomName() : "—";
            var row = card.AddListRow(cardWidth, 1, null, name, null, kingdom?.data != null ? () => OpenKingdom(kingdom) : null,
                attackers ? WarDisplay.AttackText : WarDisplay.DefendText, UIHelper.RailWidth - 14f);
        }

        private static Sprite Icon(params string[] names) => UIHelper.FirstSprite(names.Select(name => "ui/icons/" + name).ToArray());

        // ───────── 右栏 ─────────
        protected override void DrawPage(AutoVertLayoutGroup page)
        {
            if (!WarDisplay.Alive(_war))
            {
                page.AddEmptyCard(PageWidth, LM.Get("WarInfoWindow Title"), LM.Get("war_gone_hint"));
                return;
            }
            switch (_page)
            {
                case Page.Fronts: DrawFronts(page); break;
                case Page.Sides: DrawSides(page); break;
                case Page.Chronicle: DrawChronicle(page); break;
                default: DrawOverview(page); break;
            }
        }

        // 概况
        private void DrawOverview(AutoVertLayoutGroup page)
        {
            War war = _war;
            string chip = string.Join(" · ", WarCatalog.Tags(war));
            page.AddPageHeader(PageWidth, LM.Get("war_page_overview"), string.IsNullOrEmpty(chip) ? WarCatalog.Kind(war) : chip);
            (string standing, string standingHex) = WarDisplay.Standing(war);
            page.AddKpiRow(PageWidth,
                (LM.Get("war_kpi_army"), $"{WarDisplay.Count(WarDisplay.Army(war, true))} / {WarDisplay.Count(WarDisplay.Army(war, false))}",
                    WarDisplay.AttackText),
                (LM.Get("war_kpi_losses"), $"{WarDisplay.Losses(war, true)} / {WarDisplay.Losses(war, false)}", WideKit.Orange),
                (LM.Get("war_kpi_lost_cities"), $"{WarDisplay.CitiesLost(war, true)} / {WarDisplay.CitiesLost(war, false)}", WideKit.Green),
                (LM.Get("war_kpi_standing"), standing, standingHex));
            DrawGoal(page, war);
            DrawVersus(page, war);
        }

        private void DrawGoal(AutoVertLayoutGroup page, War war)
        {
            WarExtension.WarExtraData data = war.GetOrCreate();
            WarGoal goal = WarGoalSystem.Get(war);
            string outcome = WarCatalog.Outcome(war);
            var card = page.BeginCard(PageWidth, string.Format(LM.Get("war_card_goal"), WarCatalog.Kind(war)), WideKit.Gold,
                WarObjective.Achievable(goal) ? LM.Get("war_goal_rule") : null);
            int row = 0;
            if (goal != WarGoal.None)
            {
                card.AddValueRow(PageWidth, row++, LM.Get("war_row_goal"), WarCatalog.GoalText(war), WideKit.Gold);
                City main = WarObjective.Main(data);
                List<City> targets = WarCatalog.Cities(data);
                if (main != null && !targets.Contains(main)) targets.Insert(0, main);
                foreach (City city in targets.OrderByDescending(city => city == main).Take(MaxTargets))
                {
                    (float share, string text, string hex) = WarDisplay.Progress(war, city);
                    string label = city == main ? city.GetCityName() + LM.Get("war_main_city_mark") : city.GetCityName();
                    City target = city;
                    card.AddBarRow(PageWidth, row++, label, share, text, hex, WarDisplay.AttackBar, WarDisplay.DefendBar,
                        () => OpenCity(target), 80f, 120f);
                }
            }
            else if (WarObjective.RebelWar(war) && war.main_defender?.capital is City capital)
            {
                (float share, string text, string hex) = WarDisplay.Progress(war, capital);
                card.AddBarRow(PageWidth, row++, capital.GetCityName() + LM.Get("war_capital_mark"), share, text, hex,
                    WarDisplay.AttackBar, WarDisplay.DefendBar, () => OpenCity(capital), 80f, 120f);
            }
            else DrawSpecialGoal(card, war, ref row);
            if (!string.IsNullOrEmpty(outcome)) card.AddValueRow(PageWidth, row++, LM.Get("war_row_outcome"), outcome, WideKit.Soft);
            if (!war.hasEnded())
            {
                if (goal != WarGoal.None)
                    card.AddValueRow(PageWidth, row++, LM.Get("war_row_peace_now"), WarDisplay.PeacePreview(war), WideKit.Orange);
                card.AddValueRow(PageWidth, row, LM.Get("war_row_truce"), WarDisplay.TruceLimit(war), WideKit.Muted);
            }
        }

        // 没有开战目标的仗：按类型写清楚要什么
        private static void DrawSpecialGoal(AutoVertLayoutGroup card, War war, ref int row)
        {
            WarExtension.WarExtraData data = war.GetOrCreate();
            switch (war.GetEmpireWarType())
            {
                case EmpireWarType.劫掠 when data.raid != null:
                    card.AddValueRow(PageWidth, row++, LM.Get("war_row_raid_goal"),
                        string.Format(LM.Get("war_raid_progress"), data.raid.sacked, data.raid.goal), WideKit.Orange);
                    card.AddValueRow(PageWidth, row++, LM.Get("war_row_raid_loot"),
                        string.Format(LM.Get("war_raid_loot"), MoneyDisplay.Format((long)data.raid.loot_money), (int)data.raid.loot_food,
                            UIHelper.ShortCount(data.raid.loot_people)), WideKit.Gold);
                    break;
                case EmpireWarType.索取法理 when war.GetTitleTarget() is var title && title != null:
                    card.AddValueRow(PageWidth, row++, LM.Get("war_row_goal"), string.Format(LM.Get("war_goal_text_claim"), title.data.name),
                        WideKit.Gold);
                    break;
                default:
                    card.AddValueRow(PageWidth, row++, LM.Get("war_row_goal"), WarCatalog.Kind(war), WideKit.Gold);
                    break;
            }
        }

        private static void DrawVersus(AutoVertLayoutGroup page, War war)
        {
            Kingdom attacker = FrontSystem.Realm(war.main_attacker), defender = FrontSystem.Realm(war.main_defender);
            if (attacker?.data == null || defender?.data == null) return;
            var card = page.BeginCard(PageWidth, LM.Get("war_card_versus"), WideKit.Gold, LM.Get("war_versus_by_realm"));
            int row = 0;
            card.AddVersusRow(PageWidth, row++, Commander(attacker), LM.Get("war_vs_commander"), Commander(defender), WideKit.Gold, WideKit.Gold);
            card.AddVersusRow(PageWidth, row++, Corps(attacker), LM.Get("war_vs_corps"), Corps(defender));
            (string af, string afHex) = Food(attacker);
            (string df, string dfHex) = Food(defender);
            card.AddVersusRow(PageWidth, row++, af, LM.Get("war_vs_food"), df, afHex, dfHex);
            (string ap, string apHex) = Pay(attacker);
            (string dp, string dpHex) = Pay(defender);
            card.AddVersusRow(PageWidth, row++, ap, LM.Get("war_vs_pay"), dp, apHex, dpHex);
            card.AddVersusRow(PageWidth, row++, LM.Get("kov_mobilization_" + WarScopeSystem.Level(attacker)), LM.Get("war_vs_mobilization"),
                LM.Get("kov_mobilization_" + WarScopeSystem.Level(defender)), WideKit.Soft, WideKit.Soft);
            bool attackerBroken = FrontCollapse.Collapsing(attacker, defender), defenderBroken = FrontCollapse.Collapsing(defender, attacker);
            card.AddVersusRow(PageWidth, row, LM.Get(attackerBroken ? "war_vs_collapsing" : "war_vs_holding"), LM.Get("war_vs_collapse"),
                LM.Get(defenderBroken ? "war_vs_collapsing" : "war_vs_holding"), attackerBroken ? WideKit.Red : WideKit.Soft,
                defenderBroken ? WideKit.Red : WideKit.Soft);
        }

        private static string Commander(Kingdom realm)
        {
            Actor commander = WarCommandSystem.Commander(realm);
            return commander != null ? $"{WarCommandSystem.TitleOf(realm)} {commander.getName()}" : LM.Get("label_none");
        }

        private static string Corps(Kingdom realm)
        {
            List<CorpsData> corps = CorpsSystem.Of(realm);
            if (corps.Count == 0) return LM.Get("war_vs_no_corps");
            float strength = corps.Sum(item => item.strength), full = corps.Sum(item => item.full);
            return string.Format(LM.Get("war_vs_corps_value"), corps.Count, full > 0f ? strength / full : 0f);
        }

        private static (string, string) Food(Kingdom realm)
        {
            var data = realm.GetOrCreate();
            if (data.war_food_need <= 0f || data.war_food_short <= 0f) return (LM.Get("war_vs_enough"), WideKit.Green);
            return (string.Format(LM.Get("war_vs_food_short"), data.war_food_short / data.war_food_need), WideKit.Red);
        }

        private static (string, string) Pay(Kingdom realm)
        {
            int months = realm.GetOrCreate().corps_unpaid_months;
            return months > 0 ? (string.Format(LM.Get("war_vs_unpaid"), months), WideKit.Red) : (LM.Get("war_vs_paid"), WideKit.Green);
        }

        // 战线
        private void DrawFronts(AutoVertLayoutGroup page)
        {
            War war = _war;
            List<FrontSystem.BattleView> battles = WarDisplay.Battles(war);
            List<NavySystem.BattleView> naval = WarDisplay.NavalBattles(war);
            page.AddPageHeader(PageWidth, LM.Get("war_page_fronts"), string.Format(LM.Get("war_fronts_count"), battles.Count));
            if (!CorpsSystem.Enabled)
            {
                page.AddEmptyCard(PageWidth, LM.Get("war_page_fronts"), LM.Get("war_fronts_no_corps"));
                return;
            }
            int attackTaken = battles.Where(battle => WarDisplay.AttackerSide(war, World.world.kingdoms.get(battle.attacker_id)))
                .Sum(battle => battle.taken);
            int defendTaken = battles.Sum(battle => battle.taken) - attackTaken;
            page.AddKpiRow(PageWidth,
                (LM.Get("war_kpi_fronts"), battles.Count.ToString(), WideKit.Gold),
                (LM.Get("war_kpi_push"), string.Format(LM.Get("war_push_value"), attackTaken, defendTaken), WarDisplay.AttackText),
                (LM.Get("war_kpi_naval"), naval.Count.ToString(), WideKit.Cyan),
                (LM.Get("war_kpi_landing"), battles.Count(battle => battle.landing).ToString(), WideKit.Orange));
            var card = page.BeginCard(PageWidth, LM.Get("war_card_land"), WideKit.Gold, LM.Get("war_land_hint"));
            if (battles.Count == 0) card.AddParagraph(PageWidth, LM.Get("war_fronts_none"), hex: WideKit.Muted);
            int row = 0;
            foreach (FrontSystem.BattleView battle in battles.Take(MaxBattles))
            {
                City city = World.world.cities.get(battle.city_id);
                if (city?.data == null) continue;
                Kingdom attacker = World.world.kingdoms.get(battle.attacker_id);
                bool ours = WarDisplay.AttackerSide(war, attacker);
                float odds = Mathf.Clamp01(battle.ratio);
                string tags = TerrainSystem.Name(battle.terrain);
                if (battle.wall) tags += " · " + LM.Get(battle.wall_breach ? "war_front_wall_breached" : "war_front_wall");
                if (battle.landing) tags += " · " + LM.Get("war_front_landing");
                string detail = string.Format(LM.Get("war_front_detail"), odds, tags, battle.taken,
                    city.GetOccupiedTileZoneRate(attacker));
                City target = city;
                card.AddBarRow(PageWidth, row++, $"{city.GetCityName()}({city.kingdom?.GetKingdomName() ?? ""})", ours ? odds : 1f - odds,
                    detail, ours ? WarDisplay.AttackText : WarDisplay.DefendText, WarDisplay.AttackBar, WarDisplay.DefendBar,
                    () => OpenCity(target), 110f, 70f);
            }
            if (naval.Count == 0) return;
            var sea = page.BeginCard(PageWidth, LM.Get("war_card_naval"), WideKit.Cyan, naval.Count.ToString());
            row = 0;
            foreach (NavySystem.BattleView battle in naval.Take(MaxBattles))
            {
                Kingdom a = World.world.kingdoms.get(battle.attacker_id), d = World.world.kingdoms.get(battle.defender_id);
                sea.AddValueRow(PageWidth, row++, string.Format(LM.Get("war_naval_sides"), a?.GetKingdomName() ?? "?", d?.GetKingdomName() ?? "?"),
                    string.Format(LM.Get("war_naval_value"), battle.attacker_ships, battle.defender_ships, battle.sunk_attacker,
                        battle.sunk_defender), WideKit.Cyan);
            }
        }

        // 双方
        private void DrawSides(AutoVertLayoutGroup page)
        {
            War war = _war;
            List<Kingdom> attackers = WarDisplay.Side(war, true), defenders = WarDisplay.Side(war, false);
            int left = war.getPastAttackers().Count() + war.getPastDefenders().Count();
            page.AddPageHeader(PageWidth, LM.Get("war_page_sides"), string.Format(LM.Get("war_sides_summary"), attackers.Count, defenders.Count, left));
            var columns = page.BeginColumns(PageWidth);
            DrawParty(columns, war, true, attackers);
            DrawParty(columns, war, false, defenders);

            var corps = page.BeginCard(PageWidth, LM.Get("war_card_corps"), WideKit.Gold);
            int row = 0;
            foreach ((CorpsData item, bool attack) in WarDisplay.Realms(war, true).SelectMany(CorpsSystem.Of).Select(item => (item, true))
                         .Concat(WarDisplay.Realms(war, false).SelectMany(CorpsSystem.Of).Select(item => (item, false)))
                         .OrderByDescending(pair => pair.item.strength).Take(MaxCorps))
            {
                Actor general = item.general_id >= 0 ? World.world.units.get(item.general_id) : null;
                City sector = item.sector_city_id >= 0 ? World.world.cities.get(item.sector_city_id) : null;
                string task = LM.Get("corps_task_" + item.task) + (sector != null ? " " + sector.GetCityName() : "") +
                              (general != null ? " · " + general.getName() : "");
                corps.AddTableRow(PageWidth, row++, null, CorpsSystem.Name(item).ColorString(attack ? WarDisplay.AttackText : WarDisplay.DefendText),
                    90f, (CorpsArmSystem.Name(CorpsArmSystem.Of(item)), WideKit.Soft),
                    ($"{WarDisplay.Count(item.strength)} / {WarDisplay.Count(item.full)}", WideKit.Value), (task, WideKit.Muted));
            }
            if (row == 0) corps.AddParagraph(PageWidth, LM.Get("war_corps_none"), hex: WideKit.Muted);
        }

        private static void DrawParty(AutoHoriLayoutGroup columns, War war, bool attackers, List<Kingdom> kingdoms)
        {
            var card = columns.BeginColumnCard(ColumnWidth, LM.Get(attackers ? "war_card_attackers" : "war_card_defenders"),
                attackers ? WarDisplay.AttackText : WarDisplay.DefendText,
                string.Format(LM.Get("war_party_summary"), WarDisplay.Cities(war, attackers), WarDisplay.Count(WarDisplay.Army(war, attackers))));
            int row = 0;
            foreach (Kingdom kingdom in kingdoms.Take(MaxParties))
            {
                Kingdom target = kingdom;
                card.AddListRow(ColumnWidth, row++, null, kingdom.GetKingdomName(),
                    string.Format(LM.Get("war_party_detail"), WarDisplay.Role(war, kingdom, attackers), kingdom.cities?.Count ?? 0),
                    () => OpenKingdom(target), WideKit.Hex(kingdom.getColor()?._color_text ?? Color.white), 70f);
            }
            foreach (Kingdom kingdom in (attackers ? war.getPastAttackers() : war.getPastDefenders()).Where(k => k?.data != null).Take(3))
                card.AddListRow(ColumnWidth, row++, null, kingdom.GetKingdomName(), LM.Get("war_party_left"), null, WideKit.Muted, 70f);
            foreach (Kingdom kingdom in (attackers ? war.getDiedAttackers() : war.getDiedDefenders()).Where(k => k?.data != null).Take(3))
                card.AddListRow(ColumnWidth, row++, null, kingdom.data.name, LM.Get("war_party_died"), null, WideKit.Red, 70f);
        }

        // 战报
        private void DrawChronicle(AutoVertLayoutGroup page)
        {
            List<WarChronicleEntry> log = Log();
            page.AddPageHeader(PageWidth, LM.Get("war_page_chronicle"), string.Format(LM.Get("war_log_count"), log.Count));
            page.AddChipRow(PageWidth,
                (LM.Get("war_log_filter_all"), _logKind < 0, () => SetLog(-1)),
                (LM.Get("war_log_filter_city"), _logKind == WarChronicle.City, () => SetLog(WarChronicle.City)),
                (LM.Get("war_log_filter_commander"), _logKind == WarChronicle.Commander, () => SetLog(WarChronicle.Commander)),
                (LM.Get("war_log_filter_diplomacy"), _logKind == WarChronicle.Diplomacy, () => SetLog(WarChronicle.Diplomacy)));
            List<WarChronicleEntry> shown = Enumerable.Reverse(log).Where(entry => _logKind < 0 || entry.kind == _logKind ||
                                                                                   _logKind == WarChronicle.Diplomacy &&
                                                                                   entry.kind is WarChronicle.Declare or WarChronicle.End)
                .ToList();
            var card = page.BeginCard(PageWidth, LM.Get("war_card_chronicle"), WideKit.Gold, shown.Count.ToString());
            if (shown.Count == 0)
            {
                card.AddParagraph(PageWidth, LM.Get("war_log_none"), hex: WideKit.Muted);
                return;
            }
            int row = 0;
            foreach (WarChronicleEntry entry in shown)
                card.AddTimelineRow(PageWidth, row++, HistoryDateFormatter.GetYear(entry.time), entry.text, KindHex(entry.kind), dateWidth: 56f);
        }

        private static string KindHex(int kind) => kind switch
        {
            WarChronicle.Declare => WideKit.Gold,
            WarChronicle.City => WarDisplay.AttackText,
            WarChronicle.Commander => WideKit.Red,
            WarChronicle.Collapse => WideKit.Red,
            WarChronicle.Goal => WideKit.Green,
            WarChronicle.End => WideKit.Cyan,
            _ => WideKit.Soft
        };

        private void SetLog(int kind)
        {
            _logKind = kind;
            Refresh();
        }

        private static void OpenCity(City city)
        {
            if (city?.data == null || city.isRekt()) return;
            SelectedMetas.selected_city = city;
            ScrollWindow.showWindow("city");
        }

        private static void OpenKingdom(Kingdom kingdom)
        {
            if (kingdom?.data == null || kingdom.isRekt()) return;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom == kingdom && !empire.isRekt())
            {
                SelectedMetas.selected_kingdom = empire.CoreKingdom;
                GameLibrary.EmpireCraftMetaTypeLibrary.selected_empire = empire;
                ScrollWindow.showWindow(nameof(EmpireWindow));
                return;
            }
            KingdomOverviewWindow.Open(kingdom);
        }
    }
}
