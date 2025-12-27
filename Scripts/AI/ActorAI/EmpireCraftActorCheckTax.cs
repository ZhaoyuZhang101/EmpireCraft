using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckTax: GameAIActorBase
{
    public override Type OriginalBeh =>  typeof(BehActorGiveTax);

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.IsNeedToSubmitTax()) return BehResult.Continue;
        if (!pActor.hasCity()) return BehResult.Continue;
        if (!pActor.hasKingdom()) return BehResult.Continue;
        pActor.kingdom.CheckEmpire();
        var pTaxRate = pActor.kingdom.GetTaxRate();
        City city = pActor.getCity();
        if (pActor.loot > 0)
        {
            //战利品
            int loot = pActor.loot;
            pActor.lootEmpty();
            if (pActor.kingdom.IsInEmpire())
            {
                Empire empire = pActor.kingdom.GetEmpire();
                loot += empire.data.additions.addition[OfficerPowerType.财政] / 2;
            }
            pActor.addMoney(loot);
            //抽成
            int num = (int)((float)pActor.money* pTaxRate);
            if (num <= 0)
            {
                num = 1;
            }
            pActor.addMoney(-num);
            city.AddMoney(num);
        }
        pActor.RecordTaxTime();
        return BehResult.Continue;
    }
}