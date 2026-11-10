using System;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 随机地图生成高原(见 PlateauGenerator)：原版 MapGenerator.prepare 排好加载队列之后，把"生成高原"插到"Map Chunk Manager (1/2)"前面——
// 那时格子已按高度定好，岛屿、区块、生物群系、植被都还没算，高原与外缘山脊接着照常长草木、冻山顶。找不到那一步就排到最后，补算一遍区块
public class PlateauPatch : GamePatch
{
    private const string Anchor = "Map Chunk Manager (1/2)", StepId = "EmpireCraft: Plateau";

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(PlateauPatch)).Patch(
            AccessTools.Method(typeof(MapGenerator), nameof(MapGenerator.prepare)),
            postfix: new HarmonyMethod(typeof(PlateauPatch), nameof(AfterPrepare)));
    }

    public static void AfterPrepare()
    {
        if (!PlateauGenerator.Enabled) return;
        int at = SmoothLoader._actions.FindIndex(action => action?.id == Anchor);
        if (at >= 0)
        {
            SmoothLoader._actions.Insert(at, new MapLoaderContainer(Run, StepId));
            return;
        }
        SmoothLoader.add(delegate
        {
            Run();
            World.world.map_chunk_manager.allDirty();
            World.world.map_chunk_manager.update(0f, pForce: true);
        }, StepId);
    }

    private static void Run()
    {
        try
        {
            PlateauGenerator.Generate();
        }
        catch (Exception exception)
        {
            LogService.LogError("[EmpireCraft][高原] 随机图生成高原失败: " + exception);
        }
    }
}
