using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

namespace EmpireCraft.Scripts.GeneralSystems;

// 某个月的流水(按类别，含央地拨款)；month 是游戏里的月份(1~12)，months_ago = 0 为本月
public sealed class TreasuryMonth
{
    public int month;
    public int months_ago;
    public bool has_data;
    public readonly Dictionary<string, long> inflow = new();
    public readonly Dictionary<string, long> outflow = new();
    public long In => inflow.Values.Sum();
    public long Out => outflow.Values.Sum();
}

public static class TreasurySystem
{
    private const double MonthLength = 5d;

    // 近 12 个月逐月流水，从 11 个月前排到本月；没有记账的月份为空
    public static List<TreasuryMonth> MonthlyFlows(Kingdom kingdom)
    {
        var result = new List<TreasuryMonth>();
        if (!Enabled(kingdom)) return result;
        double now = World.world.getCurWorldTime();
        for (int ago = 11; ago >= 0; ago--)
            result.Add(new TreasuryMonth { months_ago = ago, month = Date.getMonth(now - ago * MonthLength) });
        List<TreasuryPeriod> periods = kingdom.GetOrCreate().treasury?.periods;
        if (periods == null) return result;
        foreach (TreasuryPeriod period in periods)
        {
            if (period == null || period.timestamp > now) continue;
            int ago = Date.getMonthsSince(period.timestamp);
            if (ago < 0 || ago > 11) continue;
            TreasuryMonth slot = result[11 - ago];
            slot.has_data = true;
            Merge(slot.inflow, TreasuryRules.SplitTaxTransfers(period.income, period.tax_transfer_income));
            Merge(slot.outflow, period.expense);
        }
        return result;
    }

    private static void Merge(Dictionary<string, long> into, Dictionary<string, long> amounts)
    {
        if (amounts == null) return;
        foreach (KeyValuePair<string, long> pair in amounts)
        {
            if (pair.Value <= 0) continue;
            into.TryGetValue(pair.Key, out long old);
            into[pair.Key] = old + pair.Value;
        }
    }

    public static IEnumerable<(string key, string value)> Details(TreasuryReport report)
    {
        foreach (TreasuryCategory category in Enum.GetValues(typeof(TreasuryCategory)))
        {
            string source = category.ToString();
            report.income_sources.TryGetValue(source, out long income);
            report.expense_sources.TryGetValue(source, out long expense);
            report.tax_transfer_sources.TryGetValue(source, out long sharedTax);
            if (sharedTax > 0) yield return ("fiscal_transfer_source_" + source, MoneyDisplay.Format(sharedTax));
            if (income <= 0 && expense <= 0) continue;
            yield return ("fiscal_source_" + source, string.Format(LM.Get("fiscal_source_format"),
                MoneyDisplay.Format(income), MoneyDisplay.Format(expense)));
        }
    }

    public static TreasuryReport Consolidated(Kingdom authority)
    {
        if (!Enabled(authority)) return new TreasuryReport();
        var empire = authority?.GetEmpire();
        IEnumerable<Kingdom> realms = empire?.CoreKingdom == authority ? empire.kingdoms_list : new[] { authority };
        IEnumerable<TreasuryReport> Accounts()
        {
            var visitedCities = new HashSet<City>();
            foreach (var realm in realms.Concat(new[] { authority }).Where(Enabled).Distinct())
            {
                yield return Report(realm);
                foreach (var city in realm.cities.Where(Enabled))
                    if (visitedCities.Add(city)) yield return Report(city);
            }
        }
        return TreasuryRules.Consolidate(Accounts());
    }

