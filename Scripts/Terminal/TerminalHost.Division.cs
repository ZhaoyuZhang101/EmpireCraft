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
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 行政区划(用户 2026-11-12 "玩家作为皇帝可以在手机端撤销行政区、建立行政区、规划行政区")。只有帝国之主办得了，三道令：
//   · 新设行政区(prov_create)：从直辖各城里划出几座，头一座作治所。照神力「设立行政区」的办法落地
//     (治所城自立一国再并回帝国)，政体跟朝廷、设流官、赋税归朝廷；
//   · 撤销行政区(prov_dissolve)：各城收归朝廷直辖(同神力「撤销行政区」)；
//   · 改隶(prov_move)：一座城在直辖与各行政区之间挪。都城、行政区的治所不能挪；行政区只剩一座城时要动就撤销。
// 新设时可选种类(用户 2026-11-12 "设立军府或者封地")：
//   · 行政区：流官，政体跟朝廷；
//   · 军府：点一位将领作节度使。只有律令制有军府(藩镇)；照剧本立藩镇的两步办法——先设成流官的节度使，核对成了再改世袭，
//     一步到位会被判成"国"。世袭的军府一直叫"某某军"，不会变成封国；
//   · 封地：点一位宗室(皇子、族人，储君除外)受封。照分封的办法落地——世袭、自办外交、自养兵、不援中央、轻税，
//     治所是法理首府的把那块法理一并授与。
//   军府、封地设出去就是世袭的，往后撤它、动它的城都不归这里管，走附庸博弈。
// 只管流官的行政区：世袭的封国、军府、羁縻朝廷不能说撤就撤，那是附庸博弈的事(TerminalHost.Vassal)；有真人领着的也不动。
// 令从都城发出，同别的都城里办的事一样下月施行(有电报当月)。不要廷议、不花钱——这两条用户没提，是我的默认
public static partial class TerminalHost
{
    private const int MaxDivisionCities = 80, MaxDivisionProvinces = 40;

    // 流官的行政区(朝廷想设就设、想撤就撤)
    private static bool PlainProvince(Seat seat, Empire court, Kingdom member) =>
        member?.data != null && !member.isRekt() && court != null && member != court.CoreKingdom && member.GetEmpire() == court &&
        !JimiSystem.IsJimiAdministration(member) && (member.IsAdministrativeKingdomType() || !Hereditary(member)) &&
        !EmpireCraftKingdomBehCheckKingdomType.IsHereditaryGarrison(member, member.GetRegime()) &&
        HostOf(member, seat) == null && !seat.Hidden.Contains(member.getID());

    private static List<City> LiveCities(Kingdom kingdom) =>
        (kingdom?.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt()).ToList();

    // 设成流官的行政区：政体跟朝廷走、设流官、不自办外交、赋税归朝廷(同改土归流的并法，见 VassalReduction)
    private static void AsProvince(Kingdom subject, Kingdom court)
    {
        RegimeType type = court.GetRegime()?.type ?? RegimeType.LvLing;
        if (subject.GetRegime()?.type != type)
        {
            subject.SetRegimeType(type);
            subject.LoadRegime();
        }
        Regime regime = subject.GetRegime();
        if (regime != null)
        {
            regime.SetAllowDiplomacy(false);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            TreasurySystem.EnsureCollectsTax(regime);
        }
        var data = subject.GetOrCreate();
        data.jimi_mode = (int)JimiMode.Direct;
        data.jimi_homeland_checked = true;
        data.last_kingdom_status_ts = -1d;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(subject);
    }

    // 授得出去的人：活着、是本国的人、不是哪一国的君主
    private static bool Grantable(Seat seat, Actor actor) =>
        actor?.data != null && actor.isAlive() && actor != seat.Ruler && !actor.isKing() && actor.kingdom == seat.Realm;

