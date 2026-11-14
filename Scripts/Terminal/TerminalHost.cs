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
//   · 有席位领着君主、手机也还连着的时候世界走得慢(设置里的终端流速，默认一成；"终端模式下地图的事件流速会变慢")，消息也就细；
//     手机都断了(ClientSeconds 秒没来取过)世界照常速走，再连上又放慢——以前只看领没领君主，手机一断线世界就一直拖着一成的流速
//     (用户 2026-11-12 "我更新终端后变卡了""游戏整体变卡了"，选"断线就恢复全速")；
//   · 这一期只读：奏报、地图、国势。下命令(外交、批奏、立储、改元之类，只下简单的令、剩下让下面的人去办)是下一期。
// 线程：游戏对象只在主线程碰。Tick 每秒把各席位的状态拼成字符串放着，网络线程只读这些字符串、往 Commands 里放要办的事。
// 手机版游戏上不启用(终端就是给电脑上的游戏配的)
public static partial class TerminalHost
{
    public static bool Enabled;
    // 终端这一头的版本：手机网页每次打开都读磁盘上最新的文件，游戏里跑的却是启动时编译的那一份。
    // 网页要的版本比这个高，就提示重启游戏(用户 2026-11-11 进游戏后："很多政令我点了都没有回应"——新按钮发给了旧服务)。
    // 加了网页用得着的新命令、新字段就加一
    public const int Version = 17;
    public static int Port = 8765;
    // 有席位领着君主时世界流速的百分比
    public static int SpeedPercent = 10;

    private const float StateSeconds = 2f, SightSeconds = 15f, IdleSightSeconds = 60f, ClientSeconds = 30f, RetrySeconds = 30f;
    private const int MaxReports = 200, MaxPending = 500, MaxCities = 40, MaxRulers = 120;
    // 眼界的远近按地块(TileZone，8×8 格)算：隔着水、无主地至多 BorderGap 块算接壤，看得见的地往外 FogRadius 块以内的山川也看得见
    private const int BorderGap = 2, FogRadius = 3;

    internal sealed class Report
    {
        public long Id;
        public string Text, Kind;
        public double At;
        public int DueMonth, Late;
        public bool Own;
        // 这道奏报说的是哪座城(岁入见少的那座，手机上点它可以遣官彻查，见 TerminalHost.Audit)
        public long City;
    }

    // 一部配过对的手机
    public sealed class Seat
    {
        public string Token, Name;
        public long RulerId = -1;
        public Actor Ruler;
        public Kingdom Realm;
        internal string VacantReason = "none", FormerRuler = "", FormerRealm = "";
        // 流官(见 Resolve)：上一任是不是朝廷派的(不是世袭)，离任后等改派从哪个月起等
        internal bool Appointed;
        internal int AwaitSince = int.MinValue;
        internal double Corruption;
        internal TerminalIntel.Sight Sight;
        // 上回算的眼界：已知之国
        internal readonly HashSet<long> Known = new();
        internal readonly List<string> KnownNames = new();
        internal readonly List<Report> Reports = new(), Pending = new();
        internal int Lost, LedgerYear = int.MinValue;
        // 衣冠(见 TerminalHost.Wardrobe)：买来的("哪一样:名字")、眼下穿着的(哪一样 → 名字)
        internal readonly HashSet<string> Wardrobe = new();
        internal readonly Dictionary<string, string> Wear = new();
        // 考课与弹章(见 TerminalHost.Review)：近三年的弹章、上回是哪一季看的、各官头一回见到是哪个月、
        // 各城上缴与积弊的两年之数(上年、前年)、查过的记号
        internal readonly List<Charge> Charges = new();
        internal int ChargeQuarter = int.MinValue;
        internal readonly Dictionary<long, int> Tenure = new();
        internal readonly Dictionary<long, (long last, long prev)> RemitYears = new();
        internal readonly Dictionary<long, (double last, double prev)> CorrYears = new();
        internal readonly Dictionary<long, (int at, string kind)> Marks = new();
        // 查账(见 TerminalHost.Books)：封存着的账册、哪个月起才能再查；哪些城的账眼下照实报(查账问责过、彻查查实过，记的是哪个月)
        internal BookCase Books;
        internal int BooksNext;
        internal readonly Dictionary<long, int> TrueBooks = new();
        // 半年财报(见 TerminalHost.Fiscal)：最近几份(新的在前)、上回是哪半年出的
        internal readonly List<JObject> Fiscals = new();
        internal int FiscalHalf = int.MinValue;
        // 上年各城的岁入(每年岁计时记一回，见 TaxWatch)
        internal readonly Dictionary<long, long> CityIncome = new();
        internal long LedgerTreasury, LedgerPeople, LedgerArmy;
        internal volatile string StateJson, MapJson;
        // 第二期(见 TerminalHost.Decrees.cs)：下了还没办结的命令、待批的案子、等着批红的奏议
        internal readonly List<Order> Orders = new();
        internal readonly List<Case> Cases = new();
        internal readonly Dictionary<long, int> Judged = new();
        internal string HoldMark;
        internal bool Abdicated;
        // 委首辅代批(奏议、案子都不等手机)、这件奏议上用过的筹码
        internal bool Delegated;
        // 受理过几道命令(下了令、当场办了、回了话都算；一道命令进来这个数没动就是没受理)
        internal int Acks;
        internal int DelegateMonth;
        internal readonly HashSet<string> Chips = new();
        // 暗探(见 TerminalHost.Intrigue.cs)：派出去的、拿获别人的、各国已探到的几项、彻查到哪个月；传闻之国也派得了暗探
        // 别的席位递来、等着裁的外交提议(见 TerminalHost.Scale.cs)
        internal readonly List<Offer> Offers = new();
        // 君主在外(见 TerminalHost.Journey.cs)：campaign 亲征、tour 私访；私访看过的城(哪个月看的)；朝廷还不该知道的叛军
        internal string Away;
        internal long AwayCity;
        internal int AwayUntil, AwayRolled;
        internal readonly Dictionary<long, int> Toured = new();
        internal readonly HashSet<long> Hidden = new();
        internal readonly List<string> HiddenNames = new();
        internal readonly List<Spy> Spies = new();
        internal readonly List<CaughtSpy> Caught = new();
        internal readonly Dictionary<long, Dictionary<string, string>> Dossiers = new();
        internal readonly HashSet<long> Rumoured = new();
        internal int SweepUntil;
        internal float HoldSince, NextDecrees;
        internal JObject DecreesCache;
        // 可议之事(见 TerminalHost.DecisionScale)：每 DecreeSeconds 秒重算一回
        internal JArray DecisionOptions;
        internal float NextDecisionOptions;
        internal long LastClientTicks;
        internal float NextState, NextSight;
        // 舆图(见 TerminalHost.Field.cs)：上回备的总图(格、各国在图上的编号与几等)、手机要的细节那一片、
        // 上回报来的军情(哪个月的)、还在路上的手令
        internal byte[] MapCells;
        internal int MapW, MapH, MapTile = 1;
        internal readonly Dictionary<long, int> Slots = new();
        internal int[] SlotTier = new int[0];
        // 图上哪些编号是本朝的行政区、羁縻、封国(帝国之主才有，见 BuildMap)
        internal bool[] SlotProv = new bool[0];
        internal volatile string ArmyJson;
        // 探路的人走过、买来的舆图上画着的地方(总图的格，见 TerminalHost.Survey)：不在眼界里也看得见
        internal readonly HashSet<int> Charted = new();
        internal int ChartedW, ChartedH;
        // 总图：上回发的那一份(不带 stamp)、第几版；地貌：眼下该是哪一版(MapTs)、备好的是哪一版、各片、手机来要过没有
        internal string MapBody;
        internal volatile int MapStamp, MapTs, TerrainTs;
        internal volatile string[] TerrainParts;
        // 上回发给这部手机的地貌(抹过迷雾的)是哪一版；手机报上来它手上是哪一版；备好的这几片是补在哪一版上的(0 = 整份)
        internal byte[] TerrainBase;
        internal int TerrainBaseTs;
        internal volatile int TerrainHave, TerrainFrom;
        internal volatile bool TerrainWant;
        internal JArray ArmySeen;
        internal string ArmySeenText;
        internal int ArmyMonth, ArmyStamp;
        internal float NextArmy;
        internal bool ArmyDirty;
        internal readonly List<Writ> Writs = new();
        // 给哪些行政区的军下过手令(席位腾空时一并撤掉)
        internal readonly HashSet<long> HostOrdered = new();

