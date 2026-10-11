using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 朝堂博弈的年度结算：政治资本入账、圣眷与怨望回落、皇帝定方略、各派各出一招、怨望过高者兵谏，
// 以及内阁的任免与吏治。
public static partial class CourtSystem
{
    private const float ImpeachCost = 20f;
    private const float ObstructCost = 12f;
    private const float FavorCost = 10f;
    private const float AllyCost = 8f;
    private const float RecruitCost = 15f;

    private static void YearlyIfDue(Empire empire, CourtState state, double now)
    {
        if (state.last_year >= 0d && now >= state.last_year && Date.getYearsSince(state.last_year) < 1) return;
        bool first = state.last_year < 0d;
        state.last_year = now;
        List<FixedFaction> factions = Factions(empire);
        var ids = new HashSet<string>(factions.Select(faction => faction.GetID()));
        foreach (string stale in state.factions.Keys.Where(id => !ids.Contains(id)).ToList()) state.factions.Remove(stale);
        foreach (FixedFaction faction in factions) FactionState(empire, faction);
        if (first) return;

        List<Actor> council = Council(empire);
        foreach (FixedFaction faction in factions) Income(empire, faction, council);
        UpdateStrategy(empire, state, factions);
        if (CouncilKind(empire) == CourtCouncilKind.Cabinet) ReshuffleCabinet(empire, state);
        foreach (FixedFaction faction in factions.OrderBy(_ => Random.value))
            TakeAction(empire, state, faction, factions, Council(empire));
        Escalate(empire, state, factions);
        if (CouncilKind(empire) == CourtCouncilKind.Cabinet) CabinetAdministration(empire);
    }

    #region 资本、圣眷、怨望

    private static void Income(Empire empire, FixedFaction faction, List<Actor> council)
    {
        CourtFactionState data = FactionState(empire, faction);
        int seats = council.Count(actor => actor.GetFaction() == faction);
        int officials = faction.AllMembers.Count(actor => actor != null && !actor.isRekt() && actor.IsOnOffice());
        float income = 3f + seats * 3f + Mathf.Min(8f, officials * 0.5f) + faction.CentralRatio / 10f +
                       Mathf.Clamp(faction.ClassPower / 20f, 0f, 3f);
        data.capital = Mathf.Min(100f, data.capital + income);
        data.favor += (50f - data.favor) * 0.1f;
        data.grievance = Mathf.Max(0f, data.grievance - (data.favor >= 60f ? 8f : 4f));
        data.obstruction = 0f;
        data.last_action = "";
        data.last_action_detail = "";
        if (string.IsNullOrEmpty(data.ally)) return;
        FixedFaction partner = FindFaction(empire, data.ally);
        if (partner == null || partner.CentralRatio > 55 || faction.CentralRatio > 55)
        {
            if (partner != null) FactionState(empire, partner).ally = "";
            data.ally = "";
        }
    }

