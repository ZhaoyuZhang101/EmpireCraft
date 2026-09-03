using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckWarriorMoveAdvanced : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    private const bool AggressiveMode = false;

    private const int MinWarriorsPerFrontZone = 1;
    private const int MaxWarriorsPerFrontZone = 3;

    // 保守模式：让占领区尽量连成一片
    private const int ConservativeConnectedBonus = 300;
    private const int ConservativeFriendlyControlledBonus = 120;
    private const int ConservativeIsolatedPenalty = 500;
    private const float RetreatHealthThreshold = 10f;
    
    private static int _lastGlobalMobilizeFrame = -1;
    private const int GlobalMobilizeIntervalFrames = 15;
    // 已经在前线的士兵尽量不要乱跑
    private const int HoldFrontMinEnemyNeighbour = 1;
    private static int _zoneWarriorCacheFrame = -1;
    private static readonly Dictionary<TileZone, List<Actor>> _zoneWarriors = new();
    private static readonly Dictionary<TileZone, Dictionary<Kingdom, int>> _zoneWarriorCounts = new();
    private static int _frontZoneCacheFrame = -1;
    private static readonly Dictionary<Kingdom, List<TileZone>> _frontZoneCache = new();
    private static int _occupiedOwnZoneCacheFrame = -1;
    private static readonly Dictionary<Kingdom, List<TileZone>> _occupiedOwnZoneCache = new();
    private static int _armyMemberCountCacheFrame = -1;
    private static readonly Dictionary<Kingdom, int> _armyMemberCountCache = new();
    private static readonly List<TileZone> _nearbyRetakeCandidates = new();
    private static readonly List<TileZone> _availableFrontZoneCandidates = new();
    private static int _targetZoneCacheFrame = -1;
    private static readonly Dictionary<(Kingdom kingdom, TileZone currentZone), TileZone> _targetZoneCache = new();

    public override BehResult execute(Actor pActor)
    {
        return BehResult.Continue;
        if (!EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled())
        {
            pActor?.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }
        if (pActor == null)
        {
            return BehResult.Continue;
        }

        if (pActor.kingdom == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (pActor.current_tile == null || pActor.current_tile.zone == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        Kingdom actorKingdom = pActor.kingdom;

        if (actorKingdom.isRekt() || actorKingdom.isNeutral())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (!actorKingdom.hasEnemies())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (!pActor.hasArmy())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (TryEngageNearbyEnemy(pActor))
        {
            return BehResult.Continue;
        }

        if (pActor.HasFrontLineMoveTarget())
        {
            pActor.goTo(pActor.GetFrontLineMoveTarget());
            return BehResult.Continue;
        }
        // 2. 如果附近有己方城市被敌军占领的区块，优先清除占领状态
        // 3. 保守模式下，如果已经站在有效前线，就不要乱跑
        if (!AggressiveMode && ShouldHoldCurrentFrontZone(pActor))
        {
            pActor.SetFrontLineMoveTarget(pActor.current_tile);
            return BehResult.Continue;
        }

        // 4. 重新计算目标：
        // 先处理本国被占领区；
        // 没有被占领区时，才推进敌方前线。
        TileZone targetZone = FindTargetZoneForActor(pActor);

        if (targetZone == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        WorldTile targetTile = GetZoneTile(targetZone);

        if (targetTile == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        pActor.SetFrontLineMoveTarget(targetTile);
        if (pActor.is_army_captain)
        {
            pActor.army._prev_captain_position = targetTile;
        }
        pActor.goTo(targetTile);
        ((ZoneFlash)EffectsLibrary.spawn("fx_zone_highlight", targetTile, null, null, 0.8f)).start(Color.blue, 0.8f);
        return BehResult.Continue;
    }
    private static void EnsureZoneWarriorCache()
    {
        int frame = HighPopulationPerformance.GetFrontLineCacheKey();
        if (_zoneWarriorCacheFrame == frame)
        {
            return;
        }

        _zoneWarriorCacheFrame = frame;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();
        _armyMemberCountCacheFrame = frame;
        _armyMemberCountCache.Clear();

        foreach (Actor actor in World.world.units)
        {
            if (!IsOccupyArmyGroupMember(actor))
            {
                continue;
            }

            TileZone zone = actor.current_tile?.zone;
            if (zone == null)
            {
                continue;
            }

            if (!_zoneWarriors.TryGetValue(zone, out var actorsInZone))
            {
                actorsInZone = new List<Actor>();
                _zoneWarriors[zone] = actorsInZone;
            }
            actorsInZone.Add(actor);

            if (!_zoneWarriorCounts.TryGetValue(zone, out var kingdomCounts))
            {
                kingdomCounts = new Dictionary<Kingdom, int>();
                _zoneWarriorCounts[zone] = kingdomCounts;
            }

            kingdomCounts.TryGetValue(actor.kingdom, out int count);
            kingdomCounts[actor.kingdom] = count + 1;

            _armyMemberCountCache.TryGetValue(actor.kingdom, out int armyCount);
            _armyMemberCountCache[actor.kingdom] = armyCount + 1;
        }
    }

    private static TileZone FindTargetZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        // 1. 如果本国有土地被敌军占领，优先夺回
        int frame = Time.frameCount;
        if (_targetZoneCacheFrame != frame)
        {
            _targetZoneCacheFrame = frame;
            _targetZoneCache.Clear();
        }

        var cacheKey = (actor.kingdom, actor.current_tile.zone);
        if (_targetZoneCache.TryGetValue(cacheKey, out TileZone cachedTarget))
        {
            return cachedTarget;
        }

        // 2. 没有被占领土地时，才推进敌国前线
        TileZone frontZone = FindTargetFrontZoneForActor(actor);
        _targetZoneCache[cacheKey] = frontZone;
        return frontZone;
    }

    private static List<TileZone> GetCachedEnemyFrontZones(Kingdom kingdom)
    {
        int frame = Time.frameCount;
        if (_frontZoneCacheFrame != frame)
        {
            _frontZoneCacheFrame = frame;
            _frontZoneCache.Clear();
        }

        if (kingdom == null)
        {
            return null;
        }

        if (!_frontZoneCache.TryGetValue(kingdom, out var zones))
        {
            zones = KingdomFrontLineHelper.GetAllEnemyFrontZones(kingdom);
            _frontZoneCache[kingdom] = zones;
        }

        return zones;
    }

    private static List<TileZone> GetCachedOccupiedOwnZones(Kingdom kingdom)
    {
        int frame = Time.frameCount;
        if (_occupiedOwnZoneCacheFrame != frame)
        {
            _occupiedOwnZoneCacheFrame = frame;
            _occupiedOwnZoneCache.Clear();
        }

        if (kingdom == null)
        {
            return null;
        }

        if (!_occupiedOwnZoneCache.TryGetValue(kingdom, out var zones))
        {
            zones = GetOccupiedOwnZones(kingdom);
            _occupiedOwnZoneCache[kingdom] = zones;
        }

        return zones;
    }

    private static TileZone FindNearbyRetakeZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        Kingdom actorKingdom = actor.kingdom;
        TileZone currentZone = actor.current_tile.zone;

        _nearbyRetakeCandidates.Clear();
        List<TileZone> candidates = _nearbyRetakeCandidates;
        candidates.Add(currentZone);

        if (currentZone.neighbours_all != null)
        {
            foreach (TileZone neighbour in currentZone.neighbours_all)
            {
                if (neighbour == null)
                {
                    continue;
                }

                candidates.Add(neighbour);
            }
        }

        TileZone bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;
        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actorKingdom, Math.Max(candidates.Count, 1));

        foreach (TileZone zone in candidates)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            City city = zone.city;

            if (city == null || city.kingdom == null)
            {
                continue;
            }

            // 只清理自己的城市
            if (city.kingdom != actorKingdom)
            {
                continue;
            }

            Kingdom occupier = city.GetTileZoneOccupier(zone);

            if (occupier == null)
            {
                continue;
            }

            if (occupier == actorKingdom)
            {
                continue;
            }

            if (actorKingdom.isInWarOnSameSide(occupier))
            {
                continue;
            }

            if (!actorKingdom.isInWarWith(occupier))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actorKingdom);

            if (friendlyCount >= desiredWarriorsPerFrontZone)
            {
                continue;
            }

            int enemyCount = CountEnemyWarriorsInZone(zone, actorKingdom);
            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(currentZone, zone);

            int score = 0;

            // 当前脚下被占，最高优先级
            if (zone == currentZone)
            {
                score += 10000;
            }

            // 有敌军时，优先过去处理
            score += enemyCount * 300;

            // 没有友军正在处理的被占区优先
            if (friendlyCount == 0)
            {
                score += 200;
            }
            else
            {
                score += 50;
            }

            score -= (int)(distance * 0.1f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (score == bestScore && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        _nearbyRetakeCandidates.Clear();
        return bestZone;
    }

    private static TileZone FindBestRetakeZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }
        Kingdom actorKingdom = actor.kingdom;

        List<TileZone> occupiedOwnZones = GetCachedOccupiedOwnZones(actorKingdom);

        if (occupiedOwnZones == null || occupiedOwnZones.Count == 0)
        {
            return null;
        }

        TileZone bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;
        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actorKingdom, Math.Max(occupiedOwnZones.Count, 1));

        foreach (TileZone zone in occupiedOwnZones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actorKingdom);

            // 已经有足够友军在清理，就不再增派
            if (friendlyCount >= desiredWarriorsPerFrontZone)
            {
                continue;
            }

            int enemyCount = CountEnemyWarriorsInZone(zone, actorKingdom);
            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(actor.current_tile.zone, zone);

            int score = 0;

            // 有敌军的被占领区优先处理
            score += enemyCount * 200;

            // 还没人去夺回的 zone 优先
            if (friendlyCount == 0)
            {
                score += 100;
            }
            else
            {
                score += 40;
            }

            // 距离越近越好
            score -= (int)(distance * 0.1f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (score == bestScore && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        return bestZone;
    }

    private static List<TileZone> GetOccupiedOwnZones(Kingdom kingdom)
    {
        List<TileZone> result = new List<TileZone>();

        if (kingdom == null || kingdom.cities == null)
        {
            return result;
        }

        foreach (City city in kingdom.cities)
        {
            if (city == null || city.zones == null)
            {
                continue;
            }

            foreach (TileZone zone in city.zones)
            {
                if (!KingdomFrontLineHelper.IsValidZone(zone))
                {
                    continue;
                }

                Kingdom occupier = city.GetTileZoneOccupier(zone);

                if (occupier == null)
                {
                    continue;
                }

                // 自己占自己的地，不算
                if (occupier == kingdom)
                {
                    continue;
                }

                // 友军占领，不需要夺回
                if (kingdom.isInWarOnSameSide(occupier))
                {
                    continue;
                }

                // 敌对国家占领，才需要夺回
                if (kingdom.isInWarWith(occupier))
                {
                    if (CanRetakeZoneFromFriendlyEdge(kingdom, zone))
                    {
                        result.Add(zone);
                    }
                }
            }
        }

        return result;
    }

    private static bool CanRetakeZoneFromFriendlyEdge(Kingdom kingdom, TileZone zone)
    {
        if (kingdom == null || zone == null || zone.city == null)
        {
            return false;
        }

        if (zone.neighbours_all == null || zone.neighbours_all.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < zone.neighbours_all.Length; i++)
        {
            TileZone neighbour = zone.neighbours_all[i];
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            City neighbourCity = neighbour.city;
            if (neighbourCity == null || neighbourCity.isRekt())
            {
                continue;
            }

            Kingdom neighbourOwner = neighbourCity.kingdom;
            Kingdom neighbourOccupier = neighbourCity.GetTileZoneOccupier(neighbour);

            bool isFriendlyNativeEdge = neighbourOwner == kingdom && neighbourOccupier == null;
            bool isFriendlyRecoveredEdge = neighbourOccupier == kingdom;

            if (isFriendlyNativeEdge || isFriendlyRecoveredEdge)
            {
                return true;
            }
        }

        return false;
    }

    private static TileZone FindTargetFrontZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        List<TileZone> allFrontZones = GetCachedEnemyFrontZones(actor.kingdom);

        if (allFrontZones == null || allFrontZones.Count == 0)
        {
            return null;
        }

        _availableFrontZoneCandidates.Clear();
        List<TileZone> availableZones = _availableFrontZoneCandidates;

        foreach (TileZone zone in allFrontZones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            if (!TouchesOwnAdvanceEdge(actor.kingdom, zone))
            {
                continue;
            }
            availableZones.Add(zone);
        }

        if (availableZones.Count == 0)
        {
            foreach (TileZone zone in allFrontZones)
            {
                if (!KingdomFrontLineHelper.IsValidZone(zone))
                {
                    continue;
                }

                availableZones.Add(zone);
            }
        }

        if (availableZones.Count == 0)
        {
            _availableFrontZoneCandidates.Clear();
            return null;
        }

        TileZone targetZone = AggressiveMode
            ? FindAggressiveTargetZone(actor, availableZones)
            : FindConservativeTargetZone(actor, availableZones);
        _availableFrontZoneCandidates.Clear();
        return targetZone;
    }

    private static TileZone FindAggressiveTargetZone(Actor actor, List<TileZone> zones)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        TileZone bestZone = null;
        int bestFriendlyCount = int.MaxValue;
        float bestDistance = float.MaxValue;

        foreach (TileZone zone in zones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actor.kingdom);
            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(actor.current_tile.zone, zone);

            // 激进模式：
            // 先填空 zone，避免所有士兵挤一个点；
            // 不强调连续战线，只看人数和距离。
            if (friendlyCount < bestFriendlyCount)
            {
                bestFriendlyCount = friendlyCount;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (friendlyCount == bestFriendlyCount && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        return bestZone;
    }

    private static TileZone FindConservativeTargetZone(Actor actor, List<TileZone> zones)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actor.kingdom, zones.Count);

        TileZone bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;

        foreach (TileZone zone in zones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actor.kingdom);

            int friendlyControlledNeighbours = CountFriendlyControlledNeighbours(actor.kingdom, zone);
            int friendlyOccupiedNeighbours = CountFriendlyOccupiedNeighbours(actor.kingdom, zone);
            int enemyNeighbours = CountEnemyNeighbours(actor.kingdom, zone);

            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(actor.current_tile.zone, zone);

            int score = 0;

            // 核心：保守模式下，优先选择贴着友军控制区/友军占领区的 zone
            score += friendlyControlledNeighbours * ConservativeFriendlyControlledBonus;
            score += friendlyOccupiedNeighbours * ConservativeConnectedBonus;

            // 如果这个 zone 周围没有任何友军控制/占领区，说明是孤立推进，强烈惩罚
            if (friendlyControlledNeighbours <= 0 && friendlyOccupiedNeighbours <= 0)
            {
                score -= ConservativeIsolatedPenalty;
            }

            // 无人前线可以补，但不能压过“连成一片”的权重
            if (friendlyCount == 0)
            {
                score += 80;
            }
            else
            {
                score += 30;
            }

            score -= Math.Max(0, friendlyCount - desiredWarriorsPerFrontZone) * 40;

            // 敌方邻居太多，说明太深入敌区，保守模式降低优先级
            score -= enemyNeighbours * 25;
            
            score -= (int)(distance * 0.01f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (score == bestScore && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        return bestZone;
    }
    public static WorldTile TryGetFrontLineMoveTileForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        if (!actor.hasArmy())
        {
            return null;
        }

        if (!actor.kingdom.hasEnemies())
        {
            return null;
        }

        TileZone targetZone = FindTargetZoneForActor(actor);

        if (targetZone == null)
        {
            return null;
        }

        return GetZoneTile(targetZone);
    }
    private static bool ShouldHoldCurrentFrontZone(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return false;
        }

        TileZone currentZone = actor.current_tile.zone;

        if (!KingdomFrontLineHelper.IsValidZone(currentZone))
        {
            return false;
        }

        Kingdom actorKingdom = actor.kingdom;
        Kingdom zoneKingdom = KingdomFrontLineHelper.GetZoneKingdom(currentZone);
        Kingdom occupier = KingdomFrontLineHelper.GetZoneOccupier(currentZone);

        bool isFriendlyControlled = zoneKingdom != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, zoneKingdom);

        if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
        {
            isFriendlyControlled = true;
        }

        if (!isFriendlyControlled)
        {
            return false;
        }

        List<TileZone> actualFrontZones = GetCachedEnemyFrontZones(actorKingdom);
        if (actualFrontZones == null || !actualFrontZones.Contains(currentZone))
        {
            return false;
        }

        int enemyNeighbours = CountEnemyNeighbours(actorKingdom, currentZone);

        if (enemyNeighbours < HoldFrontMinEnemyNeighbour)
        {
            return false;
        }

        int friendlyCount = CountFriendlyWarriorsInZone(currentZone, actorKingdom);
        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actorKingdom, actualFrontZones.Count);

        // 当前 zone 是前线，而且人数没有明显超上限，就留在这里
        if (friendlyCount <= desiredWarriorsPerFrontZone)
        {
            return true;
        }

        return false;
    }

    private static bool TryEngageNearbyEnemy(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile?.zone == null)
        {
            return false;
        }

        Actor enemy = FindNearbyEnemyWarrior(actor);
        if (enemy == null)
        {
            return false;
        }

        if (enemy.current_tile != null)
        {
            actor.SetFrontLineMoveTarget(enemy.current_tile);
            actor.goTo(enemy.current_tile);
        }
        actor.setAttackTarget(enemy);
        return true;
    }

    private static Actor FindNearbyEnemyWarrior(Actor actor)
    {
        TileZone currentZone = actor?.current_tile?.zone;
        if (currentZone == null)
        {
            return null;
        }

        Actor enemy = FindEnemyWarriorInZone(actor, currentZone);
        if (enemy != null)
        {
            return enemy;
        }

        if (currentZone.neighbours_all == null)
        {
            return null;
        }

        foreach (TileZone neighbour in currentZone.neighbours_all)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            enemy = FindEnemyWarriorInZone(actor, neighbour);
            if (enemy != null)
            {
                return enemy;
            }
        }

        return null;
    }

    private static bool ShouldRetreat(Actor actor)
    {
        return actor?.data != null && actor.data.health <= RetreatHealthThreshold;
    }

    private static WorldTile FindRetreatTileForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null)
        {
            return null;
        }

        TileZone bestZone = FindBestFriendlyRetreatZone(actor);
        if (bestZone != null)
        {
            return GetZoneTile(bestZone);
        }

        return actor.kingdom.capital._city_tile;
    }

    private static TileZone FindBestFriendlyRetreatZone(Actor actor)
    {
        TileZone currentZone = actor?.current_tile?.zone;
        Kingdom actorKingdom = actor?.kingdom;
        if (currentZone == null || actorKingdom == null || currentZone.neighbours_all == null)
        {
            return null;
        }

        TileZone bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;
        Vector3 capitalCenter = actorKingdom.capital?.city_center ?? actor.current_tile.posV3;

        foreach (TileZone neighbour in currentZone.neighbours_all)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Kingdom occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);
            Kingdom owner = KingdomFrontLineHelper.GetZoneKingdom(neighbour);
            bool isFriendly = (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier)) ||
                              (occupier == null && owner != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, owner));
            if (!isFriendly)
            {
                continue;
            }

            int enemyNeighbours = CountEnemyNeighbours(actorKingdom, neighbour);
            int friendlyNeighbours = CountFriendlyControlledNeighbours(actorKingdom, neighbour) + CountFriendlyOccupiedNeighbours(actorKingdom, neighbour);
            float distanceToCapital = Toolbox.SquaredDist(neighbour.centerTile.posV3.x, neighbour.centerTile.posV3.y, capitalCenter.x, capitalCenter.y);
            int score = friendlyNeighbours * 100 - enemyNeighbours * 250;

            if (score > bestScore || (score == bestScore && distanceToCapital < bestDistance))
            {
                bestScore = score;
                bestDistance = distanceToCapital;
                bestZone = neighbour;
            }
        }

        return bestZone;
    }

    private static int GetDesiredWarriorsPerFrontZone(Kingdom actorKingdom, int frontZoneCount)
    {
        if (actorKingdom == null || frontZoneCount <= 0)
        {
            return MaxWarriorsPerFrontZone;
        }

        int totalWarriors = CountOccupyArmyGroupMembers(actorKingdom);
        if (totalWarriors <= 0)
        {
            return MaxWarriorsPerFrontZone;
        }

        int desired = (int)Math.Ceiling(totalWarriors / (double)frontZoneCount);
        return Math.Max(MaxWarriorsPerFrontZone, desired);
    }

    private static bool IsOccupyArmyGroupMember(Actor actor)
    {
        return actor.isArmyGroupLeader()||actor.isArmyGroupWarrior();
    }

    private static int CountOccupyArmyGroupMembers(Kingdom kingdom)
    {
        if (kingdom == null)
        {
            return 0;
        }

        EnsureZoneWarriorCache();
        return _armyMemberCountCache.TryGetValue(kingdom, out int cachedCount) ? cachedCount : 0;
    }

    public static void HandleCurrentZoneWarState(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return;
        }

        TileZone zone = actor.current_tile.zone;
        City city = zone.city;

        if (city == null || city.kingdom == null)
        {
            return;
        }

        Kingdom actorKingdom = actor.kingdom;
        Kingdom cityKingdom = city.kingdom;

        // 1. 如果当前 zone 有敌军，先攻击
        Actor enemy = FindEnemyWarriorInZone(actor, zone);

        if (enemy != null)
        {
            AttackEnemyInZone(actor, enemy);
            return;
        }

        Kingdom occupier = city.GetTileZoneOccupier(zone);

        // 2. 如果这是自己的城市，并且 zone 被敌军占领，但现在没有敌军，则夺回
        if (cityKingdom == actorKingdom)
        {
            if (occupier != null && occupier != actorKingdom)
            {
                if (!actorKingdom.isInWarOnSameSide(occupier) && actorKingdom.isInWarWith(occupier))
                {
                    city.RemoveOccupiedTileZone(occupier, zone);
                }
            }

            return;
        }

        // 3. 如果这是敌方城市，并且没有敌军，则尝试占领
        if (actorKingdom.isInWarWith(cityKingdom))
        {
            // 已经是友军占领，不重复覆盖
            if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
            {
                return;
            }

            city.AddOccupiedTileZone(actorKingdom, zone, actor);
        }
    }

    public static Actor FindEnemyWarriorInZone(Actor actor, TileZone zone)
    {
        if (actor == null || actor.kingdom == null || zone == null)
        {
            return null;
        }

        EnsureZoneWarriorCache();
        if (!_zoneWarriors.TryGetValue(zone, out var actorsInZone))
        {
            return null;
        }

        foreach (Actor other in actorsInZone)
        {
            if (other == null)
            {
                continue;
            }

            if (other == actor)
            {
                continue;
            }

            if (other.kingdom == null)
            {
                continue;
            }

            if (!IsOccupyArmyGroupMember(other))
            {
                continue;
            }
            if (actor.kingdom.isInWarWith(other.kingdom))
            {
                return other;
            }
        }

        return null;
    }

    private static void AttackEnemyInZone(Actor actor, Actor enemy)
    {
        if (actor == null || enemy == null)
        {
            return;
        }

        // 如果你的项目里攻击方法名不同，只需要改这里。
        actor.setAttackTarget(enemy);
    }

    private static int CountFriendlyWarriorsInZone(TileZone zone, Kingdom actorKingdom)
    {
        if (zone == null || actorKingdom == null)
        {
            return 0;
        }

        EnsureZoneWarriorCache();
        if (!_zoneWarriorCounts.TryGetValue(zone, out var kingdomCounts))
        {
            return 0;
        }

        int count = 0;
        foreach (var pair in kingdomCounts)
        {
            if (pair.Key != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, pair.Key))
            {
                count += pair.Value;
            }
        }

        return count;
    }

    private static int CountEnemyWarriorsInZone(TileZone zone, Kingdom actorKingdom)
    {
        if (zone == null || actorKingdom == null)
        {
            return 0;
        }

        EnsureZoneWarriorCache();
        if (!_zoneWarriorCounts.TryGetValue(zone, out var kingdomCounts))
        {
            return 0;
        }

        int count = 0;
        foreach (var pair in kingdomCounts)
        {
            if (pair.Key != null && actorKingdom.isInWarWith(pair.Key))
            {
                count += pair.Value;
            }
        }

        return count;
    }

    private static int CountFriendlyControlledNeighbours(Kingdom actorKingdom, TileZone zone)
    {
        if (actorKingdom == null || zone == null || zone.neighbours_all == null)
        {
            return 0;
        }

        int count = 0;

        foreach (TileZone neighbour in zone.neighbours_all)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Kingdom zoneKingdom = KingdomFrontLineHelper.GetZoneKingdom(neighbour);
            Kingdom occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);

            if (zoneKingdom != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, zoneKingdom))
            {
                count++;
                continue;
            }

            if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFriendlyOccupiedNeighbours(Kingdom actorKingdom, TileZone zone)
    {
        if (actorKingdom == null || zone == null || zone.neighbours_all == null)
        {
            return 0;
        }

        int count = 0;

        foreach (TileZone neighbour in zone.neighbours_all)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Kingdom occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);

            if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountEnemyNeighbours(Kingdom actorKingdom, TileZone zone)
    {
        if (actorKingdom == null || zone == null || zone.neighbours_all == null)
        {
            return 0;
        }

        int count = 0;

        foreach (TileZone neighbour in zone.neighbours_all)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Kingdom zoneKingdom = KingdomFrontLineHelper.GetZoneKingdom(neighbour);

            if (zoneKingdom != null && actorKingdom.isInWarWith(zoneKingdom))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TouchesOwnAdvanceEdge(Kingdom actorKingdom, TileZone zone)
    {
        if (actorKingdom == null || zone == null || zone.neighbours_all == null)
        {
            return false;
        }

        foreach (TileZone neighbour in zone.neighbours_all)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Kingdom occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);
            Kingdom owner = KingdomFrontLineHelper.GetZoneKingdom(neighbour);

            if (occupier == actorKingdom)
            {
                return true;
            }

            if (occupier == null && owner == actorKingdom)
            {
                return true;
            }
        }

        return false;
    }

    private static WorldTile GetZoneTile(TileZone zone)
    {
        if (!KingdomFrontLineHelper.IsValidZone(zone))
        {
            return null;
        }

        WorldTile tile = zone.centerTile;

        if (tile == null)
        {
            return null;
        }

        if (tile.zone == null)
        {
            return null;
        }

        if (tile.zone.world_edge)
        {
            return null;
        }

        return tile;
    }
}
