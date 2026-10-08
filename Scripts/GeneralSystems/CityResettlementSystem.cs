using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 空城优先接收附近城市的真实平民。每个出资政权一次一座，选源和海路搜索分帧执行。
internal static class CityResettlementSystem
{
    private sealed class Work
    {
        public long[] sources;
        public int cursor;
        public City landSource;
        public float landPeople;
        public long landDistance = long.MaxValue;
        public readonly List<(City city, long distance)> seaSources = new();
        public int seaCursor;
        public SettlementSeaSearch sea;
        public string unavailable = "resettlement_people";
    }

    private static readonly Dictionary<long, Work> Works = new();
    private static readonly Queue<long> Pending = new();
    private static bool Enabled => CityPopulationSystem.AbstractPopulationEnabled && !ModClass.IS_CLEAR;
    private static bool Valid(City city) => city?.data != null && !city.isRekt() &&
        city.kingdom != null && !city.kingdom.isRekt() && !city.kingdom.wild &&
        !AncientWarfareCompatibility.OwnsObject(city) && !AncientWarfareCompatibility.Owns(city.kingdom) &&
        city.getTile()?.region?.island != null;

    private static bool Managed(Kingdom payer, City city)
    {
        if (!Valid(city)) return false;
        if (StateSettlementSystem.FundingRealm(city.kingdom) == payer) return true;
        var empire = payer.GetEmpire();
        return empire != null && empire.CoreKingdom == payer && city.kingdom.GetEmpire() == empire;
    }

    private static bool Due(City city) => CityPopulationSystem.Get(city).last_resettlement < 0d ||
        Date.getMonthsSince(CityPopulationSystem.Get(city).last_resettlement) >= 1;

    private static bool Empty(City city) => Valid(city) && CityPopulationSystem.GetTotal(city) < 1f &&
        city.units?.Any(actor => actor != null && actor.city == city && !actor.isRekt() && actor.isAlive()) != true &&
        !StateSettlementSystem.AwaitingPopulation(city) && Due(city) &&
        !(city.GetOrCreate().OccupiedStatus?.Count > 0) &&
        city.zones.Any(zone => zone.tiles_with_ground > 0);

    public static bool Request(Kingdom payer, StateSettlementData state)
    {
        if (!Enabled) return false;
        if (state.resettlement == null)
        {
            City target = StateSettlementSystem.ExpansionCities(payer).FirstOrDefault(Empty);
            if (target == null) return false;
            state.resettlement = new CityResettlementProject { target = target.id, target_owner = target.kingdom.id };
        }
        if (!Works.ContainsKey(payer.id))
        {
            Works[payer.id] = new Work { sources = StateSettlementSystem.ExpansionCities(payer).Select(city => city.id).ToArray() };
            Pending.Enqueue(payer.id);
        }
        return true;
    }

    private static float Migrants(City source, City target)
    {
        if (!Valid(source) || !Due(source) || source == target ||
            source.units?.Any(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.hasTrait("plague")) == true)
            return 0f;
        float civilians = CityPopulationSystem.CivilianBackground(source);
        float people = Math.Max(0f, Math.Min(civilians * 0.05f, civilians - CityPopulationSystem.PeoplePerSlot(source)));
        float seed = CityPopulationSystem.PeoplePerSlot(target) * 2f;
        float capacity = Math.Max(0f, target.getPopulationMaximum());
        if (capacity > 0f) seed = Math.Min(seed, Math.Max(1f, capacity - CityPopulationSystem.GetTotal(target)));
        return (float)Math.Floor(Math.Min(people, seed));
    }

    private static long Distance(City a, City b)
    {
        long dx = (long)a.getTile().x - b.getTile().x, dy = (long)a.getTile().y - b.getTile().y;
        return dx * dx + dy * dy;
    }

