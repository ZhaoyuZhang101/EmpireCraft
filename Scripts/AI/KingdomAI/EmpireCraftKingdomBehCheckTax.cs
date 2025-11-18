using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTax: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.IsNeedToSubmitTax()) return BehResult.Continue;
        var pTaxRate = pKingdom.GetTaxRate();
        Kingdom pEmpireKingdom = pKingdom.GetEmpire().CoreKingdom;
        //金钱
        int money = pKingdom.GetMoney();
        //抽成
        int num = (int)((float)money * pTaxRate);
        int corruptedMoney = 0;
        if (pKingdom.hasKing())
        {
            Actor actor = pKingdom.king;
            var corruptionValue = actor.CalcCorruptionValue();
            corruptedMoney = (int)(corruptionValue / 2) * num;
            actor.addMoney(corruptedMoney);
        }
        pKingdom.SubMoney(num);
        pEmpireKingdom.AddMoney(num-corruptedMoney);
        pKingdom.RecordTaxTime();
        return BehResult.Continue;
    }
}