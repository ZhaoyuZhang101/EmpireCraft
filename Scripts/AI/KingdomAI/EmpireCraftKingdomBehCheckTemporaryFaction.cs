using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.GeneralSystems;
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
            if (factions[i] != null)
            {
                factions[i].EmpireId = empireId;
                factions[i].FixMissedTemporaryFactions();
            }
        }
        FixedFaction dominateFaction = regime.GetDominateFaction();
        bool court = CourtSystem.Applies(empire);
        for (int i = 0; i < factions.Count; i++)
        {
            var ff = factions[i];
            if (ff?.TemporaryFactions == null || (!court && ff == dominateFaction)) continue;
            var tfs = ff.TemporaryFactions;
            for (int j = 0; j < tfs.Count; j++)
            {
                var tf = tfs[j];
                if (tf == null) continue;
                if (tf.IsLocallyPushed && tf.Active && tf.CheckLocalContinue(tf.GetKingdom())) continue;
                if (tf.IsStarted() && tf.CanProceedOutsideGovernment && !ff.Ban && tf.CheckContinue()) continue;
                // 朝堂模式：皇帝准奏的奏议、挑战皇权的直接行动照常进行；其余未经朝议开跑的一律叫停
                if (court && tf.IsStarted() && (CourtSystem.IsMemorialClaim(empire, tf) ||
                                                CourtIssues.IsDirectAction(tf.type) && ff == dominateFaction)) continue;
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
        // 朝堂模式：奏议的上奏、票拟、廷议、批红、施行与年度博弈都在这里推进
        if (court) CourtSystem.Tick(empire);
        if (run?.IsStarted() == true) return;
        TemporaryPushProgress.TrySubmitReadyRequests(empire);
        if (empire.RunningTemporaryFaction?.IsStarted() == true) return;
        ClaimAgendaContext context = ClaimAgendaSystem.BuildContext(empire);
        if (context?.PartyOpening?.HasChallenge == true)
        {
            // Opening party politics is a popular reform campaign, not ordinary
            // cabinet business. An opposition faction may represent that pressure.
            var opening = factions.Where(f => f != null && !f.Ban && f.TemporaryFactions != null)
                .SelectMany(f => f.TemporaryFactions.Select(tf => (faction: f, claim: tf)))
                .Where(entry => entry.claim?.type == TemporaryFactionType.开放党禁 &&
                                entry.claim.Active && entry.claim.CountDown <= 0)
                .Select(entry => (entry.claim, view: ClaimAgendaSystem.Evaluate(context, entry.faction, entry.claim)))
                .Where(entry => entry.view.CanPropose)
                .OrderByDescending(entry => entry.view.Score)
                .Select(entry => entry.claim).FirstOrDefault();
            if (opening != null && opening.CheckCondition() && opening.CheckTarget())
            {
                opening.ShowAsPlot = false;
                opening.pusherType = MetaType.None;
                opening.Start();
                return;
            }
        }
        if (court)
        {
            // 挑战皇权的诉求(索取皇位)不经朝议，仍由最大派系直接发动
            TryStartDirectAction(empire, dominateFaction);
            return;
        }
        if (dominateFaction == null || dominateFaction.Ban || dominateFaction.TemporaryFactions == null) return;
        if (dominateFaction.GetLeader() == null && regime.type!= RegimeType.Feudalism) return;
        if (ParliamentSystem.HasParliament(pKingdom.GetEmpire()))
        {
            // 责任政府下只有执政一方(联合政府时为整个执政联盟)可以发起诉求
            if (ParliamentSystem.HasResponsibleGovernment(pKingdom.GetEmpire()) &&
                !ParliamentSystem.IsGoverningFaction(pKingdom.GetEmpire(), dominateFaction.GetID())) return;
        }
        else if (regime.has_cabinet)
        {
            if (regime.type != RegimeType.Feudalism)
            {
                if (pKingdom.GetEmpire().GetCabinetLeader()?.GetFaction() != dominateFaction) return;
            }
        }
        var agenda = dominateFaction.TemporaryFactions
            .Where(tf => tf != null && tf.Active && tf.CountDown <= 0)
            .Select(tf => (claim: tf, view: ClaimAgendaSystem.Evaluate(context, dominateFaction, tf)))
            .Where(entry => entry.view.CanPropose)
            .OrderByDescending(entry => entry.view.Score)
            .ThenBy(entry => entry.claim.type)
            .ToList();
        for (int i = 0; i < agenda.Count; i++)
        {
            var tf = agenda[i].claim;
            if (tf.CheckCondition() && tf.CheckTarget())
            {
                tf.pusherType = MetaType.None;
                tf.Start();
                return;
            }
        }
        return;
    }

    private static void TryStartDirectAction(Empire empire, FixedFaction dominateFaction)
    {
        if (empire.RunningTemporaryFaction?.IsStarted() == true || CourtSystem.State(empire)?.memorial != null) return;
        if (dominateFaction?.TemporaryFactions == null || dominateFaction.Ban) return;
        if (dominateFaction.GetLeader() == null && empire.CoreKingdom.GetRegime()?.type != RegimeType.Feudalism) return;
        ClaimAgendaContext context = ClaimAgendaSystem.BuildContext(empire);
        foreach (var tf in dominateFaction.TemporaryFactions)
        {
            if (tf == null || !tf.Active || tf.CountDown > 0 || !CourtIssues.IsDirectAction(tf.type)) continue;
            if (!ClaimAgendaSystem.Evaluate(context, dominateFaction, tf).CanPropose) continue;
            if (!tf.CheckCondition() || !tf.CheckTarget()) continue;
            tf.pusherType = MetaType.None;
            tf.Start();
            return;
        }
    }
}
