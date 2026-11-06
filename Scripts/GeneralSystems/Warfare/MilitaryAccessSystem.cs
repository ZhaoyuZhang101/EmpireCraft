using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 借道(军事通行；用户 2026-11-06 选"加短期长期条约"，起因是军队被第三国隔在飞地)：
//   · 主国许借道国借道，单向，一方一条：短期 ShortYears 年、长期 LongYears 年，到期自动失效；再签就是续约，从签的时候起算满。
//     帝国的约管到整个帝国：主国、借道国换成各自帝国的核心国也算(见 Grants)；
//   · 没约照旧：军团能绕就绕、绕不动才穿过去(见 CorpsSystem.FindPath)，陆路战线不穿第三国(见 FrontSystem.Passable)；
//   · 有约：主国的地对借道国算能走——陆路战线可以经过(FrontSystem.Passable、LandRoute)，行军不再加重绕路(CorpsSystem.FindPath)，
//     分驻连片也算连着(FrontSystem.StationPieces)，运粮经过按本方的地折损(LogisticsSystem.Loss)；
//     只管这两国：主国不因此卷入借道国的战争，借道国的盟友也借不了道；
//   · 作废：两国交战、一方灭亡、主国对借道国的好感跌到 VoidOpinion 以下(神力定的约不看好感)；
//   · AI(每年)：这一年军团行军穿过的第三国(见 NoteCrossing)、交战时近处的敌城被第三国隔着走不到的(见 NoteBlocked)，向那国借道。
//     主国先看是不是借道去打自己人(借道国正跟主国的盟友、姻亲、同一帝国的打仗就不许)，
//     再看好感加实力(借道国国力越强越不敢拒)：够 ShortScore 许短期，够 LongScore 且借道国对主国好感也够许长期；
//     快到期还要用的续约，用不着的到期作罢。
public static class MilitaryAccessSystem
{
    public const int ShortYears = 10, LongYears = 30;
    private const double YearSeconds = 60d;
    private const float VoidOpinion = -30f, ShortScore = 0f, LongScore = 40f, LongGuestOpinion = 20f, RenewYears = 2f;
    // 实力：借道国国力是主国的几倍，每多一倍好感算多 PowerWeight，最多 PowerCap；比主国弱的最多扣 PowerFloor
    private const float PowerWeight = 10f, PowerCap = 25f, PowerFloor = -10f;
    // 一国一年最多向几国借道；记的需求、走不到的敌城最多多少条
    private const int AsksPerYear = 3, MaxDemand = 4096, MaxBlocked = 4096;

    private enum Term { None, Short, Long }

    private static MilitaryAccessData _data = new();
    private static readonly Dictionary<(long host, long guest), MilitaryAccessGrant> Index = new();
    // 这一年里军团行军穿过的第三国：(借道国所在帝国的核心国, 主国所在帝国的核心国) → 趟数
    private static readonly Dictionary<(long guest, long host), int> Demand = new();
    // 这一年里交战时近处走不到的敌城：(本方帝国的核心国, 城)
    private static readonly HashSet<(long side, long city)> Blocked = new();
    private static readonly HashSet<long> Crossed = new();
    private static int _months;

    // 约一变就加一：陆路缓存(见 FrontSystem.LandRoute)按它重找
    public static int Version { get; private set; }

    public static void ResetWorldState()
    {
        _data = new MilitaryAccessData();
        Index.Clear();
        Demand.Clear();
        Blocked.Clear();
        _months = 0;
        Version++;
    }

    public static void Load(MilitaryAccessData saved)
    {
        _data = saved ?? new MilitaryAccessData();
        _data.grants ??= new List<MilitaryAccessGrant>();
        _data.grants.RemoveAll(grant => grant == null || grant.host == grant.guest);
        Index.Clear();
        foreach (MilitaryAccessGrant grant in _data.grants) Index[(grant.host, grant.guest)] = grant;
        Demand.Clear();
        Blocked.Clear();
        _months = 0;
        Version++;
    }

    public static MilitaryAccessData Save() => _data;

    public static MilitaryAccessGrant Find(Kingdom host, Kingdom guest) =>
        host != null && guest != null && Index.TryGetValue((host.id, guest.id), out MilitaryAccessGrant grant) ? grant : null;

    // host 许 guest 借道(帝国的约管到整个帝国)；到期的、两国交战中的不算
    public static bool Grants(Kingdom host, Kingdom guest)
    {
        if (Index.Count == 0 || host?.data == null || guest?.data == null || host == guest) return false;
        Kingdom hostRealm = FrontSystem.Realm(host), guestRealm = FrontSystem.Realm(guest);
        return Live(host, guest) || Live(hostRealm, guest) || Live(host, guestRealm) || Live(hostRealm, guestRealm);
    }

