using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 削减福利(现代议题)：福利降一档，国库年年省下一笔；劳工、农民怨气上涨，商人、地主称便。五年一次
public class TempFac_削减福利 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_削减福利();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 5;

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!ModernPolicySystem.Industrial(empire) || !OffCooldown()) return false;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        return state.welfare_level > 0;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            state.welfare_level--;
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 8f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 5f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, -5f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_削减福利_done"), state.welfare_level);
        }
        End();
    }
}
