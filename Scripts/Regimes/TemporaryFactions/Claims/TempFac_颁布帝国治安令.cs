using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
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

    // 颁布后十五年内不再提(治安令施行期间的规矩见 HolyRomanSystem「帝国治安令」一节)
    public override int CooldownYears => 15;

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (HolyRomanClaimRules.IsEligible(empire))
        {
            HolyRomanSystem.EnactImperialPeace(empire, HolyRomanClaimRules.GetSecularPrinces(empire));
            CompletionOutcome = LM.Get("holy_roman_outcome_imperial_peace");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    // 未施行或已被特权、让步部分撤销，且有世俗诸侯还能开战或不出兵(自由城市、主教领不算)。
    // 以前只看后一条，新封的诸侯、本就不出兵的自由城市一出现就又能提
    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return HolyRomanClaimRules.IsEligible(empire) && OffCooldown() && HolyRomanSystem.ImperialPeaceOpen(empire) &&
               HolyRomanClaimRules.GetSecularPrinces(empire)
                   .Any(prince => prince.GetRegime()?.IsAllowDiplomacy() == true ||
                                  prince.GetRegime()?.IsAllowSupportCenterArmy() != true);
    }
}
