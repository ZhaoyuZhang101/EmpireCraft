#if !EC_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Terminal;

// 把服务器核心跑过的这一局带回游戏接着玩(用户 2026-11-16 "第二个就用双向")：
//   · 拿游戏眼下的世界生成一份原版存档(SavedMap：地形、亚种、书、物品、建筑这些照游戏的)，
//     地形先照核心的 tiles.bin 把改过的格子改过来；
//   · 国、城、人、宗族、文化、语言、宗教、家族、战争、同盟、两国往来、世界统计与时间、法则换成核心 save/ 里的，
//     核心记在别处的归属(国的君主都城、城的国与城主与地块、人的国城族文化宗教语言家族配偶与所在格、特质、职业、等级)填回原版数据的字段；
//   · 再走原版读档(SaveManager.loadData)，模组数据从 <状态目录>/save/ 那份读(读档补丁照常读 EmpireCraftModData.json)。
// 原版的剧情(plot)、军队(army)不带回：剧情由模组的决议系统读档后照它的记录重建，军队归模组的军团；
// 人的亚种按物种对回游戏里现有的亚种(核心里出生的人没有亚种编号)。只在纯净地图下用(建筑在核心里只是台账)
public static partial class CoreSaveExport
{
    public static bool Import(string stateFolder, out string error)
    {
        error = null;
        MapBox world = World.world;
        if (world == null || !Config.game_loaded || SmoothLoader.isLoading())
        {
            error = "游戏还在加载，稍后再带回";
            return false;
        }
        if (!EmpireCraft.Scripts.GeneralSystems.PureMapMode.Enabled)
        {
            error = "要先开纯净地图";
            return false;
        }
        string folder = Path.Combine(stateFolder, "save");
        string worldPath = Path.Combine(folder, "world.json"), tilesPath = Path.Combine(folder, "tiles.bin");
        if (!File.Exists(worldPath) || !File.Exists(tilesPath) || !File.Exists(Path.Combine(folder, Data.DataManager.EmpireCraftSaveFileName)))
        {
            error = "状态目录里没有完整的核心存档";
            return false;
        }
        try
        {
            JObject root;
            using (var reader = new JsonTextReader(new StreamReader(worldPath, Encoding.UTF8)))
                root = JObject.Load(reader);
            string gameId = (string)root["game_id"];
            if (!string.IsNullOrEmpty(CoreGameId.Value) && !string.IsNullOrEmpty(gameId) && gameId != CoreGameId.Value)
            {
                error = "核心那份存档不是这一局的";
                return false;
            }
            if (!ApplyTiles(world, tilesPath, out error)) return false;
            SavedMap map = SaveManager.currentWorldToSavedMap();
            Replace(map, root, world);
            if (!string.IsNullOrEmpty(gameId)) CoreGameId.Value = gameId;
            SmoothLoader.prepare();
            world.save_manager.loadData(map, folder);
            LogService.LogInfo($"[EmpireCraft][核心接手] 带回：{map.kingdoms.Count} 国 {map.cities.Count} 城 {map.actors_data.Count} 人，开始读档");
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            LogService.LogError("[EmpireCraft][核心接手] 带回失败: " + exception);
            return false;
        }
    }

    // 核心改过的地形改回游戏的格子上(核心开发雨林、改高原这些会动地形)；宽高不同说明不是同一张图
    private static bool ApplyTiles(MapBox world, string path, out string error)
    {
        error = null;
        using var file = File.OpenRead(path);
        using var packed = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(new BufferedStream(packed, 1 << 16), Encoding.UTF8);
        if (reader.ReadUInt32() != TilesMagic)
        {
            error = "核心的地形文件认不出";
            return false;
        }
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        if (width != MapBox.width || height != MapBox.height)
        {
            error = $"核心的地图({width}×{height})和游戏里的({MapBox.width}×{MapBox.height})不一样大";
            return false;
        }
        int count = reader.ReadInt32();
        var main = new TileType[count + 1];
        var top = new TopTileType[count + 1];
        for (int i = 1; i <= count; i++)
        {
            string name = reader.ReadString();
            if (AssetManager.tiles.dict.TryGetValue(name, out TileType type)) main[i] = type;
            else if (AssetManager.top_tiles.dict.TryGetValue(name, out TopTileType topType)) top[i] = topType;
        }
        WorldTile[] tiles = world.tiles_list;
        int changed = 0;
        for (int i = 0; i < tiles.Length; i++)
        {
            ushort mainIndex = reader.ReadUInt16(), topIndex = reader.ReadUInt16();
            short tileHeight = reader.ReadInt16();
            TileType mainType = mainIndex <= count ? main[mainIndex] : null;
            TopTileType topType = topIndex <= count ? top[topIndex] : null;
            WorldTile tile = tiles[i];
            if (mainType != null && (tile.main_type != mainType || tile.top_type != topType))
            {
                tile.setTileTypes(mainType, topType);
                changed++;
            }
            if (tile.Height != tileHeight) tile.Height = tileHeight;
        }
        if (changed > 0) LogService.LogInfo($"[EmpireCraft][核心接手] 带回：{changed} 格地形照核心改过");
        return true;
    }

