using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 村镇实体(用户 2026-10-17 "村庄既然生成的就不要随意动了，因为已经是个固定地方了，只有可能通过战乱才能毁灭"
// "这些城市村庄虽然是模拟的，但在游戏中都是可以实际存在的、有互动的")。第一期：固定在地图上、随地易主——
//   · 城郊的镇、村一旦生成就记进存档(SaveData.rural_settlements)，钉在那一格上，不再随城市平面图每次重算挪地方；
//   · 归哪座城看它落在谁的地上：割地、城破时跟着土地换主，由新主人的城来画；
//   · 城长大(档位升高)、本城的镇村不够数时，才在本城空地上照原来的选址办法新建(见 UrbanFabric.PlaceCountryside)；
//     城变小不拆；被城区盖住的不画、不算数，城退回去又露出来；毁于兵火的留在原地(废墟)，照样算数，不在别处另建。
// 第二期：战火焚毁与重建(用户选"战火焚毁与重建")——
//   · 敌军占下一块地(战线推进、原版占领都走 CityExtension.AddOccupiedTileZone)，地上的村三成五、镇两成五毁于兵火；
//     劫掠之战七成、五成。毁了的在原地留一片废墟(精细图残垣断瓦、焦土；粗图上镇的废墟是一片暗土色)；
//   · 重建：废墟所在的城不在打仗、那块地没被敌军占着，毁了五年以后每年约两成机会在原址重建(每月查一次，见 Tick)。
public static class RuralSettlementSystem
{
    private const float VillageBurn = 0.35f, TownBurn = 0.25f, RaidVillageBurn = 0.7f, RaidTownBurn = 0.5f;
    private const int RebuildYears = 5;
    private const float RebuildChancePerYear = 0.2f;
    private static double _lastMonth = -1d;

    private static readonly Dictionary<int, RuralSettlement> All = new();
    // 地块 → 落在这块地上的镇村(读档后头一次用到时建)
    private static Dictionary<int, List<RuralSettlement>> _byZone;
    private static int _nextId = 1;

    // 镇村有增减、毁建就加一(城市平面图的指纹里算进去，见 PureMapPatch.Stamp)
    public static int Revision { get; private set; }

    public static void ResetWorldState()
    {
        All.Clear();
        _byZone = null;
        _nextId = 1;
        _lastMonth = -1d;
        Revision++;
    }

    public static void Load(List<RuralSettlement> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (RuralSettlement settlement in saved)
        {
            if (settlement == null) continue;
            if (settlement.id <= 0 || All.ContainsKey(settlement.id)) settlement.id = _nextId;
            All[settlement.id] = settlement;
            _nextId = Math.Max(_nextId, settlement.id + 1);
        }
    }

    public static List<RuralSettlement> Save() => All.Values.ToList();

    private static Dictionary<int, List<RuralSettlement>> ByZone()
    {
        if (_byZone != null) return _byZone;
        var index = new Dictionary<int, List<RuralSettlement>>();
        if (World.world == null) return index;
        foreach (RuralSettlement settlement in All.Values)
        {
            TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
            if (zone == null) continue;
            if (!index.TryGetValue(zone.id, out List<RuralSettlement> list)) index[zone.id] = list = new List<RuralSettlement>();
            list.Add(settlement);
        }
        _byZone = index;
        return index;
    }

    // 落在本城地上的镇村(含废墟)
    public static List<RuralSettlement> InCity(City city)
    {
        var result = new List<RuralSettlement>();
        if (city?.zones == null) return result;
        Dictionary<int, List<RuralSettlement>> index = ByZone();
        foreach (TileZone zone in city.zones)
            if (zone != null && index.TryGetValue(zone.id, out List<RuralSettlement> list)) result.AddRange(list);
        return result;
    }

    public static List<RuralSettlement> InZone(TileZone zone) =>
        zone != null && ByZone().TryGetValue(zone.id, out List<RuralSettlement> list) ? list : new List<RuralSettlement>();

    public static RuralSettlement Add(int x, int y, bool town, int size, bool walled)
    {
        var settlement = new RuralSettlement
        {
            id = _nextId++, x = x, y = y, town = town, size = size, walled = walled,
            founded = World.world?.getCurWorldTime() ?? 0d
        };
        All[settlement.id] = settlement;
        TileZone zone = World.world?.GetTile(x, y)?.zone;
        if (_byZone != null && zone != null)
        {
            if (!_byZone.TryGetValue(zone.id, out List<RuralSettlement> list)) _byZone[zone.id] = list = new List<RuralSettlement>();
            list.Add(settlement);
        }
        Revision++;
        return settlement;
    }

    public static void Touch() => Revision++;

    #region 兵火与重建

    // 举城投降、望风归附时成片易手的地不烧(见 FrontSystem.Surrender、CityExtension.AddOccupiedTileZone)
    public static bool Sparing;

    // 敌军占下一块地(CityExtension.AddOccupiedTileZone)
    public static void OnZoneOccupied(City city, Kingdom occupier, TileZone zone)
    {
        if (Sparing || All.Count == 0 || city?.kingdom == null || occupier == null || occupier == city.kingdom) return;
        List<RuralSettlement> list = InZone(zone);
        if (list.Count == 0) return;
        Kingdom owner = city.kingdom;
        bool raid = occupier.getWars().Any(war => war != null && !war.hasEnded() && war.GetEmpireWarType() == EmpireWarType.劫掠 &&
                                                  (war.isAttacker(occupier) && war.isDefender(owner) ||
                                                   war.isDefender(occupier) && war.isAttacker(owner)));
        double now = World.world.getCurWorldTime();
        foreach (RuralSettlement settlement in list)
        {
            if (settlement.ruined_at >= 0d) continue;
            float chance = settlement.town ? raid ? RaidTownBurn : TownBurn : raid ? RaidVillageBurn : VillageBurn;
            if (UnityEngine.Random.value >= chance) continue;
            settlement.ruined_at = now;
            Revision++;
        }
    }

    // 每月：在原址重建(见 PureMapPatch.RefreshUrbanSlice)
    public static void Tick()
    {
        if (World.world == null || All.Count == 0) return;
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (RuralSettlement settlement in All.Values)
        {
            if (settlement.ruined_at < 0d || now >= settlement.ruined_at && Date.getYearsSince(settlement.ruined_at) < RebuildYears) continue;
            TileZone zone = World.world.GetTile(settlement.x, settlement.y)?.zone;
            City city = zone?.city;
            if (city?.kingdom == null || city.isRekt() || city.kingdom.hasEnemies() || city.GetTileZoneOccupier(zone) != null) continue;
            if (UnityEngine.Random.value >= RebuildChancePerYear / 12f) continue;
            settlement.ruined_at = -1d;
            settlement.founded = now;
            Revision++;
        }
    }

    #endregion
}
