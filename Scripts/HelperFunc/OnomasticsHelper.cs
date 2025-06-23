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
    public static List<string> AllCivSpecies = new List<string>() 
    {
        "human", "orc","elf","dwarf","civ_necromancer","civ_alien",
        "civ_druid","civ_bee","civ_beetle","civ_evil_mage","civ_white_mage",
        "civ_bandit","civ_demon","civ_cold_one","civ_angle","civ_snowman",
        "civ_garlic_man","civ_lemon_man","civ_acid_gentleman","civ_crystal_golem",
        "civ_candy_man","civ_liliar","civ_greg","civ_cat","civ_dog","civ_chicken",
        "civ_rabbit","civ_monkey","civ_fox","civ_sheep","civ_cow","civ_armadillo",
        "civ_wolf","civ_bear","civ_rhino","civ_buffalo","civ_hyena","civ_rat",
        "civ_alpaca","civ_capybara","civ_goat","civ_scorpion","civ_crab",
        "civ_penguin","civ_turtle","civ_crocodile","civ_snake","civ_frog","civ_piranha",
    };
    /// <summary>
    /// 通用载入命名模板并分组
    /// </summary>
    /// <param name="data">要操作的 OnomasticsData 即命名模板数据 </param>
    /// <param name="cultureName">文化名称， 可为null，但一旦输入需要创建相应的文件至Locales目录下
    /// 比如"模组根目录/Locales/"文化名称""
    /// 同时也要创建对应的文字集文件需为.csv格式
    /// 比如我的文化是华夏，我的名字集合是姓氏集，那就目录就应该是这样子的"模组根目录/Locales/华夏/姓氏集.csv"</param>
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
    public static List<string> getKeysFromPath(string path)
    {
        if (!File.Exists(path))
        {
            LogService.LogWarning("File not found: " + path);
            return null;
        }
        else
        {
            string[] lines = File.ReadAllLines(path);
            int index = 0;
            List<String> keys = new();
            foreach (string line in lines)
            {
                string[] strings = line.Split(',');
                if (index != 0)
                {
                    keys.Add(strings[0]);
                }
                index++;
            }
            return keys;
        }
    }

    public static void LoadSpeciesCulturePair() 
    {
        string culturesPath = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        if (!Directory.Exists(culturesPath))
        {
            // 如果目录不存在，直接返回空列表
            return;
        }
        var dirs = Directory.EnumerateDirectories(culturesPath, "Culture_*", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetFileName(path))  // 只要文件夹名，不要完整路径
                    .ToList();
        foreach (var cultureFolderName in dirs) 
        {
            var species = cultureFolderName.Split('_')[2];
            var culture = cultureFolderName.Split('_')[1];
            if (AllCivSpecies.Contains(species))
                ConfigData.speciesCulturePair.Add(species, culture);
        }
    }

}
