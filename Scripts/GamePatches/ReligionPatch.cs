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
        LogService.LogInfo("语言命名模板加载成功");
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

    }
    public static void InsertReligionNameTemplate(Religion religion, string cultureName)
    {
        string culturePath = ModPath + $"Cultures/Culture_{cultureName}/";
        string religionNamePath = culturePath + $"{cultureName}ReligionNames.csv";
        List<string> religionKeys = OnomasticsHelper.getKeysFromPath(religionNamePath);
        religion.data.name = LM.Get(religionKeys[UnityEngine.Random.Range(0, religionKeys.Count)].ToString());
        LogService.LogInfo(cultureName + "宗教名称: " + religion.data.name);
    }
}
