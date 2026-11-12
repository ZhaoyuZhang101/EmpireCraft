using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EmpireCraft.Scripts.Terminal;

// 终端模式的小服务(用户 2026-11-11 "做个远程的遥控指挥世界盒子的政体""用网页端做，世界盒子做终端")：
// 游戏里开一个端口，手机浏览器连上来看奏报、地图、国势。只用 System.dll 里的套接字，自己读写最简单的 HTTP，
// 不引别的库(手机版加载器在手机上编译源码，见 TerminalHost 里不在手机上启用的说明)。
// 这个文件不碰游戏和 Unity：收到请求在网络线程里调 Handler，Handler 只许读主线程备好的字符串、往队列里放命令。
public sealed class TerminalServer
{
    public struct Reply
    {
        public int Status;
        public string Type;
        public byte[] Body;

        public static Reply Json(string json, int status = 200) =>
            new() { Status = status, Type = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes(json ?? "{}") };

        public static Reply Html(byte[] page) => new() { Status = 200, Type = "text/html; charset=utf-8", Body = page };

        public static Reply Error(int status, string code) => Json("{\"error\":\"" + code + "\"}", status);
    }

    // (方法, 路径, 令牌, 正文) → 回复
    public Func<string, string, string, string, Reply> Handler;

    private const int MaxHead = 8192, MaxBody = 4096, TimeoutMs = 5000;
    private TcpListener _listener;
    private Thread _thread;
    private volatile bool _running;

    public int Port { get; private set; }
    // 局域网连得进来(否则只听本机，要靠隧道)
    public bool Lan { get; private set; }
    public string Error { get; private set; }
    public bool Running => _running;

    public bool Start(int port)
    {
        Stop();
        Error = null;
        foreach (IPAddress address in new[] { IPAddress.Any, IPAddress.Loopback })
        {
            try
            {
                var listener = new TcpListener(address, port);
                listener.Start(16);
                _listener = listener;
                Lan = Equals(address, IPAddress.Any);
                break;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
            }
        }
        if (_listener == null) return false;
        Error = null;
        Port = port;
        _running = true;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "EmpireCraftTerminal" };
        _thread.Start();
        return true;
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // 关的时候出错不管
        }
        _listener = null;
        _thread = null;
    }

    private void AcceptLoop()
    {
        TcpListener listener = _listener;
        while (_running && listener != null)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch
            {
                if (!_running) return;
                Thread.Sleep(50);
                continue;
            }
            ThreadPool.QueueUserWorkItem(_ => Serve(client));
        }
    }

    private void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = TimeoutMs;
                client.SendTimeout = TimeoutMs;
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                Reply reply;
                try
                {
                    reply = Read(stream, out string method, out string path, out string token, out string body)
                        ? Handler?.Invoke(method, path, token, body) ?? Reply.Error(503, "not_ready")
                        : Reply.Error(400, "bad_request");
                }
                catch (Exception)
                {
                    reply = Reply.Error(500, "failed");
                }
                Write(stream, reply);
            }
        }
        catch
        {
            // 对面断了、超时：不管
        }
    }

    private static bool Read(Stream stream, out string method, out string path, out string token, out string body)
    {
        method = path = token = body = null;
        var head = new byte[MaxHead];
        int length = 0, end = -1;
        while (end < 0 && length < MaxHead)
        {
            int read = stream.Read(head, length, MaxHead - length);
            if (read <= 0) return false;
            length += read;
            for (int i = 3; i < length; i++)
                if (head[i] == '\n' && head[i - 1] == '\r' && head[i - 2] == '\n' && head[i - 3] == '\r')
                {
                    end = i + 1;
                    break;
                }
        }
        if (end < 0) return false;
        string[] lines = Encoding.ASCII.GetString(head, 0, end).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return false;
        string[] first = lines[0].Split(' ');
        if (first.Length < 2) return false;
        method = first[0].ToUpperInvariant();
        path = first[1];
        int query = path.IndexOf('?');
        if (query >= 0) path = path.Substring(0, query);
        int contentLength = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            string name = lines[i].Substring(0, colon).Trim().ToLowerInvariant(), value = lines[i].Substring(colon + 1).Trim();
            if (name == "content-length") int.TryParse(value, out contentLength);
            else if (name == "x-terminal-token") token = value;
        }
        if (contentLength < 0 || contentLength > MaxBody) return false;
        var content = new byte[contentLength];
        int have = Math.Min(contentLength, length - end);
        Array.Copy(head, end, content, 0, have);
        while (have < contentLength)
        {
            int read = stream.Read(content, have, contentLength - have);
            if (read <= 0) return false;
            have += read;
        }
        body = Encoding.UTF8.GetString(content);
        return true;
    }

    private static void Write(Stream stream, Reply reply)
    {
        byte[] body = reply.Body ?? Array.Empty<byte>();
        string status = reply.Status switch
        {
            200 => "200 OK", 400 => "400 Bad Request", 401 => "401 Unauthorized", 403 => "403 Forbidden",
            404 => "404 Not Found", 429 => "429 Too Many Requests", 503 => "503 Service Unavailable", _ => reply.Status + " Error"
        };
        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + (reply.Type ?? "text/plain") +
                                              "\r\nContent-Length: " + body.Length +
                                              "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        stream.Write(head, 0, head.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }
}

