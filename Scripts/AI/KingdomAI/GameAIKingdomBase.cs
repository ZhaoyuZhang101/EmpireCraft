using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public abstract class GameAIKingdomBase : BehaviourActionKingdom
{
    public abstract Type OriginalBeh { get; }
}