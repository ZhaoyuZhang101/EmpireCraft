using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EmpireCraft.Scripts.Terminal;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 轻量主机模式(见 LightweightHostMode)停本机画面时跳过的原版入口：整套渲染、看得见的东西的清点、灯光、铭牌、
// 地块动画与选中、鼠标键盘的操控。世界的模拟一样不拦
public class LightweightHostPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        if (Application.isMobilePlatform) return;
        var harmony = new Harmony("EmpireCraft.LightweightHost");
        var prefix = new HarmonyMethod(typeof(LightweightHostPatch), nameof(BeforeLocalPresentation)) { priority = Priority.First };
        foreach ((Type type, string method) in new[]
                 {
                     (typeof(MapBox), "renderStuff"), (typeof(MapBox), "calculateVisibleObjects"),
                     (typeof(LightRenderer), "update"), (typeof(NameplateManager), "update"),
                     (typeof(ZoneCalculator), "updateAnimationsAndSelections"), (typeof(PlayerControl), "updateControls")
                 })
            harmony.Patch(AccessTools.DeclaredMethod(type, method), prefix: prefix);

        harmony.Patch(AccessTools.DeclaredMethod(typeof(MapBox), "updateMapLayers"),
            transpiler: new HarmonyMethod(typeof(LightweightHostPatch), nameof(GateLayerDraw)));
        // 任一核心入口在新版游戏中挂不上时，保留正常显示。
        LightweightHostMode.HooksReady = true;
    }

    public static bool BeforeLocalPresentation() => !LightweightHostMode.SkipDrawing;

    // updateMapLayers 也跑热量、火和区块模拟；只替换画图的虚调用，保留所有 update。
    public static IEnumerable<CodeInstruction> GateLayerDraw(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo draw = AccessTools.DeclaredMethod(typeof(MapLayer), nameof(MapLayer.draw));
        MethodInfo guarded = AccessTools.DeclaredMethod(typeof(LightweightHostPatch), nameof(DrawLayer));
        bool found = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(draw))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = guarded;
                found = true;
            }
            yield return instruction;
        }
        if (!found) throw new InvalidOperationException("MapBox.updateMapLayers no longer calls MapLayer.draw.");
    }

    public static void DrawLayer(MapLayer layer, float elapsed)
    {
        if (!LightweightHostMode.SkipDrawing) layer.draw(elapsed);
    }
}
