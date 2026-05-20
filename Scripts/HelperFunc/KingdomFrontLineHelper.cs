using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

public static class KingdomFrontLineHelper
{
    private const float NearSideRange = 400f;
    private static int _cacheFrame = -1;
    private static readonly Dictionary<Kingdom, HashSet<TileZone>> _kingdomZonesCache = new();
    private static readonly Dictionary<Kingdom, HashSet<TileZone>> _effectiveZonesCache = new();
    private static readonly Dictionary<Kingdom, List<Kingdom>> _friendlyWarKingdomsCache = new();
    private static readonly Dictionary<Kingdom, HashSet<TileZone>> _occupiedZonesByKingdomCache = new();
    private static bool _occupiedZonesIndexed;

    private static void EnsureFrameCache()
    {
        int frame = Time.frameCount;
        if (_cacheFrame == frame)
        {
            return;
        }

        _cacheFrame = frame;
        _kingdomZonesCache.Clear();
        _effectiveZonesCache.Clear();
        _friendlyWarKingdomsCache.Clear();
        _occupiedZonesByKingdomCache.Clear();
        _occupiedZonesIndexed = false;
    }

    /// <summary>
    /// 获取 actorKingdom 面对所有敌国的有效敌方前线 zone。
    /// </summary>
    public static List<TileZone> GetAllEnemyFrontZones(Kingdom actorKingdom)
    {
        List<TileZone> result = new List<TileZone>();
        HashSet<TileZone> uniqueZones = new HashSet<TileZone>();

        if (actorKingdom == null)
        {
            return result;
        }

        var enemies = actorKingdom.getEnemiesKingdoms();

        if (enemies == null)
        {
            return result;
        }

        foreach (Kingdom enemyKingdom in enemies)
        {
            if (enemyKingdom == null || enemyKingdom.isRekt() || enemyKingdom.isNeutral())
            {
                continue;
            }

            if (!actorKingdom.isInWarWith(enemyKingdom))
            {
                continue;
            }

            List<TileZone> zones = GetEnemyFrontZones(actorKingdom, enemyKingdom);

            foreach (TileZone zone in zones)
            {
                if (zone == null)
                {
                    continue;
                }

                if (uniqueZones.Add(zone))
                {
                    result.Add(zone);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 获取 enemyKingdom 面向 actorKingdom 阵营的有效前线。
    /// 如果有直接接壤前线，则返回直接接壤前线。
    /// 如果没有直接接壤，则返回敌国靠近 actorKingdom 阵营的那一侧边界。
    /// </summary>
    public static List<TileZone> GetEnemyFrontZones(Kingdom actorKingdom, Kingdom enemyKingdom)
    {
        List<TileZone> directZones = GetDirectEnemyFrontZones(actorKingdom, enemyKingdom);

        if (directZones.Count > 0)
        {
            return directZones;
        }

        return GetNearSideEnemyBorderZones(actorKingdom, enemyKingdom, NearSideRange);
    }

    /// <summary>
    /// 获取直接接壤的敌方前线。
    /// enemyZone 属于 enemyKingdom，
    /// 并且它相邻的 zone 中存在 actorKingdom 同阵营控制区。
    /// </summary>
    public static List<TileZone> GetDirectEnemyFrontZones(Kingdom actorKingdom, Kingdom enemyKingdom)
    {
        List<TileZone> result = new List<TileZone>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<TileZone> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (enemyZones.Count == 0)
        {
            return result;
        }

        foreach (TileZone enemyZone in enemyZones)
        {
            if (!IsValidZone(enemyZone))
            {
                continue;
            }

            Kingdom occupier = GetZoneOccupier(enemyZone);

            // 已经被己方或友军占领的敌方 zone，不再算敌方前线目标
            if (occupier != null && IsFriendlyKingdom(actorKingdom, occupier))
            {
                continue;
            }

            bool touchesFriendlyControlledZone = false;
            bool touchesNonWarKingdom = false;

            if (enemyZone.neighbours_all == null)
            {
                continue;
            }

            foreach (TileZone neighbour in enemyZone.neighbours_all)
            {
                if (!IsValidZone(neighbour))
                {
                    continue;
                }

                Kingdom neighbourController = GetEffectiveZoneController(neighbour);

                // 友军占领区算作我方控制区
                if (IsFriendlyControlledZone(actorKingdom, neighbour))
                {
                    touchesFriendlyControlledZone = true;
                    continue;
                }

                // 自己或同阵营友军原生领土，也算作我方控制区

                // 敌国自己的 zone，不算边界
                if (neighbourController == enemyKingdom)
                {
                    continue;
                }

                // 接壤非交战第三方国家的边界，不算当前战争前线
                if (neighbourController != null &&
                    !IsFriendlyKingdom(actorKingdom, neighbourController) &&
                    !actorKingdom.isInWarWith(neighbourController))
                {
                    touchesNonWarKingdom = true;
                    break;
                }
            }

            if (!touchesFriendlyControlledZone)
            {
                continue;
            }

            if (touchesNonWarKingdom)
            {
                continue;
            }

            result.Add(enemyZone);
        }

        return result;
    }

    /// <summary>
    /// 不直接接壤时，获取敌国靠近 actorKingdom 同阵营的那一侧边界。
    /// 注意：这里不会使用所有友军国土，而只使用“面向该敌国的友军源头 zone”。
    /// </summary>
    public static List<TileZone> GetNearSideEnemyBorderZones(Kingdom actorKingdom, Kingdom enemyKingdom, float range)
    {
        List<TileZone> result = new List<TileZone>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<TileZone> sourceZones = GetFriendlyFrontSourceZonesFacingEnemy(actorKingdom, enemyKingdom);
        HashSet<TileZone> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (sourceZones.Count == 0 || enemyZones.Count == 0)
        {
            return result;
        }

        List<TileZone> enemyBorderZones = new List<TileZone>();

        foreach (TileZone enemyZone in enemyZones)
        {
            if (!IsValidZone(enemyZone))
            {
                continue;
            }

            Kingdom occupier = GetZoneOccupier(enemyZone);

            // 已经被己方或友军占领的敌方 zone，不再作为敌方边界目标
            if (occupier != null && IsFriendlyKingdom(actorKingdom, occupier))
            {
                continue;
            }

            bool isBorder = false;
            bool touchesNonWarKingdom = false;

            if (enemyZone.neighbours_all == null)
            {
                continue;
            }

            foreach (TileZone neighbour in enemyZone.neighbours_all)
            {
                if (!IsValidZone(neighbour))
                {
                    continue;
                }

                Kingdom neighbourController = GetEffectiveZoneController(neighbour);

                if (neighbourController != enemyKingdom)
                {
                    isBorder = true;

                    // 如果这段边界接壤的是中立或非当前战争国家，则排除
                    if (neighbourController != null &&
                        !IsFriendlyKingdom(actorKingdom, neighbourController) &&
                        !actorKingdom.isInWarWith(neighbourController))
                    {
                        touchesNonWarKingdom = true;
                        break;
                    }
                }
            }

            if (!isBorder)
            {
                continue;
            }

            if (touchesNonWarKingdom)
            {
                continue;
            }

            enemyBorderZones.Add(enemyZone);
        }

        if (enemyBorderZones.Count == 0)
        {
            return result;
        }

        float nearestDistance = float.MaxValue;
        Dictionary<TileZone, float> bestDistances = new Dictionary<TileZone, float>(enemyBorderZones.Count);

        foreach (TileZone enemyBorderZone in enemyBorderZones)
        {
            float bestDistanceToSource = float.MaxValue;

            foreach (TileZone sourceZone in sourceZones)
            {
                float distance = GetZoneDistanceSqr(enemyBorderZone, sourceZone);

                if (distance < bestDistanceToSource)
                {
                    bestDistanceToSource = distance;
                }
            }

            if (bestDistanceToSource < float.MaxValue)
            {
                bestDistances[enemyBorderZone] = bestDistanceToSource;
            }

            if (bestDistanceToSource < nearestDistance)
            {
                nearestDistance = bestDistanceToSource;
            }
        }

        if (nearestDistance == float.MaxValue)
        {
            return result;
        }

        foreach (TileZone enemyBorderZone in enemyBorderZones)
        {
            if (bestDistances.TryGetValue(enemyBorderZone, out float bestDistanceToSource) &&
                bestDistanceToSource <= nearestDistance + range)
            {
                result.Add(enemyBorderZone);
            }
        }

        return result;
    }

    /// <summary>
    /// 获取“面向 enemyKingdom 的友军前线源头”。
    /// 这个方法不会返回所有友军国土，只返回：
    /// 1. actorKingdom 自己的国土；
    /// 2. 与 enemyKingdom 接壤的同阵营友军 zone；
    /// 3. 已经被同阵营友军占领的 enemyKingdom zone。
    /// </summary>
    public static HashSet<TileZone> GetFriendlyFrontSourceZonesFacingEnemy(Kingdom actorKingdom, Kingdom enemyKingdom)
    {
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<TileZone> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (enemyZones.Count == 0)
        {
            return result;
        }

        // actorKingdom 自己的国土永远作为基础方向来源
        foreach (TileZone zone in GetEffectiveControlledZones(actorKingdom))
        {
            if (IsValidZone(zone) && IsFriendlyControlledZone(actorKingdom, zone))
            {
                result.Add(zone);
            }
        }

        // 友军只有在真正接壤当前 enemyKingdom 时，才作为方向来源
        foreach (Kingdom friendlyKingdom in GetFriendlyWarKingdoms(actorKingdom))
        {
            if (friendlyKingdom == actorKingdom)
            {
                continue;
            }

            foreach (TileZone zone in GetEffectiveControlledZones(friendlyKingdom))
            {
                if (!IsValidZone(zone))
                {
                    continue;
                }

                if (!IsFriendlyControlledZone(actorKingdom, zone))
                {
                    continue;
                }

                if (ZoneTouchesKingdom(zone, enemyKingdom))
                {
                    result.Add(zone);
                }
            }
        }

        // 已经被任意同阵营友军占领的敌方 zone，也作为下一轮推进源头
        foreach (TileZone enemyZone in enemyZones)
        {
            if (!IsValidZone(enemyZone))
            {
                continue;
            }

            Kingdom occupier = GetZoneOccupier(enemyZone);

            if (occupier != null && IsFriendlyKingdom(actorKingdom, occupier))
            {
                result.Add(enemyZone);
            }
        }

        return result;
    }

    /// <summary>
    /// 获取所有同一战争阵营的友军国家，包括 actorKingdom 自己。
    /// </summary>
    public static List<Kingdom> GetFriendlyWarKingdoms(Kingdom actorKingdom)
    {
        EnsureFrameCache();

        if (actorKingdom == null)
        {
            return new List<Kingdom>();
        }

        if (_friendlyWarKingdomsCache.TryGetValue(actorKingdom, out var cachedResult))
        {
            return cachedResult;
        }

        List<Kingdom> result = new List<Kingdom>();
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom == null || kingdom.isRekt() || kingdom.isNeutral())
            {
                continue;
            }

            if (!IsFriendlyKingdom(actorKingdom, kingdom))
            {
                continue;
            }

            if (!result.Contains(kingdom))
            {
                result.Add(kingdom);
            }
        }

        _friendlyWarKingdomsCache[actorKingdom] = result;
        return result;
    }

    /// <summary>
    /// 获取所有同阵营控制区。
    /// 这个方法可以用于士兵填充、连续战线判断。
    /// 不要用它来判断“不接壤时的敌国靠近哪一侧”，否则会把远处友军国土也算进去。
    /// </summary>
    public static HashSet<TileZone> GetAllFriendlyControlledZones(Kingdom actorKingdom)
    {
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (actorKingdom == null)
        {
            return result;
        }

        foreach (Kingdom friendlyKingdom in GetFriendlyWarKingdoms(actorKingdom))
        {
            foreach (TileZone zone in GetEffectiveControlledZones(friendlyKingdom))
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 获取某个王国全部城市 zone。
    /// </summary>
    public static HashSet<TileZone> GetKingdomZones(Kingdom kingdom)
    {
        EnsureFrameCache();
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (kingdom == null || kingdom.cities == null)
        {
            return result;
        }

        if (_kingdomZonesCache.TryGetValue(kingdom, out var cachedResult))
        {
            return cachedResult;
        }

        foreach (City city in kingdom.cities)
        {
            if (city == null || city.zones == null)
            {
                continue;
            }

            foreach (TileZone zone in city.zones)
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        _kingdomZonesCache[kingdom] = result;
        return result;
    }

    public static HashSet<TileZone> GetEffectiveControlledZones(Kingdom kingdom)
    {
        EnsureFrameCache();
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (kingdom == null)
        {
            return result;
        }

        if (_effectiveZonesCache.TryGetValue(kingdom, out var cachedResult))
        {
            return cachedResult;
        }

        foreach (TileZone zone in GetKingdomZones(kingdom))
        {
            if (!IsValidZone(zone))
            {
                continue;
            }

            Kingdom occupier = GetZoneOccupier(zone);
            if (occupier == null || occupier == kingdom)
            {
                result.Add(zone);
            }
        }

        EnsureOccupiedZoneIndex();
        if (_occupiedZonesByKingdomCache.TryGetValue(kingdom, out var occupiedZones))
        {
            foreach (TileZone zone in occupiedZones)
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        _effectiveZonesCache[kingdom] = result;
        return result;
    }

    private static void EnsureOccupiedZoneIndex()
    {
        EnsureFrameCache();
        if (_occupiedZonesIndexed)
        {
            return;
        }

        _occupiedZonesIndexed = true;
        if (World.world?.cities == null)
        {
            return;
        }

        foreach (City city in World.world.cities)
        {
            if (city == null || city.isRekt() || city.zones == null)
            {
                continue;
            }

            foreach (TileZone zone in city.zones)
            {
                if (!IsValidZone(zone))
                {
                    continue;
                }

                Kingdom occupier = GetZoneOccupier(zone);
                if (occupier == null)
                {
                    continue;
                }

                if (!_occupiedZonesByKingdomCache.TryGetValue(occupier, out var zones))
                {
                    zones = new HashSet<TileZone>();
                    _occupiedZonesByKingdomCache[occupier] = zones;
                }

                zones.Add(zone);
            }
        }
    }

    /// <summary>
    /// 判断一个 zone 是否与指定王国接壤。
    /// </summary>
    public static bool ZoneTouchesKingdom(TileZone zone, Kingdom kingdom)
    {
        if (!IsValidZone(zone) || kingdom == null)
        {
            return false;
        }

        if (zone.neighbours_all == null)
        {
            return false;
        }

        HashSet<TileZone> controlledZones = GetEffectiveControlledZones(kingdom);

        foreach (TileZone neighbour in zone.neighbours_all)
        {
            if (!IsValidZone(neighbour))
            {
                continue;
            }

            if (controlledZones.Contains(neighbour))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取 zone 原本所属王国。
    /// </summary>
    public static Kingdom GetZoneKingdom(TileZone zone)
    {
        if (zone == null || zone.city == null)
        {
            return null;
        }

        return zone.city.kingdom;
    }

    /// <summary>
    /// 获取 zone 当前占领国。
    /// </summary>
    public static Kingdom GetZoneOccupier(TileZone zone)
    {
        if (zone == null || zone.city == null)
        {
            return null;
        }

        return zone.city.GetTileZoneOccupier(zone);
    }

    public static Kingdom GetEffectiveZoneController(TileZone zone)
    {
        if (zone == null || zone.city == null)
        {
            return null;
        }

        return GetZoneOccupier(zone) ?? GetZoneKingdom(zone);
    }

    public static bool IsFriendlyControlledZone(Kingdom actorKingdom, TileZone zone)
    {
        if (actorKingdom == null || !IsValidZone(zone))
        {
            return false;
        }

        Kingdom controller = GetEffectiveZoneController(zone);
        return controller != null && IsFriendlyKingdom(actorKingdom, controller);
    }

    /// <summary>
    /// 判断两个王国是否属于同一战争阵营。
    /// </summary>
    public static bool IsFriendlyKingdom(Kingdom actorKingdom, Kingdom otherKingdom)
    {
        if (actorKingdom == null || otherKingdom == null)
        {
            return false;
        }

        if (actorKingdom == otherKingdom)
        {
            return true;
        }

        return actorKingdom.isInWarOnSameSide(otherKingdom);
    }

    public static bool IsValidZone(TileZone zone)
    {
        if (zone == null)
        {
            return false;
        }

        if (zone.world_edge)
        {
            return false;
        }

        return true;
    }

    public static float GetZoneDistanceSqr(TileZone a, TileZone b)
    {
        if (a == null || b == null)
        {
            return float.MaxValue;
        }

        int dx = a.x - b.x;
        int dy = a.y - b.y;

        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 统计 zone 内同阵营士兵数量。
    /// 用于判断某个前线 zone 是否已经被友军填满。
    /// </summary>
    public static int CountFriendlyWarriorsInZone(TileZone zone, Kingdom actorKingdom)
    {
        if (zone == null || actorKingdom == null)
        {
            return 0;
        }

        int count = 0;

        foreach (Actor actor in World.world.units)
        {
            if (actor == null || actor.kingdom == null)
            {
                continue;
            }

            if (!actor.isWarrior())
            {
                continue;
            }

            if (actor.current_tile == null || actor.current_tile.zone != zone)
            {
                continue;
            }

            if (IsFriendlyKingdom(actorKingdom, actor.kingdom))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 调试用：打印并标记该王国面对所有敌人的前线。
    /// </summary>
    public static void DebugFrontZones(Kingdom actorKingdom)
    {
        if (actorKingdom == null)
        {
            LogService.LogInfo("FrontLine Debug: actorKingdom is null");
            return;
        }

        var enemies = actorKingdom.getEnemiesKingdoms();

        if (enemies == null)
        {
            LogService.LogInfo("FrontLine Debug: no enemies");
            return;
        }

        foreach (Kingdom enemyKingdom in enemies)
        {
            if (enemyKingdom == null || enemyKingdom.isRekt() || enemyKingdom.isNeutral())
            {
                continue;
            }

            if (!actorKingdom.isInWarWith(enemyKingdom))
            {
                continue;
            }

            List<TileZone> directZones = GetDirectEnemyFrontZones(actorKingdom, enemyKingdom);
            List<TileZone> finalZones = GetEnemyFrontZones(actorKingdom, enemyKingdom);

            LogService.LogInfo(
                "FrontLine Debug | " +
                actorKingdom.data.name +
                " vs " +
                enemyKingdom.data.name +
                " | direct front zones: " +
                directZones.Count +
                " | final front zones: " +
                finalZones.Count
            );

            foreach (TileZone zone in finalZones)
            {
                if (zone == null)
                {
                    continue;
                }

                MarkDebugFrontZone(zone);

                LogService.LogInfo(
                    "FrontZone: enemy=" +
                    enemyKingdom.data.name +
                    " zone=(" +
                    zone.x +
                    "," +
                    zone.y +
                    ") city=" +
                    (zone.city == null ? "null" : zone.city.data.name)
                );
            }
        }
    }

    public static void MarkDebugFrontZone(TileZone zone)
    {
        if (zone == null)
        {
            return;
        }

        WorldTile tile = zone.centerTile;

        if (tile == null)
        {
            return;
        }

        if (tile.zone == null)
        {
            return;
        }

        if (tile.zone.world_edge)
        {
            return;
        }

        HighlightZones(zone, Color.red);
    }

    public static void HighlightZones(TileZone zone, Color pColor, float pAlpha = 0.8f)
    {
        if (zone == null || zone.centerTile == null)
        {
            return;
        }

        ((ZoneFlash)EffectsLibrary.spawn("fx_zone_highlight", zone.centerTile, null, null, pAlpha)).start(pColor, pAlpha);
    }
}