        public bool Watched => DateTime.UtcNow.Ticks - Interlocked.Read(ref LastClientTicks) < TimeSpan.FromSeconds(ClientSeconds).Ticks;

        internal void ClearIntel()
        {
            Reports.Clear();
            Pending.Clear();
            Known.Clear();
            KnownNames.Clear();
            Lost = 0;
            Orders.Clear();
            Chips.Clear();
            Spies.Clear();
            Offers.Clear();
            Away = null;
            Toured.Clear();
            Hidden.Clear();
            HiddenNames.Clear();
            Caught.Clear();
            Dossiers.Clear();
            Rumoured.Clear();
            SweepUntil = 0;
            Delegated = false;
            Cases.Clear();
            Judged.Clear();
            HoldMark = null;
            DecreesCache = null;
            NextDecrees = 0f;
            LedgerYear = int.MinValue;
            CityIncome.Clear();
            Fiscals.Clear();
            Books = null;
            BooksNext = 0;
            Wardrobe.Clear();
            Wear.Clear();
            Charges.Clear();
            ChargeQuarter = int.MinValue;
            Tenure.Clear();
            RemitYears.Clear();
            CorrYears.Clear();
            Marks.Clear();
            TrueBooks.Clear();
            FiscalHalf = int.MinValue;
            NextSight = NextState = 0f;
            MapJson = null;
            MapCells = null;
            Slots.Clear();
            ArmyJson = MapBody = null;
            Charted.Clear();
            TerrainParts = null;
            TerrainBase = null;
            TerrainWant = false;
            ArmySeen = null;
            ArmyDirty = false;
            Writs.Clear();
        }

        internal void Vacate(string reason)
        {
            // 君主不在位了：点名调过的军交还朝廷自己调度(见 CorpsDirectOrders)
            if (Realm != null) CorpsDirectOrders.ClearKingdom(Realm);
            foreach (long hostId in Writs.Where(writ => writ.Kingdom != 0).Select(writ => writ.Kingdom).Distinct().ToList())
                CorpsDirectOrders.ClearKingdom(World.world?.kingdoms?.get(hostId));
            foreach (long hostId in HostOrdered) CorpsDirectOrders.ClearKingdom(World.world?.kingdoms?.get(hostId));
            HostOrdered.Clear();
            RulerId = -1;
            Ruler = null;
            Realm = null;
            VacantReason = reason;
            ClearIntel();
        }
    }

    private static TerminalServer _server;
    // 局域网里的固定名字(empirecraft.local)，见 TerminalMdns
    private static TerminalMdns _mdns;
    private static readonly TerminalPairing Pairing = new();
    private static readonly ConcurrentDictionary<string, Seat> SeatByToken = new();
    private static readonly ConcurrentQueue<Action> Commands = new();
    private static string _pagePath;
    private static byte[] _page;
    private static DateTime _pageStamp;
    private static float _nextTick, _retryAt;
    private static long _seq;
    private static volatile bool _anyRuler;
    // 领着君主的席位里有没有手机还连着的(定世界放不放慢，见 SpeedFactor)
    private static volatile bool _anyWatching;

    public static bool Running => _server?.Running == true;
    public static bool Lan => _server?.Lan == true;
    public static string Error => _server?.Error;
    public static string Code => Pairing.Code;
    // 不随电脑的地址变的那个地址；FixedReady 为真是正在局域网里应答这个名字
    public static string FixedAddress => TerminalMdns.FullName + ":" + Port;
    public static bool FixedReady => _mdns?.Running == true && _mdns.Listening > 0;

    // 世界流速的倍数(见 TerminalPatch)：有席位领着君主、手机还连着时放慢；手机都断线了照常速走
    public static float SpeedFactor => Enabled && _anyWatching ? Mathf.Clamp(SpeedPercent, 1, 100) / 100f : 1f;
    // 世界眼下是不是按终端流速走着(终端窗口显示用)
    public static bool Slowed => Enabled && _anyWatching;
    // 终端模式开着、世界在跑：山川水陆不许改(用户 2026-11-12 "服务端终端模式开启后禁止地形的变更")。
    // 手机上的山川是整份取下来的，各位君主也是照着这张图调兵的；见 TerminalPatch.BeforeTerraform
    public static bool TerrainLocked => Enabled && WorldReady();

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
    // 接着联机大厅时用大厅里本机的房间地址(用户 2026-11-11 "二维码能扫描后直接进大厅的房间""不需要手动输入配对码")：
    // 手机在哪个网络都扫得进来，地址里带着配对码，网页打开就自己配上；没接大厅才用外网地址、局域网地址
    public static string PairUrl() =>
        (LobbyOnline ? RoomUrl : PublicUrl.Length > 0 ? PublicUrl : "http://" + PrimaryAddress()) + "/?c=" + Code;

    // 外网地址(用户 2026-11-11 "那还是考虑第三方的吧")：模组只在本机开端口；不在同一个网络时，由第三方隧道工具(组网、内网穿透)
    // 把这个端口引出去，它给一个固定网址。把网址填在终端窗口里，二维码、手机桌面图标就都用它，手机不用知道电脑的地址
    // 填的是什么就存什么(PublicText)，用的时候再补全(PublicUrl)：只填了组网地址 100.x.x.x 的，补上 http:// 和端口
    // (用户 2026-11-11 填了组网工具给的地址、没带 http://，被整条清掉，二维码还是局域网地址)。端口后来改了也跟着走
    public static string PublicText = "";

    public static string PublicUrl => TerminalIntel.OutsideUrl(PublicText, Port);

    // 填了、但不成个地址(窗口里要说一声，不能悄悄当没填)
    public static bool PublicBad => (PublicText ?? "").Trim().Length > 0 && PublicUrl.Length == 0;

    // 组网工具给本机的地址(100.64.0.0/10 这一段是组网工具专用的)：有的话窗口里给个按钮，一点就拿它作外网地址
    public static string MeshAddress()
    {
        foreach (string address in Addresses())
        {
            string host = address.Substring(0, address.LastIndexOf(':'));
            if (TerminalIntel.IsMesh(host)) return host;
        }
        return null;
    }

    // 窗口上显示的东西变了没有(配对码、席位、谁领着谁、在不在线)：变了窗口自己重画
    private static string _signature;

