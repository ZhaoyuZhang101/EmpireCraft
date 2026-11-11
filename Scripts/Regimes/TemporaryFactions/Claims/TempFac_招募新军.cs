using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 招募新军(用户 2026-11-11 转群友"目前诉求没有招募新军吗？或加强军备"，"可以加")：征兵加饷，在兵源最多、够得上成军的几座城各成一支新军(每八城多一支，至多四支)，
// 兵额照本国一军的平均、不按军制裁撤(CorpsSystem.RecruitNew)；农民怨气 +5、商人 +2，军人 -6。八年一次
public class TempFac_招募新军 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_招募新军();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 8;

    public static int Wanted(Kingdom core) => Mathf.Clamp(1 + (core?.cities?.Count ?? 0) / 8, 1, 4);

    public override bool CheckCondition()
    {
        Kingdom core = GetEmpire()?.CoreKingdom;
        return core != null && OffCooldown() && CorpsSystem.Enabled && CorpsSystem.RecruitSeats(core, Wanted(core)) > 0;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core != null)
        {
            List<CorpsData> raised = CorpsSystem.RecruitNew(core, Wanted(core));
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 5f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 2f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Army, -6f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_招募新军_done"), raised.Count,
                Mathf.RoundToInt(raised.Sum(corps => corps.strength)));
        }
        End();
    }
}
