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
    //   · 概况：兵力、伤亡、失城、战局；战争目标(目标城逐座进度、主城池、拿到会怎样、门槛格、此刻议和会怎样、强制停战)；两军对比；
    //   · 议和(11-10 定稿示意图 Tasks/ui-mockups/peace-table.png)：两国各出条件，可加可撤，每项折价，天平算两国肯不肯(见 PeaceTable)；
    //   · 战线：这场仗双方交战的城(胜率、地形、城墙、登陆、本月推进)，海战；
    //   · 双方：参战国(怎么卷进来的、城、兵；中途退出的、亡于此战的)，双方主力军团；
    //   · 战报：宣战、参战退出、攻城失城、统帅阵亡、兵败、收场(见 WarChronicle)，按类筛。
    public class WarInfoWindow : WideScrollWindow<WarInfoWindow>
    {
        // 议和页追加在末尾(左栏里排在概况之后)
        public enum Page { Overview, Fronts, Sides, Chronicle, Peace }

        private const int MaxTargets = 6, MaxBattles = 10, MaxCorps = 8, MaxParties = 10;
        // 添加条件里列几座城(全部 / 只看城池)、意愿明细里每边列几项
        private const int MaxPickCitiesAll = 6, MaxPickCities = 30, MaxTipItems = 4;
        private const float ColumnWidth = UIHelper.PageColumnWidth;

        private static War _pending;
        private static Page _pendingPage;
        private War _war;
        private Page _page;
        private int _logKind = -1;
        // 议和页：正在给哪一方添加条件(-1 没开、0 守方出、1 攻方出)，按哪类筛
        private int _peaceAdding = -1;
        private int _peaceFilter;

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
            if (_pending != _war)
            {
                _logKind = -1;
                _peaceAdding = -1;
                _peaceFilter = 0;
            }
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
            if (PeaceTable.Usable(war))
            {
                int years = (int)WarSituation.WarYears(war);
                string open = years >= WarPeaceRules.MinWarYears ? LM.Get("peace_rail_open")
                    : string.Format(LM.Get("peace_rail_wait"), WarPeaceRules.MinWarYears - years);
                menu.AddRailOption(Icon("iconDiplomacy", "iconWar"), LM.Get("war_page_peace"), open, _page == Page.Peace,
                    () => Go(Page.Peace), index++);
            }
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
                case Page.Peace: DrawPeace(page); break;
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
            // 神力点名战争目标：一排可点的目标，不要代价、不受门槛限制(见 WarGoalSystem.SetByPlayer)；说明挂在各块的提示框里
            if (!war.hasEnded() && WarGoalSystem.Switchable(war))
            {
                var chips = card.AddChipRow(PageWidth - 12f);
                WarGoal current = WarGoalSystem.Coarse(goal);
                foreach (WarGoal option in WarGoalSystem.PlayerGoals)
                {
                    WarGoal chosen = option;
                    string name = WarGoalSystem.Name(option, war.main_attacker);
                    var chip = chips.AddChip(name, option == current, () =>
                    {
                        if (WarGoalSystem.SetByPlayer(war, chosen)) Refresh();
                    });
                    UIHelper.AttachTextTooltip(chip.gameObject, "war_goal_chip_" + (int)option, name, LM.Get("war_goal_chip_tip"));
                }
            }
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
            if (war.hasEnded()) return;
            // 为什么是这个目标、还差什么才能升到割地或吞并：四格门槛(以前是十几行数值，用户 11-10"这样子也不美观")
            if (goal != WarGoal.None && WarGoalSystem.Facts(war) is WarGoalSystem.GoalFacts facts) DrawGates(card, war, facts);
            (string preview, string previewHex) = PeaceTable.Preview(war);
            if (preview != null)
                card.AddValueRow(PageWidth, row++, LM.Get("war_row_peace_now"), preview + " ›", previewHex, () => Go(Page.Peace),
                    (PageWidth - 20f) * 0.3f);
            else if (goal != WarGoal.None)
                card.AddValueRow(PageWidth, row++, LM.Get("war_row_peace_now"), WarDisplay.PeacePreview(war), WideKit.Orange);
            card.AddValueRow(PageWidth, row, LM.Get("war_row_truce"), WarDisplay.TruceLimit(war), WideKit.Muted);
        }

        // 门槛格：兵力对比、割地、吞并·臣服、城数(与 WarGoalSystem.Decide 同一套算式)；绿 = 已达，橙 = 还差
        private static void DrawGates(AutoVertLayoutGroup card, War war, WarGoalSystem.GoalFacts facts)
        {
            float cede = WarGoalSystem.ConquestStrength, annex = WarGoalSystem.AnnexStrength;
            int small = WarGoalSystem.AnnexSmallCities;
            string cityHex = facts.SmallEnough ? WideKit.Green : WideKit.Orange;
            card.AddGateRow(PageWidth,
                (LM.Get("war_gate_strength"), null, null, string.Format(LM.Get("war_gate_ratio"), facts.strength), WideKit.Value,
                    facts.strength / annex),
                (LM.Get("war_gate_cede"), string.Format(LM.Get("war_gate_cede_rule"), cede), facts.CanCede,
                    facts.borders ? string.Format(LM.Get("war_gate_cede_value"), facts.strength) : LM.Get("war_gate_no_border"),
                    facts.CanCede ? WideKit.Green : WideKit.Orange, facts.borders ? facts.strength / cede : 0f),
                (LM.Get("war_gate_annex"), string.Format(LM.Get("war_gate_annex_rule"), annex), facts.CanAnnex,
                    string.Format(LM.Get("war_gate_annex_value"), facts.strength, annex), facts.CanAnnex ? WideKit.Green : WideKit.Orange,
                    facts.strength / annex),
                (LM.Get("war_gate_cities"), string.Format(LM.Get("war_gate_cities_rule"), small), facts.SmallEnough,
                    string.Format(LM.Get("war_gate_cities_value"), war.main_defender.GetKingdomName(), facts.theirs), cityHex,
                    facts.SmallEnough || facts.theirs <= 0 ? 1f : Mathf.Max(small, facts.ours / 2) / (float)facts.theirs));
            string note = LM.Get("war_gate_note_" + facts.rule);
            if (!facts.SmallEnough) note += " · " + string.Format(LM.Get("war_gate_annex_short"), small, facts.ours / 2);
            card.AddParagraph(PageWidth, note, 6, WideKit.Muted);
        }

        // ───────── 议和(示意图 ①②③) ─────────
        private void DrawPeace(AutoVertLayoutGroup page)
        {
            War war = _war;
            if (!PeaceTable.Usable(war))
            {
                page.AddPageHeader(PageWidth, LM.Get("war_page_peace"));
                page.AddEmptyCard(PageWidth, LM.Get("war_page_peace"), LM.Get("peace_unusable_hint"));
                return;
            }
            PeaceDeal deal = PeaceTable.Draft(war);
            PeaceBasis basis = PeaceTable.Basis(war);
            (PeaceOpinion a, PeaceOpinion d) = PeaceTable.Evaluate(war, deal, basis);
            int years = (int)WarSituation.WarYears(war);
            page.AddPageHeader(PageWidth, LM.Get("war_page_peace"), string.Format(LM.Get(years >= WarPeaceRules.MinWarYears
                ? "peace_header_open" : "peace_header_wait"), years, WarPeaceRules.MinWarYears));
            // 照目标开价一格放不下就只写几项，悬停看全文
            PeaceDeal offer = PeaceTable.Offer(war);
            string summary = PeaceTable.Summary(war, offer);
            float tile = (PageWidth - 2f * UIHelper.CardGap) / 3f;
            string shown = UIHelper.EstimateTextWidth(summary, 12) <= tile - 9f ? summary
                : string.Format(LM.Get("peace_offer_count"), offer.terms.Count);
            var kpis = page.AddKpiRow(PageWidth,
                (LM.Get("peace_kpi_leverage"), $"{N(basis.attacker.Total)} / {N(basis.defender.Total)}", WarDisplay.AttackText),
                (LM.Get("peace_kpi_wear"), $"{N(basis.attackerWear.Total)} / {N(basis.defenderWear.Total)}", WideKit.Orange),
                (LM.Get("peace_kpi_offer"), shown, WideKit.Gold));
            if (kpis.transform.childCount >= 3)
            {
                UIHelper.AttachTextTooltip(kpis.transform.GetChild(0).gameObject, "peace_tip_leverage",
                    LM.Get("peace_tip_leverage_title"), LeverageTip(war, basis));
                UIHelper.AttachTextTooltip(kpis.transform.GetChild(1).gameObject, "peace_tip_wear",
                    LM.Get("peace_tip_wear_title"), WearTip(war, basis));
                UIHelper.AttachTextTooltip(kpis.transform.GetChild(2).gameObject, "peace_tip_offer",
                    LM.Get("peace_kpi_offer"), PeaceTable.Describe(war, offer));
            }
            var columns = page.BeginColumns(PageWidth);
            DrawGives(columns, war, deal, basis, false, d);
            DrawGives(columns, war, deal, basis, true, a);
            if (_peaceAdding >= 0) DrawPicker(page, war, deal, basis, _peaceAdding == 1);
            DrawWill(page, war, deal, basis, a, d);
            bool both = a.Accepts && d.Accepts;
            page.AddActionRow(PageWidth,
                (LM.Get("peace_btn_offer"), WideKit.Value, true, () =>
                {
                    PeaceTable.ResetToOffer(war);
                    Refresh();
                }),
                (LM.Get("peace_btn_mediate"), WideKit.Value, true, () =>
                {
                    PeaceTable.Mediate(war);
                    Refresh();
                }),
                (LM.Get("peace_btn_clear"), WideKit.Value, true, () =>
                {
                    PeaceTable.Clear(war);
                    Refresh();
                }),
                (LM.Get("peace_btn_sign"), WideKit.Green, both, () => Sign(false)),
                (LM.Get("peace_btn_force"), WideKit.Gold, true, () => Sign(true)));
        }

        private void Sign(bool forced)
        {
            if (!PeaceTable.Sign(_war, forced))
            {
                Refresh();
                return;
            }
            _peaceAdding = -1;
            // 签下了：去战报看和约那一条
            Go(Page.Chronicle);
        }

        // 一方出的条件(左栏守方、右栏攻方)
        private void DrawGives(AutoHoriLayoutGroup columns, War war, PeaceDeal deal, PeaceBasis basis, bool byAttacker, PeaceOpinion giverOpinion)
        {
            Kingdom giver = byAttacker ? war.main_attacker : war.main_defender;
            List<PeaceTerm> terms = deal.terms.Where(term => term.by_attacker == byAttacker).ToList();
            string hex = byAttacker ? WarDisplay.AttackText : WarDisplay.DefendText;
            var card = columns.BeginColumnCard(ColumnWidth, string.Format(LM.Get("peace_gives"), giver.GetKingdomName()), hex,
                string.Format(LM.Get("peace_items"), terms.Count));
            int row = 0;
            if (terms.Count == 0)
                card.AddParagraph(ColumnWidth, string.Format(LM.Get("peace_gives_none"), giver.GetKingdomName()), 7, WideKit.Muted);
            foreach (PeaceTerm term in terms)
            {
                (string label, string tag, string tagHex) = PeaceTable.Label(war, term);
                if (term.added)
                {
                    string mark = LM.Get("peace_tag_new").ColorString(WideKit.Orange);
                    tag = string.IsNullOrEmpty(tag) ? mark : tag.ColorString(tagHex ?? WideKit.Muted) + " " + mark;
                    tagHex = WideKit.Muted;
                }
                PeaceTerm target = term;
                card.AddTermRow(ColumnWidth, row++, TermIcon(term), label, tag, tagHex, N(PeaceTable.Value(war, term, basis)), () =>
                {
                    PeaceTable.Remove(war, target);
                    Refresh();
                });
            }
            bool open = _peaceAdding == (byAttacker ? 1 : 0);
            card.AddActionRow(ColumnWidth - 12f, (string.Format(LM.Get("peace_add"), giver.GetKingdomName()),
                open ? WideKit.Gold : WideKit.Cyan, true, () =>
                {
                    _peaceAdding = open ? -1 : byAttacker ? 1 : 0;
                    _peaceFilter = 0;
                    Refresh();
                }));
            card.AddValueRow(ColumnWidth, 1, LM.Get("peace_sum"), N(giverOpinion.loss), hex);
        }

        // 添加条件(示意图 ②)：按类列出这一方能出的，标折价；出不了的灰着写原因；已在台上的点了撤下
        private void DrawPicker(AutoVertLayoutGroup page, War war, PeaceDeal deal, PeaceBasis basis, bool byAttacker)
        {
            Kingdom giver = byAttacker ? war.main_attacker : war.main_defender;
            var card = page.BeginCard(PageWidth, string.Format(LM.Get("peace_pick_title"), giver.GetKingdomName()), WideKit.Cyan,
                LM.Get("peace_pick_hint"));
            card.AddChipRow(PageWidth - 12f,
                (LM.Get("peace_cat_all"), _peaceFilter == 0, () => SetPeaceFilter(0)),
                (LM.Get("peace_cat_city"), _peaceFilter == 1, () => SetPeaceFilter(1)),
                (LM.Get("peace_cat_money"), _peaceFilter == 2, () => SetPeaceFilter(2)),
                (LM.Get("peace_cat_realm"), _peaceFilter == 3, () => SetPeaceFilter(3)),
                (LM.Get("peace_cat_other"), _peaceFilter == 4, () => SetPeaceFilter(4)));
            int row = 0, cities = 0;
            foreach ((PeaceTerm term, string reason) in PeaceTable.Options(war, byAttacker))
            {
                int category = PeaceTable.Category(term);
                if (_peaceFilter != 0 && category != _peaceFilter) continue;
                if (category == 1 && ++cities > (_peaceFilter == 1 ? MaxPickCities : MaxPickCitiesAll)) continue;
                (string label, string tag, string tagHex) = PeaceTable.Label(war, term);
                bool on = deal.Has(term);
                string text = label.ColorString(reason != null ? WideKit.Muted : on ? WideKit.Gold : WideKit.Value);
                if (!string.IsNullOrEmpty(tag)) text += " " + tag.ColorString(reason != null ? WideKit.Muted : tagHex ?? WideKit.Muted);
                int value = reason == null ? PeaceTable.Value(war, term, basis) : 0;
                string right = reason != null ? LM.Get(reason) : on ? string.Format(LM.Get("peace_on_table"), N(value)) : N(value);
                PeaceTerm target = term;
                card.AddValueRow(PageWidth, row++, text, right, reason != null ? WideKit.Muted : on ? WideKit.Gold : WideKit.Soft,
                    reason == null ? () =>
                    {
                        PeaceTable.Toggle(war, target);
                        Refresh();
                    } : null, (PageWidth - 20f) * 0.62f);
            }
        }

        private void SetPeaceFilter(int filter)
        {
            _peaceFilter = filter;
            Refresh();
        }

        // 两国意愿(示意图 ①③)：以 0 为中线的条，悬停看明细；下面一行写还差多少、撤哪项就够
        private static void DrawWill(AutoVertLayoutGroup page, War war, PeaceDeal deal, PeaceBasis basis, PeaceOpinion a, PeaceOpinion d)
        {
            var card = page.BeginCard(PageWidth, LM.Get("peace_card_will"), WideKit.Gold, LM.Get("peace_will_hint"));
            float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(a.Total), Mathf.Abs(d.Total)) * 1.15f);
            Meter(card, war, 0, true, a, scale, basis);
            Meter(card, war, 1, false, d, scale, basis);
            (string left, string right) = PeaceTable.Hint(war, deal, basis, a, d);
            card.AddValueRow(PageWidth, 2, left, right, a.Accepts && d.Accepts ? WideKit.Green : WideKit.Soft, null,
                (PageWidth - 20f) * 0.3f);
        }

        private static void Meter(AutoVertLayoutGroup card, War war, int index, bool attacker, PeaceOpinion opinion, float scale,
            PeaceBasis basis)
        {
            Kingdom kingdom = attacker ? war.main_attacker : war.main_defender;
            string text = $"{Signed(opinion.Total)} · {LM.Get(opinion.Accepts ? "peace_accept" : "peace_refuse")}";
            var row = card.AddMeterRow(PageWidth, index, kingdom.GetKingdomName(), attacker ? WarDisplay.AttackText : WarDisplay.DefendText,
                opinion.Total, scale, text, opinion.Accepts ? WideKit.Green : WideKit.Red);
            UIHelper.AttachTextTooltip(row.gameObject, attacker ? "peace_tip_will_attacker" : "peace_tip_will_defender",
                string.Format(LM.Get("peace_tip_will_title"), kingdom.GetKingdomName(), text), WillTip(war, opinion, attacker, basis));
        }

        // 意愿明细：所得、所失、战局、疲敝、势头、死战逐项
        private static string WillTip(War war, PeaceOpinion opinion, bool attacker, PeaceBasis basis)
        {
            Kingdom self = attacker ? war.main_attacker : war.main_defender, other = attacker ? war.main_defender : war.main_attacker;
            int selfLeverage = attacker ? basis.attacker.Total : basis.defender.Total;
            int otherLeverage = attacker ? basis.defender.Total : basis.attacker.Total;
            PeaceTermRules.Wear wear = attacker ? basis.attackerWear : basis.defenderWear;
            var lines = new List<string>
            {
                LM.Get("peace_tip_formula"),
                string.Format(LM.Get("peace_tip_gain"), Signed(opinion.gain)) + Items(war, opinion.gains),
                string.Format(LM.Get("peace_tip_loss"), Signed(-opinion.loss)) + Items(war, opinion.losses),
                string.Format(LM.Get("peace_tip_standing"), Signed(opinion.standing), other.GetKingdomName(), N(otherLeverage),
                    self.GetKingdomName(), N(selfLeverage)),
                string.Format(LM.Get("peace_tip_wear"), Signed(opinion.wear)) + WearParts(wear),
                string.Format(LM.Get("peace_tip_momentum"), Signed(-opinion.momentum)),
                opinion.desperate > 0 ? string.Format(LM.Get("peace_tip_desperate"), Signed(-opinion.desperate))
                    : LM.Get("peace_tip_desperate_none")
            };
            // 民族意识：近代割让、立傀儡他国本土，失地一方另记(见 PeaceTermRules 民族意识一节)
            if (opinion.nation > 0) lines.Add(string.Format(LM.Get("peace_tip_nation"), Signed(-opinion.nation)) + Items(war, opinion.nations));
            return string.Join("\n", lines);
        }

        private static string Items(War war, List<(PeaceTerm term, int value)> items)
        {
            if (items.Count == 0) return "";
            string list = string.Join(LM.Get("list_separator"), items.Take(MaxTipItems)
                .Select(item => $"{PeaceTable.Label(war, item.term).label} {N(item.value)}"));
            if (items.Count > MaxTipItems) list += LM.Get("peace_tip_more");
            return LM.Get("peace_tip_colon") + list;
        }

        private static string WearParts(PeaceTermRules.Wear wear)
        {
            var parts = new List<string>();
            if (wear.years > 0) parts.Add(string.Format(LM.Get("peace_wear_years"), wear.yearCount, N(wear.years)));
            if (wear.unpaid > 0) parts.Add(string.Format(LM.Get("peace_wear_unpaid"), wear.unpaidMonths, N(wear.unpaid)));
            if (wear.food > 0) parts.Add(string.Format(LM.Get("peace_wear_food"), Mathf.RoundToInt(wear.foodShort * 100f), N(wear.food)));
            if (wear.collapse > 0) parts.Add(string.Format(LM.Get("peace_wear_collapse"), N(wear.collapse)));
            if (wear.occupation > 0) parts.Add(string.Format(LM.Get("peace_wear_occupation"), wear.occupiedCities, N(wear.occupation)));
            return parts.Count == 0 ? "" : LM.Get("peace_tip_colon") + string.Join(LM.Get("list_separator"), parts);
        }

        // 筹码明细：两边各一行
        private static string LeverageTip(War war, PeaceBasis basis)
        {
            string Line(Kingdom kingdom, PeaceTermRules.Leverage leverage) => string.Format(LM.Get("peace_tip_leverage_line"),
                kingdom.GetKingdomName(), N(leverage.Total), N(leverage.taken), N(leverage.occupied), leverage.ratio, N(leverage.strength));
            return Line(war.main_attacker, basis.attacker) + "\n" + Line(war.main_defender, basis.defender) + "\n" +
                   string.Format(LM.Get("peace_tip_leverage_note"), PeaceTermRules.StrengthCap);
        }

        private static string WearTip(War war, PeaceBasis basis)
        {
            string Line(Kingdom kingdom, PeaceTermRules.Wear wear)
            {
                string parts = WearParts(wear);
                return kingdom.GetKingdomName() + " " + N(wear.Total) + (parts.Length > 0 ? parts : LM.Get("peace_tip_colon") + LM.Get("peace_wear_none"));
            }
            return Line(war.main_attacker, basis.attackerWear) + "\n" + Line(war.main_defender, basis.defenderWear) + "\n" +
                   string.Format(LM.Get("peace_tip_wear_note"), WarPeaceRules.MinWarYears);
        }

        private static Sprite TermIcon(PeaceTerm term) => term.kind switch
        {
            PeaceTermKind.City => Icon("iconCity"),
            PeaceTermKind.Puppet => Icon("iconKings", "iconCity"),
            PeaceTermKind.Indemnity => Icon("iconMoney"),
            PeaceTermKind.Tribute => Icon("iconGold", "iconMoney"),
            PeaceTermKind.Vassal => Icon("iconCrown"),
            PeaceTermKind.Install => Icon("iconKings", "iconCrown"),
            PeaceTermKind.Trade => Icon("iconMoneyFlow", "iconDiplomacy"),
            _ => Icon("iconReligion")
        };

        private static string N(int value) => value.ToString("#,0");

        private static string Signed(int value) => value > 0 ? "+" + N(value) : N(value);

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
                string tags = WarWeather.Tag(battle.terrain);
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
