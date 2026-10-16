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
//   · 厂：八类，同虚拟工厂(见 VirtualIndustrySystem)——火药厂(火药)、钢铁厂(工业化)、轻工厂与轻型船厂(摩托化)、
//     重工厂与重型船厂(装甲战)、轻型飞机厂(航空)、重型飞机厂(战略航空)；船厂要城里有码头。每座 1~3 级；
//   · 产出：每座每级给城里 jobs_per_factory 个工场岗位(见 UrbanEmploymentSystem)，每年按门类出一份产值(进税基，见 Produce)；
//   · 本城有厂区：矿场、伐木场能升到第三级以上，产出多两成五；每片厂区多许建一座矿场、伐木场、屠宰场(至多三座)；
//   · 点厂区打开小窗口(见 FactoryWindow)：改名、扩建缩建、换个样子，厂的升级降级、添加拆除，看产出；
//   · AI 自己建(用户 2026-10-17 "可以"——撤了规划以后 AI 城市没有厂区，矿场伐木场卡在二级)：有能建的门类、不在打仗的城，
//     每年至多一件事——没满的厂区添一座厂、给一座厂升一级、厂区满了扩建、不够数就在城区边上新建一片小厂区；
//     城的档位二级起一片、三级两片、四级三片(见 Tick)。世界法则「固定城乡格局」开着时不建，只听手动。
public static class FactorySystem
{
    // 门类：建筑 id、解锁它的科技、每级每年的产值
    public static readonly (string id, string tech, float value)[] Types =
    {
        ("gunpowder_mill", "gunpowder", 40f),
        ("steel_mill", "industrialization", 60f),
        ("light_factory", "motorization", 50f),
        ("light_shipyard", "motorization", 50f),
        ("heavy_factory", "armored_warfare", 80f),
        ("heavy_shipyard", "armored_warfare", 80f),
        ("light_aircraft_factory", "aviation", 100f),
        ("heavy_aircraft_factory", "strategic_aviation", 120f)
    };

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
        LastGrown.Clear();
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

    public static bool Covers(FactorySite site, int x, int y)
    {
        (int x0, int y0, int side) = Footprint(site);
        return x >= x0 && y >= y0 && x < x0 + side && y < y0 + side;
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
        if (value > 0f) data.produced_value += value * years;
    }

    #endregion

    #region AI 自建

    private const double GrowYears = 1d;
    private static readonly Dictionary<long, double> LastGrown = new();
    private static double _lastMonth = -1d;

    // 每月(见 PureMapPatch.RefreshUrbanSlice)：轮到满一年的城各做一件事
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
            if (LastGrown.TryGetValue(city.data.id, out double last) && now >= last && Date.getYearsSince(last) < GrowYears) continue;
            LastGrown[city.data.id] = now;
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

    private static void Grow(City city)
    {
        List<string> unlocked = Unlocked(city);
        if (unlocked.Count == 0) return;
        int tier = city.GetOrCreate().urban_tier;
        int wanted = tier >= 4 ? 3 : tier >= 3 ? 2 : tier >= 2 ? 1 : 0;
        List<FactorySite> sites = InCity(city);
        if (wanted == 0 && sites.Count == 0) return;
        // 没满的厂区添一座(先挑区里还没有的门类)
        FactorySite open = sites.FirstOrDefault(site => site.units.Count < Capacity(site.size));
        if (open != null)
        {
            List<string> fresh = unlocked.Where(type => !open.units.Exists(unit => unit.type == type)).ToList();
            List<string> pool = fresh.Count > 0 ? fresh : unlocked;
            AddUnit(open, pool[UnityEngine.Random.Range(0, pool.Count)]);
            return;
        }
        // 给一座厂升一级
        var upgradable = sites.SelectMany(site => site.units.Select((unit, index) => (site, unit, index)))
            .Where(entry => entry.unit.level < MaxLevel).ToList();
        if (upgradable.Count > 0 && UnityEngine.Random.value < 0.5f)
        {
            var pick = upgradable[UnityEngine.Random.Range(0, upgradable.Count)];
            SetLevel(pick.site, pick.index, pick.unit.level + 1);
            return;
        }
        // 厂区满了、城够大：扩建一片
        if (sites.Count >= wanted)
        {
            FactorySite small = sites.Where(site => site.size < MaxSize && site.size < tier - 1).OrderBy(site => site.size).FirstOrDefault();
            if (small != null) Resize(small, small.size + 1);
            return;
        }
        // 不够数：城区边上(离城心由近及远、不在城区里)新建一片小厂区
        WorldTile center = city.getTile();
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        if (center == null || built == null) return;
        int tries = 0;
        foreach (TileZone zone in city.zones.Where(zone => zone?.centerTile != null && !built.Contains(zone.id) && zone != center.zone)
                     .OrderBy(zone => Toolbox.SquaredDistTile(zone.centerTile, center)).ToList())
        {
            if (++tries > 16) break;
            Place(zone.centerTile, 1, out FactorySite placed);
            if (placed != null) return;
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

    // 放一片厂区：返回提示文字
    public static string Place(WorldTile tile, int size, out FactorySite placed)
    {
        placed = null;
        if (!PureMapMode.Enabled) return LM.Get("settlement_need_pure_map");
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt()) return LM.Get("factory_place_no_city");
        List<string> unlocked = Unlocked(city);
        if (unlocked.Count == 0) return string.Format(LM.Get("factory_place_no_tech"), city.GetCityName());
        var probe = new FactorySite { x = tile.x, y = tile.y, size = size };
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
            if (MapWallSystem.Contains(at) || At(at) != null) return LM.Get("factory_place_no_room");
            if (at.zone.city == city && at.Type != null && !at.Type.liquid && !at.Type.mountains) land++;
        }
        if (land * 10 < side * side * 7) return LM.Get("factory_place_no_room");
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
            if (settlement.x >= x0 - 2 && settlement.x < x0 + side + 2 && settlement.y >= y0 - 2 && settlement.y < y0 + side + 2)
                return string.Format(LM.Get("settlement_place_crowded"), RuralSettlementSystem.Label(settlement));
        placed = new FactorySite { id = _nextId++, x = tile.x, y = tile.y, size = Math.Max(1, Math.Min(MaxSize, size)),
            founded = World.world.getCurWorldTime() };
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

    // 扩建、缩建：缩到放不下的厂从末尾拆掉；扩建要四周放得下
    public static string Resize(FactorySite site, int size)
    {
        if (site == null) return null;
        size = Math.Max(1, Math.Min(MaxSize, size));
        if (size == site.size) return null;
        if (size > site.size)
        {
            var probe = new FactorySite { x = site.x, y = site.y, size = size };
            (int x0, int y0, int side) = Footprint(probe);
            City city = CityOf(site);
            for (int y = y0; y < y0 + side; y++)
            for (int x = x0; x < x0 + side; x++)
            {
                WorldTile at = World.world.GetTile(x, y);
                if (at?.zone == null || at.zone.city != city || MapWallSystem.Contains(at)) return LM.Get("factory_place_no_room");
                FactorySite other = At(at);
                if (other != null && other != site) return LM.Get("factory_place_no_room");
            }
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