    public static void Tick()
    {
        if (!Enabled || Pending.Count == 0 || !SimulationFrameBudget.HasTime) return;
        long id = Pending.Dequeue();
        try { Process(id); }
        catch (Exception error)
        {
            var state = World.world.kingdoms.get(id)?.GetOrCreate().settlement;
            if (state?.resettlement != null)
            {
                state.status = "resettlement_recovery";
                Pending.Enqueue(id);
            }
            LogService.LogWarning($"[EmpireCraft] 空城安置待恢复({id}): {error.Message}");
        }
    }

    private static void Process(long id)
    {
        Kingdom payer = World.world.kingdoms.get(id);
        StateSettlementData state = payer?.GetOrCreate().settlement;
        CityResettlementProject plan = state?.resettlement;
        if (payer == null || payer.isRekt() || plan == null) { Works.Remove(id); return; }
        City target = World.world.cities.get(plan.target);
        // 人口已经到达后，归属变化也不能丢弃已扣除的货物；只恢复实际在途部分。
        if (plan.arrived && Valid(target)) { Finish(payer, state, target); Requeue(id, state); return; }
        if (!Managed(payer, target) || target.kingdom.id != plan.target_owner)
        { Cancel(payer, state, "changed"); return; }
        if (!Empty(target)) { Cancel(payer, state, "changed"); return; }
        if (!Works.TryGetValue(id, out Work work)) return;
        if (plan.source >= 0L)
        {
            Arrive(payer, state, target);
            // 等待航行的项目由下个月 Request 重新排队，不每帧轮询世界时间。
            if (plan.arrived) Requeue(id, state);
            else if (state.resettlement != null) Works.Remove(id);
            return;
        }
        for (int steps = 0; steps < 8 && work.cursor < work.sources.Length && SimulationFrameBudget.HasTime; steps++)
        {
            City source = World.world.cities.get(work.sources[work.cursor++]);
            if (!Managed(payer, source)) continue;
            float people = Migrants(source, target);
            if (people < 1f) continue;
            work.unavailable = "resettlement_unreachable";
            long distance = Distance(source, target);
            if (source.getTile().isSameIsland(target.getTile()))
            {
                if (distance > 192L * 192L || !target.getTile().reachableFrom(source.getTile())) continue;
                // 能安置更多人优先，同等规模选择更近的城市。
                if (people > work.landPeople || people == work.landPeople && distance < work.landDistance)
                { work.landSource = source; work.landPeople = people; work.landDistance = distance; }
            }
            else
            {
                work.seaSources.Add((source, distance));
                work.seaSources.Sort((a, b) => a.distance.CompareTo(b.distance));
                if (work.seaSources.Count > 4) work.seaSources.RemoveAt(4);
            }
        }
        if (work.cursor < work.sources.Length || !SimulationFrameBudget.HasTime)
        { Pending.Enqueue(id); return; }
        if (work.landSource != null)
        {
            Select(plan, work.landSource);
            Pending.Enqueue(id); // 安置另占一帧，避免选源与建设初始化挤在一起。
            return;
        }
        if (work.sea == null)
        {
            if (work.seaCursor >= work.seaSources.Count) { Cancel(payer, state, work.unavailable); return; }
            if (Works.Values.Count(item => item.sea != null) >= 4) { Pending.Enqueue(id); return; }
            City source = work.seaSources[work.seaCursor++].city;
            Select(plan, source);
            plan.source = -1L;
            work.sea = new SettlementSeaSearch(source, StateSettlementSystem.ExpansionCities(payer), target);
        }
        City sailor = World.world.cities.get(plan.journey.source);
        if (!Managed(payer, sailor)) { Cancel(payer, state, "changed"); return; }
        work.sea.Advance(sailor);
        for (int steps = 0; steps < 8 && SimulationFrameBudget.HasTime; steps++)
        {
            var landing = work.sea.NextLanding();
            if (landing == null) break;
            if (!target.getTile().isSameIsland(landing.landfall) || !target.getTile().reachableFrom(landing.landfall) ||
                landing.zone.city != target) continue;
            work.sea.Accept(landing).Apply(plan.journey);
            plan.source = sailor.id;
            plan.journey.started = World.world.getCurWorldTime();
            state.status = "resettlement_sailing";
            Works.Remove(id);
            return;
        }
        if (work.sea.Finished)
        {
            if (work.sea.unavailable == "port" || work.sea.unavailable == "navigation" ||
                work.sea.unavailable == "overseas_tech")
                work.unavailable = "resettlement_" + work.sea.unavailable;
            work.sea = null;
        }
        Pending.Enqueue(id);
    }

