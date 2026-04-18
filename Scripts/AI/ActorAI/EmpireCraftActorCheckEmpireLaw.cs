using System;
using ai.behaviours;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckEmpireLaw : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (pActor == null || pActor.isRekt()) return BehResult.Continue;
        EmpireLawSystem.CheckAutomaticLawTriggers(pActor);
        return BehResult.Continue;
    }
}
