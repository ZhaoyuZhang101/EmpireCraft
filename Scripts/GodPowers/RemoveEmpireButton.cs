using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class RemoveEmpireButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "remove_empire",
            name = "remove_empire",
            click_action = empire_remove_action
        });
    }

    private static bool empire_remove_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.hasKingdom())
            {
                if(pTile.zone_city.kingdom.isInEmpire())
                {
                    Empire empire = pTile.zone_city.kingdom.GetEmpire();
                    empire.leave(pTile.zone_city.kingdom);
                }
            }
        }
        return true;
    }
}
