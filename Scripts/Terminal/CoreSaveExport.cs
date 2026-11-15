#if !EC_SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using EmpireCraft.Scripts.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Terminal;

// 把游戏里眼下这一局写成独立服务器(核心)的存档，让本机拉起的核心接着这一局跑(用户 2026-11-16 选"完整存档接手")。
// 写出来的 <状态目录>/save/ 与核心自己存的同一格式(见 .server/Server/WorldSave.cs)：
//   world.json               世界时间、法则、统计、各类下一个编号；文化(连同起名模板)、语言、宗教、宗族、家族、国、城、人物、战事、同盟、两国往来
//   tiles.bin                每格的底层、表层地形与高度
//   EmpireCraftModData.json  模组自己的那份(政体、帝国、法理、国库、积弊、军团战线、人口台账……)，同游戏存档里的一模一样
//   seats.json               席位所领的君主(接手时没有，写空)
// 核心一律按纯净地图跑：建筑实体不带(纯净地图下建筑本来就记在模组的建筑台账里)；只带文明的人(智慧种族)，野兽、船不带。
// 只在主线程、世界读完时调；同步写完，万人的世界几秒。先写进 save.new/，写全了才换上去，原来的 save/ 留作 save.old/
public static partial class CoreSaveExport
{
    private const int Version = 1;
    private const uint TilesMagic = 0x31544345; // "ECT1"
    private static readonly JsonSerializer Json = JsonSerializer.Create(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    });

    public static bool Export(string stateFolder, out string error)
    {
        error = null;
        MapBox world = World.world;
        if (world == null || !Config.game_loaded || SmoothLoader.isLoading() || world.cities.Count == 0)
        {
            error = "还没有世界(或正在读档)，没什么可导出";
            return false;
        }
        if (!EmpireCraft.Scripts.GeneralSystems.PureMapMode.Enabled)
        {
            error = "要先开纯净地图(核心只按纯净地图跑，带回时建筑实体会没)";
            return false;
        }
        // 这一局的编号：头一回交给核心时生成，之后沿用(随模组数据存档；先定下再写模组数据)
        if (string.IsNullOrEmpty(CoreGameId.Value)) CoreGameId.Value = Guid.NewGuid().ToString("N");
        var clock = Stopwatch.StartNew();
        string fresh = Path.Combine(stateFolder, "save.new"), current = Path.Combine(stateFolder, "save"), previous = Path.Combine(stateFolder, "save.old");
        try
        {
            Directory.CreateDirectory(stateFolder);
            if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            Directory.CreateDirectory(fresh);
            DataManager.SaveAll(fresh);
            if (!File.Exists(Path.Combine(fresh, DataManager.EmpireCraftSaveFileName)))
            {
                error = "模组的数据没写出来";
                return false;
            }
            WriteTiles(world, Path.Combine(fresh, "tiles.bin"));
            WriteWorld(world, Path.Combine(fresh, "world.json"));
            File.WriteAllText(Path.Combine(fresh, "seats.json"), JsonConvert.SerializeObject(TerminalHost.SeatRulers()));
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
            if (Directory.Exists(current)) Directory.Move(current, previous);
            Directory.Move(fresh, current);
            LogService.LogInfo($"[EmpireCraft][核心接手] 导出到 {current}，用时 {clock.ElapsedMilliseconds} 毫秒");
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            LogService.LogError("[EmpireCraft][核心接手] 导出失败: " + exception);
            try
            {
                if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            }
            catch (Exception)
            {
                // 留着下回覆盖
            }
            return false;
        }
    }

    // 状态目录里那份存档是哪一局、走到了哪个世界时刻(只读 world.json 开头，不解析全文)
    public static bool PeekSave(string stateFolder, out string gameId, out double worldTime)
    {
        gameId = null;
        worldTime = -1d;
        string path = Path.Combine(stateFolder, "save", "world.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var reader = new JsonTextReader(new StreamReader(path, Encoding.UTF8));
            bool gotId = false, gotTime = false;
            while (reader.Read() && reader.Depth <= 1)
            {
                if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1) continue;
                string name = (string)reader.Value;
                if (name == "game_id")
                {
                    gameId = reader.ReadAsString();
                    gotId = true;
                }
                else if (name == "world_time")
                {
                    worldTime = reader.ReadAsDouble() ?? -1d;
                    gotTime = true;
                }
                else if (name == "cultures") break;
                if (gotId && gotTime) break;
            }
            return gotTime;
        }
        catch (Exception exception)
        {
            LogService.LogWarning("[EmpireCraft][核心接手] 读存档开头失败: " + exception.Message);
            return false;
        }
    }

    // 存档里记的席位 → 君主(带回后交给 TerminalHost.RestoreSeatRulers)
    public static Dictionary<string, long> ReadSeats(string stateFolder)
    {
        try
        {
            string path = Path.Combine(stateFolder, "save", "seats.json");
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText(path)) ?? new Dictionary<string, long>()
                : new Dictionary<string, long>();
        }
        catch (Exception)
        {
            return new Dictionary<string, long>();
        }
    }

    // ───────── 地形 ─────────
    private static void WriteTiles(MapBox world, string path)
    {
        WorldTile[] tiles = world.tiles_list;
        var names = new List<string>();
        var index = new Dictionary<string, ushort>();

        ushort IndexOf(string id)
        {
            if (id == null) return 0;
            if (index.TryGetValue(id, out ushort known)) return known;
            names.Add(id);
            return index[id] = (ushort)names.Count;
        }

        var cells = new ushort[tiles.Length * 2];
        for (int i = 0; i < tiles.Length; i++)
        {
            cells[i * 2] = IndexOf(tiles[i].main_type?.id);
            cells[i * 2 + 1] = IndexOf(tiles[i].top_type?.id);
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
            writer.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, tiles[i].Height)));
        }
    }

    // ───────── 世界 ─────────
    private static bool Alive(NanoObject item) => item != null && !item.isRekt() && item.isAlive();
    private static JToken Id(NanoObject item) => Alive(item) ? item.getID() : JValue.CreateNull();

    private static bool Civ(Kingdom kingdom) => Alive(kingdom) && !kingdom.wild;

    private static bool Person(Actor actor) => Alive(actor) && actor.data != null && actor.isSapient();

    private static void WriteWorld(MapBox world, string path)
    {
        List<Culture> cultures = world.cultures.Where(Alive).ToList();
        List<Language> languages = world.languages.Where(Alive).ToList();
        List<Religion> religions = world.religions.Where(Alive).ToList();
        List<Clan> clans = world.clans.Where(Alive).ToList();
        List<Family> families = world.families.Where(Alive).ToList();
        List<Kingdom> kingdoms = world.kingdoms.Where(Civ).ToList();
        List<City> cities = world.cities.Where(Alive).ToList();
        List<Actor> units = world.units.Where(Person).ToList();
        List<War> wars = world.wars.Where(Alive).ToList();
        List<Alliance> alliances = world.alliances.Where(Alive).ToList();
        List<DiplomacyRelation> relations = world.diplomacy.Where(relation => relation?.data != null &&
                                                                              Civ(world.kingdoms.get(relation.data.kingdom1_id)) &&
                                                                              Civ(world.kingdoms.get(relation.data.kingdom2_id))).ToList();

        long NextOf<T>(IEnumerable<T> items) where T : NanoObject => items.Select(item => item.getID()).DefaultIfEmpty(0L).Max() + 1L;

        using var stream = new StreamWriter(path, false, new UTF8Encoding(false));
        using var json = new JsonTextWriter(stream);
        json.WriteStartObject();
        Property(json, "v", Version);
        Property(json, "game_id", CoreGameId.Value);
        Property(json, "saved", DateTime.UtcNow.ToString("o"));
        Property(json, "scenario", "");
        Property(json, "scenario_key", "");
        Property(json, "frame", "");
        Property(json, "size", -1);
        Property(json, "world_time", world.getCurWorldTime());
        json.WritePropertyName("laws");
        new JObject(world.world_laws.dict.Where(law => law.Value != null).Select(law => new JProperty(law.Key, law.Value.boolVal))).WriteTo(json);
        json.WritePropertyName("map_stats");
        Json.Serialize(json, world.map_stats);
        json.WritePropertyName("game_stats");
        Json.Serialize(json, world.game_stats.data);
        json.WritePropertyName("next");
        new JObject
        {
            ["kingdoms"] = NextOf(kingdoms), ["cities"] = NextOf(cities), ["units"] = NextOf(world.units.Where(Alive)), ["wars"] = NextOf(wars),
            ["alliances"] = NextOf(alliances), ["clans"] = NextOf(clans), ["cultures"] = NextOf(cultures), ["religions"] = NextOf(religions),
            ["languages"] = NextOf(languages), ["families"] = NextOf(families), ["relations"] = NextOf(world.diplomacy.Where(relation => relation?.data != null))
        }.WriteTo(json);

        Array(json, "cultures", cultures, (output, culture) => Meta(output, culture.data, culture.getTraitsAsStrings(), Names(culture)));
        Array(json, "languages", languages, (output, language) => Meta(output, language.data, language.getTraitsAsStrings()));
        Array(json, "religions", religions, (output, religion) => Meta(output, religion.data, religion.getTraitsAsStrings()));
        Array(json, "clans", clans, (output, clan) => Meta(output, clan.data, clan.getTraitsAsStrings()));
        Array(json, "families", families, (output, family) => Meta(output, family.data, null));
        Array(json, "wild", new List<string>(), (output, species) => output.WriteValue(species));
        Array(json, "kingdoms", kingdoms, WriteRealm);
        Array(json, "cities", cities, WriteTown);
        Array(json, "units", units, WritePerson);
        Array(json, "wars", wars, (output, war) => Meta(output, war.data, null));
        Array(json, "alliances", alliances, (output, alliance) => Meta(output, alliance.data, null, extra: new JObject
        {
            ["m"] = new JArray(alliance.kingdoms_list.Where(Civ).Select(member => member.getID()))
        }));
        Array(json, "relations", relations, (output, relation) =>
        {
            output.WriteStartObject();
            Property(output, "a", relation.data.kingdom1_id);
            Property(output, "b", relation.data.kingdom2_id);
            output.WritePropertyName("d");
            Json.Serialize(output, relation.data);
            output.WriteEndObject();
        });
        json.WriteEndObject();
    }

    private static void Property(JsonWriter writer, string name, object value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }

    private static void Array<T>(JsonWriter writer, string name, IEnumerable<T> items, Action<JsonWriter, T> writeItem)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (T item in items)
        {
            try
            {
                writeItem(writer, item);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][核心接手] {name} 里一条没导出: {exception.Message}");
            }
        }
        writer.WriteEndArray();
    }

    // 一条：原版数据("d") + 特质("tr") + 起名模板("names") + 引用编号等(extra)。先拼成 JObject 再写，写到一半出错不会留下半截
    private static void Meta(JsonWriter writer, object data, IEnumerable<string> traits, JObject names = null, JObject extra = null)
    {
        var item = new JObject { ["d"] = JObject.FromObject(data, Json) };
        List<string> traitIds = traits?.Where(trait => !string.IsNullOrEmpty(trait)).ToList();
        if (traitIds != null && traitIds.Count > 0) item["tr"] = new JArray(traitIds);
        if (names != null && names.Count > 0) item["names"] = names;
        if (extra != null)
            foreach (JProperty property in extra.Properties()) item[property.Name] = property.Value;
        item.WriteTo(writer);
    }

    // 文化的起名模板：人、族、城、国……各一套(模板的各段、各组的字词)
    private static JObject Names(Culture culture)
    {
        var all = new JObject();
        if (culture._onomastics_data is not IDictionary table) return all;
        foreach (DictionaryEntry entry in table)
        {
            if (entry.Value is not OnomasticsData data) continue;
            List<string> template = data.getFullTemplateData() ?? new List<string>();
            if (template.Count == 0 && data.groups.Count == 0) continue;
            all[((int)(MetaType)entry.Key).ToString()] = new JObject
            {
                ["t"] = new JArray(template),
                ["g"] = new JObject(data.groups.Where(group => group.Value != null)
                    .Select(group => new JProperty(group.Key, group.Value.characters_string ?? "")))
            };
        }
        return all;
    }

    private static void WriteRealm(JsonWriter writer, Kingdom kingdom)
    {
        Meta(writer, kingdom.data, kingdom.getTraitsAsStrings(), extra: new JObject
        {
            ["as"] = kingdom.asset?.id,
            ["cu"] = Id(kingdom.culture),
            ["la"] = Id(kingdom.language),
            ["re"] = Id(kingdom.religion),
            ["cap"] = Id(kingdom.capital),
            ["king"] = Person(kingdom.king) ? kingdom.king.getID() : JValue.CreateNull(),
            ["ci"] = new JArray(kingdom.cities.Where(Alive).Select(city => city.getID())),
            ["loc"] = new JArray(kingdom.location.x, kingdom.location.y, kingdom.location.z)
        });
    }

    private static void WriteTown(JsonWriter writer, City city)
    {
        var extra = new JObject
        {
            ["k"] = Civ(city.kingdom) ? city.kingdom.getID() : JValue.CreateNull(),
            ["cu"] = Id(city.culture),
            ["la"] = Id(city.language),
            ["re"] = Id(city.religion),
            ["lead"] = Person(city.leader) ? city.leader.getID() : JValue.CreateNull(),
            ["z"] = new JArray(city.zones.Where(zone => zone != null).Select(zone => zone.id)),
            ["cc"] = new JArray(city.city_center.x, city.city_center.y),
            ["lc"] = new JArray(city.last_city_center.x, city.last_city_center.y)
        };
        if (city.status != null) extra["st"] = JObject.FromObject(city.status, Json);
        WorldTile seat = city._city_tile ?? city.getTile();
        if (seat != null) extra["t"] = seat.data.tile_id;
        Meta(writer, city.data, null, extra: extra);
    }

    private static void WritePerson(JsonWriter writer, Actor actor)
    {
        var extra = new JObject
        {
            ["c"] = Id(actor.city),
            ["cl"] = Id(actor.clan),
            ["cu"] = Id(actor.culture),
            ["re"] = Id(actor.religion),
            ["la"] = Id(actor.language),
            ["fa"] = Id(actor.family),
            ["lo"] = Person(actor.lover) ? actor.lover.getID() : JValue.CreateNull(),
            ["sp"] = actor.subspecies?.data?.species_id ?? actor.asset?.id,
            ["p"] = (int)actor.data.profession,
            ["ab"] = new JArray(actor.stewardship, actor.warfare, actor.intelligence, actor.diplomacy, actor.data.level),
            ["st"] = new JObject
            {
                ["stewardship"] = (float)actor.stewardship, ["warfare"] = (float)actor.warfare,
                ["intelligence"] = (float)actor.intelligence, ["diplomacy"] = (float)actor.diplomacy
            }
        };
        // 没入国(或所属的是野处)的不记国，核心读回时挂回本族的野处
        if (Civ(actor.kingdom)) extra["k"] = actor.kingdom.getID();
        if (actor.current_tile != null) extra["t"] = actor.current_tile.data.tile_id;
        Meta(writer, actor.data, actor.getTraits()?.Select(trait => trait?.id), extra: extra);
    }
}
#endif
