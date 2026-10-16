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

// 大赦天下(清流、宽政派、后党的朝堂议题)：各阶层怨气 -3，法统 +2；十年一次
public class TempFac_大赦天下 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_大赦天下();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 10;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            foreach (SocialClass socialClass in global::System.Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
                ClaimPolicySystem.AddGrievance(empire, socialClass, -3f);
            empire.AddMandate(2);
            CompletionOutcome = LM.Get("claim_outcome_大赦天下_done");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown();
    }
}
