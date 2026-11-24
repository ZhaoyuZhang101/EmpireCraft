using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 反托拉斯(现代议题，见 ConglomerateSystem)：拆掉份额最大的一家财阀。劳工、市民、中小商人怨气降，那家财阀扶持的政党断了这笔献金。十五年一次
public class TempFac_反托拉斯 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_反托拉斯();
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
        return ConglomerateSystem.CanBreak(empire);
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            string house = ConglomerateSystem.Break(empire);
            if (house != null) CompletionOutcome = string.Format(LM.Get("claim_outcome_反托拉斯_done"), house);
        }
        End();
    }
}
