using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_转军府:TemporaryFaction
{
    public TemporaryFactionType type = TemporaryFactionType.转军府;
    public override void Execute()
    {
        if (target.meta_type == MetaType.Kingdom)
        {
            Kingdom kingdom = (Kingdom)target;
            kingdom.GetRegime().SetAllowArmy(true);
            kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
        }
        End();
    }
    public override bool CheckCondition()
    {
        
        return false;
    }
}