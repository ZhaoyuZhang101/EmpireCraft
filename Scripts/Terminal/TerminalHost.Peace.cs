using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 议和台(用户 2026-11-18 "还有战争天平也没从原版搬过来")，问答定"照游戏议和页"：
//   · 本国是主攻或主守的仗，各有一张自己的台子(不动神在战争窗口议和页上那张)：两国各出条件，可加可撤，每项折价，
//     天平算两国各自肯不肯；照目标开价、撮合、清空同游戏里的按钮；
//   · 签约：对面朝廷自己裁的，它肯才签(本国这一边君主自己拿主意，天平上的本国意愿只作参考；最短战期照常)；
//     对面是别的君主，递到他手里，他应允就照台上的签，不再看天平；
//   · 开战一方可以罢兵(算打输了，照败方赔款)；没有强令签约(那是神力)。
// 台子要在主线程算：手机发来一步(看、加撤、开价、撮合、清空、签约、罢兵)排进命令队列，办完把这张台子算好存着(rev 加一)，
// 手机隔一会儿再问，见 rev 变了就换上
public static partial class TerminalHost
{
    private const int MaxPeaceOptions = 40;
    private static readonly HashSet<string> PeaceOps = new() { "peek", "view", "toggle", "offer", "mediate", "clear", "sign", "withdraw" };
    private static readonly ConditionalWeakTable<Seat, Dictionary<long, PeaceDeal>> PeaceDrafts = new();
    private static readonly ConcurrentDictionary<string, (long war, string json)> PeaceCache = new();
    private static int _peaceRev;

    // ───────── 网络线程 ─────────
    private static TerminalServer.Reply PeaceReply(Seat seat, string body)
    {
        string op = Field(body, "op") ?? "view", key = Field(body, "key");
        if (!long.TryParse(Field(body, "war"), out long warId) || !PeaceOps.Contains(op) || key?.Length > 80)
            return TerminalServer.Reply.Error(400, "bad_request");
        // peek：只取算好的那一张，不另算(手机等一步办完时用)
        if (op != "peek") Commands.Enqueue(() => PeaceOp(seat, warId, op, key));
        return TerminalServer.Reply.Json(PeaceCache.TryGetValue(seat.Token, out var cached) && cached.war == warId ? cached.json : "{\"ready\":false}");
    }

    // ───────── 主线程 ─────────
    private static PeaceDeal SeatDraft(Seat seat, War war)
    {
        Dictionary<long, PeaceDeal> drafts = PeaceDrafts.GetOrCreateValue(seat);
        long id = war.data.id;
        if (!drafts.TryGetValue(id, out PeaceDeal deal)) drafts[id] = deal = PeaceTable.Offer(war);
        // 城已易主、库银已空的条款自然撤下
        deal.terms.RemoveAll(term => PeaceTable.Unavailable(war, term) != null);
        if (drafts.Count > 8)
            foreach (long stale in drafts.Keys.Where(other => other != id).ToList())
            {
                War old = World.world.wars.get(stale);
                if (old == null || old.hasEnded()) drafts.Remove(stale);
            }
        return deal;
    }

