using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckTax: GameAIActorBase
{
    public override Type OriginalBeh =>  typeof(BehActorGiveTax);

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.IsNeedToSubmitTax()) return BehResult.Continue;
        var pTaxRate = pActor.kingdom.GetTaxRate();
        City city = pActor.getCity();
        if (pActor.loot > 0)
        {
            //战利品
            int loot = pActor.loot;
            pActor.lootEmpty();
            pActor.addMoney(loot);
            //抽成
            int num = (int)((float)pActor.money* pTaxRate);
            if (num <= 0)
            {
                num = 1;
            }
            pActor.addMoney(-num);
            city.AddMoney(num);
            // LogService.LogInfo($"{pActor.name}交税{num}金,保有{pActor.money}");
        }
        pActor.RecordTaxTime();
        return BehResult.Continue;
    }
}