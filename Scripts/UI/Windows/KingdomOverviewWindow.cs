using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows;

// 国情(「卷册」宽窗口，组件见 WideScrollWindow / WideKit)：帝国图层上点不属于帝国、也没被帝国覆盖的国家时打开，
// 只放看一国时最常看的模组信息，详细的再跳到各自的窗口：
//   · 左栏(固定)：国名、国家类别、身份(独立 / 某朝诸侯 / 某帝国朝贡国 / 某国附庸) → 君主、首都、政体、文化、宗教、同盟
//     → 军事、政体、科技树、原版王国界面；
//   · 右栏：页头(身份 · 战事) → 主指标(国力与排名、城池、人口、兵力) → 实力 | 经济 两栏 → 物资产需(见 GoodsCard) → 科技
//     → 战事(一场一行，点开战争详情)。
public class KingdomOverviewWindow : WideScrollWindow<KingdomOverviewWindow>
{
    private const float HalfWidth = UIHelper.PageColumnWidth;
    private static long _pending = -1L;
    private Kingdom _kingdom;
    private List<War> _wars = new();

    public static void Open(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        _pending = kingdom.id;
        SelectedMetas.selected_kingdom = kingdom;
        SelectedObjects.setNanoObject(kingdom);
        ScrollWindow.showWindow(nameof(KingdomOverviewWindow));
    }

    protected override void OnOpen()
    {
        _kingdom = World.world.kingdoms.get(_pending) ?? SelectedMetas.selected_kingdom;
    }

    protected override void Prepare()
    {
        _wars = Alive(_kingdom)
            ? _kingdom.getWars().Where(war => war != null && !war.isRekt() && !war.hasEnded())
                .OrderByDescending(war => war.main_attacker == _kingdom || war.main_defender == _kingdom)
                .ThenByDescending(war => war.getAge()).ToList()
            : new List<War>();
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (!Alive(_kingdom)) return;
        Kingdom kingdom = _kingdom;
        var identity = rail.BeginRailCard("FactionFrame_dominate");
        identity.AddRailText(kingdom.GetKingdomFullName().ColorString(kingdom.getColor().color_text), 10, 26f, wrap: true);
        identity.AddRailText(LM.Get(kingdom.GetKingdomTypeDisplayKey()).ColorString(WideKit.Cyan), 7, 10f);
        identity.AddRailText(Standing(kingdom).ColorString(WideKit.Muted), 6, 9f);

        var stats = rail.BeginRailCard();
        float width = UIHelper.RailWidth - 8f;
        int row = 0;
        if (kingdom.hasKing())
            stats.AddRailRow(width, LM.Get("village_statistics_king"), kingdom.king.getName(), WideKit.Gold, row++);
        if (kingdom.hasCapital())
            stats.AddRailRow(width, LM.Get("empire_rail_capital"), kingdom.capital.GetCityName(), WideKit.Value, row++);
        var regime = kingdom.GetRegime();
        if (regime != null)
            stats.AddRailRow(width, LM.Get("kov_regime"), LM.Get("regime_type_" + regime.type), WideKit.Value, row++);
        stats.AddRailRow(width, LM.Get("kov_culture"), CultureName(CultureService.GetRealmCulture(kingdom)), WideKit.Value, row++);
        // 旧制犹存：改从了文明更落后的文化，政体与制度仍照旧制所属的文化(见 LegacyInstitutionSystem)，悬停看缘由与何时作罢
        if (LegacyInstitutionSystem.TryGet(kingdom, out string legacy))
        {
            SimpleText legacyText = stats.AddRailText(LegacyInstitutionSystem.Label(legacy).ColorString("#E9A85B"), 6, 9f);
            UIHelper.AttachTextTooltip(legacyText.gameObject, $"legacy_institution_{kingdom.id}",
                LM.Get("legacy_institution_tip_title"), LegacyInstitutionSystem.Tooltip(kingdom, legacy));
        }
        if (kingdom.religion != null && !kingdom.religion.isRekt())
            stats.AddRailRow(width, LM.Get("kov_religion"), kingdom.religion.name, WideKit.Value, row++);
        Alliance alliance = kingdom.getAlliance();
        stats.AddRailRow(width, LM.Get("kov_alliance"), alliance?.name ?? LM.Get("label_none"),
            alliance == null ? WideKit.Muted : WideKit.Value, row);

        // 自定义地图色(#RRGGBB，留空恢复原版颜色，见 CustomColorSystem)
        var look = rail.BeginRailCard();
        TextInput colorInput = null;
        colorInput = look.AddRailInput(LM.Get("custom_color_label"), CustomColorSystem.Hex(kingdom), text =>
        {
            if (!CustomColorSystem.Set(kingdom, text)) colorInput?.input.SetTextWithoutNotify(CustomColorSystem.Hex(kingdom));
        });
        colorInput.input.characterLimit = 7;
        rail.AddRailButton("kov_open_military", LM.Get("kingdom_military"), () => KingdomMilitaryWindow.Open(kingdom));
        rail.AddRailButton("kov_open_diplomacy", LM.Get("diplomacy_window"), () => DiplomacyWindow.Open(kingdom));
        rail.AddRailButton("kov_open_market", LM.Get("market_window"), () => MarketWindow.Open(kingdom));
        rail.AddRailButton("kov_open_regime", LM.Get("kov_open_regime"), () =>
        {
            SelectedMetas.selected_kingdom = kingdom;
            ScrollWindow.showWindow(nameof(RegimeWindow));
        });
        rail.AddRailButton("kov_open_tech", LM.Get("TechWindow Title"), () => TechWindow.Open(kingdom));
        rail.AddRailButton("kov_open_institutions", LM.Get("kingdom_institutions"), () => InstitutionWindow.Open(kingdom));
        rail.AddRailButton("kov_open_vanilla", LM.Get("kov_open_vanilla"), () =>
        {
            SelectedMetas.selected_kingdom = kingdom;
            ScrollWindow.showWindow("kingdom");
        });
    }

