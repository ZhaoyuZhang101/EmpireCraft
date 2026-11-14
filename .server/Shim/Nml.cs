// 加载器里核心代码用得上的三样：取文字(LM)、写日志(LogService)、模组设置(ModConfig)。
//# hand T:NeoModLoader.api.ModConfig
//# hand T:NeoModLoader.api.ModConfigItem
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace NeoModLoader.General
{
    // 文字表：服务器启动时读模组的 Locales/<语言>.json 和各处的 csv(同游戏里一样的键)
    public static class LM
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Tables = new();
        public static string Language = "cz";

        private static Dictionary<string, string> Table(string language)
        {
            if (!Tables.TryGetValue(language, out Dictionary<string, string> table)) Tables[language] = table = new Dictionary<string, string>();
            return table;
        }

        public static int Count(string language) => Tables.TryGetValue(language, out Dictionary<string, string> table) ? table.Count : 0;

        public static void LoadJson(string language, string path)
        {
            if (!File.Exists(path)) return;
            Dictionary<string, string> loaded;
            try
            {
                loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[error] 文字表读不出来 " + path + ": " + exception.Message);
                return;
            }
            if (loaded == null) return;
            Dictionary<string, string> table = Table(language);
            foreach (KeyValuePair<string, string> pair in loaded) table[pair.Key] = pair.Value;
        }

        public static string Get(string key)
        {
            if (key == null) return "";
            if (Tables.TryGetValue(Language, out Dictionary<string, string> table) && table.TryGetValue(key, out string text)) return text;
            if (Tables.TryGetValue("en", out table) && table.TryGetValue(key, out text)) return text;
            return key;
        }

        public static bool Has(string key, string lang = null) =>
            key != null && Tables.TryGetValue(lang ?? Language, out Dictionary<string, string> table) && table.ContainsKey(key);

        // 逗号分隔的文字表：头一行是 key 和各语言的列名，往下一行一个键
        public static void LoadLocales(string pFilePath, char pSep = ',')
        {
            if (pFilePath == null || !File.Exists(pFilePath)) return;
            string[] header = null;
            foreach (string line in File.ReadLines(pFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                List<string> cells = SplitCsv(line, pSep);
                if (header == null)
                {
                    header = cells.Select(cell => cell.Trim().Trim('﻿')).ToArray();
                    continue;
                }
                if (cells.Count == 0 || cells[0].Length == 0) continue;
                for (int i = 1; i < header.Length && i < cells.Count; i++)
                    if (header[i].Length > 0) Table(header[i])[cells[0]] = cells[i];
            }
        }

        private static List<string> SplitCsv(string line, char separator)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"' && cell.Length == 0) quoted = true;
                else if (c == separator)
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        public static void AddToCurrentLocale(string key, string value)
        {
            if (key != null) Table(Language)[key] = value;
        }

        public static void ApplyLocale(bool pUpdateTexts = true)
        {
        }
    }
}

namespace NeoModLoader.services
{
    public static class LogService
    {
        // 警告太多时可以关掉(开服时的 --quiet)
        public static bool Quiet;

        public static void LogInfo(string message)
        {
            if (!Quiet) Console.WriteLine(message);
        }

        public static void LogWarning(string message)
        {
            if (!Quiet) Console.WriteLine("[warn] " + message);
        }

        public static void LogError(string message) => Console.Error.WriteLine("[error] " + message);
    }
}

namespace NeoModLoader.api
{
    // 模组设置的一项(同 default_config.json 里的一条)
    public class ModConfigItem
    {
        public int Type { get; set; }
        public string Id { get; set; }
        public string IconPath { get; set; }
        public bool BoolVal { get; set; }
        public string TextVal { get; set; }
        public float FloatVal { get; set; }
        public float MaxFloatVal { get; set; } = 1f;
        public float MinFloatVal { get; set; }
        public int IntVal { get; set; }
        public int MaxIntVal { get; set; } = 1;
        public int MinIntVal { get; set; }
        public string Callback { get; set; }
    }

    // 模组设置：一份 json(组 → 各项)。每项带一个回调"类名:方法名"，ApplyCallbacks 把眼下的值逐项交给回调，
    // 模组的各个开关就是这样落到它自己的静态字段上的
    public class ModConfig
    {
        private readonly Dictionary<string, Dictionary<string, ModConfigItem>> _groups = new();
        private readonly string _path;

