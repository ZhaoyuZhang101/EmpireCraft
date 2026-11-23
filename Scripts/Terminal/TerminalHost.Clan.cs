using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 宗族(用户 2026-11-18 "手机舆图上族谱和世系表都没法放大看, 而且也看不到族谱主页和先祖前几代, 为什么不把原模组的界面搬过来")：
// 照游戏的宗族窗口(SpecificClanWindow)搬——总览、族人名录、大事记、亲属树、生平列传；点任何一个人就"换成这个人"
// (人在别的宗族就连宗族一起换)，亲属树以他为中心重画，往上一直翻得到先祖。界面目录不编进核心，口径照它在这里另写一遍。
// 手机点进来才要(/api/clan，id 是族谱身份编号，当字；空的是所领之君本人)，主线程算好放着，同典册
public static partial class TerminalHost
{
    private const int ClanMembersShown = 80, ClanChronicleShown = 120, ClanAnnalsShown = 160, ClanNotables = 6, ClanOverviewRows = 5;
    private static readonly ConcurrentDictionary<string, long> ClanWant = new();
    private static readonly ConcurrentDictionary<string, string> ClanReady = new();

    // 族人生平里算大事的(同 SpecificClanWindow.Chronicle 的 MajorEvents)
    private static readonly HashSet<string> ClanMajorEvents = new()
    {
        "personal_history_became_emperor", "personal_history_peerage_granted", "personal_history_peerage_inherited",
        "personal_history_joined_cabinet", "personal_history_became_cabinet_leader", "personal_history_tributary_title_granted",
        "personal_history_powerful_minister_usurpation", "personal_history_powerful_minister_nine_bestowments",
        "personal_history_powerful_minister_duke", "personal_history_powerful_minister_controls_court",
        "personal_history_king_of_romans", "personal_history_installed_by_empress_dowager", "personal_history_imperial_ban",
        "personal_history_impeached", "personal_history_excommunicated", "personal_history_empress_dowager_deposed_emperor",
        "personal_history_deposed_by_powerful_minister", "personal_history_deposed_by_empress_dowager",
        "personal_history_coup_leader", "personal_history_coup_deposed", "personal_history_became_chief_elector",
        "personal_history_became_faction_leader", "personal_history_war_commander", "personal_history_culture_reform_surname"
    };

    // ───────── 网络线程 ─────────
    private static TerminalServer.Reply ClanReply(Seat seat, string body)
    {
        string raw = Field(body, "id");
        long id = -1;
        if (!string.IsNullOrEmpty(raw) && !long.TryParse(raw, out id)) return TerminalServer.Reply.Error(400, "bad_request");
        string key = seat.Token + "|" + (raw ?? "");
        if (ClanReady.TryRemove(key, out string built)) return TerminalServer.Reply.Json(built);
        if (ClanWant.Count < 16) ClanWant[key] = id;
        return TerminalServer.Reply.Json("{\"ready\":false}");
    }

    // ───────── 主线程(随典册一起，见 RunAnnals) ─────────
    private static void RunClan()
    {
        if (ClanWant.IsEmpty || !WorldReady()) return;
        if (ClanReady.Count > 32) ClanReady.Clear();
        foreach (string key in ClanWant.Keys.ToList())
        {
            if (!ClanWant.TryRemove(key, out long id)) continue;
            string token = key.Substring(0, key.IndexOf('|'));
            if (!SeatByToken.TryGetValue(token, out Seat seat) || seat.Ruler == null || seat.Realm == null) continue;
            try
            {
                ClanReady[key] = ClanBundle(seat, id).ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception exception)
            {
                ClanReady[key] = "{\"ready\":true}";
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 宗族没备成: {exception.Message}");
            }
            return;
        }
    }

