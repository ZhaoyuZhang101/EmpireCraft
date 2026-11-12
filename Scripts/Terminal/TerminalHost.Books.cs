using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 查账(用户 2026-11-12 "那如何才能查到真实的账呢，帮我做个客户端的玩法")。财报上的数是报上来的，要见真账得自己查——
// 这一套只在终端这头，模组的规则不动：
//   · 封账：点一位官员主持，下月把账册封存起来——户部总账一册，各地岁入各一册(亏空大的几处混着几处寻常的，至多八册)。
//     一年只能查一回；
//   · 抽查：封存的账册不能册册都翻。主持的人才干越高，能抽的册数越多(二到五册)。抽到的那册当场见真数：亏空多少，或账实相符；
//   · 问责：认准了哪一册有鬼，就问那一处的责(至多两处)，抽没抽过都可以问——抽过是有凭据，没抽是赌。
//     问对了：追回一部分亏空、那一处的积弊降下去、长官照贪污交付问罪，那座城的账往后一年照实报；
//     问错了：查无实据，这一回问责的机会白用了；而且冤了人(用户 2026-11-12 "冤枉了别人会扣自己的威望，会引发与他同派系的人的不满")——
//     君主自己的威望掉一截，被冤的那位(那座城的长官；户部总账则是首辅)在朝里所属的一派圣眷降、怨望涨；
//   · 主持的人也可能被买通(朝廷积弊越重越容易，贪的人容易收、廉的人难收)：那就册册"账实相符"，不作标记。
//     可各册的真亏空还在，凭别的线索(财报上缴对不上的、私访见过的)硬问对了照样算数；
//   · 抽查到的那册能看完整的账目(用户 2026-11-12 "抽查的时候能看完整的账目表嘛，比如这个钱花在什么地方有什么收入")：
//     收入各项、支出各项，账面与实查两栏并排。贪掉的钱账面上没有这一项——账上是记在官俸日用头上的，实查才单列出来；
//     地方的收入账面上各项都比实收少(瞒下的)。主持的人被买通时两栏一模一样；
//   · 账册封存半年，过期自己结案。
// 亏空的数取自模组的财政账本里被贪掉的那一项(TreasuryReport.corruption)，不是凭空造的。次数与比例是我定的，用户没逐条确认
public static partial class TerminalHost
{
    private const int BooksEveryMonths = 12, BooksOpenMonths = 6, BooksSealMonths = 1, MaxBookRows = 8, BookBlames = 2;
    // 冤了人：威望掉二十分之一(至少这么多)；被冤者那一派的圣眷降多少、怨望涨多少
    private const int WrongRenownFloor = 15;
    private const float WrongFavor = 10f, WrongGrievance = 12f;

    // 账目里的一项：收入还是支出、账面数、实查数；Stolen 是被贪掉的那一项(账面上没有)
    internal sealed class BookLine
    {
        public string Name;
        public bool Income, Stolen;
        public long Book, Real;
    }

    internal sealed class BookRow
    {
        public List<BookLine> Lines = new();
        public string Id, Name, Result;
        public long CityId, Book, Gap;
        public bool Peeked, Blamed, Right;
    }

    internal sealed class BookCase
    {
        public string Official;
        public float Skill;
        public bool Bought;
        public int Peeks, Blames, Until;
        public readonly List<BookRow> Rows = new();
    }

    private static string SourceName(string key) =>
        LM.Has("fiscal_source_" + key) ? TerminalIntel.Strip(LM.Get("fiscal_source_" + key)) : key;

