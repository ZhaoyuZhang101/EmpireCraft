using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc;
public static class OnomasticsHelper
{
    /// <summary>
    /// 通用载入命名模板并分组
    /// </summary>
    /// <param name="data">要操作的 OnomasticsData 即命名模板数据 </param>
    /// <param name="cultureName">文化名称， 可为null，但一旦输入需要创建相应的文件至Locales目录下
    /// 比如"模组根目录/Locales/Cultures/"Culture_文化名称""
    /// 同时也要创建对应的文字集文件需为.csv格式
    /// 比如我的文化是华夏，我的名字集合是姓氏集，那就目录就应该是这样子的"模组根目录/Locales/Culture/Culture_华夏/姓氏集.csv"</param>
    /// <param name="template">分组顺序，用 OnomasticsType 枚举定义</param>
    /// <param name="groups">
    ///     每个元组里：
    ///       Item1 = group 名称（"group_1","group_2"…）最多只能到group_10，
    ///       Item2 = 文化组名称（不含 “.csv”；若为 null 则不读 CSV），
    ///       Item3 = 自定义内容，如果无命名集文件，也可以直接输入内容至命名模板（若为 null 则留空）
    /// </param>
    public static void Configure(
        OnomasticsData data,
        string cultureName,
        OnomasticsType[] template,
        params (string groupName, string CharacterSetName, string definedContent)[] groups
    )
    {
        // 1) 清空旧模板／分组
        data.clearTemplateData();
        data.groups.Clear();

        // 2) 设置命名规则模板
        data.setTemplateData(
            OnomasticsTypeExtensions.ToStringList(template)
        );
        // 3) 按每个 group 配置分组
        foreach (var (groupName, CharacterSetName, definedContent) in groups)
        {
            // 3.1 读 CSV（如果有）
            string content = "";
            if (!string.IsNullOrEmpty(CharacterSetName) && !string.IsNullOrEmpty(cultureName))
            {
                string ModPath = ModClass._declare.FolderPath + "/Locales/";
                string culturePath = ModPath + $"Cultures/Culture_{cultureName}/";
                string CharacterSetPath = culturePath + String.Format("{0}{1}.csv", cultureName, CharacterSetName);
                var charaterSetKey = getKeysFromPath(CharacterSetPath);
                content = string.Join(" ",
                    charaterSetKey.ToArray().Select(k => LM.Get(k))
                );
            } else
            {
                if (!string.IsNullOrEmpty(definedContent))
                {
                    content = definedContent;
                } 
            }
            // 3.3 写入这个 group
            data.setGroup(groupName, content);
        }
    }
    // 命名字表按路径缓存：放置种族时新建文化要一次读十来个字表，谥号、年号、宗教名也反复读同一批文件，
    // 每次都从磁盘读会在放置种族那一刻卡一下
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> KeyCache =
        new(StringComparer.OrdinalIgnoreCase);

    // 模组加载时在后台线程预读所有文化字表(只读文件、不碰游戏对象)，第一次放置种族时就不用再读盘
    public static void PreloadCultureFilesAsync()
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (string file in Directory.EnumerateFiles(root, "*.csv", SearchOption.AllDirectories))
                    ReadKeys(Path.GetFullPath(file));
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 预读命名字表失败: {exception.Message}");
            }
        });
    }

    private static List<string> ReadKeys(string fullPath) => KeyCache.GetOrAdd(fullPath, path =>
        File.ReadAllLines(path).Skip(1).Select(line => line.Split(',')[0]).ToList());

    public static List<string> getKeysFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string fullPath = Path.GetFullPath(path);
        if (!KeyCache.ContainsKey(fullPath) && !File.Exists(fullPath))
        {
            LogService.LogWarning("File not found: " + path);
            return null;
        }
        // 返回副本，调用方改动列表不会污染缓存
        return new List<string>(ReadKeys(fullPath));
    }

}
