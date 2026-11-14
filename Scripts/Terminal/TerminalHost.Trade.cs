using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 天平上除了库银和城，还能摆的三样(用户 2026-11-12 "城市王国 法理 军队 都可以交换")。都只列眼下真兑现得了的，
// 谈成了照游戏里现成的办法落地：
//   · 属国(vassal)：帝国之主的成员国(行政区、封国)与朝贡国、封建宗主的附庸，转隶给对方。对方是帝国之主就并进它的帝国
//     (同改设行政区的并法，流官的行政区政体跟新朝廷)；对方是用封建附庸的独立国、又够得着，就改认它作宗主；别的情形不列。
//     成员国照全境折价，朝贡国与附庸照半价(议和台收附庸的价)。有真人领着的国不当筹码；
//   · 法理(title)：握着的王国法理让给对方(主法理、所在行政区的法理不让)。折价 = 法理内各城之和的一成半；
//   · 军(corps)：一支不在阵上的军团交给对方，移驻对方都城、归它调遣。折价 = 兵力每百人四户。
// 折价的几个数是我定的，用户没逐条确认
public static partial class TerminalHost
{
    private const int MaxTermVassals = 8, MaxTermTitles = 8, MaxTermCorps = 6, MaxTermWars = 4;
    // 参战：一方答应加入另一方眼下的一场仗(用户 2026-11-14 "外交互换可以拉别人进入战场")，折价 = 敌方主国身价的这一成
    private const double WarShare = 0.25d;
    // 归附(用户 2026-11-14 "个人领袖如何选择臣服加入帝国")：不是游戏里的外交动作，终端自己办
    private const string Submit = "submit";
    private const double TitleShare = 0.15d;
    private const float CorpsHouseholds = 4f;

    private static bool Heads(Kingdom kingdom) => kingdom?.GetEmpire() is { } court && court.CoreKingdom == kingdom;

    // 这个属国转给 taker 办得成吗(办法：empire 并入帝国、feudal 认作封建宗主；办不成给 null)
    private static string VassalRoute(Kingdom subject, Kingdom taker)
    {
        if (subject?.data == null || subject.isRekt() || taker?.data == null || subject == taker || subject.isInWarWith(taker)) return null;
        if (HostOf(subject, null) != null) return null;
        if (Heads(taker)) return "empire";
        if (taker.GetEmpire() == null && FeudalVassalService.UsesFeudalVassals(taker) && !FeudalVassalService.TooFar(taker, subject)) return "feudal";
        return null;
    }

