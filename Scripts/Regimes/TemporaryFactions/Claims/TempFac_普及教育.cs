using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 普及教育(现代议题，见 ModernPolicySystem)：国民教育升一档(至多两档)，年年拨款；拨得出时本文化的研究点多出来，市民怨气低一点。八年一次
public class TempFac_普及教育 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_普及教育();
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
        return state.education_level < ModernPolicySystem.MaxEducation &&
               empire.CoreKingdom.GetMoney() >= ModernPolicySystem.EducationCost(empire, state.education_level + 1);
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (ModernPolicySystem.RaiseEducation(empire))
                CompletionOutcome = string.Format(LM.Get("claim_outcome_普及教育_done"), state.education_level);
        }
        End();
    }
}
