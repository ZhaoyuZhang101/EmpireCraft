using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

public enum TreasuryCategory
{
    Other, ResidentTax, InternalTransfer, PublicTrade, RecoveredFunds, Tribute,
    Military, Administration, Welfare, Maintenance, Research, Construction, Diplomacy, Corruption, Policy, Gift,
    Garrison, Governance
}

public sealed class TreasuryReport
{
    public long income, expense, transfer_in, transfer_out, essential_expense, maintenance_expense, stable_income, operating_balance;
    public int months;
    public long military, research, construction, corruption;
    public Dictionary<string, long> income_sources = new(), expense_sources = new();
}

public static class TreasuryRules
{
    private static readonly string[] CategoryNames = Enum.GetNames(typeof(TreasuryCategory));
    public static void Record(TreasuryData data, double now, Func<double, int> monthsSince,
        long change, TreasuryCategory category)
    {
        if (data == null || change == 0 || double.IsNaN(now) || double.IsInfinity(now)) return;
        if (data.started < 0d || now < data.started) { data.started = now; data.periods = new(); }
        var periods = data.periods ??= new List<TreasuryPeriod>();
        TreasuryPeriod period = periods.Count > 0 ? periods[periods.Count - 1] : null;
        if (period == null || now < period.timestamp || monthsSince(period.timestamp) >= 1)
        {
            Trim(data, now, monthsSince);
            period = new TreasuryPeriod { timestamp = now }; periods.Add(period);
        }
        var amounts = change > 0 ? period.income : period.expense;
        amounts ??= new Dictionary<string, long>();
        if (change > 0) period.income = amounts; else period.expense = amounts;
        string key = (int)category >= 0 && (int)category < CategoryNames.Length ? CategoryNames[(int)category] : nameof(TreasuryCategory.Other);
        amounts.TryGetValue(key, out long old);
        amounts[key] = old + Math.Abs(change);
    }

    private static void Trim(TreasuryData data, double now, Func<double, int> monthsSince)
    {
        data.periods ??= new List<TreasuryPeriod>();
        data.periods.RemoveAll(p => p == null || p.timestamp > now || monthsSince(p.timestamp) >= 12);
        // 防御损坏的旧档；正常记录最多十二个不重叠月度桶。
        if (data.periods.Count > 12) data.periods.RemoveRange(0, data.periods.Count - 12);
    }

    public static TreasuryReport Report(TreasuryData data, double now, Func<double, int> monthsSince)
    {
        var report = new TreasuryReport();
        if (data == null || data.started < 0d || now < data.started) return report;
        Trim(data, now, monthsSince);
        report.months = Math.Min(12, Math.Max(1, monthsSince(data.started) + 1));
        foreach (var period in data.periods)
        {
            Sum(period.income, false, report);
            Sum(period.expense, true, report);
        }
        return report;
    }

    private static void Sum(Dictionary<string, long> amounts, bool expense, TreasuryReport report)
    {
        if (amounts == null) return;
        foreach (var pair in amounts)
        {
            long value = Math.Max(0L, pair.Value);
            var sources = expense ? report.expense_sources : report.income_sources;
            sources.TryGetValue(pair.Key, out long previous);
            sources[pair.Key] = previous + value;
            if (pair.Key == nameof(TreasuryCategory.InternalTransfer))
            { if (expense) report.transfer_out += value; else report.transfer_in += value; }
            else if (expense) report.expense += value;
            else report.income += value;
            bool essential = pair.Key == nameof(TreasuryCategory.Military) || pair.Key == nameof(TreasuryCategory.Administration) ||
                pair.Key == nameof(TreasuryCategory.Welfare) || pair.Key == nameof(TreasuryCategory.Maintenance) ||
                pair.Key == nameof(TreasuryCategory.Garrison) || pair.Key == nameof(TreasuryCategory.Governance);
            if (essential) report.essential_expense += expense ? value : -value;
            if (expense && pair.Key == nameof(TreasuryCategory.Maintenance)) report.maintenance_expense += value;
            if (pair.Key == nameof(TreasuryCategory.Military)) report.military += expense ? value : -value;
            if (expense && pair.Key == nameof(TreasuryCategory.Research)) report.research += value;
            if (expense && pair.Key == nameof(TreasuryCategory.Construction)) report.construction += value;
            if (expense && pair.Key == nameof(TreasuryCategory.Corruption)) report.corruption += value;
            if (!expense && (pair.Key == nameof(TreasuryCategory.ResidentTax) || pair.Key == nameof(TreasuryCategory.InternalTransfer) ||
                pair.Key == nameof(TreasuryCategory.PublicTrade) || pair.Key == nameof(TreasuryCategory.Tribute))) report.stable_income += value;
            // 每个账户的经常收支包含真实央地分成，投资和偶发款项单列。
            if (pair.Key != nameof(TreasuryCategory.Construction) && pair.Key != nameof(TreasuryCategory.Research) &&
                pair.Key != nameof(TreasuryCategory.RecoveredFunds) &&
                pair.Key != nameof(TreasuryCategory.Diplomacy) && pair.Key != nameof(TreasuryCategory.Policy))
                report.operating_balance += expense ? -value : value;
        }
    }

    // 整数分摊以总账单为准，余数按大小及输入顺序稳定分配。
    public static int[] Allocate(int bill, IReadOnlyList<int> budgets)
    {
        var shares = new int[budgets.Count];
        long total = budgets.Sum(value => (long)Math.Max(0, value));
        long due = Math.Min(Math.Max(0, bill), total);
        if (total == 0 || due == 0) return shares;
        long assigned = 0;
        var remainders = new List<(int index, long remainder)>();
        for (int i = 0; i < budgets.Count; i++)
        {
            long product = due * Math.Max(0, budgets[i]);
            shares[i] = (int)(product / total); assigned += shares[i];
            remainders.Add((i, product % total));
        }
        foreach (var part in remainders.OrderByDescending(p => p.remainder).ThenBy(p => p.index))
        {
            if (assigned >= due) break;
            if (shares[part.index] >= Math.Max(0, budgets[part.index])) continue;
            shares[part.index]++; assigned++;
        }
        return shares;
    }

    public static int Available(int money, long reserve, long project) =>
        (int)Math.Max(0L, (long)money - Math.Max(0L, reserve) - Math.Max(0L, project));

    // 分两步保存余数，金额为一金币时也不会长期偏向同一个账户。
    public static (int city, int local, int central) SplitTax(int tax, int cityPercent, int centralPercent,
        ref int cityCarry, ref int centralCarry)
    {
        tax = Math.Max(0, tax);
        cityPercent = Math.Max(0, Math.Min(100, cityPercent));
        centralPercent = Math.Max(0, Math.Min(100 - cityPercent, centralPercent));
        long first = (long)tax * cityPercent + Math.Max(0, Math.Min(99, cityCarry));
        int city = (int)(first / 100); cityCarry = (int)(first % 100);
        int remainder = tax - city, divisor = 100 - cityPercent;
        if (divisor == 0) { centralCarry = 0; return (city, 0, 0); }
        long second = (long)remainder * centralPercent + Math.Max(0, Math.Min(divisor - 1, centralCarry));
        int central = (int)(second / divisor); centralCarry = (int)(second % divisor);
        return (city, remainder - central, central);
    }
}