    // payer 那一边能摆的属国、法理、军
    private static void AddTradeTerms(List<Term> terms, Kingdom payer, Kingdom taker, bool give, string side)
    {
        try
        {
            // 属国
            var subjects = new List<(Kingdom kingdom, bool member)>();
            if (Heads(payer))
            {
                Empire court = payer.GetEmpire();
                foreach (Kingdom member in court.kingdoms_list ?? new List<Kingdom>())
                    if (member != payer) subjects.Add((member, true));
                foreach (Kingdom tributary in court.taken_Kingdoms ?? new List<Kingdom>()) subjects.Add((tributary, false));
            }
            foreach (Kingdom vassal in FeudalVassalService.GetDirectVassals(payer) ?? Enumerable.Empty<Kingdom>())
                if (subjects.All(found => found.kingdom != vassal)) subjects.Add((vassal, false));
            foreach ((Kingdom kingdom, bool member) in subjects.Where(found => VassalRoute(found.kingdom, taker) != null)
                         .OrderBy(found => PeaceTermRules.RealmValue(found.kingdom)).Take(MaxTermVassals))
                terms.Add(new Term
                {
                    Id = side + ":v:" + kingdom.getID(), Give = give, Kind = "vassal", RefId = kingdom.getID(),
                    Value = Math.Max(1, member ? PeaceTermRules.RealmValue(kingdom) : PeaceTermRules.Share(kingdom, PeaceTermRules.VassalShare)),
                    Label = string.Format(LM.Get("terminal_term_vassal"), NameOf(kingdom))
                });
            // 法理
            if (taker.hasKing())
            {
                long main = payer.GetOrCreate().MainTitle;
                KingdomTitle office = payer.GetAdministrativeTitle();
                foreach (KingdomTitle title in (payer.GetControlledTitles() ?? new List<KingdomTitle>())
                         .Where(title => title?.data != null && title.data.id != main && title != office).Take(MaxTermTitles))
                    terms.Add(new Term
                    {
                        Id = side + ":t:" + title.data.id, Give = give, Kind = "title", RefId = title.data.id,
                        Value = Math.Max(1, (int)Math.Round((title.city_list ?? new List<City>())
                            .Where(city => city?.data != null && !city.isRekt()).Sum(PeaceTermRules.CityValue) * TitleShare)),
                        Label = string.Format(LM.Get("terminal_term_title"), title.data.name)
                    });
            }
            // 参战：taker 眼下在打的仗，payer 帮着打(payer 已经卷进去的、对面是自己或盟友、同在一个帝国里的不列)
            foreach (War war in taker.getWars().Where(found => found?.data != null && !found.hasEnded()).Take(MaxTermWars))
            {
                Kingdom enemy = war.isAttacker(taker) ? war.main_defender : war.main_attacker;
                if (!CanJoinWar(war, payer, taker, enemy)) continue;
                terms.Add(new Term
                {
                    Id = side + ":w:" + war.getID(), Give = give, Kind = "war", RefId = war.getID(),
                    Value = Math.Max(1, (int)Math.Round(PeaceTermRules.RealmValue(enemy) * WarShare)),
                    Label = string.Format(LM.Get("terminal_term_war"), NameOf(enemy))
                });
            }
            // 军
            if (taker.capital?.data != null && !taker.capital.isRekt())
                foreach (CorpsData corps in (payer.GetOrCreate().corps ?? new List<CorpsData>()).Where(Spare).OrderByDescending(corps => corps.strength).Take(MaxTermCorps))
                    terms.Add(new Term
                    {
                        Id = side + ":a:" + corps.id, Give = give, Kind = "corps", RefId = corps.id,
                        Value = Math.Max(1, Mathf.RoundToInt(corps.strength / CityPopulationSystem.FiscalPeoplePerHousehold * CorpsHouseholds)),
                        Label = string.Format(LM.Get("terminal_term_corps"), TerminalIntel.Strip(CorpsSystem.Name(corps)), Mathf.RoundToInt(corps.strength))
                    });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 互换的条件没列全: {exception.Message}");
        }
    }

    // joiner 能不能帮 side 打这场仗
    private static bool CanJoinWar(War war, Kingdom joiner, Kingdom side, Kingdom enemy) =>
        war?.data != null && !war.hasEnded() && enemy?.data != null && !enemy.isRekt() && joiner != enemy && !war.hasKingdom(joiner) &&
        (war.isAttacker(side) || war.isDefender(side)) && !joiner.isInWarWith(side) && !Allied(joiner, enemy) &&
        (joiner.GetEmpire() == null || joiner.GetEmpire() != enemy.GetEmpire());

    // 归附：对方在帝国里(中央或成员)就并入那个帝国，否则认对方作封建宗主。办不成给缘由
    private static string SubmitBlock(Kingdom realm, Kingdom other, out Kingdom lord)
    {
        lord = other?.GetEmpire()?.CoreKingdom ?? other;
        if (realm?.data == null || lord?.data == null || lord == realm) return LM.Get("terminal_fail_gone");
        if (realm.IsEmpire()) return Words("terminal_submit_core");
        Empire court = Heads(lord) ? lord.GetEmpire() : null;
        if (court != null && realm.GetEmpire() == court) return Words("terminal_submit_already");
        if (realm.GetEmpire() != null) return Words("terminal_submit_other");
        if (realm.isInWarWith(lord) || realm.isInWarWith(other)) return Words("terminal_submit_war");
        if (court == null && !(lord.GetEmpire() == null && FeudalVassalService.UsesFeudalVassals(lord) && !FeudalVassalService.TooFar(lord, realm)))
            return Words("terminal_submit_cannot");
        return null;
    }

