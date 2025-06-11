using HarmonyLib;
using System;
using NeoModLoader.services;
using NeoModLoader.General;
using NeoModLoader.api;
using System.Text.RegularExpressions;
using System.Collections;

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
        switch (species)
        {
            case "human":
                insertReligionNameTemplate(__instance, ModClass.HUMAN_CULTURE);
                break;
            case "orc":
                insertReligionNameTemplate(__instance, ModClass.ORC_CULTURE);
                break;
            case "elf":
                insertReligionNameTemplate(__instance, ModClass.ELF_CULTURE);
                break;
            case "dwarf":
                insertReligionNameTemplate(__instance, ModClass.DWARF_CULTURE);
                break;
            default:
                break;
        }

    }
    public static void insertReligionNameTemplate(Religion religion, string cultureName)
    {
        string culturePath = ModPath + cultureName + "/";
        string religionNamePath = culturePath + String.Format("{0}ReligionNames.csv", cultureName);
        ArrayList religionKeys = CulturePatch.getKeysFromPath(religionNamePath);
        religion.data.name = LM.Get(religionKeys[UnityEngine.Random.Range(0, religionKeys.Count)].ToString());
        LogService.LogInfo(cultureName + "宗教名称: " + religion.data.name);
    }
}
