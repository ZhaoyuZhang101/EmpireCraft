using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 人口经济(无小人模式，参考维多利亚 3)：背景人口没有实体单位，不会自己去种地、干活、吃饭，
// 这里按人口数据替他们结算——每个游戏月随"并入普通人"一起结算一次。
//
//   劳动力 = 背景人口 × 劳动年龄占比
//   岗位   = 城里每座建成的非民居建筑提供若干岗位 + 每个地块提供若干农田岗位
//   就业率 = min(1, 岗位 / 劳动力)；没有岗位的人只有一小部分产出
//   产出   = 各阶层劳动力 × 就业系数 × 该阶层的人均产出：农民产粮，工人产金钱并推进施工，商人/市民产金钱
//   消耗   = 背景人口每人每年吃一份粮食，从城市仓库里扣；扣不够的部分记为缺粮
//
// 产出直接存进城市仓库，原版和本模组读仓库的地方(存粮、国库、建造)都能看到；
// 粮食不够时仓库见底，CityPopulationSystem 的生育死亡模型就会进入饥荒。
// 施工进度交给工人阶层：城里有在建的建筑时，按工人数推进，相当于背景人口去工地干活。
public static class PopulationEconomySystem
{
    private const float WorkingAgeShare = 0.6f;
    private const int JobsPerBuilding = 12;
    private const int FarmJobsPerZone = 4;
    // 没有岗位的劳动力仍有这一比例的产出(零工、自给自足)
    private const float UnemployedOutputShare = 0.25f;
    // 每人每年的产出
    private const float PeasantFoodPerYear = 2f;
    private const float LabourGoldPerYear = 0.5f;
    private const float LabourConstructionPerYear = 6f;
    // 其他阶层也出工盖房(农闲、徭役)，按这个比例折算；否则没有工人的新城永远盖不起民居，人口也就涨不上去
    private const float OtherConstructionPerYear = 2f;
    // 非农民阶层的自给口粮(菜园、渔猎)
    private const float OtherFoodPerYear = 0.5f;
    private const float MerchantGoldPerYear = 1.5f;
    private const float CitizenGoldPerYear = 1f;
    // 原料(每户每年)：工人伐木采石，城里有矿场时还能冶出金属；其他阶层农闲时砍柴，零星出一点。
    // 原版建房要木石、打造装备要木头和金属，没有实体居民去采集后由这里补上
    private const float LabourWoodPerYear = 1.5f;
    private const float LabourStonePerYear = 0.8f;
    private const float LabourMetalPerYearWithMine = 0.5f;
    private const float LabourMetalPerYear = 0.1f;
    private const float OtherWoodPerYear = 0.3f;
    // 每人每年吃掉的粮食
    private const float FoodPerPersonYear = 1f;
    // 每次结算最多扣多少份粮食(防止超大城市一次调用过多)
    private const int MaxFoodCallsPerSettle = 400;

    private static readonly string[] FoodCandidates = { "bread", "wheat", "berries", "meat", "fish" };
    private static string _foodId;
    private static bool _reflectionResolved;
    private static MethodInfo _getFoodItem;
    private static MethodInfo _eatFoodItem;
    private static MethodInfo _updateBuild;

