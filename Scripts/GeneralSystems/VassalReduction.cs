using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 附庸博弈(用户 2026-11-12 "玩家操作帝国的情况下应该可以发起附庸博弈, 直接将一个国家变成行政区,就是得付出点代价"，
// 问答定：对象 = 朝贡国与羁縻之地、帝国里的世袭封国、封建附庸、接壤的独立小国；成败走天平，可加码或强推；
// 代价 = 库银、其余藩属离心、新设行政区不稳；同日又添"对起义军可以选择招安, 也是走天平""叛乱也是可以通过天平收买")：
//   · 天平：对方的抗拒(立国之本、兵力、离心、世袭已久、风俗不同)对朝廷的砝码(正统、兵威、恩义，另可加码许以库银、授以爵衔)，
//     数字见 VassalReductionRules。朝廷的砝码不轻于抗拒就谈得成；
//   · 谈成(Convert)：仗停了、附庸与朝贡的名分解了，并入帝国，设流官、不再自办外交、赋税归朝廷，照政体重算成行政区
//     (同改土归流、设置行政区决议的做法)；政体里没有行政区这一档的，各城直接并入京畿。原来的君长留任；
//   · 强推(Rise)：对方不从，起兵——帝国成员据地自立、封建附庸打独立之战，朝贡国与独立之国由朝廷兴师(目标吞并)。
//     起义军本来就在打，强推无从说起；
//   · 招安起义军：一样的天平与办法，不是帝国的君主也能招安(那就是各城归还本国)；
//   · 代价：改设之费、其余藩属自危(Alarm，一条会淡去的好感，见 EmpireCraftOpinionAddition 的 opinion_vassal_alarm)、
//     新设行政区各城不稳(记 tusi_reformed_at，见 LateImperialSystem.TusiStabilityLoss)。招安不惊动藩属。
// 眼下只有终端下得了(TerminalHost.Vassal.cs)；AI 不用
public static class VassalReduction
{
    public sealed class Weight
    {
        public string Key;
        public int Value;
    }

    public sealed class Scale
    {
        public int Kind, Basis, Need, Have;
        public long Fee;
        public readonly List<Weight> Resist = new(), Court = new();
    }

    // 可加的码：m0 / m1 / m2 许以库银(朝廷库银的一成、两成半、五成)，title 授以爵衔
    public sealed class Extra
    {
        public string Id;
        public int Value;
        public long Money;
        public int Tier = -1;
    }

    private const string RiseReason = "rebellion_reason_vassal_reduction";

    // 朝廷这一头：帝国的核心，或者一国自己(招安)
    private static Kingdom Court(Kingdom realm) => realm?.GetEmpire()?.CoreKingdom ?? realm;

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    // 正跟朝廷打着的起义军(叛乱之战里的攻方)
    public static bool IsRebelOf(Kingdom realm, Kingdom other)
    {
        if (!Alive(realm) || !Alive(other) || realm == other) return false;
        foreach (War war in other.getWars())
        {
            if (war?.data == null || war.hasEnded() || !MusterSystem.IsRebellion(war) || war.main_attacker != other) continue;
            if (FrontSystem.Realm(war.main_defender) == realm) return true;
        }
        return false;
    }

    // 这一国对朝廷算哪一类；-1 = 不在可以谈的对象里
    public static int KindOf(Kingdom realm, Kingdom other)
    {
        if (!Alive(realm) || !Alive(other) || realm == other || other.cities == null || other.cities.Count == 0) return -1;
        if (IsRebelOf(realm, other)) return VassalReductionRules.Rebel;
        Empire empire = realm.GetEmpire();
        if (empire == null || empire.CoreKingdom != realm || realm.isInWarWith(other)) return -1;
        if (other.GetEmpire() == empire)
        {
            if (JimiSystem.IsJimiAdministration(other)) return VassalReductionRules.Jimi;
            return other.IsAdministrativeKingdomType() ? -1 : VassalReductionRules.Member;
        }
        if (other.GetEmpire() != null) return -1;
        if (FeudalVassalService.GetOverlord(other) == realm) return VassalReductionRules.Vassal;
        if (other.GetTakenAllianceEmpire() == empire) return VassalReductionRules.Tributary;
        if (FeudalVassalService.GetOverlord(other) != null || other.HasTakenAlliance()) return -1;
        List<Kingdom> direct = TributaryRelations.Direct(empire);
        if (!VassalReductionRules.Small(PeaceTermRules.RealmValue(other), direct.Sum(PeaceTermRules.RealmValue))) return -1;
        return WarReach.TouchingFrom(direct, other) ? VassalReductionRules.Neighbour : -1;
    }

    // 可以谈的对象。nearby：要不要查接壤的独立小国，查哪些(查接壤费工夫，由调用的一方给出已知的邻国)
    public static List<(Kingdom kingdom, int kind)> Candidates(Kingdom realm, IEnumerable<Kingdom> nearby)
    {
        var found = new List<(Kingdom, int)>();
        if (!Alive(realm)) return found;
        var seen = new HashSet<Kingdom>();
        void Try(Kingdom other)
        {
            if (other == null || !seen.Add(other)) return;
            int kind = KindOf(realm, other);
            if (kind >= 0) found.Add((other, kind));
        }
        foreach (War war in realm.getWars())
            if (war?.data != null && !war.hasEnded() && MusterSystem.IsRebellion(war)) Try(war.main_attacker);
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm)
        {
            foreach (Kingdom member in empire.kingdoms_list.ToList()) Try(member);
            foreach (Kingdom tributary in empire.taken_Kingdoms.ToList()) Try(tributary);
            foreach (Kingdom vassal in FeudalVassalService.GetDirectVassals(realm).ToList()) Try(vassal);
            if (nearby != null)
                foreach (Kingdom other in nearby) Try(other);
        }
        return found;
    }

    // 称一称：两边各有哪些砝码。加码不在里面(见 Extras)
    public static Scale Weigh(Kingdom realm, Kingdom other, int kind)
    {
        int basis = Math.Max(1, PeaceTermRules.RealmValue(other));
        var scale = new Scale { Kind = kind, Basis = basis, Fee = VassalReductionRules.Fee(realm.GetTreasuryBalance()) };
        double ours = DiplomacyLedger.StrengthOf(realm, true).army, theirs = DiplomacyLedger.StrengthOf(other, false).army;
        int opinion = DiplomacyLedger.Opinion(other, realm);
        string ourCulture = TechnologySystem.GetCultureOf(realm), theirCulture = TechnologySystem.GetCultureOf(other);
        void Add(List<Weight> side, string key, int value)
        {
            if (value > 0) side.Add(new Weight { Key = key, Value = value });
        }
        Add(scale.Resist, "vassal_w_standing_" + kind, VassalReductionRules.Standing(basis, kind));
        Add(scale.Resist, "vassal_w_arms", VassalReductionRules.Arms(basis, theirs, ours));
        Add(scale.Resist, "vassal_w_estranged", VassalReductionRules.Estrangement(basis, opinion));
        Add(scale.Resist, "vassal_w_lineage", VassalReductionRules.Lineage(basis, other.getAge()));
        Add(scale.Resist, "vassal_w_custom", VassalReductionRules.Custom(basis,
            !string.IsNullOrEmpty(ourCulture) && !string.IsNullOrEmpty(theirCulture) && ourCulture != theirCulture));
        Empire empire = realm.GetEmpire();
        // 不是帝国的君主招安：正统按半数算(没有天命可恃，只有一国之君的名分)
        Add(scale.Court, "vassal_w_legitimacy", VassalReductionRules.Legitimacy(basis, empire != null ? empire.Legitimacy : 50));
        Add(scale.Court, "vassal_w_might", VassalReductionRules.Might(basis, theirs, ours));
        Add(scale.Court, "vassal_w_grace", VassalReductionRules.Grace(basis, opinion));
        scale.Need = scale.Resist.Sum(weight => weight.Value);
        scale.Have = scale.Court.Sum(weight => weight.Value);
        return scale;
    }

    // 朝廷眼下摆得出的加码
    public static List<Extra> Extras(Kingdom realm, Kingdom other, int basis)
    {
        var extras = new List<Extra>();
        for (int tier = 0; tier < PeaceTermRules.IndemnityTiers.Length; tier++)
        {
            long money = PeaceTermRules.IndemnityAmount(realm, tier);
            if (money <= 0) continue;
            extras.Add(new Extra { Id = "m" + tier, Tier = tier, Money = money, Value = PeaceTermRules.MoneyValue(realm, money) });
        }
        if (FreeTitle(realm, other) != null) extras.Add(new Extra { Id = "title", Value = VassalReductionRules.Title(basis) });
        return extras;
    }

    // 手机发来的加码(编号用逗号连着)照眼下的行情重新认一遍：库银只留一档
    public static List<Extra> Pick(Kingdom realm, Kingdom other, int basis, string ids)
    {
        var picked = new List<Extra>();
        if (string.IsNullOrEmpty(ids)) return picked;
        List<Extra> options = Extras(realm, other, basis);
        foreach (string id in ids.Split(','))
        {
            Extra extra = options.FirstOrDefault(option => option.Id == id);
            if (extra == null || picked.Contains(extra) || extra.Tier >= 0 && picked.Any(other2 => other2.Tier >= 0)) continue;
            picked.Add(extra);
        }
        return picked;
    }

    private static string FreeTitle(Kingdom realm, Kingdom other)
    {
        Empire empire = realm.GetEmpire();
        Actor king = other.king;
        if (empire == null || king == null || !king.isAlive()) return null;
        return realm.GetRegime()?.virtual_honorary_peerages?.FirstOrDefault(key => !empire.IsHonoraryPeerageReserved(key));
    }

    // 谈成了：兑现加码、收改设之费、改设、其余藩属自危。办不了返回缘由的文字键
    public static string Settle(Kingdom realm, Kingdom other, int kind, List<Extra> extras)
    {
        long fee = VassalReductionRules.Fee(realm.GetTreasuryBalance()), promised = extras.Where(extra => extra.Tier >= 0).Sum(extra => extra.Money);
        if (realm.GetTreasuryBalance() < fee + promised) return "vassal_fail_money";
        string name = other.GetKingdomName();
        Empire empire = realm.GetEmpire();
        if (extras.Any(extra => extra.Id == "title") && FreeTitle(realm, other) is string title) other.king.GrantHonoraryPeerage(empire, title);
        if (fee + promised > 0L) realm.AddMoney(-(int)Math.Min(int.MaxValue, fee + promised));
        if (promised > 0L) other.AddMoney((int)Math.Min(int.MaxValue, promised));
        bool province = Convert(realm, other, kind);
        if (kind != VassalReductionRules.Rebel && empire != null) Alarm(empire, other, VassalReductionRules.AlarmPeace);
        string text = string.Format(LM.Get(kind == VassalReductionRules.Rebel ? "vassal_history_amnesty" : province ? "vassal_history_province" : "vassal_history_annex"),
            realm.GetKingdomName(), name);
        empire?.RecordHistory(directContent: text, kingdomId: realm.id);
        TranslateHelper.LogEventMessage(text, realm);
        LogService.LogInfo($"[EmpireCraft][附庸博弈] {realm.GetKingdomName()} 改设 {name}(类别 {kind}，{(province ? "行政区" : "并入直辖")})");
        return null;
    }

    // 改设：返回是不是成了行政区(否则各城并入了朝廷直辖)
    public static bool Convert(Kingdom realm, Kingdom other, int kind)
    {
        double now = World.world.getCurWorldTime();
        foreach (War war in other.getWars().ToList())
            if (war?.data != null && !war.hasEnded() && (war.hasKingdom(realm) || FrontSystem.Realm(war.main_defender) == realm ||
                                                         FrontSystem.Realm(war.main_attacker) == realm))
                World.world.wars.endWar(war, WarWinner.Peace);
        Empire empire = realm.GetEmpire();
        List<City> cities = (other.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt()).ToList();
        foreach (City city in cities) city.GetOrCreate().tusi_reformed_at = now;
        bool province = false;
        if (empire != null && empire.CoreKingdom == realm)
        {
            if (FeudalVassalService.GetOverlord(other) != null) FeudalVassalService.Break(other, false);
            if (other.GetEmpire() != empire)
                MembershipCooldown.ByGod(() => empire.join(other, pForce: true, pLegitimacyTransfer: true));
            if (other.GetEmpire() == empire)
            {
                // 政体跟着朝廷走(律令的道、现代的省都只认本政体)，设流官，不再自办外交，赋税归朝廷
                RegimeType court = realm.GetRegime()?.type ?? RegimeType.LvLing;
                if (other.GetRegime()?.type != court)
                {
                    other.SetRegimeType(court);
                    other.LoadRegime();
                }
                Regime regime = other.GetRegime();
                if (regime != null)
                {
                    regime.SetAllowDiplomacy(false);
                    regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
                    TreasurySystem.EnsureCollectsTax(regime);
                }
                var data = other.GetOrCreate();
                data.jimi_mode = (int)JimiMode.Direct;
                data.jimi_homeland_checked = true;
                data.last_kingdom_status_ts = -1d;
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(other);
                province = other.IsAdministrativeKingdomType();
            }
        }
        if (province) return true;
        // 设不成行政区(政体里没有这一档、并不进帝国、朝廷不是帝国)：各城直接归朝廷
        foreach (City city in cities)
            if (city.kingdom == other) city.joinAnotherKingdom(realm);
        return false;
    }

    // 强推：对方起兵。成了返回 null，开不了战返回缘由的文字键
    public static string Rise(Kingdom realm, Kingdom other, int kind)
    {
        Empire empire = realm.GetEmpire();
        if (empire == null || kind == VassalReductionRules.Rebel) return "vassal_fail_gone";
        bool started;
        if (kind == VassalReductionRules.Jimi || kind == VassalReductionRules.Member)
            started = NomadCycleSystem.DeclareIndependence(other, empire, RiseReason);
        else if (kind == VassalReductionRules.Vassal)
        {
            War war = World.world.diplomacy.startWar(other, realm, WarTypeLibrary.normal);
            started = war != null;
            if (started) war.SetEmpireWarType(EmpireWarType.附庸独立);
        }
        else
        {
            if (kind == VassalReductionRules.Tributary) TributaryRelations.Renounce(other, empire);
            DiplomacyActions.Action declare = DiplomacyActions.Groups.SelectMany(group => group.actions)
                .FirstOrDefault(action => action.key == "diplomacy_act_declare");
            if (declare == null) return "vassal_fail_war";
            string reason = DiplomacyActions.Do(declare, realm, other, WarGoal.吞并);
            if (reason != null) return reason;
            started = true;
        }
        if (!started) return "vassal_fail_war";
        Alarm(empire, other, VassalReductionRules.AlarmForce);
        string text = string.Format(LM.Get("vassal_history_rise"), realm.GetKingdomName(), other.GetKingdomName());
        empire.RecordHistory(directContent: text, kingdomId: realm.id);
        TranslateHelper.LogEventMessage(text, realm);
        return null;
    }

    // ───────── 其余藩属自危 ─────────
    // 帝国里不是行政区的成员、朝贡国、朝廷的封建附庸，对朝廷的好感各添一笔会淡去的不满
    public static void Alarm(Empire empire, Kingdom except, int amount)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || amount <= 0) return;
        double now = World.world.getCurWorldTime();
        var others = new HashSet<Kingdom>();
        foreach (Kingdom member in empire.kingdoms_list)
            if (member != core && Alive(member) && (!member.IsAdministrativeKingdomType() || JimiSystem.IsJimiAdministration(member))) others.Add(member);
        foreach (Kingdom tributary in empire.taken_Kingdoms)
            if (Alive(tributary)) others.Add(tributary);
        foreach (Kingdom vassal in FeudalVassalService.GetDirectVassals(core))
            if (Alive(vassal)) others.Add(vassal);
        others.Remove(except);
        foreach (Kingdom other in others)
        {
            var data = other.GetOrCreate();
            int left = data.vassal_alarm_empire == empire.getID() ? VassalReductionRules.AlarmNow(data.vassal_alarm, Years(data.vassal_alarm_at)) : 0;
            data.vassal_alarm = VassalReductionRules.AlarmAdd(left, amount);
            data.vassal_alarm_at = now;
            data.vassal_alarm_empire = empire.getID();
        }
    }

    private static float Years(double at) => at < 0d || World.world.getCurWorldTime() < at ? 0f : Date.getYearsSince(at);

    // 好感条目(负数)：main 对 target(朝廷)的自危
    public static int AlarmOpinion(Kingdom main, Kingdom target)
    {
        if (main?.data == null || target?.data == null || main == target) return 0;
        var data = main.GetOrCreate();
        if (data.vassal_alarm <= 0 || data.vassal_alarm_empire < 0) return 0;
        Empire empire = target.GetEmpire();
        if (empire == null || empire.CoreKingdom != target || empire.getID() != data.vassal_alarm_empire) return 0;
        return -VassalReductionRules.AlarmNow(data.vassal_alarm, Years(data.vassal_alarm_at));
    }
}
