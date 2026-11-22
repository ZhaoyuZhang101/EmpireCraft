using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 特开恩科(群友 2026-11-22)：新君即位两年内，朝议请于常科之外加开一科，以慰天下读书人。准了耗一科之费、天命 +2，
// 中榜的照旧补官缺；官吏怨气 -2、农民 -2，贵族 +2(寒士多了一条出头的路)。两年内没开成的天命 -1(见 KejuSystem.Yearly)
public class TempFac_特开恩科 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_特开恩科();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override bool CheckCondition() => KejuSystem.GraceOpen(GetEmpire());

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        if (empire != null && KejuSystem.HoldGrace(empire))
        {
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Officer, -2f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -2f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 2f);
            CompletionOutcome = LM.Get("claim_outcome_特开恩科_done");
        }
        End();
    }
}
