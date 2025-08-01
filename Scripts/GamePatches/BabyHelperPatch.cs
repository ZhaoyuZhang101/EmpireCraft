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
        new Harmony(nameof(canMakeBabies)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.canMakeBabies)),
            prefix: new HarmonyMethod(GetType(), nameof(canMakeBabies))
        );
        new Harmony(nameof(makeBaby)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.makeBaby)),
            postfix: new HarmonyMethod(GetType(), nameof(makeBaby))
        );
    }

    public static SpecificClan getSpecificClanToJoin(Actor actor)
    {
        if (actor != null)
        {
            if (actor.HasSpecificClan())
            {
                SpecificClan specificClan = actor.GetSpecificClan();
                if (actor.isSexMale() && specificClan.isMalePriority())
                {
                    return specificClan;
                }
                else if (actor.isSexFemale() && !specificClan.isMalePriority())
                {
                    return specificClan;
                }
            }
        }
        return null;
    }

    public static void makeBaby(Actor pParent1, Actor pParent2, ActorSex pForcedSexType, bool pCloneTraits, int pMutationRate, WorldTile pTile, bool pAddToFamily, bool pJoinFamily, ref Actor __result)
    {
        SpecificClan specificClan = getSpecificClanToJoin(pParent1);
        if (specificClan == null)
        {
            specificClan = getSpecificClanToJoin(pParent2);
        }
        if (specificClan == null) return;
        specificClan.addActor(__result);
        LogService.LogInfo("3");
    }

    public static bool isMetaLimitsReached(Actor pActor, ref bool __result)
    {
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
