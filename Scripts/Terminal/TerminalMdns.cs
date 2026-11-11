using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace EmpireCraft.Scripts.Terminal;

// 终端的固定名字(用户 2026-11-11 "哪怕添加到桌面但是由于 ip 不固定换台电脑不就进不去了"，问答选"固定名字")：
// 手机桌面图标记的是地址，电脑的局域网地址一变、或者换一台电脑开游戏，图标就指错了地方。这里让游戏在局域网里应答一个
// 固定的名字 empirecraft.local(多播 DNS，苹果、安卓 12 起、Windows 都认)：谁开着终端谁应答，告诉对方自己在这块网卡上的地址。
// 每块网卡各开一个套接字(5353 端口与系统里别的应答器共用)，收到问 empirecraft.local 的 A 记录就答；每半分钟重新数一遍网卡。
// 只管 IPv4。同一个网络里两台电脑同时开终端会各答各的——那是用法上要避免的。不碰游戏和 Unity
public sealed class TerminalMdns
{
    public const string Host = "empirecraft";
    public const string FullName = Host + ".local";
    private const int MdnsPort = 5353, RescanSeconds = 30, AnswerSeconds = 120, LegacySeconds = 10;
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");

    private readonly object _gate = new();
    private readonly Dictionary<string, Socket> _sockets = new();
    private volatile bool _running;

    public bool Running => _running;

    // 正在几块网卡上应答
    public int Listening
    {
        get
        {
            lock (_gate) return _sockets.Count;
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        new Thread(Manage) { IsBackground = true, Name = "EmpireCraftMdns" }.Start();
    }

    public void Stop()
    {
        _running = false;
        lock (_gate)
        {
            foreach (Socket socket in _sockets.Values) Close(socket);
            _sockets.Clear();
        }
    }

    private void Manage()
    {
        while (_running)
        {
            try
            {
                Rescan();
            }
            catch
            {
                // 这一轮数不成，下一轮再数
            }
            for (int i = 0; i < RescanSeconds * 2 && _running; i++) Thread.Sleep(500);
        }
    }

    private void Rescan()
    {
        HashSet<string> present = LocalAddresses();
        lock (_gate)
        {
            if (!_running) return;
            foreach (string gone in _sockets.Keys.Where(address => !present.Contains(address)).ToList())
            {
                Close(_sockets[gone]);
                _sockets.Remove(gone);
            }
            foreach (string address in present)
            {
                if (_sockets.ContainsKey(address)) continue;
                IPAddress local = IPAddress.Parse(address);
                Socket socket = Open(local);
                if (socket == null) continue;
                _sockets[address] = socket;
                new Thread(() => Listen(socket, local)) { IsBackground = true, Name = "EmpireCraftMdns " + address }.Start();
            }
        }
    }

    private static Socket Open(IPAddress local)
    {
        Socket socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(local, MdnsPort));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, local));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.ReceiveTimeout = 2000;
            return socket;
        }
        catch
        {
            Close(socket);
            return null;
        }
    }

    private static void Close(Socket socket)
    {
        try
        {
            socket?.Close();
        }
        catch
        {
            // 关的时候出错不管
        }
    }

    private void Listen(Socket socket, IPAddress local)
    {
        var buffer = new byte[1500];
        while (_running)
        {
            try
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int length = socket.ReceiveFrom(buffer, ref from);
                var remote = (IPEndPoint)from;
                // 不是从 5353 端口问来的(老式的单播问法)：原样带上问题、直接回给它；否则回到多播组
                bool legacy = remote.Port != MdnsPort;
                byte[] answer = Answer(buffer, length, local, legacy);
                if (answer != null) socket.SendTo(answer, legacy ? remote : new IPEndPoint(Group, MdnsPort));
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
            {
                // 没人问：接着等
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                if (!_running) return;
                lock (_gate)
                    if (!_sockets.ContainsValue(socket)) return;
                Thread.Sleep(200);
            }
        }
    }

    // 本机各块网卡上的 IPv4 地址
    private static HashSet<string> LocalAddresses()
    {
        var found = new HashSet<string>();
        try
        {
            foreach (NetworkInterface card in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (card.OperationalStatus != OperationalStatus.Up || card.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation info in card.GetIPProperties().UnicastAddresses)
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(info.Address))
                        found.Add(info.Address.ToString());
            }
        }
        catch
        {
            // 数不了网卡：用下面的
        }
        if (found.Count > 0) return found;
        try
        {
            foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) found.Add(address.ToString());
        }
        catch
        {
            // 查不到就不应答
        }
        return found;
    }

    // 这个包是不是在问 empirecraft.local 的地址：是就给出应答包(答 local 这个地址)，不是给 null
    public static byte[] Answer(byte[] query, int length, IPAddress local, bool legacy)
    {
        if (query == null || length < 12 || (query[2] & 0x80) != 0) return null;
        int questions = (query[4] << 8) | query[5], at = 12;
        bool wanted = false;
        for (int i = 0; i < questions; i++)
        {
            string name = ReadName(query, length, ref at);
            if (name == null || at + 4 > length) return null;
            int type = (query[at] << 8) | query[at + 1];
            at += 4;
            if ((type == 1 || type == 255) && string.Equals(name, FullName, StringComparison.OrdinalIgnoreCase)) wanted = true;
        }
        if (!wanted) return null;

        var packet = new List<byte>(64);
        packet.Add(legacy ? query[0] : (byte)0);
        packet.Add(legacy ? query[1] : (byte)0);
        packet.AddRange(new byte[] { 0x84, 0x00, 0x00, (byte)(legacy ? 1 : 0), 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 });
        byte[] encoded = EncodeName();
        if (legacy)
        {
            packet.AddRange(encoded);
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 });
        }
        packet.AddRange(encoded);
        int seconds = legacy ? LegacySeconds : AnswerSeconds;
        packet.AddRange(new byte[]
        {
            0x00, 0x01, (byte)(legacy ? 0x00 : 0x80), 0x01, (byte)(seconds >> 24), (byte)(seconds >> 16), (byte)(seconds >> 8), (byte)seconds,
            0x00, 0x04
        });
        packet.AddRange(local.GetAddressBytes());
        return packet.ToArray();
    }

    private static byte[] EncodeName()
    {
        var bytes = new List<byte>();
        foreach (string label in FullName.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0);
        return bytes.ToArray();
    }

    // 读一个名字(标签一段一段，允许压缩指针)；at 停在名字之后
    private static string ReadName(byte[] data, int length, ref int at)
    {
        var name = new StringBuilder();
        int cursor = at, jumps = 0;
        bool jumped = false;
        while (true)
        {
            if (cursor >= length) return null;
            int size = data[cursor];
            if (size == 0)
            {
                cursor++;
                break;
            }
            if ((size & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= length || ++jumps > 8) return null;
                if (!jumped) at = cursor + 2;
                jumped = true;
                cursor = ((size & 0x3F) << 8) | data[cursor + 1];
                continue;
            }
            if (size > 63 || cursor + 1 + size > length) return null;
            if (name.Length > 0) name.Append('.');
            name.Append(Encoding.ASCII.GetString(data, cursor + 1, size));
            cursor += size + 1;
        }
        if (!jumped) at = cursor;
        return name.ToString();
    }
}
