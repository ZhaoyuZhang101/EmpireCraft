﻿using EmpireCraft.Scripts.Enums;
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
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;

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
        new Harmony(nameof(getMaxCities)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getMaxCities)),
            prefix: new HarmonyMethod(GetType(), nameof(getMaxCities))
        );
    }

    public static bool getMaxCities(Kingdom __instance, ref int __result)
    {
        int num = __instance.getActorAsset().civ_base_cities;
        if (__instance.hasKing())
        {
            num += (int)__instance.king.stats["cities"];
        }
        if (__instance.isEmpire())
        {
            foreach (Province province in __instance.GetEmpire().province_list)
            {
                if (province.HasOfficer()&&!province.IsTotalVassaled())
                {
                    num += (int)province.officer.stats["cities"];
                }
            }
        }
        if (num < 1)
        {
            num = 1;
        }
        __result = num;
        return false;
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
        if(__instance.isProvince())
        {
            Province province = __instance.GetProvince();
            if (province != null)
            {
                province.data.is_set_to_country = false;
                province.SetProvinceLevel(provinceLevel.provincelevel_3);
            }
        }
        __instance.RemoveExtraData<Kingdom, KingdomExtraData>();
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

            if (__instance.HasMainTitle())
            {
                if (__instance.isInEmpire() && !__instance.isEmpire())
                {
                    if (pActor.clan == __instance.GetEmpire().empire_clan)
                    {
                        pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_1);
                    } else
                    {
                        pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_2);
                    }

                } else if (!__instance.isInEmpire())
                {
                    pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_1);
                }
            }
            if (__instance.isEmpire())
            {
                __instance.GetEmpire().setEmperor(pActor);
            } else if (__instance.isInEmpire()&&!__instance.isEmpire())
            {
                Empire empire = __instance.GetEmpire();
                OfficeIdentity identity = pActor.GetIdentity(empire);
                if (identity == null)
                {
                    identity = new OfficeIdentity();
                    identity.init(empire, pActor);
                    pActor.SetIdentity(identity, true);
                }
                pActor.ChangeOfficialLevel(OfficialLevel.officiallevel_8);
                pActor.SetIdentityType(PeerageType.Military);
                pActor.addTrait("officer");
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
            if (__instance.isInEmpire() && !__instance.isEmpire())
            {
                if (__instance.king != null)
                {
                    try
                    {
                        __instance.king.GetIdentity(__instance.GetEmpire()).ChangeOfficialLevel(Enums.OfficialLevel.officiallevel_10);
                    }
                    catch
                    {
                        return;
                    }
                    
                }
            }
        }
    }

    public static void Initialize_level(Kingdom __instance, Actor pActor)
    {
        __instance.SetCountryLevel(Enums.countryLevel.countrylevel_3);
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
