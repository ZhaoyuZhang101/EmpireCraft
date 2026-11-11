using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 外交的关系账(外交界面只读这里，见 UI/Windows/DiplomacyWindow；方案 Docs/plan-diplomacy-window.md)：
//   · 两国之间的各种关系一处汇总(Ties)：交战、停战、同盟、同一帝国、封建宗属、殖民、朝贡与岁币、关税同盟、姻亲、兄弟之国、
//     通好、保护约、扶立、共事部落、统一战线、穿越政权的伪装属国——每项带生效与到期(世界时间，-1 不明)；
//     标签按"对方对我是什么"写(对方是我的附庸就写"附庸")；
//   · 态度：原版 KingdomOpinion 的总值与各项(模组加的好感项也在里面，见 EmpireCraftOpinionAddition)；一帧内缓存，总览每行要问一遍；
//   · 往来大事：没有按两国存的史书——从还留着的战争(原版只留最近二十场结束的)和两边帝国史册里抽：
//     记着对方国家编号的条目，和正文里出现对方国名的条目；
//   · 力量对比、宗主一系的树、市场里的身份。
public static class DiplomacyLedger
{
    public sealed class Tie
    {
        public string text = "", hex = "#F2EEE2";
        public double since = -1d, until = -1d;
    }

    public struct Strength
    {
        public float power, army, navy, cities, population, money;
    }

    private const string Green = "#65D66E", Red = "#FF6666", Cyan = "#7FD8EA", Gold = "#F3C34A", Orange = "#E9A85B", Soft = "#C8C2B4";
    private const double Year = 60d;

