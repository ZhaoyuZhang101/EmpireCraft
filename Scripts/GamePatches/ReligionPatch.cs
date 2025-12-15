using HarmonyLib;
using System;
using NeoModLoader.services;
using NeoModLoader.General;
using NeoModLoader.api;
using System.Text.RegularExpressions;
using System.Collections;
using EmpireCraft.Scripts.HelperFunc;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GamePatches;
public class ReligionPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static string ModPath;
    public void Initialize()
    {
        ModPath = declare.FolderPath + "/Locales/";
        new Harmony(nameof(set_religion_name)).Patch(AccessTools.Method(typeof(Religion), nameof(Religion.newReligion)),
        postfix: new HarmonyMethod(GetType(), nameof(set_religion_name)));
        LogService.LogInfo("宗教Patch加载成功");
    }

    private static void set_religion_name(Religion __instance, Actor pActor, WorldTile pTile, bool pAddDefaultTraits)
    {
        string species = __instance.species_id;
        LogService.LogInfo("当前文化物种: " + species);
        if (ConfigData.speciesCulturePair.TryGetValue(species, out string culture))
        {
            InsertReligionNameTemplate(__instance, culture);
        }
        else
        {
            InsertReligionNameTemplate(__instance, "Western");
        }
        __instance.SetCity(pActor?.city??pActor?.current_tile?.zone_city);

    }
    public static void InsertReligionNameTemplate(Religion religion, string cultureName)
    {
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out var setting))
        {
            if (!string.IsNullOrEmpty(setting.Religion))
            {
                var nameSet = setting.Religion.Split(' ');
                religion.data.name = nameSet.GetRandom();
                LogService.LogInfo(cultureName + "宗教名称: " + religion.data.name);
                return;
            }
        }
        string culturePath = ModPath + $"Cultures/Culture_{cultureName}/";
        string religionNamePath = culturePath + $"{cultureName}ReligionNames.csv";
        List<string> religionKeys = OnomasticsHelper.getKeysFromPath(religionNamePath);
        religion.data.name = LM.Get(religionKeys[UnityEngine.Random.Range(0, religionKeys.Count)]);
        LogService.LogInfo(cultureName + "宗教名称: " + religion.data.name);
    }
}
