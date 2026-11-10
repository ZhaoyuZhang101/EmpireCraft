using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 兴兵讨伐(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)：点名讨伐一个邻国——占着本朝法理之地的、有宿怨的优先，兵力不能弱于对方八成；
// 有法理宣称的定"争法理"，否则"割地"，目标定死。武官、军人赞成，农民、商人反对。五年一次
public class TempFac_兴兵讨伐 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_兴兵讨伐();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 5;

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core == null || !OffCooldown() || WarSituation.TooBusyForWar(core) || CorpsSystem.CannotFundWar(core)) return false;
        float ours = (float)WarSituation.Power(empire);
        Kingdom best = null;
        float bestScore = 0f;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == core || kingdom.GetEmpire() == empire) continue;
            if (!(empire.IsNeighbourWith(kingdom) || StraitReach.Across(core, kingdom))) continue;
            if (kingdom.isInWarWith(core) || EmpireCraft.Scripts.Compatibility.NuclearDoctrineSystem.IsDeterred(core, kingdom) ||
                !WarGate.CanDeclare(core, kingdom)) continue;
            float theirs = (float)WarSituation.Power(kingdom);
            if (ours < theirs * 0.8f) continue;
            float score = (DeJureAgenda.HasClaimOn(core, kingdom) ? 30f : 0f) + Mathf.Max(0, -DiplomacyActions.HistoryOpinion(core, kingdom)) +
                          5f * ours / Mathf.Max(1f, theirs);
            if (score <= bestScore) continue;
            best = kingdom;
            bestScore = score;
        }
        if (best == null) return false;
        SetKingdomTarget(best);
        return true;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{type}");
        Kingdom core = GetEmpire()?.CoreKingdom;
        Kingdom target = GetKingdomTarget();
        if (core != null && target?.data != null && !target.isRekt() && WarGate.CanDeclare(core, target))
        {
            War war = World.world.diplomacy.startWar(core, target, WarTypeLibrary.normal);
            if (war != null)
            {
                war.SetEmpireWarType(EmpireWarType.帝国扩张);
                WarGoalSystem.Preset(war, DeJureAgenda.HasClaimOn(core, target) ? WarGoal.争法理 : WarGoal.割地);
                war.GetOrCreate().war_goal_locked = true;
                CompletionOutcome = string.Format(LM.Get("claim_outcome_兴兵讨伐_done"), target.GetKingdomName());
            }
        }
        End();
    }
}
