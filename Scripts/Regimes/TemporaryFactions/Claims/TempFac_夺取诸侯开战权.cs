using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_夺取诸侯开战权 : TemporaryFaction
{
    public override bool RequireCrimeTarget => true;

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_夺取诸侯开战权();
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
        Regime regime = kingdom?.GetRegime();
        if (regime != null && !CheckRebelling(kingdom) &&
            !EmpireCraft.Scripts.GeneralSystems.ZhouEraSystem.RoyalReclaimBlocked(GetEmpire()))
        {
            if (!TryEnforceCrimeForCurrentTarget())
            {
                End();
                return;
            }
            regime.SetAllowDiplomacy(false);
        }

        End();
    }

    // 目标：自行开战、兵力超过本朝三分之一、有罪名可追究的成员。规则在 ClaimRules.json 的 target(见 ClaimTargetRules)
    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    protected override bool TargetReady()
    {
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom == null || empire.kingdoms_list == null) return false;
        // 春秋战国王室衰微，诸侯的征伐之权收不回来(周制每年都会还给诸侯，见 ZhouEraSystem)
        return !EmpireCraft.Scripts.GeneralSystems.ZhouEraSystem.RoyalReclaimBlocked(empire);
    }
}