// 配对：游戏里给六位码，手机填对了换一个长期令牌(存在本机，下回直接进)。一部手机一个令牌、一个席位，可以配好几部
// (用户 2026-11-11 "最好可以兼容多人")。连错 MaxFails 回锁 LockSeconds 秒并换码。网络线程、主线程都会用，里头加锁
public sealed class TerminalPairing
{
    // 席位至多几个：Limit 是设置里的席位上限(Terminal_Max_Seats_Text，管理员在手机上也能改，见 TerminalHost.Admin)，
    // 不超过 MaxSeats。上限调低了，已配的席位照旧留着，只是满了不再收新的
    public const int MaxSeats = 16;
    private static volatile int _limit = 8;

    public static int Limit
    {
        get => _limit;
        set => _limit = Math.Max(1, Math.Min(MaxSeats, value));
    }

    private const int MaxFails = 5, LockSeconds = 60, MaxName = 16;
    private readonly object _gate = new();
    private readonly List<KeyValuePair<string, string>> _seats = new();
    private string _path, _code;
    private int _fails;
    private DateTime _lockedUntil = DateTime.MinValue;

    public string Code
    {
        get
        {
            lock (_gate) return _code ??= NewCode();
        }
    }

    // 配过的席位：令牌 → 称呼
    public List<KeyValuePair<string, string>> Seats()
    {
        lock (_gate) return new List<KeyValuePair<string, string>>(_seats);
    }

    public void Load(string path)
    {
        lock (_gate)
        {
            _path = path;
            _seats.Clear();
            try
            {
                if (path == null || !File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split((char)9);
                    if (parts[0].Length == 32 && _seats.Count < MaxSeats)
                        _seats.Add(new KeyValuePair<string, string>(parts[0], parts.Length > 1 ? parts[1] : ""));
                }
            }
            catch
            {
                // 读不了就当没配过
            }
        }
    }

    public void NewCodeNow()
    {
        lock (_gate) _code = NewCode();
    }

    public void Remove(string token)
    {
        lock (_gate)
        {
            _seats.RemoveAll(seat => seat.Key == token);
            Save();
        }
    }

    // 对了给令牌；不对给 null：locked 为真是连错太多、暂时不收，full 为真是席位满了
    public string TryPair(string code, string name, out bool locked, out bool full)
    {
        lock (_gate)
        {
            full = false;
            locked = DateTime.UtcNow < _lockedUntil;
            if (locked) return null;
            _code ??= NewCode();
            if (code == null || !Same(_code, code.Trim()))
            {
                if (++_fails >= MaxFails)
                {
                    _fails = 0;
                    _lockedUntil = DateTime.UtcNow.AddSeconds(LockSeconds);
                    _code = NewCode();
                    locked = true;
                }
                return null;
            }
            _fails = 0;
            if (_seats.Count >= Limit)
            {
                full = true;
                return null;
            }
            _code = NewCode();
            string token = Hex(16);
            _seats.Add(new KeyValuePair<string, string>(token, CleanName(name)));
            Save();
            return token;
        }
    }

    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var clean = new StringBuilder();
        foreach (char c in name.Trim())
            if (!char.IsControl(c) && c != '<' && c != '>' && clean.Length < MaxName) clean.Append(c);
        return clean.ToString().Trim();
    }

    private void Save()
    {
        try
        {
            if (_path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var lines = new List<string>();
            foreach (KeyValuePair<string, string> seat in _seats) lines.Add(seat.Key + (char)9 + seat.Value);
            File.WriteAllLines(_path, lines.ToArray());
        }
        catch
        {
            // 存不了：这次还能用，重启后要重配
        }
    }

    private static bool Same(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static string NewCode()
    {
        var bytes = new byte[4];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return (BitConverter.ToUInt32(bytes, 0) % 1000000u).ToString("D6");
    }

    private static string Hex(int count)
    {
        var bytes = new byte[count];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        var text = new StringBuilder(count * 2);
        foreach (byte value in bytes) text.Append(value.ToString("x2"));
        return text.ToString();
    }
}
