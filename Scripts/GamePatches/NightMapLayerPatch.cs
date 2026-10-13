using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 入夜时地图图层(国家等色块)不变透明：
//   · 原版黑暗纪元(夜、绝望、月之纪元)把色块图层的不透明度乘到 0.6(ZoneCalculator._night_multiplier)，
//     这里每帧开算前复位为 1，图层保持白天的不透明度；
//   · 夜色、月色、暗雨三层纪元暗化遮罩原本铺在所有图层之上(MapOverlay 排序层)，色块被压暗、发虚；
//     改到地形之上、色块之下(MapLayer 排序层序号 1：地形 WorldBox 是 0，色块图层是 2)，地形照样入夜，色块不受影响。
//     建筑、树木的夜色由原版夜间材质(LibraryMaterials.updateNight)着色，不靠这层遮罩；雨、雪、灰等天气遮罩不动。
public class NightMapLayerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private static readonly string[] DarknessOverlays = { "night", "moon", "rain_darkness" };
    private const int OverlayOrder = 1;
    private static WorldAgeEffects _sorted;

    public void Initialize()
    {
        var harmony = new Harmony(nameof(NightMapLayerPatch));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), nameof(ZoneCalculator.updateAnimationsAndSelections)),
            prefix: new HarmonyMethod(typeof(NightMapLayerPatch), nameof(BeforeZoneAnimations)));
        harmony.Patch(AccessTools.Method(typeof(WorldAgeEffects), nameof(WorldAgeEffects.update)),
            postfix: new HarmonyMethod(typeof(NightMapLayerPatch), nameof(AfterAgeEffects)));
    }

    private static void BeforeZoneAnimations(ZoneCalculator __instance) => __instance._night_multiplier = 1f;

    // 遮罩是场景里常驻的对象，排一次就够(换场景、重建了再排)
    private static void AfterAgeEffects(WorldAgeEffects __instance)
    {
        if (_sorted == __instance || __instance?.dict_effects == null) return;
        string layer = World.world?.GetComponent<SpriteRenderer>()?.sortingLayerName;
        if (string.IsNullOrEmpty(layer)) layer = "MapLayer";
        foreach (string id in DarknessOverlays)
        {
            if (!__instance.dict_effects.TryGetValue(id, out SpriteRenderer renderer) || renderer == null) continue;
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = OverlayOrder;
        }
        _sorted = __instance;
    }
}
