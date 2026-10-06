using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 议会政局：组阁、倒阁、内阁分配与跨届施政议程。
//
//   · 议席：基础议席(Settings.json 的 parliament_seats) + 人口每 100 人一席，最多 45 席，总数保持奇数；
//   · 组阁：一党过半单独组阁；否则第一大党(不成再由第二大党)按理念远近拉拢其他政党，凑够过半组成联合政府，
//     理念相距太远的不入阁；都凑不够就由第一大党组建少数派政府；
//   · 内阁：按宪法的文官制度条款——混合制下中央部门(六部)的部长按执政联盟各党议席比例分配；
//     政党分肥制下连下级部门的主官也随党更替；职业文官制下官员政治中立，执政党不任命；
//   · 倒阁：少数派政府、联盟内部分歧、部长丑闻、民怨、国库亏空都会让在野党发起不信任投票；
//     通过后能凑出新的过半联盟就重新组阁，否则解散议会提前大选；
//   · 施政议程：执政党每次提出修改一条宪法条款，攒够票数后通过；换届后新政府主张一致就接手，否则废止。
public static partial class ParliamentSystem
{
    private const int MaxSeats = 45;
    // 理念相距超过这个距离的政党不会结成联合政府(见 PartySystem 的理念光谱坐标)
    private const float MaxCoalitionDistance = 110f;
    // 一边拥宪一边反宪的两派之间额外的隔阂
    private const float ConstitutionalRift = 100f;
    // 施政议程票数不够、卡在二读满这么多年就搁置
    private const int StalledAgendaYears = 3;

    private sealed class GovernmentPlan
    {
        public FixedFaction Formateur;
        public List<(FixedFaction faction, int seats)> Members = new();
        public string Type = "";
        public int Seats;
    }

    #region 查询

    // 本届议会的议席数(下次大选生效)
    public static int SeatCount(Empire empire)
    {
        int baseSeats = Math.Max(1, Config.parliament_seats);
        // 按户计(无小人模式下真实人数可达几百万，议席会一直顶格)
        int population = CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.Households(empire) : empire?.CountPopulation() ?? 0;
        int seats = Mathf.Clamp(baseSeats + population / 100, baseSeats, MaxSeats);
        if (seats % 2 == 0) seats += seats + 1 > MaxSeats ? -1 : 1;
        return Math.Max(1, seats);
    }

    // 执政党控制内阁：责任政府(共和国、君主立宪的责任内阁阶段)才由议会多数派出任部长
    public static bool ControlsMinistries(Empire empire) =>
        HasResponsibleGovernment(empire) && !RepublicSystem.IsTransitioning(empire);

    public static ConstitutionCivilService CivilService(Empire empire) =>
        ConstitutionSystem.GetClauses(empire)?.civil_service ?? ConstitutionCivilService.Mixed;

    // 中央部门(六部)的部长由执政联盟任命：混合制、政党分肥制
    public static bool PartyAppointsMinisters(Empire empire) =>
        ControlsMinistries(empire) && CivilService(empire) != ConstitutionCivilService.Professional;

    // 下级部门的主官也由执政联盟任命：政党分肥制
    public static bool PartyAppointsDivisions(Empire empire) =>
        ControlsMinistries(empire) && CivilService(empire) == ConstitutionCivilService.Spoils;

