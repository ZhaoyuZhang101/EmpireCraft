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
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
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
        new Harmony(nameof(newCivKingdom)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.newCivKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(newCivKingdom))
        );        
        new Harmony(nameof(new_emperor)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.setKing)),
            prefix: new HarmonyMethod(GetType(), nameof(new_emperor))
        );           
        new Harmony(nameof(emperor_left)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.removeKing)),
            prefix: new HarmonyMethod(GetType(), nameof(emperor_left))
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
        __instance.RemoveExtraData<Kingdom, KingdomExtraData>();
    }

    public static void new_emperor(Kingdom __instance, Actor pActor, bool pNewKing = true)
    {
        if (!ModClass.IS_CLEAR)
        {
            pActor.CheckSpecificClan();
            __instance.SetSpecificClan(pActor.GetSpecificClan());
            if (__instance.HasTitle())
            {
                foreach (var titleID in __instance.GetOwnedTitle())
                {
                    pActor.AddOwnedTitle(ModClass.KINGDOM_TITLE_MANAGER.get(titleID));
                }
            }

            if (__instance.HasMainTitle())
            {
                if (__instance.isInEmpire() && !__instance.isEmpire())
                {
                    if (pActor.clan == __instance.GetEmpire().EmpireClan)
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
                __instance.GetEmpire().NewEmperor(pActor);
            } else if (__instance.isInEmpire()&&!__instance.isEmpire())
            {
                Empire empire = __instance.GetEmpire();
                OfficeIdentity identity = pActor.GetIdentity();
                if (identity == null)
                {
                    identity = new OfficeIdentity();
                    identity.init(pActor);
                    pActor.SetIdentity(identity, true);
                }
                pActor.ChangeOfficialLevel( 8);
                pActor.SetIdentityType(PeerageType.Military);
                pActor.addTrait("officer");
            }
            __instance.RemoveHeir();
        }
    }

    public static void emperor_left(Kingdom __instance)
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
                        __instance.king.GetIdentity().ChangeOfficialLevel(-1);
                    }
                    catch
                    {
                        return;
                    }
                    
                }
            }
        }
    }

    public static void newCivKingdom(Kingdom __instance, Actor pActor)
    {
        __instance.SetLevel(4);
        __instance.SetEmpireID(-1L);
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(__instance.species_id, out Setting setting))
        {
            __instance.SetRegimeType(setting.regime);
        }
        else
        {
            __instance.SetRegimeType(RegimeType.Feudalism);
        }
        __instance.LoadRegime();
    }
    public static void RemovePatchData(Kingdom __instance)
    {
        Empire empire = __instance.GetEmpire();
        if (empire == null) return;
        if (__instance.isEmpire())
        {
            empire.CheckDissolve(__instance);
        }
        else
        {
            empire.leave(__instance);
        }
    }
}