    private static bool Live(Kingdom host, Kingdom guest) =>
        Find(host, guest) is { } grant && Now() < grant.until && !host.isInWarWith(guest);

    // 签约或续约(续约不改起始时间)
    public static void Sign(Kingdom host, Kingdom guest, bool longTerm, bool byGod)
    {
        double now = Now();
        MilitaryAccessGrant grant = Find(host, guest);
        bool renew = grant != null;
        if (!renew)
        {
            grant = new MilitaryAccessGrant { host = host.id, guest = guest.id, since = now };
            _data.grants.Add(grant);
            Index[(host.id, guest.id)] = grant;
        }
        int years = longTerm ? LongYears : ShortYears;
        grant.until = now + years * YearSeconds;
        grant.long_term = longTerm;
        grant.by_god = byGod;
        Version++;
        Log(renew ? "military_access_renewed" : "military_access_signed", host, guest, !renew, years);
    }

    // 作废 host 许 guest 的借道；没有返回假
    public static bool Revoke(Kingdom host, Kingdom guest, string key, bool history = true)
    {
        MilitaryAccessGrant grant = Find(host, guest);
        if (grant == null) return false;
        Remove(grant);
        Log(key, host, guest, history);
        return true;
    }

    private static void Remove(MilitaryAccessGrant grant)
    {
        _data.grants.Remove(grant);
        Index.Remove((grant.host, grant.guest));
        Version++;
    }

    // ───────── AI 的需求 ─────────
    // 军团这一趟行军要穿过的第三国(不是本方也不是敌方的地，见 CorpsSystem 行军)：记下，年底向它借道。
    // 已经许了借道的也记：快到期时看还用不用得着(见 Ask，还早的不重签)
    public static void NoteCrossing(Kingdom side, List<int> path)
    {
        if (side?.data == null || path == null || path.Count < 3 || Demand.Count >= MaxDemand) return;
        Kingdom guest = FrontSystem.Realm(side);
        Crossed.Clear();
        for (int i = 1; i < path.Count - 1; i++)
        {
            Kingdom owner = FrontSystem.Controller(CorpsSystem.ZoneById(path[i]));
            if (owner?.data == null || owner.wild || !Crossed.Add(owner.id)) continue;
            if (FrontSystem.Friendly(side, owner) || FrontSystem.Hostile(side, owner)) continue;
            AddDemand(guest, FrontSystem.Realm(owner));
        }
    }

    // 交战时近处的敌城走陆路到不了(见 FrontSystem.LandRoute)：年底看是不是被第三国隔着
    public static void NoteBlocked(Kingdom side, City target)
    {
        if (side?.data == null || target?.data == null || Blocked.Count >= MaxBlocked) return;
        Blocked.Add((FrontSystem.Realm(side).id, target.data.id));
    }

    private static void AddDemand(Kingdom guest, Kingdom host)
    {
        if (guest == null || host == null || guest == host) return;
        var key = (guest.id, host.id);
        Demand[key] = (Demand.TryGetValue(key, out int count) ? count : 0) + 1;
    }

    // 走不到的敌城：与那座城接壤、又与本方接壤的第三国，就是隔在中间的
    private static void DemandFromBlocked()
    {
        var near = new Dictionary<long, HashSet<Kingdom>>();
        foreach ((long sideId, long cityId) in Blocked)
        {
            Kingdom side = Alive(sideId);
            City city = World.world.cities.get(cityId);
            if (side == null || city?.data == null || city.isRekt() || city.neighbours_kingdoms == null ||
                !FrontSystem.Hostile(side, city.kingdom)) continue;
            if (!near.TryGetValue(sideId, out HashSet<Kingdom> neighbours))
                near[sideId] = neighbours = DiplomacyLedger.NeighboursOf(side);
            foreach (Kingdom between in city.neighbours_kingdoms)
            {
                if (between?.data == null || between.isRekt() || between.wild) continue;
                Kingdom host = FrontSystem.Realm(between);
                if (!neighbours.Contains(between) && !neighbours.Contains(host)) continue;
                if (FrontSystem.Friendly(side, between) || FrontSystem.Hostile(side, between) || Grants(between, side)) continue;
                AddDemand(side, host);
            }
        }
    }

    // ───────── 每年 ─────────
    // 每月(军团月结开头，见 CorpsSystem.BeginMonthCycle)调一次，满十二个月结一年
    public static void Monthly()
    {
        if (++_months % 12 != 0) return;
        Yearly();
    }

