using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国债的纯数值规则(不触 Unity/游戏对象，离线测试直接编译，见 Tasks/FiscalPhase2.Tests.ps1)
public static class StateDebtRules
{
    public const double DebtCapYears = 2d;
    public const double LendShare = 0.5d;

    // 年利率：4% 起，未掌握银行 +3%，正统越低越贵(最多 +4%)，有欠饷 +2%；限 3%~12%
    public static float AnnualRate(bool hasBanking, int legitimacy, bool hasArrears)
    {
        double rate = 0.04d;
        if (!hasBanking) rate += 0.03d;
        rate += 0.04d * (1d - Math.Max(0, Math.Min(100, legitimacy)) / 100d);
        if (hasArrears) rate += 0.02d;
        return (float)Math.Max(0.03d, Math.Min(0.12d, rate));
    }

    // 月息：本金 × 年息 / 12，零头留在 carry 里下月继续，不丢也不重复
    public static int MonthlyInterest(long principal, float rate, ref double carry)
    {
        if (principal <= 0L || rate <= 0f) return 0;
        if (double.IsNaN(carry) || double.IsInfinity(carry) || carry < 0d) carry = 0d;
        // 存档里利率是 float(0.12f → 0.11999...)，计息前规整回名义值(万分位)，大额本金才不会被表示误差磨掉利息
        double nominal = Math.Round(rate * 10000d) / 10000d;
        double interest = principal * nominal / 12d + carry;
        int due = (int)Math.Min(int.MaxValue, Math.Floor(interest + .00000001d));
        carry = Math.Max(0d, interest - due);
        return due;
    }

    // 还能借多少：年经常收入 × 上限倍数 − 现有债务
    public static long BorrowRoom(long annualStableIncome, long outstanding) =>
        Math.Max(0L, (long)(Math.Max(0L, annualStableIncome) * DebtCapYears) - Math.Max(0L, outstanding));

    // 一座城可借出：现金超出日用底线部分的一半，加投资池的一半
    public static long Lendable(long cash, long reserve, long investmentPool) =>
        (long)(Math.Max(0L, cash - reserve) * LendShare) + (long)(Math.Max(0L, investmentPool) * LendShare);

    // 同城合并借款时的加权利率
    public static float MergedRate(long oldPrincipal, float oldRate, long added, float newRate) =>
        oldPrincipal + added <= 0L ? newRate
            : (float)((oldRate * (double)oldPrincipal + newRate * (double)added) / (oldPrincipal + (double)added));
}
