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
public static class ModLayerToggle
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "ModLayer_layer",
            name = "ModLayer_layer",
            unselect_when_window = true,
            toggle_name = "map_ModLayer_layer",
            toggle_action = ToggleAction
        });
    }
    private static void ToggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        if (!playerOptionData.boolVal)
        {
            
            ModClass.CURRENT_MAP_MOD = ModMapMode.ModLayer;
        }
        else
        {
            ModClass.CURRENT_MAP_MOD = ModMapMode.None;
        }
    }
}