    // 撤军看实际能支付这座城费用的三个账户，避免每座危机城市都重新汇总整个帝国。
    public static TreasuryReport FundingReport(City city)
    {
        var total = new TreasuryReport();
        if (!Enabled(city)) return total;
        void Add(TreasuryReport report)
        {
            total.months = Math.Max(total.months, report.months);
            total.operating_balance += report.operating_balance;
        }
        Add(Report(city));
        Add(Report(city.kingdom));
        Kingdom central = city.kingdom.GetEmpire()?.CoreKingdom;
        if (central != city.kingdom && Enabled(central)) Add(Report(central));
        return total;
    }
    public static string FlowText(TreasuryReport report) => string.Format(LM.Get("fiscal_flows_format"),
        report.months, MoneyDisplay.Format(report.income), MoneyDisplay.Format(report.expense));
    public static string TransferText(TreasuryReport report) => string.Format(LM.Get("fiscal_transfers_format"),
        MoneyDisplay.Format(report.transfer_in), MoneyDisplay.Format(report.transfer_out));
    public static string SpendingText(TreasuryReport report) => string.Format(LM.Get("fiscal_spending_format"),
        MoneyDisplay.Format(report.military), MoneyDisplay.Format(report.research), MoneyDisplay.Format(report.construction),
        MoneyDisplay.Format(report.corruption));
    public static bool Enabled(Kingdom kingdom) => !ModClass.IS_CLEAR && World.world != null &&
        kingdom?.data != null && !kingdom.isRekt() &&
        !kingdom.wild && !AncientWarfareCompatibility.Owns(kingdom);
    public static bool Enabled(City city) => city?.data != null && !city.isRekt() &&
        !AncientWarfareCompatibility.OwnsObject(city) && Enabled(city.kingdom);

    public static void Record(Kingdom kingdom, long change, TreasuryCategory category)
    {
        if (!Enabled(kingdom) || change == 0) return;
        var data = kingdom.GetOrCreate().treasury ??= new TreasuryData();
        TreasuryRules.Record(data, World.world.getCurWorldTime(), Date.getMonthsSince, change, category);
    }
    public static void Record(City city, long change, TreasuryCategory category)
    {
        if (!Enabled(city) || change == 0) return;
        var data = city.GetOrCreate().treasury ??= new TreasuryData();
        TreasuryRules.Record(data, World.world.getCurWorldTime(), Date.getMonthsSince, change, category);
    }
    public static TreasuryReport Report(Kingdom kingdom) => Enabled(kingdom)
        ? TreasuryRules.Report(kingdom.GetOrCreate().treasury, World.world.getCurWorldTime(), Date.getMonthsSince) : new();
    public static TreasuryReport Report(City city) => Enabled(city)
        ? TreasuryRules.Report(city.GetOrCreate().treasury, World.world.getCurWorldTime(), Date.getMonthsSince) : new();

    public static long SafetyReserve(Kingdom kingdom) => Enabled(kingdom)
        ? Math.Max(Math.Max(0, ModClass.SETTLEMENT_RESERVE_GOLD), TreasuryRules.EssentialExpense(
            kingdom.GetOrCreate().treasury, World.world.getCurWorldTime(), Date.getMonthsSince) * (kingdom.hasEnemies() ? 2 : 1)) : 0;

    public static long SafetyReserve(City city) => Enabled(city)
        ? Math.Max(Math.Max(0, ModClass.SETTLEMENT_RESERVE_GOLD / 4), TreasuryRules.EssentialExpense(
            city.GetOrCreate().treasury, World.world.getCurWorldTime(), Date.getMonthsSince)) : 0;

    public static int DiscretionaryFunds(City city) => city == null || city.isRekt() ? 0
        : (int)Math.Min(int.MaxValue, DiscretionaryBalance(city));

    public static long DiscretionaryBalance(City city) => city == null || city.isRekt() ? 0L
        : TreasuryRules.AvailableBalance(city.GetTreasuryBalance(), SafetyReserve(city), 0);

    public static bool TrySpend(Kingdom kingdom, int amount, TreasuryCategory category, bool discretionary = true)
    {
        if (kingdom == null || kingdom.isRekt() || amount < 0) return false;
        int available = discretionary ? StateSettlementSystem.DiscretionaryFunds(kingdom) : Math.Max(0, kingdom.GetMoney());
        if (amount > available) return false;
        if (amount > 0) kingdom.SubMoney(amount, category);
        return true;
    }

