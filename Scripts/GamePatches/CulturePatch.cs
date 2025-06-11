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
            __instance.language.data.name = __instance.kingdom.name.Split(' ')[0] + LM.Get("Language") + __instance.city.name.Split(' ')[0] + LM.Get("Dialect");
        __instance.culture.data.name = __instance.kingdom.name.Split(' ')[0] + "-" + LM.Get("OriginalCulture");
            __instance.culture.data.creator_city_name = __instance.city.data.name;
        LogService.LogInfo("当前文化名称: " + __instance.culture.data.name);
    }

    [Hotfixable]
    private static void set_culture_name(Culture __instance, Actor pActor)
    {
        __instance.data.name = pActor.kingdom.name.Split(' ')[0] + "-" + pActor.city.name.Split(' ')[0] + LM.Get("Culture");
        LogService.LogInfo("当前文化名称: " + __instance.data.name);
        setDefaultNameTemplate(__instance);

    }
    [Hotfixable]
    private static void clone_culture_name(Culture __instance)
    {
        __instance.data.name = __instance.data.creator_kingdom_name.Split(' ')[0]+"-"+ __instance.data.creator_city_name.Split(' ')[0]+ LM.Get("EvolvedCulture");
        LogService.LogInfo("当前文化名称: " + __instance.data.name);
    }
    [Hotfixable]
    private static void setDefaultNameTemplate(Culture culture)
    {

        string species = culture.data.creator_species_id;
        LogService.LogInfo("当前文化物种: " + species);
        switch (species)
        {
            case "human":
                insertCultureNameTemplate(culture, ModClass.HUMAN_CULTURE);
                break;
            case "orc":
                insertCultureNameTemplate(culture, ModClass.ORC_CULTURE);
                break;
            case "elf":
                insertCultureNameTemplate(culture, ModClass.ELF_CULTURE);
                break;
            case "dwarf":
                insertCultureNameTemplate(culture, ModClass.DWARF_CULTURE);
                break;
            default:
                break;
        }
    }

    public static void insertCultureNameTemplate(Culture culture, string cultureName)
    {
        string culturePath = ModPath + cultureName + "/";
        OnomasticsData kindomData = culture.getOnomasticData(MetaType.Kingdom);
        OnomasticsData clanData = culture.getOnomasticData(MetaType.Clan);
        OnomasticsData familyData = culture.getOnomasticData(MetaType.Family);
        OnomasticsData CityData = culture.getOnomasticData(MetaType.City);
        OnomasticsData UnitData = culture.getOnomasticData(MetaType.Unit);
        kindomData.clearTemplateData();
        kindomData.groups.Clear();
        OnomasticsType[] kindomTemplateData ={ 
            OnomasticsType.group_1, OnomasticsType.space, OnomasticsType.group_3, OnomasticsType.group_4 
        };
        kindomData.setTemplateData(OnomasticsTypeExtensions.ToStringList(kindomTemplateData));
        string countryNamePath = culturePath + String.Format("{0}CountryNames.csv", cultureName);
        ArrayList countryNameKeys = getKeysFromPath(countryNamePath);
        kindomData.setGroup("group_1", countryNameKeys.ToArray().Join(t => LM.Get((string)t), " "));
        kindomData.setGroup("group_4", LM.Get("Country"));

        CityData.clearTemplateData();
        CityData.groups.Clear();
        OnomasticsType[] cityTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.group_2, OnomasticsType.space, OnomasticsType.group_3
                };
        string cityName1Path = culturePath + String.Format("{0}CityNames1.csv", cultureName);
        ArrayList cityName1Keys = getKeysFromPath(cityName1Path);
        string cityName2Path = culturePath + String.Format("{0}CityNames2.csv", cultureName);
        ArrayList cityName2Keys = getKeysFromPath(cityName2Path);
        CityData.setTemplateData(OnomasticsTypeExtensions.ToStringList(cityTemplateData));
        CityData.setGroup("group_1", cityName1Keys.ToArray().Join(t => LM.Get((string)t), " "));
        CityData.setGroup("group_2", cityName2Keys.ToArray().Join(t => LM.Get((string)t), " "));
        CityData.setGroup("group_3", LM.Get("City"));

        clanData.clearTemplateData();
        clanData.groups.Clear();
        OnomasticsType[] clanTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.space, OnomasticsType.group_2
                };
        string clanNamePath = culturePath + String.Format("{0}ClanNames.csv", cultureName);
        ArrayList clanNameKeys = getKeysFromPath(clanNamePath);
        clanData.setTemplateData(OnomasticsTypeExtensions.ToStringList(clanTemplateData));
        clanData.setGroup("group_1", clanNameKeys.ToArray().Join(t => LM.Get((string)t), " "));
        clanData.setGroup("group_2", LM.Get("Clan"));

        familyData.clearTemplateData();
        familyData.groups.Clear();
        OnomasticsType[] familyTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.space, OnomasticsType.group_2
                };
        string familyNamePath = culturePath + String.Format("{0}FamilyNames.csv", cultureName);
        ArrayList familyNameKeys = getKeysFromPath(familyNamePath);
        familyData.setTemplateData(OnomasticsTypeExtensions.ToStringList(familyTemplateData));
        familyData.setGroup("group_1", clanNameKeys.ToArray().Join(t => LM.Get((string)t), " "));
        familyData.setGroup("group_2", LM.Get("Family"));

        UnitData.clearTemplateData();
        UnitData.groups.Clear();
        OnomasticsType[] unitTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.sex_male, OnomasticsType.coin_flip, OnomasticsType.group_2, OnomasticsType.sex_male,
                    OnomasticsType.group_3, OnomasticsType.sex_female, OnomasticsType.coin_flip, OnomasticsType.group_4,OnomasticsType.sex_female, 
                };
        UnitData.setTemplateData(OnomasticsTypeExtensions.ToStringList(unitTemplateData));
        string unitNameMale1Path = culturePath + String.Format("{0}UnitNamesMale1.csv", cultureName);
        ArrayList unitNameMale1Keys = getKeysFromPath(unitNameMale1Path);
        string unitNameMale2Path = culturePath + String.Format("{0}UnitNamesMale2.csv", cultureName);
        ArrayList unitNameMale2Keys = getKeysFromPath(unitNameMale2Path);
        string unitNameFemale1Path = culturePath + String.Format("{0}UnitNamesFemale1.csv", cultureName);
        ArrayList unitNameFemale1Keys = getKeysFromPath(unitNameFemale1Path);
        string unitNameFemale2Path = culturePath + String.Format("{0}UnitNamesFemale2.csv", cultureName);
        ArrayList unitNameFemale2Keys = getKeysFromPath(unitNameFemale2Path);
        UnitData.setGroup("group_1", unitNameMale1Keys.ToArray().Join(t => LM.Get((string)t), " "));
        UnitData.setGroup("group_2", unitNameMale2Keys.ToArray().Join(t => LM.Get((string)t), " "));
        UnitData.setGroup("group_3", unitNameFemale1Keys.ToArray().Join(t => LM.Get((string)t), " "));
        UnitData.setGroup("group_4", unitNameFemale2Keys.ToArray().Join(t => LM.Get((string)t), " "));
    }

    public static ArrayList getKeysFromPath(string path)
    {
        if (!File.Exists(path))
        {
            LogService.LogWarning("File not found: " + path);
            return null;
        }
        else
        {
            string[] lines = File.ReadAllLines(path);
            int index = 0;
            ArrayList keys = new();
            foreach (string line in lines)
            {
                string[] strings = line.Split(',');
                if (index != 0)
                {
                    keys.Add(strings[0]);
                }
                index++;
            }
            return keys;
        }
    }
}