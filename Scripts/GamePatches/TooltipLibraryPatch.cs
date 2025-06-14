using EmpireCraft.Scripts;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class TooltipLibraryPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(add_empire_tool_tip)).Patch(
            AccessTools.Method(typeof(TooltipLibrary), nameof(TooltipLibrary.showKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(add_empire_tool_tip))
        );
    }

    public static bool add_empire_tool_tip(TooltipLibrary __instance, Tooltip pTooltip, string pType, TooltipData pData)
    {
        if (PlayerConfig.dict["map_empire_layer"].boolVal && pData.kingdom.isInEmpire())
        {
            showEmpireToolTip(pTooltip, pType, pData);
            return false;
        }
        return true;
    }

    public static void showEmpireToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        pTooltip.clearTextRows();
        pTooltip.clearStats();
        Kingdom tKingdom = pData.kingdom.GetEmpire().empire;
        Empire pEmpire = ModClass.EMPIRE_MANAGER.get(tKingdom.GetEmpireID());
        pTooltip.setDescription(tKingdom.getMotto(), null);
        string tColorHex = tKingdom.getColor().color_text;
        pTooltip.setTitle(pEmpire.name, LM.Get("EmpireText"), tColorHex);
        int tAge = pEmpire.getAge();
        setIconValue(pTooltip, "i_age", (float)tAge, "", "");
        setIconValue(pTooltip, "i_population", (float)pEmpire.countPopulation(), "", "");
        setIconValue(pTooltip, "i_army", (float)pEmpire.countWarriors(), "", "");
        string pValue = "-";
        if (pEmpire.empire.hasKing())
        {
            pValue = pEmpire.empire.king.getName();
        }
        pTooltip.addLineText(LM.Get("emperor"), pValue, "#FE9900", false, true, 21);
        if (tKingdom.hasKing() && tKingdom.king.hasClan())
        {
            pTooltip.addLineText(LM.Get("empire_clan"), tKingdom.king.clan.data.name, tKingdom.king.clan.getColor().color_text, false, true, 21);
        }
        pTooltip.addLineText(LM.Get("empire_capital"), pEmpire.empire.data.name, "#CC6CE7", false, true, 21);
        pTooltip.addLineText(LM.Get("year_name"), "-", "#FE9900", false, true, 21);
        pTooltip.addLineBreak();
        pTooltip.addLineText(LM.Get("current_selected_province"), pData.kingdom.data.name, pData.kingdom.getColor().color_text, false, true, 21);
        string color = tKingdom.getColor().color_text;
        string leaderName = "-";
        if (pData.kingdom.hasKing()) 
        { 
            leaderName = pData.kingdom.king.name;
            if (pData.kingdom.king.hasClan())
            {
                color = pData.kingdom.king.clan.getColor().color_text;
            }
        }
        pTooltip.addLineText(LM.Get("province_leader"), leaderName, color, false, true, 21);
        pTooltip.addLineText(LM.Get("province_level"), LM.Get("default_"+ pData.kingdom.GetCountryLevel().ToString()), tKingdom.getColor().color_text, false, true, 21);
        pTooltip.addLineBreak();
        pTooltip.addLineIntText(
            LM.Get("adults"),
            pEmpire.countAdults(),
            null, true);

        pTooltip.addLineIntText(
            LM.Get("children"),
            pEmpire.countChildren(),
            null, true);

        pTooltip.addLineIntText(
            LM.Get("territory"),
            pEmpire.countZones(),
            null, true);

        pTooltip.addLineIntText(
            LM.Get("housed"),
            pEmpire.countHoused(),
            null, true);

        pTooltip.addLineIntText(
            LM.Get("deaths"),
            pEmpire.getTotalDeaths(),
            null, true);
    }
    public static void setIconValue(Tooltip pTooltip, string pName, float pMainVal, string pEnding = "", string pColor = "")
    {
        Transform tTransform = pTooltip.transform.FindRecursive(pName);
        StatsIcon component = tTransform.GetComponent<StatsIcon>();
        component.enable_animation = false;
        component.setValue(pMainVal, pEnding, pColor, false);
    }
}
