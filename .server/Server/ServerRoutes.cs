using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.Terminal;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Server;

// 独立服务器自己的接口(排在终端主机的接口前头，见 TerminalHost.ServerRoutes)：
//   GET  /api/server                    这是什么服务器、怎么登录
//   POST /api/account/register          {user, password, invite} → {token}       玩家注册
//   POST /api/account/login             {user, password} → {token}                玩家登录
//   POST /api/admin/pair                {user, password} 或 {code} → {token, admin:true}   管理员登入
//   GET  /api/admin/accounts            → {accounts:[{user, created, last, seat}]}  (以下都要管理员令牌)
//   POST /api/admin/account/delete      {user}                                   销号
//   GET  /api/admin/settings            → {settings:[{id, name, desc, kind: switch|text, value}], laws:[{id, name, desc, on}]}  模组设置与世界法则
//   POST /api/admin/settings            {id, value}                              改一项模组设置
//   POST /api/admin/law                 {id, on}                                 开关一条世界法则
//   POST /api/admin/scenario            {name, files:[{path, text|base64}], start} 上传剧本并开局
//   GET  /api/admin/scenario            → {state: idle|loading|ready|failed, name, step, detail}
//   GET  /api/admin/scenarios           → {state, name, key, step, detail, world, frame, size, frames:[{id, name}], sizes:[{id, name}], defaultFrame, defaultSize,
//                                          list:[{key, name, year, frame, custom, fixed, author}]}   服务器上有哪些剧本(预设的、上传过的)，眼下开的是哪个
//   POST /api/admin/scenarios           {key, frame, size} 按这个剧本开局；{reset: true} 把眼下这个剧本重开一遍(现有的世界先归档存档)
//   GET/POST /api/admin/save            看上回什么时候存的档 / 现在存一次
// 这些都在网络线程里跑：只读写账号表、存文件、往主线程的队列里放事，不碰世界
internal static class ServerRoutes
{
    internal static ServerConfig Config;
    internal static readonly Accounts Accounts = new();
    // 要在主线程办的事(开局、销号撤席位)
    internal static readonly ConcurrentQueue<Action> MainThread = new();
    // 开局的进度(给剧本编辑器看)
    internal static volatile string ScenarioState = "idle", ScenarioName = "", ScenarioStep = "", ScenarioDetail = "";
    // 眼下这一局是怎么开的(重开时照样再来)：剧本(预设的名字，或 custom/文件夹名)、范围、大小
    internal static volatile string ScenarioKey = "", ScenarioFrame = "";
    internal static volatile int ScenarioSize = -1;
    internal static int DefaultSize = 1;
    // 主线程里按剧本开局(Program 给)：剧本、范围(null 照默认)、大小(null 照默认)
    internal static Func<string, string, int?, bool> StartScenario;
    private const int MaxUpload = 16 * 1024 * 1024;
    private static int _adminFails;
    private static DateTime _adminLockedUntil = DateTime.MinValue;

    private static TerminalServer.Reply Json(JObject body, int status = 200) => TerminalServer.Reply.Json(body.ToString(Newtonsoft.Json.Formatting.None), status);
    private static TerminalServer.Reply Error(int status, string code, string detail = null) =>
        Json(detail == null ? new JObject { ["error"] = code } : new JObject { ["error"] = code, ["detail"] = detail }, status);

