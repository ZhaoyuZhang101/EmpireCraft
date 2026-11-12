using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 遣官彻查(用户 2026-11-12 "可以派官员去地方彻查贪腐，如果地方上税收少了")：
//   · 岁入见少就报：每年岁计时把各城这一年的岁入同上年比，少了四分之一以上的递一道奏报("某城岁入较去年少了约三成")。
//     一年至多报三处(少得最多的)；政令不达、账都看不清的城报不上来；这道奏报同别的一样会迟、会丢；
//   · 遣官：点那道奏报、点城，挑一位官员(中枢各官、别城的长官)去查。路上按远近一到三个月，查账两个月；
//   · 查的结果看人：才干高的查得深(当地积弊降得多、追回的赃银多)；积弊重的地方会拿钱堵他的嘴，
//     贪的人容易收、廉的人难收——收了就回一句"账目无亏"，同真没事时的回话一模一样，不作标记；
//   · 查实了、当地积弊又重的，长官照贪污交付问罪(走律法，不是当场定罪)。
// 追回的赃银入国库。数值都是我定的，用户没逐条确认
public static partial class TerminalHost
{
    private const int AuditWorkMonths = 2, MaxTaxAlerts = 3;
    private const double TaxFallShare = 0.25d, AuditDropBase = 0.10d, AuditDropSkill = 0.20d, AuditChargeAbove = 0.4d, AuditCleanBelow = 0.08d;
    private const long TaxFallFloor = 50;

    // 朝廷管得着账的城：本国各城；帝国之主连同各流官行政区的城
    private static List<City> AuditCities(Seat seat)
    {
        List<City> cities = LiveCities(seat.Realm);
        Empire court = OwnEmpire(seat);
        if (court != null)
            foreach (Kingdom member in court.kingdoms_list ?? new List<Kingdom>())
                if (PlainProvince(seat, court, member)) cities.AddRange(LiveCities(member));
        return cities;
    }

    private static long YearIncome(City city)
    {
        try
        {
            return TreasurySystem.Report(city).income;
        }
        catch (Exception)
        {
            return 0L;
        }
    }

    // 每年岁计时(见 Ledger)：各城岁入同上年比
    private static void TaxWatch(Seat seat, bool compare)
    {
        var now = new Dictionary<long, long>();
        var fallen = new List<(City city, double share)>();
        foreach (City city in AuditCities(seat))
        {
            long id = city.getID(), income = YearIncome(city);
            now[id] = income;
            if (!compare || !seat.CityIncome.TryGetValue(id, out long before) || before < TaxFallFloor || income >= before * (1d - TaxFallShare)) continue;
            float control = city == seat.Realm.capital || !AdministrativeControlSystem.Known(city) ? 1f : AdministrativeControlSystem.Get(city);
            if (TerminalIntel.Clarity(control) >= 2) continue;
            fallen.Add((city, (before - income) / (double)before));
        }
        foreach ((City city, double share) in fallen.OrderByDescending(found => found.share).Take(MaxTaxAlerts))
            Add(seat, string.Format(LM.Get("terminal_tax_fall"), city.data.name, Math.Max(1, (int)Math.Round(share * 10d))), true, false, "tax", city.getID());
        seat.CityIncome.Clear();
        foreach (KeyValuePair<long, long> entry in now) seat.CityIncome[entry.Key] = entry.Value;
    }

    // 查账的本事(0~1)：看才智与理政
    private static float AuditSkill(Actor official)
    {
        try
        {
            return Mathf.Clamp01((official.stats["intelligence"] + official.stats["stewardship"]) / 40f);
        }
        catch (Exception)
        {
            return 0.5f;
        }
    }

    // 从都城到那座城要走几个月(同手令的走法)
    private static int TravelMonths(Seat seat, City city)
    {
        if (seat.Sight == TerminalIntel.Sight.World) return 0;
        TileZone from = seat.Realm.capital?.getTile()?.zone, to = city?.getTile()?.zone;
        float distance = from == null || to == null ? 12f : CorpsSystem.ZoneDistance(from, to);
        int months = distance <= 6f ? 1 : distance <= 16f ? 2 : 3;
        return seat.Sight == TerminalIntel.Sight.Reach ? Math.Max(1, months - 1) : months;
    }

