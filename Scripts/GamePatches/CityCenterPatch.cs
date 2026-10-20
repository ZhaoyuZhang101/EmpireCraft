using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 城心固定(用户 2026-10-17 "为什么你现在的城市在打仗的时候还会跟着战线跑的""现在都是我的虚拟城市了")：
// 原版每次刷新城市状态都重算城心——有篝火取篝火那一格，有建筑从建筑里挑，一座建筑都没有(纯净地图的虚拟城市)就取
// 离领地中心最近的那块地。打仗时城的地块一增一减，领地中心跟着挪，城心也就跟着挪；城市平面图(城墙、街道、城门、镇村、
// 官道)都以城心为准，整座城看着像跟着战线跑。现在记住第一次定下的城心(CityExtraData.center_tile_x/y，随存档)，
// 只要那一格还归这座城、不是海，就一直用它；那块地归了别的城才照原版重新定。营建页「迁城」就是改记这一格(见 CityRelocationSystem)。
// 铭牌也钉在城心上(用户 2026-10-17 "这些城市铭牌怎么一直在闪"，是位置来回跳)：原版铭牌放在 city_center——
// 离领地中心最近那块地的块心，地块一增一减就换一块地；现在城心定了就用城心那块地的块心(见 AfterUpdateCityCenter)。
public class CityCenterPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        try
        {
            var harmony = new Harmony(nameof(CityCenterPatch));
            harmony.Patch(AccessTools.Method(typeof(City), "recalculateCityTile"),
                prefix: new HarmonyMethod(typeof(CityCenterPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(CityCenterPatch), nameof(Postfix)));
            harmony.Patch(AccessTools.Method(typeof(City), "updateCityCenter"),
                postfix: new HarmonyMethod(typeof(CityCenterPatch), nameof(AfterUpdateCityCenter)));
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 城心固定补丁失败: {exception.Message}");
        }
    }

    public static bool Prefix(City __instance)
    {
        if (__instance?.data == null || World.world == null) return true;
        CityExtension.CityExtraData data = __instance.GetOrCreate();
        if (data.center_tile_x < 0) return true;
        WorldTile tile = World.world.GetTile(data.center_tile_x, data.center_tile_y);
        if (tile?.zone?.city != __instance || tile.Type == null || tile.Type.ocean) return true;
        __instance._city_tile = tile;
        return false;
    }

    public static void Postfix(City __instance)
    {
        if (__instance?.data == null) return;
        WorldTile tile = __instance._city_tile;
        if (tile == null) return;
        CityExtension.CityExtraData data = __instance.GetOrCreate();
        data.center_tile_x = tile.x;
        data.center_tile_y = tile.y;
        Pin(__instance, tile);
    }

    // 原版的 city_center 改成城心那块地的块心(原版也是取块心、往上挪两格)
    public static void AfterUpdateCityCenter(City __instance)
    {
        if (__instance?.data == null || World.world == null || !__instance.hasZones()) return;
        CityExtension.CityExtraData data = __instance.GetOrCreate();
        if (data.center_tile_x < 0) return;
        WorldTile tile = World.world.GetTile(data.center_tile_x, data.center_tile_y);
        if (tile?.zone?.city == __instance) Pin(__instance, tile);
    }

    private static void Pin(City city, WorldTile tile)
    {
        WorldTile anchor = tile.zone?.centerTile ?? tile;
        city.city_center.x = anchor.posV3.x;
        city.city_center.y = anchor.posV3.y + 2f;
        city.last_city_center = city.city_center;
    }
}
