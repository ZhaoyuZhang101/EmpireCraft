using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using HarmonyLib;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城市人口数据层(无小人模式第一步)。
//
// 人口组保存在每座城市的存档数据里(CityExtraData.population)，按"阶层 × 文化 × 物种 × 理念"分组记人数。
// 每组人数 = 背景人口 + 实体单位：
//   - 实体单位照常由原版生成时，背景人口为 0，每年用城里的单位重新校准，数据层就是单位统计的镜像；
//   - 开启无小人模式后(第三步做成世界法则)，背景人口不再被校准覆盖，而是按住房容量自行增减；
//     实体单位只保留国王、城主、军人、官员等"名人"，校准时只重数这些名人，背景人口保持不变。
//
// 年度结算分帧进行：到期时把全部城市排进队列，之后每帧最多处理 SliceBudgetMs 毫秒，剩下的下一帧接着做。
// 第二步会把土地、就业、理念、政党投票等统计改为读这里的数据，而不是遍历城里的单位。
public static class CityPopulationSystem
{
    private const double SliceBudgetMs = 2d;
    // ---- 背景人口的生育与死亡(每年) ----
    // 基础出生率/死亡率：吃得饱、住得下、不打仗时，每年自然增长约 8%(和原版新文明的扩张速度相当)
    private const float BaseBirthRate = 0.1f;
    private const float BaseDeathRate = 0.02f;
    // 人均存粮达到这个值算"吃得饱"；低于 FamineFoodPerCapita 发生饥荒
    private const float ComfortFoodPerCapita = 1f;
    private const float FamineFoodPerCapita = 0.2f;
    private const float FamineDeathRate = 0.08f;
    // 粮食越富余生得越多，最多到基础出生率的 1.5 倍
    private const float MaxFoodBirthFactor = 1.5f;
    // 经济越繁荣(商人、市民多)：死亡率下降，生育意愿也下降(人口转型)
    private const float ProsperityDeathReduction = 0.5f;
    private const float ProsperityBirthReduction = 0.4f;
    // 城里有敌军占领的地块(战乱)时额外的死亡率
    private const float WarDeathRate = 0.04f;
    // 住房剩下不到两成时出生率开始按比例下降，住满则不再出生
    private const float HousingSlowdownShare = 0.2f;
    // 拥挤流失和最低迁入的纯数值公式在 PopulationMathWorkers 中计算。

    // ---- 人口规模(无小人模式) ----
    // 原版一个住房位住一个单位；没有实体单位后，一个住房位代表一户聚居的人：古代 100 人，现代 1000 人，
    // 大城市可以到几十万、几百万。原版的岗位、兵额、仓库这些机制仍按"户"(人数 ÷ 每位人数)运行，
    // 免得原版按人头循环、按人头征兵时数字爆掉(见 Households)
    public const int PremodernPeoplePerSlot = 100;
    public const int ModernPeoplePerSlot = 1000;
    // 征召兵：一个实体士兵代表一个军团，最多 PeoplePerLegion 人；交战时最多征召背景人口的 MaxLevyShare
    public const float PeoplePerLegion = 10000f;
    private const float MaxLevyShare = 0.1f;
    #region 军制(无小人模式)：军队驻在哪里、平时养多少、战时征多少

    // 不是每座城都有兵。军队只驻在"军镇"，兵源从全国(本王国各城)的人口里按人口多少分摊：
    //   中央：王国首都(帝国为京师)；
    //   军区：帝国内各藩属王国(行省、节度使)的首都；
    //   军府：府兵制下，除首都外按人口挑出约三分之一的城设军府。
    // 驻在哪里、平时是否常备由制度决定(制度特性，见 InstitutionTrees)：
    //   默认(封建、部族征召)：领主各自在首都征兵，仗打完就解散，平时只留一名将领；
    //   army_prefecture 府兵制：军府平时轮番宿卫(各留一人)，战时各军府出兵；
    //   army_standing 常备军(募兵、马穆鲁克、怯薛)：中央常年养着一支职业军队；
    //   army_regional 地方军(藩镇、十户制万户)：军区常年驻军且兵多，中央相对弱；
    //   army_mobilization：战时可征召的人口比例(十户制全民皆兵)；
    //   现代国家：中央与军区都有常备军(国防军、军区)，战时总动员。
    public enum ArmyPost { None, Central, District, Prefecture }

    public readonly struct ArmyDoctrine
    {
        public readonly bool Prefecture, CentralStanding, DistrictStanding, Regional;
        public readonly float LevyShare;

        public ArmyDoctrine(bool prefecture, bool centralStanding, bool districtStanding, bool regional, float levyShare)
        {
            Prefecture = prefecture;
            CentralStanding = centralStanding;
            DistrictStanding = districtStanding;
            Regional = regional;
            LevyShare = levyShare;
        }
    }

    public const string FeaturePrefecture = "army_prefecture";
    public const string FeatureStanding = "army_standing";
    public const string FeatureRegional = "army_regional";
    public const string FeatureMobilization = "army_mobilization";
    private const float ModernLevyShare = 0.15f;
    // 战时兵额(实体士兵个数)按本王国户数折算；上限是为了全图实体单位数可控(帧数)
    private const float WarLegionsPerHousehold = 0.3f;
    private const float PrefectureLegionsPerHousehold = 0.5f;
    private const float StandingLegionsPerHousehold = 0.05f;

    // 军制按王国所属的帝国(没有帝国就是王国自己)的主文化制度来定：同一个帝国的中央与各军区用同一套军制
    public static ArmyDoctrine DoctrineOf(Kingdom kingdom)
    {
        if (kingdom == null) return ComputeDoctrine(null);
        EnsureFrameCache();
        if (DoctrineCache.TryGetValue(kingdom, out ArmyDoctrine cached)) return cached;
        ArmyDoctrine doctrine = ComputeDoctrine(kingdom);
        DoctrineCache[kingdom] = doctrine;
        return doctrine;
    }

    private static ArmyDoctrine ComputeDoctrine(Kingdom kingdom)
    {
        Kingdom realm = kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;
        string culture = realm == null ? null : CultureService.GetRealmCulture(realm);
        bool valid = CultureService.IsValidCulture(culture);
        bool prefecture = valid && InstitutionSystem.HasFeature(culture, FeaturePrefecture);
        bool standing = valid && InstitutionSystem.HasFeature(culture, FeatureStanding);
        bool regional = valid && InstitutionSystem.HasFeature(culture, FeatureRegional);
        float share = valid ? InstitutionSystem.GetFeature(culture, FeatureMobilization) : 0f;
        if (share <= 0f) share = MaxLevyShare;
        if (ModernStability.IsModern(kingdom))
            return new ArmyDoctrine(false, true, true, regional, Mathf.Max(share, ModernLevyShare));
        // 募兵制取代府兵制：有了职业常备军，军府不再出兵
        return new ArmyDoctrine(prefecture && !standing, standing, regional, regional, share);
    }

    private static readonly Dictionary<Kingdom, (double at, HashSet<City> cities)> PrefectureCache = new();

    // 军府：除首都外，按户数从多到少挑出约三分之一的城(每个游戏年重选一次)
    private static bool IsPrefecture(City city)
    {
        Kingdom kingdom = city.kingdom;
        if (kingdom?.cities == null) return false;
        double now = World.world?.getCurWorldTime() ?? 0d;
        if (!PrefectureCache.TryGetValue(kingdom, out var cached) || now < cached.at || Date.getYearsSince(cached.at) >= 1)
        {
            List<City> others = kingdom.cities.Where(other => other != null && !other.isRekt() && other != kingdom.capital)
                .OrderByDescending(Households).ToList();
            cached = (now, new HashSet<City>(others.Take(Mathf.CeilToInt(others.Count / 3f))));
            PrefectureCache[kingdom] = cached;
        }
        return cached.cities.Contains(city);
    }

