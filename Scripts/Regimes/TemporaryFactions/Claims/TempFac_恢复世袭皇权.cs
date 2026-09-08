using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_恢复世袭皇权 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var result = new TempFac_恢复世袭皇权();
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
            empire.CoreKingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            CompletionOutcome = LM.Get("holy_roman_outcome_hereditary");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return HolyRomanClaimRules.IsEligible(empire) &&
               empire.CoreKingdom.GetRegime().GetLeaderSelectMethod() == LeaderSelectMethod.Vote;
    }
}
