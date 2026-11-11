using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 舆图(用户 2026-11-12 "地图那一栏是不是太小了, 要是能全屏就好了""放大后应该显示更多的细节而不是单纯的像素块"
// "地图那里应该有更多的功能比如微操军队""但是会有延迟""也会显示军队""只不过不是实时更新的""科技越厉害更新越实时"
// "军队应该在地图上显示, 手指点击下达命令")：
//   · 细节：手机放大到一定程度，把眼下看的那一片(按总图的格)报上来，这边照世界的地块重新取样——山川地貌的颜色、
//     各国各城的界线——回给它；总图上是迷雾的格照旧是迷雾。一次只备一片，手机拿到之前先看着放大的总图；
//   · 军情：本国各军、看得见的地方的别国军队画在图上。军情是驿报送来的，不是眼前的事：只知接壤的三个月一报，
//     驿路航路通了一个月一报，有了电报随时报(TerminalIntel.Sight 三档)。图上标着是哪个月的军情；
//   · 军令：点一支本国的军、再点一座城，就是一道手令(CorpsDirectOrders)——本国的城去驻守，交战敌国的城去打。
//     令在路上要走：都城到那支军越远越久(一到三个月，驿路通了快一档，有电报当月到)；令到了回一条奏报，
//     军团下个月起照办。新令顶旧令；撤令也是一道令，同样要走
public static partial class TerminalHost
{
    private const int ViewWidth = 200, ViewMaxHeight = 360, MaxMapCities = 400, MaxArmies = 160, MaxWrits = 60;
    private const float ViewSeconds = 0.25f, LiveArmySeconds = 3f;
    private const byte NoCity = 255;

    internal sealed class Writ
    {
        public long Id, Corps, City;
        public bool Release;
        public int Due;
        public string Text;
    }

    private static float _nextView;

    // 军情几个月一报
    private static int ArmyEvery(Seat seat) =>
        seat.Sight == TerminalIntel.Sight.World ? 0 : seat.Sight == TerminalIntel.Sight.Reach ? 1 : 3;

    // 手令从都城送到这支军要几个月
    private static int WritMonths(Seat seat, CorpsData corps)
    {
        if (seat.Sight == TerminalIntel.Sight.World) return 0;
        TileZone from = seat.Realm.capital?.getTile()?.zone, to = CorpsSystem.Zone(corps);
        float distance = from == null || to == null ? 12f : CorpsSystem.ZoneDistance(from, to);
        int months = distance <= 6f ? 1 : distance <= 16f ? 2 : 3;
        return seat.Sight == TerminalIntel.Sight.Reach ? Math.Max(1, months - 1) : months;
    }

    // 地块坐标 → 总图上的格(行 0 在上)
    private static (double x, double y) CellOf(Seat seat, float tileX, float tileY) =>
        (Math.Round(tileX / seat.MapTile, 2), Math.Round(seat.MapH - tileY / seat.MapTile, 2));

    private static bool Fogged(Seat seat, float tileX, float tileY)
    {
        int x = (int)(tileX / seat.MapTile), row = seat.MapH - 1 - (int)(tileY / seat.MapTile);
        if (x < 0 || row < 0 || x >= seat.MapW || row >= seat.MapH) return true;
        return seat.MapCells[row * seat.MapW + x] == TerminalIntel.Fog;
    }

    // ───────── 总图上的城 ─────────
    // 本国的城、看得见的别国的城(传闻之国的不知道)：给手机标城名、点选军令的目的地。迷雾里的不给
    private static JArray MapCities(Seat seat, RealmMiniMap.MapData map)
    {
        var cities = new JArray();
        foreach (City city in map.Cities)
        {
            if (cities.Count >= MaxMapCities) break;
            if (city?.data == null || city.isRekt() || city.kingdom == null) continue;
            if (!seat.Slots.TryGetValue(city.kingdom.getID(), out int slot) || seat.SlotTier[slot] > 1) continue;
            Vector2 center = city.city_center;
            if (Fogged(seat, center.x, center.y)) continue;
            (double x, double y) = CellOf(seat, center.x, center.y);
            var row = new JObject
            {
                ["id"] = city.getID().ToString(), ["name"] = city.data.name, ["x"] = x, ["y"] = y, ["r"] = slot,
                ["z"] = city.zones?.Count ?? 0
            };
            if (city.kingdom.capital == city) row["cap"] = true;
            cities.Add(row);
        }
        return cities;
    }

    // ───────── 细节 ─────────
    // 手机报上来想看的那一片(总图的格：左、上、宽、高)。网络线程只记下来，主线程每 ViewSeconds 备一片
    private static TerminalServer.Reply ViewReply(Seat seat, string body)
    {
        if (!int.TryParse(Field(body, "x"), out int x) || !int.TryParse(Field(body, "y"), out int y) ||
            !int.TryParse(Field(body, "w"), out int w) || !int.TryParse(Field(body, "h"), out int h) ||
            w < 1 || h < 1 || w > 4096 || h > 4096 || x < -4096 || y < -4096 || x > 8192 || y > 8192)
            return TerminalServer.Reply.Error(400, "bad_request");
        string want = x + "," + y + "," + w + "," + h;
        seat.ViewWant = want;
        string built = seat.ViewJson;
        return TerminalServer.Reply.Json(built != null && seat.ViewKey == want ? built : "{\"ready\":false}");
    }

    private static void RunViews(float now)
    {
        if (now < _nextView) return;
        _nextView = now + ViewSeconds;
        if (!WorldReady()) return;
        foreach (Seat seat in SeatByToken.Values)
        {
            string want = seat.ViewWant;
            if (want == null || want == seat.ViewKey || seat.Ruler == null || seat.MapCells == null) continue;
            try
            {
                seat.ViewJson = BuildView(seat, want);
            }
            catch (Exception exception)
            {
                seat.ViewJson = "{\"ready\":false}";
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 细节图没备成: {exception.Message}");
            }
            seat.ViewKey = want;
            // 一回只备一片：取样要扫几万块地
            return;
        }
    }

    private static string BuildView(Seat seat, string want)
    {
        string[] parts = want.Split(',');
        int x = int.Parse(parts[0]), y = int.Parse(parts[1]), w = int.Parse(parts[2]), h = int.Parse(parts[3]);
        x = Mathf.Clamp(x, 0, seat.MapW - 1);
        y = Mathf.Clamp(y, 0, seat.MapH - 1);
        w = Mathf.Clamp(w, 1, seat.MapW - x);
        h = Mathf.Clamp(h, 1, seat.MapH - y);
        int tile = seat.MapTile;
        int step = Mathf.Max(1, Mathf.CeilToInt(w * tile / (float)ViewWidth), Mathf.CeilToInt(h * tile / (float)ViewMaxHeight));
        int width = Mathf.Max(1, w * tile / step), height = Mathf.Max(1, h * tile / step);
        int left = x * tile, top = (seat.MapH - y) * tile;
        var terrain = new byte[width * height];
        var owner = new byte[width * height];
        var cityAt = new byte[width * height];
        var palette = new List<int>();
        var paletteIndex = new Dictionary<int, byte>();
        var zoneInfo = new Dictionary<TileZone, (byte owner, byte city)>();
        var cityIndex = new Dictionary<long, byte>();
        var cities = new JArray();
        MapBox world = World.world;
        long self = seat.Realm.getID();

        byte Shade(Color32 color)
        {
            int rgb = color.r << 16 | color.g << 8 | color.b;
            if (paletteIndex.TryGetValue(rgb, out byte index)) return index;
            if (palette.Count >= 250) return 0;
            palette.Add(rgb);
            return paletteIndex[rgb] = (byte)(palette.Count - 1);
        }

        // 迷雾、图外都用头一色(不画出来)
        Shade(new Color32(14, 15, 12, 255));
        for (int py = 0; py < height; py++)
        for (int px = 0; px < width; px++)
        {
            int at = py * width + px;
            int tileX = left + px * step + step / 2, tileY = top - 1 - (py * step + step / 2);
            cityAt[at] = NoCity;
            if (Fogged(seat, tileX, tileY))
            {
                owner[at] = TerminalIntel.Fog;
                continue;
            }
            WorldTile found = tileX < 0 || tileY < 0 || tileX >= MapBox.width || tileY >= MapBox.height ? null : world.GetTile(tileX, tileY);
            if (found?.Type == null)
            {
                owner[at] = TerminalIntel.Water;
                terrain[at] = Shade(RealmMiniMap.Water);
                continue;
            }
            terrain[at] = Shade(found.getColor());
            if (found.Type.liquid || found.zone == null)
            {
                owner[at] = TerminalIntel.Water;
                continue;
            }
            if (!zoneInfo.TryGetValue(found.zone, out (byte owner, byte city) info))
            {
                City city = found.zone.city;
                if (city?.data == null || city.isRekt()) city = null;
                Kingdom kingdom = city?.kingdom ?? WildernessSystem.OwnerOf(found.zone);
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) kingdom = null;
                info = (TerminalIntel.Wild, NoCity);
                if (kingdom != null && seat.Slots.TryGetValue(kingdom.getID(), out int slot))
                {
                    info.owner = (byte)slot;
                    // 城界只给本国与已知之国的(传闻之国只知道大概在哪)；朝廷还不知道的叛军占的城照旧算本国的
                    if (city != null && seat.SlotTier[slot] <= 1 && cities.Count < 250)
                    {
                        if (!cityIndex.TryGetValue(city.getID(), out byte index))
                        {
                            cityIndex[city.getID()] = index = (byte)cities.Count;
                            cities.Add(new JObject { ["id"] = city.getID().ToString(), ["name"] = city.data.name, ["own"] = kingdom.getID() == self });
                        }
                        info.city = index;
                    }
                }
                zoneInfo[found.zone] = info;
            }
            owner[at] = info.owner;
            cityAt[at] = info.city;
        }
        var colors = new JArray();
        foreach (int rgb in palette) colors.Add("#" + rgb.ToString("x6"));
        return new JObject
        {
            ["ready"] = true, ["key"] = want, ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h, ["cw"] = width, ["ch"] = height, ["pal"] = colors,
            ["t"] = Convert.ToBase64String(TerminalIntel.Rle(terrain)), ["o"] = Convert.ToBase64String(TerminalIntel.Rle(owner)),
            ["c"] = Convert.ToBase64String(TerminalIntel.Rle(cityAt)), ["cities"] = cities
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    // ───────── 军情 ─────────
    // 每秒(主线程)：到了该报的时候重新看一遍各军在哪；手令有变动只重拼一遍(军的位置还是上回报的)
    private static void RunArmies(Seat seat, float now)
    {
        if (seat.MapCells == null) return;
        int month = MonthNow(), every = ArmyEvery(seat);
        bool due = seat.ArmySeen == null || (every == 0 ? now >= seat.NextArmy : month - seat.ArmyMonth >= every || month < seat.ArmyMonth);
        if (due && (seat.Watched || seat.ArmySeen == null))
        {
            JArray seen = SeeArmies(seat);
            string text = seen.ToString(Newtonsoft.Json.Formatting.None);
            // 有电报时几秒看一回：各军都没动就不算新军情，手机不用再取
            if (seat.ArmySeen == null || text != seat.ArmySeenText || month != seat.ArmyMonth) seat.ArmyDirty = true;
            seat.ArmySeen = seen;
            seat.ArmySeenText = text;
            seat.ArmyMonth = month;
            seat.NextArmy = now + LiveArmySeconds;
        }
        if (!seat.ArmyDirty || seat.ArmySeen == null) return;
        seat.ArmyDirty = false;
        var writs = new JArray();
        foreach (Writ writ in seat.Writs)
            writs.Add(new JObject { ["corps"] = writ.Corps.ToString(), ["text"] = writ.Text, ["left"] = Math.Max(0, writ.Due - month) });
        // 手令是君主自己下的，到没到他知道：各军眼下奉的令现拼，不等下一回军报
        var orders = new JObject();
        List<CorpsData> list = seat.Realm.GetOrCreate().corps;
        if (list != null)
            foreach (CorpsData corps in list)
                if (CorpsDirectOrders.Target(seat.Realm, corps) is City goal)
                    orders[corps.id.ToString()] = new JObject { ["id"] = goal.getID().ToString(), ["name"] = goal.data.name };
        seat.ArmyStamp++;
        seat.ArmyJson = new JObject
        {
            ["ready"] = true, ["stamp"] = seat.ArmyStamp, ["at"] = seat.ArmyMonth, ["every"] = every, ["armies"] = seat.ArmySeen,
            ["writs"] = writs, ["orders"] = orders
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JArray SeeArmies(Seat seat)
    {
        var armies = new JArray();
        Kingdom realm = seat.Realm;
        foreach (CorpsData corps in realm.GetOrCreate().corps ?? new List<CorpsData>())
        {
            if (corps.strength < 1f || armies.Count >= MaxArmies) continue;
            WorldTile at = CorpsSystem.Zone(corps)?.centerTile;
            if (at == null) continue;
            (double x, double y) = CellOf(seat, at.x, at.y);
            var row = new JObject
            {
                ["id"] = corps.id.ToString(), ["own"] = true, ["name"] = TerminalIntel.Strip(CorpsSystem.Name(corps)),
                ["n"] = Mathf.RoundToInt(corps.strength), ["full"] = Mathf.RoundToInt(corps.full), ["org"] = Mathf.RoundToInt(corps.org),
                ["task"] = corps.embarked ? "sea" : corps.task ?? "reserve", ["x"] = x, ["y"] = y
            };
            WorldTile to = corps.target_zone_id == corps.zone_id ? null : CorpsSystem.ZoneById(corps.target_zone_id)?.centerTile;
            if (to != null)
            {
                (double tx, double ty) = CellOf(seat, to.x, to.y);
                row["tx"] = tx;
                row["ty"] = ty;
            }
            City sector = corps.sector_city_id < 0 ? null : World.world.cities.get(corps.sector_city_id);
            if (sector?.data != null && !sector.isRekt()) row["at"] = sector.data.name;
            City home = CorpsSystem.Home(corps);
            if (home?.data != null) row["home"] = home.data.name;
            armies.Add(row);
        }
        // 别国的军：已知之国的、落在看得见的地方的才知道，兵力只知道个大概(整千)
        var enemies = new HashSet<long>();
        foreach (War war in realm.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            foreach (Kingdom side in new[] { war.main_attacker, war.main_defender })
                if (side != null && side != realm && realm.isInWarWith(side)) enemies.Add(side.getID());
        }
        foreach (long id in seat.Known.ToList())
        {
            Kingdom other = World.world.kingdoms.get(id);
            if (other?.data == null || other.isRekt() || other == realm || seat.Hidden.Contains(id)) continue;
            if (!seat.Slots.TryGetValue(id, out int slot)) continue;
            bool foe = enemies.Contains(id) || realm.isInWarWith(other);
            foreach (CorpsData corps in other.GetOrCreate().corps ?? new List<CorpsData>())
            {
                if (armies.Count >= MaxArmies) break;
                if (corps.strength < 1f) continue;
                WorldTile at = CorpsSystem.Zone(corps)?.centerTile;
                if (at == null || Fogged(seat, at.x, at.y)) continue;
                (double x, double y) = CellOf(seat, at.x, at.y);
                var row = new JObject { ["r"] = slot, ["n"] = Math.Max(1000, Mathf.RoundToInt(corps.strength / 1000f) * 1000), ["x"] = x, ["y"] = y };
                if (foe) row["foe"] = true;
                if (corps.embarked) row["task"] = "sea";
                armies.Add(row);
            }
        }
        return armies;
    }

    // ───────── 军令 ─────────
    // 手机发来：corps_order(target = 军 id，choice = 城 id)、corps_release(target = 军 id)。是这两样返回 true
    private static bool FieldDecree(Seat seat, string id, string target, string choice)
    {
        if (id != "corps_order" && id != "corps_release") return false;
        Kingdom realm = seat.Realm;
        CorpsData corps = long.TryParse(target, out long corpsId) ? realm.GetOrCreate().corps?.FirstOrDefault(found => found.id == corpsId) : null;
        if (corps == null) return true;
        string name = TerminalIntel.Strip(CorpsSystem.Name(corps));
        var writ = new Writ { Id = ++_seq, Corps = corpsId, Due = MonthNow() + WritMonths(seat, corps) };
        if (id == "corps_release")
        {
            // 没有手令、也没有在路上的令：没什么可撤的
            if (!CorpsDirectOrders.Has(corps) && seat.Writs.All(found => found.Corps != corpsId)) return true;
            writ.Release = true;
            writ.Text = string.Format(LM.Get("terminal_writ_release"), name);
        }
        else
        {
            City city = long.TryParse(choice, out long cityId) ? World.world.cities.get(cityId) : null;
            if (city?.data == null || city.isRekt() || city.kingdom == null) return true;
            bool own = city.kingdom == realm;
            if (!own && !realm.isInWarWith(city.kingdom))
            {
                Notify(seat, string.Format(LM.Get("terminal_writ_not_enemy"), city.data.name));
                return true;
            }
            writ.City = cityId;
            writ.Text = string.Format(LM.Get(own ? "terminal_writ_post" : "terminal_writ_strike"), name, city.data.name);
        }
        // 新令顶旧令：还在路上的旧令作废
        seat.Writs.RemoveAll(found => found.Corps == corpsId);
        seat.Writs.Add(writ);
        if (seat.Writs.Count > MaxWrits) seat.Writs.RemoveAt(0);
        seat.ArmyDirty = true;
        int months = writ.Due - MonthNow();
        if (months <= 0) RunWrits(seat);
        else Notify(seat, string.Format(LM.Get("terminal_writ_sent"), writ.Text, months));
        return true;
    }

    // 每秒：到了日子的手令送到军前
    private static void RunWrits(Seat seat)
    {
        if (seat.Writs.Count == 0) return;
        int month = MonthNow();
        List<CorpsData> list = seat.Realm.GetOrCreate().corps;
        for (int index = seat.Writs.Count - 1; index >= 0; index--)
        {
            Writ writ = seat.Writs[index];
            if (writ.Due > month) continue;
            seat.Writs.RemoveAt(index);
            seat.ArmyDirty = true;
            CorpsData corps = list?.FirstOrDefault(found => found.id == writ.Corps);
            City city = writ.Release ? null : World.world.cities.get(writ.City);
            if (corps == null || !writ.Release && (city?.data == null || city.isRekt()))
            {
                Notify(seat, string.Format(LM.Get("terminal_writ_void"), writ.Text));
                continue;
            }
            if (writ.Release) CorpsDirectOrders.Clear(corps);
            else CorpsDirectOrders.Set(corps, city);
            Notify(seat, string.Format(LM.Get("terminal_writ_arrived"), writ.Text));
        }
    }
}
