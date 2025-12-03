using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_确立国教 : TemporaryFaction
{
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        empire.Religion = empire.CoreKingdom.religion;
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire.Religion.isRekt()&&empire.CoreKingdom.hasReligion())
        {
            return true;
        }
        return false;
    }
}
