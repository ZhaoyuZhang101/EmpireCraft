using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EmpireCraft.Scripts.GamePatches;

public class KingdomWindowPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public static Kingdom _kingdom  { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(OnEnable)).Patch(
            AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.OnEnable)),
            prefix: new HarmonyMethod(GetType(), nameof(OnEnable))
        );   
        new Harmony(nameof(showStatsRows)).Patch(
            AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows)),
            prefix: new HarmonyMethod(GetType(), nameof(showStatsRows))
        );     
    }

    public static void OnEnable(KingdomWindow __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(SelectedMetas.selected_kingdom)) return;
        if (__instance.meta_type != MetaType.Kingdom) return;
        _kingdom = SelectedMetas.selected_kingdom;
        Transform space = __instance.tabs.transform.Find("space (1)");
        if (space != null)
        {
            Object.Destroy(space.gameObject);
        }
        if (__instance.tabs._tabs.All(p => p.name != "regime"))
        {
            SimpleWindowTab simpleWindowTab = Object.Instantiate(SimpleWindowTab.Prefab);
            simpleWindowTab.Setup("regime", __instance.scroll_window, action:(_) => ShowRegime(), sprite:SpriteTextureLoader.getSprite("TabConstitution"));
        }
        if (__instance.tabs._tabs.All(p => p.name != "technology"))
        {
            SimpleWindowTab techTab = Object.Instantiate(SimpleWindowTab.Prefab);
            techTab.Setup("technology", __instance.scroll_window, action:(_) => TechWindow.Open(_kingdom),
                sprite:SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
        }
    }
    public static bool showStatsRows(KingdomWindow __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(SelectedMetas.selected_kingdom)) return true;
        Kingdom metaObject = __instance.meta_object;
        __instance.tryShowPastNames();
        if (metaObject.HasMainTitle())
        {
            __instance.showStatRow("main_title", (object) metaObject.GetMainTitle().name, "#43FF43", pIconPath: "iconKings");
        }
        __instance.showStatRow("founded", (object) metaObject.getFoundedDate(), MetaType.None, -1L, "iconAge", (string) null, (TooltipDataGetter) null);
        __instance.tryShowPastRulers();
        __instance.tryToShowActor("king", pObject: metaObject.king, pIconPath: "iconKings");
        __instance.tryToShowActor("heir", pObject: SuccessionTool.findNextHeir(metaObject, metaObject.king), pIconPath: "iconChildren");
        if (metaObject.hasKing())
        {
            if (metaObject.king.s_personality != null)
                __instance.showStatRow("creature_statistics_personality", (object) metaObject.king.s_personality.getTranslatedName(), MetaType.None, -1L, "actor_traits/iconStupid", (string) null, (TooltipDataGetter) null);
            __instance.showStatRow("kingdom_statistics_king_ruled", (object) Date.getYearsSince(metaObject.data.timestamp_king_rule), MetaType.None, -1L, "iconClock", (string) null, (TooltipDataGetter) null);
            if (!metaObject.wild)
                __instance.showStatRow("plan_policy", EmpireCraft.Scripts.GeneralSystems.ZonePlanSystem.PolicyName(
                    EmpireCraft.Scripts.GeneralSystems.ZonePlanSystem.PolicyOf(metaObject)), "#F3C34A", pIconPath: "iconCity");
            if (metaObject.GetKingdomType() == KingdomType.Feudalism_papal_state)
            {
                __instance.showStatRow("religion_point", (object) metaObject.GetRegime().religion_point, "#43FF43", pIconPath: "iconMoney");
            }
        }
        
        __instance.showStatRow("ruler_money", EmpireCraft.Scripts.HelperFunc.MoneyDisplay.Format(metaObject.GetTreasuryBalance()), "#43FF43", pIconPath: "iconMoney");
        if (EmpireCraft.Scripts.GeneralSystems.TreasurySystem.Enabled(metaObject))
        {
            var fiscal = EmpireCraft.Scripts.GeneralSystems.TreasurySystem.Report(metaObject);
            __instance.showStatRow("fiscal_safety_reserve", EmpireCraft.Scripts.HelperFunc.MoneyDisplay.Format(
                EmpireCraft.Scripts.GeneralSystems.TreasurySystem.SafetyReserve(metaObject)), "#E6A166", pIconPath: "iconMoney");
            __instance.showStatRow("fiscal_cash_balance", EmpireCraft.Scripts.HelperFunc.MoneyDisplay.Format(
                fiscal.cash_balance), fiscal.cash_balance >= 0 ? "#66D98A" : "#E66B66", pIconPath: "iconMoney");
            __instance.showStatRow("fiscal_available", EmpireCraft.Scripts.HelperFunc.MoneyDisplay.Format(
                EmpireCraft.Scripts.GeneralSystems.StateSettlementSystem.DiscretionaryBalance(metaObject)), "#43FF43", pIconPath: "iconMoney");
            long stateDebt = EmpireCraft.Scripts.GeneralSystems.StateDebtSystem.Outstanding(metaObject);
            if (stateDebt > 0L)
                __instance.showStatRow("state_debt", string.Format(NeoModLoader.General.LM.Get("state_debt_format"),
                        EmpireCraft.Scripts.HelperFunc.MoneyDisplay.Format(stateDebt),
                        EmpireCraft.Scripts.GeneralSystems.StateDebtSystem.AverageRate(metaObject)),
                    EmpireCraft.Scripts.GeneralSystems.StateDebtSystem.DefaultedRecently(metaObject) ? "#E66B66" : "#E6A166",
                    pIconPath: "iconMoney");
            double budgetRatio = EmpireCraft.Scripts.GeneralSystems.FiscalBudgetPlanner.NormalRatio(metaObject);
            if (budgetRatio < 0.999d)
                __instance.showStatRow("fiscal_budget_ratio", string.Format(NeoModLoader.General.LM.Get("fiscal_budget_ratio_format"), budgetRatio),
                    "#E6A166", pIconPath: "iconMoney");
            __instance.showStatRow("fiscal_flows", EmpireCraft.Scripts.GeneralSystems.TreasurySystem.FlowText(fiscal), "#B8C6CC", pIconPath: "iconMoney");
            __instance.showStatRow("fiscal_transfers", EmpireCraft.Scripts.GeneralSystems.TreasurySystem.TransferText(fiscal), "#B8C6CC", pIconPath: "iconMoney");
            __instance.showStatRow("fiscal_spending", EmpireCraft.Scripts.GeneralSystems.TreasurySystem.SpendingText(fiscal), "#F3C34A", pIconPath: "iconMoney");
            __instance.showStatRow("fiscal_operating_balance", EmpireCraft.Scripts.HelperFunc.MoneyDisplay.Format(fiscal.operating_balance),
                fiscal.operating_balance >= 0 ? "#66D98A" : "#E66B66", pIconPath: "iconMoney");
            foreach (var item in EmpireCraft.Scripts.GeneralSystems.TreasurySystem.Details(fiscal))
                __instance.showStatRow(item.key, item.value, "#B8C6CC", pIconPath: "iconMoney");
        }
        if (EmpireCraft.Scripts.GeneralSystems.TreasurySystem.Enabled(metaObject))
        {
            __instance.showStatRow("tax", metaObject.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_local_low");
            __instance.showStatRow("fiscal_tax_shares", EmpireCraft.Scripts.GeneralSystems.TreasurySystem.TaxSharingText(metaObject),
                "#B8C6CC", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_tribute_high");
        }
        else
            __instance.showStatRow("tribute", (object) metaObject.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_tribute_high");
        if (EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled && !metaObject.wild)
            __instance.showStatRow("state_settlement", EmpireCraft.Scripts.GeneralSystems.StateSettlementSystem.Status(metaObject),
                "#F3C34A", pIconPath: "iconCity");
        __instance.showStatRow("national_power", (object) metaObject.GetNationalPower().ToString("0.##"), "#FFD34E", pIconPath: "iconKings");
        if (EmpireCraft.Scripts.GeneralSystems.TreasurySystem.Enabled(metaObject) &&
            EmpireCraft.Scripts.GeneralSystems.ExclaveMaintenanceSystem.Status(metaObject) is string exclaveStatus)
            __instance.showStatRow("exclave_maintenance", exclaveStatus, "#E6D36A", pIconPath: "iconMoney");
        __instance.tryToShowMetaSpecies("founder_species", metaObject.getFounderSpecies().id);
        return false;
    }
    private static void ShowRegime()
    {
        ScrollWindow.showWindow(nameof(RegimeWindow));
    }
}
