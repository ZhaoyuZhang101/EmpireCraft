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
        new Harmony(nameof(NewReligion)).Patch(AccessTools.Method(typeof(Religion), nameof(Religion.newReligion)),
        postfix: new HarmonyMethod(GetType(), nameof(NewReligion)));
        LogService.LogInfo("宗教补丁加载成功");
    }

    private static void NewReligion(Religion __instance, Actor pActor, WorldTile pTile, bool pAddDefaultTraits)
    {
        // todo: 创立宗教时触发
    }
}
