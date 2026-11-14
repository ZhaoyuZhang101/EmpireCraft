using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace EmpireCraft.Server;

// 玩家账号：注册一个账号、设一个密码，登录后领一个席位。同一个账号在哪部手机上登录都是同一个席位(所领的君主跟着账号走)。
// 密码不存原文，只存加盐后算出来的摘要。存在 <存放目录>/accounts.json。网络线程与主线程都会用，里头加锁
public sealed class Accounts
{
    public sealed class Account
    {
        public string user = "";
        public string salt = "";
        public string hash = "";
        // 这个账号的席位令牌(席位被撤了就空着，下回登录再领)
        public string token = "";
        public string created = "";
        public string last = "";
    }

    private const int Rounds = 100000, MaxFails = 5, LockSeconds = 60;
    private readonly object _gate = new();
    private readonly Dictionary<string, Account> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int fails, DateTime until)> _locks = new(StringComparer.OrdinalIgnoreCase);
    private string _path;

    public void Load(string path)
    {
        lock (_gate)
        {
            _path = path;
            _byName.Clear();
            try
            {
                if (!File.Exists(path)) return;
                foreach (Account account in JsonConvert.DeserializeObject<List<Account>>(File.ReadAllText(path)) ?? new List<Account>())
                    if (!string.IsNullOrEmpty(account?.user)) _byName[account.user] = account;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("账号文件读不出来: " + exception.Message);
            }
        }
    }

    private void Save()
    {
        try
        {
            if (_path == null) return;
            File.WriteAllText(_path, JsonConvert.SerializeObject(_byName.Values.OrderBy(account => account.created).ToList(), Formatting.Indented));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("账号文件写不了: " + exception.Message);
        }
    }

    // 账号名：2~16 个字，汉字、字母、数字、下划线
    public static bool GoodName(string user) =>
        user != null && user.Length >= 2 && user.Length <= 16 && user.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static bool GoodPassword(string password) => password != null && password.Length >= 6 && password.Length <= 64;

    private static string Digest(string password, string salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(salt), Rounds, HashAlgorithmName.SHA256, 32));

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");

    // 注册。回的是出了什么岔子：null 成了、taken 名字有人用了
    public string Register(string user, string password, Func<string, string> issueSeat, out string token)
    {
        token = null;
        lock (_gate)
        {
            if (_byName.ContainsKey(user)) return "taken";
            token = issueSeat(user);
            if (token == null) return "full";
            string salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            _byName[user] = new Account { user = user, salt = salt, hash = Digest(password, salt), token = token, created = Now(), last = Now() };
            Save();
            return null;
        }
    }

    // 登录。null 成了；wrong 账号或密码不对；locked 连错太多先歇一分钟；full 席位满了
    public string Login(string user, string password, Func<string, bool> seatAlive, Func<string, string> issueSeat, out string token)
    {
        token = null;
        lock (_gate)
        {
            string key = user ?? "";
            if (_locks.TryGetValue(key, out var state) && DateTime.UtcNow < state.until) return "locked";
            if (!_byName.TryGetValue(key, out Account account) || !FixedTimeEquals(account.hash, Digest(password ?? "", account.salt)))
            {
                int fails = (_locks.TryGetValue(key, out state) ? state.fails : 0) + 1;
                _locks[key] = fails >= MaxFails ? (0, DateTime.UtcNow.AddSeconds(LockSeconds)) : (fails, DateTime.MinValue);
                if (_locks.Count > 4096) _locks.Clear();
                return fails >= MaxFails ? "locked" : "wrong";
            }
            _locks.Remove(key);
            if (string.IsNullOrEmpty(account.token) || !seatAlive(account.token))
            {
                account.token = issueSeat(account.user) ?? "";
                if (account.token.Length == 0)
                {
                    Save();
                    return "full";
                }
            }
            account.last = Now();
            Save();
            token = account.token;
            return null;
        }
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a ?? ""), Encoding.ASCII.GetBytes(b ?? ""));

    // 销号。回的是它原先的席位令牌(调用的一方接着把席位撤掉)；没有这个账号回 null
    public string Delete(string user)
    {
        lock (_gate)
        {
            if (user == null || !_byName.Remove(user, out Account account)) return null;
            Save();
            return account.token ?? "";
        }
    }

    public List<Account> List()
    {
        lock (_gate) return _byName.Values.OrderBy(account => account.created).ToList();
    }

    public int Count
    {
        get
        {
            lock (_gate) return _byName.Count;
        }
    }
}
