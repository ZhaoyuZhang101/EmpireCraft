using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class ParliamentSeatView
{
    public FixedFaction Faction;
    public Actor Representative;
    public bool Constitutionalist;
    public bool Governing;
}

public sealed class ParliamentFactionView
{
    public FixedFaction Faction;
    public int Seats;
    public int CentralRatio;
    public bool Constitutionalist;
    public bool Governing;
    public PartyIdeology Ideology;
    // 上届议席(-1 = 上届没有议席)、本届与上届得票占比(0~1，-1 = 无记录)
    public int PreviousSeats = -1;
    public float VoteShare = -1f;
    public float PreviousVoteShare = -1f;
}

public sealed class ParliamentView
{
    public bool Exists;
    public bool ResponsibleGovernment;
    public int Term;
    public int TotalSeats;
    public int YearsUntilElection;
    public Actor PrimeMinister;
    public FixedFaction PrimeMinisterFaction;
    public string GovernmentType = "";
    public int GovernmentSeats;
    public List<ParliamentSeatView> Seats = new();
    public List<ParliamentFactionView> Factions = new();
    public GovernmentAgenda Agenda;
    public int NextSeatCount;
}

// 议会与总理大臣。
//
// 制宪改革进入 constitution.parliament_stage 后召开议会，取代原有内阁（选帝侯团性质的内阁除外）：
//   · 议席分配：议席总数 parliament_seats 按各派系中央占比，用最大余额法分给未被取缔的派系；
//   · 议员产生：每个派系按"有官职者优先 → 政绩 → 声望"推举本派成员出任，人数不够的议席空缺；
//   · 任期：每 parliament_term_years 年全面改选；期间议员去世、离开帝国或改换派系，由原派系补选；
//   · 总理大臣由议会选举(组阁见 ParliamentSystem.Government.cs)：单一派系议席过半 → 单独组阁；
//     否则第一大党(不成再由第二大党)拉拢理念最接近的派系凑够过半 → 联合政府；谈判破裂 → 少数派政府。
//   · 皇帝照旧按继承法在位；总理占据原"权臣"的位置，议会存续期间不再产生权臣。
public static partial class ParliamentSystem
{
    // 议会改选周期：有宪法时按宪法的任期条款，没有宪法时按 InstitutionTrees/Settings.json 的 parliament_term_years
    public static int TermYears(Empire empire)
    {
        int years = ConstitutionSystem.GetClauses(empire)?.term_years ?? 0;
        return years > 0 ? years : Config.parliament_term_years;
    }

    public const string GovernmentMajority = "majority";
    public const string GovernmentCoalition = "coalition";
    public const string GovernmentMinority = "minority";

    private static ConstitutionConfig Config => InstitutionDefinitionRegistry.Global.constitution;

    private static ConstitutionalEconomyState GetState(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return null;
        state.parliament_seats ??= new List<ParliamentSeat>();
        state.prime_minister_faction_id ??= "";
        state.government_type ??= "";
        return state;
    }

    private static bool HasReachedStage(Empire empire, int stage)
    {
        ConstitutionalEconomyState state = GetState(empire);
        if (state == null) return false;
        // 临时政府和制宪会议不是已选出的议会。
        if (state.is_republic) return state.republic_transition_stage == 0;
        if (!RegimeManager.IsMonarchy(empire.CoreKingdom?.GetRegime()?.type)) return false;
        return state.constitutional_monarchy ||
               state.constitutional_reform_active && state.constitutional_reform_stage >= stage;
    }

    public static bool HasParliament(Empire empire) => HasReachedStage(empire, Config.parliament_stage);

    public static bool HasResponsibleGovernment(Empire empire) =>
        HasReachedStage(empire, Config.responsible_government_stage);

    // 议会存在时，原有内阁是否仍然保留（只有选帝侯团性质的内阁保留）
    public static bool KeepsCabinet(Empire empire) =>
        RegimeManager.IsCabinetElectoralCollege(empire?.CoreKingdom?.GetRegime()?.type);

    #region 查询

