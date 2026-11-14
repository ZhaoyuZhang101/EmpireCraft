using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.Terminal;

// 财报页的明细与开源节流(用户 2026-11-15 "我的收入支出来源也要清晰啊""收入不好的之后至少可以让我能调整税收,裁军或者别的什么")：
//   · 明细：收入、支出各按游戏账上的细目(fiscal_source_*)列，照朝廷报上来的数(积弊越重越失真，同总账)；
//   · 赋税：帝国才有统一的基础税率，一回加减 TaxStep(加税损天命、减税增天命，见 Empire.AddTaxRate)；有议会掌征税权的要议会点头；
//   · 裁军：没在打仗的军可以裁撤，省下军饷(见 CorpsSystem.Disband)；
// 都照政令一个月后办
public static partial class TerminalHost
{
    private const float TaxStep = 0.05f;
    private const int MaxDetailLines = 8, MaxDisband = 8;

    // 细目：键 → 报上来的数，从大到小
    private static JArray DetailJson(Seat seat, Dictionary<string, long> sources, double factor, int bias, int salt)
    {
        var list = new JArray();
        if (sources == null) return list;
        int i = 0;
        foreach (KeyValuePair<string, long> pair in sources.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Take(MaxDetailLines))
        {
            long truth = (long)Math.Round(pair.Value * factor);
            long shown = TerminalIntel.Reported(truth, seat.Corruption, bias, TerminalIntel.Hash(seat.Realm.getID(), 70 + salt + i++, MonthNow() / 6), out _);
            string key = "fiscal_source_" + pair.Key;
            list.Add(new JObject { ["label"] = LM.Has(key) ? LM.Get(key) : pair.Key, ["v"] = shown });
        }
        return list;
    }

    private static JObject FiscalDetailJson(Seat seat)
    {
        Kingdom realm = seat.Realm;
        if (realm?.data == null) return null;
        TreasuryReport book = TreasurySystem.Report(realm);
        int months = Math.Max(1, book.months);
        double factor = Math.Min(6, months) / (double)months;
        return new JObject
        {
            ["inTitle"] = LM.Get("fiscal_detail_in"), ["outTitle"] = LM.Get("fiscal_detail_out"), ["total"] = LM.Get("fiscal_detail_total"),
            ["note"] = LM.Get("fiscal_detail_note"),
            ["in"] = DetailJson(seat, TreasuryRules.SplitTaxTransfers(book.income_sources, book.tax_transfer_sources), factor, 1, 0),
            ["out"] = DetailJson(seat, book.expense_sources, factor, -1, 20),
            ["levers"] = LeversJson(seat)
        };
    }

    private static JObject LeversJson(Seat seat)
    {
        Kingdom realm = seat.Realm;
        var levers = new JObject { ["title"] = LM.Get("fiscal_lever_title"), ["note"] = LM.Get("fiscal_lever_note") };
        Empire empire = OwnEmpire(seat);
        if (empire != null)
        {
            bool can = ConstitutionalEconomySystem.CanChangeTax(empire);
            var tax = new JObject
            {
                ["label"] = string.Format(LM.Get("fiscal_tax_rate"), Math.Round(empire.data.TaxRate * 100f)),
                ["acts"] = new JArray(
                    new JObject { ["id"] = "fiscal:tax_up", ["label"] = LM.Get("fiscal_tax_up"), ["note"] = LM.Get("fiscal_tax_up_note") },
                    new JObject { ["id"] = "fiscal:tax_down", ["label"] = LM.Get("fiscal_tax_down"), ["note"] = LM.Get("fiscal_tax_down_note") })
            };
            if (!can)
                foreach (JObject act in (JArray)tax["acts"]) act["off"] = LM.Get("fiscal_tax_locked");
            levers["tax"] = tax;
        }
        else levers["taxNote"] = LM.Get("fiscal_tax_kingdom");
        var corps = new JArray();
        foreach (CorpsData item in CorpsSystem.Of(realm).Where(Idle).OrderByDescending(item => item.strength).Take(MaxDisband))
        {
            City home = World.world.cities.get(item.home_city_id);
            corps.Add(new JObject
            {
                ["id"] = "fiscal:disband:" + item.id, ["label"] = LM.Get("fiscal_disband"),
                ["name"] = CorpsSystem.Name(item), ["sub"] = string.Format(LM.Get("fiscal_corps_line"), (int)item.strength, home?.data?.name ?? "")
            });
        }
        levers["disbandTitle"] = LM.Get("fiscal_disband_title");
        levers["disbandNone"] = LM.Get("fiscal_disband_none");
        levers["corps"] = corps;
        return levers;
    }

    private static bool Idle(CorpsData corps) => corps != null && corps.task is not ("attack" or "defend");

    private static bool FiscalOrder(Seat seat, string id, Order made)
    {
        Kingdom realm = seat.Realm;
        if (id == "fiscal:tax_up" || id == "fiscal:tax_down")
        {
            if (OwnEmpire(seat) == null) return false;
            made.Kind = "fiscal";
            made.Arg = id.Substring(7);
            made.Text = LM.Get("terminal_order_" + made.Arg);
            return true;
        }
        if (id.StartsWith("fiscal:disband:", StringComparison.Ordinal) && long.TryParse(id.Substring(15), out long corpsId))
        {
            CorpsData corps = CorpsSystem.Of(realm).FirstOrDefault(item => item.id == corpsId);
            if (corps == null) return false;
            made.Kind = "fiscal";
            made.Arg = "disband";
            made.Target = corpsId;
            made.Text = string.Format(LM.Get("terminal_order_disband"), CorpsSystem.Name(corps));
            return true;
        }
        return false;
    }

    private static string FiscalExecute(Seat seat, Order order)
    {
        Kingdom realm = seat.Realm;
        switch (order.Arg)
        {
            case "tax_up":
            case "tax_down":
            {
                Empire empire = OwnEmpire(seat);
                if (empire == null) return LM.Get("terminal_fail_generic");
                if (!ConstitutionalEconomySystem.CanChangeTax(empire)) return LM.Get("fiscal_tax_locked");
                float before = empire.data.TaxRate;
                if (order.Arg == "tax_up") empire.AddTaxRate(TaxStep);
                else empire.SubTaxRate(TaxStep);
                return Math.Abs(empire.data.TaxRate - before) < 0.001f ? LM.Get("fiscal_tax_limit") : null;
            }
            case "disband":
            {
                CorpsData corps = CorpsSystem.Of(realm).FirstOrDefault(item => item.id == order.Target);
                if (corps == null) return LM.Get("terminal_fail_generic");
                if (!Idle(corps)) return LM.Get("fiscal_disband_busy");
                CorpsSystem.Disband(realm, corps);
                return null;
            }
        }
        return LM.Get("terminal_fail_generic");
    }
}
