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

    private static void set_culture_name(Culture __instance, Actor pActor)
    {
        __instance.data.name = pActor.kingdom.name.Split(' ')[0] + "-" + pActor.city.name.Split(' ')[0] + LM.Get("Culture");
        LogService.LogInfo("当前文化名称: " + __instance.data.name);
        setDefaultNameTemplate(__instance);

    }
    private static void clone_culture_name(Culture __instance)
    {
        __instance.data.name = __instance.data.creator_kingdom_name.Split(' ')[0]+"-"+ __instance.data.creator_city_name.Split(' ')[0]+ LM.Get("EvolvedCulture");
        LogService.LogInfo("当前文化名称: " + __instance.data.name);
    }
    private static void setDefaultNameTemplate(Culture culture)
    {

        string species = culture.data.creator_species_id;
        LogService.LogInfo("当前文化物种: " + species);
        if (ConfigData.speciesCulturePair.TryGetValue(species, out var insertCulture))
        {
            insertCultureNameTemplate(culture, insertCulture);
        }
        else
        {
            insertCultureNameTemplate(culture, "Western");
        }
    }

    public static void insertCultureNameTemplate(Culture culture, string cultureName)
    {
        OnomasticsData kindomData = culture.getOnomasticData(MetaType.Kingdom);
        OnomasticsData clanData = culture.getOnomasticData(MetaType.Clan);
        OnomasticsData familyData = culture.getOnomasticData(MetaType.Family);
        OnomasticsData CityData = culture.getOnomasticData(MetaType.City);
        OnomasticsData unitData = culture.getOnomasticData(MetaType.Unit);
        
        kindomData.clearTemplateData();
        kindomData.groups.Clear();


        OnomasticsType[] kindomTemplateData ={ 
            OnomasticsType.group_1, OnomasticsType.space, OnomasticsType.group_2
        };
        OnomasticsHelper.Configure(
            kindomData,
            cultureName,
            kindomTemplateData,
            (OnomasticsType.group_1.ToString(), "CountryNames", null),
            (OnomasticsType.group_2.ToString(), null, LM.Get("Country"))
            );


        OnomasticsType[] cityTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.group_2, OnomasticsType.space, OnomasticsType.group_3
                };
        OnomasticsHelper.Configure(
            CityData,
            cultureName,
            cityTemplateData,
            (OnomasticsType.group_1.ToString(), "CityNames1", null),
            (OnomasticsType.group_2.ToString(), "CityNames2", null),
            (OnomasticsType.group_3.ToString(), "CityNames3", null)
            );

        OnomasticsType[] clanTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.space, OnomasticsType.group_2
                };

        OnomasticsHelper.Configure(
            clanData,
            cultureName,
            clanTemplateData,
            (OnomasticsType.group_1.ToString(), "ClanNames", null),
            (OnomasticsType.group_2.ToString(), null, LM.Get("Clan"))
            );

        OnomasticsType[] familyTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.space, OnomasticsType.group_2
                };

        OnomasticsHelper.Configure(
            familyData,
            cultureName,
            familyTemplateData,
            (OnomasticsType.group_1.ToString(), "FamilyNames", null),
            (OnomasticsType.group_2.ToString(), null, LM.Get("Family"))
            );


        OnomasticsType[] unitTemplateData = {
                    OnomasticsType.group_1, OnomasticsType.sex_male, OnomasticsType.coin_flip, OnomasticsType.group_2, OnomasticsType.sex_male,
                    OnomasticsType.group_3, OnomasticsType.sex_female, OnomasticsType.coin_flip, OnomasticsType.group_4,OnomasticsType.sex_female
                };
        OnomasticsHelper.Configure(
            unitData,
            cultureName,
            unitTemplateData,
            (OnomasticsType.group_1.ToString(), "UnitNamesMale1", null),
            (OnomasticsType.group_2.ToString(), "UnitNamesMale2", null),
            (OnomasticsType.group_3.ToString(), "UnitNamesFemale1", null),
            (OnomasticsType.group_4.ToString(), "UnitNamesFemale2", null)

            );
    }
}