    public static ArmyPost PostOf(City city, ArmyDoctrine doctrine)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.wild) return ArmyPost.None;
        if (city == kingdom.capital)
            return kingdom.IsInEmpire() && !kingdom.IsEmpire() ? ArmyPost.District : ArmyPost.Central;
        return doctrine.Prefecture && IsPrefecture(city) ? ArmyPost.Prefecture : ArmyPost.None;
    }

    // 这座城现在应有的兵额(实体士兵个数，每个代表一个军团)：不是军镇为 0；国库亏空发不出饷时只留将领
    public static int LevyTarget(City city) => LevyTarget(city, false);

    // forceWar：按战时兵额算(不管现在是否在打仗)
    public static int LevyTarget(City city, bool forceWar)
    {
        if (!AbstractPopulationEnabled || city?.kingdom == null) return 0;
        ArmyDoctrine doctrine = DoctrineOf(city.kingdom);
        ArmyPost post = PostOf(city, doctrine);
        int guards = CityStabilitySystem.FundedSlots(city);
        if (post == ArmyPost.None) return guards;
        if (EmpireBankruptcySystem.IsUnpaidGarrison(city)) return 1;
        int households = Households(city.kingdom);
        if (!forceWar && !OnWarFooting(city))
        {
            bool standing = post == ArmyPost.Central ? doctrine.CentralStanding && !doctrine.Regional ||
                                                       doctrine.CentralStanding && doctrine.DistrictStanding
                : post == ArmyPost.District && doctrine.DistrictStanding;
            if (!standing) return Math.Max(1, guards);
            int max = post == ArmyPost.District && doctrine.Regional ? 10 : 12;
            return Math.Max(guards, Mathf.Clamp(Mathf.CeilToInt(households * StandingLegionsPerHousehold), 2, max));
        }
        switch (post)
        {
            case ArmyPost.Central:
                // 地方军强、中央弱(藩镇)：中央的战时兵额减半
                return doctrine.Regional && !doctrine.CentralStanding
                    ? Mathf.Clamp(Mathf.CeilToInt(households * WarLegionsPerHousehold * 0.5f), 3, 20)
                    : Mathf.Clamp(Mathf.CeilToInt(households * WarLegionsPerHousehold), 5, 40);
            case ArmyPost.District:
                return doctrine.Regional
                    ? Mathf.Clamp(Mathf.CeilToInt(households * WarLegionsPerHousehold * 1.3f), 4, 40)
                    : Mathf.Clamp(Mathf.CeilToInt(households * WarLegionsPerHousehold), 3, 30);
            default:
                return Mathf.Clamp(Mathf.CeilToInt(Households(city) * PrefectureLegionsPerHousehold), 2, 15);
        }
    }

    // 战备状态：已经开战，或者本国(帝国则为整个帝国)正在谋划发动战争。谋划期间各军镇就按战时兵额征兵集结，
    // 谋划完成宣战时大军已在城下——"陈兵百万，一举灭敌"。被攻击的一方事先不知情，宣战后才开始动员
    private static readonly string[] WarPlots = { "new_war", "empirecraft_war" };
    private static readonly HashSet<Kingdom> Mobilizing = new();
    // 谋划开战的目标国(兵往哪一侧的边境集结)
    private static readonly Dictionary<Kingdom, Kingdom> MobilizationTargets = new();
    private static double _mobilizingAt = -1d;

    public static bool OnWarFooting(City city) => IsAtWar(city) || IsMobilizing(city?.kingdom);

    public static bool IsMobilizing(Kingdom kingdom)
    {
        if (kingdom == null || World.world == null) return false;
        double now = World.world.getCurWorldTime();
        // 每秒(世界时间)重算一次
        if (_mobilizingAt < 0d || now < _mobilizingAt || now - _mobilizingAt >= 1d)
        {
            _mobilizingAt = now;
            Mobilizing.Clear();
            MobilizationTargets.Clear();
            try
            {
                foreach (Plot plot in World.world.plots)
                {
                    if (plot == null || !plot.isActive()) continue;
                    string id = plot.getAsset()?.id;
                    if (id == null || Array.IndexOf(WarPlots, id) < 0) continue;
                    Kingdom author = plot.getAuthor()?.kingdom;
                    if (author == null) continue;
                    Kingdom target = plot.target_kingdom;
                    Mobilizing.Add(author);
                    if (target != null) MobilizationTargets[author] = target;
                    // 帝国核心谋划开战：整个帝国(中央与各军区)一起动员
                    Layer.Empire empire = author.IsEmpire() ? author.GetEmpire() : null;
                    if (empire?.kingdoms_list != null)
                        foreach (Kingdom member in empire.kingdoms_list)
                        {
                            if (member == null) continue;
                            Mobilizing.Add(member);
                            if (target != null) MobilizationTargets[member] = target;
                        }
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 无小人模式战备检查失败: {exception.Message}");
            }
        }
        return Mobilizing.Contains(kingdom);
    }

    // ---- 陈兵边境 ----
    // 动员的兵直接生成在本国朝向敌国的边境上(KingdomFrontLineHelper 算出的前沿地块)，
    // 战备期间各军镇的军团统领每月开往边境，原版士兵跟着统领走
    private static readonly Dictionary<Kingdom, (double at, List<TileZone> zones)> FrontCache = new();

    // 兵锋所向：谋划开战的目标国，其次是正在交战的主要敌国
    public static Kingdom EnemyOf(Kingdom kingdom)
    {
        if (kingdom == null) return null;
        IsMobilizing(kingdom);
        if (MobilizationTargets.TryGetValue(kingdom, out Kingdom target) && target != null && !target.isRekt())
            return target;
        Kingdom realm = kingdom.GetEmpire()?.CoreKingdom ?? kingdom;
        foreach (Kingdom side in new[] { kingdom, realm })
        {
            foreach (War war in side.getWars())
            {
                if (war == null || war.hasEnded()) continue;
                Kingdom enemy = war.main_attacker == side ? war.main_defender : war.main_attacker;
                if (enemy != null && !enemy.isRekt() && enemy != kingdom) return enemy;
            }
        }
        return null;
    }

    // 本国朝向敌国的边境上的一个地块；没有接壤或算不出来时返回 null
    public static WorldTile FrontTile(Kingdom kingdom)
    {
        Kingdom enemy = EnemyOf(kingdom);
        if (enemy == null || World.world == null) return null;
        double now = World.world.getCurWorldTime();
        if (!FrontCache.TryGetValue(kingdom, out var cached) || now < cached.at || now - cached.at >= 5d)
        {
            List<TileZone> zones;
            try
            {
                zones = KingdomFrontLineHelper.GetFriendlyFrontSourceZonesFacingEnemy(kingdom, enemy)
                    .Where(KingdomFrontLineHelper.IsValidZone).ToList();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 无小人模式边境计算失败: {exception.Message}");
                zones = new List<TileZone>();
            }
            cached = (now, zones);
            FrontCache[kingdom] = cached;
        }
        if (cached.zones.Count == 0) return null;
        return cached.zones[UnityEngine.Random.Range(0, cached.zones.Count)]?.centerTile;
    }

    // 战备期间：军镇的军团统领开往边境(已经在边境附近的不动)
    private const int FrontHoldDistance = 20;

    public static void MarchToFront(City city)
    {
        if (!AbstractPopulationEnabled || city?.army == null || !OnWarFooting(city)) return;
        Actor captain = city.army.getCaptain();
        if (captain == null || captain.isRekt() || !captain.isAlive() || captain.current_tile == null) return;
        WorldTile front = FrontTile(city.kingdom);
        if (front == null) return;
        if (Toolbox.SquaredDistTile(captain.current_tile, front) <= FrontHoldDistance * FrontHoldDistance) return;
        try
        {
            captain.goTo(front);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式开往边境失败: {exception.Message}");
        }
    }

    // 本王国各城背景人口之和(兵源池)
    private static float KingdomBackground(Kingdom kingdom)
    {
        float total = 0f;
        if (kingdom?.cities == null) return total;
        foreach (City city in kingdom.cities)
            if (city != null && !city.isRekt()) total += GetBackgroundTotal(city);
        return total;
    }

    // 按背景人口加权抽一座本国城市(兵源从哪座城出、退伍回哪座城)
    private static City DrawKingdomCity(Kingdom kingdom)
    {
        if (kingdom?.cities == null) return null;
        float total = 0f;
        City chosen = null;
        // 加权蓄水池抽样：保持相同分布，只遍历一次，不再先统计全国再重复读各城人口组。
        foreach (City city in kingdom.cities)
        {
            if (city == null || city.isRekt()) continue;
            float weight = GetBackgroundTotal(city);
            if (weight <= 0f) continue;
            total += weight;
            if (UnityEngine.Random.value * total < weight) chosen = city;
        }
        return chosen;
    }

    #endregion

    // ---- 并入普通人(无小人模式) ----
    // 每座城保留的普通劳动者。生产与施工已由人口经济按数据结算(见 PopulationEconomySystem)，不再留"没用的人"；
    // 军人另算：太平时每城一名将领，交战时按兵额征召；官职、城主空缺时按需从人口中生成(见 SpawnForOffice)
    public const int KeptWorkersPerCity = 0;
    // 并入检查的间隔(游戏月)
    private const int FoldIntervalMonths = 1;
    // 人数低于这个值的人口组在结算后删除，避免碎片组越积越多
    private const float MinimumGroupSize = 0.05f;
    // 背景人口每年向本城同阶层名人的理念分布靠拢的比例：名人(有实体的单位)照常受交往、党派、
    // 战争等逐人模拟影响，背景人口以他们为样本跟随思潮变化
    private const float BackgroundOpinionDrift = 0.3f;

    private static readonly Queue<City> PendingCities = new();
    private static readonly Queue<City> PendingFolds = new();
    private static City _foldCity;
    private static Kingdom _foldOwner;
    private static int _foldPhase;
    private static double _foldNow;
    private static float _foldHarvest;
    private static bool _foldEconomyDue;
    private static bool _foldsFirst;
    private static bool _waitingForMath;
    private static double _lastFoldPass = -1d;
    private static object _world;
    private static double _lastPass = -1d;
    private static bool? _populationMode;

    // 无小人模式开关(世界法则)。关闭时背景人口始终为 0，数据层只是实体单位的统计镜像
    public static bool AbstractPopulationEnabled =>
        EmpireCraft.Scripts.GameLibrary.EmpireCraftWorldLawLibrary.empirecraft_law_no_commoners?.isEnabled() == true;

    // 最近一次年度结算的统计，用于日志和性能验证
    public static int LastPassCities { get; private set; }
    public static double LastPassMilliseconds { get; private set; }
    private static double _passMilliseconds;
    private static int _passCities;

    public static void ResetWorldState()
    {
        ExclaveMaintenanceSystem.ResetWorldState();
        StateSettlementSystem.ResetWorldState();
        PopulationParallelSystem.ResetWorldState();
        ResetClassification();
        LeaderRetryAt.Clear();
        GranarySystem.ResetWorldState();
        MarketSystem.ResetWorldState();
        PrefectureCache.Clear();
        _frameCacheFrame = -1;
        FrontCache.Clear();
        MobilizationTargets.Clear();
        Mobilizing.Clear();
        _mobilizingAt = -1d;
        PendingCities.Clear();
        PendingFolds.Clear();
        _foldCity = null;
        _foldOwner = null;
        _foldPhase = 0;
        _world = World.world;
        _lastPass = -1d;
        _lastFoldPass = -1d;
        _populationMode = null;
        _homelessRemaining = _homelessCursor = _homelessFolded = 0;
        EmpireCraft.Scripts.GamePatches.NoCommonersPatch.ResetPopulationIndex();
        VirtualGenealogySystem.ResetWorldState();
        _passMilliseconds = 0d;
        _passCities = 0;
    }

    public static CityPopulationData Get(City city)
    {
        if (city?.data == null) return null;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        extra.population ??= new CityPopulationData();
        extra.population.groups ??= new List<PopGroup>();
        return extra.population;
    }

    #region 年度结算

    // 每帧调用一次(ModClass.Update)
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities == null || !Config.game_loaded || Config.paused || SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_world, world)) ResetWorldState();

        double now = world.getCurWorldTime();
        bool enabled = AbstractPopulationEnabled;
        bool changed = _populationMode.HasValue && _populationMode.Value != enabled;
        if (changed || !_populationMode.HasValue)
        {
            if (changed) ExclaveMaintenanceSystem.ModeChanged();
            if (changed || !enabled) StateSettlementSystem.ModeChanged(enabled);
            foreach (City city in world.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                CityPopulationData population = Get(city);
                PopulationSettlementRules.SynchronizeMode(population, enabled, now, changed);
                if (changed)
                {
                    population.economy_periods?.Clear();
                    population.last_income = population.last_tax_income = population.last_consumption = 0f;
                    population.other_background_tax = 0f;
                }
            }
            if (changed)
            {
                EmpireCraft.Scripts.GamePatches.NoCommonersPatch.ResetPopulationIndex();
                PopulationParallelSystem.ResetWorldState();
                ResetClassification();
                _homelessRemaining = _homelessCursor = _homelessFolded = 0;
                SimulationFrameBudget.Reset();
                AnimalHusbandrySystem.ResetWorldState();
                PendingFolds.Clear(); _foldCity = null; _foldOwner = null; _lastFoldPass = -1d;
            }
            _populationMode = enabled;
        }
        // 虚拟族人的年度身故：关掉无小人模式后已有的虚拟族人仍会老去
        _foldsFirst = !_foldsFirst;
        if (AbstractPopulationEnabled && _foldsFirst) TickFolds(world, now);
        if (SimulationFrameBudget.HasTime) VirtualGenealogySystem.Tick();
        if (AbstractPopulationEnabled)
        {
            if (!_foldsFirst && SimulationFrameBudget.HasTime) TickFolds(world, now);
        }
        else { PendingFolds.Clear(); _foldCity = null; _foldOwner = null; }
        if (enabled && SimulationFrameBudget.HasTime) TickHomeless();
        if (PendingCities.Count == 0)
        {
            bool due = _lastPass < 0d || now < _lastPass || Date.getYearsSince(_lastPass) >= 1;
            if (!due) return;
            _lastPass = now;
            _passMilliseconds = 0d;
            _passCities = 0;
            foreach (City city in world.cities)
                if (city?.data != null && !city.isRekt()) PendingCities.Enqueue(city);
            if (PendingCities.Count == 0) return;
        }

        using var timing = new PerfTimer("城市人口数据层年度结算");
        Stopwatch watch = Stopwatch.StartNew();
        // 每帧至少校准一座城：月度结算把每帧预算用光时，年度校准也不会被一直饿着(以前一年都跑不完)
        int calibrated = 0;
        while (PendingCities.Count > 0 && (calibrated == 0 ||
                                           watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime))
        {
            calibrated++;
            City city = PendingCities.Dequeue();
            if (city?.data == null || city.isRekt()) continue;
            try
            {
                UpdateCity(city, now);
                _passCities++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 城市人口数据层结算失败({city.data?.name}): {exception.Message}");
            }
        }
        _passMilliseconds += watch.Elapsed.TotalMilliseconds;
        if (PendingCities.Count == 0)
        {
            LastPassCities = _passCities;
            LastPassMilliseconds = _passMilliseconds;
            LogService.LogInfo($"[EmpireCraft][人口数据层] 本年校准 {LastPassCities} 座城市，共耗时 {LastPassMilliseconds:0.0} ms(分帧，每帧上限 {SliceBudgetMs} ms)");
            FoldHomeless();
            // 全世界内存普查只在调试时主动调用，避免年度分帧结束后再集中扫描一遍所有单位与族谱。
            // 人口诊断只遍历城市，开销小，每年记一次
            LogPopulationDiagnosis();
        }
    }

    // 流民：属于某个文明国家、却不属于任何城市的普通人和散兵(城破逃散、迁徙落空、军团解散后留在野外的)。
    // 原版只有城市会收拢自己的人，这些人在无小人模式下永远留在地图上越积越多；
    // 每年一次把它们并回本国随机一城的背景人口(名人照旧保留)
    private const int MaxHomelessFoldsPerYear = 400;
    private static int _homelessRemaining, _homelessCursor, _homelessFolded;

    private static void FoldHomeless()
    {
        if (!AbstractPopulationEnabled || World.world?.units == null) return;
        // 年度结算只排队，不在同一帧扫描全世界并连做四百次身故/族谱回调。
        if (_homelessRemaining > 0) return;
        _homelessRemaining = World.world.units.Count;
        _homelessFolded = 0;
    }

    private static void TickHomeless()
    {
        if (!AbstractPopulationEnabled || _homelessRemaining <= 0 || World.world?.units == null) return;
        using var timing = FrameProfiler.Measure("虚拟人口·流民收拢");
        var actors = World.world.units.getSimpleList();
        int scanned = 0, folded = 0;
        while (_homelessRemaining > 0 && actors.Count > 0 && scanned < 128 && folded < 4 &&
               (scanned == 0 || SimulationFrameBudget.HasTime))
        {
            if (_homelessFolded >= MaxHomelessFoldsPerYear) break;
            if (_homelessCursor >= actors.Count) _homelessCursor = 0;
            Actor actor = actors[_homelessCursor++];
            _homelessRemaining--;
            scanned++;
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.city != null) continue;
            if (actor.asset == null || actor.asset.is_boat || !actor.asset.civ) continue;
            Kingdom kingdom = actor.kingdom;
            if (kingdom == null || kingdom.wild || kingdom.isRekt() || kingdom.cities.Count == 0) continue;
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(actor)) continue;
            if (actor.is_army_captain || actor.army != null) continue;
            if (IsNotable(actor)) continue;
            try
            {
                City home = DrawKingdomCity(actor.kingdom);
                if (home == null) continue;
                FoldIntoPopulation(actor, home);
                folded++;
                _homelessFolded++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 无小人模式收拢流民失败: {exception.Message}");
            }
        }
        if (_homelessRemaining <= 0 || actors.Count == 0 || _homelessFolded >= MaxHomelessFoldsPerYear)
        {
            _homelessRemaining = 0;
            if (_homelessFolded > 0)
                LogService.LogInfo($"[EmpireCraft][无小人模式] 分帧收拢流民 {_homelessFolded} 人并入背景人口");
        }
    }

    // 内存普查用：实体单位按"为什么还留在地图上"分类(按 IsNotable 的判断顺序取第一条)
    private static string UnitCategory(Actor actor)
    {
        if (actor.asset == null || !actor.asset.civ) return "非文明生物";
        if (actor.kingdom == null || actor.kingdom.wild) return "无国";
        if (actor.city == null) return IsSoldier(actor) ? "无城士兵" : IsNotable(actor) ? "无城名人" : "流民";
        if (actor.isKing()) return "君主";
        if (actor.isCityLeader()) return "城主";
        if (IsSoldier(actor)) return "士兵";
        if (actor.isFavorite() || actor.isCameraFollowingUnit()) return "收藏";
        if (actor.plot != null) return "谋划";
        SocialClass socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
        if (socialClass == SocialClass.Officer) return "官员";
        if (socialClass == SocialClass.Landlord) return "地主";
        if (socialClass == SocialClass.Noble && IsImportantNoble(actor)) return "贵族";
        if (actor.GetFaction() != null) return "派系";
        if (actor.hasTrait("gongshi") || actor.hasTrait("jingshi") || actor.hasTrait("juren")) return "士人";
        if (VirtualGenealogySystem.IsClanHead(actor)) return "族长";
        return "普通人";
    }

    // 内存普查：每年记一次只增不减的几类对象有多少，找出内存膨胀的来源
    private static void LogMemoryCensus()
    {
        try
        {
            int persons = EmpireCraft.Scripts.System.SpecificClanManager._globalPersonLookup.Count, alive = 0, virtuals = 0, histories = 0;
            foreach (EmpireCraft.Scripts.System.PersonalClanIdentity person in EmpireCraft.Scripts.System.SpecificClanManager._globalPersonLookup.Values)
            {
                if (person == null) continue;
                if (person.is_alive) alive++;
                if (person.is_virtual) virtuals++;
                histories += person.personal_history?.Count ?? 0;
            }
            int groups = 0;
            foreach (City city in World.world.cities) groups += Get(city)?.groups?.Count ?? 0;
            var categories = new Dictionary<string, int>();
            foreach (Actor actor in World.world.units)
            {
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                string category = UnitCategory(actor);
                categories[category] = categories.TryGetValue(category, out int n) ? n + 1 : 1;
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in categories.OrderByDescending(pair => pair.Value))
                parts.Add($"{pair.Key} {pair.Value}");
            var wild = new Dictionary<string, int>();
            foreach (Actor actor in World.world.units)
                if (actor?.asset != null && !actor.asset.civ && actor.isAlive())
                    wild[actor.asset.id] = wild.TryGetValue(actor.asset.id, out int w) ? w + 1 : 1;
            var wildParts = new List<string>();
            foreach (KeyValuePair<string, int> pair in wild.OrderByDescending(pair => pair.Value).Take(6))
                wildParts.Add($"{pair.Key} {pair.Value}");
            parts.Add("非文明生物前几位：" + string.Join("、", wildParts));
            int wheat = 0;
            foreach (Building building in World.world.buildings)
                if (building?.asset != null && building.asset.wheat) wheat++;
            long mono = GC.GetTotalMemory(false) / (1024 * 1024);
            LogService.LogInfo($"[EmpireCraft][内存普查] 托管内存 {mono} MB；宗族 {EmpireCraft.Scripts.System.SpecificClanManager._specificClans.Count}，" +
                               $"族谱身份 {persons}(在世 {alive}，虚拟 {virtuals})，个人经历 {histories} 条；" +
                               $"原版氏族 {World.world.clans.Count}，家庭 {World.world.families.Count}，书 {World.world.books.Count}，" +
                               $"单位 {World.world.units.Count}，城市 {World.world.cities.Count}，人口组 {groups}；" +
                               $"建筑 {World.world.buildings.Count}(庄稼 {wheat})；单位构成：{string.Join("，", parts)}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 内存普查失败: {exception.Message}");
        }
        LogPopulationDiagnosis();
    }

    // 人口诊断(每年一次，跟内存普查一起)：全世界人口与住房上限、饥荒城数、超员城数、平均出生死亡率，
    // 以及人口最少的几座城各卡在哪一项，用来找"城市人口上不去"的原因
    private static void LogPopulationDiagnosis()
    {
        if (!AbstractPopulationEnabled) return;
        try
        {
            int cities = 0, famine = 0, crowded = 0, war = 0;
            double total = 0d, capacity = 0d, birth = 0d, death = 0d, satisfaction = 0d;
            int satisfied = 0;
            var smallest = new List<(float total, string line)>();
            foreach (City city in World.world.cities)
            {
                if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom.wild) continue;
                CityPopulationData data = Get(city);
                if (data == null) continue;
                GrowthFactors f = GetGrowthFactors(city, data);
                cities++;
                total += f.Total;
                capacity += f.Capacity;
                birth += f.BirthRate;
                death += f.DeathRate;
                if (f.Famine) famine++;
                if (f.War) war++;
                if (f.Total >= f.Capacity * 0.95f) crowded++;
                float sat = PopulationSettlementRules.FoodSatisfaction(data);
                if (sat >= 0f)
                {
                    satisfaction += sat;
                    satisfied++;
                }
                smallest.Add((f.Total, $"{city.data.name} 人口 {f.Total:0} / 上限 {f.Capacity:0}，存粮 {f.FoodPerCapita:0.0}/户，" +
                                       $"吃饱 {(sat < 0f ? "-" : sat.ToString("P0"))}，生 {f.BirthRate:P1} 死 {f.DeathRate:P1}" +
                                       $"{(f.Famine ? " 饥荒" : "")}{(f.War ? " 战乱" : "")}，废墟 {CityConstructionSystem.CountRuins(city)}"));
            }
            if (cities == 0) return;
            smallest.Sort((a, b) => a.total.CompareTo(b.total));
            var worst = new List<string>();
            for (int i = 0; i < Mathf.Min(5, smallest.Count); i++) worst.Add(smallest[i].line);
            LogService.LogInfo($"[EmpireCraft][人口诊断] {cities} 城，总人口 {total:0}，住房上限合计 {capacity:0}；" +
                               $"饥荒 {famine} 城，战乱 {war} 城，住满 {crowded} 城；" +
                               $"平均出生 {birth / cities:P1} 死亡 {death / cities:P1}，平均吃饱 " +
                               $"{(satisfied == 0 ? "-" : (satisfaction / satisfied).ToString("P0"))}；" +
                               CityConstructionSystem.TakeStats() + "。最小的城：" +
                               string.Join("；", worst));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 人口诊断失败: {exception.Message}");
        }
    }

    // 立即结算一座城市(界面打开、读档修复等需要最新数据的场合)
    public static void UpdateCity(City city, double now)
    {
        CityPopulationData data = Get(city);
        if (data == null) return;
        // 背景人口始终保留：关掉开关时只是冻结(不增减、不计入统计)，重新打开后接着用
        Census(city, data, keepBackground: true);
        if (AbstractPopulationEnabled) DriftBackgroundTowardNamed(data, BackgroundOpinionDrift);
        data.last_census = now;
    }

    // 用城里的实体单位校准人口组。keepBackground 为 false 时丢弃背景人口，人口组完全等于单位统计；
    // 为 true 时保留每组的背景人口，只把实体单位部分换成这次数到的人数
    public static void Census(City city, CityPopulationData data, bool keepBackground, bool preserveSmallGroups = false)
    {
        var counted = new Dictionary<(SocialClass, string, string, PartyIdeology), int>();
        int named = 0;
        if (city.units != null)
        {
            for (int i = 0; i < city.units.Count; i++)
            {
                Actor actor = city.units[i];
                if (actor?.data == null || actor.isRekt() || !actor.isAlive() || IsVehicle(actor)) continue;
                SocialClass socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
                actor.SetSocialClass(socialClass);
                var key = (socialClass, CultureService.GetActorCulture(actor) ?? "", actor.asset?.id ?? "",
                    IdeologyPopulationSystem.Get(actor));
                counted.TryGetValue(key, out int count);
                counted[key] = count + 1;
                named++;
            }
        }

        var rebuilt = new List<PopGroup>();
        if (keepBackground)
        {
            foreach (PopGroup group in data.groups)
            {
                float background = group.Background;
                if (background < MinimumGroupSize && (!preserveSmallGroups || background <= 0f)) continue;
                rebuilt.Add(new PopGroup
                {
                    social_class = group.social_class, culture = group.culture ?? "", species = group.species ?? "",
                    ideology = group.ideology, size = background, named = 0
                });
            }
        }
        foreach (KeyValuePair<(SocialClass, string, string, PartyIdeology), int> pair in counted)
        {
            (SocialClass socialClass, string culture, string species, PartyIdeology ideology) = pair.Key;
            PopGroup group = Find(rebuilt, socialClass, culture, species, ideology);
            if (group == null)
            {
                group = new PopGroup
                {
                    social_class = socialClass, culture = culture, species = species, ideology = ideology
                };
                rebuilt.Add(group);
            }
            group.size += pair.Value;
            group.named += pair.Value;
        }
        data.groups = rebuilt;
        data.named_units = named;
    }

    // ---- 城市间迁徙(无小人模式) ----
    // 原版迁徙只搬实体单位。背景人口每月从"推力"大的城(过挤、缺粮、失业)流向本国"拉力"大的城
    // (空房多、不缺粮、就业好)，每月最多迁出 MaxMigrationRate × 推力。颁布逃人法的国家禁止流动
    private const float MaxMigrationRate = 0.02f;

    private static float Push(City city, CityPopulationData data)
    {
        GrowthFactors factors = GetGrowthFactors(city, data);
        float push = 0f;
        if (factors.Capacity > 0f && factors.Total > factors.Capacity) push += Mathf.Clamp01(factors.Total / factors.Capacity - 1f);
        if (data.last_food_shortage > 0f || factors.Famine) push += 0.5f;
        if (data.last_workforce > 0f) push += 0.5f * (1f - Mathf.Clamp01(data.last_jobs / data.last_workforce));
        return Mathf.Clamp01(push);
    }

    private static float Pull(City city, CityPopulationData data)
    {
        GrowthFactors factors = GetGrowthFactors(city, data);
        if (factors.Capacity <= 0f || factors.Famine || data.last_food_shortage > 0f) return 0f;
        float room = Mathf.Clamp01((factors.Capacity - factors.Total) / factors.Capacity);
        float jobs = data.last_workforce > 0f ? Mathf.Clamp01(data.last_jobs / data.last_workforce) : 1f;
        return room * (0.5f + 0.5f * jobs);
    }

    private static void Migrate(City city, CityPopulationData data, double now)
    {
        if (data == null || city.kingdom == null || city.kingdom.wild || city.kingdom.cities == null) return;
        KingdomExtension.KingdomExtraData kingdomData = city.kingdom.GetOrCreate();
        if (kingdomData.fugitive_household_law_enacted || EmpireCraft.Scripts.GeneralSystems.EmpireLaw.EmpireLawSystem.HasLaw(city.kingdom, EmpireCraft.Scripts.GeneralSystems.EmpireLaw.LawType.逃人法)) return;
        float push = Push(city, data);
        if (push <= 0.05f) return;
        City destination = null;
        float best = 0.1f;
        foreach (City other in city.kingdom.cities)
        {
            if (other == null || other == city || other.isRekt()) continue;
            CityPopulationData otherData = Get(other);
            if (otherData == null) continue;
            float pull = Pull(other, otherData);
            if (pull <= best) continue;
            best = pull;
            destination = other;
        }
        if (destination != null) TransferBackground(city, destination, MaxMigrationRate * push);
    }

    // ---- 读书(无小人模式) ----
    // 原版由小人去藏书处读书，读书给本文化攒科技点、名著被别的文化读到会启发那个文化。
    // 没有平民实体后由城里的读书人代读：每月读 1 + 识字人口户数/3 本(最多 4 本)
    private const int MaxReadsPerMonth = 4;

    private static void ReadBooks(City city)
    {
        if (city.countBooks() <= 0) return;
        int reads = Mathf.Clamp(1 + LiterateHouseholds(city) / 3, 1, MaxReadsPerMonth);
        for (int i = 0; i < reads; i++)
        {
            Book book = city.getRandomBook();
            if (book == null) break;
            book.increaseReadTimes();
        }
    }

    // ---- 瘟疫(无小人模式) ----
    // 原版的瘟疫只在实体单位之间传染。城里有染疫的实体单位时，背景人口也按染疫比例死亡：
    // 每月 PlagueBaseDeath + PlagueDeathPerShare × 染疫比例(最多 PlagueMaxDeath)
    private const float PlagueBaseDeath = 0.01f;
    private const float PlagueDeathPerShare = 0.04f;
    private const float PlagueMaxDeath = 0.05f;

    private static void Plague(City city, CityPopulationData data)
    {
        if (data?.groups == null || city.units == null || city.units.Count == 0) return;
        int infected = 0, alive = 0;
        foreach (Actor actor in city.units)
        {
            if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
            alive++;
            if (actor.hasTrait("plague")) infected++;
        }
        if (infected == 0 || alive == 0) return;
        float rate = Mathf.Min(PlagueMaxDeath, PlagueBaseDeath + PlagueDeathPerShare * infected / alive);
        foreach (PopGroup group in data.groups) RemoveBackground(group, group.Background * rate);
    }

    // 城市建设：按上次结算以来的秒数推进施工(见 CityConstructionSystem)
    private static void SettleConstruction(City city, CityPopulationData data, double now)
    {
        if (data == null) return;
        if (data.last_construction < 0d || now < data.last_construction)
        {
            data.last_construction = now;
            return;
        }
        float seconds = Mathf.Min(120f, (float)(now - data.last_construction));
        data.last_construction = now;
        CityConstructionSystem.Settle(city, data, seconds);
    }

    // 背景人口的自然增减(每年结算一次)：
    //   出生 = 基础出生率 × 粮食系数 × (1 - 繁荣度 × 0.4) × 住房系数
    //   死亡 = 基础死亡率 × (1 - 繁荣度 × 0.5) + 饥荒死亡率(人均存粮过低) + 战乱死亡率(城中有敌占地块)
    //   超出住房上限的部分每年再流失 25%。住房上限由城里的民居等建筑决定(getPopulationMaximum)
    private static void GrowBackground(City city, CityPopulationData data, double now)
    {
        if (data.last_growth < 0d || now < data.last_growth)
        {
            data.last_growth = now;
            return;
        }
        float years = Mathf.Clamp(Date.getMonthsSince(data.last_growth) / 12f, 0f, 5f);
        if (years <= 0f) return;
        data.last_growth = now;
        AssimilateBackground(city, data, years);
        DriftClasses(city, data, years);

        PopulationParallelSystem.StartGrowth(city, data, now, years);
    }

    // ---- 背景人口的阶层结构(无小人模式) ----
    // 实体单位的阶层由模组逐人判断(爵位 → 贵族、任官 → 官僚、地多 → 地主……)；背景人口没有这些个人条件，
    // 按时代、制度与这座城自身的发展(规模、工厂、矿场、高级民居、无地比例、首都首府、城市国库)给出应有的阶层结构，
    // 每月按差额靠拢(每年约 ClassDriftRate，第一次直接到位)，城市发展了结构就跟着变。
    // 流动时保留文化、物种、理念，换了阶层后的理念再由理念交往慢慢变化。
    private const float ClassDriftRate = 0.1f;

    public static Dictionary<SocialClass, float> TargetClassShares(City city)
    {
        var shares = new Dictionary<SocialClass, float>();
        Kingdom kingdom = city.kingdom;
        Regime regime = kingdom?.GetRegime();
        RegimeType? type = regime?.type;
        int households = Households(city);
        float size = Mathf.Clamp01(households / 200f);
        int factories = 0;
        int mines = 0;
        bool advancedHousing = false;
        try
        {
            factories = UrbanEmploymentSystem.CountFactories(city);
            advancedHousing = UrbanCitizenSystem.GetCapacity(city, Mathf.Max(1, households)) > 0;
            if (city.buildings != null)
                foreach (Building building in city.buildings)
                    if ((IndustryBuildingSystem.IsMine(building?.asset) || IndustryBuildingSystem.IsLumber(building?.asset)) &&
                        !building.isUnderConstruction()) mines++;
        }
        catch
        {
            // 取不到就按没有算
        }
        // 商贸：有市场、码头的城才有成规模的商人；古代乡村小城没有商人
        bool market = city.hasBuildingType("type_market");
        bool docks = city.hasBuildingType("type_docks");
        float trade = (market ? 0.03f : 0f) + (docks ? 0.02f : 0f);
        bool town = households >= 50 || CityConstructionSystem.SeatBonus(city) > 0f;
        // 工人：城市工场岗位(按生产阶段、建筑、商人、航运、工厂算，见 UrbanEmploymentSystem)占成年人口的比例，
        // 加上伐木采矿等体力活
        float urban = 0f;
        try
        {
            urban = Mathf.Clamp01(UrbanEmploymentSystem.HouseholdCapacity(city) / Mathf.Max(1f, households * 0.7f));
        }
        catch
        {
            // 算不出按没有工场
        }
        bool landMarket = LandEconomySystem.IsLandMarketOpen(kingdom);
        bool landlords = landMarket && RegimeManager.AllowsLandlordClass(type);
        float landless = Get(city)?.background_landless ?? 0f;
        if (ModernStability.IsModern(kingdom))
        {
            shares[SocialClass.Noble] = RegimeManager.IsMonarchy(type) ? 0.005f : 0f;
            shares[SocialClass.Officer] = 0.03f;
            shares[SocialClass.Landlord] = landlords ? Mathf.Min(0.03f, landless * 0.05f) : 0f;
            shares[SocialClass.Merchant] = 0.02f + trade + 0.02f * size;
            // 矿场、伐木场的体力活：和古代一样每座多 1% 工人
            shares[SocialClass.Labour] = Mathf.Clamp(urban + 0.05f + 0.01f * mines, 0.05f, 0.45f);
            shares[SocialClass.Citizen] = advancedHousing ? 0.25f : 0.1f;
        }
        else
        {
            bool feudal = type is RegimeType.Feudalism or RegimeType.ZhouFeudalism or RegimeType.YouMu;
            bool bureaucratic = GranarySystem.Enabled(kingdom);
            shares[SocialClass.Noble] = feudal ? 0.02f : 0.008f;
            shares[SocialClass.Officer] = bureaucratic ? 0.02f : 0.01f;
            shares[SocialClass.Landlord] = landlords ? Mathf.Clamp(landless * 0.08f, 0.005f, 0.05f) : 0f;
            shares[SocialClass.Merchant] = trade > 0f || town ? trade + 0.02f * size : 0f;
            shares[SocialClass.Labour] = Mathf.Min(0.4f, urban + 0.02f + 0.01f * mines);
            shares[SocialClass.Citizen] = advancedHousing ? 0.05f : 0.01f * size;
        }
        // 城市自身的发展：首都、首府是官府与朝廷所在，官僚、贵族、商人更多；城市国库越充裕，商人、市民越多
        float seat = CityConstructionSystem.SeatBonus(city);
        float treasury = Mathf.Clamp01(city.GetMoney() / 1000f);
        shares[SocialClass.Officer] *= 1f + 2f * seat;
        shares[SocialClass.Noble] *= 1f + seat;
        if (shares[SocialClass.Merchant] > 0f || seat > 0f) shares[SocialClass.Merchant] += 0.02f * seat + 0.02f * treasury;
        shares[SocialClass.Citizen] += 0.03f * treasury;
        shares[SocialClass.Army] = 0f;
        float others = 0f;
        foreach (float share in shares.Values) others += share;
        shares[SocialClass.Peasant] = Mathf.Max(0.1f, 1f - others);
        return shares;
    }

    private static void DriftClasses(City city, CityPopulationData data, float years)
    {
        if (!AbstractPopulationEnabled || data?.groups == null) return;
        float total = 0f;
        var current = new Dictionary<SocialClass, float>();
        foreach (PopGroup group in data.groups)
        {
            float amount = group.Background;
            if (amount <= 0f) continue;
            total += amount;
            current.TryGetValue(group.social_class, out float sum);
            current[group.social_class] = sum + amount;
        }
        if (total < 1f) return;
        Dictionary<SocialClass, float> shares = TargetClassShares(city);
        float shareSum = 0f;
        foreach (float share in shares.Values) shareSum += share;
        float rate = data.classes_initialized ? Mathf.Clamp01(ClassDriftRate * years) : 1f;
        data.classes_initialized = true;

        // 各阶层比目标多出的部分按 rate 流出，按缺口大小流向少于目标的阶层
        var deficits = new Dictionary<SocialClass, float>();
        float deficitTotal = 0f;
        foreach (KeyValuePair<SocialClass, float> pair in shares)
        {
            current.TryGetValue(pair.Key, out float have);
            float gap = total * pair.Value / shareSum - have;
            if (gap <= 0f) continue;
            deficits[pair.Key] = gap;
            deficitTotal += gap;
        }
        if (deficitTotal <= 0f) return;
        var moves = new List<(PopGroup from, float amount)>();
        foreach (PopGroup group in data.groups)
        {
            float amount = group.Background;
            if (amount <= 0f || !current.TryGetValue(group.social_class, out float have)) continue;
            shares.TryGetValue(group.social_class, out float share);
            float surplus = have - total * share / shareSum;
            if (surplus <= 0f) continue;
            float outflow = amount * Mathf.Clamp01(surplus / have) * rate;
            if (outflow > 0.001f) moves.Add((group, outflow));
        }
        foreach ((PopGroup from, float amount) in moves)
        {
            float removed = RemoveBackground(from, amount);
            if (removed <= 0f) continue;
            foreach (KeyValuePair<SocialClass, float> deficit in deficits)
                AddBackground(city, deficit.Key, from.culture, from.species, from.ideology,
                    removed * deficit.Value / deficitTotal);
        }
    }

    // 背景人口的文化同化：城里非主流文化的居民每年有 AssimilationRate 的比例改用主流文化
    // (通婚、上学、做官都要用主流文化)，阶层、物种、理念不变。种族(物种)不会被同化
    private const float AssimilationRate = 0.02f;

    private static void AssimilateBackground(City city, CityPopulationData data, float years)
    {
        string main = CultureService.GetMainCulture(city, initialize: false);
        if (!CultureService.IsValidCulture(main) || data?.groups == null) return;
        var moves = new List<(PopGroup from, float amount)>();
        foreach (PopGroup group in data.groups)
        {
            if (string.Equals(group.culture, main, StringComparison.Ordinal)) continue;
            float amount = group.Background * Mathf.Clamp01(AssimilationRate * years);
            if (amount > 0f) moves.Add((group, amount));
        }
        foreach ((PopGroup from, float amount) in moves)
        {
            float removed = RemoveBackground(from, amount);
            if (removed > 0f) AddBackground(city, from.social_class, main, from.species, from.ideology, removed);
        }
    }

    // 城市的背景人口整体改用某个文化(文化复原等决议)
    public static void ConvertBackgroundCulture(City city, string culture)
    {
        CityPopulationData data = Get(city);
        if (data?.groups == null || !CultureService.IsValidCulture(culture)) return;
        var moves = new List<(PopGroup from, float amount)>();
        foreach (PopGroup group in data.groups)
            if (!string.Equals(group.culture, culture, StringComparison.Ordinal) && group.Background > 0f)
                moves.Add((group, group.Background));
        foreach ((PopGroup from, float amount) in moves)
        {
            float removed = RemoveBackground(from, amount);
            if (removed > 0f) AddBackground(city, from.social_class, culture, from.species, from.ideology, removed);
        }
    }

    public readonly struct GrowthFactors
    {
        public readonly float Total, Capacity, FoodPerCapita, Prosperity, BirthRate, DeathRate;
        public readonly bool Famine, War;

        public GrowthFactors(float total, float capacity, float foodPerCapita, float prosperity, float birthRate,
            float deathRate, bool famine, bool war)
        {
            Total = total;
            Capacity = capacity;
            FoodPerCapita = foodPerCapita;
            Prosperity = prosperity;
            BirthRate = birthRate;
            DeathRate = deathRate;
            Famine = famine;
            War = war;
        }
    }

    // 当前的年出生率、死亡率及其成因(界面和日志可以直接显示)
    public static GrowthFactors GetGrowthFactors(City city, CityPopulationData data = null)
    {
        data ??= Get(city);
        float total = GetTotal(data);
        float capacity = Mathf.Max(0, city.getPopulationMaximum());
        float food = 0f;
        try
        {
            food = city.getTotalFood();
        }
        catch
        {
            // 读不到存粮按吃得饱处理
            food = total * ComfortFoodPerCapita;
        }
        // 仓库里的粮食按"户"计(见 PopulationEconomySystem)，人均存粮也按户算
        float households = total / PeoplePerSlot(city);
        // 不到一户的零星人口靠采集渔猎就能糊口，不算饥荒(否则人口一旦跌到个位数，没人种地又判饥荒，永远长不回来)
        float foodPerCapita = households >= 1f ? food / households : ComfortFoodPerCapita;
        float prosperity = Mathf.Clamp01(IdeologyPopulationSystem.CachedEconomy(city).Prosperity);
        float satisfaction = PopulationSettlementRules.FoodSatisfaction(data);
        bool famine = households >= 1f && (satisfaction >= 0f
            ? satisfaction < 0.8f : foodPerCapita < FamineFoodPerCapita);
        bool war = city.GetOrCreate().OccupiedStatus?.Count > 0;

        float foodFactor = Mathf.Clamp(foodPerCapita / ComfortFoodPerCapita, 0f, MaxFoodBirthFactor);
        if (satisfaction >= 0f) foodFactor = Mathf.Max(foodFactor, satisfaction);
        if (famine) foodFactor *= satisfaction >= 0f ? satisfaction : 0f;
        float housingFactor = capacity <= 0f ? 0f
            : Mathf.Clamp01((capacity - total) / Mathf.Max(1f, capacity * HousingSlowdownShare));
        // 就业：岗位不够时一部分人外出谋生、晚婚少育(没结算过经济的城市按充分就业算)
        float employment = data != null && data.last_workforce > 0f
            ? Mathf.Clamp01(data.last_jobs / data.last_workforce) : 1f;
        float employmentFactor = 0.5f + 0.5f * employment;
        float birth = BaseBirthRate * foodFactor * (1f - ProsperityBirthReduction * prosperity) * housingFactor *
                      employmentFactor;
        float death = BaseDeathRate * (1f - ProsperityDeathReduction * prosperity) +
                      (famine ? FamineDeathRate : 0f) + (war ? WarDeathRate : 0f);
        return new GrowthFactors(total, capacity, foodPerCapita, prosperity, birth, death, famine, war);
    }

    #endregion

    #region 并入普通人(无小人模式)

    // 每个游戏月把全部城市排队检查一次，分帧处理(与年度结算共用每帧时间上限)
    private static void TickFolds(MapBox world, double now)
    {
        _waitingForMath = false;
        if (_foldCity == null && PendingFolds.Count == 0)
        {
            bool due = _lastFoldPass < 0d || now < _lastFoldPass || Date.getMonthsSince(_lastFoldPass) >= FoldIntervalMonths;
            if (!due) return;
            _lastFoldPass = now;
            foreach (City city in world.cities)
                if (city?.data != null && !city.isRekt()) PendingFolds.Enqueue(city);
            if (PendingFolds.Count == 0) return;
        }
        using var timing = new PerfTimer("无小人模式并入普通人");
        PopulationJurenRanking.Prewarm(PendingFolds);
        PopulationParallelSystem.PrewarmWorkforce(PendingFolds, now);
        Stopwatch watch = Stopwatch.StartNew();
        while ((_foldCity != null || PendingFolds.Count > 0) && watch.Elapsed.TotalMilliseconds < SliceBudgetMs &&
               SimulationFrameBudget.HasTime)
        {
            if (_foldCity == null)
            {
                _foldCity = PendingFolds.Dequeue();
                _foldOwner = _foldCity?.kingdom;
                _foldPhase = 0;
                _foldNow = now;
            }
            City city = _foldCity;
            if (city?.data == null || city.isRekt() || city.kingdom == null || city.kingdom != _foldOwner ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city))
            {
                PopulationParallelSystem.Discard(city);
                _foldCity = null;
                continue;
            }
            try
            {
                using var phaseTiming = new PerfTimer($"城市月度步骤 {_foldPhase} ({city.data.name})");
                if (ExecuteFoldPhase(city, Get(city), _foldNow, _foldPhase++)) _foldCity = null;
                if (_waitingForMath) break;
            }
            catch (Exception exception)
            {
                PopulationParallelSystem.Discard(city);
                _foldCity = null;
                LogService.LogWarning($"[EmpireCraft] 无小人模式并入普通人失败({city.data?.name}): {exception.Message}");
            }
        }
    }

    // 每个步骤完成后即可让出这一帧；同一次城市结算使用同一个时间，避免分帧重复扣粮/增长。
    private static bool ExecuteFoldPhase(City city, CityPopulationData data, double now, int phase)
    {
        switch (phase)
        {
            case 0:
                _foldHarvest = 0f;
                _foldEconomyDue = data.last_economy >= 0d && now >= data.last_economy && Date.getMonthsSince(data.last_economy) > 0;
                EnsureFloor(city);
                break;
            case 1:
                FoldCommoners(city);
                // 实体转背景必须在本步骤内完成校准，避免跨帧重复计入同一个人。
                Census(city, data, keepBackground: true);
                break;
            case 2: RaiseLevies(city); Census(city, data, keepBackground: true); break;
            case 3: MarchToFront(city); PopulationParallelSystem.PrepareWorkforce(city, now, true); break;
            case >= 4 and <= 8:
                if (_foldEconomyDue)
                    _foldHarvest += PopulationEconomySystem.DepositFarmHarvest(city, data,
                        FarmlandSystem.SettlePhase(city, data, phase - 4));
                break;
            case 9: PopulationEconomySystem.Settle(city, data, now, _foldHarvest); break;
            case 10: MarketSystem.Settle(city, data); break;
            case 11: MarketSystem.CheckSiegeSurrender(city, data); break;
            case 12:
                if (_foldEconomyDue) EmpireCraft.Scripts.GamePatches.CityExpansionPatch.GrowVirtualCity(city);
                SettleConstruction(city, data, now);
                break;
            case 13: ScorchedEarthSystem.Settle(city); break;
            case 14: Migrate(city, data, now); break;
            case 15: Plague(city, data); break;
            case 16: ReadBooks(city); break;
            case 17: GrowBackground(city, data, now); break;
            default:
                if (!PopulationParallelSystem.TryCompleteGrowth(city))
                { _foldPhase = 18; _waitingForMath = true; return false; }
                Census(city, data, keepBackground: true);
                return true;
        }
        return false;
    }

    public static void FlushPendingGrowth()
    {
        if (!AbstractPopulationEnabled) { PopulationParallelSystem.ResetWorldState(); return; }
        foreach (City city in PopulationParallelSystem.FlushGrowth())
            if (city?.data != null && !city.isRekt()) Census(city, Get(city), keepBackground: true);
    }

    // 名人：保留为实体单位的人(军人另算，见 IsSoldier)。君主、城主、官僚、贵族、地主，党派/派系成员，
    // 正在谋划的人、玩家收藏或镜头跟随的人
    public static bool IsNotable(Actor actor) => IsNotable(actor, includeClanHead: true);

    public static bool IsNotable(Actor actor, bool includeClanHead)
    {
        if (actor?.data == null || actor.isRekt()) return true;
        if (actor.isKing() || actor.isCityLeader()) return true;
        if (actor.isFavorite() || actor.isCameraFollowingUnit()) return true;
        if (actor.plot != null) return true;
        // 各步分别累计计时(见 FrameProfiler)，用来找月度并入"分类"一步的大头
        SocialClass socialClass;
        using (FrameProfiler.Measure("名人判定·阶层")) socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
        if (socialClass == SocialClass.Officer || socialClass == SocialClass.Landlord) return true;
        // 贵族只保留要紧的人；世袭国家整个统治氏族都算贵族，远支一律留在地图上会代代繁衍、越滚越多
        if (socialClass == SocialClass.Noble)
            using (FrameProfiler.Measure("名人判定·要紧贵族"))
                if (IsImportantNoble(actor)) return true;
        using (FrameProfiler.Measure("名人判定·派系"))
            if (actor.GetFaction() != null) return true;
        // 有功名的读书人：选官的人才库。进士、贡士一律保留；举人每城只留政绩最好的 MaxKeptJuren 位，
        // 其余回到人口里(族谱里的人转为虚拟族人)，免得每年中举的人越积越多
        if (actor.hasTrait("gongshi") || actor.hasTrait("jingshi")) return true;
        if (actor.hasTrait("juren"))
            using (FrameProfiler.Measure("名人判定·举人"))
                if (IsKeptJuren(actor)) return true;
        // 宗族族长：每个宗族至少保留一名实体族长
        if (includeClanHead)
            using (FrameProfiler.Measure("名人判定·族长"))
                if (VirtualGenealogySystem.IsClanHead(actor)) return true;
        return false;
    }

    // 月度并入最多重算十二人，由每城游标轮转，未知者等待检查。
    private const int MaxNotableRecomputes = 12;
    private static ConditionalWeakTable<City, PopulationClassificationCache> Classification = new();

    private static void ResetClassification()
    {
        Classification = new();
        PopulationJurenRanking.Reset();
    }

    private static bool ImmediatelyProtected(Actor actor) => actor.isKing() || actor.isCityLeader() ||
        actor.isFavorite() || actor.isCameraFollowingUnit() || actor.plot != null;

    // 要紧的贵族：君主、继承人、君主的配偶与子女、有封地或爵位的人。其余宗室并入虚拟族谱，需要时再落成
    private static bool IsImportantNoble(Actor actor)
    {
        if (actor.isKing()) return true;
        Kingdom kingdom = actor.kingdom;
        if (kingdom != null && kingdom.GetHeir() == actor) return true;
        if (actor.lover != null && !actor.lover.isRekt() && actor.lover.isKing()) return true;
        foreach (Actor parent in actor.getParents())
            if (parent != null && !parent.isRekt() && parent.isKing()) return true;
        if (actor.GetOwnedTitle()?.Count > 0) return true;
        return actor.HasHonoraryPeerage() || actor.HasVirtualEnfeoff();
    }

    private const int MaxKeptJuren = 5;

    private static bool IsKeptJuren(Actor actor)
    {
        if (PopulationJurenRanking.TryIsKept(actor, out bool kept)) return kept;
        City city = actor.city;
        if (city?.units == null) return true;
        double mine = actor.GetIdentity()?.TotalPerformance ?? 0d;
        int better = 0;
        foreach (Actor other in city.units)
        {
            if (other == null || other == actor || other.isRekt() || !other.isAlive() || !other.hasTrait("juren")) continue;
            double theirs = other.GetIdentity()?.TotalPerformance ?? 0d;
            if (theirs > mine || theirs == mine && other.id < actor.id) better++;
            if (better >= MaxKeptJuren) return false;
        }
        return true;
    }

    public static bool IsSoldier(Actor actor) =>
        actor != null && (actor.isWarrior() || actor.army != null || actor.is_army_captain);

    // 无小人模式下的城市实体单位(参考 CK3 的征召兵)：
    //   - 名人一律保留；
    //   - 太平时每座城只驻扎一名实体将领(优先现任统领)，其余士兵解散回人口数据；
    //   - 交战时士兵全部保留，并按空余兵额从背景人口里当场征召(见 RaiseLevies)；
    //   - 普通人保留 KeptWorkersPerCity 个劳动者(成年人优先)，其余并入背景人口。
    public static int FoldCommoners(City city)
    {
        if (!AbstractPopulationEnabled || city?.units == null || city.units.Count == 0) return 0;
        using var timing = FrameProfiler.Measure("虚拟人口·实体并入");
        PopulationJurenRanking.BeginPass(city);
        try { return FoldCommonersPass(city); }
        finally { PopulationJurenRanking.EndPass(); }
    }

    private static int FoldCommonersPass(City city)
    {
        bool atWar = OnWarFooting(city);
        PopulationClassificationCache cache = Classification.GetOrCreateValue(city);
        double now = World.world.getCurWorldTime();
        var commoners = new List<Actor>();
        var soldiers = new List<Actor>();
        var liveIds = new List<long>();
        bool unknown = false;
        // 分段计时：整段超过 8 ms 时在日志里列出各段耗时，定位月度结算的大头
        long t0 = Stopwatch.GetTimestamp();
        cache.Warm(city.units.Count, MaxNotableRecomputes, index =>
        {
            Actor actor = city.units[index];
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.city != city ||
                actor.asset == null || IsVehicle(actor) || ImmediatelyProtected(actor)) return false;
            if (cache.TryGet(actor.id, now, Date.getMonthsSince, out _)) return false;
            cache.Set(actor.id, now, IsNotable(actor));
            return true;
        }, () => SimulationFrameBudget.HasTime);
        foreach (Actor actor in city.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.city != city) continue;
            if (actor.asset == null || IsVehicle(actor)) continue;
            liveIds.Add(actor.id);
            if (ImmediatelyProtected(actor)) continue;
            if (!cache.TryGet(actor.id, now, Date.getMonthsSince, out bool notable)) { unknown = true; continue; }
            if (notable) continue;
            if (IsSoldier(actor)) soldiers.Add(actor);
            else commoners.Add(actor);
        }
        cache.Prune(liveIds);

        long tClassify = Stopwatch.GetTimestamp();
        // 城主由士兵兼任(城里只剩征召兵时原版会这样选)：卸任城主，另行补位，士兵留在军中
        if (city.leader != null && !city.leader.isRekt() && IsSoldier(city.leader)) city.removeLeader();
        // 城主空缺：从人口中当场生成一人出任(总人口不变)
        if (city.leader == null || city.leader.isRekt() || !city.leader.isAlive())
        {
            Actor leader = SpawnCivilian(city);
            if (leader != null) city.setLeader(leader, true);
        }

        long tLeader = Stopwatch.GetTimestamp();
        // 留在地图上的人不用吃饭、不用睡觉：饥饿值补满，正在睡的叫醒
        foreach (Actor actor in city.units)
            if (actor?.data != null && !actor.isRekt() && actor.isAlive())
            {
                KeepFedAndAwake(actor);
                // 已经领了原版差事(农夫、樵夫、矿工……)的名人放下差事
                if (actor.citizen_job != null && !IsSoldier(actor)) actor.endJob();
            }

        long tCare = Stopwatch.GetTimestamp();
        var toFold = new List<Actor>();
        // 军制(见 LevyTarget)：太平时不是军镇的城不留兵、军镇只留常备兵额(没有常备军就留一名将领)；
        // 交战(含战备)期间一律不遣散——起义城市临时武装的义军、调防途中的军团都留着
        int keep = LevyTarget(city);
        if (!atWar && soldiers.Count > Mathf.Max(0, keep))
        {
            // 现任统领优先留下，其次带着部队的人
            soldiers.Sort((left, right) => GeneralRank(right).CompareTo(GeneralRank(left)));
            for (int i = Mathf.Max(0, keep); i < soldiers.Count; i++) toFold.Add(soldiers[i]);
        }
        // 劳动者不够(被征去当兵、死亡)时，从背景人口里补上
        for (int missing = unknown ? 0 : KeptWorkersPerCity - commoners.Count; missing > 0; missing--)
        {
            PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                               !string.IsNullOrEmpty(candidate.species));
            if (group == null || SpawnFromGroup(city, group, soldier: false) == null) break;
            RemoveBackground(group, 1f);
        }
        if (commoners.Count > KeptWorkersPerCity)
        {
            // 成年人优先留下干活，孩子优先并入
            commoners.Sort((left, right) => right.isAdult().CompareTo(left.isAdult()));
            for (int i = KeptWorkersPerCity; i < commoners.Count; i++) toFold.Add(commoners[i]);
        }

        if (toFold.Count > MaxFoldsPerPass) toFold.RemoveRange(MaxFoldsPerPass, toFold.Count - MaxFoldsPerPass);
        int folded = 0;
        int reviewed = 0;
        foreach (Actor actor in toFold)
        {
            if (reviewed++ > 0 && !SimulationFrameBudget.HasTime) break;
            // 缓存期间可能升官、获封或被收藏。真正并入前实时复核，不能把新名人误删。
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.city != city) continue;
            bool notable = IsNotable(actor);
            cache.Set(actor.id, now, notable);
            if (notable) continue;
            FoldIntoPopulation(actor, city);
            folded++;
        }
        long tFold = Stopwatch.GetTimestamp();
        UpdateLegions(city);
        long tEnd = Stopwatch.GetTimestamp();
        double Ms(long a, long b) => (b - a) * 1000d / Stopwatch.Frequency;
        if (Ms(t0, tEnd) >= 8d)
            LogService.LogWarning($"[EmpireCraft][性能] 并入分段({city.data?.name}) 单位 {city.units.Count}：" +
                                  $"分类 {Ms(t0, tClassify):0.0} / 城主补位 {Ms(tClassify, tLeader):0.0} / " +
                                  $"喂饱叫醒 {Ms(tLeader, tCare):0.0} / 并入 {folded} 人 {Ms(tCare, tFold):0.0} / " +
                                  $"军团 {Ms(tFold, tEnd):0.0} ms");
        return folded;
    }

    public static void EnsureLegionOrigins(Actor actor)
    {
        ActorExtension.ActorExtraData extra = actor.GetOrCreate();
        if (extra.legion_population != null || extra.legion_size <= 0f) return;
        extra.legion_population = new List<LegionPopulationContribution>();
        extra.legion_home_city_id = actor.city?.id ?? -1L;
        // 旧存档没有来源记录，只能保留当前士兵的身份；新征召逐组记来源。
        LegionPopulationRules.Add(extra.legion_population, extra.legion_home_city_id, new PopGroup
        {
            social_class = SocialClass.Peasant, culture = CultureService.GetActorCulture(actor) ?? "",
            species = actor.asset?.id ?? "", ideology = IdeologyPopulationSystem.Get(actor)
        }, extra.legion_size - 1f);
    }

    public static void DemobilizeSoldier(Actor actor, City home)
    {
        if (actor == null || !actor.isAlive() || home == null || actor.isKing() || actor.isCityLeader()) return;
        if (AbstractPopulationEnabled)
        {
            LegionAlive(actor);
            var extra = actor.GetOrCreate();
            var origins = extra.legion_population;
            extra.legion_population = null;
            extra.legion_size = extra.legion_full = 0f;
            extra.legion_home_city_id = -1L;
            if (origins != null)
                foreach (var origin in origins)
                    if (origin != null && origin.size > 0f)
                    {
                        City destination = home.kingdom?.cities.FirstOrDefault(candidate => candidate != null &&
                            !candidate.isRekt() && candidate.id == origin.city_id) ?? home;
                        AddBackground(destination, origin.social_class == SocialClass.Army ? SocialClass.Peasant :
                            origin.social_class, origin.culture, origin.species, origin.ideology, origin.size);
                    }
        }
        actor.removeFromArmy();
        actor.stopBeingWarrior();
        UpdateLegions(home);
        InvalidateHouseholdCaches();
    }

    private static void FoldIntoPopulation(Actor actor, City fallback)
    {
        SocialClass socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
        if (socialClass == SocialClass.Army) socialClass = SocialClass.Peasant;
        string culture = CultureService.GetActorCulture(actor) ?? "";
        string species = actor.asset?.id ?? "";
        PartyIdeology ideology = IdeologyPopulationSystem.Get(actor);
        ActorExtension.ActorExtraData extra = actor.GetOrCreate();
        LegionAlive(actor);
        List<LegionPopulationContribution> origins = extra.legion_population;
        City Home(long cityId) => fallback.kingdom?.cities.FirstOrDefault(candidate =>
            candidate?.data != null && !candidate.isRekt() && candidate.id == cityId) ?? fallback;
        City home = Home(extra.legion_home_city_id);
        if (extra.legion_home_city_id >= 0L) socialClass = extra.legion_home_social_class;
        if (socialClass == SocialClass.Army) socialClass = SocialClass.Peasant;
        // 在 die 的回调前清空，避免退伍重复扣减或让数据跟随复用的 Actor。
        extra.legion_size = extra.legion_full = 0f;
        extra.legion_population = null;
        extra.legion_home_city_id = -1L;
        VirtualGenealogySystem.Virtualize(actor, home);
        actor.die(true, AttackType.Other, false, false);
        AddBackground(home, socialClass, culture, species, ideology, 1f);
        if (origins == null) return;
        foreach (LegionPopulationContribution origin in origins)
            if (origin != null && origin.size > 0f)
                AddBackground(Home(origin.city_id), origin.social_class == SocialClass.Army
                    ? SocialClass.Peasant : origin.social_class, origin.culture, origin.species, origin.ideology, origin.size);
    }

    private static bool _careResolved;
    private static FieldInfo _nutritionField;
    private static MethodInfo _maxNutrition;
    private static MethodInfo _finishStatus;

    // 饥饿值补满并结束睡眠。原版字段/方法名用反射查找(不同版本可能不同)，找不到的部分跳过
    public static void KeepFedAndAwake(Actor actor)
    {
        if (!_careResolved)
        {
            _careResolved = true;
            _nutritionField = AccessTools.Field(actor.data.GetType(), "nutrition");
            _maxNutrition = AccessTools.Method(typeof(Actor), "getMaxNutrition", Type.EmptyTypes);
            _finishStatus = AccessTools.Method(typeof(Actor), "finishStatusEffect", new[] { typeof(string) });
            LogService.LogInfo($"[EmpireCraft][无小人模式] 补满饥饿={(_nutritionField != null ? "可用" : "不可用")} 叫醒={(_finishStatus != null ? "可用" : "不可用")}");
        }
        try
        {
            if (_nutritionField != null && _nutritionField.FieldType == typeof(int))
            {
                int max = _maxNutrition != null ? Convert.ToInt32(_maxNutrition.Invoke(actor, null)) : 100;
                if (max > 0) _nutritionField.SetValue(actor.data, max);
            }
            if (_finishStatus != null && actor.hasStatus("sleeping"))
                _finishStatus.Invoke(actor, new object[] { "sleeping" });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][无小人模式] 补满饥饿/叫醒失败，停用: {exception.Message}");
            _nutritionField = null;
            _finishStatus = null;
        }
    }

    private static int GeneralRank(Actor actor) =>
        (actor.is_army_captain ? 4 : 0) + (actor.army != null ? 2 : 0) + (actor.isAdult() ? 1 : 0);

    public static bool IsAtWar(City city) => city?.kingdom != null && city.kingdom.hasEnemies();

    #endregion

    #region 征召兵(无小人模式)

    // 每座城每次结算最多征召的人数：生成单位、打造装备都不便宜，分多次结算慢慢补满，免得一帧卡住
    private const int MaxLeviesPerPass = 3;
    // 每座城每次结算最多并入的人数(移除单位也不便宜)
    private const int MaxFoldsPerPass = 10;
    private static bool _spawnResolved;
    private static MethodInfo _createWithSubspecies;

    // 交战时按空余兵额从背景人口里当场生成士兵(在城里随机地块)，仗打完由 FoldCommoners 解散
    public static int RaiseLevies(City city)
    {
        if (!AbstractPopulationEnabled || city?.status == null || city.kingdom == null) return 0;
        // 兵额按军制(见 LevyTarget)，并写回城市状态，免得原版以"超编"为由把新兵解散
        int slots = LevyTarget(city);
        city.status.warrior_slots = slots;
        int need = Mathf.Min(MaxLeviesPerPass, slots - city.status.warriors_current);
        if (need <= 0) return 0;
        // 每个兵代表一个军团：兵源是全国的背景人口，可征比例由军制决定(默认一成)，按全国各军镇的兵额平分，每团最多一万人
        ArmyDoctrine doctrine = DoctrineOf(city.kingdom);
        float pool = KingdomBackground(city.kingdom) * doctrine.LevyShare;
        int totalSlots = 0;
        foreach (City other in city.kingdom.cities)
            if (other != null && !other.isRekt()) totalSlots += Mathf.Max(0, LevyTarget(other));
        float legion = Mathf.Clamp(pool / Mathf.Max(1, totalSlots), 1f, PeoplePerLegion);
        int raised = 0;
        for (int i = 0; i < need; i++)
        {
            if (i > 0 && !SimulationFrameBudget.HasTime) break;
            City source = DrawKingdomCity(city.kingdom) ?? city;
            PopGroup group = DrawBackground(source, candidate => candidate.Background >= 1f &&
                                                                 !string.IsNullOrEmpty(candidate.species));
            if (group == null) break;
            Actor actor = SpawnFromGroup(city, group, soldier: true);
            if (actor == null) break;
            // 主要从抽中的城与组出人，不够的从本国其他城补
            ActorExtension.ActorExtraData extra = actor.GetOrCreate();
            extra.legion_home_city_id = source.id;
            extra.legion_home_social_class = group.social_class;
            extra.legion_population = new List<LegionPopulationContribution>();
            float taken = RemoveBackground(group, legion);
            LegionPopulationRules.Add(extra.legion_population, source.id, group, taken - 1f);
            for (int attempt = 0; taken < legion && attempt < 8; attempt++)
            {
                City other = DrawKingdomCity(city.kingdom);
                PopGroup more = other == null ? null : DrawBackground(other, candidate => candidate.Background >= 1f);
                if (more == null) break;
                float contribution = RemoveBackground(more, legion - taken);
                LegionPopulationRules.Add(extra.legion_population, other.id, more, contribution);
                taken += contribution;
            }
            actor.GetOrCreate().legion_size = Mathf.Max(1f, taken);
            actor.GetOrCreate().legion_full = Mathf.Max(1f, taken);
            raised++;
        }
        if (raised > 0) UpdateLegions(city);
        return raised;
    }

    // 义军(无小人模式)：起义、革命时从本城背景人口里当场武装 count 个兵，每个兵代表一支义军，
    // 一共动员背景人口的 share。返回实际征到的人数(兵)
    public static int RaiseMilitia(City city, int count, float share)
    {
        if (!AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null || count <= 0)
            return 0;
        float background = GetBackgroundTotal(city);
        if (background < 1f) return 0;
        float legion = Mathf.Clamp(background * Mathf.Clamp01(share) / count, 1f, PeoplePerLegion);
        int raised = 0;
        for (int i = 0; i < count; i++)
        {
            PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                               !string.IsNullOrEmpty(candidate.species));
            if (group == null) break;
            Actor actor = SpawnFromGroup(city, group, soldier: true);
            if (actor == null) break;
            ActorExtension.ActorExtraData extra = actor.GetOrCreate();
            extra.legion_home_city_id = city.id;
            extra.legion_home_social_class = group.social_class;
            extra.legion_population = new List<LegionPopulationContribution>();
            float taken = RemoveBackground(group, legion);
            LegionPopulationRules.Add(extra.legion_population, city.id, group, taken - 1f);
            for (int attempt = 0; taken < legion && attempt < 8; attempt++)
            {
                PopGroup more = DrawBackground(city, candidate => candidate.Background >= 1f);
                if (more == null) break;
                float contribution = RemoveBackground(more, legion - taken);
                LegionPopulationRules.Add(extra.legion_population, city.id, more, contribution);
                taken += contribution;
            }
            actor.GetOrCreate().legion_size = Mathf.Max(1f, taken);
            actor.GetOrCreate().legion_full = Mathf.Max(1f, taken);
            raised++;
        }
        if (raised > 0) UpdateLegions(city);
        return raised;
    }

    // 战时能动员的兵力(各军镇的战时兵额与现有士兵取大者之和)：原版 AI 判断要不要开战、打谁时用
    public static int WarPotential(Kingdom kingdom)
    {
        if (!AbstractPopulationEnabled || kingdom?.cities == null) return 0;
        int total = 0;
        foreach (City city in kingdom.cities)
        {
            if (city == null || city.isRekt()) continue;
            total += Mathf.Max(LevyTarget(city, true), city.status?.warriors_current ?? 0);
        }
        return total;
    }

    // 守方还能动员的兵(实体士兵之外，各军镇按战时兵额尚未征召的部分)。入城即降据此判断是否真的无兵可征
    public static int MobilizableReserve(Kingdom kingdom, bool forceRefresh = false)
    {
        if (!AbstractPopulationEnabled || kingdom?.cities == null) return 0;
        EnsureFrameCache();
        if (!forceRefresh && MobilizableReserveCache.TryGetValue(kingdom, out int cached)) return cached;
        int reserve = 0;
        foreach (City city in kingdom.cities)
        {
            if (city == null || city.isRekt() || city.status == null) continue;
            reserve += Mathf.Max(0, LevyTarget(city) - city.status.warriors_current);
        }
        MobilizableReserveCache[kingdom] = reserve;
        return reserve;
    }

    // 军团现存人数：按士兵当前生命值折算满编人数，掉血就是减员，阵亡的人不会回来
    // 载具(WarBox 的坦克、装甲车、飞机等)不是人：不计人口、不代表军团、不并回人口
    public static bool IsVehicle(Actor actor) =>
        actor?.asset != null && (actor.asset.is_boat || actor.asset.id.StartsWith("warbox_") || actor.hasTrait("warbox_unit"));

    public static float LegionAlive(Actor actor)
    {
        if (actor?.data == null || actor.isRekt()) return 0f;
        if (IsVehicle(actor)) return 0f;
        ActorExtension.ActorExtraData extra = actor.GetOrCreate();
        EnsureLegionOrigins(actor);
        if (extra.legion_size <= 1f) return 1f;
        if (extra.legion_full < extra.legion_size) extra.legion_full = extra.legion_size;
        float alive = Mathf.Max(1f, extra.legion_full * HealthRatio(actor));
        if (alive < extra.legion_size) extra.legion_size = alive;
        LegionPopulationRules.ReduceTo(extra.legion_population, extra.legion_size - 1f);
        return extra.legion_size;
    }

    private static float HealthRatio(Actor actor)
    {
        try
        {
            int max = actor.getMaxHealth();
            if (max > 0) return Mathf.Clamp01((float)actor.getHealth() / max);
        }
        catch
        {
            // 读不到生命值按满员算
        }
        return 1f;
    }

    // 补员：士兵回血时，军团按生命值补回满编，补的人从本国各城的背景人口里征调；人口不够就补到能补的数
    private static void ReinforceLegion(Actor actor, Kingdom kingdom)
    {
        ActorExtension.ActorExtraData extra = actor.GetOrCreate();
        if (extra.legion_size <= 0f || extra.legion_full <= extra.legion_size || kingdom == null) return;
        LegionAlive(actor);
        float need = extra.legion_full * HealthRatio(actor) - extra.legion_size;
        if (need < 1f) return;
        float taken = 0f;
        for (int attempt = 0; taken < need && attempt < 8; attempt++)
        {
            City source = DrawKingdomCity(kingdom);
            PopGroup group = source == null ? null : DrawBackground(source, candidate => candidate.Background >= 1f);
            if (group == null) break;
            float contribution = RemoveBackground(group, need - taken);
            LegionPopulationRules.Add(extra.legion_population, source.id, group, contribution);
            taken += contribution;
        }
        extra.legion_size += taken;
    }

    // 士兵战死：他代表的整个军团(当前存活的人)当场从所属城市的人口里扣掉，不用等每月重算。
    // 解散退伍时会先把军团人数清零再移除单位，不会被当成阵亡
    public static void OnSoldierDied(Actor actor)
    {
        if (!AbstractPopulationEnabled || actor?.data == null) return;
        ActorExtension.ActorExtraData extra = actor.GetOrCreate();
        if (extra.legion_size <= 0f) return;
        float lost = LegionAlive(actor) - 1f;
        extra.legion_size = extra.legion_full = 0f;
        extra.legion_population = null;
        extra.legion_home_city_id = -1L;
        CityPopulationData data = actor.city == null ? null : Get(actor.city);
        if (data != null && lost > 0f) data.levied = Mathf.Max(0f, data.levied - lost);
    }

    // 重算本城在外军团的人数(士兵本人已作为实体单位计入，这里只记其余的人)。士兵战死后不再出现在城里，
    // 他的军团也随之从人口里消失
    public static void UpdateLegions(City city)
    {
        CityPopulationData data = Get(city);
        if (data == null || city.units == null) return;
        float levied = 0f;
        foreach (Actor actor in city.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
            ActorExtension.ActorExtraData extra = actor.GetOrCreate();
            if (extra.legion_size <= 0f || (extra.legion_size <= 1f && extra.legion_full <= 1f)) continue;
            // 旧版生成的征召兵年龄是刚出生，补成服役年龄
            if (!actor.isAdult()) AssignAdultAge(actor, 18, 35);
            ReinforceLegion(actor, actor.kingdom ?? city.kingdom);
            levied += LegionAlive(actor) - 1f;
        }
        data.levied = levied;
    }

    // 按人口组生成一个具体的人：物种、文化、理念取自人口组，加入本城与本国；soldier 为 true 时直接成为士兵
    public static Actor SpawnFromGroup(City city, PopGroup group, bool soldier)
    {
        // 征召兵直接在朝向敌国的边境集结，找不到边境就在城里
        WorldTile tile = (soldier ? FrontTile(city.kingdom) : null) ?? PickTile(city);
        if (tile == null) return null;
        Actor actor = CreateUnit(group.species, tile, city);
        if (actor?.data == null) return null;
        Culture culture = CultureService.GetNativeCultureObject(group.culture);
        if (culture != null) actor.setCulture(culture);
        actor.joinKingdom(city.kingdom);
        actor.joinCity(city);
        IdeologyPopulationSystem.Set(actor, group.ideology);
        // 从人口里落成的都是成年人：士兵 18~35 岁(服役年龄)，平民(补位城主、官员) 25~55 岁；
        // 寿命短的物种按寿命折算，并保证已经成年
        AssignAdultAge(actor, soldier ? 18 : 25, soldier ? 35 : 55);
        KeepFedAndAwake(actor);
        if (soldier)
        {
            actor.setProfession(UnitProfession.Warrior);
            actor.SetSocialClass(SocialClass.Army);
            EquipLevy(actor, city);
        }
        else
        {
            actor.SetSocialClass(group.social_class);
            // 从人口里生成的人自立一个氏族(按文化取姓)，不会被并进别人的氏族、冒用别人的姓
            if (!actor.hasClan())
            {
                try
                {
                    World.world.clans.newClan(actor, true);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft] 无小人模式生成平民建氏族失败: {exception.Message}");
                }
            }
        }
        return actor;
    }

    // 征召兵的装备：武器、头盔、铠甲、靴子逐件发放——先发本城库存里的，库存没有就用本城资源当场打造
    // (原版打造流程：按文化偏好挑本城资源够得上、已研究的最好装备，扣本城资源)，打造的钱由国库出。
    // 资源或国库不够时那一件就空着，发完为止
    private static readonly EquipmentType[] LevyGear =
        { EquipmentType.Weapon, EquipmentType.Helmet, EquipmentType.Armor, EquipmentType.Boots };

    private static void EquipLevy(Actor actor, City city)
    {
        if (actor?.equipment == null || city?.data == null) return;
        try
        {
            if (!actor.understandsHowToUseItems()) return;
            Kingdom treasury = city.kingdom;
            string maker = city.data.name ?? "";
            foreach (EquipmentType type in LevyGear)
            {
                List<long> stock = city.getEquipmentList(type);
                if (stock != null && stock.Count > 0 && City.giveItem(actor, stock, city)) continue;
                int budget = treasury == null ? 0 : treasury.GetMoney();
                if (budget <= 0) continue;
                int saved = actor.data.money;
                actor.data.money = budget;
                bool made = ItemCrafting.craftItem(actor, maker, type, Mathf.Max(1, actor.asset.item_making_skill), city);
                int spent = Mathf.Max(0, budget - actor.data.money);
                actor.data.money = saved;
                if (made && spent > 0) treasury.SubMoney(spent, TreasuryCategory.Military);
            }
            actor.setStatsDirty();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式征召兵配发装备失败: {exception.Message}");
        }
    }

    private static void AssignAdultAge(Actor actor, int min, int max)
    {
        try
        {
            float lifespan = actor.stats?["lifespan"] ?? 0f;
            if (lifespan > 0f && lifespan < 80f)
            {
                min = Mathf.Max(1, Mathf.RoundToInt(min * lifespan / 80f));
                max = Mathf.Max(min, Mathf.RoundToInt(max * lifespan / 80f));
            }
            int age = UnityEngine.Random.Range(min, max + 1);
            VirtualGenealogySystem.SetAge(actor, age);
            for (int guard = 0; !actor.isAdult() && guard < 20; guard++)
                VirtualGenealogySystem.SetAge(actor, ++age);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式设定年龄失败: {exception.Message}");
        }
    }

    // 起事首领(无小人模式)：民变、农民起义要有个带头的平民，城里没有平民实体时从背景人口里生成一人，优先农民
    public static Actor SpawnRebelLeader(City city)
    {
        if (!AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null) return null;
        PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                           !string.IsNullOrEmpty(candidate.species) &&
                                                           candidate.social_class == SocialClass.Peasant)
                         ?? DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                              !string.IsNullOrEmpty(candidate.species));
        if (group == null) return null;
        Actor actor = SpawnFromGroup(city, group, soldier: false);
        if (actor != null) RemoveBackground(group, 1f);
        return actor;
    }

    // 执笔的读书人(无小人模式)：写书不需要实体居民，但书要有作者。城里没有合适的实体单位时，
    // 从人口里请一位读书人(优先官僚、市民、商人、贵族、地主这些识字阶层)落成实体执笔，书署他的名字；
    // 写完他照常是普通人，下次并入时回到人口数据里
    public static Actor SpawnScholar(City city)
    {
        if (!AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null) return null;
        PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                           !string.IsNullOrEmpty(candidate.species) &&
                                                           candidate.social_class is SocialClass.Officer or SocialClass.Citizen or
                                                               SocialClass.Merchant or SocialClass.Noble or SocialClass.Landlord)
                         ?? DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                              !string.IsNullOrEmpty(candidate.species));
        if (group == null) return null;
        Actor actor = SpawnFromGroup(city, group, soldier: false);
        if (actor == null) return null;
        RemoveBackground(group, 1f);
        if (actor.language == null && city.language != null) actor.setLanguage(city.language);
        if (actor.culture == null && city.culture != null) actor.setCulture(city.culture);
        if (actor.religion == null && city.religion != null) actor.setReligion(city.religion);
        return actor;
    }

    // 识字人口(户)：官僚与市民阶层的背景人口折成户数
    public static int LiterateHouseholds(City city)
    {
        if (!AbstractPopulationEnabled) return 0;
        CityPopulationData data = Get(city);
        if (data?.groups == null) return 0;
        float literate = 0f;
        foreach (PopGroup group in data.groups)
            if (group.social_class is SocialClass.Officer or SocialClass.Citizen) literate += group.Background;
        return Mathf.RoundToInt(literate / PeoplePerSlot(city));
    }

    // 从背景人口中生成一名成年平民(物种、文化、理念、阶层取自抽中的人口组)，并从背景人口里扣掉，总人口不变
    public static Actor SpawnCivilian(City city)
    {
        if (!AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null) return null;
        PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                           !string.IsNullOrEmpty(candidate.species));
        if (group == null) return null;
        Actor actor = SpawnFromGroup(city, group, soldier: false);
        if (actor != null) RemoveBackground(group, 1f);
        return actor;
    }

    // 官职空缺且找不到合适人选时(无小人模式)，从官职所在城市(否则本国首都)的人口中生成一人。
    // 王位不在此列：君主空缺走继承制度，不能凭空生成
    public static Actor SpawnForOffice(OfficeObject office, Kingdom kingdom)
    {
        if (!AbstractPopulationEnabled || office == null) return null;
        if (office.is_local && office.meta_object is Kingdom) return null;
        City city = office.meta_object as City ?? (office.meta_object as Army)?._city ?? kingdom?.capital;
        if (city == null || city.isRekt() || city.kingdom == null) return null;
        Actor actor = SpawnCivilian(city);
        return actor != null && actor.CanServeOffice(kingdom ?? city.kingdom) ? actor : null;
    }

    // 落成一名指定物种/文化的人(虚拟族人用)：优先从同物种同文化的背景人口里扣，其次同物种，
    // 都没有就直接生成(人口多出一人)
    public static Actor SpawnPerson(City city, string species, string culture)
    {
        if (city?.data == null || city.isRekt() || city.kingdom == null || string.IsNullOrEmpty(species)) return null;
        PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f && candidate.species == species &&
                                                           candidate.culture == (culture ?? ""))
                         ?? DrawBackground(city, candidate => candidate.Background >= 1f && candidate.species == species);
        PopGroup template = group ?? new PopGroup
        {
            social_class = SocialClass.Peasant, culture = culture ?? "", species = species,
            ideology = IdeologyPopulationSystem.GetDominant(city)
        };
        Actor actor = SpawnFromGroup(city, template, soldier: false);
        if (actor != null && group != null) RemoveBackground(group, 1f);
        return actor;
    }

    // 从背景人口里减去一名指定物种/文化的人(虚拟族人身故)
    public static void RemovePerson(City city, string species, string culture)
    {
        CityPopulationData data = Get(city);
        if (data?.groups == null) return;
        PopGroup group = data.groups.FirstOrDefault(candidate => candidate.Background >= 1f &&
                                                                 candidate.species == species && candidate.culture == (culture ?? ""))
                         ?? data.groups.FirstOrDefault(candidate => candidate.Background >= 1f && candidate.species == species)
                         ?? data.groups.FirstOrDefault(candidate => candidate.Background >= 1f);
        RemoveBackground(group, 1f);
    }

    private static WorldTile PickTile(City city)
    {
        if (city.zones == null || city.zones.Count == 0) return null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            TileZone zone = city.zones[UnityEngine.Random.Range(0, city.zones.Count)];
            if (zone?.centerTile != null) return zone.centerTile;
        }
        return null;
    }

    // 优先用带亚种参数的 createNewUnit 重载，给新兵用本城的主要亚种(否则原版可能为每个新兵新建亚种)
    private static Actor CreateUnit(string species, WorldTile tile, City city)
    {
        ResolveSpawn();
        Subspecies subspecies = null;
        try
        {
            subspecies = city.getMainSubspecies();
        }
        catch
        {
            // 取不到主要亚种就交给原版决定
        }
        if (_createWithSubspecies != null && subspecies != null)
        {
            try
            {
                ParameterInfo[] parameters = _createWithSubspecies.GetParameters();
                object[] args = new object[parameters.Length];
                bool idSet = false, tileSet = false;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type type = parameters[i].ParameterType;
                    if (type == typeof(string) && !idSet) { args[i] = species; idSet = true; }
                    else if (type == typeof(WorldTile) && !tileSet) { args[i] = tile; tileSet = true; }
                    else if (type == typeof(Subspecies)) args[i] = subspecies;
                    else if (parameters[i].HasDefaultValue) args[i] = parameters[i].DefaultValue;
                    else args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
                }
                if (_createWithSubspecies.Invoke(World.world.units, args) is Actor created) return created;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 征召兵按亚种生成失败，改用默认生成: {exception.Message}");
                _createWithSubspecies = null;
            }
        }
        return World.world.units.createNewUnit(species, tile);
    }

    private static void ResolveSpawn()
    {
        if (_spawnResolved) return;
        _spawnResolved = true;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ActorManager)))
        {
            if (method.Name != nameof(ActorManager.createNewUnit) || method.ReturnType != typeof(Actor)) continue;
            ParameterInfo[] parameters = method.GetParameters();
            bool hasSubspecies = false, hasTile = false;
            foreach (ParameterInfo parameter in parameters)
            {
                if (parameter.ParameterType == typeof(Subspecies)) hasSubspecies = true;
                if (parameter.ParameterType == typeof(WorldTile)) hasTile = true;
            }
            if (hasSubspecies && hasTile)
            {
                _createWithSubspecies = method;
                break;
            }
        }
    }

    #endregion

    #region 背景人口增减(供无小人模式的生成、死亡、迁移使用)

    // 往城里加入一批背景人口(例如迁入、或无小人模式下把普通单位并回数据)
    public static void AddBackground(City city, SocialClass socialClass, string culture, string species,
        PartyIdeology ideology, float amount)
    {
        CityPopulationData data = Get(city);
        if (data == null || amount <= 0f) return;
        PopGroup group = Find(data.groups, socialClass, culture ?? "", species ?? "", ideology);
        if (group == null)
        {
            group = new PopGroup
            {
                social_class = socialClass, culture = culture ?? "", species = species ?? "", ideology = ideology
            };
            data.groups.Add(group);
        }
        group.size += amount;
    }

    // 从背景人口中扣除一批人(死亡、迁出，或按需生成实体单位时从数据中"落成"一个人)，返回实际扣除的人数
    public static float RemoveBackground(PopGroup group, float amount)
    {
        if (group == null || amount <= 0f) return 0f;
        float removed = Mathf.Min(amount, group.Background);
        group.size -= removed;
        return removed;
    }

    // 按背景人口加权随机抽一个人口组；filter 为空表示不限条件。用于按需生成单位时决定这个人的阶层、文化与理念
    public static PopGroup DrawBackground(City city, Func<PopGroup, bool> filter = null)
    {
        CityPopulationData data = Get(city);
        if (data == null) return null;
        float total = 0f;
        foreach (PopGroup group in data.groups)
            if (filter == null || filter(group)) total += group.Background;
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (PopGroup group in data.groups)
        {
            if (filter != null && !filter(group)) continue;
            roll -= group.Background;
            if (roll <= 0f) return group;
        }
        return null;
    }

    #endregion

    #region 背景人口的理念变化

    // 外力说服(藏书、思想引入)对背景人口的作用：每个阶层按 chance 的比例改信 target。返回改信的人数
    public static int ConvertBackground(City city, PartyIdeology target, Func<SocialClass, float> chance)
    {
        if (!AbstractPopulationEnabled || chance == null) return 0;
        CityPopulationData data = Get(city);
        if (data?.groups == null) return 0;
        var moves = new List<(PopGroup from, float amount)>();
        foreach (PopGroup group in data.groups)
        {
            if (group.ideology == target) continue;
            float amount = group.Background * Mathf.Clamp01(chance(group.social_class));
            if (amount > 0f) moves.Add((group, amount));
        }
        float moved = 0f;
        foreach ((PopGroup from, float amount) in moves)
        {
            float removed = RemoveBackground(from, amount);
            if (removed <= 0f) continue;
            AddBackground(city, from.social_class, from.culture, from.species, target, removed);
            moved += removed;
        }
        return Mathf.RoundToInt(moved);
    }

    // 背景人口向同阶层名人的理念分布靠拢：按(阶层, 文化, 物种)分桶，桶内背景总人数不变，
    // 只按 rate 的比例把理念构成换成该阶层名人的构成。没有名人的阶层保持原样
    private static void DriftBackgroundTowardNamed(CityPopulationData data, float rate)
    {
        if (data?.groups == null || rate <= 0f) return;
        var namedByClass = new Dictionary<SocialClass, Dictionary<PartyIdeology, float>>();
        foreach (PopGroup group in data.groups)
        {
            if (group.named <= 0) continue;
            if (!namedByClass.TryGetValue(group.social_class, out Dictionary<PartyIdeology, float> byIdeology))
                namedByClass[group.social_class] = byIdeology = new Dictionary<PartyIdeology, float>();
            byIdeology.TryGetValue(group.ideology, out float count);
            byIdeology[group.ideology] = count + group.named;
        }
        if (namedByClass.Count == 0) return;

        var buckets = new Dictionary<(SocialClass, string, string), float>();
        foreach (PopGroup group in data.groups)
        {
            if (!namedByClass.ContainsKey(group.social_class)) continue;
            float background = group.Background;
            if (background <= 0f) continue;
            var key = (group.social_class, group.culture ?? "", group.species ?? "");
            buckets.TryGetValue(key, out float total);
            buckets[key] = total + background;
            // 先按 rate 缩减原有构成，再把缩减掉的人按名人构成补回
            group.size -= background * rate;
        }
        foreach (KeyValuePair<(SocialClass, string, string), float> bucket in buckets)
        {
            (SocialClass socialClass, string culture, string species) = bucket.Key;
            Dictionary<PartyIdeology, float> target = namedByClass[socialClass];
            float namedTotal = 0f;
            foreach (float count in target.Values) namedTotal += count;
            if (namedTotal <= 0f) continue;
            float pool = bucket.Value * rate;
            foreach (KeyValuePair<PartyIdeology, float> pair in target)
            {
                PopGroup group = Find(data.groups, socialClass, culture, species, pair.Key);
                if (group == null)
                {
                    group = new PopGroup
                    {
                        social_class = socialClass, culture = culture, species = species, ideology = pair.Key
                    };
                    data.groups.Add(group);
                }
                group.size += pool * pair.Value / namedTotal;
            }
        }
        data.groups.RemoveAll(group => group.size < MinimumGroupSize && group.named <= 0);
    }

    #endregion

    #region 给各系统的统计补上背景人口

    #region 保底人口与城主补位(无小人模式)

    // 每座城至少保有这么多户背景人口：城里永远有人，可以随时补位城主、官员
    private const float MinimumHouseholds = 1f;
    // 城主补位失败后隔多久(世界时间秒)再试，免得每帧都跑选官
    private const double LeaderRetrySeconds = 1d;
    private static readonly Dictionary<City, double> LeaderRetryAt = new();

    // 背景人口不足保底时补足：按现有人口组(没有背景人口就按名人)的构成补，一个人口组都没有时按城市的物种、主流文化补
    public static void EnsureFloor(City city)
    {
        if (StateSettlementSystem.AwaitingPopulation(city)) return;
        if (!AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null ||
            city.kingdom.wild) return;
        CityPopulationData data = Get(city);
        if (data == null) return;
        if (data.spontaneous_settlement) return;
        // 真正的空城等待真实移民。刚开启模式、尚未校准但仍有实体居民的城市继续原有初始化。
        if (GetTotal(city) < 1f && city.units?.Any(actor => actor != null && actor.city == city &&
            !actor.isRekt() && actor.isAlive()) != true) return;
        float floor = MinimumHouseholds * PeoplePerSlot(city);
        float background = GetBackgroundTotal(city);
        if (background >= floor) return;
        float missing = floor - background;
        PopGroup template = DrawBackground(city, group => !string.IsNullOrEmpty(group.species));
        if (template == null)
            foreach (PopGroup group in data.groups)
                if (group.named > 0 && !string.IsNullOrEmpty(group.species) &&
                    (template == null || group.named > template.named))
                    template = group;
        if (template != null)
        {
            AddBackground(city, template.social_class, template.culture, template.species, template.ideology, missing);
            return;
        }
        string species = null;
        try
        {
            species = city.getSpecies();
        }
        catch
        {
            // 取不到物种就不补
        }
        if (string.IsNullOrEmpty(species)) return;
        AddBackground(city, SocialClass.Peasant, CultureService.GetMainCulture(city, initialize: false) ?? "", species,
            IdeologyPopulationSystem.GetDominant(city), missing);
    }

    // 城主空缺时立即补位(每帧由城市更新调用)：先按官职选人，选不到就从背景人口里生成一人接任
    public static void EnsureLeader(City city)
    {
        if (!AbstractPopulationEnabled || city?.data == null || city.isRekt() || city.kingdom == null ||
            city.kingdom.wild || city.hasLeader()) return;
        if (World.world == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        double now = World.world.getCurWorldTime();
        if (LeaderRetryAt.TryGetValue(city, out double retry) && now >= 0d && now < retry) return;
        LeaderRetryAt[city] = now + LeaderRetrySeconds;
        try
        {
            OfficeObject office = city.GetOffice();
            if (office != null)
            {
                office.meta_object = city;
                office.Select(city.kingdom, "城市");
                if (city.hasLeader() && !IsSoldier(city.leader)) return;
                // 选中的是征召兵(城里只剩士兵时)：不让军团统领兼任城主
                if (city.hasLeader()) city.removeLeader();
            }
            EnsureFloor(city);
            Actor leader = SpawnCivilian(city);
            if (leader != null && !city.hasLeader()) city.setLeader(leader, true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式城主补位失败({city.data?.name}): {exception.Message}");
        }
    }

    #endregion

    // 迁出一部分背景人口到另一座城(建新城时的移民)：各组按同一比例迁出，构成不变。返回迁出的人数
    public static float TransferBackground(City from, City to, float share)
    {
        if (!AbstractPopulationEnabled || from == null || to == null || from == to || share <= 0f) return 0f;
        CityPopulationData data = Get(from);
        if (data?.groups == null) return 0f;
        float totalBefore = GetBackgroundTotal(from);
        var moves = new List<(PopGroup group, float amount)>();
        foreach (PopGroup group in data.groups)
        {
            float amount = group.Background * Mathf.Clamp01(share);
            if (amount > 0f) moves.Add((group, amount));
        }
        float moved = 0f;
        foreach ((PopGroup group, float amount) in moves)
        {
            float removed = RemoveBackground(group, amount);
            if (removed <= 0f) continue;
            AddBackground(to, group.social_class, group.culture, group.species, group.ideology, removed);
            moved += removed;
        }
        CityAssetSettlement.MoveSavingsWithMigrants(from, to, moved, totalBefore);
        return moved;
    }

    // 国家移民只迁真实背景平民，按同一比例保留文化、物种、阶层和理念。
    public static float CivilianBackground(City city)
    {
        CityPopulationData data = Get(city);
        if (data?.groups == null) return 0f;
        float total = 0f;
        foreach (PopGroup group in data.groups)
            if (group != null && group.social_class != SocialClass.Army && group.social_class != SocialClass.Officer)
                total += group.Background;
        return total;
    }

    public static float TransferCivilianBackground(City from, City to, float amount)
    {
        if (from == null || to == null || from == to || amount <= 0f) return 0f;
        CityPopulationData source = Get(from);
        CityPopulationData destination = Get(to);
        float total = CivilianBackground(from);
        if (source?.groups == null || destination == null || total <= 0f) return 0f;
        PopulationParallelSystem.Discard(from);
        PopulationParallelSystem.Discard(to);
        float totalBefore = GetBackgroundTotal(from);
        float share = Mathf.Min(1f, amount / total);
        float moved = 0f;
        foreach (PopGroup group in source.groups)
        {
            if (group == null || group.social_class == SocialClass.Army || group.social_class == SocialClass.Officer) continue;
            float removed = RemoveBackground(group, group.Background * share);
            if (removed <= 0f) continue;
            AddBackground(to, group.social_class, group.culture, group.species, group.ideology, removed);
            moved += removed;
        }
        CityAssetSettlement.MoveSavingsWithMigrants(from, to, moved, totalBefore);
        InvalidateHouseholdCaches();
        return moved;
    }

    public static void InvalidateHouseholdCaches() => _frameCacheFrame = -1;

    // 军团兵力(真实人数)：本国各城征召在外的军团人数 + 实体士兵
    public static int LegionPeople(Kingdom kingdom)
    {
        if (kingdom?.cities == null) return 0;
        float total = kingdom.countTotalWarriors();
        foreach (City city in kingdom.cities)
            if (city != null && !city.isRekt()) total += Get(city)?.levied ?? 0f;
        return Mathf.RoundToInt(total);
    }

    // 城里的背景人口(没开无小人模式时为 0)
    public static int BackgroundCount(City city) =>
        AbstractPopulationEnabled ? Mathf.RoundToInt(GetBackgroundTotal(city) + (Get(city)?.levied ?? 0f)) : 0;

    // 一个住房位住多少人(没开无小人模式时为 1)
    public static int PeoplePerSlot(City city) =>
        !AbstractPopulationEnabled ? 1 : ModernStability.IsModern(city?.kingdom) ? ModernPeoplePerSlot : PremodernPeoplePerSlot;

    // 背景人口折成"户"：原版机制和按人口计算的模组公式(科研、开销)用这个尺度
    public static int BackgroundHouseholds(City city) =>
        AbstractPopulationEnabled ? Mathf.CeilToInt(GetBackgroundTotal(city) / PeoplePerSlot(city)) : 0;

    // 按户计的城市人口：实体单位 + 背景人口的户数
    // 户数、军制在同一帧里会被反复问(原版城市状态、兵额、征兵……)，每帧只算一次
    private static int _frameCacheFrame = -1;
    private static readonly Dictionary<City, int> CityHouseholdsCache = new();
    private static readonly Dictionary<Kingdom, int> KingdomHouseholdsCache = new();
    private static readonly Dictionary<Kingdom, ArmyDoctrine> DoctrineCache = new();
    private static readonly Dictionary<Kingdom, int> MobilizableReserveCache = new();

    private static void EnsureFrameCache()
    {
        int frame = Time.frameCount;
        if (frame == _frameCacheFrame) return;
        _frameCacheFrame = frame;
        CityHouseholdsCache.Clear();
        KingdomHouseholdsCache.Clear();
        DoctrineCache.Clear();
        MobilizableReserveCache.Clear();
    }

    public static int Households(City city)
    {
        if (city == null) return 0;
        EnsureFrameCache();
        if (CityHouseholdsCache.TryGetValue(city, out int cached)) return cached;
        int units = 0;
        if (city.units != null)
            foreach (Actor actor in city.units)
                if (actor != null && !actor.isRekt() && actor.isAlive()) units++;
        int result = units + BackgroundHouseholds(city);
        CityHouseholdsCache[city] = result;
        return result;
    }

    public static int Households(Kingdom kingdom)
    {
        if (kingdom?.cities == null) return 0;
        EnsureFrameCache();
        if (KingdomHouseholdsCache.TryGetValue(kingdom, out int cached)) return cached;
        int total = 0;
        foreach (City city in kingdom.cities)
            if (city != null && !city.isRekt()) total += Households(city);
        KingdomHouseholdsCache[kingdom] = total;
        return total;
    }

    public static int Households(Layer.Empire empire)
    {
        if (empire?.kingdoms_list == null) return 0;
        int total = 0;
        foreach (Kingdom kingdom in empire.kingdoms_list)
            if (kingdom != null && !kingdom.isRekt()) total += Households(kingdom);
        return total;
    }

    // 原尺度：显示用真实人数，但原版按人口做判断的逻辑(能否繁殖、要不要移民、灾害与谋划的门槛……)
    // 在这个范围内读到的城市/王国人口和人口上限都按"户"计，和没开无小人模式时同一个量级(见 NoCommonersPatch)
    [ThreadStatic] private static int _vanillaScale;
    public static bool VanillaScale => _vanillaScale > 0;
    public static void EnterVanillaScale() => _vanillaScale++;
    public static void ExitVanillaScale()
    {
        if (_vanillaScale > 0) _vanillaScale--;
    }

    // 这些王国名下的城市(去重，跳过已灭亡和兼容模组接管的王国)
    public static IEnumerable<City> CitiesOf(IEnumerable<Kingdom> kingdoms)
    {
        if (kingdoms == null) yield break;
        var seen = new HashSet<City>();
        foreach (Kingdom kingdom in kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.cities == null ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) continue;
            foreach (City city in kingdom.cities)
                if (city?.data != null && !city.isRekt() && seen.Add(city)) yield return city;
        }
    }

    // 把这些城市的背景人口按 keyOf 分类后加进按实体单位数出来的 counts。没开无小人模式时什么都不做
    public static void AddBackgroundCounts<TKey>(IEnumerable<City> cities, Dictionary<TKey, int> counts,
        Func<PopGroup, TKey> keyOf, Func<PopGroup, bool> filter = null)
    {
        if (!AbstractPopulationEnabled || cities == null || counts == null) return;
        var sums = new Dictionary<TKey, float>();
        foreach (City city in cities)
        {
            CityPopulationData data = Get(city);
            if (data?.groups == null) continue;
            foreach (PopGroup group in data.groups)
            {
                float background = group.Background;
                if (background <= 0f || filter != null && !filter(group)) continue;
                TKey key = keyOf(group);
                sums.TryGetValue(key, out float sum);
                sums[key] = sum + background;
            }
        }
        foreach (KeyValuePair<TKey, float> pair in sums)
        {
            int amount = Mathf.RoundToInt(pair.Value);
            if (amount <= 0) continue;
            counts.TryGetValue(pair.Key, out int count);
            counts[pair.Key] = count + amount;
        }
    }

    public static void AddBackgroundCounts<TKey>(City city, Dictionary<TKey, int> counts,
        Func<PopGroup, TKey> keyOf, Func<PopGroup, bool> filter = null)
    {
        // 这个重载在热路径上(每座城的理念统计)，没开无小人模式时不分配任何东西
        if (city == null || !AbstractPopulationEnabled) return;
        AddBackgroundCounts(new[] { city }, counts, keyOf, filter);
    }

    #endregion

    #region 查询

    public static float GetTotal(City city) => GetTotal(Get(city));

    public static float GetTotal(CityPopulationData data)
    {
        if (data?.groups == null) return 0f;
        float total = 0f;
        foreach (PopGroup group in data.groups) total += group.size;
        return total;
    }

    public static float GetBackgroundTotal(City city)
    {
        CityPopulationData data = Get(city);
        if (data?.groups == null) return 0f;
        float total = 0f;
        foreach (PopGroup group in data.groups) total += group.Background;
        return total;
    }

    public static Dictionary<SocialClass, float> GetClassCounts(City city) =>
        Sum(city, group => group.social_class);

    public static Dictionary<PartyIdeology, float> GetIdeologyCounts(City city) =>
        Sum(city, group => group.ideology);

    public static Dictionary<string, float> GetCultureCounts(City city) =>
        Sum(city, group => group.culture ?? "");

    public static Dictionary<string, float> GetSpeciesCounts(City city) =>
        Sum(city, group => group.species ?? "");

    // 把人数换算成占比(0~1)
    public static Dictionary<TKey, float> ToShares<TKey>(Dictionary<TKey, float> counts)
    {
        var shares = new Dictionary<TKey, float>();
        float total = 0f;
        foreach (float value in counts.Values) total += value;
        foreach (KeyValuePair<TKey, float> pair in counts)
            shares[pair.Key] = total > 0f ? pair.Value / total : 0f;
        return shares;
    }

    private static Dictionary<TKey, float> Sum<TKey>(City city, Func<PopGroup, TKey> keyOf)
    {
        var result = new Dictionary<TKey, float>();
        CityPopulationData data = Get(city);
        if (data?.groups == null) return result;
        foreach (PopGroup group in data.groups)
        {
            TKey key = keyOf(group);
            result.TryGetValue(key, out float value);
            result[key] = value + group.size;
        }
        return result;
    }

    private static PopGroup Find(List<PopGroup> groups, SocialClass socialClass, string culture, string species,
        PartyIdeology ideology)
    {
        foreach (PopGroup group in groups)
            if (group.SameKind(socialClass, culture, species, ideology)) return group;
        return null;
    }

    #endregion
}
