using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_颁布金玺诏书 : TemporaryFaction
{
    public TempFac_颁布金玺诏书()
    {
        canBePushByLocal = true;
    }

    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var result = new TempFac_颁布金玺诏书();
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
        if (HolyRomanClaimRules.HasElectoralCollege(empire))
        {
            empire.CoreKingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Vote);
            CompletionOutcome = LM.Get("holy_roman_outcome_golden_bull");
            LogService.LogInfo($"执行{type}");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return HolyRomanClaimRules.HasElectoralCollege(empire) &&
               empire.CoreKingdom.GetRegime().GetLeaderSelectMethod() != LeaderSelectMethod.Vote;
    }
}
