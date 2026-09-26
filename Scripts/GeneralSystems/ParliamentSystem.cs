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
}

// 议会与总理大臣。
//
// 制宪改革进入 constitution.parliament_stage 后召开议会，取代原有内阁（选帝侯团性质的内阁除外）：
//   · 议席分配：议席总数 parliament_seats 按各派系中央占比，用最大余额法分给未被取缔的派系；
//   · 议员产生：每个派系按"有官职者优先 → 政绩 → 声望"推举本派成员出任，人数不够的议席空缺；
//   · 任期：每 parliament_term_years 年全面改选；期间议员去世、离开帝国或改换派系，由原派系补选；
//   · 总理大臣由议会选举：单一派系议席过半 → 该派领袖出任（多数政府）；
//     否则支持宪制的派系合计过半 → 其中议席最多的派系领袖出任（联合政府）；
//     再否则由议席最多的派系领袖组建少数派政府。
//   · 皇帝照旧按继承法在位；总理占据原"权臣"的位置，议会存续期间不再产生权臣。
public static class ParliamentSystem
{
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
        if (state == null || !RegimeManager.IsMonarchy(empire.CoreKingdom?.GetRegime()?.type)) return false;
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
            : Math.Max(0, Config.parliament_term_years - Date.getYearsSince(state.last_parliament_election));
        view.PrimeMinister = GetPrimeMinister(empire);
        view.PrimeMinisterFaction = GetGoverningFaction(empire);
        view.GovernmentType = state.government_type;
        view.GovernmentSeats = state.government_seats;
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
                Governing = governing.Contains(group.Key)
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
        if (!HasParliament(empire))
        {
            if (state.parliament_seats.Count > 0 || state.prime_minister_id > 0) Dissolve(state);
            return;
        }
        if (!KeepsCabinet(empire)) RetireCabinet(empire);

        bool firstSession = state.parliament_seats.Count == 0 && state.last_parliament_election < 0;
        if (state.parliament_seats.Count == 0 || state.last_parliament_election < 0 ||
            Date.getYearsSince(state.last_parliament_election) >= Config.parliament_term_years)
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

    public static void Dissolve(ConstitutionalEconomyState state)
    {
        state.parliament_seats.Clear();
        state.prime_minister_id = -1L;
        state.prime_minister_faction_id = "";
        state.government_type = "";
        state.government_seats = 0;
        state.last_parliament_election = -1d;
        state.last_parliament_by_election = -1d;
    }

    private static void HoldGeneralElection(Empire empire, ConstitutionalEconomyState state)
    {
        state.parliament_seats.Clear();
        state.last_parliament_election = World.world.getCurWorldTime();
        state.last_parliament_by_election = World.world.getCurWorldTime();
        state.parliament_term++;
        List<FixedFaction> factions = GetSeatedFactions(empire);
        Dictionary<FixedFaction, int> allocation = AllocateSeats(factions, Config.parliament_seats);
        var used = new HashSet<long>();
        foreach (FixedFaction faction in factions)
        {
            if (!allocation.TryGetValue(faction, out int count) || count <= 0) continue;
            List<Actor> members = RankCandidates(empire, faction, used).Take(count).ToList();
            for (int i = 0; i < count; i++)
            {
                Actor actor = i < members.Count ? members[i] : null;
                if (actor != null) used.Add(actor.id);
                state.parliament_seats.Add(new ParliamentSeat { faction_id = faction.GetID(), actor_id = actor?.id ?? -1L });
            }
        }
        ElectPrimeMinister(empire, state);
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
            Actor replacement = faction == null ? null : RankCandidates(empire, faction, used).FirstOrDefault();
            long newId = replacement?.id ?? -1L;
            if (newId == seat.actor_id) continue;
            seat.actor_id = newId;
            if (replacement != null) used.Add(replacement.id);
            changed = true;
        }
        return changed;
    }

    private static bool IsSeatHolderValid(Empire empire, ParliamentSeat seat)
    {
        if (seat.actor_id <= 0) return false;
        Actor actor = World.world.units.get(seat.actor_id);
        return IsValidMember(empire, actor) && actor.GetFaction()?.GetID() == seat.faction_id;
    }

    private static void ElectPrimeMinister(Empire empire, ConstitutionalEconomyState state)
    {
        long previous = state.prime_minister_id;
        int total = state.parliament_seats.Count;
        bool budding = state.capitalist_budding;
        List<(FixedFaction faction, int seats)> blocs = state.parliament_seats
            .GroupBy(seat => seat.faction_id)
            .Select(group => (faction: FindFaction(empire, group.Key), seats: group.Count()))
            .Where(item => item.faction != null)
            .OrderByDescending(item => item.seats)
            .ThenByDescending(item => item.faction.CentralRatio)
            .ToList();

        FixedFaction chosen = null;
        string type = "";
        int governmentSeats = 0;
        if (blocs.Count > 0 && blocs[0].seats * 2 > total)
        {
            chosen = blocs[0].faction;
            type = GovernmentMajority;
            governmentSeats = blocs[0].seats;
        }
        else
        {
            List<(FixedFaction faction, int seats)> coalition = blocs
                .Where(item => ConstitutionalEconomySystem.IsConstitutionalist(item.faction, budding)).ToList();
            int coalitionSeats = coalition.Sum(item => item.seats);
            if (coalition.Count > 0 && coalitionSeats * 2 > total)
            {
                chosen = coalition[0].faction;
                type = GovernmentCoalition;
                governmentSeats = coalitionSeats;
            }
            else if (blocs.Count > 0)
            {
                chosen = blocs[0].faction;
                type = GovernmentMinority;
                governmentSeats = blocs[0].seats;
            }
        }

        Actor primeMinister = chosen == null ? null : FindPartyLeader(empire, state, chosen);
        // 选中的派系一个合格人选都没有时，按议席顺序往下找能组阁的派系
        if (primeMinister == null)
        {
            foreach (var (faction, seats) in blocs)
            {
                primeMinister = FindPartyLeader(empire, state, faction);
                if (primeMinister == null) continue;
                chosen = faction;
                type = GovernmentMinority;
                governmentSeats = seats;
                break;
            }
        }

        state.prime_minister_id = primeMinister?.id ?? -1L;
        state.prime_minister_faction_id = primeMinister == null ? "" : chosen.GetID();
        state.government_type = primeMinister == null ? "" : type;
        state.government_seats = primeMinister == null ? 0 : governmentSeats;
        if (primeMinister == null || primeMinister.id == previous) return;

        string content = string.Format(LM.Get("parliament_prime_minister_elected_history"), primeMinister.getName(),
            chosen.Name, LM.Get($"parliament_government_{type}"), governmentSeats, total);
        empire.RecordHistory(directContent: content, actorId: primeMinister.id, kingdomId: empire.CoreKingdom.id);
        primeMinister.RecordPersonalHistory(content);
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
    public static Dictionary<FixedFaction, int> AllocateSeats(List<FixedFaction> factions, int seats)
    {
        var result = new Dictionary<FixedFaction, int>();
        float total = factions.Sum(faction => (float)Math.Max(0, faction.CentralRatio));
        if (factions.Count == 0 || total <= 0f || seats <= 0) return result;
        var remainders = new List<(FixedFaction faction, float remainder)>();
        int assigned = 0;
        foreach (FixedFaction faction in factions)
        {
            float quota = Math.Max(0, faction.CentralRatio) / total * seats;
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

    // 议员与总理的资格：在世、成年、身在本帝国，且不是皇帝本人
    private static bool IsValidMember(Empire empire, Actor actor) =>
        actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
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
