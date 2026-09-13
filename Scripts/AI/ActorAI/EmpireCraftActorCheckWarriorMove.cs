using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using life.taxi;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckWarriorMove : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    private const int MinWarriorsPerFrontZone = 1;
    private const int MaxWarriorsPerFrontZone = 3;

    private const float WarriorCrowdingPenalty = 240f;
    private const float UnderDefendedZoneBonus = 360f;

    // ------------------------------------------------------------
    // 分帧性能参数
    // ------------------------------------------------------------

    // 每帧最多处理多少名“开战立即出征”的 Warrior。
    // 48 名在视觉上仍然近似同时动员，但不会把几百/几千人塞进同一帧。
    private const int MobilizeActorsPerFrame = 48;

    // 每帧最多实际向 TaxiManager 提交多少个跨岛请求。
    private const int TaxiRequestsPerFrame = 12;

    // 前线 Zone 缓存 120 帧，避免每个 Actor 都重新调用 GetAllEnemyFrontZones。
    private const int FrontZoneCacheLifetimeFrames = 120;

    private const int ProgressCheckIntervalFrames = 45;
    private const int SoftStuckFrames = 180;
    private const int HardRecoveryAfter = 2;
    private const int TaxiRetryIntervalFrames = 180;
    private const int MovementWatchdogActorsPerFrame = 24;
    private const int MovementWatchdogSnapshotFrames = 60;

    // ------------------------------------------------------------
    // 开战动员队列
    // ------------------------------------------------------------

    private sealed class MobilizeEntry
    {
        public Actor actor;
        public Kingdom kingdom;
    }

    private static readonly Queue<MobilizeEntry> _mobilizeQueue = new();
    private static readonly HashSet<long> _queuedMobilizeActorIds = new();

    // ------------------------------------------------------------
    // Taxi 队列
    // ------------------------------------------------------------

    private sealed class TaxiEntry
    {
        public Actor actor;
        public WorldTile target;
    }

    private static readonly Queue<TaxiEntry> _taxiQueue = new();
    private static readonly HashSet<long> _queuedTaxiActorIds = new();

    // 确保即使 War.update 对多场战争各调用一次，
    // 本帧也只真正处理一次队列。
    private static int _lastQueueProcessFrame = -1;

    // ------------------------------------------------------------
    // Front Zone 缓存
    // ------------------------------------------------------------

    private sealed class FrontZoneCacheEntry
    {
        public int frame;
        public List<TileZone> zones;
    }

    private static readonly Dictionary<Kingdom, FrontZoneCacheEntry> _frontZoneCache = new();

    // 同一次开战分配中记录已计划去各 Zone 的人数，
    // 防止同一批士兵全部扎到第一个目标。
    private static readonly Dictionary<Kingdom, Dictionary<TileZone, int>> _plannedFrontCounts = new();

    // ------------------------------------------------------------
    // Zone Warrior 缓存
    // ------------------------------------------------------------

    private static int _zoneWarriorCacheFrame = -1;
    private static readonly Dictionary<TileZone, List<Actor>> _zoneWarriors = new();
    private static readonly Dictionary<TileZone, Dictionary<Kingdom, int>> _zoneWarriorCounts = new();

    // ------------------------------------------------------------
    // Actor 移动状态
    // ------------------------------------------------------------

    private static readonly Dictionary<long, ActorMoveState> _moveStates = new();
    private static List<Actor> _movementWatchdogSnapshot = new();
    private static int _movementWatchdogIndex;
    private static int _movementWatchdogSnapshotFrame = -1;

    private sealed class ActorMoveState
    {
        public Actor actor;
        public WorldTile targetTile;
        public WorldTile lastObservedTile;
        public int lastProgressFrame;
        public int lastCheckFrame;
        public int lastTaxiFrame = -TaxiRetryIntervalFrames;
        public int recoveryCount;
    }

    public override BehResult execute(Actor pActor)
    {
        if (pActor == null)
        {
            return BehResult.Continue;
        }

        // 本类 V3 只负责战争移动。
        // Warrior 生育已经彻底移到 EmpireCraftActorCheckWarrior
        // 并由 ArmyManager.update 独立驱动，不再依赖这个军事 Behaviour
        // 在和平时期是否会被 AI 调度。
        if (!EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled())
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        if (pActor.kingdom == null ||
            pActor.current_tile == null ||
            pActor.current_tile.zone == null)
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        Kingdom kingdom = pActor.kingdom;

        if (kingdom.isRekt() ||
            kingdom.isNeutral() ||
            !pActor.isWarrior())
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        // 和平时期只停止战争移动，不停止 Warrior 生育。
        if (!kingdom.hasEnemies())
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        ActorMoveState state = GetState(pActor);

        // 已经在真实战斗中，绝不抢任务。
        if (pActor.has_attack_target || pActor.isFighting())
        {
            MarkProgress(pActor, state);
            return BehResult.Continue;
        }

        Actor localEnemy = FindEnemyWarriorInZone(
            pActor,
            pActor.current_tile.zone);

        if (localEnemy != null)
        {
            pActor.ClearFrontLineMoveTarget();
            state.targetTile = null;
            pActor.setAttackTarget(localEnemy);
            return BehResult.Continue;
        }

        if (!IsValidFrontTarget(pActor, state.targetTile))
        {
            state.targetTile = null;
            pActor.ClearFrontLineMoveTarget();
        }

        if (state.targetTile == null)
        {
            WorldTile target = FindTargetZone(pActor);

            if (target == null)
            {
                pActor.ClearFrontLineMoveTarget();
                return BehResult.Continue;
            }

            SetStateTarget(pActor, state, target);

            // 平时只补目标，不无条件 cancelAllBeh。
            IssueFrontLineMove(pActor, target, false);
        }

        WorldTile targetTile = state.targetTile;

        if (targetTile == null)
            return BehResult.Continue;

        if (pActor.current_tile.zone == targetTile.zone)
        {
            pActor.ClearFrontLineMoveTarget();
            state.targetTile = null;
            state.recoveryCount = 0;
            return BehResult.Continue;
        }

        if (!pActor.current_tile.isSameIsland(targetTile))
        {
            HandleCrossIsland(pActor, targetTile, state);
            return BehResult.Continue;
        }

        MaintainMovement(pActor, targetTile, state);
        return BehResult.Continue;
    }

    // ============================================================
    // 战争开始：只排队，不同步处理全部士兵
    // ============================================================

    public static void QueueKingdomMobilizationOnWarStart(Kingdom kingdom)
    {
        if (kingdom == null ||
            kingdom.isRekt() ||
            kingdom.isNeutral())
        {
            return;
        }

        if (!EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled())
            return;

        // 开战时刷新一次该国前线缓存，之后所有士兵共享。
        InvalidateFrontZoneCache(kingdom);
        GetCachedFrontZones(kingdom);

        if (!_plannedFrontCounts.ContainsKey(kingdom))
        {
            _plannedFrontCounts[kingdom] = new Dictionary<TileZone, int>();
        }
        else
        {
            _plannedFrontCounts[kingdom].Clear();
        }

        foreach (Actor actor in kingdom.getUnits().ToList())
        {
            QueueActorForWarMobilization(actor, kingdom);
        }
    }

    // 新征召成功的 Warrior 可直接加入同一个分帧队列。
    public static void QueueActorForWarMobilization(Actor actor, Kingdom kingdom)
    {
        if (actor == null ||
            kingdom == null ||
            !actor.isAlive() ||
            !actor.isWarrior() ||
            actor.kingdom != kingdom)
        {
            return;
        }

        long id = actor.getID();

        if (!_queuedMobilizeActorIds.Add(id))
            return;

        _mobilizeQueue.Enqueue(new MobilizeEntry
        {
            actor = actor,
            kingdom = kingdom
        });
    }

    // 由 WarPatch.update 每帧调用；内部有 frame guard。
    public static void ProcessQueuedMovementJobs()
    {
        int frame = Time.frameCount;

        if (_lastQueueProcessFrame == frame)
            return;

        _lastQueueProcessFrame = frame;

        ProcessMobilizeQueue();
        ProcessActiveMovementWatchdog();
        ProcessTaxiQueue();
    }

    private static void ProcessActiveMovementWatchdog()
    {
        int frame = Time.frameCount;
        if (_movementWatchdogSnapshotFrame < 0 ||
            frame - _movementWatchdogSnapshotFrame >= MovementWatchdogSnapshotFrames)
        {
            _movementWatchdogSnapshot = _moveStates.Values
                .Select(state => state.actor)
                .Where(actor => actor != null)
                .ToList();
            _movementWatchdogIndex = 0;
            _movementWatchdogSnapshotFrame = frame;
        }

        int budget = MovementWatchdogActorsPerFrame;
        while (budget-- > 0 && _movementWatchdogIndex < _movementWatchdogSnapshot.Count)
        {
            Actor actor = _movementWatchdogSnapshot[_movementWatchdogIndex++];
            if (actor == null || !actor.isAlive() || !actor.isWarrior() ||
                actor.kingdom == null || !actor.kingdom.hasEnemies() ||
                actor.current_tile?.zone == null)
            {
                ClearActorMoveState(actor);
                continue;
            }

            ActorMoveState state = GetState(actor);
            WorldTile target = state.targetTile;
            if (!IsValidFrontTarget(actor, target))
            {
                ClearActorMoveState(actor);
                continue;
            }

            if (actor.has_attack_target || actor.isFighting())
            {
                MarkProgress(actor, state);
                continue;
            }

            if (actor.current_tile.zone == target.zone)
            {
                ClearActorMoveState(actor);
                continue;
            }

            if (!actor.current_tile.isSameIsland(target))
            {
                HandleCrossIsland(actor, target, state);
                continue;
            }

            MaintainMovement(actor, target, state);
        }
    }

    private static void ProcessMobilizeQueue()
    {
        int budget = MobilizeActorsPerFrame;

        while (budget > 0 && _mobilizeQueue.Count > 0)
        {
            MobilizeEntry entry = _mobilizeQueue.Dequeue();
            budget--;

            Actor actor = entry.actor;
            Kingdom kingdom = entry.kingdom;

            if (actor != null)
            {
                _queuedMobilizeActorIds.Remove(actor.getID());
            }

            if (actor == null ||
                kingdom == null ||
                !actor.isAlive() ||
                actor.kingdom != kingdom ||
                !actor.isWarrior() ||
                !kingdom.hasEnemies())
            {
                continue;
            }

            if (actor.has_attack_target || actor.isFighting())
                continue;

            Dictionary<TileZone, int> planned =
                GetPlannedFrontCountMap(kingdom);

            WorldTile target = FindTargetZone(actor, planned);

            if (target == null)
                continue;

            if (target.zone != null)
            {
                planned.TryGetValue(target.zone, out int plannedCount);
                planned[target.zone] = plannedCount + 1;
            }

            ActorMoveState state = GetState(actor);
            SetStateTarget(actor, state, target);

            // 只在这一名士兵真正轮到开战动员时 cancel 一次。
            IssueFrontLineMove(actor, target, true);

            if (actor.current_tile != null &&
                !actor.current_tile.isSameIsland(target))
            {
                QueueTaxi(actor, target);
            }
        }
    }

    private static Dictionary<TileZone, int> GetPlannedFrontCountMap(Kingdom kingdom)
    {
        if (!_plannedFrontCounts.TryGetValue(
                kingdom,
                out Dictionary<TileZone, int> map))
        {
            map = new Dictionary<TileZone, int>();
            _plannedFrontCounts[kingdom] = map;
        }

        return map;
    }

    // ============================================================
    // Taxi 分帧
    // ============================================================

    private static void QueueTaxi(Actor actor, WorldTile target)
    {
        if (actor == null ||
            target == null ||
            !actor.isAlive())
        {
            return;
        }

        long id = actor.getID();

        if (!_queuedTaxiActorIds.Add(id))
            return;

        _taxiQueue.Enqueue(new TaxiEntry
        {
            actor = actor,
            target = target
        });
    }

    private static void ProcessTaxiQueue()
    {
        int budget = TaxiRequestsPerFrame;

        while (budget > 0 && _taxiQueue.Count > 0)
        {
            TaxiEntry entry = _taxiQueue.Dequeue();
            budget--;

            Actor actor = entry.actor;
            WorldTile target = entry.target;

            if (actor != null)
            {
                _queuedTaxiActorIds.Remove(actor.getID());
            }

            if (actor == null ||
                target == null ||
                !actor.isAlive() ||
                actor.kingdom == null ||
                !actor.kingdom.hasEnemies() ||
                actor.current_tile == null)
            {
                continue;
            }

            if (actor.current_tile.isSameIsland(target))
                continue;

            try
            {
                TaxiManager.newRequest(actor, target);
            }
            catch
            {
                // 下一次 Actor AI 卡死恢复时仍可再次请求。
            }
        }
    }

    // ============================================================
    // 常规移动 / 卡死恢复
    // ============================================================

    private static void IssueFrontLineMove(
        Actor actor,
        WorldTile target,
        bool forceWake)
    {
        if (actor == null || target == null)
            return;

        if (forceWake)
        {
            actor.cancelAllBeh();
        }

        actor.ClearFrontLineMoveTarget();
        actor.SetFrontLineMoveTarget(target);

        // Setting the bookkeeping fields alone is not enough when another behaviour
        // currently owns movement. Explicitly wake the pathfinder for this target.
        actor.goTo(target);

        if (actor.is_army_captain && actor.army != null)
        {
            actor.army._prev_captain_position = target;
        }
    }

    private static void MaintainMovement(
        Actor actor,
        WorldTile target,
        ActorMoveState state)
    {
        int frame = Time.frameCount;

        // Other behaviours can replace the engine's live target without clearing our
        // persistent front-line target. Restore it immediately instead of waiting for
        // the stuck timer, which keeps armies advancing between AI ticks.
        if (actor.tile_target != target || actor.beh_tile_target != target)
        {
            actor.SetFrontLineMoveTarget(target);
            actor.goTo(target);
        }

        if (!actor.HasFrontLineMoveTarget())
        {
            actor.SetFrontLineMoveTarget(target);
            actor.goTo(target);
        }

        if (frame - state.lastCheckFrame < ProgressCheckIntervalFrames)
            return;

        state.lastCheckFrame = frame;

        if (actor.current_tile != state.lastObservedTile)
        {
            state.lastObservedTile = actor.current_tile;
            state.lastProgressFrame = frame;
            state.recoveryCount = 0;
            return;
        }

        if (frame - state.lastProgressFrame < SoftStuckFrames)
            return;

        state.recoveryCount++;

        // 真正卡住才 reset 一次。
        IssueFrontLineMove(actor, target, true);
        state.lastObservedTile = actor.current_tile;
        state.lastProgressFrame = frame;

        if (state.recoveryCount >= HardRecoveryAfter &&
            actor.army != null &&
            !actor.is_army_captain)
        {
            try
            {
                actor.setTask(
                    "warrior_army_follow_leader",
                    true,
                    false,
                    true);
            }
            catch
            {
            }

            state.recoveryCount = 0;
        }
    }

    private static void HandleCrossIsland(
        Actor actor,
        WorldTile target,
        ActorMoveState state)
    {
        int frame = Time.frameCount;

        if (frame - state.lastTaxiFrame < TaxiRetryIntervalFrames)
            return;

        state.lastTaxiFrame = frame;
        QueueTaxi(actor, target);
    }

    // ============================================================
    // 前线 Zone 缓存
    // ============================================================

    private static List<TileZone> GetCachedFrontZones(Kingdom kingdom)
    {
        if (kingdom == null)
            return new List<TileZone>();

        int frame = Time.frameCount;

        if (_frontZoneCache.TryGetValue(
                kingdom,
                out FrontZoneCacheEntry cached) &&
            cached != null &&
            cached.zones != null &&
            frame - cached.frame <= FrontZoneCacheLifetimeFrames)
        {
            return cached.zones;
        }

        List<TileZone> zones;

        try
        {
            zones = KingdomFrontLineHelper
                .GetAllEnemyFrontZones(kingdom) ?? new List<TileZone>();
        }
        catch
        {
            zones = new List<TileZone>();
        }

        // 在缓存写入时就先去掉空对象，减少每名士兵后续循环成本。
        zones = zones
            .Where(z => z != null && z.centerTile != null)
            .Distinct()
            .ToList();

        _frontZoneCache[kingdom] = new FrontZoneCacheEntry
        {
            frame = frame,
            zones = zones
        };

        return zones;
    }

    private static void InvalidateFrontZoneCache(Kingdom kingdom)
    {
        if (kingdom != null)
            _frontZoneCache.Remove(kingdom);
    }

    public static WorldTile FindTargetZone(Actor pActor)
    {
        return FindTargetZone(pActor, null);
    }

    private static WorldTile FindTargetZone(
        Actor pActor,
        Dictionary<TileZone, int> plannedCounts)
    {
        if (pActor == null ||
            pActor.kingdom == null ||
            pActor.current_tile == null)
        {
            return null;
        }

        Kingdom kingdom = pActor.kingdom;
        List<TileZone> zones = GetCachedFrontZones(kingdom);

        if (zones.Count == 0)
            return null;

        EnsureZoneWarriorCache();

        TileZone bestZone = null;
        float bestScore = float.MaxValue;

        // 第一轮：优先尚未达到每前线 3 人的区域。
        foreach (TileZone zone in zones)
        {
            if (!IsValidEnemyFrontZone(kingdom, zone))
                continue;

            int current = GetWarriorCount(zone, kingdom);
            int planned = 0;

            if (plannedCounts != null)
                plannedCounts.TryGetValue(zone, out planned);

            int effective = current + planned;

            if (effective >= MaxWarriorsPerFrontZone)
                continue;

            float score = CalculateZoneScore(
                pActor,
                zone,
                effective);

            if (score < bestScore)
            {
                bestScore = score;
                bestZone = zone;
            }
        }

        // 所有前线都达到 3 人时，继续推进，但仍按距离 + 拥挤度选择。
        if (bestZone == null)
        {
            foreach (TileZone zone in zones)
            {
                if (!IsValidEnemyFrontZone(kingdom, zone))
                    continue;

                int current = GetWarriorCount(zone, kingdom);
                int planned = 0;

                if (plannedCounts != null)
                    plannedCounts.TryGetValue(zone, out planned);

                int effective = current + planned;

                float score = CalculateZoneScore(
                    pActor,
                    zone,
                    effective);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestZone = zone;
                }
            }
        }

        return bestZone?.centerTile;
    }

    private static float CalculateZoneScore(
        Actor actor,
        TileZone zone,
        int friendlyCount)
    {
        float score = Toolbox.SquaredDistTile(
            actor.current_tile,
            zone.centerTile);

        score += friendlyCount * WarriorCrowdingPenalty;

        if (friendlyCount < MinWarriorsPerFrontZone)
        {
            score -= UnderDefendedZoneBonus;
        }

        return score;
    }

    private static bool IsValidEnemyFrontZone(
        Kingdom kingdom,
        TileZone zone)
    {
        if (kingdom == null ||
            zone == null ||
            zone.city == null ||
            zone.city.kingdom == null)
        {
            return false;
        }

        return AreWarSidesHostile(
            kingdom,
            zone.city.kingdom);
    }

    private static bool IsValidFrontTarget(
        Actor actor,
        WorldTile target)
    {
        return actor != null &&
               target != null &&
               target.zone != null &&
               IsValidEnemyFrontZone(
                   actor.kingdom,
                   target.zone);
    }

    // 帝国战争可能登记在 CoreKingdom 上。
    private static bool AreWarSidesHostile(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b)
            return false;

        if (AreDirectlyAtWar(a, b))
            return true;

        Empire aEmpire = a.GetEmpire();
        Empire bEmpire = b.GetEmpire();

        Kingdom aCore = aEmpire?.CoreKingdom;
        Kingdom bCore = bEmpire?.CoreKingdom;

        if (aCore == null && a.IsEmpire())
            aCore = a;

        if (bCore == null && b.IsEmpire())
            bCore = b;

        if (aCore != null && AreDirectlyAtWar(aCore, b))
            return true;

        if (bCore != null && AreDirectlyAtWar(a, bCore))
            return true;

        return aCore != null &&
               bCore != null &&
               AreDirectlyAtWar(aCore, bCore);
    }

    private static bool AreDirectlyAtWar(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b)
            return false;

        try
        {
            return a.isInWarWith(b) || b.isInWarWith(a);
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // 当前 Zone Warrior 缓存
    // ============================================================

    private static void EnsureZoneWarriorCache()
    {
        int frame = HighPopulationPerformance.GetFrontLineCacheKey();

        if (_zoneWarriorCacheFrame == frame)
            return;

        _zoneWarriorCacheFrame = frame;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();

        foreach (Actor actor in World.world.units)
        {
            if (!IsOccupyArmyGroupMember(actor))
                continue;

            TileZone zone = actor.current_tile?.zone;

            if (zone == null)
                continue;

            if (!_zoneWarriors.TryGetValue(
                    zone,
                    out List<Actor> actorsInZone))
            {
                actorsInZone = new List<Actor>();
                _zoneWarriors[zone] = actorsInZone;
            }

            actorsInZone.Add(actor);

            if (!_zoneWarriorCounts.TryGetValue(
                    zone,
                    out Dictionary<Kingdom, int> kingdomCounts))
            {
                kingdomCounts = new Dictionary<Kingdom, int>();
                _zoneWarriorCounts[zone] = kingdomCounts;
            }

            if (actor.kingdom != null)
            {
                kingdomCounts.TryGetValue(
                    actor.kingdom,
                    out int count);

                kingdomCounts[actor.kingdom] = count + 1;
            }
        }
    }

    private static int GetWarriorCount(
        TileZone zone,
        Kingdom kingdom)
    {
        if (zone == null || kingdom == null)
            return 0;

        if (!_zoneWarriorCounts.TryGetValue(
                zone,
                out Dictionary<Kingdom, int> kingdomCounts))
        {
            return 0;
        }

        return kingdomCounts.TryGetValue(
            kingdom,
            out int count)
            ? count
            : 0;
    }

    public static Actor FindEnemyWarriorInZone(
        Actor actor,
        TileZone zone)
    {
        if (actor == null ||
            actor.kingdom == null ||
            zone == null)
        {
            return null;
        }

        EnsureZoneWarriorCache();

        if (!_zoneWarriors.TryGetValue(
                zone,
                out List<Actor> actorsInZone))
        {
            return null;
        }

        Actor nearest = null;
        float bestDistance = float.MaxValue;

        foreach (Actor other in actorsInZone)
        {
            if (other == null ||
                other == actor ||
                !other.isAlive() ||
                other.kingdom == null ||
                !IsOccupyArmyGroupMember(other))
            {
                continue;
            }

            if (!AreWarSidesHostile(
                    actor.kingdom,
                    other.kingdom))
            {
                continue;
            }

            float distance = Toolbox.SquaredDistTile(
                actor.current_tile,
                other.current_tile);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = other;
            }
        }

        return nearest;
    }

    private static bool IsOccupyArmyGroupMember(Actor actor)
    {
        return actor != null &&
               actor.isAlive() &&
               actor.isWarrior();
    }

    // ============================================================
    // State
    // ============================================================

    private static ActorMoveState GetState(Actor actor)
    {
        long id = actor.getID();

        if (!_moveStates.TryGetValue(
                id,
                out ActorMoveState state))
        {
            state = new ActorMoveState
            {
                actor = actor,
                lastObservedTile = actor.current_tile,
                lastProgressFrame = Time.frameCount,
                lastCheckFrame = Time.frameCount
            };

            _moveStates[id] = state;
        }

        state.actor = actor;

        return state;
    }

    private static void SetStateTarget(
        Actor actor,
        ActorMoveState state,
        WorldTile target)
    {
        state.targetTile = target;
        state.lastObservedTile = actor.current_tile;
        state.lastProgressFrame = Time.frameCount;
        state.lastCheckFrame = Time.frameCount;
        state.recoveryCount = 0;
    }

    private static void MarkProgress(
        Actor actor,
        ActorMoveState state)
    {
        state.lastObservedTile = actor.current_tile;
        state.lastProgressFrame = Time.frameCount;
        state.lastCheckFrame = Time.frameCount;
        state.recoveryCount = 0;
    }

    private static void ClearActorMoveState(Actor actor)
    {
        if (actor == null)
            return;

        actor.ClearFrontLineMoveTarget();
        _moveStates.Remove(actor.getID());
    }
}
