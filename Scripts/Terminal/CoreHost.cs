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
    internal static string ButtonText() => "";
    internal static string SecondButtonText() => null;
    internal static void OnButton(int index) { }
}
#else
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
//   · 找核心：<persistentDataPath>/EmpireCraftCore/bin/<模组版本>/ 里有 EmpireCraftServer.exe 就用；没有就照更新清单里的 core 一项
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
// 手机版游戏上不启用。导出与带回见 CoreSaveExport(格式与 .server/Server/WorldSave.cs 是同一份)
internal static class CoreHost
{
    internal const string SettingId = "terminal_core_host_toggle";
    internal static bool Enabled;

    internal enum Phase { Idle, Fetching, Asking, Exporting, Starting, Running, Stopping, Importing, Loading }
    internal static Phase State { get; private set; }
    // 本体停着、黑屏牌子归核心用
    internal static bool Holding => State >= Phase.Asking;
    // 端口归核心：本体的终端不开服务
    internal static bool OwnsPort => State is Phase.Starting or Phase.Running or Phase.Stopping;
    internal static bool CanStop => State is Phase.Asking or Phase.Starting or Phase.Running;

    private const string ExeName = "EmpireCraftServer.exe";
    private const float StartSeconds = 300f, StopSeconds = 120f, FetchTipSeconds = 3f, LoadSeconds = 600f, LoadGraceSeconds = 5f;
    private const string TokensFile = "EmpireCraft.terminal.tokens";
    private static readonly ConcurrentQueue<string> Lines = new();
    private static Process _process;
    private static string _exe, _address = "", _adminCode, _lastLine = "", _fetchError;
    private static volatile bool _fetchDone;
    private static volatile int _fetchPercent;
    private static bool _wasEnabled, _loadSeen, _seatsPending, _skipStale;
    // 核心那边没带回的那份存档走到的世界时刻
    private static double _staleTime;
    private static int _phaseFrame;
    private static float _deadline, _since, _nextTip;

    private static string Root => Path.Combine(Application.persistentDataPath, "EmpireCraftCore");
    private static string StateFolder => Path.Combine(Root, "state");

    internal static void Tick()
    {
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
                if (Enabled && Ready()) Begin();
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
        }
        // 交出去的这段时间本体的世界不许走(两边各走各的，带回来就对不上了)；读档那一段由原版自己管
        if (Holding && State != Phase.Loading) Config.paused = true;
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
            Release();
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
        if (State != Phase.Asking)
        {
            if (index == 0) RequestStop();
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
        if (_process == null) return;
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
        Phase.Stopping => LM.Get("terminal_core_stopping"),
        Phase.Importing or Phase.Loading => LM.Get("terminal_core_importing"),
        _ => ""
    };

    internal static string LinkLine() =>
        State == Phase.Asking ? "" :
        string.Format(LM.Get("terminal_core_link"), _address) +
        (string.IsNullOrEmpty(_adminCode) ? "" : " · " + string.Format(LM.Get("terminal_core_admin"), _adminCode));

    internal static string ButtonText() => LM.Get(State switch
    {
        Phase.Asking => "terminal_core_stale_back",
        Phase.Starting => "terminal_core_cancel",
        _ => "terminal_core_stop"
    });

    // 右边那个按钮：只在问"没带回的进度"时有
    internal static string SecondButtonText() => State == Phase.Asking ? LM.Get("terminal_core_stale_drop") : null;

    private static string Span(float seconds)
    {
        int total = Mathf.Max(0, (int)seconds);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60}:{total % 60:00}";
    }

    // ───────── 各步 ─────────
    private static bool Ready() =>
        !Application.isMobilePlatform && TerminalHost.Enabled && World.world != null && Config.game_loaded &&
        !SmoothLoader.isLoading() && !ScrollWindow.isWindowActive() && !World.world.isPaused();

    private static void Announce()
    {
        if (Application.isMobilePlatform) return;
        Tip(LM.Get(TerminalHost.Enabled ? "terminal_core_armed" : "terminal_core_need_terminal"));
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
            Fail(error);
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
                Fail(code == 4 ? string.Format(LM.Get("terminal_core_port"), TerminalHost.Port) : $"{code} {_lastLine}");
                return;
            }
            // 跑着跑着没了：照它最后存的那份带回
            LogService.LogWarning($"[EmpireCraft][服务器核心] 核心自己退出了(退出码 {code})：{_lastLine}。照它最后存的档带回。");
            Enter(Phase.Importing);
            return;
        }
        if (State == Phase.Starting && Time.unscaledTime > _deadline)
        {
            Kill();
            Fail(LM.Get("terminal_core_timeout"));
        }
    }

    private static void TickStop()
    {
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
        else Tip(LM.Get("terminal_core_back"));
        _seatsPending = true;
        Release();
    }

    // 核心的输出：认出世界读好了、管理员码；最后一行留着，没起来时写进提示
    private static void Drain()
    {
        while (Lines.TryDequeue(out string line))
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            _lastLine = line;
            int at = line.IndexOf("管理员码:", StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = line.Substring(at + 5).Trim();
                int end = rest.IndexOf(' ');
                _adminCode = end > 0 ? rest.Substring(0, end) : rest;
            }
            if (State == Phase.Starting && (line.Contains("读回:") || line.Contains("世界已就绪")))
            {
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
            return ExeIn(Path.Combine(Root, "bin", EmpireCraftUpdateService.InstalledVersion()));
        }
        catch (Exception)
        {
            return null;
        }
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