    // 与政体引擎一样，制度性质读模板；旧档中的政体克隆可能缺少新增字段。
    public static (int city, int local, int central) TaxShares(Kingdom local)
    {
        var empire = local?.GetEmpire();
        var central = empire?.CoreKingdom;
        if (empire == null || empire.isRekt() || empire.IsArchived() || !Enabled(central)) return (40, 60, 0);
        var type = central.GetRegime()?.type;
        var territory = ConstitutionSystem.GetClauses(empire)?.territory;
        int centralPercent;
        if (type == RegimeType.Modern || type == RegimeType.Republic || territory.HasValue)
            centralPercent = territory == ConstitutionTerritory.Federal ? 35 : 60;
        else if (JimiSystem.IsJimiAdministration(local))
            centralPercent = JimiSystem.CentralTaxPercent;
        else
        {
            var regime = local.GetRegime();
            bool administrative = RegimeManager.GetTemplate(regime?.type)?.enfeoff_virtual_only ??
                regime?.enfeoff_virtual_only == true;
            centralPercent = administrative || local.GetKingdomType() == KingdomType.ZhouFeudalism_jun ||
                local.GetKingdomType() == KingdomType.Feudalism_intendancy ||
                local.GetKingdomType() == KingdomType.Feudalism_diocese ? 55 : 20;
            // 周室衰微(见 ZhouEraSystem)：春秋诸侯只给天子象征性的贡赋(霸主另收一成)，战国诸侯不再向周室交税；郡照旧
            int era = ZhouEraSystem.Era(empire);
            if (era >= ZhouEraSystem.Spring && local.GetKingdomType() != KingdomType.ZhouFeudalism_jun)
                centralPercent = era == ZhouEraSystem.Spring ? 5 : 0;
        }
        return (25, 75 - centralPercent, centralPercent);
    }

    public static string TaxSharingText(Kingdom kingdom)
    {
        var shares = TaxShares(kingdom);
        return string.Format(LM.Get("fiscal_tax_shares_format"), shares.city, shares.local, shares.central);
    }
    public static string TaxSharingText(City city) => city.getLoyalty() <= 0 && !CityStabilitySystem.ControlsTax(city)
        ? LM.Get("fiscal_tax_withheld") : TaxSharingText(city.kingdom);

    // 原法律系统对国王每次记罪都会增加暴虐值。即时缴税不能把一次年度贪污变成数千次治理惩罚。
    private static void RecordTaxCorruption(Actor actor)
    {
        var data = actor.GetOrCreate();
        double now = World.world.getCurWorldTime();
        if (data.last_fiscal_corruption_crime_timestamp >= 0d && now >= data.last_fiscal_corruption_crime_timestamp &&
            Date.getYearsSince(data.last_fiscal_corruption_crime_timestamp) < 1) return;
        if (actor.RecordCrime(LawType.贪污)) data.last_fiscal_corruption_crime_timestamp = now;
    }

    // 劫掠只转移现有现金；债务不能作为负数战利品转嫁给胜方。
    // share：拿走几成(劫掠三成，用户 2026-10-17 选"劫掠只拿三成国库")
    public static void TransferWarSpoils(City city, Kingdom recipient, float share = 1f)
    {
        if (city?.data == null || city.isRekt() || recipient == null || recipient.isRekt() ||
            city.kingdom == null || city.kingdom == recipient) return;
        bool capital = city.isCapitalCity();
        int amount = Math.Max(0, (int)((capital ? city.kingdom.GetMoney() : city.GetMoney()) * Math.Max(0f, Math.Min(1f, share))));
        if (amount == 0) return;
        if (capital) city.kingdom.SubMoney(amount, TreasuryCategory.Diplomacy);
        else city.SubMoney(amount, TreasuryCategory.Diplomacy);
        recipient.AddMoney(amount, TreasuryCategory.Diplomacy);
    }

