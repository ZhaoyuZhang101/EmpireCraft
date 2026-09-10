using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
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
            id = "empireCore",
            prefab_id = "tooltips/tooltip_kingdom",
            callback = showEmpireCoreToolTip
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
        tl.add(new TooltipAsset
        {
            id = "empirecraft_actor",
            prefab_id = "tooltips/tooltip_normal",
            callback = showEmpireCraftActor
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
		pTooltip.setTitle(kingdom.GetKingdomFullName(), type.ToString(), kingdom.getColor().color_text);
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
        Transform stats = pTooltip.transform.FindRecursive("Stats");
        if (stats != null) stats.gameObject.SetActive(false);
        KingdomBanner[] array = pTooltip.transform.FindAllRecursive<KingdomBanner>();
        for (int i = 0; i < array.Length; i++)
        {
            array[i].load(tKingdom);
        }
        long explicitEmpireId;
        Empire pEmpire = long.TryParse(pData.tip_description, out explicitEmpireId)
            ? ModClass.EMPIRE_MANAGER.get(explicitEmpireId)
            : null;
        pEmpire ??= ModClass.EMPIRE_MANAGER.get(tKingdom.GetEmpireID());
        if (pEmpire == null) return;
        if (pEmpire.isRekt() || pEmpire.IsArchived()) return;
        pTooltip.setDescription(tKingdom.getMotto(), null);
        string tColorHex = tKingdom.getColor().color_text;
        pTooltip.setTitle(pEmpire.GetEmpireFullName(), "EmpireText", tColorHex);
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
        pTooltip.addLineText("empire_capital", pEmpire.CoreKingdom.GetKingdomFullName(), "#CC6CE7", false, true, 21);
        Regime regime = pEmpire.CoreKingdom.GetRegime();
        if (regime?.HasEraName() == true && pEmpire.HasYearName())
        {
            pTooltip.addLineText("year_name", pEmpire.GetYearNameWithTime(), "#FE9900", false, true, 21);
        }

        pTooltip.addLineBreak();
        EmpireCore core = EmpireCoreManager.Get(pEmpire);
        AddTooltipLine(pTooltip, "empire_tooltip_core", EmpireCoreManager.GetDisplayName(core), "#74D7FF", true);
        AddTooltipLine(pTooltip, "empire_tooltip_ascension_title",
            GetAscensionTitleName(pEmpire, core), "#FFD34E", true);
        AddTooltipLine(pTooltip, "label_treasury", pEmpire.CurrentMoney.ToString(),
            pEmpire.CurrentMoney < 0 ? "#FF6666" : "#76E6C2", true);
        AddTooltipLine(pTooltip, "label_mandate", pEmpire.Mandate.ToString(), "#FFCF55", true);

        FixedFaction dominantFaction = regime?.GetDominateFaction();
        AddTooltipLine(pTooltip, "label_dominant_faction", dominantFaction?.Name, "#E78BFF", true);
        TemporaryFaction runningClaim = GetRunningClaim(pEmpire, regime);
        AddTooltipLine(pTooltip, "empire_tooltip_running_claim",
            FormatRunningClaim(runningClaim, regime), "#7EE6A8", true);

        pTooltip.addLineBreak();
        AddTooltipLine(pTooltip, "current_selected_province", tKingdom.GetKingdomName(),
            tKingdom.getColor().color_text, true);
        AddTooltipLine(pTooltip, "empire_tooltip_province_ruler", tKingdom.king?.getName(), "#FE9900", true);
        AddTooltipLine(pTooltip, "empire_tooltip_province_type",
            LM.Get(tKingdom.GetKingdomType().ToString()), "#8FE7FF", true);
        AddTooltipLine(pTooltip, "empire_tooltip_province_titles",
            GetOwnedTitleNames(tKingdom.king, null), "#FFD34E", true);
    }

    private static TemporaryFaction GetRunningClaim(Empire empire, Regime regime)
    {
        TemporaryFaction running = empire?.RunningTemporaryFaction;
        if (running?.IsStarted() == true) return running;
        return regime?.GetPlayerFactions()?.Where(faction => faction?.TemporaryFactions != null)
            .SelectMany(faction => faction.TemporaryFactions)
            .FirstOrDefault(claim => claim?.IsStarted() == true);
    }

    private static string FormatRunningClaim(TemporaryFaction claim, Regime regime)
    {
        if (claim == null) return "";
        string claimName = TranslateHelper.GetTemporaryFactionClaimText(claim.type);
        FixedFaction sponsor = regime?.GetPlayerFactions()?.FirstOrDefault(faction => faction?.GetID() == claim.factionID);
        string sponsorName = sponsor?.Name ?? LM.Get("label_none");
        int progress = claim.progressMax > 0
            ? Mathf.Clamp(Mathf.RoundToInt(claim.progress / claim.progressMax * 100f), 0, 100)
            : 0;
        string progressText = claim.ShowAsPlot ? LM.Get("tf_starting") : progress + "%";
        return string.Format(LM.Get("empire_tooltip_claim_format"), claimName, sponsorName, progressText);
    }

    private static string GetAscensionTitleName(Empire empire, EmpireCore core)
    {
        KingdomTitle title = empire?.CoreKingdom?.GetMainTitle() ?? empire?.Emperor?.GetMainTitle();
        if (title == null || title.isRekt())
        {
            title = empire?.CoreKingdom?.capital?.GetTitle();
        }
        if ((title == null || title.isRekt()) && core != null)
        {
            title = EmpireCoreManager.GetColorTitle(core);
        }
        return title?.data?.name;
    }
    public static void showKingdomTitleToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        City city = pData.city;
        KingdomTitle title = city?.GetTitle();
        if (title == null || title.isRekt()) return;
        pTooltip.setDescription(LM.Get("kingdom_title_description"), null);
        string tColorHex = title.getColor().color_text;
        pTooltip.setTitle(title.data.name, "KingdomTitleWindowTitle", tColorHex);
        int tAge = title.getAge();
        AssetManager.tooltips.setIconValue(pTooltip, "i_age", tAge);
        AssetManager.tooltips.setIconValue(pTooltip, "i_population", title.countPopulation());

        string provinceName = string.IsNullOrWhiteSpace(title.data.province_name)
            ? title.title_capital?.GetCityName()
            : title.data.province_name;
        AddTooltipLine(pTooltip, "province_name", provinceName, "#FFD34E", true);
        AddTooltipLine(pTooltip, "kingdom_title_capital", title.title_capital?.GetCityName(), "#CC6CE7", true);

        List<KingdomTitleHolderRelation> holders = KingdomTitleRelationResolver.ResolveCurrentHolders(title);
        AddTooltipLine(pTooltip, "kingdom_title_administrator",
            FormatAdministrationRulers(KingdomTitleRelationResolver.FindCurrentAdministrations(title)),
            "#76E6C2", true);
        AddTooltipLine(pTooltip, "kingdom_title_landed_holder",
            FormatTitleHolders(holders, false), "#FE9900", true);
        AddTooltipLine(pTooltip, "kingdom_title_unlanded_holder",
            FormatTitleHolders(holders, true), "#8FE7FF", true);

        if (title.isBeenControlled())
        {
            Kingdom controller = title.control_kingdom;
            AddTooltipLine(pTooltip, "kingdom_title_controller",
                controller?.GetKingdomFullName(), "#CC6CE7", true);
            AddTooltipLine(pTooltip, "title_been_controlled_year",
                $"{title.GetTitleBeenControlledYear()}{LM.Get("Year")}", tColorHex, true);
        }
    }

    private static string FormatAdministrationRulers(IEnumerable<Kingdom> administrations)
    {
        return JoinTooltipValues(administrations?
            .Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Select(kingdom => kingdom.king?.getName()));
    }

    private static string FormatTitleHolders(IEnumerable<KingdomTitleHolderRelation> holders, bool unlanded)
    {
        return JoinTooltipValues(holders?
            .Where(relation => relation != null && relation.Unlanded == unlanded &&
                               relation.Holder != null && !relation.Holder.isRekt())
            .Select(relation =>
            {
                string holderName = relation.Holder.getName();
                string empireName = relation.Empire?.GetEmpireName();
                return string.IsNullOrWhiteSpace(empireName) ? holderName : $"{empireName} · {holderName}";
            }));
    }

    private static string JoinTooltipValues(IEnumerable<string> values)
    {
        List<string> names = values?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct().ToList() ?? new List<string>();
        const int visibleCount = 4;
        string result = string.Join(" / ", names.Take(visibleCount));
        return names.Count > visibleCount ? result + $" ... (+{names.Count - visibleCount})" : result;
    }

    public static void showEmpireCoreToolTip(Tooltip pTooltip, string pType, TooltipData pData)
    {
        pTooltip.clear();
        City city = pData.city;
        EmpireCore core = city?.GetEmpireCore();
        if (core == null) return;

        City repCity = EmpireCoreManager.GetRepresentativeCity(core) ?? city;
        Kingdom repKingdom = repCity?.kingdom;
        string colorText = repKingdom?.getColor().color_text ?? "#FFFFFF";
        pTooltip.setDescription(LM.Get("empire_core_description"), null);
        pTooltip.setTitle(EmpireCoreManager.GetDisplayName(core), "EmpireCoreWindowTitle", colorText);
        AssetManager.tooltips.setIconValue(pTooltip, "i_age", Date.getYearsSince(core.create_timestamp));
        AssetManager.tooltips.setIconValue(pTooltip, "i_population", EmpireCoreManager.GetCities(core).Sum(c => c?.CountLivingPopulation() ?? 0));
        AssetManager.tooltips.setIconValue(pTooltip, "i_army", EmpireCoreManager.GetCities(core).Sum(c => c?.CountLivingWarriors() ?? 0));

        string createTime = $"{Date.getDate(core.create_timestamp)}";
        pTooltip.addLineText("empire_core_created_time", createTime, colorText, false, true, 21);
        string foundingEmpire = EmpireCoreManager.GetFoundingEmpireName(core);
        if (!string.IsNullOrWhiteSpace(foundingEmpire))
        {
            pTooltip.addLineText("empire_core_founding_empire", foundingEmpire, colorText, false, true, 21);
        }
        pTooltip.addLineText("empire_core_titles_count", EmpireCoreManager.GetTitles(core).Count.ToString(), colorText, false, true, 21);
        pTooltip.addLineText("empire_core_cities_count", EmpireCoreManager.GetCities(core).Count.ToString(), colorText, false, true, 21);

        List<string> empires = EmpireCoreManager.GetCurrentEmpireNames(core);
        if (empires.Count > 0)
        {
            pTooltip.addLineText("empire_core_current_empires", string.Join(" / ", empires), colorText, false, true, 21);
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

    private static void showEmpireCraftActor(Tooltip pTooltip, string pType, TooltipData pData)
    {
        Actor actor = pData.actor;
        long actorId = actor?.id ?? -1L;
        if (actorId <= 0) long.TryParse(pData.tip_name, out actorId);
        long identityId = -1L;
        long.TryParse(pData.tip_description, out identityId);
        PersonalClanIdentity identity = SpecificClanManager.getPerson(identityId) ??
            FindIdentityByActorId(actorId) ?? actor?.GetPersonalIdentity();
        if (actor == null && identity == null) return;

        pTooltip.clear();
        string actorName = actor?.getName() ?? identity?.name ?? LM.Get("empirecraft_actor_unknown");
        string color = actor?.kingdom?.getColor().color_text ?? identity?._specificClan?.color ?? "#FFFFFF";
        pTooltip.setTitle(actorName, "empirecraft_actor_tooltip", color);
        pTooltip.setDescription(LM.Get(actor != null ? "empirecraft_actor_live_description" :
            "empirecraft_actor_history_description"), null);

        AddTooltipLine(pTooltip, "empirecraft_actor_full_name", actorName, color);
        AddTooltipLine(pTooltip, "empirecraft_actor_age", GetActorAge(actor, identity), "#FFD34E", true);
        AddTooltipLine(pTooltip, "empirecraft_actor_gender", GetActorGender(actor, identity), "#FF9CB9", true);
        AddTooltipLine(pTooltip, "empirecraft_actor_realm", GetRealmName(actor, identity), "#8FE7FF");
        AddTooltipLine(pTooltip, "empirecraft_actor_titles", GetOwnedTitleNames(actor, identity), "#FFD34E", true);
        AddTooltipLine(pTooltip, "empirecraft_actor_office", GetFullOfficeName(actor, identity), "#76E6C2");
        AddTooltipLine(pTooltip, "empirecraft_actor_peerage", GetPeerageName(actor, identity), "#FFB45C");
        AddTooltipLine(pTooltip, "empirecraft_actor_faction", actor?.GetFaction()?.Name ?? identity?.factionName, "#E78BFF");

        pTooltip.addLineBreak();
        AddTooltipLine(pTooltip, "empirecraft_actor_specific_clan",
            actor?.GetSpecificClan()?.name ?? identity?._specificClan?.name, "#5CFFAF");
        AddTooltipLine(pTooltip, "empirecraft_actor_clan",
            actor?.hasClan() == true ? actor.clan.data.name : identity?.clanName, "#8CE1FF");
        AddTooltipLine(pTooltip, "empirecraft_actor_family",
            actor?.hasFamily() == true ? actor.family.data.name : identity?.familyName, "#8CE1FF");
        AddTooltipLine(pTooltip, "empirecraft_actor_parents", GetParentNames(identity), "#F1F1F1");
        AddTooltipLine(pTooltip, "empirecraft_actor_spouse", GetSpouseName(identity), "#FF9CB9");
    }

    private static PersonalClanIdentity FindIdentityByActorId(long actorId)
    {
        if (actorId <= 0) return null;
        if (SpecificClanManager._actorToPersonLookup.TryGetValue(actorId, out PersonalClanIdentity identity))
            return identity;
        return SpecificClanManager._globalPersonLookup.Values.FirstOrDefault(person => person?.actor_id == actorId);
    }

    private static string GetActorAge(Actor actor, PersonalClanIdentity identity)
    {
        int age = actor != null ? actor.getAge() : identity?.age ?? -1;
        return age >= 0 ? age.ToString() : "";
    }

    private static string GetActorGender(Actor actor, PersonalClanIdentity identity)
    {
        ActorSex sex = actor != null ? actor.data.sex : identity?.sex ?? ActorSex.None;
        return sex switch
        {
            ActorSex.Male => LM.Get("empirecraft_actor_gender_male"),
            ActorSex.Female => LM.Get("empirecraft_actor_gender_female"),
            _ => LM.Get("empirecraft_actor_gender_unknown")
        };
    }

    private static void AddTooltipLine(Tooltip tooltip, string key, string value, string color, bool showNone = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!showNone) return;
            value = LM.Get("label_none");
        }
        tooltip.addLineText(key, value, color, false, true, 21);
    }

    private static string GetRealmName(Actor actor, PersonalClanIdentity identity)
    {
        Empire empire = actor?.GetEmpire();
        if (empire != null && !empire.isRekt()) return empire.GetEmpireFullName();
        return actor?.kingdom?.GetKingdomName() ?? identity?.kingdomName;
    }

    private static string GetOwnedTitleNames(Actor actor, PersonalClanIdentity identity)
    {
        List<string> names = actor == null
            ? identity?.ownedTitleNames?.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList()
            : (actor.GetOwnedTitle() ?? new List<long>())
                .Select(titleId => ModClass.KINGDOM_TITLE_MANAGER.get(titleId)?.data?.name)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        if (names == null || names.Count == 0) return "";
        const int visibleCount = 6;
        string result = string.Join(" / ", names.Take(visibleCount));
        return names.Count > visibleCount ? result + $" ... (+{names.Count - visibleCount})" : result;
    }

    private static string GetFullOfficeName(Actor actor, PersonalClanIdentity identity)
    {
        OfficeObject office = actor?.GetOffice();
        if (office != null)
        {
            string fullName = office.GetName(office.meta_object);
            if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
            return office.GetOfficeName(office.meta_object);
        }
        return !string.IsNullOrWhiteSpace(identity?.fullOfficeName) ? identity.fullOfficeName : identity?.officeName;
    }

    private static string GetPeerageName(Actor actor, PersonalClanIdentity identity)
    {
        if (actor != null)
        {
            string peerage = actor.GetPeerageDisplayName();
            if (!string.IsNullOrWhiteSpace(peerage)) return peerage;
        }
        if (string.IsNullOrWhiteSpace(identity?.PeeragesLevel)) return "";
        return LM.Get(identity.PeeragesLevel) ?? identity.PeeragesLevel;
    }

    private static string GetParentNames(PersonalClanIdentity identity)
    {
        if (identity == null) return "";
        string father = SpecificClanManager.getPerson(identity.father)?.name;
        string mother = SpecificClanManager.getPerson(identity.mother)?.name;
        if (string.IsNullOrWhiteSpace(father) && string.IsNullOrWhiteSpace(mother)) return "";
        return $"{LM.Get("empirecraft_actor_father")}: {father ?? LM.Get("label_none")} / " +
               $"{LM.Get("empirecraft_actor_mother")}: {mother ?? LM.Get("label_none")}";
    }

    private static string GetSpouseName(PersonalClanIdentity identity)
    {
        if (identity?.hasLover() != true) return "";
        return SpecificClanManager.getPerson(identity.lover.identity)?.name;
    }
}
