using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.Terminal;

// 管理员附身(用户 2026-11-18 "管理员随时切换到任何没人玩的国家去操作")：
//   · 每位管理员有一个专用的附身席位(Seat.Possessor = 他的管理员令牌)，放在 SeatByToken 里，奏报、眼界、军令、外交、决议都照普通席位办；
//     不在配对表里(Seats() 不列、不占席位与同时在线的名额、不投流速之议、不随存档记)；
//   · 只能附身没人领着的国：普通席位领着的不能抢，别的管理员附身着的也不行；
//   · 附身着的国有玩家来领：让给玩家(附身席位腾空，写明是让出来的)，管理员那头收到一条；
//   · 别国看它照没人领算(HostOf 不认附身席位)：递来的外交照常由朝廷自己裁，不挂着等管理员；
//   · 回到上帝视角不放手(还能切回来接着办)；附身另一国就是换过去；放手是交还朝廷自己办。
//     管理员令牌作废(退出、换码)、管理员与附身席位都十分钟没来取东西，就自己放手
// 网页拿附身席位的令牌直接走席位那一套接口(/api/state、/api/decree ……)，不另开一套
public static partial class TerminalHost
{
    private const double PossessIdleMinutes = 10d;

    // ───────── 网络线程 ─────────
    // POST /api/admin/possess {id: 君主的人物编号} 或 {kid: 国的编号}：附身(已附身别国就换过去)；{id: -1}：放手。回附身席位的令牌。
    // 管理员状态里的君主名单有上限(MaxRulers)，名单外的国网页只知道国的编号，由这边找它眼下的君主
    private static TerminalServer.Reply PossessReply(Seat god, string body)
    {
        long actorId = -1, kingdomId = -1;
        if (!long.TryParse(Field(body, "id"), out actorId) && !long.TryParse(Field(body, "kid"), out kingdomId))
            return TerminalServer.Reply.Error(400, "bad_request");
        string seatToken = god.PossessToken;
        if (seatToken == null)
        {
            Interlocked.CompareExchange(ref god.PossessToken, NewPossessToken(), null);
            seatToken = god.PossessToken;
        }
        Commands.Enqueue(() => Possess(god, kingdomId >= 0 ? KingOf(kingdomId) : actorId));
        return TerminalServer.Reply.Json("{\"ok\":true,\"token\":\"" + seatToken + "\"}");
    }

    // 同配对的令牌一样是 32 位十六进制
    private static string NewPossessToken()
    {
        byte[] bytes = new byte[16];
        using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        var text = new StringBuilder(32);
        foreach (byte b in bytes) text.Append(b.ToString("x2"));
        return text.ToString();
    }

    // ───────── 主线程 ─────────
    private static Seat PossessionOf(Seat god) =>
        god?.PossessToken != null && SeatByToken.TryGetValue(god.PossessToken, out Seat body) && body.Possessor == god.Token ? body : null;

