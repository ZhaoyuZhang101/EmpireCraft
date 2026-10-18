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
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;

namespace EmpireCraft.Scripts.GamePatches;
public class BabyHelperPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(IsMetaLimitsReached)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.isMetaLimitsReached)),
            prefix: new HarmonyMethod(GetType(), nameof(IsMetaLimitsReached))
        );
        new Harmony(nameof(ApplyParentsMeta)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.applyParentsMeta)),
            postfix: new HarmonyMethod(GetType(), nameof(ApplyParentsMeta))
        );
        new Harmony(nameof(MakeBaby)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.makeBaby)),
            postfix: new HarmonyMethod(GetType(), nameof(MakeBaby))
        );
        new Harmony(nameof(CanMakeBabies)).Patch(
            AccessTools.Method(typeof(BabyHelper), nameof(BabyHelper.canMakeBabies)),
            prefix: new HarmonyMethod(GetType(), nameof(CanMakeBabies))
        );
        new Harmony(nameof(MakeBabyFromMiracle)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.makeBabyFromMiracle)),
            prefix: new HarmonyMethod(GetType(), nameof(MakeBabyFromMiracle))
        );
        new Harmony(nameof(SpawnBabyFromSpore)).Patch(
            AccessTools.Method(typeof(BabyMaker), nameof(BabyMaker.spawnBabyFromSpore)),
            prefix: new HarmonyMethod(GetType(), nameof(SpawnBabyFromSpore))
        );
        new Harmony(nameof(CheckReproduction)).Patch(
            AccessTools.Method(typeof(BehCheckParthenogenesisReproduction), nameof(BehCheckFissionReproduction.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(CheckReproduction))
        );
        new Harmony(nameof(CheckReproduction)).Patch(
            AccessTools.Method(typeof(BehCheckParthenogenesisReproduction), nameof(BehCheckParthenogenesisReproduction.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(CheckReproduction))
        );
        new Harmony(nameof(ActionBabyFinish)).Patch(
            AccessTools.Method(typeof(StatusLibrary), nameof(StatusLibrary.actionBuddingFinish)),
            prefix: new HarmonyMethod(GetType(), nameof(ActionBabyFinish))
        );
        new Harmony(nameof(ActionBabyFinish)).Patch(
            AccessTools.Method(typeof(StatusLibrary), nameof(StatusLibrary.actionTakingRootsFinish)),
            prefix: new HarmonyMethod(GetType(), nameof(ActionBabyFinish))
        );
        new Harmony(nameof(ActionBabyFinish)).Patch(
            AccessTools.Method(typeof(StatusLibrary), nameof(StatusLibrary.actionPregnancyFinish)),
            prefix: new HarmonyMethod(GetType(), nameof(ActionBabyFinish))
        );
    }

    public static void MakeBaby(BabyMaker __instance, Actor pParent1, Actor pParent2, ActorSex pForcedSexType,
        bool pCloneTraits, int pMutationRate, WorldTile pTile, bool pAddToFamily,
        bool pJoinFamily, ref Actor __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pParent1)) return;
        if (__result == null) return;
        EmpireCraft.Scripts.GeneralSystems.RoyalHouseSystem.NoteBirth(pParent1, pParent2);
        City birthCity = __result.city ?? pParent1?.city ?? pParent2?.city;
        float localCultureChance = CultureService.GetNewbornLocalCultureChance(birthCity);
        if (birthCity != null && UnityEngine.Random.value < localCultureChance)
            CultureService.SyncActorToCityMainCulture(__result, birthCity);
        if (__result.HasSpecificClan())
        {
            PersonalClanIdentity pci = __result.GetPersonalIdentity();
            pci.sex = __result.data.sex;
        }
    }

    public static void JudgeBabyJoinMainParent(Actor pBaby, Actor pParent)
    {
        if (pBaby?.data != null && !pBaby.isRekt() && pParent?.data != null && !pParent.isRekt())
        {
            if (pParent.HasSpecificClan())
            {
                PersonalClanIdentity pci = pParent.GetPersonalIdentity();
                if (pci?.is_main == true || SurnameInheritanceSystem.Reigns(pParent))
                {
                    // 第一个父母回调可能已经建了族谱身份；最终入族须与最终选中的亲本一致。
                    PersonalClanIdentity childIdentity = pBaby.GetPersonalIdentity();
                    if (childIdentity != null && pci?._specificClan != null)
                    {
                        SpecificClanManager.MoveToClan(childIdentity, pci._specificClan);
                        childIdentity.matrilineal_birth = !pParent.isSexMale() && pci._specificClan.Matrilineal();
                    }
                    if (pParent.hasCulture())
                    {
                        pBaby.setCulture(pParent.GetCulture());
                    }
                    if (pParent.clan?.data == null) return;
                    pBaby.setClan(pParent.clan);
                    if (pBaby.clan?.data == null) return;
                    string surname = SurnameInheritanceSystem.FamilyNameOf(pParent);
                    pBaby.SetFamilyName(!string.IsNullOrWhiteSpace(surname) ? surname : pBaby.clan.GetMemberSurname());
                    pBaby.GetModName()?.SetName(pBaby);
                }
            }
        }
    }
    public static void ApplyParentsMeta(Actor pParent1, Actor pParent2, Actor pBaby)
    {
        if (pBaby?.data == null || pBaby.isRekt()) return;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pBaby)) return;
        // 原版已设置父母后才走这里：不能再依次套用两方，让最后执行的母亲覆盖已经选好的父系姓氏。
        Actor parent = SurnameInheritanceSystem.SelectParent(new[] { pParent1, pParent2 }.Concat(pBaby.getParents()));
        JudgeBabyJoinMainParent(pBaby, parent);
    }

    public static bool CheckReproduction(Actor pActor, ref BehResult __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
        if (BabyHelper.isMetaLimitsReached(pActor))
        {
            __result = BehResult.Stop;
            return false;
        }
        return true;
    }


    public static bool SpawnBabyFromSpore(Actor pActor, Vector3 pPosition)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
        if (BabyHelper.isMetaLimitsReached(pActor))
        {
            return false;
        }
        return true;
    }

    public static bool MakeBabyFromMiracle(Actor pActor, ActorSex pSex = ActorSex.None, bool pAddToFamily = false)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
        if (BabyHelper.isMetaLimitsReached(pActor))
        {
            return false;
        }
        return true;
    }
    
    public static bool ActionBabyFinish(BaseSimObject pTarget, WorldTile pTile, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pTarget)) return true;
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

    public static bool IsMetaLimitsReached(Actor pActor, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
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

    public static bool CanMakeBabies(Actor pActor, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
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
        // 君主、储君一年半至多添一个孩子，不育的不能生(见 RoyalHouseSystem)
        if (!EmpireCraft.Scripts.GeneralSystems.RoyalHouseSystem.CanBirth(pActor))
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
