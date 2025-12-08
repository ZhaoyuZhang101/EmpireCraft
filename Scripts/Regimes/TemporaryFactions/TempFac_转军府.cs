using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_转军府 : TemporaryFaction
{

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Kingdom kingdom = GetKingdomTarget();
        if (kingdom != null)
        {
            Regime regime = kingdom.GetRegime();
            regime.SetAllowDiplomacy(true);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            regime.SetTaxLevel(TaxLevel.None);
        }
        End();
    }

    public override bool CheckCondition()
    {
        //
        Empire empire = GetEmpire();
        if (empire == null) return false;
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            if (k.GetKingdomType() != KingdomType.LvLing_jiedushi)
            {
                if (k.IsBorder())
                {
                    SetKingdomTarget(k);
                    return true;
                }
            }
        }
        return false;
    }
}
