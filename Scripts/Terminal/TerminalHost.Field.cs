using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 舆图(用户 2026-11-12 "地图那一栏是不是太小了, 要是能全屏就好了""放大后应该显示更多的细节而不是单纯的像素块"
// "地图那里应该有更多的功能比如微操军队""但是会有延迟""也会显示军队""只不过不是实时更新的""科技越厉害更新越实时"
// "军队应该在地图上显示, 手指点击下达命令")：
//   · 地貌：图是手机自己画的(群里的意见 "应该把一些东西放到本地去计算，而不是服务器发包图像过来")。总图(TerminalHost.BuildMap)
//     给每块地归哪国哪城，这里给整张图的山川地貌，手机拿两样在本地合成，放大缩小拖动都不用再问这边；迷雾里的山川不发；
//   · 军情：本国各军、看得见的地方的别国军队画在图上。军情是驿报送来的，不是眼前的事：只知接壤的三个月一报，
//     驿路航路通了一个月一报，有了电报随时报(TerminalIntel.Sight 三档)。图上标着是哪个月的军情；
//   · 军令：点一支本国的军、再点一座城，就是一道手令(CorpsDirectOrders)——本国的城(帝国之主连同各行政区、封国的城)去驻守，
//     交战敌国的城去打。
//     令在路上要走：都城到那支军越远越久(一到三个月，驿路通了快一档，有电报当月到)；令到了回一条奏报，
//     军团下个月起照办。新令顶旧令；撤令也是一道令，同样要走；
//   · 给地方下令(用户 2026-11-12 "玩家既然可以指定单个军队进攻，也可以直接给州府行政区下命令让他们派兵去干嘛")：
//     点一个本朝的行政区、或一座自家的城，再点目的地——那个行政区的各军、驻地在那座城的各军一并奉令，
//     各军的手令照各自的远近走
public static partial class TerminalHost
{
    private const int TerrainMaxWidth = 1024, TerrainMaxHeight = 768, TerrainPart = 120 * 1024, MaxMapCities = 1500, MaxArmies = 160, MaxWrits = 200;
    private const float TerrainTick = 0.25f, TerrainSeconds = 120f, LiveArmySeconds = 3f;
    // 地貌里的记号：这一格在迷雾里(颜色号到不了这么大)
    private const byte NoTerrain = 255, KeepTerrain = 254;

    internal sealed class Writ
    {
        // Kingdom：这支军是哪一国的(0 = 本国；给行政区下的令是它的军)。Quiet：一道令里除头一支军以外的，送到了不另报
        public long Id, Corps, City, Kingdom;
        public bool Release, Quiet;
        public int Due;
        public string Text;
    }

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
    // 本国的城、看得见的别国的城(传闻之国的不知道)：给手机标城名、点选军令的目的地。迷雾里的不给。
    // 各城顺带标上法理(用户 2026-11-12 "图层分两种, 一种是政体图层一种是法理图层""天下拉近是帝国, 帝国拉近是王国级法理")：
    // dk 王国法理、de 帝国法理、dt 天下，是 dejure 里三张表的下标，表里是名字和法理图层上的颜色
    private static JArray MapCities(Seat seat, List<City> all, out Dictionary<long, int> shownAt, out JObject dejure)
    {
        var cities = new JArray();
        shownAt = new Dictionary<long, int>();
        var titles = new JArray();
        var cores = new JArray();
        var lands = new JArray();
        var titleAt = new Dictionary<long, int>();
        var coreAt = new Dictionary<long, int>();
        var landAt = new Dictionary<long, int>();
        dejure = new JObject { ["k"] = titles, ["e"] = cores, ["t"] = lands };
        foreach (City city in all)
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
            try
            {
                KingdomTitle title = city.GetTitle();
                if (title?.data != null && !title.isRekt())
                {
                    if (!titleAt.TryGetValue(title.id, out int at))
                    {
                        titleAt[title.id] = at = titles.Count;
                        titles.Add(new JObject { ["name"] = TerminalIntel.Strip(title.data.name ?? ""), ["color"] = HexOf(DeJureMap.TitleColor(title)) });
                    }
                    row["dk"] = at;
                }
                EmpireCore core = DeJureMap.CoreOf(city);
                if (core != null)
                {
                    if (!coreAt.TryGetValue(core.id, out int at))
                    {
                        coreAt[core.id] = at = cores.Count;
                        cores.Add(new JObject
                        {
                            ["name"] = TerminalIntel.Strip(EmpireCoreManager.GetDisplayName(core) ?? ""), ["color"] = HexOf(DeJureMap.CoreMeta(core)?.getColor())
                        });
                    }
                    row["de"] = at;
                }
                EmpireCore lead = TianxiaSystem.LeadOf(city);
                if (lead != null)
                {
                    if (!landAt.TryGetValue(lead.id, out int at))
                    {
                        landAt[lead.id] = at = lands.Count;
                        // 天下的颜色照游戏里的天下图层(TianxiaMap)
                        lands.Add(new JObject { ["name"] = TerminalIntel.Strip(TianxiaSystem.Name(lead) ?? ""), ["color"] = TianxiaMap.HexOf(lead) });
                    }
                    row["dt"] = at;
                }
            }
            catch (Exception)
            {
                // 法理还没排好：这座城先不标
            }
            shownAt[city.getID()] = cities.Count;
            cities.Add(row);
        }
        return cities;
    }

    private static string HexOf(ColorAsset color)
    {
        if (color == null) return null;
        Color32 main = color.getColorMain32();
        return "#" + main.r.ToString("x2") + main.g.ToString("x2") + main.b.ToString("x2");
    }

    // ───────── 地貌 ─────────
    // 整张地图的山川地貌：一格一个颜色号(地块种类的颜色，另附哪几号是水)，世界太大时隔几格取一格。全图取一回各席位共用，
    // 隔 TerrainSeconds 秒有人看着才重取，同上回不一样才算新的一版。发给哪部手机之前先把它迷雾里的抹掉(看不到的山川不出这台电脑)。
    // 头一回发整份；之后手机报上它手上是哪一版，同这边记的对得上就只补发变了的格(迷雾揭开的、草木田地变了的)，没变的写 KeepTerrain。
    // 立体舆图(群里 "能不能做成欧陆风云那种小 3D 的地图"，用户 2026-11-12 "地形就不用像素了, 就可以直接用颜色和高低起伏来表达")：
    // 每个颜色号另带地貌种类(kind)和高低(elev，原版地面格的高度线：深海 0、浅海 70、沙 98、低地 108、高地 128、高原 160~180、丘陵 199、山 210、峰 230)，
    // 手机照种类上色、照高低起伏打光、把山抬起来。种类和高低跟着颜色号走，每格仍只一个字节，补发照旧
    private sealed class Terrain
    {
        public MapBox World;
        public float At;
        public int Step, Width, Height, Version;
        public uint Hash;
        public byte[] Index;
        public readonly List<long> Palette = new();
    }

    // 地貌种类：0 平原 1 田 2 林 3 荒漠 4 沙滩 5 雪原 6 沼泽 7 丘陵 8 山 9 高原 10 水 11 熔岩 12 冰 13 雨林 14 荒原 15 疏林
    private const byte KindPlain = 0, KindFarm = 1, KindForest = 2, KindDesert = 3, KindSand = 4, KindSnow = 5, KindSwamp = 6, KindHills = 7,
        KindMountain = 8, KindPlateau = 9, KindWater = 10, KindLava = 11, KindIce = 12, KindJungle = 13, KindWaste = 14, KindWoods = 15;

    // trees：这一格四周 5×5 个取样格里有几格长着树(纯净地图没有树，林地只看生物群系)
    private static byte TerrainKind(WorldTile tile, int trees)
    {
        TileTypeBase type = tile.Type;
        TileType main = tile.main_type;
        string id = tile.top_type?.id ?? "";
        if (type.lava || main != null && main.lava) return KindLava;
        if (id == "ice") return KindIce;
        if (type.liquid || main != null && main.liquid) return KindWater;
        if (main != null && (main.mountains || main.summit)) return KindMountain;
        if (type.farm_field) return KindFarm;
        int land = LandKinds.Of(type);
        bool pure = PureMapMode.Enabled;
        if ((land & LandKinds.Forest) != 0 && (pure || trees >= 2) || trees >= 5)
            return (land & LandKinds.Wet) != 0 ? KindJungle : KindForest;
        if (id.StartsWith("frozen") || id.StartsWith("snow") || id.StartsWith("permafrost")) return KindSnow;
        if ((land & LandKinds.Wet) != 0) return KindSwamp;
        if (PlateauTerrain.IsPlateau(main)) return KindPlateau;
        if ((land & LandKinds.Rough) != 0) return KindHills;
        if (type.sand || (land & LandKinds.Sand) != 0) return KindSand;
        string biome = type.biome_id ?? "";
        if (biome == "biome_desert") return KindDesert;
        if ((land & LandKinds.Infernal) != 0 || biome is "biome_wasteland" or "biome_corrupted" or "biome_tumor" or "biome_biomass") return KindWaste;
        if ((land & LandKinds.Woodland) != 0 && (pure || trees >= 2)) return KindWoods;
        return KindPlain;
    }

    // 高低：地面格的高度线(没有高度线的：水当浅海以下，别的当低地)
    private static byte TerrainElev(WorldTile tile)
    {
        TileType main = tile.main_type;
        if (main == null) return 108;
        int height = main.height_min >= 0 ? main.height_min : main.liquid && !main.lava ? 40 : 110;
        return (byte)Mathf.Clamp(height, 0, 255);
    }

    // 取样格上 5×5 的和(树的疏密)
    private static byte[] BoxCount(byte[] cells, int width, int height)
    {
        var rows = new byte[cells.Length];
        var sums = new byte[cells.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sum = 0;
            for (int d = -2; d <= 2; d++)
            {
                int at = x + d;
                if (at >= 0 && at < width) sum += cells[y * width + at];
            }
            rows[y * width + x] = (byte)sum;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sum = 0;
            for (int d = -2; d <= 2; d++)
            {
                int at = y + d;
                if (at >= 0 && at < height) sum += rows[at * width + x];
            }
            sums[y * width + x] = (byte)sum;
        }
        return sums;
    }

    private static Terrain _terrain;
    private static int _terrainVersion;
    private static float _nextTerrain;

    private static Terrain TerrainNow(bool refresh)
    {
        MapBox world = World.world;
        float now = Time.realtimeSinceStartup;
        if (_terrain != null && ReferenceEquals(_terrain.World, world) && (!refresh || now - _terrain.At < TerrainSeconds && now >= _terrain.At))
            return _terrain;
        int step = Mathf.Max(1, Mathf.CeilToInt(MapBox.width / (float)TerrainMaxWidth), Mathf.CeilToInt(MapBox.height / (float)TerrainMaxHeight));
        var built = new Terrain
        {
            World = world, At = now, Step = step, Width = Mathf.Max(1, MapBox.width / step), Height = Mathf.Max(1, MapBox.height / step)
        };
        built.Index = new byte[built.Width * built.Height];
        var paletteAt = new Dictionary<long, byte>();
        // 头一号留给图外：水
        Color32 sea = RealmMiniMap.Water;
        built.Palette.Add(PaletteKey(sea, true, KindWater, 40));
        // 树的疏密(纯净地图没有树，不数)
        byte[] trees = null;
        if (!PureMapMode.Enabled)
        {
            var tree = new byte[built.Index.Length];
            for (int py = 0; py < built.Height; py++)
            for (int px = 0; px < built.Width; px++)
            {
                int tileX = px * step + step / 2, tileY = MapBox.height - 1 - (py * step + step / 2);
                if (tileX < 0 || tileY < 0 || tileX >= MapBox.width || tileY >= MapBox.height) continue;
                Building building = world.GetTile(tileX, tileY)?.building;
                if (building?.asset != null && building.asset.building_type == BuildingType.Building_Tree) tree[py * built.Width + px] = 1;
            }
            trees = BoxCount(tree, built.Width, built.Height);
        }
        // 颜色号接着上回的排：补发时没变的格还照旧号认颜色
        if (_terrain != null && ReferenceEquals(_terrain.World, world) && _terrain.Palette.Count > 0 && _terrain.Palette[0] == built.Palette[0])
            for (int i = 1; i < _terrain.Palette.Count; i++) built.Palette.Add(_terrain.Palette[i]);
        for (int i = built.Palette.Count - 1; i >= 0; i--) paletteAt[built.Palette[i]] = (byte)i;
        uint hash = 2166136261u;
        for (int py = 0; py < built.Height; py++)
        for (int px = 0; px < built.Width; px++)
        {
            int tileX = px * step + step / 2, tileY = MapBox.height - 1 - (py * step + step / 2);
            WorldTile tile = tileX < 0 || tileY < 0 || tileX >= MapBox.width || tileY >= MapBox.height ? null : world.GetTile(tileX, tileY);
            byte index = 0;
            if (tile?.Type != null)
            {
                // 路格当普通地面(底下那层的颜色)
                Color32 color = tile.Type.road && tile.main_type != null ? tile.main_type.color : tile.getColor();
                long key = PaletteKey(color, tile.Type.liquid, TerrainKind(tile, trees?[py * built.Width + px] ?? 0), TerrainElev(tile));
                if (!paletteAt.TryGetValue(key, out index))
                {
                    // 颜色号用完了(NoTerrain 以上留作记号)：归到头一号
                    if (built.Palette.Count < KeepTerrain - 4)
                    {
                        index = (byte)built.Palette.Count;
                        built.Palette.Add(key);
                    }
                    paletteAt[key] = index;
                }
            }
            built.Index[py * built.Width + px] = index;
            unchecked
            {
                hash = (hash ^ index) * 16777619u;
            }
        }
        built.Hash = hash;
        if (_terrain == null || !ReferenceEquals(_terrain.World, world) || _terrain.Hash != hash || _terrain.Width != built.Width ||
            _terrain.Height != built.Height || _terrain.Palette.Count != built.Palette.Count)
            _terrainVersion++;
        built.Version = _terrainVersion;
        return _terrain = built;
    }

    // 颜色号的键：高低 << 40 | 种类 << 32 | 是水 << 24 | 颜色
    private static long PaletteKey(Color32 color, bool liquid, byte kind, byte elev) =>
        (long)elev << 40 | (long)kind << 32 | (liquid ? 1L << 24 : 0L) | (long)color.r << 16 | (long)color.g << 8 | color.b;

    // 手机来取地貌的第 p 片(ts 是它在总图里看到的那一版)。没备好、备的不是这一版：记下来，主线程备
    private static TerminalServer.Reply TerrainReply(Seat seat, string body)
    {
        if (!int.TryParse(Field(body, "p"), out int part) || part < 0) return TerminalServer.Reply.Error(400, "bad_request");
        int.TryParse(Field(body, "have"), out int have);
        string[] parts = seat.TerrainParts;
        // 备好的是补发的、补的又不是手机手上那一版：重备
        if (parts == null || seat.TerrainTs != seat.MapTs || seat.TerrainFrom != 0 && seat.TerrainFrom != have)
        {
            seat.TerrainHave = have;
            seat.TerrainWant = true;
            return TerminalServer.Reply.Json("{\"ready\":false}");
        }
        return part < parts.Length ? TerminalServer.Reply.Json(parts[part]) : TerminalServer.Reply.Error(400, "bad_request");
    }

    private static void RunTerrain(float now)
    {
        if (now < _nextTerrain) return;
        _nextTerrain = now + TerrainTick;
        if (!WorldReady()) return;
        foreach (Seat seat in SeatByToken.Values)
        {
            if (!seat.TerrainWant || seat.Ruler == null || seat.MapCells == null) continue;
            seat.TerrainWant = false;
            try
            {
                seat.TerrainParts = TerrainFor(seat, TerrainNow(false), seat.MapTs, seat.TerrainHave);
                seat.TerrainTs = seat.MapTs;
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 地貌没备成: {exception.Message}");
            }
            // 一回只备一部手机的：要扫几十万格
            return;
        }
    }

    private static string[] TerrainFor(Seat seat, Terrain terrain, int ts, int have)
    {
        var masked = (byte[])terrain.Index.Clone();
        int step = terrain.Step, tile = seat.MapTile;
        for (int py = 0; py < terrain.Height; py++)
        {
            int row = (py * step + step / 2) / tile;
            if (row >= seat.MapH) row = seat.MapH - 1;
            for (int px = 0; px < terrain.Width; px++)
            {
                int column = (px * step + step / 2) / tile;
                if (column >= seat.MapW) column = seat.MapW - 1;
                if (seat.MapCells[row * seat.MapW + column] == TerminalIntel.Fog) masked[py * terrain.Width + px] = NoTerrain;
            }
        }
        byte[] send = masked, old = seat.TerrainBase;
        bool patchOnly = have != 0 && have == seat.TerrainBaseTs && old != null && old.Length == masked.Length;
        if (patchOnly)
        {
            send = new byte[masked.Length];
            for (int i = 0; i < send.Length; i++) send[i] = masked[i] == old[i] ? KeepTerrain : masked[i];
        }
        seat.TerrainBase = masked;
        seat.TerrainBaseTs = ts;
        seat.TerrainFrom = patchOnly ? have : 0;
        byte[] packed = TerminalIntel.Rle(send);
        int count = Math.Max(1, (packed.Length + TerrainPart - 1) / TerrainPart);
        var colors = new JArray();
        var liquid = new JArray();
        var kinds = new JArray();
        var elevs = new JArray();
        foreach (long key in terrain.Palette)
        {
            colors.Add("#" + (key & 0xffffff).ToString("x6"));
            liquid.Add((int)(key >> 24 & 1));
            kinds.Add((int)(key >> 32 & 0xff));
            elevs.Add((int)(key >> 40 & 0xff));
        }
        var parts = new string[count];
        for (int i = 0; i < count; i++)
        {
            int from = i * TerrainPart, length = Math.Min(TerrainPart, packed.Length - from);
            var json = new JObject
            {
                ["ready"] = true, ["ts"] = ts, ["p"] = i, ["n"] = count, ["d"] = Convert.ToBase64String(packed, from, Math.Max(0, length))
            };
            if (i == 0)
            {
                json["w"] = terrain.Width;
                json["h"] = terrain.Height;
                json["step"] = step;
                json["pal"] = colors;
                json["liq"] = liquid;
                json["kind"] = kinds;
                json["elev"] = elevs;
                if (patchOnly) json["from"] = have;
            }
            parts[i] = json.ToString(Newtonsoft.Json.Formatting.None);
        }
        return parts;
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
                ["task"] = corps.embarked ? "sea" : corps.task ?? "reserve", ["x"] = x, ["y"] = y,
                // 兵种：图上的牌子画这个兵种的图标(用户 2026-11-12 "军队为什么不用兵种图标")
                ["arm"] = CorpsArmSystem.Of(corps).ToString().ToLowerInvariant()
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
                var row = new JObject
                {
                    ["r"] = slot, ["n"] = Math.Max(1000, Mathf.RoundToInt(corps.strength / 1000f) * 1000), ["x"] = x, ["y"] = y,
                    ["arm"] = CorpsArmSystem.Of(corps).ToString().ToLowerInvariant()
                };
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
        if (id == "host_order") return HostOrder(seat, target, choice) || true;
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
            // 帝国之主还能把军调进本朝行政区、封国的城驻守
            bool foe = realm.isInWarWith(city.kingdom);
            bool own = !foe && (city.kingdom == realm || realm.GetEmpire() is { } court && court.CoreKingdom == realm && city.kingdom.GetEmpire() == court);
            if (!own && !foe)
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

    // 给地方下的令：target 是 k王国号(本国或本朝的行政区、封国) 或 c城号(自家的城：驻地在它的各军)，choice 是目的地的城
    private static bool HostOrder(Seat seat, string target, string choice)
    {
        Kingdom realm = seat.Realm;
        if (string.IsNullOrEmpty(target) || target.Length < 2 || !long.TryParse(target.Substring(1), out long hostId)) return true;
        City goal = long.TryParse(choice, out long goalId) ? World.world.cities.get(goalId) : null;
        if (goal?.data == null || goal.isRekt() || goal.kingdom == null) return true;
        Empire court = realm.GetEmpire() is { } found && found.CoreKingdom == realm ? found : null;
        bool Mine(Kingdom kingdom) => kingdom == realm || court != null && kingdom?.GetEmpire() == court;
        Kingdom host;
        City home = null;
        string hostName;
        if (target[0] == 'c')
        {
            home = World.world.cities.get(hostId);
            if (home?.data == null || home.isRekt() || !Mine(home.kingdom)) return true;
            host = home.kingdom;
            hostName = home.data.name;
        }
        else
        {
            host = World.world.kingdoms.get(hostId);
            if (host?.data == null || host.isRekt() || !Mine(host)) return true;
            hostName = NameOf(host);
        }
        bool foe = realm.isInWarWith(goal.kingdom) || host.isInWarWith(goal.kingdom);
        bool own = !foe && Mine(goal.kingdom);
        if (!own && !foe)
        {
            Notify(seat, string.Format(LM.Get("terminal_writ_not_enemy"), goal.data.name));
            return true;
        }
        var troops = (host.GetOrCreate().corps ?? new List<CorpsData>())
            .Where(corps => corps.strength >= 1f && (home == null || CorpsSystem.Home(corps) == home)).ToList();
        if (troops.Count == 0)
        {
            Notify(seat, string.Format(LM.Get("terminal_host_none"), hostName));
            return true;
        }
        string text = string.Format(LM.Get(own ? "terminal_host_post" : "terminal_host_strike"), hostName, goal.data.name);
        int month = MonthNow(), longest = 0;
        bool first = true;
        foreach (CorpsData corps in troops)
        {
            int months = WritMonths(seat, corps);
            longest = Math.Max(longest, months);
            seat.Writs.RemoveAll(old => old.Corps == corps.id);
            seat.Writs.Add(new Writ
            {
                Id = ++_seq, Corps = corps.id, City = goalId, Kingdom = host == realm ? 0 : host.getID(), Due = month + months, Quiet = !first,
                Text = text
            });
            first = false;
        }
        if (host != realm) seat.HostOrdered.Add(host.getID());
        while (seat.Writs.Count > MaxWrits) seat.Writs.RemoveAt(0);
        seat.ArmyDirty = true;
        Notify(seat, string.Format(LM.Get("terminal_host_sent"), text, troops.Count, longest));
        if (longest <= 0) RunWrits(seat);
        return true;
    }

    // 每秒：到了日子的手令送到军前
    private static void RunWrits(Seat seat)
    {
        if (seat.Writs.Count == 0) return;
        int month = MonthNow();
        List<CorpsData> own = seat.Realm.GetOrCreate().corps;
        for (int index = seat.Writs.Count - 1; index >= 0; index--)
        {
            Writ writ = seat.Writs[index];
            if (writ.Due > month) continue;
            seat.Writs.RemoveAt(index);
            seat.ArmyDirty = true;
            List<CorpsData> list = writ.Kingdom == 0 ? own : World.world.kingdoms.get(writ.Kingdom)?.GetOrCreate().corps;
            CorpsData corps = list?.FirstOrDefault(found => found.id == writ.Corps);
            City city = writ.Release ? null : World.world.cities.get(writ.City);
            if (corps == null || !writ.Release && (city?.data == null || city.isRekt()))
            {
                if (!writ.Quiet) Notify(seat, string.Format(LM.Get("terminal_writ_void"), writ.Text));
                continue;
            }
            if (writ.Release) CorpsDirectOrders.Clear(corps);
            else CorpsDirectOrders.Set(corps, city);
            if (!writ.Quiet) Notify(seat, string.Format(LM.Get("terminal_writ_arrived"), writ.Text));
        }
    }
}