    private static JObject Parse(string body)
    {
        try
        {
            return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static TerminalServer.Reply? Handle(string method, string path, string token, string body)
    {
        if (method == "GET" && path == "/api/server")
            return Json(new JObject
            {
                ["server"] = "empirecraft", ["v"] = 1, ["lang"] = NeoModLoader.General.LM.Language, ["accounts"] = true, ["registration"] = Config.registration,
                ["adminLogin"] = Config.AdminByPassword ? "password" : "code", ["editor"] = false,
                ["online"] = TerminalHost.OnlineSeats(), ["max"] = TerminalPairing.Limit
            });
        // 同时在线到了上限：还没在线的席位先等着(已经在线的不受影响；管理员、登录注册不在此列)
        if (path.StartsWith("/api/", StringComparison.Ordinal) && !path.StartsWith("/api/account/", StringComparison.Ordinal) && path != "/api/admin/pair" &&
            TerminalHost.HasSeat(token) && !TerminalHost.IsAdmin(token) && !TerminalHost.SeatOnline(token))
        {
            int online = TerminalHost.OnlineSeats();
            if (online >= TerminalPairing.Limit) return Json(new JObject { ["error"] = "server_full", ["online"] = online, ["max"] = TerminalPairing.Limit }, 503);
        }
        // 独立服务器上玩家凭账号进，不收配对码
        if (method == "POST" && path == "/api/pair") return Error(403, "accounts_only");
        if (method == "POST" && path == "/api/account/register") return Register(body);
        if (method == "POST" && path == "/api/account/login") return Login(body);
        if (method == "POST" && path == "/api/admin/pair") return AdminLogin(body);
        if (path == "/api/admin/accounts" || path == "/api/admin/account/delete" || path == "/api/admin/scenario" || path == "/api/admin/settings" || path == "/api/admin/law" ||
            path == "/api/admin/save" || path == "/api/admin/scenarios")
        {
            if (!TerminalHost.IsAdmin(token)) return Error(401, "unpaired");
            // 存档：GET 看上回什么时候存的，POST 现在存一次(排到主循环里办，结果再 GET 看)
            if (path == "/api/admin/save")
            {
                if (method == "POST") MainThread.Enqueue(() => Program.SaveNow("管理员"));
                return Json(new JObject
                {
                    ["ok"] = true, ["exists"] = WorldSave.Exists, ["saved"] = WorldSave.LastSaved == DateTime.MinValue ? null : WorldSave.LastSaved.ToString("o"),
                    ["autosave_minutes"] = Config.autosave_minutes, ["last"] = Program.LastSaveResult
                });
            }
            if (method == "GET" && path == "/api/admin/settings") return ListSettings();
            if (method == "POST" && path == "/api/admin/settings") return ChangeSetting(body);
            if (method == "POST" && path == "/api/admin/law") return ChangeLaw(body);
            if (method == "GET" && path == "/api/admin/accounts") return ListAccounts();
            if (method == "POST" && path == "/api/admin/account/delete") return DeleteAccount(body);
            if (method == "GET" && path == "/api/admin/scenario")
                return Json(new JObject { ["state"] = ScenarioState, ["name"] = ScenarioName, ["step"] = ScenarioStep, ["detail"] = ScenarioDetail });
            if (method == "POST" && path == "/api/admin/scenario") return Upload(body);
            if (method == "GET" && path == "/api/admin/scenarios") return Scenarios();
            if (method == "POST" && path == "/api/admin/scenarios") return StartByKey(body);
            return Error(404, "not_found");
        }
        return null;
    }

    private static string Seat(string user) => TerminalHost.IssueSeat(user, out _);

    private static TerminalServer.Reply Register(string body)
    {
        JObject request = Parse(body);
        if (request == null) return Error(400, "bad_request");
        if (Config.registration == "closed") return Error(403, "closed");
        string user = ((string)request["user"] ?? "").Trim(), password = (string)request["password"] ?? "";
        if (Config.registration == "code" && !Same(((string)request["invite"] ?? "").Trim(), Config.invite_code ?? "")) return Error(403, "wrong_invite");
        if (!Accounts.GoodName(user)) return Error(400, "bad_user");
        if (!Accounts.GoodPassword(password)) return Error(400, "bad_password");
        string problem = Accounts.Register(user, password, Seat, out string token);
        if (problem == "taken") return Error(409, "taken");
        if (problem == "full") return Error(403, "full");
        Console.WriteLine("  新账号: " + user);
        return Json(new JObject { ["token"] = token, ["user"] = user });
    }

    private static TerminalServer.Reply Login(string body)
    {
        JObject request = Parse(body);
        if (request == null) return Error(400, "bad_request");
        string user = ((string)request["user"] ?? "").Trim();
        string problem = Accounts.Login(user, (string)request["password"] ?? "", TerminalHost.HasSeat, Seat, out string token);
        return problem switch
        {
            null => Json(new JObject { ["token"] = token, ["user"] = user }),
            "locked" => Error(429, "locked"),
            "full" => Error(403, "full"),
            _ => Error(403, "wrong_login")
        };
    }

    private static bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? ""), Encoding.UTF8.GetBytes(b ?? ""));

