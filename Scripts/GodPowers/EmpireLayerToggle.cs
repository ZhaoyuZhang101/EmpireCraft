using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GodPowers;
public static class EmpireLayerToggle
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_layer",
            name = "empire_layer",
            unselect_when_window = true,
            toggle_name = "map_empire_layer",
            toggle_action = toggleAction
        });
    }

    private static void toggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        if (!playerOptionData.boolVal)
        {
            disableOtherPower(pPower);
            PlayerConfig.dict["map_kingdom_layer"].boolVal = true;
            PlayerConfig.dict["map_title_layer"].boolVal = false;
            ModClass.CURRENT_MAP_MOD = EmpireCraftMapMode.Empire;
        }
        else
        {
            disableOtherPower(pPower);
            PlayerConfig.dict["map_kingdom_layer"].boolVal = true;
            ModClass.CURRENT_MAP_MOD = EmpireCraftMapMode.None;
        }
    }

    public static void disableOtherPower(string mainpPower)
    {
        for (int i = 0; i < AssetManager.powers.list.Count; i++)
        {
            GodPower cpower = AssetManager.powers.list[i];
            if (cpower.map_modes_switch&&cpower.id != mainpPower)
            {
                PlayerOptionData playerOptionData = PlayerConfig.dict[cpower.toggle_name];
                if (playerOptionData.boolVal)
                {
                    playerOptionData.boolVal = false;
                }
            }
        }
    }
  
}
