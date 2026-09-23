using UnityEngine;
using NeoModLoader.General.Event.Handlers;
using NeoModLoader.api;
using System.Text;
using NeoModLoader.services;
using HarmonyLib;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Collections;
using System.Linq;
using System.IO;
using System.Drawing;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Data;
using System.Configuration;
using EmpireCraft.Scripts.GameClassExtensions;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
namespace EmpireCraft.Scripts.GamePatches;

public class CulturePatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static string ModPath;
    public void Initialize()
    {
        ModPath = declare.FolderPath + "/Locales/";
        new Harmony(nameof(set_culture_name)).Patch(AccessTools.Method(typeof(Culture), nameof(Culture.createCulture)),
            postfix: new HarmonyMethod(GetType(), nameof(set_culture_name)));
        new Harmony(nameof(set_default_culture_name)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.createDefaultCultureAndLanguageAndClan)),
            postfix: new HarmonyMethod(GetType(), nameof(set_default_culture_name)));
        new Harmony(nameof(clone_culture_name)).Patch(AccessTools.Method(typeof(Culture), nameof(Culture.cloneAndEvolveOnomastics)),
            postfix: new HarmonyMethod(GetType(), nameof(clone_culture_name)));
    }

    private static void set_default_culture_name(Actor __instance, string pCultureName)
    {
        if (__instance?.data == null || AncientWarfareCompatibility.Owns(__instance)) return;
        EnsureEmpireNaming(__instance?.culture);
        try
        {
            if (__instance.kingdom?.data == null || __instance.city?.data == null ||
                __instance.culture?.data == null || __instance.language?.data == null) return;
            string realmCulture = CultureService.GetMainCulture(__instance.city, initialize: false);
            if (!CultureService.IsValidCulture(realmCulture))
                realmCulture = GetInjectedCultureName(__instance.culture);
            if (CultureService.IsValidCulture(realmCulture))
                CultureService.SetRealmCulture(__instance.kingdom, realmCulture, updatePoliticalSystem: false);
            OnomasticsData kingdomNames = GetOnomasticDataSafe(__instance.culture, MetaType.Kingdom);
            OnomasticsData cityNames = GetOnomasticDataSafe(__instance.culture, MetaType.City);
            if (kingdomNames == null || cityNames == null) return;
            var beforeKingdomName = __instance.kingdom.data.name;
            __instance.kingdom.data.name = kingdomNames.generateName()
                .UseLocalizedNameSeparator();
            __instance.kingdom.RememberInitialRandomKingdomName(__instance.kingdom.data.name, overwrite: true);
            __instance.kingdom.GetOrCreate().core_name = "";
            __instance.kingdom.GetOrCreate().core_name_source = "";
            __instance.kingdom.EnsureKingdomCoreName();
            var afterKingdomName = __instance.kingdom.data.name;
            TranslateHelper.LogChangeKingdomName(__instance, __instance.kingdom, beforeKingdomName, afterKingdomName);
            var beforeCityName = __instance.city.data.name;
            __instance.city.data.name = cityNames.generateName()
                .UseLocalizedNameSeparator();
            __instance.city.GetOrCreate().core_name = "";
            __instance.city.GetOrCreate().core_name_source = "";
            __instance.city.EnsureCityCoreName();
            var afterCityName = __instance.city.data.name;
            TranslateHelper.LogChangeCityName(__instance, __instance.city, beforeCityName, afterCityName);
            __instance.language.data.name = __instance.kingdom.GetKingdomName() + LM.Get("Language") +
                                            __instance.city.GetCityName() + LM.Get("Dialect");
            SyncCultureDisplayName(__instance.culture, realmCulture);
            __instance.culture.data.creator_city_name = __instance.city.data.name;
            // 城邦国名默认用城市名
            CityStateService.ApplyCityName(__instance.kingdom);
        }
        catch (Exception e)
        {
            LogService.LogError($"文化命名失败: {e}");
        }

    }

    private static void set_culture_name(Culture __instance, Actor pActor)
    {
        if (__instance?.data == null || pActor?.data == null) return;
        if (AncientWarfareCompatibility.Owns(pActor)) return;
        string kingdomName = pActor.kingdom?.data == null ? "" : pActor.kingdom.GetKingdomName();
        string cityName = pActor.city?.data == null ? "" : pActor.city.GetCityName();
        __instance.data.name = kingdomName + "-" + cityName + LM.Get("Culture");
        setDefaultNameTemplate(__instance, pActor);
        EnsureEmpireNaming(__instance);
        
    }
    private static void clone_culture_name(Culture __instance)
    {
        if (__instance?.data == null) return;
        if (SyncCultureDisplayName(__instance)) return;
        string kingdomName = ExtractStoredCoreName(__instance.data.creator_kingdom_name);
        string cityName = ExtractStoredCoreName(__instance.data.creator_city_name);
        __instance.data.name = kingdomName + "-" + cityName + LM.Get("EvolvedCulture");
    }

    private static string ExtractStoredCoreName(string storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return "";
        if (OverallHelperFunc.TryExtractEnglishPrefixedCountryName(storedName, out string prefixedName))
            return prefixedName;
        string[] parts = storedName.SplitNameParts();
        return parts.Length > 0 ? parts[0] : storedName.Trim();
    }
    private static void setDefaultNameTemplate(Culture culture, Actor founder)
    {
        string insertCulture = CultureService.GetMainCulture(founder?.city, initialize: false);
        if (!CultureService.IsValidCulture(insertCulture))
            insertCulture = CultureService.GetActorCulture(founder);
        if (!CultureService.IsValidCulture(insertCulture))
            insertCulture = OverallHelperFunc.GetCultureFromSpecies(culture.data.creator_species_id);
        insertCultureTemplate(culture, insertCulture);
    }

    private sealed class NamingTemplateState
    {
        public NamingTemplateState() { }
        internal string signature;
        internal object data;
        internal string empireCraftCulture;
    }
    private static readonly ConditionalWeakTable<Culture, NamingTemplateState> NamingTemplates = new();

    internal static void EnsureEmpireNaming(Culture culture)
    {
        if (!AncientWarfareCompatibility.Loaded || culture?.data == null) return;
        NamingTemplateState state = NamingTemplates.GetOrCreateValue(culture);
        string cultureName = !string.IsNullOrWhiteSpace(state.empireCraftCulture)
            ? state.empireCraftCulture
            : OverallHelperFunc.GetCultureFromSpecies(culture.data.creator_species_id);
        if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(cultureName)) return;
        string signature = cultureName + "/" + PlayerConfig.detectLanguage();
        state.empireCraftCulture = cultureName;
        if (state.signature == signature && ReferenceEquals(state.data, culture.data)) return;
        // Repair saved/previously AW-created templates, without adding EC political traits.
        insertCultureTemplate(culture, cultureName, false);
        state.signature = signature;
        state.data = culture.data;
    }

    public static string GetInjectedCultureName(Culture culture)
    {
        if (culture?.data == null) return "";
        NamingTemplateState state = NamingTemplates.GetOrCreateValue(culture);
        if (!string.IsNullOrWhiteSpace(state.empireCraftCulture) &&
            OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(state.empireCraftCulture))
        {
            return state.empireCraftCulture;
        }

        // Legacy cultures did not persist the injected key. Recover it from the culture's
        // own creator metadata, never from the current actor's race.
        string recovered = OverallHelperFunc.GetCultureFromSpecies(culture.data.creator_species_id);
        if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(recovered)) return "";
        state.empireCraftCulture = recovered;
        return recovered;
    }

    public static bool SyncCultureDisplayName(Culture culture, string cultureName = null)
    {
        if (culture?.data == null) return false;
        if (!CultureService.IsValidCulture(cultureName)) cultureName = GetInjectedCultureName(culture);
        if (!CultureService.IsValidCulture(cultureName)) return false;

        string displayName;
        try
        {
            displayName = cultureName.GetCultureTranslate();
        }
        catch (Exception)
        {
            displayName = cultureName;
        }

        if (string.IsNullOrWhiteSpace(displayName)) return false;
        culture.data.name = displayName;
        return true;
    }

    public static Dictionary<long, string> ExportCultureBindings()
    {
        Dictionary<long, string> bindings = new();
        if (World.world?.cultures == null) return bindings;
        foreach (Culture culture in World.world.cultures)
        {
            if (culture?.data == null) continue;
            string cultureName = GetInjectedCultureName(culture);
            if (OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(cultureName))
                bindings[culture.id] = cultureName;
        }
        return bindings;
    }

    public static void ImportCultureBindings(IDictionary<long, string> bindings)
    {
        if (bindings == null || World.world?.cultures == null) return;
        foreach (KeyValuePair<long, string> binding in bindings)
        {
            if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(binding.Value)) continue;
            Culture culture = World.world.cultures.get(binding.Key);
            if (culture?.data == null) continue;
            NamingTemplates.GetOrCreateValue(culture).empireCraftCulture = binding.Value;
            insertCultureTemplate(culture, binding.Value, addTraits: false);
        }
    }

    internal static OnomasticsData GetOnomasticDataSafe(Culture culture, MetaType type)
    {
        if (culture?.data == null) return null;
        try
        {
            return culture.getOnomasticData(type);
        }
        catch (NullReferenceException)
        {
            return null;
        }
    }

    public static void insertCultureTemplate(Culture culture, string cultureName, bool addTraits = true)
    {
        OnomasticsData kindomData = culture.getOnomasticData(MetaType.Kingdom);
        OnomasticsData clanData = culture.getOnomasticData(MetaType.Clan);
        OnomasticsData familyData = culture.getOnomasticData(MetaType.Family);
        OnomasticsData CityData = culture.getOnomasticData(MetaType.City);
        OnomasticsData unitData = culture.getOnomasticData(MetaType.Unit);


        if (!OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out Setting setting))
        {
            return;
        }
        NamingTemplates.GetOrCreateValue(culture).empireCraftCulture = cultureName;
        SyncCultureDisplayName(culture, cultureName);
        FamilySetting familySetting = setting.Family;
        UnitSetting unitSetting = setting.Unit;
        KingdomSetting kingdomSetting = setting.Kingdom;
        ClanSetting clanSetting = setting.Clan;
        CitySetting citySetting = setting.City;
        List<string> traits = setting.traits;
        foreach (string trait in addTraits ? traits : new List<string>())
        {
            if (!culture.hasTrait(trait))
            {
                culture.addTrait(trait);
            }
        }


        OnomasticsHelper.Configure(
            kindomData,
            cultureName,
            kingdomSetting.rule,
            setGroup(kingdomSetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            familyData,
            cultureName,
            familySetting.rule,
            setGroup(familySetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            clanData,
            cultureName,
            clanSetting.rule,
            setGroup(clanSetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            unitData,
            cultureName,
            unitSetting.rule,
            setGroup(unitSetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            CityData,
            cultureName,
            citySetting.rule,
            setGroup(citySetting.groups, cultureName)
            );

    }

    public static (string groupName, string CharacterSetName, string definedContent)[] setGroup(Dictionary<string, string> groupPair, string culture)
    {
        (string groupName, string CharacterSetName, string definedContent)[] groups = Array.Empty<(string groupName, string CharacterSetName, string definedContent)>();
        foreach (KeyValuePair<string, string> group in groupPair)
        {
            string key = group.Key;
            string value = group.Value;

            string ModPath = Path.Combine(ModClass._declare.FolderPath, "Locales");
            string culturePath = Path.Combine(ModPath, "Cultures", $"Culture_{culture}");
            string CharacterSetPath = Path.Combine(culturePath, String.Format("{0}{1}.csv", culture, value));
            if (File.Exists(CharacterSetPath))
            {
                groups = groups.Append((key, value, null)).ToArray();
            }
            else
            {
                groups = groups.Append((key, null, LM.Get(value))).ToArray();
            }

        }
        return groups;
    }
}
