using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 地方政治：选举制国家(开放党禁、有议会、多党制)的各行政区有自己的政治倾向与地方选举。
//   · 省级政治倾向：按住在本省的选民算出各党支持率(与全国大选同一套计票)，玩家可以资助/打压某党；
//   · 地方选举：每届议会任期过半时办一次，各省得票最多的党执政，由该党在本省的党员出任省长；
//   · 地方抗衡：在野党执政的省份抵制中央——城市忠诚下降、执政党的施政议程推进变慢；
//     在野党连续两次地方选举控制六成以上行政区(按人口)，单一制国家被迫修宪改行联邦制。
// 帝国核心国(首都所在行政区)由中央直辖，不办地方选举。每年由 ConstitutionalEconomySystem 调用。
public static class ProvincialPoliticsSystem
{
    public const int OppositionLoyalty = -5;
    public const int GoverningLoyalty = 3;
    private const float InfluenceStep = 0.1f;
    private const float MaxInfluence = 0.5f;
    private const float InfluenceDecay = 0.8f;
    private const float FederalPressureShare = 0.6f;

    #region 查询

    public static bool HasLocalElections(Empire empire) =>
        empire?.CoreKingdom != null && PartySystem.IsActive(empire) && ParliamentSystem.HasParliament(empire) &&
        !RepublicSystem.IsOneParty(empire) && !PartyBanSystem.UsesDemocraticCentralism(empire) &&
        !RepublicSystem.IsTransitioning(empire);

    public static bool IsProvince(Empire empire, Kingdom kingdom) =>
        empire != null && kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
        kingdom.GetEmpire() == empire;

    private static ConstitutionalEconomyState State(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return null;
        state.provinces ??= new Dictionary<long, ProvincePolitics>();
        return state;
    }

    public static ProvincePolitics Get(Empire empire, Kingdom province)
    {
        ConstitutionalEconomyState state = State(empire);
        return state != null && province != null && state.provinces.TryGetValue(province.id, out ProvincePolitics data)
            ? data
            : null;
    }

    private static ProvincePolitics GetOrCreate(ConstitutionalEconomyState state, Kingdom province)
    {
        if (!state.provinces.TryGetValue(province.id, out ProvincePolitics data) || data == null)
            state.provinces[province.id] = data = new ProvincePolitics();
        data.vote_shares ??= new Dictionary<string, float>();
        data.influence ??= new Dictionary<string, float>();
        data.governing_party_id ??= "";
        return data;
    }

    // 本省地方选举选出的执政党；没有地方选举返回 null
    public static FixedFaction GetGoverningParty(Empire empire, Kingdom province)
    {
        if (!HasLocalElections(empire)) return null;
        string id = Get(empire, province)?.governing_party_id;
        return string.IsNullOrEmpty(id) ? null : FindParty(empire, id);
    }

    // 本省是否由在野党执政(地方抗衡)
    public static bool IsOppositionHeld(Empire empire, Kingdom province)
    {
        FixedFaction party = GetGoverningParty(empire, province);
        return party != null && !ParliamentSystem.GetCoalitionIds(empire).Contains(party.GetID());
    }

    // 各党在本省的支持率(0~1)，按支持率从高到低
    public static List<(FixedFaction party, float share)> GetShares(Empire empire, Kingdom province)
    {
        ProvincePolitics data = Get(empire, province);
        if (data == null || data.vote_shares == null || data.vote_shares.Count == 0)
            return ComputeShares(empire, province, data);
        return data.vote_shares
            .Select(pair => (party: FindParty(empire, pair.Key), share: pair.Value))
            .Where(item => item.party != null).OrderByDescending(item => item.share).ToList();
    }

    public static float GetInfluence(Empire empire, Kingdom province, FixedFaction party)
    {
        ProvincePolitics data = Get(empire, province);
        return data?.influence != null && party != null && data.influence.TryGetValue(party.GetID(), out float value)
            ? value
            : 0f;
    }

    // 执政联盟控制的行政区占比(按人口)；没办过地方选举返回 1
    public static float CoalitionProvinceShare(Empire empire)
    {
        if (!HasLocalElections(empire)) return 1f;
        var coalition = new HashSet<string>(ParliamentSystem.GetCoalitionIds(empire));
        float total = 0f, held = 0f;
        foreach (Kingdom province in Provinces(empire))
        {
            string party = Get(empire, province)?.governing_party_id;
            if (string.IsNullOrEmpty(party)) continue;
            float population = Mathf.Max(1, province.getPopulationPeople());
            total += population;
            if (coalition.Contains(party)) held += population;
        }
        return total <= 0f ? 1f : held / total;
    }