    // 身份：周室诸侯 / 帝国之主、帝国治下 / 朝贡国 / 封建附庸 / 独立。
    // 以前不看帝国：律令制的封国、藩王(国名同样带"国")在帝国里也显示"独立之国"(用户 2026-10-27 问"为什么迟迟不打燕国""他是独立国")
    private static string Standing(Kingdom kingdom)
    {
        Empire sphere = ZhouEraSystem.SphereOf(kingdom);
        if (sphere != null) return string.Format(LM.Get("kov_standing_zhou"), sphere.GetEmpireName());
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.isRekt() && !empire.IsArchived())
            return string.Format(LM.Get(empire.CoreKingdom == kingdom ? "kov_standing_empire_core" : "kov_standing_empire_member"),
                empire.GetEmpireName());
        Empire suzerain = kingdom.HasTakenAlliance() ? kingdom.GetTakenAllianceEmpire() : null;
        if (suzerain != null && !suzerain.isRekt()) return string.Format(LM.Get("kov_standing_tributary"), suzerain.GetEmpireName());
        Kingdom metropole = ColonySystem.MetropoleOf(kingdom);
        if (Alive(metropole))
            return string.Format(LM.Get(ColonySystem.Of(kingdom)?.kind == ColonyState.Company ? "kov_standing_company" : "kov_standing_colony"),
                metropole.GetKingdomName());
        Kingdom lord = FeudalVassalService.GetOverlord(kingdom);
        if (Alive(lord)) return string.Format(LM.Get("kov_standing_vassal"), lord.GetKingdomName());
        return LM.Get("kov_standing_independent");
    }

    private static string CultureName(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return LM.Get("label_none");
        return OnomasticsRule.ALL_CULTURE_TRANSLATE.ContainsKey(culture) ? culture.GetCultureTranslate() : culture;
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (!Alive(_kingdom))
        {
            page.AddEmptyCard(PageWidth, "kov_gone");
            return;
        }
        Kingdom kingdom = _kingdom;
        page.AddPageHeader(PageWidth, "kov_page", Standing(kingdom),
            _wars.Count > 0 ? string.Format(LM.Get("empire_rail_wars_value"), _wars.Count) : LM.Get("empire_rail_peace"));

        double power = kingdom.GetNationalPower();
        int rank = 1 + World.world.kingdoms.list.Count(other => other != kingdom && Alive(other) && !other.isNeutral() &&
                                                                 other.GetNationalPower() > power);
        page.AddKpiRow(PageWidth,
            (LM.Get("kov_kpi_power"), string.Format(LM.Get("kov_power_rank"), UIHelper.ShortCount(power), rank), WideKit.Gold),
            (LM.Get("kov_kpi_cities"), kingdom.countCities().ToString(), WideKit.Green),
            (LM.Get("kov_kpi_population"), UIHelper.ShortCount(kingdom.getPopulationPeople()), WideKit.Cyan),
            (LM.Get("kov_kpi_army"), UIHelper.ShortCount(kingdom.countTotalWarriors()), WideKit.Orange));

        var columns = page.BeginColumns(PageWidth);
        DrawStrength(columns.BeginColumnCard(HalfWidth, "kov_strength", WideKit.Orange, null, "ui/icons/iconWar"), kingdom);
        DrawEconomy(columns.BeginColumnCard(HalfWidth, "kov_economy", WideKit.Green, null, "ui/icons/iconMoney"), kingdom);
        GoodsCard.Draw(page, PageWidth, kingdom.cities, GranarySystem.Stock(kingdom));
        MarketCard.Draw(page, PageWidth, kingdom, new[] { kingdom });
        DrawColonies(page, kingdom);
        DrawTech(page, kingdom);
        DrawWars(page, kingdom);
    }

    // 殖民关系：殖民地写宗主国、总督府还是特许公司、现任总督、离心；宗主国列出各殖民地。点一行打开那一国(用户："我没法看到殖民政府和宗主国的关系")
    private static void DrawColonies(AutoVertLayoutGroup page, Kingdom kingdom)
    {
        Kingdom metropole = ColonySystem.MetropoleOf(kingdom);
        List<Kingdom> colonies = ColonySystem.ColoniesOf(kingdom).Where(colony => Alive(colony)).ToList();
        if (!Alive(metropole) && colonies.Count == 0) return;
        var card = page.BeginCard(PageWidth, "kov_colonies", WideKit.Cyan,
            Alive(metropole) ? metropole.GetKingdomName() : string.Format(LM.Get("kov_colony_count"), colonies.Count), "ui/icons/iconKingdom");
        if (Alive(metropole))
        {
            ColonyState state = ColonySystem.Of(kingdom);
            Kingdom lord = metropole;
            card.AddListRow(PageWidth, 0, null, LM.Get("kov_colony_metropole"), metropole.GetKingdomFullName(),
                () => Open(lord), WideKit.Cyan, 90f);
            Actor governor = state == null ? null : World.world.units.get(state.governor_id);
            card.AddMetricRow(PageWidth,
                (LM.Get("kov_colony_kind"), LM.Get("kov_colony_kind_" + (state?.kind ?? ColonyState.Governorate)), WideKit.Gold),
                (LM.Get("kov_colony_governor"), governor != null && !governor.isRekt() ? governor.getName() : LM.Get("label_none"), WideKit.Value),
                (LM.Get("kov_colony_autonomy"), $"{state?.autonomy ?? 0f:0}", (state?.autonomy ?? 0f) >= 60f ? WideKit.Red : WideKit.Value));
            return;
        }
        for (int i = 0; i < colonies.Count; i++)
        {
            Kingdom colony = colonies[i];
            ColonyState state = ColonySystem.Of(colony);
            string detail = string.Format(LM.Get("kov_colony_row"), LM.Get("kov_colony_kind_" + (state?.kind ?? ColonyState.Governorate)),
                colony.countCities(), state?.autonomy ?? 0f);
            card.AddListRow(PageWidth, i, null, colony.GetKingdomName(), detail, () => Open(colony), WideKit.Cyan, 160f);
        }
    }

    private static void DrawStrength(AutoVertLayoutGroup card, Kingdom kingdom)
    {
        int level = WarScopeSystem.Level(kingdom);
        if (CorpsSystem.Enabled)
        {
            List<CorpsData> corps = CorpsSystem.Of(kingdom);
            card.AddMetricRow(HalfWidth,
                (LM.Get("corps_kpi_count"), corps.Count.ToString(), WideKit.Value),
                (LM.Get("corps_kpi_strength"), CorpsSystem.ShortStrength(corps.Sum(entry => entry.strength)), WideKit.Green));
        }
        int battles = FrontSystem.Battles.Values.Count(battle => battle.attacker_id == kingdom.id || battle.defender_id == kingdom.id);
        card.AddMetricRow(HalfWidth,
            (LM.Get("kov_mobilization"), LM.Get("kov_mobilization_" + level), level > 0 ? WideKit.Red : WideKit.Value),
            (LM.Get("corps_kpi_battles"), battles.ToString(), battles > 0 ? WideKit.Red : WideKit.Muted));
    }

    private static void DrawEconomy(AutoVertLayoutGroup card, Kingdom kingdom)
    {
        double corruption = CorruptionSystem.GetRate(kingdom);
        if (TreasurySystem.Enabled(kingdom))
        {
            TreasuryReport report = TreasurySystem.Report(kingdom);
            card.AddMetricRow(HalfWidth,
                (LM.Get("label_national_treasury"), MoneyDisplay.Format(kingdom.GetTreasuryBalance()), WideKit.Gold),
                (LM.Get("kov_balance"), MoneyDisplay.Format(report.operating_balance),
                    report.operating_balance < 0 ? WideKit.Red : WideKit.Green));
        }
        else
            card.AddMetricRow(HalfWidth, (LM.Get("label_national_treasury"), MoneyDisplay.Format(kingdom.GetMoney()), WideKit.Gold));
        card.AddMetricRow(HalfWidth,
            (LM.Get("empire_rail_corruption"), $"{corruption:P0}", corruption >= 0.5d ? WideKit.Red : WideKit.Value),
            (LM.Get("granary"), UIHelper.ShortCount(GranarySystem.Stock(kingdom)), WideKit.Value));
    }

    // 科技按文化算：时代、已研究、年研究点、眼下在研究的技术与进度
    private static void DrawTech(AutoVertLayoutGroup page, Kingdom kingdom)
    {
        string culture = TechnologySystem.GetCultureOf(kingdom);
        var card = page.BeginCard(PageWidth, "kov_tech", WideKit.Cyan, CultureName(culture), "ui/icons/iconKnowledge");
        if (string.IsNullOrWhiteSpace(culture) || !TechnologySystem.IsActiveFor(culture))
        {
            card.AddListRow(PageWidth, 0, null, LM.Get("kov_tech_off"), "", titleWidth: 220f);
            return;
        }
        CultureTechState state = TechnologySystem.GetState(culture);
        int total = TechnologySystem.Techs.Count();
        card.AddMetricRow(PageWidth,
            (LM.Get("kov_tech_era"), TechnologySystem.GetEraName(culture), WideKit.Gold),
            (LM.Get("kov_tech_researched"), $"{state?.researched_techs?.Count ?? 0}/{total}", WideKit.Value),
            (LM.Get("kov_tech_points"), (state?.last_yearly_points ?? 0f).ToString("0.#"), WideKit.Green));
        var targets = TechnologySystem.GetResearchTargets(culture);
        if (targets.Count == 0)
            card.AddListRow(PageWidth, 0, null, LM.Get("kov_tech_current"), LM.Get("kov_tech_idle"),
                () => TechWindow.Open(kingdom), WideKit.Cyan, 70f);
        for (int i = 0; i < targets.Count; i++)
        {
            TechNodeConfig tech = targets[i];
            float cost = TechnologySystem.GetCost(culture, tech);
            float share = cost <= 0f ? 0f : TechnologySystem.GetProgress(culture, tech.id) / cost;
            string detail = $"{TechnologySystem.GetTechName(tech.id)}  {UnityEngine.Mathf.Clamp01(share):P0}";
            card.AddListRow(PageWidth, i, null, LM.Get("kov_tech_current"), detail,
                () => TechWindow.Open(kingdom), WideKit.Cyan, 70f);
        }
    }

    // 战事：一场一行(攻守、对手、年数、双方死伤)，点开战争详情
    private void DrawWars(AutoVertLayoutGroup page, Kingdom kingdom)
    {
        var card = page.BeginCard(PageWidth, "empire_rail_wars", WideKit.Red,
            _wars.Count > 0 ? string.Format(LM.Get("empire_rail_wars_value"), _wars.Count) : LM.Get("empire_rail_peace"),
            "ui/icons/iconWar");
        if (_wars.Count == 0)
        {
            card.AddListRow(PageWidth, 0, null, LM.Get("kov_no_war"), "", titleWidth: 220f);
            return;
        }
        UnityEngine.Sprite icon = SpriteTextureLoader.getSprite(WarBadge.IconPath);
        int index = 0;
        foreach (War war in _wars)
        {
            bool attacker = war.isAttacker(kingdom);
            Kingdom foe = attacker ? war.main_defender : war.main_attacker;
            int ours = attacker ? war.getDeadAttackers() : war.getDeadDefenders();
            int theirs = attacker ? war.getDeadDefenders() : war.getDeadAttackers();
            string detail = string.Format(LM.Get("kov_war_detail"),
                LM.Get(attacker ? "kov_war_attack" : "kov_war_defend"), foe?.GetKingdomName() ?? "?", war.getAge(),
                UIHelper.ShortCount(ours), UIHelper.ShortCount(theirs));
            War target = war;
            card.AddListRow(PageWidth, index++, icon, war.data.name, detail, () =>
            {
                if (target.isRekt()) return;
                SelectedMetas.selected_war = target;
                ScrollWindow.showWindow("war");
            }, attacker ? "#FFB3A8" : "#A8CCFF", 120f);
        }
    }
}
