using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EmpireCraft.Scripts.Data;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无人区命名(用户 2026-10-29："依据最近的文化进行命名比如撒哈拉 大沙漠，太行 山脉什么的")：
//   · 连片的山脉、沙漠、冻原、雨林、大泽(见 WildernessSystem)按离区域最近的城的主流文化起名，名字 = 地名 + 类型后缀
//     (太行 + 山脉、撒哈拉 + 大沙漠、Taihang + Mountains)；
//   · 文化配置(CultureRule.jsonc 的 setting.Wilderness，见 WildernessSetting)给各类型绑地名词库与后缀，可写"别的文化:词库名"；
//     没配的用 Locales/Cultures/Wilderness/ 的通用词库(世界各地的真实地名)；
//   · 词库同村镇(见 SettlementNaming)：CSV(key,cz,en,ch)，直接读当前语言那一列(空着退回英文、简体)；
//     后缀英文"_"开头表示前面加空格、单写"_"表示不加；后缀里写 {0} 的代入地名(Great {0} Desert、{0}的旷野)；
//     地名末尾写"!"表示整名、不接后缀(瀚海!、Alps!)；地名已以本类型的某个后缀结尾的也不再接(燕山 + 山 → 燕山)；
//   · 很大的区域(≥ GreatZones 个区块)用大字头的后缀(撒哈拉大沙漠、云梦大泽、Great Sahara Desert)，本文化没配照常用寻常后缀；
//   · 不重名：本世界已用的名字避开(同一地名换个后缀的写法也算用过：太行山脉用了，不再起太行山)，换一个词；
//     词用完了加方位字头(北、南、东、西、大；字头已在名字里的不加)，再不够加序号(太行山脉二、Taihang Mountains II)；
//   · 稳定：同样的类型、文化、seed、已用名字起出同一个名字(按 seed 洗牌、取后缀，不用随机数)。
public static class WildernessNaming
{
    // 很大的区域：两座满城的地盘(城最多 50 个区块，见 ModClass.CITY_MAX_ZONES 的默认值)
    public const int GreatZones = 100;
    private const string Folder = "Wilderness";

    // 词库里的一个地名：whole 为整名(末尾写了"!")，不接后缀
    private readonly struct Word
    {
        public readonly string text;
        public readonly bool whole;

        public Word(string text, bool whole)
        {
            this.text = text;
            this.whole = whole;
        }
    }

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

