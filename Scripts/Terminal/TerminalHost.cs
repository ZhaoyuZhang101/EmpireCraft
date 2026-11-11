using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 终端模式(用户 2026-11-11，群里的主意)：离开电脑后用手机网页接着管一国。电脑前是神，终端上只是那一位君主——
// "手机终端模拟的是现实统治者的这个身份"：看得到的只有他拿得到的消息(见 TerminalIntel)。
//   · 设置里开终端模式(默认关)，游戏里开一个端口(TerminalServer)，手机填六位配对码连上；
//   · 一部手机一个席位(Seat)，可以好几个人各连各的："我可以选择一个君主，另一个也可以选一个""最后大家都用终端这一台服务器"。
//     各席位各领一位君主(同一位不能两人领)，奏报、地图、国势各算各的，互相看不到；
//   · 领的是君主本人(帝国的皇帝、普通的国王、帝国里的成员国君主都行)：他驾崩、失位、亡国，席位不撤，手机上再挑一位在世的；
//   · 有席位领着君主的时候世界走得慢(设置里的终端流速，默认一成；"终端模式下地图的事件流速会变慢")，消息也就细；
//   · 这一期只读：奏报、地图、国势。下命令(外交、批奏、立储、改元之类，只下简单的令、剩下让下面的人去办)是下一期。
// 线程：游戏对象只在主线程碰。Tick 每秒把各席位的状态拼成字符串放着，网络线程只读这些字符串、往 Commands 里放要办的事。
// 手机版游戏上不启用(终端就是给电脑上的游戏配的)
public static class TerminalHost
{
    public static bool Enabled;
    public static int Port = 8765;
    // 有席位领着君主时世界流速的百分比
    public static int SpeedPercent = 10;

    private const float StateSeconds = 2f, SightSeconds = 15f, IdleSightSeconds = 60f, ClientSeconds = 30f, RetrySeconds = 30f;
    private const int MaxReports = 200, MaxPending = 500, MaxCities = 40, MaxRulers = 120;
    private const int MapWidth = 220, BorderGap = 3, FogRadius = 5;

    internal sealed class Report
    {
        public long Id;
        public string Text, Kind;
        public double At;
        public int DueMonth, Late;
        public bool Own;
    }

    // 一部配过对的手机
    public sealed class Seat
    {
        public string Token, Name;
        public long RulerId = -1;
        public Actor Ruler;
        public Kingdom Realm;
        internal string VacantReason = "none", FormerRuler = "", FormerRealm = "";
        internal double Corruption;
        internal TerminalIntel.Sight Sight;
        // 上回算的眼界：已知之国
        internal readonly HashSet<long> Known = new();
        internal readonly List<string> KnownNames = new();
        internal readonly List<Report> Reports = new(), Pending = new();
        internal int Lost, LedgerYear = int.MinValue;
        internal long LedgerTreasury, LedgerPeople, LedgerArmy;
        internal volatile string StateJson, MapJson;
        internal long LastClientTicks;
        internal float NextState, NextSight;

        public bool Watched => DateTime.UtcNow.Ticks - Interlocked.Read(ref LastClientTicks) < TimeSpan.FromSeconds(ClientSeconds).Ticks;

        internal void ClearIntel()
        {
            Reports.Clear();
            Pending.Clear();
            Known.Clear();
            KnownNames.Clear();
            Lost = 0;
            LedgerYear = int.MinValue;
            NextSight = NextState = 0f;
            MapJson = null;
        }

        internal void Vacate(string reason)
        {
            RulerId = -1;
            Ruler = null;
            Realm = null;
            VacantReason = reason;
            ClearIntel();
        }
    }

    private static TerminalServer _server;
    private static readonly TerminalPairing Pairing = new();
    private static readonly ConcurrentDictionary<string, Seat> SeatByToken = new();
    private static readonly ConcurrentQueue<Action> Commands = new();
    private static string _pagePath;
    private static byte[] _page;
    private static DateTime _pageStamp;
    private static float _nextTick, _retryAt;
    private static long _seq;
    private static volatile bool _anyRuler;

    public static bool Running => _server?.Running == true;
    public static bool Lan => _server?.Lan == true;
    public static string Error => _server?.Error;
    public static string Code => Pairing.Code;

