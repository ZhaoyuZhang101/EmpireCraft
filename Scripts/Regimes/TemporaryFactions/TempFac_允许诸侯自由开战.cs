using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_允许诸侯自由开战 : TemporaryFaction
{

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Regime regime = kingdom.GetRegime();
            regime.SetAllowDiplomacy(true);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            Regime regime = kingdom.GetRegime();
            if (!regime.IsAllowDiplomacy())
            {
                SetKingdomTarget(kingdom);
                return true;
            }
        }
        return false;
    }
}
