using System;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomMindAI;

public class EmpireCraftKingdomBehCheckCentreMind: GameAIKingdomMindBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.hasKing()) return BehResult.Continue;
        var regime = pKingdom.GetRegime();
        var centreMind = regime?.CentreMind;
        if (centreMind == null) return BehResult.Continue;
        //todo: 國家意志檢測
        return BehResult.Continue;
    }

}