    // 管理员登入：设了账号密码的凭账号密码；没设的凭管理员码(写在 code 里，或账号留空、密码框里填码)
    private static TerminalServer.Reply AdminLogin(string body)
    {
        JObject request = Parse(body);
        if (request == null) return Error(400, "bad_request");
        string user = ((string)request["user"] ?? "").Trim(), password = (string)request["password"] ?? "", code = ((string)request["code"] ?? "").Trim();
        if (!Config.AdminByPassword)
        {
            string made = TerminalHost.AdminLoginCode(code.Length > 0 ? code : password, out bool locked);
            return made != null ? Json(new JObject { ["token"] = made, ["admin"] = true }) : locked ? Error(429, "locked") : Error(403, "wrong_admin");
        }
        lock (Accounts)
        {
            if (DateTime.UtcNow < _adminLockedUntil) return Error(429, "locked");
            // 手机网页的管理员入口只有一个框时，可以写成"账号:密码"
            if (password.Length == 0 && code.Contains(':'))
            {
                user = code.Substring(0, code.IndexOf(':')).Trim();
                password = code.Substring(code.IndexOf(':') + 1);
            }
            if (!Same(user, Config.admin_user ?? "") || !Same(password, Config.admin_password))
            {
                if (++_adminFails >= 5)
                {
                    _adminFails = 0;
                    _adminLockedUntil = DateTime.UtcNow.AddSeconds(60);
                    return Error(429, "locked");
                }
                return Error(403, "wrong_admin");
            }
            _adminFails = 0;
        }
        string token = TerminalHost.IssueAdmin();
        return token != null ? Json(new JObject { ["token"] = token, ["admin"] = true }) : Error(503, "not_ready");
    }

    // 这几项由服务器自己定，不给改：终端开关与端口(关了就连不上了)、画面相关的、席位上限(在 server.json 里)
    private static readonly System.Collections.Generic.HashSet<string> Fixed = new()
    {
        "terminal_mode_toggle", "Terminal_Port_Text", "terminal_lightweight_host_toggle", "pure_map_toggle", "Terminal_Max_Seats_Text"
    };

    private static string Text(string key, string fallback) => NeoModLoader.General.LM.Has(key) ? NeoModLoader.General.LM.Get(key) : fallback;

    // 模组设置与世界法则：原先在游戏里的设置窗口、法则窗口改，服务器没有界面，都从管理员这里改
    private static TerminalServer.Reply ListSettings()
    {
        var settings = new JArray();
        NeoModLoader.api.ModConfig config = EmpireCraft.Scripts.ModClass.modConfig;
        if (config != null)
            foreach (NeoModLoader.api.ModConfigItem item in config.Items().ToArray())
            {
                if (item.Id == null || Fixed.Contains(item.Id)) continue;
                bool isSwitch = item.Type == 0;
                settings.Add(new JObject
                {
                    ["id"] = item.Id, ["name"] = Text(item.Id, item.Id), ["desc"] = Text(item.Id + " Description", ""),
                    ["kind"] = isSwitch ? "switch" : "text",
                    ["value"] = isSwitch ? item.BoolVal : item.Type == 1 ? item.IntVal : item.Type == 3 ? item.FloatVal : item.TextVal ?? ""
                });
            }
        var laws = new JArray();
        WorldLaws world = World.world?.world_laws;
        foreach (WorldLawAsset law in AssetManager.world_laws_library.list.ToArray())
        {
            if (law?.id == null || !law.id.StartsWith("empirecraft_", StringComparison.Ordinal)) continue;
            laws.Add(new JObject
            {
                ["id"] = law.id, ["name"] = Text(law.id + "_title", law.id), ["desc"] = Text(law.id + "_description", ""),
                ["on"] = world != null && world.dict.TryGetValue(law.id, out PlayerOptionData option) ? option.boolVal : law.default_state
            });
        }
        return Json(new JObject { ["settings"] = settings, ["laws"] = laws });
    }

