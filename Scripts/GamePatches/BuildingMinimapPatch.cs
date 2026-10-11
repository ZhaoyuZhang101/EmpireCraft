using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

/// <summary>
/// 原版 Building.getColorForMinimap 在某个建筑的贴图集里缺少 "mini_" 前缀的
/// 小地图图标贴图时（asset.building_sprites.map_icon 为 null，通常出自没有
/// 提供对应 mini 贴图的其它模组自定义建筑，而不是 EmpireCraft 自己——本模组
/// 没有注册任何 BuildingAsset），会直接空引用崩溃。这个异常发生在
/// MapBox.Update -> renderStuff -> redrawMiniMap -> updateDirtyTile 的每帧
/// 调用链里，一旦触发就会每帧反复抛出，疯狂刷日志并把帧率拖到个位数。
/// 这里加一个安全兜底：缺图标时直接跳过原方法，返回透明色，不再崩溃刷屏。
/// </summary>
public class BuildingMinimapPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(BuildingMinimapPatch)).Patch(
            AccessTools.Method(typeof(Building), "getColorForMinimap"),
            prefix: new HarmonyMethod(GetType(), nameof(GetColorForMinimapPrefix))
        );
    }

    public static bool GetColorForMinimapPrefix(Building __instance, ref Color32 __result)
    {
        if (__instance?.asset?.building_sprites?.map_icon == null)
        {
            __result = default;
            return false;
        }
        return true;
    }
}
