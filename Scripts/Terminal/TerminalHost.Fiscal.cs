using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 半年财报(用户 2026-11-12 "每半年总结一次财报，也不一定真实""下面贪腐越严重财报越虚，模拟现实")：
// 每满半年户部呈一份：这半年收了多少(赋税、官营与关税、贡赋、其它)、支了多少(军费、官俸与日用、营建、研究、其它)、结余，
// 连同各地报上来的岁入与上缴。都是"报上来的数"，不是真账：
//   · 朝廷的数按朝廷的积弊虚报——收入往多里报、支出往少里报，账面比实情好看；被贪掉的钱账上没有这一项，混在"官俸与日用"里；
//   · 各地的岁入按各地自己的积弊虚报，往少里报(报歉收才好截留)；积弊越重报得越离谱，积弊轻的照实报；
//   · 上缴是现银入库，做不了假：照实数。报的岁入不少、缴上来的却少，就是该查的地方(点它可以遣官彻查，见 TerminalHost.Audit)；
//   · 政令不达的城连账都报不上来；微服私访亲眼看过、查账问责过、彻查查实过的城一年之内照实数(见 TerminalHost.Books)；
//   · 各期的收、支、库银留着，手机上画成折线看走势(画的也是报上来的数)。
// 虚多少用同一套(TerminalIntel.Reported：至多四成，同一期同一项报的数不变)。
// 账取自模组的财政账本(TreasurySystem.Report，近十二个月滚动，这里折成半年)
public static partial class TerminalHost
{
    // 留多少期(一期半年)：折线图画这么长的走势(用户 2026-11-12 "记得做折线图")
    private const int MaxFiscals = 24, MaxFiscalPlaces = 40;

    // 给手机画走势的：各期的收、支、库银，旧的在前
    private static JArray FiscalTrend(Seat seat)
    {
        var trend = new JArray();
        for (int i = seat.Fiscals.Count - 1; i >= 0; i--)
        {
            JObject report = seat.Fiscals[i];
            trend.Add(new JObject
            {
                ["y"] = report["y"], ["h"] = report["h"], ["now"] = report["now"], ["income"] = report["income"], ["expense"] = report["expense"],
                ["treasury"] = report["treasury"]
            });
        }
        return trend;
    }

    private static readonly string[] TaxKeys =
    {
        nameof(TreasuryCategory.ResidentTax), nameof(TreasuryCategory.LandTax), nameof(TreasuryCategory.IndustrialTax), nameof(TreasuryCategory.CommercialTax)
    };
    private static readonly string[] TradeKeys = { nameof(TreasuryCategory.PublicTrade), nameof(TreasuryCategory.PublicEnterprise), nameof(TreasuryCategory.Customs) };
    private static readonly string[] TributeKeys = { nameof(TreasuryCategory.Tribute), nameof(TreasuryCategory.Reparations), nameof(TreasuryCategory.ImperialLevy) };
    private static readonly string[] MilitaryKeys = { nameof(TreasuryCategory.Military), nameof(TreasuryCategory.War), nameof(TreasuryCategory.Garrison) };
    private static readonly string[] BuildKeys = { nameof(TreasuryCategory.Construction) };
    private static readonly string[] ResearchKeys = { nameof(TreasuryCategory.Research) };
    // 被贪掉的钱(Corruption)账上不单列：并在这一项里
    private static readonly string[] AdminKeys =
    {
        nameof(TreasuryCategory.Administration), nameof(TreasuryCategory.Governance), nameof(TreasuryCategory.Welfare), nameof(TreasuryCategory.Maintenance),
        nameof(TreasuryCategory.Palace), nameof(TreasuryCategory.Rites), nameof(TreasuryCategory.Corruption)
    };

    // 这座城的账眼下照实报：微服私访亲眼看过、查账问责过、彻查查实过，一年之内
    private static bool TrueSeen(Seat seat, City city, int month) =>
        seat.Toured.TryGetValue(city.getID(), out int seenAt) && month - seenAt < TourTruthMonths ||
        seat.TrueBooks.TryGetValue(city.getID(), out int checkedAt) && month - checkedAt < TourTruthMonths;

    // 每秒：过了半年的坎就出一份。头一回(刚领这位君主)先出一份眼下的账，不递奏报
    private static void RunFiscal(Seat seat)
    {
        int half = MonthNow() / 6;
        if (half == seat.FiscalHalf) return;
        bool first = seat.FiscalHalf == int.MinValue;
        seat.FiscalHalf = half;
        JObject report;
        try
        {
            report = BuildFiscal(seat, half, first);
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 财报没备成: {exception.Message}");
            return;
        }
        seat.Fiscals.Insert(0, report);
        if (seat.Fiscals.Count > MaxFiscals) seat.Fiscals.RemoveRange(MaxFiscals, seat.Fiscals.Count - MaxFiscals);
        seat.NextState = 0f;
        if (first) return;
        long balance = (long)report["balance"];
        Add(seat, string.Format(LM.Get("terminal_fiscal"), string.Format(LM.Get("terminal_fiscal_h" + (int)report["h"]), (int)report["y"]),
            (long)report["income"], (long)report["expense"], balance > 0 ? "+" + balance : balance.ToString()), true, false, "fiscal");
    }

