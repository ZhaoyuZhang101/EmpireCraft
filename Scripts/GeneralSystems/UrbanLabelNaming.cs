using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城内的坊里、官署、店铺与作坊标注：按本城主流文化取名，不把其他文明都叫成华夏的坊、衙、寺。
// 这些只是地图标注，不写存档；村镇与玩家手改的厂区名仍由各自的系统保存。
public static class UrbanLabelNaming
{
    private static readonly Dictionary<string, string[]> Pools = new(StringComparer.OrdinalIgnoreCase);
    private static string _language;

    public static Names For(City city)
    {
        string language = LocalizedTextManager.instance?.language ?? "cz";
        if (language is not ("cz" or "ch" or "en")) language = "en";
        Load(language);
        string culture = CultureService.GetMainCulture(city, initialize: false);
        if (string.IsNullOrEmpty(culture) && city?.kingdom != null)
            culture = CultureService.GetRealmCulture(city.kingdom);
        return new Names(culture ?? "", language, city?.GetCityName() ?? "");
    }

    public sealed class Names
    {
        private readonly string _culture, _family, _language, _city;
        internal Names(string culture, string language, string city)
        {
            _culture = culture;
            _family = string.IsNullOrEmpty(culture) ? "common" : CityStyleSystem.Family(culture);
            _language = language;
            _city = city;
        }

        // 改名、文化同化或切换界面语言时，即使建筑布局没变也要重排标注。
        public string Key => _culture + "\n" + _family + "\n" + _language + "\n" + _city;

        public string Residence(bool modern, long seed) => Name(modern ? "modern_ward" : "ward", seed);

        public string Numbered(string estate, int number) => _language == "en"
            ? estate + " " + number
            : estate + number + (_language == "ch" ? "棟" : "栋");

        public string Building(string icon, bool modern, long seed)
        {
            string kind = icon.Split('_')[0];
            if (kind is not ("civic" or "market" or "industry" or "temple" or "barracks" or "docks" or "palace")) return null;
            bool modernWorks = kind == "industry" && (icon.EndsWith("1") || icon.EndsWith("2"));
            if ((modern || modernWorks) && kind is not ("temple" or "palace")) kind = "modern_" + kind;
            return Name(kind, seed);
        }

        private string Name(string kind, long seed)
        {
            Load(_language);
            // 个别文化可覆盖其建筑家族的词库(例如犹太文化的会堂，不能借用阿拉伯清真寺)。
            if (!Pools.TryGetValue(_culture + "/" + kind, out string[] pool) &&
                !Pools.TryGetValue(_family + "/" + kind, out pool) &&
                !Pools.TryGetValue("common/" + kind, out pool))
                return _city;
            unchecked
            {
                ulong hash = (ulong)seed * 0x9E3779B97F4A7C15UL;
                hash ^= hash >> 29;
                return pool[(int)(hash % (ulong)pool.Length)].Replace("{city}", _city);
            }
        }
    }

    // family,kind,cz,en,ch；同格内以 | 分隔。空语言列依次退回英文、简体。
    private static void Load(string language)
    {
        if (_language == language) return;
        _language = language;
        Pools.Clear();
        string path = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "UrbanLabels", "Labels.csv");
        try
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return;
            string[] header = lines[0].Split(',');
            int[] columns = new[] { Array.IndexOf(header, language), Array.IndexOf(header, "en"), Array.IndexOf(header, "cz") }
                .Where(index => index >= 2).Distinct().ToArray();
            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split(',');
                if (cells.Length < 3 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1])) continue;
                string value = columns.Where(index => index < cells.Length).Select(index => cells[index].Trim())
                    .FirstOrDefault(text => text.Length > 0);
                if (string.IsNullOrEmpty(value)) continue;
                string[] names = value.Split('|').Select(text => text.Trim()).Where(text => text.Length > 0).ToArray();
                if (names.Length > 0) Pools[cells[0].Trim() + "/" + cells[1].Trim()] = names;
            }
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][城内命名] 读词库失败 {path}: {exception.Message}");
        }
    }
}
