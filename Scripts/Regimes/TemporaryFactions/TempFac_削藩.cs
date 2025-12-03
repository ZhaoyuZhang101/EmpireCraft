using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_削藩 : TemporaryFaction
{

    public override void Execute()
    {
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            if (!CheckRebelling(kingdom))
            {
                foreach (var c in kingdom.cities)
                {
                    c.joinAnotherKingdom(GetEmpire().CoreKingdom);
                }
                LogService.LogInfo("执行成功");
            }
        } 
        End();
    }
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom.IsEmpire()) continue;
            if (kingdom.IsFactionRebelling()) continue;
            if (kingdom.getWars().Any()) continue;
            Regime regime = kingdom.GetRegime();
            if (regime.GetReligionLevel()== ReligionLevel.High) continue;
            if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
            {
                if (kingdom.countTotalWarriors() * 5 <= empire.countWarriors() - kingdom.countTotalWarriors())
                {
                    SetKingdomTarget(kingdom);
                    return true;
                }
            }
        }
        return false;
    }
}
