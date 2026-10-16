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

// 重定律令(变法派、天子派的朝堂议题)：重修律令，法统 +3；贵族、地主怨气 +3，农民、官僚 -2；三十年一次
public class TempFac_重定律令 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_重定律令();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 30;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            empire.AddMandate(3);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 3f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, 3f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -2f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Officer, -2f);
            CompletionOutcome = LM.Get("claim_outcome_重定律令_done");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown();
    }
}
