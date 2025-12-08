using System;
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
            corruptedMoney = (int)(corruptionValue / 2) * num;
            actor.addMoney(corruptedMoney);
        }
        pCity.SubMoney(num);
        pKingdom.AddMoney(num-corruptedMoney);
        pCity.RecordTaxTime();
        // LogService.LogInfo($"{pCity.name}交税{num}金,保有{pCity.GetMoney()}");
        return BehResult.Continue;
    }
}