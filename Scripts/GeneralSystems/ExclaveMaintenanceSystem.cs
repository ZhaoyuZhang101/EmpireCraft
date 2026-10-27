using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 两种人口模式共用年度维护。按区域邻接分帧扫描，跨封国的帝国陆路也算连通。
// 飞地独立(用户 2026-10-27 截图西辽汗国"飞地维护 202 区域"："都说了飞地要独立""这个西遼汗国的地盘隔这么远")：
//   · 与都城同在一块陆地、却被别国隔开的陆上飞地，每年结算时整片自立——整个在这片里的成员国连国离开帝国，
//     其余的城按原属国各自由当地首领立国(都城、正在打仗、被占领、神力拉入保护期内的那片今年不动)；
//   · 隔海的照旧交维护费，连年财政吃紧时也是整片自立，不再交给接壤的邻国(以前"交接给接壤的某国")；
//   · 不开财政也照样审计、照样让陆上飞地自立，只是不收维护费；
//   · 正统崩坏(帝国正统不到 FalterLegitimacy，同日用户截图神圣瑞典帝国正统 0、苛政满额，"明摆着已经没法控制这么多国家，
//     领土已经缩回法理地了，维持飞地以及这些地方国王服从他也没任何好处，为什么他不主动放弃自己的海外领土，以及其他国王为什么不独立"
//     "不这么做这个地块真的很乱")：隔海的领土也整片放弃(自立)，都城在本帝国法理之外、或远在中央控制范围之外的成员国一律自立。
public static class ExclaveMaintenanceSystem
{
    private const int FalterLegitimacy = 30;
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
        if (kingdom?.data == null || kingdom.isRekt() ||
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
        // 陆上飞地整片自立；正统崩坏的帝国隔海的也放弃(见文件头)
        WorldTile seat = work.capital.getTile();
        Layer.Empire empire = work.realm.IsEmpire() ? work.realm.GetEmpire() : null;
        bool faltering = empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.Legitimacy < FalterLegitimacy;
        foreach (List<TileZone> part in remote.Where(part => faltering || seat != null && part.FirstOrDefault()?.centerTile is WorldTile at &&
                                                              at.isSameIsland(seat)).ToList())
            if (Secede(work, part)) remote.Remove(part);
        if (faltering) ReleaseDistantMembers(empire, work.realm);
        var state0 = work.realm.GetOrCreate().exclave_maintenance ??= new ExclaveMaintenanceData();
        if (!TreasurySystem.Enabled(work.realm))
        {
            state0.last_settled = World.world.getCurWorldTime();
            state0.zones = remote.Sum(part => part.Count);
            return;
        }
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
        // 每年最多放走一个连通片，先处理后勤负担最大的一片：整片自立(以前交接给接壤的邻国)
        foreach (var pair in fees)
            if (Secede(work, pair.part)) { state.weak_years = 0; break; }
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

    // 正统崩坏：都城在本帝国法理之外、或远在中央控制范围之外的成员国一律自立(正在与中央交战、神力拉入保护期内的不算)
    private static void ReleaseDistantMembers(Layer.Empire empire, Kingdom core)
    {
        EmpireCore own = EmpireCoreManager.Get(empire);
        var gone = new List<Kingdom>();
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member?.data == null || member.isRekt() || member == core || member.capital == null || member.isInWarWith(core) ||
                MembershipCooldown.ManualJoinActive(member) || AncientWarfareCompatibility.Owns(member)) continue;
            bool outside = own != null && member.capital.GetEmpireCore() != own;
            if (!outside && !AdministrativeControlSystem.OutOfReach(core, member.capital)) continue;
            member.FinishedSelfPlot();
            empire.leave(member, true, false);
            if (member.IsInEmpire()) continue;
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            gone.Add(member);
        }
        if (gone.Count == 0) return;
        UpheavalGuard.Mark(empire, "正统崩坏，远方诸侯自立", detail: $"走 {gone.Count} 国");
        string names = string.Join("、", gone.Take(6).Select(member => member.GetKingdomName())) + (gone.Count > 6 ? $"等{gone.Count}国" : "");
        string text = string.Format(LM.Get("faltering_members_history"), empire.GetEmpireName(), empire.Legitimacy, names);
        empire.RecordHistory(directContent: text);
        TranslateHelper.LogMajor(text, core);
        LogService.LogInfo($"[EmpireCraft][正统崩坏] {empire.GetEmpireName()}(正统{empire.Legitimacy}) 法理之外、鞭长莫及的成员自立：" +
                           string.Join("、", gone.Select(member => member.GetKingdomName())));
    }

    private static bool Front(City city, Kingdom realm)
    {
        if (!city.kingdom.hasEnemies() && !realm.hasEnemies()) return false;
        foreach (City next in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
            if (next?.kingdom != null && !next.isRekt() && (next.kingdom.isInWarWith(city.kingdom) || next.kingdom.isInWarWith(realm)))
                return true;
        return false;
    }

    // 一片飞地整片自立：整个在这片里的成员国连国离开帝国，其余的城按原属国各自由当地首领立国；返回是否走了
    private static bool Secede(Audit work, List<TileZone> part)
    {
        var zones = new HashSet<TileZone>(part);
        var cities = part.Select(zone => zone.city).Where(city => city != null).Distinct().ToList();
        if (cities.Count == 0) return false;
        // 神力拉入未满期的成员不走(见 MembershipCooldown)；挨着敌国(前线上)、被占领的今年不动；城的地跨了几片的不动。
        // 以前只要在打仗就不动，帝国常年有仗，远方的飞地就年年留着(用户 2026-10-27 截图拜占庭远在东方的行省)
        if (cities.Any(city => !Valid(city, work.realm) || city == work.realm.capital || Front(city, work.realm) ||
                               city.GetOrCreate().OccupiedStatus.Count > 0 || MembershipCooldown.ManualJoinActive(city.kingdom) ||
                               city.zones.Any(zone => zone.tiles_with_ground > 0 && !zones.Contains(zone)))) return false;
        var gone = new List<string>();
        foreach (var group in cities.GroupBy(city => city.kingdom).ToList())
        {
            Kingdom owner = group.Key;
            var held = new HashSet<City>(group);
            if (owner != work.realm && owner.cities.All(held.Contains))
            {
                Layer.Empire empire = owner.GetEmpire();
                owner.FinishedSelfPlot();
                empire?.leave(owner, true, false);
                if (owner.IsInEmpire()) continue;
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(owner);
                gone.Add(owner.GetKingdomName());
                continue;
            }
            // 原属国的都城在这片里、别处还有城：都城不动，其余的走
            List<City> cluster = group.Where(city => city != owner.capital)
                .OrderByDescending(city => city.CountLivingPopulation()).ThenBy(city => city.id).ToList();
            if (cluster.Count == 0) continue;
            Kingdom local = AdministrativeControlSystem.ReleaseToLocal(work.realm, cluster, _ => "independent", out _);
            if (local != null) gone.Add(local.GetKingdomName());
        }
        if (gone.Count == 0) return false;
        string names = string.Join("、", cities.Select(city => city.data.name));
        EventRecorder.Record(work.realm.GetEmpire(), logKingdom: work.realm, text: string.Format(
            LM.Get("exclave_independence_history"), work.realm.GetKingdomName(), names, string.Join("、", gone)));
        LogService.LogInfo($"[EmpireCraft][飞地独立] {work.realm.GetKingdomName()} 的飞地 {names} 自立为 {string.Join("、", gone)}");
        return true;
    }
}
