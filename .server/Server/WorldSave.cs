using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using EmpireCraft.Scripts;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Terminal;
using NeoModLoader.services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Server;

// 存档。一份存档是存放目录里的一个文件夹 save/：
//   world.json               世界核心：时间、国、城、人物及各自的归属、文化宗教宗族、战事同盟、起名模板、各类对象下一个编号
//   tiles.bin                地形(压缩过)
//   EmpireCraftModData.json  模组自己的那份(政体、帝国、法理、人口台账……)，同游戏存档里的是一个格式，由模组自己写、自己读
//   seats.json               各席位所领的君主
// 先写进 save.new/，写全了才换上去；换下来的留作 save.old/(这一份读不出来时用它)。
// 重新开局前，眼下的存档挪到 saves-archive/<时间>/，留最近三份。
internal static class WorldSave
{
    internal const string WorldFile = "world.json", TilesFile = "tiles.bin", SeatsFile = "seats.json";
    private const int Version = 1, KeepArchives = 3;
    private const uint TilesMagic = 0x31544345; // "ECT1"
    private static readonly JsonSerializer Json = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    internal static string Root = "";
    internal static DateTime LastSaved = DateTime.MinValue;
    // 读档没读成(存档坏了)：加载队列走完后主循环据此把半截世界清掉
    internal static bool LoadFailed { get; private set; }
    internal static string LoadError { get; private set; } = "";
    // 读档读回来的"席位 → 君主"：等终端主机把席位载入了再交还(见 Program)
    internal static Dictionary<string, long> PendingSeats;
    private static bool _wrote;
    private static JObject _loading;
    private static string _loadingFolder;

    private static string Current => Path.Combine(Root, "save");
    private static string Previous => Path.Combine(Root, "save.old");
    private static string Fresh => Path.Combine(Root, "save.new");
    private static string Archives => Path.Combine(Root, "saves-archive");

    private static bool Has(string folder) => File.Exists(Path.Combine(folder, WorldFile)) && File.Exists(Path.Combine(folder, TilesFile));
    internal static bool Exists => Has(Current) || Has(Previous);
    private static bool WorldReady => World.world != null && Config.game_loaded && !SmoothLoader.isLoading() && World.world.cities.Count > 0;

    // ───────── 存 ─────────

