using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 图层城墙：原版设顶层格(神力画城墙)之后，纯净地图下排队转成图层城墙；炸弹、擦除、画别的地形落在图层城墙上就把它毁掉
// (见 MapWallSystem)
// 设地面格、顶层格之后还把这块地的地形、城墙缓存作废(见 TerrainSystem、TerrainResistance)，那两份缓存因此不必定时过期；
// 设地面格还看海区要不要重划(见 SeaRegionSystem)；格子的样子(Type)变了让图层色块重画(见 ZonePaintBudget.TerrainChanged)
public class MapWallPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(MapWallPatch)).Patch(
            AccessTools.Method(typeof(WorldTile), nameof(WorldTile.setTopTileType)),
            prefix: new HarmonyMethod(typeof(MapWallPatch), nameof(BeforeSetType)),
            postfix: new HarmonyMethod(typeof(MapWallPatch), nameof(AfterSetTopTileType)));
        new Harmony(nameof(MapWallPatch) + ".TileType").Patch(
            AccessTools.Method(typeof(WorldTile), nameof(WorldTile.setTileType), new[] { typeof(TileType), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(MapWallPatch), nameof(BeforeSetType)),
            postfix: new HarmonyMethod(typeof(MapWallPatch), nameof(AfterSetTileType)));
        new Harmony(nameof(MapWallPatch) + ".Terraform").Patch(
            AccessTools.Method(typeof(MapAction), nameof(MapAction.terraformTile),
                new[] { typeof(WorldTile), typeof(TileType), typeof(TopTileType), typeof(TerraformOptions), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(MapWallPatch), nameof(BeforeTerraform)));
    }

    public static void BeforeTerraform(WorldTile pTile, TileType pNewTypeMain, TopTileType pTopType, TerraformOptions pOptions) =>
        MapWallSystem.OnTerraform(pTile, pNewTypeMain, pTopType, pOptions);

    // 记下设格之前这一格的样子(原版 Type 在 updateStats 里才换：setTileTypes 先不更新地设顶层格，再设地面格时一起换)
    public static void BeforeSetType(WorldTile __instance, out TileTypeBase __state) => __state = __instance?.Type;

    public static void AfterSetTopTileType(WorldTile __instance, TopTileType pTopTile, TileTypeBase __state)
    {
        MapWallSystem.OnTopTileSet(__instance, pTopTile);
        TileChanged(__instance);
        ZonePaintBudget.TerrainChanged(__instance, __state);
    }

    // 地面格还决定这块地算不算海(水、陆格数只在这里变)：变了海区要重划(见 SeaRegionSystem)
    public static void AfterSetTileType(WorldTile __instance, TileTypeBase __state)
    {
        TileChanged(__instance);
        SeaRegionSystem.OnTileChanged(__instance?.zone);
        ZonePaintBudget.TerrainChanged(__instance, __state);
    }

    private static void TileChanged(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (zone == null) return;
        TerrainSystem.Invalidate(zone);
        TerrainResistance.InvalidateWall(zone);
    }
}
