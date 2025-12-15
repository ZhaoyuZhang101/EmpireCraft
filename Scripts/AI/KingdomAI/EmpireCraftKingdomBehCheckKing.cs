using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

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
        if (NeedSuccession(pKingdom))
        {
            if (pKingdom.HasHeir())
            {
                ChooseKingFromHeir(pKingdom);  
                return BehResult.Continue;
            }

            if (pKingdom.IsEmpire())
            {
                return BehResult.Continue;
            }
        } 
        OfficeObject office = pKingdom.GetOffice();
        if (office == null) return BehResult.Continue;
        office.is_local = true;
        office.meta_object = pKingdom;
        office.Select(pKingdom);
        return BehResult.Continue;
    }

    public static bool NeedSuccession(Kingdom pKingdom)
    {
        Regime regime = pKingdom.GetRegime();
        return (!pKingdom.IsEmpire() && regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession) ||
               (pKingdom.IsEmpire() && regime.leader_select_method == LeaderSelectMethod.Succession);
    }

    public void ChooseKingFromHeir(Kingdom pKingdom)
    {
        pKingdom.clearKingData();
        if (!pKingdom.HasHeir()) return;
        var heir = pKingdom.GetHeir();
        Kingdom lastKingdom = null;
        if (heir.isKing())
        {
            lastKingdom = heir.kingdom;
        }

        if (lastKingdom != null && !(lastKingdom.GetRegime() is {type: RegimeType.LvLing|RegimeType.ZhouFeudalism}))
        {
            if (pKingdom.IsEmpire())
            {
                Empire empire = pKingdom.GetEmpire();
                empire.CoreKingdom = lastKingdom;
            }
            pKingdom.cities.ForEach(c=>c.joinAnotherKingdom(lastKingdom));
            return;
        }

        if (lastKingdom != null && lastKingdom.GetRegime() is {type: RegimeType.LvLing|RegimeType.ZhouFeudalism})
        {
            if (heir.getChildren().ToList().FindAll(a=>!a.isKing()).Any())
            {
                MakeKingAndMoveToCapital(lastKingdom, heir.getChildren().ToList().Find(a=>!a.isKing()));
            }
            else
            {
                lastKingdom.StartToChooseHeir();
            }
        }
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
    }
}