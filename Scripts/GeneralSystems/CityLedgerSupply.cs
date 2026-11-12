using System;
using System.Collections.Generic;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 建筑台账下的城间转运(见 CityLedger)。原版 CityBehSupplyKingdomCities 每隔约 30 秒把本城仓库里富余的货
// (多于 supply_bound_give)分一份(supply_give)给同国缺这种货的城(不到 supply_bound_take)：木头就是"30 以上的城给不到 10 的城
// 送 10"，港口、新城的木头多靠它补。这一步要仓库实体，仓库记进台账以后它原地返回，各城再不互通有无(纯净地图下城里木头的
// 中位数从 9 掉到 3，码头造不起船)。这里照原版的门槛、数量与冷却，改从账面库存(settlement_supplies)出货，
// 送货走 CityResourceTransferSystem.Move(货换个城放，不算公家用料)。还有仓库实体的城照原版。
public static class CityLedgerSupply
{
    // 原版的转运冷却：30 秒 × 城主的转运倍率，至少 10 秒
    public static float Cooldown(float leaderMultiplier) => Math.Max(10f, 30f * leaderMultiplier);

    // 接不接管这座城的转运：台账在用、又没有仓库实体
    public static bool Takes(City city) => city?.data != null && CityLedger.Uses(city) && !city.hasStockpiles();

    // 账面库存里富余、可以分出去的货
    public static List<ResourceAsset> Surplus(Dictionary<string, int> stock)
    {
        var list = new List<ResourceAsset>();
        if (stock == null) return list;
        foreach (KeyValuePair<string, int> pair in stock)
            if (AssetManager.resources.dict.TryGetValue(pair.Key ?? "", out ResourceAsset asset) && asset.supply_give > 0 &&
                pair.Value > asset.supply_bound_give)
                list.Add(asset);
        return list;
    }

    // 同原版 CityBehSupplyKingdomCities.execute，只是货从账面库存出
    public static BehResult Run(City city)
    {
        try
        {
            if (!city.hasCulture()) return BehResult.Stop;
            Kingdom kingdom = city.kingdom;
            if (kingdom?.cities == null || kingdom.countCities() == 1) return BehResult.Stop;
            if (city.data.timer_supply > 0f) return BehResult.Stop;
            List<ResourceAsset> surplus = Surplus(city.GetOrCreate().population?.settlement_supplies);
            if (surplus.Count == 0) return BehResult.Stop;
            foreach (City other in kingdom.cities.LoopRandom())
            {
                if (other == city || other?.data == null || other.isRekt()) continue;
                foreach (ResourceAsset resource in surplus.LoopRandom())
                {
                    if (other.getResourcesAmount(resource.id) >= resource.supply_bound_take) continue;
                    CityResourceTransferSystem.Move(city, other, resource.id, resource.supply_give);
                    city.data.timer_supply = Cooldown(city.hasLeader() ? city.leader.stats["multiplier_supply_timer"] : 1f);
                    return BehResult.Continue;
                }
            }
            return BehResult.Continue;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][建筑台账] 城间转运失败({city?.data?.name}): {exception.Message}");
            return BehResult.Stop;
        }
    }
}
