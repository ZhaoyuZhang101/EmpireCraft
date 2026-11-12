using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 终端第二期续：御驾亲征、微服私访、叛乱的反应延迟。
//   · 亲征、私访(用户 2026-11-11 "可以操作皇帝御驾亲征还是微服私访""只不过会生病会有危险""机制直接放在客户端")：
//     整个做在终端这一头，游戏里别的君主不受影响。君主一出京，待批的奏议和案子都不等他(朝廷自决)；每月掷一回患病、遇险。
//       亲征——本国有仗打才去得成：本国各军战力加一成半(FrontSystem.ResolveCity 的乘式里问 CampaignFactor)、启程时组织度各加十，
//       军国大事当月就知道；战况吃亏时更险，遇险三成是中流矢而崩。仗打完自己回銮，也可以下令班师。
//       私访——去本国一座城，路上按政令到得了几分算日子；回来带回这座城的真数(不照吏治虚报、不管政令达不达)，
//       此后一年这座城都按真数显示，所过之处积弊略减；遇险两成是遇刺。
//   · 叛乱迟报("一个人举兵造反，要一段时间远处才反应过来，因此你最先面对的可能只是就近的几个县的兵力，势力大了才会有朝廷的注意，
//     但也要等待一段时间朝廷才会来清剿")：游戏里叛乱本来就有集结期(MusterSystem：这期间朝廷不反攻、只有起兵地附近的军应战、
//     守方战力打折，一至五个月)。终端开着、有人领着君主时，新起的叛乱把集结期按起兵地离朝廷都城的远近拉长到四至二十个月
//     (有电报两个月)；叛军坐大(四城，或朝廷的四分之一)就提前，只再等两个月。集结期里朝廷那边的君主是真不知道：
//     开战的消息不报、战事里不列、地图上那块地还算本国的；期满才来一道急报
public static partial class TerminalHost
{
    private const float CampaignBonus = 0.15f, CampaignOrg = 10f;
    private const double CampaignIll = 0.03d, CampaignPerilAhead = 0.005d, CampaignPerilBehind = 0.02d, CampaignDeath = 0.3d;
    private const double TourIll = 0.02d, TourPeril = 0.01d, TourDeath = 0.2d, TourCleanup = 0.03d;
    private const int TourTruthMonths = 12, RebelMinMonths = 4, RebelMaxMonths = 20, RebelTelegraphMonths = 2, RebelLateMonths = 2, RebelBigCities = 4;

    // 正御驾亲征的国(每秒更新；战斗结算里逐军问 CampaignFactor，不能每回都去翻席位)
    private static Kingdom[] _campaigning = Array.Empty<Kingdom>();
    // 叛乱 → 朝廷哪个月才知道(-1：已经知道了，或者终端开之前就在打、不归这里管)
    private static readonly Dictionary<long, int> RebellionNoticed = new();

    // FrontSystem.ResolveCity 的战力乘式里的一项：这一国的君主正御驾亲征
    public static float CampaignFactor(Kingdom kingdom)
    {
        Kingdom[] list = _campaigning;
        for (int i = 0; i < list.Length; i++)
            if (list[i] == kingdom) return 1f + CampaignBonus;
        return 1f;
    }

    private static bool Unnoticed(Seat seat, Kingdom other) => other != null && seat.Hidden.Contains(other.getID());

    private static IEnumerable<War> OpenWars(Seat seat) => seat.Realm.getWars().Where(war =>
        war?.data != null && !war.hasEnded() && !Unnoticed(seat, war.main_attacker == seat.Realm ? war.main_defender : war.main_attacker));

    // 眼下的仗是不是占着上风(有一场吃亏就不算)
    private static bool Ahead(Seat seat)
    {
        foreach (War war in OpenWars(seat))
        {
            int attackers = PeaceTermRules.LeverageOf(war, true).Total, defenders = PeaceTermRules.LeverageOf(war, false).Total;
            if (war.isAttacker(seat.Realm) ? attackers < defenders : defenders < attackers) return false;
        }
        return true;
    }

    // 政令清单里宫廷那一组的三项：亲征(或班师)、私访
    private static void AddJourney(Seat seat, JArray palace)
    {
        Kingdom realm = seat.Realm;
        if (seat.Away == "campaign") palace.Add(new JObject { ["id"] = "return", ["ok"] = true });
        else palace.Add(new JObject { ["id"] = "campaign", ["ok"] = seat.Away == null && OpenWars(seat).Any() });
        var cities = new JArray();
        foreach (City city in (realm.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt() && city != realm.capital)
                 .OrderByDescending(city => city.getPopulationPeople()).Take(MaxRecruitCities))
            cities.Add(new JObject { ["id"] = city.getID().ToString(), ["name"] = city.data.name, ["months"] = TourMonths(city) });
        palace.Add(new JObject { ["id"] = "tour", ["ok"] = seat.Away == null && cities.Count > 0, ["cities"] = cities });
    }

