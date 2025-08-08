using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EpPathFinding.cs;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;

namespace EmpireCraft.Scripts.GamePatches;
public class ActorPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static int startSessionMonth { get; set; }
    public static bool isReadyToSet = false;
    public void Initialize()
    {
        ActorPatch.startSessionMonth = Date.getMonthsSince(World.world.getCurSessionTime());
        ActorPatch.isReadyToSet = true;
        // Actor类的补丁
        new Harmony(nameof(RemoveData)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(RemoveData)));
        new Harmony(nameof(SetArmy)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setArmy)),
            postfix: new HarmonyMethod(GetType(), nameof(SetArmy)));
        new Harmony(nameof(RemoveFromArmy)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.removeFromArmy)),
            prefix: new HarmonyMethod(GetType(), nameof(RemoveFromArmy)));
        new Harmony(nameof(SetKingdom)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(SetKingdom)));
        new Harmony(nameof(SetLover)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setLover)),
            postfix: new HarmonyMethod(GetType(), nameof(SetLover)));
        new Harmony(nameof(SetParent)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setParent1)),
            postfix: new HarmonyMethod(GetType(), nameof(SetParent)));
        new Harmony(nameof(SetParent)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setParent2)),
            postfix: new HarmonyMethod(GetType(), nameof(SetParent)));
        new Harmony(nameof(setCity)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setCity)),
            postfix: new HarmonyMethod(GetType(), nameof(setCity)));
        LogService.LogInfo("角色补丁加载成功");
    }

    public static void setCity(Actor __instance, City pCity)
    {
        // todo: 当角色加入城市时触发
    }
    public static void SetParent(Actor __instance, Actor pActor, bool pIncreaseChildren)
    {
        // todo: 当角色拥有父母时触发
    }

    public static void SetLover(Actor __instance, Actor pActor)
    {
        if (pActor==null) return;
        // todo: 当角色拥有伴侣时触发
    }
    public static void SetKingdom(Actor __instance, Kingdom pKingdomToSet)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        // todo: 当角色加入王国时触发
    }
    public static void SetArmy(Actor __instance, Army pObject)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        // todo: 当角色加入军队时触发
    }

    public static void RemoveFromArmy(Actor __instance)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        // todo: 当角色离开军队时触发
    }
    public static void RemoveData(Actor __instance)
    {
        __instance.RemoveExtraData<Actor, ActorExtraData>();
        // todo: 当角色数据被清除时触发
    }
}