    // 每个游戏月对一座城结算一次(由 CityPopulationSystem 的并入流程调用)
    public static void Settle(City city, CityPopulationData data, double now)
    {
        if (city?.data == null || data?.groups == null) return;
        if (data.last_economy < 0d || now < data.last_economy)
        {
            data.last_economy = now;
            return;
        }
        int months = Mathf.Clamp(Date.getMonthsSince(data.last_economy), 0, 24);
        if (months <= 0) return;
        data.last_economy = now;
        float years = months / 12f;
        ResolveReflection();

        var workforceByClass = new Dictionary<SocialClass, float>();
        float background = 0f;
        // 产出、吃粮、施工都按"户"结算(一户 = 一个住房位的人)，仓库装得下、数值和原版居民一个量级
        float perSlot = CityPopulationSystem.PeoplePerSlot(city);
        foreach (PopGroup group in data.groups)
        {
            float amount = group.Background / perSlot;
            if (amount <= 0f) continue;
            background += amount;
            workforceByClass.TryGetValue(group.social_class, out float workers);
            workforceByClass[group.social_class] = workers + amount * WorkingAgeShare;
        }
        // 农田自动耕作(开田、播种、收割)，粮食来自实际收割的麦子(见 FarmlandSystem)
        // 收成按粮食征收政策一部分交国家粮仓(见 GranarySystem)，其余留在本城
        float harvested = GranarySystem.Collect(city, FarmlandSystem.Settle(city, data));
        if (harvested > 0f) Deposit(city, data, _foodId ?? "wheat", harvested);
        if (background <= 0f) return;

        float workforce = background * WorkingAgeShare;
        float jobs = CountJobs(city);
        float employment = workforce <= 0f ? 0f : Mathf.Clamp01(jobs / workforce);
        float productivity = employment + (1f - employment) * UnemployedOutputShare;

        // 生产不看工人：原版按城里的建筑与周围地形给每座城算好了岗位(有风车才有农田岗位、有矿场才有矿工、
        // 树木灌木矿脉的多少决定樵夫采集采矿的岗位)，每个岗位按种类自动产出。另有自给口粮与商业收入
        float food = background * SubsistenceFoodPerYear * years + harvested;
        foreach ((CitizenJobAsset job, (string resource, float amount)[] outputs) in JobOutputs())
        {
            if (job == null) continue;
            int slots = city.jobs.countCurrentJobs(job);
            if (slots <= 0) continue;
            foreach ((string resource, float amount) in outputs)
            {
                float produced = slots * amount * years;
                if (IsFood(resource)) food += produced;
                Deposit(city, data, resource, produced);
            }
        }
        Deposit(city, data, _foodId ?? "berries", background * SubsistenceFoodPerYear * years);
        // 矿场固定产矿：每座建成的矿场每年产出固定数量，不看岗位和人口
        int mines = 0;
        if (city.buildings != null)
            foreach (Building building in city.buildings)
                if (building?.asset?.type == "type_mine" && !building.isUnderConstruction()) mines++;
        if (mines > 0)
            foreach ((string resource, float amount) in MineOutput)
                if (AssetManager.resources?.get(resource) != null) Deposit(city, data, resource, mines * amount * years);
        float gold = (Get(workforceByClass, SocialClass.Labour) * LabourGoldPerYear +
                      Get(workforceByClass, SocialClass.Merchant) * MerchantGoldPerYear +
                      Get(workforceByClass, SocialClass.Citizen) * CitizenGoldPerYear) * productivity * years;
        Deposit(city, data, "gold", gold);
        PayTaxes(city, data, workforceByClass, years);
        // 施工改由城市建设力推进(见 CityConstructionSystem)，不再按工人数
        float eaten = EatFood(city, data, background * FoodPerPersonYear * years, out float shortage);
        // 饥荒：国家粮仓开仓赈灾，调来的粮食补上缺口
        if (shortage > 0f)
        {
            float relief = GranarySystem.Relieve(city, shortage);
            if (relief > 0f)
            {
                Deposit(city, data, _foodId ?? "wheat", relief);
                shortage = Mathf.Max(0f, shortage - relief);
            }
        }

        data.last_jobs = jobs;
        data.last_workforce = workforce;
        data.last_food_output = years > 0f ? food / years : 0f;
        data.last_food_eaten = years > 0f ? eaten / years : 0f;
        data.last_food_shortage = years > 0f ? shortage / years : 0f;
        data.last_gold_output = years > 0f ? gold / years : 0f;
    }

    // 没有农田时每户每年的自给口粮(不够吃饱：一户一年吃 1 份)
    private const float SubsistenceFoodPerYear = 0.5f;
    private static List<(CitizenJobAsset, (string, float)[])> _jobOutputs;

