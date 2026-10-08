using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 两种人口模式共用年度维护。按区域邻接分帧扫描，跨封国的帝国陆路也算连通。
public static class ExclaveMaintenanceSystem
{
    private sealed class Audit
    {
        public Kingdom realm;
        public City capital;
        public City[] cities;
        public int cityCursor, zoneCursor;
        public readonly HashSet<TileZone> zones = new();
        public readonly HashSet<TileZone> seen = new();
        public readonly Queue<TileZone> frontier = new();
        public readonly List<List<TileZone>> components = new();
        public IEnumerator<TileZone> roots;
        public List<TileZone> component;
    }

    private static readonly Dictionary<long, Audit> Audits = new();
    private static readonly Queue<long> Pending = new();
    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;
    private static bool Valid(City city, Kingdom realm) => city?.data != null && !city.isRekt() &&
        !AncientWarfareCompatibility.OwnsObject(city) && Realm(city.kingdom) == realm;

    public static void ResetWorldState() { Audits.Clear(); Pending.Clear(); }

    public static void ModeChanged()
    {
        ResetWorldState();
        // 人口统计来源变化不等于新财政年度，保留已付维护费和压力记录。
    }

    public static string Status(Kingdom kingdom)
    {
        var state = kingdom == Realm(kingdom) ? kingdom?.GetOrCreate().exclave_maintenance : null;
        if (state == null || state.last_settled < 0d) return null;
        string text = string.Format(LM.Get("exclave_maintenance_format"), state.zones, MoneyDisplay.Format(state.annual_cost));
        if (state.unpaid > 0) text += " · " + string.Format(LM.Get("exclave_unpaid_format"), MoneyDisplay.Format(state.unpaid));
        return text;
    }

    public static void Check(Kingdom kingdom)
    {
        if (!TreasurySystem.Enabled(kingdom) || kingdom?.data == null || kingdom.isRekt() ||
            AncientWarfareCompatibility.Owns(kingdom) || Realm(kingdom) != kingdom ||
            !Valid(kingdom.capital, kingdom) || Audits.ContainsKey(kingdom.id)) return;
        ExclaveMaintenanceData state = kingdom.GetOrCreate().exclave_maintenance;
        double now = World.world.getCurWorldTime();
        if (state?.last_settled >= 0d && now >= state.last_settled && Date.getYearsSince(state.last_settled) < 1)
            return;
        var cities = StateSettlementSystem.ExpansionCities(kingdom).Where(city => Valid(city, kingdom)).Distinct().ToArray();
        Audits[kingdom.id] = new Audit { realm = kingdom, capital = kingdom.capital, cities = cities };
        Pending.Enqueue(kingdom.id);
    }

    public static void Tick()
    {
        if (World.world == null || ModClass.IS_CLEAR) { ResetWorldState(); return; }
        if (!SimulationFrameBudget.HasTime || Pending.Count == 0) return;
        long id = Pending.Dequeue();
        if (!Audits.TryGetValue(id, out Audit work)) return;
        try { Advance(id, work); }
        catch (Exception error)
        {
            Audits.Remove(id);
            LogService.LogWarning($"[EmpireCraft] 飞地审计失败({id}): {error.Message}");
        }
    }

    private static void Advance(long id, Audit work)
    {
        if (work.realm.isRekt() || Realm(work.realm) != work.realm || work.realm.capital != work.capital ||
            !Valid(work.capital, work.realm)) { Audits.Remove(id); return; }
        int budget = 128;
        while (work.cityCursor < work.cities.Length && budget > 0 && SimulationFrameBudget.HasTime)
        {
            City city = work.cities[work.cityCursor];
            if (!Valid(city, work.realm)) { Audits.Remove(id); return; }
            if (work.zoneCursor >= city.zones.Count) { work.cityCursor++; work.zoneCursor = 0; budget--; continue; }
            TileZone zone = city.zones[work.zoneCursor++];
            if (zone?.city == city && zone.tiles_with_ground > 0) work.zones.Add(zone);
            budget--;
        }
        if (work.cityCursor < work.cities.Length) { Pending.Enqueue(id); return; }
        work.roots ??= work.zones.GetEnumerator();
        while (budget-- > 0 && SimulationFrameBudget.HasTime)
        {
            if (work.frontier.Count == 0)
            {
                TileZone root = null;
                bool exhausted = false;
                while (budget > 0)
                {
                    if (!work.roots.MoveNext()) { exhausted = true; break; }
                    if (!work.seen.Contains(work.roots.Current)) { root = work.roots.Current; break; }
                    budget--;
                }
                if (root == null)
                {
                    if (exhausted) { Finish(work); Audits.Remove(id); }
                    else Pending.Enqueue(id);
                    return;
                }
                work.component = new List<TileZone>();
                work.components.Add(work.component);
                work.seen.Add(root); work.frontier.Enqueue(root);
            }
            TileZone current = work.frontier.Dequeue();
            if (!Valid(current.city, work.realm)) { Audits.Remove(id); return; }
            work.component.Add(current);
            foreach (TileZone next in current.neighbours ?? Array.Empty<TileZone>())
                if (next != null && work.zones.Contains(next) && work.seen.Add(next)) work.frontier.Enqueue(next);
        }
        Pending.Enqueue(id);
    }