    // 设成世袭的军府(藩镇)。先设成流官的节度使，核对成了再改世袭(见 WorldMapScenario 立藩镇的写法)；不成返回 false
    private static bool AsGarrison(Kingdom subject)
    {
        if (subject.GetRegime()?.type != RegimeType.LvLing)
        {
            subject.SetRegimeType(RegimeType.LvLing);
            subject.LoadRegime();
        }
        Regime regime = subject.GetRegime();
        if (regime == null) return false;
        var data = subject.GetOrCreate();
        data.jimi_mode = (int)JimiMode.Direct;
        data.jimi_homeland_checked = true;
        data.last_kingdom_status_ts = -1d;
        regime.SetAllowDiplomacy(true);
        regime.SetAllowArmy(true);
        regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
        TreasurySystem.EnsureCollectsTax(regime);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(subject);
        if (subject.GetKingdomType() != KingdomType.LvLing_jiedushi) return false;
        regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
        data.last_kingdom_status_ts = -1d;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(subject);
        return true;
    }

    // 设成封国(照 EnfeoffmentHelper.TryEnfeoffActor 的落地)：政体跟朝廷、世袭、自办外交自养兵、不援中央、轻税；并回帝国后理法理与爵位
    private static bool AsFief(Kingdom fief, Empire court, City seatCity, Actor lord)
    {
        Regime courtRegime = court.CoreKingdom.GetRegime();
        if (courtRegime == null) return false;
        fief.SetRegimeType(courtRegime.type);
        fief.LoadRegime();
        Regime regime = fief.GetRegime();
        if (regime != null)
        {
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime.SetAllowDiplomacy(true);
            regime.SetAllowArmy(true);
            regime.SetAllowSupportCenterArmy(false);
            regime.SetTaxLevel(TaxLevel.Low);
        }
        KingdomTitle title = seatCity.GetTitle();
        if (title?.title_capital == seatCity)
        {
            fief.SetMainTitle(title);
            fief.king?.AddOwnedTitle(title);
        }
        fief.SetFiedTimestamp(World.world.getCurWorldTime());
        MembershipCooldown.ByGod(() => court.join(fief, pForce: true));
        if (fief.GetEmpire() != court || fief.king != lord) return false;
        fief.ReconcileMainTitle(title == null ? null : new[] { title });
        court.SynchronizeLandedLegalTitles(fief);
        lord.CheckSpecificClan(false);
        TranslateHelper.LogPeerageGranted(lord, court, (title?.data?.name ?? fief.data.name) + LM.Get("default_peerages_2"));
        fief.GetOrCreate().last_kingdom_status_ts = -1d;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(fief);
        return true;
    }

    // 给手机的：直辖各城、各流官行政区和它们的城
    private static void AddDivision(Seat seat, JArray groups)
    {
        Empire court = OwnEmpire(seat);
        if (court == null) return;
        Kingdom realm = seat.Realm;
        var own = new JArray();
        foreach (City city in LiveCities(realm).OrderByDescending(city => city == realm.capital).ThenByDescending(city => city.getPopulationPeople())
                     .Take(MaxDivisionCities))
            own.Add(new JObject { ["id"] = city.getID().ToString(), ["name"] = city.data.name, ["cap"] = city == realm.capital });
        var list = new JArray();
        foreach (Kingdom member in (court.kingdoms_list ?? new List<Kingdom>()).Where(found => PlainProvince(seat, court, found))
                     .OrderByDescending(found => found.cities?.Count ?? 0).Take(MaxDivisionProvinces).ToList())
        {
            var cities = new JArray();
            foreach (City city in LiveCities(member).OrderByDescending(city => city == member.capital).ThenByDescending(city => city.getPopulationPeople())
                         .Take(MaxDivisionCities))
                cities.Add(new JObject { ["id"] = city.getID().ToString(), ["name"] = city.data.name, ["seat"] = city == member.capital });
            list.Add(new JObject { ["id"] = member.getID().ToString(), ["name"] = NameOf(member), ["cities"] = cities });
        }
        // 军府的人选：本国各军的主将；封地的人选：皇子与宗室(储君除外)
        var generals = new JArray();
        var lords = new JArray();
        var seen = new HashSet<long>();
        foreach (CorpsData corps in realm.GetOrCreate().corps ?? new List<CorpsData>())
        {
            Actor general = corps.general_id < 0 ? null : World.world.units.get(corps.general_id);
            if (!Grantable(seat, general) || generals.Count >= 12 || !seen.Add(general.getID())) continue;
            generals.Add(new JObject { ["id"] = general.getID().ToString(), ["name"] = general.getName(), ["sub"] = TerminalIntel.Strip(CorpsSystem.Name(corps)) });
        }
        seen.Clear();
        Actor heir = realm.GetHeir();
        var kin = new List<Actor>(EmpireCraftKingdomBehCheckHeir.HeirCandidates(realm) ?? new List<Actor>());
        try
        {
            foreach (Actor member in realm.getKingClan()?.getUnits() ?? Enumerable.Empty<Actor>()) kin.Add(member);
        }
        catch (Exception)
        {
            // 宗族取不到：只列皇子
        }
        foreach (Actor person in kin)
        {
            if (!Grantable(seat, person) || person == heir || person.getAge() < 14 || lords.Count >= 12 || !seen.Add(person.getID())) continue;
            lords.Add(new JObject { ["id"] = person.getID().ToString(), ["name"] = person.getName(), ["sub"] = string.Format(LM.Get("terminal_an_age"), person.getAge()) });
        }
        groups.Add(new JObject
        {
            ["id"] = "division", ["own"] = own, ["list"] = list, ["garrison"] = realm.GetRegime()?.type == RegimeType.LvLing,
            ["generals"] = generals, ["lords"] = lords
        });
    }

