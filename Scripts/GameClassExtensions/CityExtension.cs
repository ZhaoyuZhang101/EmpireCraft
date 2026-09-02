using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class CityExtension
{
    // 正统自动扩张最低门槛
    private const int OccupySpreadMinMandate = 80;

    // 基础要求：进攻方正统 - 防守方正统 至少达到这个值才会开始明显扩张
    private const int OccupySpreadMinMandateDiff = 20;

    // 每次占领后最多自动扩张多少层，防止一次性扩太多影响性能
    private const int OccupySpreadMaxDepth = 2;

    // 每次触发最多自动占领多少个 zone
    private const int OccupySpreadMaxZonesPerTrigger = 5;

    // 同族加成，异族惩罚
    private const int OccupySpreadSameCultureBonus = 10;
    private const int OccupySpreadDifferentCulturePenalty = 15;
    private const int OccupySpreadKingDeadMandateBonus = 25;
    private const int OccupationCaptureLordPerformanceReward = 100;
    private const int OccupationCaptureKingPerformanceReward = 200;
    private const int OccupationCaptureEmperorPerformanceReward = 300;
    private const float OccupationCaptureLordChance = 0.20f;
    private const float OccupationCaptureKingChance = 0.35f;
    private const float OccupationCaptureEmperorChance = 0.50f;
    private const float OccupationCaptureKingReleaseChance = 0.50f;
    // Temporary isolation guard: capture resolution must never run while testing
    // the war-start crash. Occupation progress remains enabled.
    private const bool EnableOccupationCaptureEvents = false;
    public class CityExtraData: ExtraDataBase
    {
        public string kingdom_names = "";
        public long title_id = -1L;
        public long empire_core_id = -1L;
        public List<long> exam_pass_person = new List<long>();
        public int MAX_POPULATION = 100;
        public bool MAX_POPULATION_LIMIT = false;
        public double last_tax_timestamp = -1L;
        public int Money = 0;
        [JsonIgnore]
        public TextInput limitationNumber { get; set; }

        public double corruption_rate = 0.0f;
        public long personalIdentityId = -1L;
        public bool is_choosing_heir = false;
        [JsonIgnore]
        public SimpleButton limitToggle { get; set; }
        public CityType cityType { get; set; }
        public long office_id { get; set; } = -1L;
        public int cached_warriors = 0;
        public int cached_population = 0;
        public double last_cached_timestamp = -1L;
        public double last_army_check_ts = -1L;
        public double last_law_scan_ts = -1L;
        [JsonConverter(typeof(OccupiedStatusConverter))]
        public Dictionary<long, List<int>> OccupiedStatus = new();
        [JsonIgnore]
        public Dictionary<int, long> OccupiedZoneOwners = new();
    }
    
    private static int GetZoneId(TileZone zone)
    {
        return zone?.id ?? -1;
    }
    private static TileZone GetZoneById(int zoneId)
    {
        if (zoneId < 0 || World.world?.zone_calculator == null)
        {
            return null;
        }

        return World.world.zone_calculator.getZoneByID(zoneId);
    }
    private static Dictionary<int, long> GetOccupiedZoneOwnerMap(this City city)
    {
        if (city == null)
        {
            return null;
        }

        CityExtraData data = city.GetOrCreate();
        data.OccupiedZoneOwners ??= new Dictionary<int, long>();

        if (data.OccupiedZoneOwners.Count == 0 && data.OccupiedStatus != null && data.OccupiedStatus.Count > 0)
        {
            foreach (var pair in data.OccupiedStatus)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int zoneId = pair.Value[i];
                    if (zoneId < 0)
                    {
                        continue;
                    }

                    data.OccupiedZoneOwners[zoneId] = pair.Key;
                }
            }
        }

        return data.OccupiedZoneOwners;
    }
    public static void TrySpreadOccupiedZonesByMandate(this City city, Kingdom occupier, TileZone startZone)
    {
        if (city == null || occupier == null || startZone == null)
        {
            return;
        }

        if (occupier.isRekt() || occupier.isNeutral())
        {
            return;
        }

        if (city.kingdom == null || city.kingdom == occupier)
        {
            return;
        }

        Kingdom defender = city.kingdom;
        bool defenderKingDead = city.IsOccupationKingDeathAdvantage(defender);

        if (!occupier.isInWarWith(defender))
        {
            return;
        }

        Empire occupierEmpire = occupier.GetEmpire();
        Empire defenderEmpire = defender.GetEmpire();

        int occupierMandate = occupierEmpire?.Mandate ?? 0;
        int defenderMandate = defenderEmpire?.Mandate ?? 0;

        // 进攻方正统不到 80，不触发自动扩张
        if (occupierMandate < OccupySpreadMinMandate && !defenderKingDead)
        {
            return;
        }

        int effectiveDiff = city.GetEffectiveOccupySpreadMandateDiff(occupier, defender);

        // 差距不够，不自动延伸
        if (effectiveDiff < OccupySpreadMinMandateDiff)
        {
            return;
        }

        int spreadDepth = city.GetOccupySpreadDepthByMandateDiff(effectiveDiff);
        int spreadLimit = city.GetOccupySpreadLimitByMandateDiff(effectiveDiff);

        if (spreadDepth <= 0 || spreadLimit <= 0)
        {
            return;
        }

        HashSet<TileZone> visited = new HashSet<TileZone>();
        Queue<(TileZone zone, int depth)> queue = new Queue<(TileZone zone, int depth)>();

        visited.Add(startZone);
        queue.Enqueue((startZone, 0));

        int spreadCount = 0;

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            TileZone currentZone = item.zone;
            int currentDepth = item.depth;

            if (currentZone == null || currentDepth >= spreadDepth)
            {
                continue;
            }

            if (currentZone.neighbours_all == null)
            {
                continue;
            }

            foreach (TileZone neighbour in currentZone.neighbours_all)
            {
                if (neighbour == null)
                {
                    continue;
                }

                if (!visited.Add(neighbour))
                {
                    continue;
                }

                if (!city.IsValidAutoSpreadOccupyZone(occupier, neighbour))
                {
                    continue;
                }

                bool occupied = city.AddOccupiedTileZoneByMandateSpread(occupier, neighbour);

                if (!occupied)
                {
                    continue;
                }

                spreadCount++;

                if (spreadCount >= spreadLimit)
                {
                    return;
                }

                queue.Enqueue((neighbour, currentDepth + 1));
            }
        }
    }
    private static int GetEffectiveOccupySpreadMandateDiff(this City city, Kingdom occupier, Kingdom defender)
    {
        if (city == null || occupier == null || defender == null)
        {
            return 0;
        }

        Empire occupierEmpire = occupier.GetEmpire();
        Empire defenderEmpire = defender.GetEmpire();

        int occupierMandate = occupierEmpire?.Mandate ?? 0;
        int defenderMandate = defenderEmpire?.Mandate ?? 0;

        int diff = occupierMandate - defenderMandate;

        bool sameCulture = false;

        try
        {
            sameCulture =
                occupier.GetEmpireCraftCulture() != null &&
                occupier.GetEmpireCraftCulture() == defender.GetEmpireCraftCulture();
        }
        catch
        {
            sameCulture = false;
        }

        if (sameCulture)
        {
            diff += OccupySpreadSameCultureBonus;
        }
        else
        {
            diff -= OccupySpreadDifferentCulturePenalty;
        }

        if (city.IsOccupationKingDeathAdvantage(defender))
        {
            diff += OccupySpreadKingDeadMandateBonus;
        }

        return diff;
    }
    private static bool IsOccupationKingDeathAdvantage(this City city, Kingdom defender)
    {
        if (city == null || defender == null)
        {
            return false;
        }

        if (!defender.hasKing())
        {
            return true;
        }

        Actor king = defender.king;
        if (king == null)
        {
            return true;
        }

        return king.isRekt() || !king.isAlive();
    }
    private static int GetOccupySpreadDepthByMandateDiff(this City city, int effectiveDiff)
    {
        if (effectiveDiff < OccupySpreadMinMandateDiff)
        {
            return 0;
        }

        if (effectiveDiff >= 70)
        {
            return 3;
        }

        if (effectiveDiff >= 45)
        {
            return 2;
        }

        return 1;
    }

    private static int GetOccupySpreadLimitByMandateDiff(this City city, int effectiveDiff)
    {
        if (effectiveDiff < OccupySpreadMinMandateDiff)
        {
            return 0;
        }

        if (effectiveDiff >= 70)
        {
            return 8;
        }

        if (effectiveDiff >= 45)
        {
            return 5;
        }

        return 3;
    }
    private static bool IsValidAutoSpreadOccupyZone(this City city, Kingdom occupier, TileZone zone)
    {
        if (city == null || occupier == null || zone == null)
        {
            return false;
        }

        if (zone.world_edge)
        {
            return false;
        }

        if (zone.city != city)
        {
            return false;
        }

        if (city.kingdom == null)
        {
            return false;
        }

        if (!occupier.isInWarWith(city.kingdom))
        {
            return false;
        }

        Kingdom currentOccupier = city.GetTileZoneOccupier(zone);

        // 已经是自己占领，不需要重复扩张
        if (currentOccupier == occupier)
        {
            return false;
        }

        // 友军已经占领，不抢友军占领区
        if (currentOccupier != null && occupier.isInWarOnSameSide(currentOccupier))
        {
            return false;
        }

        if (!city.CanOccupyZoneFromFriendlyEdge(occupier, zone))
        {
            return false;
        }

        return true;
    }
    private static bool CanOccupyZoneFromFriendlyEdge(this City city, Kingdom occupier, TileZone zone)
    {
        if (city == null || occupier == null || zone == null)
        {
            return false;
        }

        if (zone.neighbours_all == null || zone.neighbours_all.Count() == 0)
        {
            return false;
        }

        for (int i = 0; i < zone.neighbours_all.Count(); i++)
        {
            TileZone neighbour = zone.neighbours_all[i];

            if (neighbour == null || neighbour.world_edge)
            {
                continue;
            }

            City neighbourCity = neighbour.city;
            if (neighbourCity == null || neighbourCity.isRekt())
            {
                continue;
            }

            Kingdom neighbourOccupier = neighbourCity.GetTileZoneOccupier(neighbour);
            if (neighbourOccupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(occupier, neighbourOccupier))
            {
                return true;
            }

            Kingdom neighbourOwner = neighbourCity.kingdom;
            if (neighbourOccupier == null && neighbourOwner != null && KingdomFrontLineHelper.IsFriendlyKingdom(occupier, neighbourOwner))
            {
                return true;
            }
        }

        return false;
    }
    private static bool AddOccupiedTileZoneByMandateSpread(this City city, Kingdom occupier, TileZone tileZone)
    {
        if (city == null || occupier == null || tileZone == null)
        {
            return false;
        }

        if (occupier.isRekt())
        {
            return false;
        }

        if (!city.CanOccupyZoneFromFriendlyEdge(occupier, tileZone))
        {
            return false;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;

        if (occupiedStatus == null)
        {
            return false;
        }

        Kingdom currentOccupier = city.GetTileZoneOccupier(tileZone);

        if (currentOccupier != null)
        {
            if (currentOccupier == occupier)
            {
                return false;
            }

            if (occupier.isInWarOnSameSide(currentOccupier))
            {
                return false;
            }

            city.RemoveOccupiedTileZone(currentOccupier, tileZone);
        }

        if (!occupiedStatus.ContainsKey(occupier.id))
        {
            occupiedStatus[occupier.id] = new List<int>();
        }

        List<int> zones = occupiedStatus[occupier.id];

        if (zones == null)
        {
            zones = new List<int>();
            occupiedStatus[occupier.id] = zones;
        }

        int tileZoneId = GetZoneId(tileZone);
        if (tileZoneId < 0 || zones.Contains(tileZoneId))
        {
            return false;
        }

        zones.Add(tileZoneId);
        city.GetOccupiedZoneOwnerMap()[tileZoneId] = occupier.id;
        if (city.TryTriggerOccupationCaptureEvent(occupier, tileZone, zoneOccupationMode:true))
        {
            return true;
        }

        return true;
    }
    public static bool AddOccupiedTileZone(this City city, Kingdom occupier, TileZone tileZone, Actor capturer = null)
    {
        var occupierId = occupier?.id??-1L;
        if (city == null || occupier == null || tileZone == null)
        {
            return false;
        }

        if (occupier.isRekt())
        {
            return false;
        }

        if (!city.CanOccupyZoneFromFriendlyEdge(occupier, tileZone))
        {
            return false;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;

        if (occupiedStatus == null)
        {
            return false;
        }

        Kingdom currentOccupier = city.GetTileZoneOccupier(tileZone);

        if (currentOccupier != null)
        {
            // 已经是自己占领，不重复添加
            if (currentOccupier == occupier)
            {
                return false;
            }

            // 原版已有判断：如果双方在同一场战争中属于同一边，不转移
            if (occupier.isInWarOnSameSide(currentOccupier))
            {
                return false;
            }

            // 不是同一边，说明可以抢占/转移
            city.RemoveOccupiedTileZone(currentOccupier, tileZone);
        }

        if (!occupiedStatus.ContainsKey(occupierId))
        {
            occupiedStatus[occupierId] = new List<int>();
        }

        List<int> zones = occupiedStatus[occupierId];

        if (zones == null)
        {
            zones = new List<int>();
            occupiedStatus[occupierId] = zones;
        }

        int tileZoneId = GetZoneId(tileZone);
        if (tileZoneId < 0 || zones.Contains(tileZoneId))
        {
            return false;
        }

        zones.Add(tileZoneId);
        city.GetOccupiedZoneOwnerMap()[tileZoneId] = occupierId;

        // 高正统占领自动扩张：只在手动/士兵占领成功后触发
        city.TrySpreadOccupiedZonesByMandate(occupier, tileZone);

        // 检查是否完成占领
        city.CheckFinishedCapture(occupier);

        return true;
    }
    public static bool RemoveOccupiedTileZone(this City city, Kingdom occupier, TileZone tileZone)
    {
        var occupierId = occupier?.id??-1L;
        if (city == null || occupier == null || tileZone == null)
        {
            return false;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;

        if (occupiedStatus == null)
        {
            return false;
        }

        if (!occupiedStatus.TryGetValue(occupierId, out var zones))
        {
            return false;
        }

        if (zones == null)
        {
            occupiedStatus.Remove(occupierId);
            return false;
        }

        int tileZoneId = GetZoneId(tileZone);
        bool removed = tileZoneId >= 0 && zones.Remove(tileZoneId);
        Dictionary<int, long> ownerMap = city.GetOccupiedZoneOwnerMap();
        if (tileZoneId >= 0 && ownerMap != null && ownerMap.TryGetValue(tileZoneId, out var currentOwner) && currentOwner == occupierId)
        {
            ownerMap.Remove(tileZoneId);
        }

        if (zones.Count == 0)
        {
            occupiedStatus.Remove(occupierId);
        }
        return removed;
    }
    public static bool TransferOccupiedTileZone(this City city, Kingdom toKingdom, TileZone tileZone)
    {
        if (city == null || toKingdom == null || tileZone == null)
        {
            return false;
        }

        if (toKingdom.isRekt() || toKingdom.isNeutral())
        {
            return false;
        }

        Kingdom currentOccupier = city.GetTileZoneOccupier(tileZone);

        if (currentOccupier == null)
        {
            bool added = city.AddOccupiedTileZone(toKingdom, tileZone);
            
            return added;
        }

        if (currentOccupier == toKingdom)
        {
            return false;
        }

        if (toKingdom.isInWarOnSameSide(currentOccupier))
        {
            return false;
        }

        city.RemoveOccupiedTileZone(currentOccupier, tileZone);

        bool transferred = city.AddOccupiedTileZone(toKingdom, tileZone);

        return transferred;
    }
    public static Dictionary<Kingdom, List<TileZone>> GetOccupiedStatus(this City city)
    {
        if (city == null)
        {
            return null;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;
        Dictionary<Kingdom, List<TileZone>> result = new Dictionary<Kingdom, List<TileZone>>();

        if (occupiedStatus == null)
        {
            return result;
        }

        foreach (var pair in occupiedStatus)
        {
            var kingdom = World.world.kingdoms.get(pair.Key);
            if (kingdom == null || pair.Value == null)
            {
                continue;
            }

            List<TileZone> zones = new List<TileZone>();
            for (int i = 0; i < pair.Value.Count; i++)
            {
                TileZone zone = GetZoneById(pair.Value[i]);
                if (zone != null)
                {
                    zones.Add(zone);
                }
            }

            result[kingdom] = zones;
        }

        return result;
    }
    public static bool RemoveOccupiedTileZoneFromAll(this City city, TileZone tileZone)
    {
        if (city == null || tileZone == null)
        {
            return false;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;
        Dictionary<int, long> ownerMap = city.GetOccupiedZoneOwnerMap();

        if (occupiedStatus == null || occupiedStatus.Count == 0)
        {
            return false;
        }

        int tileZoneId = GetZoneId(tileZone);
        if (tileZoneId >= 0 && ownerMap != null && ownerMap.TryGetValue(tileZoneId, out var directOwner))
        {
            return city.RemoveOccupiedTileZone(World.world.kingdoms.get(directOwner), tileZone);
        }

        bool removedAny = false;

        List<long> emptyKingdoms = new List<long>();

        foreach (var pair in occupiedStatus)
        {
            List<int> zones = pair.Value;

            if (zones == null)
            {
                emptyKingdoms.Add(pair.Key);
                continue;
            }

            if (tileZoneId >= 0 && zones.Remove(tileZoneId))
            {
                removedAny = true;
            }

            if (zones.Count == 0)
            {
                emptyKingdoms.Add(pair.Key);
            }
        }

        foreach (long kingdom in emptyKingdoms)
        {
            occupiedStatus.Remove(kingdom);
        }

        if (removedAny)
        {
            if (tileZoneId >= 0)
            {
                ownerMap?.Remove(tileZoneId);
            }
        }

        return removedAny;
    }
    public static Kingdom GetTileZoneOccupier(this City city, TileZone tileZone)
    {
        if (city == null || tileZone == null)
        {
            return null;
        }

        Dictionary<int, long> ownerMap = city.GetOccupiedZoneOwnerMap();
        if (ownerMap == null || ownerMap.Count == 0)
        {
            return null;
        }

        int tileZoneId = GetZoneId(tileZone);
        if (tileZoneId < 0)
        {
            return null;
        }

        ownerMap.TryGetValue(tileZoneId, out var occupier);
        return World.world.kingdoms.get(occupier);
    }
    public static bool IsTileZoneOccupied(this City city, TileZone tileZone)
    {
        return city.GetTileZoneOccupier(tileZone) != null;
    }
    public static int GetOccupiedTileZoneCount(this City city, Kingdom occupier)
    {
        if (city == null || occupier == null)
        {
            return 0;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;

        if (occupiedStatus == null)
        {
            return 0;
        }

        if (!occupiedStatus.TryGetValue(occupier?.id??-1L, out var zones))
        {
            return 0;
        }

        if (zones == null)
        {
            return 0;
        }

        return zones.Count;
    }
    public static bool CheckFinishedCapture(this City city, Kingdom occupier)
    {
        return city.CheckFinishedCapture();
    }
    public static float GetOccupiedTileZoneRate(this City city, Kingdom occupier)
    {
        if (city == null || occupier == null)
        {
            return 0f;
        }

        if (city.zones == null || city.zones.Count == 0)
        {
            return 0f;
        }

        int occupiedCount = city.GetOccupiedTileZoneCount(occupier);

        return (float)occupiedCount / city.zones.Count;
    }
    public static Kingdom GetFinishedCaptureWinner(this City city)
    {
        if (city == null)
        {
            return null;
        }

        Kingdom originKingdom = city.kingdom;

        if (originKingdom == null || originKingdom.isRekt())
        {
            return null;
        }

        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;

        if (occupiedStatus == null || occupiedStatus.Count == 0)
        {
            return null;
        }

        int validZoneCount = city.GetValidCityZoneCount();

        if (validZoneCount <= 0)
        {
            return null;
        }

        Dictionary<Kingdom, int> validOccupiedCountMap = new Dictionary<Kingdom, int>();
        Dictionary<int, long> ownerMap = city.GetOccupiedZoneOwnerMap();
        int allEnemyOccupiedCount = 0;

        foreach (var pair in occupiedStatus)
        {
            Kingdom occupier = World.world.kingdoms.get(pair.Key);
            List<int> zones = pair.Value;

            if (occupier == null || zones == null || zones.Count == 0)
            {
                continue;
            }

            if (occupier.isRekt() || occupier.isNeutral())
            {
                continue;
            }

            if (occupier == originKingdom)
            {
                continue;
            }

            // 如果和原城市国家在同一战争阵营，不算敌对占领
            if (occupier.isInWarOnSameSide(originKingdom))
            {
                continue;
            }

            int count = 0;

            for (int i = 0; i < zones.Count; i++)
            {
                TileZone zone = GetZoneById(zones[i]);

                if (!city.IsValidCaptureZone(zone))
                {
                    continue;
                }

                int zoneId = GetZoneId(zone);
                if (ownerMap != null && zoneId >= 0 && ownerMap.TryGetValue(zoneId, out var currentOwner) && currentOwner != pair.Key)
                {
                    continue;
                }

                count++;
                allEnemyOccupiedCount++;
            }

            if (count > 0)
            {
                validOccupiedCountMap[occupier] = count;
            }
        }

        if (validOccupiedCountMap.Count == 0)
        {
            return null;
        }

        Kingdom bestOccupier = null;
        int bestCount = 0;

        foreach (var pair in validOccupiedCountMap)
        {
            Kingdom occupier = pair.Key;
            int count = pair.Value;

            if (bestOccupier == null || count > bestCount)
            {
                bestOccupier = occupier;
                bestCount = count;
                continue;
            }

            if (count == bestCount)
            {
                // 平手时，用正统值更高的国家获得优先权
                int currentMandate = occupier.GetEmpire()?.Mandate ?? 0;
                int bestMandate = bestOccupier.GetEmpire()?.Mandate ?? 0;

                if (currentMandate > bestMandate)
                {
                    bestOccupier = occupier;
                    bestCount = count;
                }
            }
        }

        if (bestOccupier == null)
        {
            return null;
        }

        float occupiedRateByAllEnemies = (float)allEnemyOccupiedCount / validZoneCount;
        float requiredRate = city.GetRequiredFinishedCaptureRate(bestOccupier);

        if (occupiedRateByAllEnemies >= requiredRate)
        {
            return bestOccupier;
        }

        return null;
    }
    public static bool IsFullyOccupiedBy(this City city, Kingdom occupier)
    {
        if (city == null || occupier == null)
        {
            return false;
        }

        Kingdom winner = city.GetFinishedCaptureWinner();

        return winner == occupier;
    }

    public static int GetValidCityZoneCount(this City city)
    {
        if (city == null || city.zones == null || city.zones.Count == 0)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < city.zones.Count; i++)
        {
            TileZone zone = city.zones[i];

            if (!city.IsValidCaptureZone(zone))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public static bool IsValidCaptureZone(this City city, TileZone zone)
    {
        if (city == null || zone == null)
        {
            return false;
        }

        if (zone.world_edge)
        {
            return false;
        }

        if (zone.city != city)
        {
            return false;
        }

        return true;
    }

    public static float GetRequiredFinishedCaptureRate(this City city, Kingdom occupier)
    {
        if (city == null || occupier == null)
        {
            return 1.0f;
        }

        Kingdom originKingdom = city.kingdom;

        if (originKingdom == null)
        {
            return 1.0f;
        }

        Empire originEmpire = originKingdom.GetEmpire();
        Empire captureEmpire = occupier.GetEmpire();

        int originMandate = originEmpire?.Mandate ?? 0;
        int captureMandate = captureEmpire?.Mandate ?? 0;

        bool sameCulture = false;

        try
        {
            sameCulture =
                originKingdom.GetEmpireCraftCulture() != null &&
                originKingdom.GetEmpireCraftCulture() == occupier.GetEmpireCraftCulture();
        }
        catch
        {
            sameCulture = false;
        }

        // 基础值：默认需要 80% 土地被敌对势力占领
        float requiredRate = 0.80f;

        // 正统差距：进攻方正统越高，越容易完成占领；防守方正统越高，越难完成占领
        int mandateDiff = captureMandate - originMandate;

        // 每 1 点正统差距影响 0.5%，最多影响 15%
        float mandateModifier = Mathf.Clamp(mandateDiff * 0.005f, -0.15f, 0.15f);

        requiredRate -= mandateModifier;

        // 同族更容易接管，异族更难接管
        if (sameCulture)
        {
            requiredRate -= 0.10f;
        }
        else
        {
            requiredRate += 0.10f;
        }

        // 最低 50%，最高 95%
        requiredRate = Mathf.Clamp(requiredRate, 0.50f, 0.95f);

        return requiredRate;
    }
    
    public static bool CheckFinishedCapture(this City city)
    {
        if (city == null)
        {
            return false;
        }

        Kingdom winner = city.GetFinishedCaptureWinner();

        if (winner == null)
        {
            return false;
        }

        if (city.kingdom == winner)
        {
            return false;
        }

        city.finishCapture(winner);
        city.ClearOccupiedStatus();

        return true;
    }
    private static float GetOccupationCaptureChance(this Actor victim)
    {
        if (victim == null) return 0f;
        if (victim.IsEmperor()) return OccupationCaptureEmperorChance;
        if (victim.isKing()) return OccupationCaptureKingChance;
        if (victim.isCityLeader()) return OccupationCaptureLordChance;
        return 0f;
    }

    private static Actor FindOccupationCaptureTarget(this City city, TileZone zone, bool zoneOccupationMode)
    {
        if (city == null || city.kingdom == null)
        {
            return null;
        }

        Actor emperor = city.kingdom.GetEmpire()?.Emperor;
        if (emperor != null && !emperor.isRekt() && emperor.isAlive())
        {
            if (!zoneOccupationMode || emperor.current_tile?.zone == zone)
            {
                return emperor;
            }
        }

        Actor king = city.kingdom.king;
        if (king != null && !king.isRekt() && king.isAlive())
        {
            if (!zoneOccupationMode || king.current_tile?.zone == zone)
            {
                return king;
            }
        }

        Actor leader = city.hasLeader() ? city.leader : null;
        if (leader != null && !leader.isRekt() && leader.isAlive())
        {
            if (!zoneOccupationMode || leader.current_tile?.zone == zone)
            {
                return leader;
            }
        }

        return null;
    }

    private static void ApplyOccupationCaptureRewards(this City city, Actor capturer, Actor victim)
    {
        if (capturer == null || capturer.isRekt() || victim == null || victim.isRekt())
        {
            return;
        }

        int performanceReward = victim.IsEmperor()
            ? OccupationCaptureEmperorPerformanceReward
            : (victim.isKing() ? OccupationCaptureKingPerformanceReward : OccupationCaptureLordPerformanceReward);

        if (capturer.GetIdentity() != null)
        {
            capturer.GetIdentity().TotalPerformance += performanceReward;
        }

        int capturedInfluence = victim.data?.renown ?? 0;
        if (capturer.data != null && capturedInfluence > 0)
        {
            capturer.data.renown += capturedInfluence;
            victim.data.renown = 0;
        }
    }

    private static void ForceOccupyAllRemainingZones(this City city, Kingdom occupier)
    {
        if (city == null || occupier == null || city.zones == null)
        {
            return;
        }
        var occupierId = occupier?.id??-1L;
        Dictionary<long, List<int>> occupiedStatus = city.GetOrCreate().OccupiedStatus;
        if (!occupiedStatus.ContainsKey(occupierId))
        {
            occupiedStatus[occupierId] = new List<int>();
        }

        List<int> occupierZones = occupiedStatus[occupierId];
        for (int i = 0; i < city.zones.Count; i++)
        {
            TileZone currentZone = city.zones[i];
            if (!city.IsValidCaptureZone(currentZone))
            {
                continue;
            }

            Kingdom currentOccupier = city.GetTileZoneOccupier(currentZone);
            if (currentOccupier != null && currentOccupier != occupier)
            {
                city.RemoveOccupiedTileZone(currentOccupier, currentZone);
            }

            int currentZoneId = GetZoneId(currentZone);
            if (currentZoneId < 0)
            {
                continue;
            }

            if (!occupierZones.Contains(currentZoneId))
            {
                occupierZones.Add(currentZoneId);
            }

            city.GetOccupiedZoneOwnerMap()[currentZoneId] = occupierId;
        }
    }

    private static void ForceKingdomSurrenderTo(this Kingdom defeated, Kingdom occupier, City capturedCity)
    {
        if (defeated == null || occupier == null || defeated.isRekt() || defeated == occupier)
        {
            return;
        }

        List<City> citySnapshot = new List<City>(defeated.cities);
        if (capturedCity != null && capturedCity.kingdom == defeated)
        {
            capturedCity.finishCapture(occupier);
        }

        for (int i = 0; i < citySnapshot.Count; i++)
        {
            City targetCity = citySnapshot[i];
            if (targetCity == null || targetCity.isRekt() || targetCity.kingdom != defeated)
            {
                continue;
            }

            targetCity.joinAnotherKingdom(occupier, true);
        }
    }

    public static bool TryTriggerOccupationCaptureEvent(this City city, Kingdom occupier, TileZone zone = null, Actor capturer = null, bool zoneOccupationMode = false)
    {
        if (!EnableOccupationCaptureEvents)
        {
            return false;
        }

        // Only a concrete occupied battle zone can produce a capture event.
        if (city == null || occupier == null || zone == null || city.kingdom == null || city.kingdom == occupier)
        {
            return false;
        }

        Actor victim = city.FindOccupationCaptureTarget(zone, zoneOccupationMode);
        if (victim == null || victim.kingdom == null || victim.kingdom == occupier)
        {
            return false;
        }

        float chance = victim.GetOccupationCaptureChance();
        if (chance <= 0f || UnityEngine.Random.value > chance)
        {
            return false;
        }

        Actor captureActor = capturer;
        if (captureActor == null || captureActor.isRekt() || captureActor.kingdom != occupier)
        {
            captureActor = occupier.king;
        }

        city.ApplyOccupationCaptureRewards(captureActor, victim);

        if (victim.IsEmperor())
        {
            victim.GetEmpire()?.AddMandate(-3000);
            if (zoneOccupationMode)
            {
                city.ForceOccupyAllRemainingZones(occupier);
                city.CheckFinishedCapture();
            }
            else
            {
                city.finishCapture(occupier);
            }

            TranslateHelper.LogOccupationCaptureEvent(captureActor, victim, "occupation_capture_result_emperor", occupier.GetEmpire());
            return true;
        }

        if (victim.isKing())
        {
            Kingdom victimKingdom = victim.kingdom;
            if (victimKingdom == null || victimKingdom.isRekt()) return false;
            War activeWar = null;
            War typedWar = null;
            foreach (War war in occupier.getWars())
            {
                if (war == null || !war.isAlive())
                {
                    continue;
                }

                bool occupierParticipates = war._list_attackers.Contains(occupier) || war._list_defenders.Contains(occupier);
                bool victimParticipates = war._list_attackers.Contains(victimKingdom) || war._list_defenders.Contains(victimKingdom);
                if (!occupierParticipates || !victimParticipates)
                {
                    continue;
                }

                activeWar ??= war;
                if (war.GetEmpireWarType() != EmpireWarType.None)
                {
                    typedWar = war;
                    break;
                }
            }

            if (typedWar != null)
            {
                typedWar.lostWar(victimKingdom);
                if (occupier.GetEmpire()?.CoreKingdom?.GetRegime()?.enable_auto_honorary_peerages == true)
                {
                    captureActor.GrantHonoraryPeerage(occupier.GetEmpire(), "tang_honorary_zhongyi_hou");
                }
                TranslateHelper.LogOccupationCaptureEvent(captureActor, victim, "occupation_capture_result_king_war_released", occupier.GetEmpire());
                return true;
            }

            if (activeWar != null)
            {
                activeWar.lostWar(victimKingdom);
                if (occupier.GetEmpire()?.CoreKingdom?.GetRegime()?.enable_auto_honorary_peerages == true)
                {
                    captureActor.GrantHonoraryPeerage(occupier.GetEmpire(), "tang_honorary_zhongyi_hou");
                }
            }

            if (UnityEngine.Random.value <= OccupationCaptureKingReleaseChance)
            {
                TranslateHelper.LogOccupationCaptureEvent(captureActor, victim, "occupation_capture_result_king_released", occupier.GetEmpire());
                return true;
            }

            if (zoneOccupationMode)
            {
                city.ForceOccupyAllRemainingZones(occupier);
                city.CheckFinishedCapture();
            }
            else
            {
                city.finishCapture(occupier);
            }

            TranslateHelper.LogOccupationCaptureEvent(captureActor, victim, "occupation_capture_result_king_city", occupier.GetEmpire());
            return true;
        }

        if (victim.isCityLeader())
        {
            if (zoneOccupationMode)
            {
                city.ForceOccupyAllRemainingZones(occupier);
                city.CheckFinishedCapture();
            }
            else
            {
                city.finishCapture(occupier);
            }

            TranslateHelper.LogOccupationCaptureEvent(captureActor, victim, "occupation_capture_result_lord", occupier.GetEmpire());
            return true;
        }

        return false;
    }
    public static void ClearOccupiedStatus(this City city)
    {
        if (city == null)
        {
            return;
        }

        try
        {
            city.clearCapture();
        }
        catch
        {
            // Ignore vanilla capture visual cleanup failures here.
        }

        CityExtraData data = city.GetOrCreate();
        if (data.OccupiedStatus == null || data.OccupiedStatus.Count == 0)
        {
            data.OccupiedZoneOwners?.Clear();
            return;
        }
        data.OccupiedStatus = new Dictionary<long, List<int>>();
        data.OccupiedZoneOwners?.Clear();
    }
    public static EmpireCore GetEmpireCore(this City c)
    {
        EmpireCoreManager.EmpireCores.TryGetValue(c.GetEmpireCoreID(), out EmpireCore core);
        return core;
    }
    public static void AddCorruptionRate(this City city, double addition)
    {
        if (city == null || addition == 0) return;
        double current = city.GetCorruptionRate();
        if ((current >= 1.0f && addition > 0) || (current <= 0.0f && addition < 0))
        {
            return;
        }

        city.SetCorruptionRate(current + addition);
    }

    public static bool HasBeenCombined(this City city)
    {
        return city.buildings.Any(b => b.asset.id.Contains("city_"));
    }
    public static void InitialRegime(this City city)
    {
        if (!city.hasKingdom()) return;
        if (city.kingdom.GetRegime()==null) return;
        CityType cityType = EmpireCraftKingdomBehCheckKingdomType.CalcCityType(city.kingdom);
        city.SetCityType(cityType);
        BureauSetting citySetting = null;
        var bc = city.kingdom.GetRegime().bureau_config;
        if (bc != null && bc.cities != null)
        {
            bc.cities.TryGetValue(cityType, out citySetting);
        }
        if (citySetting == null)
        {
            citySetting = new BureauSetting
            {
                type = 0,
                pre = "",
                description = "",
                powers = new List<OfficerPowerType>(),
                merit = 0,
                honorary = 0,
                select_from_local = false,
                leader_select_method = LeaderSelectMethod.Default,
                require_traits = new List<string>(),
                condition = new List<string>(),
                city_type = cityType
            };
        }
        OfficeObject officeObject2 = new OfficeObject();
        officeObject2.InitialOffice(citySetting);
        officeObject2.regimeType = city.kingdom.GetRegime().type;
        officeObject2.meta_object = city;
        officeObject2.is_local = true;
        if (city.hasLeader())
        {
            officeObject2.SetActor(city.leader);
        }
        city.SetOffice(officeObject2);
    }
    public static double GetCorruptionRate(this City city)
    {
        return city.GetOrCreate().corruption_rate;
    }

    public static void SetCorruptionRate(this City city, Double value)
    {
        if (city == null) return;
        city.GetOrCreate().corruption_rate = Math.Max(0.0, Math.Min(1.0, value));
    }
    public static void SetCityType(this City c, CityType type)
    {
        c.GetOrCreate().cityType = type;
    }
    public static double GetLastTaxTime(this City k)
    {
        return k.GetOrCreate().last_tax_timestamp;
    }
    public static void RecordTaxTime(this City k)
    {
        k.GetOrCreate().last_tax_timestamp = World.world.getCurWorldTime();
    }

    public static bool IsLawScanDue(this City city, float years = 1f)
    {
        if (city == null) return false;
        double value = city.GetOrCreate().last_law_scan_ts;
        if (value < 0) return true;
        return Date.getYearsSince(value) >= years;
    }

    public static void RecordLawScan(this City city)
    {
        if (city == null) return;
        city.GetOrCreate().last_law_scan_ts = World.world.getCurWorldTime();
    }

    public static bool IsNeedToSubmitTax(this City k)
    {
        if (!k.hasKingdom()) return false;
        return Date.getYearsSince(k.GetLastTaxTime()) >= 1;
    }
    
    public static int GetMoney(this City c)
    {
        return c.GetOrCreate().Money;
    }

    public static void AddMoney(this City c, int money)
    {
        c.GetOrCreate().Money += money;
    }

    public static void SubMoney(this City c, int money)
    {
        c.GetOrCreate().Money -= money; 
    }

    public static CityType GetCityType(this City c)
    {
        return c.GetOrCreate().cityType;
    }
    public static void SetOffice(this City c, OfficeObject off)
    {
        OfficeManager.Remove(c.GetOfficeID());
        c.GetOrCreate().office_id = off.OfficeID;
    }

    public static OfficeObject GetOffice(this City c)
    {
        return OfficeManager.Offices.TryGetValue(c.GetOrCreate().office_id, out OfficeObject office) ? office : null;
    }

    public static long GetOfficeID(this City c)
    {
        return c.GetOrCreate().office_id;
    }
    public static CityExtraData GetOrCreate(this City a, bool isSave=false)
    {
        var ed = a.GetOrCreate< City, CityExtraData>(isSave);
        return ed;
    }

    public static int CountLivingPopulation(this City city)
    {
        if (city == null || city.units == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < city.units.Count; i++)
        {
            Actor actor = city.units[i];
            if (actor == null || actor.isRekt()) continue;
            if (!actor.isAlive()) continue;
            count++;
        }

        return count;
    }

    public static int CountLivingWarriors(this City city)
    {
        if (city == null || city.units == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < city.units.Count; i++)
        {
            Actor actor = city.units[i];
            if (actor == null || actor.isRekt()) continue;
            if (!actor.isAlive()) continue;
            if (!actor.isWarrior()) continue;
            count++;
        }

        return count;
    }

    public static bool IsBorderCity(this City city)
    {
        return city != null && city.neighbours_kingdoms != null && city.neighbours_kingdoms.Count > 0;
    }

    public static City FindExileCity(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || kingdom.cities == null || kingdom.cities.Count <= 0)
        {
            return null;
        }

        City borderEmptyCity = null;
        City leastPopulatedCity = null;
        int leastPopulation = int.MaxValue;

        for (int i = 0; i < kingdom.cities.Count; i++)
        {
            City city = kingdom.cities[i];
            if (city == null || city.isRekt()) continue;

            int population = city.CountLivingPopulation();
            if (population <= 0 && city.IsBorderCity())
            {
                borderEmptyCity = city;
                break;
            }

            if (population < leastPopulation)
            {
                leastPopulation = population;
                leastPopulatedCity = city;
            }
        }

        if (borderEmptyCity != null)
        {
            return borderEmptyCity;
        }

        if (leastPopulatedCity != null)
        {
            return leastPopulatedCity;
        }

        return kingdom.capital;
    }

    public static void StartChoosingHeir(this City c)
    {
        c.GetOrCreate().is_choosing_heir = true;
    }

    public static bool IsChoosingHeir(this City c)
    {
        return c.GetOrCreate().is_choosing_heir;
    }

    public static void EndChoosingHeir(this City c)
    {
        c.GetOrCreate().is_choosing_heir = false;
    }
    public static PersonalClanIdentity GetPersonalIdentity(this City a)
    {
        return SpecificClanManager.getPerson(a.GetOrCreate().personalIdentityId);
    }

    public static void SetPersonalIdentity(this City a, PersonalClanIdentity personalId)
    {
        a.GetOrCreate().personalIdentityId = personalId?.id??-1L;
    }
    public static void SetLimitInput(this City c, TextInput input)
    {
        var ed = c.GetOrCreate();
        ed.limitationNumber = input;
    }

    public static TextInput GetLimitInput(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.limitationNumber;
    }

    public static bool HasReachedPlayerPopLimit(this City c)
    {
        if (c == null) return true;
        var ed = c.GetOrCreate();
        if (ed == null) return true;
        if (c.getPopulationPeople()>ed.MAX_POPULATION&&ed.MAX_POPULATION_LIMIT)
        {
            return true;
        }
        return false;
    }

    public static void SetLimitToggle(this City c, SimpleButton limitToggle)
    {
        var ed = c.GetOrCreate();
        ed.limitToggle = limitToggle;
    }

    public static SimpleButton GetLimitToggle(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.limitToggle;
    }

    public static int GetMaxPopulation(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.MAX_POPULATION;
    }
    public static void SetMaxPopulation(this City c, int value)
    {
        var ed = c.GetOrCreate();
        ed.MAX_POPULATION = value;
    }
    public static void OpenMaxPopulationLimit(this City c)
    {
        var ed = c.GetOrCreate();
        ed.MAX_POPULATION_LIMIT = true;
    }
    public static bool GetMaxPopulationLimitStats(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.MAX_POPULATION_LIMIT;
    }
    public static void CloseMaxPopulationLimit(this City c)
    {
        var ed = c.GetOrCreate();
        ed.MAX_POPULATION_LIMIT = false;
    }
    public static List<long> GetExamPassPersonIDs(this City c)
    {
        if (GetOrCreate(c).exam_pass_person== null)
        {
            GetOrCreate(c).exam_pass_person = new List<long> {0};
        }
        return GetOrCreate(c).exam_pass_person;
    }
 
    public static long GetEmpireCoreID(this  City a)
    {
        return GetOrCreate(a).empire_core_id;
    }

    public static void SetEmpireCore(this City a, EmpireCore core)
    {
        GetOrCreate(a).empire_core_id = core?.id ?? -1L;
    }

    public static bool hasTitle(this City c)
    {
        if (c == null) return false;
        if (GetOrCreate(c)==null) return false; 
        return GetOrCreate(c).title_id!=-1L;
    }
    
    public static void Clear()
    {
        ExtensionManager<City, CityExtraData>.Clear();
    }

    public static long GetTitleID(this City c)
    {
        if (c == null) return -1;
        return GetOrCreate(c).title_id;
    }

    public static void SetTitleID(this City c, long id)
    {
        GetOrCreate(c).title_id = id;
    }

    public static KingdomTitle GetTitle(this City c)
    {
        var ed = GetOrCreate(c);
        if (ed == null) return null;
        KingdomTitle title = ed.title_id==-1L?null:ModClass.KINGDOM_TITLE_MANAGER.get(ed.title_id);
        if (title==null) c.RemoveTitle();
        return title;
    }
    

    public static void SetTitle(this City c, KingdomTitle title)
    {
        var ed = GetOrCreate(c);
        ed.title_id = title.getID();
    }

    public static void RemoveTitle(this City c)
    {
        GetOrCreate(c).title_id = -1L;
    }

    public static string GetCityName(this City city)
    {
        if (city == null) return null;
        if (string.IsNullOrEmpty(city.name)) return null;
        string[] nameParts = city.name.Split('\u200A');
        string result = null;

        if (ConfigData.speciesCulturePair.TryGetValue(city.getSpecies(), out var culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length-1 >= setting.City.name_pos)
                {
                    result = nameParts[setting.City.name_pos].Split(' ').Last();
                }
            }
        }
        result ??= nameParts[0].Split(' ').Last();
        if (string.IsNullOrWhiteSpace(result))
        {
            return result;
        }

        if (city.hasKingdom())
        {
            string citySuffix = LM.Get(city.GetCityType().ToString());
            if (!string.IsNullOrWhiteSpace(citySuffix) &&
                result.Length > citySuffix.Length &&
                result.EndsWith(citySuffix, StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - citySuffix.Length);
            }
        }

        return result;
    }

    public static string GetKingdomNames(this City city)
    {
        return GetOrCreate(city).kingdom_names;
    }
    public static void SetKingdomNames(this City city, string value)
    {
        GetOrCreate(city).id = city.getID();
        GetOrCreate(city).kingdom_names = value;
    }

    public static Empire GetEmpire(this City city)
    {
        if (city == null) return null;
        if (city.kingdom == null) return null;
        return ModClass.EMPIRE_MANAGER.get(city.kingdom.GetEmpireID());
    }
    
    public static void AddKingdomName(this City city, string kingdomName)
    {
        if (!GetOrCreate(city).kingdom_names.Contains(kingdomName))
        {
            GetOrCreate(city).kingdom_names = String.Join("\u200A", GetOrCreate(city).kingdom_names,kingdomName);
        }
    }
    public static string SelectKingdomName(this City city)
    {
        if (city == null) return "";
        var names = (GetOrCreate(city).kingdom_names ?? "")
            .Split('\u200A')
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        return names.Count > 0 ? names.GetRandom() : "";
    }

    public static bool HasKingdomName(this City city) 
    {
        if (city == null) return false;
        return !string.IsNullOrWhiteSpace(SelectKingdomName(city));
    }

}
