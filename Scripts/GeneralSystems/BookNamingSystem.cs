using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 普通书籍的文化风格书名。词库都在 Locales/Cultures 里(key,cz,en,ch 的 CSV，增删直接改文件)：
//   Culture_<文化>/<文化>Books<类型>.csv          名著式的固定书名(史记、源氏物语……)，全世界每个只用一次；
//   Culture_<文化>/<文化>BookTemplates<类型>.csv  书名模板；
//   Culture_<文化>/<文化>BookWords<类型>.csv      模板里 $word$ 用的词；
//   Culture_<文化>/<文化>BookTopics<类型>.csv     模板里 $topic$ 用的主题词(艺文、食货、狐、鬼……)。
// 直接读文件里当前语言那一列(cz/en/ch，别的语言读 en)，所以往文件里加一行就生效，key 只要在文件里不重复即可。
// 文化没配就用 Books/ 下同名去掉文化前缀的通用文件(Books<类型>.csv 等)。
// 固定书名用完(或一半概率)就用模板组合；再撞名就加"卷二、卷三"。
// 占位符：$word$ 词库  $author$ 作者  $city$ 城市  $kingdom$ 国家  $clan$ 氏族
//         $era$ 本文化的科技时代  $dynasty$ 朝代(帝国名)  $year_name$ 当朝年号  $emperor$ 当朝皇帝
//         $past_emperor$ 某位先帝(有庙号用庙号)  $past_year_name$ 那位先帝的年号
//         $topic$ 主题词  $dynasty_short$ 去掉"朝/国"的朝代名(宋朝 → 宋，用于"宋书")
// 中文下人名、国名里的空格会去掉(赵 平程 → 赵平程)。
// 模板里的占位符取不到值(没有帝国、没有年号、没有先帝)就换一个模板。含占位符的固定书名也当模板用。
public static class BookNamingSystem
{
    private static readonly Dictionary<string, List<string>> PoolCache = new(StringComparer.Ordinal);
    private static HashSet<string> _usedNames;
    private static object _usedWorld;

    public static void Rename(Book book, Actor author)
    {
        if (book?.data == null || author == null || World.world == null) return;
        string culture = TechnologySystem.GetCultureOf(author);
        string tag = Camel(book.data.book_type);
        if (string.IsNullOrEmpty(tag)) return;
        List<string> classics = Pool(culture, "Books" + tag);
        List<string> templates = Pool(culture, "BookTemplates" + tag);
        List<string> words = Pool(culture, "BookWords" + tag);
        List<string> topics = Pool(culture, "BookTopics" + tag);
        HashSet<string> used = UsedNames();

        var fixedTitles = classics.Where(title => !title.Contains('$') && !used.Contains(title)).ToList();
        var patterns = templates.Concat(classics.Where(title => title.Contains('$'))).ToList();
        string name = null;
        if (fixedTitles.Count > 0 && (patterns.Count == 0 || UnityEngine.Random.value < 0.5f))
            name = fixedTitles.GetRandom();
        else if (patterns.Count > 0)
        {
            // 多试几次，尽量组合出没用过的书名
            for (int attempt = 0; attempt < 8 && (name == null || used.Contains(name)); attempt++)
                name = Fill(patterns.GetRandom(), words, topics, author, culture);
        }
        if (string.IsNullOrWhiteSpace(name)) return; // 这个文化没有词库：保留原版书名
        name = EnsureUnique(name, used);
        book.setName(name);
        used.Add(name);
    }

