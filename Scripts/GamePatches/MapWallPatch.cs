using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 图层城墙：原版设顶层格(神力画城墙)之后，纯净地图下排队转成图层城墙；炸弹、擦除、画别的地形落在图层城墙上就把它毁掉
// (见 MapWallSystem)
// 设地面格、顶层格之后还把这块地的地形、城墙缓存作废(见 TerrainSystem、TerrainResistance)，那两份缓存因此不必定时过期
public class MapWallPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(MapWallPatch)).Patch(
            AccessTools.Method(typeof(WorldTile), nameof(WorldTile.setTopTileType)),
            postfix: new HarmonyMethod(typeof(MapWallPatch), nameof(AfterSetTopTileType)));
        new Harmony(nameof(MapWallPatch) + ".TileType").Patch(
            AccessTools.Method(typeof(WorldTile), nameof(WorldTile.setTileType), new[] { typeof(TileType), typeof(bool) }),
            postfix: new HarmonyMethod(typeof(MapWallPatch), nameof(AfterSetTileType)));
        new Harmony(nameof(MapWallPatch) + ".Terraform").Patch(
            AccessTools.Method(typeof(MapAction), nameof(MapAction.terraformTile),
                new[] { typeof(WorldTile), typeof(TileType), typeof(TopTileType), typeof(TerraformOptions), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(MapWallPatch), nameof(BeforeTerraform)));
    }

    public static void BeforeTerraform(WorldTile pTile, TileType pNewTypeMain, TopTileType pTopType, TerraformOptions pOptions) =>
        MapWallSystem.OnTerraform(pTile, pNewTypeMain, pTopType, pOptions);

    public static void AfterSetTopTileType(WorldTile __instance, TopTileType pTopTile)
    {
        MapWallSystem.OnTopTileSet(__instance, pTopTile);
        TileChanged(__instance);
    }

    public static void AfterSetTileType(WorldTile __instance) => TileChanged(__instance);

    private static void TileChanged(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (zone == null) return;
        TerrainSystem.Invalidate(zone);
        TerrainResistance.InvalidateWall(zone);
    }
}
