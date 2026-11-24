using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 自由贸易(现代议题)：本国市场改行自由贸易(关税降到四分之一、外国货进得多、本地货价压得低)，景气涨得快，也更容易被别国的恐慌波及。八年一次
public class TempFac_自由贸易 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_自由贸易();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 8;

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (!ModernPolicySystem.Industrial(empire) || !OffCooldown()) return false;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        Kingdom core = empire.CoreKingdom;
        return CommodityMarketSystem.RootOf(core) == core && CommodityMarketSystem.PolicyOf(core) != TradePolicy.Free &&
               CommodityMarketSystem.CanSetPolicy(core, TradePolicy.Free) == null;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (CommodityMarketSystem.SetPolicy(empire.CoreKingdom, TradePolicy.Free) == null)
                CompletionOutcome = string.Format(LM.Get("claim_outcome_通商_done"), CommodityMarketSystem.PolicyName(TradePolicy.Free));
        }
        End();
    }
}