    // 两种人口模式共用实际税款分成；年度 AI 不再抽取历史余额。
    public static void CollectResidentTax(City city, int amount, TreasuryCategory category = TreasuryCategory.ResidentTax)
    {
        if (amount <= 0 || city?.data == null || city.isRekt()) return;
        if (!Enabled(city)) { city.AddMoney(amount); return; }
        // 鞭长莫及：控制力低的城赋税收不齐(满控全收，最低一半，见 AdministrativeControlSystem.Yield)
        if (TaxErosionSystem.IsTax(category))
        {
            amount = (int)Math.Round(amount * (double)AdministrativeControlSystem.Yield(city));
            if (amount <= 0) return;
        }
        var local = city.kingdom;
        var empire = local.GetEmpire();
        var central = empire?.CoreKingdom;
        bool inEmpire = empire != null && !empire.isRekt() && !empire.IsArchived() && Enabled(central);
        var profile = TaxShares(local);
        int cityPercent = profile.city, centralPercent = profile.central;
        var data = city.GetOrCreate();
        string relationship = $"{local.id}/{(inEmpire ? central.id : -1L)}/{cityPercent}/{centralPercent}";
        if (data.tax_sharing_relationship != relationship)
        {
            data.tax_sharing_relationship = relationship;
            data.tax_city_carry = data.tax_central_carry = data.tax_corruption_carry = 0;
        }
        // 税基流失：积弊深了，征收前就有隐田逃户、胥吏舞弊(见 TaxErosionSystem)
        Layer.Empire realm = inEmpire ? empire : null;
        bool taxed = realm != null && TaxErosionSystem.IsTax(category);
        if (taxed)
        {
            var (hiddenShare, fraudShare) = TaxErosionSystem.Evasion(realm, category);
            int hidden = Math.Min(amount, TaxErosionSystem.Round(amount * (double)hiddenShare));
            int fraud = Math.Min(amount - hidden, TaxErosionSystem.Round(amount * (double)fraudShare));
            TaxErosionSystem.RecordAssessed(realm, amount, hidden, fraud);
            amount -= hidden + fraud;
            if (amount <= 0) return;
        }
        city.AddMoney(amount, category);
        // 城市征收环节的漏损只扣本期款项，并明确记为腐败支出。
        bool hasCollector = city.hasLeader() && city.leader.isAlive() && !city.leader.isRekt();
        double leak = hasCollector ? amount * CorruptionRules.Leak(city.GetCorruptionRate()) + data.tax_corruption_carry / 10000d : 0d;
        int stolen = Math.Min(amount, (int)leak);
        if (hasCollector) data.tax_corruption_carry = (int)Math.Floor((leak - stolen) * 10000d);
        if (stolen > 0)
        {
            city.SubMoney(stolen, TreasuryCategory.Corruption);
            city.leader.addMoney(stolen);
            RecordTaxCorruption(city.leader);
            if (taxed) TaxErosionSystem.RecordStolen(realm, stolen);
        }
        // 保留原城市征税 AI 的政治拒缴规则；恢复忠诚后只分配新税款，不追抽留存余额。
        if (city.getLoyalty() <= 0 && !CityStabilitySystem.ControlsTax(city)) return;
        var share = TreasuryRules.SplitTax(amount - stolen, cityPercent, centralPercent,
            ref data.tax_city_carry, ref data.tax_central_carry);
        int localShare = share.local, centralShare = share.central;
        // 地方截留：吏治坏了，省里把该解送中央的截下一部分(见 TaxErosionSystem)——截下的一半落入经手官吏私囊，
        // 只有一半真进了省里的库(用户 2026-10-20 选"积弊动真格"：以前截下的全归成员国，吏治越坏藩属越有钱养兵)
        int pocketed = 0;
        if (taxed && centralShare > 0 && local != central)
        {
            int withheld = Math.Min(centralShare, TaxErosionSystem.Round(centralShare * (double)TaxErosionSystem.Withholding(realm)));
            pocketed = Math.Min(withheld, TaxErosionSystem.Round(withheld * 0.5d));
            centralShare -= withheld;
            localShare += withheld - pocketed;
            TaxErosionSystem.RecordWithheld(realm, withheld - pocketed);
            TaxErosionSystem.RecordStolen(realm, pocketed);
        }
        if (pocketed > 0) city.SubMoney(pocketed, TreasuryCategory.Corruption);
        int paid = localShare + centralShare;
        if (paid > 0) city.SubMoney(paid, TreasuryCategory.InternalTransfer);
        if (localShare > 0) CreditTaxShare(local, localShare, category);
        if (centralShare > 0) CreditTaxShare(central, centralShare, category);
    }

    private static double Rate(double rate) => double.IsNaN(rate) || double.IsInfinity(rate) ? 0d : Math.Max(0d, Math.Min(1d, rate));

    private static void CreditTaxShare(Kingdom kingdom, int amount, TreasuryCategory source)
    {
        kingdom.AddMoney(amount, TreasuryCategory.InternalTransfer);
        TreasuryRules.AttributeTaxTransfer(kingdom.GetOrCreate().treasury, amount, source);
        if (!kingdom.hasKing() || kingdom.king.isRekt() || !kingdom.king.isAlive()) return;
        var data = kingdom.GetOrCreate();
        double leak = amount * CorruptionRules.Leak(kingdom.GetCorruptionRate()) + data.tax_corruption_carry / 10000d;
        int stolen = Math.Min(amount, (int)leak);
        data.tax_corruption_carry = (int)Math.Floor((leak - stolen) * 10000d);
        if (stolen <= 0) return;
        kingdom.SubMoney(stolen, TreasuryCategory.Corruption);
        kingdom.king.addMoney(stolen);
        RecordTaxCorruption(kingdom.king);
    }
}