    // 读当前语言那一列(空着退回英文、简体)；"_"开头换成空格，单个"_"是空串(同村镇词库)
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
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][无人区命名] 读词库失败 {path}: {exception.Message}");
        }
        Cache[path] = words;
        return words;
    }

    // 文化配的词库(可写"别的文化:词库名")；没配或读不到是空表
    private static List<string> Own(string culture, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return new List<string>();
        string owner = culture ?? "", group = configured.Trim();
        int split = group.IndexOf(':');
        if (split > 0)
        {
            owner = group.Substring(0, split).Trim();
            group = group.Substring(split + 1).Trim();
        }
        if (owner.Length == 0 || group.Length == 0) return new List<string>();
        return Read(Path.Combine(Root, $"Culture_{owner}", owner + group + ".csv"));
    }

    // 通用词库 Locales/Cultures/Wilderness/<名>.csv
    private static List<string> General(string name) => Read(Path.Combine(Root, Folder, name + ".csv"));

    private static WildernessSetting Config(string culture) =>
        !string.IsNullOrEmpty(culture) && OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting?.Wilderness
            : null;

    private static string Configured(Dictionary<string, string> map, string key) =>
        map?.FirstOrDefault(pair => string.Equals(pair.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string KindKey(WildKind kind) => kind switch
    {
        WildKind.Mountain => "mountain",
        WildKind.Desert => "desert",
        WildKind.Tundra => "tundra",
        WildKind.Jungle => "jungle",
        WildKind.Swamp => "swamp",
        _ => null
    };

    // 通用词库的文件名：Wild + 类型首字母大写(WildMountain、WildMountainSuffix、WildMountainGreat)
    private static string Stem(string key) => "Wild" + char.ToUpperInvariant(key[0]) + key.Substring(1);

    private static List<Word> Words(string culture, WildernessSetting config, string key)
    {
        List<string> raw = Own(culture, Configured(config?.words, key));
        if (raw.Count == 0) raw = General(Stem(key));
        var words = new List<Word>(raw.Count);
        foreach (string text in raw)
        {
            string value = text.Trim();
            bool whole = value.EndsWith("!", StringComparison.Ordinal);
            if (whole) value = value.TrimEnd('!').TrimEnd();
            if (value.Length > 0) words.Add(new Word(value, whole));
        }
        return words;
    }

    // 后缀：寻常的用 suffix(默认 Wild<类型>Suffix)；大区域用 great_suffix，没配的照本文化的 suffix，
    // 本文化两样都没配用通用的 Wild<类型>Great(没有这个文件的类型照寻常后缀，如山脉)
    private static List<string> Suffixes(string culture, WildernessSetting config, string key, bool great)
    {
        List<string> normal = Own(culture, Configured(config?.suffix, key));
        if (great)
        {
            List<string> own = Own(culture, Configured(config?.great_suffix, key));
            if (own.Count > 0) return own;
            if (normal.Count > 0) return normal;
            List<string> general = General(Stem(key) + "Great");
            if (general.Count > 0) return general;
        }
        return normal.Count > 0 ? normal : General(Stem(key) + "Suffix");
    }

    // 本类型各后缀去掉 {0} 及其前面的部分剩下的尾巴(山脉、山、大沙漠、Desert、的旷野)：地名已以它结尾的不再接后缀
    private static List<string> Endings(IEnumerable<string> suffixes)
    {
        var endings = new List<string>();
        foreach (string suffix in suffixes)
        {
            string tail = suffix ?? "";
            int slot = tail.IndexOf("{0}", StringComparison.Ordinal);
            if (slot >= 0) tail = tail.Substring(slot + 3);
            tail = tail.Trim();
            if (tail.Length > 0 && !endings.Contains(tail)) endings.Add(tail);
        }
        return endings;
    }

    #endregion

    // 全名(本地化好的，如"撒哈拉大沙漠""太行山脉""Sahara Desert")；seed 稳定(同一区域每次起同一个名)；
    // used：本世界已用的全名，起名避开；zones：区域多大(≥ GreatZones 用大字头的后缀)
    public static string Name(WildKind kind, string culture, long seed, ICollection<string> used, int zones)
    {
        string key = KindKey(kind);
        if (key == null) return "";
        WildernessSetting config = Config(culture);
        List<Word> words = Words(culture, config, key);
        // 词库都读不到(文件缺了)：拿类型名当整名，往下靠方位字头、序号区分
        if (words.Count == 0) words.Add(new Word(KindName(kind), true));
        List<string> normal = Suffixes(culture, config, key, false), great = Suffixes(culture, config, key, true);
        List<string> pool = zones >= GreatZones ? great : normal;
        List<string> endings = Endings(normal.Concat(great));
        bool chinese = Language() != "en";
        int[] order = Shuffle(words.Count, seed ^ ((long)kind * 0x632BE59BD9B4E019L));

        // 第 index 个词的名字：后缀按 seed 取，同一区域每次取同一个
        string Chosen(int index) => Compose(words[index], pool.Count == 0 ? "" : pool[(int)(Mix(seed, -1L - index) % (ulong)pool.Count)], endings);
        // 这个词换各种后缀的写法：任一写法用过就算这个词用过
        List<string> Variants(int index) =>
            normal.Concat(great).Select(suffix => Compose(words[index], suffix, endings)).Append(Chosen(index)).Distinct().ToList();
        bool Free(IEnumerable<string> names) => used == null || names.All(name => !used.Contains(name));

        // 一、地名 + 后缀
        foreach (int index in order)
            if (Free(Variants(index))) return Chosen(index);

        // 二、方位字头(北、南、东、西、大)：字头已在名字里的不加(北海冰原不起"北北海冰原"，云梦大泽不起"大云梦大泽")
        List<string> directions = Own(culture, config?.directions);
        if (directions.Count == 0) directions = General("WildDirections");
        foreach (string direction in directions)
        {
            string mark = direction.Replace("{0}", "").Trim();
            if (mark.Length == 0) continue;
            foreach (int index in order)
            {
                string chosen = Chosen(index);
                if (chosen.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (Free(Variants(index).Select(name => Direct(direction, name)))) return Direct(direction, chosen);
            }
        }

        // 三、序号(太行山脉二、Taihang Mountains II)：已用的名字有限，总能排到空着的号
        int limit = (used?.Count ?? 0) + 2;
        for (int n = 2; n <= limit; n++)
            foreach (int index in order)
                if (Free(Variants(index).Select(name => Numbered(name, n, chinese)))) return Numbered(Chosen(index), n, chinese);
        return Numbered(Chosen(order[0]), limit + 1, chinese);
    }

    // 类型名(山脉、沙漠、冻原、雨林、大泽)，悬停提示用
    public static string KindName(WildKind kind)
    {
        string key = KindKey(kind);
        return key == null ? "" : LM.Get("wild_kind_" + key);
    }

    // 地名 + 后缀：整名、已以本类型后缀结尾的不接；后缀写 {0} 的代入地名，{0} 前面的字地名已打头的不重复(Great Victoria → Great Victoria Desert)
    private static string Compose(Word word, string suffix, List<string> endings)
    {
        string text = word.text;
        if (word.whole || string.IsNullOrEmpty(suffix) ||
            endings.Any(ending => text.EndsWith(ending, StringComparison.OrdinalIgnoreCase))) return text;
        int slot = suffix.IndexOf("{0}", StringComparison.Ordinal);
        if (slot < 0) return text + suffix;
        string head = suffix.Substring(0, slot), tail = suffix.Substring(slot + 3);
        if (head.Trim().Length > 0 && text.StartsWith(head.Trim(), StringComparison.OrdinalIgnoreCase)) head = "";
        return head + text + tail;
    }

    // 方位字头：写 {0} 的代入名字(北{0}、North {0})，没写的直接加在前面
    private static string Direct(string direction, string name) =>
        direction.Contains("{0}") ? direction.Replace("{0}", name) : direction + name;

    private static string Numbered(string name, int n, bool chinese) => chinese ? name + ChineseNumber(n) : name + " " + Roman(n);

    private static string ChineseNumber(int n)
    {
        string[] digits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        if (n < 0 || n >= 100) return n.ToString();
        if (n < 10) return digits[n];
        return (n / 10 == 1 ? "" : digits[n / 10]) + "十" + (n % 10 == 0 ? "" : digits[n % 10]);
    }

    private static string Roman(int n)
    {
        if (n <= 0 || n >= 4000) return n.ToString();
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] numerals = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var text = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
            while (n >= values[i])
            {
                text.Append(numerals[i]);
                n -= values[i];
            }
        return text.ToString();
    }

    // 确定性的洗牌：同一个 seed 每次排出同一个次序
    private static int[] Shuffle(int count, long seed)
    {
        int[] order = Enumerable.Range(0, count).ToArray();
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(Mix(seed, i) % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    // splitmix64
    private static ulong Mix(long seed, long salt)
    {
        unchecked
        {
            ulong x = (ulong)seed + 0x9E3779B97F4A7C15UL * (ulong)(salt + 1);
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}

// 文化配置里的无人区命名(CultureRule.jsonc 的 setting.Wilderness，见 WildernessNaming)：
// 类型(mountain 山脉、desert 沙漠、tundra 冻原、jungle 雨林、swamp 大泽) → 词库名，词库是本文件夹 <文化><词库名>.csv，
// 可写"别的文化:词库名"；没配的用 Locales/Cultures/Wilderness/ 的通用词库
public class WildernessSetting
{
    // 地名(太行、撒哈拉；末尾写"!"的是整名，不接后缀)；默认 Wilderness/Wild<类型>.csv(如 WildMountain)
    public Dictionary<string, string> words;
    // 类型后缀(山脉、沙漠，可写 {0} 代入地名)；默认 Wilderness/Wild<类型>Suffix.csv
    public Dictionary<string, string> suffix;
    // 大区域的后缀(大沙漠、大泽)；没配的照本文化的 suffix，两样都没配用 Wilderness/Wild<类型>Great.csv
    public Dictionary<string, string> great_suffix;
    // 方位字头(北{0}、南{0}……)，地名用完了才加；默认 Wilderness/WildDirections.csv
    public string directions = "";
}
