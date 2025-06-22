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
public static class TitleLayerToggle
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "title_layer",
            name = "title_layer",
            unselect_when_window = true,
            toggle_name = "map_title_layer",
            toggle_action = toggleAction
        });
    }

    [Hotfixable]
    private static void toggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        if (!playerOptionData.boolVal)
        {
            EmpireLayerToggle.disableOtherPower(pPower);
            PlayerConfig.dict["map_city_layer"].boolVal = true;
            PlayerConfig.dict["map_empire_layer"].boolVal = false;
            ModClass.CURRENT_MAP_MOD = EmpireCraftMapMode.Title;
        }
        else
        {
            EmpireLayerToggle.disableOtherPower(pPower);
            PlayerConfig.dict["map_city_layer"].boolVal = true;
            ModClass.CURRENT_MAP_MOD = EmpireCraftMapMode.None;
        }
    }
}
