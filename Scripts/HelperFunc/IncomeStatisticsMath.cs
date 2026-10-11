using System;

namespace EmpireCraft.Scripts.HelperFunc;

// Saturation protects statistical counters only; cash balances never use this rule.
public static class IncomeStatisticsMath
{
    public static long Add(long current, long received)
    {
        current = Math.Max(0L, current);
        received = Math.Max(0L, received);
        return current > long.MaxValue - received ? long.MaxValue : current + received;
    }

    public static long MerchantThreshold(long median, int minimum) =>
        Math.Max(minimum, Add(median, median));
}
