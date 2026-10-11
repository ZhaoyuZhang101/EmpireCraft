using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 开放移民：移民国策放宽一档(驱逐 → 常态 → 开放)。开放后邻国不比本地富裕的城市每年有平民迁入边城(见 ClaimPolicySystem)
public class TempFac_开放移民 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_开放移民();
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
        int level = ClaimPolicySystem.MigrationPolicy(empire) + 1;
        ClaimPolicySystem.SetMigrationPolicy(empire, level);
        CompletionOutcome = string.Format(LM.Get("claim_outcome_migration_policy"), ClaimPolicySystem.MigrationPolicyText(level));
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire != null && CityPopulationSystem.AbstractPopulationEnabled &&
               ClaimPolicySystem.MigrationPolicy(empire) < 1 && ClaimPolicySystem.HasForeignBorder(empire);
    }
}
