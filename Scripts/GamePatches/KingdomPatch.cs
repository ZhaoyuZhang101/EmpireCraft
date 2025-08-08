using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using EmpireCraft.Scripts.HelperFunc;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;

namespace EmpireCraft.Scripts.GamePatches;

public class KingdomPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(SetKing)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.setKing)),
            prefix: new HarmonyMethod(GetType(), nameof(SetKing))
        );           
        new Harmony(nameof(RemoveKing)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.removeKing)),
            prefix: new HarmonyMethod(GetType(), nameof(RemoveKing))
        );               
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(removeData))
        );            
    }

    public static void removeData(Kingdom __instance)
    {
        if (__instance == null)
        {
            return;
        }
        __instance.RemoveExtraData<Kingdom, KingdomExtraData>();
    }

    public static void SetKing(Kingdom __instance, Actor pActor, bool pNewKing = true)
    {
        // todo: 新国王上位时触发
    }
    public static void RemoveKing(Kingdom __instance)
    {
        // todo: 国王退位时触发
    }
}
