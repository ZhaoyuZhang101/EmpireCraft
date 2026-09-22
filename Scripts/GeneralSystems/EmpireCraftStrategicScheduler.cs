using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.System;

/// <summary>
/// Keeps EmpireCraft strategy work responsive without taking over WorldBox's
/// simulation loop. Work is round-robin, time-budgeted, and never fully starved.
/// </summary>
public static class EmpireCraftStrategicScheduler
{
    private sealed class KingdomScheduleState
    {
        public double LastStrategicWorldTime = -1d;
        public float NextMembershipCheck;
    }

    private sealed class EmpireScheduleState
    {
        public double LastStrategicWorldTime = -1d;
        public long EmperorId = -1L;
    }

    private static readonly Dictionary<long, KingdomScheduleState> KingdomStates = new();
    private static readonly Dictionary<long, EmpireScheduleState> EmpireStates = new();
    private static readonly Dictionary<long, float> FaultRetryTimes = new();
    private static readonly EmpireCraftKingdomBehCheckTemporaryFaction TemporaryFactionCheck = new();
    private static readonly EmpireCraftKingdomBehCheckPlots PlotCheck = new();
    private static readonly EmpireCraftKingdomBehCheckKingdomType KingdomTypeCheck = new();
    private static readonly EmpireCraftKingdomBehCheckReligionKingdom ReligionKingdomCheck = new();
    private static MapBox _boundWorld;
    private static int _kingdomCursor;
    private static int _empireCursor;
    private static float _nextCompatibilityRefresh;
    private static float _nextStatePrune;

    public static void Tick()
    {
        MapBox currentWorld = World.world;
        if (currentWorld == null || !Config.game_loaded || Config.paused || SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_boundWorld, currentWorld)) Reset(currentWorld);

        float realtime = Time.realtimeSinceStartup;
        if (realtime >= _nextCompatibilityRefresh)
        {
            AncientWarfareCompatibility.Refresh();
            _nextCompatibilityRefresh = realtime + 0.5f;
        }

        int population = currentWorld.units?.Count ?? 0;
        double frameMilliseconds = Math.Max(0d, Time.unscaledDeltaTime * 1000d);
        double elapsedSeconds = Math.Max(0d, Time.unscaledDeltaTime);
        double budgetMilliseconds = EmpireCraftFrameSchedulingRules.ResolveBudgetMilliseconds(
            ModClass.PERFORMANCE_HIGH_POPULATION_MODE, ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE,
            population, frameMilliseconds);
        int maximumKingdoms = EmpireCraftFrameSchedulingRules.ResolveMaximumKingdoms(
            ModClass.PERFORMANCE_HIGH_POPULATION_MODE, ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE,
            population);
        int maximumEmpires = EmpireCraftFrameSchedulingRules.ResolveMaximumEmpires(
            ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE, population);

        var kingdomManager = currentWorld.kingdoms;
        kingdomManager?.checkLists();
        IList<Kingdom> kingdoms = kingdomManager?.list;
        var empireManager = ModClass.EMPIRE_MANAGER;
        empireManager?.checkLists();
        IList<Empire> empires = empireManager?.list;

        int kingdomCount = kingdoms?.Count ?? 0;
        int empireCount = empires?.Count ?? 0;
        if (realtime >= _nextStatePrune)
        {
            PruneStaleStates(kingdoms, empires);
            _nextStatePrune = realtime + 10f;
        }

        maximumKingdoms = Math.Min(kingdomCount, maximumKingdoms);
        maximumEmpires = Math.Min(empireCount, maximumEmpires);
        int minimumKingdoms = ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE
            ? EmpireCraftFrameSchedulingRules.ResolveMinimumItemsForSweep(kingdomCount, elapsedSeconds,
                EmpireCraftFrameSchedulingRules.KingdomSweepTargetSeconds, 64)
            : Math.Min(1, kingdomCount);
        int minimumEmpires = ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE
            ? EmpireCraftFrameSchedulingRules.ResolveMinimumItemsForSweep(empireCount, elapsedSeconds,
                EmpireCraftFrameSchedulingRules.EmpireSweepTargetSeconds, 32)
            : Math.Min(1, empireCount);
        maximumKingdoms = Math.Max(maximumKingdoms, minimumKingdoms);
        maximumEmpires = Math.Max(maximumEmpires, minimumEmpires);
        long started = Stopwatch.GetTimestamp();

