using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 管理员(用户 2026-11-13 "我需要终端开启的时候附赠一个管理员账号，只有输入特定的配对码才能进去，无论是否公开，所以我需要一个管理员入口。
// 管理员可以踢出某个玩家，修改某些配置，可以主动调时间流速不需要投票，也可以上帝模式看全图，直接看")，问答定：
//   · 独立身份，不占席位、不领君主；同一部手机可以另配一个普通席位，网页里两个身份点一下切换。领着君主的那个身份照旧有迷雾；
//   · 管理员码(TerminalAdminPass)终端一开就有，只在电脑上的终端窗口里显示。大厅的 /api/join、报给大厅的房间信息、二维码、
//     各席位的状态里都没有它；它也不会因为房间公开就被大厅替人填上——公开不公开，只有凭这个码才进得了管理员；
//   · 能办的：除名(令牌作废)、收回君主、直接定世界流速(不经流速之议，正在议的当场作罢)、暂停与继续、换普通配对码、
//     房间公开与否、房间名、席位上限。终端开关、端口、外网地址、大厅线路不开放：改了会把管理员自己的连接切断；
//   · 锁定时代(用户 2026-11-13 "管理员可以锁定时代")：科技至多到哪个时代，锁在工业时代以前立宪、政党也不新有，
//     已有的不收回；跟着存档走(见 TechnologySystem.EraLock)；
//   · 看的是没有迷雾的全图(借一个不领君主的 Seat 走 BuildMap，没领君主的席位本来就给全图)，各国的军照实兵数都看得见。
// 都在主线程办(命令队列与每秒那一趟)；网络线程只读备好的字符串
public static partial class TerminalHost
{
    private const int MaxAdminArmies = 400, MaxAdminNotes = 8;
    private static readonly TerminalAdminPass AdminPass = new();
    // 已登入的管理员：令牌 → 他那一份全图、军情、状态(Ruler 永远为空)
    private static readonly ConcurrentDictionary<string, Seat> AdminByToken = new();
    // 网页认席位用的记号(令牌的摘要，不把别人的令牌发给管理员)
    private static readonly Dictionary<string, string> SeatKeys = new();
    private static readonly HashSet<string> AdminIds = new()
        { "kick", "recall", "speed", "pause", "new_code", "room_open", "room_name", "seat_limit", "era_lock" };

    public static string AdminCode => AdminPass.Code;
    public static int AdminCount => AdminByToken.Count;
    public static int AdminOnline => AdminByToken.Values.Count(god => god.Watched);

    // 换管理员码(终端窗口上的按钮)：已登入的管理员一并作废，手机上要重新填码
    public static void RenewAdmin()
    {
        AdminPass.Renew();
        AdminByToken.Clear();
    }

    // 窗口要不要重画看的一截：管理员几位、在线几位
    private static string AdminLine() => "|A" + AdminCount + "/" + AdminOnline;

    private static void LoadAdmins()
    {
        AdminPass.Load(Path.Combine(ModClass.ModsConfigFolder(), "EmpireCraft.terminal.admin"));
        SyncAdmins();
    }

    // 令牌表变了(登入顶掉了最早的、换了码)：这边的跟上
    private static void SyncAdmins()
    {
        List<string> tokens = AdminPass.Tokens();
        foreach (string token in AdminByToken.Keys.ToList())
            if (!tokens.Contains(token)) AdminByToken.TryRemove(token, out _);
        foreach (string token in tokens) AdminByToken.GetOrAdd(token, made => new Seat { Token = made, Name = "admin" });
    }

    // ───────── 网络线程 ─────────
    private static TerminalServer.Reply AdminPair(string body)
    {
        string token = AdminPass.TryLogin(Field(body, "code"), out bool locked);
        if (token == null) return locked ? TerminalServer.Reply.Error(429, "locked") : TerminalServer.Reply.Error(403, "wrong_admin");
        SyncAdmins();
        if (AdminByToken.TryGetValue(token, out Seat god)) Interlocked.Exchange(ref god.LastClientTicks, DateTime.UtcNow.Ticks);
        return TerminalServer.Reply.Json("{\"token\":\"" + token + "\",\"admin\":true}");
    }

    // 管理员的请求：总图、山川、军情的取法同席位一样(网页不用两套)，状态与命令是管理员自己的
    private static TerminalServer.Reply AdminHandle(Seat god, string method, string path, string body)
    {
        Interlocked.Exchange(ref god.LastClientTicks, DateTime.UtcNow.Ticks);
        if (method == "GET" && path == "/api/state") return TerminalServer.Reply.Json(god.StateJson ?? "{\"loaded\":false,\"admin\":true}");
        if (method == "GET" && path == "/api/map") return TerminalServer.Reply.Json(god.MapJson ?? "{\"ready\":false}");
        if (method == "GET" && path == "/api/armies") return TerminalServer.Reply.Json(god.ArmyJson ?? "{\"ready\":false}");
        if (method == "POST" && path == "/api/terrain") return TerrainReply(god, body);
        if (method == "POST" && path == "/api/admin")
        {
            string id = Field(body, "id"), target = Field(body, "target"), choice = Field(body, "choice");
            if (id == null || !AdminIds.Contains(id) || target?.Length > 40 || choice?.Length > 60)
                return TerminalServer.Reply.Error(400, "bad_request");
            Commands.Enqueue(() => AdminDo(god, id, target, choice));
            return TerminalServer.Reply.Json("{\"ok\":true}");
        }
        // 这部手机退出管理员(令牌作废；别的管理员不受影响)
        if (method == "POST" && path == "/api/admin/logout")
        {
            AdminPass.Remove(god.Token);
            AdminByToken.TryRemove(god.Token, out _);
            return TerminalServer.Reply.Json("{\"ok\":true}");
        }
        return TerminalServer.Reply.Error(404, "not_found");
    }

    // ───────── 每秒(主线程) ─────────
    // 没人看着的管理员不备(登入那一下就算看着)；地图取样同席位轮着来，一秒里至多一份
    private static void RunAdmins(bool ready, float now, ref bool mapped)
    {
        foreach (Seat god in AdminByToken.Values)
        {
            if (!ready)
            {
                // 换世界、读档：全图、山川、军情都从头取
                god.ClearIntel();
                god.StateJson = WaitingJson(true);
                continue;
            }
            bool watched = god.Watched;
            if (!watched && god.StateJson != null && god.MapJson != null) continue;
            try
            {
                if (!mapped && now >= god.NextSight)
                {
                    god.NextSight = now + (watched ? SightSeconds : IdleSightSeconds);
                    BuildMap(god);
                    mapped = true;
                }
                GodArmies(god, now);
                if (now >= god.NextState || god.StateJson == null)
                {
                    god.NextState = now + StateSeconds;
                    god.StateJson = AdminState(god).ToString(Newtonsoft.Json.Formatting.None);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][终端] 备管理员状态出错: {exception.Message}");
            }
        }
    }

    // 各国的军都在图上，照实兵数(上帝视角不打折扣)；几秒看一回，没动就不算新军情
    private static void GodArmies(Seat god, float now)
    {
        if (god.MapCells == null || now < god.NextArmy && god.ArmyJson != null) return;
        god.NextArmy = now + LiveArmySeconds;
        var armies = new JArray();
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (armies.Count >= MaxAdminArmies) break;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !god.Slots.TryGetValue(kingdom.getID(), out int slot)) continue;
            foreach (CorpsData corps in kingdom.GetOrCreate().corps ?? new List<CorpsData>())
            {
                if (armies.Count >= MaxAdminArmies) break;
                if (corps.strength < 1f) continue;
                WorldTile at = CorpsSystem.Zone(corps)?.centerTile;
                if (at == null) continue;
                (double x, double y) = CellOf(god, at.x, at.y);
                var row = new JObject
                {
                    ["r"] = slot, ["n"] = Mathf.RoundToInt(corps.strength), ["x"] = x, ["y"] = y,
                    ["arm"] = CorpsArmSystem.Of(corps).ToString().ToLowerInvariant(), ["name"] = TerminalIntel.Strip(CorpsSystem.Name(corps))
                };
                if (corps.embarked) row["task"] = "sea";
                armies.Add(row);
            }
        }
        string text = armies.ToString(Newtonsoft.Json.Formatting.None);
        if (god.ArmyJson != null && text == god.ArmySeenText) return;
        god.ArmySeenText = text;
        god.ArmyStamp++;
        god.ArmyJson = new JObject
        {
            ["ready"] = true, ["stamp"] = god.ArmyStamp, ["at"] = MonthNow(), ["every"] = 0, ["armies"] = armies,
            ["writs"] = new JArray(), ["orders"] = new JObject()
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JObject AdminState(Seat god)
    {
        var state = new JObject
        {
            ["loaded"] = true, ["admin"] = true, ["v"] = Version, ["lang"] = LocalizedTextManager.instance?.language ?? "en",
            ["year"] = Date.getCurrentYear(), ["month"] = Date.getCurrentMonth(), ["speed"] = SpeedPercent, ["slowed"] = Slowed,
            ["paused"] = global::Config.paused, ["page"] = PageVersion(), ["mapStamp"] = god.MapStamp, ["armyStamp"] = god.ArmyStamp,
            ["monthNo"] = MonthNow(), ["code"] = Code, ["limit"] = TerminalPairing.Limit, ["maxLimit"] = TerminalPairing.MaxLimit,
            ["admins"] = AdminCount,
            ["room"] = new JObject
            {
                ["lobby"] = LobbyEnabled, ["online"] = LobbyOnline, ["open"] = RoomOpen, ["name"] = RoomName ?? "",
                ["auto"] = DefaultRoomName(), ["line"] = LobbyCustom ? "custom" : LineKey
            }
        };
        var seats = new JArray();
        foreach (Seat seat in Seats())
        {
            var row = new JObject { ["key"] = SeatKey(seat), ["name"] = SeatLabel(seat), ["online"] = seat.Watched };
            if (seat.Ruler != null && seat.Realm?.data != null)
            {
                row["ruler"] = seat.Ruler.getName();
                row["rid"] = seat.Ruler.getID().ToString();
                row["realm"] = NameOf(seat.Realm);
                row["kid"] = seat.Realm.getID().ToString();
                row["role"] = Role(seat.Realm);
                row["age"] = seat.Ruler.getAge();
                row["cities"] = seat.Realm.cities?.Count ?? 0;
                row["look"] = Look(seat.Ruler);
                row["color"] = Hex(RealmMiniMap.KingdomColor(seat.Realm));
            }
            seats.Add(row);
        }
        state["seats"] = seats;
        // 在世的君主(点图上的国看是谁、哪个席位领着)
        var rulers = new JArray();
        foreach (RulerInfo info in Rulers())
            rulers.Add(new JObject
            {
                ["id"] = info.Id.ToString(), ["kid"] = info.KingdomId.ToString(), ["ruler"] = info.Ruler, ["realm"] = info.Realm, ["kind"] = info.Kind,
                ["cities"] = info.Cities, ["age"] = info.Age, ["taken"] = info.TakenBy, ["tianxia"] = info.Tianxia, ["empire"] = info.Empire
            });
        state["rulers"] = rulers;
        // 办过的事回的话(网页按编号只提示新的)
        var notes = new JArray();
        foreach (Report note in god.Reports) notes.Add(new JObject { ["id"] = note.Id, ["k"] = note.Kind, ["a"] = note.Text });
        state["notes"] = notes;
        AddSpeed(state, god);
        AddEraLock(state);
        return state;
    }

    // 时代锁定(见 TechnologySystem.EraLock)：眼下锁在哪一档、最先进的文化到了哪一档、各档的名字与这一档的几项科技
    private static void AddEraLock(JObject state)
    {
        state["eraLock"] = TechnologySystem.EraLockTier;
        state["industrial"] = TechnologySystem.IndustrialTier;
        try
        {
            state["eraTop"] = TechnologySystem.TopEraTier();
        }
        catch (Exception)
        {
            // 文化表还没备好：不写
        }
        var eras = new JArray();
        foreach ((int tier, string name) in TechnologySystem.EraChoices())
            eras.Add(new JObject
            {
                ["t"] = tier, ["n"] = name,
                ["s"] = string.Join(LocalizedTextManager.instance?.language is "cz" or "ch" ? "、" : ", ",
                    TechnologySystem.Techs.Where(tech => tech.tier == tier).Take(4).Select(tech => TechnologySystem.GetTechName(tech.id)))
            });
        state["eras"] = eras;
    }

    // 锁定或解除时代：各位在位的君主都收到一条(电脑上终端窗口解除时 god 为空)
    public static void LockEra(int tier) => AdminEraLock(null, tier);

    private static void AdminEraLock(Seat god, int tier)
    {
        if (tier == TechnologySystem.EraLockTier) return;
        TechnologySystem.SetEraLock(tier);
        tier = TechnologySystem.EraLockTier;
        string name = tier > 0 ? TechnologySystem.EraNameOfTier(tier) : "";
        string text = tier <= 0 ? LM.Get("terminal_admin_era_unlock")
            : string.Format(LM.Get(TechnologySystem.EraLockBlocksModern ? "terminal_admin_era_lock_modern" : "terminal_admin_era_lock"), name);
        foreach (Seat seat in Seats())
            if (seat.Ruler != null)
            {
                Notify(seat, text);
                // 政令清单里立宪、制度的能办不能办跟着变
                seat.DecreesCache = null;
            }
        if (god != null) AdminNote(god, tier > 0 ? "era_lock" : "era_unlock", name);
        foreach (Seat other in AdminByToken.Values) other.NextState = 0f;
        LogService.LogInfo($"[EmpireCraft][终端] 时代锁定：{(tier > 0 ? name : "解除")}");
    }

    private static string SeatKey(Seat seat)
    {
        if (SeatKeys.TryGetValue(seat.Token, out string key)) return key;
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes("ec-seat:" + seat.Token));
            var text = new StringBuilder(12);
            for (int i = 0; i < 6; i++) text.Append(hash[i].ToString("x2"));
            key = text.ToString();
        }
        return SeatKeys[seat.Token] = key;
    }

    private static Seat SeatOfKey(string key) =>
        string.IsNullOrEmpty(key) ? null : Seats().FirstOrDefault(seat => SeatKey(seat) == key);

    // 回给管理员的话：k 是哪一种(网页照它出字)，a 是要嵌进去的名字或数
    private static void AdminNote(Seat god, string kind, string about = "")
    {
        god.Reports.Add(new Report { Id = ++_seq, Kind = kind, Text = about ?? "" });
        if (god.Reports.Count > MaxAdminNotes) god.Reports.RemoveRange(0, god.Reports.Count - MaxAdminNotes);
        god.NextState = 0f;
    }

    // ───────── 管理员下的令(主线程) ─────────
    private static void AdminDo(Seat god, string id, string target, string choice)
    {
        if (!AdminByToken.ContainsKey(god.Token)) return;
        switch (id)
        {
            case "kick":
            {
                Seat seat = SeatOfKey(target);
                if (seat == null)
                {
                    AdminNote(god, "gone");
                    return;
                }
                string label = SeatLabel(seat);
                Remove(seat);
                SeatKeys.Remove(seat.Token);
                AdminNote(god, "kicked", label);
                break;
            }
            case "recall":
            {
                Seat seat = SeatOfKey(target);
                if (seat == null)
                {
                    AdminNote(god, "gone");
                    return;
                }
                if (seat.Ruler == null) return;
                string who = seat.Ruler.getName();
                Assign(seat, -1);
                // 手机上写明是收回的(不是驾崩、失位)；人还在位，身后去向那一串(子孙、新君)不给
                seat.VacantReason = "recalled";
                ForgetLegacy(seat);
                seat.NextState = 0f;
                AdminNote(god, "recalled", SeatLabel(seat) + " · " + who);
                break;
            }
            case "speed":
            {
                if (!int.TryParse(choice, out int percent) || percent < 1 || percent > 100) return;
                AdminSpeed(percent);
                AdminNote(god, "speed", percent.ToString());
                break;
            }
            case "pause":
            {
                bool on = choice == "on";
                if (global::Config.paused != on)
                {
                    global::Config.paused = on;
                    // 电脑前的人也看得到是怎么停的、怎么走的(同原版的暂停键)
                    WorldTip.instance?.setText(on ? "game_paused" : "game_unpaused");
                }
                foreach (Seat seat in SeatByToken.Values) seat.NextState = 0f;
                AdminNote(god, on ? "paused" : "resumed");
                break;
            }
            case "new_code":
                Pairing.NewCodeNow();
                AdminNote(god, "new_code");
                break;
            case "room_open":
            {
                bool open = choice == "on";
                EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.SetSwitch("terminal_room_open_toggle", open);
                RoomOpen = open;
                AdminNote(god, open ? "room_open" : "room_private");
                break;
            }
            case "room_name":
            {
                string name = CleanRoomName(choice);
                EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.SetText("Terminal_Room_Name_Text", name);
                RoomName = name;
                AdminNote(god, "room_name", name.Length > 0 ? name : DefaultRoomName());
                break;
            }
            case "seat_limit":
            {
                if (!int.TryParse(choice, out int limit) || limit < 1 || limit > TerminalPairing.MaxLimit) return;
                EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.SetText("Terminal_Max_Seats_Text", limit.ToString());
                TerminalPairing.Limit = limit;
                AdminNote(god, "seat_limit", limit.ToString());
                break;
            }
            case "era_lock":
            {
                // 0 = 解除；锁定跟着存档走，世界没读好时不办
                if (!WorldReady() || !int.TryParse(choice, out int tier) || tier < 0 || tier > TechnologySystem.MaxEraTier) return;
                AdminEraLock(god, tier);
                break;
            }
        }
        foreach (Seat other in AdminByToken.Values) other.NextState = 0f;
        EmpireCraft.Scripts.UI.Windows.TerminalWindow.RefreshIfOpen();
    }

    // 直接定世界流速(不经流速之议)：正在议的当场作罢，各位在位的君主都收到一条
    private static void AdminSpeed(int percent)
    {
        bool voided = _vote != null;
        _vote = null;
        SpeedPercent = percent;
        try
        {
            EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.SetText("Terminal_Speed_Text", percent.ToString());
        }
        catch (Exception)
        {
            // 设置没存上：这一局照改后的走
        }
        string text = string.Format(LM.Get(voided ? "terminal_admin_speed_void" : "terminal_admin_speed"), percent);
        foreach (Seat seat in Seats())
            if (seat.Ruler != null) Notify(seat, text);
        TouchAll();
    }

    private static string CleanRoomName(string text)
    {
        var clean = new StringBuilder();
        foreach (char c in (text ?? "").Trim())
            if (!char.IsControl(c) && c != '<' && c != '>' && clean.Length < MaxRoomName) clean.Append(c);
        return clean.ToString().Trim();
    }
}
