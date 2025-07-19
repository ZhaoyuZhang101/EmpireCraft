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
        LogService.LogInfo("文化模板加载成功");
    }

    private static void set_default_culture_name(Actor __instance, string pCultureName)
    {
        if (pCultureName != null)
            __instance.kingdom.data.name = __instance.culture.getOnomasticData(MetaType.Kingdom).generateName();
            __instance.city.data.name = __instance.culture.getOnomasticData(MetaType.City).generateName();
            __instance.language.data.name = __instance.kingdom.GetKingdomName() + LM.Get("Language") + __instance.city.GetCityName() + LM.Get("Dialect");
        __instance.culture.data.name = __instance.kingdom.GetKingdomName() + "-" + LM.Get("OriginalCulture");
            __instance.culture.data.creator_city_name = __instance.city.data.name;
        LogService.LogInfo("当前文化名称: " + __instance.culture.data.name);
    }

    private static void set_culture_name(Culture __instance, Actor pActor)
    {
        __instance.data.name = pActor.kingdom.GetKingdomName() + "-" + pActor.city.GetCityName() + LM.Get("Culture");
        LogService.LogInfo("当前文化名称: " + __instance.data.name);
        setDefaultNameTemplate(__instance);

    }
    private static void clone_culture_name(Culture __instance)
    {
        __instance.data.name = __instance.data.creator_kingdom_name.Split('\u200A')[0].Split(' ').Last()+"-"+ __instance.data.creator_city_name.Split('\u200A')[0].Split(' ').Last()+ LM.Get("EvolvedCulture");
        LogService.LogInfo("当前文化名称: " + __instance.data.name);
    }
    private static void setDefaultNameTemplate(Culture culture)
    {

        string species = culture.data.creator_species_id;
        LogService.LogInfo("当前文化物种: " + species);
        string insertCulture = OverallHelperFunc.GetCultureFromSpecies(species);
        insertCultureTemplate(culture, insertCulture);
    }

    public static void insertCultureTemplate(Culture culture, string cultureName)
    {
        OnomasticsData kindomData = culture.getOnomasticData(MetaType.Kingdom);
        OnomasticsData clanData = culture.getOnomasticData(MetaType.Clan);
        OnomasticsData familyData = culture.getOnomasticData(MetaType.Family);
        OnomasticsData CityData = culture.getOnomasticData(MetaType.City);
        OnomasticsData unitData = culture.getOnomasticData(MetaType.Unit);


        if (!OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out Setting setting))
        {
            LogService.LogInfo($"文化：{cultureName}的配置不存在");
            return;
        }
        FamilySetting familySetting = setting.Family;
        UnitSetting unitSetting = setting.Unit;
        KingdomSetting kingdomSetting = setting.Kingdom;
        ClanSetting clanSetting = setting.Clan;
        CitySetting citySetting = setting.City;

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
        (string groupName, string CharacterSetName, string definedContent)[] groups = new (string groupName, string CharacterSetName, string definedContent)[0];
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