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

        if (!pActor.isWarrior())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (pActor.HasFrontLineMoveTarget()||pActor.has_attack_target)
        {
            return BehResult.Continue;
        }
        WorldTile targetTile = FindTargetZone(pActor);

        if (targetTile == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }
        ((ZoneFlash)EffectsLibrary.spawn("fx_zone_highlight", targetTile, null, null, 0.8f)).start(pActor.kingdom.getColor()._color_main, 0.8f);
        var enemy = FindEnemyWarriorInZone(pActor, targetTile.zone);
        if (enemy != null)
        {
            AttackEnemyInZone(pActor, enemy);
            return BehResult.Continue;
        }
        pActor.cancelAllBeh();
        pActor.SetFrontLineMoveTarget(targetTile);
        if (pActor.is_army_captain)
        {
            pActor.army._prev_captain_position = targetTile;
        }
        return BehResult.Continue;
    }

    public static WorldTile FindTargetZone(Actor pActor)
    {
        var kingdom = pActor.kingdom;
        var zones = KingdomFrontLineHelper.GetAllEnemyFrontZones(kingdom);
        var validZones = zones.FindAll(z =>
        {
            if (z == null) return false;
            if (!z.city?.kingdom?.isInWarWith(kingdom) ?? true)  return false;
            return true;
        });
        if (validZones.Count == 0) return null;
        return validZones.GetRandom().centerTile;
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
    private static bool IsOccupyArmyGroupMember(Actor actor)
    {
        return actor.isWarrior();
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
}
