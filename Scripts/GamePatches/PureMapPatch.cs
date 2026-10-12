using ai;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 纯净地图模式(见 PureMapMode)：
//   · 国家色块完全不透明：原版近看时国家内部画成透明、只留边框，内部颜色也是半透明的；
//     这里任何缩放都填满，颜色不透明(透明的清除色不动，否则空白处会变成白块)；
//     没选地图图层时默认显示国家图层、关了"地图图层"也照样显示；图层不随地图不透明度设置变淡(夜色、云照旧)；
//   · 国家图层下，帝国对外的边(对面不是本帝国的国家或无主地)画成金色，帝国内部各成员之间照旧；
//   · 人物要去哪里直接到位、不走路(地图上也不画人物)：原版的行为照常完成(回家、上任、相会生子)，不再寻路；
//     鼠标附近只认得船，悬停与点击都落到城市、国家上；除船以外的人物逐帧设为不可见；
//   · 纯装饰建筑(雕像、训练木桩、花园、喷泉)建造上限为 0，城市不再建
public class PureMapPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(PureMapPatch));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "shouldBeClearColor"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeShouldBeClearColor)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeApplyColors)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZoneFull"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeApplyColorsFull)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "colorZone"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeColorZone)));
        harmony.Patch(AccessTools.Method(typeof(ActorMove), nameof(ActorMove.goTo)),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeGoTo)));
        harmony.Patch(AccessTools.Method(typeof(MapBox), nameof(MapBox.getActorNearCursor)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterActorNearCursor)));
        harmony.Patch(AccessTools.Method(typeof(BatchActors), "updateVisibility"),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterUpdateVisibility)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getLimitOfBuildingsType)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterBuildingLimit)));
        harmony.Patch(AccessTools.Method(typeof(Zones), nameof(Zones.getMapMetaAsset)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterMapMetaAsset)));
        harmony.Patch(AccessTools.Method(typeof(Zones), nameof(Zones.showMapBorders)),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeShowMapBorders)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), nameof(ZoneCalculator.updateAnimationsAndSelections)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterZoneAnimations)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterApplyColors)));
    }

    private static MetaTypeAsset _kingdomLayer;

    // 没选地图图层时默认显示国家图层
    private static void AfterMapMetaAsset(ref MetaTypeAsset __result)
    {
        if (!PureMapMode.Enabled || __result != null) return;
        _kingdomLayer ??= AssetManager.meta_type_library.list.Find(asset =>
            asset.map_mode == MetaType.Kingdom && !string.IsNullOrEmpty(asset.option_id));
        __result = _kingdomLayer;
    }

    private static bool BeforeShowMapBorders(ref bool __result)
    {
        if (!PureMapMode.Enabled) return true;
        __result = true;
        return false;
    }

    // 图层不随地图不透明度设置变淡(亮度、夜色照原版)
    private static void AfterZoneAnimations(ZoneCalculator __instance)
    {
        if (!PureMapMode.Enabled || __instance.sprRnd == null) return;
        Color color = __instance.sprRnd.color;
        color.a = 1f;
        __instance.sprRnd.color = color;
    }

    private static readonly Color32 EmpireGold = new(232, 184, 64, 255);

    // 帝国对外的边画成金色：原版画完这块地后，把对面不是本帝国的那几条边重新涂一遍
    private static void AfterApplyColors(ZoneCalculator __instance, TileZone pZone, bool pUp, bool pDown, bool pLeft, bool pRight)
    {
        if (!PureMapMode.Enabled || pZone?.tiles == null || __instance.pixels == null) return;
        Kingdom kingdom = pZone.city?.kingdom;
        if (kingdom == null || World.world.getCachedMapMetaAsset()?.map_mode != MetaType.Kingdom) return;
        Empire empire = kingdom.GetEmpire();
        if (empire == null) return;
        bool up = pUp && Outside(pZone.zone_up, empire), down = pDown && Outside(pZone.zone_down, empire);
        bool left = pLeft && Outside(pZone.zone_left, empire), right = pRight && Outside(pZone.zone_right, empire);
        if (!up && !down && !left && !right) return;
        Color32[] pixels = __instance.pixels;
        foreach (WorldTile tile in pZone.tiles)
        {
            WorldTileZoneBorder border = tile?.world_tile_zone_border;
            if (border == null || !border.border) continue;
            if (up && border.border_up || down && border.border_down || left && border.border_left || right && border.border_right)
                pixels[tile.data.tile_id] = EmpireGold;
        }
    }

    private static bool Outside(TileZone zone, Empire empire) =>
        zone?.city?.kingdom == null || zone.city.kingdom.GetEmpire() != empire;

    // 鼠标只认得船
    private static void AfterActorNearCursor(ref Actor __result)
    {
        if (PureMapMode.Enabled && __result != null && !PureMapMode.Visible(__result)) __result = null;
    }

    // 原版逐帧算单位可见与否：除船以外一律不可见(不画、不做动画)
    private static void AfterUpdateVisibility(BatchActors __instance)
    {
        if (!PureMapMode.Enabled || __instance._array == null) return;
        Actor[] array = __instance._array;
        int count = global::System.Math.Min(__instance._count, array.Length);
        for (int i = 0; i < count; i++)
        {
            Actor actor = array[i];
            if (actor != null && actor.is_visible && !PureMapMode.Visible(actor)) actor.is_visible = false;
        }
    }

    private static void AfterBuildingLimit(City __instance, BuildOrder pElement, ref int __result)
    {
        if (PureMapMode.Enabled && __result > 0 && PureMapMode.IsDecor(pElement?.getBuildingAsset(__instance)))
            __result = 0;
    }

    private static bool BeforeShouldBeClearColor(ref bool __result)
    {
        if (!PureMapMode.Enabled) return true;
        __result = false;
        return false;
    }

    private static void Opaque(ref Color32 color)
    {
        if (color.a > 0) color.a = byte.MaxValue;
    }

    private static void BeforeApplyColors(ref Color32 pColorInside, ref Color32 pColorOutside)
    {
        if (!PureMapMode.Enabled) return;
        Opaque(ref pColorInside);
        Opaque(ref pColorOutside);
    }

    private static void BeforeApplyColorsFull(ref Color32 pColor)
    {
        if (PureMapMode.Enabled) Opaque(ref pColor);
    }

    private static void BeforeColorZone(ref Color32 pColor)
    {
        if (PureMapMode.Enabled) Opaque(ref pColor);
    }

    private static bool BeforeGoTo(Actor pActor, WorldTile pTileTarget, ref ExecuteEvent __result)
    {
        // 船照常航行(地图上要看得到它们在海上走)
        if (!PureMapMode.Enabled || pActor?.asset == null || pTileTarget == null || pActor.current_tile == null ||
            pActor.asset.is_boat)
            return true;
        pActor.clearOldPath();
        pActor.setCurrentTilePosition(pTileTarget);
        pActor.dirty_current_tile = true;
        __result = ExecuteEvent.True;
        return false;
    }
}
