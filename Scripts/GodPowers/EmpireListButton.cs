using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GodPowers;

public static class EmpireListButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_list_window",
            name = "empire_list_window",
            click_action = Open_empire_list_window
        });
    }

    private static bool Open_empire_list_window(WorldTile pTile, string pPowerID)
    {
        ScrollWindow.showWindow("kingdom");
        return true;
    }
}
