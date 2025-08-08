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
        new Harmony(nameof(createCulture)).Patch(AccessTools.Method(typeof(Culture), nameof(Culture.createCulture)),
            postfix: new HarmonyMethod(GetType(), nameof(createCulture)));
        LogService.LogInfo("文化模板加载成功");
    }

    private static void createCulture(Actor __instance, string pCultureName)
    {
        // todo: 建立文化时触发
    }
}