    // ───────── 名单与筛选 ─────────
    public static IEnumerable<Kingdom> Realms() =>
        World.world?.kingdoms?.list?.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.isCiv() && !kingdom.wild)
        ?? Enumerable.Empty<Kingdom>();

    // 接壤的国家。帝国看整个帝国，别的国家看自己；城的地块相接，或只隔着走得通的无人区(同宣战的邻国，见 WarReach.WalkableWildNeighbours)。
    // 挨着别国帝国的成员，那个帝国也算接壤；帝国自己的成员不算，它们在"本帝国"筛选里
    public static HashSet<Kingdom> NeighboursOf(Kingdom focus)
    {
        var near = new HashSet<Kingdom>();
        if (focus?.data == null) return near;
        Empire own = focus.GetEmpire();
        bool whole = own != null && focus.IsEmpire();
        var side = new List<Kingdom>();
        if (whole && own.kingdoms_list != null) side.AddRange(own.kingdoms_list);
        if (!side.Contains(focus)) side.Add(focus);
        foreach (Kingdom member in side)
        {
            if (member?.cities == null) continue;
            foreach (City city in member.cities)
            {
                if (city?.data == null) continue;
                if (city.neighbours_kingdoms != null)
                    foreach (Kingdom next in city.neighbours_kingdoms)
                        Touch(near, next, focus, own, whole);
                foreach (City other in WarReach.WalkableWildNeighbours(city))
                    Touch(near, other.kingdom, focus, own, whole);
            }
        }
        return near;
    }

    private static void Touch(HashSet<Kingdom> near, Kingdom next, Kingdom focus, Empire own, bool whole)
    {
        if (next?.data == null || next == focus) return;
        Empire theirs = next.GetEmpire();
        bool sameEmpire = own != null && theirs == own;
        if (sameEmpire && whole) return;
        near.Add(next);
        if (!sameEmpire && theirs?.CoreKingdom != null) near.Add(theirs.CoreKingdom);
    }

    public static bool SameTianxia(Kingdom a, Kingdom b)
    {
        EmpireCore lead = a?.capital == null ? null : TianxiaSystem.LeadOf(a.capital);
        return lead != null && b?.capital != null && TianxiaSystem.LeadOf(b.capital) == lead;
    }

    public static Kingdom TopOf(Kingdom kingdom) => kingdom == null ? null : EmpireCoreControl.Sovereign(kingdom) ?? kingdom;

    public static bool SameBloc(Kingdom a, Kingdom b) => a != null && b != null && TopOf(a) == TopOf(b);

    public static bool SameMarket(Kingdom a, Kingdom b)
    {
        Kingdom root = CommodityMarketSystem.RootOf(a);
        return root != null && CommodityMarketSystem.RootOf(b) == root;
    }

    public static bool Allied(Kingdom a, Kingdom b) =>
        a != null && b != null && a.hasAlliance() && b.hasAlliance() && Alliance.isSame(a.getAlliance(), b.getAlliance());

    public static string Standing(Kingdom kingdom)
    {
        string type = LM.Get(kingdom.GetKingdomTypeDisplayKey());
        Kingdom top = TopOf(kingdom);
        return top != null && top != kingdom ? $"{type} · {RoleOf(kingdom, top)}" : type;
    }

    // ───────── 附属 ─────────
    // 直接附属：封建附庸、殖民地、帝国成员(帝国之主)、朝贡国(帝国之主)、关税同盟成员不算
    public static List<Kingdom> Dependents(Kingdom kingdom)
    {
        var result = new List<Kingdom>();
        if (kingdom?.data == null) return result;
        result.AddRange(FeudalVassalService.GetDirectVassals(kingdom));
        result.AddRange(ColonySystem.ColoniesOf(kingdom));
        Empire empire = kingdom.GetEmpire();
        if (empire != null && empire.CoreKingdom == kingdom)
        {
            if (empire.kingdoms_list != null) result.AddRange(empire.kingdoms_list.Where(member => member != kingdom));
            if (empire.taken_Kingdoms != null) result.AddRange(empire.taken_Kingdoms);
        }
        return result.Where(other => other?.data != null && !other.isRekt() && other != kingdom).Distinct().ToList();
    }

    // child 对 parent 是什么(体系图、市场成员)
    // 种类：self 本国、vassal 附庸、colony 殖民地、member 帝国成员、tributary 朝贡国、union 关税同盟、subject 其余属下；
    // 文字是 diplomacy_role_<种类>(简图按种类上色，见 DiplomacyWindow.Map)
    public static string RoleKey(Kingdom child, Kingdom parent)
    {
        if (child == null || parent == null) return "";
        if (child == parent) return "self";
        if (FeudalVassalService.GetOverlord(child) == parent) return "vassal";
        if (ColonySystem.MetropoleOf(child) == parent) return "colony";
        Empire empire = parent.GetEmpire();
        if (empire != null && empire.CoreKingdom == parent)
        {
            if (child.GetEmpire() == empire) return "member";
            if (child.GetTakenAllianceEmpire() == empire) return "tributary";
        }
        if (CommodityMarketSystem.UnionOf(child) == parent) return "union";
        return "subject";
    }

    public static string RoleOf(Kingdom child, Kingdom parent)
    {
        string key = RoleKey(child, parent);
        return key.Length == 0 ? "" : LM.Get("diplomacy_role_" + key);
    }

    // 直接上级(与 Dependents 相对)：封建宗主、殖民宗主国、所在帝国之主、受贡的帝国之主
    public static HashSet<Kingdom> Overlords(Kingdom kingdom)
    {
        var result = new HashSet<Kingdom>();
        if (kingdom?.data == null) return result;
        foreach (Kingdom other in new[]
                 {
                     FeudalVassalService.GetOverlord(kingdom), ColonySystem.MetropoleOf(kingdom), kingdom.GetEmpire()?.CoreKingdom,
                     kingdom.GetTakenAllianceEmpire()?.CoreKingdom
                 })
            if (other?.data != null && !other.isRekt() && other != kingdom) result.Add(other);
        return result;
    }

    public static string MarketRole(Kingdom member, Kingdom leader) =>
        member == leader ? LM.Get("diplomacy_role_leader") : RoleOf(member, leader);

    // 宗主一系的树：根在前，附属按 RoleOf 写身份(key 是 RoleKey 的种类，根为 self)，至多 MaxDepth 层
    private const int MaxDepth = 4;

    public static IEnumerable<(Kingdom kingdom, int depth, string role, string key)> Tree(Kingdom root)
    {
        if (root?.data == null) yield break;
        var seen = new HashSet<Kingdom>();
        var stack = new Stack<(Kingdom kingdom, int depth, Kingdom parent)>();
        stack.Push((root, 0, null));
        while (stack.Count > 0)
        {
            (Kingdom kingdom, int depth, Kingdom parent) = stack.Pop();
            if (!seen.Add(kingdom)) continue;
            yield return (kingdom, depth, parent == null ? Standing(kingdom) : RoleOf(kingdom, parent),
                parent == null ? "self" : RoleKey(kingdom, parent));
            if (depth >= MaxDepth) continue;
            List<Kingdom> children = Dependents(kingdom).OrderBy(child => child.cities.Count).ToList();
            foreach (Kingdom child in children) stack.Push((child, depth + 1, kingdom));
        }
    }

    // ───────── 关系 ─────────
    public static List<Tie> Ties(Kingdom a, Kingdom b)
    {
        var ties = new List<Tie>();
        if (a?.data == null || b?.data == null || a == b) return ties;
        double now = World.world.getCurWorldTime();
        void Add(string key, string hex, double since = -1d, double years = -1d, params object[] args)
        {
            ties.Add(new Tie
            {
                text = args.Length == 0 ? LM.Get(key) : string.Format(LM.Get(key), args), hex = hex, since = since,
                until = since >= 0d && years > 0d ? since + years * Year : -1d
            });
        }

        if (a.isInWarWith(b)) Add("diplomacy_tie_war", Red);
        else
        {
            double ended = World.world.diplomacy?.getRelation(a, b)?.data?.timestamp_last_war_ended ?? -1d;
            if (ended > 0d && Date.getYearsSince(ended) < SimGlobals.m.minimum_years_between_wars)
                Add("diplomacy_tie_truce", Orange, ended, SimGlobals.m.minimum_years_between_wars);
        }
        if (Allied(a, b))
            Add("diplomacy_tie_alliance", Green, Mathf.Max((float)a.data.timestamp_alliance, (float)b.data.timestamp_alliance));

        Empire ea = a.GetEmpire(), eb = b.GetEmpire();
        if (ea != null && ea == eb)
            Add(ea.CoreKingdom == b ? "diplomacy_tie_empire_head" : ea.CoreKingdom == a ? "diplomacy_tie_empire_member" : "diplomacy_tie_same_empire",
                Cyan);
        if (FeudalVassalService.GetOverlord(b) == a) Add("diplomacy_tie_vassal", Cyan, b.GetOrCreate().feudal_vassal_since);
        if (FeudalVassalService.GetOverlord(a) == b) Add("diplomacy_tie_overlord", Cyan, a.GetOrCreate().feudal_vassal_since);
        if (ColonySystem.MetropoleOf(b) == a) Add("diplomacy_tie_colony", Cyan, ColonySystem.Of(b)?.founded_at ?? -1d);
        if (ColonySystem.MetropoleOf(a) == b) Add("diplomacy_tie_metropole", Cyan, ColonySystem.Of(a)?.founded_at ?? -1d);
        if (ColonySystem.MetropoleOf(a) is Kingdom metropole && metropole == ColonySystem.MetropoleOf(b))
            Add("diplomacy_tie_sister_colony", Cyan);
        if (ea != null && ea.CoreKingdom == a && b.GetTakenAllianceEmpire() == ea)
            Add("diplomacy_tie_tributary", Gold, b.GetOrCreate().last_taken_alliance_timestamp);
        if (eb != null && eb.CoreKingdom == b && a.GetTakenAllianceEmpire() == eb)
            Add("diplomacy_tie_suzerain", Gold, a.GetOrCreate().last_taken_alliance_timestamp);
        if (ea != null && ea.CoreKingdom == a && b.GetGivenAllianceEmpire() == ea)
            Add("diplomacy_tie_subsidy_paid", Gold, b.GetOrCreate().last_given_alliance_timestamp, 20);
        if (eb != null && eb.CoreKingdom == b && a.GetGivenAllianceEmpire() == eb)
            Add("diplomacy_tie_subsidy_received", Gold, a.GetOrCreate().last_given_alliance_timestamp, 20);
        // 借道：按约上的起止(续约过的到期不是起始加年数)
        if (MilitaryAccessSystem.Find(b, a) is { } passage)
            ties.Add(new Tie { text = LM.Get("diplomacy_tie_access_in"), hex = Cyan, since = passage.since, until = passage.until });
        if (MilitaryAccessSystem.Find(a, b) is { } granted)
            ties.Add(new Tie { text = LM.Get("diplomacy_tie_access_out"), hex = Cyan, since = granted.since, until = granted.until });
        if (CommodityMarketSystem.UnionOf(b) == a) Add("diplomacy_tie_union_member", Green);
        if (CommodityMarketSystem.UnionOf(a) == b) Add("diplomacy_tie_union_leader", Green);
        if (RoyalMarriageSystem.Allied(a, b)) Add("diplomacy_tie_marriage", "#FF8FB0", MarriageSince(a, b), 20);
        if (EmpirePartitionSystem.InAmity(a, b)) Add("diplomacy_tie_amity", Green, ea?.data?.partition_time ?? -1d, 40);
        if (GreatPowerDiplomacy.Bound(a, b)) Add("diplomacy_tie_pact", Green, PactSince(ea, eb), 30);
        if (HolyRomanSystem.ProtectedBy(b, a)) Add("diplomacy_tie_protected", Cyan, b.GetOrCreate().hre_protected_at, 30);
        if (HolyRomanSystem.ProtectedBy(a, b)) Add("diplomacy_tie_protector", Cyan, a.GetOrCreate().hre_protected_at, 30);
        if (WarGoalSystem.Installed(a, b))
            Add(b.GetOrCreate().installed_by_kingdom_id == a.id ? "diplomacy_tie_installed" : "diplomacy_tie_installer", Soft,
                b.GetOrCreate().installed_by_kingdom_id == a.id ? b.GetOrCreate().installed_at : a.GetOrCreate().installed_at, 20);
        if (TribalFissionService.Bound(a, b)) Add("diplomacy_tie_tribal", Soft);
        if (NationalSentimentSystem.InSameUnitedFront(a, b)) Add("diplomacy_tie_united_front", Green);
        if (TransmigrantSystem.DisguiseLord(b) is Empire lordOfB && lordOfB.CoreKingdom == a) Add("diplomacy_tie_disguised", Orange);
        if (TransmigrantSystem.DisguiseLord(a) is Empire lordOfA && lordOfA.CoreKingdom == b) Add("diplomacy_tie_nominal_lord", Orange);
        int god = GodOpinionOf(a, b);
        if (god != 0) Add("diplomacy_tie_god", god > 0 ? Green : Red, -1d, -1d, god > 0 ? "+" + god : god.ToString());
        return ties;
    }

    private static double MarriageSince(Kingdom a, Kingdom b)
    {
        foreach ((Kingdom x, Kingdom y) in new[] { (a, b), (a.GetEmpire()?.CoreKingdom, b.GetEmpire()?.CoreKingdom) })
            if (x?.data != null && y != null && x.GetOrCreate().marriage_pacts is { } pacts && pacts.TryGetValue(y.id, out double since))
                return since;
        return -1d;
    }

    private static double PactSince(Empire a, Empire b) =>
        a?.data?.diplomatic_pacts != null && b != null && a.data.diplomatic_pacts.TryGetValue(b.id, out double since) ? since : -1d;

    private static int GodOpinionOf(Kingdom a, Kingdom b) =>
        a?.data != null && b != null && a.GetOrCreate().god_opinion is { } map && map.TryGetValue(b.id, out int value) ? value : 0;

    public static string TermText(Tie tie)
    {
        var parts = new List<string>();
        if (tie.since >= 0d) parts.Add(string.Format(LM.Get("diplomacy_tie_since"), HistoryDateFormatter.GetYear(tie.since)));
        if (tie.until >= 0d)
        {
            int left = Mathf.Max(0, Mathf.CeilToInt((float)((tie.until - World.world.getCurWorldTime()) / Year)));
            parts.Add(string.Format(LM.Get("diplomacy_tie_left"), left));
        }
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    // ───────── 态度 ─────────
    private static readonly Dictionary<(long, long), KingdomOpinion> OpinionCache = new();
    private static int _opinionFrame = -1;

    private static KingdomOpinion OpinionOf(Kingdom a, Kingdom b)
    {
        if (a?.data == null || b?.data == null || a == b || World.world?.diplomacy == null) return null;
        if (_opinionFrame != Time.frameCount)
        {
            _opinionFrame = Time.frameCount;
            OpinionCache.Clear();
        }
        if (!OpinionCache.TryGetValue((a.id, b.id), out KingdomOpinion opinion))
            OpinionCache[(a.id, b.id)] = opinion = World.world.diplomacy.getOpinion(a, b);
        return opinion;
    }

    public static int Opinion(Kingdom a, Kingdom b) => OpinionOf(a, b)?.total ?? 0;

    public static List<(string label, int value)> OpinionParts(Kingdom a, Kingdom b)
    {
        KingdomOpinion opinion = OpinionOf(a, b);
        if (opinion?.results == null) return new List<(string, int)>();
        return opinion.results.Where(pair => pair.Value != 0)
            .OrderByDescending(pair => Mathf.Abs(pair.Value))
            .Select(pair => (LM.Get(pair.Key.getTranslationKey(pair.Value)), pair.Value)).ToList();
    }

    // ───────── 力量 ─────────
    public static Strength StrengthOf(Kingdom kingdom, bool bloc)
    {
        var result = new Strength();
        if (kingdom?.data == null) return result;
        IEnumerable<Kingdom> members = bloc ? Tree(kingdom).Select(entry => entry.kingdom) : new[] { kingdom };
        foreach (Kingdom member in members)
        {
            if (member?.data == null || member.isRekt()) continue;
            result.power += (float)member.GetNationalPower();
            result.army += CorpsSystem.Of(member).Sum(corps => corps.strength);
            result.navy += NavySystem.Of(member).Sum(fleet => fleet.warships);
            result.cities += member.cities.Count;
            result.population += CityPopulationSystem.AbstractPopulationEnabled
                ? CityPopulationSystem.KingdomBackground(member) : member.getPopulationPeople();
            result.money += member.GetMoney();
        }
        return result;
    }

    // ───────── 往来大事 ─────────
    public static List<(double at, string text)> History(Kingdom a, Kingdom b)
    {
        var events = new List<(double at, string text)>();
        if (a?.data == null || b?.data == null) return events;
        var seen = new HashSet<string>();
        void Add(double at, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !seen.Add(at.ToString("0") + text)) return;
            events.Add((at, text));
        }

        // 战争：两国分在两边
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war?.data == null) continue;
            var attackers = new HashSet<long>(war.data.list_attackers ?? new List<long>());
            attackers.UnionWith(war.data.past_attackers ?? new List<long>());
            var defenders = new HashSet<long>(war.data.list_defenders ?? new List<long>());
            defenders.UnionWith(war.data.past_defenders ?? new List<long>());
            bool opposed = attackers.Contains(a.id) && defenders.Contains(b.id) || attackers.Contains(b.id) && defenders.Contains(a.id);
            if (!opposed) continue;
            Add(war.data.created_time, string.Format(LM.Get("diplomacy_history_war_started"), war.data.name));
            if (war.hasEnded() && war.data.died_time > 0d)
                Add(war.data.died_time, string.Format(LM.Get("diplomacy_history_war_ended"), war.data.name,
                    LM.Get("diplomacy_war_result_" + war.data.winner.ToString().ToLowerInvariant())));
        }

        // 帝国史册：记着对方编号、或正文提到对方国名的条目
        foreach ((Kingdom self, Kingdom other) in new[] { (a, b), (b, a) })
        {
            string otherName = other.GetKingdomName();
            var otherIds = new HashSet<long> { other.id };
            if (other.GetEmpire()?.CoreKingdom is Kingdom otherCore) otherIds.Add(otherCore.id);
            foreach (EmpireCraftHistory record in Histories(self))
            {
                if (record?.descriptions == null) continue;
                foreach (HistoryDescription entry in record.descriptions)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.description)) continue;
                    bool mentions = otherIds.Contains(entry.kingdom_id) ||
                                    otherName.Length >= 2 && entry.description.Contains(otherName);
                    if (mentions) Add(entry.timestamp, entry.description);
                }
            }
        }
        return events.OrderByDescending(item => item.at).ToList();
    }

    private static IEnumerable<EmpireCraftHistory> Histories(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire?.data == null) return Enumerable.Empty<EmpireCraftHistory>();
        EmpireCore core = EmpireCoreManager.Get(empire);
        List<EmpireCraftHistory> all = core != null ? EmpireCoreManager.GetAllHistories(core) : null;
        return all != null && all.Count > 0 ? all : empire.data.history ?? new List<EmpireCraftHistory>();
    }

    // ───────── 市场 ─────────
    public static string GoodName(CommodityConfig good)
    {
        string key = "commodity_" + good.id;
        return LM.Has(key) ? LM.Get(key) : good.id;
    }
}