    // 给页面顶上的：君主眼下在哪
    private static JObject AwayJson(Seat seat)
    {
        if (seat.Away == null) return null;
        var away = new JObject { ["kind"] = seat.Away };
        if (seat.Away == "tour")
        {
            away["city"] = World.world.cities.get(seat.AwayCity)?.data?.name ?? "";
            away["left"] = Math.Max(0, seat.AwayUntil - MonthNow());
        }
        return away;
    }

    // 私访来回几个月：政令到得了的近，到不了的远
    private static int TourMonths(City city)
    {
        float control = AdministrativeControlSystem.Known(city) ? AdministrativeControlSystem.Get(city) : 1f;
        return 2 + Mathf.RoundToInt((1f - Mathf.Clamp01(control)) * 6f);
    }

    // 当场办的三样：是这三样就办了并返回真
    private static bool Journey(Seat seat, string id, string target)
    {
        int month = MonthNow();
        if (id == "campaign")
        {
            if (seat.Away != null || !OpenWars(seat).Any()) return false;
            seat.Away = "campaign";
            seat.AwayRolled = month;
            foreach (Data.CorpsData corps in CorpsSystem.Of(seat.Realm) ?? new List<Data.CorpsData>())
                if (corps != null) corps.org = Mathf.Min(100f, corps.org + CampaignOrg);
            Notify(seat, LM.Get("terminal_campaign_start"));
            return true;
        }
        if (id == "return")
        {
            if (seat.Away != "campaign") return false;
            seat.Away = null;
            Notify(seat, LM.Get("terminal_campaign_end"));
            return true;
        }
        if (id == "tour")
        {
            if (seat.Away != null || !long.TryParse(target, out long cityId)) return false;
            City city = World.world.cities.get(cityId);
            if (city?.data == null || city.isRekt() || city.kingdom != seat.Realm || city == seat.Realm.capital) return false;
            seat.Away = "tour";
            seat.AwayCity = cityId;
            seat.AwayRolled = month;
            seat.AwayUntil = month + TourMonths(city);
            Notify(seat, string.Format(LM.Get("terminal_tour_start"), city.data.name));
            return true;
        }
        return false;
    }

    // 每秒：在外的君主每月掷一回患病、遇险；仗打完回銮；私访到日子带真数回来
    private static void RunJourney(Seat seat)
    {
        if (seat.Away == null) return;
        int month = MonthNow();
        if (seat.Away == "campaign" && !OpenWars(seat).Any())
        {
            seat.Away = null;
            Notify(seat, LM.Get("terminal_campaign_over"));
            return;
        }
        if (month != seat.AwayRolled)
        {
            seat.AwayRolled = month;
            if (Hazard(seat, month)) return;
        }
        if (seat.Away != "tour" || month < seat.AwayUntil) return;
        seat.Away = null;
        City city = World.world.cities.get(seat.AwayCity);
        if (city?.data == null || city.isRekt() || city.kingdom != seat.Realm)
        {
            Notify(seat, LM.Get("terminal_tour_lost"));
            return;
        }
        seat.Toured[city.getID()] = month;
        city.AddCorruptionRate(-TourCleanup);
        Notify(seat, string.Format(LM.Get("terminal_tour_report"), city.data.name, city.getPopulationPeople(), city.GetTreasuryBalance(),
            city.countWarriors(), Math.Round(CorruptionRules.Bound(city.GetCorruptionRate()) * 100d)), "tour");
    }

