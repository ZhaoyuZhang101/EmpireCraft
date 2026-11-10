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

// 整军经武(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：发帑募兵、整饬武备——武备废弛减 30，各军团历练 +10；军人怨气 -6、农民 +4(加派)。十年一次
public class TempFac_整军经武 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_整军经武();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 10;

    public override bool CheckCondition() => GetEmpire()?.CoreKingdom != null && OffCooldown();

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core != null)
        {
            MaladySystem.Relieve(empire, MaladySystem.Military, 30f);
            List<EmpireCraft.Scripts.Data.CorpsData> corps = CorpsSystem.Of(core) ?? new List<EmpireCraft.Scripts.Data.CorpsData>();
            foreach (var unit in corps) unit.veterancy = Mathf.Min(100f, unit.veterancy + 10f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Army, -6f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 4f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_整军经武_done"), corps.Count);
        }
        End();
    }
}
