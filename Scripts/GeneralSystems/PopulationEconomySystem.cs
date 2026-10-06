using System;
using System.Collections.Generic;
using System.Reflection;
using EmpireCraft.Scripts.Data;
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
    private const float MerchantGoldPerYear = 1.5f;
    private const float CitizenGoldPerYear = 1f;
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
        foreach (PopGroup group in data.groups)
        {
            float amount = group.Background;
            if (amount <= 0f) continue;
            background += amount;
            workforceByClass.TryGetValue(group.social_class, out float workers);
            workforceByClass[group.social_class] = workers + amount * WorkingAgeShare;
        }
        if (background <= 0f) return;

        float workforce = background * WorkingAgeShare;
        float jobs = CountJobs(city);
        float employment = workforce <= 0f ? 0f : Mathf.Clamp01(jobs / workforce);
        float productivity = employment + (1f - employment) * UnemployedOutputShare;

        float food = Get(workforceByClass, SocialClass.Peasant) * PeasantFoodPerYear * productivity * years;
        float gold = (Get(workforceByClass, SocialClass.Labour) * LabourGoldPerYear +
                      Get(workforceByClass, SocialClass.Merchant) * MerchantGoldPerYear +
                      Get(workforceByClass, SocialClass.Citizen) * CitizenGoldPerYear) * productivity * years;
        float construction = Get(workforceByClass, SocialClass.Labour) * LabourConstructionPerYear * productivity * years;

        if (_foodId != null) Deposit(city, data, _foodId, food);
        Deposit(city, data, "gold", gold);
        AdvanceConstruction(city, data, construction);
        float eaten = EatFood(city, data, background * FoodPerPersonYear * years, out float shortage);

        data.last_jobs = jobs;
        data.last_workforce = workforce;
        data.last_food_output = years > 0f ? food / years : 0f;
        data.last_food_eaten = years > 0f ? eaten / years : 0f;
        data.last_food_shortage = years > 0f ? shortage / years : 0f;
        data.last_gold_output = years > 0f ? gold / years : 0f;
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
        if (_getFoodItem == null || _eatFoodItem == null) return 0f;
        float total = data.food_need_carry + need;
        int portions = Mathf.FloorToInt(total);
        data.food_need_carry = total - portions;
        int calls = Mathf.Min(portions, MaxFoodCallsPerSettle);
        int eaten = 0;
        try
        {
            object[] getArgs = new object[_getFoodItem.GetParameters().Length];
            for (int i = 0; i < calls; i++)
            {
                object item = _getFoodItem.Invoke(city, getArgs);
                if (item is not Asset asset || string.IsNullOrEmpty(asset.id)) break;
                _eatFoodItem.Invoke(city, new object[] { asset.id });
                eaten++;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][人口经济] 扣粮失败，停用扣粮: {exception.Message}");
            _eatFoodItem = null;
        }
        // 超过单次上限的部分按"已吃到"处理，避免大城被误判缺粮
        shortage = Mathf.Max(0, calls - eaten);
        return eaten + Mathf.Max(0, portions - calls);
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
