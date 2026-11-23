#if EC_SERVER
namespace EmpireCraft.Scripts.Terminal;

// 独立服务器核心里没有"交给核心"这回事：只留别处引用得到的空壳(真身见下半个文件)
internal static class CoreHost
{
    internal const string SettingId = "terminal_core_host_toggle";
    internal static bool Enabled;
    internal static bool Holding => false;
    internal static bool OwnsPort => false;
    internal static bool CanStop => false;
    internal static void Tick() { }
    internal static void RequestStop() { }
    internal static void Shutdown() { }
    internal static string StatusLine() => "";
    internal static string LinkLine() => "";
    internal static string ScanUrl() => "";
    internal static string ButtonText() => "";
    internal static string SecondButtonText() => null;
    internal static string ThirdButtonText() => null;
    internal static void OnButton(int index) { }
}
#else
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 用服务器核心当主机(用户 2026-11-16 "模组里面可以选择轻量也可以选择直接用服务器核心""自动开启""完整存档接手""用下载""用双向")：
// 游戏本体算一步世界要连原版的整套主循环一起走，关了画面也省不下多少(见 LightweightHostMode)；独立服务器核心(.server)只跑模组
// 的那一套，多核、不画图。这里把游戏里当前这一局交给本机的核心接着跑，手机连核心，本体停着；停下核心时再把这一局带回游戏。
//   · 进：设置里打开开关，终端在跑、世界读完、窗口都关了、世界没暂停，就开始(同轻量主机；两个开关只能开一个)；
//   · 找核心：模组包里自带一份(RegimeEditor/core/win-x64，见 InstallBundled)，拷到 EmpireCraftCore/bin/current 用；包里没有的才看
//     <persistentDataPath>/EmpireCraftCore/bin/<模组版本>/ 里有 EmpireCraftServer.exe 就用；没有就照更新清单里的 core 一项
//     下载、校验、解包(见 EmpireCraftUpdateService.FetchCore)，下载时世界照走。同目录下的 core.path 写着别的目录时先用它(联调用)；
//   · 只在纯净地图下能用：核心一律按纯净地图跑、建筑只记台账，普通地图交过去再带回，实体建筑就没了；
//   · 交出去：CoreSaveExport.Export 把当前世界写成核心的存档(状态目录 EmpireCraftCore/state/save/)，写 server.json，把模组设置
//     和手机的配对令牌拷进状态目录的 mods_config(核心照同一套设置跑、手机不用重新配对)，本体的终端让出端口(见 TerminalHost.Tick)，
//     拉起核心；它的输出里出现"读回:"就是世界读好了；
//   · 交出去以后：本体的世界暂停、画面黑着(LightweightHostMode 的那块牌子，换成核心的字)，每帧看核心还在不在；
//   · 带回来：点牌子上的按钮或 Ctrl+Shift+F10——往核心的标准输入写一行 quit(它存档后退出)，退了就把令牌拷回来(核心那边可能
//     新配了手机)、CoreSaveExport.Import 排上读档(同平常读存档，分帧读几秒到十几秒)；读完本体的终端重新开端口、各席位照核心
//     存档里记的领回君主，开关拨回去。核心自己没了的，也照它最后存的那份带回；
//   · 关游戏时核心还在跑：给它写 quit，让它存好档再退。
//   · 上回没带回的进度(游戏被直接关掉、崩了)：状态目录里那份存档是这一局的(CoreGameId 对得上)而且走得比游戏里远，
//     接手前先在牌子上问——带回它，还是丢弃它、从眼下交给核心；
//   · 关掉游戏、只留核心(用户 2026-11-16 "直接关掉本体, 关掉服务器核心才开启本体""本体不能全关掉吗"：本体停着也占一个多核、
//     六个多 G 内存)：牌子上点「关闭游戏，只留核心」——把眼下的世界(交出去时的原样)存成一份原版存档(EmpireCraftCore/handoff)，
//     给核心写 detach <游戏 exe>，它回"已脱手"后写下标记(detached.json)、退出游戏，核心自己接着跑。停核心改在网页管理页点
//     「停下核心，回到游戏」：核心存档、把游戏开起来、自己退出。游戏再开起来、世界读完后见到标记：核心端口还通就挂着牌子等它退；
//     不通了就先读 handoff 那份存档(带回要拿同一局的世界作底)，读完照常带回；
// 手机版游戏上不启用。导出与带回见 CoreSaveExport(格式与 .server/Server/WorldSave.cs 是同一份)
internal static class CoreHost
{
    internal const string SettingId = "terminal_core_host_toggle";
    internal static bool Enabled;

    // 后三个是关掉游戏只留核心那一路：正在脱手、游戏重开后核心还在跑、读回交出去时的那一局
    internal enum Phase { Idle, Fetching, Asking, Exporting, Starting, Running, Stopping, Importing, Loading, Detaching, Attached, Reloading }
    internal static Phase State
    {
        get => _state;
        private set
        {
            _state = value;
            // 本体只是等着的那几步压低帧率；写存档、读档带回要全速
            StableFrameRate.SetIdle(value is Phase.Asking or Phase.Starting or Phase.Running or Phase.Stopping or Phase.Attached);
        }
    }
    private static Phase _state;
    // 本体停着、黑屏牌子归核心用
    internal static bool Holding => State >= Phase.Asking;
    // 端口归核心：本体的终端不开服务
    internal static bool OwnsPort => State is Phase.Starting or Phase.Running or Phase.Stopping or Phase.Detaching or Phase.Attached;
    // 牌子上的头一个按钮在不在(核心在跑时是停下它，游戏重开后核心还在跑时是打开管理页)
    internal static bool CanStop => State is Phase.Asking or Phase.Starting or Phase.Running or Phase.Attached;

