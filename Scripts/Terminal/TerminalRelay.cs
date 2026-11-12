using System;
using System.IO;
using System.IO.Compression;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EmpireCraft.Scripts.Terminal;

// 联机大厅的这一头(用户 2026-11-11 "我的服务器主要是需要玩家能够通过网页端查看当前有的房间, 加入""实际的数据都是玩家自己的主机处理的"
// "所以服务器既是中转站也是网页的入口")：游戏自己往外连到大厅服务器，登记一个房间；别人在网页上进这个房间，手机发的请求由服务器
// 顺着这条连接转过来，这里照常交给 TerminalHost.Handle，回复再顺着送回去。房主不用开端口、不用动防火墙和路由器。
//   · 连接是 WebSocket(ws:// 或 wss://)，只用 System.dll 里的套接字和 SslStream 自己读写帧，不引别的库(同 TerminalServer)；
//   · 连上先发 HELLO(房间的几行信息，见 TerminalHost.Lobby)，信息变了再发 UPDATE；
//   · 服务器发来文本帧 "REQ\n编号\n方法\n路径\n令牌\n正文"，回一个二进制帧 "RES 编号 状态 类型\n" 后跟正文；
//   · 服务器连上后若发来文本帧 "FEATURES\n…gzip…"，就是说它认得压过的回复：较大的文字回复(网页、国势、疆域、山川)先 gzip，
//     头一行改成 "RESZ 编号 状态 类型\n"(用户 2026-11-13 经国内中转出入境，一份山川 8 秒传不完、图一直黑着)；
//     旧的服务器不发 FEATURES，照旧不压；
//   · 每 PingSeconds 秒发一次心跳，DeadSeconds 秒没听到任何动静就断开重连；断了按 2、4、8……至多 30 秒重试；
//     被服务器回绝的(钥匙不对、这个地址开的房间太多、大厅满了)隔 RefusedSeconds 秒再试，免得白耗大厅的额度。
// 这个文件不碰游戏和 Unity：Handler 在线程池里调，只许读主线程备好的字符串、往队列里放命令(同 TerminalServer.Handler)
public sealed class TerminalRelay
{
    public Func<string, string, string, string, TerminalServer.Reply> Handler;
    // 连上时要报的房间信息(各行 键=值)
    public Func<string> Hello;

    // SendMs：连上以后一帧最多写这么久(线路慢时一份大回复要写好一阵，按连接时的 10 秒算会被当成断线)；PackFrom：多大的回复才压
    private const int ConnectMs = 10000, SendMs = 60000, PingSeconds = 25, DeadSeconds = 70, MaxFrame = 65536, MaxReply = 900 * 1024, MaxBackoff = 30,
        RefusedSeconds = 300, PackFrom = 1024;
    private readonly object _write = new();
    private Thread _thread, _pinger;
    private volatile bool _running;
    private TcpClient _client;
    private Stream _stream;
    private long _heardTicks;
    // 这一回是被服务器回绝的(不是网络断了)
    private volatile bool _refused;
    // 这一回连上的服务器认得压过的回复(它发来过 FEATURES gzip)
    private volatile bool _gzip;
    private Uri _uri;

    // off 没开、connecting 正在连、online 连上了、retry 断了等着重连
    public string State { get; private set; } = "off";
    // 最近一次连不上、被服务器回绝的缘由(连上了清空)
    public string Error { get; private set; }
    public bool Online => State == "online";

