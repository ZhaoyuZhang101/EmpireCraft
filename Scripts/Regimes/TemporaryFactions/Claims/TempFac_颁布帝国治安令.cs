using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_颁布帝国治安令 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var result = new TempFac_颁布帝国治安令();
        result.Init(faction);
        result.ShowAsPlot = ShowAsPlot;
        result.Hide = Hide;
        result.Active = Active;
        result.canBePushByLocal = canBePushByLocal;
        return result;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (HolyRomanClaimRules.IsEligible(empire))
        {
            foreach (Kingdom prince in HolyRomanClaimRules.GetPrinces(empire))
            {
                Regime regime = prince.GetRegime();
                if (regime == null) continue;
                regime.SetAllowDiplomacy(false);
                regime.SetAllowSupportCenterArmy(true);
            }
            CompletionOutcome = LM.Get("holy_roman_outcome_imperial_peace");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return HolyRomanClaimRules.IsEligible(empire) && HolyRomanClaimRules.GetPrinces(empire)
            .Any(prince => prince.GetRegime()?.IsAllowDiplomacy() == true ||
                           prince.GetRegime()?.IsAllowSupportCenterArmy() != true);
    }
}
