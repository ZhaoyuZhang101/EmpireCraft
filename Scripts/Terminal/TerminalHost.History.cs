using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.Terminal;

// 管理员页的「史」(用户 2026-11-16 "我作为管理员希望能看到世界历史")：
//   · 编年——世界上发过的消息(WorldChronicle)，按年分组、新的在前，可只看大事；一页一页取(POST /api/admin/history，part=chron)；
//   · 历朝——现存的帝国与已亡的王朝(连同开局前的史实朝代)，按立国先后排，点开看历代君主(part=dyn)；
//   · 一国(用户 2026-11-16 "就像奏报一样""不局限于一国""只能看不能操作""也能单独点开某个国家去看他的历史和君主世系")：
//     史册与君主世系照席位「典册」的拼法(LineageJson、HistoryJson)，借一个只读的看客席位按那一国拼，改法理、改国号这些不给(part=realm)；
//     编年也可只看这一国(kid)。
// 编年：主线程有新条目时把要显示的几样(年、月、文字、大事)抄成一份只读的行，网络线程按页从这份里取，不回主线程；
// 历朝：网络线程记下"要"，主线程在 RunAdmins 里拼好(至多 DynastySeconds 秒一次)再给。
public static partial class TerminalHost
{
    private const int ChroniclePage = 120;
    private const float DynastySeconds = 5f;

    private sealed class ChronicleRow
    {
        public int Year, Month;
        public string YearLabel, Text, Kid = "", KingdomName = "";
        public bool Major;
    }

    // 一国的史册与世系：网络线程记下要哪一国，主线程拼好放着(至多 RealmSeconds 秒前拼的照给)
    private const float RealmSeconds = 10f;
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<long, bool> RealmWant = new();
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<long, (string json, DateTime at)> RealmJson = new();

    // 主线程维护的行(与 WorldChronicle.Entries 一一对应)，抄给网络线程的只读一份
    private static readonly List<ChronicleRow> ChronicleRows = new();
    private static long _chronicleStamp = -1, _chronicleTrims = -1;
    // 年份的写法按年缓存(格式化要走正则)
    private static readonly Dictionary<int, string> YearLabels = new();
    private static volatile ChronicleRow[] _chronicleSnap;
    private static volatile string _dynastyJson;
    private static volatile bool _dynastyWant;
    private static float _nextDynasty;

    // 网络线程
    private static TerminalServer.Reply HistoryReply(string body)
    {
        string part = Field(body, "part");
        if (part == "dyn")
        {
            _dynastyWant = true;
            return TerminalServer.Reply.Json(_dynastyJson ?? "{\"ready\":false}");
        }
        if (part == "realm")
        {
            if (!long.TryParse(Field(body, "id"), out long realmId)) return TerminalServer.Reply.Error(400, "bad_request");
            if (RealmJson.TryGetValue(realmId, out var cached) && (DateTime.UtcNow - cached.at).TotalSeconds < RealmSeconds)
                return TerminalServer.Reply.Json(cached.json);
            if (RealmWant.Count < 8) RealmWant[realmId] = true;
            return TerminalServer.Reply.Json("{\"ready\":false}");
        }
        string only = Field(body, "kid") ?? "";
        ChronicleRow[] rows = _chronicleSnap;
        if (rows == null) return TerminalServer.Reply.Json("{\"ready\":false}");
        int.TryParse(Field(body, "from"), out int from);
        bool majorOnly = Field(body, "major") == "1";
        var items = new JArray();
        int seen = 0, next = -1, total = 0;
        // 新的在前
        for (int i = rows.Length - 1; i >= 0; i--)
        {
            ChronicleRow row = rows[i];
            if (majorOnly && !row.Major || only.Length > 0 && row.Kid != only) continue;
            total++;
            if (seen++ < from) continue;
            if (items.Count >= ChroniclePage)
            {
                if (next < 0) next = from + items.Count;
                continue;
            }
            var item = new JObject { ["y"] = row.Year, ["yl"] = row.YearLabel, ["m"] = row.Month, ["x"] = row.Text, ["j"] = row.Major ? 1 : 0 };
            if (row.Kid.Length > 0)
            {
                item["k"] = row.Kid;
                item["kn"] = row.KingdomName;
            }
            items.Add(item);
        }
        return TerminalServer.Reply.Json(new JObject { ["ready"] = true, ["total"] = total, ["next"] = next, ["items"] = items }
            .ToString(Newtonsoft.Json.Formatting.None));
    }