    public void Start(string url)
    {
        Stop();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _uri) || _uri.Scheme != "ws" && _uri.Scheme != "wss")
        {
            Error = "bad_url";
            return;
        }
        _running = true;
        State = "connecting";
        _thread = new Thread(Loop) { IsBackground = true, Name = "EmpireCraftLobby" };
        _thread.Start();
        _pinger = new Thread(PingLoop) { IsBackground = true, Name = "EmpireCraftLobbyPing" };
        _pinger.Start();
    }

    public void Stop()
    {
        _running = false;
        Close();
        _thread = null;
        _pinger = null;
        State = "off";
    }

    // 房间信息变了(主线程调)：没连上就算了，连上时 HELLO 会带最新的
    public void Update(string lines)
    {
        if (!Online) return;
        try
        {
            Send(0x1, Encoding.UTF8.GetBytes("UPDATE\n" + (lines ?? "")));
        }
        catch
        {
            Close();
        }
    }

    private void Close()
    {
        try
        {
            _client?.Close();
        }
        catch
        {
            // 已经断了：不管
        }
        _client = null;
        _stream = null;
    }

    private void Loop()
    {
        int backoff = 2;
        while (_running)
        {
            try
            {
                State = "connecting";
                Connect();
                Error = null;
                State = "online";
                backoff = 2;
                Send(0x1, Encoding.UTF8.GetBytes("HELLO\n" + (Hello?.Invoke() ?? "")));
                ReadLoop();
            }
            catch (Exception exception)
            {
                Error ??= exception.Message;
            }
            Close();
            if (!_running) break;
            State = "retry";
            int wait = _refused ? RefusedSeconds : backoff;
            _refused = false;
            for (int waited = 0; waited < wait * 10 && _running; waited++) Thread.Sleep(100);
            backoff = Math.Min(MaxBackoff, backoff * 2);
        }
        State = "off";
    }

    private void Connect()
    {
        int port = _uri.Port > 0 ? _uri.Port : _uri.Scheme == "wss" ? 443 : 80;
        var client = new TcpClient { NoDelay = true };
        IAsyncResult pending = client.BeginConnect(_uri.Host, port, null, null);
        if (!pending.AsyncWaitHandle.WaitOne(ConnectMs))
        {
            client.Close();
            throw new IOException("connect_timeout");
        }
        client.EndConnect(pending);
        client.SendTimeout = ConnectMs;
        Stream stream = client.GetStream();
        if (_uri.Scheme == "wss")
        {
            var secure = new SslStream(stream, false);
            secure.AuthenticateAsClient(_uri.Host);
            stream = secure;
        }
        _client = client;
        _stream = stream;
        _gzip = false;

        var nonce = new byte[16];
        using (var random = new RNGCryptoServiceProvider()) random.GetBytes(nonce);
        string host = _uri.IsDefaultPort ? _uri.Host : _uri.Host + ":" + _uri.Port;
        byte[] request = Encoding.ASCII.GetBytes("GET " + _uri.PathAndQuery + " HTTP/1.1\r\nHost: " + host +
                                                 "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " +
                                                 Convert.ToBase64String(nonce) + "\r\nSec-WebSocket-Version: 13\r\nUser-Agent: EmpireCraft-Terminal/" +
                                                 TerminalHost.Version + "\r\n\r\n");
        client.ReceiveTimeout = ConnectMs;
        stream.Write(request, 0, request.Length);
        stream.Flush();
        string head = ReadHead(stream);
        string status = head.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
        if (!status.Contains(" 101"))
        {
            // 服务器回绝：状态行里带着缘由(403 钥匙不对、429 开的房间太多……)
            Error = status.Length > 9 ? status.Substring(9).Trim() : status;
            _refused = true;
            throw new IOException("refused");
        }
        // 往后读不设超时(SslStream 读到一半超时会坏掉)，靠心跳那条线程看多久没动静
        client.ReceiveTimeout = 0;
        client.SendTimeout = SendMs;
        Interlocked.Exchange(ref _heardTicks, DateTime.UtcNow.Ticks);
    }

    private static string ReadHead(Stream stream)
    {
        var head = new byte[4096];
        int length = 0;
        while (length < head.Length)
        {
            int read = stream.ReadByte();
            if (read < 0) throw new IOException("closed");
            head[length++] = (byte)read;
            if (length >= 4 && head[length - 1] == '\n' && head[length - 2] == '\r' && head[length - 3] == '\n' && head[length - 4] == '\r')
                return Encoding.ASCII.GetString(head, 0, length);
        }
        throw new IOException("head_too_long");
    }

    private void ReadLoop()
    {
        Stream stream = _stream;
        var message = new MemoryStream();
        int kind = 0;
        while (_running && stream != null)
        {
            byte[] start = ReadExact(stream, 2);
            bool fin = (start[0] & 0x80) != 0, masked = (start[1] & 0x80) != 0;
            int opcode = start[0] & 0x0F;
            long length = start[1] & 0x7F;
            if (length == 126)
            {
                byte[] more = ReadExact(stream, 2);
                length = more[0] << 8 | more[1];
            }
            else if (length == 127)
            {
                byte[] more = ReadExact(stream, 8);
                length = 0;
                for (int i = 0; i < 8; i++) length = length << 8 | more[i];
            }
            if (length < 0 || length > MaxFrame) throw new IOException("frame_too_long");
            byte[] mask = masked ? ReadExact(stream, 4) : null;
            byte[] payload = ReadExact(stream, (int)length);
            if (mask != null)
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
            Interlocked.Exchange(ref _heardTicks, DateTime.UtcNow.Ticks);
            switch (opcode)
            {
                case 0x8:
                    // 服务器要断：带着缘由(4000 别处顶替、4029 房间开多了……)
                    if (payload.Length > 2) Error = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
                    // 4000 是自己重连顶掉了旧连接，不算回绝
                    if (payload.Length >= 2 && (payload[0] << 8 | payload[1]) is >= 4001 and <= 4999) _refused = true;
                    return;
                case 0x9:
                    Send(0xA, payload);
                    continue;
                case 0xA:
                    continue;
                case 0x1:
                case 0x2:
                    kind = opcode;
                    message.SetLength(0);
                    break;
                case 0x0:
                    break;
                default:
                    continue;
            }
            if (message.Length + payload.Length > MaxFrame) throw new IOException("message_too_long");
            message.Write(payload, 0, payload.Length);
            if (!fin) continue;
            if (kind == 0x1)
            {
                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                if (text.StartsWith("FEATURES\n", StringComparison.Ordinal)) _gzip = text.IndexOf("gzip", StringComparison.Ordinal) > 0;
                else ThreadPool.QueueUserWorkItem(_ => Answer(text));
            }
            message.SetLength(0);
        }
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        int have = 0;
        while (have < count)
        {
            int read = stream.Read(buffer, have, count - have);
            if (read <= 0) throw new IOException("closed");
            have += read;
        }
        return buffer;
    }

    // "REQ\n编号\n方法\n路径\n令牌\n正文" → 交给 Handler → "RES 编号 状态 类型\n" + 正文
    private void Answer(string text)
    {
        string[] parts = text.Split(new[] { '\n' }, 6);
        if (parts.Length < 5 || parts[0] != "REQ") return;
        string id = parts[1];
        TerminalServer.Reply reply;
        try
        {
            reply = Handler?.Invoke(parts[2], parts[3], parts[4].Length == 0 ? null : parts[4], parts.Length > 5 ? parts[5] : "") ??
                    TerminalServer.Reply.Error(503, "not_ready");
        }
        catch (Exception)
        {
            reply = TerminalServer.Reply.Error(500, "failed");
        }
        byte[] body = reply.Body ?? Array.Empty<byte>();
        bool packed = false;
        if (_gzip && body.Length > PackFrom && Packable(reply.Type))
        {
            byte[] zipped = Gzip(body);
            if (zipped != null && zipped.Length < body.Length)
            {
                body = zipped;
                packed = true;
            }
        }
        if (body.Length > MaxReply)
        {
            reply = TerminalServer.Reply.Error(500, "too_large");
            body = reply.Body;
            packed = false;
        }
        byte[] head = Encoding.ASCII.GetBytes((packed ? "RESZ " : "RES ") + id + " " + reply.Status + " " + (reply.Type ?? "text/plain") + "\n");
        var frame = new byte[head.Length + body.Length];
        Buffer.BlockCopy(head, 0, frame, 0, head.Length);
        Buffer.BlockCopy(body, 0, frame, head.Length, body.Length);
        try
        {
            Send(0x2, frame);
        }
        catch
        {
            Close();
        }
    }

    // 文字的才压(网页、JSON)；图片本来就压过，再压白费工夫
    private static bool Packable(string type)
    {
        if (string.IsNullOrEmpty(type)) return false;
        return type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 ||
               type.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // 压不成(运行库里缺了压缩用的本机库之类)就回 null，照原样发
    private static byte[] Gzip(byte[] data)
    {
        try
        {
            using var output = new MemoryStream(data.Length / 3 + 64);
            using (var zip = new GZipStream(output, CompressionMode.Compress, true)) zip.Write(data, 0, data.Length);
            return output.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 发一帧(客户端发的帧必须带掩码)
    private void Send(int opcode, byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        int extra = payload.Length < 126 ? 0 : payload.Length <= 0xFFFF ? 2 : 8;
        var frame = new byte[2 + extra + 4 + payload.Length];
        frame[0] = (byte)(0x80 | opcode);
        if (extra == 0) frame[1] = (byte)(0x80 | payload.Length);
        else if (extra == 2)
        {
            frame[1] = 0x80 | 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
        }
        else
        {
            frame[1] = 0x80 | 127;
            long length = payload.Length;
            for (int i = 0; i < 8; i++) frame[2 + i] = (byte)(length >> (56 - 8 * i));
        }
        int at = 2 + extra;
        var mask = new byte[4];
        using (var random = new RNGCryptoServiceProvider()) random.GetBytes(mask);
        Buffer.BlockCopy(mask, 0, frame, at, 4);
        for (int i = 0; i < payload.Length; i++) frame[at + 4 + i] = (byte)(payload[i] ^ mask[i & 3]);
        lock (_write)
        {
            Stream stream = _stream;
            if (stream == null) throw new IOException("closed");
            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }
    }

    private void PingLoop()
    {
        int seconds = 0;
        while (_running)
        {
            Thread.Sleep(1000);
            if (!Online) continue;
            if ((DateTime.UtcNow.Ticks - Interlocked.Read(ref _heardTicks)) / TimeSpan.TicksPerSecond > DeadSeconds)
            {
                // 太久没动静：断开，读的那条线程随即出错、重连
                Close();
                continue;
            }
            if (++seconds < PingSeconds) continue;
            seconds = 0;
            try
            {
                Send(0x9, null);
            }
            catch
            {
                Close();
            }
        }
    }
}
