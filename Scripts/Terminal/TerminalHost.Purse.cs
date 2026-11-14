using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.Terminal;

// 手机上的内帑(用户 2026-11-15 "玩家怎么给自己赚钱")：账目(今年、去年各从哪来)、年例四档、皇庄置田、卖官鬻爵、动用公帑；
// 规则见 PrivyPurse。都照政令一个月后办。一国之主才有(流官的钱不归这里)
public static partial class TerminalHost
{
    private const int MaxLandCities = 6, MaxSellOffices = 8;
    private static readonly string[] PurseSources = { PrivyPurse.Allowance, PrivyPurse.Estates, PrivyPurse.Offices, PrivyPurse.Taken, PrivyPurse.Graft };

    private static JObject PurseJson(Seat seat)
    {
        Kingdom realm = seat.Realm;
        Actor king = seat.Ruler;
        if (realm?.data == null || king == null || realm.king != king) return null;
        PrivyPurseData book = PrivyPurse.Book(realm);
        book.active = true;
        JArray Ledger(Dictionary<string, long> sums)
        {
            var list = new JArray();
            foreach (string source in PurseSources)
                if (sums.TryGetValue(source, out long v) && v > 0) list.Add(new JObject { ["label"] = LM.Get("purse_src_" + source), ["v"] = v });
            return list;
        }
        var tiers = new JArray();
        for (int tier = 0; tier < PrivyPurse.TierShare.Length; tier++)
        {
            var row = new JObject
            {
                ["id"] = "purse:tier:" + tier, ["label"] = LM.Get("purse_tier_" + tier), ["on"] = book.tier == tier,
                ["note"] = tier == 0 ? null : string.Format(LM.Get("purse_tier_note"), Math.Round(PrivyPurse.TierShare[tier] * 100f), PrivyPurse.AllowanceOf(realm, tier))
            };
            tiers.Add(row);
        }
        float land = PrivyPurse.RoyalLand(realm, out List<(City city, float share)> holdings);
        var lands = new JArray();
        foreach ((City city, float share) in holdings.Take(MaxLandCities))
            lands.Add(new JObject { ["name"] = city.data.name, ["share"] = Math.Round(share, 1) });
        var buy = new JArray();
        bool canBuy = realm.cities.Any(city => PrivyPurse.CanBuyLand(realm, city));
        if (canBuy)
            foreach (City city in realm.cities.Where(city => PrivyPurse.CanBuyLand(realm, city))
                         .OrderByDescending(city => city == realm.capital).ThenByDescending(city => city.getPopulationPeople()).Take(MaxLandCities))
                buy.Add(new JObject
                {
                    ["id"] = "purse:land:" + city.getID(), ["name"] = city.data.name,
                    ["label"] = string.Format(LM.Get("purse_land_buy"), PrivyPurse.BuyPoints, PrivyPurse.LandPrice(city))
                });
        var offices = new JArray();
        Empire empire = OwnEmpire(seat);
        if (empire != null)
            foreach ((int slot, OfficeObject office) in PrivyPurse.VacantOffices(empire).Take(MaxSellOffices))
                offices.Add(new JObject
                {
                    ["id"] = "purse:sell:" + slot, ["name"] = TerminalIntel.Strip(office.GetOfficeName(null) ?? ""),
                    ["label"] = string.Format(LM.Get("purse_sell"), PrivyPurse.OfficePrice(realm))
                });
        return new JObject
        {
            ["title"] = LM.Get("purse_title"), ["money"] = king.money, ["moneyLabel"] = LM.Get("purse_money"),
            ["thisYearLabel"] = LM.Get("purse_this_year"), ["lastYearLabel"] = LM.Get("purse_last_year"),
            ["thisYear"] = Ledger(book.thisYear), ["lastYear"] = Ledger(book.lastYear), ["none"] = LM.Get("purse_none"),
            ["tierTitle"] = LM.Get("purse_tier_title"), ["tierNote"] = LM.Get("purse_tier_help"), ["tiers"] = tiers,
            ["landTitle"] = LM.Get("purse_land_title"),
            ["landLine"] = string.Format(LM.Get("purse_land_line"), Math.Round(land, 1), Math.Round(PrivyPurse.MonthlyRent(realm), 1)),
            ["lands"] = lands, ["buy"] = buy, ["landClosed"] = canBuy ? null : LM.Get("purse_land_closed"),
            ["officeTitle"] = LM.Get("purse_office_title"), ["officeNote"] = LM.Get("purse_office_help"), ["offices"] = offices,
            ["officeNone"] = LM.Get(empire == null ? "purse_office_kingdom" : "purse_office_none"),
            ["takeTitle"] = LM.Get("purse_take_title"), ["takeNote"] = LM.Get("purse_take_help"),
            ["take"] = new JArray(
                new JObject { ["id"] = "purse:take:small", ["label"] = string.Format(LM.Get("purse_take"), PrivyPurse.TakeAmount(realm, PrivyPurse.SmallTake)) },
                new JObject { ["id"] = "purse:take:big", ["label"] = string.Format(LM.Get("purse_take"), PrivyPurse.TakeAmount(realm, PrivyPurse.BigTake)) })
        };
    }

