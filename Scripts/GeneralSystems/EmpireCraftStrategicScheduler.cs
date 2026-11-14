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
        public int MonthlyPhase = -1;
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
        if (currentWorld == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_boundWorld, currentWorld)) Reset(currentWorld);
        // 世界地图剧本进世界后的安排(行政区、多京、殖民地、兵力、名人……)，等各国的城表就绪再做(见 WorldMapScenario.TickAfterLoad)；
        // 加载界面等它做完才揭开，暂停着也照做
        EmpireCraft.Scripts.GeneralSystems.WorldMap.WorldMapScenario.TickAfterLoad();
        if (Config.paused) return;
        // 自定剧本的剧情事件(每年查一次，见 ScenarioEventSystem)
        EmpireCraft.Scripts.GeneralSystems.WorldMap.ScenarioEventSystem.Tick();
        // 读档后第 1、6、12、36 个月对一次帝国的账(见 EmpireLossDiagnostics)，平时第一行就返回
        EmpireCraft.Scripts.Diagnostics.EmpireLossDiagnostics.Tick();
        // 计时标签都是字面量(不每帧拼字符串)；路网、行政控制力、文化辐射、地方稳定与财政在各自里面计时
        // 下面各子系统按档从 FrameJobs 过(月结中档，年结与杂活低档)：到期那一帧照旧整批结，几个撞在同一帧时一帧一个往后排，
        // 至多顺延半秒(中档)、三秒(低档)。计时标签照旧(字面量，不每帧拼字)；自己里面分段计时的不再套一层
        FrameJobs.Run("国家移民与空城安置", FrameJobs.Priority.Low, StateSettlementSystem.Tick);
        // 路网规划、修路养路(自己按真实时间与游戏月份限频，见 RoadNetwork、RoadBuildingSystem)
        FrameJobs.Run("路网", FrameJobs.Priority.Low, RoadNetwork.Tick, measure: false);
        FrameJobs.Run("皇帝出行", FrameJobs.Priority.Normal, EmperorTourSystem.Tick);
        FrameJobs.Run("宗室·生老病死", FrameJobs.Priority.Normal, RoyalHouseSystem.Tick);
        FrameJobs.Run("饥荒动荡", FrameJobs.Priority.Normal, FamineUnrestSystem.Tick);
        FrameJobs.Run("天下大势", FrameJobs.Priority.Normal, UnificationSystem.Tick);
        // 本土帝国抵御异族入侵：号召同族来援(每月，见 NativeDefenseSystem)
        FrameJobs.Run("御侮", FrameJobs.Priority.Normal, NativeDefenseSystem.Tick);
        // 傀儡随主国参战、流亡政府换东道国与还都(每月，见 PuppetStateSystem、ExileGovernmentSystem)；
        // 近代占领他国：军管满一年的交给这一片的傀儡、议和割给傀儡的城(见 OccupationPuppetSystem)
        FrameJobs.Run("傀儡与流亡政府", FrameJobs.Priority.Normal, () =>
        {
            PuppetStateSystem.Tick();
            ExileGovernmentSystem.Tick();
            OccupationPuppetSystem.Tick();
        });
        // 异族之地：年度扫描、正统跌破五十时镇压或自立(见 ForeignLandSystem)
        FrameJobs.Run("异族之地", FrameJobs.Priority.Low, ForeignLandSystem.Tick);
        // 独立的游牧国换君后诸子分帐(见 NomadAppanageSystem)
        FrameJobs.Run("游牧分帐", FrameJobs.Priority.Low, NomadAppanageSystem.Tick, measure: false);
        // 一两城的小国被大国包着、国力悬殊时称臣(每年，见 SmallStateSubmissionSystem)
        FrameJobs.Run("小国归附", FrameJobs.Priority.Low, SmallStateSubmissionSystem.Tick);
        FrameJobs.Run("飞地年度维护", FrameJobs.Priority.Low, ExclaveMaintenanceSystem.Tick);
        FrameJobs.Run("行政控制力", FrameJobs.Priority.Low, AdministrativeControlSystem.Tick, measure: false);
        // 水土：游牧离草、定居入草(每帧至多一个政权，一年一次，见 ShuiTuSystem)
        FrameJobs.Run("水土", FrameJobs.Priority.Low, ShuiTuSystem.Tick, measure: false);
        // 通达层：城与城之间走多远(快照分帧、计算在后台线程，见 ConnectivitySystem)；教化接着用它的城际图
        FrameJobs.Run("通达层", FrameJobs.Priority.Low, ConnectivitySystem.Tick, measure: false);
        FrameJobs.Run("文化辐射", FrameJobs.Priority.Low, CultureRadiation.Tick, measure: false);
        FrameJobs.Run("地方稳定与财政", FrameJobs.Priority.Normal, CityStabilitySystem.Tick, measure: false);
        // 府库积压：每年一次国库、城库余钱折损(见 TreasuryHoardSystem)
        FrameJobs.Run("府库积压", FrameJobs.Priority.Low, TreasuryHoardSystem.Tick);
        // 穿越政体：元老院席位继承、执委会主席改选、阶段、出版指导小组(每年一次，见 TransmigrantSystem)
        FrameJobs.Run("穿越政体", FrameJobs.Priority.Low, TransmigrantSystem.Tick);
        // 殖民：海外城编入殖民地、总督任期、解送分红、离心独立、殖民船(每年一次分帧，见 ColonySystem)
        FrameJobs.Run("殖民", FrameJobs.Priority.Low, ColonySystem.Tick);
        // 商品市场：各城记账、市场结算、市场间贸易、关税同盟(每年一次分帧，见 CommodityMarketSystem)
        FrameJobs.Run("商品市场", FrameJobs.Priority.Low, CommodityMarketSystem.Tick);
        FrameJobs.Run("帝国改划", FrameJobs.Priority.Low, AdministrationReorganizationSystem.Tick);
        // 决议：每月推进在办的，按人轮转想发起新的(见 DecisionSystem，自己里面分段计时)
        // 内帑：年例、皇庄收租(每月一回，只算有人亲自掌着的，见 PrivyPurse)
        FrameJobs.Run("内帑", FrameJobs.Priority.Low, PrivyPurse.Tick);
        FrameJobs.Run("决议", FrameJobs.Priority.Normal, EmpireCraft.Scripts.GeneralSystems.Decisions.DecisionSystem.Tick, measure: false);

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
        // 每月整理一次帝国存续：没有领土的帝国就此解散(见 EmpireManager.CheckLiveness)；放在取帝国列表之前
        using (FrameProfiler.Measure("帝国存续")) empireManager?.CheckLiveness();
        // 分裂进程每月推进一次：化解、预警期满割据、割据期满破局、争位之战满五年(自带月度计时，见 SchismSystem)
        using (FrameProfiler.Measure("分裂进程")) SchismSystem.Tick();
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
        using (FrameProfiler.Measure("头衔管理")) ModClass.KINGDOM_TITLE_MANAGER?.update(0f);

        // 每帧至少处理最少的那几个(minimum)，再看共享预算：前面的子系统、军团用光了预算时，帝国与王国也不至于一帧一个都轮不上
        // (平常模式以前不受预算管，现在受管，见 FrameJobs)
        int processedEmpires = 0;
        do
        {
            if (processedEmpires >= minimumEmpires && !SimulationFrameBudget.HasTime || !ProcessNextEmpire(realtime, empires)) break;
            processedEmpires++;
        }
        while ((processedEmpires < minimumEmpires || SimulationFrameBudget.HasTime) && EmpireCraftFrameSchedulingRules.CanContinue(
            processedEmpires, minimumEmpires, maximumEmpires, ElapsedMilliseconds(started), Math.Max(MinimumEmpireBudgetMilliseconds,
                budgetMilliseconds * EmpireBudgetShare)));

        // 重要的国家先过一遍(见 RealmPriority)：每帧至少 MinMajorsPerFrame 国，有预算就接着，一帧至多把名单过一轮；下面轮转时跳过它们
        IReadOnlyList<Kingdom> majors = RealmPriority.Majors;
        for (int done = 0; done < majors.Count && (done < MinMajorsPerFrame || SimulationFrameBudget.HasTime); done++)
        {
            if (_majorCursor >= majors.Count) _majorCursor = 0;
            ProcessKingdom(realtime, majors[_majorCursor++], true);
        }

        int processed = 0;
        do
        {
            if (processed >= minimumKingdoms && !SimulationFrameBudget.HasTime || !ProcessNextKingdom(realtime, kingdoms)) break;
            processed++;
        }
        while ((processed < minimumKingdoms || SimulationFrameBudget.HasTime) && EmpireCraftFrameSchedulingRules.CanContinue(
            processed, minimumKingdoms, maximumKingdoms, ElapsedMilliseconds(started), budgetMilliseconds));
    }

    public static void Reset()
    {
        Reset(World.world);
    }

    private static void Reset(MapBox world)
    {
        StateSettlementSystem.ResetWorldState();
        CityStabilitySystem.ResetWorldState();
        AdministrationReorganizationSystem.ResetWorldState();
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

    private const int MinMajorsPerFrame = 2;
    private static int _majorCursor;

    // 轮转到下一国(重要的国家另在前面过，这里跳过)
    private static bool ProcessNextKingdom(float realtime, IList<Kingdom> kingdoms)
    {
        if (kingdoms == null || kingdoms.Count == 0) return false;
        if (_kingdomCursor >= kingdoms.Count) _kingdomCursor = 0;
        Kingdom kingdom = kingdoms[_kingdomCursor++];
        if (RealmPriority.IsMajor(kingdom)) return true;
        ProcessKingdom(realtime, kingdom, false);
        return true;
    }

    // major：重要的国家(见 RealmPriority)，月度各步预算用完也至少推进一步
    private static void ProcessKingdom(float realtime, Kingdom kingdom, bool major)
    {
        if (kingdom == null || kingdom.isRekt() || AncientWarfareCompatibility.Owns(kingdom)) return;
        if (FaultRetryTimes.TryGetValue(kingdom.id, out float retryAt) && realtime < retryAt) return;

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
            if (!CityPopulationSystem.AbstractPopulationEnabled) state.MonthlyPhase = -1;
            if (!CityPopulationSystem.AbstractPopulationEnabled &&
                (state.LastStrategicWorldTime < 0d || Date.getMonthsSince(state.LastStrategicWorldTime) >= 1))
            {
                double now = World.world.getCurWorldTime();
                state.LastStrategicWorldTime = now;
                KingdomTypeCheck.execute(kingdom);
                CultureService.UpdateCityCultureShiftCandidates(kingdom);
                CultureService.UpdateCulturalAssimilationDuty(kingdom);
                PlotCheck.execute(kingdom);
                TemporaryFactionCheck.execute(kingdom);
                ReligionKingdomCheck.execute(kingdom);
                ExclaveMaintenanceSystem.Check(kingdom);
            }
            else if (CityPopulationSystem.AbstractPopulationEnabled)
            {
                if (state.MonthlyPhase < 0 &&
                    (state.LastStrategicWorldTime < 0d || Date.getMonthsSince(state.LastStrategicWorldTime) >= 1))
                {
                    state.LastStrategicWorldTime = World.world.getCurWorldTime();
                    state.MonthlyPhase = 0;
                }
                // 各个检查分别续跑，不把正在执行的旧月度工作重置为下一月。重要的国家预算用完也至少推进一步
                int steps = 0;
                while (state.MonthlyPhase >= 0 && (SimulationFrameBudget.HasTime || major && steps == 0))
                {
                    steps++;
                    switch (state.MonthlyPhase++)
                    {
                        case 0: KingdomTypeCheck.execute(kingdom); break;
                        case 1: CultureService.UpdateCityCultureShiftCandidates(kingdom); break;
                        case 2: CultureService.UpdateCulturalAssimilationDuty(kingdom); break;
                        case 3: PlotCheck.execute(kingdom); break;
                        case 4: TemporaryFactionCheck.execute(kingdom); break;
                        case 5: ReligionKingdomCheck.execute(kingdom); break;
                        case 6: StateSettlementSystem.Check(kingdom); break;
                        case 7: ExclaveMaintenanceSystem.Check(kingdom); break;
                    }
                    if (state.MonthlyPhase >= 8) state.MonthlyPhase = -1;
                }
            }
            FaultRetryTimes.Remove(kingdom.id);
        }
        catch (Exception error)
        {
            FaultRetryTimes[kingdom.id] = realtime + 5f;
            LogService.LogWarning($"[EmpireCraft] Strategic kingdom slice failed for {kingdom.id}: {error}");
        }
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
                using (FrameProfiler.Measure("帝国·月度")) empire.update();
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
