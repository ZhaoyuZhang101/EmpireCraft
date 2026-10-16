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

// 裁省用度(节用派、清流、宽政派的朝堂议题)：罢宫室、减宗室后宫用度，省下两份经费入国库；贵族怨气 +4、农民 -3；十年一次
public class TempFac_裁省用度 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_裁省用度();
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
            int money = 2 * ClaimTuning.Share(empire);
            empire.CoreKingdom.AddMoney(money, TreasuryCategory.Policy);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 4f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -3f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_裁省用度_done"), MoneyDisplay.Format(money));
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown();
    }
}
