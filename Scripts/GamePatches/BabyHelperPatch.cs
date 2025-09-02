using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ai.behaviours;
using UnityEngine;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;

namespace EmpireCraft.Scripts.GamePatches;
public class BabyHelperPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(isMetaLimitsReached)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.isMetaLimitsReached)),
            prefix: new HarmonyMethod(GetType(), nameof(isMetaLimitsReached))
        );
        new Harmony(nameof(applyParentsMeta)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.applyParentsMeta)),
            postfix: new HarmonyMethod(GetType(), nameof(applyParentsMeta))
        );
        new Harmony(nameof(makeBaby)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.makeBaby)),
            postfix: new HarmonyMethod(GetType(), nameof(makeBaby))
        );
        new Harmony(nameof(canMakeBabies)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.canMakeBabies)),
            prefix: new HarmonyMethod(GetType(), nameof(canMakeBabies))
        );
        new Harmony(nameof(makeBabyFromMiracle)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.makeBabyFromMiracle)),
            prefix: new HarmonyMethod(GetType(), nameof(makeBabyFromMiracle))
        );
        new Harmony(nameof(spawnBabyFromSpore)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.spawnBabyFromSpore)),
            prefix: new HarmonyMethod(GetType(), nameof(spawnBabyFromSpore))
        );
        new Harmony(nameof(CheckReproduction)).Patch(
            AccessTools.Method(typeof(BehCheckParthenogenesisReproduction), nameof(BehCheckFissionReproduction.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(CheckReproduction))
        );
        new Harmony(nameof(CheckReproduction)).Patch(
            AccessTools.Method(typeof(BehCheckParthenogenesisReproduction), nameof(BehCheckParthenogenesisReproduction.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(CheckReproduction))
        );
        new Harmony(nameof(actionBabyFinish)).Patch(
            AccessTools.Method(typeof(StatusLibrary), nameof(StatusLibrary.actionBuddingFinish)),
            prefix: new HarmonyMethod(GetType(), nameof(actionBabyFinish))
        );
        new Harmony(nameof(actionBabyFinish)).Patch(
            AccessTools.Method(typeof(StatusLibrary), nameof(StatusLibrary.actionTakingRootsFinish)),
            prefix: new HarmonyMethod(GetType(), nameof(actionBabyFinish))
        );
        new Harmony(nameof(actionBabyFinish)).Patch(
            AccessTools.Method(typeof(StatusLibrary), nameof(StatusLibrary.actionPregnancyFinish)),
            prefix: new HarmonyMethod(GetType(), nameof(actionBabyFinish))
        );
    }

    public static void makeBaby(BabyMaker __instance, Actor pParent1, Actor pParent2, ActorSex pForcedSexType,
        bool pCloneTraits, int pMutationRate, WorldTile pTile, bool pAddToFamily,
        bool pJoinFamily, ref Actor __result)
    {
        if (__result == null) return;
        if (__result.HasSpecificClan())
        {
            PersonalClanIdentity pci = __result.GetPersonalIdentity();
            pci.sex = __result.data.sex;
        }
    }

    public static void judgeBabyJoinMainParent(Actor pBaby, Actor pParent)
    {
        if (pParent != null)
        {
            if (pParent.HasSpecificClan())
            {
                PersonalClanIdentity pci = pParent.GetPersonalIdentity();
                if (pci.is_main)
                {
                    if (pParent.hasCulture())
                    {
                        pBaby.setCulture(pParent.GetCulture());
                    }
                    pBaby.setClan(pParent.clan);
                    pBaby.GetModName().familyName = pBaby.clan.GetClanName();
                    pBaby.GetModName().SetName(pBaby);
                }
            }
        }
    }
    public static void applyParentsMeta(Actor pParent1, Actor pParent2, Actor pBaby)
    {
        judgeBabyJoinMainParent(pBaby, pParent1);
        judgeBabyJoinMainParent(pBaby, pParent2);
    }

    public static bool CheckReproduction(Actor pActor, ref BehResult __result)
    {
        if (BabyHelper.isMetaLimitsReached(pActor))
        {
            __result = BehResult.Stop;
            return false;
        }
        return true;
    }


    public static bool spawnBabyFromSpore(Actor pActor, Vector3 pPosition)
    {
        if (BabyHelper.isMetaLimitsReached(pActor))
        {
            return false;
        }
        return true;
    }

    public static bool makeBabyFromMiracle(Actor pActor, ActorSex pSex = ActorSex.None, bool pAddToFamily = false)
    {
        if (BabyHelper.isMetaLimitsReached(pActor))
        {
            return false;
        }
        return true;
    }
    
    public static bool actionBabyFinish(BaseSimObject pTarget, WorldTile pTile, ref bool __result)
    {
        if (!pTarget.isAlive())
        {
            __result = false;
            return false;
        }
        Actor actor = pTarget.a;
        if (BabyHelper.isMetaLimitsReached(actor))
        {
            __result = true;
            return false;
        }
        return true;
    }

    public static bool isMetaLimitsReached(Actor pActor, ref bool __result)
    {
        __result = false;
        if (pActor==null) return false;
        if (pActor.subspecies.hasReachedPopulationLimit())
        {
            __result = true;
            return false;
        }
        if (pActor.hasCity())
        {
            if (pActor.city.hasReachedWorldLawLimit())
            {
                __result = true;
                return false;
            }
            if (pActor.city.HasReachedPlayerPopLimit())
            {
                __result = true; 
                return false;
            }
            Actor lover = pActor.lover;
            bool num = pActor.isImportantPerson() && !pActor.hasReachedOffspringLimit();
            bool flag = lover != null && lover.isImportantPerson() && !lover.hasReachedOffspringLimit();
            if (num || flag)
            {
                __result = false;
                return false;
            }
            if (pActor.subspecies.isReproductionSexual() && pActor.current_children_count == 0)
            {
                __result = false;
                return false;
            }
            if (!pActor.city.hasFreeHouseSlots())
            {
                __result = true;
                return false;
            }
        }
        __result = false;
        return false;
    }

    public static bool canMakeBabies(Actor pActor, ref bool __result)
    {
        if (!pActor.isAdult())
        {
            __result = false;
            return false;
        }
        if (pActor.hasCity())
        {
            if (pActor.city.HasReachedPlayerPopLimit())
            {
                __result = false;
                return false;
            }
        }
        if (pActor.hasReachedOffspringLimit())
        {
            __result = false;
            return false;
        }
        if (!pActor.haveNutritionForNewBaby())
        {
            __result = false;
            return false;
        }
        __result = true;
        return false;
    }
}