    // 给手机的：派得出去的官员
    private static JObject AuditJson(Seat seat)
    {
        var people = new JArray();
        foreach ((Actor actor, string post) in Notables(seat))
            people.Add(new JObject
            {
                ["id"] = actor.getID().ToString(), ["name"] = actor.getName(), ["post"] = post, ["skill"] = Mathf.RoundToInt(AuditSkill(actor) * 100f),
                // 近三年被参过几回(挑人时好掂量，见 TerminalHost.Review)
                ["hits"] = Hits(seat, actor.getID())
            });
        return new JObject { ["people"] = people, ["work"] = AuditWorkMonths };
    }

    // 下令：target 城，choice 官员。受理了返回 true
    private static bool AuditOrder(Seat seat, Order made, string target, string choice)
    {
        City city = long.TryParse(target, out long cityId) ? World.world.cities.get(cityId) : null;
        if (city?.data == null || city.isRekt() || !AuditCities(seat).Contains(city)) return false;
        Actor official = long.TryParse(choice, out long officialId)
            ? Notables(seat).Select(person => person.actor).FirstOrDefault(actor => actor.getID() == officialId) : null;
        // 自己查自己不算
        if (official == null || official == city.leader) return false;
        if (seat.Orders.Any(order => order.Kind == "audit" && order.Target == cityId && order.State == "sent"))
        {
            Notify(seat, string.Format(LM.Get("terminal_audit_busy"), city.data.name));
            return false;
        }
        made.Kind = "audit";
        made.Target = cityId;
        made.Arg = officialId.ToString();
        made.Due = MonthNow() + TravelMonths(seat, city) + AuditWorkMonths;
        made.Text = string.Format(LM.Get("terminal_order_audit"), official.getName(), city.data.name);
        return true;
    }

    // 查完回奏。办成返回 null(查无实据也算办成)
    private static string AuditExecute(Seat seat, Kingdom realm, Order order)
    {
        City city = World.world.cities.get(order.Target);
        if (city?.data == null || city.isRekt() || !AuditCities(seat).Contains(city)) return LM.Get("terminal_fail_gone");
        Actor official = long.TryParse(order.Arg, out long officialId) ? World.world.units.get(officialId) : null;
        if (official == null || !official.isAlive()) return LM.Get("terminal_fail_audit_dead");
        string name = official.getName(), place = city.data.name;
        double corruption = CorruptionRules.Bound(city.GetCorruptionRate());
        float skill = AuditSkill(official);
        // 积弊越重越舍得拿钱堵嘴；贪的人容易收，廉的人难收
        double bribe = Math.Min(0.8d, corruption * (official.hasTrait("greedy") ? 1.2d : official.hasTrait("honest") ? 0.15d : 0.6d));
        bool bought = TerminalIntel.Unit(TerminalIntel.Hash(order.Id, city.getID(), 31)) < bribe;
        if (bought || corruption < AuditCleanBelow)
        {
            Mark(seat, city.leader, "clean");
            Notify(seat, string.Format(LM.Get("terminal_audit_clean"), name, place));
            return null;
        }
        double drop = AuditDropBase + AuditDropSkill * skill;
        city.AddCorruptionRate(-drop);
        // 查实过的城：往后一年账照实报
        seat.TrueBooks[city.getID()] = MonthNow();
        Mark(seat, city.leader, "found");
        long stolen = 0L;
        try
        {
            stolen = Math.Max(0L, TreasurySystem.Report(city).corruption);
        }
        catch (Exception)
        {
            // 账本取不到：只整饬，不追赃
        }
        long recovered = (long)(stolen * (0.3d + 0.4d * skill));
        if (recovered > 0) realm.AddMoney((int)Math.Min(int.MaxValue, recovered));
        string text = string.Format(LM.Get("terminal_audit_found"), name, place, Mathf.RoundToInt((float)drop * 100f), recovered);
        // 积弊重的：长官照贪污交付问罪
        Actor head = city.leader;
        if (corruption >= AuditChargeAbove && head != null && head.isAlive() && head != seat.Ruler && head != official)
        {
            try
            {
                if (EmpireLawSystem.Prosecute(head, LawType.贪污, head.kingdom ?? realm, "terminal_reason_decree", official) != null)
                    text += string.Format(LM.Get("terminal_audit_charged"), head.getName());
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][终端] 彻查后的问罪没办成: {exception.Message}");
            }
        }
        Notify(seat, text);
        seat.NextDecrees = 0f;
        return null;
    }
}
