using EmpireCraft.Scripts.Data;
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
        tl.add(new TooltipAsset
        {
            id = "actor_officer",
            prefab_id = "tooltips/tooltip_actor",
            callback = showOfficer
        });
        tl.add(new TooltipAsset
        {
            id = "actor_emperor",
            prefab_id = "tooltips/tooltip_actor",
            callback = showEmperor
        });
        tl.add(new TooltipAsset
        {
            id = "all_titles",
            callback = showTitleList
        });
    }
    public static void showTitleList(Tooltip pTooltip, string pType, TooltipData pData)
    {
        Actor actor = pData.actor;
        pTooltip.name.text = pData.tip_name.Localize();
        if (actor == null || !actor.isAlive())
        {
            return;
        }
        List<KingdomTitle> titles = actor.GetOwnedTitle().Select(id=>ModClass.KINGDOM_TITLE_MANAGER.get(id)).ToList();
        foreach (var title in titles)
        {
            if (title == null) continue;
            string text = title.data.name;
            float num = Date.getYearsSince(title.data.timestamp_been_controlled);
            pTooltip.addLineText(text, $"{num}", null, pPercent: false, pLocalize: false);
        }
    }
    public static void showEmpireToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        Kingdom tKingdom = pData.kingdom.GetEmpire().CoreKingdom;
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
        if (pEmpire.Emperor != null)
        {
            if (pEmpire.Emperor.isAlive())
            {
                pValue = pEmpire.Emperor.getName();
            }
        }
        pTooltip.addLineText("emperor", pValue, "#FE9900", false, true, 21);
        if (pEmpire.EmpireClan != null)
        {
            if (pEmpire.EmpireClan.isAlive())
            {
                pTooltip.addLineText("empire_clan", pEmpire.EmpireClan.data.name, pEmpire.EmpireClan.getColor().color_text, false, true, 21);
            }
        }
        pTooltip.addLineText("empire_capital", pEmpire.CoreKingdom.data.name, "#CC6CE7", false, true, 21);
        pTooltip.addLineText("year_name", pEmpire.HasYearName()?pEmpire.data.year_name:pEmpire.Emperor.GetModName().firstName??"无", "#FE9900", false, true, 21);
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
            pTooltip.addLineText("title_been_controlled_year", $"{title.GetTitleBeenControlledYear()}{LM.Get("Year")}", tColorHex, false, true, 21);
        }
    }
    public static void showProvinceToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        City city = pData.city;
        Province province = city.GetProvince();
        pTooltip.setDescription(LM.Get("province_description"), null);
        string tColorHex = province.empire.CoreKingdom.getColor().color_text;
        pTooltip.setTitle(province.data.name, "ProvinceWindowTitle", tColorHex);
        int tAge = province.getAge();
        AssetManager.tooltips.setIconValue(pTooltip, "i_age", tAge);
        AssetManager.tooltips.setIconValue(pTooltip, "i_population", province.countPopulation());
        pTooltip.addLineText("province_owner", province.empire.data.name, "#FE9900", false, true, 21);
        pTooltip.addLineText("province_capital", province.province_capital.data.name, "#CC6CE7", false, true, 21);
        string officer_name = "-";
        if (province.officer != null)
        {
            if (province.officer.isAlive())
            {
                officer_name = province.officer.data.name;
            }
        }
        pTooltip.addLineText("province_officer", officer_name, "#CC6CE7", false, true, 21);
        pTooltip.addLineText("province_officers_num", province.data.history_officers.Count().ToString(), "#CC6CE7", false, true, 21);
        ConfigData.speciesCulturePair.TryGetValue(city.getSpecies(), out string culture);
        string provinceType = "";
        if (culture != null)
        {
            provinceType = LM.Get($"{culture}_{province.data.provinceLevel.ToString()}");
        } else
        {
            provinceType = LM.Get($"Western_{province.data.provinceLevel.ToString()}");
        }
        pTooltip.addLineText("province_type", provinceType, "#CC6CE7", false, true, 21);
    }

    private static void showOfficer(Tooltip pTooltip, string pType, TooltipData pData)
    {
        AssetManager.tooltips.showActor("actor_officer", pTooltip, pData);
    }

    private static void showEmperor(Tooltip pTooltip, string pType, TooltipData pData)
    {
        AssetManager.tooltips.showActor("actor_emperor", pTooltip, pData);
    }
}
