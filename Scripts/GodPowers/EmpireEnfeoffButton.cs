using EmpireCraft.Scripts.GameClassExtensions;
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
        if (!pTile.zone_city.kingdom.isInEmpire()) return false;
        pTile.zone_city.kingdom.GetEmpire().AutoEnfeoff();
        ActionLibrary.showWhisperTip("分封天下");
        return true;
    }
}
