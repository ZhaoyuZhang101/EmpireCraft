using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_确认诸侯特权 : TemporaryFaction
{
    public TempFac_确认诸侯特权()
    {
        canBePushByLocal = true;
    }

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var result = new TempFac_确认诸侯特权();
        result.Init(faction);
        result.ShowAsPlot = ShowAsPlot;
        result.Hide = Hide;
        result.Active = Active;
        result.canBePushByLocal = canBePushByLocal;
        return result;
    }

    public override void Execute()
    {
        Kingdom target = GetKingdomTarget();
        Regime regime = target?.GetRegime();
        if (HolyRomanClaimRules.IsEligible(GetEmpire()) && regime != null)
        {
            regime.SetAllowDiplomacy(true);
            regime.SetAllowSupportCenterArmy(false);
            CompletionOutcome = string.Format(LM.Get("holy_roman_outcome_princely_privileges"),
                target.GetKingdomName());
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Kingdom target = HolyRomanClaimRules.GetPrinces(GetEmpire())
            .Where(NeedsPrivilege)
            .OrderByDescending(kingdom => kingdom.countTotalWarriors())
            .FirstOrDefault();
        return target != null && TrySetTarget(target);
    }

    public override bool CheckLocalCondition(Kingdom actor)
    {
        if (!base.CheckLocalContinue(actor) || !NeedsPrivilege(actor)) return false;
        return TrySetTarget(actor);
    }

    private bool NeedsPrivilege(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || kingdom == GetEmpire()?.CoreKingdom ||
            kingdom.GetEmpire() != GetEmpire()) return false;
        Regime regime = kingdom.GetRegime();
        return regime != null && (!regime.IsAllowDiplomacy() || regime.IsAllowSupportCenterArmy());
    }
}
