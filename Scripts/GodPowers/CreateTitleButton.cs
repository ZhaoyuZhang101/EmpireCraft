using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class CreateTitleButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "create_title",
            name = "create_title",
            click_action = title_create_action
        });
    }

    private static bool title_create_action(WorldTile pTile, string pPower)
    {
        if (pTile.hasCity())
        {

            ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(pTile.zone_city);
        }
        return true;
    }
}
