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
using EmpireCraft.Scripts.AI.KingdomAI;
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
            prefix: new HarmonyMethod(GetType(), nameof(before_new_emperor)),
            postfix: new HarmonyMethod(GetType(), nameof(new_emperor))
        );           
        new Harmony(nameof(emperor_left)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.removeKing)),
            prefix: new HarmonyMethod(GetType(), nameof(emperor_left))
        );             
        new Harmony(nameof(GetMaxCities)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getMaxCities)),
            prefix: new HarmonyMethod(GetType(), nameof(GetMaxCities))
        );               
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(removeData))
        );
        new Harmony(nameof(countTotalWarriors)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.countTotalWarriors)),
            prefix: new HarmonyMethod(GetType(), nameof(countTotalWarriors)));
        new Harmony(nameof(getPopulationPeople)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.getPopulationPeople)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationPeople)));
    }

    public static bool GetMaxCities(Kingdom __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        int maxCities = __instance.getActorAsset().civ_base_cities;
        if (__instance.hasKing())
            maxCities += (int) __instance.king.stats["cities"];
        if (maxCities < 1)
            maxCities = 1;
        __result = maxCities;
        return false;
    }

    public static void removeData(Kingdom __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance == null)
        {
            return;
        }
        KingdomExtraData extraData = __instance.GetOrCreate();
        KingdomTitle mainTitle = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.MainTitle);
        if (mainTitle != null)
        {
            mainTitle.EndJurisdiction(__instance, KingdomTitle.JurisdictionHolder);
            if (mainTitle.main_kingdom == __instance) mainTitle.main_kingdom = null;
        }
        KingdomTitle administrativeTitle = ModClass.KINGDOM_TITLE_MANAGER.get(extraData.AdministrativeTitle);
        administrativeTitle?.EndJurisdiction(__instance, KingdomTitle.JurisdictionAdministration);
        if (__instance.HasGivenAlliance())
        {
            __instance.RemoveGivenAlliance();
        }

        if (__instance.HasTakenAlliance())
        {
            __instance.RemoveTakenAlliance(recordHistory: false);
        }
        __instance.RemoveExtraData<Kingdom, KingdomExtraData>();
    }

    public static void new_emperor(Kingdom __instance, Actor pActor, bool pFromLoad, Actor __state)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (pActor == null) return;
        if (!ModClass.IS_CLEAR)
        {
            pActor.CheckSpecificClan();
            __instance.SetSpecificClan(pActor.GetSpecificClan());
            __instance.TransferRealmTitlesToRuler(pActor);
            (__instance.GetEmpire() ?? __instance.GetTakenAllianceEmpire())?
                .SynchronizeLandedLegalTitles(__instance);
            foreach (var kt in ModClass.KINGDOM_TITLE_MANAGER)
            {
                if (kt.main_kingdom == __instance)
                {
                    if (kt.owner != pActor) pActor.AddOwnedTitle(kt);
                }
            }

            Regime regime = __instance.GetRegime();
            if (regime?.type == RegimeType.Feudalism)
            {
                KingdomType kingdomType = EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(__instance);
                if (WesternPeerageRules.TryGetRulerLevel(kingdomType, out PeeragesLevel level))
                {
                    pActor.SetPeeragesLevel(level);
                }
            }
            else if (__instance.HasMainTitle())
            {
                if (__instance.IsInEmpire() && !__instance.IsEmpire())
                {
                    Empire localEmpire = __instance.GetEmpire();
                    bool isImperialClan = pActor.GetSpecificClan() != null &&
                                           pActor.GetSpecificClan() == localEmpire?.EmpireSpecificClan;
                    pActor.SetPeeragesLevel(isImperialClan
                        ? Enums.PeeragesLevel.peerages_2
                        : Enums.PeeragesLevel.peerages_3);
                    localEmpire?.SynchronizeLandedLegalTitles(__instance);

                } else if (!__instance.IsInEmpire())
                {
                    pActor.SetPeeragesLevel(Enums.PeeragesLevel.peerages_1);
                }
            }
            if (__instance.IsEmpire())
            {
                Empire empire = __instance.GetEmpire();
                bool isActualSuccession = !pFromLoad && (__state == null || __state.id != pActor.id);
                if (isActualSuccession)
                {
                    empire?.NewEmperor(pActor);
                    if (empire != null)
                    {
                        pActor.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_emperor"), empire.GetEmpireName()));
                    }
                }
                else
                {
                    empire?.RepairFoundingEmperorMarker();
                }
                LogService.LogInfo("触发原版选择国王");
            }
            __instance.RemoveHeir();
        }
    }

    public static void before_new_emperor(Kingdom __instance, out Actor __state)
    {
        __state = __instance?.king;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (ModClass.IS_CLEAR || __instance == null) return;
        __instance.SyncRealmTitlesFromRuler(__instance.king);
    }

    public static void emperor_left(Kingdom __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (ModClass.IS_CLEAR) return;
        Actor king = __instance.king;
        __instance.SyncRealmTitlesFromRuler(king);
        if (__instance.HasMainCrime()) __instance.RemoveMainCrime();
        if (king != null && king.HasOfficeIdentity())
        {
            var officeIdentity = king.GetIdentity();
            officeIdentity?.RemoveOffice();
        }
        if (__instance.IsEmpire())
        {
            __instance.GetEmpire()?.EmperorLeft();
        }
    }

    public static void NewCivKingdom(Kingdom __instance, Actor pActor)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        __instance.RememberInitialRandomKingdomName();
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
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
    public static bool countTotalWarriors(Kingdom __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        var ed = __instance.GetOrCreate();
        if (ed is { last_cached_timestamp: > 0 })
        {
            __result = ed.cached_warriors;
            return false;
        }
        return true;
    }
    public static bool getPopulationPeople(Kingdom __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        var ed = __instance.GetOrCreate();
        if (ed is { last_cached_timestamp: > 0 })
        {
            __result = ed.cached_population;
            return false;
        }
        return true;
    }
}
