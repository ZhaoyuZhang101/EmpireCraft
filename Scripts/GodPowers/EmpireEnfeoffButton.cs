using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class EmpireEnfeoffButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_enfeoff",
            name = "empire_enfeoff",
            click_action = enfeoff_action
        });
    }

    private static bool enfeoff_action(WorldTile pTile, string pPower)
    {
        if (!pTile.hasCity()) return false;
        if (!pTile.zone_city.kingdom.IsInEmpire()) return false;
        // 神力分出的成员：十五年内不因离心自行离开(见 MembershipCooldown)
        Empire empire = pTile.zone_city.kingdom.GetEmpire();
        EmpireCraft.Scripts.GeneralSystems.MembershipCooldown.ByGod(() => empire.AutoEnfeoff());
        ActionLibrary.showWhisperTip("start_enfeoff");
        return true;
    }
}
