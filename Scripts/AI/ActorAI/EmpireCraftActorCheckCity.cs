using System;
using ai.behaviours;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckCity:GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Actor pActor)
    {
        if (pActor.current_zone.hasCity())
        {
            City city = pActor.current_zone.city;
            if (pActor.hasKingdom() && city != null && city.getPopulationPeople()<=0)
            {
                LogService.LogInfo("开始检测");
                Kingdom kingdom = pActor.kingdom;
                city.joinAnotherKingdom(kingdom);
            }
        }
        return BehResult.Continue;
    }
}