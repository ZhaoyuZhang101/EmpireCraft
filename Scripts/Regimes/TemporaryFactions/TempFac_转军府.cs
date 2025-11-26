using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_转军府:TemporaryFaction
{
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        if (targetType == MetaType.Kingdom)
        {
            Kingdom kingdom = World.world.kingdoms.get(targetID);
            kingdom.GetRegime().SetAllowArmy(true);
            kingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
        }
        End();
    }
    public override bool CheckCondition()
    {
        if (targetType == MetaType.Kingdom)
        {
            Kingdom kingdom = World.world.kingdoms.get(targetID);
            Regime regime = kingdom.GetRegime();
            if (regime.type == RegimeType.LvLing)
            {
                if (!regime.IsAllowArmy() || regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession)
                {
                    return true;
                }
            }
        }
        return false;
    }
}