    // 皇帝方略：一派独大则制衡；威权不足又有宠信的派系则偏宠；有派系怨望过高而皇帝威权足则打压；否则亲政
    private static void UpdateStrategy(Empire empire, CourtState state, List<FixedFaction> factions)
    {
        (Actor _, string role) = Decider(empire);
        string strategy = "personal", target = "";
        if (role != "emperor" || factions.Count == 0)
        {
            strategy = "regency";
        }
        else
        {
            int legitimacy = empire.Legitimacy;
            FixedFaction top = factions.OrderByDescending(faction => faction.CentralRatio).First();
            FixedFaction darling = factions.Where(faction => FactionState(empire, faction).favor >= 75f)
                .OrderByDescending(faction => FactionState(empire, faction).favor).FirstOrDefault();
            FixedFaction angry = factions.Where(faction => FactionState(empire, faction).grievance >= 70f)
                .OrderByDescending(faction => FactionState(empire, faction).grievance).FirstOrDefault();
            if (top.CentralRatio >= 55) { strategy = "balance"; target = top.GetID(); }
            else if (darling != null && legitimacy < 60) { strategy = "favorite"; target = darling.GetID(); }
            else if (angry != null && (legitimacy >= 70 || empire.Emperor.hasTrait("paranoid")))
            { strategy = "suppress"; target = angry.GetID(); }
        }
        if (strategy != state.strategy || target != state.strategy_target)
            Log(state, "court_log_strategy_" + strategy, FindFaction(empire, target)?.Name ?? "");
        state.strategy = strategy;
        state.strategy_target = target;
        foreach (FixedFaction faction in factions)
        {
            CourtFactionState data = FactionState(empire, faction);
            bool isTarget = faction.GetID() == target;
            switch (strategy)
            {
                case "balance":
                    data.favor += isTarget ? -4f : 2f;
                    break;
                case "favorite":
                    data.favor += isTarget ? 3f : -1f;
                    break;
                case "suppress":
                    data.favor += isTarget ? -6f : 1f;
                    if (isTarget) data.grievance += 6f;
                    break;
            }
            data.favor = Mathf.Clamp(data.favor, 0f, 100f);
            data.grievance = Mathf.Clamp(data.grievance, 0f, 100f);
        }
    }

    #endregion

    #region 派系动作

    // 每派每年一招：罢朝 / 掣肘 / 弹劾 / 邀宠 / 结盟 / 拉拢，都没条件就蓄势。
    // 邀宠、结盟、拉拢要给上奏留出本钱
    private static void TakeAction(Empire empire, CourtState state, FixedFaction faction, List<FixedFaction> factions,
        List<Actor> council)
    {
        CourtFactionState data = FactionState(empire, faction);
        if (IsBoycotting(data))
        {
            Act(data, "boycott", "");
            Log(state, "court_log_boycott", faction.Name);
            return;
        }
        CourtMemorial memorial = state.memorial;
        if (memorial?.stage == 4 && memorial.stances.TryGetValue(faction.GetID(), out int stance) && stance < 0 &&
            data.capital >= ObstructCost)
        {
            data.capital -= ObstructCost;
            data.obstruction = Mathf.Min(0.6f, data.obstruction + 0.3f);
            Act(data, "obstruct", IssueName(memorial.claim));
            Log(state, "court_log_obstruct", faction.Name, IssueName(memorial.claim));
            return;
        }
        if (data.capital >= ImpeachCost && (data.grievance >= 25f || Random.value < 0.25f) &&
            TryImpeach(empire, state, faction, data, council))
            return;
        if (data.favor < 45f && data.capital >= FavorCost + ProposalCost)
        {
            data.capital -= FavorCost;
            data.favor = Mathf.Min(100f, data.favor + 8f);
            Act(data, "favor", "");
            Log(state, "court_log_favor", faction.Name);
            return;
        }
        if (string.IsNullOrEmpty(data.ally) && data.capital >= AllyCost + ProposalCost &&
            TryAlly(empire, state, faction, data, factions))
            return;
        if (data.capital >= RecruitCost + ProposalCost && Random.value < 0.6f)
        {
            data.capital -= RecruitCost;
            empire.CoreKingdom.TryIncreaseFactionRatio(faction, 3);
            Act(data, "recruit", "");
            Log(state, "court_log_recruit", faction.Name);
            return;
        }
        Act(data, "save", "");
    }

    private static void Act(CourtFactionState data, string action, string detail)
    {
        data.last_action = action;
        data.last_action_detail = detail ?? "";
    }

