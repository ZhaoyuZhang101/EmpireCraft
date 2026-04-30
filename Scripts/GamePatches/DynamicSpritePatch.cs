using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.GamePatches;

public class DynamicSpritePatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(GetRecoloredBuilding)).Patch(
            AccessTools.Method(typeof(DynamicSprites), nameof(DynamicSprites.getRecoloredBuilding)),
            prefix: new HarmonyMethod(GetType(), nameof(GetRecoloredBuilding))
        );
    }
    
    public static bool GetRecoloredBuilding(Sprite pBuildingSprite, ColorAsset pColor, DynamicSpritesAsset pAtlasAsset, ref Sprite __result)
    {
        long buildingSpriteID = DynamicSprites.getBuildingSpriteID(pBuildingSprite.GetHashCode(), pColor);
        Sprite sprite = pAtlasAsset.getSprite(buildingSpriteID);
        if ((object)sprite == null)
        {
            sprite = DynamicSpriteCreator.createNewSpriteBuilding(pAtlasAsset, buildingSpriteID, pBuildingSprite, pColor);
            pAtlasAsset.addSprite(buildingSpriteID, sprite);
        }
        __result = sprite;
        return false;
    }
}