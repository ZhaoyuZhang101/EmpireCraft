using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 领土铭牌(简化铭牌)的字体。包含随模组分发的篆书和本机字体，提供三种选择：
//   · auto(默认)：优先使用内置篆书，其次按隶书 → 其他篆书 → 草书 → 行书 → 魏碑 → 楷书 → 宋/明体挑本机字体，
//     一个都没有就用游戏字体；含英文字母的名字仍用西文衬线字体；
//   · game：一律用游戏自带字体，所有电脑显示一致；
//   · 其他：指定某个已安装字体的名字(选了不存在的字体按游戏字体显示)。
// 想用新字体：在字体文件上右键"为所有用户安装"，重启游戏后即出现在列表中。
// 选择存在模组目录外(Mods/EmpireCraftTerritoryFont.txt)，模组更新不会清掉。
public static class TerritoryFontSettings
{
    public const string Auto = "auto";
    public const string Game = "game";

    // 传统书体：(本地化 key, 优先级, 字体名关键词)
    private static readonly (string key, int rank, string[] keywords)[] Styles =
    {
        ("font_style_clerical", 0, new[] { "隶", "隸", "LiSu", "Liti", "LiShu" }),
        // 篆书：先认大篆(金文、钟鼎、甲骨)，再认小篆(说文、秦篆)，其余带"篆/Seal"的归篆书
        ("font_style_seal_large", 1, new[] { "大篆", "金文", "钟鼎", "鐘鼎", "甲骨", "Bronze", "Oracle", "DaZhuan" }),
        ("font_style_seal_small", 1, new[] { "小篆", "说文", "說文", "Shuowen", "XiaoZhuan", "Small Seal" }),
        ("font_style_seal", 1, new[] { "篆", "Zhuan", "Seal" }),
        ("font_style_cursive", 2, new[] { "草", "Cao" }),
        ("font_style_running", 3, new[] { "行楷", "行书", "行書", "Xingkai", "XingShu", "舒体", "ShuTi" }),
        ("font_style_weibei", 4, new[] { "魏", "WeiBei", "Xinwei" }),
        ("font_style_regular", 5, new[] { "楷", "KaiTi", "Kaiti", "Kai-SB", "FZKai", "STKai" }),
        ("font_style_song", 6, new[] { "宋", "SongTi", "Songti", "SimSun", "STSong", "仿宋", "FangSong", "明朝", "明體",
            "MingLiU", "Mincho" })
    };

    private static string _choice;
    private static bool? _sealHuaxia;

    private static string SealHuaxiaPath
    {
        get
        {
            string parent = Directory.GetParent(ModClass._declare.FolderPath)?.FullName ?? ModClass._declare.FolderPath;
            return Path.Combine(parent, "EmpireCraftSealHuaxia.txt");
        }
    }

    // 华夏国号用篆书：开启后华夏文化(非现代政体)的国号铭牌一律用内置篆书、不带后缀，不受上面的字体选择影响。
    // 默认开启；存在模组目录外，模组更新不会清掉
    public static bool SealHuaxiaNames
    {
        get
        {
            if (_sealHuaxia.HasValue) return _sealHuaxia.Value;
            _sealHuaxia = true;
            try
            {
                if (File.Exists(SealHuaxiaPath)) _sealHuaxia = File.ReadAllText(SealHuaxiaPath).Trim() != "0";
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 读取华夏国号篆书设置失败: {exception.Message}");
            }
            return _sealHuaxia.Value;
        }
        set
        {
            _sealHuaxia = value;
            try
            {
                File.WriteAllText(SealHuaxiaPath, value ? "1" : "0");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 保存华夏国号篆书设置失败: {exception.Message}");
            }
            TerritoryLabelRenderer.ResetFonts();
        }
    }
    private static string[] _installed;
    // 铭牌每帧都要问用哪个字体：解析结果缓存起来，设置改变或重新扫描时作废
    private static string _resolved;
    private static bool _resolvedValid;

    private static string FilePath
    {
        get
        {
            string parent = Directory.GetParent(ModClass._declare.FolderPath)?.FullName ?? ModClass._declare.FolderPath;
            return Path.Combine(parent, "EmpireCraftTerritoryFont.txt");
        }
    }

    public static string Choice
    {
        get
        {
            if (_choice != null) return _choice;
            _choice = Auto;
            try
            {
                if (File.Exists(FilePath))
                {
                    string saved = File.ReadAllText(FilePath).Trim();
                    if (!string.IsNullOrEmpty(saved)) _choice = saved;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 读取铭牌字体设置失败: {exception.Message}");
            }
            return _choice;
        }
        set
        {
            _choice = string.IsNullOrWhiteSpace(value) ? Auto : value.Trim();
            _resolvedValid = false;
            try
            {
                File.WriteAllText(FilePath, _choice);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 保存铭牌字体设置失败: {exception.Message}");
            }
            TerritoryLabelRenderer.ResetFonts();
        }
    }

    public static string[] InstalledFonts()
    {
        if (_installed != null) return _installed;
        try
        {
            IEnumerable<string> names = Font.GetOSInstalledFontNames();
            names = names.Concat(BundledTerritoryFonts.AvailableIds);
            _installed = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 读取已安装字体失败: {exception.Message}");
            _installed = Array.Empty<string>();
        }
        return _installed;
    }

    // 重新扫描已安装字体(刚装了新字体时)
    public static void Rescan()
    {
        BundledTerritoryFonts.Reset();
        _installed = null;
        _resolvedValid = false;
        TerritoryLabelRenderer.ResetFonts();
    }

    // 字体属于哪种传统书体：返回本地化 key 与优先级；不是传统书体返回 null
    public static (string key, int rank)? StyleOf(string fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return null;
        BundledFont bundled = BundledTerritoryFonts.Find(fontName);
        if (bundled != null) return (bundled.StyleKey, -1);
        foreach ((string key, int rank, string[] keywords) in Styles)
            if (keywords.Any(keyword => fontName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                return (key, rank);
        return null;
    }

    // 本机已安装的传统书体，按优先级排列
    public static List<(string name, string styleKey)> TraditionalFonts() => InstalledFonts()
        .Select(name => (name, style: StyleOf(name)))
        .Where(item => item.style.HasValue)
        .OrderBy(item => item.style.Value.rank)
        // 同类里原先默认的隶书(LiSu)排第一，保持以前的观感
        .ThenByDescending(item => item.name is "LiSu" or "隶书")
        .ThenBy(item => item.name, StringComparer.Ordinal)
        .Select(item => (item.name, item.style.Value.key)).ToList();

    public static List<string> OtherFonts() => InstalledFonts().Where(name => !StyleOf(name).HasValue).ToList();

    public static bool IsInstalled(string fontName) =>
        InstalledFonts().Any(name => string.Equals(name, fontName, StringComparison.OrdinalIgnoreCase));

    // 汉字名实际要用的字体名；null 表示用游戏字体
    public static string ResolveFontName()
    {
        if (_resolvedValid) return _resolved;
        string choice = Choice;
        _resolved = choice == Game ? null
            : choice == Auto ? TraditionalFonts().Select(item => item.name).FirstOrDefault()
            : IsInstalled(choice) ? choice : null;
        _resolvedValid = true;
        return _resolved;
    }
}
