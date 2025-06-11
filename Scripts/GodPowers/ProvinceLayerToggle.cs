using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GodPowers;
internal static class ProvinceLayerToggle
{
    public static void init()
    {
        GodPower _power = new GodPower();
        _power.id = "ProvinceLayer";
        _power.name = "ProvinceLayer";
        _power.toggle_name = "ProvinceLayer";
        _power.toggle_action = toggleAction;
        AssetManager.powers.add(_power);
    }

    private static void toggleAction(string pPower)
    {
        
    }
}
