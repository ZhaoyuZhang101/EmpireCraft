using System;
using System.Collections.Generic;
using System.Threading;

namespace EmpireCraft.Scripts.Terminal;

// 独立服务器(.server，不带游戏的开服工具)接进终端主机的几处入口。
// 游戏里没人设 ServerRoutes，这些都不起作用：手机照旧凭配对码进，管理员照旧凭管理员码进
public static partial class TerminalHost
{
    // 独立服务器自己的接口(账号、上传剧本、托管编辑器网页)：回 null 就是它不管，照常往下走
    public static Func<string, string, string, string, TerminalServer.Reply?> ServerRoutes;

    // 账号核对过了：给这位玩家一个席位。满了回 null
    public static string IssueSeat(string name, out bool full)
    {
        string token = Pairing.Issue(name, out full);
        if (token != null) SeatByToken.GetOrAdd(token, made => new Seat { Token = made, Name = name ?? "" });
        return token;
    }

    // 端口没开成时的原因(开着、还没试过都是 null)
    public static string PortError => _server != null && !_server.Running ? _server.Error : null;

    // 同时在线(独立服务器)：近 OnlineSeconds 秒里来取过东西的席位算在线。手机锁屏、关了网页，过了这个时辰位置就让出来
    public const int OnlineSeconds = 60;

    private static bool Recent(Seat seat) =>
        DateTime.UtcNow.Ticks - Interlocked.Read(ref seat.LastClientTicks) < TimeSpan.FromSeconds(OnlineSeconds).Ticks;

    public static bool SeatOnline(string token) => !string.IsNullOrEmpty(token) && SeatByToken.TryGetValue(token, out Seat seat) && Recent(seat);

    public static int OnlineSeats()
    {
        int online = 0;
        foreach (Seat seat in SeatByToken.Values)
            if (Recent(seat)) online++;
        return online;
    }

    public static bool HasSeat(string token) => !string.IsNullOrEmpty(token) && SeatByToken.ContainsKey(token);

    // 销号：席位撤掉，所领的君主交还(要在主线程调)
    public static void DropSeat(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        if (SeatByToken.TryRemove(token, out Seat seat))
        {
            seat.Vacate("none");
            DropBallot(token);
        }
        Pairing.Remove(token);
    }

    public static bool IsAdmin(string token) => !string.IsNullOrEmpty(token) && AdminPass.Has(token);

    // 管理员的账号密码核对过了：给一个管理员令牌
    public static string IssueAdmin()
    {
        string token = AdminPass.TryLogin(AdminPass.Code, out _);
        if (token != null) SyncAdmins();
        return token;
    }

    // 凭管理员码登入(没设账号密码时)
    public static string AdminLoginCode(string code, out bool locked)
    {
        string token = AdminPass.TryLogin(code, out locked);
        if (token != null) SyncAdmins();
        return token;
    }

    // 存档：各席位眼下领着哪位君主(席位令牌 → 人物编号)
    public static Dictionary<string, long> SeatRulers()
    {
        var rulers = new Dictionary<string, long>();
        foreach (Seat seat in SeatByToken.Values)
            if (seat.RulerId >= 0) rulers[seat.Token] = seat.RulerId;
        return rulers;
    }

    // 读档：把各席位的君主交还(要在主线程调，世界读完了才行)。眼下已经领着君主的席位不动；回交还了几个
    public static int RestoreSeatRulers(IDictionary<string, long> rulers)
    {
        int restored = 0;
        if (rulers == null) return 0;
        foreach (KeyValuePair<string, long> saved in rulers)
            if (SeatByToken.TryGetValue(saved.Key, out Seat seat) && seat.RulerId < 0 && Assign(seat, saved.Value)) restored++;
        return restored;
    }
}
