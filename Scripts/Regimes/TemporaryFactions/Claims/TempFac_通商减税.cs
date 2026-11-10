using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 通商减税(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选；与重农抑商相反)：恤商、通商，商贸收入 ×1.15(见 ClaimPolicySystem.SetFinancePolicy)；
// 商人怨气 -10、市民 -4、地主 +3。二十年一次
public class TempFac_通商减税 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_通商减税();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 20;

    public override bool CheckCondition() => GetEmpire()?.CoreKingdom != null && OffCooldown();

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            ClaimPolicySystem.SetFinancePolicy(empire, 1);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, -10f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Citizen, -4f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, 3f);
            CompletionOutcome = LM.Get("claim_outcome_通商减税_done");
        }
        End();
    }
}
