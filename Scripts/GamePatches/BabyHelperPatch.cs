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
        // todo: 生孩子时触发
    }
    
    public static void ApplyParentsMeta(Actor pParent1, Actor pParent2, Actor pBaby)
    {
        // todo: 设置父母时触发
    }

    public static bool CheckReproduction(Actor pActor, ref BehResult __result)
    {
        // todo: 检测是否怀孕时触发，会重复检测
        return true;
    }


    public static bool SpawnBabyFromSpore(Actor pActor, Vector3 pPosition)
    {
        return true;
    }

    public static bool MakeBabyFromMiracle(Actor pActor, ActorSex pSex = ActorSex.None, bool pAddToFamily = false)
    {
        return true;
    }
    
    public static bool ActionBabyFinish(BaseSimObject pTarget, WorldTile pTile, ref bool __result)
    {
        // todo: 生完孩子后触发
        return true;
    }

    public static bool CanMakeBabies(Actor pActor, ref bool __result)
    {
        return true;
    }
}
