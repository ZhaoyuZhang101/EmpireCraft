using System;
using System.Globalization;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckTax:GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (!pCity.IsNeedToSubmitTax()) return BehResult.Continue;
        if (pCity.getLoyalty()<=0) return BehResult.Continue;
        var pTaxRate = pCity.kingdom.GetTaxRate();
        Kingdom pKingdom = pCity.kingdom;
        //金钱
        int money = pCity.GetMoney();
        //抽成
        int num = (int)((float)money * pTaxRate);
        int corruptedMoney = 0;
        if (pCity.hasLeader())
        {
            Actor actor = pCity.leader;
            var corruptionValue = actor.CalcCorruptionValue();
            if (pCity.kingdom.IsInEmpire())
            {
                if (corruptionValue > 0)
                {
                    pCity.AddCorruptionRate(corruptionValue/10f);
                }
                if (pCity.GetCorruptionRate() >= 0.8f)
                {
                    pCity.kingdom.AddCorruptionRate(0.01f);
                }
            }
            else
            {
                pCity.AddCorruptionRate(-0.2f);
            }
            corruptedMoney = (int)(corruptionValue / 2) * num;
            actor.addMoney(corruptedMoney);
        }
        pCity.SubMoney((int)(num*(1.0f-pCity.GetCorruptionRate())));
        pKingdom.AddMoney((int)((num-corruptedMoney)*(1.0f-pCity.GetCorruptionRate())));
        pCity.RecordTaxTime();
        // LogService.LogInfo($"{pCity.name}交税{num}金,保有{pCity.GetMoney()}");
        return BehResult.Continue;
    }
}