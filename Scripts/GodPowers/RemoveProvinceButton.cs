using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class RemoveProvinceButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "remove_province",
            name = "remove_province",
            click_action = province_remove_action
        });
    }

    private static bool province_remove_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.hasKingdom())
            {
                Kingdom kingdom = pTile.zone_city.kingdom;
                foreach (var city in kingdom.cities)
                {
                    city.joinAnotherKingdom(kingdom.GetEmpire().CoreKingdom);
                }
                return true;
            }
        }
        return false;
    }
}
