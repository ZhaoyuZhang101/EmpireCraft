using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 附庸博弈与招安(规则见 VassalReduction；用户 2026-11-12 "玩家操作帝国的情况下应该可以发起附庸博弈, 直接将一个国家变成行政区"
// "对起义军可以选择招安, 也是走天平""叛乱也是可以通过天平收买")：
//   · 政令页多一组「附庸博弈」：帝国之主看得到能改设的藩属(羁縻、封国、封建附庸、朝贡国、接壤的独立小国)，
//     任何在位的君主看得到正跟本国打着的起义军(朝廷还不知道的不列)。每一家带着天平两边的砝码、能加的码、改设之费；
//   · 遣使去谈(vassal，choice = 加码的编号)：使者到了才称——对面不是真人就照天平，够了当场改设，不够回一句还差多少户；
//     对面是真人就递到他手里，应允还是回绝由他裁(同外交天平)；
//   · 强推(choice = force)：使者到了对方起兵。起义军本来就在打，没有强推
public static partial class TerminalHost
{
    private const int MaxVassals = 24;

    private static int VassalMonths(Seat seat, Kingdom other, int kind)
    {
        if (seat.Sight == TerminalIntel.Sight.World) return 0;
        return kind == VassalReductionRules.Jimi || kind == VassalReductionRules.Member ? 1 : EnvoyMonths(seat, other);
    }

    private static string ExtraLabel(VassalReduction.Extra extra) => extra.Tier >= 0
        ? string.Format(LM.Get("terminal_term_money"), Math.Round(PeaceTermRules.IndemnityTiers[extra.Tier].share * 100d))
        : LM.Get("vassal_x_title");

    private static JArray WeightsJson(List<VassalReduction.Weight> weights)
    {
        var list = new JArray();
        foreach (VassalReduction.Weight weight in weights) list.Add(new JObject { ["label"] = Words(weight.Key), ["value"] = weight.Value });
        return list;
    }

    private static void AddVassals(Seat seat, JArray groups)
    {
        Kingdom realm = seat.Realm;
        // 接壤的独立小国只在已知之国里找(查接壤费工夫)
        var nearby = new List<Kingdom>();
        foreach (long id in seat.Known)
            if (World.world.kingdoms.get(id) is Kingdom known) nearby.Add(known);
        var items = new JArray();
        foreach ((Kingdom other, int kind) in VassalReduction.Candidates(realm, nearby))
        {
            if (items.Count >= MaxVassals) break;
            if (seat.Hidden.Contains(other.getID())) continue;
            // 帝国里的事要皇帝本人：领着核心之国却不是皇帝的(权臣当国)只能招安
            if (kind != VassalReductionRules.Rebel && OwnEmpire(seat) == null) continue;
            VassalReduction.Scale scale = VassalReduction.Weigh(realm, other, kind);
            var extras = new JArray();
            foreach (VassalReduction.Extra extra in VassalReduction.Extras(realm, other, scale.Basis))
                extras.Add(new JObject { ["id"] = extra.Id, ["label"] = ExtraLabel(extra), ["value"] = extra.Value, ["money"] = extra.Tier >= 0 });
            items.Add(new JObject
            {
                ["id"] = other.getID().ToString(), ["name"] = other.data.name, ["kind"] = kind, ["cities"] = other.cities?.Count ?? 0,
                ["need"] = scale.Need, ["have"] = scale.Have, ["fee"] = scale.Fee, ["resist"] = WeightsJson(scale.Resist), ["court"] = WeightsJson(scale.Court),
                ["extras"] = extras, ["human"] = HostOf(other, seat) != null, ["months"] = VassalMonths(seat, other, kind),
                ["force"] = kind != VassalReductionRules.Rebel
            });
        }
        if (items.Count > 0) groups.Add(new JObject { ["id"] = "vassal", ["items"] = items });
    }

    // 手机发来 vassal(target = 国 id，choice = 加码的编号用逗号连着，或 force)：填好这道令。不受理返回 false
    private static bool VassalOrder(Seat seat, Order made, string target, string choice)
    {
        Kingdom realm = seat.Realm;
        Kingdom other = long.TryParse(target, out long id) ? World.world.kingdoms.get(id) : null;
        int kind = other?.data == null || other.isRekt() ? -1 : VassalReduction.KindOf(realm, other);
        bool force = choice == "force";
        if (kind < 0 || seat.Hidden.Contains(id) || force && kind == VassalReductionRules.Rebel) return false;
        if (kind != VassalReductionRules.Rebel && OwnEmpire(seat) == null) return false;
        made.Kind = "vassal";
        made.Arg = force ? "force" : "talk";
        made.Target = id;
        made.Due = MonthNow() + VassalMonths(seat, other, kind);
        string key = force ? "terminal_order_vassal_force" : kind == VassalReductionRules.Rebel ? "terminal_order_amnesty" : "terminal_order_vassal";
        made.Text = string.Format(LM.Get(key), other.data.name);
        if (force) return true;
        List<VassalReduction.Extra> extras = VassalReduction.Pick(realm, other, Math.Max(1, PeaceTermRules.RealmValue(other)), choice);
        made.Extra = string.Join(",", extras.Select(extra => extra.Id));
        if (extras.Count > 0) made.Text += "（" + string.Join("、", extras.Select(ExtraLabel)) + "）";
        return true;
    }

    // 使者到了。成了返回 null，递给对面真人了返回 Waiting，不成返回缘由
    private static string ExecuteVassal(Seat seat, Order order)
    {
        Kingdom realm = seat.Realm, other = World.world.kingdoms.get(order.Target);
        int kind = other?.data == null || other.isRekt() ? -1 : VassalReduction.KindOf(realm, other);
        if (kind < 0) return LM.Get("terminal_fail_gone");
        Seat host = HostOf(other, seat);
        if (order.Arg == "force")
        {
            string reason = VassalReduction.Rise(realm, other, kind);
            if (reason != null) return Words(reason);
            if (host != null) Notify(host, string.Format(LM.Get("terminal_vassal_forced"), realm.data.name));
            return null;
        }
        if (host != null)
        {
            string action = kind == VassalReductionRules.Rebel ? "amnesty" : "provincialize";
            host.Offers.Add(new Offer
            {
                Id = ++_seq, From = seat, FromRealm = realm.getID(), FromName = realm.data.name, Action = action, Terms = order.Extra ?? "",
                Since = Time.realtimeSinceStartup
            });
            host.NextDecrees = host.NextState = 0f;
            Notify(host, string.Format(LM.Get("terminal_offer_came"), realm.data.name, Words("diplomacy_act_" + action)));
            return Waiting;
        }
        VassalReduction.Scale scale = VassalReduction.Weigh(realm, other, kind);
        List<VassalReduction.Extra> extras = VassalReduction.Pick(realm, other, scale.Basis, order.Extra);
        int have = scale.Have + extras.Sum(extra => extra.Value);
        if (have < scale.Need) return string.Format(LM.Get("terminal_fail_scale"), other.data.name, scale.Need - have);
        string failure = VassalReduction.Settle(realm, other, kind, extras);
        return failure == null ? null : Words(failure);
    }

    private static bool VassalOffer(Offer offer) => offer.Action == "provincialize" || offer.Action == "amnesty";

    // 递到真人手里的那一条写些什么：朝廷许了什么、应允之后怎样
    private static string VassalOfferDetail(Seat seat, Offer offer, Kingdom from)
    {
        if (from?.data == null || from.isRekt()) return "";
        List<VassalReduction.Extra> extras = VassalReduction.Pick(from, seat.Realm, Math.Max(1, PeaceTermRules.RealmValue(seat.Realm)), offer.Terms);
        string promised = extras.Count > 0 ? string.Join("、", extras.Select(ExtraLabel)) : LM.Get("terminal_terms_none");
        return string.Format(LM.Get("terminal_vassal_offer_detail"), promised);
    }

    // 真人裁了：应允就照办，回绝(或搁太久)回对方一句不许
    private static void AnswerVassal(Seat seat, Offer offer, bool accept)
    {
        seat.Offers.Remove(offer);
        seat.NextDecrees = seat.NextState = 0f;
        Kingdom from = World.world.kingdoms.get(offer.FromRealm);
        bool fromThere = offer.From?.Ruler != null && offer.From.Realm == from && SeatByToken.ContainsKey(offer.From.Token);
        string what = Words("diplomacy_act_" + offer.Action), mine = seat.Realm.data.name;
        int kind = from?.data == null || from.isRekt() ? -1 : VassalReduction.KindOf(from, seat.Realm);
        string failure = null;
        if (accept)
        {
            if (kind < 0) failure = LM.Get("terminal_fail_gone");
            else
            {
                string reason = VassalReduction.Settle(from, seat.Realm, kind,
                    VassalReduction.Pick(from, seat.Realm, Math.Max(1, PeaceTermRules.RealmValue(seat.Realm)), offer.Terms));
                if (reason != null) failure = Words(reason);
            }
        }
        bool done = accept && failure == null;
        if (fromThere)
            Notify(offer.From, string.Format(LM.Get(done ? "terminal_offer_accepted" : "terminal_offer_refused"), mine, what) +
                               (failure != null ? "（" + failure + "）" : !done && kind >= 0 && kind != VassalReductionRules.Rebel
                                   ? "（" + LM.Get("terminal_vassal_may_force") + "）" : ""));
        Notify(seat, string.Format(LM.Get(done ? "terminal_offer_did_accept" : "terminal_offer_did_refuse"), offer.FromName, what) +
                     (failure != null ? "（" + failure + "）" : ""));
    }
}
