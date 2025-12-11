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
        pKingdom.CountingFinishedSelfPlot();
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
            if (pKingdom.IsInEmpire())
            {
                if (corruptionValue > 0)
                {
                    pKingdom.AddCorruptionRate(corruptionValue/10f);
                }
            }
            else
            {
                pKingdom.AddCorruptionRate(-0.2f);
            }
            corruptedMoney = (int)(corruptionValue / 2) * num;

            int intel = actor.intelligence;
            if (intel < 0) intel = 0;
            if (intel > 40) intel = 40;
            
            double discoverProb = 0.6 - (intel / 40.0) * 0.5;
            if (discoverProb < 0.05) discoverProb = 0.05;
            if (discoverProb > 0.95) discoverProb = 0.95;
            
            Random rand = new Random();
            bool caught = rand.NextDouble() < discoverProb;

            if (!caught)
            {
                // 未被发现，国王拿到贪污款
                actor.addMoney(corruptedMoney); 
            }
            else
            {
                // 被发现：充公，帝国收到全部税（下面会统一入账）
                pKingdom.GetOffice().RemoveActor();
                corruptedMoney = 0;
            }
            actor.addMoney(corruptedMoney);
        }
        pKingdom.SubMoney((int)(num*(1.0f-pKingdom.GetCorruptionRate())));
        pEmpireKingdom.AddMoney((int)((num-corruptedMoney)*(1.0f-pKingdom.GetCorruptionRate())));
        pKingdom.RecordTaxTime();
        return BehResult.Continue;
    }
}