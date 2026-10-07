using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public static partial class ZonePlanSystem
{
    private sealed class PlanRevision { internal long Value; }
    private static ConditionalWeakTable<City, PlanRevision> PlanRevisions = new();
    private static object _planningWorld;
    private static City _planningCity;
    private static Kingdom _planningKingdom;
    private static WorldTile _planningCenter;
    private static long _planningRevision;
    private static IEnumerator<int> _planningSteps;
    public static long PlanningSteps { get; private set; }

    public static void InvalidatePlanning(City city)
    {
        if (city != null && CityPopulationSystem.AbstractPopulationEnabled)
            PlanRevisions.GetOrCreateValue(city).Value++;
    }

    private static void CancelContinuation()
    {
        _planningSteps?.Dispose();
        _planningSteps = null;
        _planningCity = null;
    }

    private static void ResetContinuation()
    {
        CancelContinuation();
        _planningWorld = null;
        PlanRevisions = new();
        PlanningSteps = 0;
    }

    private static void TickContinuation(MapBox world)
    {
        if (!ReferenceEquals(_planningWorld, world))
        {
            CancelContinuation(); Pending.Clear(); _lastPass = -1d; _planningWorld = world;
        }
        double now = world.getCurWorldTime();
        if (_planningSteps == null && Pending.Count == 0)
        {
            if (_lastPass >= 0d && now >= _lastPass && Date.getMonthsSince(_lastPass) < 1) return;
            _lastPass = now;
            foreach (City city in world.cities)
                if (city?.data != null && !city.isRekt()) Pending.Enqueue(city);
        }
        using var frameWork = SimulationFrameBudget.Measure();
        long started = Stopwatch.GetTimestamp();
        while (SimulationFrameBudget.HasTime &&
               (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency < SliceBudgetMs)
        {
            if (_planningSteps == null)
            {
                if (Pending.Count == 0) return;
                City city = Pending.Dequeue();
                if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
                _planningCity = city; _planningKingdom = city.kingdom; _planningCenter = city.getTile();
                _planningRevision = PlanRevisions.GetOrCreateValue(city).Value;
                _planningSteps = PlanCitySteps(city).GetEnumerator();
            }
            if (_planningCity?.data == null || _planningCity.isRekt()) { CancelContinuation(); continue; }
            if (_planningCity.kingdom != _planningKingdom || _planningCity.getTile() != _planningCenter ||
                PlanRevisions.GetOrCreateValue(_planningCity).Value != _planningRevision)
            {
                // 领土/手动用途变化后重排到队尾，让其他城市也能前进。
                Pending.Enqueue(_planningCity); CancelContinuation(); continue;
            }
            try
            {
                if (!_planningSteps.MoveNext()) { CancelContinuation(); continue; }
                _planningRevision = PlanRevisions.GetOrCreateValue(_planningCity).Value;
                PlanningSteps++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][城市规划] 续算失败({_planningCity.data?.name}): {exception.Message}");
                CancelContinuation();
            }
        }
    }

    private static IEnumerable<int> PlanCitySteps(City city)
    {
        if (city.zones == null) yield break;
        TileZone[] zones = city.zones.ToArray();
        Dictionary<int, int> uses = Uses(city);
        if (uses.Count > 0)
        {
            var owned = new HashSet<int>();
            foreach (TileZone zone in zones) { if (zone?.city == city) owned.Add(zone.id); yield return 0; }
            foreach (int id in uses.Keys.ToArray()) { if (!owned.Contains(id)) uses.Remove(id); yield return 0; }
        }
        foreach (int step in NormalizeDistrictSteps(city, zones, ZoneUse.Industry)) yield return step;
        foreach (int step in NormalizeDistrictSteps(city, zones, ZoneUse.Residential)) yield return step;
        foreach (TileZone zone in zones)
        {
            if (zone?.city == city) ClearHousing(city, zone, Get(city, zone));
            yield return 0;
        }
        if (city.zones.Count < 2) yield break;
        PlanPolicy policy = PolicyOf(city.kingdom);
        int usable = city.zones.Count - 1;
        foreach (ZoneUse use in AutoUses)
        {
            int current = 0;
            foreach (TileZone zone in zones) { if (zone?.city == city && Get(city, zone) == use) current++; yield return 0; }
            int target = Mathf.Min(MaxZonesPerUse, Mathf.RoundToInt(usable * TargetShare(policy, use)));
            if (use == ZoneUse.Industry && target < 1)
            {
                // 扫描可续跑；与原版 HasIndustryInCity 保持相同判定。
                foreach (Building building in city.buildings.ToArray())
                {
                    bool industry = building?.asset != null && building.city == city && !building.isOnRemove() && UseOf(building.asset) == use;
                    yield return 0;
                    if (industry) { target = 1; break; }
                }
            }
            if (use == ZoneUse.Residential && target < 1) target = 1;
            bool needsSpace = IsDistrict(use) && (city.GetOrCreate().district_space_requests & 1 << (int)use) != 0;
            if (needsSpace) target = Mathf.Max(target, Mathf.Min(usable, current + 1));
            if (target <= 0 || current >= target) continue;
            TileZone best = null; float bestScore = 0f;
            foreach (TileZone zone in zones)
            {
                if (zone?.city == city && zone.tiles != null && !IsCenter(city, zone) && Get(city, zone) == ZoneUse.None &&
                    CanAddWithCount(city, zone, use, current))
                {
                    float score = Score(city, zone, use);
                    if (score > 0f)
                    {
                        if (IsDistrict(use))
                        {
                            int neighbours = 0;
                            if (zone.neighbours != null)
                                foreach (TileZone neighbour in zone.neighbours)
                                    if (neighbour?.city == city && (Get(city, neighbour) == use ||
                                        use == ZoneUse.Residential && IsCenter(city, neighbour))) neighbours++;
                            score += neighbours * 12f;
                        }
                        if (score > bestScore) { bestScore = score; best = zone; }
                    }
                }
                yield return 0;
            }
            if (best?.city != city || Get(city, best) != ZoneUse.None || !CanAddWithCount(city, best, use, current)) continue;
            // 前面已规范连片区域。避免 Set 再同步扫描整座城两次。
            uses[best.id] = (int)use;
            ClearHousing(city, best, use);
            city.GetOrCreate().district_space_requests &= ~(1 << (int)use);
            yield return 0;
            foreach (int step in NormalizeDistrictSteps(city, zones, ZoneUse.Industry)) yield return step;
            foreach (int step in NormalizeDistrictSteps(city, zones, ZoneUse.Residential)) yield return step;
        }
    }

    private static bool CanAddWithCount(City city, TileZone zone, ZoneUse use, int count)
    {
        if (!IsDistrict(use)) return true;
        if (Get(city, zone) == use || count == 0 || Touches(zone, city, use)) return true;
        TileZone center = city.getTile()?.zone;
        return use == ZoneUse.Residential && center?.city == city && Touches(center, city, use) &&
               zone.neighbours != null && zone.neighbours.Contains(center);
    }

    private static IEnumerable<int> NormalizeDistrictSteps(City city, TileZone[] zones, ZoneUse use)
    {
        var members = new HashSet<TileZone>();
        foreach (TileZone zone in zones) { if (zone?.city == city && Get(city, zone) == use) members.Add(zone); yield return 0; }
        if (members.Count <= 1) yield break;
        TileZone center = city.getTile()?.zone;
        if (use == ZoneUse.Residential && center?.city == city && Touches(center, city, use)) members.Add(center);
        var unseen = new HashSet<TileZone>(members);
        HashSet<TileZone> best = null; float bestScore = float.MinValue; int bestId = int.MaxValue;
        foreach (TileZone seed in zones)
        {
            if (seed != null && unseen.Remove(seed))
            {
                var component = new HashSet<TileZone> { seed }; var pending = new Queue<TileZone>(); pending.Enqueue(seed);
                float score = 0f; int firstId = seed.id;
                while (pending.Count > 0)
                {
                    TileZone zone = pending.Dequeue(); firstId = Math.Min(firstId, zone.id); score += 100f + Score(city, zone, use);
                    HashSet<Building> buildings = zone.getHashset(BuildingList.Civs);
                    if (buildings != null)
                        foreach (Building building in buildings)
                            if (building?.asset != null && building.city == city && !building.isOnRemove() &&
                                (use == ZoneUse.Residential ? IsHousing(building.asset) : UseOf(building.asset) == use)) score += 1000f;
                    if (zone.neighbours != null)
                        foreach (TileZone neighbour in zone.neighbours)
                            if (neighbour != null && unseen.Remove(neighbour)) { component.Add(neighbour); pending.Enqueue(neighbour); }
                    yield return 0;
                }
                if (score > bestScore || score == bestScore && firstId < bestId) { best = component; bestScore = score; bestId = firstId; }
            }
            yield return 0;
        }
        if (best == null) yield break;
        foreach (TileZone zone in members) { if (!best.Contains(zone) && Get(city, zone) == use) Uses(city).Remove(zone.id); yield return 0; }
    }
}
