using System;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城市资产随人、随债务人走，不随城市归属或城市销毁凭空产生或消失：
//   迁民：按迁出人口占原城背景人口的比例带走民间存款(土地、厂房、国库留在原城)
//   易主：旧政权欠下的治理/驻军/军费欠款仍由旧政权偿还，记到它的首都；旧政权已亡则记为违约
//   销毁：城市国库余额交回所属国家，欠款交给首都，民间存款随逃难百姓归入首都
public static class CityAssetSettlement
{
    // 在迁移人口之前读取 totalBefore(原城背景人口)，迁移完成后再调用
    public static void MoveSavingsWithMigrants(City from, City to, float moved, float totalBefore)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || from == null || to == null || from == to ||
            moved <= 0f || totalBefore <= 0f) return;
        CityPopulationData source = CityPopulationSystem.Get(from);
        CityPopulationData destination = CityPopulationSystem.Get(to);
        if (source == null || destination == null) return;
        float share = Mathf.Clamp01(moved / totalBefore);
        float amount = PopulationEconomySystem.Savings(from, source) * share;
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        PopulationEconomySystem.AddSavings(from, source, -amount);
        PopulationEconomySystem.AddSavings(to, destination, amount);
    }

    // 城市归属变了：旧政权的欠款交还旧政权(由 CityStabilitySystem.State 在发现换主时调用)
    public static void ReturnArrearsToDebtor(City city, CityStabilityData old)
    {
        if (old == null || old.arrears <= 0 && old.governance_arrears <= 0 && old.garrison_arrears <= 0 &&
            old.military_arrears <= 0) return;
        Kingdom debtor = old.owner_id >= 0 ? World.world?.kingdoms?.get(old.owner_id) : null;
        City capital = debtor != null && !debtor.isRekt() ? debtor.capital : null;
        CityStabilityData target = capital != null && capital != city && !capital.isRekt() && capital.kingdom == debtor
            ? CityStabilitySystem.State(capital) : null;
        if (target == null)
        {
            LogService.LogInfo($"[EmpireCraft][财政] {city?.data?.name} 原属政权({old.owner_id})已无首都可承接，" +
                               $"欠款 {old.governance_arrears + old.garrison_arrears + old.military_arrears} 记为违约");
            return;
        }
        target.governance_arrears = Add(target.governance_arrears, old.governance_arrears);
        target.garrison_arrears = Add(target.garrison_arrears, old.garrison_arrears);
        target.military_arrears = Add(target.military_arrears, old.military_arrears);
        target.arrears = target.governance_arrears + target.garrison_arrears + target.military_arrears;
    }

    // City.destroyCity 之前调用(城市仍在原国家)
    public static void OnCityDestroyed(City city)
    {
        if (city?.data == null) return;
        Kingdom kingdom = city.kingdom;
        if (kingdom == null || kingdom.isRekt() || kingdom.wild) return;
        try
        {
            var extra = city.GetOrCreate();
            // 国库余额交回国家
            long balance = extra.Money;
            while (balance > 0)
            {
                int chunk = (int)Math.Min(int.MaxValue, balance);
                city.SubMoney(chunk, TreasuryCategory.InternalTransfer);
                kingdom.AddMoney(chunk, TreasuryCategory.InternalTransfer);
                balance -= chunk;
            }
            // 欠款交给首都
            if (extra.stability != null && extra.stability.owner_id == kingdom.id)
            {
                ReturnArrearsToDebtor(city, extra.stability);
                extra.stability.governance_arrears = extra.stability.garrison_arrears =
                    extra.stability.military_arrears = extra.stability.arrears = 0;
            }
            // 民间存款随逃难百姓归入首都
            City capital = kingdom.capital;
            // 民间投资池也是百姓的钱，一并带走
            float assets = extra.population == null ? 0f
                : Math.Max(0f, extra.population.private_savings) + Math.Max(0f, extra.population.investment_pool);
            if (CityPopulationSystem.AbstractPopulationEnabled && capital != null && capital != city && !capital.isRekt() &&
                assets > 0f)
            {
                PopulationEconomySystem.AddSavings(capital, CityPopulationSystem.Get(capital), assets);
                extra.population.private_savings = 0f;
                extra.population.investment_pool = 0f;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][财政] 城市销毁结算失败({city.data.name}): {exception.Message}");
        }
    }

    private static long Add(long a, long b) => Math.Min(long.MaxValue / 6, Math.Max(0L, a) + Math.Max(0L, b));
}
