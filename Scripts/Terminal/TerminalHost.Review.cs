using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 考课与弹章(用户 2026-11-12 "我该如何知道谁是贪官谁不是呢""现实中的皇帝是如何分辨的"，定："做")。
// 手机上看不到谁贪谁廉，同真皇帝一样只能拿几条互不相干的消息对着看。只在终端这头：
//   · 考课：每位官员(中枢各官、各城长官)一行——职位、派系、在任几年；城的长官另有硬数：治下上缴(上年全年，同前年比)、
//     治下积弊(政令通达看得清的城才有，同上年比)。上缴是现银，做不了假；
//   · 弹章：每季有一两位官员上疏参人贪墨，进奏报。有真有假——上疏的人廉，多半参的是真贪的；上疏的人自己贪，多半是借题整治别派的人。
//     不参同一派的人。奏报上写明谁参谁，真假不标；考课里每人名下记着近三年被谁参过、那人是哪一派；
//   · 对着看：几个不同派的人都参他、治下钱粮又少，八九不离十；只有对头那一派在参，多半是党争。认准了再遣官彻查、或直接降罪；
//   · 查过的留个记号：彻查回奏属实、回奏无亏(未必是真无亏)、查账问责属实、曾被冤。
// 谁真贪：贪婪的人、治下积弊重的长官(只在这里用来定弹章的真假，不发给手机)。次数与比例是我定的，用户没逐条确认
public static partial class TerminalHost
{
    private const int MaxChargeLog = 60, MaxReviewRows = 40, ChargeKeepMonths = 36, ChargeRepeatMonths = 12;

    internal sealed class Charge
    {
        public long Accuser, Target, City;
        public string AccuserName, AccuserFaction;
        public int Month;
    }

    // 在朝在任的官：中枢各官职的现任(帝国之主才有)、朝廷管得着账的各城的长官
    private static List<(Actor actor, string post, City city)> Officials(Seat seat)
    {
        var list = new List<(Actor, string, City)>();
        var seen = new HashSet<long>();
        void Take(Actor actor, string post, City city)
        {
            if (actor == null || !actor.isAlive() || actor == seat.Ruler || list.Count >= MaxReviewRows || !seen.Add(actor.getID())) return;
            list.Add((actor, post, city));
        }
        Empire court = OwnEmpire(seat);
        if (court != null)
            foreach (OfficeObject office in court.data.centerOffice?.GetAllOffices(court) ?? new List<OfficeObject>())
                if (office != null) Take(office.GetActor(), TerminalIntel.Strip(office.GetOfficeName(office.meta_object) ?? ""), null);
        foreach (City city in AuditCities(seat).OrderByDescending(city => city.getPopulationPeople())) Take(city.leader, city.data.name, city);
        return list;
    }

    // 这人有多贪(0~1)：贪婪的高、廉洁的低；城的长官再看治下的积弊。只用来定弹章的真假
    private static double Venal(Actor actor, City city)
    {
        double trait = actor.hasTrait("greedy") ? 0.85d : actor.hasTrait("honest") ? 0.1d : 0.45d;
        return city == null ? trait : 0.5d * trait + 0.5d * CorruptionRules.Bound(city.GetCorruptionRate());
    }

    private static string FactionName(Seat seat, Actor actor)
    {
        Empire court = seat.Realm.GetEmpire();
        return court == null ? "" : TerminalIntel.Strip(FactionOf(court, actor)?.Name ?? "");
    }

    // 每年岁计时：记下各城这一年的上缴与积弊(考课拿上年同前年比)
    private static void ReviewSnapshot(Seat seat)
    {
        foreach (City city in AuditCities(seat))
        {
            long id = city.getID(), remit = 0L;
            try
            {
                remit = TreasurySystem.Report(city).transfer_out;
            }
            catch (Exception)
            {
                // 账本取不到：记零
            }
            seat.RemitYears[id] = (remit, seat.RemitYears.TryGetValue(id, out (long last, long prev) money) ? money.last : -1L);
            double corruption = CorruptionRules.Bound(city.GetCorruptionRate());
            seat.CorrYears[id] = (corruption, seat.CorrYears.TryGetValue(id, out (double last, double prev) rate) ? rate.last : -1d);
        }
    }