    // 弹劾对手：内阁制弹劾别派阁臣，其他朝廷弹劾别派的中央官员。罪证(贪腐、近年财政案)越多越容易成，失败反坐
    private static bool TryImpeach(Empire empire, CourtState state, FixedFaction faction, CourtFactionState data,
        List<Actor> council)
    {
        bool cabinet = CouncilKind(empire) == CourtCouncilKind.Cabinet;
        var targets = new List<(Actor actor, OfficeObject office)>();
        if (cabinet)
        {
            targets.AddRange(council.Select(actor => (actor, (OfficeObject)null)));
        }
        else if (empire.data.centerOffice != null)
        {
            foreach (long id in (empire.data.centerOffice.CoreOffices ?? new List<long>())
                     .Concat(empire.data.centerOffice.Divisions ?? new List<long>()))
            {
                if (!OfficeManager.Offices.TryGetValue(id, out OfficeObject office)) continue;
                Actor holder = office.GetActor();
                if (holder != null) targets.Add((holder, office));
            }
        }
        (Actor actor, OfficeObject office) target = targets
            .Where(item => item.actor?.data != null && !item.actor.isRekt() && item.actor != empire.Emperor)
            .Where(item =>
            {
                FixedFaction rival = item.actor.GetFaction();
                return rival != null && rival != faction && rival.GetID() != data.ally;
            })
            .OrderByDescending(item => (cabinet && council.FirstOrDefault() == item.actor ? 2f : 1f) *
                                       (0.5f + (float)item.actor.CalcCorruptionValue()) *
                                       Mathf.Max(5, item.actor.GetFaction().CentralRatio))
            .FirstOrDefault();
        if (target.actor == null) return false;
        FixedFaction rivalFaction = target.actor.GetFaction();
        CourtFactionState rivalData = FactionState(empire, rivalFaction);
        double crime = target.actor.GetOrCreate().last_fiscal_corruption_crime_timestamp;
        bool evidence = crime >= 0d && Date.getYearsSince(crime) < 5;
        float chance = 0.3f + (float)target.actor.CalcCorruptionValue() * 0.4f + (evidence ? 0.2f : 0f) +
                       (data.favor - rivalData.favor) / 200f;
        data.capital -= ImpeachCost;
        string name = target.actor.getName();
        if (Random.value < Mathf.Clamp(chance, 0.05f, 0.9f))
        {
            if (cabinet) empire.RemoveCabinetMember(target.actor);
            else target.office?.RemoveActor();
            rivalData.grievance = Mathf.Min(100f, rivalData.grievance + 8f);
            rivalData.favor = Mathf.Max(0f, rivalData.favor - 4f);
            target.actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_impeached"), faction.Name));
            Act(data, "impeach", name);
            Log(state, "court_log_impeach_success", faction.Name, rivalFaction.Name, name);
        }
        else
        {
            data.favor = Mathf.Max(0f, data.favor - 8f);
            data.grievance = Mathf.Min(100f, data.grievance + 4f);
            Act(data, "impeach_failed", name);
            Log(state, "court_log_impeach_failed", faction.Name, rivalFaction.Name, name);
        }
        return true;
    }

    // 结盟：找立场相近(各阶层亲和度方向一致)的派系，都不能一家独大
    private static bool TryAlly(Empire empire, CourtState state, FixedFaction faction, CourtFactionState data,
        List<FixedFaction> factions)
    {
        if (faction.CentralRatio > 55) return false;
        FixedFaction partner = factions
            .Where(other => other != faction && other.CentralRatio <= 55 &&
                            string.IsNullOrEmpty(FactionState(empire, other).ally))
            .Select(other => (other, similarity: Similarity(faction, other)))
            .Where(item => item.similarity >= 0.35f)
            .OrderByDescending(item => item.similarity).Select(item => item.other).FirstOrDefault();
        if (partner == null) return false;
        data.capital -= AllyCost;
        data.ally = partner.GetID();
        FactionState(empire, partner).ally = faction.GetID();
        Act(data, "ally", partner.Name);
        Log(state, "court_log_ally", faction.Name, partner.Name);
        return true;
    }

