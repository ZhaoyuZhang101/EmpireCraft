using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class CreateProvinceButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "create_province",
            name = "create_province",
            click_action = province_create_action
        });
    }

    private static bool province_create_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (!pTile.zone_city.kingdom.isEmpire())
            {
                return false;
            }
            Province province = ModClass.PROVINCE_MANAGER.newProvince(pTile.zone_city);
            if ( pTile.zone_city.isCapitalCity())
            {
                province.SetDirectRule();
            }
        }
        return true;
    }
}
