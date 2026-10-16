using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 重农抑商(重农派、清流的朝堂议题)：商人怨气 +10，农民 -5、地主 -2；二十年一次
public class TempFac_重农抑商 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_重农抑商();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 20;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 10f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -5f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, -2f);
            CompletionOutcome = LM.Get("claim_outcome_重农抑商_done");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown();
    }
}
