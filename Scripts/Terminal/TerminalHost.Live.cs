using System;
using System.Collections.Generic;
using System.Threading;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 游戏里看核心的实时地图(用户 2026-11-18："核心在外面算，然后结果给游戏本体"，先做实时地图，见 CoreLiveMap)：
// 管理员才取得到的两份数据，同手机总图分开备——手机那份一格一个字节，至多认 250 国，大图上多出来的国都成了无主地；这里每格两个字节、国数不封顶，
// 也不管迷雾(上帝视角)。
//   GET /api/admin/live/map   每块地归哪一国(被敌军占着的算占领方，同游戏里的图)、各国名字颜色与版图重心、各城、交战的两两国家；
//                             地图有人取着才备：战线动了隔一秒重算，平时四秒一回，内容没变不换版本号
//   GET /api/admin/live/army  各军(哪国、多少人、在哪、去哪)与各处交战(见 FrontSystem.Battles)、年月、流速(设定与实际，百分数；
//                             游戏那头折成原版的 ×N 档显示)、暂停没有，每秒一回
// 国家的编号以这一版地图为准：军情里的 r、交战的 a/d 都按它；地图换了版本，游戏这头连同军情一起重取
public static partial class TerminalHost
{
    private const float LiveMapSeconds = 4f, LiveMapMinSeconds = 1f, LiveIdleSeconds = 15f;
    private const int MaxLiveArmies = 4000, MaxLiveBattles = 400;
    internal const ushort LiveWater = 0xFFFF, LiveWild = 0xFFFE;
    private static long _liveWant;
    private static volatile string _liveMapJson, _liveArmyJson;
    private static float _liveMapAt = -999f;
    private static int _liveTerritory = int.MinValue, _liveMapStamp, _liveArmyStamp, _liveW, _liveH;
    private static float _liveTile = 1f;
    private static string _liveMapBody, _liveArmyBody;
    private static readonly Dictionary<long, int> LiveIndex = new();

    // 网络线程
    private static TerminalServer.Reply LiveReply(bool map)
    {
        Interlocked.Exchange(ref _liveWant, DateTime.UtcNow.Ticks);
        string json = map ? _liveMapJson : _liveArmyJson;
        return TerminalServer.Reply.Json(json ?? "{\"ready\":false}");
    }

    private static bool LiveWanted => DateTime.UtcNow.Ticks - Interlocked.Read(ref _liveWant) < TimeSpan.FromSeconds(LiveIdleSeconds).Ticks;

