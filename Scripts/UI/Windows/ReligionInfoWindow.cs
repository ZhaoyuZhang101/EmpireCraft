using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 宗教窗口(用户 2026-10-27 定稿示意图 Tasks/ui-mockups/religion-wide.png ②~⑥)：点宗教图层、原版宗教链接
    // (见 ReligionWindowRedirectPatch)、天下宗教名册打开。
    //   · 左栏：宗教档案(名、地位、类型 · 天下 · 立教)、圣地、教首(教宗、哈里发、兼管的皇帝)、页目、原版宗教窗口；
    //   · 右栏六页：概况(主指标、宗教性质、近况、奉为国教的国家)、教廷(教权、教宗正统构成、各帝国与教廷)、
    //     分布(各国信众、本教为国教的国家里的异教城)、谱系(母教与分出的教会、并入的地方教会、与各分支的关系)、
    //     圣战与骑士团、大事记(按类筛选，见 ReligionChronicle)。
    public class ReligionInfoWindow : WideScrollWindow<ReligionInfoWindow>
    {
        public enum Page { Overview, Papacy, Distribution, Lineage, HolyWar, Chronicle }

        private const int MaxStates = 6, MaxHolders = 12, MaxEvents = ReligionChronicle.MaxEvents;
        private const float ColumnWidth = UIHelper.PageColumnWidth;

        private static Religion _pending;
        private static Page _pendingPage;
        private Religion _religion;
        private Page _page;
        private int _chronicleKind = -1;
        private int _distributionFilter;

        private readonly List<City> _cities = new();
        private float _people, _worldPeople;
        private List<Kingdom> _states = new();

        public static void Open(Religion religion, Page page = Page.Overview)
        {
            if (!ReligionDisplay.Alive(religion)) return;
            _pending = religion;
            _pendingPage = page;
            ScrollWindow.showWindow(nameof(ReligionInfoWindow));
        }

        protected override void OnOpen()
        {
            if (_pending == null) return;
            if (_pending != _religion)
            {
                _chronicleKind = -1;
                _distributionFilter = 0;
            }
            _religion = _pending;
            _page = _pendingPage;
            _pending = null;
        }

        private void Go(Page page)
        {
            _page = page;
            Refresh(resetScroll: true);
        }

        protected override void Prepare()
        {
            _cities.Clear();
            _people = _worldPeople = 0f;
            _states = new List<Kingdom>();
            if (!ReligionDisplay.Alive(_religion)) return;
            foreach (City city in World.world.cities.list)
            {
                if (city?.data == null || city.isRekt()) continue;
                float count = city.CountLivingPopulation();
                _worldPeople += count;
                if (city.religion != _religion) continue;
                _cities.Add(city);
                _people += count;
            }
            _states = World.world.kingdoms.list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                                                  kingdom.religion == _religion)
                .OrderByDescending(kingdom => kingdom.cities.Count).ToList();
        }

        // ───────── 左栏 ─────────
        protected override void DrawRail(AutoVertLayoutGroup rail)
        {
            if (!ReligionDisplay.Alive(_religion))
            {
                rail.AddRailTitle(LM.Get("ReligionInfoWindow Title"));
                return;
            }
            Religion religion = _religion;
            float cardWidth = UIHelper.RailWidth + 4f, rowWidth = UIHelper.RailWidth - 8f;
            var head = rail.BeginRailCard("FactionFrame_dominate");
            head.AddColorStrip(cardWidth, ReligionDisplay.MainColor(religion), 3f);
            head.AddRailText(religion.name.ColorString(WideKit.Gold), 10, 14f);
            (string standing, string standingHex) = ReligionDisplay.Standing(religion);
            head.AddRailText(standing.ColorString(standingHex), 7, 10f);
            head.AddRailText(string.Format(LM.Get("religion_rail_summary"), ReligionDisplay.FaithName(religion),
                ReligionDisplay.SphereName(religion)).ColorString(WideKit.Muted), 6, 9f);
            head.AddRailText(string.Format(LM.Get("religion_rail_age"), religion.getAge()).ColorString(WideKit.Muted), 6, 9f);

            City holy = PapacySystem.HolyCity(religion);
            var seat = rail.BeginRailCard();
            seat.AddRailRow(rowWidth, LM.Get("papacy_holy_city"), holy != null ? holy.GetCityName() : LM.Get("papacy_see_lost"),
                holy != null ? WideKit.Gold : WideKit.Red, 0);
            if (holy?.kingdom != null)
                seat.AddRailRow(rowWidth, LM.Get("religion_holy_owner"), holy.kingdom.GetKingdomName(), WideKit.Soft, 1);

            Actor leader = PapacySystem.Pope(religion);
            if (ReligionFaith.HasHierarchy(religion))
            {
                var who = rail.BeginRailCard();
                who.AddPersonRow(cardWidth, leader, ReligionDisplay.HeadCaption(religion, leader),
                    leader != null ? leader.getName() : LM.Get("label_none"),
                    PopeLegitimacy.Applies(religion)
                        ? string.Format(LM.Get("religion_pope_legitimacy_short"), PopeLegitimacy.Get(religion))
                        : null);
            }

            var menu = rail.BeginRailMenu(LM.Get("religion_rail_group"));
            int index = 0;
            menu.AddRailOption(Icon("iconReligion"), LM.Get("religion_page_overview"), null, _page == Page.Overview,
                () => Go(Page.Overview), index++);
            menu.AddRailOption(Icon("iconKings", "iconKingdom"), ReligionDisplay.HierarchyName(religion), null, _page == Page.Papacy,
                () => Go(Page.Papacy), index++);
            menu.AddRailOption(Icon("iconCity"), LM.Get("religion_page_distribution"), _cities.Count.ToString(),
                _page == Page.Distribution, () => Go(Page.Distribution), index++);
            menu.AddRailOption(Icon("iconFamily"), LM.Get("religion_page_lineage"),
                ReligionDisplay.Children(religion).Count.ToString(), _page == Page.Lineage, () => Go(Page.Lineage), index++);
            menu.AddRailOption(Icon("iconWar"), LM.Get("religion_page_holywar"), PapacySystem.Orders(religion).Count().ToString(),
                _page == Page.HolyWar, () => Go(Page.HolyWar), index++);
            menu.AddRailOption(Icon("iconWorldLog", "iconClock"), LM.Get("religion_page_chronicle"),
                ReligionChronicle.Events(religion).Count.ToString(), _page == Page.Chronicle, () => Go(Page.Chronicle), index++);

            rail.AddRailButton("religion_open_vanilla", LM.Get("religion_open_vanilla"),
                () => ReligionWindowRedirectPatch.OpenVanilla(religion));
        }

        private static Sprite Icon(params string[] names) => UIHelper.FirstSprite(names.Select(name => "ui/icons/" + name).ToArray());

        // ───────── 右栏 ─────────
        protected override void DrawPage(AutoVertLayoutGroup page)
        {
            if (!ReligionDisplay.Alive(_religion))
            {
                page.AddEmptyCard(PageWidth, LM.Get("ReligionInfoWindow Title"), LM.Get("religion_gone_hint"));
                return;
            }
            switch (_page)
            {
                case Page.Papacy: DrawPapacy(page); break;
                case Page.Distribution: DrawDistribution(page); break;
                case Page.Lineage: DrawLineage(page); break;
                case Page.HolyWar: DrawHolyWar(page); break;
                case Page.Chronicle: DrawChronicle(page); break;
                default: DrawOverview(page); break;
            }
        }

        private string Share() => _worldPeople > 0f ? $"{_people / _worldPeople:P0}" : "—";

        // 概况
        private void DrawOverview(AutoVertLayoutGroup page)
        {
            Religion religion = _religion;
            (string standing, _) = ReligionDisplay.Standing(religion);
            page.AddPageHeader(PageWidth, LM.Get("religion_page_overview"), standing);
            bool hierarchy = ReligionFaith.HasHierarchy(religion);
            page.AddKpiRow(PageWidth,
                (string.Format(LM.Get("religion_kpi_people"), Share()), UIHelper.ShortCount(_people), WideKit.Green),
                (LM.Get("religion_kpi_cities"), _cities.Count.ToString(), WideKit.Gold),
                (LM.Get("religion_kpi_states"), _states.Count.ToString(), WideKit.Cyan),
                hierarchy
                    ? (LM.Get("papacy_authority"), $"{PapacySystem.Authority(religion):0}", WideKit.Orange)
                    : (LM.Get("religion_kpi_age"), religion.getAge().ToString(), WideKit.Muted));

            var columns = page.BeginColumns(PageWidth);
            var nature = columns.BeginColumnCard(ColumnWidth, LM.Get("religion_card_nature"), WideKit.Gold,
                ReligionDisplay.FaithName(religion));
            int row = 0;
            nature.AddValueRow(ColumnWidth, row++, LM.Get("religion_row_hierarchy"), ReligionDisplay.HierarchyName(religion),
                hierarchy ? WideKit.Gold : WideKit.Soft);
            nature.AddValueRow(ColumnWidth, row++, LM.Get("religion_row_exclusive"), ReligionDisplay.ExclusiveName(religion));
            nature.AddValueRow(ColumnWidth, row++, LM.Get("religion_row_mission"), ReligionDisplay.MissionName(religion), WideKit.Green);
            nature.AddValueRow(ColumnWidth, row++, LM.Get("religion_row_inertia"), ReligionDisplay.InertiaName(religion));
            nature.AddValueRow(ColumnWidth, row, LM.Get("religion_row_holywar"), ReligionDisplay.HolyWarName(religion),
                ReligionFaith.WagesHolyWar(religion) ? WideKit.Red : WideKit.Soft);

            var data = religion.GetOrCreate();
            War crusade = PapacySystem.ActiveCrusade(religion);
            var recent = columns.BeginColumnCard(ColumnWidth, LM.Get("religion_card_recent"), WideKit.Gold);
            row = 0;
            recent.AddValueRow(ColumnWidth, row++, LM.Get("religion_row_crusade"),
                crusade != null ? crusade.data.name : LM.Get("label_none"), crusade != null ? WideKit.Red : WideKit.Soft);
            recent.AddValueRow(ColumnWidth, row++, LM.Get("papacy_orders_title"), PapacySystem.Orders(religion).Count().ToString(), WideKit.Gold);
            recent.AddValueRow(ColumnWidth, row++, LM.Get("religion_row_antipope"),
                LM.Get(data.Schism ? "religion_yes" : "religion_no"), data.Schism ? WideKit.Red : WideKit.Green);
            recent.AddValueRow(ColumnWidth, row++, LM.Get("papacy_state_captive"),
                LM.Get(PapacySystem.IsCaptive(religion) ? "religion_yes" : "religion_no"),
                PapacySystem.IsCaptive(religion) ? WideKit.Red : WideKit.Green);
            recent.AddValueRow(ColumnWidth, row, LM.Get("religion_row_children"), ReligionDisplay.Children(religion).Count.ToString(),
                ReligionDisplay.Purple);

            var states = page.BeginCard(PageWidth, LM.Get("religion_card_states"), WideKit.Gold, _states.Count.ToString());
            if (_states.Count == 0) states.AddParagraph(PageWidth, LM.Get("religion_states_none"), hex: WideKit.Muted);
            int index = 0;
            foreach (Kingdom kingdom in _states.Take(MaxStates))
            {
                Kingdom target = kingdom;
                states.AddListRow(PageWidth, index++, null, kingdom.GetKingdomName(), StateDetail(kingdom), () => OpenKingdom(target),
                    WideKit.Hex(kingdom.getColor()?._color_text ?? Color.white), 110f);
            }
        }

        private static string StateDetail(Kingdom kingdom)
        {
            string detail = string.Format(LM.Get("religion_state_cities"), kingdom.cities.Count);
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom == kingdom && PapacySystem.Applies(empire))
            {
                var state = PapacySystem.State(empire);
                detail += " · " + LM.Get(PapacySystem.IsCrowned(empire)
                    ? state.self_crowned ? "papacy_crown_self" : "papacy_crown_pope"
                    : "papacy_crown_none");
            }
            if (ReligionConversionSystem.RecentlyConverted(kingdom)) detail += " · " + LM.Get("religion_state_converted");
            return detail;
        }

        // 教廷
        private void DrawPapacy(AutoVertLayoutGroup page)
        {
            Religion religion = _religion;
            if (!ReligionFaith.HasHierarchy(religion))
            {
                page.AddEmptyCard(PageWidth, ReligionDisplay.HierarchyName(religion), LM.Get("religion_no_hierarchy_hint"));
                return;
            }
            Kingdom see = PapacySystem.Papacy(religion);
            page.AddPageHeader(PageWidth, ReligionDisplay.HierarchyName(religion),
                see != null ? see.GetKingdomName() : LM.Get("papacy_see_lost"), SeeState(religion));
            List<Empire> empires = (ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
                .Where(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt() &&
                                 PapacySystem.ReligionOf(empire) == religion).ToList();
            int crowned = empires.Count(PapacySystem.IsCrowned), banned = empires.Count(PapacySystem.IsExcommunicated);
            bool papal = PopeLegitimacy.Applies(religion);
            int legitimacy = PopeLegitimacy.Get(religion);
            page.AddKpiRow(PageWidth,
                (LM.Get("papacy_authority"), $"{PapacySystem.Authority(religion):0}", WideKit.Orange),
                (LM.Get("religion_pope_legitimacy"), papal ? legitimacy.ToString() : "—",
                    papal ? WideKit.Grade(legitimacy, 60, PopeLegitimacy.AntipopeBelow) : WideKit.Muted),
                (LM.Get("religion_kpi_crowned"), $"{crowned} / {empires.Count}", WideKit.Cyan),
                (LM.Get("religion_kpi_banned"), banned.ToString(), banned > 0 ? WideKit.Red : WideKit.Green));

            var columns = page.BeginColumns(PageWidth);
            Actor head = PapacySystem.Pope(religion);
            var who = columns.BeginColumnCard(ColumnWidth, ReligionDisplay.HeadTitle(religion), WideKit.Gold);
            who.AddPersonRow(ColumnWidth, head, ReligionDisplay.HeadCaption(religion, head),
                head != null ? head.getName() : LM.Get("label_none"));
            var data = religion.GetOrCreate();
            int chosen = head != null && data.LeaderID == head.id ? data.PopeChosenBy : 0;
            who.AddValueRow(ColumnWidth, 0, LM.Get("religion_row_chosen"), LM.Get("religion_chosen_" + chosen),
                chosen == PapalElection.ByConclave ? WideKit.Green : chosen == PapalElection.ByImposition ? WideKit.Red : WideKit.Soft);
            who.AddValueRow(ColumnWidth, 1, LM.Get("papacy_holy_city"), PapacySystem.HolyCity(religion)?.GetCityName() ?? LM.Get("papacy_see_lost"),
                WideKit.Gold);

            var parts = columns.BeginColumnCard(ColumnWidth, LM.Get("religion_card_pope_legitimacy"), WideKit.Gold,
                papal ? legitimacy.ToString() : "—");
            if (!papal)
                parts.AddParagraph(ColumnWidth, LM.Get("religion_caliph_legitimacy_hint"), hex: WideKit.Muted);
            int row = 0;
            foreach ((string key, int value) in PopeLegitimacy.Breakdown(religion))
                parts.AddValueRow(ColumnWidth, row++, LM.Get(key), value > 0 && key != "pope_legit_base" ? $"+{value}" : value.ToString(),
                    value < 0 ? WideKit.Red : key == "pope_legit_base" ? WideKit.Soft : WideKit.Green);

            var relations = page.BeginCard(PageWidth, LM.Get("religion_card_empires"), WideKit.Gold, empires.Count.ToString());
            if (empires.Count == 0) relations.AddParagraph(PageWidth, LM.Get("religion_empires_none"), hex: WideKit.Muted);
            int index = 0;
            foreach (Empire empire in empires)
            {
                Kingdom core = empire.CoreKingdom;
                relations.AddListRow(PageWidth, index++, null, empire.GetEmpireFullName(), EmpireDetail(empire),
                    () => OpenKingdom(core), WideKit.Hex(core?.getColor()?._color_text ?? Color.white), 120f);
            }
        }

        private static string SeeState(Religion religion)
        {
            var data = religion.GetOrCreate();
            if (data.Caesaropapist) return LM.Get("religion_state_caesaropapist");
            if (data.Schism) return LM.Get("papacy_state_schism");
            if (PapacySystem.IsCaptive(religion)) return LM.Get("papacy_state_captive");
            if (PapacySystem.Papacy(religion) == null) return LM.Get("papacy_see_lost");
            return LM.Get("papacy_state_free");
        }

        private static string EmpireDetail(Empire empire)
        {
            if (!PapacySystem.Applies(empire) && !PapacySystem.AppliesCaliphal(empire)) return LM.Get("religion_empire_na");
            var state = PapacySystem.State(empire);
            if (PapacySystem.IsExcommunicated(empire)) return LM.Get("papacy_crown_banned");
            string crown = LM.Get(PapacySystem.IsCrowned(empire)
                ? state.self_crowned ? "papacy_crown_self" : "papacy_crown_pope"
                : HolyRomanSystem.Romzug(empire) == HolyRomanSystem.RomzugTravelling ? "religion_romzug" : "papacy_crown_none");
            if (!PapacySystem.Applies(empire)) return crown;
            return string.Format(LM.Get("religion_empire_detail"), crown, LM.Get("papacy_investiture_" + PapacySystem.Investiture(empire)),
                MoneyDisplay.Format(state.tithe_paid));
        }

        // 分布
        private void DrawDistribution(AutoVertLayoutGroup page)
        {
            Religion religion = _religion;
            page.AddPageHeader(PageWidth, LM.Get("religion_page_distribution"), string.Format(LM.Get("religion_state_cities"), _cities.Count));
            page.AddChipRow(PageWidth,
                (LM.Get("religion_filter_all"), _distributionFilter == 0, () => SetDistribution(0)),
                (LM.Get("religion_filter_state"), _distributionFilter == 1, () => SetDistribution(1)),
                (LM.Get("religion_filter_minority"), _distributionFilter == 2, () => SetDistribution(2)));
            int stateCities = _cities.Count(city => city.kingdom?.religion == religion);
            page.AddKpiRow(PageWidth,
                (LM.Get("religion_kpi_cities"), _cities.Count.ToString(), WideKit.Gold),
                (LM.Get("religion_kpi_state_cities"), stateCities.ToString(), WideKit.Cyan),
                (LM.Get("religion_kpi_minority_cities"), (_cities.Count - stateCities).ToString(), WideKit.Orange),
                (LM.Get("religion_kpi_share"), Share(), WideKit.Green));

            var holders = _cities.Where(city => city.kingdom != null)
                .Where(city => _distributionFilter == 0 || (_distributionFilter == 1) == (city.kingdom.religion == religion))
                .GroupBy(city => city.kingdom).OrderByDescending(group => group.Count()).ToList();
            var card = page.BeginCard(PageWidth, LM.Get("religion_card_holders"), WideKit.Gold, holders.Count.ToString());
            if (holders.Count == 0) card.AddParagraph(PageWidth, LM.Get("religion_holders_none"), hex: WideKit.Muted);
            int index = 0;
            foreach (IGrouping<Kingdom, City> group in holders.Take(MaxHolders))
            {
                Kingdom kingdom = group.Key;
                bool state = kingdom.religion == religion;
                string detail = state
                    ? string.Format(LM.Get("religion_holder_state"), group.Count())
                    : string.Format(LM.Get("religion_holder_minority"), group.Count(),
                        LM.Get("religion_policy_" + ReligiousTension.PolicyOf(kingdom)));
                card.AddListRow(PageWidth, index++, null, kingdom.GetKingdomName(), detail, () => OpenKingdom(kingdom),
                    state ? WideKit.Gold : WideKit.Red, 110f);
            }

            // 本教为国教的国家里的异教城
            var heterodox = _states.SelectMany(kingdom => kingdom.cities).Where(ReligiousTension.IsHeterodox).ToList();
            var tension = page.BeginCard(PageWidth, LM.Get("religion_card_heterodox"), WideKit.Gold, heterodox.Count.ToString());
            int row = 0;
            foreach (ReligiousTension.Policy policy in new[]
                         { ReligiousTension.Policy.Intolerant, ReligiousTension.Policy.PollTax, ReligiousTension.Policy.Tolerant })
            {
                int count = heterodox.Count(city => ReligiousTension.PolicyOf(city.kingdom) == policy);
                tension.AddValueRow(PageWidth, row++, LM.Get("religion_policy_" + policy),
                    string.Format(LM.Get("religion_heterodox_" + policy), count),
                    policy == ReligiousTension.Policy.Tolerant ? WideKit.Green : policy == ReligiousTension.Policy.PollTax ? WideKit.Orange : WideKit.Red);
            }
            int persecuted = heterodox.Count(city => ReligiousPersecution.StabilityLoss(city) > 0f);
            tension.AddValueRow(PageWidth, row, LM.Get("stability_persecution_detail"),
                string.Format(LM.Get("religion_heterodox_persecuted"), persecuted), persecuted > 0 ? WideKit.Red : WideKit.Soft);
        }

        private void SetDistribution(int filter)
        {
            _distributionFilter = filter;
            Refresh();
        }

        // 谱系
        private void DrawLineage(AutoVertLayoutGroup page)
        {
            Religion religion = _religion;
            Religion root = religion;
            for (int depth = 0; depth < 8 && ReligionDisplay.Parent(root) is Religion parent; depth++) root = parent;
            List<Religion> children = ReligionDisplay.Children(root);
            var absorbed = root.GetOrCreate().Absorbed ?? new List<ReligionExtension.ReligionEventData>();
            page.AddPageHeader(PageWidth, LM.Get("religion_page_lineage"),
                string.Format(LM.Get("religion_lineage_summary"), children.Count, absorbed.Count));

            var tree = page.BeginCard(PageWidth, LM.Get("religion_card_tree"), WideKit.Gold);
            int index = 0;
            AddTreeRow(tree, index++, root, "", religion);
            for (int i = 0; i < children.Count; i++)
            {
                Religion child = children[i];
                AddTreeRow(tree, index++, child, i == children.Count - 1 ? "└ " : "├ ", religion);
                foreach (Religion grandchild in ReligionDisplay.Children(child))
                    AddTreeRow(tree, index++, grandchild, "　└ ", religion);
            }

            var merged = page.BeginCard(PageWidth, LM.Get("religion_card_absorbed"), WideKit.Gold, absorbed.Count.ToString());
            if (absorbed.Count == 0) merged.AddParagraph(PageWidth, LM.Get("religion_absorbed_none"), hex: WideKit.Muted);
            index = 0;
            foreach (var entry in Enumerable.Reverse(absorbed).Take(MaxHolders))
                merged.AddListRow(PageWidth, index++, null, entry.text,
                    string.Format(LM.Get("religion_absorbed_at"), HistoryDateFormatter.GetYear(entry.at)), null, WideKit.Soft);

            if (children.Count == 0) return;
            var relations = page.BeginCard(PageWidth, LM.Get("religion_card_branch_relations"), WideKit.Gold);
            index = 0;
            foreach (Religion child in children)
            {
                bool heretic = ReligionDisplay.Heretic(child, out string until);
                relations.AddValueRow(PageWidth, index++, child.name,
                    heretic ? string.Format(LM.Get("religion_branch_heretic"), until) : LM.Get("religion_branch_peace"),
                    heretic ? WideKit.Red : WideKit.Green);
            }
        }

        private void AddTreeRow(AutoVertLayoutGroup card, int index, Religion religion, string branch, Religion current)
        {
            var data = religion.GetOrCreate();
            int cities = religion.cities?.Count ?? 0;
            string detail = data.ParentReligionId >= 0
                ? string.Format(LM.Get("religion_tree_split"), ReligionDisplay.SplitCause(religion), HistoryDateFormatter.GetYear(data.SplitAt),
                    cities)
                : string.Format(LM.Get("religion_tree_native"), HistoryDateFormatter.GetYear(religion.data.created_time), cities);
            Religion target = religion;
            card.AddListRow(PageWidth, index, null, branch + religion.name, detail,
                religion == current ? null : () => Open(target, Page.Lineage), religion == current ? WideKit.Gold : ReligionDisplay.TextHex(religion),
                120f);
        }

        // 圣战与骑士团
        private void DrawHolyWar(AutoVertLayoutGroup page)
        {
            Religion religion = _religion;
            if (!ReligionFaith.WagesHolyWar(religion))
            {
                page.AddEmptyCard(PageWidth, LM.Get("religion_page_holywar"), LM.Get("religion_no_holywar_hint"));
                return;
            }
            var data = religion.GetOrCreate();
            War active = PapacySystem.ActiveCrusade(religion);
            List<KnightOrderData> orders = PapacySystem.Orders(religion).ToList();
            page.AddPageHeader(PageWidth, LM.Get("religion_page_holywar"), ReligionDisplay.HolyWarName(religion));
            page.AddKpiRow(PageWidth,
                (LM.Get("religion_kpi_crusades"), data.CrusadeCount.ToString(), WideKit.Gold),
                (LM.Get("religion_kpi_crusade_losses"), data.CrusadeLosses.ToString(), data.CrusadeLosses > 0 ? WideKit.Red : WideKit.Green),
                (LM.Get("religion_row_crusade"), active != null ? LM.Get("religion_yes") : LM.Get("religion_no"),
                    active != null ? WideKit.Red : WideKit.Soft),
                (LM.Get("papacy_orders_title"), orders.Count.ToString(), WideKit.Cyan));

            var war = page.BeginCard(PageWidth, LM.Get("religion_card_crusade"), WideKit.Red);
            if (active == null)
                war.AddParagraph(PageWidth, World.world.getCurWorldTime() < data.CrusadeCooldownUntil
                    ? string.Format(LM.Get("religion_crusade_cooldown"), HistoryDateFormatter.GetYear(data.CrusadeCooldownUntil))
                    : LM.Get("papacy_crusade_idle"), hex: WideKit.Muted);
            else
            {
                war.AddValueRow(PageWidth, 0, LM.Get("religion_row_crusade"), active.data.name, WideKit.Red);
                war.AddValueRow(PageWidth, 1, LM.Get("religion_crusade_sides"),
                    string.Format(LM.Get("religion_crusade_sides_value"), active.main_attacker?.GetKingdomName() ?? "—",
                        active._list_attackers.Count, active.main_defender?.GetKingdomName() ?? "—", active._list_defenders.Count));
            }

            var card = page.BeginCard(PageWidth, LM.Get("papacy_orders_title"), WideKit.Gold, orders.Count.ToString());
            if (orders.Count == 0) card.AddParagraph(PageWidth, LM.Get("papacy_orders_hint"), hex: WideKit.Muted);
            for (int i = 0; i < orders.Count; i++)
            {
                KnightOrderData order = orders[i];
                List<CorpsData> corps = PapacySystem.OrderCorps(order);
                card.AddListRow(PageWidth, i, null, order.name, string.Format(LM.Get("papacy_order_detail"),
                    PapacySystem.GrandMaster(order)?.getName() ?? LM.Get("label_none"),
                    PapacySystem.Host(order)?.GetKingdomName() ?? LM.Get("label_none"),
                    corps.Count, UIHelper.ShortCount(corps.Sum(item => item.strength)), MoneyDisplay.Format(order.treasury)), titleWidth: 110f);
            }
        }

        // 大事记
        private void DrawChronicle(AutoVertLayoutGroup page)
        {
            List<ReligionExtension.ReligionEventData> events = ReligionChronicle.Events(_religion);
            page.AddPageHeader(PageWidth, LM.Get("religion_page_chronicle"), string.Format(LM.Get("religion_chronicle_count"), events.Count));
            page.AddChipRow(PageWidth,
                (LM.Get("religion_filter_all"), _chronicleKind < 0, () => SetChronicle(-1)),
                (ReligionDisplay.KindName(ReligionChronicle.KindPapacy), _chronicleKind == ReligionChronicle.KindPapacy,
                    () => SetChronicle(ReligionChronicle.KindPapacy)),
                (ReligionDisplay.KindName(ReligionChronicle.KindHolyWar), _chronicleKind == ReligionChronicle.KindHolyWar,
                    () => SetChronicle(ReligionChronicle.KindHolyWar)),
                (ReligionDisplay.KindName(ReligionChronicle.KindSchism), _chronicleKind == ReligionChronicle.KindSchism,
                    () => SetChronicle(ReligionChronicle.KindSchism)),
                (ReligionDisplay.KindName(ReligionChronicle.KindConversion), _chronicleKind == ReligionChronicle.KindConversion,
                    () => SetChronicle(ReligionChronicle.KindConversion)));
            List<ReligionExtension.ReligionEventData> shown = Enumerable.Reverse(events)
                .Where(entry => _chronicleKind < 0 || entry.kind == _chronicleKind).Take(MaxEvents).ToList();
            var card = page.BeginCard(PageWidth, LM.Get("religion_card_chronicle"), WideKit.Gold, shown.Count.ToString());
            if (shown.Count == 0)
            {
                card.AddParagraph(PageWidth, LM.Get("religion_chronicle_none"), hex: WideKit.Muted);
                return;
            }
            int index = 0;
            foreach (ReligionExtension.ReligionEventData entry in shown)
                card.AddTimelineRow(PageWidth, index++, HistoryDateFormatter.GetYear(entry.at), entry.text, ReligionDisplay.KindHex(entry.kind),
                    dateWidth: 56f);
        }

        private void SetChronicle(int kind)
        {
            _chronicleKind = kind;
            Refresh();
        }

        private static void OpenKingdom(Kingdom kingdom)
        {
            if (kingdom?.data == null || kingdom.isRekt()) return;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom != null && !empire.isRekt())
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
