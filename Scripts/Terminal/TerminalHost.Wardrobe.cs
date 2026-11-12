using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 衣冠(用户 2026-11-12 "玩家可以去购买衣服，不同朝代和时期的服饰也要适配，以及官服")。立绘的衣着、冠帽、发式是一件一件的
// (Terminal/wardrobe.json，由 Tools/IconForge/terminal_portraits.py 从立绘的说明里抄出来)：每件写着是哪个文化的、通行在哪几个时期、
// 是哪一类(君主礼服、官服、常服、戎装、僧道、平民、女装)。只在终端这头：
//   · 别人穿什么不用管——手机按各人的文化、时期、身份自己配(见网页的 lookParts)；
//   · 君主自己可以买衣服换上：花的是君主的私财(同雇暗探)。价钱看是哪一类；别的文化的贵一倍；已经过时的古装贵五成；
//     比本国眼下的时期还新的买不到；男女的衣裳不混穿；
//   · 买来的进衣柜，随时换，也可以换回本来的装束。别的席位看这位君主，看到的就是他穿着的这一身。
// 衣柜跟着这一回扮演的君主，不存档。价钱是我定的，用户没逐条确认
public static partial class TerminalHost
{
    private static readonly string[] WearSlots = { "body", "hat", "hair" };
    private static readonly string[] WearPeriods = { "p0", "p1", "p2", "p3", "p4", "p5", "p6" };
    private static readonly Dictionary<string, int> WearBase = new()
    {
        ["commoner"] = 20, ["casual"] = 60, ["clergy"] = 60, ["lady"] = 80, ["court"] = 150, ["armor"] = 180, ["royal"] = 400
    };
    private const int HairPrice = 10;

    private sealed class Garment
    {
        public string Slot, Id, Name, Civ, Kind, Sex;
        public int First = 0, Last = 6;
    }

    private static Dictionary<string, Garment> _garments = new();
    private static DateTime _garmentStamp;

