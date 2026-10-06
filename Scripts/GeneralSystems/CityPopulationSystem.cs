using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
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
    // 基础出生率/死亡率：吃得饱、住得下、不打仗时，每年自然增长约 2%
    private const float BaseBirthRate = 0.04f;
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
    // 超出住房上限的人每年有这么多比例离开或死去
    private const float OvercrowdingLossRate = 0.25f;

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
    private static double _lastFoldPass = -1d;
    private static object _world;
    private static double _lastPass = -1d;

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
        PendingCities.Clear();
        PendingFolds.Clear();
        _world = World.world;
        _lastPass = -1d;
        _lastFoldPass = -1d;
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
        // 虚拟族人的年度身故：关掉无小人模式后已有的虚拟族人仍会老去
        VirtualGenealogySystem.Tick();
        if (AbstractPopulationEnabled) TickFolds(world, now);
        else PendingFolds.Clear();
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
        while (PendingCities.Count > 0 && watch.Elapsed.TotalMilliseconds < SliceBudgetMs)
        {
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
        }
    }

    // 立即结算一座城市(界面打开、读档修复等需要最新数据的场合)
    public static void UpdateCity(City city, double now)
    {
        CityPopulationData data = Get(city);
        if (data == null) return;
        if (AbstractPopulationEnabled) GrowBackground(city, data, now);
        // 背景人口始终保留：关掉开关时只是冻结(不增减、不计入统计)，重新打开后接着用
        Census(city, data, keepBackground: true);
        if (AbstractPopulationEnabled) DriftBackgroundTowardNamed(data, BackgroundOpinionDrift);
        data.last_census = now;
    }

    // 用城里的实体单位校准人口组。keepBackground 为 false 时丢弃背景人口，人口组完全等于单位统计；
    // 为 true 时保留每组的背景人口，只把实体单位部分换成这次数到的人数
    public static void Census(City city, CityPopulationData data, bool keepBackground)
    {
        var counted = new Dictionary<(SocialClass, string, string, PartyIdeology), int>();
        int named = 0;
        if (city.units != null)
        {
            for (int i = 0; i < city.units.Count; i++)
            {
                Actor actor = city.units[i];
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
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
                if (background < MinimumGroupSize) continue;
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
        float years = Mathf.Clamp(Date.getYearsSince(data.last_growth), 0, 5);
        if (years <= 0f) return;
        data.last_growth = now;

        float background = 0f;
        foreach (PopGroup group in data.groups) background += group.Background;
        if (background <= 0f) return;

        GrowthFactors factors = GetGrowthFactors(city, data);
        float change = background * (factors.BirthRate - factors.DeathRate) * years;
        // 出生不能超过空余住房
        if (change > 0f) change = Mathf.Min(change, Mathf.Max(0f, factors.Capacity - factors.Total));
        if (factors.Total > factors.Capacity)
            change -= Mathf.Min(background, (factors.Total - factors.Capacity) * OvercrowdingLossRate * years);
        change = Mathf.Max(change, -background);
        if (Mathf.Abs(change) < 0.001f) return;
        // 增减按各组背景人口的比例分摊
        float ratio = change / background;
        foreach (PopGroup group in data.groups)
            group.size = Mathf.Max(group.named, group.size + group.Background * ratio);
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
        float foodPerCapita = total > 0f ? food / total : ComfortFoodPerCapita;
        float prosperity = Mathf.Clamp01(IdeologyPopulationSystem.CachedEconomy(city).Prosperity);
        bool famine = foodPerCapita < FamineFoodPerCapita;
        bool war = city.GetOrCreate().OccupiedStatus?.Count > 0;

        float foodFactor = Mathf.Clamp(foodPerCapita / ComfortFoodPerCapita, 0f, MaxFoodBirthFactor);
        float housingFactor = capacity <= 0f ? 0f
            : Mathf.Clamp01((capacity - total) / Mathf.Max(1f, capacity * HousingSlowdownShare));
        float birth = BaseBirthRate * foodFactor * (1f - ProsperityBirthReduction * prosperity) * housingFactor;
        float death = BaseDeathRate * (1f - ProsperityDeathReduction * prosperity) +
                      (famine ? FamineDeathRate : 0f) + (war ? WarDeathRate : 0f);
        return new GrowthFactors(total, capacity, foodPerCapita, prosperity, birth, death, famine, war);
    }

    #endregion

    #region 并入普通人(无小人模式)

    // 每个游戏月把全部城市排队检查一次，分帧处理(与年度结算共用每帧时间上限)
    private static void TickFolds(MapBox world, double now)
    {
        if (PendingFolds.Count == 0)
        {
            bool due = _lastFoldPass < 0d || now < _lastFoldPass || Date.getMonthsSince(_lastFoldPass) >= FoldIntervalMonths;
            if (!due) return;
            _lastFoldPass = now;
            foreach (City city in world.cities)
                if (city?.data != null && !city.isRekt()) PendingFolds.Enqueue(city);
            if (PendingFolds.Count == 0) return;
        }
        using var timing = new PerfTimer("无小人模式并入普通人");
        Stopwatch watch = Stopwatch.StartNew();
        while (PendingFolds.Count > 0 && watch.Elapsed.TotalMilliseconds < SliceBudgetMs)
        {
            City city = PendingFolds.Dequeue();
            if (city?.data == null || city.isRekt() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) continue;
            try
            {
                FoldCommoners(city);
                RaiseLevies(city);
                PopulationEconomySystem.Settle(city, Get(city), now);
                // 并入/征召后立刻重数实体单位：刚并入的人已从"实体"挪到"背景"，上次校准后死去或新生的单位也一并更新，
                // 否则在下次年度校准前同一个人会被同时算作实体和背景。无小人模式下城里单位很少，开销很小
                Census(city, Get(city), keepBackground: true);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 无小人模式并入普通人失败({city.data?.name}): {exception.Message}");
            }
        }
    }

    // 名人：保留为实体单位的人(军人另算，见 IsSoldier)。君主、城主、官僚、贵族、地主，党派/派系成员，
    // 正在谋划的人、玩家收藏或镜头跟随的人
    public static bool IsNotable(Actor actor)
    {
        if (actor?.data == null || actor.isRekt()) return true;
        if (actor.isKing() || actor.isCityLeader()) return true;
        if (actor.isFavorite() || actor.isCameraFollowingUnit()) return true;
        if (actor.plot != null) return true;
        SocialClass socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
        if (socialClass == SocialClass.Noble || socialClass == SocialClass.Officer ||
            socialClass == SocialClass.Landlord) return true;
        if (actor.GetFaction() != null) return true;
        return false;
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
        bool atWar = IsAtWar(city);
        var commoners = new List<Actor>();
        var soldiers = new List<Actor>();
        foreach (Actor actor in city.units)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.city != city) continue;
            if (actor.asset == null || actor.asset.is_boat) continue;
            if (IsNotable(actor)) continue;
            if (IsSoldier(actor)) soldiers.Add(actor);
            else commoners.Add(actor);
        }

        // 城主空缺：从人口中当场生成一人出任(总人口不变)
        if (city.leader == null || city.leader.isRekt() || !city.leader.isAlive())
        {
            Actor leader = SpawnCivilian(city);
            if (leader != null) city.setLeader(leader, true);
        }

        // 留在地图上的人不用吃饭、不用睡觉：饥饿值补满，正在睡的叫醒
        foreach (Actor actor in city.units)
            if (actor?.data != null && !actor.isRekt() && actor.isAlive()) KeepFedAndAwake(actor);

        var toFold = new List<Actor>();
        if (!atWar && soldiers.Count > 1)
        {
            // 留一名将领：现任统领优先，其次带着部队的人
            soldiers.Sort((left, right) => GeneralRank(right).CompareTo(GeneralRank(left)));
            for (int i = 1; i < soldiers.Count; i++) toFold.Add(soldiers[i]);
        }
        // 劳动者不够(被征去当兵、死亡)时，从背景人口里补上
        for (int missing = KeptWorkersPerCity - commoners.Count; missing > 0; missing--)
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

        foreach (Actor actor in toFold)
        {
            SocialClass socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
            // 解散的士兵回到平民身份(按农民计入)，之后由同阶层思潮带着走
            if (socialClass == SocialClass.Army) socialClass = SocialClass.Peasant;
            string culture = CultureService.GetActorCulture(actor) ?? "";
            string species = actor.asset?.id ?? "";
            PartyIdeology ideology = IdeologyPopulationSystem.Get(actor);
            // 族谱里的人转为虚拟族人(仍在世，需要时再落成实体)，单位移除时不会被记为死亡
            VirtualGenealogySystem.Virtualize(actor, city);
            // 不计入死亡统计、不写收藏日志
            actor.die(true, AttackType.Other, false, false);
            AddBackground(city, socialClass, culture, species, ideology, 1f);
        }
        return toFold.Count;
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

    // 每次最多征召的人数，避免一帧里生成太多单位
    private const int MaxLeviesPerPass = 30;
    private static bool _spawnResolved;
    private static MethodInfo _createWithSubspecies;

    // 交战时按空余兵额从背景人口里当场生成士兵(在城里随机地块)，仗打完由 FoldCommoners 解散
    public static int RaiseLevies(City city)
    {
        if (!AbstractPopulationEnabled || city?.status == null || city.kingdom == null || !IsAtWar(city)) return 0;
        int need = Mathf.Min(MaxLeviesPerPass, city.status.warrior_slots - city.status.warriors_current);
        int raised = 0;
        for (int i = 0; i < need; i++)
        {
            PopGroup group = DrawBackground(city, candidate => candidate.Background >= 1f &&
                                                               !string.IsNullOrEmpty(candidate.species));
            if (group == null) break;
            Actor actor = SpawnFromGroup(city, group, soldier: true);
            if (actor == null) break;
            RemoveBackground(group, 1f);
            raised++;
        }
        return raised;
    }

    // 按人口组生成一个具体的人：物种、文化、理念取自人口组，加入本城与本国；soldier 为 true 时直接成为士兵
    public static Actor SpawnFromGroup(City city, PopGroup group, bool soldier)
    {
        WorldTile tile = PickTile(city);
        if (tile == null) return null;
        Actor actor = CreateUnit(group.species, tile, city);
        if (actor?.data == null) return null;
        Culture culture = CultureService.GetNativeCultureObject(group.culture);
        if (culture != null) actor.setCulture(culture);
        actor.joinKingdom(city.kingdom);
        actor.joinCity(city);
        IdeologyPopulationSystem.Set(actor, group.ideology);
        KeepFedAndAwake(actor);
        if (soldier)
        {
            actor.setProfession(UnitProfession.Warrior);
            actor.SetSocialClass(SocialClass.Army);
        }
        else actor.SetSocialClass(group.social_class);
        return actor;
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

    // 城里的背景人口(没开无小人模式时为 0)
    public static int BackgroundCount(City city) =>
        AbstractPopulationEnabled ? Mathf.RoundToInt(GetBackgroundTotal(city)) : 0;

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
