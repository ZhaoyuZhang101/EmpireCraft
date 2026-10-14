using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国债(无小人模式；参考维多利亚3“有偿债能力的借贷”)：
//   借：国库不到安全储备的四分之一，或正对一般地方限额拨款(FiscalBudgetPlanner)时，向本国各城百姓的民间现金借钱——
//       只借每户日用底线以上的部分，每城最多借出一半；借款不超过缺口，总债务不超过年经常收入的 2 倍。
//       没掌握“数学”的文明不会举债；一年内违约过的，没人肯借。借来的是真钱(百姓现金 → 国库)，记“举债所得”，不算经常收入。
//   息：年利率 4%，未掌握“银行”+3%，正统越低越贵(最多 +4%)，有欠饷 +2%，限 3%~12%；每月付给债权城的民间现金。
//       付不起就把利息滚进本金，记一次违约。
//   废债：债务超过年经常收入的 3 倍(多是利息滚进本金滚出来的)，各笔按比例削到合计 1 年收入，记一次违约。
//       以前没有这一步，旧档里有欠到几万亿、每月把国库全付了利息的国家
//   还：国库宽裕(可用资金为正)且没有限额拨款时，先还最早的一笔，最多拿出可用资金的一半。
//   债权城被毁：这笔债无人可还，作废并记违约。城易主仍照付(债权人是那里的百姓)。
//   正统：债务超过年经常收入、或一年内违约过，都损正统(见 MonarchyLegitimacy)。
public static class StateDebtSystem
{
    private const int CashReservePerHousehold = 2;
    private const int MinimumLoan = 10;
    private static double _lastScan = -1d;

    private static bool Active => CityPopulationSystem.AbstractPopulationEnabled;

    public static List<StateBond> Bonds(Kingdom kingdom) => kingdom?.data == null ? null : kingdom.GetOrCreate().state_bonds;

    public static long Outstanding(Kingdom kingdom)
    {
        long total = 0L;
        List<StateBond> bonds = Bonds(kingdom);
        if (bonds != null) foreach (StateBond bond in bonds) total += Math.Max(0L, bond.principal);
        return total;
    }

    public static float AverageRate(Kingdom kingdom)
    {
        double weighted = 0d, total = 0d;
        List<StateBond> bonds = Bonds(kingdom);
        if (bonds != null)
            foreach (StateBond bond in bonds) { weighted += bond.principal * (double)bond.rate; total += bond.principal; }
        return total > 0d ? (float)(weighted / total) : 0f;
    }

    public static long AnnualStableIncome(Kingdom kingdom)
    {
        TreasuryReport report = TreasurySystem.Report(kingdom);
        return report == null || report.months <= 0 ? 0L : report.stable_income * 12L / Math.Max(1, report.months);
    }

    public static bool DefaultedRecently(Kingdom kingdom)
    {
        double at = kingdom?.data == null ? -1d : kingdom.GetOrCreate().state_debt_default_at;
        return at >= 0d && Date.getYearsSince(at) < 1;
    }

