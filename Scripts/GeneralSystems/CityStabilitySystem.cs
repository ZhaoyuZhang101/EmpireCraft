using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CityStabilitySystem
{
    private static MapBox _world;
    private static int _cursor;
    private static bool _virtual;
    private sealed class Request
    {
        public City City;
        public Kingdom Owner;
        public CityStabilityData State;
        public CityStabilityInput Input;
    }
    private sealed class Batch
    {
        public Request[] Requests;
        public PopulationMathWorkers.Job Job;
        public CityStabilityBudget[] Results;
        public int Cursor;
        public float SubmittedAt;
        public bool FromWorker;
    }
    private static readonly List<Request> Capturing = new();
    private static readonly Queue<Batch> Pending = new();
    private static readonly HashSet<City> Queued = new();
    private sealed class Retirement
    {
        public City City;
        public Kingdom Owner;
        public CityStabilityData State;
        public int Cursor;
    }
    private static readonly Queue<Retirement> Retirements = new();
    private static readonly HashSet<City> Retiring = new();
    public static int WorkerResults { get; private set; }
    public static int FallbackResults { get; private set; }
    public static int PendingCities => Queued.Count;
    public static int PendingRetirements => Retiring.Count;
    private readonly struct GuardCount
    {
        public readonly int Assigned, Present;
        public GuardCount(int assigned, int present) { Assigned = assigned; Present = present; }
    }
    private static readonly Dictionary<City, GuardCount> GuardCounts = new();
    private static readonly Dictionary<City, int> RecruitCursors = new();
    private static int _countFrame = -1;
    private static MapBox _countWorld;
    private static bool _countVirtual;

    public static void ResetWorldState()
    {
        _world = World.world; _virtual = CityPopulationSystem.AbstractPopulationEnabled; _cursor = 0;
        Capturing.Clear(); Pending.Clear(); Queued.Clear(); GuardCounts.Clear(); _countFrame = -1;
        RecruitCursors.Clear();
        Retirements.Clear(); Retiring.Clear();
        WorkerResults = FallbackResults = 0;
    }

    public static void Tick()
    {
        if (ModClass.IS_CLEAR || World.world == null) { ResetWorldState(); return; }
        if (!ReferenceEquals(_world, World.world) || _virtual != CityPopulationSystem.AbstractPopulationEnabled) ResetWorldState();
        if (!SimulationFrameBudget.HasTime) return;
        using var measured = SimulationFrameBudget.Measure();
        using var profile = FrameProfiler.Measure("地方稳定与财政");
        DrainRetirements();
        if (!SimulationFrameBudget.HasTime) return;
        if (_virtual && Pending.Count > 0)
        {
            Batch batch = Pending.Peek();
            if (batch.Results == null)
            {
                if (batch.Job != null && batch.Job.TryGetResult(out PopulationNumericResult numeric))
                { batch.Results = numeric.Stability; batch.FromWorker = true; }
                else if (batch.Job == null || Time.realtimeSinceStartup - batch.SubmittedAt > 0.25f)
                    batch.Results = CityStabilityMath.Compute(batch.Requests.Select(request => request.Input).ToArray());
            }
            if (batch.Results != null)
            {
                Request request = batch.Requests[batch.Cursor];
                City city = request.City;
                Queued.Remove(city);
                if (TreasurySystem.Enabled(city) && city.kingdom == request.Owner &&
                    ReferenceEquals(city.GetOrCreate().stability, request.State) && Due(request.State))
                {
                    CityStabilityInput current = CaptureInput(city);
                    bool matches = request.Input.Equals(current);
                    CityStabilityBudget budget = matches ? batch.Results[batch.Cursor] : CityStabilityMath.Compute(current);
                    if (matches && batch.FromWorker) WorkerResults++; else FallbackResults++;
                    UpdatePrepared(city, request.State, current, budget);
                }
                if (++batch.Cursor == batch.Requests.Length) Pending.Dequeue();
            }
        }
        if (!SimulationFrameBudget.HasTime || _virtual && Queued.Count >= 16) return;
        var cities = World.world.cities?.list;
        if (cities == null || cities.Count == 0) return;
        if (_cursor >= cities.Count) _cursor = 0;
        City next = cities[_cursor++];
        if (TreasurySystem.Enabled(next))
        {
            if (!_virtual) Update(next);
            else if (!Queued.Contains(next))
            {
                CityStabilityData state = State(next);
                if (Due(state))
                {
                    Capturing.Add(new Request { City = next, Owner = next.kingdom, State = state, Input = CaptureInput(next) });
                    Queued.Add(next);
                }
            }
        }
        if (_virtual && Capturing.Count > 0 && (Capturing.Count >= 8 || _cursor >= cities.Count))
        {
            Request[] requests = Capturing.ToArray(); Capturing.Clear();
            var input = new PopulationNumericInput(requests.Select(request => request.Input).ToArray());
            Pending.Enqueue(new Batch { Requests = requests, Job = PopulationMathWorkers.Submit(input),
                SubmittedAt = Time.realtimeSinceStartup });
        }
    }

    private static bool Due(CityStabilityData state) => state != null &&
        (state.last_update < 0d || Date.getMonthsSince(state.last_update) >= 1);

    private static bool CanRetire(Actor actor, City city) => actor != null && actor.isAlive() && actor.isWarrior() &&
        !actor.isKing() && !actor.isCityLeader() && actor.city == city && actor.current_tile?.zone_city == city &&
        actor.kingdom != null && (actor.kingdom == city.kingdom || actor.kingdom.IsInSameEmpire(city.kingdom));

    private static void DrainRetirements()
    {
        if (Retirements.Count == 0) return;
        Retirement work = Retirements.Dequeue();
        City city = work.City;
        if (!TreasurySystem.Enabled(city) || city.kingdom != work.Owner ||
            !ReferenceEquals(city.GetOrCreate().stability, work.State) || !work.State.withdrawn)
        { Retiring.Remove(city); return; }
        int visited = 0, retired = 0;
        while (work.Cursor < city.units.Count && visited < 16 && retired < 4 && SimulationFrameBudget.HasTime)
        {
            Actor soldier = city.units[work.Cursor++]; visited++;
            if (!CanRetire(soldier, city)) continue;
            CityPopulationSystem.DemobilizeSoldier(soldier, city); retired++;
        }
        GuardCounts.Remove(city);
        if (work.Cursor < city.units.Count) { Retirements.Enqueue(work); return; }
        Retiring.Remove(city);
        CityStabilityBudget budget = CityStabilityMath.Compute(CaptureInput(city));
        ReviewWithdrawal(city, work.State, budget.GuardMonthly + budget.GovernanceAnnual / 12d);
    }

    public static CityStabilityData State(City city)
    {
        if (!TreasurySystem.Enabled(city)) return null;
        var extra = city.GetOrCreate();
        var state = extra.stability;
        if (state != null && state.owner_id == city.kingdom.id) return state;
        GuardCounts.Remove(city);
        RecruitCursors.Remove(city);
        float natural = Natural(city, out string detail);
        extra.stability = new CityStabilityData
        {
            owner_id = city.kingdom.id,
            stability = state?.stability ?? natural,
            natural_stability = natural,
            governance_policy = state?.governance_policy ?? -1,
            garrison_policy = state?.garrison_policy ?? -1,
            fear = state?.fear ?? 0f,
            last_massacre = state?.last_massacre ?? -1d,
            pressure_detail = detail
        };
        return extra.stability;
    }

    private static CityStabilityInput CaptureInput(City city)
    {
        Empire empire = city.kingdom.GetEmpire();
        int loyalty = city.getLoyalty();
        int legitimacy = empire?.Legitimacy ?? -1;
        float corruption = (float)CorruptionSystem.GetRate(city.kingdom);
        float sentiment = NationalSentimentSystem.GetCity(city);
        float landless = LandEconomySystem.GetLandlessRatio(city);
        int households = CityPopulationSystem.Households(city);
        bool famine = CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.GetGrowthFactors(city).Famine
            : households > 0 && city.getTotalFood() < households * 0.5f;
        float grievance = empire == null ? 0f : InstitutionSystem.GetClassGrievances(empire).Values.DefaultIfEmpty(0f).Max();
        var state = city.GetOrCreate().stability;
        return new CityStabilityInput(city.CountLivingPopulation(), households, loyalty, legitimacy, corruption,
            sentiment, landless, grievance, famine, WorldLawLibrary.world_law_civ_army.isEnabled(),
            state?.governance_policy ?? -1, state?.garrison_policy ?? -1);
    }

    private static string Pressure(CityStabilityInput input)
    {
        var causes = new List<string>();
        if (input.Loyalty < 0) causes.Add(LM.Get("rebellion_reason_city_loyalty") + " " + input.Loyalty);
        if (input.Corruption > 0.2f) causes.Add(string.Format(LM.Get("stability_corruption_detail"), input.Corruption * 100f));
        if (input.Sentiment > 20f) causes.Add(string.Format(LM.Get("stability_national_detail"), input.Sentiment));
        if (input.Landless >= 0.2f) causes.Add(string.Format(LM.Get("stability_land_detail"), input.Landless * 100f));
        if (input.Famine) causes.Add(LM.Get("city_pop_famine"));
        if (input.Grievance >= 40f) causes.Add(string.Format(LM.Get("stability_grievance_detail"), input.Grievance));
        return string.Join(" / ", causes);
    }

    private static float Natural(City city, out string detail)
    {
        CityStabilityInput input = CaptureInput(city);
        detail = Pressure(input);
        return CityStabilityRules.Natural(input.Loyalty, input.Legitimacy, input.Corruption, input.Sentiment,
            input.Landless, input.Famine, input.Grievance);
    }

    public static float Effective(City city)
    {
        var state = State(city);
        if (state == null) return 0f;
        Counts(city, out _, out int present);
        float funding = state.withdrawn ? 0f : state.garrison_funding;
        float suppression = CityStabilityRules.Suppression(present, state.garrison_required, funding);
        return CityStabilityRules.Clamp(state.stability + suppression + state.fear);
    }

    public static bool CanRise(City city) => city != null && !city.isRekt() &&
        (!TreasurySystem.Enabled(city) || Effective(city) < CityStabilityRules.RebellionThreshold);
    public static bool ControlsTax(City city)
    {
        var state = State(city);
        if (state == null || state.withdrawn) return false;
        Counts(city, out _, out int present);
        float suppression = CityStabilityRules.Suppression(present, state.garrison_required, state.garrison_funding);
        return suppression > 0f && CityStabilityRules.Clamp(state.stability + suppression + state.fear) >=
            CityStabilityRules.RebellionThreshold;
    }

    public static int FundedSlots(City city) => TreasurySystem.Enabled(city) &&
        city.GetOrCreate().stability is CityStabilityData state && state.owner_id == city.kingdom.id && !state.withdrawn
        ? state.funded_slots : 0;

    private static float Funding(int due, int paid, bool fractionalBudget) => due > 0
        ? Mathf.Clamp01(paid / (float)due) : fractionalBudget ? 1f : 0f;

    public static void Counts(City city, out int assigned, out int present, bool refresh = false)
    {
        if (_countFrame != Time.frameCount || !ReferenceEquals(_countWorld, World.world) ||
            _countVirtual != CityPopulationSystem.AbstractPopulationEnabled)
        { _countFrame = Time.frameCount; _countWorld = World.world;
          _countVirtual = CityPopulationSystem.AbstractPopulationEnabled; GuardCounts.Clear(); }
        if (city != null && !refresh && GuardCounts.TryGetValue(city, out GuardCount cached))
        { assigned = cached.Assigned; present = cached.Present; return; }
        double all = 0d, local = 0d;
        if (city?.units == null) { assigned = present = 0; return; }
        foreach (Actor actor in city.units)
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isWarrior() || actor.kingdom == null || actor.city != city ||
                actor.kingdom != city.kingdom && !actor.kingdom.IsInSameEmpire(city.kingdom)) continue;
            double people = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.LegionAlive(actor) : 1d;
            all += people;
            if (actor.current_tile?.zone_city == city) local += people;
        }
        assigned = (int)Math.Min(int.MaxValue, Math.Floor(all));
        present = (int)Math.Min(int.MaxValue, Math.Floor(local));
        GuardCounts[city] = new GuardCount(assigned, present);
    }

    private static IEnumerable<Kingdom> Payers(City city)
    {
        Kingdom local = city.kingdom;
        if (TreasurySystem.Enabled(local)) yield return local;
        Kingdom central = local.GetEmpire()?.CoreKingdom;
        if (central != local && TreasurySystem.Enabled(central)) yield return central;
    }

    private static long Available(City city) => Math.Max(0, city.GetMoney()) +
        Payers(city).Sum(kingdom => (long)Math.Max(0, kingdom.GetMoney()));

    private static int Pay(City city, int bill, TreasuryCategory category, bool municipalFirst)
    {
        int remaining = Math.Max(0, bill);
        void Municipality()
        {
            int paid = Math.Min(remaining, Math.Max(0, city.GetMoney()));
            if (paid > 0) city.SubMoney(paid, category);
            remaining -= paid;
        }
        if (municipalFirst) Municipality();
        foreach (Kingdom payer in Payers(city))
        {
            int paid = Math.Min(remaining, Math.Max(0, payer.GetMoney()));
            if (paid > 0) payer.SubMoney(paid, category);
            remaining -= paid;
            if (remaining == 0) break;
        }
        if (!municipalFirst && remaining > 0) Municipality();
        return Math.Max(0, bill) - remaining;
    }

    private static void Recruit(City city, int needed)
    {
        if (needed <= 0 || !WorldLawLibrary.world_law_civ_army.isEnabled()) return;
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            float pool = CityPopulationSystem.GetBackgroundTotal(city);
            if (pool < 1f) return;
            int people = Math.Min(needed, Math.Max(1, (int)(pool * 0.02f)));
            int units = Math.Min(2, Math.Max(1, Mathf.CeilToInt(people / CityPopulationSystem.PeoplePerLegion)));
            CityPopulationSystem.RaiseMilitia(city, units, people / pool);
            return;
        }
        if (city.units == null || city.units.Count == 0) return;
        int filled = 0, visited = 0;
        RecruitCursors.TryGetValue(city, out int cursor);
        // 不复制整城单位表；未轮到的居民留到下月继续，单城征募最多检查 64 人、补 4 人。
        while (visited < Math.Min(64, city.units.Count))
        {
            if (visited > 0 && !SimulationFrameBudget.HasTime) break;
            if (filled >= Math.Min(4, needed)) break;
            if (cursor >= city.units.Count) cursor = 0;
            Actor actor = city.units[cursor++]; visited++;
            if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || actor.isWarrior() ||
                actor.isKing() || actor.isCityLeader() || actor.equipment == null || !actor.CanFoundCivKingdom()) continue;
            city.makeWarrior(actor);
            if (actor.isWarrior()) filled++;
        }
        RecruitCursors[city] = cursor;
    }

    public static void Update(City city)
    {
        CityStabilityData state = State(city);
        if (!Due(state)) return;
        CityStabilityInput input = CaptureInput(city);
        UpdatePrepared(city, state, input, CityStabilityMath.Compute(input));
    }

    private static void UpdatePrepared(City city, CityStabilityData state, CityStabilityInput input, CityStabilityBudget budget)
    {
        state.last_update = World.world.getCurWorldTime();
        state.natural_stability = budget.Natural;
        state.pressure_detail = Pressure(input);
        float governance = budget.Governance;
        double governanceAnnual = budget.GovernanceAnnual;
        state.governance_due = CityStabilityRules.Invoice(governanceAnnual, ref state.governance_carry);
        bool municipalBudget = Available(city) > 0;
        state.governance_paid = Pay(city, state.governance_due, TreasuryCategory.Governance, true);
        float governanceFunding = governance > 0f ? Funding(state.governance_due, state.governance_paid, municipalBudget) : 0f;
        state.governance_quality = CityStabilityRules.Clamp(state.governance_quality +
            (governanceFunding >= 0.99f ? 2f * governance : -2f));
        state.garrison_required = budget.Guards;
        Counts(city, out int assigned, out _);
        double plannedMonthly = budget.GuardMonthly;
        bool guardBudget = state.garrison_required > 0 &&
            Available(city) >= Math.Max(1d, Math.Ceiling(plannedMonthly + state.garrison_carry));
        if (guardBudget) { state.withdrawn = false; Recruit(city, Math.Max(0, state.garrison_required - assigned)); }
        Counts(city, out assigned, out int present, refresh: true);
        int maintained = Math.Min(present, state.garrison_required);
        double garrisonAnnual = CityStabilityRules.GarrisonAnnual(maintained, state.natural_stability);
        state.garrison_due = CityStabilityRules.Invoice(garrisonAnnual, ref state.garrison_carry);
        state.garrison_paid = Pay(city, state.garrison_due, TreasuryCategory.Garrison, false);
        float guardFunding = Funding(state.garrison_due, state.garrison_paid, guardBudget);
        // 超出本城驻防需求的现有部队另记野战军费，替代旧的“国库增长×比例”账单。
        state.military_due = CityStabilityRules.Invoice(CityStabilityRules.MilitaryAnnual(
            Math.Max(0, assigned - maintained), city.kingdom.hasEnemies()), ref state.military_carry);
        state.military_paid = Pay(city, state.military_due, TreasuryCategory.Military, false);
        long unpaid = (long)state.governance_due - state.governance_paid + state.garrison_due - state.garrison_paid +
                      state.military_due - state.military_paid;
        state.governance_arrears = Math.Min(long.MaxValue / 6, state.governance_arrears + state.governance_due - state.governance_paid);
        state.garrison_arrears = Math.Min(long.MaxValue / 6, state.garrison_arrears + state.garrison_due - state.garrison_paid);
        state.military_arrears = Math.Min(long.MaxValue / 6, state.military_arrears + state.military_due - state.military_paid);
        state.arrears = state.governance_arrears + state.garrison_arrears + state.military_arrears;
        if (unpaid == 0 && state.arrears > 0)
        {
            long allowance = Math.Max(1L, (long)state.governance_due + state.garrison_due + state.military_due);
            void Repay(ref long debt, TreasuryCategory category)
            {
                int quote = (int)Math.Min(int.MaxValue, Math.Min(debt, allowance));
                int paid = Pay(city, quote, category, category == TreasuryCategory.Governance);
                debt -= paid; allowance -= paid;
            }
            Repay(ref state.governance_arrears, TreasuryCategory.Governance);
            Repay(ref state.garrison_arrears, TreasuryCategory.Garrison);
            Repay(ref state.military_arrears, TreasuryCategory.Military);
            state.arrears = state.governance_arrears + state.garrison_arrears + state.military_arrears;
        }
        state.garrison_assigned = assigned;
        state.garrison_actual = present;
        state.garrison_funding = guardFunding;
        state.funded_slots = state.garrison_required > 0 && guardFunding > 0f ? CityPopulationSystem.AbstractPopulationEnabled
            ? Math.Max(1, Mathf.CeilToInt(state.garrison_required * guardFunding / CityPopulationSystem.PeoplePerLegion))
            : Mathf.CeilToInt(state.garrison_required * guardFunding) : 0;
        state.suppression = CityStabilityRules.Suppression(present, state.garrison_required, guardFunding);
        bool unsupported = state.garrison_required > 0 && (!guardBudget || guardFunding < 0.99f) ||
            governance > 0f && governanceFunding < 0.99f || state.military_paid < state.military_due;
        state.unfunded_months = unsupported ? Math.Min(1200, state.unfunded_months + 1) : 0;
        float before = state.stability;
        state.stability = CityStabilityRules.Advance(before, state.natural_stability, state.governance_quality);
        state.last_change = state.stability - before;
        var extra = city.GetOrCreate();
        if (MassacreSystem.LoyaltyPenalty(city) < 0 && extra.massacre_last > state.last_massacre)
        { state.last_massacre = extra.massacre_last; state.fear = 15f; }
        else state.fear = Mathf.Max(0f, state.fear - 0.5f);
        ReviewWithdrawal(city, state, plannedMonthly + governanceAnnual / 12d);
    }

    private static void ReviewWithdrawal(City city, CityStabilityData state, double monthlyCost)
    {
        Kingdom origin = city.kingdom;
        Empire empire = origin.GetEmpire();
        Kingdom authority = empire?.CoreKingdom ?? origin;
        if (city == authority.capital || origin.hasEnemies() || authority.hasEnemies() ||
            RebellionSystem.GraceYearsRemaining(origin) > 0) return;
        if (state.unfunded_months < CityStabilityRules.WithdrawalMonths ||
            state.stability >= CityStabilityRules.RebellionThreshold || state.last_change > 0f) return;
        TreasuryReport fiscal = TreasurySystem.FundingReport(city);
        if (!CityStabilityRules.ShouldWithdraw(state.unfunded_months, state.stability, state.last_change,
            fiscal.months >= 12, fiscal.operating_balance - state.arrears,
            (int)Math.Min(int.MaxValue, Available(city)), monthlyCost)) return;
        state.withdrawn = true;
        state.funded_slots = 0;
        state.suppression = 0f;
        state.garrison_funding = 0f;
        if (Retiring.Contains(city)) return;
        if (city.units.Any(actor => CanRetire(actor, city)))
        {
            Retiring.Add(city);
            Retirements.Enqueue(new Retirement { City = city, Owner = origin, State = state });
            return;
        }
        GuardCounts.Remove(city);
        if (authority.GetOrCreate().last_stability_release >= 0d &&
            Date.getYearsSince(authority.GetOrCreate().last_stability_release) < 1) return;
        Kingdom independent;
        if (city == origin.capital)
        {
            if (empire == null || origin == authority || !origin.hasKing() ||
                origin.cities.Any(held => Effective(held) >= CityStabilityRules.RebellionThreshold)) return;
            empire.leave(origin);
            independent = origin;
        }
        else
        {
            Actor founder = city.leader;
            if (founder?.CanFoundCivKingdom() != true)
                founder = city.units?.FirstOrDefault(actor => actor.CanFoundCivKingdom() && actor.isAlive() && actor.isAdult());
            founder ??= CityPopulationSystem.SpawnRebelLeader(city);
            if (founder == null) return;
            independent = city.makeOwnKingdom(founder);
            if (independent == null) return;
        }
        independent.SetKingdomType(KingdomType.default_country_post);
        independent.GetRegime()?.SetAllowDiplomacy(true);
        independent.GetRegime()?.SetAllowArmy(true);
        authority.GetOrCreate().last_stability_release = World.world.getCurWorldTime();
        EventRecorder.Record(empire, string.Format(LM.Get("stability_release_history"), authority.GetKingdomFullName(),
            city.GetCityName(), state.unfunded_months, state.stability), independent.king, independent);
    }

    public static string GarrisonText(City city)
    {
        var state = State(city);
        if (state == null) return "";
        Counts(city, out _, out int present);
        return string.Format(LM.Get("city_garrison_format"), present, state.garrison_required);
    }

    public static string CostText(CityStabilityData state) => string.Format(LM.Get("city_stability_cost_format"),
        state.governance_paid, state.governance_due, state.garrison_paid, state.garrison_due);
    public static string PolicyText(int policy) => LM.Get(policy < 0 ? "stability_policy_auto" :
        policy == 0 ? "stability_policy_none" : policy == 1 ? "stability_policy_standard" : "stability_policy_extra");
}
