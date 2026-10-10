using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国库预算统筹的纯数值规则(不触 Unity/游戏对象，离线测试直接编译，见 Tasks/FiscalPhase2.Tests.ps1)
public static class FiscalBudgetRules
{
    // 一般地方的拨付比例：本月可用 = 月初余额 + 预计月经常收入，先保要地，剩下按上月一般需求的比例分。
    // 没有上月记录(刚读档、新国家)时不限额
    public static double NormalRatio(long balance, long expectedMonthlyIncome, long lastPriorityNeed,
        long lastNormalNeed, bool hasHistory)
    {
        if (!hasHistory || lastNormalNeed <= 0L) return 1d;
        double available = Math.Max(0L, balance) + Math.Max(0L, expectedMonthlyIncome) - Math.Max(0L, lastPriorityNeed);
        return Math.Max(0d, Math.Min(1d, available / lastNormalNeed));
    }

    public static long SafeAdd(long total, int amount)
    {
        long add = Math.Max(0, amount);
        return total > long.MaxValue - add ? long.MaxValue : total + add;
    }

    // 限额下本城这笔账可从国库拿的数额(向上取整，免得小额账单被四舍五入成 0)
    public static int Allowance(int remaining, double ratio) =>
        remaining <= 0 ? 0 : ratio >= 1d ? remaining : (int)Math.Ceiling(remaining * Math.Max(0d, ratio));
}