        // Titles are internally sliced. Calling once per render frame avoids a full
        // title scan on every physics tick.
        ModClass.KINGDOM_TITLE_MANAGER?.update(0f);

        int processedEmpires = 0;
        do
        {
            if (!ProcessNextEmpire(realtime, empires)) break;
            processedEmpires++;
        }
        while (EmpireCraftFrameSchedulingRules.CanContinue(processedEmpires, minimumEmpires, maximumEmpires,
            ElapsedMilliseconds(started), Math.Max(MinimumEmpireBudgetMilliseconds,
                budgetMilliseconds * EmpireBudgetShare)));

        int processed = 0;
        do
        {
            if (!ProcessNextKingdom(realtime, kingdoms)) break;
            processed++;
        }
        while (EmpireCraftFrameSchedulingRules.CanContinue(processed, minimumKingdoms, maximumKingdoms,
            ElapsedMilliseconds(started), budgetMilliseconds));
    }

    public static void Reset()
    {
        Reset(World.world);
    }

    private static void Reset(MapBox world)
    {
        _boundWorld = world;
        _kingdomCursor = 0;
        _empireCursor = 0;
        _nextCompatibilityRefresh = 0f;
        _nextStatePrune = 0f;
        KingdomStates.Clear();
        EmpireStates.Clear();
        FaultRetryTimes.Clear();
    }

    private static void PruneStaleStates(IList<Kingdom> kingdoms, IList<Empire> empires)
    {
        HashSet<long> liveKingdomIds = new HashSet<long>();
        if (kingdoms != null)
        {
            for (int i = 0; i < kingdoms.Count; i++)
            {
                Kingdom kingdom = kingdoms[i];
                if (kingdom != null && !kingdom.isRekt())
                {
                    liveKingdomIds.Add(kingdom.id);
                }
            }
        }

        HashSet<long> liveEmpireIds = new HashSet<long>();
        if (empires != null)
        {
            for (int i = 0; i < empires.Count; i++)
            {
                Empire empire = empires[i];
                if (empire != null && !empire.IsArchived() && !empire.isRekt())
                {
                    liveEmpireIds.Add(empire.id);
                }
            }
        }

        foreach (long key in KingdomStates.Keys.ToList())
        {
            if (!liveKingdomIds.Contains(key))
            {
                KingdomStates.Remove(key);
            }
        }

        foreach (long key in EmpireStates.Keys.ToList())
        {
            if (!liveEmpireIds.Contains(key))
            {
                EmpireStates.Remove(key);
            }
        }

        foreach (long key in FaultRetryTimes.Keys.ToList())
        {
            if (liveKingdomIds.Contains(key))
            {
                continue;
            }

            long empireId = unchecked(key - long.MinValue);
            if (liveEmpireIds.Contains(empireId))
            {
                continue;
            }

            FaultRetryTimes.Remove(key);
        }
    }

    private static bool ProcessNextKingdom(float realtime, IList<Kingdom> kingdoms)
    {
        if (kingdoms == null || kingdoms.Count == 0) return false;
        if (_kingdomCursor >= kingdoms.Count) _kingdomCursor = 0;
        Kingdom kingdom = kingdoms[_kingdomCursor++];
        if (kingdom == null || kingdom.isRekt() || AncientWarfareCompatibility.Owns(kingdom)) return true;
        if (FaultRetryTimes.TryGetValue(kingdom.id, out float retryAt) && realtime < retryAt) return true;

        try
        {
            if (!KingdomStates.TryGetValue(kingdom.id, out KingdomScheduleState state))
            {
                state = new KingdomScheduleState();
                KingdomStates[kingdom.id] = state;
            }
            if (realtime >= state.NextMembershipCheck)
            {
                kingdom.CheckEmpire();
                state.NextMembershipCheck = realtime + 0.5f + (Math.Abs(kingdom.id) % 8) * 0.025f;
            }

            ProcessTemporaryFactions(kingdom);
            if (state.LastStrategicWorldTime < 0d || Date.getMonthsSince(state.LastStrategicWorldTime) >= 1)
            {
                double now = World.world.getCurWorldTime();
                state.LastStrategicWorldTime = now;
                KingdomTypeCheck.execute(kingdom);
                CultureService.UpdateCityCultureShiftCandidates(kingdom);
                CultureService.UpdateCulturalAssimilationDuty(kingdom);
                PlotCheck.execute(kingdom);
                TemporaryFactionCheck.execute(kingdom);
                ReligionKingdomCheck.execute(kingdom);
            }
            FaultRetryTimes.Remove(kingdom.id);
        }
        catch (Exception error)
        {
            FaultRetryTimes[kingdom.id] = realtime + 5f;
            LogService.LogWarning($"[EmpireCraft] Strategic kingdom slice failed for {kingdom.id}: {error}");
        }
        return true;
    }

    private static void ProcessTemporaryFactions(Kingdom kingdom)
    {
        if (!kingdom.IsEmpire()) return;
        Empire empire = kingdom.GetEmpire();
        var factions = kingdom.GetRegime()?.GetPlayerFactions();
        if (empire == null || factions == null) return;
        for (int factionIndex = 0; factionIndex < factions.Count; factionIndex++)
        {
            var temporaryFactions = factions[factionIndex]?.TemporaryFactions;
            if (temporaryFactions == null) continue;
            for (int claimIndex = 0; claimIndex < temporaryFactions.Count; claimIndex++)
            {
                var claim = temporaryFactions[claimIndex];
                if (claim == null) continue;
                claim.SetEmpire(empire);
                if (claim.IsNeedToCountDown() && claim.CountDown > 0) claim.CountDown--;
                if (claim.IsStarted() && !claim.ShowAsPlot) claim.CheckNeedToUpdate();
            }
        }
    }

    private const double EmpireBudgetShare = 0.35d;
    private const double MinimumEmpireBudgetMilliseconds = 0.25d;

    private static bool ProcessNextEmpire(float realtime, IList<Empire> empires)
    {
        if (empires == null || empires.Count == 0) return false;
        if (_empireCursor >= empires.Count) _empireCursor = 0;
        Empire empire = empires[_empireCursor++];
        if (empire == null || empire.IsArchived() || empire.isRekt() ||
            AncientWarfareCompatibility.OwnsObject(empire)) return true;
        long faultKey = unchecked(long.MinValue + empire.id);
        if (FaultRetryTimes.TryGetValue(faultKey, out float retryAt) && realtime < retryAt) return true;
        try
        {
            if (!EmpireStates.TryGetValue(empire.id, out EmpireScheduleState state))
            {
                state = new EmpireScheduleState();
                EmpireStates[empire.id] = state;
            }
            long emperorId = empire.Emperor?.id ?? -1L;
            bool successionChanged = emperorId != state.EmperorId;
            if (successionChanged || state.LastStrategicWorldTime < 0d ||
                Date.getMonthsSince(state.LastStrategicWorldTime) >= 1)
            {
                state.EmperorId = emperorId;
                state.LastStrategicWorldTime = World.world.getCurWorldTime();
                empire.update();
            }
            FaultRetryTimes.Remove(faultKey);
        }
        catch (Exception error)
        {
            FaultRetryTimes[faultKey] = realtime + 5f;
            LogService.LogWarning($"[EmpireCraft] Strategic empire slice failed for {empire.id}: {error}");
        }
        return true;
    }

    private static double ElapsedMilliseconds(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }
}
