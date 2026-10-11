using System;
using ai.behaviours;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckTax : GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (!pCity.IsNeedToSubmitTax()) return BehResult.Continue;
        if (pCity.getLoyalty() <= 0)
        {
            if (TreasurySystem.Enabled(pCity))
            {
                EmpireCraftActorCheckTax.SettleCity(pCity);
                pCity.RecordTaxTime();
            }
            return BehResult.Continue;
        }
        var pTaxRate = pCity.kingdom.GetTaxRate();
        Kingdom pKingdom = pCity.kingdom;
        int money = Math.Max(0, pCity.GetMoney());
        int num = (int)(money * Math.Max(0d, Math.Min(1d, pTaxRate)));
        int gold = (int)((float)num * 0.1f);
        num -= gold;
        int corruptedMoney = 0;
        if (pCity.hasLeader() && !pCity.leader.isRekt() && pCity.leader.isAlive())
        {
            Actor actor = pCity.leader;
            var corruptionValue = actor.CalcCorruptionValue();
            pCity.AddCorruptionRate(CorruptionSystem.AnnualOfficeChange(actor, pKingdom, corruptionValue));
            corruptedMoney = (int)(num * Math.Max(0d, Math.Min(1d, corruptionValue / 2d)));
        }
        if (TreasurySystem.Enabled(pCity))
        {
            EmpireCraftActorCheckTax.SettleCity(pCity);
            pCity.RecordTaxTime();
            return BehResult.Continue;
        }
        int collected = (int)(num * Math.Max(0d, Math.Min(1d, 1d - pCity.GetCorruptionRate())));
        corruptedMoney = Math.Min(collected, corruptedMoney);
        if (corruptedMoney > 0 && pCity.hasLeader())
        {
            pCity.leader.addMoney(corruptedMoney);
            pCity.leader.RecordCrime(LawType.贪污);
        }
        pCity.SubMoney(collected);
        pKingdom.AddMoney(collected - corruptedMoney);
        pCity.addResourcesToRandomStockpile("gold", gold);
        pCity.RecordTaxTime();
        return BehResult.Continue;
    }
}
