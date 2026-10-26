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
    // 小地图每秒最多刷新 5 次(大地图上每次重画小地图都不便宜)
    private const float MiniMapInterval = 0.2f;
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
    // 原版随地图大小涨的整图开销(小地图、地形边缘、地块重绘、各图层贴图上传，见 RenderThrottlePatch)：以前只在无小人模式下省，
    // 巨大及以上的地图(边长 384 格起)不开无小人也省(用户 2026-10-27 反馈什么都不放、地图够大也掉帧)；小图照原版
    private const int LargeMapTiles = 384 * 384;
    public static bool Throttled => CityPopulationSystem.AbstractPopulationEnabled || MapBox.width * MapBox.height >= LargeMapTiles;
    public static long PartialEdgeRedraws { get; private set; }

    public void Initialize()
    {
        var harmony = new Harmony(HarmonyId);
        Prefix(harmony, typeof(MapBox), "redrawMiniMap", nameof(BeforeMiniMap));
        Prefix(harmony, typeof(WorldLayerEdges), "redraw", nameof(BeforeEdges));
        Prefix(harmony, typeof(WorldTilemap), "redrawTiles", nameof(BeforeTiles));
        Prefix(harmony, typeof(QuantumSpriteLibrary), "drawLavaLightBlobs", nameof(BeforeLavaLights));
        Prefix(harmony, typeof(PixelFlashEffects), "UpdateDirty", nameof(BeforeFlashes));
        Prefix(harmony, typeof(UnitLayer), "UpdateDirty", nameof(BeforeUnitDots));
        Prefix(harmony, typeof(LavaLayer), "update", nameof(BeforeLavaLayer));
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
        // 原版·updateCities 偶有几百毫秒的尖峰：城市自身的几步与城市 AI 各行为(原版与模组的)分别计时，尖峰帧里直接点名
        harmony.Patch(AccessTools.Method(typeof(MapBox), "Update"),
            prefix: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), nameof(BeforeMainLoop)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), nameof(AfterProfile)) { priority = Priority.Last });
        Profile(harmony, typeof(City), "updateCityStatus");
        Profile(harmony, typeof(City), "updateCitizens");
        Profile(harmony, typeof(City), "updateCapture");
        foreach (Type type in AccessTools.AllTypes())
        {
            if (type == null || type.IsAbstract || !type.IsSubclassOf(typeof(BehaviourActionCity))) continue;
            MethodInfo execute = AccessTools.DeclaredMethod(type, "execute", new[] { typeof(City) });
            if (execute == null) continue;
            harmony.Patch(execute,
                prefix: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), nameof(BeforeCityBehaviour)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(NativeWorldOptimizationPatch), nameof(AfterProfile)) { priority = Priority.Last });
        }
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
        if (!Throttled || pForce || !MapBox.isRenderMiniMap() ||
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

    // 地形边缘(山脚、海岸的描边)：没变就不重画上传；变了，原版把每个变了的格所在的区块连同周围八块整块重算(一格变动算 2304 格)，
    // 大地图上草木蔓生、结霜化冻散在各处，一次几百块。一格的描边只看它自己和上下两格，这里只重算变了的格和它上下两格；
    // 变的格太多(读档、整图重画)照原版
    private const int EdgeTileLimit = 4096;

    public static bool BeforeEdges(WorldLayerEdges __instance, MethodBase __originalMethod)
    {
        // 如 Optime 使用自己的脏地块列表，不能用原版空集合判断它没有工作。
        if (!Throttled || HasOtherPrefixes(__originalMethod)) return true;
        if (__instance._dirty_chunks.Count == 0 && __instance._chunks_to_redraw.Count == 0)
        {
            SkippedEdgeUploads++;
            return false;
        }
        // 变了的格都在 tiles_dirty 里(原版只在 setTileDirty 里标脏区块，同时记下这一格；redrawMiniMap 先画边缘再清 tiles_dirty)
        HashSet<WorldTile> dirty = World.world.tiles_dirty;
        if (__instance._chunks_to_redraw.Count > 0 || dirty.Count == 0 || dirty.Count > EdgeTileLimit) return true;
        Color32[] pixels = __instance.pixels;
        foreach (WorldTile tile in dirty)
        {
            RedrawEdge(__instance, pixels, tile);
            RedrawEdge(__instance, pixels, tile.tile_up);
            RedrawEdge(__instance, pixels, tile.tile_down);
        }
        __instance._dirty_chunks.Clear();
        __instance.updatePixels();
        PartialEdgeRedraws++;
        return false;
    }

    private static void RedrawEdge(WorldLayerEdges layer, Color32[] pixels, WorldTile tile)
    {
        if (tile?.data == null) return;
        pixels[tile.data.tile_id] = Toolbox.clear;
        layer.redrawTile(tile);
    }

    // 岩浆的夜光斑点：原版每帧把镜头里每块地(拉远时全图五千多块)查一遍有没有岩浆(每块四次字典查找)。
    // 光斑只有开着夜景灯光才画，纯净地图另画灯光(见 PureMapPatch.BeforeDrawLights)不读它，世上没有岩浆也不必查
    public static bool BeforeLavaLights() =>
        !PureMapMode.Enabled && PlayerConfig.optionBoolEnabled("night_lights") && WorldBehaviourActionLava.hasLava();

    // 地块闪白(草木蔓生、结霜化冻、画地形时一格闪一下)：原版只要有一格在闪，每 0.01 秒就把与地图等大的贴图整张上传一次，
    // 大地图上蔓生几乎不停，等于每次图层更新都整张上传。改为至多每秒 20 次，按隔了多久一次退几档，闪的快慢不变
    private const float FlashInterval = 0.05f, FlashStep = 0.01f;
    private static float _flashAt = float.NegativeInfinity;
    private static readonly List<WorldTile> FlashDone = new();

    public static bool BeforeFlashes(PixelFlashEffects __instance)
    {
        if (!Throttled) return true;
        float now = Time.unscaledTime, since = now - _flashAt;
        if (since >= 0f && since < FlashInterval) return false;
        _flashAt = now;
        if (__instance.pixels_to_update.Count == 0) return false;
        int skip = Mathf.Clamp(Mathf.RoundToInt(since / FlashStep), 1, __instance.colors_amount) - 1;
        Color32[] pixels = __instance.pixels;
        FlashDone.Clear();
        foreach (WorldTile tile in __instance.pixels_to_update)
        {
            if (tile.flash_state < 0)
            {
                FlashDone.Add(tile);
                continue;
            }
            tile.flash_state = Math.Max(0, tile.flash_state - skip);
            pixels[tile.data.tile_id] = tile.color_array.colors[tile.flash_state];
            tile.flash_state--;
        }
        foreach (WorldTile tile in FlashDone) __instance.pixels_to_update.Remove(tile);
        FlashDone.Clear();
        __instance.updatePixels();
        return false;
    }

    // 拉远时的单位小点：原版每 0.1 秒(游戏时间)把全体单位过一遍、整张贴图上传，一个单位都没有、也没有旧点要擦时照样上传。
    // 改为没东西可画就不传，有单位时至多每秒 5 次(同小地图)
    private const float UnitDotInterval = 0.2f;
    private static float _unitDotsAt = float.NegativeInfinity;

    public static bool BeforeUnitDots(UnitLayer __instance)
    {
        if (!Throttled || MapBox.isRenderGameplay() || __instance.timer > 0f) return true;
        if (__instance.prevTiles.Count == 0 && World.world.units.Count == 0)
        {
            __instance.timer = __instance.interval;
            return false;
        }
        float now = Time.unscaledTime;
        if (now >= _unitDotsAt && now - _unitDotsAt < UnitDotInterval) return false;
        _unitDotsAt = now;
        return true;
    }

    // 岩浆图层：世上只要有一格岩浆，原版每次图层更新都把与地图等大的贴图整张擦了重画再上传，镜头拉近、这层根本不显示时也一样。
    // 改为不显示时不画(再显示的头一回当场擦旧画新)，显示时至多每秒 10 次
    private const float LavaInterval = 0.1f;
    private static float _lavaAt = float.NegativeInfinity;

    public static bool BeforeLavaLayer(LavaLayer __instance)
    {
        if (!Throttled) return true;
        bool shown = __instance.sprRnd.enabled;
        __instance.checkAutoDisable();
        if (!__instance.sprRnd.enabled) return false;
        float now = Time.unscaledTime;
        if (shown && now >= _lavaAt && now - _lavaAt < LavaInterval) return false;
        _lavaAt = now;
        __instance.updateLava();
        return false;
    }

    public static bool BeforeTiles(WorldTilemap __instance, bool pForceAll, MethodBase __originalMethod)
    {
        if (!Throttled || pForceAll || HasOtherPrefixes(__originalMethod)) return true;
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
        // 不再强开原版的并行建筑作业(原版调试选项，默认关)：强开后挂在建筑更新上的补丁(本模组的植物休眠、
        // WarBox 等别的模组的)都跑在工作线程上，多线程同时改同一个集合会损坏内存，玩一段时间后原生崩溃
        // (worldbox.exe 在 ntdll.dll 访问冲突 0xc0000005)。按原版设置走
        if (Config.parallel_jobs_updater) BuildingParallelFrames++;
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

    // 整个原版主循环(含上面各分项)：帧耗时减去它就是主循环外(渲染提交、别的模组、GC)
    public static void BeforeMainLoop(out FrameProfiler.Scope? __state) =>
        __state = Profiling ? FrameProfiler.Measure(FrameProfiler.MainLoopLabel) : null;

    // 城市 AI 行为都叫 execute：按类名记(标签缓存，免得每次拼字符串)
    private static readonly Dictionary<MethodBase, string> BehaviourLabels = new();
    public static void BeforeCityBehaviour(MethodBase __originalMethod, out FrameProfiler.Scope? __state)
    {
        if (!Profiling)
        {
            __state = null;
            return;
        }
        if (!BehaviourLabels.TryGetValue(__originalMethod, out string label))
            BehaviourLabels[__originalMethod] = label = "城市AI·" + __originalMethod.DeclaringType?.Name;
        __state = FrameProfiler.Measure(label);
    }

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
        DeferredMiniMaps = SkippedEdgeUploads = ZoneChecks = OriginalZoneChecks = PartialEdgeRedraws = 0;
        _flashAt = _unitDotsAt = _lavaAt = float.NegativeInfinity;
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
