using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using HarmonyLib;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckWarrior : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    // ============================================================
    // V3：人口保护 / 战时紧急征召记录
    // ============================================================

    // 只记录“开战补满时额外征出来”的 Warrior。
    // 原本的职业军人不会被记录，因此和平后不会被自动复员。
    private static readonly HashSet<Actor> _emergencyConscripts = new();

    // 战争结束后不要在 endWar 同一帧立刻复员，
    // 延迟少量帧，让 WorldBox 的战争关系先稳定下来。
    private static bool _demobilizationCheckRequested = false;
    private static int _demobilizationCheckFrame = -1;
    private const int DemobilizationDelayFrames = 30;

    // ============================================================
    // V3：独立 Warrior 生育维护
    //
    // 不再放在 WarriorMove.execute() 里。
    // ArmyManager.update 无论和平/战争都会调用这里。
    // ============================================================

    private const int FertilityActorsPerFrame = 24;
    private const int FertilityMinIntervalFrames = 450;
    private const int FertilityMaxIntervalFrames = 750;
    private const int FertilityInitialMinDelayFrames = 120;
    private const int FertilityInitialMaxDelayFrames = 600;

    private static readonly Dictionary<long, int> _nextFertilityCheckFrame = new();

    // 只 snapshot Army 列表；不 snapshot 全世界人口。
    private static List<Army> _fertilityArmySnapshot = new();
    private static int _fertilityArmyIndex = 0;
    private static int _fertilityUnitIndex = 0;
    private static int _fertilitySnapshotRefreshFrame = -1;
    private const int FertilityArmySnapshotRefreshFrames = 300;

    // ============================================================
    // 分帧战争征兵
    // ============================================================

    // 每帧最多检查多少个开战紧急征兵候选。
    // 多国同时开战时也不会在一帧扫描完几万人口。
    private const int EmergencyRecruitCandidatesPerFrame = 40;

    private static int _lastRecruitQueueProcessFrame = -1;

    private sealed class CityRecruitState
    {
        public City city;
        public Kingdom kingdom;

        public int need;

        // 已按人口保护顺序排好：
        // 1. 男性
        // 2. 没有 lover 的女性
        // 3. 有 lover 的女性（最后才动）
        //
        // pregnant 女性完全排除，不参与紧急征召。
        public List<Actor> candidates;
        public int normalIndex;

        // 第一轮 checkCanMakeWarrior 不通过的人，
        // 第二轮才作为紧急征召候选。
        public List<Actor> fallbackCandidates = new();
        public int fallbackIndex;

        public bool initialized;
        public bool normalPassFinished;
    }

    private sealed class KingdomRecruitJob
    {
        public Kingdom kingdom;
        public List<City> cities;
        public int cityIndex;
        public CityRecruitState currentCity;
    }

    private static readonly Queue<KingdomRecruitJob> _recruitJobs = new();
    private static readonly HashSet<long> _queuedRecruitKingdomIds = new();

    public override BehResult execute(Actor pActor)
    {
        if (pActor == null || pActor.isRekt())
            return BehResult.Continue;

        if (pActor.isKing())
        {
            pActor.stopBeingWarrior();
        }

        if (EmpireCraftWorldLawLibrary
            .empirecraft_law_prevent_building_destroy
            .isEnabled())
        {
            pActor.asset.can_attack_buildings = false;
        }

        if (!pActor.isUnitFitToRule()) return BehResult.Continue;
        if (!pActor.hasKingdom()) return BehResult.Continue;
        if (pActor.isKing()) return BehResult.Continue;
        if (pActor.isCityLeader()) return BehResult.Continue;
        if (!pActor.isAdult()) return BehResult.Continue;
        if (!pActor.hasCity()) return BehResult.Continue;
        if (pActor.isWarrior()) return BehResult.Continue;

        Kingdom pKingdom = pActor.kingdom;

        if (pKingdom.GetRegime() == null) return BehResult.Continue;
        if (!pKingdom.GetRegime().IsAllowArmy()) return BehResult.Continue;
        if (!WorldLawLibrary.world_law_civ_army.isEnabled()) return BehResult.Continue;

        // 平时征兵：保留原逻辑，不改成高频强征。
        TryNormalRecruit(pActor);

        return BehResult.Continue;
    }

    // ============================================================
    // WarPatch 只负责“排队一次”
    // ============================================================

    public static void QueueWarStartFill(Kingdom kingdom)
    {
        if (!CanEmergencyMobilizeKingdom(kingdom))
            return;

        long id = kingdom.id;

        if (!_queuedRecruitKingdomIds.Add(id))
            return;

        var cities = kingdom.cities?.ToList();

        if (cities == null || cities.Count == 0)
        {
            _queuedRecruitKingdomIds.Remove(id);
            return;
        }

        _recruitJobs.Enqueue(new KingdomRecruitJob
        {
            kingdom = kingdom,
            cities = cities,
            cityIndex = 0,
            currentCity = null
        });
    }

    // 由 WarPatch.update 每帧调用；内部只跑一次。
    public static void ProcessEmergencyRecruitmentJobs()
    {
        int frame = Time.frameCount;

        if (_lastRecruitQueueProcessFrame == frame)
            return;

        _lastRecruitQueueProcessFrame = frame;

        int budget = EmergencyRecruitCandidatesPerFrame;

        // Round-robin：
        // 每次从队头拿一个国家，用掉一部分预算后放回队尾。
        // 多国同时开战时不会一直卡在第一个大国。
        int jobsThisFrame = _recruitJobs.Count;

        while (budget > 0 &&
               jobsThisFrame > 0 &&
               _recruitJobs.Count > 0)
        {
            KingdomRecruitJob job = _recruitJobs.Dequeue();
            jobsThisFrame--;

            if (!IsRecruitJobStillValid(job))
            {
                FinishRecruitJob(job);
                continue;
            }

            int slice = Math.Min(10, budget);
            int used = ProcessRecruitJobSlice(job, slice);

            // 至少消耗 1 个预算，避免某个异常 job 空转。
            budget -= Math.Max(1, used);

            if (IsRecruitJobFinished(job))
            {
                FinishRecruitJob(job);
            }
            else
            {
                _recruitJobs.Enqueue(job);
            }
        }
    }

    private static bool IsRecruitJobStillValid(KingdomRecruitJob job)
    {
        if (job == null ||
            job.kingdom == null ||
            job.kingdom.isRekt() ||
            !job.kingdom.hasEnemies())
        {
            return false;
        }

        return CanEmergencyMobilizeKingdom(job.kingdom);
    }

    private static bool IsRecruitJobFinished(KingdomRecruitJob job)
    {
        if (job == null)
            return true;

        return job.currentCity == null &&
               job.cityIndex >= job.cities.Count;
    }

    private static void FinishRecruitJob(KingdomRecruitJob job)
    {
        if (job?.kingdom != null)
        {
            _queuedRecruitKingdomIds.Remove(job.kingdom.id);
        }
    }

    private static int ProcessRecruitJobSlice(
        KingdomRecruitJob job,
        int budget)
    {
        int used = 0;

        while (used < budget)
        {
            if (job.currentCity == null)
            {
                if (job.cityIndex >= job.cities.Count)
                    break;

                City city = job.cities[job.cityIndex++];
                job.currentCity = CreateCityRecruitState(
                    city,
                    job.kingdom);

                if (job.currentCity == null)
                {
                    continue;
                }
            }

            CityRecruitState state = job.currentCity;

            if (!state.initialized)
            {
                InitializeCityRecruitState(state);

                if (state.need <= 0 ||
                    state.candidates == null ||
                    state.candidates.Count == 0)
                {
                    job.currentCity = null;
                    continue;
                }
            }

            // 第一轮：只征正常 checkCanMakeWarrior 允许的人。
            if (!state.normalPassFinished)
            {
                if (state.normalIndex >= state.candidates.Count ||
                    state.need <= 0)
                {
                    state.normalPassFinished = true;

                    if (state.need <= 0)
                    {
                        job.currentCity = null;
                        continue;
                    }
                }
                else
                {
                    Actor actor = state.candidates[state.normalIndex++];
                    used++;

                    if (!CanEmergencyRecruitActor(
                            actor,
                            state.city,
                            state.kingdom))
                    {
                        continue;
                    }

                    bool normalAllowed = false;

                    try
                    {
                        normalAllowed =
                            state.city.checkCanMakeWarrior(actor);
                    }
                    catch
                    {
                    }

                    if (normalAllowed)
                    {
                        if (TryMakeWarrior(
                                state.city,
                                actor,
                                state.kingdom))
                        {
                            state.need--;
                        }
                    }
                    else
                    {
                        // 只记录一次，不再重新扫描 city.units。
                        state.fallbackCandidates.Add(actor);
                    }

                    continue;
                }
            }

            // 第二轮：正常候选不足时，才使用紧急征召名单。
            if (state.need > 0)
            {
                if (state.fallbackIndex >= state.fallbackCandidates.Count)
                {
                    LogService.LogInfo(
                        $"战争动员未完全补满：{state.city.name}，仍缺 {state.need} 名合格成年市民");

                    job.currentCity = null;
                    continue;
                }

                Actor actor =
                    state.fallbackCandidates[state.fallbackIndex++];

                used++;

                if (!CanEmergencyRecruitActor(
                        actor,
                        state.city,
                        state.kingdom))
                {
                    continue;
                }

                if (TryMakeWarrior(
                        state.city,
                        actor,
                        state.kingdom))
                {
                    state.need--;
                }

                continue;
            }

            job.currentCity = null;
        }

        return used;
    }

    private static CityRecruitState CreateCityRecruitState(
        City city,
        Kingdom kingdom)
    {
        if (city == null ||
            kingdom == null ||
            city.kingdom != kingdom)
        {
            return null;
        }

        return new CityRecruitState
        {
            city = city,
            kingdom = kingdom
        };
    }

    private static void InitializeCityRecruitState(
        CityRecruitState state)
    {
        state.initialized = true;

        try
        {
            int current = state.city.countWarriors();
            int maximum = state.city.getMaxWarriors();
            state.need = Math.Max(0, maximum - current);
        }
        catch
        {
            state.need = 0;
            return;
        }

        // 只在轮到这座城市时才 snapshot，
        // 不在宣战那一帧把全国所有 city.units 一口气 ToList。
        var source =
            state.city.units?.ToList() ?? new List<Actor>();

        // 人口保护：
        // pregnant 永远不参与这次紧急征召。
        //
        // 优先男性；其次没有 lover 的女性；
        // 有 lover 的女性只有真的不够补满时才会被征。
        var males = new List<Actor>();
        var femalesWithoutLover = new List<Actor>();
        var femalesWithLover = new List<Actor>();

        foreach (Actor actor in source)
        {
            if (!CanEmergencyRecruitActor(
                    actor,
                    state.city,
                    state.kingdom))
            {
                continue;
            }

            if (actor.hasStatus("pregnant"))
                continue;

            if (!actor.isSexFemale())
            {
                males.Add(actor);
                continue;
            }

            if (actor.lover == null ||
                !actor.lover.isAlive())
            {
                femalesWithoutLover.Add(actor);
            }
            else
            {
                femalesWithLover.Add(actor);
            }
        }

        state.candidates = new List<Actor>(
            males.Count +
            femalesWithoutLover.Count +
            femalesWithLover.Count);

        state.candidates.AddRange(males);
        state.candidates.AddRange(femalesWithoutLover);
        state.candidates.AddRange(femalesWithLover);
    }

    private static bool TryMakeWarrior(
        City city,
        Actor actor,
        Kingdom kingdom)
    {
        if (city == null ||
            actor == null ||
            actor.isWarrior())
        {
            return false;
        }

        try
        {
            city.makeWarrior(actor);

            if (!actor.isWarrior())
                return false;

            // 记录为“本次战争额外紧急征召”。
            // 和平后只复员这些人，不碰原职业军人。
            _emergencyConscripts.Add(actor);

            // 新征出的 Warrior 不同步开始复杂寻路。
            // 只丢进移动分帧队列。
            EmpireCraftActorCheckWarriorMove
                .QueueActorForWarMobilization(
                    actor,
                    kingdom);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanEmergencyMobilizeKingdom(
        Kingdom kingdom)
    {
        if (kingdom == null ||
            kingdom.isRekt() ||
            kingdom.isNeutral())
        {
            return false;
        }

        if (kingdom.GetRegime() == null)
            return false;

        if (!kingdom.GetRegime().IsAllowArmy())
            return false;

        return WorldLawLibrary
            .world_law_civ_army
            .isEnabled();
    }

    private static bool CanEmergencyRecruitActor(
        Actor actor,
        City city,
        Kingdom kingdom)
    {
        if (actor == null ||
            !actor.isAlive() ||
            actor.isRekt())
        {
            return false;
        }

        if (actor.city != city ||
            actor.kingdom != kingdom)
        {
            return false;
        }

        if (actor.isWarrior() ||
            actor.isKing() ||
            actor.isCityLeader() ||
            !actor.isAdult() ||
            !actor.isUnitFitToRule())
        {
            return false;
        }

        return true;
    }

    // ============================================================
    // V3：ArmyManager.update 每帧调用一次
    //
    // 这里负责：
    // 1. 分帧紧急征兵；
    // 2. Warrior 生育；
    // 3. 战争结束后的紧急征召兵复员。
    // ============================================================

    public static void ProcessArmyPopulationMaintenance()
    {
        ProcessEmergencyRecruitmentJobs();
        ProcessSoldierFertilityBudget();
        ProcessPendingDemobilization();
    }

    // ============================================================
    // Warrior 生育
    // ============================================================

    private static void ProcessSoldierFertilityBudget()
    {
        RefreshFertilityArmySnapshotIfNeeded();

        if (_fertilityArmySnapshot == null ||
            _fertilityArmySnapshot.Count == 0)
        {
            return;
        }

        int budget = FertilityActorsPerFrame;
        int safety = 0;

        while (budget > 0 &&
               _fertilityArmySnapshot.Count > 0 &&
               safety < 512)
        {
            safety++;

            if (_fertilityArmyIndex >= _fertilityArmySnapshot.Count)
            {
                _fertilityArmyIndex = 0;
                _fertilityUnitIndex = 0;
            }

            Army army = _fertilityArmySnapshot[_fertilityArmyIndex];

            if (army == null ||
                army.isRekt() ||
                army.units == null ||
                army.units.Count == 0)
            {
                _fertilityArmyIndex++;
                _fertilityUnitIndex = 0;
                continue;
            }

            if (_fertilityUnitIndex >= army.units.Count)
            {
                _fertilityArmyIndex++;
                _fertilityUnitIndex = 0;
                continue;
            }

            Actor actor = army.units[_fertilityUnitIndex++];
            budget--;

            TrySoldierPregnancyIndependent(actor);
        }
    }

    private static void RefreshFertilityArmySnapshotIfNeeded()
    {
        int frame = Time.frameCount;

        bool needsRefresh =
            _fertilityArmySnapshot == null ||
            _fertilityArmySnapshot.Count == 0 ||
            _fertilitySnapshotRefreshFrame < 0 ||
            frame - _fertilitySnapshotRefreshFrame >=
                FertilityArmySnapshotRefreshFrames;

        if (!needsRefresh)
            return;

        _fertilitySnapshotRefreshFrame = frame;

        try
        {
            _fertilityArmySnapshot =
                World.world.armies?
                    .Where(a => a != null && !a.isRekt())
                    .ToList()
                ?? new List<Army>();
        }
        catch
        {
            _fertilityArmySnapshot = new List<Army>();
        }

        if (_fertilityArmyIndex >= _fertilityArmySnapshot.Count)
            _fertilityArmyIndex = 0;

        _fertilityUnitIndex = 0;
    }

    private static void TrySoldierPregnancyIndependent(Actor actor)
    {
        if (actor == null ||
            !actor.isAlive() ||
            actor.isRekt() ||
            !actor.isWarrior())
        {
            return;
        }

        long actorId = actor.getID();
        int frame = Time.frameCount;

        // 第一次只安排未来检查时间，避免一批新兵在同一帧集体检查。
        if (!_nextFertilityCheckFrame.TryGetValue(
                actorId,
                out int nextFrame))
        {
            _nextFertilityCheckFrame[actorId] =
                frame + UnityEngine.Random.Range(
                    FertilityInitialMinDelayFrames,
                    FertilityInitialMaxDelayFrames + 1);

            return;
        }

        if (frame < nextFrame)
            return;

        _nextFertilityCheckFrame[actorId] =
            frame + UnityEngine.Random.Range(
                FertilityMinIntervalFrames,
                FertilityMaxIntervalFrames + 1);

        if (!actor.isSexFemale())
            return;

        Actor lover = actor.lover;

        if (lover == null ||
            lover == actor ||
            !lover.isAlive() ||
            lover.isRekt())
        {
            return;
        }

        if (!actor.canBreed() ||
            !lover.canBreed() ||
            !actor.isAdult() ||
            !lover.isAdult())
        {
            return;
        }

        if (actor.hasReachedOffspringLimit())
            return;

        if (actor.hasStatus("pregnant") ||
            actor.hasStatus("afterglow") ||
            lover.hasStatus("afterglow"))
        {
            return;
        }

        if (actor.city == null)
            return;

        try
        {
            float maturationTime =
                actor.getMaturationTimeSeconds();

            actor.addStatusEffect(
                "pregnant",
                maturationTime,
                true);

            float afterglowTime =
                maturationTime + 30f;

            actor.addStatusEffect(
                "afterglow",
                afterglowTime,
                true);

            lover.addStatusEffect(
                "afterglow",
                afterglowTime,
                true);
        }
        catch
        {
            // 生育失败绝不影响军事 AI。
        }
    }

    // ============================================================
    // 战后复员
    // ============================================================

    public static void RequestDemobilizationCheck()
    {
        _demobilizationCheckRequested = true;
        _demobilizationCheckFrame =
            Time.frameCount + DemobilizationDelayFrames;
    }

    private static void ProcessPendingDemobilization()
    {
        if (!_demobilizationCheckRequested)
            return;

        if (Time.frameCount < _demobilizationCheckFrame)
            return;

        _demobilizationCheckRequested = false;

        var snapshot = _emergencyConscripts.ToList();

        foreach (Actor actor in snapshot)
        {
            if (actor == null ||
                !actor.isAlive() ||
                actor.isRekt())
            {
                _emergencyConscripts.Remove(actor);
                continue;
            }

            Kingdom kingdom = actor.kingdom;

            if (kingdom == null ||
                kingdom.isRekt())
            {
                _emergencyConscripts.Remove(actor);
                continue;
            }

            // 如果本人所在国家/帝国仍在任何活跃战争中，不复员。
            if (IsKingdomOrEmpireStillAtWar(kingdom))
                continue;

            try
            {
                if (actor.isWarrior() &&
                    !actor.isKing() &&
                    !actor.isCityLeader())
                {
                    actor.ClearFrontLineMoveTarget();
                    actor.stopBeingWarrior();

                    // 只在复员瞬间 reset 一次，
                    // 让其重新进入普通市民工作/社交/生育 AI。
                    actor.cancelAllBeh();
                }
            }
            catch
            {
            }

            _emergencyConscripts.Remove(actor);
        }

        // 如果还有因为其它战争没能复员的人，
        // 后续某场战争结束时 WarPatch 会再次 Request。
    }

    private static bool IsKingdomOrEmpireStillAtWar(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt())
            return false;

        if (IsKingdomDirectlyInActiveWar(kingdom))
            return true;

        Empire empire = kingdom.GetEmpire();
        Kingdom core = empire?.CoreKingdom;

        if (core == null && kingdom.IsEmpire())
            core = kingdom;

        if (core != null &&
            core != kingdom &&
            IsKingdomDirectlyInActiveWar(core))
        {
            return true;
        }

        return false;
    }

    private static bool IsKingdomDirectlyInActiveWar(Kingdom kingdom)
    {
        if (kingdom == null)
            return false;

        try
        {
            if (kingdom.hasEnemies())
                return true;
        }
        catch
        {
        }

        try
        {
            foreach (War war in World.world.wars.getWars(kingdom))
            {
                if (war != null &&
                    war.isAlive() &&
                    !war.hasEnded())
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    // ============================================================
    // 原平时征兵逻辑
    // ============================================================

    private static void TryNormalRecruit(Actor pActor)
    {
        Kingdom pKingdom = pActor.kingdom;

        if (!pKingdom.IsInEmpire())
        {
            if (pActor.city.checkCanMakeWarrior(pActor))
            {
                try
                {
                    pActor.city.makeWarrior(pActor);
                }
                catch
                {
                }
            }

            return;
        }

        Empire empire = pKingdom.GetEmpire();

        if (empire == null || empire.isRekt())
            return;

        if (CountAllCenterArmy(empire) <
                empire.data.MilitaryExpenditure * 25 &&
            pKingdom.GetMoney() > 0)
        {
            if (pActor.city?.checkCanMakeWarrior(pActor) ?? false)
            {
                try
                {
                    pActor.city.makeWarrior(pActor);
                }
                catch
                {
                }
            }

            if (pKingdom.GetRegime().IsAllowSupportCenterArmy())
            {
                var armies = GetAllCenterArmy(empire);

                foreach (var a in armies)
                {
                    if (a == null ||
                        !a.hasCaptain() ||
                        a._captain == null)
                    {
                        continue;
                    }

                    if (a.units.Count < a._captain.warfare)
                    {
                        var city = a._captain.city;

                        try
                        {
                            pActor.setArmy(a);

                            if (city != null)
                                pActor.setCity(city);

                            if (empire.CoreKingdom != null)
                            {
                                pActor.setKingdom(
                                    empire.CoreKingdom);
                            }
                        }
                        catch
                        {
                        }

                        break;
                    }
                }
            }
        }
        else
        {
            City city = pActor.city;

            if (city != null &&
                city.checkCanMakeWarrior(pActor))
            {
                try
                {
                    city.makeWarrior(pActor);
                }
                catch
                {
                }
            }
        }
    }

    public static List<Army> GetAllCenterArmy(Empire empire)
    {
        List<Army> res = new List<Army>();

        if (empire == null ||
            empire.kingdoms_list == null)
        {
            return res;
        }

        var ks = empire.kingdoms_list;

        for (int i = 0; i < ks.Count; i++)
        {
            var k = ks[i];

            if (k == null)
                continue;

            var a = k.GetCenterArmy();

            if (a != null && !a.isRekt())
            {
                res.Add(a);
            }
        }

        return res;
    }

    public static int CountAllCenterArmy(Empire empire)
    {
        var armies = GetAllCenterArmy(empire);
        int total = 0;

        for (int i = 0; i < armies.Count; i++)
        {
            total += armies[i].units.Count;
        }

        return total;
    }
}