    // 新设行政区要划出去的城：都得是直辖的、不是都城、不重样
    private static List<City> PickedCities(Kingdom realm, string ids)
    {
        var picked = new List<City>();
        foreach (string part in (ids ?? "").Split(','))
        {
            City city = long.TryParse(part, out long id) ? World.world.cities.get(id) : null;
            if (city?.data == null || city.isRekt() || city.kingdom != realm || city == realm.capital || picked.Contains(city)) return null;
            picked.Add(city);
        }
        return picked.Count > 0 && picked.Count <= MaxDivisionCities ? picked : null;
    }

    // 改隶：这座城、它眼下归谁、要去哪。挪不得的给 null
    private static (City city, Kingdom from, Kingdom to)? MovePlan(Seat seat, Empire court, long cityId, string choice)
    {
        Kingdom realm = seat.Realm;
        City city = World.world.cities.get(cityId);
        if (city?.data == null || city.isRekt() || city.kingdom == null) return null;
        Kingdom from = city.kingdom;
        if (from != realm && !PlainProvince(seat, court, from)) return null;
        Kingdom to = choice == "0" ? realm : long.TryParse(choice, out long toId) ? World.world.kingdoms.get(toId) : null;
        if (to == null || to == from || to != realm && !PlainProvince(seat, court, to)) return null;
        // 都城、治所不挪；行政区只剩这一座城的要动就撤销
        if (city == from.capital || LiveCities(from).Count <= 1) return null;
        return (city, from, to);
    }

    // 下令。受理了返回 true(made 填好)
    private static bool DivisionOrder(Seat seat, Order made, string id, string target, string choice)
    {
        Empire court = OwnEmpire(seat);
        if (court == null) return false;
        Kingdom realm = seat.Realm;
        if (id == "prov_create")
        {
            List<City> picked = PickedCities(realm, target);
            if (picked == null) return false;
            // choice：种类[:人]。种类 province 行政区、garrison 军府、fief 封地；后两样要点人
            string[] parts = string.IsNullOrEmpty(choice) ? new[] { "province" } : choice.Split(':');
            string kind = parts[0];
            if (kind != "province" && kind != "garrison" && kind != "fief") return false;
            Actor person = parts.Length > 1 && long.TryParse(parts[1], out long personId) ? World.world.units.get(personId) : null;
            if (kind != "province" && !Grantable(seat, person)) return false;
            if (kind == "garrison" && realm.GetRegime()?.type != RegimeType.LvLing) return false;
            if (kind == "fief" && person == realm.GetHeir()) return false;
            made.Arg = string.Join(",", picked.Select(city => city.getID().ToString()));
            made.Extra = kind == "province" ? kind : kind + ":" + person.getID();
            made.Text = kind == "province"
                ? string.Format(LM.Get("terminal_order_prov_create"), picked[0].data.name, picked.Count)
                : string.Format(LM.Get("terminal_order_" + kind + "_create"), picked[0].data.name, picked.Count, person.getName());
        }
        else if (id == "prov_dissolve")
        {
            Kingdom member = long.TryParse(target, out long memberId) ? World.world.kingdoms.get(memberId) : null;
            if (!PlainProvince(seat, court, member)) return false;
            made.Target = memberId;
            made.Text = string.Format(LM.Get("terminal_order_prov_dissolve"), NameOf(member));
        }
        else
        {
            if (!long.TryParse(target, out long cityId) || MovePlan(seat, court, cityId, choice) is not { } plan) return false;
            made.Target = cityId;
            made.Arg = choice;
            made.Text = string.Format(LM.Get("terminal_order_prov_move"), plan.city.data.name,
                plan.to == realm ? LM.Get("terminal_direct") : NameOf(plan.to));
        }
        made.Kind = id;
        return true;
    }

