using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 民间投资、官营产业与财政支出回流(无小人模式，参考维多利亚3；正常小人模式由实体钱包自己结算，这里不动)。
// 不铸新钱、不重复征税，只理顺钱的来去：
//   1. 财政支出回流：俸禄、军饷(含补发欠饷)、工程款付出去不是凭空消失，而是本城官员、士兵、工匠的收入，
//      交工资所得税后进本城民间现金(见 CityStabilitySystem.Pay、CityConstructionSystem)。
//   2. 民间投资：民间现金超过每户底线的部分，每年按 InvestSharePerYear 转入城市的民间投资池
//      (私人资产内部转移，不算收入、不征税)。投资池先付民居升级、民营产业的建造与升级，有余钱再出资加快施工；
//      付给本城工匠的工钱回到民间现金——真钱在城里流转并交工资税，建设变快，国家没钱民间也能建。
//      民间资产估值(private_savings，含存货)不直接投资，也不兑换成钱。
//   3. 所有权决定收益：新开工的矿场、伐木场、工坊、牧场、屠宰场由谁出资谁所有。投资池出钱或百姓自备材料自建
//      为民营；国库出钱为官营。官营产业按占全城产业的比例，从工业产值里拿出 PublicProfitShare 作为官营利润，
//      按税收分成上缴(见 TreasuryCategory.PublicEnterprise)；这部分不再进民间收入，也不再征工业税。
//      重商、农商并重时民间投资优先；工业化、重农抑商或施行“盐铁专卖”类制度(特性 state_enterprise)时官办优先。
public static class EnterpriseSystem
{
    private const float InvestSharePerYear = 0.3f;
    // 每户留作日用的现金，超出部分才投资
    private const int CashReservePerHousehold = 2;
    private const float PoolCap = 200000f;
    private const float PoolFloor = 100f;
    private const float PublicProfitShare = 0.35f;
    private const int BaseEnterpriseCost = 10;
    // 投资池超过这个数，民间出资加快施工(每 10 点施工 1 金)
    public const float PrivateFundingThreshold = 300f;
    public const string StateEnterpriseFeature = "state_enterprise";

    private static bool Active => CityPopulationSystem.AbstractPopulationEnabled;

    public static bool IsEnterprise(BuildingAsset asset) =>
        asset != null && (IndustryBuildingSystem.IsMine(asset) || IndustryBuildingSystem.IsLumber(asset) ||
                          asset.type == AnimalHusbandrySystem.PastureType ||
                          asset.type == AnimalHusbandrySystem.SlaughterhouseType || TechnologySystem.IsFactory(asset));

    public static float Pool(City city) => Active && city?.data != null ? CityPopulationSystem.Get(city)?.investment_pool ?? 0f : 0f;

    // ---- 1. 财政支出回流 ----
    // 工钱是收入：按居民税率交工资所得税(独立税基，不是产值，不与生产税重复)，税后进本城民间资产
    public static void ReturnToLocal(City city, int amount)
    {
        if (!Active || amount <= 0 || city?.data == null || city.isRekt()) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        int tax = 0;
        Kingdom kingdom = city.kingdom;
        if (kingdom != null && !kingdom.wild)
        {
            tax = Math.Min(amount, SectorTaxRules.Assess(amount, kingdom.GetTaxRate(), TreasuryCategory.ResidentTax,
                ref data.wage_tax_carry));
            if (tax > 0) TreasurySystem.CollectResidentTax(city, tax, TreasuryCategory.ResidentTax);
        }
        CashIn(data, amount - tax);
        data.wage_inflow_acc += amount;
    }

    // ---- 民间现金 ----
    // 真钱单独记在城市的普通人口现金账户(civilian_wallet_reserve.cash，与并入人物时保管的钱包同一账户)：
    // 俸饷工钱、公家买料付的钱都进这里；private_savings 仍是民间资产估值(含存货)，不再收真钱，也不凭空兑成钱
    public static long Cash(CityPopulationData data) => Math.Max(0L, data?.civilian_wallet_reserve?.cash ?? 0L);

    public static void CashIn(CityPopulationData data, long amount)
    {
        if (data == null || amount <= 0L) return;
        var reserve = data.civilian_wallet_reserve ??= new WalletReserve();
        reserve.cash = reserve.cash > long.MaxValue - amount ? long.MaxValue : Math.Max(0L, reserve.cash) + amount;
    }

