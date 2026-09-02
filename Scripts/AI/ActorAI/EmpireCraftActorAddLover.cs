using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorAddLover:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.isUnitFitToRule()) return BehResult.Continue;
        if (!pActor.isAdult()) return BehResult.Continue;
        if (!pActor.hasKingdom()) return BehResult.Continue;
        if (pActor.IsEmperor()) return BehResult.Continue;
        var culture = pActor.culture;
        if (culture==null) return BehResult.Continue;
        if (!culture.hasTrait("patriarchy")&&!culture.hasTrait("matriarchy")) return BehResult.Continue;
        var loverSex = culture.hasTrait("patriarchy") ? ActorSex.Female : ActorSex.Male;
        var identity = pActor.GetPersonalIdentity();
        if ( identity == null) return BehResult.Continue;
        if (pActor.age>70) return BehResult.Continue;
        var cus = identity.concubines;
        if (cus.Count > 0)
        {
            cus.ForEach(c =>
            {
                var id = c.identity;
                var cIdentity = SpecificClanManager.getPerson(id);
                var cActor = cIdentity._actor;
                if (cActor != null)
                {
                    if (OverallHelperFunc.HasChangeToGiveBirth(cActor, pActor)&&cActor.IsNeedToGiveBirth())
                    {
                        BabyMaker.makeBaby(pActor, cActor); 
                        cActor.RecordGiveBirthTime();
                    }
                }
            });
        }
        if (pActor.money > 25&&pActor.IsNeedToAddLover())
        {
            Random random = new Random();
            var possibility = random.NextDouble();
            if (possibility < 0.3f)
            {
                pActor.addMoney(-25);
                var actor = pActor.kingdom.units.Find(a => a.data.sex == loverSex 
                                                              && a.age <= 35 && a.isAdult()
                                                              && !a.hasLover());
                if (actor == null) return BehResult.Continue;
                identity.setLover(actor, true);
            }
        }
        return BehResult.Continue;
    }
}
