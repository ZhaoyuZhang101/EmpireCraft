using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

public enum TreasuryCategory
{
    Other, ResidentTax, InternalTransfer, PublicTrade, RecoveredFunds, Tribute,
    Military, Administration, Welfare, Maintenance, Research, Construction, Diplomacy, Corruption, Policy, Gift,
    Garrison, Governance, LandTax, IndustrialTax, CommercialTax,
    GovernanceArrears, GarrisonArrears, MilitaryArrears,
    PublicEnterprise
}

public sealed class TreasuryReport
{
    public long cash_balance => income - expense + transfer_in - transfer_out;
    public long income, expense, transfer_in, transfer_out, essential_expense, maintenance_expense, stable_income, operating_balance;
    public int months;
    public long military, research, construction, corruption, debt_repayment;
    public Dictionary<string, long> income_sources = new(), expense_sources = new(), tax_transfer_sources = new();
}

public static class TreasuryRules
{
    // 存档余额使用64位；原版建造/AI的一次报价仍是int，不能截断实际资产。
    public static int QuoteBalance(long balance) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, balance));

    public static long ChangeBalance(ref long balance, long change)
    {
        long next = checked(balance + change);
        balance = next;
        return change;
    }

    public static TreasuryCategory ArrearsCategory(TreasuryCategory service) => service switch
    {
        TreasuryCategory.Governance => TreasuryCategory.GovernanceArrears,
        TreasuryCategory.Garrison => TreasuryCategory.GarrisonArrears,
        TreasuryCategory.Military => TreasuryCategory.MilitaryArrears,
        _ => throw new ArgumentOutOfRangeException(nameof(service))
    };

    private static readonly string[] CategoryNames = Enum.GetNames(typeof(TreasuryCategory));
    private static readonly string[] EssentialCategories = { nameof(TreasuryCategory.Military),
        nameof(TreasuryCategory.Administration), nameof(TreasuryCategory.Welfare),
        nameof(TreasuryCategory.Maintenance), nameof(TreasuryCategory.Garrison), nameof(TreasuryCategory.Governance) };
    public static void Record(TreasuryData data, double now, Func<double, int> monthsSince,
        long change, TreasuryCategory category)
    {
        if (data == null || change == 0 || double.IsNaN(now) || double.IsInfinity(now)) return;
        data.cached_essential_expense = -1;
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

    public static void AttributeTaxTransfer(TreasuryData data, long amount, TreasuryCategory source)
    {
        if (amount <= 0 || data?.periods?.Count > 0 != true) return;
        if (source != TreasuryCategory.ResidentTax && source != TreasuryCategory.LandTax &&
            source != TreasuryCategory.IndustrialTax && source != TreasuryCategory.CommercialTax) return;
        var period = data.periods[data.periods.Count - 1];
        var sources = period.tax_transfer_income ??= new();
        period.income.TryGetValue(nameof(TreasuryCategory.InternalTransfer), out long transfers);
        long attributed = sources.Values.Sum();
        amount = Math.Min(amount, Math.Max(0L, transfers - attributed));
        if (amount <= 0) return;
        string key = source.ToString();
        sources.TryGetValue(key, out long old);
        sources[key] = old + amount;
    }

    private static void Trim(TreasuryData data, double now, Func<double, int> monthsSince)
    {
        data.periods ??= new List<TreasuryPeriod>();
        // Avoid allocating a captured predicate on every budget query.
        for (int i = data.periods.Count - 1; i >= 0; i--)
        {
            TreasuryPeriod period = data.periods[i];
            if (period != null && period.timestamp <= now && monthsSince(period.timestamp) < 12) continue;
            data.periods.RemoveAt(i);
            data.cached_essential_expense = -1;
        }
        // 防御损坏的旧档；正常记录最多十二个不重叠月度桶。
        if (data.periods.Count > 12)
        { data.periods.RemoveRange(0, data.periods.Count - 12); data.cached_essential_expense = -1; }
    }

    // Budget queries run during construction and research. Reuse the scalar between
    // transactions; check the bounded periods for expiry without building dictionaries.
    public static long EssentialExpense(TreasuryData data, double now, Func<double, int> monthsSince)
    {
        if (data == null || data.started < 0d || now < data.started) return 0;
        Trim(data, now, monthsSince);
        if (data.cached_essential_expense >= 0) return data.cached_essential_expense;
        long net = 0;
        foreach (var period in data.periods)
            foreach (string category in EssentialCategories)
            {
                if (period.expense?.TryGetValue(category, out long expense) == true) net += Math.Max(0L, expense);
                if (period.income?.TryGetValue(category, out long refund) == true) net -= Math.Max(0L, refund);
            }
        return data.cached_essential_expense = Math.Max(0L, net);
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
            if (period.tax_transfer_income != null)
                foreach (var source in period.tax_transfer_income)
                {
                    report.tax_transfer_sources.TryGetValue(source.Key, out long old);
                    report.tax_transfer_sources[source.Key] = old + Math.Max(0L, source.Value);
                }
        }
        return report;
    }

    private static void Sum(Dictionary<string, long> amounts, bool expense, TreasuryReport report, bool copySources = true)
    {
        if (amounts == null) return;
        foreach (var pair in amounts)
        {
            long value = Math.Max(0L, pair.Value);
            if (copySources)
            {
                var sources = expense ? report.expense_sources : report.income_sources;
                sources.TryGetValue(pair.Key, out long previous);
                sources[pair.Key] = checked(previous + value);
            }
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
            bool repayment = pair.Key == nameof(TreasuryCategory.GovernanceArrears) ||
                pair.Key == nameof(TreasuryCategory.GarrisonArrears) || pair.Key == nameof(TreasuryCategory.MilitaryArrears);
            if (repayment) report.debt_repayment += expense ? value : -value;
            if (!expense && (pair.Key == nameof(TreasuryCategory.ResidentTax) || pair.Key == nameof(TreasuryCategory.LandTax) ||
                pair.Key == nameof(TreasuryCategory.IndustrialTax) || pair.Key == nameof(TreasuryCategory.CommercialTax) || pair.Key == nameof(TreasuryCategory.InternalTransfer) ||
                pair.Key == nameof(TreasuryCategory.PublicTrade) || pair.Key == nameof(TreasuryCategory.Tribute) ||
                pair.Key == nameof(TreasuryCategory.PublicEnterprise))) report.stable_income += value;
            // 每个账户的经常收支包含真实央地分成，投资和偶发款项单列。
            if (pair.Key != nameof(TreasuryCategory.Construction) && pair.Key != nameof(TreasuryCategory.Research) &&
                pair.Key != nameof(TreasuryCategory.RecoveredFunds) &&
                pair.Key != nameof(TreasuryCategory.Diplomacy) && pair.Key != nameof(TreasuryCategory.Policy) && !repayment)
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

    public static int Available(long money, long reserve, long project)
        => (int)Math.Min(int.MaxValue, AvailableBalance(money, reserve, project));

    public static long AvailableBalance(long money, long reserve, long project)
    {
        // 先扣预留再限制单次报价，避免余额超过int上限时误判没有可用资金。
        long available = Math.Max(0L, money);
        available -= Math.Min(available, Math.Max(0L, reserve));
        available -= Math.Min(available, Math.Max(0L, project));
        return available;
    }

    // 合并实际账户后只显示净划拨。跨出本汇总范围的净额仍影响现金，不能一并删除。
    public static TreasuryReport Consolidate(IEnumerable<TreasuryReport> accounts)
    {
        var total = new TreasuryReport();
        foreach (var account in accounts)
        {
            if (account == null) continue;
            total.months = Math.Max(total.months, account.months);
            Merge(total.income_sources, account.income_sources);
            Merge(total.expense_sources, account.expense_sources);
        }
        string transfer = nameof(TreasuryCategory.InternalTransfer);
        total.income_sources.TryGetValue(transfer, out long incoming);
        total.expense_sources.TryGetValue(transfer, out long outgoing);
        long cancel = Math.Min(incoming, outgoing);
        if (incoming > cancel) total.income_sources[transfer] = incoming - cancel;
        else total.income_sources.Remove(transfer);
        if (outgoing > cancel) total.expense_sources[transfer] = outgoing - cancel;
        else total.expense_sources.Remove(transfer);
        Sum(total.income_sources, false, total, copySources: false);
        Sum(total.expense_sources, true, total, copySources: false);
        return total;
    }

    private static void Merge(Dictionary<string, long> target, Dictionary<string, long> source)
    {
        if (source == null) return;
        foreach (var item in source)
        {
            target.TryGetValue(item.Key, out long old);
            target[item.Key] = checked(old + Math.Max(0L, item.Value));
        }
    }

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