    // 存一次(要在主循环里调)。成了回真，detail 是一句话的结果
    internal static bool Save(out string detail)
    {
        if (!WorldReady)
        {
            detail = "还没有世界，没什么可存";
            return false;
        }
        var clock = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(Fresh)) Directory.Delete(Fresh, true);
            Directory.CreateDirectory(Fresh);
            _wrote = false;
            // 照游戏里存档的走法：模组的前置补丁写它那一份，本体(见 WriteWorld)写世界核心，后置补丁清缓存
            SaveManager.saveMapData(Fresh, false);
            if (!_wrote)
            {
                detail = "这会儿存不了(模组正在整理数据)，稍后再存";
                return false;
            }
            if (!File.Exists(Path.Combine(Fresh, DataManager.EmpireCraftSaveFileName)))
            {
                detail = "模组的数据没写出来，这次没存，原来的存档没动";
                return false;
            }
            File.WriteAllText(Path.Combine(Fresh, SeatsFile), JsonConvert.SerializeObject(TerminalHost.SeatRulers()));
            long bytes = new DirectoryInfo(Fresh).GetFiles().Sum(file => file.Length);
            if (Directory.Exists(Previous)) Directory.Delete(Previous, true);
            if (Directory.Exists(Current)) Directory.Move(Current, Previous);
            Directory.Move(Fresh, Current);
            LastSaved = DateTime.UtcNow;
            detail = Date.getCurrentYear() + " 年 " + Date.getCurrentMonth() + " 月，" + (bytes / 1048576d).ToString("0.0") + " MB，用时 " + clock.ElapsedMilliseconds + " 毫秒";
            return true;
        }
        catch (Exception exception)
        {
            detail = "没存成，原来的存档没动: " + exception.Message;
            LogService.LogError("[存档] " + exception);
            return false;
        }
    }

    // 重新开局前：眼下的存档挪去归档(只留最近几份)。回归档到了哪里；没有存档回 null
    internal static string ArchiveCurrent()
    {
        try
        {
            if (!Has(Current) && Has(Previous) && !Directory.Exists(Current)) Directory.Move(Previous, Current);
            if (!Has(Current)) return null;
            Directory.CreateDirectory(Archives);
            string target = Path.Combine(Archives, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.Move(Current, target);
            if (Directory.Exists(Previous)) Directory.Delete(Previous, true);
            foreach (string old in Directory.GetDirectories(Archives).OrderByDescending(path => path, StringComparer.Ordinal).Skip(KeepArchives))
                Directory.Delete(old, true);
            LastSaved = DateTime.MinValue;
            return target;
        }
        catch (Exception exception)
        {
            LogService.LogError("[存档] 归档没成: " + exception.Message);
            return null;
        }
    }

    // 世界核心这一份(SaveManager.saveMapData 的本体)
    internal static void WriteWorld(string folder)
    {
        MapBox world = World.world;
        WriteTiles(Path.Combine(folder, TilesFile));
        var root = new JObject
        {
            ["v"] = Version,
            ["saved"] = DateTime.UtcNow.ToString("o"),
            ["scenario"] = ServerRoutes.ScenarioName ?? "",
            ["scenario_key"] = ServerRoutes.ScenarioKey ?? "",
            ["frame"] = ServerRoutes.ScenarioFrame ?? "",
            ["size"] = ServerRoutes.ScenarioSize,
            ["world_time"] = world.world_time,
            ["laws"] = new JObject(world.world_laws.dict.Where(law => law.Value != null).Select(law => new JProperty(law.Key, law.Value.boolVal))),
            ["map_stats"] = JObject.FromObject(world.map_stats, Json),
            ["game_stats"] = JObject.FromObject(world.game_stats.data, Json),
            ["next"] = new JObject
            {
                ["kingdoms"] = world.kingdoms.ShimNextId, ["cities"] = world.cities.ShimNextId, ["units"] = world.units.ShimNextId, ["wars"] = world.wars.ShimNextId,
                ["alliances"] = world.alliances.ShimNextId, ["clans"] = world.clans.ShimNextId, ["cultures"] = world.cultures.ShimNextId,
                ["religions"] = world.religions.ShimNextId, ["languages"] = world.languages.ShimNextId, ["families"] = world.families.ShimNextId,
                ["relations"] = world.diplomacy.ShimNextId
            },
            ["cultures"] = new JArray(world.cultures.Where(Alive).Select(culture => Meta(culture, culture.data, culture.ShimTraitIds, Names(culture)))),
            ["languages"] = new JArray(world.languages.Where(Alive).Select(language => Meta(language, language.data, language.ShimTraitIds))),
            ["religions"] = new JArray(world.religions.Where(Alive).Select(religion => Meta(religion, religion.data, religion.ShimTraitIds))),
            ["clans"] = new JArray(world.clans.Where(Alive).Select(clan => Meta(clan, clan.data, clan.ShimTraitIds))),
            ["families"] = new JArray(world.families.Where(Alive).Select(family => Meta(family, family.data, null))),
            ["wild"] = new JArray(world.kingdoms.ShimWildSpecies),
            ["kingdoms"] = new JArray(world.kingdoms.Where(Alive).Select(Realm)),
            ["cities"] = new JArray(world.cities.Where(Alive).Select(Town)),
            ["units"] = new JArray(world.units.Where(Alive).Select(Person)),
            ["wars"] = new JArray(world.wars.Where(Alive).Select(war => Meta(war, war.data, null))),
            ["alliances"] = new JArray(world.alliances.Where(Alive).Select(alliance =>
            {
                JObject item = Meta(alliance, alliance.data, null);
                item["m"] = new JArray(alliance.kingdoms_list.Where(Alive).Select(member => member.id));
                return item;
            })),
            ["relations"] = new JArray(world.diplomacy.ShimRelations.Where(pair => Alive(pair.Value)).Select(pair =>
                new JObject { ["a"] = pair.Key.Item1, ["b"] = pair.Key.Item2, ["d"] = JObject.FromObject(pair.Value.data, Json) }))
        };
        using (var writer = new StreamWriter(Path.Combine(folder, WorldFile), false, new UTF8Encoding(false)))
        using (var json = new JsonTextWriter(writer))
            root.WriteTo(json);
        _wrote = true;
    }

    private static bool Alive(NanoObject item) => item != null && item.isAlive();
    private static JToken Id(NanoObject item) => Alive(item) ? item.id : JValue.CreateNull();

    private static JObject Meta<TData>(NanoObject item, TData data, IEnumerable<string> traits, JObject names = null)
    {
        var saved = new JObject { ["d"] = JObject.FromObject(data, Json) };
        JObject values = Values(item);
        if (values.Count > 0) saved["f"] = values;
        if (traits != null && traits.Any()) saved["tr"] = new JArray(traits);
        if (names != null && names.Count > 0) saved["names"] = names;
        return saved;
    }

    // 一种文化的起名模板(人、族、城、国各一套)：模板的各段，和各组的字词
    private static JObject Names(Culture culture)
    {
        var all = new JObject();
        foreach (KeyValuePair<MetaType, OnomasticsData> entry in culture.ShimNames)
        {
            if (entry.Value == null || entry.Value.ShimTemplate.Count == 0 && entry.Value.groups.Count == 0) continue;
            all[((int)entry.Key).ToString()] = new JObject
            {
                ["t"] = new JArray(entry.Value.ShimTemplate),
                ["g"] = new JObject(entry.Value.groups.Where(group => group.Value != null).Select(group => new JProperty(group.Key, group.Value.characters_string ?? "")))
            };
        }
        return all;
    }

    private static JObject Realm(Kingdom kingdom)
    {
        JObject saved = Meta(kingdom, kingdom.data, kingdom.ShimTraitIds);
        saved["as"] = kingdom.asset?.id;
        saved["cu"] = Id(kingdom.culture);
        saved["la"] = Id(kingdom.language);
        saved["re"] = Id(kingdom.religion);
        saved["cap"] = Id(kingdom.capital);
        saved["king"] = Id(kingdom.king);
        saved["ci"] = new JArray(kingdom.cities.Where(Alive).Select(city => city.id));
        saved["loc"] = new JArray(kingdom.location.x, kingdom.location.y, kingdom.location.z);
        return saved;
    }

    private static JObject Town(City city)
    {
        JObject saved = Meta(city, city.data, null);
        if (city.status != null) saved["st"] = JObject.FromObject(city.status, Json);
        saved["k"] = Id(city.kingdom);
        saved["cu"] = Id(city.culture);
        saved["la"] = Id(city.language);
        saved["re"] = Id(city.religion);
        saved["lead"] = Id(city.leader);
        saved["z"] = new JArray(city.zones.Select(zone => zone.id));
        if (city._city_tile != null) saved["t"] = city._city_tile.data.tile_id;
        saved["cc"] = new JArray(city.city_center.x, city.city_center.y);
        saved["lc"] = new JArray(city.last_city_center.x, city.last_city_center.y);
        return saved;
    }

    private static JObject Person(Actor actor)
    {
        JObject saved = Meta(actor, actor.data, actor.ShimTraitIds);
        // 没入国的人挂在本族的"野处"(编号是负的)，照样记编号
        if (actor.kingdom != null && actor.kingdom.isAlive()) saved["k"] = actor.kingdom.id;
        saved["c"] = Id(actor.city);
        saved["cl"] = Id(actor.clan);
        saved["cu"] = Id(actor.culture);
        saved["re"] = Id(actor.religion);
        saved["la"] = Id(actor.language);
        saved["fa"] = Id(actor.family);
        saved["lo"] = Id(actor.lover);
        saved["sp"] = actor.subspecies?.data?.species_id;
        if (actor.current_tile != null) saved["t"] = actor.current_tile.data.tile_id;
        saved["p"] = (int)actor.ShimProfession;
        saved["ab"] = new JArray(actor.stewardship, actor.warfare, actor.intelligence, actor.diplomacy, actor.level);
        if (actor.stats != null && actor.stats.ShimValues.Count > 0)
            saved["st"] = new JObject(actor.stats.ShimValues.Select(stat => new JProperty(stat.Key, stat.Value)));
        return saved;
    }

    // 地形：每格的底层、表层(各记在名字表里的号)和高度
    private static void WriteTiles(string path)
    {
        WorldTile[] tiles = World.world.tiles_list;
        var names = new List<string>();
        var index = new Dictionary<TileTypeBase, ushort>();
        var cells = new ushort[tiles.Length * 2];
        var heights = new short[tiles.Length];

        ushort IndexOf(TileTypeBase type)
        {
            if (type?.id == null) return 0;
            if (index.TryGetValue(type, out ushort known)) return known;
            names.Add(type.id);
            return index[type] = (ushort)names.Count;
        }

        for (int i = 0; i < tiles.Length; i++)
        {
            cells[i * 2] = IndexOf(tiles[i].main_type);
            cells[i * 2 + 1] = IndexOf(tiles[i].top_type);
            heights[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, tiles[i].Height));
        }
        using var file = File.Create(path);
        using var packed = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new BinaryWriter(new BufferedStream(packed, 1 << 16), Encoding.UTF8);
        writer.Write(TilesMagic);
        writer.Write(MapBox.width);
        writer.Write(MapBox.height);
        writer.Write(names.Count);
        foreach (string name in names) writer.Write(name);
        for (int i = 0; i < tiles.Length; i++)
        {
            writer.Write(cells[i * 2]);
            writer.Write(cells[i * 2 + 1]);
            writer.Write(heights[i]);
        }
        writer.Flush();
    }

    // ───────── 读 ─────────

    // 读最近的一份存档(要在主循环里调)：这里只查验、清世界、把步骤排进加载队列，步骤由主循环接着走。
    // 排上了回真；没有存档、两份都读不出来回假
    internal static bool Load(out string detail)
    {
        detail = "没有存档";
        foreach (string folder in new[] { Current, Previous })
        {
            if (!Has(folder)) continue;
            JObject root;
            try
            {
                // 文字照原样读：长得像日期的字符串(存档时刻、碰巧像日期的名字)不许被认成日期再换个写法
                using (var reader = new JsonTextReader(new StreamReader(Path.Combine(folder, WorldFile), Encoding.UTF8)) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(reader);
                if ((int?)root["v"] != Version) throw new InvalidDataException("存档是另一个版本的(" + root["v"] + ")");
                CheckTiles(Path.Combine(folder, TilesFile));
            }
            catch (Exception exception)
            {
                detail = Path.GetFileName(folder) + " 读不出来: " + exception.Message;
                Console.Error.WriteLine("[error] 存档 " + detail);
                continue;
            }
            _loading = root;
            _loadingFolder = folder;
            LoadFailed = false;
            LoadError = "";
            PendingSeats = ReadSeats(Path.Combine(folder, SeatsFile));
            ServerRoutes.ScenarioName = (string)root["scenario"] ?? "";
            ServerRoutes.ScenarioKey = (string)root["scenario_key"] ?? "";
            ServerRoutes.ScenarioFrame = (string)root["frame"] ?? "";
            ServerRoutes.ScenarioSize = (int?)root["size"] ?? -1;
            ServerRoutes.ScenarioState = "loading";
            ServerRoutes.ScenarioDetail = "";
            if (DateTime.TryParse((string)root["saved"], null, global::System.Globalization.DateTimeStyles.RoundtripKind, out DateTime saved)) LastSaved = saved.ToUniversalTime();
            // 照游戏里读档的走法：清世界(模组的清理补丁跟着跑)，再"读数据"——本体(见 QueueSteps)排世界核心的步骤，
            // 模组的后置补丁把它那份数据的步骤排在后面
            World.world.clearWorld();
            SmoothLoader.prepare();
            new SaveManager().loadData(new SavedMap(), folder);
            detail = Path.GetFileName(folder);
            return true;
        }
        return false;
    }

    private static void CheckTiles(string path)
    {
        using var file = File.OpenRead(path);
        using var packed = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(packed, Encoding.UTF8);
        if (reader.ReadUInt32() != TilesMagic) throw new InvalidDataException("地形文件不对");
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        if (width <= 0 || height <= 0 || width % Config.MAP_BLOCK_SIZE != 0 || height % Config.MAP_BLOCK_SIZE != 0) throw new InvalidDataException("地形文件里的地图大小不对");
    }

    private static Dictionary<string, long> ReadSeats(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            // 读不出来就当各席位都没领着君主
        }
        return null;
    }

    // SaveManager.loadData 的本体：把世界核心的几步排进加载队列
    internal static void QueueSteps(string folder)
    {
        Queue("LOADING WORLD TILES", LoadTiles);
        Queue("LOADING WORLD REALMS", LoadRealms);
        // 建人这一步走原版的名字(模组在它前后各有一段：先修城与国的归属，后记下会话起点)
        Queue("LOADING WORLD PEOPLE", () => new SaveManager().loadActors());
        Queue("LOADING WORLD WARS", LoadWars);
        SmoothLoader.add(() =>
        {
            _loading = null;
            if (!LoadFailed) World.world.finishingUpLoading();
        }, "FINISHING WORLD");
    }

    private static void Queue(string id, Action step) => SmoothLoader.add(() =>
    {
        if (LoadFailed) return;
        try
        {
            step();
        }
        catch (Exception exception)
        {
            LoadFailed = true;
            LoadError = id + ": " + exception.Message;
            Console.Error.WriteLine("[error] 读档 " + id + " 出错: " + exception);
        }
    }, id);

    private static void LoadTiles()
    {
        MapBox world = World.world;
        JObject root = _loading;
        using var file = File.OpenRead(Path.Combine(_loadingFolder, TilesFile));
        using var packed = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(new BufferedStream(packed, 1 << 16), Encoding.UTF8);
        reader.ReadUInt32();
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        world.setMapSize(width / Config.MAP_BLOCK_SIZE, height / Config.MAP_BLOCK_SIZE);
        int count = reader.ReadInt32();
        var main = new TileType[count + 1];
        var top = new TopTileType[count + 1];
        for (int i = 1; i <= count; i++)
        {
            string name = reader.ReadString();
            if (AssetManager.tiles.has(name)) main[i] = AssetManager.tiles.get(name);
            else top[i] = AssetManager.top_tiles.get(name);
        }
        WorldTile[] tiles = world.tiles_list;
        for (int i = 0; i < tiles.Length; i++)
        {
            ushort mainIndex = reader.ReadUInt16(), topIndex = reader.ReadUInt16();
            short tileHeight = reader.ReadInt16();
            tiles[i].setTileTypes(mainIndex <= count ? main[mainIndex] : null, topIndex <= count ? top[topIndex] : null, false);
            tiles[i].Height = tileHeight;
        }
        world.world_time = (double?)root["world_time"] ?? 0d;
        world.world_laws.init();
        if (root["laws"] is JObject laws)
            foreach (JProperty law in laws.Properties())
            {
                if (!world.world_laws.dict.TryGetValue(law.Name, out PlayerOptionData option)) world.world_laws.dict[law.Name] = option = new PlayerOptionData();
                option.boolVal = (bool)law.Value;
            }
        if (root["map_stats"] is JObject mapStats) Json.Populate(mapStats.CreateReader(), world.map_stats);
        if (root["game_stats"] is JObject gameStats) Json.Populate(gameStats.CreateReader(), world.game_stats.data);
    }

    private static IEnumerable<JObject> Items(string key) => _loading[key] is JArray list ? list.OfType<JObject>() : Enumerable.Empty<JObject>();
    private static long Ref(JToken token) => token == null || token.Type == JTokenType.Null ? -1L : (long)token;
    private static TData Data<TData>(JObject item) where TData : new() => item["d"] is JObject data ? data.ToObject<TData>(Json) : new TData();

    private static TObject Restore<TObject, TData>(SystemManager<TObject, TData> manager, JObject item, Action<TObject> prepare = null)
        where TObject : NanoObject, ILoadable<TData>, new() where TData : BaseSystemData, new()
    {
        TObject made = manager.ShimRestore(Data<TData>(item), prepare);
        Apply(made, item["f"] as JObject);
        return made;
    }

    private static void Traits(HashSet<string> into, JObject item)
    {
        if (item["tr"] is not JArray traits) return;
        foreach (JToken trait in traits) into.Add((string)trait);
    }

    // 文化、语言、宗教、宗族、家族，再是国与城(只连国、城之间和它们同文化宗教的归属；君主、城主等人建好了再连)
    private static void LoadRealms()
    {
        MapBox world = World.world;
        foreach (JObject item in Items("cultures"))
        {
            Culture culture = Restore(world.cultures, item);
            Traits(culture.ShimTraitIds, item);
            if (item["names"] is not JObject names) continue;
            foreach (JProperty entry in names.Properties())
            {
                OnomasticsData data = culture.getOnomasticData((MetaType)int.Parse(entry.Name), true);
                data.setTemplateData(entry.Value["t"] is JArray template ? template.Select(part => (string)part).ToList() : new List<string>());
                if (entry.Value["g"] is JObject groups)
                    foreach (JProperty group in groups.Properties()) data.setGroup(group.Name, (string)group.Value);
            }
        }
        foreach (JObject item in Items("languages")) Traits(Restore(world.languages, item).ShimTraitIds, item);
        foreach (JObject item in Items("religions")) Traits(Restore(world.religions, item).ShimTraitIds, item);
        foreach (JObject item in Items("clans")) Traits(Restore(world.clans, item).ShimTraitIds, item);
        foreach (JObject item in Items("families")) Restore(world.families, item);
        if (_loading["wild"] is JArray wild)
            foreach (JToken species in wild) world.kingdoms.ShimWild((string)species);

        foreach (JObject item in Items("kingdoms"))
        {
            string kind = (string)item["as"];
            Kingdom kingdom = Restore(world.kingdoms, item, fresh =>
            {
                if (kind == null) return;
                if (!AssetManager.kingdoms.dict.ContainsKey(kind)) ShimSpecies.EnsureRealmKinds(kind);
                fresh.asset = AssetManager.kingdoms.get(kind);
            });
            Traits(kingdom.ShimTraitIds, item);
            kingdom.culture = world.cultures.get(Ref(item["cu"]));
            kingdom.language = world.languages.get(Ref(item["la"]));
            kingdom.religion = world.religions.get(Ref(item["re"]));
            if (item["loc"] is JArray at && at.Count == 3) kingdom.location = new Vector3((float)at[0], (float)at[1], (float)at[2]);
        }
        foreach (JObject item in Items("cities"))
        {
            City city = Restore(world.cities, item);
            city.status ??= new CityStatus();
            if (item["st"] is JObject status) Json.Populate(status.CreateReader(), city.status);
            city.kingdom = world.kingdoms.get(Ref(item["k"]));
            city.culture = world.cultures.get(Ref(item["cu"]));
            city.language = world.languages.get(Ref(item["la"]));
            city.religion = world.religions.get(Ref(item["re"]));
            if (item["z"] is JArray zones)
                foreach (JToken zoneId in zones)
                {
                    TileZone zone = world.zone_calculator.getZoneByID((int)zoneId);
                    if (zone == null || zone.city != null) continue;
                    city.zones.Add(zone);
                    zone.city = city;
                }
            long tile = Ref(item["t"]);
            if (tile >= 0 && tile < world.tiles_list.Length) city._city_tile = world.tiles_list[tile];
            if (item["cc"] is JArray center && center.Count == 2) city.city_center = new Vector2((float)center[0], (float)center[1]);
            if (item["lc"] is JArray last && last.Count == 2) city.last_city_center = new Vector2((float)last[0], (float)last[1]);
        }
        // 各国的城照存档里的次序排(头一座多半是都城，模组有的地方按次序取)
        foreach (JObject item in Items("kingdoms"))
        {
            Kingdom kingdom = world.kingdoms.get(Ref(item["d"]?["id"]));
            if (kingdom == null) continue;
            if (item["ci"] is JArray cities)
                foreach (JToken cityId in cities)
                {
                    City city = world.cities.get((long)cityId);
                    if (city != null && city.kingdom == kingdom && !kingdom.cities.Contains(city)) kingdom.cities.Add(city);
                }
            kingdom.capital = world.cities.get(Ref(item["cap"]));
        }
        foreach (City city in world.cities)
            if (city.kingdom != null && !city.kingdom.cities.Contains(city)) city.kingdom.cities.Add(city);
        ShimCities.MarkDirty();
        ShimCities.Refresh();
        Next(world.cultures, "cultures");
        Next(world.languages, "languages");
        Next(world.religions, "religions");
        Next(world.clans, "clans");
        Next(world.families, "families");
        Next(world.kingdoms, "kingdoms");
        Next(world.cities, "cities");
    }

    private static void Next<TObject, TData>(SystemManager<TObject, TData> manager, string key)
        where TObject : NanoObject, ILoadable<TData>, new() where TData : BaseSystemData, new()
    {
        long next = Ref(_loading["next"]?[key]);
        if (next > 0) manager.ShimNextId = next;
    }

    // 人物(SaveManager.loadActors 的本体)：先都建出来，再连各自的归属、配偶、父母，最后把君主、城主接回各国各城
    internal static void LoadActors()
    {
        MapBox world = World.world;
        var made = new List<(Actor actor, JObject item)>();
        foreach (JObject item in Items("units"))
        {
            Actor actor = Restore(world.units, item);
            actor.asset = AssetManager.actor_library.get(actor.data.asset_id ?? "human");
            actor.ShimProfession = (UnitProfession)((int?)item["p"] ?? 0);
            if (item["tr"] is JArray traits)
                foreach (JToken trait in traits) actor.ShimRestoreTrait((string)trait);
            if (item["st"] is JObject stats)
                foreach (JProperty stat in stats.Properties()) actor.stats[stat.Name] = (float)stat.Value;
            // 才干与等级(以前的档没存，重开后全是 0：照新造人掷一回)
            if (item["ab"] is JArray abilities && abilities.Count >= 5)
            {
                actor.ShimSetAbilities((int)abilities[0], (int)abilities[1], (int)abilities[2], (int)abilities[3]);
                actor.level = Math.Max(1, (int)abilities[4]);
            }
            else actor.ShimRollAbilities();
            made.Add((actor, item));
        }
        foreach ((Actor actor, JObject item) in made)
        {
            long realm = Ref(item["k"]);
            Kingdom kingdom = realm >= 0 ? world.kingdoms.get(realm) : null;
            // 没入国的(或所属之国已不在的)挂回本族的野处
            actor.kingdom = kingdom ?? world.kingdoms.ShimWild(actor.data.asset_id);
            if (kingdom != null) kingdom.units.Add(actor);
            Join(actor.city = world.cities.get(Ref(item["c"])), actor);
            Join(actor.clan = world.clans.get(Ref(item["cl"])), actor);
            Join(actor.culture = world.cultures.get(Ref(item["cu"])), actor);
            Join(actor.religion = world.religions.get(Ref(item["re"])), actor);
            Join(actor.language = world.languages.get(Ref(item["la"])), actor);
            Join(actor.family = world.families.get(Ref(item["fa"])), actor);
            actor.subspecies = ActorManager.SpeciesOf((string)item["sp"] ?? actor.data.asset_id);
            actor.subspecies.units.Add(actor);
            actor.lover = world.units.get(Ref(item["lo"]));
            long tile = Ref(item["t"]);
            if (tile >= 0 && tile < world.tiles_list.Length) actor.setCurrentTile(world.tiles_list[tile]);
            world.units.get(actor.data.parent_id_1)?.ShimRestoreChild(actor);
            world.units.get(actor.data.parent_id_2)?.ShimRestoreChild(actor);
        }
        foreach (JObject item in Items("kingdoms"))
        {
            Kingdom kingdom = world.kingdoms.get(Ref(item["d"]?["id"]));
            if (kingdom != null) kingdom.king = world.units.get(Ref(item["king"]));
        }
        foreach (JObject item in Items("cities"))
        {
            City city = world.cities.get(Ref(item["d"]?["id"]));
            if (city != null) city.leader = world.units.get(Ref(item["lead"]));
        }
        Next(world.units, "units");
    }

    private static void Join<TData>(MetaObject<TData> group, Actor actor) where TData : MetaObjectData => group?.units.Add(actor);

    // 战事、同盟、两国之间记着的往来
    private static void LoadWars()
    {
        MapBox world = World.world;
        foreach (JObject item in Items("wars"))
        {
            War war = Restore(world.wars, item);
            foreach (long id in war.data.list_attackers)
                if (world.kingdoms.get(id) is { } attacker) war._list_attackers.Add(attacker);
            foreach (long id in war.data.list_defenders)
                if (world.kingdoms.get(id) is { } defender) war._list_defenders.Add(defender);
            war.main_attacker = world.kingdoms.get(war.data.main_attacker);
            war.main_defender = world.kingdoms.get(war.data.main_defender);
        }
        foreach (JObject item in Items("alliances"))
        {
            Alliance alliance = Restore(world.alliances, item);
            if (item["m"] is not JArray members) continue;
            foreach (JToken memberId in members)
            {
                Kingdom member = world.kingdoms.get((long)memberId);
                if (member == null || !alliance.kingdoms_hashset.Add(member)) continue;
                alliance.kingdoms_list.Add(member);
                member.ShimAlliance = alliance;
            }
        }
        foreach (JObject item in Items("relations"))
            world.diplomacy.ShimRestoreRelation(Ref(item["a"]), Ref(item["b"]), item["d"] is JObject data ? data.ToObject<DiplomacyRelationData>(Json) : null);
        Next(world.wars, "wars");
        Next(world.alliances, "alliances");
        Next(world.diplomacy, "relations");
    }

    // ───────── 对象上的零散数值 ─────────
    // 国、城、人这些对象上，除了"属于谁"之外还带着些开关和数(才干、等级、是不是野处……)。有哪些随模组用到的原版接口增减，
    // 所以不逐个列：凡是公开的、能读能写的简单值(真假、整数、小数、文字、枚举)都存，同新建对象时一样的不占地方

    private sealed class Member
    {
        public string Name;
        public Type Type;
        public Func<object, object> Get;
        public Action<object, object> Set;
        public object Fresh;
    }

    private static readonly Dictionary<Type, List<Member>> MembersByType = new();
    // 这几样另有去处(记在数据里，或是算出来的)
    private static readonly HashSet<string> Skipped = new() { "id", "name", "meta_type", "age", "renown", "money", "loot", "species_id" };

    private static bool Simple(Type type) =>
        type == typeof(bool) || type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) || type == typeof(string) || type.IsEnum;

    private static List<Member> MembersOf(Type type)
    {
        if (MembersByType.TryGetValue(type, out List<Member> known)) return known;
        var members = new List<Member>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (FieldInfo field in type.GetFields(flags))
            if (!field.IsInitOnly && !field.IsLiteral && Simple(field.FieldType) && !Skipped.Contains(field.Name))
                members.Add(new Member { Name = field.Name, Type = field.FieldType, Get = field.GetValue, Set = field.SetValue });
        foreach (PropertyInfo property in type.GetProperties(flags))
            if (property.CanRead && property.CanWrite && property.GetSetMethod() != null && property.GetIndexParameters().Length == 0 && Simple(property.PropertyType) &&
                !Skipped.Contains(property.Name))
                members.Add(new Member { Name = property.Name, Type = property.PropertyType, Get = property.GetValue, Set = property.SetValue });
        object fresh = null;
        try
        {
            fresh = Activator.CreateInstance(type);
        }
        catch (Exception)
        {
            // 建不出样板：那就都存
        }
        foreach (Member member in members)
        {
            try
            {
                member.Fresh = fresh == null ? null : member.Get(fresh);
            }
            catch (Exception)
            {
                member.Fresh = null;
            }
        }
        return MembersByType[type] = members;
    }

    private static JObject Values(object item)
    {
        var values = new JObject();
        foreach (Member member in MembersOf(item.GetType()))
        {
            object value;
            try
            {
                value = member.Get(item);
            }
            catch (Exception)
            {
                continue;
            }
            if (value == null || value.Equals(member.Fresh)) continue;
            values[member.Name] = member.Type.IsEnum ? Convert.ToInt64(value) : JToken.FromObject(value);
        }
        return values;
    }

    private static void Apply(object item, JObject values)
    {
        if (values == null) return;
        foreach (Member member in MembersOf(item.GetType()))
        {
            if (!values.TryGetValue(member.Name, out JToken value)) continue;
            try
            {
                member.Set(item, member.Type.IsEnum ? Enum.ToObject(member.Type, (long)value) : value.ToObject(member.Type));
            }
            catch (Exception)
            {
                // 这一项对不上(接口变了)：留着新建时的值
            }
        }
    }

    // ───────── 核对 ─────────

    // 一行世界的现状，带几个校验数：存之前、读回来之后各打一行，两行一样就是原样读回来了
    internal static string Digest()
    {
        MapBox world = World.world;
        ulong tiles = 14695981039346656037UL, realms = tiles, people = tiles;

        static void Mix(ref ulong hash, long value)
        {
            for (int i = 0; i < 8; i++) hash = (hash ^ (byte)(value >> (i * 8))) * 1099511628211UL;
        }

        static void MixText(ref ulong hash, string text)
        {
            foreach (char letter in text ?? "") hash = (hash ^ letter) * 1099511628211UL;
        }

        foreach (WorldTile tile in world.tiles_list)
        {
            MixText(ref tiles, tile.Type?.id);
            Mix(ref tiles, tile.Height);
        }
        foreach (TileZone zone in world.zone_calculator.zones) Mix(ref tiles, zone.city?.id ?? -1);
        foreach (Kingdom kingdom in world.kingdoms.Where(Alive).OrderBy(item => item.id))
        {
            Mix(ref realms, kingdom.id);
            MixText(ref realms, kingdom.name);
            Mix(ref realms, kingdom.king?.id ?? -1);
            Mix(ref realms, kingdom.capital?.id ?? -1);
            Mix(ref realms, kingdom.culture?.id ?? -1);
            foreach (City city in kingdom.cities) Mix(ref realms, city.id);
            Mix(ref realms, kingdom.units.Count);
        }
        foreach (City city in world.cities.Where(Alive).OrderBy(item => item.id))
        {
            Mix(ref realms, city.id);
            MixText(ref realms, city.name);
            Mix(ref realms, city.kingdom?.id ?? -1);
            Mix(ref realms, city.leader?.id ?? -1);
            Mix(ref realms, city.zones.Count);
            Mix(ref realms, city.units.Count);
        }
        foreach (War war in world.wars.Where(Alive).OrderBy(item => item.id))
        {
            Mix(ref realms, war.id);
            foreach (Kingdom side in war._list_attackers) Mix(ref realms, side.id);
            foreach (Kingdom side in war._list_defenders) Mix(ref realms, -side.id);
        }
        foreach (Actor actor in world.units.Where(Alive).OrderBy(item => item.id))
        {
            Mix(ref people, actor.id);
            MixText(ref people, actor.name);
            Mix(ref people, actor.kingdom?.id ?? -1);
            Mix(ref people, actor.city?.id ?? -1);
            Mix(ref people, actor.clan?.id ?? -1);
            Mix(ref people, actor.getAge());
            Mix(ref people, actor.stewardship * 1000 + actor.warfare);
        }
        return Date.getCurrentYear() + " 年 " + Date.getCurrentMonth() + " 月(" + world.world_time.ToString("0.00") + ")：" + world.kingdoms.Count + " 国 " + world.cities.Count + " 城 " +
               world.units.Count + " 人 " + world.wars.Count + " 战 " + world.alliances.Count + " 盟 " + world.clans.Count + " 族 " + world.cultures.Count + " 文化；帝国 " +
               (ModClass.EMPIRE_MANAGER?.Count ?? 0) + " 法理 " + (ModClass.KINGDOM_TITLE_MANAGER?.Count ?? 0) + "；校验 地 " + (tiles & 0xffffff).ToString("x6") + " 国城 " +
               (realms & 0xffffff).ToString("x6") + " 人 " + (people & 0xffffff).ToString("x6");
    }
}
