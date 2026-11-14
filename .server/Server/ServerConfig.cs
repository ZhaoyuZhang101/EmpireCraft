using System;
using System.IO;
using EmpireCraft.Scripts.Terminal;
using Newtonsoft.Json;

namespace EmpireCraft.Server;

// 服务器的配置文件(默认 <存放目录>/server.json；头一回启动自动写一份默认的)。命令行给了同名参数的以命令行为准。
public sealed class ServerConfig
{
    // 终端的端口(同一台机器上的联机大厅默认用 8765，这里错开)
    public int port = 8766;
    // 语言：cz 简体、ch 繁体、en 英文
    public string lang = "cz";
    // 开局用的剧本：预设剧本的名字(如 ming_qing)，或自定剧本的文件夹；留空就等管理员上传
    public string scenario = "";
    // 地图范围 eurasia / oldworld / eastasia / westworld；留空照剧本或默认
    public string frame = "";
    // 地图大小 0 小、1 中、2 大
    public int size = 1;
    // 世界流速的倍数(1 = 五秒一月)
    public float speed = 1f;
    // 管理员的账号与密码：设了就不再生成管理员码，手机网页的管理员入口和剧本编辑器的上传都凭这一组进；
    // 两样都留空则照旧用启动时打印的管理员码
    public string admin_user = "";
    public string admin_password = "";
    // 玩家注册：open 谁都能注册、code 要填邀请码(invite_code)、closed 不收新账号
    public string registration = "open";
    public string invite_code = "";
    // 至多几位玩家同时有席位(1~16)
    public int max_seats = 16;
    // (账号注册多少个都行；上面这个数管的是同时在线的人数，离线的不占位置，至多 64)
    // 联机大厅：填了大厅的地址，服务器就去那里登记一个房间，别人在大厅网页上看得到、点得进来。
    //   同一台机器上自己架着大厅的，填 127.0.0.1:大厅端口(大厅默认占 8765，这时上面的 port 要换一个，比如 8766)；
    //   用公共大厅的填 global(国际线路)或 china(国内线路)；留空不接大厅
    public string lobby = "";
    // 房间在大厅里叫什么(留空自动起)；公开不公开(公开的在大厅里点了就进到登录页)
    public string room_name = "";
    public bool room_open = true;
    // 每隔几分钟(现实时间)自动存一次档；0 不自动存。退出时(quit、Ctrl+C、被系统停掉)总会存一次。
    // 存档在 <存放目录>/save，下回启动接着这个世界跑；要丢开它重新开局，启动时加 --new
    public int autosave_minutes = 5;

    [JsonIgnore] public string Path = "";
    [JsonIgnore] public bool AdminByPassword => !string.IsNullOrEmpty(admin_password);

    public static ServerConfig Load(string path)
    {
        ServerConfig config = null;
        try
        {
            if (File.Exists(path)) config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("配置文件读不出来，按默认的来: " + exception.Message);
        }
        bool fresh = config == null;
        config ??= new ServerConfig();
        config.Path = path;
        config.registration = (config.registration ?? "open").Trim().ToLowerInvariant();
        if (config.registration is not ("open" or "code" or "closed")) config.registration = "open";
        config.max_seats = Math.Max(1, Math.Min(TerminalPairing.MaxConcurrent, config.max_seats));
        config.autosave_minutes = Math.Max(0, config.autosave_minutes);
        if (fresh) config.Save();
        return config;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");
            File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("配置文件写不了: " + exception.Message);
        }
    }
}
