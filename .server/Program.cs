using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.Terminal;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;

namespace EmpireCraft.Server;

// 帝国盒子独立服务器的入口：没有窗口、没有画面，只算数据、开终端的端口，手机网页连上来玩。
//   EmpireCraftServer [--data 目录] [--state 目录] [--config 文件] [--port 8766] [--lang cz] [--scenario 剧本] [--frame 范围] [--size 0|1|2] [--speed 1] [--new]
//     --data      模组的数据目录(里头有 Terminal/index.html、Locales、WorldMaps 等)；不给就从程序所在处往上找
//     --state     本服务器自己存东西的目录(配置、账号、席位、上传的剧本、存档)；默认 ./state
//     --config    配置文件；默认 <存放目录>/server.json，头一回启动自动写一份(管理员账号密码、注册方式等在里头设)
//     --scenario  开局用的剧本：预设剧本的名字(如 ming_qing)，或自定剧本的文件夹(里头有 scenario.json)；不给就等管理员上传
//     --frame     地图范围 eurasia / oldworld / eastasia / westworld(剧本自己定了范围的照剧本)；--size 小中大
//     --speed     世界流速的倍数(1 = 五秒一月)
//     --new       丢开存档重新开局(原来的存档挪到 <存放目录>/saves-archive)。不加的话，有存档就接着存档里的世界跑，剧本只在没有存档时用来开局
//   存档在 <存放目录>/save：每隔几分钟自动存(配置文件 autosave_minutes)，退出时再存一次。
//   端口、语言、剧本、范围、大小、流速在配置文件里也能设，命令行给了的以命令行为准。
// 控制台命令：status 现状 · save 存档 · load 退回上次存档 · admin 管理员怎么进 · accounts 账号 · deluser <账号> 销号 · scenario <剧本> 重新开局 · reset 把眼下这个剧本重开一遍 ·
//             settings / set <项> <值> 模组设置 · laws / law <法则> on|off 世界法则 · miss 没实现的原版接口 · quit 退出
public static class Program
{
    private static volatile bool _stop;
    private static ServerConfig _config;
    private static int _size = 1;
    // 存档：上回存(或读)是什么时候(进程启动起的秒数)、新开的局等世界就绪了先存一份、眼下这趟加载是不是读档
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();
    private static double _lastSaveAt;
    private static bool _saveWhenReady, _resuming, _verify, _serial;
    internal static volatile string LastSaveResult = "";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        // 模组里 async 的接续与没接住的异常排回主循环(同 Unity)，不让它们在线程池里把进程带走
        SynchronizationContext.SetSynchronizationContext(new MainThreadContext());
        string data = null, state = null, configPath = null, lang = null, scenario = null, frameId = null;
        int? port = null, size = null;
        float? speed = null;
        float runSeconds = 0f;
        bool bootOnly = false, fresh = false, profile = false;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--data":
                    data = next;
                    i++;
                    break;
                case "--state":
                    state = next;
                    i++;
                    break;
                case "--config":
                    configPath = next;
                    i++;
                    break;
                case "--port":
                    if (int.TryParse(next, out int givenPort)) port = givenPort;
                    i++;
                    break;
                case "--lang":
                    lang = next;
                    i++;
                    break;
                case "--scenario":
                    scenario = next;
                    i++;
                    break;
                case "--frame":
                    frameId = next;
                    i++;
                    break;
                case "--size":
                    if (int.TryParse(next, out int givenSize)) size = givenSize;
                    i++;
                    break;
                case "--speed":
                    if (float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float givenSpeed)) speed = givenSpeed;
                    i++;
                    break;
                case "--quiet":
                    LogService.Quiet = true;
                    break;
                case "--new":
                    fresh = true;
                    break;
                case "--profile":
                    // 排查用：打开模组自带的分段计时，每十秒报一次各段平均每步耗时
                    profile = true;
                    break;
                case "--verify-fronts":
                    // 排查用：军团战线的陆上战线段多核算完，再照原样逐国算一遍逐城逐块比(见 FrontSystem.Parallel)，收尾时报对不上的份数
                    FrontSystem.VerifyParallelSectors = true;
                    break;
                case "--serial":
                    // 排查用：不用多核(同游戏里一样逐国算)，对比快慢
                    ParallelWork.MaxWorkers = 1;
                    FrontSystem.ParallelSectors = false;
                    _serial = true;
                    break;
                case "--verify":
                    // 排查用：读档读完当场把世界核心再写一份到 <存放目录>/save.verify，同存档里的 world.json 比(应当一字不差)
                    _verify = true;
                    break;
                case "--verbose":
                    Shim.Verbose = true;
                    break;
                case "--trace":
                    Trace();
                    break;
                case "--boot-only":
                    // 排查用：初始化(给了剧本就连开局)走完，报账退出
                    bootOnly = true;
                    break;
                case "--run":
                    // 排查用：开局后照常跑这么多秒，然后报账退出
                    float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out runSeconds);
                    bootOnly = true;
                    i++;
                    break;
                case "--help" or "-h":
                    Console.WriteLine("EmpireCraftServer [--data dir] [--state dir] [--config file] [--port 8766] [--lang cz|ch|en] [--scenario name|dir] [--frame id] [--size 0|1|2] [--speed 1] [--new] [--quiet]");
                    return 0;
            }
        }
        data = data != null ? Path.GetFullPath(data) : FindData();
        if (data == null || !File.Exists(Path.Combine(data, "Terminal", "index.html")))
        {
            Console.Error.WriteLine("找不到数据目录(要有 Terminal/index.html)。用 --data 指明。");
            return 2;
        }
        state = Path.GetFullPath(state ?? "state");
        Directory.CreateDirectory(state);
        WorldSave.Root = state;
        _config = ServerConfig.Load(configPath != null ? Path.GetFullPath(configPath) : Path.Combine(state, "server.json"));
        lang ??= _config.lang;
        scenario ??= string.IsNullOrWhiteSpace(_config.scenario) ? null : _config.scenario;
        frameId ??= string.IsNullOrWhiteSpace(_config.frame) ? null : _config.frame;
        _size = size ?? _config.size;
        float baseSpeed = Math.Max(0.01f, speed ?? _config.speed);
        // 后台模拟(人口、财政、稳定……)每一步能用的时间：服务器不画图，给得比游戏宽，世界走得快就再放宽
        // (游戏里是一帧剩下的 1~4 毫秒；照那么给，流速一高月度结算就排不上，城里收不上税、国库见底)
        SimulationFrameBudget.ServerMilliseconds = Math.Clamp(12d * baseSpeed, 12d, 250d);
        // 纯数据：多核算的几处(见 ParallelWork)用上全部的核
        if (!_serial) ParallelWork.MaxWorkers = Environment.ProcessorCount;

        LM.Language = lang;
        LM.LoadJson("en", Path.Combine(data, "Locales", "en.json"));
        if (lang != "en") LM.LoadJson(lang, Path.Combine(data, "Locales", lang + ".json"));
        // 文字表读不到的话，满屏都是键名(city_suffix_county 之类)：一上来就说清楚
        if (LM.Count(lang) == 0)
            Console.Error.WriteLine("[error] 文字表没读到：" + Path.Combine(data, "Locales", lang + ".json") + " 不存在或读不出来。数据目录要带上整个 Locales 文件夹，语言写 cz(简体)、ch(繁体)或 en。");
        Application.persistentDataPath = state;
        ModClass._declare = new ModDeclare { FolderPath = data, UID = "EmpireCraft" };
        PlayerConfig.dict["language"] = new PlayerOptionData { stringVal = lang };
        // 手机网页照这个定用哪种文字
        LocalizedTextManager.instance = new LocalizedTextManager { language = lang };
        Console.WriteLine("帝国盒子独立服务器");
        Console.WriteLine("  数据: " + data);
        Console.WriteLine("  存放: " + state);
        Console.WriteLine("  配置: " + _config.Path);

        var world = new MapBox();
        World.world = world;
        ModClass mod = Boot();
        // 服务器里没有小人和建筑的实体，只能按"纯净地图"的口径算(人物建筑都是数据)：设置里写了什么都照这个来
        ModClass.PURE_MAP_MODE = true;
        if (profile) FrameProfiler.SetEnabled(true);
        // 模组每帧的那一趟(战线、人口、终端主机……)：游戏里是 Unity 每帧调的，这里由主循环调
        var modUpdate = (Action)Delegate.CreateDelegate(typeof(Action), mod, typeof(ModClass).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));

        // 独立服务器自己的那一层：玩家账号、管理员登入、上传剧本
        ServerRoutes.Config = _config;
        ServerRoutes.Accounts.Load(Path.Combine(state, "accounts.json"));
        ServerRoutes.StartScenario = (name, frame, givenSize) => StartScenario(name, frame ?? frameId, givenSize ?? _size);
        ServerRoutes.DefaultSize = _size;
        // 剧本表、地图范围先读好：管理员在手机上挑剧本时(网络线程)只读不写
        if (!WorldMapData.EnsureLoaded() || !EarthTerrain.EnsureLoaded())
            Console.Error.WriteLine("[error] 世界地图的数据读不出来(数据目录里要有 WorldMaps)：按剧本开不了局。");
        TerminalHost.ServerRoutes = ServerRoutes.Handle;
        TerminalServer.MaxBody = 24 * 1024 * 1024;
        TerminalServer.CrossOrigin = true;
        // 席位跟着账号走、注册不设上限；上限管的是同时在线的人数
        TerminalPairing.ConcurrentMode = true;
        TerminalPairing.Limit = _config.max_seats;
        // 联机大厅：照配置文件里写的接(这几样也是模组设置，管理员在手机上的「设置」里同样能改)
        if (!string.IsNullOrWhiteSpace(_config.lobby) && ModClass.modConfig != null)
        {
            string lobby = _config.lobby.Trim();
            bool line = lobby is "global" or "china";
            ModClass.modConfig.Set("Terminal_Lobby_Line_Text", line ? lobby : "global");
            ModClass.modConfig.Set("Terminal_Lobby_Url_Text", line ? "" : lobby);
            ModClass.modConfig.Set("Terminal_Room_Name_Text", _config.room_name ?? "");
            ModClass.modConfig.Set("terminal_room_open_toggle", _config.room_open ? "true" : "false");
            ModClass.modConfig.Set("terminal_lobby_toggle", "true");
        }

        // 有存档就接着存档里的世界跑；没有(或加了 --new)才按剧本开局
        bool resumed = false;
        if (!fresh && WorldSave.Exists)
        {
            resumed = Resume();
            if (!resumed) Console.Error.WriteLine("  存档读不出来，文件留在原处没动" + (scenario != null ? "；改按剧本开局(开局前它会被挪到 saves-archive)" : ""));
        }
        if (!resumed && scenario != null && !StartScenario(scenario, frameId, _size)) return 3;
        if (bootOnly)
        {
            for (int guard = 0; guard < 200000 && SmoothLoader.isLoading(); guard++) Step(world, modUpdate, 0.02f);
            if (runSeconds > 0f)
            {
                int frames = 0, failures = 0;
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed.TotalSeconds < runSeconds)
                {
                    Time.unscaledDeltaTime = Time.deltaTime = 0.02f;
                    Time.unscaledTime = Time.time += 0.02f;
                    Time.frameCount++;
                    try
                    {
                        Step(world, modUpdate, 0.02f * baseSpeed);
                    }
                    catch (Exception exception)
                    {
                        if (failures++ < 3) Console.Error.WriteLine("[error] 主循环: " + exception);
                    }
                    frames++;
                }
                Console.WriteLine("  跑了 " + frames + " 步(出错 " + failures + " 步)，到 " + Date.getCurrentYear() + " 年 " + Date.getCurrentMonth() + " 月：" + world.kingdoms.Count + " 国，" +
                                  world.cities.Count + " 城，" + world.units.Count + " 人，" + world.wars.Count + " 场战事");
                Console.WriteLine("  战线段多核：" + FrontSystem.ParallelBatches + " 批、" + FrontSystem.ParallelSides + " 份(用 " + ParallelWork.MaxWorkers + " 个线程)" +
                                  "；地形层一轮 " + FrontSystem.LayerZones + " 块、" + FrontSystem.LayerFrames + " 帧、" + FrontSystem.LayerMilliseconds.ToString("0") + " 毫秒" +
                                  (FrontSystem.VerifyParallelSectors ? "；核对 " + FrontSystem.VerifiedSides + " 份，对不上 " + FrontSystem.VerifyMismatches + " 份" : ""));
                Console.WriteLine("  " + EmpireCraft.Scripts.GeneralSystems.Decisions.DecisionSystem.Summary() + "；过秤 " + EmpireCraft.Scripts.GeneralSystems.Decisions.DecisionSystem.Planned + " 人次、有候选 " + EmpireCraft.Scripts.GeneralSystems.Decisions.DecisionSystem.Weighed);
            }
            if (world.cities.Count > 0 && Config.game_loaded)
            {
                Console.WriteLine("  存前: " + WorldSave.Digest());
                SaveNow("收尾");
            }
            Console.WriteLine("还没实现的原版接口 " + Shim.MissingKinds + " 种，调用最多的：");
            Console.Write(Shim.Report(80));
            return 0;
        }

        TerminalHost.Port = port ?? _config.port;
        TerminalHost.Enabled = true;
        Console.CancelKeyPress += (_, cancel) =>
        {
            cancel.Cancel = true;
            _stop = true;
        };
        // 被系统停掉(systemctl stop、kill)：也照常收尾存档
        IDisposable terminated = null;
        try
        {
            terminated = global::System.Runtime.InteropServices.PosixSignalRegistration.Create(global::System.Runtime.InteropServices.PosixSignal.SIGTERM, signal =>
            {
                signal.Cancel = true;
                _stop = true;
            });
        }
        catch (Exception)
        {
            // 这个系统上收不到这种信号：只靠 quit 和 Ctrl+C
        }
        new Thread(ReadConsole) { IsBackground = true, Name = "console" }.Start();

        var clock = Stopwatch.StartNew();
        double last = 0;
        bool announced = false;
        int failed = 0;
        while (!_stop)
        {
            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)(now - last);
            last = now;
            Time.unscaledDeltaTime = Time.deltaTime = Time.smoothDeltaTime = delta;
            Time.unscaledTime = Time.time = (float)now;
            Time.frameCount++;
            try
            {
                Step(world, modUpdate, Math.Min(delta, 0.25f) * baseSpeed);
            }
            catch (Exception exception)
            {
                if (failed++ < 20) Console.Error.WriteLine("[error] 主循环: " + exception);
            }
            // 端口开不了，或只在本机开得了、外头连不进来(多半被别的程序占着)：明说并退出，不装作起来了
            string portError = TerminalHost.Running ? (TerminalHost.Lan ? null : "只在本机(127.0.0.1)开得了，别的机器连不进来") : TerminalHost.PortError;
            if (!announced && TerminalHost.Running && portError == null)
            {
                announced = true;
                Announce();
            }
            if (!announced && portError != null)
            {
                Console.Error.WriteLine("[error] " + TerminalHost.Port + " 端口开不了: " + portError);
                Console.Error.WriteLine("        多半是被别的程序占着(同一台机器上的联机大厅默认用 8765)。在配置文件 " + _config.Path + " 里把 port 换一个，或启动时加 --port。");
                return 4;
            }
            bool ready = Config.game_loaded && !SmoothLoader.isLoading() && world.cities.Count > 0;
            // 读档读回来的"席位 → 君主"：终端主机把席位载入了、世界也读完了才交还
            if (WorldSave.PendingSeats != null && TerminalHost.Running && ready)
            {
                int restored = TerminalHost.RestoreSeatRulers(WorldSave.PendingSeats);
                WorldSave.PendingSeats = null;
                if (restored > 0) Console.WriteLine("  " + restored + " 个席位接着领原来的君主");
            }
            if (ready && _config.autosave_minutes > 0 && Uptime.Elapsed.TotalSeconds - _lastSaveAt >= _config.autosave_minutes * 60d) SaveNow("自动");
            Thread.Sleep(20);
        }
        terminated?.Dispose();
        if (Config.game_loaded && !SmoothLoader.isLoading() && world.cities.Count > 0) SaveNow("退出");
        TerminalHost.Enabled = false;
        TerminalHost.Tick();
        Console.WriteLine("已停。");
        return 0;
    }

    // 开服后在命令行说清楚：手机打开哪个地址、管理员怎么进、玩家怎么注册
    private static void Announce()
    {
        Console.WriteLine("  ─────────────────────────────");
        Console.WriteLine("  手机打开: http://" + TerminalHost.PrimaryAddress());
        AnnounceAdmin();
        Console.WriteLine("  玩家注册: " + (_config.registration switch
        {
            "closed" => "已关闭(配置文件 registration)",
            "code" => "凭邀请码 " + _config.invite_code,
            _ => "开放"
        }) + "；已有账号 " + ServerRoutes.Accounts.Count + " 个");
        if (TerminalHost.LobbyEnabled) Console.WriteLine("  联机大厅: " + TerminalHost.LobbySocket + (TerminalHost.RoomOpen ? "（公开）" : "（不公开）") + "，接上没有用 status 看");
        if (World.world.cities.Count == 0 && !SmoothLoader.isLoading()) Console.WriteLine("  还没有世界：用管理员账号登进手机网页挑一个剧本载入(也可以用剧本编辑器「发到服务器」，或启动时加 --scenario)");
        Console.WriteLine("  ─────────────────────────────");
    }

    private static void AnnounceAdmin()
    {
        if (_config.AdminByPassword) Console.WriteLine("  管理员: 账号 " + (_config.admin_user ?? "") + "，密码见配置文件(admin_password)");
        else Console.WriteLine("  管理员码: " + TerminalHost.AdminCode + "    (在配置文件里设 admin_user、admin_password 后改用账号密码)");
    }

    // 存一次档(要在主循环里调)，结果说一句
    internal static bool SaveNow(string why)
    {
        bool saved = WorldSave.Save(out string detail);
        _lastSaveAt = Uptime.Elapsed.TotalSeconds;
        LastSaveResult = (saved ? "已存档" : "没存成") + "（" + why + "）：" + detail;
        Console.WriteLine("  " + LastSaveResult);
        return saved;
    }

    // 读最近的一份存档(要在主循环里调)：排进加载队列，主循环接着走
    private static bool Resume()
    {
        bool queued = WorldSave.Load(out string which);
        if (!queued) return false;
        _resuming = true;
        _saveWhenReady = false;
        Console.WriteLine("  读档: " + Path.Combine(WorldSave.Root, which) + (ServerRoutes.ScenarioName.Length > 0 ? "（" + ServerRoutes.ScenarioName + "）" : ""));
        return true;
    }

    private static bool _wasLoading;
    private static readonly Stopwatch LoadClock = new();

    // 主循环的一步：网络线程排过来的事先办；开局的加载步骤没走完就接着走(一步里至多用 50 毫秒)，走完了世界才往前走
    internal static void Step(MapBox world, Action modUpdate, float seconds)
    {
        while (ServerRoutes.MainThread.TryDequeue(out Action job))
        {
            try
            {
                job();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[error] " + exception.Message);
            }
        }
        if (SmoothLoader.isLoading())
        {
            if (!_wasLoading)
            {
                _wasLoading = true;
                LoadClock.Restart();
            }
            var slice = Stopwatch.StartNew();
            string step = "";
            while (slice.ElapsedMilliseconds < 50 && SmoothLoader.isLoading())
            {
                SmoothLoader.RunNext();
                ShimCities.Refresh();
                if (SmoothLoader.CurrentStep == step) continue;
                step = SmoothLoader.CurrentStep;
                ServerRoutes.ScenarioStep = step;
                if (Shim.Verbose) Console.WriteLine("  · " + step);
            }
            if (!SmoothLoader.isLoading())
            {
                _wasLoading = false;
                bool resumed = _resuming;
                _resuming = false;
                // 存档读到一半出了错：半截世界不能留着跑(更不能存回去盖掉好的)，清掉
                if (resumed && WorldSave.LoadFailed)
                {
                    world.clearWorld();
                    WorldSave.PendingSeats = null;
                    ServerRoutes.ScenarioState = "failed";
                    ServerRoutes.ScenarioDetail = "存档读不出来: " + WorldSave.LoadError;
                    Console.Error.WriteLine("[error] " + ServerRoutes.ScenarioDetail + "。存档文件没动；用 scenario <剧本> 重新开局，或换回 save.old 再 load。");
                    return;
                }
                WorldMapGenerator.MarkDone();
                if (ServerRoutes.ScenarioState == "loading") ServerRoutes.ScenarioState = world.cities.Count > 0 ? "ready" : "failed";
                if (ServerRoutes.ScenarioState == "failed" && ServerRoutes.ScenarioDetail.Length == 0) ServerRoutes.ScenarioDetail = resumed ? "存档里一座城也没有" : "开局后一座城也没有";
                Console.WriteLine("  世界已就绪：" + world.kingdoms.Count + " 国，" + world.cities.Count + " 城，" + world.units.Count + " 人，用时 " + LoadClock.ElapsedMilliseconds + " 毫秒");
                _lastSaveAt = Uptime.Elapsed.TotalSeconds;
                if (resumed) Console.WriteLine("  读回: " + WorldSave.Digest());
                if (resumed && _verify)
                {
                    string verify = Path.Combine(WorldSave.Root, "save.verify");
                    Directory.CreateDirectory(verify);
                    WorldSave.WriteWorld(verify);
                }
                // 新开的局先存一份：这之后哪怕马上断电，下回也接得上
                else if (_saveWhenReady && world.cities.Count > 0 && Config.game_loaded) SaveNow("开局");
                _saveWhenReady = false;
            }
        }
        ShimCities.Refresh();
        world.FrameSeconds = seconds;
        float elapsed = world.calculateCurElapsed();
        if (Config.game_loaded && !Config.paused && !SmoothLoader.isLoading())
        {
            world.Advance(elapsed);
            // 国、城、人各自的行为任务(立君、收税、城主、官职、纳妾……，见 ServerAi)
            if (world.cities.Count > 0) ServerAi.Tick(world, elapsed);
        }
        world.Update();
        modUpdate();
    }

    // 按剧本开局：走模组自己的"生成世界地图"那一套。name 是预设剧本的名字、自定剧本的文件夹名，或一个文件夹的路径
    private static bool StartScenario(string scenario, string frameId, int sizeIndex)
    {
        if (!WorldMapData.EnsureLoaded() || !EarthTerrain.EnsureLoaded())
        {
            ServerRoutes.ScenarioDetail = "世界地图的数据读不出来(数据目录里要有 WorldMaps)";
            Console.Error.WriteLine(ServerRoutes.ScenarioDetail);
            return false;
        }
        // 给的是"custom/文件夹名"(上传来的剧本)或一个文件夹的完整路径：只当自定剧本读，哪怕文件夹同某个预设剧本重名
        bool uploaded = scenario.StartsWith("custom/", StringComparison.Ordinal);
        if (uploaded) scenario = Path.Combine(WorldMapData.CustomFolder, scenario.Substring("custom/".Length));
        bool byFolder = uploaded || Path.IsPathRooted(scenario);
        WorldMapEra era = byFolder ? null : WorldMapData.Eras.FirstOrDefault(item => item.id == scenario);
        if (era == null)
        {
            string folder = byFolder || Directory.Exists(scenario) ? Path.GetFullPath(scenario) : Path.Combine(WorldMapData.CustomFolder, scenario);
            string file = Path.Combine(folder, WorldMapData.CustomFile);
            if (!File.Exists(file))
            {
                ServerRoutes.ScenarioDetail = "找不到剧本 " + scenario;
                Console.Error.WriteLine(ServerRoutes.ScenarioDetail + "。预设剧本有：" + string.Join("、", WorldMapData.Eras.Select(item => item.id)));
                return false;
            }
            try
            {
                era = JsonConvert.DeserializeObject<WorldMapEra>(File.ReadAllText(file));
            }
            catch (Exception exception)
            {
                ServerRoutes.ScenarioDetail = "剧本文件读不出来: " + exception.Message;
                Console.Error.WriteLine(ServerRoutes.ScenarioDetail);
                return false;
            }
            if (era == null) return false;
            era.folder = folder;
            era.id = "custom/" + Path.GetFileName(folder);
        }
        WorldMapFrame frame = EarthTerrain.Frames.FirstOrDefault(item => item.id == (frameId ?? era.frame)) ?? EarthTerrain.Frames[0];
        Console.WriteLine("  剧本: " + era.id + "（" + era.Name + "），范围 " + frame.id + "，大小第 " + sizeIndex + " 档");
        ServerRoutes.ScenarioName = byFolder ? Path.GetFileName(scenario.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : scenario;
        ServerRoutes.ScenarioState = "loading";
        ServerRoutes.ScenarioDetail = "";
        // 记下这一局是怎么开的：重开(reset)时照样再来一遍
        ServerRoutes.ScenarioKey = !era.Custom ? era.id
            : string.Equals(Path.GetDirectoryName(era.folder), Path.GetFullPath(WorldMapData.CustomFolder), StringComparison.OrdinalIgnoreCase) ? era.id : era.folder;
        ServerRoutes.ScenarioFrame = frame.id;
        ServerRoutes.ScenarioSize = sizeIndex;
        WorldTip.ShimLast = "";
        // 换世界：各席位所领的君主交还
        TerminalHost.ResetWorldState();
        ServerAi.Reset();
        // 眼下的存档挪去归档(新世界就绪后另存)
        string archived = WorldSave.ArchiveCurrent();
        if (archived != null) Console.WriteLine("  原来的存档挪到了 " + archived);
        WorldSave.PendingSeats = null;
        _resuming = false;
        _saveWhenReady = true;
        WorldMapGenerator.Start(frame, sizeIndex, era);
        if (!SmoothLoader.isLoading())
        {
            ServerRoutes.ScenarioState = "failed";
            ServerRoutes.ScenarioDetail = WorldTip.ShimLast.Length > 0 ? WorldTip.ShimLast : "开局没排上(剧本或地图数据有问题)";
            Console.Error.WriteLine("  开局没成: " + ServerRoutes.ScenarioDetail);
            return false;
        }
        return true;
    }

    // 模组的初始化：照游戏里加载模组时那一套走(读文字表、挂补丁、登记各种内容、读设置)
    private static ModClass Boot()
    {
        var clock = Stopwatch.StartNew();
        ModClass mod = new GameObject("EmpireCraft").AddComponent<ModClass>();
        try
        {
            mod.OnLoad(ModClass._declare, mod.gameObject);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[error] 模组初始化中断: " + exception);
        }
        int patched = 0, inlinable = 0;
        foreach (MethodBase method in HarmonyLib.Harmony.GetAllPatchedMethods())
        {
            patched++;
            // 挂了补丁的方法不许被内联，否则调用处绕过补丁(模组补自己的方法不在此列：那些在头一回调用前就挂好了)
            if ((method.MethodImplementationFlags & MethodImplAttributes.NoInlining) != 0 || method.DeclaringType?.Namespace?.StartsWith("EmpireCraft") == true) continue;
            inlinable++;
            if (Shim.Verbose) Console.WriteLine("[替身] 挂了补丁却可能被内联: " + method.DeclaringType?.Name + "." + method.Name);
        }
        Console.WriteLine("  模组初始化 " + clock.ElapsedMilliseconds + " 毫秒，挂上补丁的方法 " + patched + " 个" + (inlinable > 0 ? "(其中 " + inlinable + " 个没标不许内联)" : ""));
        // 补丁挂不上，模组的大半逻辑就接不进来(行政区、城名、台账都靠它)：这种情形不能悄悄往下跑
        if (patched < 100)
            Console.Error.WriteLine("[error] 模组的补丁没挂上(只有 " + patched + " 个)。多半是按单文件发布的：发布时不要加 PublishSingleFile，" +
                                    "程序旁边要有 libclrjit.so(Windows 上是 clrjit.dll)。眼下这样跑出来的世界是不对的。");
        return mod;
    }

    // 排查用：把每处空引用之类的异常头一回出现时的调用栈打出来(模组里很多地方接住异常只留一句话)
    private static void Trace()
    {
        var seen = new HashSet<string>();
        AppDomain.CurrentDomain.FirstChanceException += (_, raised) =>
        {
            if (raised.Exception is not NullReferenceException and not InvalidCastException and not KeyNotFoundException and not ArgumentNullException) return;
            string[] frames = new StackTrace(1, true).ToString().Split('\n');
            string where = (raised.Exception.StackTrace ?? "").Split('\n')[0].Trim();
            lock (seen)
                if (!seen.Add(where)) return;
            Console.WriteLine("[trace] " + raised.Exception.GetType().Name + ": " + raised.Exception.Message + "\n   抛出处 " + where + "\n" + string.Join("\n", frames.Take(10)));
        };
    }

    // 从程序所在的目录往上找模组的数据目录
    private static string FindData()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "Terminal", "index.html")) && Directory.Exists(Path.Combine(at.FullName, "Locales")))
                return at.FullName;
        return null;
    }

    private static void ReadConsole()
    {
        while (!_stop)
        {
            string line;
            try
            {
                line = Console.ReadLine();
            }
            catch (Exception)
            {
                return;
            }
            // 没有控制台(后台运行、输入重定向到空)：不再读
            if (line == null) return;
            string[] words = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            string argument = words.Length > 1 ? words[1].Trim() : "";
            switch (words[0].ToLowerInvariant())
            {
                case "quit" or "exit" or "stop":
                    _stop = true;
                    break;
                case "save":
                    ServerRoutes.MainThread.Enqueue(() => SaveNow("手动"));
                    break;
                case "load":
                    // 退回上次存档(这之后的进展不要了)
                    ServerRoutes.MainThread.Enqueue(() =>
                    {
                        if (!WorldSave.Exists) Console.WriteLine("没有存档");
                        else if (!Resume()) Console.WriteLine("存档读不出来");
                    });
                    break;
                case "status":
                    if (TerminalHost.LobbyEnabled)
                        Console.WriteLine("联机大厅 " + TerminalHost.LobbySocket + "：" + (TerminalHost.LobbyOnline ? "已接上，房间 " + TerminalHost.RoomUrl : "没接上(" + (TerminalHost.LobbyError ?? TerminalHost.LobbyState) + ")"));
                    Console.WriteLine("端口 " + TerminalHost.Port + (TerminalHost.Running ? " 开着" : " 没开") + " · 账号 " + ServerRoutes.Accounts.Count + " · 席位 " + TerminalHost.Seats().Count +
                                      " · " + (World.world.cities.Count == 0 ? "还没有世界" : Date.getCurrentYear() + " 年 " + Date.getCurrentMonth() + " 月，" + World.world.kingdoms.Count + " 国 " +
                                                                                          World.world.cities.Count + " 城"));
                    Console.WriteLine("存档 " + (WorldSave.Exists ? "上回 " + (WorldSave.LastSaved == DateTime.MinValue ? "不详" : WorldSave.LastSaved.ToLocalTime().ToString("MM-dd HH:mm:ss")) : "还没有") +
                                      " · " + (_config.autosave_minutes > 0 ? "每 " + _config.autosave_minutes + " 分钟自动存" : "不自动存(配置文件 autosave_minutes)"));
                    break;
                case "admin":
                    AnnounceAdmin();
                    break;
                case "accounts":
                    foreach (Accounts.Account account in ServerRoutes.Accounts.List())
                        Console.WriteLine("  " + account.user + "  注册 " + account.created + "  上次登录 " + account.last);
                    Console.WriteLine("共 " + ServerRoutes.Accounts.Count + " 个账号");
                    break;
                case "deluser":
                    string seat = ServerRoutes.Accounts.Delete(argument);
                    if (seat == null) Console.WriteLine("没有这个账号: " + argument);
                    else
                    {
                        if (seat.Length > 0) ServerRoutes.MainThread.Enqueue(() => TerminalHost.DropSeat(seat));
                        Console.WriteLine("已销号: " + argument);
                    }
                    break;
                case "reset":
                    if (ServerRoutes.ScenarioKey.Length == 0) Console.WriteLine("眼下没有按剧本开的局；用 scenario <剧本> 开一局");
                    else
                        ServerRoutes.MainThread.Enqueue(() => StartScenario(ServerRoutes.ScenarioKey, ServerRoutes.ScenarioFrame.Length > 0 ? ServerRoutes.ScenarioFrame : null,
                            ServerRoutes.ScenarioSize >= 0 ? ServerRoutes.ScenarioSize : _size));
                    break;
                case "scenario":
                    if (argument.Length == 0) Console.WriteLine("用法：scenario <预设剧本名或自定剧本文件夹>");
                    else ServerRoutes.MainThread.Enqueue(() => StartScenario(argument, null, _size));
                    break;
                case "settings":
                    foreach (ModConfigItem item in ModClass.modConfig?.Items() ?? Enumerable.Empty<ModConfigItem>())
                        Console.WriteLine("  " + item.Id + " = " + (item.Type == 0 ? (item.BoolVal ? "true" : "false") : item.TextVal) + "    " + LM.Get(item.Id));
                    Console.WriteLine("改：set <设置项> <值>");
                    break;
                case "set":
                    string[] pair = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (pair.Length == 0) Console.WriteLine("用法：set <设置项> <值>(开关写 true / false)");
                    else
                        ServerRoutes.MainThread.Enqueue(() =>
                            Console.WriteLine(ModClass.modConfig != null && ModClass.modConfig.Set(pair[0], pair.Length > 1 ? pair[1] : "") ? "已改 " + pair[0] : "没有这个设置项: " + pair[0]));
                    break;
                case "laws":
                    foreach (WorldLawAsset law in AssetManager.world_laws_library.list.Where(law => law.id.StartsWith("empirecraft_")))
                        Console.WriteLine("  " + law.id + " = " + (law.isEnabled() ? "on" : "off") + "    " + LM.Get(law.id + "_title"));
                    Console.WriteLine("改：law <法则> on|off");
                    break;
                case "law":
                    string[] lawPair = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (lawPair.Length < 2) Console.WriteLine("用法：law <法则> on|off");
                    else
                        ServerRoutes.MainThread.Enqueue(() =>
                            Console.WriteLine(ServerRoutes.SetLaw(lawPair[0], lawPair[1] is "on" or "true" or "1") ? "已改 " + lawPair[0] : "没有这条法则(或还没有世界): " + lawPair[0]));
                    break;
                case "miss":
                    Console.WriteLine("还没实现的原版接口 " + Shim.MissingKinds + " 种，调用最多的：");
                    Console.Write(Shim.Report());
                    break;
                default:
                    Console.WriteLine("命令：status · save · load · admin · accounts · deluser <账号> · scenario <剧本> · reset · settings · set <项> <值> · laws · law <法则> on|off · miss · quit");
                    break;
            }
        }
    }
}
