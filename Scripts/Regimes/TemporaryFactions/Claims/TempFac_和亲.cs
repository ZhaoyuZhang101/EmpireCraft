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

// 和亲(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：与一个强邻(兵力不下本朝七成；正在交战的、游牧的优先)和亲修好——宗室女出嫁(见 RoyalMarriageSystem.ArrangeHeqin)，
// 订三十年互不侵犯，正打着的仗就此罢兵。文官怨气 -2、军人 +3(以为屈辱)。十五年一次
public class TempFac_和亲 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_和亲();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 15;

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
        if (core != null && target?.data != null && !target.isRekt())
        {
            Actor bride = RoyalMarriageSystem.ArrangeHeqin(core, target);
            empire.data.diplomatic_pacts[target.id] = World.world.getCurWorldTime();
            if (core.isInWarWith(target)) core.EndWarWith(target);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Officer, -2f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Army, 3f);
            CompletionOutcome = string.Format(LM.Get(bride != null ? "claim_outcome_和亲_done" : "claim_outcome_和亲_pact"),
                target.GetKingdomName(), bride?.getName() ?? "");
        }
        End();
    }
}
