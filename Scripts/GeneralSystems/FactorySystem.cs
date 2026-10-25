using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 工厂区(用户 2026-10-17 撤城市规划时"工业区……直接随机给一个整体的成品""多给几个工厂大小权能就好了"，
// 工业区的作用选"改由新工厂提供"；新城市规划问答选"按现有八类工厂")：一片片钉在地图上的厂区，随存档(SaveData.factories)，
// 归落在谁的地上的城。
//   · 放：营建页的小、中、大厂区神力(边长 4、6、8 格，至多 2、4、6 座厂)，点城的地放下一整片成品——
//     从本城已解锁的门类里随机挑满(见 Place)；不能压在城区、别的厂区、镇村上；
//   · 厂：古代手工工场六类(用户 2026-10-22 见厂区清一色火药厂，选"加古代手工工场")——窑场(砖瓦民居)、铸钱局(铸币)、
//     造纸坊(造纸术)、冶铁坊(冶铁)、木船坞(沿岸领航)、印书坊(雕版印刷)；近代八类同虚拟工厂(见 VirtualIndustrySystem)——
//     火药厂(火药)、钢铁厂(工业化)、轻工厂与轻型船厂(摩托化)、重工厂与重型船厂(装甲战)、轻型飞机厂(航空)、
//     重型飞机厂(战略航空)；船坞、船厂要城里有码头。每座 1~3 级；
//   · 新旧交替(问答选"AI 逐步改建成近代厂")：本城解锁了钢铁厂以后的近代门类，AI 添厂、新建只挑近代的，
//     每年先把一座古代工场改建成近代厂(火药厂不算古代工场，照旧留着)；
//   · 产出：每座每级给城里 jobs_per_factory 个工场岗位(见 UrbanEmploymentSystem)，每年按门类出一份产值(进税基，见 Produce)；
//   · 本城有厂区：矿场、伐木场能升到第三级以上，产出多两成五；每片厂区多许建一座矿场、伐木场、屠宰场(至多三座)；
//   · 点厂区打开小窗口(见 FactoryWindow)：改名、扩建缩建、换个样子，厂的升级降级、添加拆除，看产出；
//   · AI 自己建(用户 2026-10-17 "可以"——撤了规划以后 AI 城市没有厂区，矿场伐木场卡在二级)：有能建的门类、不在打仗的城，
//     每年至多一件事——没满的厂区添一座厂、给一座厂升一级、厂区满了扩建、不够数就在城区边上新建一片小厂区；
//     城的档位二级起一片、三级两片、四级三片(见 Tick)。世界法则「固定城乡格局」开着时不建，只听手动。
//   · 手动不设限(用户 2026-10-20 "手动放置建筑时候不要有任何限制")：神力放厂区、小窗口扩建添厂不看科技(八类随便挑，船厂不要码头)、
//     不看离镇村多近、压不压城区城墙、有几成是水，点在无主之地归挨着的城(四周没有城就全图最近的，那块地划进去)；
//     只留：厂区中心落在水里、别的厂区里(厂按片认，点选、画法会撞)，整片伸出地图外；一片厂区的厂数照大小(厂房按大小排，多了画不下，
//     要多放就扩建或另放一片)。AI 自建照旧要科技、守地界与间距。
public static class FactorySystem
{
    // 门类：建筑 id、解锁它的科技、每级每年的产值
    public static readonly (string id, string tech, float value)[] Types =
    {
        ("kiln", "tiled_housing", 20f),
        ("mint", "currency", 25f),
        ("paper_mill", "paper", 25f),
        ("iron_foundry", "ironworking", 30f),
        ("wooden_shipyard", "coastal_pilotage", 30f),
        ("print_shop", "printing", 30f),
        ("gunpowder_mill", "gunpowder", 40f),
        ("steel_mill", "industrialization", 60f),
        ("light_factory", "motorization", 50f),
        ("light_shipyard", "motorization", 50f),
        ("heavy_factory", "armored_warfare", 80f),
        ("heavy_shipyard", "armored_warfare", 80f),
        ("light_aircraft_factory", "aviation", 100f),
        ("heavy_aircraft_factory", "strategic_aviation", 120f)
    };

    // 古代手工工场(有了近代厂 AI 会逐步改建掉)
    public static readonly HashSet<string> Ancient = new()
        { "kiln", "mint", "paper_mill", "iron_foundry", "wooden_shipyard", "print_shop" };

    // 近代厂：工业化以后的门类(火药厂既不算古代也不算近代)
    public static bool IsModern(string type) => !Ancient.Contains(type) && type != "gunpowder_mill";

    // AI 挑门类：有近代的只挑近代的
    private static List<string> Preferred(List<string> unlocked)
    {
        List<string> modern = unlocked.Where(IsModern).ToList();
        return modern.Count > 0 ? modern : unlocked;
    }

    public const int MaxSize = 3, MaxLevel = 3;
    public static int Capacity(int size) => Math.Max(1, size) * 2;
    public static int Side(int size) => 2 + Math.Max(1, size) * 2;

    private static readonly Dictionary<int, FactorySite> All = new();
    // 地块 → 中心落在这块地上的厂区(读档后头一次用到时建)
    private static Dictionary<int, List<FactorySite>> _byZone;
    private static int _nextId = 1;

    // 工厂区有增减、改动就加一(城市平面图的指纹里算进去)
    public static int Revision { get; private set; }

    public static void ResetWorldState()
    {
        All.Clear();
        _byZone = null;
        _nextId = 1;
        _lastMonth = -1d;
        Revision++;
    }

    public static void Load(List<FactorySite> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (FactorySite site in saved)
        {
            if (site == null) continue;
            if (site.id <= 0 || All.ContainsKey(site.id)) site.id = _nextId;
            site.units ??= new List<FactoryUnit>();
            site.units.RemoveAll(unit => unit == null || TypeIndex(unit.type) < 0);
            All[site.id] = site;
            _nextId = Math.Max(_nextId, site.id + 1);
        }
    }

    public static List<FactorySite> Save() => All.Values.ToList();

    private static Dictionary<int, List<FactorySite>> ByZone()
    {
        if (_byZone != null) return _byZone;
        var index = new Dictionary<int, List<FactorySite>>();
        if (World.world == null) return index;
        foreach (FactorySite site in All.Values)
        {
            TileZone zone = World.world.GetTile(site.x, site.y)?.zone;
            if (zone == null) continue;
            if (!index.TryGetValue(zone.id, out List<FactorySite> list)) index[zone.id] = list = new List<FactorySite>();
            list.Add(site);
        }
        _byZone = index;
        return index;
    }

    public static List<FactorySite> InCity(City city)
    {
        var result = new List<FactorySite>();
        if (city?.zones == null || All.Count == 0) return result;
        Dictionary<int, List<FactorySite>> index = ByZone();
        foreach (TileZone zone in city.zones)
            if (zone != null && index.TryGetValue(zone.id, out List<FactorySite> list)) result.AddRange(list);
        return result;
    }

    // 本城有几片厂区
    public static int Count(City city) => All.Count == 0 ? 0 : InCity(city).Count;

    public static FactorySite ById(int id) => All.TryGetValue(id, out FactorySite site) ? site : null;

    public static City CityOf(FactorySite site) => site == null ? null : World.world?.GetTile(site.x, site.y)?.zone?.city;

    // 厂区占的格(左下角、边长)
    public static (int x0, int y0, int side) Footprint(FactorySite site)
    {
        int side = Side(site.size);
        return (site.x - side / 2, site.y - side / 2, side);
    }

    public static bool Covers(FactorySite site, int x, int y, int margin = 0)
    {
        (int x0, int y0, int side) = Footprint(site);
        return x >= x0 - margin && y >= y0 - margin && x < x0 + side + margin && y < y0 + side + margin;
    }

    // (x, y) 落在哪片厂区(连同四周 margin 格)里：村镇选址、立村、建城避开(见 UrbanFabric、RuralSettlementSystem、CityPlacementSystem)
    public static bool Near(int x, int y, int margin)
    {
        if (All.Count == 0) return false;
        TileZone zone = World.world?.GetTile(x, y)?.zone;
        if (zone == null) return false;
        Dictionary<int, List<FactorySite>> index = ByZone();
        var seen = new HashSet<TileZone> { zone };
        foreach (TileZone around in Around(zone))
        {
            if (index.TryGetValue(around.id, out List<FactorySite> list) && list.Exists(site => Covers(site, x, y, margin))) return true;
            foreach (TileZone far in around.neighbours_all ?? Array.Empty<TileZone>())
                if (far != null && seen.Add(far) && index.TryGetValue(far.id, out List<FactorySite> more) &&
                    more.Exists(site => Covers(site, x, y, margin))) return true;
        }
        return false;
    }

    // 这一格落在哪片厂区里
    public static FactorySite At(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (zone == null || All.Count == 0) return null;
        Dictionary<int, List<FactorySite>> index = ByZone();
        foreach (TileZone around in Around(zone))
            if (index.TryGetValue(around.id, out List<FactorySite> list))
                foreach (FactorySite site in list)
                    if (Covers(site, tile.x, tile.y)) return site;
        return null;
    }

    private static IEnumerable<TileZone> Around(TileZone zone)
    {
        yield return zone;
        if (zone.neighbours_all == null) yield break;
        foreach (TileZone neighbour in zone.neighbours_all)
            if (neighbour != null) yield return neighbour;
    }

    #region 门类

    public static int TypeIndex(string type)
    {
        for (int i = 0; i < Types.Length; i++)
            if (Types[i].id == type) return i;
        return -1;
    }

    public static string TypeName(string type) => LM.Get("factory_type_" + type);

    // 本城能建的门类(本文化研究到了；船厂要有码头)
    public static List<string> Unlocked(City city)
    {
        var result = new List<string>();
        string culture = city == null ? null : TechnologySystem.GetCultureOf(city);
        if (string.IsNullOrEmpty(culture)) return result;
        bool docks = city.hasBuildingType("type_docks");
        foreach ((string id, string tech, _) in Types)
        {
            if (!TechnologySystem.HasTech(culture, tech)) continue;
            if (id.EndsWith("_shipyard") && !docks) continue;
            result.Add(id);
        }
        return result;
    }

    // 全部门类(手动放、手动添厂用：不看科技)
    public static List<string> AllTypes() => Types.Select(type => type.id).ToList();

    // 一座厂一年的产值、岗位
    public static float ValueOf(FactoryUnit unit)
    {
        int index = TypeIndex(unit?.type);
        return index < 0 ? 0f : Types[index].value * Math.Max(1, unit.level);
    }

    public static int JobsOf(FactoryUnit unit) =>
        unit == null ? 0 : Math.Max(1, unit.level) * TechnologySystem.Config.industry.jobs_per_factory;

    // 本城厂区里的厂按等级折成几座工厂(见 VirtualIndustrySystem.Factories)：本文化还没解锁的门类不算
    public static int Units(City city)
    {
        if (All.Count == 0 || city == null) return 0;
        string culture = TechnologySystem.GetCultureOf(city);
        int total = 0;
        foreach (FactorySite site in InCity(city))
            foreach (FactoryUnit unit in site.units)
            {
                int index = TypeIndex(unit.type);
                if (index >= 0 && !string.IsNullOrEmpty(culture) && TechnologySystem.HasTech(culture, Types[index].tech))
                    total += Math.Max(1, unit.level);
            }
        return total;
    }

    // 每年产值进本城(见 PopulationEconomySystem)
    public static void Produce(City city, CityPopulationData data, float years)
    {
        if (All.Count == 0 || city == null || data == null || years <= 0f) return;
        float value = 0f;
        foreach (FactorySite site in InCity(city))
            foreach (FactoryUnit unit in site.units)
                value += ValueOf(unit);
        if (value <= 0f) return;
        // 记进工业那一栏(交工业税，官营的那份利润从这里拿，见 EnterpriseSystem.TakePublicProfit)
        float made = value * years;
        data.produced_value += made;
        var sectors = data.produced_sector_values ??= new();
        string key = TreasuryCategory.IndustrialTax.ToString();
        sectors.TryGetValue(key, out float previous);
        sectors[key] = previous + made;
    }

    // AI 营建的花费(神力手动的不花钱)：添一座厂按门类年产值，升级按年产值乘新等级的一半，扩建每级 100，新建一片小厂区 80 加里面的厂
    private const int ExpandCostPerSize = 100, SiteCost = 80;

    private static int UnitCost(string type, int level)
    {
        int index = TypeIndex(type);
        return index < 0 ? 0 : UnityEngine.Mathf.RoundToInt(Types[index].value * Math.Max(1, level) * (level <= 1 ? 1f : 0.5f));
    }

    #endregion

    #region AI 自建

    private static double _lastMonth = -1d;

    // 每月(见 PureMapPatch.RefreshUrbanSlice)：每城每月十二分之一的机会做一件事(平均一年一件，各城错开在不同的月份，
    // 免得全图同一个月一起改、一起重画)
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || !PureMapMode.Enabled || RuralSettlementSystem.Fixed) return;
        double now = world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (City city in world.cities.list.ToList())
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild || city.kingdom.hasEnemies() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) continue;
            if (UnityEngine.Random.value >= 1f / 12f) continue;
            try
            {
                Grow(city);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][厂区] {city.GetCityName()} 自建失败: {exception.Message}");
            }
        }
    }

    // 营地(游牧、林地)不建古代工场(用户 2026-10-22 "游牧哪里来的作坊"，问答选"古代不建，近代才建")：
    // 有了近代厂才建、只建近代厂；之前 AI 建下的古代工场、火药坊拆掉(神力手动放的厂区不动，见 FactorySite.manual)
    private static bool Camp(City city) => CityStyleSystem.Camp(CityStyleSystem.Established(city));

    private static void Grow(City city)
    {
        List<string> unlocked = Unlocked(city);
        if (Camp(city))
        {
            if (!unlocked.Any(IsModern))
            {
                foreach (FactorySite site in InCity(city).Where(site => !site.manual).ToList())
                {
                    site.units.RemoveAll(unit => !IsModern(unit.type));
                    if (site.units.Count == 0) Remove(site);
                    else Changed(site);
                }
                return;
            }
            unlocked = unlocked.Where(IsModern).ToList();
        }
        if (unlocked.Count == 0) return;
        int tier = city.GetOrCreate().urban_tier;
        int wanted = tier >= 4 ? 3 : tier >= 3 ? 2 : tier >= 2 ? 1 : 0;
        List<FactorySite> sites = InCity(city);
        if (wanted == 0 && sites.Count == 0) return;
        List<string> preferred = Preferred(unlocked);
        // 有了近代厂：先把一座古代工场(等级低的先)改建成近代厂，慢慢换代
        if (preferred.Any(IsModern))
        {
            var old = sites.SelectMany(site => site.units.Select((unit, index) => (site, unit, index)))
                .Where(entry => Ancient.Contains(entry.unit.type)).OrderBy(entry => entry.unit.level).FirstOrDefault();
            if (old.site != null)
            {
                List<string> missing = preferred.Where(type => !old.site.units.Exists(unit => unit.type == type)).ToList();
                List<string> choices = missing.Count > 0 ? missing : preferred;
                string type = choices[UnityEngine.Random.Range(0, choices.Count)];
                if (EnterpriseSystem.FundOwned(city, UnitCost(type, 1), old.site.state)) Rebuild(old.site, old.index, type);
                return;
            }
        }
        // 没满的厂区添一座(先挑区里还没有的门类)
        FactorySite open = sites.FirstOrDefault(site => site.units.Count < Capacity(site.size));
        if (open != null)
        {
            List<string> fresh = preferred.Where(type => !open.units.Exists(unit => unit.type == type)).ToList();
            List<string> pool = fresh.Count > 0 ? fresh : preferred;
            string type = pool[UnityEngine.Random.Range(0, pool.Count)];
            if (EnterpriseSystem.FundOwned(city, UnitCost(type, 1), open.state)) AddUnit(open, type);
            return;
        }
        // 给一座厂升一级
        var upgradable = sites.SelectMany(site => site.units.Select((unit, index) => (site, unit, index)))
            .Where(entry => entry.unit.level < MaxLevel).ToList();
        if (upgradable.Count > 0 && UnityEngine.Random.value < 0.5f)
        {
            var pick = upgradable[UnityEngine.Random.Range(0, upgradable.Count)];
            if (EnterpriseSystem.FundOwned(city, UnitCost(pick.unit.type, pick.unit.level + 1), pick.site.state))
                SetLevel(pick.site, pick.index, pick.unit.level + 1);
            return;
        }
        // 厂区满了、城够大：扩建一片
        if (sites.Count >= wanted)
        {
            FactorySite small = sites.Where(site => site.size < MaxSize && site.size < tier - 1).OrderBy(site => site.size).FirstOrDefault();
            if (small != null && EnterpriseSystem.FundOwned(city, ExpandCostPerSize * (small.size + 1), small.state))
                Resize(small, small.size + 1);
            return;
        }
        // 不够数：城区边上(离城心由近及远、不在城区里)新建一片小厂区
        WorldTile center = city.getTile();
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        if (center == null || built == null) return;
        // 新建一片小厂区(两座厂)：先看出不出得起，建成了再按国策定谁出钱
        int cost = SiteCost + Capacity(1) * UnityEngine.Mathf.RoundToInt((float)unlocked.Average(type => UnitCost(type, 1)));
        if (!EnterpriseSystem.CanFund(city, cost)) return;
        int tries = 0;
        foreach (TileZone zone in city.zones.Where(zone => zone?.centerTile != null && !built.Contains(zone.id) && zone != center.zone)
                     .OrderBy(zone => Toolbox.SquaredDistTile(zone.centerTile, center)).ToList())
        {
            if (++tries > 16) break;
            Place(zone.centerTile, 1, out FactorySite placed);
            if (placed == null) continue;
            if (EnterpriseSystem.Fund(city, cost, false, out bool state)) placed.state = state;
            else Remove(placed);
            return;
        }
    }

    #endregion

    #region 营建

    private static void Changed(FactorySite site)
    {
        Revision++;
        City city = CityOf(site);
        if (city != null) PureMapPatch.Redraw(city);
    }

    // 放一片厂区：返回提示文字。manual 为神力手动放的(不设限，见上)
    public static string Place(WorldTile tile, int size, out FactorySite placed, bool manual = false)
    {
        placed = null;
        if (!PureMapMode.Enabled) return LM.Get("settlement_need_pure_map");
        City city = tile?.zone?.city;
        bool claim = manual && city == null && tile?.zone != null;
        if (claim) city = RuralSettlementSystem.NearestCity(tile.zone, tile);
        if (city?.data == null || city.isRekt()) return LM.Get("factory_place_no_city");
        // 纯沙漠、纯冻土建不了东西(见 BarrenLandSystem)
        if (BarrenLandSystem.Barren(tile.zone)) return LM.Get("settlement_place_barren");
        List<string> unlocked = manual ? AllTypes() : Unlocked(city);
        if (unlocked.Count == 0) return string.Format(LM.Get("factory_place_no_tech"), city.GetCityName());
        if (!manual) unlocked = Preferred(unlocked);
        string refusal = manual ? CheckManual(tile.x, tile.y, size, null) : CheckFootprint(city, tile.x, tile.y, size, null);
        if (refusal != null) return refusal;
        if (claim)
        {
            using (GamePatches.CityExpansionPatch.Manual())
                city.addZone(tile.zone);
            if (tile.zone.city != city) return LM.Get("factory_place_no_city");
            CityDistricts.Reset();
        }
        placed = new FactorySite { id = _nextId++, x = tile.x, y = tile.y, size = Math.Max(1, Math.Min(MaxSize, size)),
            founded = World.world.getCurWorldTime(), manual = manual };
        // 一整片成品：从已解锁的门类里随机挑满，尽量不重样
        var pool = new List<string>(unlocked);
        for (int k = 0; k < Capacity(placed.size); k++)
        {
            if (pool.Count == 0) pool.AddRange(unlocked);
            string type = pool[UnityEngine.Random.Range(0, pool.Count)];
            pool.Remove(type);
            placed.units.Add(new FactoryUnit { type = type, level = UnityEngine.Random.value < 0.35f ? 2 : 1 });
        }
        All[placed.id] = placed;
        TileZone zone = tile.zone;
        if (_byZone != null)
        {
            if (!_byZone.TryGetValue(zone.id, out List<FactorySite> list)) _byZone[zone.id] = list = new List<FactorySite>();
            list.Add(placed);
        }
        Changed(placed);
        return string.Format(LM.Get("factory_placed"), city.GetCityName(), LM.Get("factory_size_" + placed.size));
    }

    // 厂区占地能不能用：都在本城的地上、七成是平地；不压城区、城心、城墙、别的厂区，四周两格内没有镇村；不成返回提示
    private static string CheckFootprint(City city, int cx, int cy, int size, FactorySite self)
    {
        var probe = new FactorySite { x = cx, y = cy, size = size };
        (int x0, int y0, int side) = Footprint(probe);
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        TileZone center = city.getTile()?.zone;
        int land = 0;
        for (int y = y0; y < y0 + side; y++)
        for (int x = x0; x < x0 + side; x++)
        {
            WorldTile at = World.world.GetTile(x, y);
            if (at?.zone == null) return LM.Get("factory_place_no_room");
            if (at.zone == center || built != null && built.Contains(at.zone.id))
                return string.Format(LM.Get("settlement_place_urban"), city.GetCityName());
            FactorySite other = At(at);
            if (MapWallSystem.Contains(at) || other != null && other != self) return LM.Get("factory_place_no_room");
            if (at.zone.city == city && at.Type != null && !at.Type.liquid && !at.Type.mountains) land++;
        }
        if (land * 10 < side * side * 7) return LM.Get("factory_place_no_room");
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
        {
            // 关城方城 5×5(见 RuralSettlementSystem.FortReach)，厂区不压它
            int margin = settlement.fort ? RuralSettlementSystem.FortReach + 1 : 2;
            if (settlement.x >= x0 - margin && settlement.x < x0 + side + margin && settlement.y >= y0 - margin &&
                settlement.y < y0 + side + margin)
                return string.Format(LM.Get("settlement_place_crowded"), RuralSettlementSystem.Label(settlement));
        }
        return null;
    }

    // 手动放、扩建的占地：中心不在水里、不落在别的厂区里，整片在地图里
    private static string CheckManual(int cx, int cy, int size, FactorySite self)
    {
        WorldTile center = World.world.GetTile(cx, cy);
        if (center?.Type == null || center.Type.liquid) return LM.Get("factory_place_no_room");
        FactorySite other = At(center);
        if (other != null && other != self) return LM.Get("factory_place_no_room");
        (int x0, int y0, int side) = Footprint(new FactorySite { x = cx, y = cy, size = size });
        if (World.world.GetTile(x0, y0) == null || World.world.GetTile(x0 + side - 1, y0 + side - 1) == null)
            return LM.Get("factory_place_no_room");
        return null;
    }

    public static string Remove(FactorySite site)
    {
        if (site == null || !All.Remove(site.id)) return null;
        TileZone zone = World.world?.GetTile(site.x, site.y)?.zone;
        if (_byZone != null && zone != null && _byZone.TryGetValue(zone.id, out List<FactorySite> list)) list.Remove(site);
        Changed(site);
        return string.Format(LM.Get("factory_removed"), Label(site));
    }

    public static string Label(FactorySite site) =>
        !string.IsNullOrEmpty(site?.name) ? site.name : LM.Get("factory_size_" + (site?.size ?? 1));

    public static void Rename(FactorySite site, string name)
    {
        string value = (name ?? "").Trim();
        if (site == null || value.Length == 0 || value == site.name) return;
        site.name = value.Length > 8 ? value.Substring(0, 8) : value;
        Changed(site);
    }

    public static void Reroll(FactorySite site)
    {
        if (site == null) return;
        long seed;
        do seed = ((long)UnityEngine.Random.Range(1, int.MaxValue) << 16) ^ UnityEngine.Random.Range(0, 1 << 16);
        while (seed == 0L || seed == site.seed);
        site.seed = seed;
        Changed(site);
    }

    // 扩建、缩建：缩到放不下的厂从末尾拆掉；扩建要四周放得下(manual：小窗口里手动扩建，只看不出地图)
    public static string Resize(FactorySite site, int size, bool manual = false)
    {
        if (site == null) return null;
        size = Math.Max(1, Math.Min(MaxSize, size));
        if (size == site.size) return null;
        if (size > site.size)
        {
            City city = CityOf(site);
            if (city == null && !manual) return LM.Get("factory_place_no_room");
            string refusal = manual ? CheckManual(site.x, site.y, size, site) : CheckFootprint(city, site.x, site.y, size, site);
            if (refusal != null) return refusal;
        }
        site.size = size;
        int capacity = Capacity(size);
        if (site.units.Count > capacity) site.units.RemoveRange(capacity, site.units.Count - capacity);
        Changed(site);
        return null;
    }

    public static string AddUnit(FactorySite site, string type)
    {
        if (site == null || TypeIndex(type) < 0) return null;
        if (site.units.Count >= Capacity(site.size)) return LM.Get("factory_full");
        site.units.Add(new FactoryUnit { type = type, level = 1 });
        Changed(site);
        return null;
    }

    public static void RemoveUnit(FactorySite site, int index)
    {
        if (site == null || index < 0 || index >= site.units.Count) return;
        site.units.RemoveAt(index);
        Changed(site);
    }

    // 改建：这一座拆了原地另建一座别的门类(从一级起)
    public static void Rebuild(FactorySite site, int index, string type)
    {
        if (site == null || index < 0 || index >= site.units.Count || TypeIndex(type) < 0) return;
        site.units[index] = new FactoryUnit { type = type, level = 1 };
        Changed(site);
    }

    public static void SetLevel(FactorySite site, int index, int level)
    {
        if (site == null || index < 0 || index >= site.units.Count) return;
        level = Math.Max(1, Math.Min(MaxLevel, level));
        if (site.units[index].level == level) return;
        site.units[index].level = level;
        Changed(site);
    }

    #endregion
}
