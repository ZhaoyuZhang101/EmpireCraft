using System;
using EmpireCraft.Scripts.Terminal;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 终端模式领着君主时世界走得慢(用户 2026-11-11 "终端模式下地图的事件流速会变慢""那么模拟的信息也会丰富些")：
// 原版每帧的世界时长 = 固定帧长 × 速度档倍数(MapBox.calculateCurElapsed)，这里再乘终端流速(设置里的百分比，默认一成)。
// 速度档照旧能调，是在档位之上再放慢；没领君主、没开终端模式时不动
public class TerminalPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        try
        {
            new Harmony(nameof(TerminalPatch)).Patch(AccessTools.Method(typeof(MapBox), nameof(MapBox.calculateCurElapsed)),
                postfix: new HarmonyMethod(typeof(TerminalPatch), nameof(AfterElapsed)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 流速没接上: {exception.Message}");
        }
    }

    public static void AfterElapsed(ref float __result)
    {
        float factor = TerminalHost.SpeedFactor;
        if (factor < 1f) __result *= factor;
    }
}
