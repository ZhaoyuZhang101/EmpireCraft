using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战争赔款(和约)：有明确胜负的国战结束时，败方主国向胜方主国赔款——
//   立即赔付国库现款的四分之一，另按月经常收入的两成分 36 个月支付(挂在败方 KingdomExtraData.war_reparations)。
//   付不起的月份顺延(不滚息、不另记违约)；收款方灭亡或双方再度开战(撕毁和约)时终止。
//   劫掠、游牧扩张、迫使朝贡、藩王索取皇位有各自的结算，叛乱、独立类战争是内战，都不赔款。
public static class WarReparationsSystem
{
    private const double ImmediateShare = 0.25d;
    private const double MonthlyIncomeShare = 0.2d;
    private const int Months = 36;
    private static double _lastScan = -1d;

    private static readonly HashSet<EmpireWarType> Excluded = new()
    {
        EmpireWarType.劫掠, EmpireWarType.游牧扩张, EmpireWarType.迫使朝贡, EmpireWarType.藩王索取皇位,
        EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立, EmpireWarType.附庸独立, EmpireWarType.清君侧
    };

    // 由 WarPatch.end_war 在战争收尾时调用
    public static void OnWarEnded(War war, WarWinner winner)
    {
        try
        {
            if (war?.data == null || winner == WarWinner.Nobody) return;
            if (Excluded.Contains(war.GetEmpireWarType())) return;
            if (war.getAsset() == WarTypeLibrary.rebellion) return;
            Kingdom loser = winner == WarWinner.Attackers ? war.main_defender : war.main_attacker;
            Kingdom victor = winner == WarWinner.Attackers ? war.main_attacker : war.main_defender;
            if (loser?.data == null || loser.isRekt() || victor?.data == null || victor.isRekt() || loser == victor) return;
            if (!TreasurySystem.Enabled(loser) || !TreasurySystem.Enabled(victor)) return;
            // 立即赔付：败方国库现款的四分之一
            long immediate = (long)(Math.Max(0L, loser.GetTreasuryBalance()) * ImmediateShare);
            MoveMoney(loser, victor, immediate);
            // 月付：败方月经常收入的两成，36 个月
            long monthly = Math.Max(1L, (long)(StateDebtSystem.AnnualStableIncome(loser) / 12d * MonthlyIncomeShare));
            var reparations = loser.GetOrCreate().war_reparations ??= new List<WarReparation>();
            reparations.Add(new WarReparation
            {
                creditor_kingdom_id = victor.id, monthly = monthly, months_left = Months
            });
            FiscalDiagnostics.Count("战争赔款");
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_reparations_signed"),
                loser.GetKingdomName(), victor.GetKingdomName(), MoneyDisplay.Format(immediate),
                MoneyDisplay.Format(monthly), Months), victor);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][赔款] 和约结算失败: {exception.Message}");
        }
    }

    // 每月一次(挂 CityPatch 的世界扫描轮转)
    public static void TryMonthlyScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Kingdom debtor in World.world.kingdoms)
        {
            List<WarReparation> reparations = debtor?.data == null ? null : debtor.GetOrCreate().war_reparations;
            if (reparations == null || reparations.Count == 0 || debtor.isRekt()) continue;
            try
            {
                Service(debtor, reparations);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][赔款] {debtor.name} 支付失败: {exception.Message}");
            }
        }
    }

    private static void Service(Kingdom debtor, List<WarReparation> reparations)
    {
        for (int i = reparations.Count - 1; i >= 0; i--)
        {
            WarReparation item = reparations[i];
            Kingdom creditor = World.world.kingdoms.get(item.creditor_kingdom_id);
            // 收款方灭亡或双方再度开战：和约作废
            if (item.months_left <= 0 || creditor == null || creditor.isRekt() ||
                debtor.isInWarWith(creditor))
            {
                reparations.RemoveAt(i);
                continue;
            }
            long paid = Math.Min(item.monthly, Math.Max(0L, debtor.GetTreasuryBalance()));
            if (paid > 0L) MoveMoney(debtor, creditor, paid);
            // 全额付清这个月才消一期，付不起顺延
            if (paid >= item.monthly) item.months_left--;
            if (item.months_left <= 0) reparations.RemoveAt(i);
        }
    }

    public static (long monthly, int monthsLeft) Owed(Kingdom debtor)
    {
        long monthly = 0L;
        int months = 0;
        List<WarReparation> reparations = debtor?.data == null ? null : debtor.GetOrCreate().war_reparations;
        if (reparations != null)
            foreach (WarReparation item in reparations)
            {
                monthly += item.monthly;
                months = Math.Max(months, item.months_left);
            }
        return (monthly, months);
    }

    private static void MoveMoney(Kingdom from, Kingdom to, long amount)
    {
        while (amount > 0L)
        {
            int chunk = (int)Math.Min(int.MaxValue, amount);
            from.SubMoney(chunk, TreasuryCategory.Reparations);
            to.AddMoney(chunk, TreasuryCategory.Reparations);
            amount -= chunk;
        }
    }
}