    // 政党分肥制下换了执政党后的两年：官员大换血
    public static bool InSpoilsTurnover(Empire empire)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        return state != null && state.spoils_turnover_at >= 0d && Date.getYearsSince(state.spoils_turnover_at) < 2 &&
               CivilService(empire) == ConstitutionCivilService.Spoils;
    }

    public static List<string> GetCoalitionIds(Empire empire)
    {
        if (!HasParliament(empire)) return new List<string>();
        return GetGoverningFactionIds(empire, GetState(empire)).ToList();
    }

    public static GovernmentAgenda GetAgenda(Empire empire) =>
        HasParliament(empire) ? GetState(empire)?.government_agenda : null;

    // 两个派系/政党在政治上的距离：理念光谱上的距离，拥宪与反宪之间再加一道隔阂
    public static float PoliticalDistance(FixedFaction a, FixedFaction b, bool budding)
    {
        if (a == null || b == null) return float.MaxValue;
        if (a == b) return 0f;
        float distance = PartySystem.IdeologyDistance(PartySystem.LeaningOf(a), PartySystem.LeaningOf(b));
        if (!(a.IsParty && b.IsParty) && ConstitutionalEconomySystem.IsConstitutionalist(a, budding) !=
            ConstitutionalEconomySystem.IsConstitutionalist(b, budding))
            distance += ConstitutionalRift;
        return distance;
    }

    private static List<(FixedFaction faction, int seats)> Blocs(Empire empire, ConstitutionalEconomyState state) =>
        state.parliament_seats
            .GroupBy(seat => seat.faction_id)
            .Select(group => (faction: FindFaction(empire, group.Key), seats: group.Count()))
            .Where(item => item.faction != null)
            .OrderByDescending(item => item.seats)
            .ThenByDescending(item => item.faction.CentralRatio)
            .ToList();

    #endregion

    #region 组阁

    private static GovernmentPlan FormGovernment(Empire empire, ConstitutionalEconomyState state,
        List<(FixedFaction faction, int seats)> blocs, int total, HashSet<string> excluded)
    {
        List<(FixedFaction faction, int seats)> eligible = blocs
            .Where(item => excluded == null || !excluded.Contains(item.faction.GetID())).ToList();
        if (eligible.Count == 0) return null;
        if (eligible[0].seats * 2 > total)
            return new GovernmentPlan
            {
                Formateur = eligible[0].faction, Members = { eligible[0] }, Type = GovernmentMajority,
                Seats = eligible[0].seats
            };
        // 第一大党先谈，谈不拢再由第二大党试
        foreach ((FixedFaction formateur, int seats) in eligible.Take(2))
        {
            GovernmentPlan coalition = TryCoalition(eligible, formateur, seats, total, state.capitalist_budding);
            if (coalition != null) return coalition;
        }
        return new GovernmentPlan
        {
            Formateur = eligible[0].faction, Members = { eligible[0] }, Type = GovernmentMinority,
            Seats = eligible[0].seats
        };
    }

    // 组阁人按理念远近依次拉拢，凑够过半为止(最小获胜联盟)
    private static GovernmentPlan TryCoalition(List<(FixedFaction faction, int seats)> blocs, FixedFaction formateur,
        int formateurSeats, int total, bool budding)
    {
        var plan = new GovernmentPlan
        {
            Formateur = formateur, Members = { (formateur, formateurSeats) }, Type = GovernmentCoalition,
            Seats = formateurSeats
        };
        foreach ((FixedFaction faction, int seats) in blocs
                     .Where(item => item.faction != formateur &&
                                    PoliticalDistance(formateur, item.faction, budding) <= MaxCoalitionDistance)
                     .OrderBy(item => PoliticalDistance(formateur, item.faction, budding))
                     .ThenByDescending(item => item.seats))
        {
            if (plan.Seats * 2 > total) break;
            plan.Members.Add((faction, seats));
            plan.Seats += seats;
        }
        return plan.Seats * 2 > total && plan.Members.Count > 1 ? plan : null;
    }

    // announce：election 大选后组阁 / no_confidence 倒阁后重新组阁；为空表示补选后的例行确认
    private static void ElectPrimeMinister(Empire empire, ConstitutionalEconomyState state, string announce = null,
        HashSet<string> excluded = null)
    {
        state.coalition_faction_ids ??= new List<string>();
        long previous = state.prime_minister_id;
        string previousFaction = state.prime_minister_faction_id;
        var previousCoalition = new HashSet<string>(state.coalition_faction_ids);
        int total = state.parliament_seats.Count;
        List<(FixedFaction faction, int seats)> blocs = Blocs(empire, state);
        // 补选、领袖更替不改变政府：执政一方仍然站得住就只换总理人选
        if (announce == null && excluded == null && TryKeepGovernment(empire, state, blocs, total)) return;

        GovernmentPlan plan = FormGovernment(empire, state, blocs, total, excluded);
        // 大选后组阁受宪法任期限制：党魁已做满届数就由本党另推一人
        bool termLimited = announce == "election";
        Actor primeMinister = plan == null ? null : FindPartyLeader(empire, state, plan.Formateur, termLimited);
        // 选中的派系一个合格人选都没有时，按议席顺序往下找能组阁的派系
        if (primeMinister == null)
        {
            plan = null;
            foreach ((FixedFaction faction, int seats) in blocs)
            {
                if (excluded != null && excluded.Contains(faction.GetID())) continue;
                primeMinister = FindPartyLeader(empire, state, faction, termLimited);
                if (primeMinister == null) continue;
                plan = new GovernmentPlan
                    { Formateur = faction, Members = { (faction, seats) }, Type = GovernmentMinority, Seats = seats };
                break;
            }
        }
        if (primeMinister == null || plan == null)
        {
            state.prime_minister_id = -1L;
            state.prime_minister_faction_id = "";
            state.government_type = "";
            state.government_seats = 0;
            state.coalition_faction_ids.Clear();
            return;
        }

        state.prime_minister_id = primeMinister.id;
        state.prime_minister_faction_id = plan.Formateur.GetID();
        // 总统制共和国：记下赢得大选出任总理(兼元首)的届数
        if (termLimited && RepublicSystem.IsPresidentialRepublic(empire))
        {
            state.head_terms ??= new Dictionary<long, int>();
            state.head_terms[primeMinister.id] = (state.head_terms.TryGetValue(primeMinister.id, out int served) ? served : 0) + 1;
        }
        state.government_type = plan.Type;
        state.government_seats = plan.Seats;
        state.coalition_faction_ids = plan.Members.Select(item => item.faction.GetID()).ToList();
        bool newGovernment = previousFaction != state.prime_minister_faction_id ||
                             !previousCoalition.SetEquals(state.coalition_faction_ids);
        if (newGovernment || announce != null) state.government_formed_at = World.world.getCurWorldTime();
        if (previousFaction != state.prime_minister_faction_id && !string.IsNullOrEmpty(previousFaction) &&
            CivilService(empire) == ConstitutionCivilService.Spoils)
            state.spoils_turnover_at = World.world.getCurWorldTime();
        if (announce != null) AnnounceGovernment(empire, state, plan, primeMinister, total, announce);
        if (newGovernment) HandOverAgenda(empire, state, plan.Formateur);
        if (newGovernment || announce != null) AssignMinisters(empire, state, announce: true);
        if (announce != null || primeMinister.id == previous) return;
        RecordPrimeMinister(empire, primeMinister, plan.Formateur, plan.Type, plan.Seats, total);
    }

    private static bool TryKeepGovernment(Empire empire, ConstitutionalEconomyState state,
        List<(FixedFaction faction, int seats)> blocs, int total)
    {
        if (string.IsNullOrWhiteSpace(state.prime_minister_faction_id)) return false;
        FixedFaction leaderParty = FindFaction(empire, state.prime_minister_faction_id);
        if (leaderParty == null) return false;
        var members = new HashSet<string>(state.coalition_faction_ids) { state.prime_minister_faction_id };
        int seats = blocs.Where(item => members.Contains(item.faction.GetID())).Sum(item => item.seats);
        if (seats == 0) return false;
        bool hadMajority = state.government_type == GovernmentMajority || state.government_type == GovernmentCoalition;
        if (hadMajority && seats * 2 <= total) return false;
        Actor primeMinister = FindPartyLeader(empire, state, leaderParty);
        if (primeMinister == null) return false;
        long previous = state.prime_minister_id;
        state.prime_minister_id = primeMinister.id;
        state.government_seats = seats;
        if (state.coalition_faction_ids.Count == 0) state.coalition_faction_ids.Add(state.prime_minister_faction_id);
        if (primeMinister.id != previous)
            RecordPrimeMinister(empire, primeMinister, leaderParty, state.government_type, seats, total);
        return true;
    }

    private static void RecordPrimeMinister(Empire empire, Actor primeMinister, FixedFaction party, string type,
        int seats, int total)
    {
        string content = string.Format(LM.Get("parliament_prime_minister_elected_history"), primeMinister.getName(),
            party.Name, LM.Get($"parliament_government_{type}"), seats, total);
        empire.RecordHistory(directContent: content, actorId: primeMinister.id, kingdomId: empire.CoreKingdom.id);
        primeMinister.RecordPersonalHistory(content);
    }

    private static void AnnounceGovernment(Empire empire, ConstitutionalEconomyState state, GovernmentPlan plan,
        Actor primeMinister, int total, string reason)
    {
        string partners = string.Join("、", plan.Members.Skip(1).Select(item => item.faction.Name));
        string formation = string.Format(LM.Get($"parliament_formed_{plan.Type}"), primeMinister.getName(),
            plan.Formateur.Name, plan.Seats, total, partners);
        string key = reason == "election" ? "parliament_election_result_history" : "parliament_new_government_history";
        string content = string.Format(LM.Get(key), empire.GetEmpireFullName(), state.parliament_term, formation);
        EventRecorder.Record(empire, content, primeMinister);
        primeMinister.RecordPersonalHistory(content);
    }

    #endregion

    #region 内阁

    // 由执政联盟任命的官职(混合制：六部；政党分肥制：六部与下级部门)按各党议席比例分配；
    // 本党已在任的留任，空出来的由该党推举
    // 有官职空缺时立即补任(内阁成员去世、调任后不必等到年度政局审查)
    public static void FillVacantMinistries(Empire empire)
    {
        ConstitutionalEconomyState state = GetState(empire);
        if (state != null) AssignMinisters(empire, state, announce: false);
    }

    private static void AssignMinisters(Empire empire, ConstitutionalEconomyState state, bool announce)
    {
        if (!PartyAppointsMinisters(empire) || empire.data?.centerOffice == null) return;
        IEnumerable<long> appointed = empire.data.centerOffice.CoreOffices;
        if (PartyAppointsDivisions(empire)) appointed = appointed.Concat(empire.data.centerOffice.Divisions);
        List<OfficeObject> offices = appointed
            .Select(id => OfficeManager.Offices.TryGetValue(id, out OfficeObject office) ? office : null)
            .Where(office => office != null).ToList();
        if (offices.Count == 0) return;
        var coalition = new HashSet<string>(GetGoverningFactionIds(empire, state));
        List<(FixedFaction faction, int seats)> members = Blocs(empire, state)
            .Where(item => coalition.Contains(item.faction.GetID())).ToList();
        if (members.Count == 0)
        {
            // 没有执政联盟(组阁中、过渡期)：空着的官职照常由文官补任，不让副总统、国务卿等长期悬空
            foreach (OfficeObject office in offices.Where(office => office.GetActor() == null))
                office.Select(empire.CoreKingdom);
            return;
        }
        Dictionary<string, int> quota = AllocateSeats(members.Select(item => item.faction).ToList(), offices.Count,
                faction => members.First(item => item.faction == faction).seats)
            .ToDictionary(pair => pair.Key.GetID(), pair => pair.Value);

        var vacancies = new List<OfficeObject>();
        var used = new HashSet<long>();
        if (state.prime_minister_id > 0) used.Add(state.prime_minister_id);
        foreach (OfficeObject office in offices)
        {
            Actor holder = office.GetActor();
            if (holder != null) used.Add(holder.id);
            string party = holder?.GetFaction()?.GetID();
            if (party != null && quota.TryGetValue(party, out int left) && left > 0) quota[party] = left - 1;
            else vacancies.Add(office);
        }
        bool changed = false;
        foreach (OfficeObject office in vacancies)
        {
            string partyId = quota.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value)
                .Select(pair => pair.Key).FirstOrDefault();
            if (partyId == null) break;
            quota[partyId]--;
            FixedFaction party = FindFaction(empire, partyId);
            if (party == null) continue;
            // 优先没有官职的党员，免得为了凑部长把行政区长官调空
            Actor candidate = RankCandidates(empire, party, used)
                .Where(actor => actor.CanServeOffice(empire.CoreKingdom))
                .OrderBy(actor => actor.IsOnOffice() ? 1 : 0).FirstOrDefault();
            if (candidate == null) continue;
            office.SetActor(candidate);
            if (office.GetActor() != candidate) continue;
            used.Add(candidate.id);
            changed = true;
        }
        // 按配额补不上的(某党人手不够、党员都不能任官)：先由联盟其他党的党员补，再不行由文官补任
        foreach (OfficeObject office in vacancies.Where(office => office.GetActor() == null))
        {
            Actor candidate = members.SelectMany(item => RankCandidates(empire, item.faction, used))
                .Where(actor => actor.CanServeOffice(empire.CoreKingdom))
                .OrderBy(actor => actor.IsOnOffice() ? 1 : 0).FirstOrDefault();
            if (candidate != null) office.SetActor(candidate);
            if (office.GetActor() == null) office.Select(empire.CoreKingdom);
            if (office.GetActor() == null) continue;
            used.Add(office.GetActor().id);
            changed = true;
        }
        if (!announce || !changed) return;
        string lineup = string.Join("、", offices.Select(office => office.GetActor()?.GetFaction())
            .Where(party => party != null).GroupBy(party => party.Name)
            .Select(group => string.Format(LM.Get("parliament_cabinet_party_entry"), group.Key, group.Count())));
        if (!string.IsNullOrEmpty(lineup))
            EventRecorder.Record(empire, string.Format(LM.Get("parliament_cabinet_formed_history"),
                empire.GetEmpireFullName(), lineup));
    }

    #endregion

    #region 年度政局：倒阁与施政议程

    private static void ReviewGovernment(Empire empire, ConstitutionalEconomyState state)
    {
        if (!HasParliament(empire) || GetPrimeMinister(empire) == null) return;
        AssignMinisters(empire, state, announce: false);
        // 文官制度与腐败：职业文官廉洁自律，政党分肥滋生腐败
        if (ControlsMinistries(empire))
        {
            ConstitutionCivilService civilService = CivilService(empire);
            if (civilService == ConstitutionCivilService.Professional) empire.CoreKingdom.AddCorruptionRate(-0.02);
            else if (civilService == ConstitutionCivilService.Spoils) empire.CoreKingdom.AddCorruptionRate(0.02);
        }
        if (TryNoConfidence(empire, state)) return;
        UpdateAgenda(empire, state);
    }

    private static bool CanHoldNoConfidence(Empire empire, ConstitutionalEconomyState state)
    {
        if (!HasResponsibleGovernment(empire) || RepublicSystem.IsOneParty(empire) ||
            PartyBanSystem.UsesDemocraticCentralism(empire)) return false;
        // 总统制：总统任期固定，议会不能倒阁
        if (RepublicSystem.IsPresidentialRepublic(empire)) return false;
        if (state.government_formed_at < 0d || Date.getYearsSince(state.government_formed_at) < 1) return false;
        if (state.last_no_confidence >= 0d && Date.getYearsSince(state.last_no_confidence) < 2) return false;
        // 临近大选不再倒阁
        return state.last_parliament_election >= 0d &&
               Date.getYearsSince(state.last_parliament_election) < TermYears(empire) - 1;
    }

    // 在野党发起不信任投票；返回本年是否发生了倒阁(之后不再结算议程)
    private static bool TryNoConfidence(Empire empire, ConstitutionalEconomyState state)
    {
        if (!CanHoldNoConfidence(empire, state)) return false;
        FixedFaction leaderParty = GetGoverningFaction(empire);
        Actor primeMinister = GetPrimeMinister(empire);
        if (leaderParty == null || primeMinister == null) return false;
        bool budding = state.capitalist_budding;
        var coalition = new HashSet<string>(GetGoverningFactionIds(empire, state));
        List<(FixedFaction faction, int seats)> blocs = Blocs(empire, state);
        int total = state.parliament_seats.Count;

        var reasons = new List<string>();
        float pressure = 0f;
        if (state.government_type == GovernmentMinority)
        {
            pressure += 0.35f;
            reasons.Add(LM.Get("parliament_nc_reason_minority"));
        }
        float rift = blocs.Where(item => coalition.Contains(item.faction.GetID()))
            .Select(item => PoliticalDistance(leaderParty, item.faction, budding)).DefaultIfEmpty(0f).Max();
        if (rift > 60f)
        {
            pressure += (rift - 40f) / 200f;
            reasons.Add(LM.Get("parliament_nc_reason_rift"));
        }
        bool scandal = false;
        foreach (Actor minister in Ministers(empire).Prepend(primeMinister))
        {
            string crime = EmpireLawSystem.GetResolvableCrimeName(minister, empire.CoreKingdom);
            if (string.IsNullOrEmpty(crime)) continue;
            scandal = true;
            pressure += 0.3f;
            reasons.Add(string.Format(LM.Get("parliament_nc_reason_scandal"), minister.getName(), crime));
            break;
        }
        int unrest = PublicOpinionSystem.GetLevel(empire);
        if (unrest > PublicOpinionSystem.Content)
        {
            pressure += 0.12f * unrest;
            reasons.Add(LM.Get("parliament_nc_reason_unrest"));
        }
        if (state.bankrupt_since >= 0d)
        {
            pressure += 0.15f;
            reasons.Add(LM.Get("parliament_nc_reason_bankrupt"));
        }
        if (pressure < 0.2f || UnityEngine.Random.value >= Mathf.Min(0.6f, pressure - 0.1f)) return false;

        // 表决：在野党多半投赞成(理念接近执政党的可能弃权)，联盟小党视分歧与丑闻倒戈
        int yes = 0;
        foreach ((FixedFaction faction, int seats) in blocs)
        {
            if (faction == leaderParty) continue;
            float distance = PoliticalDistance(leaderParty, faction, budding);
            float chance = coalition.Contains(faction.GetID())
                ? Mathf.Clamp01((distance - 30f) / 150f + (scandal ? 0.15f : 0f) + pressure * 0.2f)
                : distance > 40f ? 0.85f : 0.5f;
            if (UnityEngine.Random.value < chance) yes += seats;
        }
        state.last_no_confidence = World.world.getCurWorldTime();
        string country = empire.GetEmpireFullName();
        if (yes * 2 <= total)
        {
            EventRecorder.Record(empire, string.Format(LM.Get("parliament_no_confidence_failed_history"), country,
                primeMinister.getName(), yes, total, string.Join("、", reasons)), primeMinister);
            return false;
        }
        EventRecorder.Record(empire, string.Format(LM.Get("parliament_no_confidence_passed_history"), country,
            primeMinister.getName(), yes, total, string.Join("、", reasons)), primeMinister);
        primeMinister.RecordPersonalHistory(string.Format(LM.Get("parliament_no_confidence_personal_history"),
            country));

        // 能凑出新的过半政府就重新组阁，否则解散议会、提前大选
        var excluded = new HashSet<string> { leaderParty.GetID() };
        GovernmentPlan alternative = FormGovernment(empire, state, blocs, total, excluded);
        if (alternative != null && alternative.Type != GovernmentMinority &&
            FindPartyLeader(empire, state, alternative.Formateur) != null)
        {
            ElectPrimeMinister(empire, state, "no_confidence", excluded);
            RepublicSystem.UpdateHeadOfState(empire);
        }
        else if (!CanCallSnapElection(state))
        {
            // 刚选过不久(提前大选冷却中)：不解散议会，由其余政党组建看守(少数派)政府
            ElectPrimeMinister(empire, state, "no_confidence", excluded);
            RepublicSystem.UpdateHeadOfState(empire);
        }
        else
        {
            EventRecorder.Record(empire, string.Format(LM.Get("parliament_snap_election_history"), country));
            HoldGeneralElection(empire, state);
        }
        return true;
    }

    // 政府在街头抗争中倒台(见 HarshRuleSystem)：能凑出新的过半政府就重新组阁，否则解散议会提前大选。
    // 只适用于多党制的责任政府；返回是否倒台
    public static bool ForceGovernmentCollapse(Empire empire)
    {
        if (!HasResponsibleGovernment(empire) || RepublicSystem.IsOneParty(empire) ||
            PartyBanSystem.UsesDemocraticCentralism(empire)) return false;
        ConstitutionalEconomyState state = GetState(empire);
        FixedFaction leaderParty = GetGoverningFaction(empire);
        if (state == null || leaderParty == null) return false;
        state.last_no_confidence = World.world.getCurWorldTime();
        var excluded = new HashSet<string> { leaderParty.GetID() };
        List<(FixedFaction faction, int seats)> blocs = Blocs(empire, state);
        GovernmentPlan alternative = FormGovernment(empire, state, blocs, state.parliament_seats.Count, excluded);
        if (alternative != null && alternative.Type != GovernmentMinority &&
            FindPartyLeader(empire, state, alternative.Formateur) != null)
        {
            ElectPrimeMinister(empire, state, "no_confidence", excluded);
            RepublicSystem.UpdateHeadOfState(empire);
        }
        else if (!CanCallSnapElection(state))
        {
            // 刚选过不久(提前大选冷却中)：由其余政党组建看守(少数派)政府
            ElectPrimeMinister(empire, state, "no_confidence", excluded);
            RepublicSystem.UpdateHeadOfState(empire);
        }
        else
        {
            EventRecorder.Record(empire, string.Format(LM.Get("parliament_snap_election_history"),
                empire.GetEmpireFullName()));
            HoldGeneralElection(empire, state);
        }
        return true;
    }

    private static IEnumerable<Actor> Ministers(Empire empire) =>
        (empire.data?.centerOffice?.CoreOffices ?? new List<long>())
        .Select(id => OfficeManager.Offices.TryGetValue(id, out OfficeObject office) ? office.GetActor() : null)
        .Where(actor => actor != null);

    private static string AgendaText(GovernmentAgenda agenda, string from) =>
        string.Format(LM.Get("government_agenda_change"), LM.Get($"constitution_clause_{agenda.clause}"),
            ConstitutionSystem.ValueText(agenda.clause, from), ConstitutionSystem.ValueText(agenda.clause, agenda.target));

    private static void UpdateAgenda(Empire empire, ConstitutionalEconomyState state)
    {
        FixedFaction governing = GetGoverningFaction(empire);
        ConstitutionData constitution = ConstitutionSystem.Get(empire);
        if (governing == null || constitution?.clauses == null || constitution.provisional ||
            !PartySystem.IsActive(empire)) return;
        string country = empire.GetEmpireFullName();
        GovernmentAgenda agenda = state.government_agenda;
        if (agenda != null && (ConstitutionSystem.IsPlayerLocked(empire, agenda.clause) ||
                               ConstitutionSystem.CurrentValue(constitution.clauses, agenda.clause) == agenda.target))
            agenda = state.government_agenda = null;

        if (agenda == null)
        {
            PartyIdeology ideology = PartySystem.LeaningOf(governing);
            List<(string clause, string target)> options = ConstitutionSystem.AgendaClauses
                .Where(clause => !ConstitutionSystem.IsPlayerLocked(empire, clause))
                .Select(clause => (clause, target: ConstitutionSystem.PartyPosition(empire, ideology, clause)))
                .Where(option => !string.IsNullOrEmpty(option.target) &&
                                 option.target != ConstitutionSystem.CurrentValue(constitution.clauses, option.clause))
                .ToList();
            if (options.Count == 0) return;
            (string clause, string target) pick = options[UnityEngine.Random.Range(0, options.Count)];
            agenda = state.government_agenda = new GovernmentAgenda
            {
                clause = pick.clause, target = pick.target, party_id = governing.GetID(),
                started_at = World.world.getCurWorldTime(), threshold = ConstitutionSystem.AmendmentThreshold(empire)
            };
            EventRecorder.Record(empire, string.Format(LM.Get("government_agenda_proposed_history"), country,
                governing.Name, AgendaText(agenda, ConstitutionSystem.CurrentValue(constitution.clauses, pick.clause))));
            return;
        }

        // 计票：主张一致的赞成；联盟伙伴随政府投票，但主张维持现状的弃权并要价；在野党主张现状的反对
        int total = state.parliament_seats.Count;
        if (total == 0) return;
        string current = ConstitutionSystem.CurrentValue(constitution.clauses, agenda.clause);
        var coalition = new HashSet<string>(GetGoverningFactionIds(empire, state));
        // 政治协商：参加协商的友党像联盟伙伴一样参与议程——主张一致的推动，主张维持现状的保留意见、拖慢进度
        coalition.UnionWith(PartyBanSystem.GetConsultativeParties(empire).Select(party => party.GetID()));
        int support = 0, oppose = 0, reluctantPartners = 0;
        foreach ((FixedFaction faction, int seats) in Blocs(empire, state))
        {
            string position = ConstitutionSystem.PartyPosition(empire, PartySystem.LeaningOf(faction), agenda.clause);
            bool inGovernment = coalition.Contains(faction.GetID());
            if (position == agenda.target || inGovernment && position != current) support += seats;
            else if (inGovernment) reluctantPartners++;
            else if (position == current) oppose += seats;
        }
        float supportShare = support / (float)total;
        float opposeShare = oppose / (float)total;
        float threshold = ConstitutionSystem.AmendmentThreshold(empire);
        agenda.support = supportShare;
        agenda.threshold = threshold;
        agenda.stalled = supportShare < threshold;
        // 卡在二读太久(票数始终不够)就搁置，让政府改提别的议程，不能一条议程挂到换届
        if (!agenda.stalled) agenda.stalled_since = -1d;
        else if (agenda.stalled_since < 0d) agenda.stalled_since = World.world.getCurWorldTime();
        else if (Date.getYearsSince(agenda.stalled_since) >= StalledAgendaYears)
        {
            EventRecorder.Record(empire, string.Format(LM.Get("government_agenda_shelved_history"), country,
                governing.Name, AgendaText(agenda, current)));
            state.government_agenda = null;
            return;
        }
        float gain = agenda.stalled ? 12f * supportShare / threshold : 34f * (1f - 0.4f * opposeShare);
        gain *= Mathf.Pow(0.7f, reluctantPartners);
        // 地方抗衡：在野党执政的行政区抵制中央议程(见 ProvincialPoliticsSystem)
        gain *= 0.6f + 0.4f * ProvincialPoliticsSystem.CoalitionProvinceShare(empire);
        // 执政党对官僚系统的控制力：职业文官不听党派调遣，分肥制下官员都是自己人
        gain *= CivilService(empire) switch
        {
            ConstitutionCivilService.Professional => 0.8f,
            ConstitutionCivilService.Spoils => 1.25f,
            _ => 1f
        };
        // 票数不够只能把议程推到二读，过不了最后表决
        agenda.progress = agenda.stalled
            ? Mathf.Max(agenda.progress, Mathf.Min(agenda.progress + gain, 80f))
            : Mathf.Min(100f, agenda.progress + gain);
        if (agenda.progress < 100f) return;
        FixedFaction sponsor = FindFaction(empire, agenda.party_id) ?? governing;
        if (ConstitutionSystem.AmendByGovernment(empire, agenda.clause, agenda.target, sponsor.GetID()))
            EventRecorder.Record(empire, string.Format(LM.Get("government_agenda_passed_history"), country,
                sponsor.Name, AgendaText(agenda, current)));
        state.government_agenda = null;
    }

    // 换了执政党：新政府主张一致就接手上届议程(保留进度)，否则废止
    private static void HandOverAgenda(Empire empire, ConstitutionalEconomyState state, FixedFaction newParty)
    {
        GovernmentAgenda agenda = state.government_agenda;
        if (agenda == null || newParty == null || agenda.party_id == newParty.GetID()) return;
        ConstitutionData constitution = ConstitutionSystem.Get(empire);
        if (constitution?.clauses == null) return;
        string current = ConstitutionSystem.CurrentValue(constitution.clauses, agenda.clause);
        string position = ConstitutionSystem.PartyPosition(empire, PartySystem.LeaningOf(newParty), agenda.clause);
        string country = empire.GetEmpireFullName();
        if (position == agenda.target)
        {
            agenda.party_id = newParty.GetID();
            EventRecorder.Record(empire, string.Format(LM.Get("government_agenda_continued_history"), country,
                newParty.Name, AgendaText(agenda, current)));
            return;
        }
        state.government_agenda = null;
        EventRecorder.Record(empire, string.Format(LM.Get("government_agenda_abandoned_history"), country,
            newParty.Name, AgendaText(agenda, current)));
    }

    #endregion
}
