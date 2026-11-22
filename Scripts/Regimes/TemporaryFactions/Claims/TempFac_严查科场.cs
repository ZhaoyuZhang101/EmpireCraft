using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 严查科场(群友 2026-11-22)：科场舞弊案发后(见 KejuSystem)，朝议请严鞫此案、另开一科。准了再耗一科之费、天命 +2、
// 吏治之弊稍减；官吏怨气 +3(考官、行贿士子依律革问)、农民 -3。两年里没人查办就从轻发落：天命 -1、吏治之弊加重
public class TempFac_严查科场 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_严查科场();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override bool CheckCondition() => KejuSystem.FraudOpen(GetEmpire());

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        if (empire != null && KejuSystem.Punish(empire))
        {
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Officer, 3f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -3f);
            CompletionOutcome = LM.Get("claim_outcome_严查科场_done");
        }
        End();
    }
}
