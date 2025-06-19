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
            force_map_mode = MetaType.City,
            toggle_action = toggleAction
        });
    }

    [Hotfixable]
    private static void toggleAction(string pPower)
    {


        LogService.LogInfo("点击了封号层级开关");
        WorldTip.instance.showToolbarText(pPower);
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        if (godPower.map_modes_switch)
        {
            if (playerOptionData.boolVal)
            {
            }
            else
            {
                WorldTip.instance.startHide();
            }
        }
    }
}