    // 主线程，每秒(有管理员看着时)
    private static void RunHistory(float now)
    {
        if (!WorldReady()) return;
        try
        {
            SyncChronicle();
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 编年没备成: {exception.Message}");
        }
        foreach (long realmId in RealmWant.Keys.ToList())
        {
            RealmWant.TryRemove(realmId, out _);
            try
            {
                RealmJson[realmId] = (RealmHistoryJson(realmId).ToString(Newtonsoft.Json.Formatting.None), DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                RealmJson[realmId] = ("{\"ready\":true,\"gone\":true}", DateTime.UtcNow);
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 一国史册没备成: {exception.Message}");
            }
            if (RealmJson.Count > 64) RealmJson.Clear();
            break;
        }
        if (!_dynastyWant || now < _nextDynasty) return;
        _nextDynasty = now + DynastySeconds;
        _dynastyWant = false;
        try
        {
            _dynastyJson = DynastiesJson().ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (Exception exception)
        {
            _dynastyJson = "{\"ready\":true,\"items\":[]}";
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 历朝没备成: {exception.Message}");
        }
    }

    // 只补新来的条目；丢过条目(满了)或换了世界就整份重抄(行本身照旧，只是重排一遍)
    private static void SyncChronicle()
    {
        if (WorldChronicle.Stamp == _chronicleStamp && WorldChronicle.Trims == _chronicleTrims && _chronicleSnap != null) return;
        IReadOnlyList<ChronicleEntry> entries = WorldChronicle.Entries;
        if (WorldChronicle.Trims != _chronicleTrims || entries.Count < ChronicleRows.Count) ChronicleRows.Clear();
        for (int i = ChronicleRows.Count; i < entries.Count; i++)
        {
            ChronicleEntry entry = entries[i];
            if (entry.View is not ChronicleRow row)
            {
                int year = Date.getYear(entry.t);
                if (!YearLabels.TryGetValue(year, out string label))
                {
                    if (YearLabels.Count > 4096) YearLabels.Clear();
                    YearLabels[year] = label = TerminalIntel.Strip(HistoryDateFormatter.GetYear(entry.t));
                }
                entry.View = row = new ChronicleRow { Year = year, Month = Date.getMonth(entry.t), YearLabel = label, Text = entry.text ?? "", Major = entry.major };
                // 说的是哪一国(按抄行时的国名；国已经没了的不给点)
                Kingdom about = entry.kingdom >= 0 ? World.world.kingdoms.get(entry.kingdom) : null;
                if (about?.data != null && !about.isRekt())
                {
                    row.Kid = about.getID().ToString();
                    row.KingdomName = NameOf(about);
                }
            }
            ChronicleRows.Add(row);
        }
        _chronicleStamp = WorldChronicle.Stamp;
        _chronicleTrims = WorldChronicle.Trims;
        _chronicleSnap = ChronicleRows.ToArray();
    }

    // 换世界：编年、历朝从头备
    private static void ResetHistory()
    {
        ChronicleRows.Clear();
        YearLabels.Clear();
        _chronicleStamp = _chronicleTrims = -1;
        _chronicleSnap = null;
        _dynastyJson = null;
        RealmWant.Clear();
        RealmJson.Clear();
    }

    // 一国的史册与君主世系(只读)：照「典册」——帝国的核心国看帝国的史册与历代帝王，别的国看国君生平与历任国君
    private static JObject RealmHistoryJson(long realmId)
    {
        Kingdom realm = World.world.kingdoms.get(realmId);
        if (realm?.data == null || realm.isRekt()) return new JObject { ["ready"] = true, ["gone"] = true, ["id"] = realmId.ToString() };
        Actor king = realm.king != null && realm.king.isAlive() ? realm.king : null;
        // 看客席位：只拿来套典册的拼法，不登记、不领人
        var viewer = new Seat { Token = "", Name = "admin", Realm = realm, Ruler = king, RulerId = king?.getID() ?? -1 };
        Empire empire = realm.GetEmpire();
        Empire court = empire != null && empire.CoreKingdom == realm ? empire : null;
        var json = new JObject
        {
            ["ready"] = true, ["id"] = realmId.ToString(), ["name"] = NameOf(realm), ["ruler"] = king?.getName() ?? "",
            ["color"] = Hex(RealmMiniMap.KingdomColor(realm)), ["empire"] = court != null
        };
        // 没有国君的国：帝国的史册照给，国君生平、历任国君从国君往上数，给不出来
        if (king != null || court != null) json["lineage"] = Guard(() => LineageJson(viewer, court));
        if (king != null || court != null) json["history"] = Guard(() => HistoryJson(viewer, court));
        return json;
    }

    private static JObject DynastiesJson()
    {
        var rows = new List<(double at, JObject row)>();
        var listed = new HashSet<long>();
        if (ModClass.EMPIRE_MANAGER != null)
            foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
            {
                if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
                listed.Add(empire.data.id);
                var reigns = new List<EmpireCraftHistory>();
                if (empire.data.history != null) reigns.AddRange(empire.data.history.Where(reign => reign != null));
                if (empire.data.currentHistory != null && !reigns.Contains(empire.data.currentHistory)) reigns.Add(empire.data.currentHistory);
                double at = empire.data.timestamp_established_time;
                Kingdom core = empire.CoreKingdom;
                var row = new JObject
                {
                    ["id"] = empire.data.id.ToString(), ["name"] = TerminalIntel.Strip(empire.GetEmpireFullName()), ["alive"] = true,
                    ["from"] = YearLabel(at, reigns), ["years"] = Scripted(reigns) || at < 0d ? reigns.Sum(reign => reign.total_time) : Date.getYearsSince(at) + 1,
                    ["ruler"] = empire.Emperor?.getName() ?? "", ["era"] = empire.data.year_name ?? "",
                    ["color"] = core?.data != null ? Hex(RealmMiniMap.KingdomColor(core)) : "#888888",
                    ["kid"] = core?.data != null && !core.isRekt() ? core.getID().ToString() : "",
                    ["reigns"] = ReignsJson(reigns, true)
                };
                rows.Add((at, row));
            }
        foreach (FallenEmpireRecord fallen in FallenEmpireRecords.Records)
        {
            if (fallen == null || listed.Contains(fallen.id)) continue;
            listed.Add(fallen.id);
            List<EmpireCraftHistory> reigns = ModClass.ALL_HISTORY_DATA != null && ModClass.ALL_HISTORY_DATA.TryGetValue(fallen.id, out List<EmpireCraftHistory> found)
                ? found?.Where(reign => reign != null).ToList() ?? new List<EmpireCraftHistory>()
                : new List<EmpireCraftHistory>();
            rows.Add((fallen.established_at, new JObject
            {
                ["id"] = fallen.id.ToString(), ["name"] = TerminalIntel.Strip(fallen.name ?? ""), ["alive"] = false,
                ["from"] = YearLabel(fallen.established_at, reigns), ["to"] = fallen.fallen_at >= 0d ? YearLabel(fallen.fallen_at, null) : EndLabel(reigns),
                ["years"] = fallen.years, ["ruler"] = fallen.last_ruler ?? "", ["color"] = "#888888",
                ["reigns"] = ReignsJson(reigns, false)
            }));
        }
        var items = new JArray();
        foreach ((double _, JObject row) in rows.OrderBy(item => item.at)) items.Add(row);
        return new JObject { ["ready"] = true, ["items"] = items };
    }

    // 立国那年的写法：开局后立的按立国时刻；开局前就有的取头一代君主史册里写的年份
    private static string YearLabel(double at, List<EmpireCraftHistory> reigns)
    {
        // 剧本开局就在的(明清鼎革的南明、大清)：头一代君主是剧本写进史册的(没有时刻)，立国照史册写的那一年，不写成开局那年
        HistoryDescription told = reigns?.FirstOrDefault()?.descriptions?.FirstOrDefault(item => item != null);
        if (told != null && told.timestamp < 0d && !string.IsNullOrWhiteSpace(told.time)) return TerminalIntel.Strip(told.time);
        if (at >= 0d) return TerminalIntel.Strip(HistoryDateFormatter.GetYear(at));
        return told == null ? "" : TerminalIntel.Strip(told.time ?? "");
    }

    // 头一代君主是剧本写进史册的(开局前就有这一朝)
    private static bool Scripted(List<EmpireCraftHistory> reigns) =>
        reigns?.FirstOrDefault()?.descriptions?.FirstOrDefault(item => item != null) is { } told && told.timestamp < 0d && !string.IsNullOrWhiteSpace(told.time);

    // 开局前亡了的：末代君主史册里最后写的那一年
    private static string EndLabel(List<EmpireCraftHistory> reigns)
    {
        HistoryDescription told = reigns?.LastOrDefault()?.descriptions?.LastOrDefault(item => item != null && !string.IsNullOrWhiteSpace(item.time));
        return told == null ? "" : TerminalIntel.Strip(told.time);
    }

    private static JArray ReignsJson(List<EmpireCraftHistory> reigns, bool alive)
    {
        var list = new JArray();
        for (int i = 0; i < reigns.Count; i++)
        {
            EmpireCraftHistory reign = reigns[i];
            HistoryDescription first = reign.descriptions?.FirstOrDefault(item => item != null);
            string from = first == null ? "" : TerminalIntel.Strip(!string.IsNullOrWhiteSpace(first.time) ? first.time
                : first.timestamp >= 0 ? HistoryDateFormatter.GetYear(first.timestamp) : "");
            list.Add(new JObject
            {
                ["who"] = TerminalIntel.Strip(reign.emperor ?? ""), ["temple"] = TerminalIntel.Strip(Temple(reign)),
                ["era"] = TerminalIntel.Strip(reign.year_name ?? ""), ["years"] = reign.total_time, ["from"] = from,
                ["now"] = alive && i == reigns.Count - 1
            });
        }
        return list;
    }
}