    private static TerminalServer.Reply ChangeSetting(string body)
    {
        JObject request = Parse(body);
        string id = (string)request?["id"] ?? "";
        JToken raw = request?["value"];
        string value = raw == null ? "" : raw.Type == JTokenType.Boolean ? ((bool)raw ? "true" : "false") : raw.ToString();
        NeoModLoader.api.ModConfig config = EmpireCraft.Scripts.ModClass.modConfig;
        if (config == null || Fixed.Contains(id) || config.Find(id) == null) return Error(404, "no_setting");
        // 回调会碰世界：排到主线程办
        MainThread.Enqueue(() =>
        {
            config.Set(id, value);
            Console.WriteLine("  设置 " + Text(id, id) + " = " + value);
        });
        return Json(new JObject { ["ok"] = true });
    }

    internal static bool SetLaw(string id, bool on)
    {
        WorldLawAsset law = AssetManager.world_laws_library.dict.TryGetValue(id ?? "", out WorldLawAsset found) ? found : null;
        WorldLaws world = World.world?.world_laws;
        if (law == null || world == null) return false;
        if (!world.dict.TryGetValue(id, out PlayerOptionData option)) world.dict[id] = option = new PlayerOptionData();
        option.boolVal = on;
        law.on_state_change?.Invoke(option);
        world.updateCaches();
        return true;
    }

    private static TerminalServer.Reply ChangeLaw(string body)
    {
        JObject request = Parse(body);
        string id = (string)request?["id"] ?? "";
        bool on = (bool?)request?["on"] ?? false;
        if (!AssetManager.world_laws_library.dict.ContainsKey(id)) return Error(404, "no_law");
        MainThread.Enqueue(() =>
        {
            if (SetLaw(id, on)) Console.WriteLine("  法则 " + Text(id + "_title", id) + (on ? " 开" : " 关"));
        });
        return Json(new JObject { ["ok"] = true });
    }

    private static TerminalServer.Reply ListAccounts()
    {
        var rows = new JArray();
        foreach (Accounts.Account account in Accounts.List())
            rows.Add(new JObject
            {
                ["user"] = account.user, ["created"] = account.created, ["last"] = account.last,
                ["seat"] = !string.IsNullOrEmpty(account.token) && TerminalHost.HasSeat(account.token)
            });
        return Json(new JObject { ["accounts"] = rows, ["registration"] = Config.registration });
    }

    private static TerminalServer.Reply DeleteAccount(string body)
    {
        JObject request = Parse(body);
        string user = ((string)request?["user"] ?? "").Trim();
        string seat = Accounts.Delete(user);
        if (seat == null) return Error(404, "no_account");
        // 席位撤掉、所领的君主交还：在主线程办
        if (seat.Length > 0) MainThread.Enqueue(() => TerminalHost.DropSeat(seat));
        Console.WriteLine("  销号: " + user);
        return Json(new JObject { ["ok"] = true });
    }

