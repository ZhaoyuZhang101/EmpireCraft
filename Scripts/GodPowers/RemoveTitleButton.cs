using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class RemoveTitleButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "remove_title",
            name = "remove_title",
            click_action = title_remove_action
        });
    }

    private static bool title_remove_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.hasTitle()) 
            {
                pTile.zone_city.GetTitle().removeCity(pTile.zone_city);
            }
        }
        return true;
    }
}
