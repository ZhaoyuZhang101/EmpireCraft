using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckWarriorMove : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    private const bool AggressiveMode = false;

    private const int MinWarriorsPerFrontZone = 1;
    private const int MaxWarriorsPerFrontZone = 3;

    // 保守模式：让占领区尽量连成一片
    private const int ConservativeConnectedBonus = 300;
    private const int ConservativeFriendlyControlledBonus = 120;
    private const int ConservativeIsolatedPenalty = 500;

    // 已经在前线的士兵尽量不要乱跑
    private const int HoldFrontMinEnemyNeighbour = 1;
    private static int _zoneWarriorCacheFrame = -1;
    private static readonly Dictionary<TileZone, List<Actor>> _zoneWarriors = new();
    private static readonly Dictionary<TileZone, Dictionary<Kingdom, int>> _zoneWarriorCounts = new();

    public override BehResult execute(Actor pActor)
    {
        if (!EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled())
        {
            return BehResult.Continue;
        }

        if (pActor == null)
        {
            return BehResult.Continue;
        }

        if (pActor.kingdom == null)
        {
            return BehResult.Continue;
        }

        if (pActor.current_tile == null || pActor.current_tile.zone == null)
        {
            return BehResult.Continue;
        }

        Kingdom actorKingdom = pActor.kingdom;

        if (actorKingdom.isRekt() || actorKingdom.isNeutral())
        {
            return BehResult.Continue;
        }

        if (!actorKingdom.hasEnemies())
        {
            return BehResult.Continue;
        }

        // 1. 先处理当前 zone：
        // 有敌军就打；
        // 自己土地被占且无敌军就夺回；
        // 敌方土地无敌军就占领。
        HandleCurrentZoneWarState(pActor);

        // 2. 如果附近有己方城市被敌军占领的区块，优先清除占领状态
        TileZone nearbyRetakeZone = FindNearbyRetakeZoneForActor(pActor);

        if (nearbyRetakeZone != null)
        {
            WorldTile nearbyRetakeTile = GetZoneTile(nearbyRetakeZone);

            if (nearbyRetakeTile != null)
            {
                pActor.goTo(nearbyRetakeTile);
                return BehResult.Continue;
            }
        }

        // 3. 保守模式下，如果已经站在有效前线，就不要乱跑
        if (!AggressiveMode && ShouldHoldCurrentFrontZone(pActor))
        {
            return BehResult.Continue;
        }

        // 4. 重新计算目标：
        // 先处理本国被占领区；
        // 没有被占领区时，才推进敌方前线。
        TileZone targetZone = FindTargetZoneForActor(pActor);

        if (targetZone == null)
        {
            return BehResult.Continue;
        }

        WorldTile targetTile = GetZoneTile(targetZone);

        if (targetTile == null)
        {
            return BehResult.Continue;
        }

        pActor.goTo(targetTile);
        return BehResult.Continue;
    }

    private static void EnsureZoneWarriorCache()
    {
        int frame = Time.frameCount;
        if (_zoneWarriorCacheFrame == frame)
        {
            return;
        }

        _zoneWarriorCacheFrame = frame;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();

        foreach (Actor actor in World.world.units)
        {
            if (actor == null || actor.kingdom == null || !actor.isWarrior())
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
        }
    }

    private static TileZone FindTargetZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        // 1. 如果本国有土地被敌军占领，优先夺回
        TileZone retakeZone = FindBestRetakeZoneForActor(actor);

        if (retakeZone != null)
        {
            return retakeZone;
        }

        // 2. 没有被占领土地时，才推进敌国前线
        return FindTargetFrontZoneForActor(actor);
    }

    private static TileZone FindNearbyRetakeZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        Kingdom actorKingdom = actor.kingdom;
        TileZone currentZone = actor.current_tile.zone;

        List<TileZone> candidates = new List<TileZone>();
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

            if (friendlyCount >= MaxWarriorsPerFrontZone)
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

        return bestZone;
    }

    private static TileZone FindBestRetakeZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        Kingdom actorKingdom = actor.kingdom;

        List<TileZone> occupiedOwnZones = GetOccupiedOwnZones(actorKingdom);

        if (occupiedOwnZones == null || occupiedOwnZones.Count == 0)
        {
            return null;
        }

        TileZone bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;

        foreach (TileZone zone in occupiedOwnZones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actorKingdom);

            // 已经有足够友军在清理，就不再增派
            if (friendlyCount >= MaxWarriorsPerFrontZone)
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
                    result.Add(zone);
                }
            }
        }

        return result;
    }

    private static TileZone FindTargetFrontZoneForActor(Actor actor)
    {
        if (actor == null || actor.kingdom == null || actor.current_tile == null || actor.current_tile.zone == null)
        {
            return null;
        }

        List<TileZone> allFrontZones = KingdomFrontLineHelper.GetAllEnemyFrontZones(actor.kingdom);

        if (allFrontZones == null || allFrontZones.Count == 0)
        {
            return null;
        }

        List<TileZone> availableZones = new List<TileZone>();

        foreach (TileZone zone in allFrontZones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actor.kingdom);

            // 前线 zone 人数满了就不增派
            if (friendlyCount >= MaxWarriorsPerFrontZone)
            {
                continue;
            }

            availableZones.Add(zone);
        }

        if (availableZones.Count == 0)
        {
            return null;
        }

        if (AggressiveMode)
        {
            return FindAggressiveTargetZone(actor, availableZones);
        }

        return FindConservativeTargetZone(actor, availableZones);
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

            if (friendlyCount >= MaxWarriorsPerFrontZone)
            {
                continue;
            }

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
            else if (friendlyCount < MaxWarriorsPerFrontZone)
            {
                score += 30;
            }

            // 敌方邻居太多，说明太深入敌区，保守模式降低优先级
            score -= enemyNeighbours * 25;

            // 距离越近越好，但距离权重不能压过连接性权重
            score -= (int)(distance * 0.05f);

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

        bool isFriendlyControlled = false;

        if (zoneKingdom != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, zoneKingdom))
        {
            isFriendlyControlled = true;
        }

        if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
        {
            isFriendlyControlled = true;
        }

        if (!isFriendlyControlled)
        {
            return false;
        }

        int enemyNeighbours = CountEnemyNeighbours(actorKingdom, currentZone);

        if (enemyNeighbours < HoldFrontMinEnemyNeighbour)
        {
            return false;
        }

        int friendlyCount = CountFriendlyWarriorsInZone(currentZone, actorKingdom);

        // 当前 zone 是前线，而且人数没有明显超上限，就留在这里
        if (friendlyCount <= MaxWarriorsPerFrontZone)
        {
            return true;
        }

        return false;
    }

    private static void HandleCurrentZoneWarState(Actor actor)
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

    private static Actor FindEnemyWarriorInZone(Actor actor, TileZone zone)
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

            if (!other.isWarrior())
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
