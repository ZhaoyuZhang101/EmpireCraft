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

// 盐铁专营(变法派、天子派的朝堂议题)：盐铁收归官营，其利(四份经费，见 ClaimTuning.Share)入国库；商人怨气 +8、农民 +2；十五年一次
public class TempFac_盐铁专营 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_盐铁专营();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 15;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            int money = 4 * ClaimTuning.Share(empire);
            empire.CoreKingdom.AddMoney(money, TreasuryCategory.Policy);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 8f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 2f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_盐铁专营_done"), MoneyDisplay.Format(money));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown();
    }
}