    private static void Select(CityResettlementProject plan, City source)
    {
        plan.source = source.id;
        plan.source_owner = source.kingdom.id;
        plan.journey = new StateSettlementProject { source = source.id, owner = source.kingdom.id, spontaneous = true };
    }

    private static void Arrive(Kingdom payer, StateSettlementData state, City target)
    {
        var plan = state.resettlement;
        City source = World.world.cities.get(plan.source);
        if (!Managed(payer, source) || source.kingdom.id != plan.source_owner)
        { Cancel(payer, state, "changed"); return; }
        if (plan.journey.overseas)
        {
            if (!SettlementSeaSearch.Validate(plan.journey, source, out WorldTile shore, out string reason, target))
            {
                Cancel(payer, state, reason == "overseas_tech" || reason == "navigation" || reason == "port"
                    ? "resettlement_" + reason : "resettlement_unreachable");
                return;
            }
            if (!target.getTile().isSameIsland(shore) || !target.getTile().reachableFrom(shore))
            { Cancel(payer, state, "resettlement_unreachable"); return; }
            if (Date.getMonthsSince(plan.journey.started) < SettlementSeaSearch.TravelMonths(plan.journey.sea_route.Count))
            { state.status = "resettlement_sailing"; return; }
        }
        else if (!source.getTile().isSameIsland(target.getTile()) || !target.getTile().reachableFrom(source.getTile()) ||
            Distance(source, target) > 192L * 192L)
        { Cancel(payer, state, "resettlement_unreachable"); return; }
        float people = Migrants(source, target);
        if (people < 1f) { Cancel(payer, state, "resettlement_people"); return; }
        PlanCargo(plan, source, target, people);
        float before = CityPopulationSystem.CivilianBackground(target);
        try { CityPopulationSystem.TransferCivilianBackground(source, target, people); }
        finally
        {
            plan.people = Math.Max(0f, CityPopulationSystem.CivilianBackground(target) - before);
            if (plan.people >= 1f)
            {
                plan.arrived = true;
                CityPopulationSystem.Get(source).last_resettlement = CityPopulationSystem.Get(target).last_resettlement =
                    World.world.getCurWorldTime();
                CityPopulationSystem.Get(target).spontaneous_settlement = true;
            }
        }
        if (!plan.arrived) { Cancel(payer, state, "resettlement_people"); return; }
        Finish(payer, state, target);
    }

    private static void PlanCargo(CityResettlementProject plan, City source, City target, float people)
    {
        plan.cargo.Clear();
        int need = (int)Math.Ceiling(people / CityPopulationSystem.PeoplePerSlot(target));
        var foods = AssetManager.resources.list.Where(resource => resource.type == ResType.Food).ToArray();
        int surplus = Math.Max(0, foods.Sum(resource => source.getResourcesAmount(resource.id)) -
            (int)Math.Ceiling((CityPopulationSystem.GetTotal(source) - people) / CityPopulationSystem.PeoplePerSlot(source)));
        foreach (var food in foods)
        {
            int amount = Math.Min(need, Math.Min(surplus, source.getResourcesAmount(food.id)));
            if (amount > 0) plan.cargo.Add(new SettlementCargo { source = source.id, resource = food.id, amount = amount });
            need -= amount; surplus -= amount;
        }
        foreach (var item in new[] { ("wood", 6), ("stone", 4) })
        {
            int amount = Math.Min(item.Item2, Math.Max(0, source.getResourcesAmount(item.Item1)));
            if (amount > 0) plan.cargo.Add(new SettlementCargo { source = source.id, resource = item.Item1, amount = amount });
        }
    }

