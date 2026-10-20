using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
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
            // 帝国治安令施行期间：这一家记为豁免(治安令被部分撤销，可以再提)
            HolyRomanSystem.Exempt(GetEmpire(), target);
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
        // 自由城市、主教领本就不开战、不给中央出兵，不讨这份特权
        if (kingdom == null || kingdom.isRekt() || kingdom == GetEmpire()?.CoreKingdom ||
            kingdom.GetEmpire() != GetEmpire() || !HolyRomanSystem.SecularPrince(kingdom)) return false;
        Regime regime = kingdom.GetRegime();
        return regime != null && (!regime.IsAllowDiplomacy() || regime.IsAllowSupportCenterArmy());
    }
}
