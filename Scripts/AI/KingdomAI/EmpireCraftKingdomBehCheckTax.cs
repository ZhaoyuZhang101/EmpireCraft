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

            int intel = actor.intelligence;
            if (intel < 0) intel = 0;
            if (intel > 40) intel = 40;

            // 发现概率：基础 0.6，随智力线性下降 0.5，到 0.1
            double discoverProb = 0.6 - (intel / 40.0) * 0.5;
            // 手动 clamp 概率到 0.05~0.95，避免极端值
            if (discoverProb < 0.05) discoverProb = 0.05;
            if (discoverProb > 0.95) discoverProb = 0.95;

            // .NET 6+ 可用 Random.Shared；旧版请改成静态 Random 实例
            Random rand = new Random();
            bool caught = rand.NextDouble() < discoverProb;

            if (!caught)
            {
                actor.addMoney(corruptedMoney); // 未被发现，国王拿到贪污款
            }
            else
            {
                // 被发现：充公，帝国收到全部税（下面会统一入账）
                pKingdom.GetOffice().RemoveActor();
                LogService.LogInfo("贪污被发现，下马");
                corruptedMoney = 0;
            }
            actor.addMoney(corruptedMoney);
        }
        pKingdom.SubMoney(num);
        pEmpireKingdom.AddMoney(num-corruptedMoney);
        pKingdom.RecordTaxTime();
        return BehResult.Continue;
    }
}