using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 村镇命名(用户 2026-10-19："按照 Locales/Cultures 里面的名词库来，依据自己的文化；文化配置里加几种村镇命名方式，
// 比如按照姓氏(罗家村)、按照地势(什么什么山村)、按照字库，文化配置里可以绑定独有的村镇字库")：
//   · 按所在城的主流文化(CultureRule.jsonc 的 setting.Settlement，见 SettlementSetting)挑命名方式：
//       - 姓氏：本城宗族的姓(没有就取本文化的姓氏词库) + 姓氏后缀 → 罗家村、伊万诺夫卡；
//       - 地势：看村子四周的地形(山、丘岗、河湖、海边、林、平原、沙漠、沼泽)，前缀 + 地势词 + 后缀 → 青石山村、柳河村；
//       - 字库：字库词 + 后缀 → 桃花村；字库可以绑本文化独有的词库；
//   · 词库都是 CSV(key,cz,en,ch)，直接读当前语言那一列(空着退回英文、简体)；文化没配的项用 Locales/Cultures/Settlements/ 的通用词库；
//   · 同一城里不重名；起好的名字记进存档，不随归属、语言变；手改过的不再动；
//   · 旧版按写死的词库起的名字(读档时认得出来的)清掉，按文化重起。
public static class SettlementNaming
{
    public const int Version = 1;
    private const string Surname = "surname", Terrain = "terrain", Words = "words";
    private const int TerrainRadius = 4, Attempts = 10;

    #region 词库

    private static readonly Dictionary<string, List<string>> Cache = new();
    private static string _language;

    private static string Language()
    {
        try
        {
            string language = LocalizedTextManager.instance?.language ?? "cz";
            return language is "cz" or "ch" or "en" ? language : "en";
        }
        catch
        {
            return "cz";
        }
    }

    private static string Root => Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");

    // 文化配的词库(可写"别的文化:词库名")；没配或读不到就用通用词库 Settlements/<fallback>.csv
    private static List<string> Pool(string culture, string configured, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string owner = culture, group = configured.Trim();
            int split = group.IndexOf(':');
            if (split > 0)
            {
                owner = group.Substring(0, split).Trim();
                group = group.Substring(split + 1).Trim();
            }
            List<string> own = Read(Path.Combine(Root, $"Culture_{owner}", owner + group + ".csv"));
            if (own.Count > 0) return own;
        }
        return string.IsNullOrEmpty(fallback) ? new List<string>() : Read(Path.Combine(Root, "Settlements", fallback + ".csv"));
    }

    // 读当前语言那一列；"_"开头换成空格，单个"_"是空串
    private static List<string> Read(string path)
    {
        string language = Language();
        if (_language != language)
        {
            Cache.Clear();
            _language = language;
        }
        if (Cache.TryGetValue(path, out List<string> cached)) return cached;
        var words = new List<string>();
        try
        {
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                string[] header = lines.Length > 0 ? lines[0].Split(',') : Array.Empty<string>();
                int[] columns = new[] { Array.IndexOf(header, language), Array.IndexOf(header, "en"), Array.IndexOf(header, "cz") }
                    .Where(index => index > 0).Distinct().ToArray();
                for (int i = 1; i < lines.Length && columns.Length > 0; i++)
                {
                    string[] cells = lines[i].Split(',');
                    if (cells.Length == 0 || string.IsNullOrWhiteSpace(cells[0])) continue;
                    string text = columns.Where(index => index < cells.Length).Select(index => cells[index].Trim())
                        .FirstOrDefault(value => value.Length > 0);
                    if (text == null) continue;
                    words.Add(text == "_" ? "" : text.StartsWith("_") ? " " + text.Substring(1) : text);
                }
            }
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][村镇命名] 读词库失败 {path}: {exception.Message}");
        }
        Cache[path] = words;
        return words;
    }

    private static string Pick(List<string> pool, long seed)
    {
        if (pool == null || pool.Count == 0) return null;
        unchecked
        {
            ulong x = (ulong)seed * 0x9E3779B97F4A7C15UL;
            x ^= x >> 29;
            return pool[(int)(x % (ulong)pool.Count)];
        }
    }

    #endregion

    // 给村镇起名并记下(已有名字直接返回)；used 为同城已用的名字
    public static string Name(RuralSettlement settlement, City city, bool town, HashSet<string> used, long fallbackSeed)
    {
        if (settlement != null && !string.IsNullOrEmpty(settlement.name)) return settlement.name;
        used ??= new HashSet<string>();
        long seed = fallbackSeed != 0L || settlement == null ? fallbackSeed : UrbanFabricKey(settlement) + 29L;
        string culture = city?.data != null ? CultureService.GetMainCulture(city, initialize: false) : "";
        if (!CultureService.IsValidCulture(culture)) culture = CultureService.GetRealmCulture(city?.kingdom);
        OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture ?? "", out Setting setting);
        SettlementSetting config = setting?.Settlement;
        string kind = settlement?.fort == true ? "fort" : town ? "town" : "village";
        Dictionary<string, float> modes = kind switch
        {
            "fort" => config?.fort_modes,
            "town" => config?.town_modes,
            _ => config?.village_modes
        };
        // 没配：都按姓氏(本城宗族或本文化的姓)起——通用字库是汉字、英文雅名，不合别的文化
        if (modes == null || modes.Count == 0 || modes.Values.Sum() <= 0f) modes = new Dictionary<string, float> { [Surname] = 1f };

        string name = null;
        for (int attempt = 0; attempt < Attempts && name == null; attempt++)
        {
            long s = seed + attempt * 7919L;
            string mode = PickMode(modes, s);
            string candidate = mode switch
            {
                Surname => BySurname(settlement, city, culture, setting, config, kind, s),
                Terrain => ByTerrain(settlement, culture, config, kind, s),
                _ => ByWords(culture, config, kind, s)
            } ?? ByWords(culture, config, kind, s);
            if (!string.IsNullOrWhiteSpace(candidate) && used.Add(candidate)) name = candidate;
        }
        name ??= ByWords(culture, config, kind, seed + 3) ?? "";
        if (settlement != null && name.Length > 0)
        {
            settlement.name = name;
            settlement.naming_version = Version;
        }
        return name;
    }

    private static long UrbanFabricKey(RuralSettlement settlement) => EmpireCraft.Scripts.GamePatches.UrbanFabric.PosKey(settlement.x, settlement.y);

    private static string PickMode(Dictionary<string, float> modes, long seed)
    {
        float total = modes.Values.Where(weight => weight > 0f).Sum();
        unchecked
        {
            ulong x = (ulong)(seed ^ 0x5DEECE66DL) * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 31;
            float roll = (float)(x % 10000UL) / 10000f * total;
            foreach (KeyValuePair<string, float> pair in modes)
            {
                if (pair.Value <= 0f) continue;
                if (roll < pair.Value) return pair.Key;
                roll -= pair.Value;
            }
        }
        return modes.Keys.First();
    }

    private static string Suffix(string culture, SettlementSetting config, string kind, long seed) => kind switch
    {
        "fort" => Pick(Pool(culture, config?.fort_suffix, "FortSuffix"), seed),
        "town" => Pick(Pool(culture, config?.town_suffix, "TownSuffix"), seed),
        _ => Pick(Pool(culture, config?.village_suffix, "VillageSuffix"), seed)
    } ?? "";

    private static string ByWords(string culture, SettlementSetting config, string kind, long seed)
    {
        List<string> pool = kind switch
        {
            "fort" => Pool(culture, config?.fort_words, "TownWords"),
            "town" => Pool(culture, config?.town_words, "TownWords"),
            _ => Pool(culture, config?.village_words, "VillageWords")
        };
        string word = Pick(pool, seed);
        return string.IsNullOrEmpty(word) ? null : Join(word, Suffix(culture, config, kind, seed + 1));
    }

    // 姓氏：本城宗族的姓，没有就取本文化的姓氏词库
    private static string BySurname(RuralSettlement settlement, City city, string culture, Setting setting,
        SettlementSetting config, string kind, long seed)
    {
        List<string> local = LocalSurnames(city);
        string surname = local.Count > 0 && (seed & 3L) != 0L ? Pick(local, seed) : null;
        if (string.IsNullOrEmpty(surname))
        {
            string library = !string.IsNullOrWhiteSpace(config?.surname_words) ? config.surname_words
                : setting?.Family?.groups != null && setting.Family.groups.TryGetValue("group_1", out string group) ? group : "FamilyNames";
            surname = Pick(Pool(culture, library, null), seed);
        }
        // 复合姓的前半(如阿拉伯词库里的"哈迪-")去掉连字符
        surname = surname?.Trim().TrimEnd('-', '‐', '·', ' ');
        if (string.IsNullOrEmpty(surname)) return null;
        string suffix = kind == "village" ? Pick(Pool(culture, config?.surname_suffix, "SurnameSuffix"), seed + 5)
            : Suffix(culture, config, kind, seed + 5);
        return Join(surname, suffix);
    }

    // 地势：前缀 + 地势词 + 后缀
    private static string ByTerrain(RuralSettlement settlement, string culture, SettlementSetting config, string kind, long seed)
    {
        if (settlement == null) return null;
        string key = TerrainOf(settlement.x, settlement.y);
        string configured = config?.terrain != null && config.terrain.TryGetValue(key, out string library) ? library : null;
        string feature = Pick(Pool(culture, configured, "Terrain" + char.ToUpperInvariant(key[0]) + key.Substring(1)), seed + 11);
        if (string.IsNullOrEmpty(feature)) return null;
        List<string> prefixes = Pool(culture, config?.terrain_prefix, "TerrainPrefix");
        string prefix = Pick(prefixes, seed + 13) ?? "";
        string suffix = kind == "village"
            ? Pick(Pool(culture, config?.terrain_suffix, "TerrainSuffix"), seed + 17) ?? ""
            : Suffix(culture, config, kind, seed + 17);
        return Join(prefix + feature, suffix);
    }

    // 词 + 后缀；词本身已经以后缀结尾的不重复(木村 + 村 → 木村)
    private static string Join(string word, string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return word;
        string bare = suffix.Trim();
        return bare.Length > 0 && word.EndsWith(bare, StringComparison.Ordinal) ? word : word + suffix;
    }

    // 村子四周的地势：山、丘岗、海边、河湖、林、沙漠、沼泽，都不突出算平原
    public static string TerrainOf(int x, int y)
    {
        int land = 0, mountain = 0, hill = 0, forest = 0, desert = 0, swamp = 0, sea = 0, lake = 0;
        for (int dy = -TerrainRadius; dy <= TerrainRadius; dy++)
        for (int dx = -TerrainRadius; dx <= TerrainRadius; dx++)
        {
            if (dx * dx + dy * dy > TerrainRadius * TerrainRadius) continue;
            TileTypeBase type = World.world?.GetTile(x + dx, y + dy)?.Type;
            if (type == null) continue;
            if (type.liquid || type.ocean)
            {
                if (type.ocean) sea++;
                else lake++;
                continue;
            }
            land++;
            string biome = type.biome_id ?? "";
            if (type.mountains || type.block) mountain++;
            else if (type.rocks || type.id == "hills" || biome is "biome_hill" or "biome_rocklands") hill++;
            if (biome is "biome_birch" or "biome_maple" or "biome_forest" or "biome_pine" or "biome_enchanted" or "biome_jungle") forest++;
            if (biome is "biome_desert" or "biome_sand") desert++;
            if (biome is "biome_swamp" or "biome_corrupted") swamp++;
        }
        float all = Mathf.Max(1, land + sea + lake), ground = Mathf.Max(1, land);
        if (mountain / ground >= 0.25f) return "mountain";
        if (sea / all >= 0.2f) return "coast";
        if (lake / all >= 0.08f) return "water";
        if ((mountain + hill) / ground >= 0.25f) return "hill";
        if (forest / ground >= 0.4f) return "forest";
        if (desert / ground >= 0.4f) return "desert";
        if (swamp / ground >= 0.3f) return "swamp";
        return "plain";
    }

    // 本城的宗族(以本城为郡望/祖籍的)的姓：按城缓存一分钟
    private static readonly Dictionary<long, (float at, List<string> names)> Surnames = new();

    private static List<string> LocalSurnames(City city)
    {
        if (city?.data == null) return new List<string>();
        float now = Time.unscaledTime;
        if (Surnames.TryGetValue(city.id, out var hit) && now - hit.at < 60f && now >= hit.at) return hit.names;
        if (Surnames.Count > 512) Surnames.Clear();
        var names = new List<string>();
        foreach (SpecificClan clan in SpecificClanManager._specificClans.ToList())
        {
            if (clan == null || clan.ancestral_city_id != city.id) continue;
            string name = clan.VanillaClanName();
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
        }
        Surnames[city.id] = (now, names);
        return names;
    }

    #region 旧名迁移

    // 旧版写死的词库(MapLabelOverlay 原来的 TownZh/VillageZh/SurnameZh/StreetEn 与各后缀)：认得出来的名字清掉重起
    private static readonly string[] OldTownZh =
    {
        "清河", "白沙", "柳林", "石桥", "青山", "安乐", "长兴", "龙泉", "双溪", "临江", "永丰", "太平", "新安", "古城", "黄土",
        "东关", "西关", "南关", "北关", "三河", "平原", "桃源", "梅岭", "松林", "高桥"
    };
    private static readonly string[] OldVillageZh =
    {
        "桃花", "杏花", "柳树", "槐树", "枣林", "榆树", "前河", "后河", "东湾", "西坡", "南岗", "北塘", "大柳", "小桥", "石门",
        "上坪", "下坪", "芦苇", "荷塘", "水井", "菜园", "果园", "麦地", "牛栏", "羊坊"
    };
    private static readonly string[] OldSurnameZh =
    {
        "张", "王", "李", "赵", "刘", "陈", "杨", "黄", "周", "吴", "徐", "孙", "马", "胡", "朱", "郭", "何", "林", "高", "罗",
        "郑", "梁", "谢", "宋", "唐", "许", "韩", "冯", "邓", "曹"
    };
    private static readonly string[] OldEn =
    {
        "Elm", "Oak", "Maple", "Cedar", "Pine", "King", "Queen", "Church", "Market", "Mill", "River", "Bridge", "High", "Station",
        "Park", "Hill", "Garden", "Castle", "Harbor", "Victoria", "Union", "Liberty", "Spring", "Water", "Grove"
    };

    private static HashSet<string> _oldNames;

    private static HashSet<string> OldNames()
    {
        if (_oldNames != null) return _oldNames;
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Add(IEnumerable<string> words, IEnumerable<string> suffixes)
        {
            foreach (string word in words)
            foreach (string suffix in suffixes)
                set.Add(word + suffix);
        }
        Add(OldTownZh, new[] { "镇", "关" });
        Add(OldVillageZh, new[] { "村", "庄", "湾", "坪" });
        Add(OldSurnameZh, new[] { "家庄", "村", "家村", "屯", "家寨", "家集" });
        Add(OldEn, new[] { "ton", "bury", "ford", "gate", "hold", "watch", "by", "ham", "wick", "field" });
        return _oldNames = set;
    }

    public static void MigrateOldNames(IEnumerable<RuralSettlement> settlements)
    {
        int cleared = 0;
        foreach (RuralSettlement settlement in settlements)
        {
            if (settlement == null || settlement.naming_version >= Version || settlement.named_by_hand) continue;
            settlement.naming_version = Version;
            if (string.IsNullOrEmpty(settlement.name) || !OldNames().Contains(settlement.name)) continue;
            settlement.name = "";
            cleared++;
        }
        if (cleared > 0) NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][村镇命名] {cleared} 处旧名改按文化重起");
    }

    #endregion
}
