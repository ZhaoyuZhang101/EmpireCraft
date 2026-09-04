using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTemporaryFaction: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        var ked = pKingdom.GetOrCreate();
        if (ked != null && ked.last_tf_check_ts > 0)
        {
            if (Date.getMonthsSince(ked.last_tf_check_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        CheckTf(pKingdom);
        if (ked != null) ked.last_tf_check_ts = World.world.getCurWorldTime();
        return BehResult.Continue;
    }

    public static void CheckTf(Kingdom pKingdom)
    {
        if (pKingdom == null || pKingdom.isRekt()) return;
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return;
        var empire = pKingdom.GetEmpire();
        if (empire==null) return;
        Regime regime = pKingdom.GetRegime();
        var factions = regime?.GetPlayerFactions();
        if (factions == null) return;
        var empireId = empire.getID();
        for (int i = 0; i < factions.Count; i++)
        {
            if (factions[i] != null) factions[i].EmpireId = empireId;
        }
        FixedFaction dominateFaction = regime.GetDominateFaction();
        for (int i = 0; i < factions.Count; i++)
        {
            var ff = factions[i];
            if (ff?.TemporaryFactions == null || ff == dominateFaction) continue;
            var tfs = ff.TemporaryFactions;
            for (int j = 0; j < tfs.Count; j++)
            {
                var tf = tfs[j];
                if (tf == null) continue;
                if (tf.IsLocallyPushed && tf.Active && tf.CheckLocalContinue(tf.GetKingdom())) continue;
                if (tf.IsStarted()) tf.End();
            }
        }

        var run = empire.RunningTemporaryFaction;
        if (run?.IsStarted() != true)
            run = factions.Where(f => f?.TemporaryFactions != null).SelectMany(f => f.TemporaryFactions)
                .FirstOrDefault(tf => tf?.IsStarted() == true);
        empire.RunningTemporaryFaction = run;
        if (run != null && (!run.Active || (run.IsLocallyPushed && !run.CheckLocalContinue(run.GetKingdom()))))
        {
            run.End();
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
        if (run?.IsStarted() == true) return;
        TemporaryPushProgress.TrySubmitReadyRequests(empire);
        if (empire.RunningTemporaryFaction?.IsStarted() == true) return;
        if (dominateFaction == null || dominateFaction.Ban || dominateFaction.TemporaryFactions == null) return;
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
            if (tf != null && tf.Active && tf.CountDown <= 0 && tf.CheckCondition() && tf.CheckTarget())
            {
                tf.pusherType = MetaType.None;
                tf.Start();
                return;
            }
        }
        return;
    }
}
