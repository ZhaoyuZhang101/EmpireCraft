using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.KingdomMindAI;

public class EmpireCraftKingdomBehCheckTemporaryFaction: GameAIKingdomMindBase
{
    public override Type OriginalBeh { get; }
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (pKingdom.isRekt()) return BehResult.Continue;
        if (!pKingdom.IsEmpire())  return BehResult.Continue;
        Regime regime = pKingdom.GetRegime();
        if (regime==null)  return BehResult.Continue;
        var ff = regime.GetDominateFaction();
        if (ff==null)  return BehResult.Continue;
        foreach (var tf in ff.TemporaryFactions)
        {
            tf.SetEmpire(pKingdom.GetEmpire());
            if (tf.IsNeedToCountDown())
            {
                if (tf.CountDown > 0)
                {
                    tf.CountDown -= 1;
                }
            }
            if (tf.IsStarted())
            {
                tf.CheckNeedToUpdate();
            }
        }
        return BehResult.Continue;
    }
}