using System;
using System.Collections.Generic;
using ai.behaviours;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 无小人城市失去城主且旧存档的创建物种已失效时，原版建造 AI 会每次抛空引用。
public class CityBuildSafetyPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CityBuildSafetyPatch));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getActorAsset)),
            postfix: new HarmonyMethod(GetType(), nameof(AfterGetActorAsset)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.calcPossibleBuildings)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeCalcPossibleBuildings)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.buildTick)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeBuildTick)));
        // 兜底：建造 AI 里任何空引用都只让这座城本次跳过建造，不再每帧抛错；并记下城市状态便于定位根因
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.execute)),
            finalizer: new HarmonyMethod(GetType(), nameof(ExecuteFinalizer)));
    }

    private static readonly Dictionary<long, float> LastReported = new();

    public static Exception ExecuteFinalizer(Exception __exception, City pCity, ref BehResult __result)
    {
        if (__exception is not NullReferenceException) return __exception;
        __result = BehResult.Continue;
        long id = pCity?.data?.id ?? -1L;
        float now = Time.realtimeSinceStartup;
        // 同一座城一分钟内只记一次
        if (LastReported.TryGetValue(id, out float last) && now - last < 60f) return null;
        LastReported[id] = now;
        string Describe()
        {
            try
            {
                ActorAsset asset = pCity?.getActorAsset();
                string template = asset?.build_order_template_id ?? "null";
                bool templateOk = !string.IsNullOrEmpty(asset?.build_order_template_id) &&
                                  AssetManager.city_build_orders?.get(asset.build_order_template_id)?.list != null;
                return $"城市={pCity?.data?.name ?? "null"}(id={id}) 已销毁={pCity?.isRekt()} " +
                       $"王国={pCity?.kingdom?.data?.name ?? "null"} 城主={(pCity?.leader == null ? "无" : pCity.leader.getName())} " +
                       $"创建物种={pCity?.data?.original_actor_asset ?? "null"} 实际物种={asset?.id ?? "null"} " +
                       $"建造模板={template}({(templateOk ? "有效" : "无效")}) 文化={(pCity?.getCulture() == null ? "无" : pCity.getCulture().name)} " +
                       $"建筑数={pCity?.buildings?.Count ?? -1} 单位数={pCity?.units?.Count ?? -1} " +
                       $"无小人模式={CityPopulationSystem.AbstractPopulationEnabled}";
            }
            catch (Exception describeError)
            {
                return $"城市 id={id}(状态读取失败: {describeError.Message})";
            }
        }
        LogService.LogWarning($"[EmpireCraft][建造AI空引用] 已跳过本次建造。{Describe()}\n{__exception.StackTrace}");
        return null;
    }

    private static bool HasBuildTemplate(ActorAsset asset) =>
        asset != null && !string.IsNullOrEmpty(asset.build_order_template_id) &&
        AssetManager.city_build_orders?.get(asset.build_order_template_id)?.list != null;

    public static void AfterGetActorAsset(City __instance, ref ActorAsset __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || HasBuildTemplate(__result) ||
            __instance?.data == null || AncientWarfareCompatibility.OwnsObject(__instance)) return;
        ActorAsset best = null;
        float largest = -1f;
        var population = CityPopulationSystem.Get(__instance);
        if (population?.groups != null)
            foreach (var group in population.groups)
            {
                if (group == null || group.size <= largest || string.IsNullOrEmpty(group.species)) continue;
                ActorAsset candidate = AssetManager.actor_library?.get(group.species);
                if (!HasBuildTemplate(candidate)) continue;
                best = candidate;
                largest = group.size;
            }
        if (best == null)
        {
            ActorAsset ruler = __instance.kingdom?.king?.getActorAsset();
            if (HasBuildTemplate(ruler)) best = ruler;
        }
        if (best == null) return;
        __result = best;
        __instance.data.original_actor_asset = best.id;
    }

    public static bool BeforeCalcPossibleBuildings(City pCity) =>
        !CityPopulationSystem.AbstractPopulationEnabled ||
        pCity != null && AncientWarfareCompatibility.OwnsObject(pCity) ||
        pCity?.data != null && !pCity.isRekt() && HasBuildTemplate(pCity.getActorAsset());

    public static bool BeforeBuildTick(City pCity, ref bool __result)
    {
        if (BeforeCalcPossibleBuildings(pCity)) return true;
        __result = false;
        return false;
    }
}