    private const string ExeName = "EmpireCraftServer.exe";
    private const float StartSeconds = 300f, StopSeconds = 120f, FetchTipSeconds = 3f, LoadSeconds = 600f, LoadGraceSeconds = 5f, AdminWaitSeconds = 4f, LinkWaitSeconds = 2f,
        DetachWaitSeconds = 5f, ProbeSeconds = 2f;
    private const string TokensFile = "EmpireCraft.terminal.tokens";
    private static readonly ConcurrentQueue<string> Lines = new();
    private static Process _process;
    private static string _exe, _address = "", _adminCode, _lastLine = "", _fetchError;
    private static volatile bool _fetchDone;
    private static volatile int _fetchPercent;
    private static bool _wasEnabled, _loadSeen, _seatsPending, _skipStale, _adminOpened;
    // 脱手那一路：核心回了"已脱手"、游戏正为此退出、这回开游戏查过标记了、读 handoff 排上了、上一回探端口已经不通
    private static bool _detachAck, _detached, _resumeChecked, _reloadStarted, _portGone;
    private static float _detachAsked, _nextProbe;
    // 核心那边没带回的那份存档走到的世界时刻
    private static double _staleTime;
    private static int _phaseFrame;
    // 向核心要管理页地址是什么时候问的(0 = 没在等)
    private static float _linkAsked;
    // 游戏里的实时地图要登入核心(见 CoreLiveMap)：凭管理员码；配了账号密码的向核心要一张一次性的票(同"打开管理页"，只是不开浏览器)
    private static volatile string _liveTicket;
    private static float _liveTicketAsked = -999f;
    private const float LiveTicketSeconds = 15f;

    internal static string AdminCodeNow => _adminCode;

