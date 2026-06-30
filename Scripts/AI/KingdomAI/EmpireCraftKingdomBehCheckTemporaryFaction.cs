using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTemporaryFaction: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        var ked = pKingdom.GetOrCreate();
        if (ked is { last_tf_check_ts: > 0 })
        {
            if (Date.getMonthsSince(ked.last_tf_check_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        //是否有对应的本地诉求进程
        var hasValidLocalProgress = false;
        if (pKingdom.IsInEmpire())
        {
            var empire = pKingdom.GetEmpire();
            var dominateFaction = pKingdom.GetHighestFactionRatio();
            if (dominateFaction != null && empire!=null)
            {
                var validLocalFactions = dominateFaction.TemporaryFactions.FindAll(tf => tf.CheckLocalCondition(pKingdom));
                if (validLocalFactions.Count > 0)
                {
                    var factionNeedToBePush = validLocalFactions.GetRandom();
                    if (factionNeedToBePush != null)
                    {
                        pKingdom.PushProgress(factionNeedToBePush);
                        hasValidLocalProgress = true;
                    }
                }
            }
        }

        if (!hasValidLocalProgress)
        {
            CheckTf(pKingdom);
        }
        if (ked != null) ked.last_tf_check_ts = World.world.getCurWorldTime();
        return BehResult.Continue;
    }

    public static void CheckTf(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return;
        var empire = pKingdom.GetEmpire();
        if (empire==null) return;
        Regime regime = pKingdom.GetRegime();
        var factions = regime.GetPlayerFactions();
        var empireId = empire.getID();
        for (int i = 0; i < factions.Count; i++)
        {
            factions[i].EmpireId = empireId;
        }
        FixedFaction dominateFaction = regime.GetDominateFaction();
        if (dominateFaction == null) return;
        for (int i = 0; i < factions.Count; i++)
        {
            var ff = factions[i];
            if (ff == dominateFaction) continue;
            var tfs = ff.TemporaryFactions;
            for (int j = 0; j < tfs.Count; j++)
            {
                tfs[j].End();
            }
        }

        var run = dominateFaction.GetAnyTFactionRuns();
        if (run != null)
        {
            if (run.canBePushByLocal)
            {
                run?.End();
                return;
            }
            if (run?.ShowAsPlot??false)
            {
                if ((pKingdom.king?.plot?.name ?? "") != (run?.type.ToString() ?? "-"))
                {
                    run?.End();
                    return;
                }
            }
        }
        if (dominateFaction.IsAnyTFactionRuns()) return;
        if (dominateFaction.GetLeader() == null && regime.type!= RegimeType.Feudalism) return;
        if (regime.has_cabinet)
        {
            if (regime.type != RegimeType.Feudalism)
            {
                if (pKingdom.GetEmpire().GetCabinetLeader()?.GetFaction() != dominateFaction) return;
            }
        }
        var shuffledTf = dominateFaction.TemporaryFactions.ToList();
        shuffledTf.Shuffle();
        for (int i = 0; i < shuffledTf.Count; i++)
        {
            var tf = shuffledTf[i];
            if (tf.CheckCondition())
            {
                tf.Start();
                return;
            }
        }
        return;
    }
}