    // 本月可用来交税的民间现金：每户底线以上部分的一半(其余留作投资、借贷与日用)
    public static long TaxableCash(City city, CityPopulationData data)
    {
        if (!Active || data == null) return 0L;
        long surplus = Cash(data) - (long)CityPopulationSystem.BackgroundHouseholds(city) * CashReservePerHousehold;
        return Math.Max(0L, surplus / 2);
    }

    public static long TakeCash(CityPopulationData data, long amount)
    {
        long taken = Math.Min(Cash(data), Math.Max(0L, amount));
        if (taken > 0L) data.civilian_wallet_reserve.cash -= taken;
        return taken;
    }

    // 公家向百姓买料：国库付的钱进民间现金，货物离开仓库，估值同额减少(家底不变)
    public static void PublicPurchase(City city, CityPopulationData data, int paid)
    {
        if (!Active || data == null || paid <= 0) return;
        CashIn(data, paid);
        PopulationEconomySystem.AddSavings(city, data, -paid);
    }

    // ---- 2. 民间投资 ----
    // 只投真钱：民间现金超过每户底线的部分，每年按 InvestSharePerYear 转入投资池
    // (投资花出去的工钱回到民间现金并交工资税——投资与收税都是真钱流转，不由估值变出钱来)
    public static void AccrueInvestment(City city, CityPopulationData data, float years)
    {
        if (!Active || data == null || years <= 0f) return;
        long reserve = (long)CityPopulationSystem.BackgroundHouseholds(city) * CashReservePerHousehold;
        long surplus = Cash(data) - reserve;
        // 投资池只攒得下近期用得掉的钱(近一年实投的两倍 + 底数)，用不掉的留在现金里交税、借贷、日用
        float target = Mathf.Min(PoolCap, 2f * Mathf.Max(0f, data.last_private_investment) + PoolFloor);
        float room = target - Mathf.Max(0f, data.investment_pool);
        if (surplus <= 0L || room < 1f) return;
        long move = (long)Math.Min(room, Math.Floor(surplus * Math.Min(1d, InvestSharePerYear * years)));
        move = TakeCash(data, move);
        if (move <= 0L) return;
        data.investment_pool = Mathf.Max(0f, data.investment_pool) + move;
        data.investment_acc += move;
    }

    // 从投资池付建设款，付给本城工匠(回到民间资产)；返回付出的整数金额
    public static int SpendPool(City city, int amount)
    {
        if (!Active || amount <= 0 || city?.data == null) return 0;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return 0;
        int paid = Mathf.Min(amount, Mathf.FloorToInt(Mathf.Max(0f, data.investment_pool)));
        if (paid <= 0) return 0;
        data.investment_pool -= paid;
        data.private_investment_spent_acc += paid;
        ReturnToLocal(city, paid);
        return paid;
    }

    // 付得起吗(privateFirst：连民间投资池一起算)
    public static bool CanPay(City city, int cost, bool privateFirst)
    {
        if (cost <= 0) return true;
        Kingdom kingdom = city?.kingdom;
        long available = (privateFirst ? Mathf.FloorToInt(Pool(city)) : 0) + Math.Max(0, city?.GetMoney() ?? 0) +
                         (kingdom == null || kingdom.wild ? 0 : Math.Max(0, StateSettlementSystem.DiscretionaryFunds(kingdom)));
        return available >= cost;
    }

    // 民间投资池 → 城市国库 → 国家可用资金，依次付款；全额付得起才付。返回是否付清
    public static bool PayPrivateFirst(City city, int cost)
    {
        if (cost <= 0) return true;
        Kingdom kingdom = city.kingdom;
        int pool = Mathf.FloorToInt(Pool(city));
        int fromPool = Mathf.Min(cost, pool);
        int fromCity = Mathf.Min(cost - fromPool, Mathf.Max(0, city.GetMoney()));
        int fromState = cost - fromPool - fromCity;
        if (fromState > 0 && (kingdom == null || kingdom.wild || StateSettlementSystem.DiscretionaryFunds(kingdom) < fromState))
            return false;
        SpendPool(city, fromPool);
        PayPublic(city, fromCity, fromState);
        return true;
    }

    // 城市国库 → 国家可用资金(工钱回本城)
    public static bool PayPublicFirst(City city, int cost)
    {
        if (cost <= 0) return true;
        Kingdom kingdom = city.kingdom;
        int fromCity = Mathf.Min(cost, Mathf.Max(0, city.GetMoney()));
        int fromState = cost - fromCity;
        if (fromState > 0 && (kingdom == null || kingdom.wild || StateSettlementSystem.DiscretionaryFunds(kingdom) < fromState))
            return false;
        PayPublic(city, fromCity, fromState);
        return true;
    }

