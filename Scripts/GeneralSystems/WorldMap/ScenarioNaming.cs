using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本编辑器放城、放关隘时按所在地区的文化起名，不用自己想：还没进世界，文化实体没有，直接照 CultureRule.jsonc 的城名规则
// (setting.City 的 groups、rule)读词库(Locales/Cultures/Culture_<文化>/<文化><词库>.csv，一行 key,cz,en,ch)，三种语言一起拼好。
// 关隘用同样的词加"关"。起不出来(文化没配、词库读不到、试几次都重名)就退回调用方原来的"新城 N"，随时能手改。
public static class ScenarioNaming
{
    private const int Attempts = 24;
    private static readonly Dictionary<string, List<string[]>> Cache = new();
    private static readonly Random Dice = new();

    // 城名：三种语言都拼好；taken 判断简体名有没有人用
    public static bool TryCity(string culture, Func<string, bool> taken, out string zh, out string tw, out string en)
    {
        zh = tw = en = null;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (!Compose(culture, out zh, out tw, out en)) return false;
            if (taken == null || !taken(zh)) return true;
        }
        return false;
    }

    // 关隘名：剧本里写成"简体|英文|繁体"(见 WorldMapData.PickParts)
    public static bool TryPass(string culture, Func<string, bool> taken, out string name)
    {
        name = null;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (!Compose(culture, out string zh, out string tw, out string en)) return false;
            string candidate = $"{zh}关|{en} Pass|{tw}關";
            if (taken != null && taken(candidate)) continue;
            name = candidate;
            return true;
        }
        return false;
    }

    private static bool Compose(string culture, out string zh, out string tw, out string en)
    {
        zh = tw = en = "";
        if (string.IsNullOrEmpty(culture) || !OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)) return false;
        var rule = setting?.City;
        if (rule?.groups == null || rule.rule == null) return false;
        for (int i = 0; i < rule.rule.Length; i++)
        {
            OnomasticsType token = rule.rule[i];
            if (token == OnomasticsType.space)
            {
                en += " ";
                continue;
            }
            if (token == OnomasticsType.coin_flip || !rule.groups.TryGetValue(token.ToString(), out string group)) continue;
            // 后面跟着 coin_flip 的这一组只有一半机会用上(同原版命名模板)
            if (i + 1 < rule.rule.Length && rule.rule[i + 1] == OnomasticsType.coin_flip && Dice.NextDouble() < 0.5d) continue;
            List<string[]> words = Words(culture, group);
            if (words.Count == 0) continue;
            string[] word = words[Dice.Next(words.Count)];
            zh += word[0];
            en += word[1];
            tw += word[2];
        }
        return zh.Length > 0;
    }

    // 词库名可写"别的文化:词库名"；每行 [简体, 英文, 繁体]，缺的列退回简体
    private static List<string[]> Words(string culture, string group)
    {
        string owner = culture, name = (group ?? "").Trim();
        int split = name.IndexOf(':');
        if (split > 0)
        {
            owner = name.Substring(0, split).Trim();
            name = name.Substring(split + 1).Trim();
        }
        string path = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", $"Culture_{owner}", owner + name + ".csv");
        if (Cache.TryGetValue(path, out List<string[]> cached)) return cached;
        var words = new List<string[]>();
        try
        {
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path).Skip(1))
                {
                    string[] cells = line.Split(',');
                    if (cells.Length < 2 || string.IsNullOrWhiteSpace(cells[1])) continue;
                    string simplified = Clean(cells[1]);
                    string english = cells.Length > 2 && !string.IsNullOrWhiteSpace(cells[2]) ? Clean(cells[2]) : simplified;
                    string traditional = cells.Length > 3 && !string.IsNullOrWhiteSpace(cells[3]) ? Clean(cells[3]) : simplified;
                    words.Add(new[] { simplified, english, traditional });
                }
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][剧本编辑器] 读城名词库失败 {path}: {exception.Message}");
        }
        Cache[path] = words;
        return words;
    }

    // 词库约定："_"开头换成空格，单个"_"是空串
    private static string Clean(string cell)
    {
        string word = cell.Trim();
        return word == "_" ? "" : word.StartsWith("_") ? " " + word.Substring(1) : word;
    }
}