    private static long KingOf(long kingdomId)
    {
        if (!WorldReady()) return long.MaxValue;
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && kingdom.getID() == kingdomId)
                return kingdom.king != null && kingdom.king.isAlive() ? kingdom.king.getID() : long.MaxValue;
        return long.MaxValue;
    }

    private static void Possess(Seat god, long actorId)
    {
        if (!AdminByToken.ContainsKey(god.Token)) return;
        Seat body = PossessionOf(god);
        if (actorId < 0)
        {
            if (body == null) return;
            string realm = body.Realm?.data != null ? NameOf(body.Realm) : "";
            ReleasePossession(body);
            AdminNote(god, "unpossessed", realm);
            return;
        }
        if (!WorldReady()) return;
        Actor actor = World.world.units.get(actorId);
        if (body != null && body.RulerId == actorId && body.Ruler != null)
        {
            Interlocked.Exchange(ref body.LastClientTicks, DateTime.UtcNow.Ticks);
            AdminNote(god, "possessed", NameOf(body.Realm));
            return;
        }
        body ??= new Seat { Token = god.PossessToken, Name = god.Name, Possessor = god.Token };
        // 有人领着(普通席位、别的管理员附身着)、不在位了：不办
        if (!Free(actor, body))
        {
            Seat holder = actor == null ? null : TakenBy(actor.getID(), body);
            AdminNote(god, holder != null ? "possess_taken" : "possess_gone",
                holder?.Realm?.data != null ? NameOf(holder.Realm) : actor?.kingdom?.data != null ? NameOf(actor.kingdom) : "");
            return;
        }
        SeatByToken[body.Token] = body;
        // 换到别国：先前那一国的奏报、眼界、军令都清掉(同席位改领一位君主)
        ForgetLegacy(body);
        if (!Assign(body, actorId))
        {
            AdminNote(god, "possess_gone", "");
            return;
        }
        body.VacantReason = "none";
        body.FormerRuler = body.FormerRealm = "";
        // 网页拿到令牌就来取：先算这一份，不让它等一整轮
        Interlocked.Exchange(ref body.LastClientTicks, DateTime.UtcNow.Ticks);
        body.NextSight = body.NextState = 0f;
        AdminNote(god, "possessed", NameOf(body.Realm));
        NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][终端] 管理员附身 {NameOf(body.Realm)}");
    }

    // 放手：君主交还朝廷自己办，附身席位撤掉(网页再拿这个令牌来取就是 401，自己回上帝视角)
    private static void ReleasePossession(Seat body)
    {
        body.Vacate("none");
        ForgetLegacy(body);
        SeatByToken.TryRemove(body.Token, out _);
    }

    // 普通席位来领这位君主(Assign 里)：管理员附身着的让出来
    private static void YieldPossession(long actorId)
    {
        foreach (Seat body in SeatByToken.Values.ToList())
        {
            if (body.Possessor == null || body.RulerId != actorId) continue;
            string realm = body.Realm?.data != null ? NameOf(body.Realm) : "";
            string ruler = body.Ruler?.getName() ?? "";
            body.Vacate("yielded");
            body.FormerRuler = ruler;
            body.FormerRealm = realm;
            ForgetLegacy(body);
            body.NextState = 0f;
            if (AdminByToken.TryGetValue(body.Possessor, out Seat god)) AdminNote(god, "yielded", realm);
        }
    }

    // 每秒(RunAdmins 里)：管理员令牌作废了的、管理员与附身席位都许久没来的，放手
    private static void RunPossessions()
    {
        DateTime now = DateTime.UtcNow;
        foreach (Seat body in SeatByToken.Values.ToList())
        {
            if (body.Possessor == null) continue;
            bool known = AdminByToken.TryGetValue(body.Possessor, out Seat god);
            if (known && (Seen(god) || Seen(body))) continue;
            ReleasePossession(body);
            if (known) AdminNote(god, "possess_idle");
        }
        bool Seen(Seat seat) => (now - new DateTime(Interlocked.Read(ref seat.LastClientTicks), DateTimeKind.Utc)).TotalMinutes < PossessIdleMinutes;
    }

    // 管理员状态里：眼下附身着哪一国(网页据此显示"附身中"、切过去)；各位君主谁被管理员附身着
    private static void AddPossess(JObject state, Seat god, JArray rulers)
    {
        Seat mine = PossessionOf(god);
        if (mine != null)
        {
            var row = new JObject { ["token"] = mine.Token, ["vacant"] = mine.Ruler == null };
            if (mine.Ruler != null && mine.Realm?.data != null)
            {
                row["kid"] = mine.Realm.getID().ToString();
                row["rid"] = mine.Ruler.getID().ToString();
                row["realm"] = NameOf(mine.Realm);
                row["ruler"] = mine.Ruler.getName();
                row["role"] = Role(mine.Realm);
            }
            state["possess"] = row;
        }
        var held = new Dictionary<string, bool>();
        foreach (Seat body in SeatByToken.Values)
            if (body.Possessor != null && body.RulerId >= 0) held[body.RulerId.ToString()] = body == mine;
        if (held.Count == 0) return;
        foreach (JToken ruler in rulers)
            if (held.TryGetValue((string)ruler["id"] ?? "", out bool own)) ruler["held"] = own ? "self" : "admin";
    }
}
