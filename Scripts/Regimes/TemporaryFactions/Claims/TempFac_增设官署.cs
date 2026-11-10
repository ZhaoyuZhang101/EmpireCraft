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

// 增设官署(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：添设官职安置人——官僚怨气 -8；冗官冗费加重 10、本朝腐败 +1%；农民怨气 +3。十五年一次
public class TempFac_增设官署 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_增设官署();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 15;

    public override bool CheckCondition() => GetEmpire()?.CoreKingdom != null && OffCooldown();

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            MaladySystem.Aggravate(empire, MaladySystem.Bloat, 10f);
            empire.CoreKingdom.AddCorruptionRate(0.01);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Officer, -8f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 3f);
            CompletionOutcome = LM.Get("claim_outcome_增设官署_done");
        }
        End();
    }
}
