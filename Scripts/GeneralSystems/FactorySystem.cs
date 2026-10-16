using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

// 工厂(用户 2026-10-17 撤城市规划时"工业区……直接随机给一个整体的成品""多给几个工厂大小权能就好了"，
// 工业区的作用选"改由新工厂提供")：一座座钉在地图上的厂区，随存档(SaveData.factories)，归落在谁的地上的城。
// 本城有工厂：矿场、伐木场能升到第三级以上，产出多两成五；每座工厂多许建一座矿场、伐木场、屠宰场(至多三座)，
// 多一个工厂门类(见 IndustryBuildingSystem、VirtualIndustrySystem)。
public static class FactorySystem
{
    private static readonly Dictionary<int, FactorySite> All = new();
    // 地块 → 落在这块地上的工厂(读档后头一次用到时建)
    private static Dictionary<int, List<FactorySite>> _byZone;
    private static int _nextId = 1;

    // 工厂有增减就加一(城市平面图的指纹里算进去)
    public static int Revision { get; private set; }

    public static void ResetWorldState()
    {
        All.Clear();
        _byZone = null;
        _nextId = 1;
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
            All[site.id] = site;
            _nextId = global::System.Math.Max(_nextId, site.id + 1);
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

    public static int Count(City city) => All.Count == 0 ? 0 : InCity(city).Count;

    public static List<FactorySite> InZone(TileZone zone) =>
        zone != null && ByZone().TryGetValue(zone.id, out List<FactorySite> list) ? list : new List<FactorySite>();

    public static FactorySite Add(int x, int y, int size)
    {
        var site = new FactorySite { id = _nextId++, x = x, y = y, size = size, founded = World.world?.getCurWorldTime() ?? 0d };
        All[site.id] = site;
        TileZone zone = World.world?.GetTile(x, y)?.zone;
        if (_byZone != null && zone != null)
        {
            if (!_byZone.TryGetValue(zone.id, out List<FactorySite> list)) _byZone[zone.id] = list = new List<FactorySite>();
            list.Add(site);
        }
        Revision++;
        return site;
    }

    public static void Remove(FactorySite site)
    {
        if (site == null || !All.Remove(site.id)) return;
        TileZone zone = World.world?.GetTile(site.x, site.y)?.zone;
        if (_byZone != null && zone != null && _byZone.TryGetValue(zone.id, out List<FactorySite> list)) list.Remove(site);
        Revision++;
    }
}