    // 一册的完整账目。scale 把账本(近十二个月)折成半年；账面收入 = 实收 × incomeFactor，账面支出 = 实支 × expenseFactor；
    // 贪掉的钱账面上并在官俸日用那一项里(没有这一项就并在最大的一项里)
    private static List<BookLine> BookLines(TreasuryReport report, double scale, double incomeFactor, double expenseFactor)
    {
        var lines = new List<BookLine>();
        foreach (KeyValuePair<string, long> pair in report.income_sources.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value))
        {
            long real = (long)Math.Round(pair.Value * scale);
            if (real <= 0) continue;
            lines.Add(new BookLine { Name = SourceName(pair.Key), Income = true, Real = real, Book = (long)Math.Round(real * incomeFactor) });
        }
        string stolenKey = nameof(TreasuryCategory.Corruption);
        long stolen = report.expense_sources.TryGetValue(stolenKey, out long taken) ? (long)Math.Round(Math.Max(0L, taken) * scale) : 0L;
        List<KeyValuePair<string, long>> spending = report.expense_sources.Where(pair => pair.Value > 0 && pair.Key != stolenKey)
            .OrderByDescending(pair => pair.Value).ToList();
        string cover = spending.Any(pair => pair.Key == nameof(TreasuryCategory.Administration)) ? nameof(TreasuryCategory.Administration)
            : spending.Count > 0 ? spending[0].Key : null;
        foreach (KeyValuePair<string, long> pair in spending)
        {
            long real = (long)Math.Round(pair.Value * scale);
            if (real <= 0 && pair.Key != cover) continue;
            lines.Add(new BookLine { Name = SourceName(pair.Key), Real = real, Book = (long)Math.Round((real + (pair.Key == cover ? stolen : 0L)) * expenseFactor) });
        }
        if (stolen <= 0) return lines;
        if (cover == null) lines.Add(new BookLine { Name = SourceName(nameof(TreasuryCategory.Other)), Book = (long)Math.Round(stolen * expenseFactor) });
        lines.Add(new BookLine { Name = SourceName(stolenKey), Real = stolen, Stolen = true });
        return lines;
    }

    // 给手机的：查账眼下到哪一步；封存着的账册(没抽查的册不给真数)
    private static JObject BooksJson(Seat seat)
    {
        int month = MonthNow();
        BookCase open = seat.Books;
        bool sent = seat.Orders.Any(order => order.Kind == "books_open" && order.State == "sent");
        var json = new JObject { ["state"] = open != null ? "open" : sent ? "sent" : "none", ["wait"] = Math.Max(0, seat.BooksNext - month) };
        if (open == null) return json;
        json["official"] = open.Official;
        json["peeks"] = open.Peeks;
        json["blames"] = open.Blames;
        json["left"] = Math.Max(0, open.Until - month);
        var rows = new JArray();
        foreach (BookRow row in open.Rows)
        {
            var item = new JObject { ["id"] = row.Id, ["name"] = row.Name, ["book"] = row.Book, ["peeked"] = row.Peeked, ["blamed"] = row.Blamed };
            // 主持的人被买通了：抽到哪册都是账实相符
            if (row.Peeked)
            {
                item["gap"] = open.Bought ? 0 : row.Gap;
                // 完整的账目：账面与实查。被买通时实查同账面一样，贪掉的那一项也不露出来
                var lines = new JArray();
                foreach (BookLine line in row.Lines)
                {
                    if (open.Bought && line.Stolen) continue;
                    lines.Add(new JObject
                    {
                        ["n"] = line.Name, ["inc"] = line.Income, ["book"] = line.Book, ["real"] = open.Bought ? line.Book : line.Real, ["stolen"] = line.Stolen
                    });
                }
                item["lines"] = lines;
            }
            if (row.Blamed)
            {
                item["result"] = row.Result;
                item["ok"] = row.Right;
            }
            rows.Add(item);
        }
        json["rows"] = rows;
        return json;
    }

    // 下令封账：choice 是主持的官员
    private static bool BooksOrder(Seat seat, Order made, string choice)
    {
        int month = MonthNow();
        if (seat.Books != null || month < seat.BooksNext || seat.Orders.Any(order => order.Kind == "books_open" && order.State == "sent"))
        {
            Notify(seat, LM.Get("terminal_books_wait"));
            return false;
        }
        Actor official = long.TryParse(choice, out long officialId)
            ? Notables(seat).Select(person => person.actor).FirstOrDefault(actor => actor.getID() == officialId) : null;
        if (official == null) return false;
        made.Kind = "books_open";
        made.Arg = officialId.ToString();
        made.Due = month + HomeMonths(seat) + BooksSealMonths;
        made.Text = string.Format(LM.Get("terminal_order_books"), official.getName());
        return true;
    }

    // 账册封存好了：定下各册的账面数与真亏空
    private static string BooksExecute(Seat seat, Kingdom realm, Order order)
    {
        Actor official = long.TryParse(order.Arg, out long officialId) ? World.world.units.get(officialId) : null;
        if (official == null || !official.isAlive()) return LM.Get("terminal_fail_audit_dead");
        int month = MonthNow(), half = month / 6;
        float skill = AuditSkill(official);
        double bribe = Math.Min(0.8d, seat.Corruption * (official.hasTrait("greedy") ? 1.2d : official.hasTrait("honest") ? 0.15d : 0.6d));
        var books = new BookCase
        {
            Official = official.getName(), Skill = skill, Peeks = 2 + Mathf.RoundToInt(skill * 3f), Blames = BookBlames, Until = month + BooksOpenMonths,
            Bought = TerminalIntel.Unit(TerminalIntel.Hash(order.Id, realm.getID(), 61)) < bribe
        };
        // 户部总账：账面是报上来的半年支出，亏空是其中被贪掉的
        TreasuryReport center = TreasurySystem.Report(realm);
        double scale = Math.Min(6, Math.Max(1, center.months)) / (double)Math.Max(1, center.months);
        var centerRow = new BookRow
        {
            Id = "0", Name = LM.Get("terminal_books_center"), Gap = (long)Math.Round(Math.Max(0L, center.corruption) * scale),
            Book = TerminalIntel.Reported((long)Math.Round(center.expense * scale), seat.Corruption, -1, TerminalIntel.Hash(realm.getID(), 59, half), out _)
        };
        // 朝廷的账：收入往多里报，支出往少里报(同财报)
        double realIncome = center.income * scale, realExpense = center.expense * scale;
        long shownIncome = TerminalIntel.Reported((long)Math.Round(realIncome), seat.Corruption, 1, TerminalIntel.Hash(realm.getID(), 58, half), out _);
        centerRow.Lines = BookLines(center, scale, realIncome > 0d ? shownIncome / realIncome : 1d, realExpense > 0d ? centerRow.Book / realExpense : 1d);
        books.Rows.Add(centerRow);
        // 各地：亏空大的几处，混着几处寻常的；次序打乱
        var all = new List<(City city, long gap, long book)>();
        foreach (City city in AuditCities(seat))
        {
            TreasuryReport local;
            try
            {
                local = TreasurySystem.Report(city);
            }
            catch (Exception)
            {
                continue;
            }
            double part = Math.Min(6, Math.Max(1, local.months)) / (double)Math.Max(1, local.months);
            double corruption = TrueSeen(seat, city, month) ? 0d : CorruptionRules.Bound(city.GetCorruptionRate());
            all.Add((city, (long)Math.Round(Math.Max(0L, local.corruption) * part),
                TerminalIntel.Reported((long)Math.Round(local.income * part), corruption, -1, TerminalIntel.Hash(city.getID(), 41, half), out _)));
        }
        var picked = all.OrderByDescending(found => found.gap).Take(4).ToList();
        picked.AddRange(all.Except(picked).OrderBy(found => TerminalIntel.Hash(found.city.getID(), 62, order.Id)).Take(MaxBookRows - 1 - picked.Count));
        foreach ((City city, long gap, long book) in picked.OrderBy(found => TerminalIntel.Hash(found.city.getID(), 63, order.Id)))
        {
            var row = new BookRow { Id = city.getID().ToString(), Name = city.data.name, CityId = city.getID(), Gap = gap, Book = book };
            try
            {
                // 地方的账：收入各项都往少里报(合起来就是报称的岁入)，支出照实、只是贪掉的钱换了名目
                TreasuryReport local = TreasurySystem.Report(city);
                double part = Math.Min(6, Math.Max(1, local.months)) / (double)Math.Max(1, local.months), income = local.income * part;
                row.Lines = BookLines(local, part, income > 0d ? book / income : 1d, 1d);
            }
            catch (Exception)
            {
                // 这一册的细目取不到：只有亏空的总数
            }
            books.Rows.Add(row);
        }
        seat.Books = books;
        seat.BooksNext = month + BooksEveryMonths;
        Notify(seat, string.Format(LM.Get("terminal_books_ready"), books.Official, books.Rows.Count, books.Peeks));
        return null;
    }

    // 手机上当场办的三样：抽查一册、问责一处、结案。是这三样返回 true
    private static bool BooksDecree(Seat seat, string id, string target)
    {
        if (id != "books_peek" && id != "books_blame" && id != "books_close") return false;
        BookCase open = seat.Books;
        if (open == null) return true;
        seat.Acks++;
        seat.NextState = seat.NextDecrees = 0f;
        if (id == "books_close")
        {
            CloseBooks(seat);
            return true;
        }
        BookRow row = open.Rows.FirstOrDefault(found => found.Id == target);
        if (row == null) return true;
        if (id == "books_peek")
        {
            if (row.Peeked || open.Peeks <= 0) return true;
            row.Peeked = true;
            open.Peeks--;
            return true;
        }
        if (row.Blamed || open.Blames <= 0) return true;
        row.Blamed = true;
        open.Blames--;
        Kingdom realm = seat.Realm;
        // 亏空够不够算"有鬼"：小于账面二十分之一的算寻常损耗
        if (row.Gap < Math.Max(20L, row.Book / 20))
        {
            row.Result = LM.Get("terminal_books_wrong_short");
            Notify(seat, string.Format(LM.Get("terminal_books_wrong"), row.Name) + Wronged(seat, row));
            return true;
        }
        row.Right = true;
        long recovered = (long)(row.Gap * (0.4d + 0.4d * open.Skill));
        if (recovered > 0) realm.AddMoney((int)Math.Min(int.MaxValue, recovered));
        string text = string.Format(LM.Get("terminal_books_right"), row.Name, row.Gap, recovered);
        if (row.CityId == 0) realm.AddCorruptionRate(-(0.05d + 0.05d * open.Skill));
        else
        {
            City city = World.world.cities.get(row.CityId);
            if (city?.data != null && !city.isRekt())
            {
                city.AddCorruptionRate(-(0.12d + 0.18d * open.Skill));
                seat.TrueBooks[row.CityId] = MonthNow();
                Mark(seat, city.leader, "found");
                Actor head = city.leader;
                if (head != null && head.isAlive() && head != seat.Ruler)
                {
                    try
                    {
                        if (EmpireLawSystem.Prosecute(head, LawType.贪污, head.kingdom ?? realm, "terminal_reason_decree", seat.Ruler) != null)
                            text += string.Format(LM.Get("terminal_audit_charged"), head.getName());
                    }
                    catch (Exception exception)
                    {
                        LogService.LogWarning($"[EmpireCraft][终端] 查账后的问罪没办成: {exception.Message}");
                    }
                }
            }
        }
        row.Result = string.Format(LM.Get("terminal_books_right_short"), recovered);
        Notify(seat, text);
        return true;
    }

    // 冤了人的代价：君主的威望掉一截；被冤的那位所属的一派圣眷降、怨望涨。返回接在奏报后头的话
    private static string Wronged(Seat seat, BookRow row)
    {
        string text = "";
        try
        {
            Actor ruler = seat.Ruler;
            int lost = Math.Min(ruler.data.renown, Math.Max(WrongRenownFloor, ruler.data.renown / 20));
            if (lost > 0)
            {
                ruler.data.renown -= lost;
                text += string.Format(LM.Get("terminal_books_wrong_renown"), lost);
            }
            // 考课里记一笔：这人曾被冤
            if (row.CityId != 0) Mark(seat, World.world.cities.get(row.CityId)?.leader, "wronged");
            Empire court = seat.Realm.GetEmpire();
            if (court == null) return text;
            City city = row.CityId == 0 ? null : World.world.cities.get(row.CityId);
            Actor wronged = row.CityId == 0 ? CourtSystem.AgendaSetter(court) : city?.leader;
            if (wronged == null || !wronged.isAlive() || wronged == ruler) return text;
            if (row.CityId == 0) Mark(seat, wronged, "wronged");
            FixedFaction faction = FactionOf(court, wronged);
            if (faction == null) return text;
            CourtSystem.AdjustFaction(court, faction, "favor", -WrongFavor);
            CourtSystem.AdjustFaction(court, faction, "grievance", WrongGrievance);
            text += string.Format(LM.Get("terminal_books_wrong_faction"), wronged.getName(), TerminalIntel.Strip(faction.Name ?? ""));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 冤案的后果没算成: {exception.Message}");
        }
        return text;
    }

    // 每秒：封存到期的账自己结案
    private static void RunBooks(Seat seat)
    {
        if (seat.Books != null && MonthNow() >= seat.Books.Until) CloseBooks(seat);
    }

    private static void CloseBooks(Seat seat)
    {
        if (seat.Books == null) return;
        seat.Books = null;
        Notify(seat, LM.Get("terminal_books_closed"));
    }
}
