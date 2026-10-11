using System.Collections.Generic;
using ai.behaviours;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

public class StateSettlementPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(StateSettlementPatch));
        harmony.Patch(AccessTools.Method(typeof(BehKingCheckNewCityFoundation), nameof(BehKingCheckNewCityFoundation.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeFoundation)));
        harmony.Patch(AccessTools.Method(typeof(BehCheckBuildCity), nameof(BehCheckBuildCity.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeFoundation)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getResourcesAmount)),
            postfix: new HarmonyMethod(GetType(), nameof(ResourceAmount)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getTotalFood)),
            postfix: new HarmonyMethod(GetType(), nameof(TotalFood)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.hasAnyFood)),
            postfix: new HarmonyMethod(GetType(), nameof(HasFood)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.takeResource)),
            prefix: new HarmonyMethod(GetType(), nameof(TakeSupplies)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.addResourcesToRandomStockpile)),
            postfix: new HarmonyMethod(GetType(), nameof(StoreOverflow)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.calcPossibleBuildings)),
            postfix: new HarmonyMethod(GetType(), nameof(PrioritizeSettlementBuildings)));
    }

    public static bool BeforeFoundation(Actor pActor, ref BehResult __result)
    {
        Kingdom kingdom = pActor?.kingdom;
        if (!CityPopulationSystem.AbstractPopulationEnabled || kingdom == null || kingdom.wild ||
            !kingdom.hasCapital() || AncientWarfareCompatibility.Owns(kingdom)) return true;
        __result = BehResult.Stop;
        return false; // 第一座文明城市仍按原版建立，已有国家的扩张改由国库项目负责。
    }

    private static Dictionary<string, int> Supplies(City city) => city?.data == null ? null :
        city.GetOrCreate().population?.settlement_supplies;

    private static (Dictionary<string, int> supplies, Dictionary<string, int> returns) TemporaryCargo(City city)
    {
        var population = city?.data == null ? null : city.GetOrCreate().population;
        return (population?.settlement_supplies, population?.resource_returns);
    }

    private static void AddTemporaryAmount(Dictionary<string, int> cargo, string resource, ref int amount)
    {
        if (cargo?.TryGetValue(resource, out int stored) != true || stored <= 0) return;
        // Cap the native int quantity query, not the stored inventory itself.
        amount = (int)global::System.Math.Min(int.MaxValue, (long)amount + stored);
    }

    public static void ResourceAmount(City __instance, string pResourceID, ref int __result)
    {
        var cargo = TemporaryCargo(__instance);
        AddTemporaryAmount(cargo.supplies, pResourceID, ref __result);
        AddTemporaryAmount(cargo.returns, pResourceID, ref __result);
    }

    public static void TotalFood(City __instance, ref int __result)
    {
        var cargo = TemporaryCargo(__instance);
        AddTemporaryFood(cargo.supplies, ref __result);
        AddTemporaryFood(cargo.returns, ref __result);
    }

    private static void AddTemporaryFood(Dictionary<string, int> cargo, ref int amount)
    {
        if (cargo == null) return;
        foreach (var pair in cargo)
            if (pair.Value > 0 && PopulationEconomySystem.IsFood(pair.Key))
                amount = (int)global::System.Math.Min(int.MaxValue, (long)amount + pair.Value);
    }

    public static void HasFood(City __instance, ref bool __result)
    {
        if (__result) return;
        var cargo = TemporaryCargo(__instance);
        if (cargo.supplies != null || cargo.returns != null)
            __result = __instance.getTotalFood() > 0;
    }

    public static void TakeSupplies(City __instance, string pResourceID, ref int pAmount)
    {
        var cargo = TemporaryCargo(__instance);
        TakeTemporary(__instance, cargo.returns, pResourceID, ref pAmount);
        TakeTemporary(__instance, cargo.supplies, pResourceID, ref pAmount);
    }

    private static void TakeTemporary(City city, Dictionary<string, int> supplies, string resource, ref int amount)
    {
        if (amount <= 0 || supplies == null || !supplies.TryGetValue(resource, out int have)) return;
        if (have <= 0) { supplies.Remove(resource); return; }
        int take = global::System.Math.Min(amount, have);
        supplies[resource] = have - take;
        if (supplies[resource] == 0) supplies.Remove(resource);
        amount -= take;
        city._storage_version++;
    }

    // 只有国家建城开设过临时库存的城市采用此兜底；旧城市的入库行为保持原样。
    // 即使之后关闭无小人法则，已经支付的开城物资仍可使用。
    public static void StoreOverflow(City __instance, string pResourceID, int pAmount, ref int __result)
    {
        var supplies = Supplies(__instance);
        // 原版返回仓库的新余额，不是本次入库量。没有可用仓库才接管，
        // 建好仓库以后仍遵守原版容量限制。
        if (supplies == null || pAmount <= 0 || __result != 0 || __instance.getRandomStockpile() != null) return;
        supplies.TryGetValue(pResourceID, out int have);
        supplies[pResourceID] = have + pAmount;
        __result = have + pAmount;
        __instance._storage_version++;
    }

    public static void FlushSupplies(City city)
    {
        Building storage = city.getRandomStockpile();
        var cargo = TemporaryCargo(city);
        FlushTemporary(city, storage, cargo.supplies);
        FlushTemporary(city, storage, cargo.returns);
    }

    private static void FlushTemporary(City city, Building storage, Dictionary<string, int> supplies)
    {
        if (supplies == null || supplies.Count == 0 || storage == null) return;
        foreach (var pair in new List<KeyValuePair<string, int>>(supplies))
        {
            if (pair.Value <= 0) { supplies.Remove(pair.Key); continue; }
            int before = storage.getResourcesAmount(pair.Key);
            try { storage.addResources(pair.Key, pair.Value); }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][临时库存] 入库异常，保留剩余物资: {exception.Message}");
            }
            finally
            {
                int added = (int)global::System.Math.Max(0L, global::System.Math.Min(pair.Value,
                    (long)storage.getResourcesAmount(pair.Key) - before));
                if (added > 0)
                {
                    supplies[pair.Key] -= added;
                    if (supplies[pair.Key] <= 0) supplies.Remove(pair.Key);
                    city._storage_version++;
                }
            }
        }
    }

    public static void PrioritizeSettlementBuildings(City pCity)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || pCity == null ||
            AncientWarfareCompatibility.OwnsObject(pCity)) return;
        float population = CityPopulationSystem.VanillaScale ? CityPopulationSystem.Households(pCity)
            : CityPopulationSystem.GetTotal(pCity);
        bool housing = Supplies(pCity) != null && pCity.getPopulationMaximum() < population;
        bool farming = Supplies(pCity) != null && pCity.getBuildingOfType("type_windmill") == null;
        bool port = StateSettlementSystem.WantsPort(pCity);
        if (!housing && !farming && !port) return;
        var orders = CityBehBuild._possible_buildings;
        bool hasHousing = false, hasFarm = false, hasPort = false;
        foreach (BuildOrder order in orders)
        {
            string type = order.getBuildingAsset(pCity)?.type;
            if (type == "type_house") hasHousing = true;
            if (type == "type_windmill") hasFarm = true;
            if (type == "type_docks") hasPort = true;
        }
        string preferred = housing && hasHousing ? "type_house" : farming && hasFarm ? "type_windmill" :
            port && hasPort ? "type_docks" : null;
        if (preferred == null) return; // 需要大厅等前置建筑时仍走原版订单，不越过资源与科技检查。
        orders.RemoveAll(order => order.getBuildingAsset(pCity)?.type != preferred);
    }
}