    // 主线程，每秒(见 RunAdmins)
    private static void RunLive(float now)
    {
        if (!LiveWanted || World.world?.zone_calculator == null) return;
        try
        {
            bool moved = FrontSystem.TerritoryStamp != _liveTerritory;
            if (_liveMapJson == null || now - _liveMapAt >= LiveMapSeconds || now < _liveMapAt ||
                moved && now - _liveMapAt >= LiveMapMinSeconds)
            {
                _liveMapAt = now;
                _liveTerritory = FrontSystem.TerritoryStamp;
                BuildLiveMap();
            }
            BuildLiveArmies();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][终端] 备实时地图出错: {exception.Message}");
        }
    }

    private static int LiveIndexOf(Kingdom kingdom, List<Kingdom> kingdoms)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) return -1;
        if (LiveIndex.TryGetValue(kingdom.getID(), out int at)) return at;
        LiveIndex[kingdom.getID()] = at = kingdoms.Count;
        kingdoms.Add(kingdom);
        return at;
    }

    // 地块坐标 → 图上的格(行 0 在上，同手机总图)
    private static (double x, double y) LiveCell(float tileX, float tileY) =>
        (Math.Round(tileX / _liveTile, 2), Math.Round(_liveH - tileY / _liveTile, 2));

    private static void BuildLiveMap()
    {
        ZoneCalculator calc = World.world.zone_calculator;
        int width = calc.zones_total_x, height = calc.zones_total_y;
        if (width < 1 || height < 1) return;
        _liveW = width;
        _liveH = height;
        _liveTile = Mathf.Max(1, MapBox.width / width);
        LiveIndex.Clear();
        var kingdoms = new List<Kingdom>();
        var cells = new ushort[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int to = (height - 1 - y) * width + x;
            TileZone zone = calc.getZone(x, y);
            if (zone == null)
            {
                cells[to] = LiveWater;
                continue;
            }
            City city = zone.city;
            if (city?.data == null || city.isRekt()) city = null;
            int at = LiveIndexOf(city?.kingdom ?? WildernessSystem.OwnerOf(zone), kingdoms);
            if (at < 0) cells[to] = zone.centerTile?.Type == null || zone.centerTile.Type.liquid ? LiveWater : LiveWild;
            else cells[to] = (ushort)at;
        }
        // 被敌军占着的地算占领方的(同手机总图、游戏里的图)
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null) continue;
            Dictionary<long, List<int>> occupied = city.GetOrCreate().OccupiedStatus;
            if (occupied == null || occupied.Count == 0) continue;
            foreach (KeyValuePair<long, List<int>> pair in occupied)
            {
                if (pair.Value == null || pair.Value.Count == 0) continue;
                Kingdom occupier = World.world.kingdoms.get(pair.Key);
                if (occupier?.data == null || occupier.isRekt() || occupier.wild || !FrontSystem.Hostile(city.kingdom, occupier)) continue;
                int at = LiveIndexOf(occupier, kingdoms);
                if (at < 0) continue;
                foreach (int zoneId in pair.Value)
                {
                    TileZone zone = CorpsSystem.ZoneById(zoneId);
                    if (zone == null || zone.city != city || zone.x < 0 || zone.y < 0 || zone.x >= width || zone.y >= height) continue;
                    cells[(height - 1 - zone.y) * width + zone.x] = (ushort)at;
                }
            }
        }
        var sumX = new double[kingdoms.Count];
        var sumY = new double[kingdoms.Count];
        var count = new int[kingdoms.Count];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            ushort value = cells[y * width + x];
            if (value >= kingdoms.Count) continue;
            sumX[value] += x + 0.5d;
            sumY[value] += y + 0.5d;
            count[value]++;
        }
        var realms = new JArray();
        for (int i = 0; i < kingdoms.Count; i++)
        {
            Kingdom kingdom = kingdoms[i];
            var row = new JObject
            {
                ["id"] = kingdom.getID().ToString(), ["name"] = NameOf(kingdom), ["color"] = Hex(RealmMiniMap.KingdomColor(kingdom)),
                ["n"] = count[i]
            };
            if (count[i] > 0)
            {
                row["x"] = Math.Round(sumX[i] / count[i], 1);
                row["y"] = Math.Round(sumY[i] / count[i], 1);
            }
            realms.Add(row);
        }
        var cities = new JArray();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null) continue;
            Vector2 center = city.city_center;
            (double x, double y) = LiveCell(center.x, center.y);
            var row = new JObject
            {
                ["name"] = TerminalIntel.Strip(city.data.name ?? ""), ["x"] = x, ["y"] = y,
                ["r"] = LiveIndex.TryGetValue(city.kingdom.getID(), out int at) ? at : -1, ["z"] = city.zones?.Count ?? 0
            };
            if (city.kingdom.capital == city) row["cap"] = true;
            cities.Add(row);
        }
        // 交战的两两国家(攻守两边各国连同参战的)：游戏这头把两国版图相接处描成朱红战线
        var fronts = new JArray();
        var seen = new HashSet<(int, int)>();
        foreach (War war in World.world.wars)
        {
            if (war?.data == null || war.hasEnded()) continue;
            try
            {
                foreach (Kingdom left in war.getAttackers())
                foreach (Kingdom right in war.getDefenders())
                {
                    if (fronts.Count >= 4000) break;
                    if (left == null || right == null || !LiveIndex.TryGetValue(left.getID(), out int l) ||
                        !LiveIndex.TryGetValue(right.getID(), out int r) || l == r) continue;
                    if (seen.Add((Math.Min(l, r), Math.Max(l, r)))) fronts.Add(new JArray(Math.Min(l, r), Math.Max(l, r)));
                }
            }
            catch (Exception)
            {
                // 参战名单一时取不到：这一场先不描
            }
        }
        var low = new byte[cells.Length];
        var high = new byte[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            low[i] = (byte)cells[i];
            high[i] = (byte)(cells[i] >> 8);
        }
        var json = new JObject
        {
            ["ready"] = true, ["w"] = width, ["h"] = height, ["tile"] = _liveTile, ["lo"] = Convert.ToBase64String(TerminalIntel.Rle(low)),
            ["hi"] = Convert.ToBase64String(TerminalIntel.Rle(high)), ["realms"] = realms, ["cities"] = cities, ["fronts"] = fronts
        };
        string body = json.ToString(Newtonsoft.Json.Formatting.None);
        if (body == _liveMapBody && _liveMapJson != null) return;
        _liveMapBody = body;
        json["stamp"] = ++_liveMapStamp;
        _liveMapJson = json.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static void BuildLiveArmies()
    {
        if (_liveMapJson == null) return;
        var armies = new JArray();
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (armies.Count >= MaxLiveArmies) break;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) continue;
            int r = LiveIndex.TryGetValue(kingdom.getID(), out int at) ? at : -1;
            foreach (CorpsData corps in kingdom.GetOrCreate().corps ?? new List<CorpsData>())
            {
                if (armies.Count >= MaxLiveArmies) break;
                if (corps == null || corps.strength < 1f) continue;
                WorldTile tile = CorpsSystem.Zone(corps)?.centerTile;
                if (tile == null) continue;
                (double x, double y) = LiveCell(tile.x, tile.y);
                var row = new JObject { ["r"] = r, ["n"] = Mathf.RoundToInt(corps.strength), ["x"] = x, ["y"] = y };
                string task = corps.embarked ? "sea" : corps.task;
                if (!string.IsNullOrEmpty(task) && task != "reserve") row["t"] = task;
                WorldTile goal = corps.target_zone_id == corps.zone_id ? null : CorpsSystem.ZoneById(corps.target_zone_id)?.centerTile;
                if (goal != null)
                {
                    (double tx, double ty) = LiveCell(goal.x, goal.y);
                    row["tx"] = tx;
                    row["ty"] = ty;
                }
                armies.Add(row);
            }
        }
        var battles = new JArray();
        foreach (FrontSystem.BattleView battle in FrontSystem.Battles.Values)
        {
            if (battles.Count >= MaxLiveBattles) break;
            City city = World.world.cities.get(battle.city_id);
            if (city?.data == null || city.isRekt()) continue;
            WorldTile tile = CorpsSystem.ZoneById(battle.zone_id)?.centerTile ?? city.getTile();
            if (tile == null) continue;
            int zones = city.zones?.Count ?? 0, held = 0;
            for (int i = 0; i < zones; i++)
                if (FrontSystem.Hostile(city.kingdom, FrontSystem.Controller(city.zones[i]))) held++;
            (double x, double y) = LiveCell(tile.x, tile.y);
            var row = new JObject
            {
                ["x"] = x, ["y"] = y, ["c"] = TerminalIntel.Strip(city.data.name ?? ""), ["p"] = Mathf.RoundToInt(battle.ratio * 100f),
                ["h"] = held, ["z"] = zones,
                ["a"] = LiveIndex.TryGetValue(battle.attacker_id, out int a) ? a : -1,
                ["d"] = LiveIndex.TryGetValue(battle.defender_id, out int d) ? d : -1
            };
            if (battle.landing) row["l"] = true;
            battles.Add(row);
        }
        var json = new JObject
        {
            ["ready"] = true, ["map"] = _liveMapStamp, ["armies"] = armies, ["battles"] = battles,
            ["speed"] = SpeedPercent, ["actual"] = ActualSpeedPercent, ["paused"] = global::Config.paused,
            ["year"] = Date.getCurrentYear(), ["month"] = Date.getCurrentMonth()
        };
        string body = json.ToString(Newtonsoft.Json.Formatting.None);
        if (body == _liveArmyBody && _liveArmyJson != null) return;
        _liveArmyBody = body;
        json["stamp"] = ++_liveArmyStamp;
        _liveArmyJson = json.ToString(Newtonsoft.Json.Formatting.None);
    }
}
