using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_神授君权 : TemporaryFaction
{
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        var kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            empire.SetEmpireName(kingdom.capital.GetTitle().name);
            empire.data.directPre = "神圣";
        }
        
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (String.IsNullOrEmpty(empire.data.directPre)&&!empire.Religion.isRekt())
        {
            
            var religionCoreCity = empire.Religion.GetCity();
            var religionKingdom = religionCoreCity.kingdom;
            if (religionKingdom.GetRegime().GetReligionLevel() == ReligionLevel.High)
            {
                if (religionKingdom.IsInSameEmpire(empire.CoreKingdom))
                {
                    SetKingdomTarget(religionKingdom);
                    return true;
                }
            }
        }
        return false;
    }
}
