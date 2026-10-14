using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 图层城墙：原版设顶层格(神力画城墙)之后，纯净地图下排队转成图层城墙(见 MapWallSystem)
public class MapWallPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(MapWallPatch)).Patch(
            AccessTools.Method(typeof(WorldTile), nameof(WorldTile.setTopTileType)),
            postfix: new HarmonyMethod(typeof(MapWallPatch), nameof(AfterSetTopTileType)));
    }

    public static void AfterSetTopTileType(WorldTile __instance, TopTileType pTopTile) =>
        MapWallSystem.OnTopTileSet(__instance, pTopTile);
}