    // 衣冠的清单(键是 "哪一样:名字")。文件换了才重读；没有这份文件就是空的(铺子不开)
    private static Dictionary<string, Garment> Garments()
    {
        try
        {
            string path = _pagePath == null ? null : Path.Combine(Path.GetDirectoryName(_pagePath) ?? "", "wardrobe.json");
            if (path == null || !File.Exists(path)) return _garments;
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == _garmentStamp) return _garments;
            _garmentStamp = stamp;
            var loaded = new Dictionary<string, Garment>();
            JObject root = JObject.Parse(File.ReadAllText(path));
            foreach (string slot in WearSlots)
            {
                if (root[slot] is not JObject items) continue;
                foreach (KeyValuePair<string, JToken> pair in items)
                {
                    if (pair.Key == "none" || pair.Value is not JObject item) continue;
                    var garment = new Garment
                    {
                        Slot = slot, Id = pair.Key, Name = item["name"]?.ToString() ?? pair.Key, Civ = item["civ"]?.ToString() ?? "any",
                        Kind = item["kind"]?.ToString() ?? "casual", Sex = item["sex"]?.ToString() ?? "any"
                    };
                    List<int> periods = (item["period"] as JArray)?.Select(token => Array.IndexOf(WearPeriods, token.ToString())).Where(at => at >= 0).ToList();
                    if (periods != null && periods.Count > 0)
                    {
                        garment.First = periods.Min();
                        garment.Last = periods.Max();
                    }
                    loaded[slot + ":" + pair.Key] = garment;
                }
            }
            _garments = loaded;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 衣冠的清单没读成: {exception.Message}");
        }
        return _garments;
    }

    // 这一国算哪个文化的衣冠、眼下是哪个时期(同网页的 lookParts 一个算法)
    private static string WearCiv(Kingdom kingdom)
    {
        string line = RealmNamingSystem.LineOf(kingdom);
        return line == "Youmu" ? "nomad" : line == "Arabic" ? "arab" : line == "Japan" ? "jp" : line == "Huaxia" || line == "Origins" || line == "" ? "hx" : "west";
    }

    private static int WearPeriod(Kingdom kingdom)
    {
        int tier = EmpireLawSystem.GetEraTier(kingdom);
        int period = tier <= 2 ? 0 : tier == 3 ? 1 : tier == 4 ? 2 : tier == 5 ? 3 : tier == 6 ? 4 : tier <= 8 ? 5 : 6;
        return kingdom?.GetRegime()?.type == EmpireCraft.Scripts.Regimes.RegimeType.Modern ? Math.Max(5, period) : period;
    }

    // 这位君主买这一件要多少私财；买不了给 -1
    private static int WearPrice(Seat seat, Garment garment)
    {
        bool male = seat.Ruler.isSexMale();
        if (garment.Sex == "m" && !male || garment.Sex == "f" && male) return -1;
        int period = WearPeriod(seat.Realm);
        if (garment.First > period) return -1;
        double price = garment.Slot == "hair" ? HairPrice : WearBase.TryGetValue(garment.Kind, out int found) ? found : 60;
        if (garment.Slot == "hat") price = Math.Max(10d, price * 0.5d);
        if (garment.Civ != "any" && garment.Civ != WearCiv(seat.Realm)) price *= 2d;
        if (garment.Last < period) price *= 1.5d;
        return (int)Math.Round(price);
    }

    // 君主穿着的(给 Look 接在后头)：衣着;冠帽;发式，没另穿的那一样留空
    private static string WearOf(Actor actor)
    {
        Seat seat = SeatByToken.Values.FirstOrDefault(found => found.Ruler == actor);
        if (seat == null || seat.Wear.Count == 0) return "";
        return string.Join(";", WearSlots.Select(slot => seat.Wear.TryGetValue(slot, out string id) ? id : ""));
    }

    // 给手机的：私财、衣柜、穿着、铺子里各件的价钱(买得了的才有)
    private static JObject ShopJson(Seat seat)
    {
        var price = new JObject();
        foreach (KeyValuePair<string, Garment> pair in Garments())
        {
            int cost = WearPrice(seat, pair.Value);
            if (cost >= 0) price[pair.Key] = cost;
        }
        var wear = new JObject();
        foreach (KeyValuePair<string, string> pair in seat.Wear) wear[pair.Key] = pair.Value;
        return new JObject { ["money"] = seat.Ruler.money, ["own"] = new JArray(seat.Wardrobe.ToArray()), ["wear"] = wear, ["price"] = price };
    }

    // 手机上当场办的：买一件(wear_buy)、换上或换回(wear_put)。target 是 "哪一样:名字"，名字空着是换回本来的。是这两样返回 true
    private static bool WearDecree(Seat seat, string id, string target)
    {
        if (id != "wear_buy" && id != "wear_put") return false;
        seat.Acks++;
        seat.NextState = seat.NextDecrees = 0f;
        string[] parts = (target ?? "").Split(new[] { ':' }, 2);
        if (parts.Length != 2 || !WearSlots.Contains(parts[0])) return true;
        if (id == "wear_put")
        {
            if (parts[1].Length == 0) seat.Wear.Remove(parts[0]);
            else if (seat.Wardrobe.Contains(target)) seat.Wear[parts[0]] = parts[1];
            return true;
        }
        if (seat.Wardrobe.Contains(target) || !Garments().TryGetValue(target, out Garment garment)) return true;
        int cost = WearPrice(seat, garment);
        if (cost < 0) return true;
        if (seat.Ruler.money < cost)
        {
            Notify(seat, LM.Get("terminal_wear_poor"));
            return true;
        }
        seat.Ruler.spendMoney(cost);
        seat.Wardrobe.Add(target);
        // 买了就穿上
        seat.Wear[parts[0]] = parts[1];
        return true;
    }
}