    private static JObject ClanBundle(Seat seat, long id)
    {
        PersonalClanIdentity home = seat.Ruler.GetPersonalIdentity();
        PersonalClanIdentity person = id > 0 ? SpecificClanManager.getPerson(id) : home;
        SpecificClan clan = person?._specificClan;
        var json = new JObject { ["ready"] = true, ["home"] = home?.id.ToString() };
        if (person == null || clan == null) return json;
        List<PersonalClanIdentity> people = clan.SnapshotPeople().Where(found => found != null).ToList();
        List<PersonalClanIdentity> living = clan.LivingPeople ?? new List<PersonalClanIdentity>();
        Empire ruling = ClanRulingEmpire(clan);
        (string standing, string tone) = ClanStanding(clan, ruling);
        PersonalClanIdentity founder = SpecificClanManager.getPerson(clan.founder);
        int generations = people.Count == 0 ? 0 : people.Max(found => ClanGeneration(clan, found));
        json["id"] = person.id.ToString();
        json["clan"] = new JObject
        {
            ["name"] = TerminalIntel.Strip(clan.GetDisplayName() ?? ""), ["color"] = ClanHex(clan), ["standing"] = TerminalIntel.Strip(standing),
            ["tone"] = tone, ["sum"] = TerminalIntel.Strip(string.Format(LM.Get("clan_rail_summary"), ClanYear(clan.established_timestamp), generations))
        };
        json["person"] = new JObject
        {
            ["name"] = TerminalIntel.Strip(person.name ?? ""), ["caption"] = TerminalIntel.Strip(ClanCaption(clan, person)),
            ["post"] = TerminalIntel.Strip(ClanPost(person)), ["life"] = TerminalIntel.Strip(ClanLife(person)), ["alive"] = person.is_alive,
            ["look"] = LookOf(seat, person, false)
        };
        json["overview"] = ClanOverview(clan, people, living, ruling, standing, tone, founder);
        json["members"] = ClanMembers(clan, people, living);
        json["chronicle"] = ClanChronicle(clan, people, living, founder);
        Empire empire = seat.Realm.GetEmpire();
        json["tree"] = ClanJson(seat, empire != null && empire.CoreKingdom == seat.Realm ? empire : null, person);
        json["bio"] = ClanBiography(clan, person);
        return json;
    }

    // ───────── 人物文字(同 SpecificClanWindow) ─────────
    private static string ClanYear(double time) => time < 0d ? "?" : string.Format(LM.Get("clan_lineage_year"), Date.getYear(time));

    private static string ClanLife(PersonalClanIdentity person) => person.is_alive
        ? string.Format(LM.Get("clan_lineage_life_alive"), ClanYearOf(person.birthday), person.age)
        : string.Format(LM.Get("clan_lineage_life_dead"), ClanYearOf(person.birthday), ClanYearOf(person.deathday),
            person.recordedAge >= 0 ? person.recordedAge.ToString() : person.age >= 0 ? person.age.ToString() : "?");

    private static string ClanYearOf(string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return "?";
        string year = HistoryDateFormatter.GetYear(-1d, date);
        return string.IsNullOrWhiteSpace(year) ? date : year.Replace(",", "");
    }

    private static bool ClanOnOffice(PersonalClanIdentity person)
    {
        if (person == null || !person.is_alive) return false;
        Actor actor = person.is_virtual ? null : person._actor;
        if (actor?.data != null && !actor.isRekt()) return actor.IsOnOffice() || actor.isKing() || actor.isCityLeader();
        return !string.IsNullOrWhiteSpace(person.officeName);
    }

    private static int ClanRenown(PersonalClanIdentity person)
    {
        Actor actor = person.is_virtual || !person.is_alive ? null : person._actor;
        return actor?.data != null && !actor.isRekt() ? actor.renown : -1;
    }

    private static int ClanGeneration(SpecificClan clan, PersonalClanIdentity person)
    {
        PersonalClanIdentity founder = SpecificClanManager.getPerson(clan?.founder ?? -1L);
        if (founder != null && founder.generation > 0 && person.generation >= founder.generation)
            return person.generation - founder.generation + 1;
        return Mathf.Max(1, person.generation);
    }