    private static void Yearly()
    {
        Expire();
        try
        {
            DemandFromBlocked();
            Ask();
        }
        finally
        {
            Demand.Clear();
            Blocked.Clear();
        }
    }

    // 一方灭亡的删掉；交战、期满、交恶的作废
    private static void Expire()
    {
        double now = Now();
        foreach (MilitaryAccessGrant grant in _data.grants.ToList())
        {
            Kingdom host = Alive(grant.host), guest = Alive(grant.guest);
            if (host == null || guest == null)
            {
                Remove(grant);
                continue;
            }
            string key = null;
            bool history = true;
            if (host.isInWarWith(guest) || FrontSystem.Hostile(host, guest)) key = "military_access_void_war";
            else if (now >= grant.until)
            {
                key = "military_access_expired";
                history = false;
            }
            else if (!grant.by_god && Opinion(host, guest) < VoidOpinion) key = "military_access_revoked";
            if (key == null) continue;
            Remove(grant);
            Log(key, host, guest, history);
        }
    }

    // 各国按需求向第三国借道(一国一年至多 AsksPerYear 国，穿得多的先借)；已有的约还剩 RenewYears 年以上的不再借
    private static void Ask()
    {
        double now = Now();
        foreach (IGrouping<long, KeyValuePair<(long guest, long host), int>> wants in Demand.GroupBy(pair => pair.Key.guest).ToList())
        {
            Kingdom guest = Alive(wants.Key);
            if (guest == null) continue;
            int asked = 0;
            foreach (KeyValuePair<(long guest, long host), int> want in wants.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.host))
            {
                if (asked >= AsksPerYear) break;
                Kingdom host = Alive(want.Key.host);
                if (host == null || host == guest || FrontSystem.Friendly(host, guest) || FrontSystem.Hostile(host, guest)) continue;
                MilitaryAccessGrant grant = Find(host, guest);
                if (grant != null && grant.until - now > RenewYears * YearSeconds) continue;
                asked++;
                Term term = Consider(host, guest);
                if (term != Term.None) Sign(host, guest, term == Term.Long, false);
            }
        }
    }

    // 主国答不答应：借道去打自己人的不许；好感加实力够 ShortScore 许短期，够 LongScore 且借道国对主国好感也够许长期
    private static Term Consider(Kingdom host, Kingdom guest)
    {
        if (ShieldsEnemy(host, guest)) return Term.None;
        float score = Opinion(host, guest) + PowerBonus(host, guest);
        if (score >= LongScore && Opinion(guest, host) >= LongGuestOpinion) return Term.Long;
        return score >= ShortScore ? Term.Short : Term.None;
    }

    // 借道国正跟主国的盟友、姻亲、同一帝国(或并肩作战)的国家打仗
    private static bool ShieldsEnemy(Kingdom host, Kingdom guest)
    {
        foreach (Kingdom other in Kingdoms())
        {
            if (other?.data == null || other.isRekt() || other == host || other == guest || !FrontSystem.Hostile(guest, other)) continue;
            if (FrontSystem.Friendly(host, other) || Bound(host, other)) return true;
        }
        return false;
    }

    private static float PowerBonus(Kingdom host, Kingdom guest)
    {
        double mine = Power(FrontSystem.Realm(host)), theirs = Power(FrontSystem.Realm(guest));
        if (mine <= 0d) return PowerCap;
        return Mathf.Clamp(((float)(theirs / mine) - 1f) * PowerWeight, PowerFloor, PowerCap);
    }

    // ───────── 取数(回归测试换成替身) ─────────
    private static double Now() => World.world.getCurWorldTime();

    private static float Opinion(Kingdom from, Kingdom to) => DiplomacyLedger.Opinion(from, to);

    private static double Power(Kingdom kingdom) => kingdom.GetNationalPower();

    private static bool Bound(Kingdom host, Kingdom other) => DiplomacyLedger.Allied(host, other) || RoyalMarriageSystem.Allied(host, other);

    private static IEnumerable<Kingdom> Kingdoms() => World.world.kingdoms.list;

    private static Kingdom Alive(long id)
    {
        Kingdom kingdom = World.world.kingdoms.get(id);
        return kingdom?.data != null && !kingdom.isRekt() ? kingdom : null;
    }

    private static void Log(string key, Kingdom host, Kingdom guest, bool history, int years = 0)
    {
        string text = string.Format(LM.Get(key), host.GetKingdomName(), guest.GetKingdomName(), years);
        if (history) TranslateHelper.LogEventMessage(text, host);
        else TranslateHelper.LogEventOnly(text, host);
    }
}