    internal static string TakeLiveTicket()
    {
        string ticket = _liveTicket;
        if (ticket != null)
        {
            _liveTicket = null;
            return ticket;
        }
        if (Time.unscaledTime - _liveTicketAsked < LiveTicketSeconds) return null;
        _liveTicketAsked = Time.unscaledTime;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.StandardInput.WriteLine("adminlink");
                _process.StandardInput.Flush();
            }
        }
        catch (Exception)
        {
            // 写不进去：过一会儿再要
        }
        return null;
    }
    private static float _deadline, _since, _nextTip;

    private static string Root => Path.Combine(Application.persistentDataPath, "EmpireCraftCore");
    private static string StateFolder => Path.Combine(Root, "state");
    private static string HandoffFolder => Path.Combine(Root, "handoff");
    private static string DetachedFile => Path.Combine(Root, "detached.json");

    private static string BundledFolder => Path.Combine(ModClass._declare.FolderPath, "RegimeEditor", "core", "win-x64");
    private static string BundledInstall => Path.Combine(Root, "bin", "current");
    private static bool _bundleChecked;
    private const float BundleCheckSeconds = 20f;
    private static float _nextBundleCheck;
    // 后台检测只读文件。只有游戏持有核心进程的期间，才在主线程提出存档重启。
    private enum BundleProbe { Idle, Checking, UpdateReady }
    private static volatile BundleProbe _bundleProbe;
    private static Process _bundleProbeProcess;
    private static bool _bundleRestart, _bundleSaved, _bundleInstalling;
    private static volatile bool _bundleInstallDone;
    private static bool _bundleInstallOk;

    internal static void Tick()
    {
        // 游戏每回启动：把模组包里自带的核心对到 bin/current(在后台拷，八十多兆)。路径要在主线程上取
        if (!_bundleChecked)
        {
            _bundleChecked = true;
            string bundled = BundledFolder, current = BundledInstall;
            ThreadPool.QueueUserWorkItem(_ => InstallBundled(bundled, current, false));
        }
        if (Enabled != _wasEnabled)
        {
            _wasEnabled = Enabled;
            if (Enabled) Announce();
            else if (Holding) RequestStop();
            else State = Phase.Idle;
        }
        // 带回之后：本体的终端重新开起来了，各席位照核心存档里记的领回君主
        if (_seatsPending && State == Phase.Idle && TerminalHost.Running && Config.game_loaded && !SmoothLoader.isLoading())
        {
            _seatsPending = false;
            try
            {
                int seats = TerminalHost.RestoreSeatRulers(CoreSaveExport.ReadSeats(StateFolder));
                LogService.LogInfo($"[EmpireCraft][服务器核心] 带回后 {seats} 个席位领回了君主");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][服务器核心] 席位没领回君主: {exception.Message}");
            }
        }
        switch (State)
        {
            case Phase.Idle:
                // 这回开游戏头一次世界读完：上回是不是关了游戏、把这一局留给了核心
                if (!_resumeChecked && World.world != null && Config.game_loaded && !SmoothLoader.isLoading())
                {
                    _resumeChecked = true;
                    if (File.Exists(DetachedFile) && Resume()) break;
                }
                if (_resumeChecked && Enabled && Ready()) Begin();
                break;
            case Phase.Fetching:
                TickFetch();
                break;
            // 等一帧，让牌子先画出来再做会卡住主线程的事
            case Phase.Exporting:
                if (Time.frameCount > _phaseFrame + 1) Export();
                break;
            case Phase.Starting:
            case Phase.Running:
                Watch();
                break;
            case Phase.Stopping:
                TickStop();
                break;
            case Phase.Importing:
                if (Time.frameCount > _phaseFrame + 1) Import();
                break;
            case Phase.Loading:
                TickLoad();
                break;
            case Phase.Detaching:
                TickDetach();
                break;
            case Phase.Attached:
                TickAttached();
                break;
            case Phase.Reloading:
                TickReload();
                break;
        }
        // 交出去的这段时间本体的世界不许走(两边各走各的，带回来就对不上了)；读档那一段由原版自己管
        if (Holding && State != Phase.Loading && State != Phase.Reloading) Config.paused = true;
    }

    // 牌子上的按钮、快捷键、关掉开关：核心在跑就让它存档退出再带回；还在读档的直接停掉(世界没动过，不用带回)
    internal static void RequestStop()
    {
        if (State == Phase.Asking)
        {
            Release();
            return;
        }
        if (State == Phase.Starting)
        {
            Kill();
            if (_bundleRestart) ReturnAfterBundleFailure(LM.Get("terminal_core_cancel"));
            else Release();
            return;
        }
        if (State != Phase.Running) return;
        try
        {
            _process.StandardInput.WriteLine("quit");
            _process.StandardInput.Flush();
        }
        catch (Exception)
        {
            // 写不进去：下面到点了直接结束它
        }
        Enter(Phase.Stopping);
        _deadline = Time.unscaledTime + StopSeconds;
    }

    // 牌子上的按钮(0 是左边那个)。问着"没带回的进度"时：左边带回它，右边丢弃它、从眼下交给核心；别的时候只有停下
    internal static void OnButton(int index)
    {
        if (State == Phase.Attached)
        {
            if (index == 0) OpenPage(null);
            return;
        }
        if (State != Phase.Asking)
        {
            if (index == 0) RequestStop();
            else if (State == Phase.Running && index == 1) OpenAdmin();
            else if (State == Phase.Running && index == 2) Enter(Phase.Detaching);
            return;
        }
        if (index == 0)
        {
            Enter(Phase.Importing);
            return;
        }
        _skipStale = true;
        Proceed();
    }

    // 关游戏：核心还在跑就让它存好档再退(下回接手时这份存档还在状态目录里)
    internal static void Shutdown()
    {
        // 为了只留核心才退的游戏：核心接着跑，不叫它退
        if (_process == null || _detached) return;
        try
        {
            if (_process.HasExited) return;
            if (State == Phase.Running)
            {
                _process.StandardInput.WriteLine("quit");
                _process.StandardInput.Flush();
            }
            else if (State == Phase.Starting) _process.Kill();
        }
        catch (Exception)
        {
            // 游戏正在退出，顾不上了
        }
    }

    // ───────── 牌子上的字(见 LightweightHostMode.RefreshPanel) ─────────
    internal static string StatusLine() => State switch
    {
        // 一个月 5 秒世界时长、一年 60 秒
        Phase.Asking => string.Format(LM.Get("terminal_core_stale"), (int)(_staleTime / 60d) + 1, (int)(_staleTime / 5d) % 12 + 1,
            Date.getCurrentYear(), Date.getCurrentMonth()),
        Phase.Exporting => LM.Get("terminal_core_exporting"),
        Phase.Starting => LM.Get("terminal_core_starting"),
        Phase.Running => string.Format(LM.Get("terminal_core_running"), Span(Time.unscaledTime - _since)),
        Phase.Stopping => LM.Get(_bundleRestart ? "terminal_core_syncing" : "terminal_core_stopping"),
        Phase.Importing or Phase.Loading => LM.Get("terminal_core_importing"),
        Phase.Detaching => LM.Get("terminal_core_detaching"),
        Phase.Attached => LM.Get("terminal_core_attached"),
        Phase.Reloading => LM.Get("terminal_core_reloading"),
        _ => ""
    };

    internal static string LinkLine() =>
        State is Phase.Asking or Phase.Reloading ? "" :
        string.Format(LM.Get("terminal_core_link"), _address) +
        (string.IsNullOrEmpty(_adminCode) ? "" : " · " + string.Format(LM.Get("terminal_core_admin"), _adminCode));

    // 牌子上二维码里的地址：核心接手了才给(之前扫了也连不上)。核心是另开的进程，配对码不归这头管，码里只有地址：
    // 以前配过的手机带着令牌直接进，新手机打开网页照核心的规矩登入
    internal static string ScanUrl() =>
        State is not (Phase.Running or Phase.Attached) || string.IsNullOrEmpty(_address) ? "" :
        _address.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _address : "http://" + _address;

    internal static string ButtonText() => LM.Get(State switch
    {
        Phase.Asking => "terminal_core_stale_back",
        Phase.Starting => "terminal_core_cancel",
        Phase.Attached => "terminal_core_open_admin",
        _ => "terminal_core_stop"
    });

    // 第三个按钮：核心跑着时，关掉游戏只留核心
    internal static string ThirdButtonText() => State == Phase.Running ? LM.Get("terminal_core_detach") : null;

    // 右边那个按钮：问"没带回的进度"时是丢弃它；核心跑着时是在这台电脑上打开管理页
    internal static string SecondButtonText() => State switch
    {
        Phase.Asking => LM.Get("terminal_core_stale_drop"),
        Phase.Running => LM.Get("terminal_core_open_admin"),
        _ => null
    };

    // 核心没有自己的界面，管它就用终端网页里的管理员那一页：在这台电脑的浏览器里打开，登入用的东西跟在地址后面(# 后头的不发给服务器，
    // 网页记下就从地址栏去掉)，不用再照着牌子抄一遍。核心一接手自动开一回，之后点牌子上的按钮再开。
    // 地址向核心要(标准输入写 adminlink，它回一行"管理页: 地址")：凭管理员码的带码，配了账号密码的带一张只能用一回的票，
    // 两种都不用人再登。核心没回(老一点的核心不认这句)就照自己认到的管理员码开
    private static void OpenAdmin()
    {
        _adminOpened = true;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.StandardInput.WriteLine("adminlink");
                _process.StandardInput.Flush();
                _linkAsked = Time.unscaledTime;
                return;
            }
        }
        catch (Exception)
        {
            // 写不进去：照下面的直接开
        }
        OpenPage(null);
    }

    private static void OpenPage(string url)
    {
        _linkAsked = 0f;
        string home = "http://127.0.0.1:" + TerminalHost.Port + "/";
        // 核心给的地址只认本机这个端口的，别的一概不开
        if (url == null || !url.StartsWith(home, StringComparison.Ordinal))
            url = home + (string.IsNullOrEmpty(_adminCode) ? "" : "#admin=" + _adminCode);
        try
        {
            Application.OpenURL(url);
        }
        catch (Exception e)
        {
            LogService.LogWarning($"[EmpireCraft][服务器核心] 管理页没打开：{e.Message}");
        }
    }

    private static string Span(float seconds)
    {
        int total = Mathf.Max(0, (int)seconds);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60}:{total % 60:00}";
    }

    // ───────── 各步 ─────────
    // 不看终端模式(用户 2026-11-18：开着"用服务器核心"、终端模式关着，核心一直不接手，世界留在游戏本体里算)：
    // 核心自带手机终端，接手后游戏这头的终端本来就把端口让给核心；没开终端的，本体这头什么都不占
    private static bool Ready() =>
        !Application.isMobilePlatform && World.world != null && Config.game_loaded &&
        !SmoothLoader.isLoading() && !ScrollWindow.isWindowActive() && !World.world.isPaused();

    private static void Announce()
    {
        if (Application.isMobilePlatform) return;
        Tip(LM.Get("terminal_core_armed"));
    }

    private static void Begin()
    {
        if (!PureMapMode.Enabled)
        {
            Fail(LM.Get("terminal_core_need_pure"));
            return;
        }
        // 服务一让出端口就查不到局域网地址了：先记下来给牌子用
        _address = TerminalHost.PublicUrl.Length > 0 ? TerminalHost.PublicUrl : TerminalHost.PrimaryAddress();
        _adminCode = null;
        _adminOpened = false;
        _linkAsked = 0f;
        if (!_skipStale && StaleProgress(out _staleTime))
        {
            Enter(Phase.Asking);
            return;
        }
        Proceed();
    }

    // 状态目录里那份存档是这一局的、而且走得比游戏里远：上回没带回来
    private static bool StaleProgress(out double worldTime)
    {
        worldTime = 0d;
        try
        {
            return CoreSaveExport.PeekSave(StateFolder, out string gameId, out worldTime) && !string.IsNullOrEmpty(gameId) &&
                   gameId == CoreGameId.Value && worldTime > World.world.getCurWorldTime() + 1d;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 找核心：有就交出去，没有先下载
    private static void Proceed()
    {
        _exe = FindCore();
        if (_exe != null)
        {
            Enter(Phase.Exporting);
            return;
        }
        // 下载时世界照走(刚才问话时压着暂停的，松开)
        if (Holding) Config.paused = false;
        string version = EmpireCraftUpdateService.InstalledVersion();
        string folder = Path.Combine(Root, "bin", version);
        _fetchError = null;
        _fetchDone = false;
        _fetchPercent = 0;
        _nextTip = 0f;
        Enter(Phase.Fetching);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                EmpireCraftUpdateService.FetchCore(version, folder, percent => _fetchPercent = percent);
            }
            catch (Exception exception)
            {
                _fetchError = exception.Message;
            }
            _fetchDone = true;
        });
    }

    // 下载在后台线程里走，世界照常；下完回到起点，条件合适(窗口关着、世界在走)时再交出去
    private static void TickFetch()
    {
        if (!_fetchDone)
        {
            if (Time.unscaledTime < _nextTip) return;
            _nextTip = Time.unscaledTime + FetchTipSeconds;
            Tip(string.Format(LM.Get("terminal_core_fetching"), _fetchPercent), FetchTipSeconds + 1f);
            return;
        }
        if (_fetchError != null)
        {
            Fail(_fetchError);
            return;
        }
        if (FindCore() == null)
        {
            Fail(ExeName);
            return;
        }
        State = Phase.Idle;
    }

    private static void Export()
    {
        try
        {
            Directory.CreateDirectory(StateFolder);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return;
        }
        if (!CoreSaveExport.Export(StateFolder, out string error))
        {
            Fail(error);
            return;
        }
        try
        {
            WriteServerJson();
            CopyConfig(false);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return;
        }
        LogService.LogInfo($"[EmpireCraft][服务器核心] 交出去时：{Date.getCurrentYear()} 年 {Date.getCurrentMonth()} 月，" +
                           $"{World.world.kingdoms.Count} 国 {World.world.cities.Count} 城 {World.world.units.Count} 人");
        // 进程留到下一帧再拉起：先让本体的终端把端口让出来(TerminalHost.Tick 排在前头)
        _process = null;
        while (Lines.TryDequeue(out _)) { }
        Enter(Phase.Starting);
        _deadline = Time.unscaledTime + StartSeconds;
    }

    private static void Watch()
    {
        if (_process == null)
        {
            if (Launch(out string error)) return;
            if (_bundleRestart) ReturnAfterBundleFailure(error);
            else Fail(error);
            return;
        }
        Drain();
        if (_process.HasExited)
        {
            int code = _process.ExitCode;
            _process = null;
            if (State == Phase.Starting)
            {
                // 没起来：世界没动过，不用带回。4 = 端口开不了
                string error = code == 4 ? string.Format(LM.Get("terminal_core_port"), TerminalHost.Port) : $"{code} {_lastLine}";
                if (_bundleRestart) ReturnAfterBundleFailure(error);
                else Fail(error);
                return;
            }
            // 跑着跑着没了：照它最后存的那份带回
            LogService.LogWarning($"[EmpireCraft][服务器核心] 核心自己退出了(退出码 {code})：{_lastLine}。照它最后存的档带回。");
            Enter(Phase.Importing);
            return;
        }
        // 管理员码那行多半先于"世界已就绪"；万一没等到，过几秒也照开(网页里手填)
        if (State == Phase.Running && !_adminOpened && (!string.IsNullOrEmpty(_adminCode) || Time.unscaledTime - _since > AdminWaitSeconds))
            OpenAdmin();
        if (_linkAsked > 0f && Time.unscaledTime - _linkAsked > LinkWaitSeconds) OpenPage(null);
        if (State == Phase.Starting && Time.unscaledTime > _deadline)
        {
            Kill();
            if (_bundleRestart) ReturnAfterBundleFailure(LM.Get("terminal_core_timeout"));
            else Fail(LM.Get("terminal_core_timeout"));
            return;
        }
        if (State == Phase.Running && Enabled && !_detached) CheckRunningBundle();
    }

    private static void CheckRunningBundle()
    {
        string target = BundledInstall;
        if (!string.Equals(_exe, Path.Combine(target, ExeName), StringComparison.OrdinalIgnoreCase)) return;
        if (_bundleProbe == BundleProbe.UpdateReady)
        {
            _bundleProbe = BundleProbe.Idle;
            if (_bundleProbeProcess != _process) return;
            try
            {
                _process.StandardInput.WriteLine("quit");
                _process.StandardInput.Flush();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][服务器核心] 无法请求存档更新：{exception.Message}");
                return;
            }
            _bundleSaved = false;
            _bundleRestart = true;
            Enter(Phase.Stopping);
            return;
        }
        if (_bundleProbe != BundleProbe.Idle || Time.unscaledTime < _nextBundleCheck) return;
        _nextBundleCheck = Time.unscaledTime + BundleCheckSeconds;
        _bundleProbeProcess = _process;
        _bundleProbe = BundleProbe.Checking;
        string bundled = BundledFolder;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool needsUpdate = false;
            try { needsUpdate = CompleteBundle(bundled) && !SameBundle(bundled, target); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { _bundleProbe = needsUpdate ? BundleProbe.UpdateReady : BundleProbe.Idle; }
        });
    }

    private static void TickStop()
    {
        if (_bundleRestart)
        {
            TickBundleRestart();
            return;
        }
        Drain();
        bool gone = _process == null || _process.HasExited;
        if (!gone && Time.unscaledTime <= _deadline) return;
        if (!gone)
        {
            LogService.LogWarning("[EmpireCraft][服务器核心] 核心迟迟不退，直接结束它；带回的是它最后存的那份档。");
            Kill();
        }
        _process = null;
        Enter(Phase.Importing);
    }

    private static void TickBundleRestart()
    {
        if (!_bundleInstalling)
        {
            // 让正常退出写完整存档；大地图存得慢也不强杀。
            Drain();
            if (!_process.HasExited) return;
            _process.WaitForExit(); // 等输出回调送到最后一行存档结果。
            Drain();
            int exitCode = _process.ExitCode;
            _process.Dispose();
            _process = null;
            if (exitCode != 0 || !_bundleSaved)
            {
                ReturnAfterBundleFailure(LM.Get("terminal_core_sync_save_failed"));
                return;
            }
            if (!Enabled)
            {
                _bundleRestart = false;
                Enter(Phase.Importing);
                return;
            }
            _bundleInstalling = true;
            _bundleInstallDone = false;
            string bundled = BundledFolder, target = BundledInstall;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _bundleInstallOk = false;
                try { _bundleInstallOk = InstallBundled(bundled, target, true) && SameBundle(bundled, target); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally { _bundleInstallDone = true; }
            });
            return;
        }
        if (!_bundleInstallDone) return;
        _bundleInstalling = false;
        if (!_bundleInstallOk)
        {
            ReturnAfterBundleFailure(LM.Get("terminal_core_sync_install_failed"));
            return;
        }
        if (!Enabled)
        {
            _bundleRestart = false;
            Enter(Phase.Importing);
            return;
        }
        _exe = Path.Combine(BundledInstall, ExeName);
        _liveTicket = null;
        _liveTicketAsked = -999f;
        _linkAsked = 0;
        Enter(Phase.Starting);
        _deadline = Time.unscaledTime + StartSeconds;
    }

    private static void ReturnAfterBundleFailure(string error)
    {
        _bundleRestart = false;
        LogService.LogWarning($"[EmpireCraft][服务器核心] 自动更新中断：{error}");
        Tip(string.Format(LM.Get("terminal_core_failed"), error), 8f);
        Enter(Phase.Importing);
    }

    private static void Import()
    {
        try
        {
            CopyConfig(true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][服务器核心] 配对令牌没拷回来: {exception.Message}");
        }
        if (!CallImport(out string error))
        {
            LogService.LogWarning($"[EmpireCraft][服务器核心] 这一局没带回来: {error}");
            Tip(string.Format(LM.Get("terminal_core_back_failed"), error), 8f);
            Release();
            return;
        }
        // 读档排上了：同平常读存档一样分帧读，读完再放手
        _loadSeen = false;
        Enter(Phase.Loading);
        _deadline = Time.unscaledTime + LoadSeconds;
        _since = Time.unscaledTime;
    }

    private static void TickLoad()
    {
        bool loading = !Config.game_loaded || SmoothLoader.isLoading();
        if (loading) _loadSeen = true;
        float now = Time.unscaledTime;
        // 一直没见着在读(当帧就读完了)也算读完；迟迟读不完的不再挡着屏幕
        if (loading ? now <= _deadline : !_loadSeen && now - _since < LoadGraceSeconds) return;
        if (loading) LogService.LogWarning("[EmpireCraft][服务器核心] 带回的这一局迟迟没读完，先把画面放回来。");
        else
        {
            Tip(LM.Get("terminal_core_back"));
            ClearDetach();
        }
        _seatsPending = true;
        Release();
    }

    // ───────── 关掉游戏、只留核心 ─────────
    private static void TickDetach()
    {
        Drain();
        if (_process == null || _process.HasExited)
        {
            // 正脱手时核心没了：照它最后存的那份带回
            _process = null;
            Enter(Phase.Importing);
            return;
        }
        if (_detached) return;
        if (_detachAsked <= 0f)
        {
            // 等一帧，让牌子先换上字
            if (Time.frameCount <= _phaseFrame + 1) return;
            try
            {
                string folder = HandoffFolder;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                SaveManager.saveMapData(folder);
                if (!File.Exists(Path.Combine(folder, "map.wbox"))) throw new IOException("map.wbox");
                _detachAck = false;
                _process.StandardInput.WriteLine("detach " + GameExe());
                _process.StandardInput.Flush();
            }
            catch (Exception exception)
            {
                DetachFailed(exception.Message);
                return;
            }
            _detachAsked = Time.unscaledTime;
            return;
        }
        if (_detachAck)
        {
            try
            {
                File.WriteAllText(DetachedFile, new JObject
                {
                    ["game_id"] = CoreGameId.Value, ["port"] = TerminalHost.Port, ["at"] = DateTime.UtcNow.ToString("o")
                }.ToString());
            }
            catch (Exception exception)
            {
                DetachFailed(exception.Message);
                return;
            }
            _detached = true;
            LogService.LogInfo("[EmpireCraft][服务器核心] 核心已脱手，游戏退出；这一局留在核心那边跑。");
            Application.Quit();
            return;
        }
        if (Time.unscaledTime - _detachAsked > DetachWaitSeconds) DetachFailed(LM.Get("terminal_core_detach_old"));
    }

    // 没关成：核心还照原样由游戏看着
    private static void DetachFailed(string reason)
    {
        LogService.LogWarning($"[EmpireCraft][服务器核心] 游戏没关成: {reason}");
        Tip(string.Format(LM.Get("terminal_core_detach_failed"), reason), 8f);
        _detachAsked = 0f;
        State = Phase.Running;
    }

    private static string GameExe()
    {
        string exe = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "worldbox.exe");
        if (!File.Exists(exe)) exe = Process.GetCurrentProcess().MainModule?.FileName ?? exe;
        return exe;
    }

    // 游戏重开后见到标记。返回真是接过去了(牌子挂上)，假是标记作废、照平常走
    private static bool Resume()
    {
        if (!File.Exists(Path.Combine(HandoffFolder, "map.wbox")) || !CoreSaveExport.PeekSave(StateFolder, out _, out _))
        {
            LogService.LogWarning("[EmpireCraft][服务器核心] 上回留给核心的那一局不齐全(交出去时的存档或核心的存档不在了)，不带回。");
            ClearDetach();
            return false;
        }
        if (!PureMapMode.Enabled)
        {
            // 带回只在纯净地图下做；标记留着，开了纯净地图重开游戏再带
            Tip(string.Format(LM.Get("terminal_core_back_failed"), LM.Get("terminal_core_need_pure")), 8f);
            return false;
        }
        _address = TerminalHost.PublicUrl.Length > 0 ? TerminalHost.PublicUrl : TerminalHost.PrimaryAddress();
        _adminCode = SavedAdminCode();
        _since = Time.unscaledTime;
        if (PortOpen())
        {
            LogService.LogInfo("[EmpireCraft][服务器核心] 核心还在跑上回留给它的那一局，等它停下再带回。");
            _portGone = false;
            _nextProbe = _since + ProbeSeconds;
            Enter(Phase.Attached);
        }
        else StartReload();
        return true;
    }

    // 核心还在跑：隔两秒看一眼端口。连着两回不通才算它退了(存档是退出前写完的)
    private static void TickAttached()
    {
        float now = Time.unscaledTime;
        if (now < _nextProbe) return;
        _nextProbe = now + ProbeSeconds;
        if (PortOpen())
        {
            _portGone = false;
            return;
        }
        if (!_portGone)
        {
            _portGone = true;
            return;
        }
        StartReload();
    }

    private static void StartReload()
    {
        _reloadStarted = false;
        Enter(Phase.Reloading);
    }

    // 先把交出去时的那一局读回来(带回要拿同一局的世界作底)，读完接着走带回
    private static void TickReload()
    {
        float now = Time.unscaledTime;
        if (!_reloadStarted)
        {
            if (Time.frameCount <= _phaseFrame + 1) return;
            _reloadStarted = true;
            _loadSeen = false;
            _since = now;
            _deadline = now + LoadSeconds;
            LogService.LogInfo("[EmpireCraft][服务器核心] 读回交出去时的那一局，读完再带回核心跑过的进度。");
            World.world.save_manager.loadWorld(SaveManager.folderPath(HandoffFolder));
            return;
        }
        bool loading = !Config.game_loaded || SmoothLoader.isLoading();
        if (loading) _loadSeen = true;
        if (loading ? now <= _deadline : !_loadSeen && now - _since < LoadGraceSeconds) return;
        if (loading)
        {
            LogService.LogWarning("[EmpireCraft][服务器核心] 交出去时的那一局迟迟没读完，这回不带回了；标记留着，下回开游戏再试。");
            Tip(string.Format(LM.Get("terminal_core_back_failed"), LM.Get("terminal_core_timeout")), 8f);
            Release();
            return;
        }
        Enter(Phase.Importing);
    }

    private static void ClearDetach()
    {
        try
        {
            if (File.Exists(DetachedFile)) File.Delete(DetachedFile);
            if (Directory.Exists(HandoffFolder)) Directory.Delete(HandoffFolder, true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][服务器核心] 脱手留下的文件没清掉: {exception.Message}");
        }
    }

    // 本机这个端口上有没有人听着(核心还在不在)。本机连接成不成都是立刻的事
    private static bool PortOpen()
    {
        try
        {
            using var client = new global::System.Net.Sockets.TcpClient();
            return client.ConnectAsync("127.0.0.1", TerminalHost.Port).Wait(300) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 核心凭管理员码登入时，码记在状态目录里(配了账号密码的不用码)。给牌子和"打开管理页"用
    private static string SavedAdminCode()
    {
        try
        {
            string config = Path.Combine(StateFolder, "server.json");
            if (File.Exists(config) && !string.IsNullOrEmpty((string)JObject.Parse(File.ReadAllText(config))["admin_password"])) return null;
            string path = Path.Combine(StateFolder, "mods_config", "EmpireCraft.terminal.admin");
            if (!File.Exists(path)) return null;
            string[] lines = File.ReadAllLines(path);
            string code = lines.Length > 0 ? lines[0].Trim() : "";
            return code.Length == 8 ? code.Substring(0, 4) + "-" + code.Substring(4) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 核心的输出：认出世界读好了、管理员码；最后一行留着，没起来时写进提示
    private static void Drain()
    {
        while (Lines.TryDequeue(out string line))
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            _lastLine = line;
            if (_bundleRestart && line.StartsWith("已存档（退出）", StringComparison.Ordinal)) _bundleSaved = true;
            int at = line.IndexOf("管理员码:", StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = line.Substring(at + 5).Trim();
                int end = rest.IndexOf(' ');
                _adminCode = end > 0 ? rest.Substring(0, end) : rest;
            }
            if (line.Contains("已脱手")) _detachAck = true;
            int link = line.IndexOf("管理页:", StringComparison.Ordinal);
            // 实时地图要的票：地址里 #admin= 后面那一段，不开浏览器
            if (link >= 0 && _linkAsked <= 0f && Time.unscaledTime - _liveTicketAsked < LiveTicketSeconds)
            {
                _lastLine = "";
                int mark = line.IndexOf("#admin=", link, StringComparison.Ordinal);
                if (mark >= 0)
                {
                    string ticket = line.Substring(mark + 7).Trim();
                    int stop = ticket.IndexOf(' ');
                    _liveTicket = stop > 0 ? ticket.Substring(0, stop) : ticket;
                }
                continue;
            }
            if (link >= 0 && _linkAsked > 0f)
            {
                // 这行带着登入用的票：不留作"最后一行"，免得随后写进提示或日志
                _lastLine = "";
                string url = line.Substring(link + 4).Trim();
                int cut = url.IndexOf(' ');
                OpenPage(cut > 0 ? url.Substring(0, cut) : url);
                continue;
            }
            if (State == Phase.Starting && (line.Contains("读回:") || line.Contains("世界已就绪")))
            {
                _bundleRestart = false;
                _nextBundleCheck = Time.unscaledTime + BundleCheckSeconds;
                State = Phase.Running;
                _since = Time.unscaledTime;
                LogService.LogInfo($"[EmpireCraft][服务器核心] 核心接手了这一局：{line}");
            }
            // 核心读回这一局后报的国、城、人数：记下来好跟交出去时的对
            else if (line.Contains("读回:")) LogService.LogInfo($"[EmpireCraft][服务器核心] 核心{line}");
        }
    }

    private static bool Launch(out string error)
    {
        error = null;
        try
        {
            var info = new ProcessStartInfo(_exe,
                $"--data {Quote(ModClass._declare.FolderPath)} --state {Quote(StateFolder)} --port {TerminalHost.Port} --quiet")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(_exe),
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // 设了它核心换剧本时会退出等人拉起(服务器上那样用)；这里让它在进程里换
            info.EnvironmentVariables.Remove("EMPIRECRAFT_RELAUNCH");
            _process = Process.Start(info);
            if (_process == null)
            {
                error = ExeName;
                return false;
            }
            _process.OutputDataReceived += (_, data) => { if (data.Data != null) Lines.Enqueue(data.Data); };
            _process.ErrorDataReceived += (_, data) => { if (data.Data != null) Lines.Enqueue(data.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            LogService.LogInfo($"[EmpireCraft][服务器核心] 拉起 {_exe}，状态目录 {StateFolder}，端口 {TerminalHost.Port}");
            return true;
        }
        catch (Exception exception)
        {
            _process = null;
            error = exception.Message;
            return false;
        }
    }

    // 带空格的路径加引号；结尾的反斜杠会把引号吃掉，先去掉
    private static string Quote(string path) => "\"" + path.TrimEnd('\\', '/') + "\"";

    // 核心的配置：已有的照留(管理员账号之类是在那边设的)，只把这边设置里定的几项写进去
    private static void WriteServerJson()
    {
        string path = Path.Combine(StateFolder, "server.json");
        JObject json;
        try
        {
            json = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        }
        catch (Exception)
        {
            json = new JObject();
        }
        json["port"] = TerminalHost.Port;
        json["lang"] = LocalizedTextManager.instance?.language is "cz" or "ch" ? LocalizedTextManager.instance.language : "en";
        json["max_seats"] = TerminalPairing.Limit;
        // 大厅：核心读到 "global"/"china" 当线路、别的当自定地址——另填了地址的写地址，没填的写线路，选的线路原样带过去
        json["lobby"] = !TerminalHost.LobbyEnabled ? "" :
            TerminalHost.LobbyCustom ? (TerminalHost.LobbyText ?? "").Trim() : TerminalHost.LineKey;
        json["room_name"] = TerminalHost.RoomName;
        json["room_open"] = TerminalHost.RoomOpen;
        File.WriteAllText(path, json.ToString());
    }

    // 模组设置与手机的配对令牌：交出去时拷给核心(它的 mods_config 在状态目录下)；带回时只把令牌拷回来
    private static void CopyConfig(bool back)
    {
        string game = ModClass.ModsConfigFolder(), core = Path.Combine(StateFolder, "mods_config");
        if (back)
        {
            string tokens = Path.Combine(core, TokensFile);
            if (File.Exists(tokens)) File.Copy(tokens, Path.Combine(game, TokensFile), true);
            return;
        }
        Directory.CreateDirectory(core);
        foreach (string name in new[] { ModClass._declare.UID + ".config", TokensFile })
        {
            string from = Path.Combine(game, name);
            if (File.Exists(from)) File.Copy(from, Path.Combine(core, name), true);
        }
    }

    private static string FindCore()
    {
        try
        {
            string hint = Path.Combine(Root, "core.path");
            if (File.Exists(hint))
            {
                string found = ExeIn(File.ReadAllText(hint).Trim().Trim('"'));
                if (found != null) return found;
            }
            string current = BundledInstall;
            if (InstallBundled(BundledFolder, current, true)) return ExeIn(current);
            return ExeIn(Path.Combine(Root, "bin", EmpireCraftUpdateService.InstalledVersion()));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 模组包里自带的核心(一键发布时编进 RegimeEditor/core/win-x64，见 publish_all.ps1；用户 2026-11-16 "全部打包到一起去")：
    // 拷到 bin/current 再用，不直接在模组目录里跑——核心跑着时模组目录的文件不被占着，游戏更新模组照常能换。
    // 目录名不带版本：桌面上的核心快捷方式(政体编辑器放的)指着它，模组更新后还是这个位置。游戏每回启动对一遍(见 Tick)，
    // 启动器与实际程序 DLL 都相同才复用。下一次接手前重新核对，更新后只需停下并重新开启核心。
    // 包里没有(从源码跑的开发目录)返回假，照旧找 bin/<版本>，再没有才去下载
    private static readonly object BundleGate = new();
    private static readonly string[] BundleFiles =
    {
        ExeName, "EmpireCraftServer.dll", "EmpireCraftServer.deps.json", "EmpireCraftServer.runtimeconfig.json"
    };

    private static bool CompleteBundle(string folder) => Array.TrueForAll(BundleFiles, file => File.Exists(Path.Combine(folder, file)));

    private static bool SameBundle(string source, string target) => Array.TrueForAll(BundleFiles,
        file => SameCoreFile(Path.Combine(source, file), Path.Combine(target, file)));

    private static bool InstallBundled(string bundled, string target, bool log)
    {
        lock (BundleGate)
        {
            var source = new FileInfo(Path.Combine(bundled, ExeName));
            if (!source.Exists) return false;
            var have = new FileInfo(Path.Combine(target, ExeName));
            try
            {
                if (!CompleteBundle(bundled)) return false;
                if (have.Exists && SameBundle(bundled, target)) return true;
                string suffix = Guid.NewGuid().ToString("N");
                string stage = target.TrimEnd('\\', '/') + ".copy-" + suffix;
                string backup = target.TrimEnd('\\', '/') + ".old-" + suffix;
                bool moved = false;
                try
                {
                    CopyFolder(bundled, stage);
                    if (Directory.Exists(target))
                    {
                        Directory.Move(target, backup);
                        moved = true;
                    }
                    try { Directory.Move(stage, target); }
                    catch
                    {
                        if (moved) Directory.Move(backup, target);
                        throw;
                    }
                    // 旧进程还占着文件时保留备份，更新后的目录已可供下次启动。
                    if (moved)
                        try { Directory.Delete(backup, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                finally
                {
                    if (Directory.Exists(stage)) Directory.Delete(stage, true);
                }
                if (log) LogService.LogInfo($"[EmpireCraft][服务器核心] 模组包里自带的核心已装到 {target}");
            }
            catch (Exception exception)
            {
                if (log) LogService.LogWarning($"[EmpireCraft][服务器核心] 自带的核心没装成({exception.Message})，有旧的就接着用旧的。");
            }
            return File.Exists(Path.Combine(target, ExeName));
        }
    }

    private static bool SameCoreFile(string source, string target)
    {
        if (!File.Exists(source) || !File.Exists(target)) return false;
        if (new FileInfo(source).Length != new FileInfo(target).Length) return false;
        using var hash = SHA256.Create();
        using var from = File.OpenRead(source);
        using var have = File.OpenRead(target);
        return Convert.ToBase64String(hash.ComputeHash(from)) == Convert.ToBase64String(hash.ComputeHash(have));
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (string folder in Directory.GetDirectories(from)) CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    // 包里带一层目录的也认
    private static string ExeIn(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
        string exe = Path.Combine(folder, ExeName);
        if (File.Exists(exe)) return exe;
        foreach (string inner in Directory.GetDirectories(folder))
        {
            exe = Path.Combine(inner, ExeName);
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    // 返回真是读档排上了
    private static bool CallImport(out string error)
    {
        try
        {
            return CoreSaveExport.Import(StateFolder, out error);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void Enter(Phase phase)
    {
        State = phase;
        _phaseFrame = Time.frameCount;
    }

    private static void Kill()
    {
        try
        {
            if (_process != null && !_process.HasExited) _process.Kill();
        }
        catch (Exception)
        {
            // 已经没了
        }
        _process = null;
    }

    private static void Fail(string reason)
    {
        LogService.LogWarning($"[EmpireCraft][服务器核心] 没接手成: {reason}");
        Tip(string.Format(LM.Get("terminal_core_failed"), reason), 8f);
        Kill();
        Release();
    }

    // 放手：本体的世界接着走、终端重新开端口、画面回来(LightweightHostMode 下一帧见 Holding 没了就恢复)，开关拨回去
    private static void Release()
    {
        bool held = Holding;
        State = Phase.Idle;
        _bundleRestart = false;
        _skipStale = false;
        Enabled = _wasEnabled = false;
        if (held) Config.paused = false;
        ModSettingsWindow.SetSwitch(SettingId, false);
        TerminalHost.Kick();
    }

    private static void Tip(string text, float seconds = 5f)
    {
        try
        {
            WorldTip.showNow(text, false, "top", seconds);
        }
        catch (Exception)
        {
            // 提示出不来不要紧
        }
    }
}
#endif
