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
        new Harmony(nameof(NewCivKingdom)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.newCivKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(NewCivKingdom))
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

        if (__instance.HasGivenAlliance())
        {
            __instance.RemoveGivenAlliance();
        }

        if (__instance.HasTakenAlliance())
        {
            __instance.RemoveTakenAlliance();
        }
        __instance.RemoveExtraData<Kingdom, KingdomExtraData>();
    }

    public static void new_emperor(Kingdom __instance, Actor pActor, bool pFromLoad)
    {
        if (!ModClass.IS_CLEAR)
        {
            pActor.CheckSpecificClan();
            __instance.SetSpecificClan(pActor.GetSpecificClan());
            foreach (var kt in ModClass.KINGDOM_TITLE_MANAGER)
            {
                if (kt.main_kingdom == __instance)
                {
                    pActor.AddOwnedTitle(kt);
                }
            }

            if (__instance.HasMainTitle())
            {
                if (__instance.IsInEmpire() && !__instance.IsEmpire())
                {
                    if (pActor.clan == __instance.GetEmpire().EmpireClan)
                    {
                        pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_1);
                    } else
                    {
                        pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_2);
                    }

                } else if (!__instance.IsInEmpire())
                {
                    pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_1);
                }
            }
            if (__instance.IsEmpire())
            {
                __instance.GetEmpire().NewEmperor(pActor);
                LogService.LogInfo("触发原版选择国王");
            }
            __instance.RemoveHeir();
        }
    }

    public static void emperor_left(Kingdom __instance)
    {
        if (ModClass.IS_CLEAR) return;
        if (__instance.king.HasOfficeIdentity())
        {
            var officeIdentity = __instance.king.GetIdentity();
            officeIdentity?.RemoveOffice();
        }
        if (__instance.IsEmpire())
        {
            __instance.GetEmpire()?.EmperorLeft(__instance);
        }
    }

    public static void NewCivKingdom(Kingdom __instance, Actor pActor)
    {
        __instance.SetLevel(4);
        __instance.SetEmpireID(-1L);
        var culture = ConfigData.speciesCulturePair.TryGetValue(pActor.asset.id, out string speciesCulture)? speciesCulture : "Western";
        RegimeType regimeType = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting.regime
            : RegimeType.Feudalism;
        __instance.SetRegimeType(regimeType);
        __instance.LoadRegime();
        Regime regime = __instance.GetRegime();
        regime.SetAllowDiplomacy(true);
        regime.SetAllowArmy(true);
    }
    public static void RemovePatchData(Kingdom __instance)
    {
        Empire empire = __instance.GetEmpire();
        if (empire == null) return;
        if (__instance.IsEmpire())
        {
            empire.CheckDissolve(__instance);
        }
        else
        {
            empire.leave(__instance);
        }
    }
}