    private static string DoSubmit(Kingdom realm, Kingdom other)
    {
        string why = SubmitBlock(realm, other, out Kingdom lord);
        if (why != null) return why;
        if (FeudalVassalService.GetOverlord(realm) != null) FeudalVassalService.Break(realm, false);
        if (Heads(lord))
        {
            Empire court = lord.GetEmpire();
            MembershipCooldown.ByGod(() => court.join(realm, pForce: true));
            if (realm.GetEmpire() != court) return LM.Get("terminal_fail_generic");
            MembershipCooldown.MarkJoin(realm, court, null);
            realm.GetOrCreate().last_kingdom_status_ts = -1d;
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(realm);
            return null;
        }
        if (!FeudalVassalService.ForceBind(lord, realm)) return LM.Get("terminal_fail_generic");
        MembershipCooldown.MarkJoin(realm, null, lord);
        return null;
    }

    // 交得出去的军：不是乡勇、没编在哪支大军里、不在船上、不在阵上
    private static bool Spare(CorpsData corps) =>
        corps != null && corps.strength >= 1f && !corps.militia && corps.order_id < 0 && !corps.embarked && corps.parent_id < 0 &&
        corps.task != "attack" && corps.task != "defend";

    // 兑现一条。兑现不了的(谈的这几个月里情形变了)记一笔日志，不拦别的条件
    private static void SettleTrade(Term term, Kingdom payer, Kingdom taker)
    {
        try
        {
            if (term.Kind == "vassal")
            {
                Kingdom subject = World.world.kingdoms.get(term.RefId);
                string route = VassalRoute(subject, taker);
                if (route == null) return;
                if (route == "feudal")
                {
                    // 成员国先出原来的帝国(不算叛离)，再认新宗主
                    Empire old = subject.GetEmpire();
                    if (old != null && old.CoreKingdom != subject) old.leave(subject);
                    FeudalVassalService.ForceBind(taker, subject);
                    return;
                }
                Empire court = taker.GetEmpire();
                // 原先是不是流官的行政区：是的话政体跟新朝廷走
                bool province = subject.GetEmpire() != null && subject.GetRegime()?.GetLeaderSelectMethod() != LeaderSelectMethod.Succession;
                if (FeudalVassalService.GetOverlord(subject) != null) FeudalVassalService.Break(subject, false);
                if (subject.GetEmpire() != court)
                    MembershipCooldown.ByGod(() => court.join(subject, pForce: true, pLegitimacyTransfer: true));
                if (subject.GetEmpire() != court) return;
                if (province) AsProvince(subject, taker);
                else
                {
                    subject.GetOrCreate().last_kingdom_status_ts = -1d;
                    EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(subject);
                }
            }
            else if (term.Kind == "title")
            {
                if (term.RefId == payer.GetOrCreate().MainTitle) return;
                if (payer.GetControlledTitles()?.Any(title => title?.data != null && title.data.id == term.RefId) != true) return;
                taker.InheritRealmTitles(payer, new[] { term.RefId });
            }
            else if (term.Kind == "war")
            {
                War war = World.world.wars.get(term.RefId);
                if (war?.data == null || war.hasEnded()) return;
                Kingdom enemy = war.isAttacker(taker) ? war.main_defender : war.main_attacker;
                if (!CanJoinWar(war, payer, taker, enemy)) return;
                if (war.isAttacker(taker)) war.joinAttackers(payer);
                else war.joinDefenders(payer);
            }
            else if (term.Kind == "corps")
            {
                CorpsData corps = payer.GetOrCreate().corps?.FirstOrDefault(found => found.id == term.RefId);
                City home = taker.capital;
                if (!Spare(corps) || home?.data == null || home.isRekt()) return;
                Actor general = corps.general_id < 0 ? null : World.world.units.get(corps.general_id);
                if (general?.data != null && general.kingdom != taker) CorpsSystem.LoseGeneral(general);
                CorpsDirectOrders.Clear(corps);
                CorpsSystem.Transfer(corps, payer, taker, home);
                // 不占对方的兵额(免得一到就被裁)；移驻对方都城
                corps.manual = true;
                int zone = home.getTile()?.zone?.id ?? -1;
                if (zone >= 0) corps.zone_id = corps.target_zone_id = zone;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 互换的条件没兑现({term.Kind}): {exception.Message}");
        }
    }
}
