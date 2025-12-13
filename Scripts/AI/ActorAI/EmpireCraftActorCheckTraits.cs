using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckTraits: GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        var office = pActor.GetOffice();
        if (office != null)
        {
            if (office.GetActor() != pActor)
            {
                pActor.EndOffice();
            }
        }
        if (!pActor.hasKingdom()) return BehResult.Continue;
        if (!pActor.isAdult()) return BehResult.Continue;
        if (pActor.kingdom.GetRegime() == null) return BehResult.Continue;
        if (pActor.kingdom.GetRegime().type == RegimeType.LvLing|| pActor.kingdom.GetRegime().type == RegimeType.ZhouFeudalism) return BehResult.Continue;
        if (!pActor.HasChooseToBecomeCleric())
        {
            Random random = new Random();
            random.NextDouble();
            if (random.NextDouble() <= 0.4)
            {
                pActor.addTrait("cleric");
            }
            pActor.FinishChooseToBecomeCleric();
        }
        return BehResult.Continue;
    }
}