    // 世界流速的倍数(见 TerminalPatch)：有席位领着君主时放慢
    public static float SpeedFactor => Enabled && _anyRuler ? Mathf.Clamp(SpeedPercent, 1, 100) / 100f : 1f;

    public static void NewCode() => Pairing.NewCodeNow();

    // 设置刚改(开关、端口)：下一帧就重开，不等重试的间隔
    public static void Kick() => _retryAt = 0f;

    // 窗口要显示地址和二维码：这就把端口开起来(刚打开开关的那一下，还没轮到 Tick)
    public static void EnsureNow()
    {
        if (!Enabled || Application.isMobilePlatform || ModClass._declare == null) return;
        if (_server != null && _server.Running && _server.Port == Port) return;
        _retryAt = 0f;
        EnsureServer(Time.realtimeSinceStartup);
    }

    // 手机该连的那个地址：本机往外走的那块网卡的地址(只查路由，不发包)；查不到就用列表里头一个
    private static string _primary;
    private static float _primaryAt = -100f;

    public static string PrimaryAddress()
    {
        if (!Lan) return "127.0.0.1:" + Port;
        float now = Time.realtimeSinceStartup;
        if (_primary != null && now - _primaryAt < 10f) return _primary + ":" + Port;
        string found = null;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 53);
            if (socket.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address)) found = local.Address.ToString();
        }
        catch
        {
            // 没有往外的路：用下面的
        }
        if (found == null)
        {
            string first = Addresses().FirstOrDefault();
            found = first == null ? "127.0.0.1" : first.Substring(0, first.LastIndexOf(':'));
        }
        _primary = found;
        _primaryAt = now;
        return found + ":" + Port;
    }

    // 扫码用的整条地址(带配对码，扫了直接进)
    public static string PairUrl() => "http://" + PrimaryAddress() + "/?c=" + Code;

    // 窗口上显示的东西变了没有(配对码、席位、谁领着谁、在不在线)：变了窗口自己重画
    private static string _signature;

    private static void NotifyWindows()
    {
        var signature = new global::System.Text.StringBuilder();
        signature.Append(Running ? Code : "-").Append(Lan ? 'L' : 'l').Append(Port);
        foreach (Seat seat in Seats()) signature.Append('|').Append(seat.Token, 0, 4).Append(seat.RulerId).Append(seat.Watched ? '+' : '-');
        string now = signature.ToString();
        if (now == _signature) return;
        bool first = _signature == null;
        _signature = now;
        if (first) return;
        EmpireCraft.Scripts.UI.Windows.TerminalWindow.RefreshIfOpen();
        EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.RefreshTerminalPage();
    }

    // 各席位，按配对先后
    public static List<Seat> Seats()
    {
        var seats = new List<Seat>();
        foreach (KeyValuePair<string, string> paired in Pairing.Seats())
            if (SeatByToken.TryGetValue(paired.Key, out Seat seat)) seats.Add(seat);
        return seats;
    }

    // 除名：这部手机要重新配对才进得来
    public static void Remove(Seat seat)
    {
        if (seat == null) return;
        Pairing.Remove(seat.Token);
        SeatByToken.TryRemove(seat.Token, out _);
    }

    // 电脑上给席位指定君主(随时能换；actorId < 0 是收回)。同一位君主不能两个席位领
    public static bool Assign(Seat seat, long actorId)
    {
        if (seat == null) return false;
        if (actorId >= 0 && (!WorldReady() || !Free(World.world.units.get(actorId), seat))) return false;
        seat.Vacate("none");
        seat.RulerId = actorId;
        if (WorldReady()) Resolve(seat);
        return true;
    }

    // 换世界、读档：各席位所领的君主、奏报都清掉(席位留着)
    public static void ResetWorldState()
    {
        foreach (Seat seat in SeatByToken.Values)
        {
            seat.Vacate("none");
            seat.FormerRuler = seat.FormerRealm = "";
            seat.StateJson = null;
        }
        _anyRuler = false;
    }

    // 本机在局域网里的地址(给窗口显示)
    public static List<string> Addresses()
    {
        var found = new List<string>();
        try
        {
            foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    found.Add(address + ":" + Port);
        }
        catch
        {
            // 查不到就只给本机
        }
        if (found.Count == 0 || !Lan) found.Insert(0, "127.0.0.1:" + Port);
        return found;
    }

    private static bool WorldReady() => World.world != null && global::Config.game_loaded && !SmoothLoader.isLoading();

    // 每帧(ModClass.Update，暂停时也走)
    public static void Tick()
    {
        if (!Enabled || Application.isMobilePlatform)
        {
            if (_server != null)
            {
                _server.Stop();
                _server = null;
                _anyRuler = false;
            }
            return;
        }
        float now = Time.realtimeSinceStartup;
        EnsureServer(now);
        while (Commands.TryDequeue(out Action command))
        {
            try
            {
                command();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][终端] 命令没办成: {exception.Message}");
            }
        }
        if (now < _nextTick) return;
        _nextTick = now + 1f;
        bool ready = WorldReady();
        bool any = false, mapped = false;
        foreach (Seat seat in SeatByToken.Values)
        {
            if (!ready)
            {
                seat.StateJson = "{\"loaded\":false}";
                seat.MapJson = null;
                continue;
            }
            try
            {
                Resolve(seat);
                if (seat.Ruler != null)
                {
                    any = true;
                    seat.Corruption = CorruptionSystem.GetRate(seat.Realm);
                    seat.Sight = TerminalIntel.SightOf(EmpireLawSystem.GetEraTier(seat.Realm),
                        TechnologySystem.RealmHasTech(seat.Realm, "telegraph"));
                    Ledger(seat);
                    Deliver(seat);
                }
                bool watched = seat.Watched;
                // 地图取样费些工夫：一秒里至多给一个席位算
                if (seat.Ruler != null && !mapped && now >= seat.NextSight)
                {
                    seat.NextSight = now + (watched ? SightSeconds : IdleSightSeconds);
                    BuildMap(seat);
                    mapped = true;
                }
                if (now >= seat.NextState && (watched || seat.StateJson == null))
                {
                    seat.NextState = now + StateSeconds;
                    seat.StateJson = BuildState(seat).ToString(Newtonsoft.Json.Formatting.None);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][终端] 备状态出错: {exception.Message}");
            }
        }
        _anyRuler = any;
        NotifyWindows();
    }

    private static void EnsureServer(float now)
    {
        if (_server != null && _server.Running && _server.Port == Port) return;
        if (now < _retryAt) return;
        _retryAt = now + RetrySeconds;
        _server?.Stop();
        _server = new TerminalServer { Handler = Handle };
        _pagePath = Path.Combine(ModClass._declare.FolderPath, "Terminal", "index.html");
        Pairing.Load(Path.Combine(ModClass.ModsConfigFolder(), "EmpireCraft.terminal.tokens"));
        foreach (KeyValuePair<string, string> paired in Pairing.Seats())
            SeatByToken.GetOrAdd(paired.Key, token => new Seat { Token = token, Name = paired.Value });
        if (_server.Start(Port))
            LogService.LogInfo($"[EmpireCraft][终端] 已开在 {Port} 端口" + (_server.Lan ? "" : "(只听本机)"));
        else
            LogService.LogWarning($"[EmpireCraft][终端] {Port} 端口开不了: {_server.Error}");
    }

    // ───────── 网络线程 ─────────
    private static TerminalServer.Reply Handle(string method, string path, string token, string body)
    {
        if (method == "GET" && (path == "/" || path == "/index.html"))
        {
            byte[] page = Page();
            return page == null ? TerminalServer.Reply.Error(404, "no_page") : TerminalServer.Reply.Html(page);
        }
        if (method == "POST" && path == "/api/pair")
        {
            string name = Field(body, "name");
            string paired = Pairing.TryPair(Field(body, "code"), name, out bool locked, out bool full);
            if (paired == null)
                return locked ? TerminalServer.Reply.Error(429, "locked")
                    : full ? TerminalServer.Reply.Error(403, "full") : TerminalServer.Reply.Error(403, "wrong_code");
            string kept = Pairing.Seats().FirstOrDefault(seat => seat.Key == paired).Value ?? "";
            SeatByToken[paired] = new Seat { Token = paired, Name = kept };
            return TerminalServer.Reply.Json("{\"token\":\"" + paired + "\"}");
        }
        if (string.IsNullOrEmpty(token) || !SeatByToken.TryGetValue(token, out Seat mine))
            return TerminalServer.Reply.Error(401, "unpaired");
        Interlocked.Exchange(ref mine.LastClientTicks, DateTime.UtcNow.Ticks);
        if (method == "GET" && path == "/api/state") return TerminalServer.Reply.Json(mine.StateJson ?? "{\"loaded\":false}");
        if (method == "GET" && path == "/api/map") return TerminalServer.Reply.Json(mine.MapJson ?? "{\"ready\":false}");
        if (method == "POST" && path == "/api/choose")
        {
            if (!long.TryParse(Field(body, "id"), out long id)) return TerminalServer.Reply.Error(400, "bad_request");
            // 手机上只能在没有所领之君的时候挑(在位的君主要换，得回电脑前)
            Commands.Enqueue(() =>
            {
                if (mine.Ruler == null && WorldReady()) Assign(mine, id);
                mine.NextState = 0f;
            });
            return TerminalServer.Reply.Json("{\"ok\":true}");
        }
        return TerminalServer.Reply.Error(404, "not_found");
    }

    private static string Field(string body, string name)
    {
        try
        {
            return string.IsNullOrEmpty(body) ? null : JObject.Parse(body)[name]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Page()
    {
        try
        {
            if (_pagePath == null || !File.Exists(_pagePath)) return null;
            DateTime stamp = File.GetLastWriteTimeUtc(_pagePath);
            if (_page == null || stamp != _pageStamp)
            {
                _page = File.ReadAllBytes(_pagePath);
                _pageStamp = stamp;
            }
            return _page;
        }
        catch
        {
            return null;
        }
    }

    // ───────── 所领之君 ─────────
    private static bool Eligible(Actor actor) =>
        actor != null && actor.isAlive() && actor.kingdom?.data != null && !actor.kingdom.isRekt() && !actor.kingdom.wild &&
        actor.kingdom.king == actor;

    // 在位、且没被别的席位领着
    private static bool Free(Actor actor, Seat asking) => Eligible(actor) && TakenBy(actor.getID(), asking) == null;

    private static Seat TakenBy(long actorId, Seat except = null)
    {
        foreach (Seat seat in SeatByToken.Values)
            if (seat != except && seat.RulerId == actorId) return seat;
        return null;
    }

    private static void Resolve(Seat seat)
    {
        if (seat.RulerId < 0)
        {
            seat.Ruler = null;
            seat.Realm = null;
            return;
        }
        Actor actor = World.world.units.get(seat.RulerId);
        if (Eligible(actor))
        {
            if (seat.Ruler == null) seat.NextSight = 0f;
            seat.Ruler = actor;
            seat.Realm = actor.kingdom;
            seat.FormerRuler = actor.getName();
            seat.FormerRealm = seat.Realm.data.name;
            return;
        }
        // 驾崩、失位、亡国：席位不撤，等着再挑一位(用户："死后终端也不结束，可以再选地图上存活的任意君主")
        seat.Vacate(actor == null || !actor.isAlive() ? "dead" : seat.Realm == null || seat.Realm.isRekt() ? "fallen" : "deposed");
    }

    public struct RulerInfo
    {
        public long Id;
        public string Ruler, Realm, Kind, TakenBy;
        public int Cities, Age;
    }

    // 在世的君主(皇帝、国王、帝国里的成员国君主都算)，城多的在前；TakenBy 是领着他的席位的称呼
    public static List<RulerInfo> Rulers()
    {
        var rulers = new List<RulerInfo>();
        if (!WorldReady()) return rulers;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !Eligible(kingdom.king)) continue;
            Seat taken = TakenBy(kingdom.king.getID());
            rulers.Add(new RulerInfo
            {
                Id = kingdom.king.getID(), Ruler = kingdom.king.getName(), Realm = kingdom.data.name, Kind = Role(kingdom),
                Cities = kingdom.cities?.Count ?? 0, Age = kingdom.king.getAge(), TakenBy = taken == null ? null : SeatLabel(taken)
            });
        }
        rulers.Sort((a, b) => b.Cities.CompareTo(a.Cities));
        if (rulers.Count > MaxRulers) rulers.RemoveRange(MaxRulers, rulers.Count - MaxRulers);
        return rulers;
    }

    public static string SeatLabel(Seat seat) =>
        string.IsNullOrEmpty(seat.Name) ? seat.Token.Substring(0, 4).ToUpperInvariant() : seat.Name;

    private static string Role(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        return empire == null ? "king" : empire.CoreKingdom == kingdom ? "emperor" : "member";
    }

    // ───────── 奏报 ─────────
    private static int MonthNow() => (int)(World.world.getCurWorldTime() / 5.0);

    // 世界日志每来一条都过这里(ScreenFeedPatch.BeforeNewHistory)：各席位只收与本国、已知之国有关的，再按吏治定迟几个月、丢不丢
    public static void Feed(WorldLogMessage message)
    {
        if (!Enabled || !_anyRuler || message == null) return;
        try
        {
            string id = message.asset_id ?? "";
            bool modEvent = id == EmpireCraftWorldLogLibrary.empirecraft_event_log?.id;
            Kingdom about = modEvent ? ScreenFeedPatch.PendingKingdom ?? message.kingdom : message.kingdom;
            bool major = modEvent
                ? ScreenFeedPatch.Pending >= ScreenFeedPatch.Level.History
                : id.Contains("war") || id.StartsWith("king_", StringComparison.Ordinal) ||
                  id.StartsWith("worldlog_disaster_", StringComparison.Ordinal) || id.Contains("emperor") || id.Contains("empire");
            string text = TerminalIntel.Strip(message.GetEmpireHistoryFormatedText());
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (Seat seat in SeatByToken.Values)
            {
                if (seat.Ruler == null || seat.Realm?.data == null) continue;
                string realmName = seat.Realm.data.name;
                bool own = about == seat.Realm || !string.IsNullOrEmpty(realmName) && text.Contains(realmName);
                if (!own)
                {
                    bool known = about != null && seat.Known.Contains(about.getID());
                    for (int i = 0; !known && i < seat.KnownNames.Count; i++) known = text.Contains(seat.KnownNames[i]);
                    if (!known) continue;
                }
                Add(seat, text, own, major, "event");
            }
        }
        catch (Exception)
        {
            // 这条读不出来就不报
        }
    }

    private static void Add(Seat seat, string text, bool own, bool major, string kind)
    {
        long id = ++_seq;
        uint seed = TerminalIntel.Hash(id, seat.Realm.getID(), MonthNow());
        if (TerminalIntel.Lost(seat.Corruption, major, seat.Sight, seed))
        {
            seat.Lost++;
            return;
        }
        int late = TerminalIntel.DelayMonths(seat.Corruption, own, major, seat.Sight, seed);
        if (seat.Pending.Count >= MaxPending)
        {
            seat.Pending.RemoveAt(0);
            seat.Lost++;
        }
        seat.Pending.Add(new Report
        {
            Id = id, Text = text, Kind = major ? "major" : kind, At = World.world.getCurWorldTime(), DueMonth = MonthNow() + late,
            Late = late, Own = own
        });
    }

    private static void Deliver(Seat seat)
    {
        if (seat.Pending.Count == 0) return;
        int month = MonthNow();
        bool any = false;
        for (int i = 0; i < seat.Pending.Count;)
        {
            if (seat.Pending[i].DueMonth > month)
            {
                i++;
                continue;
            }
            seat.Reports.Add(seat.Pending[i]);
            seat.Pending.RemoveAt(i);
            any = true;
        }
        if (seat.Reports.Count > MaxReports) seat.Reports.RemoveRange(0, seat.Reports.Count - MaxReports);
        if (any) seat.NextState = 0f;
    }

    // 岁计：每年一条，按报上来的数(不是真数)说库银、丁口、兵额比去年多了少了——游戏里没有这条，是终端给统治者看的账
    private static void Ledger(Seat seat)
    {
        int year = Date.getCurrentYear();
        if (year == seat.LedgerYear) return;
        long treasury = Figure(seat, seat.Realm.GetTreasuryBalance(), 1, 1, out _),
            people = Figure(seat, seat.Realm.getPopulationPeople(), -1, 2, out _),
            army = Figure(seat, seat.Realm.countTotalWarriors(), 1, 3, out _);
        if (seat.LedgerYear != int.MinValue)
            Add(seat, string.Format(LM.Get("terminal_ledger"), treasury, Signed(treasury - seat.LedgerTreasury), people,
                Signed(people - seat.LedgerPeople), army, Signed(army - seat.LedgerArmy)), true, false, "ledger");
        seat.LedgerYear = year;
        seat.LedgerTreasury = treasury;
        seat.LedgerPeople = people;
        seat.LedgerArmy = army;
    }

    private static string Signed(long value) => value > 0 ? "+" + value : value.ToString();

    private static long Figure(Seat seat, long truth, int bias, int which, out bool vague) =>
        TerminalIntel.Reported(truth, seat.Corruption, bias, TerminalIntel.Hash(seat.Realm.getID(), which, Date.getCurrentYear()),
            out vague);

    // ───────── 状态 ─────────
    private static JObject BuildState(Seat seat)
    {
        var state = new JObject
        {
            ["loaded"] = true, ["lang"] = LocalizedTextManager.instance?.language ?? "en", ["seat"] = SeatLabel(seat),
            ["year"] = Date.getCurrentYear(), ["month"] = Date.getCurrentMonth(), ["speed"] = SpeedPercent,
            ["paused"] = global::Config.paused
        };
        if (seat.Ruler == null)
        {
            state["vacant"] = true;
            state["reason"] = seat.VacantReason;
            state["former"] = new JObject { ["ruler"] = seat.FormerRuler, ["realm"] = seat.FormerRealm };
            var rulers = new JArray();
            foreach (RulerInfo info in Rulers())
                rulers.Add(new JObject
                {
                    ["id"] = info.Id.ToString(), ["ruler"] = info.Ruler, ["realm"] = info.Realm, ["kind"] = info.Kind,
                    ["cities"] = info.Cities, ["age"] = info.Age, ["taken"] = info.TakenBy
                });
            state["rulers"] = rulers;
            return state;
        }
        Kingdom realm = seat.Realm;
        Empire empire = realm.GetEmpire();
        state["vacant"] = false;
        state["ruler"] = new JObject { ["name"] = seat.Ruler.getName(), ["age"] = seat.Ruler.getAge() };
        state["realm"] = new JObject
        {
            ["name"] = realm.data.name, ["color"] = Hex(RealmMiniMap.KingdomColor(realm)), ["role"] = Role(realm),
            ["empire"] = empire?.data?.name, ["era"] = TechnologySystem.GetEraName(TechnologySystem.GetCultureOf(realm)),
            ["sight"] = (int)seat.Sight, ["cities"] = realm.cities?.Count ?? 0
        };
        state["intel"] = new JObject
        {
            ["corruption"] = Math.Round(seat.Corruption, 2), ["trust"] = TerminalIntel.Trust(seat.Corruption), ["lost"] = seat.Lost,
            ["pending"] = seat.Pending.Count
        };
        state["figures"] = new JObject
        {
            ["treasury"] = FigureJson(seat, realm.GetTreasuryBalance(), 1, 1),
            ["people"] = FigureJson(seat, realm.getPopulationPeople(), -1, 2),
            ["army"] = FigureJson(seat, realm.countTotalWarriors(), 1, 3)
        };
        state["wars"] = Wars(realm);
        state["cities"] = Cities(seat);
        var reports = new JArray();
        for (int i = seat.Reports.Count - 1; i >= 0 && reports.Count < 80; i--)
        {
            Report report = seat.Reports[i];
            reports.Add(new JObject
            {
                ["id"] = report.Id, ["text"] = report.Text, ["kind"] = report.Kind, ["own"] = report.Own, ["late"] = report.Late,
                ["year"] = Date.getYear(report.At), ["month"] = Date.getMonth(report.At)
            });
        }
        state["reports"] = reports;
        return state;
    }

    private static JObject FigureJson(Seat seat, long truth, int bias, int which)
    {
        long value = Figure(seat, truth, bias, which, out bool vague);
        return new JObject { ["v"] = value, ["vague"] = vague };
    }

    private static JArray Wars(Kingdom realm)
    {
        var wars = new JArray();
        foreach (War war in realm.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            bool attacker = war.isAttacker(realm);
            Kingdom enemy = attacker ? war.main_defender : war.main_attacker;
            wars.Add(new JObject
            {
                ["enemy"] = enemy?.data?.name ?? "", ["attacker"] = attacker, ["months"] = Date.getMonthsSince(war.data.created_time),
                ["color"] = enemy == null ? null : Hex(RealmMiniMap.KingdomColor(enemy))
            });
        }
        return wars;
    }

    // 各城：行政控制力够的给数(仍照吏治虚报)，差一档只有约数，再差不详——皇权不下乡
    private static JArray Cities(Seat seat)
    {
        Kingdom realm = seat.Realm;
        var cities = new JArray();
        if (realm.cities == null) return cities;
        foreach (City city in realm.cities.Where(city => city?.data != null && !city.isRekt())
                     .OrderByDescending(city => city == realm.capital).ThenByDescending(city => city.getPopulationPeople()).Take(MaxCities))
        {
            float control = city == realm.capital || !AdministrativeControlSystem.Known(city) ? 1f : AdministrativeControlSystem.Get(city);
            int clarity = TerminalIntel.Clarity(control);
            long people = TerminalIntel.Reported(city.getPopulationPeople(), seat.Corruption, -1,
                TerminalIntel.Hash(city.getID(), 2, Date.getCurrentYear()), out _);
            var row = new JObject
            {
                ["name"] = city.data.name, ["capital"] = city == realm.capital, ["control"] = Math.Round(control, 2), ["clarity"] = clarity
            };
            if (clarity < 2) row["people"] = clarity == 0 ? people : TerminalIntel.RoundSig(people, 1);
            if (clarity == 0) row["villages"] = RuralSettlementSystem.InCity(city)?.Count ?? 0;
            cities.Add(row);
        }
        return cities;
    }

    private static string Hex(Color32 color) => "#" + color.r.ToString("x2") + color.g.ToString("x2") + color.b.ToString("x2");

    // ───────── 地图与眼界 ─────────
    // 从天下简图(RealmMiniMap)再缩一档取样；谁看得见按眼界定：本国、同一帝国、交战国、接壤(隔 BorderGap 格水或无主地也算)的都知道，
    // 眼界到"通达"档时使节到得了的也知道、通达层里有一点往来的算传闻；看得见的国四周 FogRadius 格以内的山川也看得见，其余是迷雾
    private static void BuildMap(Seat seat)
    {
        Kingdom realm = seat.Realm;
        RealmMiniMap.MapData map = RealmMiniMap.Snapshot();
        int factor = Mathf.Max(1, Mathf.CeilToInt(map.Width / (float)MapWidth));
        int width = Mathf.Max(1, map.Width / factor), height = Mathf.Max(1, map.Height / factor);
        var owner = new int[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sx = Math.Min(map.Width - 1, x * factor + factor / 2), sy = Math.Min(map.Height - 1, y * factor + factor / 2);
            owner[y * width + x] = map.Owner[sy * map.Width + sx];
        }
        int self = map.KingdomIndex.TryGetValue(realm.getID(), out int found) ? found : -1;
        HashSet<int> neighbours = Neighbours(owner, width, height, self);

        // 各国几等：0 本国、1 已知、2 传闻、3 不知
        var tier = new int[map.Kingdoms.Count];
        Empire empire = realm.GetEmpire();
        var enemies = new HashSet<Kingdom>();
        foreach (War war in realm.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            enemies.Add(war.main_attacker);
            enemies.Add(war.main_defender);
        }
        seat.Known.Clear();
        seat.KnownNames.Clear();
        for (int i = 0; i < map.Kingdoms.Count; i++)
        {
            Kingdom kingdom = map.Kingdoms[i];
            if (i == self) tier[i] = 0;
            else if (kingdom?.data == null || kingdom.isRekt()) tier[i] = 3;
            else if (seat.Sight == TerminalIntel.Sight.World || neighbours.Contains(i) || enemies.Contains(kingdom) ||
                     empire != null && kingdom.GetEmpire() == empire) tier[i] = 1;
            else if (seat.Sight == TerminalIntel.Sight.Reach)
                tier[i] = ConnectivitySystem.CanContact(realm, kingdom) == true ? 1
                    : ConnectivitySystem.ContactStrength(realm, kingdom) > 0f ? 2 : 3;
            else tier[i] = 3;
            if (tier[i] != 1) continue;
            seat.Known.Add(kingdom.getID());
            if (!string.IsNullOrEmpty(kingdom.data.name)) seat.KnownNames.Add(kingdom.data.name);
        }

        // 看得见的国重新编号(行 0 在上：游戏里 y 朝上，这里翻过来)
        var slot = new int[map.Kingdoms.Count];
        var realms = new JArray();
        var sumX = new List<float>();
        var sumY = new List<float>();
        var count = new List<int>();
        for (int i = 0; i < slot.Length; i++)
        {
            slot[i] = -1;
            if (tier[i] > 2 || realms.Count >= TerminalIntel.MaxRealms) continue;
            slot[i] = realms.Count;
            realms.Add(new JObject
            {
                ["name"] = map.Kingdoms[i].data.name, ["color"] = Hex(RealmMiniMap.KingdomColor(map.Kingdoms[i])), ["tier"] = tier[i]
            });
            sumX.Add(0f);
            sumY.Add(0f);
            count.Add(0);
        }
        var cells = new byte[width * height];
        bool[] seed = new bool[cells.Length], keep = new bool[cells.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int from = y * width + x, to = (height - 1 - y) * width + x, who = owner[from];
            if (who == -2) cells[to] = TerminalIntel.Water;
            else if (who < 0 || slot[who] < 0) cells[to] = TerminalIntel.Wild;
            else
            {
                int index = slot[who];
                cells[to] = (byte)index;
                seed[to] = tier[who] <= 1;
                keep[to] = tier[who] == 2;
                sumX[index] += x;
                sumY[index] += height - 1 - y;
                count[index]++;
            }
        }
        if (seat.Sight != TerminalIntel.Sight.World) TerminalIntel.ApplyFog(cells, seed, keep, width, height, FogRadius);
        for (int i = 0; i < realms.Count; i++)
        {
            realms[i]["n"] = count[i];
            if (count[i] == 0) continue;
            realms[i]["x"] = Math.Round(sumX[i] / count[i], 1);
            realms[i]["y"] = Math.Round(sumY[i] / count[i], 1);
        }
        var json = new JObject
        {
            ["ready"] = true, ["w"] = width, ["h"] = height, ["sight"] = (int)seat.Sight,
            ["cells"] = Convert.ToBase64String(TerminalIntel.Rle(cells)), ["realms"] = realms
        };
        City capital = realm.capital;
        if (capital?.data != null && !capital.isRekt())
        {
            // 整张图的快照从地图左下角取起，都城的地块坐标直接换成格
            float cx = capital.city_center.x / map.Step / factor, cy = capital.city_center.y / map.Step / factor;
            json["capital"] = new JArray(Math.Round(cx, 1), Math.Round(height - 1 - cy, 1));
        }
        seat.MapJson = json.ToString(Newtonsoft.Json.Formatting.None);
    }

    // 本国的格往外走，隔着水、无主地至多 BorderGap 格碰到的国
    private static HashSet<int> Neighbours(int[] owner, int width, int height, int self)
    {
        var neighbours = new HashSet<int>();
        if (self < 0) return neighbours;
        var distance = new int[owner.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < owner.Length; i++)
        {
            distance[i] = owner[i] == self ? 0 : -1;
            if (owner[i] == self) queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int at = queue.Dequeue(), d = distance[at], x = at % width, y = at / width;
            if (x > 0) Visit(at - 1);
            if (x + 1 < width) Visit(at + 1);
            if (y > 0) Visit(at - width);
            if (y + 1 < height) Visit(at + width);

            void Visit(int next)
            {
                if (distance[next] >= 0) return;
                int who = owner[next];
                if (who >= 0)
                {
                    neighbours.Add(who);
                    return;
                }
                if (d >= BorderGap) return;
                distance[next] = d + 1;
                queue.Enqueue(next);
            }
        }
        return neighbours;
    }
}
