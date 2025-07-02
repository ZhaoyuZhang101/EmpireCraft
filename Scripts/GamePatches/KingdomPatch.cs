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
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(RemovePatchData))
        );         
        new Harmony(nameof(Initialize_level)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.newCivKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(Initialize_level))
        );           
        new Harmony(nameof(new_emperor)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.setKing)),
            prefix: new HarmonyMethod(GetType(), nameof(new_emperor))
        );           
        new Harmony(nameof(empror_left)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.removeKing)),
            prefix: new HarmonyMethod(GetType(), nameof(empror_left))
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
        if (__instance.HasMainTitle())
        {
            if (__instance.GetMainTitle() != null)
            {
                __instance.GetMainTitle().main_kingdom = null;
            }
        }
        __instance.RemoveExtraData();
    }

    public static void new_emperor(Kingdom __instance, Actor pActor, bool pNewKing = true)
    {
        if (!ModClass.IS_CLEAR)
        {
            if (__instance.HasTitle())
            {
                foreach (var title_id in __instance.GetOwnedTitle())
                {
                    pActor.AddOwnedTitle(ModClass.KINGDOM_TITLE_MANAGER.get(title_id));
                }
            }
            if (__instance.isEmpire())
            {
                __instance.GetEmpire().setEmperor(pActor);
            }
        }
    }

    public static void empror_left(Kingdom __instance)
    {
        if (!ModClass.IS_CLEAR)
        {
            if (__instance.king.HasTitle())
            {
                __instance.SetOwnedTitle(__instance.king.GetOwnedTitle());
                __instance.king.ClearTitle();
            }
            if (__instance.isEmpire())
            {
                __instance.GetEmpire().EmperorLeft(__instance);
            }
        }
    }

    public static void Initialize_level(Kingdom __instance, Actor pActor)
    {
        __instance.SetCountryLevel(Enums.countryLevel.countrylevel_4);
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
        }
        else
        {
            empire.leave(__instance);
        }
    }
}