    private static long Pick(Dictionary<string, long> sources, string[] keys)
    {
        long sum = 0L;
        foreach (string key in keys)
            if (sources.TryGetValue(key, out long value)) sum += value;
        return sum;
    }

    private static JObject BuildFiscal(Seat seat, int half, bool first)
    {
        Kingdom realm = seat.Realm;
        TreasuryReport book = TreasurySystem.Report(realm);
        int months = Math.Max(1, book.months);
        double factor = Math.Min(6, months) / (double)months;
        long Half(long value) => (long)Math.Round(value * factor);
        // 朝廷的数：收入往多里报(+1)，支出往少里报(-1)
        long Shown(long truth, int bias, int salt) =>
            TerminalIntel.Reported(truth, seat.Corruption, bias, TerminalIntel.Hash(realm.getID(), 50 + salt, half), out _);

        long tax = Half(Pick(book.income_sources, TaxKeys)), trade = Half(Pick(book.income_sources, TradeKeys)),
            tribute = Half(Pick(book.income_sources, TributeKeys));
        long otherIn = Math.Max(0L, Half(book.income) - tax - trade - tribute);
        long military = Half(Pick(book.expense_sources, MilitaryKeys)), admin = Half(Pick(book.expense_sources, AdminKeys)),
            build = Half(Pick(book.expense_sources, BuildKeys)), research = Half(Pick(book.expense_sources, ResearchKeys));
        long otherOut = Math.Max(0L, Half(book.expense) - military - admin - build - research);

        var incomes = new JArray();
        var expenses = new JArray();
        long income = 0L, expense = 0L;
        void Line(JArray into, string key, long truth, int bias, int salt, ref long total)
        {
            if (truth <= 0) return;
            long shown = Shown(truth, bias, salt);
            total += shown;
            into.Add(new JObject { ["k"] = key, ["v"] = shown });
        }
        Line(incomes, "tax", tax, 1, 1, ref income);
        Line(incomes, "trade", trade, 1, 2, ref income);
        Line(incomes, "tribute", tribute, 1, 3, ref income);
        Line(incomes, "other", otherIn, 1, 4, ref income);
        Line(expenses, "military", military, -1, 5, ref expense);
        Line(expenses, "admin", admin, -1, 6, ref expense);
        Line(expenses, "build", build, -1, 7, ref expense);
        Line(expenses, "research", research, -1, 8, ref expense);
        Line(expenses, "other", otherOut, -1, 9, ref expense);

        // 各地：报称岁入(按当地积弊往少里报)、上缴(实数)
        var places = new JArray();
        int month = MonthNow();
        foreach ((City city, long truth) in AuditCities(seat).Select(city => (city, truth: YearIncome(city))).OrderByDescending(found => found.truth)
                     .Take(MaxFiscalPlaces))
        {
            var row = new JObject { ["id"] = city.getID().ToString(), ["name"] = city.data.name };
            if (city.kingdom != realm) row["prov"] = NameOf(city.kingdom);
            float control = city == realm.capital || !AdministrativeControlSystem.Known(city) ? 1f : AdministrativeControlSystem.Get(city);
            bool toured = TrueSeen(seat, city, month);
            if (!toured && TerminalIntel.Clarity(control) >= 2)
            {
                row["none"] = true;
                places.Add(row);
                continue;
            }
            TreasuryReport local = TreasurySystem.Report(city);
            int span = Math.Max(1, local.months);
            double scale = Math.Min(6, span) / (double)span;
            double corruption = toured ? 0d : CorruptionRules.Bound(city.GetCorruptionRate());
            row["income"] = TerminalIntel.Reported((long)Math.Round(local.income * scale), corruption, -1, TerminalIntel.Hash(city.getID(), 41, half), out _);
            row["remit"] = (long)Math.Round(local.transfer_out * scale);
            places.Add(row);
        }

        int year = Date.getCurrentYear(), now = Date.getCurrentMonth();
        // 总结的是刚过去的那半年：上半年的坎上报的是去年下半年
        bool lower = now <= 6;
        return new JObject
        {
            ["y"] = lower ? year - 1 : year, ["h"] = lower ? 1 : 0, ["now"] = first, ["income"] = income, ["expense"] = expense,
            ["balance"] = income - expense, ["treasury"] = Figure(seat, realm.GetTreasuryBalance(), 1, 1, out _), ["in"] = incomes, ["out"] = expenses,
            ["places"] = places
        };
    }
}
