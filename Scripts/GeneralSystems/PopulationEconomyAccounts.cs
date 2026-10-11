using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class PopulationEconomyPeriod
{
    public float years, income, tax, consumption;
}

// 保存最近一年的实际流量。季节性收割和整数税款不能用单个月乘十二解释。
public static class PopulationEconomyAccounts
{
    public static void Record(CityPopulationData data, float years, float income, float tax, float consumption)
    {
        if (data == null || years <= 0f || float.IsNaN(years) || float.IsInfinity(years)) return;
        var periods = data.economy_periods ??= new List<PopulationEconomyPeriod>();
        periods.RemoveAll(period => period == null || period.years <= 0f || float.IsNaN(period.years) || float.IsInfinity(period.years));
        periods.Add(new PopulationEconomyPeriod { years = years, income = Clean(income), tax = Clean(tax), consumption = Clean(consumption) });
        double length = 0d;
        foreach (var period in periods) length += period.years;
        while (length > 1d && periods.Count > 0)
        {
            var oldest = periods[0];
            double trim = Math.Min(oldest.years, length - 1d);
            float retained = (float)((oldest.years - trim) / oldest.years);
            length -= trim;
            if (retained < .00001f) periods.RemoveAt(0);
            else { oldest.years *= retained; oldest.income *= retained; oldest.tax *= retained; oldest.consumption *= retained; }
        }
        double incomeSum = 0d, taxSum = 0d, consumptionSum = 0d;
        foreach (var period in periods) { incomeSum += Clean(period.income); taxSum += Clean(period.tax); consumptionSum += Clean(period.consumption); }
        double divisor = Math.Max(.000001d, Math.Min(1d, length));
        data.last_income = (float)(incomeSum / divisor);
        data.last_tax_income = (float)(taxSum / divisor);
        data.last_consumption = (float)(consumptionSum / divisor);
    }

    private static float Clean(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
}
