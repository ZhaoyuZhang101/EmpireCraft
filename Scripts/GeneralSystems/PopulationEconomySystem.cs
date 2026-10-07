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
//   产出   = 农田收割、原版岗位、矿场伐木场、畜牧等真实产出，存进城市仓库
//   收入   = 产出按市场价折成的钱(自给口粮不算)，交税 = 收入 × 税率(见 PayTaxes)；不再凭空产生金钱
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
    private const float LabourConstructionPerYear = 6f;
    // 其他阶层也出工盖房(农闲、徭役)，按这个比例折算；否则没有工人的新城永远盖不起民居，人口也就涨不上去
    private const float OtherConstructionPerYear = 2f;
    // 非农民阶层的自给口粮(菜园、渔猎)
    private const float OtherFoodPerYear = 0.5f;
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
    public static void Settle(City city, CityPopulationData data, double now, float? farmHarvest = null)
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
        var numeric = PopulationParallelSystem.GetWorkforce(city, data, perSlot);
        background = numeric.BackgroundHouseholds;
        foreach (var pair in numeric.Workforce) workforceByClass[(SocialClass)pair.Key] = pair.Value;
        // 农田自动耕作(开田、播种、收割)，粮食来自实际收割的麦子(见 FarmlandSystem)
        // 收成按粮食征收政策一部分交国家粮仓(见 GranarySystem)，其余留在本城
        float harvested = farmHarvest ?? DepositFarmHarvest(city, data, FarmlandSystem.Settle(city, data));
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
        // 自给口粮自己吃掉，不是收入
        Deposit(city, data, _foodId ?? "berries", background * SubsistenceFoodPerYear * years, income: false);
        // 矿场、伐木场固定产出：每座建成的每年产出固定数量，按等级倍增、在工业区再加成，不看岗位和人口
        // (见 IndustryBuildingSystem)
        float mines = IndustryBuildingSystem.MineOutputFactor(city);
        if (mines > 0f)
            foreach ((string resource, float amount) in MineOutput)
                if (AssetManager.resources?.get(resource) != null) Deposit(city, data, resource, mines * amount * years);
        // 战略矿产(铜、煤、硝石……)按矿场等级和本城矿藏开采，见 MineralResourceSystem
        MineralResourceSystem.Produce(city, data, years);
        // 伐木场砍本城领地里的树出木头(见 IndustryBuildingSystem.HarvestTrees)
        float wood = IndustryBuildingSystem.HarvestTrees(city, years) / Mathf.Max(0.0001f, years);
        if (wood > 0f && AssetManager.resources?.get("wood") != null) Deposit(city, data, "wood", wood * years);
        PayTaxes(city, data, years);
        // 施工改由城市建设力推进(见 CityConstructionSystem)，不再按工人数
        float eaten = EatFood(city, data, background * FoodPerPersonYear * years, out float shortage);
        ConsumeGoods(city, data, background, years);
        // 吃掉的粮食、用掉的皮革是民间的消费，按价值从民间存款里扣(赈灾粮是国家给的，不扣)
        float consumption = eaten * UnitValue(city, _foodId ?? "wheat") + data.last_leather_used * UnitValue(city, "leather");
        AddSavings(city, data, -consumption);
        data.last_consumption = years > 0f ? consumption / years : 0f;
        // 饥荒：国家粮仓开仓赈灾，调来的粮食补上缺口
        if (shortage > 0f)
        {
            float relief = GranarySystem.Relieve(city, shortage);
            if (relief > 0f)
            {
                Deposit(city, data, _foodId ?? "wheat", relief, income: false);
                float served = EatFood(city, data, Mathf.Floor(Mathf.Min(shortage, relief)), out _);
                eaten += served;
                shortage = Mathf.Max(0f, shortage - served);
            }
        }

        data.last_jobs = jobs;
        data.last_workforce = workforce;
        data.last_food_output = years > 0f ? food / years : 0f;
        data.last_food_eaten = years > 0f ? eaten / years : 0f;
        data.last_food_shortage = years > 0f ? shortage / years : 0f;
    }

    // 分帧收割后立即入库，避免两步骤之间保存/占城导致已经收下的粮食丢失。
    public static float DepositFarmHarvest(City city, CityPopulationData data, float rawHarvest)
    {
        if (rawHarvest <= 0f) return 0f;
        ResolveReflection();
        float harvested = GranarySystem.Collect(city, rawHarvest);
        if (harvested > 0f) Deposit(city, data, _foodId ?? "wheat", harvested);
        return harvested;
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

    public static bool IsFood(string resource) => AssetManager.resources?.get(resource)?.type == ResType.Food;

    // 背景人口纳税：原版和模组的财政是"实体单位交税给城市国库 → 城市交给国家"，无小人模式下纳税的实体几乎没有，
    // 国库会枯竭。背景人口的收入就是他们的真实产出(按市场价折算，见 Deposit)，按本国税率交进城市国库
    // (之后照常由城市上交国家)。产出多少，收入就多少，没有凭空的收入
    private static void PayTaxes(City city, CityPopulationData data, float years)
    {
        float income = Mathf.Max(0f, data.produced_value);
        data.produced_value = 0f;
        data.last_income = years > 0f ? income / years : 0f;
        if (city.kingdom == null || city.kingdom.wild) return;
        float due = income * (float)city.kingdom.GetTaxRate();
        float tax = due + data.tax_carry;
        int whole = Mathf.FloorToInt(tax);
        data.tax_carry = tax - whole;
        if (whole > 0) city.AddMoney(whole);
        data.last_tax_income = years > 0f ? whole / years : 0f;
        // 税后收入进民间存款
        AddSavings(city, data, income - due);
    }

    // ---- 民间存款(无小人模式) ----
    // 背景人口没有各自的钱包，全城合记一个民间存款(连同仓库里属于百姓的存货，算作民间的家底)：
    //   进：税后收入(产出的价值减去税)；卖地的钱(税后)
    //   出：吃掉的粮食、用掉的皮革(按价值)；商人地主攒去买地的钱
    //   城与城之间买粮由民间存款付账：付出的钱换来同样价值的粮食，家底不变，所以只看存款够不够；
    //   卖给外城的货也是百姓的：按九成价卖掉，家底少一成(这一成是商人的利润)
    //   买木头、石头、金属是公家盖房、打造装备用的，由城市国库出钱
    // 旧存档第一次建账时，把仓库里现有粮食的价值记作民间存款
    public static float Savings(City city, CityPopulationData data)
    {
        if (data == null) return 0f;
        if (data.private_savings < 0f)
        {
            float price = 1f;
            try
            {
                price = MarketSystem.Price(city, MarketSystem.Good.Food);
            }
            catch
            {
                // 按基础价
            }
            data.private_savings = city == null ? 0f : MarketSystem.Stock(city, MarketSystem.Good.Food) * price;
        }
        return data.private_savings;
    }

    // ---- 公家用百姓的东西要付钱(无小人模式) ----
    // 仓库里的东西默认是百姓的(产出时已经算进民间收入)。盖房、造船、打造装备等公家用途从仓库里拿非粮食的东西时，
    // 先用公家自己从市场买的(public_stock，不用付钱)，不够的部分由城市国库按价付给百姓：钱换货，民间家底不变；
    // 国库钱不够，没付上的部分算征用，民间家底少了这些货的价值。
    // 市场搬货、炼钢、百姓自己用的皮革等不是公家用途，期间不算(见 PrivateUse)
    [global::System.ThreadStatic] private static int _privateUse;

    public static bool InPrivateUse => _privateUse > 0;

    public readonly struct PrivateUseScope : global::System.IDisposable
    {
        public void Dispose() => _privateUse--;
    }

    public static PrivateUseScope PrivateUse()
    {
        _privateUse++;
        return new PrivateUseScope();
    }

    public static void AddPublicStock(City city, string resource, int amount)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null || amount <= 0 || string.IsNullOrEmpty(resource)) return;
        data.public_stock ??= new Dictionary<string, int>();
        data.public_stock.TryGetValue(resource, out int have);
        data.public_stock[resource] = have + amount;
    }

    // 从公家存货里扣掉 amount(不超过公家有的)，返回扣掉的数量
    public static int TakePublicStock(City city, string resource, int amount)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.public_stock == null || amount <= 0 || !data.public_stock.TryGetValue(resource, out int have)) return 0;
        // 仓库里实际没这么多(被抢、被烧)，公家存货也跟着少
        have = Mathf.Min(have, amount + Mathf.Max(0, city.getResourcesAmount(resource)));
        int take = Mathf.Min(have, amount);
        if (have - take > 0) data.public_stock[resource] = have - take;
        else data.public_stock.Remove(resource);
        return take;
    }

    // 公家从仓库里用掉了 amount 份 resource
    public static void ChargePublicUse(City city, string resource, int amount)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city == null || amount <= 0 || InPrivateUse) return;
        if (string.IsNullOrEmpty(resource) || IsFood(resource)) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        int fromPeople = amount - TakePublicStock(city, resource, amount);
        if (fromPeople <= 0) return;
        float value = fromPeople * UnitValue(city, resource);
        int pay = Mathf.Min(Mathf.CeilToInt(value), Mathf.Max(0, city.GetMoney()));
        if (pay > 0) city.SubMoney(pay);
        data.public_paid += pay;
        float unpaid = Mathf.Max(0f, value - pay);
        if (unpaid > 0f)
        {
            data.public_unpaid += unpaid;
            AddSavings(city, data, -unpaid);
        }
    }

    public static void AddSavings(City city, CityPopulationData data, float amount)
    {
        if (data == null) return;
        data.private_savings = Mathf.Max(0f, Savings(city, data) + amount);
    }

    // 一份资源值多少钱：粮食、木头、石头、金属按国内市场价(见 MarketSystem.Price)，其它按固定价
    private static readonly Dictionary<string, float> FixedValue = new()
    {
        ["gold"] = 3f, ["leather"] = 1f, ["bones"] = 0.3f, ["herbs"] = 1f, ["honey"] = 1f
    };
    private const float StrategicMineralValue = 2f;

    public static float UnitValue(City city, string resource)
    {
        if (string.IsNullOrEmpty(resource)) return 0f;
        MarketSystem.Good? good = resource switch
        {
            "wood" => MarketSystem.Good.Wood,
            "stone" => MarketSystem.Good.Stone,
            "common_metals" => MarketSystem.Good.Metal,
            _ => IsFood(resource) ? (MarketSystem.Good?)MarketSystem.Good.Food : null
        };
        if (good.HasValue && city?.kingdom != null)
        {
            try
            {
                return MarketSystem.Price(city, good.Value);
            }
            catch
            {
                // 算不出市场价按基础价
            }
        }
        if (good.HasValue) return good.Value == MarketSystem.Good.Metal ? 3f : good.Value == MarketSystem.Good.Stone ? 1.5f : 1f;
        return FixedValue.TryGetValue(resource, out float value) ? value : StrategicMineralValue;
    }

    // 加工用掉的原料：从收入里扣掉原料的价值，只算加工增加的那部分
    public static void ConsumeInputs(City city, CityPopulationData data, string resource, float amount)
    {
        if (data == null || amount <= 0f) return;
        data.produced_value -= amount * UnitValue(city, resource);
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
    public static void Deposit(City city, CityPopulationData data, string resource, float amount, bool income = true)
    {
        if (amount <= 0f) return;
        // 产出就是收入：按市场价折成钱，累计到下次结算交税
        if (income) data.produced_value += amount * UnitValue(city, resource);
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

    // 日用消耗(按户数)：皮革做衣履、鞍具。肉类已经和粮食一起按户吃掉(见 EatFood)。
    // 现代翻倍；库存不够只记缺口
    private const float LeatherPerHouseholdYear = 0.01f;

    private static void ConsumeGoods(City city, CityPopulationData data, float households, float years)
    {
        // 本次没扣到整份时用量是 0(民间消费按这个算)
        data.last_leather_used = 0f;
        float need = households * LeatherPerHouseholdYear * years *
                     (ModernStability.IsModern(city.kingdom) ? 2f : 1f) + data.leather_need_carry;
        int whole = Mathf.FloorToInt(need);
        data.leather_need_carry = need - whole;
        if (whole <= 0) return;
        int take = 0;
        try
        {
            take = Mathf.Min(whole, city.getResourcesAmount("leather"));
            if (take > 0)
                using (PrivateUse())
                    city.takeResource("leather", take);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][人口经济] 扣皮革失败: {exception.Message}");
        }
        data.last_leather_used = take;
        data.last_leather_shortage = whole - take;
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