        public ModConfig()
        {
        }

        public ModConfig(string path, bool pIsPersistent = false)
        {
            _path = path;
            if (path == null || !File.Exists(path)) return;
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, List<ModConfigItem>>>(File.ReadAllText(path));
            if (loaded == null) return;
            foreach (KeyValuePair<string, List<ModConfigItem>> group in loaded)
            {
                var items = new Dictionary<string, ModConfigItem>();
                foreach (ModConfigItem item in group.Value ?? new List<ModConfigItem>())
                    if (item?.Id != null) items[item.Id] = item;
                _groups[group.Key] = items;
            }
        }

        public Dictionary<string, ModConfigItem> this[string pGroupId]
        {
            get
            {
                if (!_groups.TryGetValue(pGroupId, out Dictionary<string, ModConfigItem> items)) _groups[pGroupId] = items = new Dictionary<string, ModConfigItem>();
                return items;
            }
            set => _groups[pGroupId] = value;
        }

        // 默认设置里有、这份里没有的项补进来
        public void MergeWith(ModConfig pDefaultConfig)
        {
            if (pDefaultConfig == null) return;
            foreach (KeyValuePair<string, Dictionary<string, ModConfigItem>> group in pDefaultConfig._groups)
            {
                Dictionary<string, ModConfigItem> mine = this[group.Key];
                foreach (KeyValuePair<string, ModConfigItem> item in group.Value)
                    if (!mine.ContainsKey(item.Key)) mine[item.Key] = item.Value;
            }
        }

        public void Save(string path = null)
        {
            path ??= _path;
            if (path == null) return;
            var plain = _groups.ToDictionary(group => group.Key, group => group.Value.Values.ToList());
            File.WriteAllText(path, JsonConvert.SerializeObject(plain, Formatting.Indented));
        }

        // 各项(管理员在手机上改设置、控制台列设置用)
        public IEnumerable<ModConfigItem> Items() => _groups.Values.SelectMany(group => group.Values);
        public ModConfigItem Find(string id) => Items().FirstOrDefault(item => item.Id == id);

        // 改一项：按它的种类认值，交给回调，存盘。认不出这一项回假
        public bool Set(string id, string value)
        {
            ModConfigItem item = Find(id);
            if (item == null) return false;
            value = (value ?? "").Trim();
            if (item.Type == 0) item.BoolVal = value is "true" or "1" or "on" or "yes";
            else if (item.Type == 1) item.IntVal = int.TryParse(value, out int number) ? number : item.IntVal;
            else if (item.Type == 3) item.FloatVal = float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float real) ? real : item.FloatVal;
            else item.TextVal = value;
            Apply(new[] { item });
            Save();
            return true;
        }

        public void ApplyCallbacks() => Apply(Items().ToList());

        private static void Apply(IEnumerable<ModConfigItem> items)
        {
            foreach (ModConfigItem item in items)
            {
                if (string.IsNullOrEmpty(item.Callback)) continue;
                try
                {
                    string[] parts = item.Callback.Split(':');
                    if (parts.Length != 2) continue;
                    MethodInfo method = FindType(parts[0])?.GetMethod(parts[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    ParameterInfo[] parameters = method?.GetParameters();
                    if (method == null || parameters.Length != 1) continue;
                    Type wanted = parameters[0].ParameterType;
                    object value = wanted == typeof(bool) ? item.BoolVal : wanted == typeof(int) ? item.IntVal : wanted == typeof(float) ? item.FloatVal : item.TextVal;
                    method.Invoke(null, new[] { value });
                }
                catch (Exception exception)
                {
                    services.LogService.LogWarning("[设置] " + item.Id + " 没应用上: " + (exception.InnerException ?? exception).Message);
                }
            }
        }

        private static readonly Dictionary<string, Type> Found = new();

        private static Type FindType(string name)
        {
            if (Found.TryGetValue(name, out Type type)) return type;
            Assembly assembly = typeof(ModConfig).Assembly;
            type = assembly.GetType(name) ?? assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == name);
            Found[name] = type;
            return type;
        }
    }
}
