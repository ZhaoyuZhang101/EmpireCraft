using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EmpireCraft.Scripts.GamePatches;

public class KingdomPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(RemovePatchData)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.clear)),
            prefix: new HarmonyMethod(GetType(), nameof(RemovePatchData))
        );         
        new Harmony(nameof(Initialize_level)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.newCivKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(Initialize_level))
        );         
    }

    public static void Initialize_level(Kingdom __instance, Actor pActor)
    {
        __instance.SetCountryLevel(Enums.countryLevel.countrylevel_2);
        __instance.SetEmpireID(-1L);
        __instance.SetVassaledKingdomID(-1L);
    }
    public static void RemovePatchData(Kingdom __instance)
    {
        Empire empire = __instance.GetEmpire();
        if (empire == null) return; 
        if (__instance.isEmpire())
        {
            empire.checkDisolve(__instance);
        }else
        {
            empire.leave(__instance);
        }
    }
}
