using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftTooltipLibrary
{
    public static void init()
    {
        TooltipLibrary tl = AssetManager.tooltips;
        tl.add(new TooltipAsset
        {
            id = "empire",
            prefab_id = "tooltips/tooltip_kingdom",
            callback = showEmpireToolTip
        });
        tl.add(new TooltipAsset
        {
            id = "kingdom_title",
            prefab_id = "tooltips/tooltip_city",
            callback = showKingdomTitleToolTip
        });
        tl.add(new TooltipAsset
        {
            id = "province",
            prefab_id = "tooltips/tooltip_city",
            callback = showProvinceToolTip
        });
    }
    public static void showEmpireToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        Kingdom tKingdom = pData.kingdom.GetEmpire().empire;
        if (tKingdom == null) return;
        pTooltip.setSpeciesIcon(tKingdom.getSpeciesIcon());
        string color_text = tKingdom.kingdomColor.color_text;
        pTooltip.transform.FindRecursive("Stats").gameObject.SetActive(value: true);
        KingdomBanner[] array = pTooltip.transform.FindAllRecursive<KingdomBanner>();
        for (int i = 0; i < array.Length; i++)
        {
            array[i].load(tKingdom);
        }
        Empire pEmpire = ModClass.EMPIRE_MANAGER.get(tKingdom.GetEmpireID());
        pTooltip.setDescription(tKingdom.getMotto(), null);
        string tColorHex = tKingdom.getColor().color_text;
        pTooltip.setTitle(pEmpire.name, "EmpireText", tColorHex);
        int tAge = pEmpire.getAge();
        AssetManager.tooltips.setIconValue(pTooltip, "i_age", tAge);
        AssetManager.tooltips.setIconValue(pTooltip, "i_population", pEmpire.countPopulation());
        AssetManager.tooltips.setIconValue(pTooltip, "i_army", pEmpire.countWarriors());
        string pValue = "-";
        if (pEmpire.emperor != null)
        {
            if (pEmpire.emperor.isAlive())
            {
                pValue = pEmpire.emperor.getName();
            }
        }
        pTooltip.addLineText("emperor", pValue, "#FE9900", false, true, 21);
        if (pEmpire.empire_clan != null)
        {
            if (pEmpire.empire_clan.isAlive())
            {
                pTooltip.addLineText("empire_clan", pEmpire.empire_clan.data.name, pEmpire.empire_clan.getColor().color_text, false, true, 21);
            }
        }
        pTooltip.addLineText("empire_capital", pEmpire.empire.data.name, "#CC6CE7", false, true, 21);
        pTooltip.addLineText("year_name", pEmpire.data.year_name, "#FE9900", false, true, 21);
        pTooltip.addLineBreak();
        pTooltip.addLineText("current_selected_province", pData.kingdom.data.name, pData.kingdom.getColor().color_text, false, true, 21);
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
        pTooltip.addLineText("province_leader", leaderName, color, false, true, 21);
        pTooltip.addLineText("province_level", LM.Get("default_" + pData.kingdom.GetCountryLevel().ToString()), tKingdom.getColor().color_text, false, true, 21);
        pTooltip.addLineBreak();
        pTooltip.addLineIntText(
            "adults",
            pEmpire.countAdults(),
            null, true);

        pTooltip.addLineIntText(
            "children",
            pEmpire.countChildren(),
            null, true);

        pTooltip.addLineIntText(
            "territory",
            pEmpire.countZones(),
            null, true);

        pTooltip.addLineIntText(
            "housed",
            pEmpire.countHoused(),
            null, true);
    }
    public static void showKingdomTitleToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        City city = pData.city;
        KingdomTitle title = city.GetTitle();
        pTooltip.setDescription(LM.Get("kingdom_title_description"), null);
        string tColorHex = title.getColor().color_text;
        pTooltip.setTitle(title.data.name, "KingdomTitleWindowTitle", tColorHex);
        int tAge = title.getAge();
        AssetManager.tooltips.setIconValue(pTooltip, "i_age", tAge);
        AssetManager.tooltips.setIconValue(pTooltip, "i_population", title.countPopulation());
        string pValue = title.HasOwner() ? title.owner.getName() : "-";
        pTooltip.addLineText("title_holder", pValue, "#FE9900", false, true, 21);
        pTooltip.addLineText("title_capital", title.title_capital.data.name, "#CC6CE7", false, true, 21);
        if (title.isBeenControlled())
        {
            pTooltip.addLineText("title_been_controlled", city.kingdom.isEmpire() ? city.kingdom.GetEmpire().data.name : city.kingdom.data.name, "#CC6CE7", false, true, 21);
            pTooltip.addLineText("title_been_controled_year", $"{title.GetTitleBeenControlledYear()}{LM.Get("Year")}", tColorHex, false, true, 21);
        }
    }
    public static void showProvinceToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        City city = pData.city;
        Province province = city.GetProvince();
        pTooltip.setDescription(LM.Get("province_description"), null);
        string tColorHex = province.getColor().color_text;
        pTooltip.setTitle(province.data.name, "ProvinceWindowTitle", tColorHex);
        int tAge = province.getAge();
        AssetManager.tooltips.setIconValue(pTooltip, "i_age", tAge);
        AssetManager.tooltips.setIconValue(pTooltip, "i_population", province.countPopulation());
        pTooltip.addLineText("province_owner", province.empire.data.name, "#FE9900", false, true, 21);
        pTooltip.addLineText("province_capital", province.province_capital.data.name, "#CC6CE7", false, true, 21);
        if (province.officer != null)
        {
            pTooltip.addLineText("province_officer", province.officer.getName(), "#CC6CE7", false, true, 21);
        }
        pTooltip.addLineText("province_officers_num", province.data.history_officers.Count().ToString(), "#CC6CE7", false, true, 21);
        pTooltip.addLineText("province_type", province.data.history_officers.Count().ToString(), "#CC6CE7", false, true, 21);
    }
}
