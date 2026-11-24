using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 关税壁垒(现代议题)：本国市场改行保护关税(关税翻倍、外国机器货挤进来的少一半)。货卖给本国最多的那个国家有机会报复，打起关税战(见 TradeWarSystem)。八年一次
public class TempFac_关税壁垒 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_关税壁垒();
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
        return CommodityMarketSystem.RootOf(core) == core && CommodityMarketSystem.PolicyOf(core) != TradePolicy.Protective &&
               CommodityMarketSystem.CanSetPolicy(core, TradePolicy.Protective) == null;
    }

    public override void Execute()
    {
        Empire empire = GetEmpire();
        if (CheckCondition())
        {
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (CommodityMarketSystem.SetPolicy(empire.CoreKingdom, TradePolicy.Protective) == null)
            {
                TradeWarSystem.Provoke(empire, "tariff");
                CompletionOutcome = string.Format(LM.Get("claim_outcome_通商_done"), CommodityMarketSystem.PolicyName(TradePolicy.Protective));
            }
        }
        End();
    }
}
