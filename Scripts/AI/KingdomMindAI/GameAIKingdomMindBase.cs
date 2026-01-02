using System;

namespace EmpireCraft.Scripts.AI.KingdomMindAI;

public abstract class GameAIKingdomMindBase: BehaviourActionKingdom
{
    public abstract Type OriginalBeh { get; }
}