    // 每月一次(挂在 CityPatch 的世界扫描轮转上)
    public static void TryMonthlyScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom == null || kingdom.isRekt() || !TreasurySystem.Enabled(kingdom)) continue;
            try
            {
                Service(kingdom);
                if (Active) TryBorrow(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][国债] {kingdom.name} 结算失败: {exception.Message}");
            }
        }
    }

    private static float RateFor(Kingdom kingdom) => StateDebtRules.AnnualRate(
        TechnologySystem.HasTech(TechnologySystem.GetCultureOf(kingdom), "banking"),
        kingdom.GetEmpire()?.Legitimacy ?? 50, CityStabilitySystem.HasArrears(kingdom));

    private static void TryBorrow(Kingdom kingdom)
    {
        if (!TechnologySystem.HasTech(TechnologySystem.GetCultureOf(kingdom), "mathematics") || DefaultedRecently(kingdom)) return;
        long need = StateDebtRules.BorrowNeed(kingdom.GetTreasuryBalance(), TreasurySystem.SafetyReserve(kingdom),
            FiscalBudgetPlanner.NormalRatio(kingdom) < 0.999d);
        if (need <= 0L) return;
        long room = StateDebtRules.BorrowRoom(AnnualStableIncome(kingdom), Outstanding(kingdom));
        long target = Math.Min(need, room);
        if (target < MinimumLoan || kingdom.cities == null) return;

        // 各城可借：民间现金超出每户底线部分的一半
        var lenders = new List<(City city, long amount)>();
        long lendable = 0L;
        foreach (City city in kingdom.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            CityPopulationData data = CityPopulationSystem.Get(city);
            // 民间投资池也可以买国债(参考维多利亚3)
            long amount = StateDebtRules.Lendable(EnterpriseSystem.Cash(data),
                (long)CityPopulationSystem.BackgroundHouseholds(city) * CashReservePerHousehold,
                (long)Math.Max(0f, data?.investment_pool ?? 0f));
            if (amount <= 0L) continue;
            lenders.Add((city, amount));
            lendable += amount;
        }
        if (lendable < MinimumLoan) return;
        double share = Math.Min(1d, target / (double)lendable);
        float rate = RateFor(kingdom);
        long borrowed = 0L;
        var bonds = kingdom.GetOrCreate().state_bonds ??= new List<StateBond>();
        foreach ((City city, long amount) in lenders)
        {
            long take = (long)Math.Floor(amount * share);
            if (take <= 0L) continue;
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data == null) continue;
            // 先借投资池，再借现金
            long fromPool = (long)Math.Min(take, Math.Floor(Math.Max(0f, data.investment_pool)));
            long fromCash = take - fromPool;
            if (fromCash > EnterpriseSystem.Cash(data)) continue;
            data.investment_pool -= fromPool;
            EnterpriseSystem.TakeCash(data, fromCash);
            // 同一座城只记一笔：新借的并进去，利率按金额加权
            StateBond existing = bonds.Find(bond => bond.city_id == city.data.id);
            if (existing != null)
            {
                existing.rate = StateDebtRules.MergedRate(existing.principal, existing.rate, take, rate);
                existing.principal += take;
            }
            else bonds.Add(new StateBond { city_id = city.data.id, principal = take, rate = rate, since = World.world.getCurWorldTime() });
            borrowed += take;
        }
        if (borrowed <= 0L) return;
        AddKingdomMoney(kingdom, borrowed, TreasuryCategory.Borrowing);
        // 向百姓举债是例行的事，不报世界提示(国债见国家窗口)
        FiscalDiagnostics.Count("国家举债");
    }

    private static void Service(Kingdom kingdom)
    {
        List<StateBond> bonds = Bonds(kingdom);
        if (bonds == null || bonds.Count == 0) return;
        bool defaulted = Restructure(kingdom, bonds);
        for (int i = bonds.Count - 1; i >= 0; i--)
        {
            StateBond bond = bonds[i];
            City creditor = World.world.cities.get(bond.city_id);
            if (bond.principal <= 0L) { bonds.RemoveAt(i); continue; }
            if (creditor?.data == null || creditor.isRekt())
            {
                // 债权城没了：无人可还，作废
                bonds.RemoveAt(i);
                defaulted = true;
                continue;
            }
            double carry = bond.interest_carry;
            int due = StateDebtRules.MonthlyInterest(bond.principal, bond.rate, ref carry);
            bond.interest_carry = carry;
            if (due <= 0) continue;
            int paid = (int)Math.Min(due, Math.Max(0L, kingdom.GetTreasuryBalance()));
            if (paid > 0)
            {
                kingdom.SubMoney(paid, TreasuryCategory.DebtInterest);
                EnterpriseSystem.CashIn(CityPopulationSystem.Get(creditor), paid);
            }
            if (paid < due)
            {
                bond.principal += due - paid;
                defaulted = true;
            }
        }
        if (defaulted)
        {
            FiscalDiagnostics.Count("国债违约");
            kingdom.GetOrCreate().state_debt_default_at = World.world.getCurWorldTime();
            return;
        }
        // 宽裕时还本：最早的一笔先还，最多拿出可用资金的一半
        if (FiscalBudgetPlanner.NormalRatio(kingdom) < 0.999d) return;
        long spare = StateSettlementSystem.DiscretionaryBalance(kingdom) / 2;
        for (int i = 0; i < bonds.Count && spare > 0L; i++)
        {
            StateBond bond = bonds[i];
            City creditor = World.world.cities.get(bond.city_id);
            if (creditor?.data == null || creditor.isRekt()) continue;
            int repay = (int)Math.Min(int.MaxValue, Math.Min(spare, bond.principal));
            if (repay <= 0) break;
            kingdom.SubMoney(repay, TreasuryCategory.DebtPrincipal);
            EnterpriseSystem.CashIn(CityPopulationSystem.Get(creditor), repay);
            bond.principal -= repay;
            spare -= repay;
        }
        bonds.RemoveAll(bond => bond.principal <= 0L);
    }

    private static bool Restructure(Kingdom kingdom, List<StateBond> bonds)
    {
        if (TreasurySystem.Report(kingdom).months < 3) return false;
        long outstanding = Outstanding(kingdom);
        long income = AnnualStableIncome(kingdom);
        double keep = StateDebtRules.RestructureKeep(income, outstanding);
        if (keep >= 1d) return false;
        foreach (StateBond bond in bonds)
        {
            bond.principal = (long)Math.Floor(bond.principal * keep);
            bond.interest_carry = 0d;
        }
        bonds.RemoveAll(bond => bond.principal <= 0L);
        FiscalDiagnostics.Count("国债废债重组");
        LogService.LogInfo($"[EmpireCraft][国债] {kingdom.name} 债务 {outstanding} 超过年经常收入 {income} 的 " +
                           $"{StateDebtRules.MaxDebtYears} 倍，废债重组为 {Outstanding(kingdom)}");
        return true;
    }

    private static void AddKingdomMoney(Kingdom kingdom, long amount, TreasuryCategory category)
    {
        while (amount > 0L)
        {
            int chunk = (int)Math.Min(int.MaxValue, amount);
            kingdom.AddMoney(chunk, category);
            amount -= chunk;
        }
    }
}
