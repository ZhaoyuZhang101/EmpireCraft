using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckOffice:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (pActor.isRekt()) return BehResult.Continue;
        if (!pActor.IsOnOffice()) return BehResult.Continue;
        var office = pActor.GetOffice();
        if (office == null)
        {
            pActor.EndOffice();
            return BehResult.Continue;
        }
        if (office.actor_id == pActor.id) return BehResult.Continue;
        pActor.EndOffice();
        LogService.LogInfo($"{pActor.name}脱离官位{office.GetOfficeName()}");
        return BehResult.Continue;
    }
}