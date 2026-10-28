using db;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

public class EmpireManager : MetaSystemManager<Empire, EmpireData>
{
    private readonly List<Empire> _empiresToProcess = new();

    public EmpireManager() 
    {
        this.type_id = "empire";
    }
    public bool _dirty_cities = false;
    public override void updateDirtyUnits()
    {
    }

    public override void startCollectHistoryData()
    {
    }

    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        Process(runEmpireUpdates: true);
    }

    // 读档时(见 DataManager.LoadAll)只整理成员、核心国与解散无领土的帝国，不跑各帝国的月度更新(Empire.update)：
    // 这时帝国核心、法理、宗族、史书、城墙村镇都还没读进来，月度更新里的各系统按"没有核心/法理/宗族"处置——
    // 帝国改划按空法理排程、一读档就记成改划完成，武家执政把旧档的天皇家记录当成找不到清掉，分封把已见法理记成空的……
    // 读档后战略调度第一轮就会给每个帝国补跑一次(见 EmpireCraftStrategicScheduler，读档时排程已清空)
    public void RepairAfterLoad()
    {
        Process(runEmpireUpdates: false);
    }

    private double _lastLivenessCheck = -1d;

    // 存续检查分帧(用户 2026-10-26 反馈"原始部落帝国太多会造成卡顿")：以前到月就在一帧里逐帝国逐城点人口、查存续，
    // 帝国一多那一帧就卡一下；现在到月把帝国排成队，每帧至多 LivenessBudgetMs 毫秒(至少做一个)，没做完的下一帧接着做。
    // 每个帝国做的事、先后(先点人口再查存续，整轮查完才解散)与以前一样，只是摊到几帧里
    private const double LivenessBudgetMs = 1.5d;
    private readonly Queue<Empire> _livenessQueue = new();
    private readonly List<Empire> _livenessDissolve = new();
    private bool _livenessStats;

    // 战略调度每帧调用，自带月度计时：整理成员与核心国、解散已没有领土的帝国(同读档时，不跑帝国月度更新)。
    // 帝国管理器不在原版的管理器列表里，update 只在存档、读档时跑：核心国丢光城池、各省都已独立的帝国，
    // 地图上早已解体，帝国界面却一直显示现存，要到下次存档才解散
    public void CheckLiveness()
    {
        double now = World.world?.getCurWorldTime() ?? -1d;
        if (now < 0d) return;
        // 时间倒退(换了世界)：上一轮没做完的作废
        if (_lastLivenessCheck >= 0d && now < _lastLivenessCheck)
        {
            DropLivenessPass();
            _lastLivenessCheck = -1d;
        }
        if (_livenessQueue.Count == 0 && _livenessDissolve.Count == 0)
        {
            if (_lastLivenessCheck >= 0d && now >= _lastLivenessCheck && Date.getMonthsSince(_lastLivenessCheck) < 1) return;
            _lastLivenessCheck = now;
            BeginLivenessPass(now);
        }
        var budget = global::System.Diagnostics.Stopwatch.StartNew();
        do
        {
            if (_livenessQueue.Count == 0) break;
            Empire current = _livenessQueue.Dequeue();
            // 排队以后已解散、已移出的跳过
            if (current?.data == null || current.IsArchived() || get(current.id) != current) continue;
            if (_livenessStats) RefreshStats(current, World.world.getCurWorldTime());
            CheckOne(current, false, _livenessDissolve);
        } while (budget.Elapsed.TotalMilliseconds < LivenessBudgetMs);
        if (_livenessQueue.Count > 0) return;
        // 整轮查完：解散没有领土的帝国(同以前，查完才解散)
        foreach (Empire item in _livenessDissolve.ToList())
            if (item != null && !item.IsArchived() && !CoreStillHolds(item)) dissolveEmpire(item);
        _livenessDissolve.Clear();
    }

    // 存续检查分几帧做完、整轮查完才解散：查的那一帧判了空壳，到解散时中央还在、有城、还挂着本帝国编号的不解散
    // (出过世界地图开局一个月大明被当空壳解散：解散时中央明明 59 城、还归大明)；修一遍，记下当时各项供查
    private static bool CoreStillHolds(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        if (core?.data == null || core.isRekt() || (core.cities?.Count ?? 0) <= 0 || core.GetEmpireID() != empire.id) return false;
        bool repaired = empire.TryRepairState("liveness recheck");
        EmpireCraft.Scripts.Diagnostics.EmpireLossDiagnostics.OnDissolveSkipped(empire, repaired);
        return true;
    }

    // 到月开一轮：整理同盟、正统对手这些全局的当帧做完，各帝国排队(见 CheckLiveness)
    private void BeginLivenessPass(double worldTime)
    {
        _livenessQueue.Clear();
        _livenessDissolve.Clear();
        _empiresToProcess.Clear();
        foreach (Empire empire in this)
        {
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) continue;
            empire.RefreshCompatibilityMembership();
            _empiresToProcess.Add(empire);
        }
        ClearStaleLegitimacyRivalries();
        foreach (Empire empire in _empiresToProcess) _livenessQueue.Enqueue(empire);
        _empiresToProcess.Clear();
        _livenessStats = BeginStatsRefresh(worldTime);
    }

    private void DropLivenessPass()
    {
        _livenessQueue.Clear();
        _livenessDissolve.Clear();
    }

    private void Process(bool runEmpireUpdates)
    {
        // 读档、存档时整轮一次做完，还没做完的分帧存续检查就此作废
        DropLivenessPass();
        // 创建集合副本进行遍历
        _empiresToProcess.Clear();
        foreach (Empire empire in this)
        {
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(empire)) continue;
            empire.RefreshCompatibilityMembership();
            _empiresToProcess.Add(empire);
        }
        ClearStaleLegitimacyRivalries();
        double worldTime = World.world.getCurWorldTime();
        if (BeginStatsRefresh(worldTime))
            foreach (Empire e in _empiresToProcess)
                RefreshStats(e, worldTime);

        foreach (Empire current in _empiresToProcess)
            CheckOne(current, runEmpireUpdates, _to_dissolve);
        // 处理需要解散的帝国
        foreach (Empire item in _to_dissolve)
        {
            dissolveEmpire(item);
        }
        _to_dissolve.Clear();
        _empiresToProcess.Clear();
    }

    // 这一轮要不要重点人口(城变了、或满一个月)：要点就先清同盟(全局，当帧做完)，记下点过的时间
    private bool BeginStatsRefresh(double worldTime)
    {
        if (!_dirty_cities && _lastStatsCacheTimestamp > 0 && worldTime >= _lastStatsCacheTimestamp &&
            Date.getMonthsSince(_lastStatsCacheTimestamp) < 1) return false;
        // 禁止原版结盟时，旧存档遗留或其他途径漏进来的原版同盟也一并清掉(模组同盟与神力强制同盟保留)
        if (ModAllianceService.IsVanillaAllianceBanned()) ModAllianceService.DissolveVanillaAlliances();
        // 帝国本身及其成员国不留在同盟里(兜底：称帝、继承、读档等途径进帝国的)
        ModAllianceService.RemoveEmpireMembersFromAlliances();
        _lastStatsCacheTimestamp = worldTime;
        _dirty_cities = false;
        return true;
    }

    // 点一个帝国的人口、兵力(各成员国、各城一并记下)
    private static void RefreshStats(Empire e, double worldTime)
    {
        if (e.IsArchived()) return;
        e.recalculate();
        int pop = 0;
        int warriors = 0;
        int warriorsMax = 0;
        var tKingdoms = e.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            var k = tKingdoms[i];
            if (!EmpireMembershipService.BelongsTo(e, k)) continue;
            var ked = KingdomExtension.GetOrCreate(k);
            ked.last_cached_timestamp = -1L;
            int kp = k.getPopulationPeople();
            int kw = k.countTotalWarriors();
            int km = k.countWarriorsMax();
            pop += kp;
            warriors += kw;
            warriorsMax += km;
            ked.cached_population = kp;
            ked.cached_warriors = kw;
            ked.last_cached_timestamp = worldTime;
            var cities = k.cities;
            for (int j = 0; j < cities.Count; j++)
            {
                var c = cities[j];
                var ced = CityExtension.GetOrCreate(c);
                ced.last_cached_timestamp = -1L;
                ced.cached_population = c.CountLivingPopulation();
                ced.cached_warriors = c.CountLivingWarriors();
                ced.last_cached_timestamp = worldTime;
            }
        }
        e.data.cached_population = pop;
        e.data.cached_warriors = warriors;
        e.data.cached_warriors_max = warriorsMax;
        e.data.last_cached_timestamp = worldTime;
    }

    // 查一个帝国还在不在：修不好的记进 dissolve(整轮查完再解散)；runEmpireUpdates 时顺带跑月度更新
    private static void CheckOne(Empire current, bool runEmpireUpdates, List<Empire> dissolve)
    {
        if (current.IsArchived() || dissolve.Contains(current)) return;
        current.clearCursorOver();

        if (!current.checkActive())
        {
            if (!current.TryRepairState("EmpireManager.update fallback"))
            {
                dissolve.Add(current);
            }
        }
        else if (runEmpireUpdates)
        {
            current.update();
        }
    }

    // 清理失效的正统对手，并为旧存档中的既有僭越者补齐伪法理标记。
    private void ClearStaleLegitimacyRivalries()
    {
        foreach (Empire empire in _empiresToProcess)
        {
            if (empire?.data == null || empire.IsArchived() || empire.isRekt() ||
                !empire.data.legitimacy_rivalry_recognized) continue;
            Empire rival = ModClass.EMPIRE_MANAGER.get(empire.data.legitimacy_rival_empire_id);
            if (rival == null || rival.IsArchived() || rival.isRekt())
            {
                ImperialLegitimacyChallengeService.ClearRivalry(empire);
                continue;
            }
            if (!empire.data.legitimacy_challenger) continue;
            EmpireCore core = EmpireCoreManager.Get(empire);
            EmpireCore legitimateCore = EmpireCoreManager.Get(rival);
            KingdomTitle mainTitle = empire.CoreKingdom?.GetMainTitle();
            if (core == null || core.false_core_against_empire_id == rival.id ||
                !EmpireCoreManager.ContainsTitle(legitimateCore, mainTitle)) continue;
            core.false_core_against_empire_id = rival.id;
            EmpireCoreManager.SyncCitiesFromTitles(legitimateCore);
        }
    }


    // continued：皇族王国接续(见 Empire.ReplaceEmpire)，同一帝国换了中央，不记进已亡帝国名录。
    // silent：静默撤去(旧档里一部自立的部落帝国，部落不再是帝国，见 TribalFissionService.DissolveSettledTribes)——
    // 不记已亡帝国名录、不收进史书、不记皇族亡国与"本文化无帝国"的计时、不留解散诊断；进行中的制度改革照样交回核心王国；
    // 只属于它自己的帝国法理(空出来的 EmpireCore，见 OwnsCoreAlone)一并销毁
    public void dissolveEmpire(Empire pEmpire, bool continued = false, bool silent = false)
    {
        if (pEmpire == null) return;
        if (!silent)
        {
            // 帝国只从这里解散：记下是谁、由哪几层调用解散的(见 EmpireLossDiagnostics)
            EmpireCraft.Scripts.Diagnostics.EmpireLossDiagnostics.OnDissolving(pEmpire);
            // 末代君主的在位记录收进史书(移除后只剩 ALL_HISTORY_DATA 里的史书可查)；趁核心国还在记进已亡名录
            pEmpire.CloseCurrentReignRecord();
            if (!continued) FallenEmpireRecords.Record(pEmpire);
        }
        EmpireCore falseCore = EmpireCoreManager.Get(pEmpire);
        bool destroyFalseCore = falseCore?.false_core_against_empire_id > 0 &&
                                !EmpireCoreManager.GetEmpires(falseCore)
                                    .Any(other => other != pEmpire && !other.IsArchived());
        bool destroyOwnCore = silent && OwnsCoreAlone(pEmpire, falseCore);
        if (!silent) EmpireFormationService.OnEmpireDissolving(pEmpire);
        // 静默撤去的部落帝国：科技采纳记在帝国身上(见 TechnologySystem.GetRealmState)，交回本国，不退回它自立前的旧记录
        else HandBackRealmTech(pEmpire);
        // 进行中的制度改革交回核心王国(见 InstitutionSystem.Realm.cs)
        InstitutionSystem.OnEmpireDissolving(pEmpire);
        bool hadHistory = pEmpire.data != null && ModClass.ALL_HISTORY_DATA.ContainsKey(pEmpire.data.id);
        pEmpire.dissolve();
        pEmpire.Dispose();
        // Dispose 把史书收进 ALL_HISTORY_DATA；静默撤去的不留
        if (silent && !hadHistory && pEmpire.data != null) ModClass.ALL_HISTORY_DATA.Remove(pEmpire.data.id);
        pEmpire.Archive();
        removeObject(pEmpire);
        // 伪核心只是僭称者自行制造的主张，不是可被后继国家继承的历史法理。
        if ((destroyFalseCore || destroyOwnCore) && EmpireCoreManager.Get(falseCore.id) == falseCore)
            EmpireCoreManager.DestroyEmpireCore(falseCore);
    }

    // 帝国的科技采纳并进核心王国(本国没记过的整份交回；记过的补上已采纳的，采纳中的取进度大的)
    private static void HandBackRealmTech(Empire empire)
    {
        EmpireCraft.Scripts.Data.RealmTechState from = empire?.data?.realm_tech;
        Kingdom core = empire?.CoreKingdom;
        if (from == null || !from.initialized || core?.data == null || core.isRekt()) return;
        KingdomExtension.KingdomExtraData extra = KingdomExtension.GetOrCreate(core);
        EmpireCraft.Scripts.Data.RealmTechState to = extra.realm_tech;
        if (to == null || !to.initialized)
        {
            extra.realm_tech = from;
            return;
        }
        foreach (string tech in from.adopted.ToList()) to.Adopt(tech);
        foreach (KeyValuePair<string, float> entry in from.progress)
            if (!to.Has(entry.Key) && (!to.progress.TryGetValue(entry.Key, out float own) || own < entry.Value))
                to.progress[entry.Key] = entry.Value;
    }

    // 帝国法理只是它自己的：绑着它、没记过别的帝国、没有别的现存帝国用着，也不是别的法理的割据母核心
    // (部落自立时随帝国新建的那种；绑了前朝旧法理的不算，那是历史法理，要留着)
    private static bool OwnsCoreAlone(Empire empire, EmpireCore core)
    {
        if (empire == null || core == null || core.false_core_against_empire_id > 0 || core.empire_id != empire.id) return false;
        if (core.empire_history_ids != null && core.empire_history_ids.Any(id => id != empire.id)) return false;
        if (EmpireCoreManager.GetEmpires(core).Any(other => other != empire && !other.IsArchived())) return false;
        return !EmpireCoreManager.EmpireCores.Values.Any(other => other != null && other != core &&
                                                                  other.warlord_parent_core_id == core.id);
    }

    private List<Empire> _to_dissolve = new List<Empire>();

    public override void clear()
    {
        base.clear();
    }

    public List<TileZone> GetAllZones()
    {
        List<TileZone> zones = new();
        foreach (Empire e in this)
        {
            foreach(Kingdom k in e.kingdoms_list)
            {
                foreach(City c in k.cities)
                {
                    zones.AddRange(c.zones);
                }
            }
        }
        return zones;
    }

    public override void addObject(Empire pObject)
    {
        base.addObject(pObject);
        World.world.zone_calculator?.setDrawnZonesDirty();
    }
    public override void removeObject(Empire pKingdom)
    {
        base.removeObject(pKingdom);
        World.world.zone_calculator?.setDrawnZonesDirty();
    }
    
    public void RemoveArchivedEmpire(Empire pEmpire)
    {
        if (pEmpire == null) return;
        if (!pEmpire.IsArchived()) return;
        removeObject(pEmpire);
    }
    
    public void PurgeArchivedOlderThanYears(int years)
    {
        FallenEmpireRecords.PurgeOlderThanYears(years);
        var list = this.ToList();
        foreach (var e in list)
        {
            if (!e.IsArchived()) continue;
            if (Date.getYearsSince(e.data.timestamp_established_time) > years)
            {
                removeObject(e);
            }
        }
    }

    // Token: 0x0400230C RID: 8972
    public Sprite[] _cached_banner_backgrounds;

    // Token: 0x0400230D RID: 8973
    public Sprite[] _cached_banner_icons;


    // replacingEmpire：由该帝国延续而来（核心王国灭亡后由皇族王国接续等）。它此刻仍然存在，
    // 不能把它当成"同文化已有帝国"而拒绝建立继承者。
    // 成员国、分封国沿用帝国的颜色；它后来自立成帝国(分裂、皇族延续如"南唐"、正统对手)时会和原帝国同色，
    // 地图上连成一片分不清。两个现存帝国同色时，给较晚成立的一方换一个颜色
    public static void EnsureDistinctColor(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt() || ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire other in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (other == null || other == empire || other.isRekt() || other.IsArchived() ||
                other.CoreKingdom?.data == null || other.CoreKingdom.isRekt()) continue;
            if (other.CoreKingdom.data.color_id != core.data.color_id) continue;
            Empire younger = other.data.timestamp_established_time > empire.data.timestamp_established_time
                ? other
                : empire;
            Kingdom kingdom = younger.CoreKingdom;
            ActorAsset asset = kingdom.king?.asset ?? kingdom.units?.FirstOrDefault(unit => unit != null)?.asset;
            if (asset == null) return;
            ColorAsset next = kingdom.getColorLibrary()?.getNextColor(asset);
            if (next == null) return;
            kingdom.updateColor(next);
            younger.updateColor(kingdom.getColor());
            return;
        }
    }

    public Empire NewEmpire(Kingdom pKingdom, bool isSplit = false,
        bool allowCultureRival = false, bool forceNewCore = false, Empire replacingEmpire = null,
        bool suppressFoundingLog = false, EmpireCore bindCore = null)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(pKingdom)) return null;
        if (pKingdom == null || !pKingdom.isAlive())
        {
            return null;
        }
        string culture = CultureService.GetRealmCulture(pKingdom);
        if (!CultureService.IsValidCulture(culture) ||
            (!allowCultureRival && CultureService.HasActiveEmpireForCulture(culture, replacingEmpire)))
        {
            return null;
        }
        if (!EnsureKingForNewEmpire(pKingdom))
        {
            LogService.LogWarning($"[EmpireManager] Failed to create empire for kingdom {pKingdom?.name}: no valid king candidate.");
            return null;
        }
        long id = OverallHelperFunc.IdGenerator.NextId();
        var empire = newObjectFromID(id);
        empire.CreateNewEmpire(pKingdom, isSplit, replacingEmpire);
        if (empire.CoreKingdom == null)
        {
            LogService.LogWarning($"[EmpireManager] Failed to initialize empire core kingdom for {pKingdom?.name}.");
            return null;
        }
        empire.addFounder(pKingdom);
        empire.updateColor(pKingdom.getColor());
        EnsureDistinctColor(empire);
        empire.data.timestamp_given_time = World.world.getCurWorldTime();
        // 立国时间(开国气象 Empire.InFoundingGrace 用)
        empire.data.created_time = World.world.getCurWorldTime();
        // 帝国法理按本国持有的法理登记：先把本国还没有法理的城收进主法理，免得帝国法理、天下只剩都城一块地
        KingdomTitle foundingTitle = pKingdom.GetMainTitle();
        if (foundingTitle != null) KingdomTitleManager.AddUntitledCities(foundingTitle, pKingdom);
        // bindCore：称帝时指定绑哪个帝国法理(据有法理，见 RealmNamingSystem.PlanDominionBinding)
        var riseCore = forceNewCore ? null : bindCore ?? EmpireCoreManager.GetRiseCandidateCore(pKingdom);
        // 候选核心已归属别的现存帝国时不能抢过来(主法理在别国核心里的根本不能称帝，见 EmpireFormationService)；
        // 此时只可能是都城/其他法理落在别国核心里，主法理本身没有核心，按规则新建核心
        if (riseCore != null && (EmpireCoreManager.GetEmpires(riseCore).Any(other => other != empire && !other.IsArchived()) ||
                                 EmpireCoreManager.IsClaimedByContest(riseCore, empire)))
            riseCore = null;
        if (riseCore != null)
        {
            EmpireCoreManager.RebindEmpire(empire, riseCore);
        }
        else
        {
            EmpireCoreManager.newEmpireCore(empire, RealmNamingSystem.FreshCoreName(empire));
        }
        RealmNamingSystem.OnEmpireBound(empire, pKingdom);
        pKingdom.GetOrCreate().isEmpire = true;
        pKingdom.SetEmpireID(empire.id);
        EmpireFormationService.OnEmpireCreated(empire);
        // 称帝前王国在推的制度改革带进帝国(见 InstitutionSystem.Realm.cs)
        InstitutionSystem.OnEmpireProclaimed(empire);
        // 草原称汗开国不再自动把开国大汗记为雄主：同日先记(用户 2026-10-21 选"游牧雄主死后真分裂")，玩家随即反馈"帝国容易突然碎一地"，
        // 用户选回调——每个汗国的开国之君身后都必乱，太频。袭掠成名、发动统一的雄主照旧记(见 NomadCycleSystem.RememberHero)
        if (!suppressFoundingLog && empire.data.has_year_name)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, pKingdom.king.name, empire.GetEmpireName())
            {
                location = pKingdom.location,
                color_special1 = pKingdom.getColor().getColorText()
            }.add();
        }
        else if (!suppressFoundingLog)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_west_log, pKingdom.king.name, empire.GetEmpireName())
            {
                location = pKingdom.location,
                color_special1 = pKingdom.getColor().getColorText()
            }.add();
        }
        
        return empire;
    }

    private static bool EnsureKingForNewEmpire(Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.isAlive())
        {
            return false;
        }
        if (kingdom.hasKing() && kingdom.king != null && !kingdom.king.isRekt())
        {
            return true;
        }
        Actor fallbackKing = FindFallbackKingCandidate(kingdom);
        if (fallbackKing == null)
        {
            return false;
        }

        fallbackKing.removeFromArmy();
        if (fallbackKing.isCityLeader() && fallbackKing.city != null)
        {
            fallbackKing.city.removeLeader();
        }
        kingdom.setKing(fallbackKing);
        if (kingdom.capital != null)
        {
            fallbackKing.joinCity(kingdom.capital);
        }
        fallbackKing.joinKingdom(kingdom);
        return kingdom.hasKing() && kingdom.king != null && !kingdom.king.isRekt();
    }

    private static Actor FindFallbackKingCandidate(Kingdom kingdom)
    {
        if (kingdom?.cities != null)
        {
            for (int i = 0; i < kingdom.cities.Count; i++)
            {
                City city = kingdom.cities[i];
                if (city == null || city.isRekt() || city.units == null)
                {
                    continue;
                }
                for (int j = 0; j < city.units.Count; j++)
                {
                    Actor actor = city.units[j];
                    if (IsValidFallbackKingCandidate(actor))
                    {
                        return actor;
                    }
                }
            }
        }

        if (kingdom?.units != null)
        {
            for (int i = 0; i < kingdom.units.Count; i++)
            {
                Actor actor = kingdom.units[i];
                if (IsValidFallbackKingCandidate(actor))
                {
                    return actor;
                }
            }
        }

        return null;
    }

    private static bool IsValidFallbackKingCandidate(Actor actor)
    {
        return actor != null
               && !actor.isRekt()
               && actor.isAlive()
               && actor.isAdult()
               && actor.isUnitFitToRule();
    }

    public bool forceEmpire(Kingdom pKingdom1, Kingdom pKingdom2)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(pKingdom1) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.BlocksEmpireFormation(pKingdom2)) return false;
        Empire empire = ModClass.EMPIRE_MANAGER.get(pKingdom1.GetEmpireID());
        if (empire == null)
        {
            empire = ModClass.EMPIRE_MANAGER.get(pKingdom2.GetEmpireID());
        }
        bool result = false;
        if (empire == null)
        {
            empire = this.NewEmpire(pKingdom1);
            if (empire == null)
            {
                return false;
            }
            empire.join(pKingdom2, pForce: true);
            result = true;
        }
        else
        {
            // 玩家用权能手动加入是明确意图：无视以前的反叛记录(join 的 pLegitimacyTransfer 会一并清除)
            empire.join(pKingdom1, true, true, pLegitimacyTransfer: true);
            empire.join(pKingdom2, true, true, pLegitimacyTransfer: true);
        }
        return result;
    }
    public Sprite[] getBackgroundsList()
    {
        if (_cached_banner_backgrounds == null)
        {
            _cached_banner_backgrounds = SpriteTextureLoader.getSpriteList("alliances/backgrounds/");
        }
        return _cached_banner_backgrounds;
    }

    public Sprite[] getIconsList()
    {
        if (_cached_banner_icons == null)
        {
            _cached_banner_icons = SpriteTextureLoader.getSpriteList("alliances/icons/");
        }
        return _cached_banner_icons;
    }
    
    private double _lastStatsCacheTimestamp = -1L;
}
