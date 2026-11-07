using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 一次科技回退的结果(见 TechnologySystem.RollBack、TechRollbackSystem)
public sealed class TechRollbackResult
{
    public string culture = "";
    public int from_era;
    public int to_era;
    // 删掉的科技、遗忘的材料、撤下的制度(净减少的已施行节点)、排队降级建筑的城；举世退回原始时另记退了几个文化
    public int cultures;
    public int techs;
    public int materials;
    public int institutions;
    public int cities;
    public bool Changed => techs > 0 || materials > 0 || institutions > 0;
}

// 科技回退(用户 2026-11-07 转群友"各位有没有什么保存当前地图城市边界的同时，能把文化全部退回原始时代的方法？
// 我整了半天，让他们一个个科技都成为原始，但问题很快他就反弹了"，要"单独做个godpower来回退科技")：
//   · 科技回退(点城，见 TechRollbackPowers)：城所属国家的文化(同科技窗口)退回上一个时代——当前最高时代档的科技连同以它们为前置的
//     后续科技全部删掉；连点一路退到原始时代(一项科技不剩)；
//   · 举世退回原始(按钮点两下确认)：天下所有文化一齐退回原始时代。
//   城界、地块、国家、帝国、人物与人口一概不动。
// 以前逐项回退会"反弹"，是因为：读档后第一次结算按城里现有的建筑把对应科技补回来(BackfillBuildings)；没开局补齐过的文化按建筑
// 与已推行的制度补齐(Bootstrap)；旧档按各国现行政体、已推行的制度补记科技(BackfillRegimes、BackfillCivicsLine)；储备的科技点
// 下一年就买回来；接触中更先进的文化让重新研究打折(扩散)。所以这里一并处理：
//   1. 科技(TechnologySystem.RollBack)：删掉的科技视同从未研究，进度、在研、玩家目标、加速事件记录清掉，科技点储备清零；
//      绑在科技上的材料(iron、coal、saltpeter、oil、uranium 这类要前置科技才能发现的)一并遗忘；各国的采纳记录去掉；
//      开局补齐、读档补记全标成已做过，并记下 rolled_back：读档不再按建筑补科技，承袭前代帝制也要先有科技；
//   2. 制度(RevokeInstitutions)：已施行却缺了所需科技的制度，走制度窗口「回退此制度」同一条路撤下(连同后续制度，
//      被取代的旧制度随之恢复，恢复的旧制度也缺科技就再撤一轮)。政体由制度反推：各国照原有的逐国跟进(InstitutionSystem.
//      TryAdvanceRegimeTransition)在 2~5 年内退回本文化剩下的最高政体；退到原始阶段时，已成形的王国、帝国按原有规矩保留现政体
//      (不把帝国打回部落、赶走成员)，共和、现代由共和体系管；
//   3. 建筑(Tick 逐帧分批，大图不卡)：城里要某项本文化没有的科技的建筑，顺着升级链往下找本文化能建的最高一级换上——
//      民居先在同一块地上找空处盖低一级的再拆旧的，不留住房空档；别的建筑拆了在原处补盖(拆除要过一会儿才腾出地方，
//      等不到就在本块地、本城别处找地方)；没有可降的拆掉，但民居、城心的公所篝火留着(不把城拆成无房可住、没有城心)；
//      文化城池(city_<文化>_N)、村镇、厂区不受科技门槛管，不动；
//   4. 士兵身上已有的装备、已在场的载具照旧(打造、换装、再生产照新的科技卡口来；开局补齐本来就不按装备补科技，不会反弹)；
//      已写成的时代名著照旧；接触中的文化若更先进，重新研究照常打折(举世退回原始时就没有更先进的了)。
// 汇总写一条日志："[科技回退] … 文化 X：时代 A → B，删科技 n、撤制度 m、降级建筑 k"。
public static class TechRollbackSystem
{
    // 每帧至多试几处补盖；过建筑至多花几毫秒(补盖另有一份)
    private const int PlacementsPerFrame = 24;
    private const long BudgetMs = 3;
    // 补盖：拆了多少秒(游戏不暂停时)后改在本块地、本城别处找地方；等多久放弃
    private const float SpotSeconds = 4f, GiveUpSeconds = 30f;
    // 顺着升级链往下找至多几级；本城别处找地方至多试几块地
    private const int MaxChainSteps = 16, MaxZoneTries = 12;
    private const string HallType = "type_hall", BonfireType = "type_bonfire";