    private static void Finish(Audit work)
    {
        // 分帧期间版图发生变化就重做，不能按旧归属扣钱或割让。
        if (work.cities.Any(city => !Valid(city, work.realm)) ||
            work.zones.Any(zone => !Valid(zone.city, work.realm) || !zone.city.zones.Contains(zone)) ||
            work.cities.Sum(city => city.zones.Count(zone => zone?.city == city && zone.tiles_with_ground > 0)) != work.zones.Count)
            return;
        List<TileZone> home = work.components.FirstOrDefault(part => part.Contains(work.capital.getTile()?.zone));
        if (home == null) return;
        var remote = work.components.Where(part => part != home).ToList();
        var membership = new Dictionary<TileZone, int>();
        for (int i = 0; i < work.components.Count; i++)
            foreach (TileZone zone in work.components[i]) membership[zone] = i;
        // 两片在扫描期间新接通时取消旧快照，避免把已经接回本土的城市送走。
        foreach (TileZone zone in work.zones)
            foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                if (next != null && membership.TryGetValue(next, out int group) && group != membership[zone]) return;
        var fees = remote.Select(part => (part, fee: Cost(work, part))).OrderByDescending(pair => pair.fee).ToList();
        int cost = (int)Math.Min(int.MaxValue, fees.Sum(pair => (long)pair.fee));
        var state = work.realm.GetOrCreate().exclave_maintenance ??= new ExclaveMaintenanceData();
        double now = World.world.getCurWorldTime();
        int money = work.realm.GetMoney();
        var fiscal = TreasurySystem.Report(work.realm);
        bool evidence = fiscal.months >= 12 && state.last_settled >= 0d && now >= state.last_settled &&
            Date.getYearsSince(state.last_settled) >= 1;
        // 建设、科研投资和追回赃款不影响经常收支。飞地维护另行比较，避免重复计算。
        state.domestic_balance = fiscal.operating_balance + fiscal.maintenance_expense;
        state.weak_years = evidence && ExclaveMaintenanceRules.FiscalPressure(money, cost, state.domestic_balance)
            ? Math.Min(100, state.weak_years + 1) : 0;
        state.last_settled = now;
        state.annual_cost = cost; state.zones = remote.Sum(part => part.Count);
        state.paid = Math.Min(Math.Max(0, money), cost); state.unpaid = cost - state.paid;
        if (state.paid > 0) work.realm.SubMoney(state.paid, TreasuryCategory.Maintenance);
        state.treasury_after = work.realm.GetMoney();
        if (!ExclaveMaintenanceRules.ShouldRelease(state.weak_years, evidence, work.realm.hasEnemies())) return;
        // 每年最多交接一个连通片，先处理后勤负担最大的一片；不创造新国家、不拆人口。
        foreach (var pair in fees)
            if (TryRelease(work, pair.part)) { state.weak_years = 0; break; }
    }

    private static int Cost(Audit work, List<TileZone> part)
    {
        var home = work.capital.getTile();
        double households = 0d, distance = 0d;
        bool overseas = false;
        foreach (var grouping in part.GroupBy(zone => zone.city))
        {
            City city = grouping.Key;
            households += CityPopulationSystem.Households(city) * (double)grouping.Count() / Math.Max(1, city.zones.Count);
            WorldTile tile = grouping.First().centerTile;
            if (tile == null) continue;
            distance = Math.Max(distance, Math.Sqrt((double)(tile.x - home.x) * (tile.x - home.x) +
                                                   (double)(tile.y - home.y) * (tile.y - home.y)));
            overseas |= !tile.isSameIsland(home);
        }
        return ExclaveMaintenanceRules.AnnualCost(part.Count, households, distance, overseas);
    }

    private static bool TryRelease(Audit work, List<TileZone> part)
    {
        var zones = new HashSet<TileZone>(part);
        var cities = part.Select(zone => zone.city).Distinct().ToArray();
        // 本国首都不可交接。整个远方封国都在同一飞地片中时，才允许连同其首都整体移交。
        if (cities.Any(city => !Valid(city, work.realm) ||
            city == city.kingdom.capital && (city.kingdom == work.realm ||
                city.kingdom.cities.Any(held => !Valid(held, work.realm) || held.zones.Any(zone => !zones.Contains(zone)))) ||
            city.zones.Any(zone => zone.tiles_with_ground > 0 && !zones.Contains(zone)) ||
            city.kingdom.hasEnemies() || city.GetOrCreate().OccupiedStatus.Count > 0)) return false;
        var contacts = new Dictionary<Kingdom, int>();
        foreach (TileZone zone in part)
            foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
            {
                Kingdom neighbour = next?.city?.kingdom;
                if (neighbour?.data == null || neighbour.isRekt() || neighbour.wild || Realm(neighbour) == work.realm ||
                    AncientWarfareCompatibility.Owns(neighbour) || AncientWarfareCompatibility.OwnsObject(next.city) ||
                    neighbour.hasEnemies()) continue;
                contacts.TryGetValue(neighbour, out int border);
                contacts[neighbour] = border + 1;
            }
        Kingdom receiver = contacts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.id)
            .Select(pair => pair.Key).FirstOrDefault();
        if (receiver == null) return false;
        var formerMembers = cities.Select(city => city.kingdom).Distinct().ToArray();
        string names = string.Join("、", cities.Select(city => city.data.name));
        foreach (City city in cities) city.joinAnotherKingdom(receiver);
        if (!cities.All(city => city.kingdom == receiver)) return false;
        foreach (Kingdom member in formerMembers)
            if (member != work.realm && member.cities.Count == 0 && member.GetEmpire() == work.realm.GetEmpire())
                member.GetEmpire()?.leave(member);
        EventRecorder.Record(work.realm.GetEmpire(), logKingdom: work.realm, text: string.Format(
            LM.Get("exclave_release_history"), work.realm.data.name, names, receiver.data.name));
        return true;
    }
}
