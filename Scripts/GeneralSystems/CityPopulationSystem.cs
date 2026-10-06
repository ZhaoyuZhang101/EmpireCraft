using System;
using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
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
    // 背景人口在住房充足时的年自然增长率，以及超出住房上限时每年流失的超出部分比例
    private const float BackgroundGrowthRate = 0.03f;
    private const float OvercrowdingLossRate = 0.25f;
    // 人数低于这个值的人口组在结算后删除，避免碎片组越积越多
    private const float MinimumGroupSize = 0.05f;

    private static readonly Queue<City> PendingCities = new();
    private static object _world;
    private static double _lastPass = -1d;

    // 无小人模式开关。第三步接入世界法则；在此之前恒为 false，背景人口始终为 0
    public static bool AbstractPopulationEnabled => false;

    // 最近一次年度结算的统计，用于日志和性能验证
    public static int LastPassCities { get; private set; }
    public static double LastPassMilliseconds { get; private set; }
    private static double _passMilliseconds;
    private static int _passCities;

    public static void ResetWorldState()
    {
        PendingCities.Clear();
        _world = World.world;
        _lastPass = -1d;
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
        Census(city, data, keepBackground: AbstractPopulationEnabled);
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

    // 背景人口的自然增减：住房有余时按增长率增长(不超过空余住房)，超出住房上限时流失一部分超出人口
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

        float capacity = Mathf.Max(0, city.getPopulationMaximum());
        float total = GetTotal(data);
        float change = total < capacity
            ? Mathf.Min(background * BackgroundGrowthRate * years, capacity - total)
            : -Mathf.Min(background, (total - capacity) * OvercrowdingLossRate * years);
        if (Mathf.Abs(change) < 0.001f) return;
        // 增减按各组背景人口的比例分摊
        float ratio = change / background;
        foreach (PopGroup group in data.groups)
            group.size = Mathf.Max(group.named, group.size + group.Background * ratio);
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