    // 一个文化这次回退的账(城一座座过完了写汇总日志)
    private sealed class Job
    {
        public string Culture;
        public int FromEra, ToEra, Techs, Materials, Institutions;
        public int Cities;
        public int Downgraded, Removed, Kept;
        public WorldJob World;
    }

    // 举世退回原始的总账
    private sealed class WorldJob
    {
        public int Cultures, Techs, Institutions, Open;
        public int Downgraded, Removed, Kept;
    }

    // 拆了旧建筑、等着在原处补盖的低一级建筑
    private sealed class Placement
    {
        public City City;
        public WorldTile Tile;
        public BuildingAsset Asset;
        public float Since;
    }

    private static readonly Dictionary<string, Job> Jobs = new(StringComparer.Ordinal);
    private static readonly Queue<(City city, long id, string culture)> CityQueue = new();
    private static readonly HashSet<(long city, string culture)> Queued = new();
    private static readonly Queue<Placement> Pending = new();
    // 游戏不暂停时累计的现实秒数(补盖的等待按它算，暂停时拆除动画也停着)
    private static float _clock;
    private static int _lost;

    public static void ResetWorldState()
    {
        Jobs.Clear();
        CityQueue.Clear();
        Queued.Clear();
        Pending.Clear();
        _city = null;
        _cityCulture = null;
        _cityJob = null;
        _buildings = null;
        _lost = 0;
    }

    #region 回退

    // 点城：本文化退回上一个时代(已在石器时代的退回原始，一项科技不剩)；已在原始时代的照样把缺科技的制度、建筑清一遍
    public static TechRollbackResult StepBack(string culture, Kingdom kingdom)
    {
        int era = TechnologySystem.GetEraTier(culture);
        int keep = era <= 1 ? -1 : era - 1;
        TechRollbackResult result = RollBackCulture(culture, keep, null);
        if (result.techs > 0)
        {
            string text = string.Format(LM.Get("tech_rollback_log"), culture.GetCultureTranslate(),
                TechnologySystem.EraName(result.from_era), TechnologySystem.EraName(result.to_era));
            EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogMajor(text, kingdom);
            Record(culture, text);
        }
        return result;
    }

