using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 图层城墙：原版设顶层格(神力画城墙)之后，纯净地图下排队转成图层城墙；炸弹、擦除、画别的地形落在图层城墙上就把它毁掉
// (见 MapWallSystem)
public class MapWallPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(MapWallPatch)).Patch(
            AccessTools.Method(typeof(WorldTile), nameof(WorldTile.setTopTileType)),
            postfix: new HarmonyMethod(typeof(MapWallPatch), nameof(AfterSetTopTileType)));
        new Harmony(nameof(MapWallPatch) + ".Terraform").Patch(
            AccessTools.Method(typeof(MapAction), nameof(MapAction.terraformTile),
                new[] { typeof(WorldTile), typeof(TileType), typeof(TopTileType), typeof(TerraformOptions), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(MapWallPatch), nameof(BeforeTerraform)));
    }

    public static void BeforeTerraform(WorldTile pTile, TileType pNewTypeMain, TopTileType pTopType, TerraformOptions pOptions) =>
        MapWallSystem.OnTerraform(pTile, pNewTypeMain, pTopType, pOptions);

    public static void AfterSetTopTileType(WorldTile __instance, TopTileType pTopTile) =>
        MapWallSystem.OnTopTileSet(__instance, pTopTile);
}