    // 下次地方选举还有几年(-1 = 不办地方选举)
    public static int YearsUntilLocalElection(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !HasLocalElections(empire) || state.last_parliament_election < 0d) return -1;
        int term = ParliamentSystem.TermYears(empire);
        int midterm = Math.Max(1, term / 2);
        int since = Date.getYearsSince(state.last_parliament_election);
        return state.last_local_election_term == state.parliament_term
            ? Math.Max(0, term - since + midterm)
            : Math.Max(0, midterm - since);
    }

    private static IEnumerable<Kingdom> Provinces(Empire empire) =>
        empire.kingdoms_hashset.Where(kingdom => IsProvince(empire, kingdom)).ToList();

    private static FixedFaction FindParty(Empire empire, string id) =>
        PartySystem.GetParties(empire).FirstOrDefault(party => party != null && party.GetID() == id);

    private static List<(FixedFaction party, float share)> ComputeShares(Empire empire, Kingdom province,
        ProvincePolitics data)
    {
        List<FixedFaction> parties = PartySystem.GetParties(empire)
            .Where(party => party != null && !party.Ban && PartyBanSystem.IsAllowedParty(empire, party)).ToList();
        if (parties.Count == 0) return new List<(FixedFaction, float)>();
        Dictionary<FixedFaction, float> votes = PartySystem.CountProvinceVotes(empire, province, parties);
        foreach (FixedFaction party in parties)
        {
            float influence = data?.influence != null && data.influence.TryGetValue(party.GetID(), out float value)
                ? value
                : 0f;
            votes[party] = Mathf.Max(0f, votes[party] * (1f + influence));
        }
        float total = votes.Values.Sum();
        return parties.Select(party => (party, share: total <= 0f ? 0f : votes[party] / total))
            .OrderByDescending(item => item.share).ToList();
    }

    #endregion

    #region 年度结算与地方选举

    public static void Update(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || World.world == null) return;
        if (state.last_local_review >= 0d && Date.getYearsSince(state.last_local_review) < 1) return;
        state.last_local_review = World.world.getCurWorldTime();

        // 不再属于本国的行政区不再记录；资助/打压的影响逐年回落
        var current = new HashSet<long>(Provinces(empire).Select(province => province.id));
        foreach (long id in state.provinces.Keys.Where(id => !current.Contains(id)).ToList()) state.provinces.Remove(id);
        foreach (ProvincePolitics data in state.provinces.Values.Where(data => data?.influence != null))
            foreach (string party in data.influence.Keys.ToList())
            {
                float value = data.influence[party] * InfluenceDecay;
                if (Mathf.Abs(value) < 0.02f) data.influence.Remove(party);
                else data.influence[party] = value;
            }

        if (!HasLocalElections(empire))
        {
            foreach (ProvincePolitics data in state.provinces.Values.Where(data => data != null))
                data.governing_party_id = "";
            return;
        }
        if (state.last_parliament_election < 0d || state.last_local_election_term == state.parliament_term) return;
        int midterm = Math.Max(1, ParliamentSystem.TermYears(empire) / 2);
        // 第一次开放地方选举时立刻办一次，之后每届任期过半办一次
        bool first = state.provinces.Values.All(data => string.IsNullOrEmpty(data?.governing_party_id));
        if (!first && Date.getYearsSince(state.last_parliament_election) < midterm) return;
        HoldLocalElections(empire, state);
    }

    private static void HoldLocalElections(Empire empire, ConstitutionalEconomyState state)
    {
        state.last_local_election_term = state.parliament_term;
        var coalition = new HashSet<string>(ParliamentSystem.GetCoalitionIds(empire));
        int won = 0, total = 0;
        var oppositionHeld = new List<string>();
        foreach (Kingdom province in Provinces(empire))
        {
            ProvincePolitics data = GetOrCreate(state, province);
            List<(FixedFaction party, float share)> shares = ComputeShares(empire, province, data);
            if (shares.Count == 0) continue;
            data.vote_shares = shares.ToDictionary(item => item.party.GetID(), item => item.share);
            data.last_election = World.world.getCurWorldTime();
            FixedFaction winner = shares[0].party;
            data.governing_party_id = winner.GetID();
            total++;
            if (coalition.Contains(winner.GetID())) won++;
            else oppositionHeld.Add(string.Format(LM.Get("local_election_province_entry"), province.GetKingdomName(),
                winner.Name));
            TryInstallGovernor(empire, province, winner);
        }
        if (total == 0) return;
        string country = empire.GetEmpireFullName();
        string summary = string.Format(LM.Get("local_election_history"), country, won, total, total - won);
        if (oppositionHeld.Count > 0)
            summary += string.Format(LM.Get("local_election_opposition_list"),
                string.Join("、", oppositionHeld.Take(4)) + (oppositionHeld.Count > 4 ? "…" : ""));
        EventRecorder.Record(empire, summary);
        CheckFederalPressure(empire, state);
    }

    // 省长出缺(去世等)：由本省执政党另推一人递补，不另行选举；返回是否已补上
    public static bool TryFillGovernorVacancy(Kingdom province)
    {
        Empire empire = province?.GetEmpire();
        if (!IsProvince(empire, province)) return false;
        FixedFaction party = GetGoverningParty(empire, province);
        if (party == null) return false;
        TryInstallGovernor(empire, province, party);
        return province.hasKing() && province.king.GetFaction() == party;
    }

    // 由本省胜选党的党员出任省长：优先没有官职的，住在本省的
    private static void TryInstallGovernor(Empire empire, Kingdom province, FixedFaction party)
    {
        if (!OfficeManager.Offices.TryGetValue(province.GetOfficeID(), out OfficeObject office)) return;
        Actor governor = office.GetActor();
        if (governor?.GetFaction() == party) return;
        long primeMinister = empire.data.constitutional_economy?.prime_minister_id ?? -1L;
        Actor candidate = party.AllMembers
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                            !actor.IsWarMachine() && actor.kingdom == province && actor.id != empire.Emperor?.id &&
                            actor.id != primeMinister && !actor.isKing() && actor.CanServeOffice(province))
            .OrderBy(actor => actor.IsOnOffice() ? 1 : 0)
            .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
            .ThenByDescending(actor => actor.renown).FirstOrDefault();
        // 无小人模式：本省没有本党实体党员时，从本省都城人口里推举一位读书人入党出任
        if (candidate == null && CityPopulationSystem.AbstractPopulationEnabled && province.capital != null)
        {
            candidate = CityPopulationSystem.SpawnScholar(province.capital);
            if (candidate != null)
            {
                party.AddMember(candidate);
                IdeologyPopulationSystem.Set(candidate, party.Ideology);
            }
        }
        if (candidate == null) return;
        office.SetActor(candidate);
        if (office.GetActor() == candidate)
            candidate.RecordPersonalHistory(string.Format(LM.Get("local_election_governor_personal"),
                province.GetKingdomName(), party.Name));
    }

    // 在野党连续两次地方选举控制六成以上行政区：各省联合要求地方自治，单一制改行联邦制
    private static void CheckFederalPressure(Empire empire, ConstitutionalEconomyState state)
    {
        float coalitionShare = CoalitionProvinceShare(empire);
        state.local_opposition_streak = 1f - coalitionShare >= FederalPressureShare
            ? state.local_opposition_streak + 1
            : 0;
        ConstitutionClauses clauses = ConstitutionSystem.GetClauses(empire);
        if (state.local_opposition_streak < 2 || clauses == null ||
            clauses.territory != ConstitutionTerritory.Unitary) return;
        if (!ConstitutionSystem.AmendByGovernment(empire, ConstitutionSystem.ClauseTerritory,
                ConstitutionTerritory.Federal.ToString(), "provinces")) return;
        state.local_opposition_streak = 0;
        EventRecorder.Record(empire, string.Format(LM.Get("local_federalism_history"), empire.GetEmpireFullName()));
    }

    #endregion

    #region 玩家：资助 / 打压

    public static int InfluenceCost(Kingdom province) =>
        20 + Mathf.RoundToInt(Mathf.Max(0, CityPopulationSystem.Households(province)) / 10f);

    // direction：+1 资助，-1 打压。花中央国库的钱，返回失败原因的本地化 key(成功返回 null)
    public static string Influence(Empire empire, Kingdom province, FixedFaction party, int direction)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || party == null || !IsProvince(empire, province) || !PartySystem.IsActive(empire))
            return "local_influence_unavailable";
        int cost = InfluenceCost(province);
        if (StateSettlementSystem.DiscretionaryFunds(empire.CoreKingdom) < cost) return "local_influence_no_money";
        ProvincePolitics data = GetOrCreate(state, province);
        float value = data.influence.TryGetValue(party.GetID(), out float current) ? current : 0f;
        float next = Mathf.Clamp(value + InfluenceStep * Math.Sign(direction), -MaxInfluence, MaxInfluence);
        if (Mathf.Approximately(next, value)) return "local_influence_maxed";
        if (!TreasurySystem.TrySpend(empire.CoreKingdom, cost, TreasuryCategory.Policy)) return "local_influence_no_money";
        data.influence[party.GetID()] = next;
        data.vote_shares = ComputeShares(empire, province, data)
            .ToDictionary(item => item.party.GetID(), item => item.share);
        return null;
    }

    #endregion
}
