using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 纯沙漠、纯冻土(用户 2026-10-22 "沙漠无法生存的，纯沙漠地段没法建造任何建筑，但是可以优先将地块转入最近的游牧城市"
// "可以突破地块限制，2倍""还有冻土也是""能自动转给斯拉夫""同样的逻辑""不需要建立村子才扩展""限制也是两倍"；
// 问答选：整块全是沙地或荒漠才算、只转给本国的、村镇荒废房屋拆掉、2 倍上限只限这种地块)：
//   · 一块地里的陆地全是沙地、荒漠(沙漠)，或全是冻土(连冻住的沙地，见 TerrainSystem.BarrenKind)；城心那一块不算；
//   · 住不了人、建不了东西：宜居度为零(民居上限不算它，见 TerrainSystem.Livability)，原版建筑不往上盖
//     (见 IndustryBuildingPatch.AfterIsGoodTile)，村镇不往上立(见 UrbanFabric、RuralSettlementSystem.Place)；
//     原有的村镇荒废、房屋拆掉(每月一批，见 Clear)；
//   · 归属：游牧营地收沙漠与冻土，斯拉夫文化的城收冻土，阿拉伯文化的城收沙漠("沙地也可以加给阿拉伯")。
//     挨着这种地的这类城直接领下，不必先立村镇(见 Claim)：
//     无主的谁挨着、城心离得最近谁领(隔水翻山走不到的不领，关了国家扩张的世界法则不领)；
//     有主的只给本国的(这类城自己的不动，城心、被敌军占着的、划走会把那座城断开的也不动)；
//     能领到常规地块上限的 2 倍(超出常规上限的只能是这种地，原版扩张照旧停在常规上限)，离城心的远近也放宽到 2 倍；
//     倍数在模组设置里调(BARREN_ZONE_MULTIPLIER)。
public static class BarrenLandSystem
{
    public const int None = 0, Desert = 1, Frozen = 2;
    // 每座城每月至多领几块
    private const int PerCityPerMonth = 2;
    // 每月至多拆几座房屋、荒废几处村镇(读旧档时一片沙漠上的东西分几个月拆完，不卡一下)
    private const int ClearPerMonth = 40;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

    // 这块地是纯沙漠(1)、纯冻土(2)还是都不是(0)；城心那一块不算
    public static int KindOf(TileZone zone)
    {
        if (zone == null) return None;
        int kind = TerrainSystem.BarrenKind(zone);
        if (kind == None) return None;
        City city = zone.city;
        return city != null && city.getTile()?.zone == zone ? None : kind;
    }

    public static bool Barren(TileZone zone) => KindOf(zone) != None;

    public static bool BarrenAt(int x, int y) => Barren(World.world?.GetTile(x, y)?.zone);

    // 收这种地的城：游牧营地收沙漠与冻土，斯拉夫文化的城收冻土，阿拉伯文化(连制度线属阿拉伯的)的城收沙漠
    public static bool Accepts(City city, int kind)
    {
        if (kind == None || city?.data == null || city.isRekt()) return false;
        if (CityStyleSystem.Nomad(city)) return true;
        string culture = CultureService.GetMainCulture(city, initialize: false) ?? "";
        return kind == Frozen
            ? culture == "Slavonic"
            : culture == "Arab" || culture.Length > 0 && InstitutionSystem.GetCultureLine(culture) == "Arabic";
    }

    // 放宽几倍(模组设置，默认 2，用户 2026-10-22 "这个限制的倍数可以在设置里面调整")
    private static int Multiplier => Math.Max(1, ModClass.BARREN_ZONE_MULTIPLIER);

    // 这类城能领到的地块数：常规上限的几倍(没设上限就不限)
    public static int Limit => ModClass.CITY_MAX_ZONES > 0 ? ModClass.CITY_MAX_ZONES * Multiplier : int.MaxValue;

    // 离城心多远以内(格)：同村镇划地的远近(见 CityExpansionPatch.Settled)放宽同样的倍数
    private static float Reach(City city) => Multiplier * (24f + 8f * Math.Max(0, Math.Min(4, city.GetOrCreate().urban_tier)));

    public static void TryMonthlyScan()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR || !Config.game_loaded || SmoothLoader.isLoading()) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            Clear();
            Claim();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][荒漠冻土] 月结失败: {exception.Message}");
        }
    }

    // 纯沙漠、纯冻土上原有的村镇荒废(关城不动)、房屋拆掉
    private static void Clear()
    {
        int budget = ClearPerMonth;
        var redraw = new HashSet<City>();
        foreach (RuralSettlement settlement in RuralSettlementSystem.Save())
        {
            if (budget <= 0) break;
            if (settlement == null || settlement.fort || settlement.cleared || settlement.ruined_at >= 0d ||
                !BarrenAt(settlement.x, settlement.y)) continue;
            RuralSettlementSystem.Raze(settlement);
            budget--;
        }
        foreach (City city in World.world.cities.list)
        {
            if (budget <= 0) break;
            if (city?.data == null || city.isRekt() || AncientWarfareCompatibility.OwnsObject(city)) continue;
            foreach (Building building in city.buildings.ToList())
            {
                if (budget <= 0) break;
                // 城心的公所不拆(城心那块本来就不算，这里再挡一下)
                if (building?.asset == null || building.isRekt() || building.current_tile == null ||
                    building.asset.type == "type_hall" || !Barren(building.current_tile.zone)) continue;
                building.startDestroyBuilding();
                budget--;
            }
        }
    }

    // 这类城领挨着的纯沙漠、纯冻土
    private static void Claim()
    {
        var takers = World.world.cities.list.Where(city => city?.data != null && !city.isRekt() && city.zones.Count > 0 &&
                                                          !AncientWarfareCompatibility.OwnsObject(city) &&
                                                          (Accepts(city, Desert) || Accepts(city, Frozen))).ToList();
        if (takers.Count == 0) return;
        bool expansion = WorldLawLibrary.world_law_kingdom_expansion.isEnabled();
        foreach (City city in takers)
        {
            int taken = 0;
            while (taken < PerCityPerMonth && city.zones.Count < Limit)
            {
                TileZone zone = Candidate(city, takers, expansion);
                if (zone == null) break;
                using (CityExpansionPatch.Manual()) city.addZone(zone);
                if (zone.city != city) break;
                taken++;
            }
        }
    }

    private static TileZone Candidate(City city, List<City> takers, bool expansion)
    {
        WorldTile seat = city.getTile();
        if (seat == null) return null;
        float reach = Reach(city);
        TileZone best = null;
        float bestDistance = float.MaxValue;
        foreach (TileZone own in city.zones.ToList())
        {
            if (own?.neighbours == null) continue;
            foreach (TileZone zone in own.neighbours)
            {
                if (zone == null || zone.city == city || zone.centerTile == null) continue;
                int kind = KindOf(zone);
                if (!Accepts(city, kind)) continue;
                float distance = Distance(seat, zone);
                if (distance > reach * reach || distance >= bestDistance) continue;
                City holder = zone.city;
                if (holder == null)
                {
                    if (!expansion || !CityExpansionPatch.Reachable(zone, city)) continue;
                }
                else if (!Transferable(holder, zone, city, kind)) continue;
                // 挨着它的这类城里城心离得最近的来领
                if (!Nearest(city, zone, kind, takers, holder)) continue;
                best = zone;
                bestDistance = distance;
            }
        }
        return best;
    }

    // 本国别的城的地能不能划过来：那座城自己不收这种地、地不是它的城心、没被敌军占着、它还剩别的地、划走不会把它断开
    private static bool Transferable(City holder, TileZone zone, City taker, int kind)
    {
        if (holder.data == null || holder.isRekt() || holder.kingdom != taker.kingdom || Accepts(holder, kind) ||
            AncientWarfareCompatibility.OwnsObject(holder) || holder.zones.Count <= 1 ||
            holder.getTile()?.zone == zone || holder.GetTileZoneOccupier(zone) != null ||
            !CityTerritoryProtection.CanRemoveZone(holder, zone)) return false;
        return StaysConnected(holder, zone);
    }

    // 划走这块以后那座城剩下的地还连成一片(四邻相连)
    private static bool StaysConnected(City city, TileZone removed)
    {
        TileZone start = city.getTile()?.zone ?? city.zones.FirstOrDefault(zone => zone != removed);
        if (start == null || start == removed) return false;
        var seen = new HashSet<TileZone> { start };
        var queue = new Queue<TileZone>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            TileZone zone = queue.Dequeue();
            if (zone.neighbours == null) continue;
            foreach (TileZone next in zone.neighbours)
                if (next != null && next != removed && next.city == city && seen.Add(next)) queue.Enqueue(next);
        }
        return seen.Count >= city.zones.Count - 1;
    }

    // 挨着这块地、收这种地的城里，taker 的城心离它最近(有主的地只比本国的)
    private static bool Nearest(City taker, TileZone zone, int kind, List<City> takers, City holder)
    {
        float mine = Distance(taker.getTile(), zone);
        foreach (TileZone next in zone.neighbours)
        {
            City other = next?.city;
            if (other == null || other == taker || other == holder || !takers.Contains(other) || !Accepts(other, kind) ||
                holder != null && other.kingdom != holder.kingdom || other.zones.Count >= Limit) continue;
            WorldTile seat = other.getTile();
            if (seat != null && Distance(seat, zone) < mine) return false;
        }
        return true;
    }

    private static float Distance(WorldTile seat, TileZone zone)
    {
        if (seat == null || zone?.centerTile == null) return float.MaxValue;
        float dx = zone.centerTile.x - seat.x, dy = zone.centerTile.y - seat.y;
        return dx * dx + dy * dy;
    }
}
