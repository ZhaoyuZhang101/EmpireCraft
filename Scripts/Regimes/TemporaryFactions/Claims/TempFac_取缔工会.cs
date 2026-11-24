using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 取缔工会(现代议题，见 LabourSystem)：工会的地位收紧一档(合法 → 默许 → 取缔)。工会声势大跌、罢工少了，劳工怨气更深。五年一次
public class TempFac_取缔工会 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_取缔工会();
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
        return state.union_status > LabourSystem.Banned;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            LabourSystem.SetUnionStatus(empire, state.union_status - 1);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_工会_done"), LabourSystem.UnionStatusName(state.union_status));
        }
        End();
    }
}