    // 填模板；有占位符取不到值时返回 null(调用方换一个模板再试)
    private static string Fill(string pattern, List<string> words, List<string> topics, Actor author, string culture)
    {
        Layer.Empire empire = author.kingdom?.GetEmpire();
        EmpireCraftHistory past = null;
        if (pattern.Contains("$past_"))
        {
            List<EmpireCraftHistory> history = empire?.data?.history?.Where(record => record != null &&
                !string.IsNullOrWhiteSpace(record.emperor)).ToList();
            if (history == null || history.Count == 0) return null;
            past = history.GetRandom();
        }
        bool chinese = IsChinese();
        string Tidy(string text) => chinese && text != null ? text.Replace(" ", "") : text;
        string dynasty = Tidy(empire?.GetEmpireName());
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$word$"] = words.Count > 0 ? words.GetRandom() : null,
            ["$topic$"] = topics.Count > 0 ? topics.GetRandom() : null,
            ["$author$"] = Tidy(author.getName()),
            ["$city$"] = Tidy(author.city?.name ?? author.kingdom?.name),
            ["$kingdom$"] = Tidy(author.kingdom?.name ?? author.city?.name),
            ["$clan$"] = Tidy(author.clan?.name ?? author.getName()),
            ["$dynasty_short$"] = ShortDynasty(dynasty ?? Tidy(author.kingdom?.name), chinese),
            ["$era$"] = TechnologySystem.IsEnabled && CultureService.IsValidCulture(culture)
                ? TechnologySystem.GetEraName(culture) : null,
            ["$dynasty$"] = dynasty,
            ["$year_name$"] = empire != null && empire.HasYearName() ? empire.data.year_name : null,
            ["$emperor$"] = Tidy(empire?.Emperor?.getName()),
            ["$past_emperor$"] = Tidy(PastEmperorName(past)),
            ["$past_year_name$"] = string.IsNullOrWhiteSpace(past?.year_name) ? null : past.year_name
        };
        string result = pattern;
        foreach (KeyValuePair<string, string> pair in values)
        {
            if (!result.Contains(pair.Key)) continue;
            if (string.IsNullOrWhiteSpace(pair.Value)) return null;
            result = result.Replace(pair.Key, pair.Value);
        }
        return result.Contains('$') ? null : result.Trim();
    }

    // 历史保存的是庙号前缀/后缀的本地化键，不能把前缀键直接当作先帝姓名。
    private static string PastEmperorName(EmpireCraftHistory history)
    {
        if (history == null) return null;
        string prefix = LocalizedTemplePart(history.miaohao_name);
        string suffix = LocalizedTemplePart(history.miaohao_suffix);
        if (!string.IsNullOrWhiteSpace(prefix) &&
            (!string.IsNullOrWhiteSpace(suffix) ||
             history.miaohao_name.IndexOf("miaohaoprefixes_", StringComparison.Ordinal) < 0))
            return IsChinese() ? prefix + suffix : OverallHelperFunc.JoinNameParts(prefix, suffix);
        return history.emperor;
    }

    private static string LocalizedTemplePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = LM.Get(value);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text == value && (value.Contains("miaohaoprefixes_") || value.Contains("miaohaosuffixes_")))
            return null;
        return text;
    }

    private static readonly Regex LegacyTemplePrefix = new(@"(?:first|normal)_miaohaoprefixes_[a-z]+",
        RegexOptions.CultureInvariant);

    private static string RepairLegacyTitle(string title, IEnumerable<EmpireCraftHistory> history)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        return LegacyTemplePrefix.Replace(title, match =>
        {
            string prefix = LocalizedTemplePart(match.Value);
            if (prefix == null) return match.Value;
            // 旧书只保存了前缀。记录能唯一确定完整庙号时补齐后缀，否则只翻译已知部分。
            List<string> names = (history ?? Enumerable.Empty<EmpireCraftHistory>())
                .Where(record => record?.miaohao_name == match.Value &&
                                 LocalizedTemplePart(record.miaohao_suffix) != null)
                .Select(PastEmperorName).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal).ToList();
            return names.Count == 1 ? names[0] : prefix;
        });
    }

    // 帝国史书加载完成后修复已生成的书，保留原有题材、作者与卷号。
    public static int RepairLegacyNames()
    {
        if (World.world?.books == null) return 0;
        int repaired = 0;
        foreach (Book book in World.world.books)
        {
            if (book?.data == null || book.isRekt() || string.IsNullOrEmpty(book.name) ||
                !LegacyTemplePrefix.IsMatch(book.name)) continue;
            Kingdom kingdom = World.world.kingdoms.get(book.data.author_kingdom_id);
            string title = RepairLegacyTitle(book.name, kingdom?.GetEmpire()?.data?.history);
            if (title == book.name) continue;
            book.setName(title);
            repaired++;
        }
        if (repaired > 0)
        {
            _usedWorld = null;
            _usedNames = null;
        }
        return repaired;
    }

    private static readonly string[] DynastySuffixes = { "王朝", "帝国", "帝國", "朝", "国", "國" };

    // 宋朝 → 宋，北庸朝 → 北庸；去掉后缀就没字了则保留原名
    private static string ShortDynasty(string name, bool chinese)
    {
        if (string.IsNullOrWhiteSpace(name) || !chinese) return name;
        foreach (string suffix in DynastySuffixes)
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name.Substring(0, name.Length - suffix.Length);
        return name;
    }

    private static string Language() => LocalizedTextManager.instance?.language ?? "en";
    private static bool IsChinese() => Language() is "cz" or "ch";

    private static string EnsureUnique(string name, HashSet<string> used)
    {
        if (!used.Contains(name)) return name;
        for (int volume = 2; volume < 100; volume++)
        {
            string candidate = string.Format(LM.Get("book_name_volume"), name, volume);
            if (!used.Contains(candidate)) return candidate;
        }
        return name;
    }

    // 世界里已有的书名(换世界/读档后重建一次，之后随写书增量更新)
    private static HashSet<string> UsedNames()
    {
        if (_usedNames != null && ReferenceEquals(_usedWorld, World.world)) return _usedNames;
        _usedWorld = World.world;
        _usedNames = new HashSet<string>(World.world.books.Select(book => book.name)
            .Where(name => !string.IsNullOrEmpty(name)), StringComparer.Ordinal);
        return _usedNames;
    }

    // 取一个世界里没用过的书名(撞名加"卷二、卷三")并登记
    public static string ClaimUnique(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || World.world == null) return name;
        HashSet<string> used = UsedNames();
        name = EnsureUnique(name, used);
        used.Add(name);
        return name;
    }

    public static bool IsUsed(string name) => !string.IsNullOrEmpty(name) && World.world != null && UsedNames().Contains(name);

    // 带 key 的词库条目：文化自己的 Culture_<文化>/<文化><name>.csv 与通用的 Books/<name>.csv 合并
    // (理念书库按 key 前缀区分理念，见 SpeechFreedomSystem)
    public static List<(string key, string text)> Entries(string culture, string name)
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        var result = new List<(string key, string text)>();
        if (CultureService.IsValidCulture(culture))
            result.AddRange(ReadEntries(Path.Combine(root, $"Culture_{culture}", culture + name + ".csv")));
        result.AddRange(ReadEntries(Path.Combine(root, "Books", name + ".csv")));
        return result;
    }

    private static readonly Dictionary<string, List<(string key, string text)>> EntryCache = new(StringComparer.Ordinal);

    private static List<(string key, string text)> ReadEntries(string path)
    {
        string language = Language();
        string cacheKey = language + "|" + path;
        if (EntryCache.TryGetValue(cacheKey, out var cached)) return cached;
        var entries = new List<(string key, string text)>();
        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path);
            List<string> header = lines.Length > 0 ? SplitCsv(lines[0]) : new List<string>();
            int[] columns = new[] { header.IndexOf(language), header.IndexOf("en"), header.IndexOf("cz") }
                .Where(index => index > 0).Distinct().ToArray();
            for (int i = 1; i < lines.Length && columns.Length > 0; i++)
            {
                List<string> cells = SplitCsv(lines[i]);
                if (cells.Count == 0 || string.IsNullOrWhiteSpace(cells[0])) continue;
                string text = columns.Where(index => index < cells.Count).Select(index => cells[index].Trim())
                    .FirstOrDefault(value => value.Length > 0);
                if (!string.IsNullOrEmpty(text)) entries.Add((cells[0].Trim(), text));
            }
        }
        EntryCache[cacheKey] = entries;
        return entries;
    }

    public static void MarkUsed(string name)
    {
        if (!string.IsNullOrEmpty(name)) UsedNames().Add(name);
    }

    // 按文化读词库：Culture_<文化>/<文化><name>.csv，没有就读 Books/<name>.csv
    public static List<string> Pool(string culture, string name)
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        if (CultureService.IsValidCulture(culture))
        {
            List<string> own = Read(Path.Combine(root, $"Culture_{culture}", culture + name + ".csv"));
            if (own.Count > 0) return own;
        }
        return Read(Path.Combine(root, "Books", name + ".csv"));
    }

    // 直接读 CSV 当前语言那一列(不经过本地化表，所以文化没加载、key 撞了都不影响)
    private static List<string> Read(string path)
    {
        string language = Language();
        string cacheKey = language + "|" + path;
        if (PoolCache.TryGetValue(cacheKey, out List<string> cached)) return cached;
        var names = new List<string>();
        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path);
            List<string> header = lines.Length > 0 ? SplitCsv(lines[0]) : new List<string>();
            // 当前语言空着就退回 English，再退回简体，方便投稿的人只写一种语言
            int[] columns = new[] { header.IndexOf(language), header.IndexOf("en"), header.IndexOf("cz") }
                .Where(index => index > 0).Distinct().ToArray();
            for (int i = 1; i < lines.Length && columns.Length > 0; i++)
            {
                List<string> cells = SplitCsv(lines[i]);
                if (cells.Count == 0 || string.IsNullOrWhiteSpace(cells[0])) continue;
                string text = columns.Where(index => index < cells.Count).Select(index => cells[index].Trim())
                    .FirstOrDefault(value => value.Length > 0);
                if (!string.IsNullOrEmpty(text)) names.Add(text);
            }
        }
        PoolCache[cacheKey] = names;
        return names;
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new global::System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }

    private static string Camel(string id) => string.IsNullOrEmpty(id)
        ? ""
        : string.Concat(id.Split('_').Where(part => part.Length > 0)
            .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
}
