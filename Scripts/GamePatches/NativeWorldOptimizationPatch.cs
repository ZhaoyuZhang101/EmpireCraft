using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class NativeWorldOptimizationPatch : GamePatch
{
    private const string HarmonyId = "EmpireCraft.NativeWorldOptimization";
    public ModDeclare declare { get; set; }
    public static bool Initialized { get; private set; }
    private sealed class MiniMapRefresh
    {
        internal object Pixels;
        internal float Last = float.NegativeInfinity;
    }
    private static ConditionalWeakTable<MapBox, MiniMapRefresh> Refreshes = new();
    private const float MiniMapInterval = 0.05f;
    public static long DeferredMiniMaps { get; private set; }
    public static long SkippedEdgeUploads { get; private set; }
    public static long ZoneChecks { get; private set; }
    public static long OriginalZoneChecks { get; private set; }
    public static long BuildingParallelFrames { get; private set; }
    private static readonly ConcurrentDictionary<int, byte> BuildingThreads = new();
    private static volatile bool _recordBuildingThreads;
    private static int _mainThread;
    public static int LastBuildingBackgroundThreads { get; private set; }
    private static bool Profiling => CityPopulationSystem.AbstractPopulationEnabled &&
        Config.game_loaded && !Config.paused && !SmoothLoader.isLoading();

    public void Initialize()
    {
        var harmony = new Harmony(HarmonyId);
        Prefix(harmony, typeof(MapBox), "redrawMiniMap", nameof(BeforeMiniMap));
        Prefix(harmony, typeof(WorldLayerEdges), "redraw", nameof(BeforeEdges));
        Prefix(harmony, typeof(WorldTilemap), "redrawTiles", nameof(BeforeTiles));
        Prefix(harmony, typeof(CityBehCheckFarms), "check", nameof(BeforeFarmCheck));
        Postfix(harmony, typeof(Building), "setBuilding", nameof(AfterBuilding));
        Postfix(harmony, typeof(Building), "setHaveResourcesToCollect", nameof(AfterBuilding));
        Postfix(harmony, typeof(BuildingFruitGrowth), "update", nameof(AfterFruit));
        Prefix(harmony, typeof(Building), "clearComponents", nameof(BeforeDispose));
        Prefix(harmony, typeof(Building), "Dispose", nameof(BeforeDispose));
        Prefix(harmony, typeof(BatchBuildings), "updateVisibility", nameof(RecordBuildingThread));
        harmony.Patch(AccessTools.Method(typeof(MapBox), "updateBuildings"),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeBuildings)) { priority = Priority.First },
            finalizer: new HarmonyMethod(GetType(), nameof(AfterBuildings)) { priority = Priority.Last });

        // 这些入口在主线程；不在原版 Parallel 作业中使用非线程安全的 FrameProfiler。
        Profile(harmony, typeof(BuildingManager), "calculateVisibleBuildings");
        Profile(harmony, typeof(MapBox), "redrawMiniMap");
        Profile(harmony, typeof(MapBox), "updateWorldBehaviours");
        Profile(harmony, typeof(MapBox), "updateMapLayers");
        Profile(harmony, typeof(WorldBehaviourActionBiomes), "updateSingleTiles");
        Profile(harmony, typeof(MapBox), "updateActors");
        Profile(harmony, typeof(MapBox), "updateCities");
        Profile(harmony, typeof(MapBox), "calculateVisibleObjects");
        Profile(harmony, typeof(MapBox), "renderStuff");
        Profile(harmony, typeof(QuantumSpriteManager), "updateSystems");
        Profile(harmony, typeof(MapBox), "updateDirtyMetaContainersAndCleanup");
        Profile(harmony, typeof(MapBox), "checkDirtyMetaObjects");
        Profile(harmony, typeof(MapBox), "checkDirtyUnits");
        Profile(harmony, typeof(MapBox), "checkMetaObjectsDestroy");
        Profile(harmony, typeof(MapBox), "checkEventHouses");
        Profile(harmony, typeof(BatchActors), "u4_deadCheck");
        Profile(harmony, typeof(BatchActors), "u10_checkSmoothMovement");
        Initialized = true;
    }

    private static void Prefix(Harmony harmony, Type type, string name, string patch) =>
        harmony.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), patch));
    private static void Postfix(Harmony harmony, Type type, string name, string patch) =>
        harmony.Patch(AccessTools.Method(type, name), postfix: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), patch));
    private static void Profile(Harmony harmony, Type type, string name) =>
        harmony.Patch(AccessTools.Method(type, name),
            prefix: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), nameof(BeforeProfile)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), nameof(AfterProfile)) { priority = Priority.Last });

    public static bool BeforeMiniMap(MapBox __instance, bool pForce)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pForce || !MapBox.isRenderMiniMap() ||
            World.world.isAnyPowerSelected() && Input.GetMouseButton(0) ||
            !DebugConfig.isOn(DebugOption.SystemRedrawMap)) return true;
        var refresh = Refreshes.GetOrCreateValue(__instance);
        object pixels = __instance.world_layer?.pixels;
        float now = Time.unscaledTime;
        if (!ReferenceEquals(refresh.Pixels, pixels) || now < refresh.Last)
        {
            refresh.Pixels = pixels;
            refresh.Last = float.NegativeInfinity;
        }
        if (now - refresh.Last < MiniMapInterval)
        {
            // 原版还没清 tiles_dirty；下一次刷新会读取最新地块，变化不会丢失。
            DeferredMiniMaps++;
            return false;
        }
        refresh.Last = now;
        return true;
    }

    public static bool BeforeEdges(WorldLayerEdges __instance, MethodBase __originalMethod)
    {
        // 如 Optime 使用自己的脏地块列表，不能用原版空集合判断它没有工作。
        if (!CityPopulationSystem.AbstractPopulationEnabled || HasOtherPrefixes(__originalMethod) || __instance._dirty_chunks.Count > 0 ||
            __instance._chunks_to_redraw.Count > 0) return true;
        SkippedEdgeUploads++;
        return false;
    }

    public static bool BeforeTiles(WorldTilemap __instance, bool pForceAll, MethodBase __originalMethod)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pForceAll || HasOtherPrefixes(__originalMethod)) return true;
        if (__instance._dirty_zones.Count == 0 || !MapBox.isRenderGameplay()) return false;
        using var timing = Profiling ? FrameProfiler.Measure("原版地块·重绘") : default;
        List<TileZone> visible = World.world.zone_camera.getVisibleZones();
        OriginalZoneChecks += visible.Count;
        // 检查较小集合。屏外脏区块留在原版队列中，移到镜头内后照常绘制。
        bool prepared = false;
        if (__instance._dirty_zones.Count < visible.Count)
        {
            foreach (TileZone zone in __instance._dirty_zones)
            {
                ZoneChecks++;
                if (!zone.visible) continue;
                if (!prepared) { __instance.prepareToDraw(); prepared = true; }
                __instance.checkZoneToRender(zone);
            }
        }
        else
        {
            foreach (TileZone zone in visible)
            {
                ZoneChecks++;
                if (!__instance._dirty_zones.Contains(zone)) continue;
                if (!prepared) { __instance.prepareToDraw(); prepared = true; }
                __instance.checkZoneToRender(zone);
            }
        }
        if (prepared)
        {
            __instance.redrawAllLayers();
            __instance.drawFinish();
        }
        return false;
    }

    public static void AfterBuilding(Building __instance) => NativeVegetationScheduler.Reconcile(__instance);
    public static void AfterFruit(BuildingFruitGrowth __instance) => NativeVegetationScheduler.Reconcile(__instance.building);
    public static void BeforeDispose(Building __instance) => NativeVegetationScheduler.Forget(__instance);

    public readonly struct BuildingScope
    {
        internal readonly bool Active, Parallel, Profiled;
        internal readonly FrameProfiler.Scope Timing;
        internal BuildingScope(bool parallel, bool profiled)
        {
            Active = true;
            Parallel = parallel;
            Profiled = profiled;
            Timing = profiled ? FrameProfiler.Measure("原版植物建筑·更新") : default;
        }
    }
    public static void BeforeBuildings(out BuildingScope __state)
    {
        __state = default;
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        __state = new BuildingScope(Config.parallel_jobs_updater, Profiling);
        BuildingThreads.Clear();
        _mainThread = Thread.CurrentThread.ManagedThreadId;
        _recordBuildingThreads = true;
        // 只启用原版建筑作业已有的 Parallel 分支；Post 阶段的生长、扩散、地图写回仍在主线程。
        Config.parallel_jobs_updater = true;
        BuildingParallelFrames++;
    }
    public static void AfterBuildings(BuildingScope __state)
    {
        if (!__state.Active) return;
        _recordBuildingThreads = false;
        int background = 0;
        foreach (int thread in BuildingThreads.Keys) if (thread != _mainThread) background++;
        LastBuildingBackgroundThreads = background;
        Config.parallel_jobs_updater = __state.Parallel;
        if (__state.Profiled) __state.Timing.Dispose();
    }
    public static void BeforeProfile(MethodBase __originalMethod, out FrameProfiler.Scope? __state) =>
        __state = Profiling
            ? FrameProfiler.Measure("原版·" + __originalMethod.Name) : null;
    public static void AfterProfile(FrameProfiler.Scope? __state) => __state?.Dispose();

    // 原版作业线程只写线程编号，不访问 Unity、世界法则或主线程计时字典。
    public static void RecordBuildingThread()
    {
        if (_recordBuildingThreads) BuildingThreads.TryAdd(Thread.CurrentThread.ManagedThreadId, 0);
    }

    private static bool HasOtherPrefixes(MethodBase original)
    {
        var prefixes = Harmony.GetPatchInfo(original)?.Prefixes;
        if (prefixes != null)
            foreach (Patch patch in prefixes) if (patch.owner != HarmonyId) return true;
        return false;
    }

    public static bool BeforeFarmCheck(City pCity, MethodBase __originalMethod)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || World.world == null || pCity?.data == null ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pCity) ||
            HasOtherPrefixes(__originalMethod)) return true;
        VirtualFarmScan.Refresh(pCity);
        return false;
    }

    public static void Reset()
    {
        Refreshes = new ConditionalWeakTable<MapBox, MiniMapRefresh>();
        DeferredMiniMaps = SkippedEdgeUploads = ZoneChecks = OriginalZoneChecks = 0;
        BuildingParallelFrames = 0;
        _recordBuildingThreads = false;
        BuildingThreads.Clear();
        LastBuildingBackgroundThreads = 0;
        NativeVegetationScheduler.Reset();
        VirtualFarmScan.Reset();
        OccupationReadIndex.Reset();
        PopulationRuntimeDiagnostics.Reset();
        FrameProfiler.Reset();
    }
}
