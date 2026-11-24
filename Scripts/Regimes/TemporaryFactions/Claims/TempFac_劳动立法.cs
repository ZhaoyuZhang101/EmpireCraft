using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 劳动立法(现代议题，见 LabourSystem)：立一档劳动法(八小时工作、禁用童工、工伤抚恤，至多两档)。劳工怨气大减、商人不满，工厂产值每档少百分之四。八年一次
public class TempFac_劳动立法 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_劳动立法();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 8;

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!ModernPolicySystem.Industrial(empire) || !OffCooldown()) return false;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        return state.labour_law < LabourSystem.MaxLabourLaw;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (LabourSystem.RaiseLabourLaw(empire))
                CompletionOutcome = string.Format(LM.Get("claim_outcome_劳动立法_done"), state.labour_law);
        }
        End();
    }
}
