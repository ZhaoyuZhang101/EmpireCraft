using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 联机大厅(用户 2026-11-11 "要给别人连""玩家能够通过网页端查看当前有的房间, 加入"，问答定：所有玩家的公共大厅、
// 房主自己选公开还是凭配对码(默认凭码)；连接见 TerminalRelay)：
//   · 终端模式开着、又开了「联机大厅」时，游戏往外连到大厅服务器登记一个房间；别人在大厅网页上看到它、点进来，
//     之后手机的请求由服务器顺着这条连接转给本机，照常走 Handle——世界还是在房主的电脑上算；
//   · 房间号与钥匙头一回随机生成、存在设置目录(EmpireCraft.terminal.lobby)，以后一直用它：房间网址不变，手机桌面图标、
//     已配好的令牌下回还能用；钥匙只有本机知道，别人拿着房间号也顶替不了；
//   · 报给大厅的只有：房间名、公开与否、已配几部手机 / 至多几部、终端版本；公开的房间另报当前配对码(大厅替来人填上)；
//   · 大厅有几条线路(Lobbies，用户 2026-11-11 "终端可以选择公开至哪一个服务器")：国际线路在 Cloudflare 上，国内线路是国内的
//     一台服务器。各是各的大厅、各有各的房间列表，房主接哪条，房间就出现在哪条的网页上；房间号与钥匙两边通用；
//   · 设置里另填了大厅地址(LobbyText)就用填的，不看线路(换服务器、自己架大厅不用等模组更新)。
// 手机版游戏上不启用(同终端)
public static partial class TerminalHost
{
    // 线路：键(存在设置里、也是文字的键 terminal_line_键) → 游戏连大厅用的地址
    public static readonly (string key, string socket)[] Lobbies =
    {
        ("global", "wss://lobby.cmhgamedev.dpdns.org"),
        // 国内线路眼下只能用 IP：用户 2026-11-13 有了域名 lobby.cmhai.tech，但没备案的域名指到国内服务器，阿里云按请求里的
        // 域名整个拦掉(8765 端口也拦，实测回 403 未备案页)。备案通过后换成 wss://lobby.cmhai.tech
        ("china", "ws://8.159.129.136:8765")
    };
    // 选的线路(设置 Terminal_Lobby_Line_Text 里存的键；认不出的按头一条)
    public static string LobbyLine = "global";

    public static string LineKey
    {
        get
        {
            foreach ((string key, string _) in Lobbies)
                if (key == LobbyLine) return key;
            return Lobbies[0].key;
        }
    }

    public static string DefaultLobby
    {
        get
        {
            foreach ((string key, string socket) in Lobbies)
                if (key == LobbyLine) return socket;
            return Lobbies[0].socket;
        }
    }

    // 设置里另填了大厅地址：线路不作数
    public static bool LobbyCustom => LobbySocket != DefaultLobby;
    private const int MaxRoomName = 24;
    private const float RoomInfoSeconds = 240f;

    public static bool LobbyEnabled;
    // 公开：有空席位点了就进；否则要房主给的配对码
    public static bool RoomOpen;
    public static string RoomName = "";
    // 设置里另填的大厅地址(留空用内置的)
    public static string LobbyText = "";

    private static TerminalRelay _relay;
    private static string _relayUrl, _roomId, _roomKey;
    private static volatile string _roomInfo;
    private static float _roomInfoAt, _lobbyTickAt;

    public static bool LobbyOnline => _relay?.Online == true;
    public static string LobbyState => _relay?.State ?? "off";
    public static string LobbyError => _relay?.Error;

    // 游戏连大厅用的地址(ws:// 或 wss://)：只填了地址的按明文(多半是 IP:端口)，填了 http(s):// 的照着换
    public static string LobbySocket
    {
        get
        {
            string text = (LobbyText ?? "").Trim().TrimEnd('/');
            if (text.Length == 0 || text.Length > 200 || text.IndexOfAny(new[] { ' ', '"', '\'', '<', '>', '\\' }) >= 0) return DefaultLobby;
            if (text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) return text;
            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "wss://" + text.Substring(8);
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return "ws://" + text.Substring(7);
            return "ws://" + text;
        }
    }

    // 手机上打开的大厅网页、本机的房间(末尾不带斜杠)
    public static string LobbyWeb => WebOf(LobbySocket);

    // 游戏连大厅用的地址(ws / wss) → 手机上打开的网址(http / https)
    public static string WebOf(string socket) =>
        socket.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ? "https://" + socket.Substring(6) : "http://" + socket.Substring(5);

    public static string RoomUrl => _roomId == null ? "" : LobbyWeb + "/r/" + _roomId;

    // 每秒一回(主线程，见 Tick)：该连就连、该断就断；房间信息变了或隔了一阵就再报一次
    private static void TickLobby(float now)
    {
        if (now < _lobbyTickAt) return;
        _lobbyTickAt = now + 1f;
        if (!Enabled || !LobbyEnabled || !Running || Application.isMobilePlatform)
        {
            StopLobby();
            return;
        }
        try
        {
            EnsureRoom();
            string url = LobbySocket + "/host?room=" + _roomId + "&key=" + _roomKey;
            string info = RoomInfo();
            if (_relay == null || _relayUrl != url)
            {
                StopLobby();
                _roomInfo = info;
                _roomInfoAt = now;
                _relayUrl = url;
                _relay = new TerminalRelay { Handler = Handle, Hello = () => _roomInfo ?? "" };
                _relay.Start(url);
                LogService.LogInfo($"[EmpireCraft][终端] 接入联机大厅 {LobbyWeb}");
                return;
            }
            if (info == _roomInfo && now - _roomInfoAt < RoomInfoSeconds) return;
            _roomInfo = info;
            _roomInfoAt = now;
            _relay.Update(info);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 联机大厅出错: {exception.Message}");
        }
    }

    private static void StopLobby()
    {
        if (_relay == null) return;
        _relay.Stop();
        _relay = null;
        _relayUrl = null;
    }

    // 报给大厅的房间信息：一行一项 键=值
    private static string RoomInfo()
    {
        string name = (RoomName ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (name.Length == 0) name = DefaultRoomName();
        if (name.Length > MaxRoomName) name = name.Substring(0, MaxRoomName);
        var info = new StringBuilder();
        info.Append("name=").Append(name);
        info.Append("\nopen=").Append(RoomOpen ? '1' : '0');
        // 同时在线制下报的是眼下在线几位(离线的不占位置)
        info.Append("\nused=").Append(TerminalPairing.ConcurrentMode ? OnlineSeats() : Pairing.Seats().Count);
        info.Append("\nmax=").Append(TerminalPairing.Limit);
        info.Append("\ncode=").Append(RoomOpen ? Code : "");
        info.Append("\nver=").Append(Version);
        return info.ToString();
    }

    // 没起房间名：用这个世界的名字
    private static string DefaultRoomName()
    {
        try
        {
            string world = World.world?.map_stats?.name;
            if (!string.IsNullOrWhiteSpace(world)) return world.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
        catch
        {
            // 世界还没读好
        }
        return "EmpireCraft";
    }

    private static void EnsureRoom()
    {
        if (_roomId != null) return;
        string path = Path.Combine(ModClass.ModsConfigFolder(), "EmpireCraft.terminal.lobby");
        try
        {
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length >= 2 && IsHex(lines[0].Trim(), 16) && IsHex(lines[1].Trim(), 32))
                {
                    _roomId = lines[0].Trim();
                    _roomKey = lines[1].Trim();
                    return;
                }
            }
        }
        catch
        {
            // 读不了：重新生成
        }
        _roomId = RandomHex(8);
        _roomKey = RandomHex(16);
        try
        {
            File.WriteAllLines(path, new[] { _roomId, _roomKey });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 房间号存不下(下回开游戏会换一个): {exception.Message}");
        }
    }

    private static bool IsHex(string text, int length)
    {
        if (text == null || text.Length != length) return false;
        foreach (char c in text)
            if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }

    private static string RandomHex(int bytes)
    {
        var buffer = new byte[bytes];
        using (var random = new RNGCryptoServiceProvider()) random.GetBytes(buffer);
        var text = new StringBuilder(bytes * 2);
        foreach (byte value in buffer) text.Append(value.ToString("x2"));
        return text.ToString();
    }
}
