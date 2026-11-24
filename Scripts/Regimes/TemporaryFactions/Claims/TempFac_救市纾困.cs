using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 救市纾困(现代议题，见 EconomicCycleSystem)：恐慌、萧条里改以兴办工程、以工代赈为对策，年年从国库拨款，萧条短些、怨气轻些
public class TempFac_救市纾困 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_救市纾困();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!ModernPolicySystem.Industrial(empire)) return false;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        return EconomicCycleSystem.InCrisis(empire) && state.cycle_response != EconomicCycleSystem.Stimulus &&
               empire.CoreKingdom.GetMoney() >= EconomicCycleSystem.StimulusCost(empire);
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            EconomicCycleSystem.Respond(empire, state, EconomicCycleSystem.Stimulus, false);
            CompletionOutcome = LM.Get("claim_outcome_救市纾困_done");
        }
        End();
    }
}
