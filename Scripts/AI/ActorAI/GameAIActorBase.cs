using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.ActorAI;

public abstract class GameAIActorBase: BehaviourActionActor
{
    public abstract Type OriginalBeh { get; }
}