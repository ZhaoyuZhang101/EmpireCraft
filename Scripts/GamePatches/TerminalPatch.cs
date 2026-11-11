using System;
using EmpireCraft.Scripts.Terminal;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 终端模式领着君主、手机还连着时世界走得慢(手机都断线了照常速走，见 TerminalHost.SpeedFactor；用户 2026-11-11 "终端模式下地图的事件流速会变慢""那么模拟的信息也会丰富些")：
// 原版每帧的世界时长 = 固定帧长 × 速度档倍数(MapBox.calculateCurElapsed)，这里再乘终端流速(设置里的百分比，默认一成)。
// 速度档照旧能调，是在档位之上再放慢；没领君主、手机断了线、没开终端模式时不动
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
        // 第二期：廷议已毕的奏议等手机批红、要人失职的案子等手机裁决(见 TerminalHost.Decrees.cs)；没有席位领着这位君主时原样放行
        try
        {
            var harmony = new Harmony(nameof(TerminalPatch) + "Decrees");
            harmony.Patch(AccessTools.Method(typeof(EmpireCraft.Scripts.GeneralSystems.CourtSystem), "Decide"),
                prefix: new HarmonyMethod(typeof(TerminalPatch), nameof(BeforeDecide)));
            harmony.Patch(AccessTools.Method(typeof(EmpireCraft.Scripts.GeneralSystems.EmpireLaw.EmpireLawSystem),
                    nameof(EmpireCraft.Scripts.GeneralSystems.EmpireLaw.EmpireLawSystem.Prosecute)),
                prefix: new HarmonyMethod(typeof(TerminalPatch), nameof(BeforeProsecute)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 待批没接上: {exception.Message}");
        }
    }

    // 神力裁决(godDecision 不为空，手机批红也走这条)照办；每月自动批红时，终端等着的先不批
    public static bool BeforeDecide(EmpireCraft.Scripts.Layer.Empire empire, string godDecision) =>
        godDecision != null || !TerminalHost.HoldsMemorial(empire);

    public static bool BeforeProsecute(Actor actor, EmpireCraft.Scripts.GeneralSystems.EmpireLaw.LawType type, Kingdom kingdom, string reasonKey,
        Actor accuser, ref EmpireCraft.Scripts.GeneralSystems.EmpireLaw.LawEnforcementContext __result)
    {
        if (!TerminalHost.HoldsCase(actor, type, kingdom, reasonKey, accuser)) return true;
        __result = null;
        return false;
    }

    public static void AfterElapsed(ref float __result)
    {
        float factor = TerminalHost.SpeedFactor;
        if (factor < 1f) __result *= factor;
    }
}
