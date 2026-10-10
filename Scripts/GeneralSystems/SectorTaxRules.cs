using System;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class SectorTaxRules
{
    // 同一收入只归一个税基。现有总税率为基准，零税率仍然免税。
    public static double Rate(double baseRate, TreasuryCategory category)
    {
        if (double.IsNaN(baseRate) || double.IsInfinity(baseRate)) return 0d;
        double multiplier = category == TreasuryCategory.CommercialTax ? 1.5d :
            category == TreasuryCategory.IndustrialTax ? 1.25d : 1d;
        return Math.Max(0d, Math.Min(1d, baseRate * multiplier));
    }
    public static int Assess(double income, double baseRate, TreasuryCategory category, ref double carry)
    {
        if (double.IsNaN(carry) || double.IsInfinity(carry)) carry = 0d;
        if (double.IsNaN(income) || double.IsInfinity(income) || income <= 0d) return 0;
        double tax = income * Rate(baseRate, category) + Math.Max(0d, Math.Min(.999999999d, carry));
        int whole = (int)Math.Min(int.MaxValue, Math.Floor(tax + .00000001d));
        carry = Math.Max(0d, Math.Min(.999999999d, tax - whole));
        return whole;
    }
}
