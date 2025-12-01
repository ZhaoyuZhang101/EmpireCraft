using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

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
        Regime regime = pKingdom.GetRegime();
        if ((!pKingdom.IsEmpire()&&regime.GetLeaderSelectMethod()==LeaderSelectMethod.Succession)||(pKingdom.IsEmpire()&&regime.leader_select_method==LeaderSelectMethod.Succession))
        {
            if (pKingdom.HasHeir())
            {
                ChooseKingFromHeir(pKingdom);  
                return BehResult.Continue;
            }
        } 
        OfficeObject office = pKingdom.GetOffice();
        if (office == null) return BehResult.Continue;
        office.meta_object = pKingdom;
        office.Select(pKingdom);
        return BehResult.Continue;
    }

    public void ChooseKingFromHeir(Kingdom pKingdom)
    {
        pKingdom.clearKingData();
        if (!pKingdom.HasHeir()) return;
        var heir = pKingdom.GetHeir(); 
        MakeKingAndMoveToCapital(pKingdom, heir);
        OfficeObject office = pKingdom.GetOffice();
        office.meta_object = pKingdom;
        office.SetActor(heir);
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