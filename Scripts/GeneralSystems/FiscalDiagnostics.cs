using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 年度验收日志(跟人口诊断同一节律)：
//   [财政诊断] 全世界国库、经常收支、国债、欠饷、限额拨款的总览 + 最大几国的明细
//   [事件统计] 本年各系统发生了什么(战后分配、举债、起义……)，由各系统 EventTally.Count 累加
// 只在每年输出时遍历一次国家和城市，不进存档。
public static class FiscalDiagnostics
{
    // ---- 事件统计 ----
    private static readonly Dictionary<string, int> Events = new();

    // 诊断：一笔支出把国库扣掉九成以上(原有 300 以上)：记下国家、类别和调用处(每国每类两年一次)，查"一有钱就变成 0"
    private static readonly Dictionary<(long, TreasuryCategory), double> DrainLogged = new();

    public static void LargeDrain(Kingdom kingdom, long before, int amount, TreasuryCategory category)
    {
        if (kingdom?.data == null || World.world == null || before < 300L || amount < before * 0.9d) return;
        double now = World.world.getCurWorldTime();
        var key = (kingdom.id, category);
        if (DrainLogged.TryGetValue(key, out double at) && now >= at && Date.getYearsSince(at) < 2) return;
        DrainLogged[key] = now;
        var frames = new global::System.Diagnostics.StackTrace(2, false).GetFrames() ?? new global::System.Diagnostics.StackFrame[0];
        string callers = string.Join(" <- ", frames.Take(4).Select(frame => frame.GetMethod())
            .Where(method => method != null).Select(method => $"{method.DeclaringType?.Name}.{method.Name}"));
        LogService.LogInfo($"[EmpireCraft][大额支出] {kingdom.name} 国库 {before} 一次付出 {amount}({category})：{callers}");
    }

    public static void Count(string key, int amount = 1)
    {
        if (string.IsNullOrEmpty(key) || amount <= 0) return;
        Events.TryGetValue(key, out int previous);
        Events[key] = previous + amount;
    }

    public static void LogYearly()
    {
        try
        {
            LogFiscal();
            LogEvents();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][财政诊断] 输出失败: {exception.Message}");
        }
    }

    private static void LogEvents()
    {
        if (Events.Count == 0) return;
        string line = string.Join("，", Events.OrderByDescending(pair => pair.Value)
            .Select(pair => $"{pair.Key} {pair.Value}"));
        Events.Clear();
        LogService.LogInfo($"[EmpireCraft][事件统计] {line}");
    }

    private static void LogFiscal()
    {
        if (World.world?.kingdoms == null) return;
        int states = 0, deficit = 0, indebted = 0, defaulted = 0, rationed = 0;
        long treasuries = 0, debt = 0;
        // 只给最富的三国拼说明(以前每国都拼一行字再挑三行)
        var biggest = new List<(long balance, Kingdom kingdom, long monthly, long bonds, double ratio)>();
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom == null || kingdom.isRekt() || !TreasurySystem.Enabled(kingdom)) continue;
            states++;
            long balance = kingdom.GetTreasuryBalance();
            treasuries += balance;
            TreasuryReport report = TreasurySystem.Report(kingdom);
            long monthly = report == null || report.months <= 0 ? 0L : report.operating_balance / report.months;
            if (monthly < 0L) deficit++;
            long bonds = StateDebtSystem.Outstanding(kingdom);
            debt += bonds;
            if (bonds > 0L) indebted++;
            if (StateDebtSystem.DefaultedRecently(kingdom)) defaulted++;
            double ratio = FiscalBudgetPlanner.NormalRatio(kingdom);
            if (ratio < 0.999d) rationed++;
            biggest.Add((balance, kingdom, monthly, bonds, ratio));
        }
        if (states == 0) return;
        long arrears = 0;
        int arrearsCities = 0;
        foreach (City city in World.world.cities)
        {
            CityStabilityData state = city?.data == null || city.isRekt() ? null : city.GetOrCreate().stability;
            if (state == null || state.owner_id != (city.kingdom?.id ?? -2L) || state.arrears <= 0) continue;
            arrears += state.arrears;
            arrearsCities++;
        }
        biggest.Sort((a, b) => b.balance.CompareTo(a.balance));
        var text = new StringBuilder();
        text.Append($"[EmpireCraft][财政诊断] {states} 国，国库合计 {HelperFunc.MoneyDisplay.Format(treasuries)}；")
            .Append($"月度赤字 {deficit} 国，负债 {indebted} 国(共 {HelperFunc.MoneyDisplay.Format(debt)})，")
            .Append($"违约 {defaulted} 国，限额拨款 {rationed} 国；欠饷 {arrearsCities} 城共 {HelperFunc.MoneyDisplay.Format(arrears)}。");
        if (biggest.Count > 0)
            text.Append("最大的国：").Append(string.Join("；", biggest.Take(3).Select(item =>
                $"{item.kingdom.GetKingdomName()} 库 {HelperFunc.MoneyDisplay.Format(item.balance)}/" +
                $"月 {(item.monthly >= 0 ? "+" : "")}{item.monthly}/债 {HelperFunc.MoneyDisplay.Format(item.bonds)}" +
                $"{(item.ratio < 0.999d ? $"/拨付 {item.ratio:0%}" : "")}")));
        LogService.LogInfo(text.ToString());
    }
}
