using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckSkeleton: GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Actor pActor)
    {
        if(pActor.isRekt()) return BehResult.Continue;
        if (pActor.subspecies == null) return BehResult.Continue;
        if (pActor.subspecies.species_id != "skeleton") return BehResult.Continue;
        if (!pActor.hasTrait("death_mark"))
        {
            pActor.addTrait("death_mark");
        }
        return BehResult.Continue;
    }
}