using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 党内政治(用户 2026-11-10 "政党总感觉是空壳"，问答选「党内政治」)：
//   · 党代会：每 CongressYears 年开一次；党魁出缺、大选丢了三成以上议席也临时召开(大选失利的党魁不能连任)。
//     候选人是党内名望、政绩、官位靠前的三人，代表按党内主流与最大一翼的人数投票；当选的党魁任内不因官位高低被换
//     (FixedFaction.Update)，执政党换了党魁随即改推首相；
//   · 两翼：理念与本党不同的党员按理念聚成几群，最大的一群是"一翼"(WingIdeology / WingShare)。一翼过二成、
//     大选失利、党纪松弛，分裂的可能都变大(PartySystem.TrySplit 乘 SplitFactor)；
//   · 党纪(0~100)：威权与社会主义各党向 70 靠、别的向 50 靠，一党制的执政党向 80 靠；党纪 60 以上每年开除至多两名
//     理念与本党相距 90 以上的党员；
//   · 一党制的执政党：每年办党校(花党产 50，党纪 +5)；一翼过三成就清洗(开除这一翼名望最低的至多五人，党纪 +10)。
public static class PartyInnerSystem
{
    public const int CongressYears = 5;
    private const float DefeatSeatLoss = 0.3f;
    private const float ExpelDistance = 90f;
    private const int ExpelPerYear = 2, PurgeMax = 5;
    private const float PartySchoolCost = 50f;

    public static void Yearly(Empire empire)
    {
        if (!PartySystem.IsActive(empire)) return;
        FixedFaction leading = RepublicSystem.IsOneParty(empire) ? PartySystem.GetGovernmentParty(empire) : null;
        foreach (FixedFaction party in PartySystem.GetParties(empire).Where(party => !party.Ban).ToList())
        {
            UpdateWings(party);
            UpdateDiscipline(party, party == leading);
            if (party == leading)
            {
                PartySchool(party);
                Purge(empire, party);
            }
            Expel(empire, party);
            if (NeedsCongress(party)) HoldCongress(empire, party, defeat: false);
        }
    }

    // 大选后(PartySystem.AfterElection)：记下各党丢了几成议席，丢三成以上的临时开党代会追责
    public static void AfterElection(Empire empire, Dictionary<FixedFaction, int> allocation, Dictionary<string, int> previous)
    {
        if (!PartySystem.IsActive(empire) || previous == null) return;
        foreach (FixedFaction party in PartySystem.GetParties(empire).Where(party => !party.Ban).ToList())
        {
            int before = previous.TryGetValue(party.GetID(), out int seats) ? seats : 0;
            int after = allocation != null && allocation.TryGetValue(party, out int now) ? now : 0;
            party.LastSeatLoss = before > 0 ? Mathf.Clamp01((before - after) / (float)before) : 0f;
            if (party.LastSeatLoss >= DefeatSeatLoss && party.AllMembers.Count > 1) HoldCongress(empire, party, defeat: true);
        }
    }

    // 分裂倍数(PartySystem.TrySplit)：一翼过二成的部分 ×3、大选丢议席 ×1.5，党纪高于 50 的部分减
    public static float SplitFactor(FixedFaction party) =>
        party == null ? 1f : Mathf.Max(0.2f, 1f + 3f * Mathf.Max(0f, party.WingShare - 0.2f) + 1.5f * party.LastSeatLoss -
                                             (party.Discipline - 50f) / 100f);

    #region 两翼与党纪

    private static List<Actor> LivingMembers(FixedFaction party) =>
        party.AllMembers.Where(actor => actor != null && !actor.isRekt() && actor.isAlive()).ToList();

    private static void UpdateWings(FixedFaction party)
    {
        List<Actor> members = LivingMembers(party);
        IGrouping<PartyIdeology, Actor> wing = members.Where(actor => IdeologyPopulationSystem.Get(actor) != party.Ideology)
            .GroupBy(IdeologyPopulationSystem.Get).OrderByDescending(group => group.Count()).FirstOrDefault();
        party.WingIdeology = wing?.Key.ToString() ?? "";
        party.WingShare = wing == null || members.Count == 0 ? 0f : wing.Count() / (float)members.Count;
    }

    public static float DisciplineTarget(FixedFaction party, bool leading) =>
        leading ? 80f : IdeologyFamilies.IsAuthoritarian(party.Ideology) || IdeologyFamilies.IsSocialist(party.Ideology) ? 70f : 50f;

    private static void UpdateDiscipline(FixedFaction party, bool leading) =>
        party.Discipline = Mathf.Clamp(party.Discipline + (DisciplineTarget(party, leading) - party.Discipline) * 0.2f, 0f, 100f);

