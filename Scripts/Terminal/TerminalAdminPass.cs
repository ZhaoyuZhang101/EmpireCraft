using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EmpireCraft.Scripts.Terminal;

// 管理员码与管理员令牌(见 TerminalHost.Admin)。同 TerminalPairing 一样不碰游戏，网络线程、主线程都会用，里头加锁。
//   · 码是八位字母数字(去掉了 0 O 1 I 这类看着像的)，同六位数字的普通配对码分开；终端一开就有，存在设置目录，下回开游戏还是它；
//   · 码填对了换一个长期令牌。码不因用过就换(管理员可以不止一部手机)，只在电脑前点"换管理员码"时换，换了已发的令牌一并作废；
//   · 连错 MaxFails 回锁 LockSeconds 秒。码不跟着换：四十位的码猜不着，换了反倒要管理员回电脑前再看一眼
public sealed class TerminalAdminPass
{
    public const int MaxAdmins = 4;
    private const int MaxFails = 5, LockSeconds = 60, CodeLength = 8;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly object _gate = new();
    // 先登入的在前；满了顶掉最早的
    private readonly List<string> _tokens = new();
    private string _path, _code;
    private int _fails;
    private DateTime _lockedUntil = DateTime.MinValue;

    // 给人看的写法：四位一组，中间一道横(填的时候有没有横、大小写都认)
    public string Code
    {
        get
        {
            lock (_gate)
            {
                Ensure();
                return _code.Substring(0, 4) + "-" + _code.Substring(4);
            }
        }
    }

    public List<string> Tokens()
    {
        lock (_gate) return new List<string>(_tokens);
    }

    public void Load(string path)
    {
        lock (_gate)
        {
            _path = path;
            _code = null;
            _tokens.Clear();
            try
            {
                if (path != null && File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    if (lines.Length > 0 && Valid(lines[0].Trim())) _code = lines[0].Trim();
                    for (int i = 1; i < lines.Length && _tokens.Count < MaxAdmins; i++)
                    {
                        string token = lines[i].Trim();
                        if (token.Length == 32 && IsHex(token) && !_tokens.Contains(token)) _tokens.Add(token);
                    }
                }
            }
            catch
            {
                // 读不了：下面重新生成一个码
            }
            if (_code == null) _tokens.Clear();
            Ensure();
        }
    }

    public bool Has(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_gate) return _tokens.Contains(token);
    }

    // 对了给令牌；不对给 null，locked 为真是连错太多、暂时不收
    public string TryLogin(string code, out bool locked)
    {
        lock (_gate)
        {
            locked = DateTime.UtcNow < _lockedUntil;
            if (locked) return null;
            Ensure();
            if (!Same(_code, Normalize(code)))
            {
                if (++_fails >= MaxFails)
                {
                    _fails = 0;
                    _lockedUntil = DateTime.UtcNow.AddSeconds(LockSeconds);
                    locked = true;
                }
                return null;
            }
            _fails = 0;
            string token = Hex(16);
            while (_tokens.Count >= MaxAdmins) _tokens.RemoveAt(0);
            _tokens.Add(token);
            Save();
            return token;
        }
    }

    // 换管理员码：已登入的一并作废
    public void Renew()
    {
        lock (_gate)
        {
            _code = NewCode();
            _tokens.Clear();
            _fails = 0;
            _lockedUntil = DateTime.MinValue;
            Save();
        }
    }

    // 这部手机退出管理员
    public void Remove(string token)
    {
        lock (_gate)
        {
            if (_tokens.Remove(token)) Save();
        }
    }

    // 填进来的码：大写、只留码里会有的字(横、空格不算)
    public static string Normalize(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var clean = new StringBuilder(CodeLength);
        foreach (char c in code.ToUpperInvariant())
            if (Alphabet.IndexOf(c) >= 0 && clean.Length < 16) clean.Append(c);
        return clean.ToString();
    }

    private void Ensure()
    {
        if (_code != null) return;
        _code = NewCode();
        Save();
    }

    private void Save()
    {
        try
        {
            if (_path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var lines = new List<string> { _code ?? "" };
            lines.AddRange(_tokens);
            File.WriteAllLines(_path, lines.ToArray());
        }
        catch
        {
            // 存不了：这次还能用，重启后换码
        }
    }

    private static bool Valid(string code)
    {
        if (code == null || code.Length != CodeLength) return false;
        foreach (char c in code)
            if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    private static bool IsHex(string text)
    {
        foreach (char c in text)
            if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }

    private static bool Same(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static string NewCode()
    {
        var bytes = new byte[CodeLength];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        var code = new StringBuilder(CodeLength);
        // 32 个字、一字节取低五位：各字一样可能
        foreach (byte value in bytes) code.Append(Alphabet[value & 31]);
        return code.ToString();
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