    private static string ClanCaption(SpecificClan clan, PersonalClanIdentity person) =>
        string.Format(LM.Get("clan_generation"), ClanGeneration(clan, person)) + " · " +
        LM.Get(person.is_alive ? person.is_virtual ? "clan_person_virtual" : "clan_person_alive" : "clan_person_dead");

    private static string ClanHex(SpecificClan clan)
    {
        string hex = clan?.color ?? "";
        return hex.StartsWith("#") ? hex : "#" + hex;
    }

    private static (string text, string tone) ClanStanding(SpecificClan clan, Empire ruling)
    {
        if (ruling != null) return (string.Format(LM.Get("clan_standing_imperial"), ruling.GetEmpireName()), "b");
        if (!clan.gentry_assessed) return (LM.Get("clan_standing_unassessed"), "n");
        int pedigree = Mathf.RoundToInt(GentrySystem.Pedigree(clan));
        return GentrySystem.IsGentry(clan)
            ? (string.Format(LM.Get("clan_standing_gentry"), GentrySystem.GradeName(clan), pedigree), "y")
            : (string.Format(LM.Get("clan_standing_commoner"), pedigree), "n");
    }

    private static Empire ClanRulingEmpire(SpecificClan clan)
    {
        if (clan == null || ModClass.EMPIRE_MANAGER == null) return null;
        SpecificClan root = clan.GetRootClan();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            SpecificClan imperial = empire?.EmpireSpecificClan;
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || imperial == null) continue;
            if (imperial == clan || imperial.GetRootClan() == root) return empire;
        }
        return null;
    }

    private static string ClanBranchName(SpecificClan clan) =>
        clan.parent_clan_id < 0 || string.IsNullOrWhiteSpace(clan.branch_label) ? string.Format(LM.Get("clan_lineage_main"), clan.name) : clan.branch_label;

    private static string ClanReason(SpecificClan clan) =>
        string.IsNullOrEmpty(clan.branch_reason) ? "" : LM.Get("clan_branch_reason_" + clan.branch_reason);

    private static string ClanCityName(PersonalClanIdentity person) => GentrySystem.MemberCity(person)?.GetCityName();

    // 官职(同 SpecificClanWindow.Post)：当过皇帝的写帝号，已故有谥号的封君藩王写谥号，无谥号的藩王写王号，其余写官职，没有写"白身"
    internal static string ClanPost(PersonalClanIdentity identity)
    {
        string fallback = identity != null && !identity.is_alive && !string.IsNullOrWhiteSpace(identity.posthumous_name)
            ? identity.posthumous_name
            : !string.IsNullOrWhiteSpace(identity?.prince_title) ? identity.prince_title
            : string.IsNullOrWhiteSpace(identity?.officeName) ? LM.Get("clan_no_post") : identity.officeName;
        if (identity == null || identity.actor_id <= 0 || ModClass.EMPIRE_MANAGER == null) return fallback;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            EmpireCraftHistory current = empire?.data?.currentHistory;
            if (identity.is_alive && current != null && current.id == identity.actor_id)
            {
                string yearName = empire.data.has_year_name ? current.year_name : "";
                return string.IsNullOrWhiteSpace(yearName) ? fallback : $"{current.empire_name}{yearName}{LM.Get("emperor")}";
            }
        }
        if (identity.is_alive) return fallback;
        IEnumerable<EmpireCraftHistory> live = ModClass.EMPIRE_MANAGER.Where(empire => empire?.data?.history != null).SelectMany(empire => empire.data.history);
        IEnumerable<EmpireCraftHistory> archived = (ModClass.ALL_HISTORY_DATA ?? new Dictionary<long, List<EmpireCraftHistory>>())
            .Values.Where(list => list != null).SelectMany(list => list);
        EmpireCraftHistory history = live.Concat(archived).LastOrDefault(record => record != null && record.id == identity.actor_id);
        if (history == null) return fallback;
        string era = string.IsNullOrWhiteSpace(history.year_name) ? "" : $"({history.year_name})";
        if (!string.IsNullOrEmpty(history.miaohao_name)) return $"{history.empire_name}{LM.Get(history.miaohao_name)}{LM.Get(history.miaohao_suffix)}{era}";
        if (!string.IsNullOrEmpty(history.shihao_name)) return $"{PosthumousNameGenerator.ShiTitle(history)}{era}";
        return string.IsNullOrWhiteSpace(history.year_name) ? fallback : $"{history.empire_name}{history.year_name}{LM.Get("emperor")}";
    }

    private static JArray ClanRow(string label, string value, string tone = "n", PersonalClanIdentity person = null)
    {
        var row = new JArray(TerminalIntel.Strip(label ?? ""), TerminalIntel.Strip(value ?? ""), tone);
        if (person != null) row.Add(person.id.ToString());
        return row;
    }

    // 人物格：名、称谓、生卒、官职，点了换成他
    private static JObject ClanTile(SpecificClan clan, PersonalClanIdentity person) => new()
    {
        ["id"] = person.id.ToString(), ["name"] = TerminalIntel.Strip(person.name ?? ""), ["life"] = TerminalIntel.Strip(ClanLife(person)),
        ["post"] = TerminalIntel.Strip(ClanPost(person)), ["office"] = ClanOnOffice(person), ["alive"] = person.is_alive,
        ["gen"] = ClanGeneration(clan, person)
    };

    // ───────── 总览 ─────────
    private static JObject ClanOverview(SpecificClan clan, List<PersonalClanIdentity> people, List<PersonalClanIdentity> living, Empire ruling,
        string standing, string tone, PersonalClanIdentity founder)
    {
        var page = new JObject();
        page["founder"] = founder == null ? null : TerminalIntel.Strip(string.Format(LM.Get("clan_overview_founder_chip"), founder.name));
        page["founderId"] = founder?.id.ToString();
        page["since"] = ClanYear(clan.established_timestamp);
        List<ClanLandHoldingView> holdings = LandEconomySystem.GetSpecificClanHoldings(clan) ?? new List<ClanLandHoldingView>();
        float share = holdings.Sum(holding => holding.Share);
        int members = GentrySystem.Members(clan, living.Count);
        var kpis = new JArray
        {
            members > living.Count
                ? ClanRow(string.Format(LM.Get("clan_kpi_kin"), living.Count), members.ToString(), "g")
                : ClanRow(string.Format(LM.Get("clan_kpi_living"), people.Count), living.Count.ToString(), living.Count > 0 ? "g" : "b"),
            ClanRow(LM.Get("clan_kpi_pedigree"), clan.gentry_assessed && ruling == null ? Mathf.RoundToInt(GentrySystem.Pedigree(clan)).ToString() : "—", tone),
            ClanRow(string.Format(LM.Get("clan_kpi_land"), share.ToString("0.#")), string.Format(LM.Get("clan_kpi_cities"), holdings.Count), "i"),
            ClanRow(LM.Get("clan_kpi_officials"), string.Format(LM.Get("clan_kpi_people"), living.Count(ClanOnOffice)), "y")
        };
        page["kpis"] = kpis;
        // 门第
        var pedigree = new JArray { ClanRow(LM.Get("clan_pedigree_status"), standing, tone) };
        if (clan.gentry_assessed && ruling == null)
        {
            pedigree.Add(ClanRow(LM.Get("clan_pedigree_seat"), GentrySystem.JunWang(clan), "y"));
            pedigree.Add(ClanRow(LM.Get("clan_pedigree_score"), Mathf.RoundToInt(GentrySystem.Pedigree(clan)).ToString(), "y"));
            if (clan.gentry)
            {
                City seat = GentrySystem.Seat(clan);
                int cap = Mathf.RoundToInt(Mathf.Max(GentrySystem.KinCap(clan, seat), living.Count));
                pedigree.Add(ClanRow(LM.Get("clan_pedigree_kin"), string.Format(LM.Get("clan_pedigree_kin_value"), members, cap), "g"));
                pedigree.Add(ClanRow(LM.Get("clan_pedigree_influence"), Mathf.RoundToInt(GentrySystem.Influence(clan)).ToString(), "y"));
                int sheltered = GentrySystem.ShelteredBy(clan);
                if (sheltered > 0) pedigree.Add(ClanRow(LM.Get("clan_pedigree_sheltered"), string.Format(LM.Get("clan_kpi_people"), sheltered), "i"));
                int retainers = GentrySystem.Retainers(clan);
                if (retainers > 0) pedigree.Add(ClanRow(LM.Get("clan_pedigree_retainers"), string.Format(LM.Get("clan_pedigree_retainers_value"), retainers), "b"));
            }
        }
        Empire patron = clan.gentry_patron_empire_id >= 0 ? ModClass.EMPIRE_MANAGER?.get(clan.gentry_patron_empire_id) : null;
        if (patron?.data != null && !patron.isRekt()) pedigree.Add(ClanRow(LM.Get("clan_pedigree_patron"), patron.GetEmpireName()));
        if (clan.HasHistoryEmpire()) pedigree.Add(ClanRow(LM.Get("clan_pedigree_empire"), clan.empire_name, "b"));
        page["pedigree"] = pedigree;
        // 世系：出自哪一支、分出了哪些支(点了换成那一支的宗子或始祖)
        var lineage = new JArray();
        SpecificClan parent = clan.parent_clan_id >= 0 ? SpecificClanManager.Get(clan.parent_clan_id) : null;
        lineage.Add(ClanRow(LM.Get("clan_lineage_from"), parent == null ? LM.Get("clan_lineage_root") : ClanBranchName(parent), "n",
            parent == null ? null : ClanHead(parent)));
        List<SpecificClan> subs = SpecificClanManager._specificClans.ToList().Where(found => found != null && found.parent_clan_id == clan.id)
            .OrderBy(found => found.established_timestamp).ToList();
        foreach (SpecificClan sub in subs.Take(12))
        {
            int count = sub.LivingPeople?.Count ?? 0;
            lineage.Add(ClanRow(ClanBranchName(sub), count > 0 ? ClanReason(sub) + " · " + string.Format(LM.Get("clan_kpi_people"), count) : LM.Get("clan_branch_extinct"),
                count > 0 ? "n" : "m", ClanHead(sub)));
        }
        if (subs.Count == 0) page["noBranch"] = LM.Get("clan_lineage_no_branch");
        page["lineage"] = lineage;
        // 门第走势
        List<int> years = clan.pedigree_years ?? new List<int>(), values = clan.pedigree_values ?? new List<int>();
        int span = Mathf.Min(years.Count, values.Count);
        if (span >= 2) page["trend"] = new JObject { ["years"] = new JArray(years.Take(span)), ["values"] = new JArray(values.Take(span)) };
        // 田产、族人分布
        page["land"] = new JArray(holdings.OrderByDescending(holding => holding.Share).Take(ClanOverviewRows)
            .Select(holding => ClanRow(holding.CityName, $"{holding.Share:0.#}% · " + string.Format(LM.Get("clan_overview_city_rank"), holding.Rank),
                holding.Rank == 1 ? "y" : "g")));
        var spread = new JArray();
        foreach ((City city, int recorded, int unrecorded) in GentrySystem.PopulationPlaces(clan, living).Take(ClanOverviewRows))
            spread.Add(ClanRow(city == null ? LM.Get("clan_population_unknown") : city.GetCityName(),
                string.Format(LM.Get("clan_kpi_people"), recorded + unrecorded), "i"));
        page["spread"] = spread;
        // 显赫族人
        page["notables"] = new JArray(living.OrderByDescending(ClanOnOffice).ThenByDescending(ClanRenown).ThenBy(found => found.generation)
            .Take(ClanNotables).Select(found => ClanTile(clan, found)));
        var labels = new JObject
        {
            ["pedigree"] = LM.Get("clan_card_pedigree"), ["lineage"] = LM.Get("clan_card_lineage"), ["trend"] = LM.Get("clan_card_trend"),
            ["land"] = LM.Get("clan_card_land"), ["spread"] = LM.Get("clan_card_spread"), ["notables"] = LM.Get("clan_card_notables"),
            ["noLand"] = LM.Get("clan_overview_no_land")
        };
        page["labels"] = labels;
        return page;
    }

    // 一支的宗子(族长)，没有就开族始祖
    private static PersonalClanIdentity ClanHead(SpecificClan clan)
    {
        List<PersonalClanIdentity> living = clan?.LivingPeople;
        PersonalClanIdentity founder = SpecificClanManager.getPerson(clan?.founder ?? -1L);
        PersonalClanIdentity heir = living?.Where(found => found != null).OrderBy(found => found.generation).ThenBy(found => found.rank).FirstOrDefault();
        return heir ?? founder;
    }

    // ───────── 族人名录 ─────────
    // 在世的按居官、声望、辈分排(同游戏名录的默认排法)，至多 ClanMembersShown 人
    private static JObject ClanMembers(SpecificClan clan, List<PersonalClanIdentity> people, List<PersonalClanIdentity> living)
    {
        var list = new JArray();
        foreach (PersonalClanIdentity person in people.Where(found => found.is_alive).OrderByDescending(ClanOnOffice)
                     .ThenByDescending(ClanRenown).ThenBy(found => found.generation).Take(ClanMembersShown))
        {
            string detail = string.Join(" · ", new[]
            {
                string.Format(LM.Get("clan_generation"), ClanGeneration(clan, person)), ClanLife(person), ClanPost(person), ClanCityName(person)
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            list.Add(new JObject
            {
                ["id"] = person.id.ToString(), ["name"] = TerminalIntel.Strip(person.name ?? ""), ["detail"] = TerminalIntel.Strip(detail),
                ["office"] = ClanOnOffice(person)
            });
        }
        return new JObject
        {
            ["title"] = LM.Get("clan_page_members"),
            ["chip"] = TerminalIntel.Strip(string.Format(LM.Get("clan_members_chip"), living.Count, people.Count)),
            ["list"] = list, ["more"] = Math.Max(0, living.Count - list.Count)
        };
    }

    // ───────── 大事记(新的在前) ─────────
    private static JObject ClanChronicle(SpecificClan clan, List<PersonalClanIdentity> people, List<PersonalClanIdentity> living, PersonalClanIdentity founder)
    {
        var entries = new List<(double time, string date, string text, string tone, PersonalClanIdentity person, bool clan)>();
        void Add(double time, string text, string tone, PersonalClanIdentity person = null, string date = null, bool own = true)
        {
            if (time < 0d && string.IsNullOrEmpty(date) || string.IsNullOrWhiteSpace(text)) return;
            entries.Add((time, date, text, tone, person, own));
        }
        SpecificClan parent = clan.parent_clan_id >= 0 ? SpecificClanManager.Get(clan.parent_clan_id) : null;
        Add(clan.established_timestamp, parent == null
            ? string.Format(LM.Get("clan_chronicle_founded"), founder?.name ?? "?", clan.GetDisplayName())
            : string.Format(LM.Get("clan_chronicle_branched"), founder?.name ?? "?", ClanBranchName(parent), ClanReason(clan)), "y", founder);
        foreach (SpecificClan sub in SpecificClanManager._specificClans.ToList().Where(found => found != null && found.parent_clan_id == clan.id))
        {
            PersonalClanIdentity subFounder = SpecificClanManager.getPerson(sub.founder);
            Add(sub.established_timestamp, string.Format(LM.Get("clan_chronicle_sub_branch"), subFounder?.name ?? "?", ClanBranchName(sub), ClanReason(sub)), "n", subFounder);
        }
        foreach (PersonalHistoryRecord record in clan.clan_history ?? new List<PersonalHistoryRecord>())
            if (record != null) Add(record.timestamp, record.content, "y", null, record.date);
        if (clan.extinct_since >= 0d && living.Count == 0) Add(clan.extinct_since, string.Format(LM.Get("clan_chronicle_extinct"), clan.GetDisplayName()), "b");
        foreach (PersonalClanIdentity person in people)
        {
            if (person.personal_history != null)
                foreach (PersonalHistoryRecord record in person.personal_history)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.content) || !record.major && !ClanMajorEvents.Contains(record.event_key ?? "")) continue;
                    Add(record.timestamp, string.Format(LM.Get("clan_chronicle_person"), person.name, record.content), "n", person, record.date, false);
                }
            TalentRecord talent = person.actor_id > 0 ? FamousTalentSystem.OfActorId(person.actor_id) : null;
            if (talent != null && talent.founded_at >= 0d)
                Add(talent.founded_at, string.Format(LM.Get("clan_chronicle_talent"), person.name, FamousTalentSystem.KindName(talent.kind),
                    FamousTalentSystem.RankName(talent.rank)), "i", person, null, false);
        }
        var list = new JArray();
        foreach (var entry in entries.OrderByDescending(entry => entry.time).Take(ClanChronicleShown))
        {
            var row = new JObject
            {
                ["year"] = TerminalIntel.Strip(HistoryDateFormatter.GetYear(entry.time, entry.date) ?? ""),
                ["when"] = TerminalIntel.Strip(HistoryDateFormatter.GetMonthDay(entry.time, entry.date) ?? ""),
                ["text"] = TerminalIntel.Strip(entry.text), ["tone"] = entry.tone
            };
            if (entry.clan) row["clan"] = true;
            if (entry.person != null) row["pid"] = entry.person.id.ToString();
            list.Add(row);
        }
        return new JObject
        {
            ["title"] = LM.Get("clan_page_chronicle"),
            ["chip"] = TerminalIntel.Strip(string.Format(LM.Get("clan_chronicle_chip"), entries.Count, entries.Count(entry => entry.clan))),
            ["list"] = list, ["more"] = Math.Max(0, entries.Count - list.Count), ["empty"] = LM.Get("clan_chronicle_empty")
        };
    }

    // ───────── 生平列传 ─────────
    private static JObject ClanBiography(SpecificClan clan, PersonalClanIdentity person)
    {
        try
        {
            person.BackfillCurrentEraNameHistory();
            person.BackfillChildBirthHistoryNames();
        }
        catch (Exception)
        {
            // 补记不成就照现有的
        }
        TalentRecord talent = person.actor_id > 0 ? FamousTalentSystem.OfActorId(person.actor_id) : null;
        string where = person.is_virtual ? person.birthplace : ClanCityName(person);
        // 爵位：语言表里有的才写(游戏里没配字的爵位键会原样露出来，如 Frankish_peerages_6)
        string peerage = !string.IsNullOrEmpty(person.PeeragesLevel) && LM.Has(person.PeeragesLevel) ? LM.Get(person.PeeragesLevel) : null;
        string detail = string.Join(" · ", new[]
        {
            string.Format(LM.Get("clan_generation"), ClanGeneration(clan, person)), ClanLife(person), where, ClanPost(person), peerage,
            person.ownedTitleNames?.FirstOrDefault()
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bio = new JObject
        {
            ["name"] = TerminalIntel.Strip(person.name ?? ""), ["detail"] = TerminalIntel.Strip(detail),
            ["talent"] = talent == null ? null : TerminalIntel.Strip(FamousTalentSystem.KindName(talent.kind) + " · " + FamousTalentSystem.RankName(talent.rank)),
            ["text"] = TerminalIntel.Strip(BiographySystem.Compose(person) ?? ""), ["textTitle"] = LM.Get("biography_heading")
        };
        // 恩怨与志向
        var ties = new JArray();
        foreach (PersonalTie tie in person.ties ?? new List<PersonalTie>())
        {
            PersonalClanIdentity other = SpecificClanManager.getPerson(tie?.other ?? -1L);
            if (tie == null || other == null) continue;
            (string label, string tone) = tie.kind switch
            {
                1 => (LM.Get("clan_tie_patron"), "g"), 2 => (LM.Get("clan_tie_protege"), "g"), _ => (LM.Get("clan_tie_rival"), "b")
            };
            string value = tie.kind == 3 && !string.IsNullOrEmpty(tie.cause) ? other.name + " · " + LM.Get("tie_cause_" + tie.cause) : other.name;
            ties.Add(ClanRow(label, value, tone, other._specificClan != null ? other : null));
        }
        if (!string.IsNullOrEmpty(person.ambition))
            ties.Add(ClanRow(LM.Get("clan_tie_ambition"), LM.Get("ambition_" + person.ambition) + (person.ambition_done ? " · " + LM.Get("clan_ambition_done")
                : person.is_alive ? "" : " · " + LM.Get("clan_ambition_failed")), person.ambition_done ? "g" : person.is_alive ? "y" : "n"));
        if (talent != null)
        {
            string title = FamousTalentSystem.TitleText(talent);
            ties.Add(ClanRow(FamousTalentSystem.KindName(talent.kind), string.IsNullOrEmpty(title) ? FamousTalentSystem.RankName(talent.rank) : title, "y"));
        }
        bio["ties"] = ties;
        bio["tiesTitle"] = LM.Get("clan_card_ties");
        // 编年(旧的在前，同游戏)
        List<PersonalHistoryRecord> records = (person.personal_history ?? new List<PersonalHistoryRecord>()).Where(record => record != null)
            .OrderBy(record => record.timestamp).ToList();
        var annals = new JArray();
        foreach (PersonalHistoryRecord record in records.Skip(Math.Max(0, records.Count - ClanAnnalsShown)))
        {
            string content = record.content ?? "";
            if (!string.IsNullOrWhiteSpace(record.kingdom_name)) content += " · " + record.kingdom_name;
            var row = new JObject
            {
                ["year"] = TerminalIntel.Strip(HistoryDateFormatter.GetYear(record.timestamp, record.date) ?? ""),
                ["when"] = TerminalIntel.Strip(HistoryDateFormatter.GetMonthDay(record.timestamp, record.date) ?? ""),
                ["text"] = TerminalIntel.Strip(content), ["tone"] = record.major ? "y" : "n"
            };
            PersonalClanIdentity related = record.related_personal_identity_id > 0 ? SpecificClanManager.getPerson(record.related_personal_identity_id)
                : record.related_actor_id > 0 ? World.world.units.get(record.related_actor_id)?.GetPersonalIdentity() : null;
            if (related != null && related != person && related._specificClan != null) row["pid"] = related.id.ToString();
            annals.Add(row);
        }
        bio["annals"] = annals;
        bio["annalsTitle"] = LM.Get("clan_card_annals");
        bio["annalsChip"] = TerminalIntel.Strip(string.Format(LM.Get("clan_annals_count"), records.Count, records.Count(record => record.major)));
        bio["empty"] = LM.Get("personal_history_empty");
        // 相关记事：别人生平里提到此人的(新的在前)
        try
        {
            person.BackfillRelatedHistoryRecords();
        }
        catch (Exception)
        {
            // 补记不成就照现有的
        }
        var related2 = new JArray();
        foreach (PersonalHistoryRecord record in (person.related_history_records ?? new List<PersonalHistoryRecord>()).Where(record => record != null)
                     .OrderByDescending(record => record.timestamp).Take(60))
        {
            PersonalClanIdentity owner = record.owner_personal_identity_id > 0 ? SpecificClanManager.getPerson(record.owner_personal_identity_id) : null;
            string ownerName = string.IsNullOrWhiteSpace(owner?.name) ? LM.Get("related_history_owner_unknown") : owner.name;
            var row = new JObject
            {
                ["when"] = TerminalIntel.Strip(HistoryDateFormatter.GetYear(record.timestamp, record.date) + " " + HistoryDateFormatter.GetMonthDay(record.timestamp, record.date)),
                ["text"] = TerminalIntel.Strip(string.Format(LM.Get("clan_chronicle_person"), ownerName, record.content ?? ""))
            };
            if (owner != null && owner._specificClan != null) row["pid"] = owner.id.ToString();
            related2.Add(row);
        }
        bio["related"] = related2;
        bio["relatedTitle"] = LM.Get("clan_page_related");
        return bio;
    }
}
