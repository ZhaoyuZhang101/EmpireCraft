// 零碎的几样：原版的取文字入口、颜色库、选城址的帮手。
//# hand LocalizedTextManager: getText setLanguage stringExists
//# hand ColorLibrary: getColorByIndex getNextColor getNextColorIndex
//# hand CityPlaceFinder: hasPossibleZones setDirty
using System.Runtime.CompilerServices;
using NeoModLoader.General;

public partial class LocalizedTextManager
{
    // 原版的文字表服务器里没有：一律到模组的文字表里找，找不到回键名
    public static string getText(string pKey, UnityEngine.UI.Text text = null, bool pForceEnglish = false) => LM.Get(pKey);
    public static bool stringExists(string pKey) => LM.Has(pKey);

    [MethodImpl(P.N)]
    public void setLanguage(string pLanguage) => language = pLanguage;
}

public partial class ColorLibrary
{
    private int _next;
    public ColorAsset getColorByIndex(int pIndex) => ShimColors.ById(pIndex);
    public int getNextColorIndex(ActorAsset pActorAsset) => _next++ % ShimColors.Count;
    public ColorAsset getNextColor(ActorAsset pActorAsset) => ShimColors.ById(getNextColorIndex(pActorAsset));
}

public partial class CityPlaceFinder
{
    public bool hasPossibleZones() => false;

    public void setDirty()
    {
    }
}

//# hand MapAction: terraformTop terraformTile createRoadTile decreaseTile makeRoadBetween
// 改地形：服务器里就是换这一格的地块类型
public static partial class MapAction
{
    [MethodImpl(P.N)]
    public static void terraformTile(WorldTile pTile, TileType pNewTypeMain, TopTileType pTopType, TerraformOptions pOptions = null, bool pSkipTerraform = false)
    {
        if (pTile == null) return;
        pTile.setTileTypes(pNewTypeMain ?? pTile.main_type, pTopType);
    }

    public static void terraformTop(WorldTile pTile, TopTileType pTopType, TerraformOptions pOptions, bool pSkipTerraform = false) => pTile?.setTopTileType(pTopType);
    public static void createRoadTile(WorldTile pTile) => pTile?.setTopTileType(AssetManager.top_tiles.get("road"));

    public static void decreaseTile(WorldTile pTile, bool pDamage, string pTerraformOption = "flash")
    {
    }

    [MethodImpl(P.N)]
    public static void makeRoadBetween(WorldTile pTile1, WorldTile pTile2, City pCity = null, bool pForceFinished = false)
    {
    }
}
