using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 国有化(现代议题，见 ModernPolicySystem)：产业归属往官营走一档(私有化 → 官民并举 → 国有化)。到国有化时各厂区收归官营、财阀收归国有。十年一次
public class TempFac_国有化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_国有化();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 10;

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!ModernPolicySystem.Industrial(empire) || !OffCooldown()) return false;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        return state.nationalization < ModernPolicySystem.Nationalized;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            ModernPolicySystem.ShiftOwnership(empire, 1);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_产业_done"), ModernPolicySystem.NationalizationName(state.nationalization));
        }
        End();
    }
}