    private static void PeaceOp(Seat seat, long warId, string op, string key)
    {
        if (seat.Ruler == null || !WorldReady()) return;
        Kingdom realm = seat.Realm;
        War war = World.world.wars.get(warId);
        bool ours = PeaceTable.Usable(war) && (war.main_attacker == realm || war.main_defender == realm);
        if (!ours)
        {
            PeaceCache[seat.Token] = (warId, new JObject { ["ready"] = true, ["rev"] = ++_peaceRev, ["war"] = warId.ToString(), ["gone"] = true }
                .ToString(Newtonsoft.Json.Formatting.None));
            return;
        }
        bool attacker = war.main_attacker == realm;
        Kingdom other = attacker ? war.main_defender : war.main_attacker;
        PeaceDeal deal = SeatDraft(seat, war);
        switch (op)
        {
            case "toggle":
            {
                PeaceTerm term = PeaceTable.Options(war, true).Concat(PeaceTable.Options(war, false))
                    .Where(option => option.reason == null).Select(option => option.term).FirstOrDefault(option => option.Key == key);
                if (term == null) break;
                if (deal.terms.RemoveAll(item => item.Key == term.Key) > 0) break;
                deal.terms.RemoveAll(item => item.Slot == term.Slot);
                PeaceTerm copy = term.Copy();
                copy.added = true;
                deal.terms.Add(copy);
                break;
            }
            case "offer":
                deal.terms.Clear();
                deal.terms.AddRange(PeaceTable.Offer(war).terms);
                break;
            case "mediate":
                PeaceTable.MediateDeal(war, deal);
                break;
            case "clear":
                deal.terms.Clear();
                break;
            case "sign":
                SignPeace(seat, war, deal, other);
                break;
            case "withdraw":
                if (!attacker) break;
                Notify(seat, PeaceTable.Withdraw(war, false)
                    ? string.Format(LM.Get("terminal_peace_withdrawn"), NameOf(other))
                    : LM.Get("terminal_fail_generic"));
                break;
        }
        // 签成了、罢兵了：这场仗不在台上了
        PeaceCache[seat.Token] = (warId, (PeaceTable.Usable(war)
            ? PeaceJson(seat, war, deal, attacker, other)
            : new JObject { ["ready"] = true, ["rev"] = ++_peaceRev, ["war"] = warId.ToString(), ["gone"] = true }).ToString(Newtonsoft.Json.Formatting.None));
    }

    private static void SignPeace(Seat seat, War war, PeaceDeal deal, Kingdom other)
    {
        if (!WarPeaceRules.MayNegotiate(war))
        {
            Notify(seat, string.Format(LM.Get("terminal_peace_too_early"), WarPeaceRules.MinWarYears));
            return;
        }
        Seat host = HostOf(other, seat);
        if (host != null)
        {
            // 递给对面的君主：台上的条款照编号记着，他应允时照眼下的行情重新认一遍
            host.Offers.Add(new Offer
            {
                Id = ++_seq, From = seat, FromRealm = seat.Realm.getID(), FromName = NameOf(seat.Realm), Action = "peace",
                Terms = PeaceTermsTag + war.data.id + "|" + string.Join("|", deal.terms.Select(term => term.Key)), Since = Time.realtimeSinceStartup
            });
            host.NextDecrees = host.NextState = 0f;
            Notify(host, string.Format(LM.Get("terminal_offer_came"), NameOf(seat.Realm), Words("diplomacy_act_peace")));
            Notify(seat, string.Format(LM.Get("terminal_peace_sent"), NameOf(other)));
            return;
        }
        string summary = TerminalIntel.Strip(PeaceTable.Describe(war, deal));
        if (PeaceTable.SignDeal(war, deal, other))
        {
            Notify(seat, string.Format(LM.Get("terminal_peace_signed"), NameOf(other), summary));
            PeaceDrafts.GetOrCreateValue(seat).Remove(war.data.id);
            return;
        }
        Notify(seat, string.Format(LM.Get("terminal_fail_refused"), NameOf(other)));
    }

    // 递给对面君主的议和(Offer.Terms 以它开头)：战争编号|各条款的编号
    private const string PeaceTermsTag = "peace:";

    // 对面君主应允：照台上的条款签(条款照眼下的行情重新认，城易主了的自然没了)。回 null 是签成了
    private static string AcceptPeace(Seat seat, Offer offer, Kingdom from)
    {
        string[] parts = offer.Terms.Substring(PeaceTermsTag.Length).Split('|');
        War war = long.TryParse(parts[0], out long warId) ? World.world.wars.get(warId) : null;
        if (war == null || !PeaceTable.Usable(war) || !(war.main_attacker == from && war.main_defender == seat.Realm ||
                                                         war.main_defender == from && war.main_attacker == seat.Realm))
            return LM.Get("terminal_fail_gone");
        var keys = new HashSet<string>(parts.Skip(1));
        var deal = new PeaceDeal();
        foreach ((PeaceTerm term, string reason) in PeaceTable.Options(war, true).Concat(PeaceTable.Options(war, false)))
            if (reason == null && keys.Contains(term.Key) && !deal.Has(term)) deal.terms.Add(term.Copy());
        return PeaceTable.SignDeal(war, deal, null) ? null : LM.Get("terminal_fail_generic");
    }

