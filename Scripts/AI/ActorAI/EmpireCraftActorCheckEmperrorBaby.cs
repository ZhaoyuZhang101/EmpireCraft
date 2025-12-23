using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckEmperorBaby: GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.IsEmperor()) return BehResult.Continue;
        if (!pActor.hasLover()) return BehResult.Continue;
        if (!pActor.isAdult()) return BehResult.Continue;
        if (!pActor.HasSpecificClan()) return BehResult.Continue;
        var sc = pActor.GetSpecificClan();
        var pc = pActor.GetPersonalIdentity();
        if (sc == null || pc==null) return BehResult.Continue;
        if (sc.GetChildren(pc).Any(c => c.Item2.CanHeir(pc))) return BehResult.Continue;
        var lover = pActor.lover;
        //无子嗣强制生育
        BabyMaker.makeBaby(pActor, lover);
        LogService.LogInfo("判断当前无子嗣，触发强制生育");
        return BehResult.Continue;
    }
}