    private static void NotifyWindows()
    {
        var signature = new global::System.Text.StringBuilder();
        signature.Append(Running ? Code : "-").Append(Lan ? 'L' : 'l').Append(Port).Append(LobbyState).Append(RoomOpen ? 'O' : 'o');
        signature.Append(SpeedPercent).Append(VoteLine() ?? "").Append(TerminalPairing.Limit).Append(AdminLine());
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

    // 这一国真有电报：科技树对它的文化生效、又采纳了电报。科技树没开时 TechnologySystem.RealmHasTech 一律回"有"(别处拿它当
    // "不设限"用)，眼界不能照这个算——用户 2026-11-11 "还有我的迷雾呢""怎么我进了一个存档全世界都是亮的"：
    // 没开科技树的存档里人人都算有电报，地图全亮、各国全知。没开科技树时按没有电报、时代第一档算(只知接壤)
    internal static bool HasTelegraph(Kingdom realm) =>
        realm != null && TechnologySystem.IsActiveFor(TechnologySystem.GetCultureOf(realm)) && TechnologySystem.RealmHasTech(realm, "telegraph");

    // 除名(房主踢人，用户 2026-11-11 "房主可以踢出成员")：这部手机要重新配对才进得来。
    // 配对码当场换掉；接着联机大厅、房间又是公开的，同时改为凭配对码——不然被除名的人从大厅点一下又进来了
    public static void Remove(Seat seat)
    {
        if (seat == null) return;
        Pairing.Remove(seat.Token);
        SeatByToken.TryRemove(seat.Token, out _);
        DropBallot(seat.Token);
        Pairing.NewCodeNow();
        if (LobbyEnabled && RoomOpen) EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.SetSwitch("terminal_room_open_toggle", false);
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
            ForgetLegacy(seat);
            seat.StateJson = null;
        }
        RulerPacts.Clear();
        RebellionNoticed.Clear();
        _campaigning = Array.Empty<Kingdom>();
        _anyRuler = _anyWatching = false;
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
                _mdns?.Stop();
                _anyRuler = _anyWatching = false;
            }
            StopLobby();
            return;
        }
        float now = Time.realtimeSinceStartup;
        EnsureServer(now);
        // 席位上的君主由玩家亲自发起决议，天平不替他办(用户 2026-11-15 选"不代办，只给群臣建议")
        EmpireCraft.Scripts.GeneralSystems.Decisions.DecisionSystem.Steered ??= SteeredBySeat;
        // 联机大厅(见 TerminalHost.Lobby)：端口开着才连
        TickLobby(now);
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
        RunTerrain(now);
        RunAnnals(now);
        if (now < _nextTick) return;
        _nextTick = now + 1f;
        bool ready = WorldReady();
        bool any = false, watching = false, mapped = false;
        foreach (Seat seat in SeatByToken.Values)
        {
            if (!ready)
            {
                seat.StateJson = "{\"loaded\":false}";
                seat.MapJson = seat.MapBody = null;
                seat.TerrainParts = null;
                seat.TerrainBase = null;
                // 换了世界：地貌下回重取
                _terrain = null;
                continue;
            }
            try
            {
                Resolve(seat);
                if (seat.Ruler != null)
                {
                    any = true;
                    if (seat.Watched) watching = true;
                    seat.Corruption = CorruptionSystem.GetRate(seat.Realm);
                    seat.Sight = TerminalIntel.SightOf(EmpireLawSystem.GetEraTier(seat.Realm), HasTelegraph(seat.Realm));
                    Ledger(seat);
                    RunFiscal(seat);
                    RunBooks(seat);
                    RunReview(seat);
                    Deliver(seat);
                    RunOrders(seat);
                    RunCases(seat);
                    RunSpies(seat);
                    RunOffers(seat);
                    RunJourney(seat);
                    RunWrits(seat);
                }
                bool watched = seat.Watched;
                // 地图取样费些工夫：一秒里至多给一个席位算
                // 没君主的席位也给一张全图：在图上点国选人(用户 2026-11-12 "选人界面可不可以直接在地图选, 不要给列表")
                // 同时在线制下(独立服务器)账号可以很多：离线的席位不算图，回来了再算
                if (!mapped && now >= seat.NextSight && (watched || !TerminalPairing.ConcurrentMode))
                {
                    seat.NextSight = now + (watched ? SightSeconds : IdleSightSeconds);
                    BuildMap(seat);
                    mapped = true;
                }
                if (seat.Ruler != null) RunArmies(seat, now);
                if (now >= seat.NextState && (watched || seat.StateJson == null))
                {
                    seat.NextState = now + StateSeconds;
                    JObject built = BuildState(seat);
                    // 流速之议(见 TerminalHost.Speed)：各席位都看得到
                    AddSpeed(built, seat);
                    // 军情有没有新的(见 TerminalHost.Field)：手机见这个数变了才去取
                    built["armyStamp"] = seat.ArmyStamp;
                    // 总图有没有新的(见 BuildMap)：同上
                    built["mapStamp"] = seat.MapStamp;
                    built["monthNo"] = MonthNow();
                    seat.StateJson = built.ToString(Newtonsoft.Json.Formatting.None);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][终端] 备状态出错: {exception.Message}");
            }
        }
        // 管理员看的全图、军情、房间(见 TerminalHost.Admin)
        RunAdmins(ready, now, ref mapped);
        _anyRuler = any;
        _anyWatching = watching;
        if (ready) RunPacts();
        if (ready) RunSpeedVote(now);
        if (ready) RunRebellions();
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
        // 管理员码与已登入的管理员(见 TerminalHost.Admin)
        LoadAdmins();
        if (_server.Start(Port))
        {
            LogService.LogInfo($"[EmpireCraft][终端] 已开在 {Port} 端口" + (_server.Lan ? "" : "(只听本机)"));
            if (_server.Lan)
            {
                _mdns ??= new TerminalMdns();
                _mdns.Start();
            }
        }
        else
            LogService.LogWarning($"[EmpireCraft][终端] {Port} 端口开不了: {_server.Error}");
    }

    // ───────── 网络线程 ─────────
    private static TerminalServer.Reply Handle(string method, string path, string token, string body)
    {
        // 独立服务器自己的接口(账号、上传剧本、托管编辑器网页)先过一遍；它不管的照常往下走(见 TerminalHost.Standalone)
        TerminalServer.Reply? extra = ServerRoutes?.Invoke(method, path, token, body);
        if (extra.HasValue) return extra.Value;
        if (method == "GET" && (path == "/" || path == "/index.html"))
        {
            byte[] page = Page();
            return page == null ? TerminalServer.Reply.Error(404, "no_page") : TerminalServer.Reply.Html(page);
        }
        // 添加到手机桌面的资源和按内容命名的高清立绘分片。
        if (method == "GET" && (StaticTypes.TryGetValue(path, out string staticType) || TerminalServer.IsPortraitPage(path)))
        {
            byte[] file = StaticFile(path.Substring(1));
            return file == null ? TerminalServer.Reply.Error(404, "not_found")
                : new TerminalServer.Reply { Status = 200, Type = staticType ?? "image/webp", Body = file };
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
        // 管理员(见 TerminalHost.Admin)：凭管理员码登入；令牌是管理员的，走管理员那一头
        if (method == "POST" && path == "/api/admin/pair") return AdminPair(body);
        if (!string.IsNullOrEmpty(token) && AdminByToken.TryGetValue(token, out Seat god)) return AdminHandle(god, method, path, body);
        if (string.IsNullOrEmpty(token) || !SeatByToken.TryGetValue(token, out Seat mine))
            return TerminalServer.Reply.Error(401, "unpaired");
        Interlocked.Exchange(ref mine.LastClientTicks, DateTime.UtcNow.Ticks);
        if (method == "GET" && path == "/api/state") return TerminalServer.Reply.Json(mine.StateJson ?? "{\"loaded\":false}");
        if (method == "GET" && path == "/api/map") return TerminalServer.Reply.Json(mine.MapJson ?? "{\"ready\":false}");
        if (method == "GET" && path == "/api/armies") return TerminalServer.Reply.Json(mine.ArmyJson ?? "{\"ready\":false}");
        if (method == "POST" && path == "/api/terrain") return TerrainReply(mine, body);
        if (method == "POST" && path == "/api/annals") return AnnalsReply(mine);
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
        if (method == "POST" && path == "/api/decree")
        {
            string id = Field(body, "id"), target = Field(body, "target"), choice = Field(body, "choice");
            if (string.IsNullOrEmpty(id) || id.Length > 80) return TerminalServer.Reply.Error(400, "bad_request");
            Commands.Enqueue(() => Issue(mine, id, target, choice));
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

    private static readonly Dictionary<string, string> StaticTypes = new()
    {
        ["/manifest.json"] = "application/manifest+json", ["/icon-192.png"] = "image/png", ["/icon-512.png"] = "image/png",
        ["/apple-touch-icon.png"] = "image/png",
        // 卡片的像素框(GameResources/ui 里那两张的副本，网页卡片用同一张图)
        ["/FactionFrame.png"] = "image/png", ["/FactionFrame_dominate.png"] = "image/png"
    };

    private static readonly ConcurrentDictionary<string, byte[]> StaticFiles = new();

    private static byte[] StaticFile(string name)
    {
        try
        {
            if (StaticFiles.TryGetValue(name, out byte[] cached)) return cached;
            string path = _pagePath == null ? null : Path.Combine(Path.GetDirectoryName(_pagePath) ?? "", name);
            if (path == null || !File.Exists(path)) return null;
            return StaticFiles[name] = File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    // 网页文件的版本(改的时间，秒)：随 state 发给手机，变了手机就重新载入网页，不接着跑旧代码。至多五秒看一回文件
    private static long _pageVersion;
    private static float _pageVersionAt = -100f;

    private static long PageVersion()
    {
        float now = Time.realtimeSinceStartup;
        if (now - _pageVersionAt < 5f) return _pageVersion;
        _pageVersionAt = now;
        try
        {
            if (_pagePath != null && File.Exists(_pagePath)) _pageVersion = File.GetLastWriteTimeUtc(_pagePath).Ticks / TimeSpan.TicksPerSecond;
        }
        catch (Exception)
        {
            // 读不到就照上回的
        }
        return _pageVersion;
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
        Kingdom ruling = RulingRealm(actor);
        if (ruling != null)
        {
            // 新到任(流官改派到别处也是)：地图、眼界重算
            if (seat.Ruler == null || seat.Realm != ruling) seat.NextSight = seat.NextState = 0f;
            seat.Ruler = actor;
            seat.Realm = ruling;
            seat.FormerRuler = actor.getName();
            seat.FormerRealm = NameOf(seat.Realm);
            seat.Appointed = !Hereditary(ruling);
            seat.AwaitSince = int.MinValue;
            // 身后可以接着领谁(见 TerminalHost.Succession)：在位时记下子孙、国、都城
            RememberLegacy(seat);
            return;
        }
        // 流官离任、人还在(任满改派、调任别处)：席位跟着这个人，一年之内派到哪里就跟到哪里
        // (用户 2026-11-14 "流官换执政区后玩家的视角也会换到那个行政区")；这期间也可以另择一君
        bool alive = actor != null && actor.isAlive();
        if (alive && seat.Appointed)
        {
            int now = MonthNow();
            if (seat.AwaitSince == int.MinValue) seat.AwaitSince = now;
            if (now - seat.AwaitSince < TransferMonths)
            {
                if (seat.Ruler != null || seat.VacantReason != "transfer")
                {
                    long keep = seat.RulerId;
                    seat.Vacate("transfer");
                    seat.RulerId = keep;
                }
                return;
            }
        }
        seat.AwaitSince = int.MinValue;
        // 驾崩、失位、亡国：席位不撤，等着再挑一位(用户："死后终端也不结束，可以再选地图上存活的任意君主")
        seat.Vacate(!alive ? "dead" : seat.Realm == null || seat.Realm.isRekt() ? "fallen" : "deposed");
    }

    // 流官离任后等改派至多几个月
    private const int TransferMonths = 12;

    // 这人眼下在哪一国为君(流官改派时人还记在旧任上的，也认新任)
    private static Kingdom RulingRealm(Actor actor)
    {
        if (actor == null || !actor.isAlive()) return null;
        if (Eligible(actor)) return actor.kingdom;
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && kingdom.isAlive() && !kingdom.wild && kingdom.king == actor) return kingdom;
        return null;
    }

    public struct RulerInfo
    {
        public long Id, KingdomId;
        public string Ruler, Realm, Kind, TakenBy;
        // 归在哪个天下、哪个帝国之下(独立的国 Empire 为空；不在任何天下里 Tianxia 为空)
        public string Tianxia, Empire;
        internal int GroupCities, WorldCities;
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
            // 天下按都城所在的帝国核心算；帝国里的行政区跟着中央归同一个天下
            Empire empire = kingdom.GetEmpire();
            City heart = (empire?.CoreKingdom ?? kingdom).capital;
            var lead = heart == null ? null : TianxiaSystem.LeadOf(heart);
            rulers.Add(new RulerInfo
            {
                Id = kingdom.king.getID(), KingdomId = kingdom.getID(), Ruler = kingdom.king.getName(), Realm = NameOf(kingdom), Kind = Role(kingdom),
                Cities = kingdom.cities?.Count ?? 0, Age = kingdom.king.getAge(), TakenBy = taken == null ? null : SeatLabel(taken),
                Tianxia = lead == null ? "" : TerminalIntel.Strip(TianxiaSystem.Name(lead) ?? ""), Empire = empire?.data?.name ?? ""
            });
        }
        // 排成 天下 → 帝国或王国 → 行政区(用户 2026-11-11 "请按照天下->帝国/王国->行政区做归类，不然太乱了")：
        // 天下大的在前、不在任何天下里的垫后；一个天下里帝国、王国按城数排；帝国里中央在前、行政区在后
        var worldCities = new Dictionary<string, int>();
        var groupCities = new Dictionary<string, int>();
        string Group(RulerInfo info) => info.Tianxia + "/" + (info.Empire.Length > 0 ? "e:" + info.Empire : "k:" + info.Id);
        foreach (RulerInfo info in rulers)
        {
            worldCities[info.Tianxia] = (worldCities.TryGetValue(info.Tianxia, out int world) ? world : 0) + info.Cities;
            groupCities[Group(info)] = (groupCities.TryGetValue(Group(info), out int group) ? group : 0) + info.Cities;
        }
        for (int i = 0; i < rulers.Count; i++)
        {
            RulerInfo info = rulers[i];
            info.WorldCities = worldCities[info.Tianxia];
            info.GroupCities = groupCities[Group(info)];
            rulers[i] = info;
        }
        rulers.Sort((a, b) =>
        {
            int order = (a.Tianxia.Length == 0).CompareTo(b.Tianxia.Length == 0);
            if (order == 0) order = b.WorldCities.CompareTo(a.WorldCities);
            if (order == 0) order = string.CompareOrdinal(a.Tianxia, b.Tianxia);
            if (order == 0) order = b.GroupCities.CompareTo(a.GroupCities);
            if (order == 0) order = string.CompareOrdinal(Group(a), Group(b));
            if (order == 0) order = (a.Kind == "member").CompareTo(b.Kind == "member");
            return order != 0 ? order : b.Cities.CompareTo(a.Cities);
        });
        if (rulers.Count > MaxRulers) rulers.RemoveRange(MaxRulers, rulers.Count - MaxRulers);
        return rulers;
    }

    // 一国归在哪个天下、哪个帝国之下(凡是列国家的地方都按 天下 → 帝国或王国 → 行政区 归类，
    // 用户 2026-11-11 "凡是涉及到国家列表的都应该分类")。天下按都城所在的帝国核心算，帝国里的行政区跟着中央
    internal static (string tianxia, string empire) Belong(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        City heart = (empire?.CoreKingdom ?? kingdom).capital;
        var lead = heart == null ? null : TianxiaSystem.LeadOf(heart);
        return (lead == null ? "" : TerminalIntel.Strip(TianxiaSystem.Name(lead) ?? ""), empire?.data?.name ?? "");
    }

    public static string SeatLabel(Seat seat) =>
        string.IsNullOrEmpty(seat.Name) ? seat.Token.Substring(0, 4).ToUpperInvariant() : seat.Name;

    // 这一国是哪位玩家在领(不算 except 自己)：给外交、战事、舆图标上。没人领回 null
    private static JObject PlayerOf(Kingdom kingdom, Seat except)
    {
        if (kingdom == null) return null;
        Seat host = HostOf(kingdom, except);
        return host == null ? null : new JObject { ["name"] = SeatLabel(host), ["online"] = host.Watched };
    }

    private static string Role(Kingdom kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        return empire == null ? "king" : empire.CoreKingdom == kingdom ? "emperor" : Hereditary(kingdom) ? "member" : "official";
    }

    // 这国的君位是世袭的(不是朝廷任命、考选、推举出来的)。帝国里的行政区多是流官：位子是朝廷的，不是他家的
    // (用户 2026-11-11 进游戏后："南直隶任官是只能立太子吧，怎么把普通官员也算上了")
    private static bool Hereditary(Kingdom kingdom) =>
        kingdom?.GetRegime()?.GetLeaderSelectMethod() == EmpireCraft.Scripts.Regimes.LeaderSelectMethod.Succession;

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
            bool publicNews = ScreenFeedPatch.PublicNews(message);
            foreach (Seat seat in SeatByToken.Values)
            {
                if (seat.Ruler == null || seat.Realm?.data == null) continue;
                // 朝廷还不该知道的叛乱：开战的消息、提到叛军的消息都不报(见 TerminalHost.Journey.cs)
                if (about != null && seat.Hidden.Contains(about.getID()) || seat.HiddenNames.Any(text.Contains) ||
                    id.Contains("war") && FreshRebellionAgainst(seat)) continue;
                string realmName = NameOf(seat.Realm);
                bool own = about == seat.Realm || !string.IsNullOrEmpty(realmName) && text.Contains(realmName);
                // 皇帝治下各行政区的事也算本国的，照吏治迟报、缺报；只是小事另丢一半(皇权不下乡)
                if (!own && about != null && OwnEmpire(seat) is { } court && about.GetEmpire() == court)
                {
                    if (!major && TerminalIntel.Unit(TerminalIntel.Hash(_seq + 1, about.getID(), 7)) < 0.5d)
                    {
                        seat.Lost++;
                        continue;
                    }
                    own = true;
                }
                if (!own)
                {
                    // 别国的事只听得到军国大事(开战、议和、换君、称帝、亡国这类)；国库、吏治、任免这些内情没有暗探不该知道
                    // (用户 2026-11-11 进游戏后："邻国的信息也太详细了""我连暗探都没派呢就直接知道了国库亏空?")
                    // 再收紧(群里看了截图："这情报还是太详细了""皇上还是上帝""得再加强迷雾")：进了史书的也不够，
                    // 得是别国也听得到的事(ScreenFeedPatch.PublicNews)。别国判了谁的罪、朝里议什么、派系涨落都不报
                    if (!publicNews) continue;
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

    private static void Add(Seat seat, string text, bool own, bool major, string kind, long city = 0L)
    {
        long id = ++_seq;
        uint seed = TerminalIntel.Hash(id, seat.Realm.getID(), MonthNow());
        if (TerminalIntel.Lost(seat.Corruption, major, seat.Sight, seed))
        {
            seat.Lost++;
            return;
        }
        int late = TerminalIntel.DelayMonths(seat.Corruption, own, major, seat.Sight, seed);
        // 君主亲征在军中：军国大事当月就知道
        if (seat.Away == "campaign" && own && major) late = 0;
        if (seat.Pending.Count >= MaxPending)
        {
            seat.Pending.RemoveAt(0);
            seat.Lost++;
        }
        seat.Pending.Add(new Report
        {
            Id = id, Text = text, Kind = major ? "major" : kind, At = World.world.getCurWorldTime(), DueMonth = MonthNow() + late,
            Late = late, Own = own, City = city
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
        if (seat.LedgerYear != int.MinValue) FalseReport(seat, year);
        // 各城岁入同上年比，少得多的报上来(见 TerminalHost.Audit)
        TaxWatch(seat, seat.LedgerYear != int.MinValue);
        // 考课用的年数：各城这一年的上缴与积弊(见 TerminalHost.Review)
        ReviewSnapshot(seat);
        seat.LedgerYear = year;
        seat.LedgerTreasury = treasury;
        seat.LedgerPeople = people;
        seat.LedgerArmy = army;
    }

    // 假奏报(群里定的："也可能有假情报""所以玩家要积极反腐，不然收不到情报两眼一抹黑")：吏治坏到一定地步，
    // 地方上每年可能递一道报喜的假奏报上来(境内安靖、剿寇大捷、岁稔足额、河工告成)，和真的混在一起、不作标记
    private static void FalseReport(Seat seat, int year)
    {
        if (seat.Corruption <= 0.4d || seat.Realm.cities == null || seat.Realm.cities.Count == 0) return;
        uint roll = TerminalIntel.Hash(seat.Realm.getID(), 11, year);
        if (TerminalIntel.Unit(roll) >= (seat.Corruption - 0.4d) * 1.5d) return;
        City city = seat.Realm.cities[(int)(TerminalIntel.Hash(seat.Realm.getID(), 12, year) % (uint)seat.Realm.cities.Count)];
        if (city?.data == null) return;
        Add(seat, string.Format(LM.Get("terminal_false_" + (1 + roll % 4)), city.data.name), true, false, "event");
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
            ["loaded"] = true, ["v"] = Version, ["lang"] = LocalizedTextManager.instance?.language ?? "en", ["seat"] = SeatLabel(seat),
            ["year"] = Date.getCurrentYear(), ["month"] = Date.getCurrentMonth(), ["speed"] = SpeedPercent,
            ["paused"] = global::Config.paused, ["page"] = PageVersion()
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
                    ["id"] = info.Id.ToString(), ["kid"] = info.KingdomId.ToString(), ["ruler"] = info.Ruler, ["realm"] = info.Realm, ["kind"] = info.Kind,
                    ["cities"] = info.Cities, ["age"] = info.Age, ["taken"] = info.TakenBy, ["tianxia"] = info.Tianxia,
                    ["empire"] = info.Empire, ["look"] = Look(World.world.units.get(info.Id))
                });
            state["rulers"] = rulers;
            // 身后可以接着领的君主：即位的子孙、原国新君、占着故都的、受天命的(见 TerminalHost.Succession)
            state["next"] = LegacyJson(seat);
            return state;
        }
        Kingdom realm = seat.Realm;
        Empire empire = realm.GetEmpire();
        state["vacant"] = false;
        state["ruler"] = new JObject
        {
            ["id"] = seat.Ruler.getID().ToString(), ["name"] = seat.Ruler.getName(), ["age"] = seat.Ruler.getAge(), ["look"] = Look(seat.Ruler)
        };
        state["realm"] = new JObject
        {
            ["name"] = NameOf(realm), ["color"] = Hex(RealmMiniMap.KingdomColor(realm)), ["role"] = Role(realm),
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
        if (seat.DecreesCache == null || Time.realtimeSinceStartup >= seat.NextDecrees)
        {
            seat.NextDecrees = Time.realtimeSinceStartup + DecreeSeconds;
            // 政令清单要问好几个系统，哪一处出了错不该连奏报、国势一起拖垮：出错就先用上一份
            try
            {
                seat.DecreesCache = Decrees(seat);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][终端] 备政令清单出错: {exception.Message}");
            }
        }
        state["decrees"] = seat.DecreesCache;
        state["wars"] = Wars(seat);
        state["away"] = AwayJson(seat);
        state["cities"] = Cities(seat);
        state["provinces"] = Provinces(seat);
        // 半年财报：最新一份与上一份(手机上对着看)
        state["fiscal"] = new JArray(seat.Fiscals.Take(2));
        state["fiscalTrend"] = FiscalTrend(seat);
        // 决议天平(见 TerminalHost.DecisionScale.cs)、朝局(见 TerminalHost.Court.cs)：出错不拖垮整份国势
        try
        {
            state["decisions"] = DecisionScaleJson(seat);
            state["court"] = CourtJson(seat);
            state["fiscalDetail"] = FiscalDetailJson(seat);
            state["purse"] = PurseJson(seat);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 备决议天平出错: {exception.Message}");
        }
        var reports = new JArray();
        for (int i = seat.Reports.Count - 1; i >= 0 && reports.Count < 80; i--)
        {
            Report report = seat.Reports[i];
            var row = new JObject
            {
                ["id"] = report.Id, ["text"] = report.Text, ["kind"] = report.Kind, ["own"] = report.Own, ["late"] = report.Late,
                ["year"] = Date.getYear(report.At), ["month"] = Date.getMonth(report.At)
            };
            if (report.City > 0)
            {
                row["city"] = report.City.ToString();
                row["cityName"] = World.world.cities.get(report.City)?.data?.name ?? "";
            }
            reports.Add(row);
        }
        state["reports"] = reports;
        return state;
    }

    private static JObject FigureJson(Seat seat, long truth, int bias, int which)
    {
        long value = Figure(seat, truth, bias, which, out bool vague);
        return new JObject { ["v"] = value, ["vague"] = vague };
    }

    private static JArray Wars(Seat seat)
    {
        Kingdom realm = seat.Realm;
        var wars = new JArray();
        foreach (War war in realm.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            bool attacker = war.isAttacker(realm);
            Kingdom enemy = attacker ? war.main_defender : war.main_attacker;
            if (Unnoticed(seat, enemy)) continue;
            wars.Add(new JObject
            {
                ["enemy"] = enemy?.data?.name ?? "", ["attacker"] = attacker, ["months"] = Date.getMonthsSince(war.data.created_time),
                ["color"] = enemy == null ? null : Hex(RealmMiniMap.KingdomColor(enemy)), ["player"] = PlayerOf(enemy, seat)
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
            cities.Add(CityRow(seat, city, realm.capital, 0, true));
        return cities;
    }

    // 行政区与藩属(用户 2026-11-12 看国势页："这好歹让我能看到我的行政区啊""怎么只有直辖城市")：帝国之主才有。
    // 帝国的成员一家一条——流官的行政区、羁縻之地、世袭的封国——各带它的城(都城在前、人多的在前，至多 MaxProvinceCities 座)。
    // 行政区的城同直辖的城一个看法(政令到得了才有确数)；羁縻与封国自治，朝廷至多知道个约数。朝廷还不知道的叛军不列
    private const int MaxProvinces = 24, MaxProvinceCities = 10;

    private static JArray Provinces(Seat seat)
    {
        var list = new JArray();
        Kingdom realm = seat.Realm;
        Empire empire = realm.GetEmpire();
        if (empire == null || empire.CoreKingdom != realm) return list;
        foreach (Kingdom member in empire.kingdoms_list
                     .Where(found => found?.data != null && !found.isRekt() && found != realm && !seat.Hidden.Contains(found.getID()))
                     .OrderByDescending(found => found.IsAdministrativeKingdomType()).ThenByDescending(found => found.cities?.Count ?? 0)
                     .Take(MaxProvinces).ToList())
        {
            bool jimi = JimiSystem.IsJimiAdministration(member);
            // 世袭的军府(藩镇)单算一类：它算行政区的类型，却是世袭的
            bool garrison = !jimi && EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.IsHereditaryGarrison(member, member.GetRegime());
            string kind = jimi ? "jimi" : garrison ? "garrison" : member.IsAdministrativeKingdomType() || !Hereditary(member) ? "province" : "fief";
            var rows = new JArray();
            long people = 0;
            List<City> cities = (member.cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt()).ToList();
            foreach (City city in cities.OrderByDescending(city => city == member.capital).ThenByDescending(city => city.getPopulationPeople())
                         .Take(MaxProvinceCities))
            {
                JObject row = CityRow(seat, city, member.capital, kind == "province" ? 0 : 1, false);
                if (row["people"] != null) people += (long)row["people"];
                rows.Add(row);
            }
            var item = new JObject
            {
                ["id"] = member.getID().ToString(), ["name"] = NameOf(member), ["kind"] = kind, ["n"] = cities.Count, ["cities"] = rows
            };
            if (people > 0) item["people"] = TerminalIntel.RoundSig(people, 2);
            if (member.king != null && member.king.isAlive()) item["ruler"] = member.king.getName();
            list.Add(item);
        }
        return list;
    }

    // 一座城给手机的那一条。capital：它所在那一国的都城(治所)；floor：最清楚也只到这一档(羁縻、封国的城朝廷至多知道约数)；
    // details：要不要点进去看的细节(兵、库银、村镇)
    private static JObject CityRow(Seat seat, City city, City capital, int floor, bool details)
    {
        {
            float control = city == capital || !AdministrativeControlSystem.Known(city) ? 1f : AdministrativeControlSystem.Get(city);
            int clarity = Math.Max(floor, TerminalIntel.Clarity(control));
            // 微服私访亲眼看过的城：一年之内按真数给(不照吏治虚报、不管政令达不达)
            bool toured = seat.Toured.TryGetValue(city.getID(), out int seenAt) && MonthNow() - seenAt < TourTruthMonths;
            double padding = toured ? 0d : seat.Corruption;
            if (toured) clarity = 0;
            if (!details && clarity == 0) details = false;
            long people = TerminalIntel.Reported(city.getPopulationPeople(), padding, -1,
                TerminalIntel.Hash(city.getID(), 2, Date.getCurrentYear()), out _);
            var row = new JObject
            {
                ["id"] = city.getID().ToString(), ["name"] = city.data.name, ["capital"] = city == capital, ["control"] = Math.Round(control, 2), ["clarity"] = clarity, ["toured"] = toured
            };
            if (clarity < 2)
            {
                row["people"] = clarity == 0 ? people : TerminalIntel.RoundSig(people, 1);
                if (city.leader != null && city.leader.isAlive()) row["leader"] = city.leader.getName();
            }
            if (clarity == 0 && !details)
                row["villages"] = RuralSettlementSystem.InCity(city)?.Count ?? 0;
            if (clarity == 0 && details)
            {
                // 点进去看的细节(用户 2026-11-11 "单个行政区也可以点进去查看细节")：政令到得了的城才有，数照吏治虚报
                List<EmpireCraft.Scripts.Data.RuralSettlement> rural = RuralSettlementSystem.InCity(city);
                int year = Date.getCurrentYear();
                row["villages"] = rural?.Count ?? 0;
                row["towns"] = rural?.Count(place => place.town) ?? 0;
                row["forts"] = rural?.Count(place => place.fort) ?? 0;
                row["troops"] = TerminalIntel.Reported(city.countWarriors(), padding, 1, TerminalIntel.Hash(city.getID(), 3, year), out _);
                row["treasury"] = TerminalIntel.Reported(city.GetTreasuryBalance(), padding, 1, TerminalIntel.Hash(city.getID(), 1, year), out _);
                row["corruption"] = Math.Round(CorruptionRules.Bound(city.GetCorruptionRate()), 2);
                row["zones"] = city.countZones();
            }
            string admin = AdministrativeControlSystem.Describe(city);
            if (admin != null) row["admin"] = admin;
            return row;
        }
    }

    // 人物的外貌依据(用户 2026-11-12 "预制大概20来个人物立绘要求能换装，换发型胡子面部纹理肤色瞳色，用于表现客户端的各个人物")：
    // 性别|年岁|身份|文化线|是否近代|科技时代档|穿着(君主自己买来换上的衣着;冠帽;发式，见 TerminalHost.Wardrobe)|肤色|是否清制(施行八旗)。
    // 长什么样不在这里定——手机按这几样挑一套预制的立绘，再拿人物编号定细处(脸型、胡子…)，同一个人每回都是同一张脸(见网页的 lookParts)
    internal static string Look(Actor actor)
    {
        if (actor?.data == null) return "";
        try
        {
            Kingdom kingdom = actor.kingdom;
            string role = actor.isKing() ? RulerRole(kingdom)
                : kingdom != null && kingdom.GetHeir() == actor ? "heir"
                : CorpsSystem.IsGeneral(actor) ? "general"
                : actor.HasOfficeIdentity() ? "minister"
                : actor.city != null && actor.city.leader == actor ? "governor" : "commoner";
            return LookText(actor.isSexMale(), actor.getAge(), role, kingdom, WearOf(actor), SkinOf(actor));
        }
        catch (Exception)
        {
            return "";
        }
    }

    // 一国之主算什么身份：帝国之主是皇帝；帝国里的行政区(郡、省、节度使…)在游戏里也是一国，主官却是朝廷命官，照地方官
    // (用户 2026-11-12 "官员为什么也是皇帝的冕冠")；别的(含封国之主)是国王
    private static string RulerRole(Kingdom kingdom)
    {
        if (kingdom == null) return "king";
        if (kingdom.IsEmpire()) return "emperor";
        Empire court = kingdom.GetEmpire();
        if (court?.CoreKingdom == null || court.CoreKingdom == kingdom) return "king";
        return kingdom.IsAdministrativeKingdomType() ? "governor" : "king";
    }

    internal static string LookText(bool male, int age, string role, Kingdom kingdom, string wear = "", string skin = "") =>
        (male ? "m" : "f") + "|" + Math.Max(0, age) + "|" + role + "|" + RealmNamingSystem.LineOf(kingdom) + "|" +
        (kingdom?.GetRegime()?.type == EmpireCraft.Scripts.Regimes.RegimeType.Modern ? 1 : 0) + "|" + EmpireLawSystem.GetEraTier(kingdom) + "|" +
        wear + "|" + skin + "|" + (BannerSystem.Organized(kingdom) ? 1 : 0);

    // 这个人在地图上的肤色(原版按亚种的表型与深浅给小人上色，见 PhenotypeLibrary.createShades)，六位十六进制；取不到给空，手机照文化配
    private static string SkinOf(Actor actor)
    {
        try
        {
            PhenotypeAsset asset = AssetManager.phenotype_library?.getAssetByPhenotypeIndex(actor.data.phenotype_index);
            if (asset?.colors == null || asset.colors.Length == 0) return "";
            Color32 color = asset.colors[Mathf.Clamp(actor.data.phenotype_shade, 0, asset.colors.Length - 1)];
            return color.r.ToString("x2") + color.g.ToString("x2") + color.b.ToString("x2");
        }
        catch (Exception)
        {
            return "";
        }
    }

    // 手机上写的国名。改过名的国(自定国名，见 rename_realm)照显示的全称：存名每月按法理重拼，改的名不在那里头
    // (群里 2026-11-12 "我改的国号怎么消失了")；其余照存名
    internal static string NameOf(Kingdom kingdom)
    {
        if (kingdom?.data == null) return "";
        try
        {
            if (kingdom.HasCustomCountryNaming())
            {
                string full = kingdom.GetKingdomFullName();
                if (!string.IsNullOrWhiteSpace(full)) return TerminalIntel.Strip(full);
            }
        }
        catch (Exception)
        {
            // 国号还拼不出来：照存名
        }
        return kingdom.data.name ?? "";
    }

    private static string Hex(Color32 color) => "#" + color.r.ToString("x2") + color.g.ToString("x2") + color.b.ToString("x2");

    // ───────── 地图与眼界 ─────────
    // 手机上的舆图是它自己画的(用户 2026-11-12 转来的意见："应该把一些东西放到本地去计算，而不是服务器发包图像过来"
    // "首先服务器发过来地图结构，然后就算每个地块的政治数据，在本地进行计算")。这里只给数据：
    //   · 每块地(TileZone，8×8 格)归哪国、哪座城——就是这一份，变了手机才来取(state 里的 mapStamp)；
    //   · 山川地貌另发一份(TerminalHost.Field 的 /api/terrain)，迷雾变了才重发(这一份里的 ts)；
    //   · 各国属哪个帝国、哪个天下：手机上分层级看(用户 "地图可以像模组一样分层级")。
    // 谁看得见按眼界定：本国、同一帝国、交战国、接壤(隔 BorderGap 块水或无主地也算)的都知道，
    // 眼界到"通达"档时使节到得了的也知道、通达层里有一点往来的算传闻；看得见的国四周 FogRadius 块以内的山川也看得见，其余是迷雾
    private static void BuildMap(Seat seat)
    {
        Kingdom realm = seat.Realm;
        ZoneCalculator calc = World.world.zone_calculator;
        int width = calc.zones_total_x, height = calc.zones_total_y;
        if (width < 1 || height < 1) return;
        var owner = new int[width * height];
        var cityAt = new int[width * height];
        var kingdoms = new List<Kingdom>();
        var kingdomIndex = new Dictionary<long, int>();
        var cities = new List<City>();
        var cityIndex = new Dictionary<long, int>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = y * width + x;
            owner[at] = -1;
            cityAt[at] = -1;
            TileZone zone = calc.getZone(x, y);
            if (zone == null)
            {
                owner[at] = -2;
                continue;
            }
            City city = zone.city;
            if (city?.data == null || city.isRekt()) city = null;
            Kingdom kingdom = city?.kingdom ?? WildernessSystem.OwnerOf(zone);
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) kingdom = null;
            if (kingdom == null)
            {
                // 没有主的：正中是水的算水域，其余是无主之地
                if (zone.centerTile?.Type == null || zone.centerTile.Type.liquid) owner[at] = -2;
                continue;
            }
            if (!kingdomIndex.TryGetValue(kingdom.getID(), out int kingdomAt))
            {
                kingdomIndex[kingdom.getID()] = kingdomAt = kingdoms.Count;
                kingdoms.Add(kingdom);
            }
            owner[at] = kingdomAt;
            if (city == null) continue;
            if (!cityIndex.TryGetValue(city.getID(), out int cityIdx))
            {
                cityIndex[city.getID()] = cityIdx = cities.Count;
                cities.Add(city);
            }
            cityAt[at] = cityIdx;
        }
        // 没君主的席位(选人时)：realm 为空，天下各国都算已知，不加迷雾
        int self = realm != null && kingdomIndex.TryGetValue(realm.getID(), out int found) ? found : -1;
        // 朝廷还不该知道的叛军：它占的地在图上还算本国的
        if (self >= 0)
            foreach (long hiddenId in seat.Hidden)
                if (kingdomIndex.TryGetValue(hiddenId, out int rebelIndex))
                    for (int cell = 0; cell < owner.Length; cell++)
                        if (owner[cell] == rebelIndex) owner[cell] = self;
        HashSet<int> neighbours = Neighbours(owner, width, height, self);

        // 各国几等：0 本国、1 已知、2 传闻、3 不知
        var tier = new int[kingdoms.Count];
        Empire empire = realm?.GetEmpire();
        var enemies = new HashSet<Kingdom>();
        if (realm != null)
        foreach (War war in realm.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            enemies.Add(war.main_attacker);
            enemies.Add(war.main_defender);
        }
        seat.Known.Clear();
        seat.KnownNames.Clear();
        seat.Rumoured.Clear();
        for (int i = 0; i < kingdoms.Count; i++)
        {
            Kingdom kingdom = kingdoms[i];
            if (kingdom != null && seat.Hidden.Contains(kingdom.getID()))
            {
                tier[i] = 3;
                continue;
            }
            if (i == self) tier[i] = 0;
            else if (kingdom?.data == null || kingdom.isRekt()) tier[i] = 3;
            else if (realm == null || seat.Sight == TerminalIntel.Sight.World || neighbours.Contains(i) || enemies.Contains(kingdom) ||
                     empire != null && kingdom.GetEmpire() == empire) tier[i] = 1;
            else if (seat.Sight == TerminalIntel.Sight.Reach)
                tier[i] = ConnectivitySystem.CanContact(realm, kingdom) == true ? 1
                    : ConnectivitySystem.ContactStrength(realm, kingdom) > 0f ? 2 : 3;
            else tier[i] = 3;
            if (tier[i] == 2) seat.Rumoured.Add(kingdom.getID());
            if (tier[i] != 1) continue;
            seat.Known.Add(kingdom.getID());
            if (!string.IsNullOrEmpty(kingdom.data.name)) seat.KnownNames.Add(kingdom.data.name);
        }

        // 探路的人走过、买来的舆图上画着的地方(见 TerminalHost.Survey)：那里的国至少算有了传闻
        bool charted = seat.Charted.Count > 0 && seat.ChartedW == width && seat.ChartedH == height;
        if (charted)
            foreach (int cell in seat.Charted)
            {
                int who = owner[(height - 1 - cell / width) * width + cell % width];
                if (who < 0 || tier[who] != 3 || kingdoms[who]?.data == null || kingdoms[who].isRekt() || seat.Hidden.Contains(kingdoms[who].getID())) continue;
                tier[who] = 2;
                seat.Rumoured.Add(kingdoms[who].getID());
            }

        // 看得见的国重新编号(行 0 在上：游戏里 y 朝上，这里翻过来)。各国顺带标上属哪个帝国、哪个天下：
        // 手机上按天下、帝国、王国、城四个层级看这张图
        var slot = new int[kingdoms.Count];
        var realms = new JArray();
        var empires = new JArray();
        var worlds = new JArray();
        var empireAt = new Dictionary<Empire, int>();
        var worldAt = new Dictionary<object, int>();
        var sumX = new List<float>();
        var sumY = new List<float>();
        var count = new List<int>();
        for (int i = 0; i < slot.Length; i++)
        {
            slot[i] = -1;
            if (tier[i] > 2 || realms.Count >= TerminalIntel.MaxRealms) continue;
            Kingdom kingdom = kingdoms[i];
            slot[i] = realms.Count;
            var row = new JObject
            {
                ["id"] = kingdom.getID().ToString(), ["name"] = NameOf(kingdom),
                ["color"] = Hex(RealmMiniMap.KingdomColor(kingdom)), ["tier"] = tier[i]
            };
            // 哪位玩家领着(传闻之国不说)
            if (tier[i] <= 1 && PlayerOf(kingdom, seat) is { } player) row["player"] = player;
            Empire court = kingdom.GetEmpire();
            if (court?.data != null)
            {
                if (!empireAt.TryGetValue(court, out int courtAt))
                {
                    empireAt[court] = courtAt = empires.Count;
                    empires.Add(new JObject
                    {
                        ["name"] = TerminalIntel.Strip(court.data.name ?? ""), ["color"] = Hex(RealmMiniMap.KingdomColor(court.CoreKingdom ?? kingdom))
                    });
                }
                row["emp"] = courtAt;
            }
            try
            {
                City heart = (court?.CoreKingdom ?? kingdom).capital;
                var lead = heart == null ? null : TianxiaSystem.LeadOf(heart);
                if (lead != null)
                {
                    if (!worldAt.TryGetValue(lead, out int leadAt))
                    {
                        worldAt[lead] = leadAt = worlds.Count;
                        worlds.Add(new JObject { ["name"] = TerminalIntel.Strip(TianxiaSystem.Name(lead) ?? "") });
                    }
                    row["tx"] = leadAt;
                }
            }
            catch (Exception)
            {
                // 天下还没排好：这一国先不标
            }
            // 帝国之主的图上，帝国的成员(行政区、羁縻、封国)标成本朝之地：手机照本国的颜色画，彼此之间画行政区界
            // (用户 2026-11-12 "地图上也要能看到行政区的边界")
            if (tier[i] == 1 && empire != null && empire.CoreKingdom == realm && court == empire) row["prov"] = true;
            // 交战之敌：手机下令时它的城能去(同 FieldDecree 的判法)
            if (realm != null && i != self && kingdom?.data != null && realm.isInWarWith(kingdom))
            {
                row["war"] = true;
                // 是它打过来的：手机上的战争箭头由攻方指向守方(用户 2026-11-12 "开战的时候还是显示箭头和战线吧")
                try
                {
                    foreach (War war in realm.getWars())
                        if (war.getAttackers().Contains(kingdom) && war.getDefenders().Contains(realm))
                        {
                            row["atk"] = true;
                            break;
                        }
                }
                catch (Exception)
                {
                    // 战争正在结束：不标方向
                }
            }
            realms.Add(row);
            sumX.Add(0f);
            sumY.Add(0f);
            count.Add(0);
        }
        // 军情、点选都按这回的编号与几等认国(见 TerminalHost.Field)；朝廷还不知道的叛军算在本国头上
        seat.Slots.Clear();
        seat.SlotTier = new int[realms.Count];
        seat.SlotProv = new bool[realms.Count];
        for (int i = 0; i < slot.Length; i++)
        {
            if (slot[i] < 0) continue;
            seat.Slots[kingdoms[i].getID()] = slot[i];
            seat.SlotTier[slot[i]] = tier[i];
            seat.SlotProv[slot[i]] = realms[slot[i]]["prov"] != null;
        }
        if (self >= 0 && slot[self] >= 0)
            foreach (long hiddenId in seat.Hidden) seat.Slots[hiddenId] = slot[self];
        var cells = new byte[width * height];
        var keep = new bool[cells.Length];
        var reach = new int[cells.Length];
        // 本国各城看得到多远按政令(都城看满)；已知的别国只看得到它的地、往外一块
        var cityReach = new int[cities.Count];
        for (int i = 0; i < cityReach.Length; i++)
        {
            City city = cities[i];
            if (city?.data == null || city.kingdom != realm) continue;
            float control = city == realm.capital || !AdministrativeControlSystem.Known(city) ? 1f : AdministrativeControlSystem.Get(city);
            cityReach[i] = TerminalIntel.CityReach(control, FogRadius);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int from = y * width + x, to = (height - 1 - y) * width + x, who = owner[from];
            reach[to] = -1;
            if (who == -2) cells[to] = TerminalIntel.Water;
            else if (who < 0 || slot[who] < 0) cells[to] = TerminalIntel.Wild;
            else
            {
                int index = slot[who];
                cells[to] = (byte)index;
                reach[to] = who == self ? (cityAt[from] >= 0 ? cityReach[cityAt[from]] : 1) : tier[who] == 1 ? 1 : -1;
                keep[to] = tier[who] == 2;
                sumX[index] += x;
                sumY[index] += height - 1 - y;
                count[index]++;
            }
        }
        if (charted)
            foreach (int cell in seat.Charted)
                if (reach[cell] < 0) reach[cell] = 0;
        if (realm != null && seat.Sight != TerminalIntel.Sight.World) TerminalIntel.ApplyFog(cells, reach, keep, width, height);
        for (int i = 0; i < realms.Count; i++)
        {
            realms[i]["n"] = count[i];
            if (count[i] == 0) continue;
            realms[i]["x"] = Math.Round(sumX[i] / count[i], 1);
            realms[i]["y"] = Math.Round(sumY[i] / count[i], 1);
        }
        seat.MapCells = cells;
        seat.MapW = width;
        seat.MapH = height;
        seat.MapTile = Mathf.Max(1, MapBox.width / width);
        // 图上的城，和每块地是哪座城的(两个字节，城多的图一个字节不够)；迷雾里的、不知道的国的不给
        JArray shown = MapCities(seat, cities, out Dictionary<long, int> shownAt, out JObject dejure);
        var cityLow = new byte[cells.Length];
        var cityHigh = new byte[cells.Length];
        uint fog = 2166136261u;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int from = y * width + x, to = (height - 1 - y) * width + x, index = 0xFFFF;
            bool fogged = cells[to] == TerminalIntel.Fog;
            if (!fogged && cityAt[from] >= 0 && shownAt.TryGetValue(cities[cityAt[from]].getID(), out int at)) index = at;
            cityLow[to] = (byte)index;
            cityHigh[to] = (byte)(index >> 8);
            unchecked
            {
                fog = (fog ^ (fogged ? 1u : 0u)) * 16777619u;
            }
        }
        // 地貌那一份跟着迷雾走：迷雾变了、山川改了，这个数就变，手机见了重新取(见 TerminalHost.Field)
        int terrainVersion = TerrainNow(seat.Watched).Version;
        seat.MapTs = (int)(unchecked(fog * 31u + (uint)terrainVersion) & 0x7fffffff) | 1;
        var json = new JObject
        {
            ["ready"] = true, ["w"] = width, ["h"] = height, ["sight"] = (int)seat.Sight,
            ["cells"] = Convert.ToBase64String(TerminalIntel.Rle(cells)), ["realms"] = realms, ["empires"] = empires, ["worlds"] = worlds,
            ["tile"] = seat.MapTile, ["cities"] = shown, ["dejure"] = dejure, ["ts"] = seat.MapTs,
            ["zc"] = new JArray(Convert.ToBase64String(TerminalIntel.Rle(cityLow)), Convert.ToBase64String(TerminalIntel.Rle(cityHigh)))
        };
        City capital = realm?.capital;
        if (capital?.data != null && !capital.isRekt())
        {
            (double cx, double cy) = CellOf(seat, capital.city_center.x, capital.city_center.y);
            json["capital"] = new JArray(cx, cy);
        }
        string text = json.ToString(Newtonsoft.Json.Formatting.None);
        // 同上回一样就不算新图：手机见 state 里的 mapStamp 变了才来取
        if (text != seat.MapBody)
        {
            seat.MapBody = text;
            seat.MapStamp++;
            json["stamp"] = seat.MapStamp;
            seat.MapJson = json.ToString(Newtonsoft.Json.Formatting.None);
        }
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