    private static string PeaceOfferDetail(Seat seat, Offer offer)
    {
        string[] parts = offer.Terms.Substring(PeaceTermsTag.Length).Split('|');
        War war = long.TryParse(parts[0], out long warId) ? World.world.wars.get(warId) : null;
        if (war == null || !PeaceTable.Usable(war)) return "";
        var keys = new HashSet<string>(parts.Skip(1));
        var deal = new PeaceDeal();
        foreach ((PeaceTerm term, string reason) in PeaceTable.Options(war, true).Concat(PeaceTable.Options(war, false)))
            if (reason == null && keys.Contains(term.Key) && !deal.Has(term)) deal.terms.Add(term.Copy());
        return TerminalIntel.Strip(PeaceTable.Describe(war, deal));
    }

    // 给手机的一张台子
    private static JObject PeaceJson(Seat seat, War war, PeaceDeal deal, bool attacker, Kingdom other)
    {
        PeaceBasis basis = PeaceTable.Basis(war);
        (PeaceOpinion a, PeaceOpinion d) = PeaceTable.Evaluate(war, deal, basis);
        PeaceOpinion mine = attacker ? a : d, theirs = attacker ? d : a;
        (string left, string right) = PeaceTable.Hint(war, deal, basis, a, d);
        var json = new JObject
        {
            ["ready"] = true, ["rev"] = ++_peaceRev, ["war"] = war.data.id.ToString(), ["name"] = TerminalIntel.Strip(war.data.name ?? ""),
            ["me"] = attacker ? "a" : "d", ["other"] = NameOf(other), ["human"] = HostOf(other, seat) != null,
            ["years"] = WarSituation.WarYears(war), ["min"] = WarPeaceRules.MinWarYears, ["may"] = WarPeaceRules.MayNegotiate(war),
            ["give"] = PeaceSide(war, deal, basis, attacker), ["take"] = PeaceSide(war, deal, basis, !attacker),
            ["mine"] = Will(mine), ["theirs"] = Will(theirs),
            ["hint"] = new JArray(TerminalIntel.Strip(left ?? ""), TerminalIntel.Strip(right ?? "")),
            ["sum"] = TerminalIntel.Strip(PeaceTable.Describe(war, deal) ?? "")
        };
        if (attacker)
        {
            PeaceTerm pay = PeaceTable.WithdrawTerm(war);
            json["withdraw"] = pay == null ? "" : TerminalIntel.Strip(PeaceTable.Label(war, pay).label);
        }
        return json;
    }

    // 一方能出的条件：台上的在前，再是能出的(至多 MaxPeaceOptions 项)
    private static JArray PeaceSide(War war, PeaceDeal deal, PeaceBasis basis, bool byAttacker)
    {
        var list = new JArray();
        var seen = new HashSet<string>();
        IEnumerable<PeaceTerm> onTable = deal.terms.Where(term => term.by_attacker == byAttacker);
        IEnumerable<PeaceTerm> options = PeaceTable.Options(war, byAttacker).Where(option => option.reason == null).Select(option => option.term);
        foreach (PeaceTerm term in onTable.Concat(options))
        {
            if (list.Count >= MaxPeaceOptions || !seen.Add(term.Key)) continue;
            (string label, string tag, string _) = PeaceTable.Label(war, term);
            bool on = deal.Has(term);
            var row = new JObject
            {
                ["key"] = term.Key, ["label"] = TerminalIntel.Strip(label ?? ""), ["value"] = PeaceTable.Value(war, term, basis),
                ["cat"] = PeaceTable.Category(term)
            };
            if (!string.IsNullOrEmpty(tag)) row["tag"] = TerminalIntel.Strip(tag);
            if (on) row["on"] = true;
            if (on && deal.terms.First(item => item.Key == term.Key).added) row["new"] = true;
            list.Add(row);
        }
        return list;
    }

    private static JObject Will(PeaceOpinion will) => new()
    {
        ["total"] = will.Total, ["ok"] = will.Accepts, ["gain"] = will.gain, ["loss"] = will.loss, ["standing"] = will.standing,
        ["wear"] = will.wear, ["momentum"] = will.momentum, ["desperate"] = will.desperate, ["nation"] = will.nation
    };
}