    public static Actor GetPrimeMinister(Empire empire)
    {
        if (!HasParliament(empire)) return null;
        ConstitutionalEconomyState state = GetState(empire);
        Actor actor = state.prime_minister_id > 0 ? World.world?.units?.get(state.prime_minister_id) : null;
        return IsValidMember(empire, actor) ? actor : null;
    }

    // 是否属于执政一方：多数/少数派政府为总理所属派系；联合政府为整个宪政联盟
    // （在议会中有议席、且支持宪制的全部派系）。责任政府下只有执政一方能推动派系诉求。
    public static bool IsGoverningFaction(Empire empire, string factionId)
    {
        if (!HasParliament(empire) || string.IsNullOrWhiteSpace(factionId)) return false;
        return GetGoverningFactionIds(empire, GetState(empire)).Contains(factionId);
    }

    // 总理所属派系
    public static FixedFaction GetGoverningFaction(Empire empire)
    {
        if (!HasParliament(empire)) return null;
        return FindFaction(empire, GetState(empire).prime_minister_faction_id);
    }

    // 执政一方在议会中是否过半（多数政府 / 联合政府）
    public static bool HasWorkingMajority(Empire empire)
    {
        if (!HasParliament(empire)) return false;
        ConstitutionalEconomyState state = GetState(empire);
        return (state.government_type == GovernmentMajority || state.government_type == GovernmentCoalition) &&
               GetPrimeMinister(empire) != null;
    }

    // 政府首脑：有议会时为总理大臣，否则为内阁首辅
    public static Actor GetHeadOfGovernment(Empire empire) =>
        HasParliament(empire) ? GetPrimeMinister(empire) : empire?.GetCabinetLeader();

    public static List<Actor> GetRepresentatives(Empire empire)
    {
        if (!HasParliament(empire)) return new List<Actor>();
        return GetState(empire).parliament_seats
            .Select(seat => seat.actor_id > 0 ? World.world.units.get(seat.actor_id) : null)
            .Where(actor => IsValidMember(empire, actor)).ToList();
    }

    public static ParliamentView GetView(Empire empire)
    {
        var view = new ParliamentView();
        if (!HasParliament(empire)) return view;
        ConstitutionalEconomyState state = GetState(empire);
        bool budding = state.capitalist_budding;
        view.Exists = true;
        view.ResponsibleGovernment = HasResponsibleGovernment(empire);
        view.Term = state.parliament_term;
        view.TotalSeats = state.parliament_seats.Count;
        view.YearsUntilElection = state.last_parliament_election < 0
            ? 0
            : Math.Max(0, TermYears(empire) - Date.getYearsSince(state.last_parliament_election));
        view.PrimeMinister = GetPrimeMinister(empire);
        view.PrimeMinisterFaction = GetGoverningFaction(empire);
        view.GovernmentType = state.government_type;
        view.GovernmentSeats = state.government_seats;
        view.Agenda = state.government_agenda;
        view.NextSeatCount = SeatCount(empire);
        HashSet<string> governing = GetGoverningFactionIds(empire, state);
        foreach (ParliamentSeat seat in state.parliament_seats)
        {
            FixedFaction faction = FindFaction(empire, seat.faction_id);
            Actor actor = seat.actor_id > 0 ? World.world.units.get(seat.actor_id) : null;
            view.Seats.Add(new ParliamentSeatView
            {
                Faction = faction,
                Representative = IsValidMember(empire, actor) ? actor : null,
                Constitutionalist = faction != null && ConstitutionalEconomySystem.IsConstitutionalist(faction, budding),
                Governing = governing.Contains(seat.faction_id)
            });
        }
        foreach (IGrouping<string, ParliamentSeat> group in state.parliament_seats.GroupBy(seat => seat.faction_id))
        {
            FixedFaction faction = FindFaction(empire, group.Key);
            if (faction == null) continue;
            view.Factions.Add(new ParliamentFactionView
            {
                Faction = faction,
                Seats = group.Count(),
                CentralRatio = faction.CentralRatio,
                Constitutionalist = ConstitutionalEconomySystem.IsConstitutionalist(faction, budding),
                Governing = governing.Contains(group.Key),
                Ideology = PartySystem.LeaningOf(faction),
                PreviousSeats = state.previous_seat_counts != null &&
                                state.previous_seat_counts.TryGetValue(group.Key, out int before) ? before
                    : state.previous_seat_counts?.Count > 0 ? 0 : -1,
                VoteShare = state.vote_shares != null && state.vote_shares.TryGetValue(group.Key, out float share)
                    ? share : -1f,
                PreviousVoteShare = state.previous_vote_shares != null &&
                                    state.previous_vote_shares.TryGetValue(group.Key, out float previousShare)
                    ? previousShare : -1f
            });
        }
        view.Factions = view.Factions.OrderByDescending(item => item.Seats)
            .ThenByDescending(item => item.CentralRatio).ToList();
        return view;
    }

