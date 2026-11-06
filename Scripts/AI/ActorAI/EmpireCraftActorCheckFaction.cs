using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckFaction : GameAIActorBase
{
    private static readonly Random _rng = new Random();

    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        TryJoin(pActor);
        return BehResult.Continue;
    }

    // 年度补录：在任官员入派以前只靠人物自己的 AI(本行为)，可这套 AI 只在无小人模式的闲居者身上按时跑，
    // 城主、成员国君主、有小人模式下的官员几乎轮不到，派系里常年只有几个人。每年由帝国把还没入派的在任官员
    // 照同样的规矩过一遍，每帝国每年至多 MaxEnrollPerYear 人
    private const int MaxEnrollPerYear = 12;
    private static readonly Dictionary<long, double> LastEnroll = new();
    private static object _enrollWorld;

    public static int EnrollOfficials(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived() || World.world == null) return 0;
        if (!ReferenceEquals(_enrollWorld, World.world))
        {
            _enrollWorld = World.world;
            LastEnroll.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (LastEnroll.TryGetValue(empire.getID(), out double last) && now >= last && Date.getYearsSince(last) < 1) return 0;
        LastEnroll[empire.getID()] = now;
        int joined = 0;
        foreach (Actor actor in empire.getUnits()
                     .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && !actor.HasFaction() &&
                                     actor.IsOnOffice())
                     .OrderByDescending(actor => actor.renown).ToList())
        {
            if (joined >= MaxEnrollPerYear) break;
            if (TryJoin(actor)) joined++;
        }
        return joined;
    }

    public static bool TryJoin(Actor pActor)
    {
        if (pActor == null) return false;

        if (!pActor.isAdult()) return false;
        if (pActor.HasFaction()) return false;
        if (!pActor.IsOnOffice()) return false;
        if (!pActor.HasOfficeIdentity()) return false;
        if (pActor.IsEmperor()) return false;

        Kingdom pKingdom = pActor.kingdom;

        if (pKingdom == null) return false;
        if (pKingdom.isRekt()) return false;

        Empire empire = pKingdom.GetEmpire();

        if (empire == null)
        {
            return false;
        }

        var coreKingdom = empire.CoreKingdom;

        if (coreKingdom == null)
        {
            empire.CheckDissolve(null);
            return false;
        }

        Regime regime = coreKingdom.GetRegime();

        if (regime == null)
        {
            return false;
        }

        var factions = regime.GetPlayerFactions();

        if (factions == null || factions.Count == 0)
        {
            return false;
        }

        int kingdomFactionRatioTotal = pKingdom.GetFactionRatioTotal();

        // 如果当前国家已经存在任何派系影响力，那么官员必须加入一个已有影响力的派系
        bool mustJoinFaction = kingdomFactionRatioTotal > 0;

        var ps = new List<(FixedFaction fac, double p)>(factions.Count);

        foreach (var f in factions)
        {
            // 被禁的派系(党禁)不收人
            if (f == null || f.Ban)
            {
                continue;
            }

            int factionRatio = pKingdom.GetFactionRatioValue(f);

            if (factionRatio < 0)
            {
                factionRatio = 0;
            }

            if (factionRatio > 100)
            {
                factionRatio = 100;
            }

            // 如果国家已经有派系影响力，那么只从该国已有影响力的派系里抽
            if (mustJoinFaction && factionRatio <= 0)
            {
                continue;
            }

            double basePossibility = f.CalcPossibility(pActor);

            if (basePossibility < 0)
            {
                basePossibility = 0;
            }

            if (basePossibility > 1)
            {
                basePossibility = 1;
            }

            double factionInfluencePossibility = factionRatio / 100.0;

            double finalPossibility = basePossibility + factionInfluencePossibility;

            if (finalPossibility < 0)
            {
                finalPossibility = 0;
            }

            if (finalPossibility > 1)
            {
                finalPossibility = 1;
            }

            ps.Add((f, finalPossibility));
        }

        if (ps.Count == 0)
        {
            return false;
        }

        double sum = ps.Sum(t => t.p);

        if (sum <= 0)
        {
            return false;
        }

        // 国家已有派系影响力时，不能有“不加入”的概率
        // 国家没有派系影响力时，才允许不加入
        double noneWeight = mustJoinFaction ? 0.0 : Math.Max(0.0, 1.0 - sum);

        double r = _rng.NextDouble() * (sum + noneWeight);
        double acc = 0.0;

        FixedFaction chosen = null;

        foreach (var t in ps)
        {
            acc += t.p;

            if (r <= acc)
            {
                chosen = t.fac;
                break;
            }
        }

        if (chosen != null)
        {
            pActor.SetFaction(chosen);
            // TranslateHelper.LogOfficerJoinFaction(pActor.GetOffice(), pActor, chosen);
        }

        return chosen != null;
    }
}