using System;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 原版区域寻路的防护：区域图的连接偶尔会不对称(某区域被邻居列为相邻，自己的邻居表里却没有对方，多是地形
// 变化后区块重算到一半留下的)，寻路回溯时就空引用；之后凡是经过那里的寻路每次都抛异常、刷满日志，单位原地不动。
// 拦下这个异常：本次寻路按"不通"返回并清掉寻路器的中间状态；同时把全图区块标脏，让原版下一次区块更新
// 重算全部区域与连接(至多两分钟一次，重算一次约一两百毫秒)
public class RegionPathGuardPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    private const float RepairSeconds = 120f;
    private static float _nextRepair;
    private static int _swallowed;

    public void Initialize()
    {
        new Harmony(nameof(RegionPathGuardPatch)).Patch(
            AccessTools.Method(typeof(RegionPathFinder), nameof(RegionPathFinder.getGlobalPath)),
            finalizer: new HarmonyMethod(typeof(RegionPathGuardPatch), nameof(AfterGlobalPath)));
    }

    private static Exception AfterGlobalPath(Exception __exception, RegionPathFinder __instance,
        ref PathFinderResult __result)
    {
        if (__exception is not NullReferenceException) return __exception;
        try
        {
            __instance.clearRegions();
        }
        catch
        {
            // 状态本身已坏，清不掉也只影响这一次
        }
        __instance.last_globalPath = null;
        __result = PathFinderResult.NotFound;
        _swallowed++;
        float now = Time.unscaledTime;
        if (now < _nextRepair || World.world?.map_chunk_manager == null) return null;
        _nextRepair = now + RepairSeconds;
        World.world.map_chunk_manager.allDirty();
        LogService.LogWarning($"[EmpireCraft][寻路] 原版区域图连接错乱，拦下寻路异常 {_swallowed} 次，已让原版重算全图区域与连接");
        _swallowed = 0;
        return null;
    }
}
