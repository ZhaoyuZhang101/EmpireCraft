using System;
using System.Collections.Generic;
using System.Reflection;
using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 大地图帧数(无小人模式，或巨大及以上的地图，见 NativeWorldOptimizationPatch.Throttled)：
//   · 野外建筑清单：原版只要有一棵树长出或死掉，就把全图所有建筑(30×30 的图有三十万)整份重扫一遍，
//     大地图上几乎每帧两次。野外清单只有金脑之类极少数行为用到，改为最多每 2 秒重建一次；
//   · 地图图层(热量、区块、边界与各图层绘制)最多每秒 20 次，跳过的时间累计补上(火、热量照常推进)；
//     手持神力在地图上按住时照常每帧刷新；
//   · 小地图每秒最多 5 次(见 NativeWorldOptimizationPatch.BeforeMiniMap)；
//   · 建筑(主要是树木植物)更新：建筑超过十万的大地图上最多每秒 30 次，跳过的时间累计补上(生长、蔓延速度不变)；
//   · 细分计时：每种量子精灵(单位、建筑、影子、血条、各种图标)与元数据容器检查的各步各记一项，供[每帧耗时]定位。
public class RenderThrottlePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private const float WildBuildingsInterval = 2f;
    private const float MapLayerInterval = 1f / 20f;
    private static float _wildAt = float.NegativeInfinity;
    private static float _layersAt = float.NegativeInfinity;
    private static float _layersElapsed;
    private const float BuildingInterval = 1f / 30f;
    private const int BigMapBuildings = 100000;
    private static float _buildingsAt = float.NegativeInfinity;
    private static float _buildingsElapsed;
    private static bool _wrapped;

    private static bool Active => NativeWorldOptimizationPatch.Throttled && Config.game_loaded && !SmoothLoader.isLoading();

    public void Initialize()
    {
        var harmony = new Harmony("EmpireCraft.RenderThrottle");
        Try(() => harmony.Patch(AccessTools.Method(typeof(WildKingdomsManager), "beginChecksBuildings"),
            prefix: new HarmonyMethod(typeof(RenderThrottlePatch), nameof(BeforeWildBuildings))));
        Try(() => harmony.Patch(AccessTools.Method(typeof(MapBox), "updateMapLayers"),
            prefix: new HarmonyMethod(typeof(RenderThrottlePatch), nameof(BeforeMapLayers))));
        Try(() => harmony.Patch(AccessTools.Method(typeof(MapBox), "updateBuildings"),
            prefix: new HarmonyMethod(typeof(RenderThrottlePatch), nameof(BeforeBuildings)) { priority = Priority.First }));
        // 只给非泛型类自己声明的方法计时：泛型基类(SimSystemManager<,>)的方法拿不到原方法句柄，清空世界时会报错
        foreach ((Type type, string name) in new[]
                 {
                     (typeof(CityManager), "beginChecksBuildings"), (typeof(KingdomManager), "beginChecksBuildings"),
                     (typeof(WildKingdomsManager), "beginChecksBuildings"), (typeof(BuildingZonesSystem), "update"),
                     (typeof(SimObjectsZones), "update"), (typeof(ActorManager), "prepareForMetaChecks"),
                     (typeof(MapBox), "checkObjectsToDestroy"),
                     (typeof(MapBox), "checkEventUnitsDestroy"), (typeof(MapBox), "checkEventBuildingsDestroy")
                 })
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name);
            if (method == null || method.DeclaringType == null || method.DeclaringType.IsGenericType) continue;
            Try(() => harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(RenderThrottlePatch), nameof(BeforeStep)),
                postfix: new HarmonyMethod(typeof(RenderThrottlePatch), nameof(AfterStep))));
        }
        Try(() => harmony.Patch(AccessTools.Method(typeof(QuantumSpriteManager), "updateSystems"),
            prefix: new HarmonyMethod(typeof(RenderThrottlePatch), nameof(WrapQuantumSprites))));
    }

    private static void Try(Action patch)
    {
        try { patch(); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft] 帧数优化补丁未生效: {exception.Message}"); }
    }

    public static bool BeforeWildBuildings()
    {
        if (!Active) return true;
        float now = Time.unscaledTime;
        if (now >= _wildAt && now - _wildAt < WildBuildingsInterval) return false;
        _wildAt = now;
        return true;
    }

    public static bool BeforeMapLayers(ref float pElapsed)
    {
        if (!Active) return true;
        _layersElapsed += pElapsed;
        float now = Time.unscaledTime;
        bool painting = World.world.isAnyPowerSelected() && Input.GetMouseButton(0);
        if (!painting && now >= _layersAt && now - _layersAt < MapLayerInterval) return false;
        _layersAt = now;
        pElapsed = _layersElapsed;
        _layersElapsed = 0f;
        return true;
    }

    public static bool BeforeBuildings(ref float pElapsed)
    {
        if (!Active || World.world.buildings.Count < BigMapBuildings) return true;
        _buildingsElapsed += pElapsed;
        float now = Time.unscaledTime;
        if (now >= _buildingsAt && now - _buildingsAt < BuildingInterval) return false;
        _buildingsAt = now;
        pElapsed = _buildingsElapsed;
        _buildingsElapsed = 0f;
        return true;
    }

    // 细分计时仍只在无小人模式下记
    private static bool Profiling => CityPopulationSystem.AbstractPopulationEnabled && Active && !Config.paused;

    public static void BeforeStep(MethodBase __originalMethod, out FrameProfiler.Scope? __state) =>
        __state = Profiling ? FrameProfiler.Measure($"原版·{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}") : null;

    public static void AfterStep(FrameProfiler.Scope? __state) => __state?.Dispose();

    // 第一次绘制前给每种量子精灵的绘制回调套上计时
    public static void WrapQuantumSprites()
    {
        if (_wrapped) return;
        _wrapped = true;
        foreach (QuantumSpriteAsset asset in AssetManager.quantum_sprites.list)
        {
            var original = asset.draw_call;
            if (original == null) continue;
            string label = "精灵·" + asset.id;
            asset.draw_call = pAsset =>
            {
                if (!Profiling)
                {
                    original(pAsset);
                    return;
                }
                using var timing = FrameProfiler.Measure(label);
                original(pAsset);
            };
        }
    }
}