    private static bool PurseOrder(Seat seat, string id, Order made)
    {
        if (!id.StartsWith("purse:", StringComparison.Ordinal) || seat.Realm?.king != seat.Ruler) return false;
        string[] parts = id.Split(':');
        if (parts.Length != 3) return false;
        made.Kind = "purse";
        made.Arg = parts[1] + ":" + parts[2];
        switch (parts[1])
        {
            case "tier" when int.TryParse(parts[2], out int tier) && tier >= 0 && tier < PrivyPurse.TierShare.Length:
                made.Text = string.Format(LM.Get("terminal_order_purse_tier"), LM.Get("purse_tier_" + tier));
                return true;
            case "land" when long.TryParse(parts[2], out long cityId):
                City city = World.world.cities.get(cityId);
                if (city?.data == null) return false;
                made.Text = string.Format(LM.Get("terminal_order_purse_land"), city.data.name);
                return true;
            case "sell" when int.TryParse(parts[2], out _):
                made.Text = LM.Get("terminal_order_purse_sell");
                return true;
            case "take" when parts[2] is "small" or "big":
                made.Text = LM.Get("terminal_order_purse_take_" + parts[2]);
                return true;
        }
        return false;
    }

    private static string PurseExecute(Seat seat, Order order)
    {
        Kingdom realm = seat.Realm;
        if (realm?.king == null || realm.king != seat.Ruler) return LM.Get("terminal_fail_generic");
        string[] parts = order.Arg.Split(':');
        string failed;
        switch (parts[0])
        {
            case "tier":
                failed = PrivyPurse.SetTier(realm, int.Parse(parts[1]));
                break;
            case "land":
                failed = PrivyPurse.BuyLand(realm, World.world.cities.get(long.Parse(parts[1])));
                break;
            case "sell":
                Empire empire = OwnEmpire(seat);
                string buyer = null;
                failed = empire == null ? "purse_office_kingdom" : PrivyPurse.SellOffice(empire, int.Parse(parts[1]), out buyer);
                if (failed == null && buyer != null) order.Note = string.Format(LM.Get("purse_sold_to"), buyer);
                break;
            case "take":
                failed = PrivyPurse.Embezzle(realm, parts[1] == "big" ? PrivyPurse.BigTake : PrivyPurse.SmallTake, out bool exposed, out int taken);
                if (failed == null) order.Note = string.Format(LM.Get(exposed ? "purse_take_exposed" : "purse_take_quiet"), taken);
                break;
            default:
                failed = "purse_fail";
                break;
        }
        return failed == null ? null : LM.Get(failed);
    }
}
