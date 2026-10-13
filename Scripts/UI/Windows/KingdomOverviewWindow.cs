using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows;

// 国情(「卷册」宽窗口，组件见 WideScrollWindow / WideKit)：帝国图层上点不属于帝国、也没被帝国覆盖的国家时打开，
// 只放看一国时最常看的模组信息，详细的再跳到各自的窗口：
//   · 左栏(固定)：国名、国家类别、身份(独立 / 某朝诸侯 / 某帝国朝贡国 / 某国附庸) → 君主、首都、政体、文化、宗教、同盟
//     → 军事、政体、科技树、原版王国界面；
//   · 右栏：页头(身份 · 战事) → 主指标(国力与排名、城池、人口、兵力) → 实力 | 经济 两栏 → 科技 → 战事(一场一行，点开战争详情)。
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
        if (kingdom.religion != null && !kingdom.religion.isRekt())
            stats.AddRailRow(width, LM.Get("kov_religion"), kingdom.religion.name, WideKit.Value, row++);
        Alliance alliance = kingdom.getAlliance();
        stats.AddRailRow(width, LM.Get("kov_alliance"), alliance?.name ?? LM.Get("label_none"),
            alliance == null ? WideKit.Muted : WideKit.Value, row);

        rail.AddRailButton("kov_open_military", LM.Get("kingdom_military"), () => KingdomMilitaryWindow.Open(kingdom));
        rail.AddRailButton("kov_open_regime", LM.Get("kov_open_regime"), () =>
        {
            SelectedMetas.selected_kingdom = kingdom;
            ScrollWindow.showWindow(nameof(RegimeWindow));
        });
        rail.AddRailButton("kov_open_tech", LM.Get("TechWindow Title"), () => TechWindow.Open(kingdom));
        rail.AddRailButton("kov_open_vanilla", LM.Get("kov_open_vanilla"), () =>
        {
            SelectedMetas.selected_kingdom = kingdom;
            ScrollWindow.showWindow("kingdom");
        });
    }

    // 身份：周室诸侯 / 朝贡国 / 封建附庸 / 独立
    private static string Standing(Kingdom kingdom)
    {
        Empire sphere = ZhouEraSystem.SphereOf(kingdom);
        if (sphere != null) return string.Format(LM.Get("kov_standing_zhou"), sphere.GetEmpireName());
        Empire suzerain = kingdom.HasTakenAlliance() ? kingdom.GetTakenAllianceEmpire() : null;
        if (suzerain != null && !suzerain.isRekt()) return string.Format(LM.Get("kov_standing_tributary"), suzerain.GetEmpireName());
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
        DrawTech(page, kingdom);
        DrawWars(page, kingdom);
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
        string current = state?.current_tech;
        string detail = LM.Get("kov_tech_idle");
        if (!string.IsNullOrEmpty(current) && TechnologySystem.TryGetTech(current, out TechNodeConfig tech))
        {
            float cost = TechnologySystem.GetCost(culture, tech);
            float share = cost <= 0f ? 0f : TechnologySystem.GetProgress(culture, current) / cost;
            detail = $"{TechnologySystem.GetTechName(current)}  {UnityEngine.Mathf.Clamp01(share):P0}";
        }
        card.AddListRow(PageWidth, 0, null, LM.Get("kov_tech_current"), detail, () => TechWindow.Open(kingdom), WideKit.Cyan, 70f);
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
