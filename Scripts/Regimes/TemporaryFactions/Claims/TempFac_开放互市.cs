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

// 开放互市(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：与一个接壤、不在交战、还没互市的邻国开关通市(游牧、异文化的优先)，十年内照常通市
// (见 NomadRaidSystem.MarketTreaty)。商人怨气 -6、军人 +2(嫌资敌)。十年一次
public class TempFac_开放互市 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_开放互市();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 10;

    public override bool CheckCondition() => TargetReady() && PickRuleTarget();

    // 谁能当目标、朝廷自己怎么挑写在 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器的决议页可改)；这里只留决议自己的前提
    protected override bool TargetReady()
    {
        return GetEmpire()?.CoreKingdom != null && OffCooldown();
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        Kingdom target = GetKingdomTarget();
        if (core != null && target?.data != null && !target.isRekt() && !core.isInWarWith(target))
        {
            var state = NomadRaidSystem.State(core);
            state.market_treaty_with = FrontSystem.Realm(target)?.id ?? target.id;
            state.market_treaty_at = World.world.getCurWorldTime();
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, -6f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Army, 2f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_开放互市_done"), target.GetKingdomName());
        }
        End();
    }
}
