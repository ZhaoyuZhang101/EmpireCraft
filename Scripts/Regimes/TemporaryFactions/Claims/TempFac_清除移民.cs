using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 清除移民：移民国策收紧一档(开放 → 常态 → 驱逐)。驱逐时非主体文化的平民每年被逐出一部分(见 ClaimPolicySystem)
public class TempFac_清除移民 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_清除移民();
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
        int level = ClaimPolicySystem.MigrationPolicy(empire) - 1;
        ClaimPolicySystem.SetMigrationPolicy(empire, level);
        CompletionOutcome = string.Format(LM.Get("claim_outcome_migration_policy"), ClaimPolicySystem.MigrationPolicyText(level));
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire != null && CityPopulationSystem.AbstractPopulationEnabled &&
               ClaimPolicySystem.MigrationPolicy(empire) > -1;
    }
}