    public static float Similarity(FixedFaction a, FixedFaction b)
    {
        FactionClassSystem.EnsureProfile(a);
        FactionClassSystem.EnsureProfile(b);
        float dot = 0f, na = 0f, nb = 0f;
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)))
        {
            a.ClassAffinities.TryGetValue(socialClass, out float x);
            b.ClassAffinities.TryGetValue(socialClass, out float y);
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        return na <= 0f || nb <= 0f ? 0f : dot / Mathf.Sqrt(na * nb);
    }

    // 兵谏：怨望极高、圣眷尽失、又有成员国君主做本钱的派系起兵(派系叛乱)；每年最多一起
    private static void Escalate(Empire empire, CourtState state, List<FixedFaction> factions)
    {
        Kingdom core = empire.CoreKingdom;
        if (empire.Emperor == null) return;
        foreach (FixedFaction faction in factions.OrderByDescending(faction => FactionState(empire, faction).grievance))
        {
            CourtFactionState data = FactionState(empire, faction);
            if (data.grievance < 90f || data.favor > 25f || faction.CentralRatio < 20) continue;
            Kingdom rebel = empire.kingdoms_list
                .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != core &&
                                  kingdom.king?.GetFaction() == faction && RebellionSystem.CanAttempt(kingdom) &&
                                  kingdom.capital != null && CityStabilitySystem.CanRise(kingdom.capital) &&
                                  !kingdom.IsFactionRebelling() && !kingdom.getWars().Any())
                .OrderByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();
            if (rebel == null) continue;
            RebellionCauseData cause = RebellionSystem.Capture(rebel, rebel.capital, "rebellion_reason_court", faction.Name);
            rebel.StartFactionRebelling(faction);
            War war = World.world.diplomacy.startWar(rebel, core, WarTypeLibrary.normal);
            if (war == null)
            {
                rebel.EndFactionRebelling();
                continue;
            }
            war.SetEmpireWarType(EmpireWarType.派系叛乱);
            RebellionSystem.Record(rebel, war, cause);
            data.grievance = 50f;
            Log(state, "court_log_mutiny", faction.Name, rebel.GetKingdomName());
            empire.RecordHistory(directContent: string.Format(LM.Get("court_history_mutiny"), faction.Name,
                rebel.GetKingdomName()), kingdomId: core.id);
            return;
        }
    }

    #endregion

    #region 内阁

    // 内阁由皇帝任命(幼主、傀儡时由权臣/议程官按同样规则补缺)：人数随皇帝治理能力，
    // 缺额按"才干 + 所属派系圣眷 + 方略"取人；首辅出缺由得分最高的阁臣接任。
    public static void MaintainCabinet(Empire empire)
    {
        if (empire?.CoreKingdom?.GetRegime() == null) return;
        int size = CabinetSize(empire);
        List<Actor> members = empire.GetCabinetMembers();
        foreach (Actor actor in members.Where(actor => !actor.HasOfficeIdentity()).ToList())
        {
            empire.RemoveCabinetMember(actor);
            members.Remove(actor);
        }
        while (members.Count > size)
        {
            Actor drop = members.Skip(1).OrderBy(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            if (drop == null) break;
            empire.RemoveCabinetMember(drop);
            members.Remove(drop);
        }
        while (members.Count < size)
        {
            Actor pick = CabinetCandidates(empire).Where(actor => !members.Contains(actor))
                .OrderByDescending(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            if (pick == null || !empire.AddCabinetMember(pick)) break;
            members.Add(pick);
            Log(State(empire), "court_log_appointed", pick.getName(), pick.GetFaction()?.Name ?? LM.Get("label_none"));
        }
        Actor leader = empire.GetCabinetLeader();
        if (leader == null || !members.Contains(leader))
        {
            Actor best = members.OrderByDescending(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            if (best != null && empire.SetCabinetLeader(best))
                Log(State(empire), "court_log_new_leader", best.getName(), best.GetFaction()?.Name ?? LM.Get("label_none"));
        }
    }

    // 一年一次按方略换一人：制衡时拆掉占过半阁席的派系的一席，偏宠时给宠信派系一席
    private static void ReshuffleCabinet(Empire empire, CourtState state)
    {
        List<Actor> members = empire.GetCabinetMembers();
        if (members.Count < 2) return;
        int size = members.Count;
        Actor drop = null;
        Func<Actor, bool> wanted = null;
        if (state.strategy == "balance")
        {
            IGrouping<FixedFaction, Actor> dominant = members.Where(actor => actor.GetFaction() != null)
                .GroupBy(actor => actor.GetFaction()).OrderByDescending(group => group.Count()).FirstOrDefault();
            if (dominant == null || dominant.Count() * 2 <= size) return;
            drop = dominant.Where(actor => actor != members[0])
                .OrderBy(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            wanted = actor => actor.GetFaction() != dominant.Key;
        }
        else if (state.strategy == "favorite")
        {
            if (members.Any(actor => actor.GetFaction()?.GetID() == state.strategy_target)) return;
            drop = members.Skip(1).OrderBy(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            wanted = actor => actor.GetFaction()?.GetID() == state.strategy_target;
        }
        if (drop == null || wanted == null) return;
        Actor pick = CabinetCandidates(empire).Where(actor => !members.Contains(actor) && wanted(actor))
            .OrderByDescending(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
        if (pick == null) return;
        empire.RemoveCabinetMember(drop);
        if (!empire.AddCabinetMember(pick)) return;
        Log(state, "court_log_reshuffle", drop.getName(), pick.getName(), pick.GetFaction()?.Name ?? LM.Get("label_none"));
    }

    // 阁席数随皇帝治理能力(3~15 线性对应 1~满员)
    public static int CabinetSize(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null) return 0;
        int stewardship = empire.Emperor?.stewardship ?? 0;
        if (stewardship < 3) stewardship = 0;
        if (stewardship > 15) stewardship = 15;
        return Mathf.Max(1, 1 + (stewardship * regime.cabinet_number - 1) / 15);
    }

    public static IEnumerable<Actor> CabinetCandidates(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        IEnumerable<Actor> pool = (core.GetRegime()?.GetAllFactionMembers() ?? new List<Actor>())
            .Concat(core.units?.ToList() ?? new List<Actor>());
        return pool.Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                   actor != empire.Emperor && !actor.IsWarMachine() && actor.HasOfficeIdentity() &&
                                   actor.CanServeOffice(core))
            .Distinct();
    }

    public static float Competence(Actor actor) => actor == null ? 0f : actor.stewardship + actor.intelligence;

    public static float AppointScore(Empire empire, Actor actor, List<Actor> members, int size)
    {
        CourtState state = State(empire);
        FixedFaction faction = actor.GetFaction();
        float favor = faction == null ? 50f : FactionState(empire, faction).favor;
        float score = Competence(actor) + (favor - 50f) / 5f;
        string id = faction?.GetID() ?? "";
        switch (state.strategy)
        {
            case "balance":
                if (faction != null && !members.Contains(actor) &&
                    members.Count(member => member.GetFaction() == faction) * 2 >= size)
                    score -= 8f;
                if (id == state.strategy_target) score -= 6f;
                break;
            case "favorite":
                if (id == state.strategy_target) score += 10f;
                break;
            case "suppress":
                if (id == state.strategy_target) score -= 15f;
                break;
        }
        return score;
    }

    // 内阁吏治：阁臣才干均值高于 20 时逐年压低中央腐败，低于时推高(每年最多 ±2%)
    public static float CabinetEfficiency(Empire empire)
    {
        List<Actor> members = Council(empire);
        if (members.Count == 0) return 0f;
        return Mathf.Clamp((members.Average(Competence) - 20f) / 20f, -1f, 1f);
    }

    private static void CabinetAdministration(Empire empire)
    {
        float efficiency = CabinetEfficiency(empire);
        if (Mathf.Abs(efficiency) > 0.01f) empire.CoreKingdom.AddCorruptionRate(-0.02d * efficiency);
    }

    #endregion
}