    // 这个月有没有出事。君主崩了返回真(席位下一秒自己空出来)
    private static bool Hazard(Seat seat, int month)
    {
        Actor ruler = seat.Ruler;
        bool campaign = seat.Away == "campaign";
        double roll = TerminalIntel.Unit(TerminalIntel.Hash(ruler.getID(), month, 31));
        double peril = campaign ? Ahead(seat) ? CampaignPerilAhead : CampaignPerilBehind : TourPeril;
        if (roll < peril)
        {
            if (TerminalIntel.Unit(TerminalIntel.Hash(ruler.getID(), month, 32)) < (campaign ? CampaignDeath : TourDeath))
            {
                seat.Away = null;
                BiographySystem.Kill(ruler, LM.Get(campaign ? "death_cause_mishap_arrow" : "death_cause_mishap_assassin"));
                return true;
            }
            ruler.setHealth(Math.Max(1, ruler.getHealth() / 2));
            Notify(seat, LM.Get(campaign ? "terminal_peril_campaign" : "terminal_peril_tour"));
        }
        else if (roll < peril + (campaign ? CampaignIll : TourIll))
        {
            ruler.addStatusEffect("cough");
            ruler.setHealth(Math.Max(1, (int)(ruler.getHealth() * 0.7f)));
            Notify(seat, LM.Get("terminal_ill"));
        }
        return false;
    }

    // ───────── 叛乱的反应延迟 ─────────
    // 每秒(有席位领着君主时)：新起的叛乱定下朝廷几个月后才知道并写进它的集结期；坐大的提前；期满给朝廷那边的席位一道急报
    private static void RunRebellions()
    {
        var campaigning = new List<Kingdom>();
        foreach (Seat seat in SeatByToken.Values)
        {
            seat.Hidden.Clear();
            seat.HiddenNames.Clear();
            if (seat.Ruler != null && seat.Away == "campaign") campaigning.Add(seat.Realm);
        }
        _campaigning = campaigning.ToArray();
        if (!_anyRuler) return;
        int month = MonthNow();
        foreach (War war in World.world.wars)
        {
            if (war?.data == null || war.hasEnded() || !MusterSystem.IsRebellion(war)) continue;
            Kingdom rebel = war.main_attacker, court = FrontSystem.Realm(war.main_defender);
            if (rebel?.data == null || rebel.isRekt() || court?.data == null || court.isRekt()) continue;
            long id = war.data.id;
            int elapsed = Date.getMonthsSince(war.data.created_time);
            if (!RebellionNoticed.TryGetValue(id, out int due))
            {
                if (elapsed > 1)
                {
                    RebellionNoticed[id] = -1;
                    continue;
                }
                int months = RebelMonths(rebel, court);
                war.GetOrCreate().muster_months = months;
                RebellionNoticed[id] = due = month + months - elapsed;
                Seat leader = SeatByToken.Values.FirstOrDefault(seat => seat.Ruler != null && seat.Realm == rebel);
                if (leader != null) Notify(leader, string.Format(LM.Get("terminal_rebel_head_start"), months));
            }
            if (due < 0) continue;
            int cities = rebel.cities?.Count ?? 0;
            if (month < due - RebelLateMonths && (cities >= RebelBigCities || cities * 4 >= (court.cities?.Count ?? 0)))
            {
                RebellionNoticed[id] = due = month + RebelLateMonths;
                war.GetOrCreate().muster_months = elapsed + RebelLateMonths;
            }
            foreach (Seat seat in SeatByToken.Values)
            {
                if (seat.Ruler == null || (seat.Realm != court && seat.Realm != war.main_defender)) continue;
                if (month < due)
                {
                    seat.Hidden.Add(rebel.getID());
                    if (!string.IsNullOrEmpty(rebel.data.name)) seat.HiddenNames.Add(rebel.data.name);
                }
                else Notify(seat, string.Format(LM.Get("terminal_rebel_known"), NameOf(rebel)), "major");
            }
            if (month >= due) RebellionNoticed[id] = -1;
        }
    }

    private static int RebelMonths(Kingdom rebel, Kingdom court)
    {
        if (HasTelegraph(court)) return RebelTelegraphMonths;
        float span = Mathf.Max(MapBox.width, MapBox.height, 1);
        float distance = rebel.capital != null && court.capital != null
            ? Vector2.Distance(rebel.capital.city_center, court.capital.city_center) : span * 0.25f;
        return Mathf.Clamp(RebelMinMonths + Mathf.RoundToInt((RebelMaxMonths - RebelMinMonths) * 2f * distance / span), RebelMinMonths, RebelMaxMonths);
    }

    // 这位君主的国正挨着一场刚起、他还不该知道的叛乱(开战那一下的消息要拦下，那时还没轮到 RunRebellions 记账)
    private static bool FreshRebellionAgainst(Seat seat) => seat.Realm.getWars().Any(war =>
        war?.data != null && !war.hasEnded() && MusterSystem.IsRebellion(war) && Date.getMonthsSince(war.data.created_time) <= 1 &&
        war.main_attacker != seat.Realm && (FrontSystem.Realm(war.main_defender) == seat.Realm || war.main_defender == seat.Realm));
}
