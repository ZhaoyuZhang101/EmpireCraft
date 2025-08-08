using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class CreateModLayerButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "create_ModLayer",
            name = "create_ModLayer",
            click_action = province_create_action
        });
    }

    private static bool province_create_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.IsInModLayer())
            {
                return false;
            }
            ModClass.ModLayer_MANAGER.newModLayer(pTile.zone_city);
        }
        return true;
    }
}
