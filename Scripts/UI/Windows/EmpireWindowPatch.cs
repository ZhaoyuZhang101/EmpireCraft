using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NCMS.Extensions;
using NCMS.Utils;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography.Xml;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Windows;

// Token: 0x0200069B RID: 1691
public class EmpireWindowPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static GameObject KingdomListElement;
    public static List<GameObject> _kingdomItems;

    public void Initialize()
    {
        //KingdomListElement = PrefabHelper.FindPrefabByName("list_element_kingdom");
        //new Harmony(nameof(empire_window_show)).Patch(AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.startShowingWindow)),
        //    prefix: new HarmonyMethod(GetType(), nameof(empire_window_show)));
        //new Harmony(nameof(apply_input_name)).Patch(AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.applyInputName)),
        //    prefix: new HarmonyMethod(GetType(), nameof(apply_input_name)));
        //new Harmony(nameof(set_stats_rows)).Patch(
        //    AccessTools.Method(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows)),
        //    prefix: new HarmonyMethod(GetType(), nameof(set_stats_rows))
        //);
    }



    public static bool empire_window_show(KingdomWindow __instance)
    {
        //if (_kingdomItems != null)
        //    ClearKingdomItems();
        //if (PlayerConfig.dict["map_empire_layer"].boolVal && __instance.meta_object.isInEmpire())
        //{
        //    initializeEmpireWindow(__instance);
        //    return false;
        //}
        return true;
    }

    public static void ClearKingdomItems()
    {
        // 反向遍历可以安全地在 Destroy 时移出引用
        for (int i = _kingdomItems.Count - 1; i >= 0; i--)
        {
            var go = _kingdomItems[i];
            if (go != null)
            {
                GameObject.Destroy(go);
                Debug.Log($"已移除：{go.name}");
            }
        }
        // 最后把列表清空，以便下一轮重建
        _kingdomItems.Clear();
    }
    private static bool set_stats_rows(KingdomWindow __instance)
    {
        countryLevel country_level = __instance.meta_object.GetCountryLevel();
        __instance.showStatRow("CountryLevel", LM.Get("default_" + country_level.ToString()), MetaType.None, -1L, null, LM.Get("CountryLevel"), null);
        if (__instance.meta_object.isInEmpire() && !__instance.meta_object.isEmpire())
        {
            __instance.showStatRow("EmpireText", __instance.meta_object.GetEmpire().name, __instance.meta_object.GetEmpire().empire.getColor().color_text, MetaType.Kingdom, -1L, false, null, null, null, true);
            __instance.showStatRow("EmpireLoyalty", __instance.meta_object.GetLoyalty(), "#43FF43", MetaType.None, -1L, false, null, null, null, true);
            __instance.showStatRow("EmpireTaxRate", __instance.meta_object.GetTaxtRate(), "#43FF43", MetaType.None, -1L, false, null, null, null, true);
        }
        if (PlayerConfig.dict["map_empire_layer"].boolVal && __instance.meta_object.isInEmpire())
        {
            if (__instance.meta_object == null)
            {
                return false;
            }
            showStatsRows(__instance);
            return false;
        }
        return true;
    }
    public static void initializeEmpireWindow(KingdomWindow window)
    {

        Empire empire = window.meta_object.GetEmpire();
        Config.selected_kingdom = empire.empire;
        window.clear();
        loadNameInput(window);
        window.loadBanners();
        var go = window.transform.Find("Background/Scroll View/Viewport/Content");
        // 然后
        if (KingdomListElement == null)
        {

            KingdomListElement = PrefabHelper.FindPrefabByName("list_element_kingdom");
        }
        window.refreshMetaList();
        foreach (Kingdom k in empire.kingdoms_hashset)
        {


        }

    }

    // Token: 0x06003026 RID: 12326 RVA: 0x0017F784 File Offset: 0x0017D984
    public static void loadNameInput(KingdomWindow window)
    {
        Empire empire = window.meta_object.GetEmpire();
        window._name_input.setText(empire.data.name);
        ColorAsset tColorAsset = empire.getColor();
        if (tColorAsset != null)
        {
            window._name_input.textField.color = tColorAsset.getColorText();
            return;
        }
        window._name_input.textField.color = Toolbox.color_white;
    }

    public static bool apply_input_name(KingdomWindow __instance, string pInput)
    {
        if (PlayerConfig.dict["map_empire_layer"].boolVal && __instance.meta_object.isInEmpire())
        {
            if (pInput == null)
            {
                return false;
            }
            if (__instance.meta_object == null)
            {
                return false;
            }

            Empire empire = __instance.meta_object.GetEmpire();
            empire.data.name = pInput;
            return false;
        }
        return true;
    }

    public static void showStatsRows(KingdomWindow window)
    {
        Kingdom tKingdom = window.meta_object;
        window.showStatRow("founded", tKingdom.getFoundedDate(), MetaType.None, -1L, "iconAge", null, null);
        window.tryToShowActor("emperor", -1L, null, tKingdom.king, "iconKings");
        window.showStatRow("past_emperors", tKingdom.data.total_kings, MetaType.None, -1L, "iconKingdomList", "past_rulers", new TooltipDataGetter(window.getTooltipPastRulers));
        Actor tHeir = SuccessionTool.findNextHeir(tKingdom, tKingdom.king);
        window.tryToShowActor("heir", -1L, null, tHeir, "iconChildren");
    }
}