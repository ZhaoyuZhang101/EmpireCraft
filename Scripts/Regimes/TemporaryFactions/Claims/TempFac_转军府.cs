using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_转军府 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_转军府();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

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
        // 律令制不允许军府借由转制取得独立国家地位。
        if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing) return false;
        foreach (var k in empire.kingdoms_list)
        {
            if (k.IsEmpire()) continue;
            if (k.GetKingdomType() == KingdomType.LvLing_kingdom) continue;
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