    // 每个原版岗位每年的产出
    private static List<(CitizenJobAsset, (string, float)[])> JobOutputs()
    {
        if (_jobOutputs != null) return _jobOutputs;
        _jobOutputs = new List<(CitizenJobAsset, (string, float)[])>
        {
            (CitizenJobLibrary.gatherer_bushes, new[] { ("berries", 2f) }),
            (CitizenJobLibrary.gatherer_herbs, new[] { ("herbs", 1f) }),
            (CitizenJobLibrary.gatherer_honey, new[] { ("honey", 1f) }),
            (CitizenJobLibrary.hunter, new[] { ("meat", 2f) }),
            (CitizenJobLibrary.woodcutter, new[] { ("wood", 4f) }),
            (CitizenJobLibrary.miner_deposit, new[] { ("stone", 1.5f), ("common_metals", 0.5f) })
        };
        // 这个版本没有的资源不产
        foreach ((CitizenJobAsset job, (string, float)[] outputs) in _jobOutputs.ToArray())
        {
            (string, float)[] valid = outputs.Where(output => AssetManager.resources?.get(output.Item1) != null).ToArray();
            _jobOutputs[_jobOutputs.FindIndex(item => item.Item1 == job)] = (job, valid);
        }
        return _jobOutputs;
    }

    // 每座矿场每年的固定产出
    private static readonly (string, float)[] MineOutput = { ("stone", 6f), ("common_metals", 4f), ("gold", 1f) };

    private static bool IsFood(string resource) => AssetManager.resources?.get(resource)?.type == ResType.Food;

    // 背景人口纳税：原版和模组的财政是"实体单位交税给城市国库 → 城市交给国家"，无小人模式下纳税的实体几乎没有，
    // 国库会枯竭。这里按户和阶层算出背景人口的年收入，按本国税率交进城市国库(之后照常由城市上交国家)
    private static readonly Dictionary<SocialClass, float> IncomePerHousehold = new()
    {
        [SocialClass.Peasant] = 1f, [SocialClass.Labour] = 1.5f, [SocialClass.Citizen] = 2f,
        [SocialClass.Officer] = 3f, [SocialClass.Merchant] = 4f, [SocialClass.Landlord] = 5f,
        [SocialClass.Noble] = 5f, [SocialClass.Army] = 0.5f
    };

    private static void PayTaxes(City city, CityPopulationData data, Dictionary<SocialClass, float> workforceByClass,
        float years)
    {
        if (city.kingdom == null || city.kingdom.wild) return;
        float income = 0f;
        foreach (KeyValuePair<SocialClass, float> pair in workforceByClass)
            income += pair.Value / WorkingAgeShare * (IncomePerHousehold.TryGetValue(pair.Key, out float rate) ? rate : 1f);
        float tax = income * years * (float)city.kingdom.GetTaxRate() + data.tax_carry;
        int whole = Mathf.FloorToInt(tax);
        data.tax_carry = tax - whole;
        if (whole > 0) city.AddMoney(whole);
        data.last_tax_income = years > 0f ? whole / years : 0f;
    }

    private static float Get(Dictionary<SocialClass, float> values, SocialClass key) =>
        values.TryGetValue(key, out float value) ? value : 0f;

    // 岗位：每座建成的非民居建筑 + 每个地块的农田岗位
    public static float CountJobs(City city)
    {
        int buildings = 0;
        if (city.buildings != null)
            foreach (Building building in city.buildings)
            {
                if (building?.asset == null || building.isUnderConstruction()) continue;
                if (building.asset.type == "type_house") continue;
                buildings++;
            }
        int zones = city.zones?.Count ?? 0;
        return buildings * JobsPerBuilding + zones * FarmJobsPerZone;
    }

