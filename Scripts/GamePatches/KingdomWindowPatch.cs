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
            simpleWindowTab.Setup("regime", __instance.scroll_window, action:(_) => ShowRegime(), sprite:SpriteTextureLoader.getSprite("ui/regime"));
        }
    }
    public static bool showStatsRows(KingdomWindow __instance)
    {
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
            __instance.showStatRow("ruler_money", (object) metaObject.GetMoney(), "#43FF43", pIconPath: "iconMoney");
            if (metaObject.GetKingdomType() == KingdomType.Feudalism_papal_state)
            {
                __instance.showStatRow("religion_point", (object) metaObject.GetRegime().religion_point, "#43FF43", pIconPath: "iconMoney");
            }
        }
        
        __instance.showStatRow("tribute", (object) metaObject.GetTaxRate().ToString("0%"), "#43FF43", pIconPath: "kingdom_traits/kingdom_trait_tax_rate_tribute_high");
        __instance.tryToShowMetaSpecies("founder_species", metaObject.getFounderSpecies().id);
        return false;
    }
    private static void ShowRegime()
    {
        ScrollWindow.showWindow(nameof(RegimeWindow));
        LogService.LogInfo($"开启RegimeWindow");
    }
}