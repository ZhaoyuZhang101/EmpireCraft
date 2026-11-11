using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 外交天平(用户 2026-11-11 "关于外交，需要用帝国盒子里面的外交天平机制，这也是为什么适配玩家和玩家之间更好的利益交换"
// "如果对面不是玩家就由 ai 自行决定他所需的筹码")。帝国盒子里的外交天平就是议和台(PeaceTable)：条款统一折成"户"来称，
// 对方那一边不亏才肯。游戏里结盟、和亲、通好没有天平(DiplomacyActions 直接强写)，这里照议和台的口径给它们也搭一个：
//   · 两边都能往台上摆条件：库银三档(照议和台赔款的档，折价用 PeaceTermRules.MoneyValue)、割一座小城(CityValue)；
//   · 这件事本身对乙值多少 = 乙全境户数的几成 ×(乐意线 − 乙对甲的好感)%：好感够了不要价，越冷淡要价越高；
//   · 乙那边的账 = 甲出的 − 甲索的 − 要价，不小于零才肯；
//   · 乙不是真人：它自己开价(够抵要价的最省的一组条件)，照它的开价去准成；乙是真人：提议递到他手里待批，肯不肯由他裁，不看天平；
//   · 谈成了才落地：库银走赔款的路子(WarReparationsSystem.Impose)，割城排队换主(WarGoalSystem.QueueCession)。
// 议和照旧走议和台本身；对面是真人时同样递给他裁
public static partial class TerminalHost
{
    // 走天平的事 → (对乙值乙全境的几成, 乙好感到多少就不要价)
    private static readonly Dictionary<string, (double share, int line)> ScaleDeals = new()
    {
        ["ally"] = (0.08d, 40), ["marry"] = (0.04d, 20), ["pact"] = (0.03d, 20)
    };

    private const int MaxTermCities = 4;
    // Execute 的返回：提议已递到对面真人手里，等他回话(不算办成也不算没成)
    private const string Waiting = "\u0001waiting";

    internal sealed class Term
    {
        public string Id, Label;
        public bool Give, Money;
        public int Tier, Value;
        public long CityId;
    }

    internal sealed class Offer
    {
        public long Id, FromRealm;
        public Seat From;
        public string FromName, Action, Terms;
        public float Since;
    }

    // 领着这一国的别的席位(对面是不是真人)
    private static Seat HostOf(Kingdom kingdom, Seat except) =>
        SeatByToken.Values.FirstOrDefault(seat => seat != except && seat.Ruler != null && seat.Realm == kingdom);

    // 这件事乙要多少(户)；负数是乙也乐意
    private static int Need(string action, Kingdom from, Kingdom to)
    {
        (double share, int line) = ScaleDeals[action];
        double full = PeaceTermRules.RealmValue(to) * share;
        return (int)Math.Round(Math.Max(-full * 0.5d, full * (line - DiplomacyLedger.Opinion(to, from)) / 100d));
    }

    // 两边各能摆上台的条件。Give = 提议的一方(甲)出
    private static List<Term> TermOptions(Kingdom from, Kingdom to)
    {
        var terms = new List<Term>();
        foreach (bool give in new[] { true, false })
        {
            Kingdom payer = give ? from : to;
            string side = give ? "g" : "a";
            for (int tier = 0; tier < PeaceTermRules.IndemnityTiers.Length; tier++)
            {
                long money = PeaceTermRules.IndemnityAmount(payer, tier);
                if (money <= 0) continue;
                terms.Add(new Term
                {
                    Id = side + ":m:" + tier, Give = give, Money = true, Tier = tier, Value = PeaceTermRules.MoneyValue(payer, money),
                    Label = string.Format(LM.Get("terminal_term_money"), Math.Round(PeaceTermRules.IndemnityTiers[tier].share * 100d))
                });
            }
            foreach (City city in (payer.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt() && city != payer.capital)
                     .OrderBy(PeaceTermRules.CityValue).Take(MaxTermCities))
                terms.Add(new Term
                {
                    Id = side + ":c:" + city.getID(), Give = give, CityId = city.getID(), Value = PeaceTermRules.CityValue(city),
                    Label = string.Format(LM.Get("terminal_term_city"), city.data.name)
                });
        }
        return terms;
    }

    // 对面自己开价：够抵它要价的最省的一组条件(先看库银哪一档够，不够再把最大那档加上小城)；怎么都凑不够给 null
    private static List<string> Quote(int need, List<Term> terms)
    {
        var picked = new List<string>();
        if (need <= 0) return picked;
        List<Term> money = terms.Where(term => term.Give && term.Money).OrderBy(term => term.Value).ToList();
        Term enough = money.FirstOrDefault(term => term.Value >= need);
        if (enough != null)
        {
            picked.Add(enough.Id);
            return picked;
        }
        int sum = 0;
        if (money.Count > 0)
        {
            picked.Add(money[money.Count - 1].Id);
            sum = money[money.Count - 1].Value;
        }
        foreach (Term city in terms.Where(term => term.Give && !term.Money).OrderBy(term => term.Value))
        {
            if (sum >= need) break;
            picked.Add(city.Id);
            sum += city.Value;
        }
        return sum >= need ? picked : null;
    }

    // 给手机的：对这一国，各件事的要价与对面的开价、两边能摆的条件、对面是不是真人
    private static JObject ScaleJson(Seat seat, Kingdom other)
    {
        List<Term> terms = TermOptions(seat.Realm, other);
        var deals = new JObject();
        foreach (string action in ScaleDeals.Keys)
        {
            int need = Need(action, seat.Realm, other);
            List<string> quote = Quote(need, terms);
            deals[action] = new JObject { ["need"] = need, ["quote"] = quote == null ? null : new JArray(quote.ToArray()) };
        }
        var list = new JArray();
        foreach (Term term in terms)
            list.Add(new JObject { ["id"] = term.Id, ["give"] = term.Give, ["money"] = term.Money, ["value"] = term.Value, ["label"] = term.Label });
        return new JObject { ["deals"] = deals, ["terms"] = list, ["human"] = HostOf(other, seat) != null };
    }

    // 手机发来的条件(编号用逗号连着)照眼下的行情重新认一遍：城已易主、库银已空的自然就没了；同一边的库银只留一档
    private static List<Term> ParseTerms(string ids, Kingdom from, Kingdom to)
    {
        var picked = new List<Term>();
        if (string.IsNullOrEmpty(ids)) return picked;
        List<Term> options = TermOptions(from, to);
        foreach (string id in ids.Split(','))
        {
            Term term = options.FirstOrDefault(option => option.Id == id);
            if (term == null || picked.Contains(term) || term.Money && picked.Any(other => other.Money && other.Give == term.Give)) continue;
            picked.Add(term);
        }
        return picked;
    }

    // theirs：给对面(乙)看的说法(彼出、彼索)
    private static string Describe(List<Term> terms, bool theirs = false)
    {
        string gives = string.Join("、", terms.Where(term => term.Give).Select(term => term.Label));
        string asks = string.Join("、", terms.Where(term => !term.Give).Select(term => term.Label));
        if (gives.Length == 0 && asks.Length == 0) return "";
        return string.Format(LM.Get(theirs ? "terminal_terms_theirs" : "terminal_terms"), gives.Length > 0 ? gives : LM.Get("terminal_terms_none"),
            asks.Length > 0 ? asks : LM.Get("terminal_terms_none"));
    }

    // 谈成了：事办了，条件兑现。办不了返回缘由
    // force：两位真人谈成的结盟——不查条件、照神力的口径在游戏里尽量结上，结不上也不算没成(盟约记在终端这头)
    private static string Settle(Kingdom from, Kingdom to, DiplomacyActions.Action action, List<Term> terms, bool force = false)
    {
        string result = force
            ? MembershipCooldown.ByGod(() => action.run(from, to, DiplomacyActions.Goals[0]))
            : DiplomacyActions.Do(action, from, to, DiplomacyActions.Goals[0]);
        if (result != null && !force) return Words(result);
        foreach (Term term in terms)
        {
            Kingdom payer = term.Give ? from : to, taker = term.Give ? to : from;
            if (term.Money)
            {
                (double share, bool installments) = PeaceTermRules.IndemnityTiers[term.Tier];
                WarReparationsSystem.Impose(payer, taker, share, installments);
            }
            else
            {
                City city = World.world.cities.get(term.CityId);
                if (city?.data != null && !city.isRekt() && city.kingdom == payer) WarGoalSystem.QueueCession(city, taker);
            }
        }
        return null;
    }

    private static War WarBetween(Kingdom realm, Kingdom other) => realm.getWars().FirstOrDefault(war =>
        war?.data != null && !war.hasEnded() && (war.isAttacker(other) || war.isDefender(other) || war.main_attacker == other || war.main_defender == other));

    // 使者到了：办这一桩外交。成了返回 null，递给对面真人了返回 Waiting，不成返回缘由
    private static string Negotiate(Seat seat, Order order, Kingdom other, DiplomacyActions.Action action)
    {
        Kingdom realm = seat.Realm;
        Seat host = HostOf(other, seat);
        if (order.Arg == "peace")
        {
            War war = WarBetween(realm, other);
            if (war == null) return LM.Get("terminal_fail_gone");
            if (host != null)
            {
                Present(host, seat, order);
                return Waiting;
            }
            PeaceTable.Mediate(war);
            return PeaceTable.Sign(war, false) ? null : string.Format(LM.Get("terminal_fail_refused"), other.data.name);
        }
        if (order.Arg == "leave_alliance" && Allied(realm, other))
        {
            // 背盟：终端这头的盟约当场解除，游戏里有同盟的也退(退不了不管)；对面是真人的告诉他一声
            Unbind(realm, other);
            DiplomacyActions.Do(action, realm, other, DiplomacyActions.Goals[0]);
            if (host != null) Notify(host, string.Format(LM.Get("terminal_pact_broken"), realm.data.name));
            return null;
        }
        if (!ScaleDeals.ContainsKey(order.Arg))
        {
            // 单方面的事(宣战、退盟、遣使修好)：不用对方答应
            string result = DiplomacyActions.Do(action, realm, other, DiplomacyActions.Goals[0]);
            return result == null ? null : Words(result);
        }
        if (host != null)
        {
            Present(host, seat, order);
            return Waiting;
        }
        List<Term> terms = ParseTerms(order.Extra, realm, other);
        int total = terms.Where(term => term.Give).Sum(term => term.Value) - terms.Where(term => !term.Give).Sum(term => term.Value) -
                    Need(order.Arg, realm, other);
        return total < 0 ? string.Format(LM.Get("terminal_fail_scale"), other.data.name, -total) : Settle(realm, other, action, terms);
    }

    // 把提议递到对面真人手里
    private static void Present(Seat host, Seat from, Order order)
    {
        host.Offers.Add(new Offer
        {
            Id = ++_seq, From = from, FromRealm = from.Realm.getID(), FromName = from.Realm.data.name, Action = order.Arg, Terms = order.Extra ?? "",
            Since = Time.realtimeSinceStartup
        });
        host.NextDecrees = host.NextState = 0f;
        Notify(host, string.Format(LM.Get("terminal_offer_came"), from.Realm.data.name, Words("diplomacy_act_" + order.Arg)));
    }

    private static void AddOffers(Seat seat, JArray pending)
    {
        foreach (Offer offer in seat.Offers)
        {
            Kingdom from = World.world.kingdoms.get(offer.FromRealm);
            string detail = from?.data == null || from.isRekt() ? "" : Describe(ParseTerms(offer.Terms, from, seat.Realm), true);
            pending.Add(new JObject
            {
                ["id"] = "offer:" + offer.Id, ["kind"] = "offer",
                ["title"] = string.Format(LM.Get("terminal_offer_title"), offer.FromName, Words("diplomacy_act_" + offer.Action)), ["detail"] = detail,
                ["left"] = Mathf.Max(0, Mathf.RoundToInt((HoldSeconds - (Time.realtimeSinceStartup - offer.Since)) / 60f)),
                ["choices"] = new JArray("accept", "refuse")
            });
        }
    }

    // 真人裁了：应允就照提议办，回绝(或搁太久)就回对方一句不许
    private static void Answer(Seat seat, Offer offer, bool accept)
    {
        seat.Offers.Remove(offer);
        seat.NextDecrees = seat.NextState = 0f;
        Kingdom from = World.world.kingdoms.get(offer.FromRealm);
        bool fromThere = offer.From?.Ruler != null && offer.From.Realm == from && SeatByToken.ContainsKey(offer.From.Token);
        string what = Words("diplomacy_act_" + offer.Action);
        string failure = null;
        if (accept)
        {
            DiplomacyActions.Action action = OpenActions().FirstOrDefault(found => Suffix(found.key) == offer.Action);
            if (from?.data == null || from.isRekt() || action == null) failure = LM.Get("terminal_fail_gone");
            else if (offer.Action == "peace")
            {
                War war = WarBetween(seat.Realm, from);
                if (war == null) failure = LM.Get("terminal_fail_gone");
                else
                {
                    PeaceTable.Mediate(war);
                    if (!PeaceTable.Sign(war, true)) failure = LM.Get("terminal_fail_generic");
                }
            }
            else
            {
                // 两位真人谈成的结盟不看游戏里的外交开关：对方应允就成
                bool pact = offer.Action == "ally";
                string reason = pact ? null : DiplomacyActions.Check(action, from, seat.Realm);
                failure = reason != null ? Words(reason) : Settle(from, seat.Realm, action, ParseTerms(offer.Terms, from, seat.Realm), pact);
                if (failure == null && pact) Bind(from, seat.Realm);
            }
        }
        bool done = accept && failure == null;
        if (fromThere)
            Notify(offer.From, string.Format(LM.Get(done ? "terminal_offer_accepted" : "terminal_offer_refused"), seat.Realm.data.name, what) +
                               (failure != null ? "（" + failure + "）" : ""));
        Notify(seat, string.Format(LM.Get(done ? "terminal_offer_did_accept" : "terminal_offer_did_refuse"), offer.FromName, what) +
                     (failure != null ? "（" + failure + "）" : ""));
    }

    // ───────── 君主之间的盟约 ─────────
    // (用户 2026-11-11 "玩家的结盟不需要原版开关，只要玩家直接同意结盟两边就会成为同盟，一旦有人攻打另一方自己就必须应战")
    // 两位真人谈成的结盟，盟约记在终端这头，不靠游戏里的同盟(那个受外交开关、远近限制，AI 还会自己退)。
    // 盟约在，一方被人打上门(在一场战争里作守方)，另一方就以守方参战。背盟(退盟)或一方亡国，盟约解除
    private static readonly HashSet<(long, long)> RulerPacts = new();

    private static (long, long) PactKey(Kingdom a, Kingdom b) =>
        a.getID() < b.getID() ? (a.getID(), b.getID()) : (b.getID(), a.getID());

    private static bool Allied(Kingdom a, Kingdom b) => RulerPacts.Contains(PactKey(a, b));

    private static void Bind(Kingdom a, Kingdom b) => RulerPacts.Add(PactKey(a, b));

    private static void Unbind(Kingdom a, Kingdom b) => RulerPacts.Remove(PactKey(a, b));

    // 每秒：守约——盟邦被打上门的，把另一方拉进守方
    private static void RunPacts()
    {
        if (RulerPacts.Count == 0) return;
        foreach ((long first, long second) in RulerPacts.ToList())
        {
            Kingdom a = World.world.kingdoms.get(first), b = World.world.kingdoms.get(second);
            if (a?.data == null || a.isRekt() || b?.data == null || b.isRekt())
            {
                RulerPacts.Remove((first, second));
                continue;
            }
            Defend(a, b);
            Defend(b, a);
        }
    }

    private static void Defend(Kingdom victim, Kingdom ally)
    {
        foreach (War war in victim.getWars().ToList())
        {
            if (war?.data == null || war.hasEnded() || !war.isDefender(victim) || war.hasKingdom(ally)) continue;
            war.joinDefenders(ally);
            Seat seat = SeatByToken.Values.FirstOrDefault(found => found.Ruler != null && found.Realm == ally);
            if (seat != null)
                Notify(seat, string.Format(LM.Get("terminal_pact_called"), victim.data.name, war.main_attacker?.data?.name ?? ""));
        }
    }

    private static void RunOffers(Seat seat)
    {
        for (int i = seat.Offers.Count - 1; i >= 0; i--)
            if (Time.realtimeSinceStartup - seat.Offers[i].Since >= HoldSeconds) Answer(seat, seat.Offers[i], false);
    }
}