    // 举世退回原始：存过科技、制度的文化与眼下有国家、有城的文化，一齐退回原始时代
    public static TechRollbackResult WorldToPrimitive()
    {
        var total = new TechRollbackResult();
        if (World.world?.cities == null) return total;
        var cultures = new HashSet<string>(StringComparer.Ordinal);
        cultures.UnionWith(TechnologySystem.ExportStates().Keys);
        cultures.UnionWith(InstitutionSystem.ExportCultureStates().Keys);
        cultures.UnionWith(CultureService.GetActiveCultureKeys());
        foreach (City city in World.world.cities.list)
            if (city?.data != null && !city.isRekt()) cultures.Add(TechnologySystem.GetCultureOf(city));
        cultures.RemoveWhere(culture => !CultureService.IsValidCulture(culture));
        var world = new WorldJob();
        foreach (string culture in cultures.OrderBy(key => key, StringComparer.Ordinal))
        {
            TechRollbackResult result = RollBackCulture(culture, -1, world, queueCities: false);
            world.Cultures++;
            total.techs += result.techs;
            total.materials += result.materials;
            total.institutions += result.institutions;
        }
        // 城一次排上(按文化各过一遍全图太慢)
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt()) continue;
            string culture = TechnologySystem.GetCultureOf(city);
            if (cultures.Contains(culture) && Enqueue(city, culture)) total.cities++;
        }
        world.Techs = total.techs;
        world.Institutions = total.institutions;
        total.cultures = world.Cultures;
        // 没有城要过的文化当场记账
        foreach (string culture in cultures)
            if (Jobs.TryGetValue(culture, out Job job) && job.World == world && job.Cities <= 0) Finish(job);
        if (world.Open <= 0) FinishWorld(world);
        string text = LM.Get("tech_rollback_world_log");
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogMajor(text);
        Record(null, text);
        return total;
    }

    // 退一个文化：科技、制度当场退，城里的建筑排队逐帧降级
    private static TechRollbackResult RollBackCulture(string culture, int keepTier, WorldJob world, bool queueCities = true)
    {
        TechRollbackResult result = TechnologySystem.RollBack(culture, keepTier);
        result.institutions = RevokeInstitutions(culture);
        // 上一次的城还没过完又点了一次：并进同一本账(时代起点照旧)；举世退回原始时记进总账
        if (!Jobs.TryGetValue(culture, out Job job)) Jobs[culture] = job = new Job { Culture = culture, FromEra = result.from_era };
        if (world != null && job.World != world)
        {
            if (job.World != null && --job.World.Open <= 0) FinishWorld(job.World);
            job.World = world;
            world.Open++;
        }
        job.ToEra = result.to_era;
        job.Techs += result.techs;
        job.Materials += result.materials;
        job.Institutions += result.institutions;
        if (queueCities)
        {
            foreach (City city in World.world?.cities?.list ?? new List<City>())
                if (city?.data != null && !city.isRekt() &&
                    string.Equals(TechnologySystem.GetCultureOf(city), culture, StringComparison.Ordinal) && Enqueue(city, culture))
                    result.cities++;
            if (job.Cities <= 0) Finish(job);
        }
        return result;
    }

    // 撤下本文化已施行、却缺了所需科技的制度(走制度窗口「回退此制度」同一条路，见 InstitutionSystem.ForceRevokeNodes)；
    // 被它们取代的旧制度随之恢复，恢复的旧制度若也缺科技，再撤一轮。返回净撤下的项数
    public static int RevokeInstitutions(string culture)
    {
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state?.enacted_node_ids == null) return 0;
        var before = new HashSet<string>(state.enacted_node_ids, StringComparer.Ordinal);
        for (int round = 0; round < 16; round++)
        {
            List<string> lacking = state.enacted_node_ids.Where(id => LacksTech(culture, InstitutionDefinitionRegistry.Get(id))).ToList();
            if (lacking.Count == 0 || InstitutionSystem.ForceRevokeNodes(culture, lacking, announce: false) <= 0) break;
        }
        // 正在推的改革，目标缺了科技或前置的作废(否则推完又施行回来)
        InstitutionSystem.DropReformsLackingTech(culture);
        return before.Count(id => !state.enacted_node_ids.Contains(id));
    }

    // 制度要的科技(节点自己写的 + 按特性挂的，见 TechnologySystem.GetInstitutionRequiredTechs)本文化有没有缺的；科技树里没有的 id 不算
    private static bool LacksTech(string culture, InstitutionNodeConfig node) =>
        node != null && TechnologySystem.GetInstitutionRequiredTechs(node)
            .Any(tech => TechnologySystem.TryGetTech(tech, out _) && !TechnologySystem.HasTech(culture, tech));

    // 回退记进本文化各帝国的史书(culture 为空：记进所有帝国)
    private static void Record(string culture, string text)
    {
        if (ModClass.EMPIRE_MANAGER == null || string.IsNullOrEmpty(text)) return;
        foreach (Layer.Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            if (culture != null && InstitutionSystem.GetCustomsCulture(empire) != culture) continue;
            EmpireCraft.Scripts.System.HistoryRecordSystem.RecordHistory(empire, directContent: text,
                kingdomId: empire.CoreKingdom?.id ?? -1L);
        }
    }

    private static bool Enqueue(City city, string culture)
    {
        if (!Jobs.TryGetValue(culture, out Job job) || !Queued.Add((city.data.id, culture))) return false;
        CityQueue.Enqueue((city, city.data.id, culture));
        job.Cities++;
        return true;
    }

    #endregion

    #region 建筑(逐帧)

    // 正在过的城(大城的建筑一帧过不完，下一帧接着过)
    private static City _city;
    private static string _cityCulture;
    private static Job _cityJob;
    private static List<Building> _buildings;
    private static int _index;

    // 每帧(见 ModClass.Update)：在时间片里一座座城过建筑，再试几处补盖；暂停时只过建筑，补盖等拆除动画走完
    public static void Tick()
    {
        if (_buildings == null && CityQueue.Count == 0 && Pending.Count == 0) return;
        if (World.world == null || !global::Config.game_loaded || SmoothLoader.isLoading()) return;
        using (FrameProfiler.Measure("后台·科技回退"))
        {
            var watch = global::System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < BudgetMs)
            {
                if (_buildings == null)
                {
                    if (CityQueue.Count == 0) break;
                    (City city, long id, string culture) = CityQueue.Dequeue();
                    Queued.Remove((id, culture));
                    if (!BeginCity(city, culture)) continue;
                }
                StepCity(watch);
            }
            if (global::Config.paused) return;
            _clock += Time.unscaledDeltaTime;
            int count = Math.Min(Pending.Count, PlacementsPerFrame);
            for (int i = 0; i < count && watch.ElapsedMilliseconds < BudgetMs * 2; i++)
            {
                Placement placement = Pending.Dequeue();
                if (placement.City?.data == null || placement.City.isRekt()) continue;
                bool placed;
                try
                {
                    placed = Place(placement);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][科技回退] 补盖建筑失败: {exception.Message}");
                    placed = true;
                }
                if (placed) continue;
                if (_clock - placement.Since < GiveUpSeconds) Pending.Enqueue(placement);
                else _lost++;
            }
            if (Pending.Count == 0 && _lost > 0)
            {
                LogService.LogInfo($"[EmpireCraft][科技回退] {_lost} 座降级的建筑没有地方补盖，已拆除(城市照科技自己再建)");
                _lost = 0;
            }
        }
    }

    // 开始过一座城；城没了、排队以后易手给了别的文化(照新主人的科技，不在这里动)就直接记过
    private static bool BeginCity(City city, string culture)
    {
        if (!Jobs.TryGetValue(culture ?? "", out Job job)) return false;
        if (city?.data == null || city.isRekt() || city.buildings == null ||
            !string.Equals(TechnologySystem.GetCultureOf(city), culture, StringComparison.Ordinal))
        {
            EndCity(null, job);
            return false;
        }
        _city = city;
        _cityCulture = culture;
        _cityJob = job;
        _buildings = new List<Building>(city.buildings);
        _index = 0;
        return true;
    }

    private static void StepCity(global::System.Diagnostics.Stopwatch watch)
    {
        try
        {
            if (_city?.data == null || _city.isRekt()) _index = _buildings.Count;
            while (_index < _buildings.Count && watch.ElapsedMilliseconds < BudgetMs)
                Downgrade(_city, _cityCulture, _buildings[_index++], _cityJob);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][科技回退] {_city?.data?.name} 的建筑降级失败: {exception.Message}");
            _index = _buildings.Count;
        }
        if (_index < _buildings.Count) return;
        City city = _city;
        Job job = _cityJob;
        _city = null;
        _cityCulture = null;
        _cityJob = null;
        _buildings = null;
        EndCity(city, job);
    }

    private static void EndCity(City city, Job job)
    {
        try
        {
            // 手动城区的城：一秒后对一次账，空出的建筑位补民居(见 CityZoningSystem.Sync)
            if (city?.data != null && !city.isRekt() && CityZoningSystem.Manual(city)) CityZoningSystem.Later(city);
        }
        finally
        {
            if (job != null && --job.Cities <= 0) Finish(job);
        }
    }

    // 一座建筑：要的科技本文化没有，就换成升级链上本文化能建的最高一级(没有就拆；民居、城心留着)
    private static void Downgrade(City city, string culture, Building building, Job job)
    {
        BuildingAsset asset = building?.asset;
        if (asset == null || building.isRekt() || building.isOnRemove() || building.isRuin()) return;
        string tech = TechnologySystem.GetRequiredTechForBuilding(asset.id);
        if (tech == null || TechnologySystem.HasTech(culture, tech)) return;
        // 城心的公所、篝火不动：拆了城就没有城心(rolled_back 记着，读档也不会按它补科技)
        if (asset.type == HallType || asset.type == BonfireType)
        {
            job.Kept++;
            return;
        }
        bool house = CityZoningSystem.IsHouse(asset);
        bool unfinished = building.isUnderConstruction();
        BuildingAsset lower = unfinished ? null : Lower(culture, asset);
        if (lower == null && house && !unfinished)
        {
            lower = CityZoningSystem.BestAsset(city, CityZoningSystem.HouseType);
            if (lower != null && lower.id == asset.id) lower = null;
        }
        if (lower == null && house && !unfinished)
        {
            // 没有能住的低一级民居：留着，不让城没房可住
            job.Kept++;
            return;
        }
        WorldTile origin = building.current_tile;
        // 民居先在同一块地上找空处盖低一级的、再拆旧的(住房不留空档)
        if (lower != null && house && origin?.zone != null && origin.zone.city == city && FindSpot(city, origin.zone, lower) is WorldTile spot &&
            Put(city, lower, spot))
        {
            Remove(city, building);
            job.Downgraded++;
            return;
        }
        Remove(city, building);
        if (lower != null && origin != null)
        {
            Pending.Enqueue(new Placement { City = city, Tile = origin, Asset = lower, Since = _clock });
            job.Downgraded++;
        }
        else job.Removed++;
    }

    private static void Remove(City city, Building building)
    {
        if (city.under_construction_building == building) city.under_construction_building = null;
        building.startRemove();
    }

    // 顺着升级链往下(谁升到它)找本文化能建的最高一级；要的科技本文化有、或不受科技管的才算
    private static BuildingAsset Lower(string culture, BuildingAsset asset)
    {
        for (int step = 0; asset != null && step < MaxChainSteps; step++)
        {
            asset = Below(asset);
            if (asset == null) return null;
            string tech = TechnologySystem.GetRequiredTechForBuilding(asset.id);
            if (tech == null || TechnologySystem.HasTech(culture, tech)) return asset;
        }
        return null;
    }

    // 升级链倒查表：升级目标 → 升到它的那一级(几座都升到同一座时取同类型、等级最高的)。资产数变了重建
    private static Dictionary<string, BuildingAsset> _below;
    private static int _belowCount = -1;

    private static BuildingAsset Below(BuildingAsset asset)
    {
        List<BuildingAsset> list = AssetManager.buildings?.list;
        if (list == null || asset?.id == null) return null;
        if (_below == null || _belowCount != list.Count)
        {
            _below = new Dictionary<string, BuildingAsset>(StringComparer.Ordinal);
            _belowCount = list.Count;
            foreach (BuildingAsset candidate in list)
            {
                if (string.IsNullOrEmpty(candidate?.id) || string.IsNullOrEmpty(candidate.upgrade_to) ||
                    candidate.upgrade_to == candidate.id) continue;
                if (_below.TryGetValue(candidate.upgrade_to, out BuildingAsset old) && !Prefer(candidate, old)) continue;
                _below[candidate.upgrade_to] = candidate;
            }
        }
        if (_below.TryGetValue(asset.id, out BuildingAsset below) && below != asset) return below;
        BuildingAsset from = string.IsNullOrEmpty(asset.upgraded_from) ? null : AssetManager.buildings.get(asset.upgraded_from);
        return from != null && from.id != asset.id && from.upgrade_to == asset.id ? from : null;
    }

    private static bool Prefer(BuildingAsset candidate, BuildingAsset old)
    {
        BuildingAsset target = AssetManager.buildings.get(candidate.upgrade_to);
        bool sameNew = target != null && candidate.type == target.type;
        bool sameOld = target != null && old.type == target.type;
        if (sameNew != sameOld) return sameNew;
        return candidate.upgrade_level > old.upgrade_level;
    }

    // 补盖：原处腾出来了就盖在原处；等了 SpotSeconds 还没腾出来，改在本块地、本城别的地块找地方(民居不去沙漠冻土)
    private static bool Place(Placement placement)
    {
        City city = placement.City;
        WorldTile tile = placement.Tile;
        if (tile?.zone != null && tile.zone.city == city && Fits(city, tile, placement.Asset))
            return Put(city, placement.Asset, tile);
        if (_clock - placement.Since < SpotSeconds) return false;
        bool house = CityZoningSystem.IsHouse(placement.Asset);
        if (tile?.zone != null && tile.zone.city == city && FindSpot(city, tile.zone, placement.Asset) is WorldTile near)
            return Put(city, placement.Asset, near);
        if (city.zones == null) return false;
        int tries = 0;
        foreach (TileZone zone in city.zones)
        {
            if (tries >= MaxZoneTries) break;
            if (zone == null || zone == tile?.zone || house && BarrenLandSystem.Barren(zone)) continue;
            tries++;
            if (FindSpot(city, zone, placement.Asset) is WorldTile spot) return Put(city, placement.Asset, spot);
        }
        return false;
    }

    // 同 CityZoningSystem 的放置：格子放得下、原版允许本城在这里建
    private static bool Fits(City city, WorldTile tile, BuildingAsset asset) =>
        tile?.Type != null && tile.canBuildOn(asset) && World.world.buildings.canBuildFrom(tile, asset, city);

    private static WorldTile FindSpot(City city, TileZone zone, BuildingAsset asset)
    {
        if (zone?.tiles == null || asset == null) return null;
        foreach (WorldTile tile in zone.tiles)
            if (Fits(city, tile, asset)) return tile;
        return null;
    }

    // 当场盖一座建成的(同 CityZoningSystem.Put)
    private static bool Put(City city, BuildingAsset asset, WorldTile tile)
    {
        Building building = World.world.buildings.addBuilding(asset, tile);
        if (building?.asset == null) return false;
        if (building.isUnderConstruction())
        {
            building.updateBuild(Mathf.Max(1, building.asset.construction_progress_needed + 1 - building.getConstructionProgress()));
            if (city.under_construction_building == building) city.under_construction_building = null;
        }
        return true;
    }

    #endregion

    #region 汇总

    private static void Finish(Job job)
    {
        if (job == null || !Jobs.TryGetValue(job.Culture, out Job current) || current != job) return;
        Jobs.Remove(job.Culture);
        LogService.LogInfo($"[EmpireCraft][科技回退] {(job.World != null ? "举世退回原始 · " : "")}文化 {job.Culture}：" +
                           $"时代 {job.FromEra}({TechnologySystem.EraName(job.FromEra)}) → {job.ToEra}({TechnologySystem.EraName(job.ToEra)})，" +
                           $"删科技 {job.Techs}、忘材料 {job.Materials}、撤制度 {job.Institutions}、降级建筑 {job.Downgraded}" +
                           $"(另拆 {job.Removed}、留 {job.Kept})");
        WorldJob world = job.World;
        if (world == null) return;
        world.Downgraded += job.Downgraded;
        world.Removed += job.Removed;
        world.Kept += job.Kept;
        if (--world.Open <= 0) FinishWorld(world);
    }

    private static void FinishWorld(WorldJob world)
    {
        if (world.Open < 0) return;
        world.Open = -1;
        LogService.LogInfo($"[EmpireCraft][科技回退] 举世退回原始：{world.Cultures} 个文化，删科技 {world.Techs}、撤制度 {world.Institutions}、" +
                           $"降级建筑 {world.Downgraded}(另拆 {world.Removed}、留 {world.Kept})");
    }

    #endregion
}
