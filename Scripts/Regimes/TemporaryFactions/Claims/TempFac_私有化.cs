using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 私有化(现代议题，见 ModernPolicySystem)：产业归属往民营走一档(国有化 → 官民并举 → 私有化)。到私有化时各厂区发卖给民间，国库得一笔。十年一次
public class TempFac_私有化 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_私有化();
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
        return state.nationalization > ModernPolicySystem.Privatized;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            int proceeds = ModernPolicySystem.ShiftOwnership(empire, -1);
            string policy = ModernPolicySystem.NationalizationName(state.nationalization);
            CompletionOutcome = proceeds > 0 ? string.Format(LM.Get("claim_outcome_私有化_sold"), policy, proceeds)
                : string.Format(LM.Get("claim_outcome_产业_done"), policy);
        }
        End();
    }
}
