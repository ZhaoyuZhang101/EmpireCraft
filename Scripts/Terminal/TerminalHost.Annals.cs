using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 典册(用户 2026-11-12 "我老觉得这套玩家没有什么参与感，最好多加点功能比如查看君主世系，用帝国模组里的那种方式做出树，
// 还有自己的族谱，还能查看帝国历史和法理，玩家也能切换自己帝国的主法理，还有前缀后缀")：
//   · 世系——本朝历代君主连同把他们连起来的先人，一棵树(照 DynastyLineageWindow 的拼法：史册里的历代君主记录对上族谱的人，
//     顺着父母往上接到共同的祖先)；不是帝国的国王没有史册，就从本人往上数、连同族中做过国君的；
//   · 族谱——以所领之君为中心：往上三代，旁及叔伯、兄弟、堂兄弟，往下到孙辈；
//   · 史册、法理、国号——见本文件后半。
// 这些都是自家的事，不过迷雾、不打折。数是手机点进这一页才要的(/api/annals)，主线程备好一份放着，不随每秒的国势一起发。
public static partial class TerminalHost
{
    // 族谱往上 8 代、往下 3 代、至多 240 人(用户 2026-11-12 手机上的族谱"少了人"：以前上 3 下 2、140 人)
    private const int LineageUp = 12, ClanUp = 8, ClanDown = 3, ClanCap = 240, HistoryCap = 160;
    private const float AnnalsSeconds = 0.5f;
    private static readonly ConcurrentDictionary<string, bool> AnnalsWant = new();
    private static readonly ConcurrentDictionary<string, string> AnnalsJson = new();
    private static float _nextAnnals;

    // 网络线程：记下"要"，备好了就给
    private static TerminalServer.Reply AnnalsReply(Seat seat)
    {
        if (AnnalsJson.TryRemove(seat.Token, out string built)) return TerminalServer.Reply.Json(built);
        AnnalsWant[seat.Token] = true;
        return TerminalServer.Reply.Json("{\"ready\":false}");
    }

    // 主线程：一回只备一份
    private static void RunAnnals(float now)
    {
        if (now < _nextAnnals || AnnalsWant.IsEmpty) return;
        _nextAnnals = now + AnnalsSeconds;
        if (!WorldReady()) return;
        foreach (string token in AnnalsWant.Keys.ToList())
        {
            AnnalsWant.TryRemove(token, out _);
            if (!SeatByToken.TryGetValue(token, out Seat seat) || seat.Ruler == null || seat.Realm == null) continue;
            try
            {
                AnnalsJson[token] = BuildAnnals(seat).ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception exception)
            {
                AnnalsJson[token] = "{\"ready\":true}";
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 典册没备成: {exception.Message}");
            }
            return;
        }
    }

    private static JObject BuildAnnals(Seat seat)
    {
        Kingdom realm = seat.Realm;
        Empire empire = realm.GetEmpire();
        bool court = empire != null && empire.CoreKingdom == realm;
        return new JObject
        {
            ["ready"] = true,
            ["lineage"] = Guard(() => LineageJson(seat, court ? empire : null)),
            ["clan"] = Guard(() => ClanJson(seat, court ? empire : null)),
            ["history"] = Guard(() => HistoryJson(seat, court ? empire : null)),
            ["dejure"] = Guard(() => DejureJson(seat, court ? empire : null))
        };
    }

    // 一页出错不拖垮别的页
    private static JObject Guard(Func<JObject> make)
    {
        try
        {
            return make() ?? new JObject();
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 典册一页没备成: {exception.Message}");
            return new JObject();
        }
    }

    // ───────── 世系 ─────────
    private sealed class Kin
    {
        public PersonalClanIdentity Person;
        public EmpireCraftHistory Reign;
        public int Order = -1;
        public long Id, Parent = -1;
    }

    // 帝系上认哪位作上一辈：一般认父；母亲是君主而父亲不是，认母
    private static PersonalClanIdentity LineParent(PersonalClanIdentity person, HashSet<long> rulers)
    {
        PersonalClanIdentity father = person.father > 0 ? SpecificClanManager.getPerson(person.father) : null;
        PersonalClanIdentity mother = person.mother > 0 ? SpecificClanManager.getPerson(person.mother) : null;
        if (father == null) return mother;
        if (mother != null && rulers.Contains(mother.id) && !rulers.Contains(father.id)) return mother;
        return father;
    }

    private static JObject LineageJson(Seat seat, Empire empire)
    {
        // 历代君主：帝国看史册(历朝都列，用户 2026-11-12 世系图"少了人"：以前只取今上这一朝)，王国看原版记的历任国君
        var reigns = new List<(PersonalClanIdentity person, EmpireCraftHistory reign, string name)>();
        string title;
        if (empire != null)
        {
            var all = new List<EmpireCraftHistory>();
            if (empire.data?.history != null) all.AddRange(empire.data.history.Where(found => found != null));
            EmpireCraftHistory current = empire.data?.currentHistory;
            if (current != null && !all.Contains(current)) all.Add(current);
            all = all.Where(found => !found.is_republic).ToList();
            foreach (EmpireCraftHistory reign in all)
                reigns.Add((reign.id > 0 ? SpecificClanManager.PersonByActorId(reign.id) : null, reign, reign.emperor ?? ""));
            // 只有一朝的照朝代叫，换过朝代的照国号叫
            List<string> dynasties = all.Select(DynastyOf).Distinct().ToList();
            title = string.Format(LM.Get("lineage_chart_title"), dynasties.Count == 1 ? dynasties[0] : NameOf(seat.Realm));
        }
        else
        {
            foreach (LeaderEntry entry in seat.Realm.data.past_rulers ?? new List<LeaderEntry>())
            {
                PersonalClanIdentity person = entry == null ? null : SpecificClanManager.PersonByActorId(entry.id);
                if (person != null && reigns.All(found => found.person != person)) reigns.Add((person, null, person.name ?? entry.name ?? ""));
            }
            PersonalClanIdentity self = seat.Ruler.GetPersonalIdentity();
            if (self != null && reigns.All(found => found.person != self)) reigns.Add((self, null, self.name ?? seat.Ruler.getName()));
            title = string.Format(LM.Get("lineage_chart_title"), NameOf(seat.Realm));
        }

        var rulers = new HashSet<long>(reigns.Where(found => found.person != null).Select(found => found.person.id));
        // 各位君主往上的先人；一位先人出现在几位君主的上头，就是把他们连起来的那一位
        var chains = new List<List<PersonalClanIdentity>>();
        var seenBy = new Dictionary<long, int>();
        foreach ((PersonalClanIdentity person, _, _) in reigns)
        {
            var chain = new List<PersonalClanIdentity>();
            var walked = new HashSet<long>();
            for (PersonalClanIdentity at = person; at != null && chain.Count <= LineageUp && walked.Add(at.id); at = LineParent(at, rulers))
                chain.Add(at);
            chains.Add(chain);
            foreach (PersonalClanIdentity at in chain) seenBy[at.id] = seenBy.TryGetValue(at.id, out int count) ? count + 1 : 1;
        }

        var kin = new Dictionary<long, Kin>();
        var order = new List<Kin>();
        for (int i = 0; i < reigns.Count; i++)
        {
            (PersonalClanIdentity person, EmpireCraftHistory reign, _) = reigns[i];
            if (person == null)
            {
                // 族谱里对不上的(剧本写的先帝)：单独一格
                order.Add(new Kin { Reign = reign, Order = i, Id = -(i + 1) });
                continue;
            }
            List<PersonalClanIdentity> chain = chains[i];
            // 留到最高的那位"别的君主上头也有"的先人为止；再往上的只跟这一位有关，不画
            int top = 0;
            for (int at = 1; at < chain.Count; at++)
                if (seenBy[chain[at].id] >= 2 || rulers.Contains(chain[at].id)) top = at;
            for (int at = 0; at <= top; at++)
            {
                PersonalClanIdentity member = chain[at];
                if (!kin.TryGetValue(member.id, out Kin node))
                {
                    node = kin[member.id] = new Kin { Person = member, Id = member.id };
                    order.Add(node);
                }
                if (at == 0)
                {
                    node.Reign = reign;
                    node.Order = i;
                }
                if (at < top) node.Parent = chain[at + 1].id;
            }
        }

        PersonalClanIdentity mine = seat.Ruler.GetPersonalIdentity();
        var nodes = new JArray();
        int index = 0;
        foreach (Kin node in order)
        {
            bool reigning = empire != null ? node.Reign != null && node.Reign == empire.data?.currentHistory : node.Person != null && node.Person == mine;
            var item = new JObject
            {
                ["id"] = node.Id, ["parent"] = node.Parent > 0 ? node.Parent : null,
                ["name"] = TerminalIntel.Strip(node.Person?.name ?? node.Reign?.emperor ?? ""),
                ["title"] = TerminalIntel.Strip(LineTitle(node, empire, reigning, index)),
                ["ruler"] = node.Order >= 0, ["alive"] = node.Person?.is_alive ?? reigning,
                ["self"] = node.Person != null && node.Person == mine,
                ["look"] = node.Person != null ? LookOf(seat, node.Person, node.Order >= 0) : LookText(true, 40, node.Order >= 0 ? "emperor" : "commoner", seat.Realm)
            };
            if (node.Order >= 0)
            {
                item["no"] = node.Order + 1;
                int years = reigning && empire != null && empire.data.newEmperor_timestamp > 0
                    ? Date.getYearsSince(empire.data.newEmperor_timestamp) : node.Reign?.total_time ?? -1;
                if (years >= 0) item["years"] = string.Format(LM.Get("lineage_reign_years"), years);
            }
            else if (node.Person != null) item["years"] = AgeText(node.Person);
            item["detail"] = LineDetail(node, empire, order, mine);
            nodes.Add(item);
            index++;
        }
        return new JObject
        {
            ["title"] = title, ["sum"] = string.Format(LM.Get("lineage_chip_total"), reigns.Count), ["nodes"] = nodes,
            ["focus"] = mine != null && kin.ContainsKey(mine.id) ? mine.id : order.LastOrDefault(found => found.Order >= 0)?.Id
        };
    }

    private static string DynastyOf(EmpireCraftHistory history) =>
        !string.IsNullOrWhiteSpace(history.dynasty_name) ? history.dynasty_name
        : !string.IsNullOrWhiteSpace(history.royal_surname) ? history.royal_surname
        : history.empire_name ?? "";

    private static string Temple(EmpireCraftHistory reign) =>
        string.IsNullOrEmpty(reign?.miaohao_name) ? "" : LM.Get(reign.miaohao_name) + (string.IsNullOrEmpty(reign.miaohao_suffix) ? "" : LM.Get(reign.miaohao_suffix));

    private static string LineTitle(Kin node, Empire empire, bool reigning, int index)
    {
        if (node.Order >= 0)
        {
            string temple = Temple(node.Reign);
            if (temple.Length > 0) return temple;
            if (node.Reign != null && !string.IsNullOrEmpty(node.Reign.shihao_name)) return PosthumousNameGenerator.ShiTitle(node.Reign, empire);
            return reigning ? LM.Get("lineage_reigning") : string.Format(LM.Get("lineage_reign_n"), node.Order + 1);
        }
        return PersonTitle(node.Person);
    }

    // 不是君主的人，格子里写什么：谥号、王号、封号、官职，有哪样写哪样
    private static string PersonTitle(PersonalClanIdentity person)
    {
        if (person == null) return "";
        if (!string.IsNullOrEmpty(person.posthumous_name)) return person.posthumous_name;
        if (!string.IsNullOrEmpty(person.prince_title)) return person.prince_title;
        if (person.ownedTitleNames != null && person.ownedTitleNames.Count > 0) return person.ownedTitleNames[0];
        return person.officeName ?? "";
    }

    // 族谱里的人(未必还活着)的外貌依据：活着的照本人，不在了的照族谱上记的性别年岁；做过君主的照君主
    private static string LookOf(Seat seat, PersonalClanIdentity person, bool ruler)
    {
        Actor actor = person.is_alive && person.actor_id > 0 ? World.world.units.get(person.actor_id) : null;
        if (actor != null && actor.isAlive() && !ruler) return Look(actor);
        return LookText(person.sex == ActorSex.Male, person.age, ruler ? seat.Realm.IsEmpire() ? "emperor" : "king" : "commoner", seat.Realm);
    }

    private static string AgeText(PersonalClanIdentity person)
    {
        int age = person.age;
        if (age < 0) return "";
        return string.Format(LM.Get(person.is_alive ? "terminal_an_age" : "terminal_an_died"), age);
    }

    private static void Line(JArray lines, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(new JArray(LM.Get(key), TerminalIntel.Strip(value)));
    }

    private static JArray LineDetail(Kin node, Empire empire, List<Kin> order, PersonalClanIdentity mine)
    {
        var lines = new JArray();
        EmpireCraftHistory reign = node.Reign;
        if (reign != null)
        {
            Line(lines, "terminal_an_temple", Temple(reign));
            if (!string.IsNullOrEmpty(reign.shihao_name)) Line(lines, "terminal_an_posthumous", PosthumousNameGenerator.ShiTitle(reign, empire));
            Line(lines, "terminal_an_era", reign.year_name);
            // 与上一位的关系
            Kin previous = order.Where(found => found.Order >= 0 && found.Order < node.Order).OrderByDescending(found => found.Order).FirstOrDefault();
            string relation = reign.is_first ? LM.Get("lineage_rel_founder")
                : previous?.Person != null && node.Person != null ? LineageKinship.Term(previous.Person, node.Person) : "";
            if (!string.IsNullOrEmpty(reign.succession_note)) relation += (relation.Length > 0 ? " · " : "") + LM.Get("lineage_note_" + reign.succession_note);
            Line(lines, "terminal_an_succession", relation);
            if (reign.max_cities > 0) Line(lines, "terminal_an_cities", reign.start_cities + " → " + reign.end_cities + " (" + reign.max_cities + ")");
            if (reign.wars_won + reign.wars_lost > 0) Line(lines, "terminal_an_wars", reign.wars_won + " / " + reign.wars_lost);
        }
        PersonalClanIdentity person = node.Person;
        if (person != null)
        {
            if (reign == null) Line(lines, "terminal_an_post", SpecificClanWindow.Post(person));
            Line(lines, "terminal_an_born", person.birthday);
            if (!person.is_alive) Line(lines, "terminal_an_death", person.deathday);
            Line(lines, "terminal_an_years", AgeText(person));
            if (mine != null && person != mine) Line(lines, "terminal_an_kin", LineageKinship.Term(mine, person));
        }
        return lines;
    }

    // ───────── 族谱 ─────────
    private static JObject ClanJson(Seat seat, Empire empire)
    {
        PersonalClanIdentity self = seat.Ruler.GetPersonalIdentity();
        if (self == null) return new JObject();
        // 做过本朝君主的人(格子描金)
        var crowned = new HashSet<long>();
        if (empire != null)
        {
            foreach (EmpireCraftHistory reign in empire.data?.history ?? new List<EmpireCraftHistory>())
                if (reign != null && reign.id > 0) crowned.Add(reign.id);
            if (empire.data?.currentHistory != null) crowned.Add(empire.data.currentHistory.id);
        }
        else crowned.Add(seat.RulerId);

        // 本人往上的直系(认父，无父认母)
        var line = new List<PersonalClanIdentity> { self };
        var walked = new HashSet<long> { self.id };
        while (line.Count <= ClanUp)
        {
            PersonalClanIdentity at = line[line.Count - 1];
            PersonalClanIdentity up = (at.father > 0 ? SpecificClanManager.getPerson(at.father) : null) ?? (at.mother > 0 ? SpecificClanManager.getPerson(at.mother) : null);
            if (up == null || !walked.Add(up.id)) break;
            line.Add(up);
        }

        var parentOf = new Dictionary<long, long>();
        var members = new List<PersonalClanIdentity>();
        var taken = new HashSet<long>();
        void Take(PersonalClanIdentity person, long parent)
        {
            if (person == null || members.Count >= ClanCap || !taken.Add(person.id)) return;
            members.Add(person);
            if (parent > 0) parentOf[person.id] = parent;
        }
        // 往下带几代
        void Descend(PersonalClanIdentity from, int depth)
        {
            if (depth <= 0 || from.children == null) return;
            var kids = from.children.Select(SpecificClanManager.getPerson).Where(found => found != null).OrderBy(found => found.rank).ToList();
            foreach (PersonalClanIdentity kid in kids) Take(kid, from.id);
            foreach (PersonalClanIdentity kid in kids)
                if (parentOf.TryGetValue(kid.id, out long via) && via == from.id) Descend(kid, depth - 1);
        }
        // 直系先都放进去(从最上头往下)，再一圈圈往外带：本人的子孙、父亲的别的子女及其子女、祖父的……
        for (int i = line.Count - 1; i >= 0; i--) Take(line[i], i + 1 < line.Count ? line[i + 1].id : -1);
        for (int i = 0; i < line.Count; i++) Descend(line[i], ClanDown + (i > 0 ? 1 : 0) + (i > 1 ? i - 1 : 0));

        var nodes = new JArray();
        int living = 0;
        foreach (PersonalClanIdentity person in members)
        {
            if (person.is_alive) living++;
            var lines = new JArray();
            Line(lines, "terminal_an_post", SpecificClanWindow.Post(person));
            Line(lines, "terminal_an_born", person.birthday);
            if (!person.is_alive) Line(lines, "terminal_an_death", person.deathday);
            Line(lines, "terminal_an_years", AgeText(person));
            PersonalClanIdentity spouse = person.hasLover() ? SpecificClanManager.getPerson(person.lover.identity) : null;
            Line(lines, "terminal_an_spouse", spouse?.name);
            if (person != self) Line(lines, "terminal_an_kin", LineageKinship.Term(self, person));
            nodes.Add(new JObject
            {
                ["id"] = person.id, ["parent"] = parentOf.TryGetValue(person.id, out long parent) ? parent : null,
                ["name"] = TerminalIntel.Strip(person.name ?? ""), ["title"] = TerminalIntel.Strip(person == self ? LM.Get("terminal_an_self") : ShortPost(person)),
                ["years"] = AgeText(person), ["ruler"] = crowned.Contains(person.actor_id), ["alive"] = person.is_alive,
                ["self"] = person == self, ["detail"] = lines, ["look"] = LookOf(seat, person, crowned.Contains(person.actor_id))
            });
        }
        SpecificClan clan = seat.Ruler.GetSpecificClan();
        return new JObject
        {
            ["title"] = clan?.GetDisplayName() ?? LM.Get("terminal_an_clan"),
            ["sum"] = string.Format(LM.Get("terminal_an_members"), members.Count, living),
            ["nodes"] = nodes, ["focus"] = self.id
        };
    }

    // 格子里放得下的官爵：太长的截一截
    private static string ShortPost(PersonalClanIdentity person)
    {
        string post = PersonTitle(person);
        if (string.IsNullOrEmpty(post)) post = SpecificClanWindow.Post(person) ?? "";
        post = TerminalIntel.Strip(post);
        return post.Length > 8 ? post.Substring(0, 8) : post;
    }

    // ───────── 史册 ─────────
    // 朝廷看帝国史册(成品文字，新的在前)；不是帝国之主的，看所领之君自己的生平
    private static JObject HistoryJson(Seat seat, Empire empire)
    {
        var items = new JArray();
        string title;
        if (empire != null)
        {
            var reigns = new List<EmpireCraftHistory>();
            if (empire.data?.currentHistory != null) reigns.Add(empire.data.currentHistory);
            if (empire.data?.history != null)
                for (int i = empire.data.history.Count - 1; i >= 0; i--)
                    if (empire.data.history[i] != null && !reigns.Contains(empire.data.history[i])) reigns.Add(empire.data.history[i]);
            foreach (EmpireCraftHistory reign in reigns)
            {
                if (reign.descriptions == null) continue;
                for (int i = reign.descriptions.Count - 1; i >= 0 && items.Count < HistoryCap; i--)
                {
                    HistoryDescription told = reign.descriptions[i];
                    if (told == null || string.IsNullOrWhiteSpace(told.description)) continue;
                    items.Add(new JObject
                    {
                        ["head"] = TerminalIntel.Strip(string.IsNullOrWhiteSpace(told.time) ? HistoryDateFormatter.GetYear(told.timestamp) : told.time),
                        ["when"] = told.timestamp >= 0 ? HistoryDateFormatter.GetMonthDay(told.timestamp) : "",
                        ["text"] = TerminalIntel.Strip(told.description)
                    });
                }
                if (items.Count >= HistoryCap) break;
            }
            title = string.Format(LM.Get("terminal_an_history_of"), empire.GetEmpireFullName());
        }
        else
        {
            PersonalClanIdentity self = seat.Ruler.GetPersonalIdentity();
            List<PersonalHistoryRecord> life = self?.personal_history ?? new List<PersonalHistoryRecord>();
            for (int i = life.Count - 1; i >= 0 && items.Count < HistoryCap; i--)
            {
                PersonalHistoryRecord told = life[i];
                if (told == null || string.IsNullOrWhiteSpace(told.content)) continue;
                items.Add(new JObject
                {
                    ["head"] = HistoryDateFormatter.GetYear(told.timestamp, told.date),
                    ["when"] = HistoryDateFormatter.GetMonthDay(told.timestamp, told.date),
                    ["text"] = TerminalIntel.Strip(told.content)
                });
            }
            title = string.Format(LM.Get("terminal_an_life_of"), seat.Ruler.getName());
        }
        return new JObject { ["title"] = title, ["sum"] = string.Format(LM.Get("terminal_an_entries"), items.Count), ["items"] = items };
    }

    // ───────── 法理与国号 ─────────
    // 帝国之主：握着哪几处帝国法理、奉哪一处为主(国号随它)；握有它的首府或过半之城，就能改奉它为主。
    // 国王：握着哪几处王国法理、以哪一处为主(国名随它)；首府在手的可以改立。成员国、流官只能看
    private static readonly string[] PrefixKeys = { "Eastern", "Western", "Southern", "Northern", "Former", "Later", "great" };

    private static bool MayRename(Seat seat) => OwnEmpire(seat) != null || seat.Realm.GetEmpire() == null && Hereditary(seat.Realm);

    private static bool CanTakeCore(Empire empire, EmpireCore core, out int held, out int total, out City capital, out bool seated)
    {
        List<City> mine = EmpireCoreControl.HeldCities(empire, core);
        held = mine.Count;
        total = EmpireCoreControl.GetHolders(core).total;
        capital = core.CoreCapital > 0 ? World.world.cities.get(core.CoreCapital) : null;
        seated = capital != null && mine.Contains(capital);
        Empire bound = core.empire_id > 0 && core.empire_id != empire.id ? ModClass.EMPIRE_MANAGER.get(core.empire_id) : null;
        bool free = bound == null || bound.isRekt();
        return free && core.false_core_against_empire_id <= 0 && total > 0 && (seated || held * 2 > total);
    }

    private static JObject DejureJson(Seat seat, Empire empire)
    {
        Kingdom realm = seat.Realm;
        var titles = new JArray();
        var lands = new JArray();
        string sub(int held, int total, City capital, bool seated) =>
            string.Format(LM.Get("terminal_an_hold"), held, total) + (capital?.data == null ? ""
                : " · " + string.Format(LM.Get(seated ? "terminal_an_seat" : "terminal_an_seat_lost"), capital.data.name));
        if (empire != null)
        {
            EmpireCore current = EmpireCoreManager.Get(empire);
            foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values.ToList())
            {
                if (core == null) continue;
                bool can = CanTakeCore(empire, core, out int held, out int total, out City capital, out bool seated);
                if (held == 0 && core != current) continue;
                var row = new JObject
                {
                    ["id"] = "c" + core.id, ["name"] = TerminalIntel.Strip(EmpireCoreManager.GetDisplayName(core) ?? core.name ?? ""),
                    ["sub"] = sub(held, total, capital, seated), ["main"] = core == current, ["can"] = core != current && can
                };
                if (core != current && !can) row["why"] = LM.Get("terminal_an_need_seat");
                if (core == current) titles.Insert(0, row);
                else titles.Add(row);
            }
            // 所奉法理之下的各处王国法理，各在谁手里
            if (current != null)
                foreach (KingdomTitle title in EmpireCoreManager.GetTitles(current))
                {
                    if (title?.data == null || title.isRekt()) continue;
                    int all = title.city_list.Count, own = title.city_list.Count(city => city != null && !city.isRekt() && EmpireCoreControl.Sovereign(city.kingdom) == realm);
                    Kingdom holder = title.main_kingdom;
                    lands.Add(new JObject
                    {
                        ["name"] = TerminalIntel.Strip(title.data.name ?? ""),
                        ["sub"] = string.Format(LM.Get("terminal_an_hold"), own, all) +
                                  (holder?.data != null && !holder.isRekt() && holder != realm ? " · " + NameOf(holder) : ""),
                        ["own"] = all > 0 && own == all
                    });
                }
        }
        else
        {
            KingdomTitle main = realm.GetMainTitle();
            bool may = MayRename(seat);
            foreach (KingdomTitle title in realm.GetControlledTitles())
            {
                if (title?.data == null || title.isRekt()) continue;
                City capital = title.title_capital;
                bool seated = capital != null && !capital.isRekt() && capital.kingdom == realm;
                int own = title.city_list.Count(city => city != null && !city.isRekt() && city.kingdom == realm);
                var row = new JObject
                {
                    ["id"] = "t" + title.id, ["name"] = TerminalIntel.Strip(title.data.name ?? ""),
                    ["sub"] = sub(own, title.city_list.Count, capital, seated), ["main"] = title == main, ["can"] = may && title != main && seated
                };
                if (may && title != main && !seated) row["why"] = LM.Get("terminal_an_need_capital");
                if (title == main) titles.Insert(0, row);
                else titles.Add(row);
            }
        }

        var result = new JObject
        {
            ["title"] = LM.Get(empire != null ? "terminal_an_cores" : "terminal_an_titles"),
            ["sum"] = string.Format(LM.Get("terminal_an_entries"), titles.Count),
            ["note"] = LM.Get(empire != null ? "terminal_an_cores_note" : "terminal_an_titles_note"),
            ["titles"] = titles, ["lands"] = lands
        };
        if (!MayRename(seat)) return result;

        // 国号：前缀、后缀
        int chance = Mathf.RoundToInt(ScaleChance(realm, RealmNameBase, RealmNameSlope) * 100f);
        var name = new JObject { ["note"] = string.Format(LM.Get("terminal_an_name_note"), chance) };
        JObject part(string id, string shown, bool prefix)
        {
            var options = new JArray { new JObject { ["id"] = "auto", ["label"] = LM.Get("terminal_an_auto") } };
            if (prefix)
            {
                options.Add(new JObject { ["id"] = "none", ["label"] = "" });
                foreach (string key in PrefixKeys) options.Add(new JObject { ["id"] = key, ["label"] = LM.Get(key) });
            }
            return new JObject { ["id"] = id, ["current"] = shown, ["options"] = options, ["custom"] = true };
        }
        if (empire != null)
        {
            name["full"] = empire.GetEmpireFullName();
            bool fixedName = empire.CoreKingdom.HasCustomCountryNaming() || !string.IsNullOrWhiteSpace(empire.CoreKingdom.GetOrCreate().ideology_country_suffix);
            if (fixedName) name["why"] = LM.Get("terminal_an_name_fixed");
            else
            {
                EmpireData data = empire.data;
                name["prefix"] = part(!data.prefix_manual ? "auto" : string.IsNullOrEmpty(data.prefix_choice) ? "none" : data.prefix_choice,
                    OverallHelperFunc.LocalizeDirectPrefix(data), true);
                string typeName = string.IsNullOrWhiteSpace(data.empire_type_key) ? "" : LM.Get(data.empire_type_key);
                name["suffix"] = part(string.IsNullOrWhiteSpace(data.suffix_choice) ? "auto" : "=", string.IsNullOrWhiteSpace(data.suffix_choice) ? typeName : data.suffix_choice, false);
            }
        }
        else
        {
            name["full"] = realm.GetKingdomFullName();
            string custom = realm.GetOrCreate().custom_country_suffix;
            name["suffix"] = part(string.IsNullOrWhiteSpace(custom) ? "auto" : "=", string.IsNullOrWhiteSpace(custom) ? realm.GetKingdomTypeSuffixText() : custom, false);
        }
        result["name"] = name;
        return result;
    }

    // 前缀后缀手填的字：至多四个
    private static string NamePart(string choice)
    {
        string text = CleanText(choice.Substring(1));
        return text == null ? null : text.Length > 4 ? text.Substring(0, 4) : text;
    }

    // 下令：改奉主法理、改国号前缀后缀(都城里的事，照例过些时候办到)
    private static bool AnnalsOrder(Seat seat, Order made, string id, string target, string choice)
    {
        if (id != "dejure_main" && id != "name_prefix" && id != "name_suffix") return false;
        if (!MayRename(seat)) return false;
        Empire empire = OwnEmpire(seat);
        if (id == "dejure_main")
        {
            if (string.IsNullOrEmpty(target) || target.Length < 2 || !long.TryParse(target.Substring(1), out long titleId)) return false;
            string name;
            if (target[0] == 'c' && empire != null && EmpireCoreManager.EmpireCores.TryGetValue(titleId, out EmpireCore core) && core != null)
                name = EmpireCoreManager.GetDisplayName(core) ?? core.name;
            else if (target[0] == 't' && empire == null && ModClass.KINGDOM_TITLE_MANAGER.get(titleId) is { } title && !title.isRekt())
                name = title.data.name;
            else return false;
            made.Kind = id;
            made.Arg = target;
            made.Text = string.Format(LM.Get("terminal_order_dejure_main"), TerminalIntel.Strip(name ?? ""));
            return true;
        }
        if (string.IsNullOrEmpty(choice)) return false;
        bool prefix = id == "name_prefix";
        if (prefix && empire == null) return false;
        string value, shown;
        if (choice == "auto") (value, shown) = ("auto", LM.Get("terminal_an_auto"));
        else if (prefix && choice == "none") (value, shown) = ("none", LM.Get("terminal_an_none"));
        else if (prefix && PrefixKeys.Contains(choice)) (value, shown) = (choice, LM.Get(choice));
        else if (choice[0] == '=' && NamePart(choice) is { } text) (value, shown) = ("=" + text, text);
        else return false;
        made.Kind = id;
        made.Arg = value;
        made.Text = string.Format(LM.Get("terminal_order_" + id), shown);
        return true;
    }

    // 办：同改国号一样看权威，廷议不合就办不成
    private static string AnnalsExecute(Seat seat, Kingdom realm, Order order)
    {
        if (!MayRename(seat)) return LM.Get("terminal_fail_generic");
        float chance = ScaleChance(realm, RealmNameBase, RealmNameSlope);
        if (!ScaleRoll(order, realm, chance)) return string.Format(LM.Get("terminal_fail_era"), Mathf.RoundToInt(chance * 100f));
        Empire empire = OwnEmpire(seat);
        switch (order.Kind)
        {
            case "dejure_main":
            {
                if (!long.TryParse(order.Arg.Substring(1), out long id)) return LM.Get("terminal_fail_generic");
                if (order.Arg[0] == 'c')
                {
                    if (empire == null || !EmpireCoreManager.EmpireCores.TryGetValue(id, out EmpireCore core) || core == null ||
                        !CanTakeCore(empire, core, out _, out _, out _, out _)) return LM.Get("terminal_an_need_seat");
                    if (EmpireCoreManager.Get(empire) == core) return null;
                    // 改奉这一处为主：帝国绑到它名下，国号用它的名字(同称帝定号的写法)
                    EmpireCoreManager.RebindEmpire(empire, core);
                    empire.data.name_source = RealmNamingSystem.SourceEmpireTitle;
                    empire.data.named_core_id = core.id;
                    string coreName = EmpireCoreNamingSystem.GetCoreBaseName(core);
                    if (!string.IsNullOrWhiteSpace(coreName)) empire.SetEmpireName(coreName, true);
                    return null;
                }
                KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(id);
                if (empire != null || title == null || title.isRekt() || title.title_capital == null || title.title_capital.isRekt() ||
                    title.title_capital.kingdom != realm || realm.king == null) return LM.Get("terminal_an_need_capital");
                realm.king.AddOwnedTitle(title);
                realm.king.SetMainTitle(title);
                realm.SetMainTitle(title);
                EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(realm);
                return null;
            }
            case "name_prefix":
            {
                if (empire == null) return LM.Get("terminal_fail_generic");
                empire.data.prefix_manual = order.Arg != "auto";
                empire.data.prefix_choice = order.Arg == "auto" || order.Arg == "none" ? "" : order.Arg[0] == '=' ? order.Arg.Substring(1) : order.Arg;
                // 国号前段不变，照新前缀重写一遍显示名
                empire.SetEmpireName(empire.GetEmpireName(), true);
                return null;
            }
            case "name_suffix":
            {
                string text = order.Arg == "auto" ? "" : order.Arg.Substring(1);
                if (empire != null)
                {
                    empire.data.suffix_choice = text;
                    empire.SetEmpireName(empire.GetEmpireName(), true);
                }
                else
                {
                    realm.SetCustomCountrySuffix(text);
                    EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(realm);
                }
                return null;
            }
        }
        return LM.Get("terminal_fail_generic");
    }
}
