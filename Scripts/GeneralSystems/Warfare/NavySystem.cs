using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 海军(借鉴钢铁雄心)：与军团同在无小人模式 + 帝国占领模式下运行，每月结算(先于陆上战线)。
//   · 舰队：每个港口城(有码头)一支，战舰、运输船都只是数字；母港每月造舰，花国库，按港口数与战备定额；
//     舰种随科技：楼船 → 远洋帆舰(远洋航海) → 蒸汽铁甲(工业化) → 现代舰艇(电力或机动化)，战力 1 / 1.5 / 2.5 / 4；
//   · 任务：太平驻港；交战时最强的舰队为本国登陆护航(打通航线)，其余去封锁最近的敌港，组织度太低回港休整；
//     舰队每月沿海区航行至多 3 个海区；
//   · 海战：同一海区敌我舰队相遇即交战，战力 = 战舰 × 舰种 × 组织度(运输船几乎没有战力)，按胜算损舰、掉组织度；
//     运输船在败方损失最重，船上的登陆部队随之折损；
//   · 制海权：海区里只剩一方的舰队就归它；三个月没有舰队就失效；
//   · 运兵(硬限制，见 FrontSystem)：跨海登陆要有一条从本国港口出发、不经过敌方制海海区的航线，
//     且运输船的总运力(每艘 1500 人 × 舰种)装得下派去登陆的军；
//   · 封锁：港口外的海区被敌方控制，港口的海上商路中断、跨海建城暂停，海外登陆部队断了补给。
//   · 旗舰：开着「军团统领实体」时每支舰队一艘旗舰(原版运兵船，挂在母港码头)跟着兵牌走；关掉就只有兵牌。
public static class NavySystem
{
    private const int MaxRegionsPerMonth = 3;
    private const int ControlMonths = 3;
    private const float TransportCapacityBase = 1500f;
    private const float RetreatOrg = 25f;
    private const float PortOrg = 15f;
    private const float SeaOrg = 4f;
    private const float FlagshipMoveInterval = 3f;

    public sealed class BattleView
    {
        public int region_id = -1;
        public int zone_id = -1;
        public long attacker_id = -1L, defender_id = -1L;
        public float ratio;
        public int attacker_ships, defender_ships, sunk_attacker, sunk_defender;
        public double time;
    }

    public static readonly Dictionary<int, BattleView> Battles = new();
    private static readonly Dictionary<long, float> FlagshipMoves = new();
    private static readonly Dictionary<long, (long kingdom, long fleet)> FlagshipIndex = new();
    // 本国上月想登陆的敌城(由 FrontSystem 记下)，舰队据此护航
    public static readonly Dictionary<long, long> DesiredLanding = new();

    public static void ResetWorldState()
    {
        Battles.Clear();
        FlagshipMoves.Clear();
        FlagshipRetry.Clear();
        StuckSeconds.Clear();
        FlagshipIndex.Clear();
        DesiredLanding.Clear();
        SeaRegionSystem.ResetWorldState();
    }

    #region 查询

    private static readonly List<FleetData> Empty = new();

    public static List<FleetData> Of(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return Empty;
        return kingdom.GetOrCreate().fleets ??= new List<FleetData>();
    }

