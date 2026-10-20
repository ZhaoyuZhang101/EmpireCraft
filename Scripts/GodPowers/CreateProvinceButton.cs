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
            if (pTile.zone_city.kingdom.IsEmpire())
            {
                Empire empire = pTile.zone_city.kingdom.GetEmpire();
                Kingdom kingdom = pTile.zone_city.makeOwnKingdom(pTile.zone_city?.leader??pTile.zone_city?.units.GetRandom());
                if (kingdom != null)
                {
                    // 神力设的省：十五年内不因离心自行离开(见 MembershipCooldown)
                    EmpireCraft.Scripts.GeneralSystems.MembershipCooldown.ByGod(() => empire.join(kingdom, pForce: true));
                }
                return false;
            }
        }
        return true;
    }
}