    // 到日子了。办成返回 null
    private static string DivisionExecute(Seat seat, Kingdom realm, Order order)
    {
        Empire court = OwnEmpire(seat);
        if (court == null) return LM.Get("terminal_fail_prov");
        if (order.Kind == "prov_create")
        {
            List<City> picked = PickedCities(realm, order.Arg);
            if (picked == null) return LM.Get("terminal_fail_prov");
            City seatCity = picked[0];
            string[] parts = string.IsNullOrEmpty(order.Extra) ? new[] { "province" } : order.Extra.Split(':');
            string kind = parts[0];
            Actor head;
            if (kind == "province")
            {
                head = seatCity.leader;
                if (head == null || !head.isAlive())
                    head = seatCity.units != null && seatCity.units.Count > 0 ? seatCity.units.GetRandom() : null;
                if (head == null || !head.isAlive() || head == seat.Ruler) return LM.Get("terminal_fail_prov_nobody");
            }
            else
            {
                head = parts.Length > 1 && long.TryParse(parts[1], out long personId) ? World.world.units.get(personId) : null;
                if (!Grantable(seat, head) || kind == "fief" && head == realm.GetHeir()) return LM.Get("terminal_fail_prov_person");
                if (kind == "garrison" && realm.GetRegime()?.type != RegimeType.LvLing) return LM.Get("terminal_fail_prov");
            }
            Kingdom province = seatCity.makeOwnKingdom(head);
            if (province == null) return LM.Get("terminal_fail_prov");
            for (int i = 1; i < picked.Count; i++)
                if (picked[i].kingdom == realm) picked[i].joinAnotherKingdom(province);
            bool done;
            if (kind == "fief") done = AsFief(province, court, seatCity, head);
            else
            {
                // 朝廷设的：十五年内不因离心自行离开(同神力，见 MembershipCooldown)
                MembershipCooldown.ByGod(() => court.join(province, pForce: true));
                done = province.GetEmpire() == court;
                if (done && kind == "garrison") done = AsGarrison(province);
                else if (done) AsProvince(province, realm);
            }
            if (!done)
            {
                // 没设成：把城收回，不留一个脱出去的国
                foreach (City city in LiveCities(province)) city.joinAnotherKingdom(realm);
                World.world.zone_calculator.dirtyAndClear();
                return LM.Get("terminal_fail_prov");
            }
        }
        else if (order.Kind == "prov_dissolve")
        {
            Kingdom member = World.world.kingdoms.get(order.Target);
            if (!PlainProvince(seat, court, member)) return LM.Get("terminal_fail_prov");
            foreach (City city in LiveCities(member)) city.joinAnotherKingdom(realm);
        }
        else
        {
            if (MovePlan(seat, court, order.Target, order.Arg) is not { } plan) return LM.Get("terminal_fail_prov");
            plan.city.joinAnotherKingdom(plan.to);
        }
        World.world.zone_calculator.dirtyAndClear();
        seat.NextSight = seat.NextDecrees = 0f;
        return null;
    }
}
