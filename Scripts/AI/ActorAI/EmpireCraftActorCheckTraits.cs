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
            // 宗教等级越高，入神职的人越多(原来一律四成)：政教合一 25%、国教限制 15%、国教自由 8%、无国教 4%
            double clergy = pActor.kingdom.GetRegime().GetReligionLevel() switch
            {
                ReligionLevel.High => 0.25,
                ReligionLevel.Medium => 0.15,
                ReligionLevel.Low => 0.08,
                _ => 0.04
            };
            if (random.NextDouble() <= clergy)
            {
                pActor.addTrait("cleric");
            }
            pActor.FinishChooseToBecomeCleric();
        }
        return BehResult.Continue;
    }
}