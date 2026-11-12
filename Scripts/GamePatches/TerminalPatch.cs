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
        // 终端模式开着时山川水陆不许改(见 TerminalHost.TerrainLocked)
        try
        {
            new Harmony(nameof(TerminalPatch) + "Terrain").Patch(AccessTools.Method(typeof(MapAction), nameof(MapAction.terraformTile)),
                prefix: new HarmonyMethod(typeof(TerminalPatch), nameof(BeforeTerraform)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 地形锁没接上: {exception.Message}");
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

    private static float _nextTerrainTip;

    // 改的是底下那一层(山、丘、土、沙、水——挖山填海、炸出坑、陨石、酸雨蚀地)：不许。只动表层的(草木荣枯、开田、修路、烧荒)照常，
    // 城要盖房种地。读档、生成世界不走这里。拿着神力画不上去时在屏幕上说一声，免得以为是坏了
    public static bool BeforeTerraform(WorldTile pTile, TileType pNewTypeMain, TerraformOptions pOptions)
    {
        if (!TerminalHost.TerrainLocked || pTile == null) return true;
        bool deeper = pNewTypeMain != null && pNewTypeMain != pTile.main_type || pOptions != null && pOptions.remove_water && pTile.Type != null && pTile.Type.ocean;
        if (!deeper) return true;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now >= _nextTerrainTip && World.world.isAnyPowerSelected())
        {
            _nextTerrainTip = now + 6f;
            WorldTip.showNow(NeoModLoader.General.LM.Get("terminal_terrain_locked"), false, "top", 3f);
        }
        return false;
    }

    public static void AfterElapsed(ref float __result)
    {
        float factor = TerminalHost.SpeedFactor;
        if (factor < 1f) __result *= factor;
    }
}