    private static void Expel(Empire empire, FixedFaction party)
    {
        if (party.Discipline < 60f) return;
        Actor leader = party.GetLeader();
        List<Actor> expelled = LivingMembers(party)
            .Where(actor => actor != leader && PartySystem.IdeologyDistance(IdeologyPopulationSystem.Get(actor), party.Ideology) >= ExpelDistance)
            .OrderBy(actor => actor.data?.renown ?? 0).Take(ExpelPerYear).ToList();
        foreach (Actor actor in expelled) actor.RemoveFaction();
        if (expelled.Count > 0)
            Record(empire, string.Format(LM.Get("party_expel_history"), party.Name, string.Join("、", expelled.Select(actor => actor.getName()))),
                expelled[0]);
    }

    private static void PartySchool(FixedFaction party)
    {
        if (party.PartyFunds < PartySchoolCost) return;
        party.PartyFunds -= PartySchoolCost;
        party.Discipline = Mathf.Min(100f, party.Discipline + 5f);
    }

    private static void Purge(Empire empire, FixedFaction party)
    {
        if (party.WingShare < 0.3f || !Enum.TryParse(party.WingIdeology, out PartyIdeology wing)) return;
        Actor leader = party.GetLeader();
        List<Actor> purged = LivingMembers(party)
            .Where(actor => actor != leader && IdeologyPopulationSystem.Get(actor) == wing)
            .OrderBy(actor => actor.data?.renown ?? 0).Take(PurgeMax).ToList();
        if (purged.Count == 0) return;
        foreach (Actor actor in purged) actor.RemoveFaction();
        party.Discipline = Mathf.Min(100f, party.Discipline + 10f);
        UpdateWings(party);
        Record(empire, string.Format(LM.Get("party_purge_history"), party.Name, PartySystem.GetIdeologyName(wing), purged.Count), leader);
    }

    #endregion

    #region 党代会

    private static bool NeedsCongress(FixedFaction party)
    {
        if (LivingMembers(party).Count == 0) return false;
        Actor leader = party.GetLeader();
        if (leader == null || party.ElectedLeaderId != leader.id) return true;
        return party.LeaderElectedAt < 0d || Date.getYearsSince(party.LeaderElectedAt) >= CongressYears;
    }

    // 候选人分：名望 + 政绩/5 + 在任官职 100
    private static float Score(Actor actor) =>
        (actor.data?.renown ?? 0) + (float)((actor.GetIdentity()?.TotalPerformance ?? 0d) / 5d) + (actor.IsOnOffice() ? 100f : 0f);

    public static void HoldCongress(Empire empire, FixedFaction party, bool defeat)
    {
        Actor incumbent = party.GetLeader();
        List<Actor> candidates = LivingMembers(party)
            .Where(actor => actor.isAdult() && (!defeat || actor != incumbent))
            .OrderByDescending(Score).Take(3).ToList();
        if (candidates.Count == 0) return;
        bool hasWing = Enum.TryParse(party.WingIdeology, out PartyIdeology wing) && party.WingShare > 0f;
        Actor winner = null;
        float best = float.MinValue;
        foreach (Actor candidate in candidates)
        {
            bool wingMan = hasWing && IdeologyPopulationSystem.Get(candidate) == wing;
            float delegates = wingMan ? party.WingShare : 1f - party.WingShare;
            float votes = (Score(candidate) + 50f) * delegates * Random.Range(0.8f, 1.2f);
            if (votes <= best) continue;
            best = votes;
            winner = candidate;
        }
        if (winner == null) return;
        bool changed = incumbent != winner;
        party.SetLeader(winner);
        party.ElectedLeaderId = winner.id;
        party.LeaderElectedAt = World.world?.getCurWorldTime() ?? 0d;
        FactionEditor.FollowLeaderName(party);
        string key = defeat && incumbent != null ? "party_congress_defeat_history"
            : changed ? "party_congress_new_history" : "party_congress_kept_history";
        Record(empire, string.Format(LM.Get(key), party.Name, winner.getName(), incumbent?.getName() ?? ""), winner);
        // 执政党换了党魁：改推首相
        if (changed && ParliamentSystem.HasParliament(empire) &&
            empire.data?.constitutional_economy?.prime_minister_faction_id == party.GetID())
            ParliamentSystem.ReelectGovernment(empire);
    }

    #endregion

    private static void Record(Empire empire, string text, Actor actor) =>
        EventRecorder.RecordRoutine(empire, $"{empire.GetEmpireFullName()}：{text}", actor);
}