    // 上传剧本：存进自定剧本的文件夹，要开局的排给主线程
    private static TerminalServer.Reply Upload(string body)
    {
        if (body != null && body.Length > MaxUpload) return Error(413, "too_large");
        JObject request = Parse(body);
        if (request == null) return Error(400, "bad_request", "正文不是 JSON");
        string name = new string(((string)request["name"] ?? "").Trim().ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-').ToArray());
        if (name.Length == 0 || name.Length > 48) return Error(400, "bad_name", "剧本文件夹名只用小写字母、数字、下划线、短横");
        if (request["files"] is not JArray files || files.Count == 0) return Error(400, "no_files", "没有文件");
        string folder = Path.Combine(WorldMapData.CustomFolder, name);
        bool hasScenario = false;
        try
        {
            Directory.CreateDirectory(folder);
            string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            foreach (JToken file in files)
            {
                string relative = ((string)file["path"] ?? "").Replace('\\', '/').TrimStart('/');
                if (relative.Length == 0 || relative.Split('/').Any(part => part == ".." || part.Length == 0)) return Error(400, "bad_path", relative);
                string target = Path.GetFullPath(Path.Combine(folder, relative));
                if (!target.StartsWith(root, StringComparison.Ordinal)) return Error(400, "bad_path", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? folder);
                if (file["base64"] != null) File.WriteAllBytes(target, Convert.FromBase64String((string)file["base64"] ?? ""));
                else File.WriteAllText(target, (string)file["text"] ?? "", new UTF8Encoding(false));
                hasScenario |= relative == WorldMapData.CustomFile;
            }
        }
        catch (Exception exception)
        {
            return Error(400, "write_failed", exception.Message);
        }
        if (!hasScenario) return Error(400, "no_scenario", "文件里没有 " + WorldMapData.CustomFile);
        bool start = (bool?)request["start"] ?? true;
        Console.WriteLine("  收到剧本: " + name + "（" + files.Count + " 个文件）" + (start ? "，这就开局" : ""));
        // 带 custom/ 前缀：上传的剧本只当自定剧本开，不被同名的预设剧本顶掉
        if (start) Begin("custom/" + name, name, null, null);
        return Json(new JObject { ["ok"] = true, ["name"] = name, ["starting"] = start });
    }

    // 排一次开局(主线程办)：进度在 ScenarioState 里看
    private static void Begin(string key, string display, string frame, int? size)
    {
        ScenarioName = display;
        ScenarioStep = ScenarioDetail = "";
        ScenarioState = "loading";
        MainThread.Enqueue(() =>
        {
            try
            {
                if (StartScenario == null || !StartScenario(key, frame, size))
                {
                    ScenarioState = "failed";
                    if (ScenarioDetail.Length == 0) ScenarioDetail = "剧本读不出来";
                }
            }
            catch (Exception exception)
            {
                ScenarioState = "failed";
                ScenarioDetail = exception.Message;
            }
        });
    }

    // 上传过的剧本只读几样(叫什么、哪一年、谁做的)：文件没动过就不重读
    private sealed class Header
    {
        public string zh = "", tw = "", en = "", frame = "", author = "";
        public int year;
        public int[] size;
        public object map;
    }

    private static readonly Dictionary<string, (DateTime stamp, Header header)> Headers = new();

    private static Header ReadHeader(string file)
    {
        DateTime stamp = File.GetLastWriteTimeUtc(file);
        lock (Headers)
            if (Headers.TryGetValue(file, out var cached) && cached.stamp == stamp) return cached.header;
        Header header;
        try
        {
            using var reader = new Newtonsoft.Json.JsonTextReader(new StreamReader(file, Encoding.UTF8));
            header = Newtonsoft.Json.JsonSerializer.CreateDefault().Deserialize<Header>(reader) ?? new Header();
        }
        catch (Exception)
        {
            return null;
        }
        lock (Headers) Headers[file] = (stamp, header);
        return header;
    }

    private static string CustomFile(string key) =>
        key != null && key.StartsWith("custom/", StringComparison.Ordinal) && key.Length > 7 && key.Substring(7).All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
            ? Path.Combine(WorldMapData.CustomFolder, key.Substring(7), WorldMapData.CustomFile)
            : null;

    // 服务器上有哪些剧本、眼下开的是哪个
    private static TerminalServer.Reply Scenarios()
    {
        var list = new JArray();
        foreach (WorldMapEra era in WorldMapData.Eras.OrderBy(era => era.order).ThenBy(era => era.year))
            list.Add(new JObject
            {
                ["key"] = era.id, ["name"] = era.Name, ["year"] = era.year, ["frame"] = era.frame ?? "", ["custom"] = false, ["fixed"] = era.HasSize || era.map != null
            });
        try
        {
            if (Directory.Exists(WorldMapData.CustomFolder))
                foreach (string folder in Directory.GetDirectories(WorldMapData.CustomFolder).OrderBy(path => path, StringComparer.Ordinal))
                {
                    string file = Path.Combine(folder, WorldMapData.CustomFile), key = "custom/" + Path.GetFileName(folder);
                    if (!File.Exists(file) || CustomFile(key) == null) continue;
                    Header header = ReadHeader(file);
                    if (header == null) continue;
                    string name = WorldMapData.Pick(header.zh, header.tw, header.en);
                    list.Add(new JObject
                    {
                        ["key"] = key, ["name"] = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(folder) : name, ["year"] = header.year, ["frame"] = header.frame ?? "",
                        ["custom"] = true, ["fixed"] = header.map != null || header.size is { Length: >= 2 }, ["author"] = header.author ?? ""
                    });
                }
        }
        catch (Exception)
        {
            // 文件夹读不了：只给预设的
        }
        var frames = new JArray();
        foreach (WorldMapFrame frame in EarthTerrain.Frames)
            frames.Add(new JObject { ["id"] = frame.id, ["name"] = NeoModLoader.General.LM.Get("world_map_frame_" + frame.id) });
        var sizes = new JArray();
        for (int size = 0; size < 3; size++) sizes.Add(new JObject { ["id"] = size, ["name"] = NeoModLoader.General.LM.Get("world_map_size_" + size) });
        return Json(new JObject
        {
            ["state"] = ScenarioState, ["name"] = ScenarioName, ["key"] = ScenarioKey, ["step"] = ScenarioStep, ["detail"] = ScenarioDetail,
            ["world"] = global::Config.game_loaded && (World.world?.cities.Count ?? 0) > 0, ["frame"] = ScenarioFrame, ["size"] = ScenarioSize,
            ["frames"] = frames, ["sizes"] = sizes, ["defaultFrame"] = Config.frame ?? "", ["defaultSize"] = DefaultSize, ["list"] = list
        });
    }

    // 按服务器上已有的剧本开局，或把眼下这个剧本重开一遍
    private static TerminalServer.Reply StartByKey(string body)
    {
        JObject request = Parse(body);
        if (request == null) return Error(400, "bad_request");
        if (ScenarioState == "loading") return Error(409, "busy");
        bool reset = (bool?)request["reset"] == true;
        string key = reset ? ScenarioKey : ((string)request["key"] ?? "").Trim();
        if (string.IsNullOrEmpty(key)) return Error(400, "no_scenario");
        string frame = reset ? ScenarioFrame : ((string)request["frame"] ?? "").Trim();
        int? size = reset ? (ScenarioSize >= 0 ? ScenarioSize : null) : (int?)request["size"];
        if (size.HasValue) size = Math.Max(0, Math.Min(2, size.Value));
        if (!string.IsNullOrEmpty(frame) && EarthTerrain.Frames.All(known => known.id != frame)) return Error(400, "bad_frame");
        WorldMapEra preset = WorldMapData.Eras.FirstOrDefault(era => era.id == key);
        string file = preset == null ? CustomFile(key) : null;
        // 眼下这一局是从别处的文件夹开的(启动参数给的路径)：重开时照原路
        bool elsewhere = preset == null && file == null && reset && Path.IsPathRooted(key) && File.Exists(Path.Combine(key, WorldMapData.CustomFile));
        if (preset == null && !elsewhere && (file == null || !File.Exists(file))) return Error(404, "no_scenario");
        string display = preset != null ? preset.id : key.StartsWith("custom/", StringComparison.Ordinal) ? key.Substring(7) : Path.GetFileName(key.TrimEnd('/', (char)92));
        Console.WriteLine("  管理员" + (reset ? "重开" : "换") + "剧本: " + key);
        Begin(key, display, string.IsNullOrEmpty(frame) ? null : frame, size);
        return Json(new JObject { ["ok"] = true, ["key"] = key, ["starting"] = true });
    }
}
