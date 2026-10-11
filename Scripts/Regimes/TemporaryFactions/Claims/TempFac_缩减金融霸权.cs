using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 缩减金融霸权：金融国策收紧一档(霸权 → 常态 → 缩减)。缩减时城市公营贸易收入 -10%，劳工农民得利、商人不满(见 ClaimPolicySystem)
public class TempFac_缩减金融霸权 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_缩减金融霸权();
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
        Empire empire = GetEmpire();
        int level = ClaimPolicySystem.FinancePolicy(empire) - 1;
        ClaimPolicySystem.SetFinancePolicy(empire, level);
        CompletionOutcome = string.Format(LM.Get("claim_outcome_finance_policy"), ClaimPolicySystem.FinancePolicyText(level));
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.data?.constitutional_economy != null && ClaimPolicySystem.FinancePolicy(empire) > -1;
    }
}
