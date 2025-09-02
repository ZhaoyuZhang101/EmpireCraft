using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckKing : GameAIKingdomBase
{
    public override Type OriginalBeh => typeof(KingdomBehCheckKing);
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.data.timer_new_king > 0f)
        {
            return BehResult.Continue;
        }
        if (pKingdom.hasKing())
        {
            Actor king = pKingdom.king;
            if (king.isAlive())
            {
                TryToGiveGoldenTooth(king);
                king.CheckSpecificClan();
                return BehResult.Continue;
            }
        }
        pKingdom.clearKingData();
        if (!pKingdom.HasHeir()) return BehResult.Continue;
        var heir = pKingdom.GetHeir(); 
        MakeKingAndMoveToCapital(pKingdom, heir);
        return BehResult.Continue;
    }
    public void TryToGiveGoldenTooth(Actor pActor)
    {
        if (pActor.getAge() > 45 && Randy.randomChance(0.05f))
        {
            pActor.addTrait("golden_tooth");
        }
    }

    public void MakeKingAndMoveToCapital(Kingdom pKingdom, Actor pNewKing)
    {
        if (pNewKing.hasCity())
        {
            pNewKing.removeFromArmy();
            if (pNewKing.isCityLeader())
            {
                pNewKing.city.removeLeader();
            }
        }

        if (pKingdom.hasCapital() && pNewKing.city != pKingdom.capital)
        {
            pNewKing.joinCity(pKingdom.capital);
        }

        pKingdom.setKing(pNewKing);
        WorldLog.logNewKing(pKingdom);
    }
}