    public static IEnumerable<(Kingdom kingdom, FleetData fleet)> All()
    {
        if (World.world?.kingdoms == null) yield break;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            List<FleetData> list = kingdom.GetOrCreate().fleets;
            if (list == null) continue;
            foreach (FleetData fleet in list) yield return (kingdom, fleet);
        }
    }

    public static City Home(FleetData fleet) => World.world?.cities?.get(fleet.home_city_id) is City city &&
                                                city.data != null && !city.isRekt() ? city : null;

    public static Building Port(City city) => SettlementSeaSearch.Port(city);

    public static IEnumerable<City> Ports(Kingdom kingdom) =>
        (kingdom?.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt() && Port(city) != null);

    public static bool IsFlagship(Actor actor) => actor?.data != null && FlagshipIndex.ContainsKey(actor.id);

    // 舰种(战力倍率)：楼船 1 / 远洋帆舰 1.5 / 蒸汽铁甲 2.5 / 现代舰艇 4
    public static float Tier(Kingdom kingdom)
    {
        string culture = kingdom?.capital == null ? null : TechnologySystem.GetCultureOf(kingdom.capital);
        if (culture == null) return 1f;
        if (TechnologySystem.HasTech(culture, "electricity") || TechnologySystem.HasTech(culture, "motorization")) return 4f;
        if (TechnologySystem.HasTech(culture, SettlementSeaSearch.IndustrialNavigationTech)) return 2.5f;
        return TechnologySystem.HasTech(culture, SettlementSeaSearch.OceanNavigationTech) ? 1.5f : 1f;
    }

    public static string TierKey(Kingdom kingdom) => Tier(kingdom) switch
    {
        >= 4f => "navy_tier_3",
        >= 2.5f => "navy_tier_2",
        >= 1.5f => "navy_tier_1",
        _ => "navy_tier_0"
    };

    // 本国(连同宗主帝国中央)运输船能装下多少人
    public static float TransportCapacity(Kingdom kingdom)
    {
        if (kingdom == null) return 0f;
        float capacity = Of(kingdom).Sum(fleet => fleet.transports) * TransportCapacityBase * Tier(kingdom);
        Kingdom realm = FrontSystem.Realm(kingdom);
        if (realm != null && realm != kingdom)
            capacity += Of(realm).Sum(fleet => fleet.transports) * TransportCapacityBase * Tier(realm);
        return capacity;
    }

    private static float Power(Kingdom kingdom, FleetData fleet) =>
        fleet.warships * Tier(kingdom) * (0.3f + 0.7f * Mathf.Clamp01(fleet.org / 100f)) + fleet.transports * 0.05f;

    // 制海权在谁手里(过期的不算)
    public static Kingdom Controller(SeaRegionSystem.Region region)
    {
        if (region == null || region.ControllerId < 0) return null;
        if (region.ControlledAt < 0d || Date.getMonthsSince(region.ControlledAt) > ControlMonths) return null;
        Kingdom kingdom = World.world.kingdoms.get(region.ControllerId);
        return kingdom?.data != null && !kingdom.isRekt() ? kingdom : null;
    }

    private static bool HostileControl(Kingdom side, SeaRegionSystem.Region region)
    {
        Kingdom controller = Controller(region);
        return controller != null && FrontSystem.Hostile(side, controller);
    }

    // 港口被封锁：港外任一海区在敌方手里
    public static bool IsBlockaded(City city)
    {
        if (!CorpsSystem.Enabled || city?.kingdom == null || Port(city) == null) return false;
        return SeaRegionSystem.RegionsAround(city).Any(region => HostileControl(city.kingdom, region));
    }

    // 本国(连同宗主中央)可用的出海口
    private static List<SeaRegionSystem.Region> HomeWaters(Kingdom kingdom)
    {
        var result = new List<SeaRegionSystem.Region>();
        Kingdom realm = FrontSystem.Realm(kingdom);
        foreach (Kingdom side in realm != null && realm != kingdom ? new[] { kingdom, realm } : new[] { kingdom })
            foreach (City port in Ports(side))
                result.AddRange(SeaRegionSystem.RegionsAround(port));
        return result.Distinct().ToList();
    }

    // 跨海登陆的航线：从本国港口出发、不经过敌方制海海区到目标城外的海区；没有港口或航线被截断返回 null
    public static List<SeaRegionSystem.Region> LandingRoute(Kingdom side, City target)
    {
        if (side == null || target == null) return null;
        List<SeaRegionSystem.Region> start = HomeWaters(side);
        if (start.Count == 0) return null;
        List<SeaRegionSystem.Region> goal = SeaRegionSystem.RegionsAround(target);
        if (goal.Count == 0) return null;
        return SeaRegionSystem.Route(start, goal, region => HostileControl(side, region));
    }

    // 海外的军(与驻地不在同一块陆地上)回国的航线被敌方制海截断：断了补给
    public static bool IsCutOff(Kingdom kingdom, CorpsData corps)
    {
        if (!CorpsSystem.Enabled || kingdom == null || corps == null) return false;
        TileZone zone = CorpsSystem.Zone(corps);
        WorldTile home = CorpsSystem.Home(corps)?.getTile();
        if (zone?.centerTile == null || home == null || zone.city == null || SeaRegionSystem.IsSea(zone)) return false;
        if (zone.centerTile.isSameIsland(home)) return false;
        return LandingRoute(kingdom, zone.city) == null;
    }

    // 不论敌方制海、只看海路通不通(护航舰队找要打通的海区用)
    private static List<SeaRegionSystem.Region> AnyRoute(Kingdom side, City target)
    {
        List<SeaRegionSystem.Region> start = HomeWaters(side);
        List<SeaRegionSystem.Region> goal = SeaRegionSystem.RegionsAround(target);
        return start.Count == 0 || goal.Count == 0 ? null : SeaRegionSystem.Route(start, goal);
    }

    #endregion

    #region 月度

    public static void MonthTick(double now)
    {
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                Compatibility.AncientWarfareCompatibility.OwnsObject(kingdom)) continue;
            List<City> ports = Ports(kingdom).ToList();
            List<FleetData> list = kingdom.GetOrCreate().fleets;
            if (ports.Count == 0 && (list == null || list.Count == 0)) continue;
            try
            {
                Organize(kingdom, ports, now);
                Build(kingdom, ports);
                Assign(kingdom);
                Sail(kingdom);
                Recover(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][海军] {kingdom.data.name} 结算失败: {exception.Message}");
            }
        }
        try { Fight(now); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][海军] 海战结算失败: {exception.Message}"); }
        UpdateControl(now);
        foreach (int id in Battles.Where(pair => Date.getMonthsSince(pair.Value.time) > 1).Select(pair => pair.Key).ToList())
            Battles.Remove(id);
    }

    // 每个港口一支舰队；港口丢了，舰队改驻别的港口，没有港口就逐月损耗
    private static void Organize(Kingdom kingdom, List<City> ports, double now)
    {
        List<FleetData> list = Of(kingdom);
        foreach (FleetData fleet in list.ToList())
        {
            City home = Home(fleet);
            if (home != null && home.kingdom == kingdom && Port(home) != null) continue;
            City other = ports.Where(port => list.All(existing => existing == fleet || existing.home_city_id != port.id))
                             .OrderBy(port => SeaRegionSystem.Distance(SeaRegionSystem.Get(fleet.region_id),
                                 SeaRegionSystem.RegionsAround(port).FirstOrDefault())).FirstOrDefault()
                         ?? ports.FirstOrDefault();
            if (other != null)
            {
                fleet.home_city_id = other.id;
                continue;
            }
            // 无港可归：每月折损两成，打光就解散
            fleet.warships = Mathf.FloorToInt(fleet.warships * 0.8f);
            fleet.transports = Mathf.FloorToInt(fleet.transports * 0.8f);
            fleet.task = "retreat";
            if (fleet.warships + fleet.transports <= 0) Disband(kingdom, fleet);
        }
        // 同一港口多支舰队并成一支
        foreach (var group in list.GroupBy(fleet => fleet.home_city_id).Where(group => group.Count() > 1).ToList())
        {
            FleetData keep = group.OrderByDescending(fleet => fleet.warships).First();
            foreach (FleetData extra in group.Where(fleet => fleet != keep).ToList())
            {
                keep.warships += extra.warships;
                keep.transports += extra.transports;
                Disband(kingdom, extra);
            }
        }
        foreach (City port in ports)
        {
            if (list.Any(fleet => fleet.home_city_id == port.id)) continue;
            SeaRegionSystem.Region water = SeaRegionSystem.RegionsAround(port).FirstOrDefault();
            if (water == null) continue;
            list.Add(new FleetData
            {
                id = HelperFunc.OverallHelperFunc.IdGenerator.NextId(), serial = ++kingdom.GetOrCreate().fleet_serial,
                kingdom_id = kingdom.id, home_city_id = port.id, region_id = water.Id, target_region_id = water.Id,
                zone_id = water.Center?.id ?? -1
            });
        }
    }

    // 造舰：每港定额(太平 战舰 2 + 运输 1；交战或战备 战舰 6 + 运输 4，有跨海登陆的需要再多 4 艘运输)，
    // 港口每升一级定额 +50%；每港每月下水 0.5 艘(交战 1 艘)，先造缺得多的；钱从国库出，没钱就停
    private static void Build(Kingdom kingdom, List<City> ports)
    {
        bool war = CorpsSystem.IsAtWar(kingdom);
        bool landing = DesiredLanding.ContainsKey(kingdom.id);
        float tier = Tier(kingdom);
        foreach (FleetData fleet in Of(kingdom))
        {
            City home = Home(fleet);
            Building port = home == null ? null : Port(home);
            if (port == null || fleet.task == "retreat" && fleet.region_id != HomeRegion(fleet)?.Id) continue;
            float level = 1f + 0.5f * Mathf.Clamp(port.asset.upgrade_level, 0, 3);
            int wantWar = Mathf.RoundToInt((war ? 6 : 2) * level);
            int wantTransport = Mathf.RoundToInt(((war ? 4 : 1) + (landing ? 4 : 0)) * level);
            int lackWar = wantWar - fleet.warships, lackTransport = wantTransport - fleet.transports;
            if (lackWar <= 0 && lackTransport <= 0) continue;
            fleet.build_progress += war ? 1f : 0.5f;
            while (fleet.build_progress >= 1f && (lackWar > 0 || lackTransport > 0))
            {
                bool warship = lackWar >= lackTransport;
                int cost = Mathf.RoundToInt((warship ? 15f : 6f) * tier);
                if (!TreasurySystem.TrySpend(kingdom, cost, TreasuryCategory.Military)) { fleet.build_progress = 0f; break; }
                fleet.build_progress -= 1f;
                if (warship) { fleet.warships++; lackWar--; }
                else { fleet.transports++; lackTransport--; }
            }
            if (lackWar <= 0 && lackTransport <= 0) fleet.build_progress = 0f;
        }
    }

    private static SeaRegionSystem.Region HomeRegion(FleetData fleet)
    {
        City home = Home(fleet);
        SeaRegionSystem.Region current = SeaRegionSystem.Get(fleet.region_id);
        return home == null ? null : SeaRegionSystem.RegionsAround(home)
            .OrderBy(region => SeaRegionSystem.Distance(region, current)).FirstOrDefault();
    }

    private static void Assign(Kingdom kingdom)
    {
        List<FleetData> list = Of(kingdom);
        foreach (FleetData fleet in list)
            if (fleet.org <= RetreatOrg && fleet.task != "retreat")
            {
                fleet.task = "retreat";
                FrontSystem.Log(kingdom, string.Format(LM.Get("navy_log_retreat"), Name(fleet)));
            }
        bool war = kingdom.hasEnemies() || FrontSystem.Realm(kingdom)?.hasEnemies() == true;
        var ready = list.Where(fleet => fleet.task != "retreat" && fleet.warships > 0).ToList();
        foreach (FleetData fleet in list.Where(fleet => fleet.task == "retreat"))
        {
            fleet.target_region_id = HomeRegion(fleet)?.Id ?? fleet.region_id;
            if (fleet.org >= 60f && fleet.region_id == fleet.target_region_id) fleet.task = "port";
        }
        if (!war)
        {
            DesiredLanding.Remove(kingdom.id);
            foreach (FleetData fleet in list.Where(fleet => fleet.task != "retreat"))
            {
                fleet.task = "port";
                fleet.target_region_id = HomeRegion(fleet)?.Id ?? fleet.region_id;
            }
            return;
        }
        var assigned = new HashSet<FleetData>();
        // ① 护航：本国要跨海登陆时，最强的舰队去打通航线(先去航线上被敌方控制的海区，没有就守在登陆点外)
        if (DesiredLanding.TryGetValue(kingdom.id, out long cityId) && World.world.cities.get(cityId) is City target)
        {
            List<SeaRegionSystem.Region> route = AnyRoute(kingdom, target);
            if (route != null && route.Count > 0)
            {
                SeaRegionSystem.Region contested = route.FirstOrDefault(region => HostileControl(kingdom, region)) ?? route.Last();
                foreach (FleetData fleet in ready.OrderByDescending(fleet => fleet.warships).Take(Mathf.Max(1, ready.Count / 2)))
                {
                    fleet.task = "escort";
                    fleet.target_region_id = contested.Id;
                    assigned.Add(fleet);
                }
            }
        }
        // ② 封锁：其余舰队去最近的敌港外
        List<SeaRegionSystem.Region> enemyWaters = World.world.kingdoms
            .Where(other => other?.data != null && !other.isRekt() && FrontSystem.Hostile(kingdom, other))
            .SelectMany(Ports).SelectMany(SeaRegionSystem.RegionsAround).Distinct().ToList();
        foreach (FleetData fleet in ready.Where(fleet => !assigned.Contains(fleet)))
        {
            SeaRegionSystem.Region current = SeaRegionSystem.Get(fleet.region_id);
            List<SeaRegionSystem.Region> path = enemyWaters.Count == 0 || current == null ? null
                : SeaRegionSystem.Route(new[] { current }, enemyWaters);
            if (path == null || path.Count == 0 || fleet.warships < 3 || fleet.org < 50f)
            {
                fleet.task = "port";
                fleet.target_region_id = HomeRegion(fleet)?.Id ?? fleet.region_id;
                continue;
            }
            fleet.task = "blockade";
            fleet.target_region_id = path.Last().Id;
        }
    }

    // 每月至多航行 3 个海区
    private static void Sail(Kingdom kingdom)
    {
        foreach (FleetData fleet in Of(kingdom))
        {
            SeaRegionSystem.Region current = SeaRegionSystem.Get(fleet.region_id);
            SeaRegionSystem.Region target = SeaRegionSystem.Get(fleet.target_region_id);
            if (current == null)
            {
                SeaRegionSystem.Region home = HomeRegion(fleet);
                if (home == null) continue;
                fleet.region_id = home.Id;
                fleet.zone_id = home.Center?.id ?? -1;
                continue;
            }
            if (target == null || target == current) continue;
            List<SeaRegionSystem.Region> path = SeaRegionSystem.Route(new[] { current }, new[] { target });
            if (path == null || path.Count < 2)
            {
                fleet.target_region_id = current.Id;
                continue;
            }
            fleet.region_id = path[Mathf.Min(MaxRegionsPerMonth, path.Count - 1)].Id;
        }
    }

    private static void Recover(Kingdom kingdom)
    {
        foreach (FleetData fleet in Of(kingdom))
        {
            bool inPort = fleet.region_id == HomeRegion(fleet)?.Id;
            fleet.org = Mathf.Min(100f, fleet.org + (inPort ? PortOrg : SeaOrg));
        }
    }

    // 海战：同一海区里敌对的两方(各取与主力友好的舰队)交战
    private static void Fight(double now)
    {
        var byRegion = All().Where(entry => entry.fleet.warships + entry.fleet.transports > 0 && entry.fleet.region_id >= 0)
            .GroupBy(entry => entry.fleet.region_id).ToList();
        foreach (var group in byRegion)
        {
            var entries = group.ToList();
            if (entries.Count < 2) continue;
            var lead = entries.OrderByDescending(entry => Power(entry.kingdom, entry.fleet)).First();
            var enemies = entries.Where(entry => FrontSystem.Hostile(lead.kingdom, entry.kingdom)).ToList();
            if (enemies.Count == 0) continue;
            var foe = enemies.OrderByDescending(entry => Power(entry.kingdom, entry.fleet)).First();
            var sideA = entries.Where(entry => !FrontSystem.Hostile(lead.kingdom, entry.kingdom) &&
                                               FrontSystem.Hostile(foe.kingdom, entry.kingdom)).ToList();
            var sideB = entries.Where(entry => FrontSystem.Hostile(lead.kingdom, entry.kingdom) &&
                                               !FrontSystem.Hostile(foe.kingdom, entry.kingdom)).ToList();
            if (sideA.Count == 0 || sideB.Count == 0) continue;
            float a = sideA.Sum(entry => Power(entry.kingdom, entry.fleet));
            float b = sideB.Sum(entry => Power(entry.kingdom, entry.fleet));
            float ratio = a + b <= 0f ? 0.5f : a / (a + b);
            int shipsA = sideA.Sum(entry => entry.fleet.warships + entry.fleet.transports);
            int shipsB = sideB.Sum(entry => entry.fleet.warships + entry.fleet.transports);
            int sunkA = Damage(sideA, 1f - ratio), sunkB = Damage(sideB, ratio);
            SeaRegionSystem.Region region = SeaRegionSystem.Get(group.Key);
            Battles[group.Key] = new BattleView
            {
                region_id = group.Key, zone_id = region?.Center?.id ?? -1, attacker_id = lead.kingdom.id,
                defender_id = foe.kingdom.id, ratio = ratio, attacker_ships = shipsA, defender_ships = shipsB,
                sunk_attacker = sunkA, sunk_defender = sunkB, time = now
            };
            Kingdom winner = ratio >= 0.5f ? lead.kingdom : foe.kingdom, loser = winner == lead.kingdom ? foe.kingdom : lead.kingdom;
            string text = string.Format(LM.Get("navy_log_battle"), winner.GetKingdomName(), loser.GetKingdomName(),
                ratio >= 0.5f ? sunkB : sunkA, ratio >= 0.5f ? sunkA : sunkB);
            FrontSystem.Log(winner, text);
            FrontSystem.Log(loser, text);
        }
    }

    // 一方按"对方占优程度"损舰、掉组织度；运输船损失加倍，船上的登陆部队随之折损。返回沉没的船数
    private static int Damage(List<(Kingdom kingdom, FleetData fleet)> side, float enemyEdge)
    {
        int sunk = 0;
        foreach ((Kingdom kingdom, FleetData fleet) in side)
        {
            int lostWar = Mathf.Min(fleet.warships, Mathf.RoundToInt(fleet.warships * 0.3f * enemyEdge + (enemyEdge > 0.6f ? 1 : 0)));
            int lostTransport = Mathf.Min(fleet.transports, Mathf.RoundToInt(fleet.transports * 0.6f * enemyEdge));
            if (lostTransport > 0 && fleet.transports > 0) SinkTroops(kingdom, lostTransport / (float)fleet.transports);
            fleet.warships -= lostWar;
            fleet.transports -= lostTransport;
            fleet.org = Mathf.Max(0f, fleet.org - (10f + 30f * enemyEdge));
            sunk += lostWar + lostTransport;
        }
        return sunk;
    }

    // 运兵船沉没：还在海上的登陆部队按比例折损
    private static void SinkTroops(Kingdom kingdom, float share)
    {
        foreach (CorpsData corps in CorpsSystem.Of(kingdom))
        {
            TileZone zone = CorpsSystem.Zone(corps);
            if (corps.task != "attack" || !SeaRegionSystem.IsSea(zone)) continue;
            CorpsSystem.Casualties(corps, corps.strength * Mathf.Clamp01(share) * 0.5f);
            corps.org = Mathf.Max(0f, corps.org - 20f);
        }
    }

    // 制海权：海区里只剩一方(及其友方)的舰队就归它(记在帝国中央名下)
    private static void UpdateControl(double now)
    {
        foreach (var group in All().Where(entry => entry.fleet.warships > 0 && entry.fleet.region_id >= 0)
                     .GroupBy(entry => entry.fleet.region_id))
        {
            var entries = group.ToList();
            var lead = entries.OrderByDescending(entry => Power(entry.kingdom, entry.fleet)).First();
            if (entries.Any(entry => FrontSystem.Hostile(lead.kingdom, entry.kingdom))) continue;
            SeaRegionSystem.Region region = SeaRegionSystem.Get(group.Key);
            if (region == null) continue;
            region.ControllerId = FrontSystem.Realm(lead.kingdom)?.id ?? lead.kingdom.id;
            region.ControlledAt = now;
        }
    }

    // 关闭占领模式：舰队留着(船不是人口)，旗舰实体撤编
    public static void RemoveAllFlagships()
    {
        foreach ((Kingdom _, FleetData fleet) in All().ToList()) RemoveFlagship(fleet);
    }

    private static void Disband(Kingdom kingdom, FleetData fleet)
    {
        RemoveFlagship(fleet);
        Of(kingdom).Remove(fleet);
    }

    #endregion

    #region 旗舰与航行动画

    private static readonly Dictionary<long, int> StuckSeconds = new();
    // 旗舰下水失败(港外没有可用海面等)后隔一阵再试
    private static readonly Dictionary<long, float> FlagshipRetry = new();
    private const float FlagshipRetryInterval = 30f;

    public static void SecondTick()
    {
        bool flagships = CorpsSystem.GeneralUnits;
        foreach ((Kingdom kingdom, FleetData fleet) in All().ToList())
        {
            StepZone(fleet);
            if (flagships) EnsureFlagship(kingdom, fleet);
            else RemoveFlagship(fleet);
            MoveFlagship(fleet);
        }
    }

    // 兵牌在海上一块一块挪向所在海区的中心
    private static void StepZone(FleetData fleet)
    {
        SeaRegionSystem.Region region = SeaRegionSystem.Get(fleet.region_id);
        TileZone target = region?.Center;
        if (target == null) return;
        TileZone current = CorpsSystem.ZoneById(fleet.zone_id);
        if (current == null || !SeaRegionSystem.IsSea(current))
        {
            fleet.zone_id = target.id;
            return;
        }
        if (current == target) return;
        for (int step = 0; step < 3 && current != target; step++)
        {
            TileZone best = null;
            float bestDistance = CorpsSystem.ZoneDistance(current, target);
            foreach (TileZone next in current.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (!SeaRegionSystem.IsSea(next)) continue;
                float distance = CorpsSystem.ZoneDistance(next, target);
                if (distance < bestDistance)
                {
                    best = next;
                    bestDistance = distance;
                }
            }
            if (best == null)
            {
                // 绕不过去(半岛、海峡)：卡住几秒就直接到位
                StuckSeconds.TryGetValue(fleet.id, out int stuck);
                StuckSeconds[fleet.id] = ++stuck;
                if (stuck >= 4) { current = target; StuckSeconds.Remove(fleet.id); }
                break;
            }
            current = best;
        }
        fleet.zone_id = current.id;
    }

    private static void EnsureFlagship(Kingdom kingdom, FleetData fleet)
    {
        if (fleet.flagship_id >= 0)
        {
            Actor existing = World.world.units.get(fleet.flagship_id);
            if (existing?.data != null && !existing.isRekt() && existing.isAlive()) return;
            FlagshipIndex.Remove(fleet.flagship_id);
            fleet.flagship_id = -1L;
        }
        if (fleet.warships <= 0) return;
        float clock = Time.unscaledTime;
        if (FlagshipRetry.TryGetValue(fleet.id, out float retryAt) && clock < retryAt) return;
        FlagshipRetry[fleet.id] = clock + FlagshipRetryInterval;
        City home = Home(fleet);
        Building building = home == null ? null : Port(home);
        Docks docks = building?.component_docks;
        if (docks == null || home.kingdom != kingdom) return;
        if (docks.tiles_ocean == null || docks.tiles_ocean.Count == 0)
        {
            docks.recalculateOceanTiles();
            return;
        }
        // 旗舰按本国舰种：楼船 / 远洋帆舰 / 蒸汽铁甲 / 现代舰艇(见 EmpireCraftWarshipLibrary)；缺资源时退回原版运兵船
        string assetId = GameLibrary.EmpireCraftWarshipLibrary.IdFor(Tier(kingdom));
        if (AssetManager.actor_library.get(assetId) == null)
            assetId = building.asset.getBoatAssetIDFromType("boat_type_transport", home);
        if (string.IsNullOrEmpty(assetId)) return;
        try
        {
            Actor boat = World.world.units.createNewUnit(assetId, docks.tiles_ocean.GetRandom());
            if (boat == null) return;
            docks.addBoatToDock(boat);
            fleet.flagship_id = boat.id;
            FlagshipIndex[boat.id] = (kingdom.id, fleet.id);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][海军] 旗舰下水失败: {exception.Message}");
        }
    }

    private static void RemoveFlagship(FleetData fleet)
    {
        if (fleet.flagship_id < 0) return;
        Actor boat = World.world?.units?.get(fleet.flagship_id);
        FlagshipIndex.Remove(fleet.flagship_id);
        fleet.flagship_id = -1L;
        if (boat?.data == null || boat.isRekt() || !boat.isAlive()) return;
        try { boat.die(true, AttackType.Other, false, false); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][海军] 旗舰撤编失败: {exception.Message}"); }
    }

    // 旗舰沉没：组织度大挫，下个月补上
    public static void OnFlagshipDied(Actor actor)
    {
        if (actor?.data == null || !FlagshipIndex.TryGetValue(actor.id, out var key)) return;
        FlagshipIndex.Remove(actor.id);
        Kingdom kingdom = World.world.kingdoms.get(key.kingdom);
        FleetData fleet = kingdom == null ? null : Of(kingdom).FirstOrDefault(entry => entry.id == key.fleet);
        if (fleet == null) return;
        fleet.flagship_id = -1L;
        fleet.org = Mathf.Max(0f, fleet.org - 15f);
        FrontSystem.Log(kingdom, string.Format(LM.Get("navy_log_flagship_lost"), Name(fleet)));
    }

    private static void MoveFlagship(FleetData fleet)
    {
        if (fleet.flagship_id < 0) return;
        Actor boat = World.world.units.get(fleet.flagship_id);
        TileZone zone = CorpsSystem.ZoneById(fleet.zone_id);
        if (boat?.data == null || boat.isRekt() || !boat.isAlive() || zone?.centerTile == null || boat.current_tile == null) return;
        float now = Time.unscaledTime;
        if (FlagshipMoves.TryGetValue(boat.id, out float next) && now < next) return;
        FlagshipMoves[boat.id] = now + FlagshipMoveInterval;
        if (boat.current_tile.zone == zone) return;
        try { boat.goTo(zone.centerTile, pPathOnWater: true); }
        catch (Exception exception) { LogService.LogWarning($"[EmpireCraft][海军] 旗舰航行失败: {exception.Message}"); }
    }

    #endregion

    #region 名称

    public static string Name(FleetData fleet)
    {
        string format = LM.Get("navy_fleet_name");
        string home = Home(fleet)?.GetCityName() ?? "";
        return string.Format(format, home);
    }

    #endregion
}
