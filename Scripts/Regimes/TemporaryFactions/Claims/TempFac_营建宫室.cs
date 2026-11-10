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

// 营建宫室(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：大兴土木修宫殿苑囿——各城征发民力(算大兴土木，见 CorveeSystem)、天命 +2；
// 贵族怨气 -6、农民 +6。国库亏空时不议。二十年一次
public class TempFac_营建宫室 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_营建宫室();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 20;

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown() && !EmpireBankruptcySystem.IsBankrupt(empire);
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            CorveeSystem.AddPerCity(empire.CoreKingdom, CorveeSystem.FengshanPerCity, true);
            empire.AddMandate(2);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, -6f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 6f);
            CompletionOutcome = LM.Get("claim_outcome_营建宫室_done");
        }
        End();
    }
}
