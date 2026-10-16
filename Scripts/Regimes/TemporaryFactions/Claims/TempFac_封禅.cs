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

// 封禅(方士、巫祝、天子派的朝堂议题)：受了天命(见 TianxiaSystem)或在开国气象里、天子成年，才封禅告成于天——法统 +5，劳民伤财，农民怨气 +3；五十年一次
public class TempFac_封禅 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_封禅();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 50;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            empire.AddMandate(5);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 3f);
            CompletionOutcome = LM.Get("claim_outcome_封禅_done");
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown() && Worthy(empire);
    }

    private static bool Worthy(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        if (empire.Emperor == null || !empire.Emperor.isAdult()) return false;
        return TianxiaSystem.MandateOf(core) != null || UnificationSystem.InGrace(core);
    }
}