    private static void Finish(Kingdom payer, StateSettlementData state, City target)
    {
        var plan = state.resettlement;
        City source = World.world.cities.get(plan.source);
        var population = CityPopulationSystem.Get(target);
        population.settlement_supplies ??= new Dictionary<string, int>();
        try
        {
            foreach (var cargo in plan.cargo)
            {
                if (cargo.delivered) continue;
                if (!cargo.taken)
                {
                    int before = Valid(source) && source.kingdom.id == plan.source_owner &&
                        target.kingdom.id == plan.target_owner && Managed(payer, target) ? source.getResourcesAmount(cargo.resource) : 0;
                    try
                    {
                        if (before > 0)
                            using (PopulationEconomySystem.PrivateUse()) source.takeResource(cargo.resource, Math.Min(before, cargo.amount));
                    }
                    finally
                    {
                        int taken = Math.Max(0, before - (before > 0 ? source.getResourcesAmount(cargo.resource) : 0));
                        cargo.in_transit = taken;
                        cargo.public_in_transit = taken > 0 ? PopulationEconomySystem.TakePublicStock(source, cargo.resource, taken) : 0;
                        cargo.taken = true;
                    }
                }
                population.settlement_supplies.TryGetValue(cargo.resource, out int have);
                population.settlement_supplies[cargo.resource] = have + cargo.in_transit;
                PopulationEconomySystem.AddPublicStock(target, cargo.resource, cargo.public_in_transit);
                cargo.delivered = true;
            }
            if (string.IsNullOrEmpty(CultureService.GetMainCulture(target, initialize: false)) && Valid(source))
                CultureService.SetCityMainCulture(target, CultureService.GetMainCulture(source));
            CityPopulationSystem.Census(target, population, keepBackground: true, preserveSmallGroups: true);
            if (Enabled)
            {
                CityPopulationSystem.EnsureLeader(target);
                CityPopulationSystem.Census(target, population, keepBackground: true, preserveSmallGroups: true);
                CityConstructionSystem.StartSettlement(target);
            }
            CityPopulationSystem.InvalidateHouseholdCaches();
            target.updateCityStatus();
            state.resettlement = null;
            state.status = "resettled";
            Works.Remove(payer.id);
            string message = string.Format(LM.Get("state_settlement_resettlement_history"), source?.GetCityName() ?? "?",
                (int)Math.Round(plan.people), target.GetCityName());
            LogService.LogInfo(message);
            payer.GetEmpire()?.RecordHistory(directContent: message, kingdomId: payer.id);
        }
        catch (Exception error)
        {
            state.status = state.resettlement == null ? "resettled" : "resettlement_recovery";
            LogService.LogWarning($"[EmpireCraft] 空城安置待恢复({target.id}): {error.Message}");
        }
    }

    private static void Requeue(long id, StateSettlementData state)
    { if (state.resettlement != null) Pending.Enqueue(id); }
    private static void Cancel(Kingdom payer, StateSettlementData state, string status)
    { state.resettlement = null; state.status = status; Works.Remove(payer.id); }
    public static void ResetWorldState() { Works.Clear(); Pending.Clear(); }
    public static void ModeChanged(bool enabled)
    {
        if (enabled) return;
        foreach (Kingdom payer in World.world.kingdoms)
        {
            var state = payer.GetOrCreate().settlement;
            if (state?.resettlement == null) continue;
            City target = World.world.cities.get(state.resettlement.target);
            if (state.resettlement.arrived && Valid(target)) Finish(payer, state, target);
            else Cancel(payer, state, "idle");
        }
    }
}
