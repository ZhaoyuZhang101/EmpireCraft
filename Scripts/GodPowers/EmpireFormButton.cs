using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class EmpireFormButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_form",
            name = "empire_form",
            click_action = crown_become_action
        });
    }

    private static bool crown_become_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {
            if (pTile.zone_city.kingdom.isEmpire())
            {
                return true;
            }
            ActionLibrary.showWhisperTip("empire_form");
            ModClass.EMPIRE_MANAGER.newEmpire(pTile.zone_city.kingdom);
        }
        return true;
    }
}