    // 每秒：过了一季的坎就看有没有人上疏参人。头一回只记在任的人，不出弹章
    private static void RunReview(Seat seat)
    {
        int quarter = MonthNow() / 3;
        if (quarter == seat.ChargeQuarter) return;
        bool first = seat.ChargeQuarter == int.MinValue;
        seat.ChargeQuarter = quarter;
        int month = MonthNow();
        List<(Actor actor, string post, City city)> officials = Officials(seat);
        foreach ((Actor actor, string post, City city) in officials)
            if (!seat.Tenure.ContainsKey(actor.getID())) seat.Tenure[actor.getID()] = month;
        seat.Charges.RemoveAll(charge => month - charge.Month > ChargeKeepMonths);
        if (first || officials.Count < 3) return;
        long realm = seat.Realm.getID();
        // 上疏的多是中枢的官；没有中枢官职的国由各城长官互参
        List<(Actor actor, string post, City city)> accusers = officials.Where(found => found.city == null).ToList();
        if (accusers.Count == 0) accusers = officials;
        int count = 1 + (int)(TerminalIntel.Hash(realm, 71, quarter) % 2u);
        for (int n = 0; n < count; n++)
        {
            (Actor accuser, string accuserPost, City accuserCity) = accusers[(int)(TerminalIntel.Hash(realm, 72 + n, quarter) % (uint)accusers.Count)];
            string side = FactionName(seat, accuser);
            // 不参自己，不参同一派的人
            List<(Actor actor, string post, City city)> others =
                officials.Where(found => found.actor != accuser && (side.Length == 0 || FactionName(seat, found.actor) != side)).ToList();
            if (others.Count == 0) continue;
            (Actor actor, string post, City city) target;
            // 上疏的人越廉，参的越是真贪的；自己贪的多半是借题整人
            if (TerminalIntel.Unit(TerminalIntel.Hash(realm, 80 + n, quarter)) < 1d - Venal(accuser, accuserCity) * 0.8d)
            {
                List<(Actor actor, string post, City city)> rotten =
                    others.Where(found => Venal(found.actor, found.city) >= 0.5d).OrderByDescending(found => Venal(found.actor, found.city)).Take(3).ToList();
                if (rotten.Count == 0) continue;
                target = rotten[(int)(TerminalIntel.Hash(realm, 90 + n, quarter) % (uint)rotten.Count)];
            }
            else target = others[(int)(TerminalIntel.Hash(realm, 90 + n, quarter) % (uint)others.Count)];
            long targetId = target.actor.getID();
            if (seat.Charges.Any(charge => charge.Accuser == accuser.getID() && charge.Target == targetId && month - charge.Month < ChargeRepeatMonths)) continue;
            seat.Charges.Add(new Charge
            {
                Accuser = accuser.getID(), Target = targetId, City = target.city?.getID() ?? 0L, AccuserName = accuser.getName(), AccuserFaction = side, Month = month
            });
            if (seat.Charges.Count > MaxChargeLog) seat.Charges.RemoveAt(0);
            Add(seat, string.Format(LM.Get("terminal_impeach"), accuser.getName(), target.post, target.actor.getName()), true, false, "impeach",
                target.city?.getID() ?? 0L);
        }
    }

    // 近三年被参过几回(给挑人去查的名单用)
    private static int Hits(Seat seat, long actorId) => seat.Charges.Count(charge => charge.Target == actorId);

    // 查过的记号：found 查实、clean 回奏无亏、wronged 曾被冤
    private static void Mark(Seat seat, Actor actor, string kind)
    {
        if (actor != null) seat.Marks[actor.getID()] = (MonthNow(), kind);
    }

    // 给手机的考课表
    private static JObject ReviewJson(Seat seat)
    {
        int month = MonthNow();
        var rows = new JArray();
        foreach ((Actor actor, string post, City city) in Officials(seat))
        {
            long id = actor.getID();
            var row = new JObject
            {
                ["id"] = id.ToString(), ["name"] = actor.getName(), ["post"] = post, ["fac"] = FactionName(seat, actor), ["age"] = actor.getAge(),
                ["years"] = seat.Tenure.TryGetValue(id, out int since) ? Math.Max(0, (month - since) / 12) : 0
            };
            if (city != null)
            {
                row["city"] = city.getID().ToString();
                if (seat.RemitYears.TryGetValue(city.getID(), out (long last, long prev) money))
                {
                    row["remit"] = money.last;
                    if (money.prev >= 0) row["remitWas"] = money.prev;
                }
                // 积弊只有看得清的城才知道(同国势页那一行)
                float control = city == seat.Realm.capital || !AdministrativeControlSystem.Known(city) ? 1f : AdministrativeControlSystem.Get(city);
                if (TerminalIntel.Clarity(control) == 0 && seat.CorrYears.TryGetValue(city.getID(), out (double last, double prev) rate))
                {
                    row["corr"] = Math.Round(rate.last, 2);
                    if (rate.prev >= 0d) row["corrWas"] = Math.Round(rate.prev, 2);
                }
            }
            var hits = new JArray();
            foreach (Charge charge in seat.Charges.Where(charge => charge.Target == id).OrderByDescending(charge => charge.Month).Take(6))
                hits.Add(new JObject { ["by"] = charge.AccuserName, ["fac"] = charge.AccuserFaction, ["ago"] = month - charge.Month });
            row["hits"] = hits;
            if (seat.Marks.TryGetValue(id, out (int at, string kind) mark)) row["mark"] = new JObject { ["kind"] = mark.kind, ["ago"] = month - mark.at };
            rows.Add(row);
        }
        return new JObject { ["rows"] = rows };
    }
}
