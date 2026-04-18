using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_削藩 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_削藩();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            if (!CheckRebelling(kingdom))
            {
                EmpireLawSystem.TryEnforceCrimeForClaim(kingdom.king, kingdom);
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
        if (empire == null) return false;
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
