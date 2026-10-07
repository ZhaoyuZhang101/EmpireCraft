using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Diagnostics;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class PopulationRuntimeDiagnostics
{
    private static object _world;
    private static float _at;
    private static bool _active;
    public static void Reset() { _world = null; _active = false; _at = 0f; }
    public static void Tick()
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) { _active = false; return; }
        if (World.world?.cities == null || !Config.game_loaded || Config.paused || SmoothLoader.isLoading()) return;
        bool started = !_active || !ReferenceEquals(_world, World.world) || Time.unscaledTime < _at;
        if (started)
        {
            _active = true;
            _world = World.world;
            FrameProfiler.Reset();
            string header = "[EmpireCraft][性能采样启动] 版本 2026-10-07-profile-3；虚拟人口已开启；原版计时补丁 " +
                EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.Initialized +
                "；u4_deadCheck 前缀=" + NativeBenchmarkSnapshot.ActorPrefixOwners() +
                "；记录均值与单次峰值，嵌套标签不要相加；时间戳为 UTC";
            PerformanceTraceFile.Record(header);
            LogService.LogInfo(header);
        }
        else if (Time.unscaledTime - _at < 30f) return;
        _at = Time.unscaledTime;
        int groups = 0;
        double background = 0d;
        // 只看各城已经保存的数值，不遍历单位和族谱，不重新计算经济、阶层或住房。
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            var population = CityPopulationSystem.Get(city);
            groups += population.groups.Count;
            foreach (var group in population.groups) if (group != null) background += group.Background;
        }
        string report = $"[EmpireCraft][虚拟人口负载] 实体单位 {World.world.units.Count}；" +
            $"城市 {World.world.cities.Count}；人口组 {groups}；城内背景人口 {background:0}；" +
            $"建筑 {World.world.buildings.Count}；后台待办 {PopulationMathWorkers.PendingCount}/64；" +
            $"可见植物建筑 {World.world.buildings.countVisibleBuildings()}；地块 {World.world.tiles_list.Length}；" +
            $"满果植物休眠 {NativeVegetationScheduler.SleepingCount}；地块检查/原版扫描 " +
            $"{EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.ZoneChecks}/" +
            $"{EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.OriginalZoneChecks}；" +
            $"合并地图刷新 {EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.DeferredMiniMaps}；" +
            $"省略空边缘上传 {EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.SkippedEdgeUploads}；" +
            $"原版建筑并行帧 {EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.BuildingParallelFrames}；" +
            $"最近建筑批次实际后台线程 {EmpireCraft.Scripts.GamePatches.NativeWorldOptimizationPatch.LastBuildingBackgroundThreads}；" +
            $"线程 {PopulationMathWorkers.WorkerCount}；举人排名后台命中/即时计算 " +
            $"{PopulationJurenRanking.WorkerResults}/{PopulationJurenRanking.FallbackResults}；理念后台命中/即时计算 " +
            $"{IdeologyPopulationSystem.ContactWorkerResults}/{IdeologyPopulationSystem.ContactFallbackResults}；经济后台命中/即时计算 " +
            $"{PopulationParallelSystem.EconomyWorkerResults}/{PopulationParallelSystem.EconomyFallbackResults}；" +
            $"规划续跑步数 {ZonePlanSystem.PlanningSteps}；占领视图命中/重建 {OccupationReadIndex.Hits}/{OccupationReadIndex.Builds}；" +
            $"战争统计命中/重算 {OccupationReadIndex.ControlHits}/{OccupationReadIndex.ControlBuilds}；" +
            $"官员名单命中/重建 {OfficeCandidateRoster.Hits}/{OfficeCandidateRoster.Builds}；" +
            $"军团统计命中/重建 {LegionStatisticsReadCache.Hits}/{LegionStatisticsReadCache.Builds}；" +
            $"风车农田扫描/地块读取 {VirtualFarmScan.Scans}/{VirtualFarmScan.TileChecks}。结合[帧率]与[每帧耗时]判断瓶颈";
        LogService.LogInfo(report);
        PerformanceTraceFile.Record(report);
        NativeBenchmarkSnapshot.Record();
    }
}
