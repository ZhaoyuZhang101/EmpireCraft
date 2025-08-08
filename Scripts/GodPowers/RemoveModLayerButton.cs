using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class RemoveModLayerButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "remove_ModLayer",
            name = "remove_ModLayer",
            click_action = province_remove_action
        });
    }

    private static bool province_remove_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.IsInModLayer())
            {
                ModLayer pModLayer = pTile.zone_city.GetModLayer();
                if (pModLayer == null)
                {
                    return false;
                }
                pModLayer.removeCity(pTile.zone_city);
            }
        }
        return true;
    }
}
