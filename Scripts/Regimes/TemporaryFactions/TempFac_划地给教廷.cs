using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_划地给教廷 : TemporaryFaction
{
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        End();
    }

    public override bool CheckCondition()
    {
        return false;
    }
}
