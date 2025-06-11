using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GodPowers;

internal static class SetEmpirePower
{
    
    public static void init()
    {
        GodPower _power = new GodPower();
        _power.id = "SetEmpire";
        _power.name = "SetEmpire";
        _power.click_action = clickAction;
        _power.show_tool_sizes = true;
        AssetManager.powers.add(_power);
    }

    public static bool clickAction(WorldTile pTile, string pPowerID)
    {
        LogService.LogInfo("点击了SetEmpirePower");
        if (pTile.hasCity())
        {
            if (pTile.zone_city.hasKingdom())
            {
                Kingdom pKingdom = pTile.zone_city.kingdom;
                pKingdom.SetCountryLevel(countryLevel.countrylevel_0);
                Actor king = pTile.zone_city.kingdom.king;
                king.SetPeeragesLevel(king.isSexMale()?PeeragesLevel.peerages_emperor:PeeragesLevel.peerages_empress);
            }
        }

        return true;
    }
}