    private static HashSet<string> GetGoverningFactionIds(Empire empire, ConstitutionalEconomyState state)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(state.prime_minister_faction_id)) return result;
        if (state.coalition_faction_ids?.Count > 0)
        {
            result.UnionWith(state.coalition_faction_ids);
            result.Add(state.prime_minister_faction_id);
            return result;
        }
        if (state.government_type != GovernmentCoalition)
        {
            result.Add(state.prime_minister_faction_id);
            return result;
        }
        bool budding = state.capitalist_budding;
        foreach (string factionId in state.parliament_seats.Select(seat => seat.faction_id).Distinct())
        {
            FixedFaction faction = FindFaction(empire, factionId);
            if (faction != null && ConstitutionalEconomySystem.IsConstitutionalist(faction, budding))
                result.Add(factionId);
        }
        return result;
    }

    #endregion

    #region 更新（由 ConstitutionalEconomySystem.Update 调用）

    public static void Update(Empire empire)
    {
        ConstitutionalEconomyState state = GetState(empire);
        if (state == null || World.world == null) return;
        if (RepublicSystem.IsTransitioning(empire))
        {
            if (state.parliament_seats.Count > 0 || state.prime_minister_id > 0) Dissolve(state);
            return;
        }
        if (!HasParliament(empire))
        {
            if (state.parliament_seats.Count > 0 || state.prime_minister_id > 0) Dissolve(state);
            return;
        }
        if (!KeepsCabinet(empire)) RetireCabinet(empire);

        // 议席指向已不存在的派系(政党解散、被取缔，或旧存档派系不进存档)：
        // 全部议席都失效才重新大选；只是个别议席失效就转给议员现在的党或按得票补选，
        // 不能因此提前大选(否则政党一解散就改选一次，届数飞涨)
        if (state.parliament_seats.Count > 0 &&
            state.parliament_seats.All(seat => FindFaction(empire, seat.faction_id) == null))
            state.last_parliament_election = -1d;
        else if (ReassignOrphanSeats(empire, state))
            ElectPrimeMinister(empire, state);
        bool firstSession = state.parliament_seats.Count == 0 && state.last_parliament_election < 0;
        if (state.parliament_seats.Count == 0 || state.last_parliament_election < 0 ||
            Date.getYearsSince(state.last_parliament_election) >= TermYears(empire))
        {
            HoldGeneralElection(empire, state);
            if (firstSession) RecordHistory(empire, "parliament_first_session_history");
            return;
        }

        bool yearly = state.last_parliament_by_election < 0 ||
                      Date.getYearsSince(state.last_parliament_by_election) >= 1;
        if (yearly)
        {
            state.last_parliament_by_election = World.world.getCurWorldTime();
            if (FillVacancies(empire, state)) ElectPrimeMinister(empire, state);
        }
        if (GetPrimeMinister(empire) == null) ElectPrimeMinister(empire, state);
        if (state.last_government_review < 0 || Date.getYearsSince(state.last_government_review) >= 1)
        {
            state.last_government_review = World.world.getCurWorldTime();
            ReviewGovernment(empire, state);
        }
    }

    // 议会成立或存续期间，普通内阁不再存在
    public static void RetireCabinet(Empire empire)
    {
        if (empire?.data?.CabinetMembers == null || empire.data.CabinetMembers.Count == 0) return;
        foreach (long memberId in empire.data.CabinetMembers.ToList())
        {
            Actor member = World.world.units.get(memberId);
            if (member != null && !member.isRekt()) empire.RemoveCabinetMember(member);
            else empire.data.CabinetMembers.Remove(memberId);
        }
    }

    // 政治原因的提前大选(民意不信任、革命浪潮、废君危机)距上次实际大选至少间隔这么多年；
    // 制度变动(宪法改选举制度、开关党禁、革命后重组)引起的改选不受限制
    public const int MinYearsBetweenSnapElections = 2;

    public static bool CanCallSnapElection(ConstitutionalEconomyState state)
    {
        if (state == null || World.world == null) return false;
        double last = state.last_general_election_held >= 0d ? state.last_general_election_held : state.last_parliament_election;
        return last < 0d || Date.getYearsSince(last) >= MinYearsBetweenSnapElections;
    }

    public static void Dissolve(ConstitutionalEconomyState state)
    {
        state.parliament_seats.Clear();
        state.prime_minister_id = -1L;
        state.prime_minister_faction_id = "";
        state.government_type = "";
        state.government_seats = 0;
        state.last_parliament_election = -1d;
        state.last_parliament_by_election = -1d;
        state.coalition_faction_ids?.Clear();
        state.government_formed_at = -1d;
        state.government_agenda = null;
    }

    private static void HoldGeneralElection(Empire empire, ConstitutionalEconomyState state)
    {
        // 上届议席与得票留作对比(议席图的涨跌箭头)
        state.previous_seat_counts = state.parliament_seats.GroupBy(seat => seat.faction_id)
            .ToDictionary(group => group.Key, group => group.Count());
        state.previous_vote_shares = state.vote_shares ?? new Dictionary<string, float>();
        int seatCount = SeatCount(empire);
        state.parliament_seats.Clear();
        state.last_parliament_election = World.world.getCurWorldTime();
        state.last_parliament_by_election = World.world.getCurWorldTime();
        state.last_general_election_held = World.world.getCurWorldTime();
        state.parliament_term++;
        // 开放党禁后议席按选票分给政党；此前按各派系中央占比分
        bool partyPolitics = PartySystem.IsActive(empire);
        // 党禁关闭时只有执政党和友党参选(旧存档里可能有没被正式取缔的政党)
        List<FixedFaction> factions = partyPolitics
            ? PartySystem.GetParties(empire).Where(party => PartyBanSystem.IsAllowedParty(empire, party)).ToList()
            : GetSeatedFactions(empire);
        Dictionary<FixedFaction, float> votes = partyPolitics ? PartySystem.CountVotes(empire, factions) : null;
        // 普选后按行政区选举(区内按比例)；此前全国统一按得票/中央占比分
        List<(FixedFaction faction, long district)> slots;
        if (partyPolitics && PartyBanSystem.UsesDemocraticCentralism(empire))
            slots = PartyBanSystem.AllocateCongressSeats(empire, factions, seatCount);
        else if (partyPolitics && PartySystem.HasUniversalSuffrage(empire))
            slots = PartySystem.AllocateDistrictSeats(empire, factions, seatCount);
        else
        {
            Dictionary<FixedFaction, int> national = AllocateSeats(factions, seatCount,
                votes == null ? null : faction => votes.TryGetValue(faction, out float count) ? count : 0f);
            slots = factions.SelectMany(faction => Enumerable.Repeat((faction, -1L),
                national.TryGetValue(faction, out int count) ? count : 0)).ToList();
        }
        Dictionary<FixedFaction, int> allocation = slots.GroupBy(slot => slot.faction)
            .ToDictionary(group => group.Key, group => group.Count());
        var used = new HashSet<long>();
        foreach ((FixedFaction faction, long district) in slots)
        {
            Actor actor = PickCandidate(empire, faction, district, used);
            if (actor != null) used.Add(actor.id);
            state.parliament_seats.Add(new ParliamentSeat
                { faction_id = faction.GetID(), actor_id = actor?.id ?? -1L, district_kingdom_id = district });
        }
        float voteTotal = votes?.Values.Sum() ?? factions.Sum(faction => Math.Max(0, faction.CentralRatio));
        state.vote_shares = factions.ToDictionary(faction => faction.GetID(), faction => voteTotal <= 0f ? 0f :
            (votes != null ? votes.TryGetValue(faction, out float count) ? count : 0f : Math.Max(0, faction.CentralRatio)) / voteTotal);
        if (partyPolitics) PartySystem.AfterElection(empire, allocation, seatCount);
        ElectPrimeMinister(empire, state, announce: "election");
        RepublicSystem.OnFirstRepublicElection(empire);
        // 共和国：大选后由执政党领袖出任元首
        RepublicSystem.UpdateHeadOfState(empire);
    }

    // 补选：议员去世、离开帝国或改换派系后，由原派系另推一人；返回是否有议席变动
    private static bool FillVacancies(Empire empire, ConstitutionalEconomyState state)
    {
        bool changed = false;
        var used = new HashSet<long>(state.parliament_seats
            .Where(seat => IsSeatHolderValid(empire, seat)).Select(seat => seat.actor_id));
        foreach (ParliamentSeat seat in state.parliament_seats)
        {
            if (IsSeatHolderValid(empire, seat)) continue;
            FixedFaction faction = FindFaction(empire, seat.faction_id);
            Actor replacement = faction == null ? null : PickCandidate(empire, faction, seat.district_kingdom_id, used);
            long newId = replacement?.id ?? -1L;
            if (newId == seat.actor_id) continue;
            seat.actor_id = newId;
            if (replacement != null) used.Add(replacement.id);
            changed = true;
        }
        return changed;
    }

    // 所属派系已不存在的议席：议员还在且已转入现存的党，议席随人转党；否则按得票(没有得票记录时按议席)
    // 补给现存最大的议会党团，由该党补选一人。返回是否有议席变动
    private static bool ReassignOrphanSeats(Empire empire, ConstitutionalEconomyState state)
    {
        List<ParliamentSeat> orphans = state.parliament_seats
            .Where(seat => FindFaction(empire, seat.faction_id) == null).ToList();
        if (orphans.Count == 0) return false;
        var used = new HashSet<long>(state.parliament_seats
            .Where(seat => !orphans.Contains(seat) && seat.actor_id > 0).Select(seat => seat.actor_id));
        FixedFaction fallback = state.parliament_seats
            .Where(seat => !orphans.Contains(seat))
            .Select(seat => seat.faction_id).Distinct()
            .Select(id => FindFaction(empire, id)).Where(faction => faction != null)
            .OrderByDescending(faction => state.vote_shares != null &&
                                          state.vote_shares.TryGetValue(faction.GetID(), out float share) ? share : 0f)
            .ThenByDescending(faction => state.parliament_seats.Count(seat => seat.faction_id == faction.GetID()))
            .FirstOrDefault();
        foreach (ParliamentSeat seat in orphans)
        {
            Actor holder = seat.actor_id > 0 ? World.world.units.get(seat.actor_id) : null;
            FixedFaction current = IsValidMember(empire, holder) ? holder.GetFaction() : null;
            if (current != null && FindFaction(empire, current.GetID()) != null)
            {
                seat.faction_id = current.GetID();
                used.Add(holder.id);
                continue;
            }
            if (fallback == null)
            {
                seat.actor_id = -1L;
                continue;
            }
            seat.faction_id = fallback.GetID();
            Actor replacement = PickCandidate(empire, fallback, seat.district_kingdom_id, used);
            seat.actor_id = replacement?.id ?? -1L;
            if (replacement != null) used.Add(replacement.id);
        }
        return true;
    }

    private static bool IsSeatHolderValid(Empire empire, ParliamentSeat seat)
    {
        if (seat.actor_id <= 0) return false;
        Actor actor = World.world.units.get(seat.actor_id);
        return IsValidMember(empire, actor) && actor.GetFaction()?.GetID() == seat.faction_id;
    }

    // 执政党解散等情况下立即重新组阁
    public static void ReelectGovernment(Empire empire)
    {
        if (!HasParliament(empire)) return;
        ElectPrimeMinister(empire, GetState(empire));
        RepublicSystem.UpdateHeadOfState(empire);
    }

    // 派系领袖优先（须是本届议员或至少是本派合格成员），否则取本派排名最高的议员
    private static Actor FindPartyLeader(Empire empire, ConstitutionalEconomyState state, FixedFaction faction)
    {
        Actor leader = faction.GetLeader();
        if (IsValidMember(empire, leader)) return leader;
        string factionId = faction.GetID();
        return state.parliament_seats
            .Where(seat => seat.faction_id == factionId && seat.actor_id > 0)
            .Select(seat => World.world.units.get(seat.actor_id))
            .FirstOrDefault(actor => IsValidMember(empire, actor));
    }

    #endregion

    #region 规则

    private static List<FixedFaction> GetSeatedFactions(Empire empire) =>
        empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.Where(faction => faction != null && !faction.Ban && faction.CentralRatio > 0)
            .OrderByDescending(faction => faction.CentralRatio)
            .ToList() ?? new List<FixedFaction>();

    // 最大余额法：先按份额取整，剩余议席给小数部分最大的派系
    // weightOf 为空时按中央占比分(党禁时期的派系)；政党选举时传入得票
    public static Dictionary<FixedFaction, int> AllocateSeats(List<FixedFaction> factions, int seats,
        Func<FixedFaction, float> weightOf = null)
    {
        weightOf ??= faction => Math.Max(0, faction.CentralRatio);
        var result = new Dictionary<FixedFaction, int>();
        float total = factions.Sum(faction => Math.Max(0f, weightOf(faction)));
        if (factions.Count == 0 || total <= 0f || seats <= 0) return result;
        var remainders = new List<(FixedFaction faction, float remainder)>();
        int assigned = 0;
        foreach (FixedFaction faction in factions)
        {
            float quota = Math.Max(0f, weightOf(faction)) / total * seats;
            int whole = (int)Math.Floor(quota);
            result[faction] = whole;
            assigned += whole;
            remainders.Add((faction, quota - whole));
        }
        foreach (var (faction, _) in remainders.OrderByDescending(item => item.remainder)
                     .ThenByDescending(item => item.faction.CentralRatio).Take(Math.Max(0, seats - assigned)))
            result[faction]++;
        return result;
    }

    // 派系推举议员的顺序：有官职者优先，其次政绩，再次声望
    private static IEnumerable<Actor> RankCandidates(Empire empire, FixedFaction faction, HashSet<long> exclude) =>
        faction.AllMembers
            .Where(actor => IsValidMember(empire, actor) && !exclude.Contains(actor.id))
            .OrderByDescending(actor => actor.HasOfficeIdentity())
            .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? 0d)
            .ThenByDescending(actor => actor.renown);

    // 有选区时优先选本区住民，没有合适的再从全党挑
    private static Actor PickCandidate(Empire empire, FixedFaction faction, long district, HashSet<long> exclude)
    {
        List<Actor> ranked = RankCandidates(empire, faction, exclude).ToList();
        return (district > 0 ? ranked.FirstOrDefault(actor => actor.kingdom?.id == district) : null) ??
               ranked.FirstOrDefault();
    }

    // 议员与总理的资格：在世、成年、身在本帝国，且不是皇帝本人
    private static bool IsValidMember(Empire empire, Actor actor) =>
        actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.IsWarMachine() &&
        actor.kingdom?.GetEmpire() == empire && actor.id != empire.Emperor?.id;

    private static FixedFaction FindFaction(Empire empire, string factionId) =>
        string.IsNullOrWhiteSpace(factionId)
            ? null
            : empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()
                ?.FirstOrDefault(faction => faction != null && faction.GetID() == factionId);

    private static void RecordHistory(Empire empire, string key)
    {
        empire.RecordHistory(directContent: LM.Get(key), actorId: empire.Emperor?.id ?? -1L,
            kingdomId: empire.CoreKingdom.id);
    }

    #endregion
}
