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
        EnsureEmpireNaming(__instance?.culture);
        try
        {
            var beforeKingdomName = __instance.kingdom.data.name;
            __instance.kingdom.data.name = __instance.culture.getOnomasticData(MetaType.Kingdom).generateName();
            var afterKingdomName = __instance.kingdom.data.name;
            TranslateHelper.LogChangeKingdomName(__instance, __instance.kingdom, beforeKingdomName, afterKingdomName);
            var beforeCityName = __instance.city.data.name;
            __instance.city.data.name = __instance.culture.getOnomasticData(MetaType.City).generateName();
            var afterCityName = __instance.city.data.name;
            TranslateHelper.LogChangeCityName(__instance, __instance.city, beforeCityName, afterCityName);
            __instance.language.data.name = __instance.kingdom.GetKingdomName() + LM.Get("Language") +
                                            __instance.city.GetCityName() + LM.Get("Dialect");
            __instance.culture.data.name = __instance.kingdom.GetKingdomName() + "-" + LM.Get("OriginalCulture");
            __instance.culture.data.creator_city_name = __instance.city.data.name;
        }
        catch (Exception e)
        {
            LogService.LogError($"文化命名失败: {e}");
        }

    }

    private static void set_culture_name(Culture __instance, Actor pActor)
    {
        if (__instance?.data == null || pActor?.data == null) return;
        __instance.data.name = pActor.kingdom.GetKingdomName() + "-" + pActor.city.GetCityName() + LM.Get("Culture");
        if (!AncientWarfareCompatibility.Owns(pActor)) setDefaultNameTemplate(__instance);
        EnsureEmpireNaming(__instance);
        
    }
    private static void clone_culture_name(Culture __instance)
    {
        __instance.data.name = __instance.data.creator_kingdom_name.Split('\u200A')[0].Split(' ').Last()+"-"+ __instance.data.creator_city_name.Split('\u200A')[0].Split(' ').Last()+ LM.Get("EvolvedCulture");
    }
    private static void setDefaultNameTemplate(Culture culture)
    {

        string species = culture.data.creator_species_id;
        string insertCulture = OverallHelperFunc.GetCultureFromSpecies(species);
        insertCultureTemplate(culture, insertCulture);
    }

    private sealed class NamingTemplateState
    {
        public NamingTemplateState() { }
        internal string signature;
        internal object data;
    }
    private static readonly ConditionalWeakTable<Culture, NamingTemplateState> NamingTemplates = new();

    internal static void EnsureEmpireNaming(Culture culture)
    {
        if (!AncientWarfareCompatibility.Loaded || culture?.data == null) return;
        string cultureName = OverallHelperFunc.GetCultureFromSpecies(culture.data.creator_species_id);
        if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(cultureName)) return;
        string signature = cultureName + "/" + PlayerConfig.detectLanguage();
        NamingTemplateState state = NamingTemplates.GetOrCreateValue(culture);
        if (state.signature == signature && ReferenceEquals(state.data, culture.data)) return;
        // Repair saved/previously AW-created templates, without adding EC political traits.
        insertCultureTemplate(culture, cultureName, false);
        state.signature = signature;
        state.data = culture.data;
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