    private static void PayPublic(City city, int fromCity, int fromState)
    {
        if (fromCity > 0) city.SubMoney(fromCity, TreasuryCategory.Construction);
        if (fromState > 0) city.kingdom.SubMoney(fromState, TreasuryCategory.Construction);
        ReturnToLocal(city, fromCity + fromState);
    }

    // ---- 3. 产业出资与所有权 ----
    public static bool StatePreferred(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.wild) return false;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && InstitutionSystem.GetFeature(empire, StateEnterpriseFeature) > 0f) return true;
        PlanPolicy policy = ZonePlanSystem.PolicyOf(kingdom);
        return policy == PlanPolicy.Industrial || policy == PlanPolicy.Agrarian;
    }

    public static int EnterpriseCost(BuildingAsset asset) => BaseEnterpriseCost + Math.Max(0, asset.construction_progress_needed) / 2;

    // 新开工的建筑：产业建筑按出资方登记所有权
    public static void OnConstructionStarted(City city, Building building)
    {
        if (!Active || city?.data == null || building?.asset == null || !IsEnterprise(building.asset)) return;
        int cost = EnterpriseCost(building.asset);
        bool state = StatePreferred(city.kingdom);
        if (state && PayPublicFirst(city, cost)) { RegisterPublic(city, building); return; }
        if (Pool(city) >= cost) { SpendPool(city, cost); return; }
        if (!state && PayPublicFirst(city, cost)) { RegisterPublic(city, building); return; }
        // 没人出钱：百姓用自备材料自建，归民营
    }

    public static bool IsPublic(City city, Building building)
    {
        List<long> ids = city?.data == null ? null : city.GetOrCreate().public_enterprise_ids;
        return ids != null && building != null && ids.Contains(building.data.id);
    }

    private static void RegisterPublic(City city, Building building)
    {
        var ids = city.GetOrCreate().public_enterprise_ids ??= new List<long>();
        if (!ids.Contains(building.data.id)) ids.Add(building.data.id);
    }

    // 建成的产业建筑中官营的占比(顺带清掉已经不存在的登记)
    public static void CountEnterprises(City city, out int total, out int publicCount)
    {
        total = publicCount = 0;
        if (city?.buildings == null) return;
        List<long> ids = city.GetOrCreate().public_enterprise_ids;
        HashSet<long> alive = ids != null && ids.Count > 0 ? new HashSet<long>() : null;
        foreach (Building building in city.buildings)
        {
            if (building?.asset == null || building.data == null || !IsEnterprise(building.asset)) continue;
            bool registered = ids != null && ids.Contains(building.data.id);
            if (registered) alive.Add(building.data.id);
            if (building.isUnderConstruction()) continue;
            total++;
            if (registered) publicCount++;
        }
        if (alive != null && alive.Count != ids.Count) ids.RemoveAll(id => !alive.Contains(id));
    }

    // PayTaxes 计税前调用：官营利润从工业产值里拿出，上缴国库；返回从总收入中扣掉的价值
    public static float TakePublicProfit(City city, CityPopulationData data)
    {
        if (!Active || data?.produced_sector_values == null || city.kingdom == null || city.kingdom.wild) return 0f;
        string key = TreasuryCategory.IndustrialTax.ToString();
        if (!data.produced_sector_values.TryGetValue(key, out float industrial) || industrial <= 0f) return 0f;
        CountEnterprises(city, out int total, out int publicCount);
        if (total <= 0 || publicCount <= 0) return 0f;
        float profit = industrial * publicCount / total * PublicProfitShare;
        data.produced_sector_values[key] = industrial - profit;
        float assessed = profit + data.public_profit_carry;
        int whole = Mathf.FloorToInt(assessed);
        data.public_profit_carry = assessed - whole;
        if (whole > 0)
        {
            TreasurySystem.CollectResidentTax(city, whole, TreasuryCategory.PublicEnterprise);
            data.public_profit_acc += whole;
        }
        return profit;
    }

    // 每次月结把累计流量折成年化数值给界面看
    public static void RollPeriod(CityPopulationData data, float years)
    {
        if (data == null || years <= 0f) return;
        data.last_wage_inflow = data.wage_inflow_acc / years;
        data.last_public_profit = data.public_profit_acc / years;
        data.last_private_investment = data.private_investment_spent_acc / years;
        data.wage_inflow_acc = data.public_profit_acc = data.private_investment_spent_acc = data.investment_acc = 0f;
    }
}