    private static IEnumerable<JObject> Items(JObject root, string key) =>
        root[key] is JArray list ? list.OfType<JObject>() : Enumerable.Empty<JObject>();

    private static long Ref(JToken token) => token == null || token.Type == JTokenType.Null ? -1L : (long)token;

    private static T DataOf<T>(JObject item) where T : new() => item["d"] is JObject data ? data.ToObject<T>(Json) ?? new T() : new T();

    private static List<string> TraitsOf(JObject item) =>
        item["tr"] is JArray traits ? traits.Select(trait => (string)trait).Where(trait => !string.IsNullOrEmpty(trait)).ToList() : new List<string>();

    private static void Replace(SavedMap map, JObject root, MapBox world)
    {
        // 世界统计、时间、法则
        if (root["map_stats"] is JObject stats) Json.Populate(stats.CreateReader(), map.mapStats);
        map.mapStats.world_time = (double?)root["world_time"] ?? map.mapStats.world_time;
        if (root["laws"] is JObject laws)
            foreach (JProperty law in laws.Properties())
            {
                if (!map.worldLaws.dict.TryGetValue(law.Name, out PlayerOptionData option))
                    map.worldLaws.dict[law.Name] = option = new PlayerOptionData(law.Name);
                option.boolVal = (bool)law.Value;
            }

        // 文化：起名模板游戏里有这个文化就用游戏的，核心里新起的照核心的模板拼回原版的写法
        var oldCultures = map.cultures.Where(data => data != null).ToDictionary(data => data.id);
        map.cultures = Items(root, "cultures").Select(item =>
        {
            var data = DataOf<CultureData>(item);
            data.saved_traits = TraitsOf(item);
            if (oldCultures.TryGetValue(data.id, out CultureData old) && old.onomastics != null && old.onomastics.Count > 0)
                data.onomastics = new Dictionary<MetaType, string>(old.onomastics);
            else data.onomastics = Onomastics(item["names"] as JObject);
            return data;
        }).ToList();
        map.languages = Items(root, "languages").Select(item =>
        {
            var data = DataOf<LanguageData>(item);
            data.saved_traits = TraitsOf(item);
            return data;
        }).ToList();
        map.religions = Items(root, "religions").Select(item =>
        {
            var data = DataOf<ReligionData>(item);
            data.saved_traits = TraitsOf(item);
            return data;
        }).ToList();
        map.clans = Items(root, "clans").Select(item =>
        {
            var data = DataOf<ClanData>(item);
            data.saved_traits = TraitsOf(item);
            return data;
        }).ToList();
        map.families = Items(root, "families").Select(DataOf<FamilyData>).ToList();

        map.kingdoms = Items(root, "kingdoms").Select(item =>
        {
            var data = DataOf<KingdomData>(item);
            data.saved_traits = TraitsOf(item);
            data.id_culture = Ref(item["cu"]);
            data.id_language = Ref(item["la"]);
            data.id_religion = Ref(item["re"]);
            data.kingID = Ref(item["king"]);
            data.capitalID = Ref(item["cap"]);
            return data;
        }).ToList();
        var kingdomIds = new HashSet<long>(map.kingdoms.Select(data => data.id));

        int zonesWide = world.zone_calculator.zones_total_x;
        map.cities = Items(root, "cities").Select(item =>
        {
            var data = DataOf<CityData>(item);
            data.kingdomID = Ref(item["k"]);
            data.leaderID = Ref(item["lead"]);
            data.id_culture = Ref(item["cu"]);
            data.id_language = Ref(item["la"]);
            data.id_religion = Ref(item["re"]);
            data.zones = item["z"] is JArray zones
                ? zones.Select(zone => (int)zone).Select(id => new ZoneData { x = id % zonesWide, y = id / zonesWide }).ToList()
                : new List<ZoneData>();
            return data;
        }).ToList();
        var cityIds = new HashSet<long>(map.cities.Select(data => data.id));

        // 人：亚种按物种对回游戏里现有的(先用它自己原来的；核心里出生的按物种取人最多的那个)
        var subspecies = map.subspecies.Where(data => data != null).ToList();
        var subspeciesIds = new HashSet<long>(subspecies.Select(data => data.id));
        var bySpecies = new Dictionary<string, long>();
        foreach (IGrouping<string, SubspeciesData> group in subspecies.GroupBy(data => data.species_id ?? ""))
            bySpecies[group.Key] = group.First().id;
        int width = MapBox.width, dropped = 0;
        var actors = new List<ActorData>();
        foreach (JObject item in Items(root, "units"))
        {
            var data = DataOf<ActorData>(item);
            string species = (string)item["sp"] ?? data.asset_id ?? "human";
            if (!subspeciesIds.Contains(data.subspecies))
            {
                if (!bySpecies.TryGetValue(species, out long match) && !bySpecies.TryGetValue(data.asset_id ?? "", out match))
                {
                    dropped++;
                    continue;
                }
                data.subspecies = match;
            }
            long realm = Ref(item["k"]);
            data.civ_kingdom_id = kingdomIds.Contains(realm) ? realm : -1L;
            long city = Ref(item["c"]);
            data.cityID = cityIds.Contains(city) ? city : -1L;
            data.clan = Ref(item["cl"]);
            data.culture = Ref(item["cu"]);
            data.religion = Ref(item["re"]);
            data.language = Ref(item["la"]);
            data.family = Ref(item["fa"]);
            data.lover = Ref(item["lo"]);
            data.army = -1L;
            data.plot = -1L;
            data.transportID = -1L;
            data.saved_traits = TraitsOf(item);
            if (item["p"] != null) data.profession = (UnitProfession)(int)item["p"];
            long tile = Ref(item["t"]);
            if (tile >= 0)
            {
                data.x = (int)(tile % width);
                data.y = (int)(tile / width);
            }
            // 才干：核心里出生的人原版数据里没有底子，照核心记的补上(原版的人数据里本来就有的不动)
            if (item["ab"] is JArray abilities && abilities.Count >= 5)
            {
                if (data["stewardship"] <= 0f) data["stewardship"] = (float)abilities[0];
                if (data["warfare"] <= 0f) data["warfare"] = (float)abilities[1];
                if (data["intelligence"] <= 0f) data["intelligence"] = (float)abilities[2];
                if (data["diplomacy"] <= 0f) data["diplomacy"] = (float)abilities[3];
                data.level = Math.Max(1, (int)abilities[4]);
            }
            actors.Add(data);
        }
        if (dropped > 0) LogService.LogWarning($"[EmpireCraft][核心接手] 带回：{dropped} 人的物种在游戏里没有亚种，没带回");
        map.actors_data = actors;
        map.mapStats.population = actors.Count;

        map.wars = Items(root, "wars").Select(DataOf<WarData>).ToList();
        map.alliances = Items(root, "alliances").Select(item =>
        {
            var data = DataOf<AllianceData>(item);
            if (item["m"] is JArray members) data.kingdoms = members.Select(member => (long)member).Where(kingdomIds.Contains).ToList();
            return data;
        }).ToList();
        map.relations = Items(root, "relations").Select(item => item["d"] is JObject data ? data.ToObject<DiplomacyRelationData>(Json) : null)
            .Where(data => data != null && kingdomIds.Contains(data.kingdom1_id) && kingdomIds.Contains(data.kingdom2_id)).ToList();

        // 原版剧情、军队不带回；建筑只留城还在的(纯净地图下实体建筑本来就少)
        map.plots = new List<PlotData>();
        map.armies = new List<ArmyData>();
        map.buildings = map.buildings.Where(data => data != null && (data.cityID < 0 || cityIds.Contains(data.cityID))).ToList();
    }

    // 核心记的起名模板(各段、各组字词)拼回原版文化数据里的写法
    private static Dictionary<MetaType, string> Onomastics(JObject names)
    {
        var result = new Dictionary<MetaType, string>();
        if (names == null) return result;
        foreach (JProperty entry in names.Properties())
        {
            if (!int.TryParse(entry.Name, out int type)) continue;
            var data = new OnomasticsData();
            data.setTemplateData(entry.Value["t"] is JArray template ? template.Select(part => (string)part).ToList() : new List<string>());
            if (entry.Value["g"] is JObject groups)
                foreach (JProperty group in groups.Properties()) data.setGroup(group.Name, (string)group.Value);
            result[(MetaType)type] = data.getShortTemplate();
        }
        return result;
    }
}
#endif
