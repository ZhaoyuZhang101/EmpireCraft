using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;
using UnityEngine;

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
            id = "kingdomTitle",
            prefab_id = "tooltips/tooltip_city",
            callback = showKingdomTitleToolTip
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
        tl.add(new TooltipAsset
        {
            id = "kingdom",
            prefab_id = "tooltips/tooltip_kingdom",
            callback = showKingdom
        });
        tl.add(new TooltipAsset
        {
	        id = "actor_king",
	        prefab_id = "tooltips/tooltip_actor",
	        callback = showKing
        });
        tl.add(new TooltipAsset
        {
	        id = "actor",
	        prefab_id = "tooltips/tooltip_actor",
	        callback = showActorNormal
        });
        tl.add(new TooltipAsset
        {
	        id = "actor_leader",
	        prefab_id = "tooltips/tooltip_actor",
	        callback = showLeader
        });
    }
    
    private static void showActorNormal(Tooltip pTooltip, string pType, TooltipData pData)
    {
	    AssetManager.tooltips.showActor("", pTooltip, pData);
    }

    private static void showLeader(Tooltip pTooltip, string pType, TooltipData pData)
    {
	    string subTitle = "";
	    if (pData.actor.hasCity())
	    {  
		    City city = pData.actor.city;
		    OfficeObject office = city.GetOffice();
		    subTitle = office.GetName(city);
	    }
	    AssetManager.tooltips.showActor(string.IsNullOrEmpty(subTitle)?"village_statistics_leader":subTitle, pTooltip, pData);
    }

    private static void showKing(Tooltip pTooltip, string pType, TooltipData pData)
    {
	    string subTitle = "";
	    if (pData.actor.hasKingdom())
	    {
		    Kingdom kingdom = pData.actor.kingdom;
		    OfficeObject office = kingdom.GetOffice();
		    subTitle = office.GetName(kingdom);
	    }
	    AssetManager.tooltips.showActor(string.IsNullOrEmpty(subTitle)?"village_statistics_king":subTitle, pTooltip, pData);
    }
	public static void showKingdom(Tooltip pTooltip, string pType, TooltipData pData)
	{
		Kingdom kingdom = pData.kingdom;
		pTooltip.setSpeciesIcon(kingdom.getSpeciesIcon());
		string color_text = kingdom.getColor().color_text;
		KingdomType type = kingdom.GetKingdomType();
		pTooltip.setTitle(kingdom.name, type.ToString(), kingdom.getColor().color_text);
		pTooltip.transform.FindRecursive("Stats").gameObject.SetActive(value: true);
		AssetManager.tooltips.setIconValue(pTooltip, "i_age", kingdom.getAge());
		AssetManager.tooltips.setIconValue(pTooltip, "i_population", kingdom.getPopulationPeople());
		AssetManager.tooltips.setIconValue(pTooltip, "i_army", kingdom.countTotalWarriors());
		pTooltip.setDescription(kingdom.getMotto());
		string pValue = "-";
		if (kingdom.hasKing())
		{
			pValue = kingdom.king.getName();
		}
		pTooltip.addLineText("village_statistics_king", pValue, color_text);
		if (kingdom.hasKing())
		{
			pTooltip.addLineIntText("ruler_money", kingdom.king.money);
		}
		pTooltip.addLineBreak();
		pTooltip.addLineText("villages", kingdom.cities.Count.ToText() + "/" + kingdom.getMaxCities().ToText());
		pTooltip.addLineIntText("adults", kingdom.countAdults());
		pTooltip.addLineIntText("children", kingdom.countChildren());
		pTooltip.addLineIntText("families", kingdom.countFamilies());
		pTooltip.addLineIntText("happy", kingdom.countHappyUnits());
		pTooltip.addLineBreak();
		pTooltip.addLineIntText("food", kingdom.countTotalFood());
		pTooltip.addLineBreak();
		string pValue2 = "-";
		if (kingdom.hasCapital())
		{
			pValue2 = kingdom.capital.name;
		}
		pTooltip.addLineText("kingdom_statistics_capital", pValue2, color_text);
		if (kingdom.hasKing() && kingdom.king.hasClan())
		{
			pTooltip.addLineText("clan", kingdom.king.clan.data.name, kingdom.king.clan.getColor().color_text);
		}
		if (kingdom.hasCulture())
		{
			pTooltip.addLineText("culture", kingdom.culture.data.name, kingdom.culture.getColor().color_text);
		}
		if (kingdom.hasLanguage())
		{
			pTooltip.addLineText("language", kingdom.language.data.name, kingdom.language.getColor().color_text);
		}
		if (kingdom.hasReligion())
		{
			pTooltip.addLineText("religion", kingdom.religion.data.name, kingdom.religion.getColor().color_text);
		}
		Alliance alliance = kingdom.getAlliance();
		if (alliance != null)
		{
			int yearsSince = Date.getYearsSince(kingdom.data.timestamp_alliance);
			pTooltip.addLineText("alliance", alliance.data.name, alliance.getColor().color_text);
			pTooltip.addLineIntText("kingdom_time_in_alliance", yearsSince, alliance.getColor().color_text);
		}
		pTooltip.addLineBreak();
		pTooltip.addLineIntText("births", kingdom.getTotalBirths());
		pTooltip.addLineIntText("deaths", kingdom.getTotalDeaths());
		pTooltip.addLineIntText("kills", kingdom.getTotalKills());
		pTooltip.addLineBreak();
		pTooltip.addLineText("species", kingdom.getActorAsset().getTranslatedName());
		KingdomBanner[] array = pTooltip.transform.FindAllRecursive<KingdomBanner>();
		for (int i = 0; i < array.Length; i++)
		{
			array[i].load(kingdom);
		}
		TooltipKingdomTraitsRow componentInChildren = pTooltip.GetComponentInChildren<TooltipKingdomTraitsRow>(includeInactive: true);
		if (componentInChildren != null)
		{
			componentInChildren.init(pTooltip, pData);
		}
		AssetManager.tooltips.showTabBannerTip(pTooltip, pData);
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
        Kingdom tKingdom = pData.kingdom;
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
            pTooltip.addLineText("title_been_controlled", city.kingdom.IsEmpire() ? city.kingdom.GetEmpire().data.name : city.kingdom.data.name, "#CC6CE7", false, true, 21);
            pTooltip.addLineText("title_been_controlled_year", $"{title.GetTitleBeenControlledYear()}{LM.Get("Year")}", tColorHex, false, true, 21);
        }
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