    // 产出攒满整数再存进仓库，零头留到下次
    private static void Deposit(City city, CityPopulationData data, string resource, float amount)
    {
        if (amount <= 0f) return;
        data.output_carry ??= new Dictionary<string, float>();
        data.output_carry.TryGetValue(resource, out float carry);
        carry += amount;
        int whole = Mathf.FloorToInt(carry);
        data.output_carry[resource] = carry - whole;
        if (whole <= 0) return;
        try
        {
            city.addResourcesToRandomStockpile(resource, whole);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][人口经济] 存入 {resource} 失败: {exception.Message}");
        }
    }

    // 从仓库里一份一份扣粮(原版居民吃饭也是这样)；扣不到就记为缺粮
    private static float EatFood(City city, CityPopulationData data, float need, out float shortage)
    {
        shortage = 0f;
        float total = data.food_need_carry + need;
        int portions = Mathf.FloorToInt(total);
        data.food_need_carry = total - portions;
        if (portions <= 0) return 0f;
        // 按粮食种类整批扣(以前一份一份地反射调用原版吃饭方法，大城一次几百次，结算那一帧会卡)
        int eaten = 0;
        try
        {
            foreach (ResourceAsset food in FoodAssets())
            {
                if (eaten >= portions) break;
                int have = city.getResourcesAmount(food.id);
                if (have <= 0) continue;
                int take = Mathf.Min(have, portions - eaten);
                city.takeResource(food.id, take);
                eaten += take;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][人口经济] 扣粮失败: {exception.Message}");
            return portions;
        }
        shortage = portions - eaten;
        return eaten;
    }

    // 工人去工地：按攒下的施工点数推进城里在建的建筑
    private static void AdvanceConstruction(City city, CityPopulationData data, float points)
    {
        data.construction_carry += points;
        int whole = Mathf.FloorToInt(data.construction_carry);
        if (whole <= 0 || _updateBuild == null || city.buildings == null) return;
        int used = 0;
        try
        {
            foreach (Building building in city.buildings)
            {
                if (used >= whole) break;
                if (building?.asset == null || !building.isUnderConstruction()) continue;
                int share = Mathf.Max(1, (whole - used) / 2);
                _updateBuild.Invoke(building, new object[] { share });
                used += share;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][人口经济] 推进施工失败，停用自动施工: {exception.Message}");
            _updateBuild = null;
        }
        // 没有在建的建筑时点数不留存(工人闲着)
        data.construction_carry = used > 0 ? data.construction_carry - used : 0f;
    }

    private static List<ResourceAsset> _foodAssets;

    private static List<ResourceAsset> FoodAssets()
    {
        if (_foodAssets != null) return _foodAssets;
        _foodAssets = new List<ResourceAsset>();
        if (AssetManager.resources?.list != null)
            foreach (ResourceAsset asset in AssetManager.resources.list)
                if (asset != null && asset.type == ResType.Food) _foodAssets.Add(asset);
        return _foodAssets;
    }

    // 原版的吃粮、施工方法名在不同游戏版本里可能不同，用反射查找；找不到就跳过那一部分，并在日志里说明
    private static void ResolveReflection()
    {
        if (_reflectionResolved) return;
        _reflectionResolved = true;
        foreach (string id in FoodCandidates)
            if (AssetManager.resources?.get(id) != null)
            {
                _foodId = id;
                break;
            }
        _getFoodItem = AccessTools.Method(typeof(City), "getFoodItem");
        _eatFoodItem = AccessTools.Method(typeof(City), "eatFoodItem", new[] { typeof(string) });
        MethodInfo updateBuild = AccessTools.Method(typeof(Building), "updateBuild");
        if (updateBuild != null)
        {
            ParameterInfo[] parameters = updateBuild.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int)) _updateBuild = updateBuild;
        }
        LogService.LogInfo($"[EmpireCraft][人口经济] 产粮={_foodId ?? "无"} 扣粮={(_getFoodItem != null && _eatFoodItem != null ? "可用" : "不可用")} 自动施工={(_updateBuild != null ? "可用" : "不可用")}");
    }
}
