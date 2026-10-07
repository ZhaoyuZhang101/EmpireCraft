using System;
using System.IO;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

public static class BundledTerritoryFonts
{
    public const string Seal = "Jingfeng_ZSKSS";
    private static bool _loaded;
    private static Font _font;

    public static bool Available => Load() != null;

    public static Font Load()
    {
        if (_loaded) return _font;
        _loaded = true;
        string path = Path.Combine(ModClass._declare.FolderPath, "Assets", "Fonts", "JFZSKSealScript",
            "JFZSKSealScript_V3.ttf");
        if (!File.Exists(path))
        {
            LogService.LogWarning($"[EmpireCraft] 内置篆书文件不存在: {path}");
            return null;
        }
        try
        {
            // 带目录的路径走 Unity 的 Internal_CreateFontFromPath：直接读字体文件，随模组加载，不要求系统安装。
            Font candidate = new Font(path);
            candidate.RequestCharactersInTexture("华夏国", 64, FontStyle.Normal);
            // 刚从文件建的字体 HasCharacter 可能还报 false，再看字形是否真的进了图集；两种都不认才算失败
            bool has = candidate.HasCharacter('华') ||
                       candidate.GetCharacterInfo('华', out CharacterInfo info, 64, FontStyle.Normal) && info.advance > 0;
            if (has)
            {
                _font = candidate;
                LogService.LogInfo($"[EmpireCraft] 内置篆书已加载: {candidate.name}");
            }
            else
            {
                LogService.LogWarning("[EmpireCraft] 内置篆书已读入但取不到字形，改用游戏字体");
                UnityEngine.Object.Destroy(candidate);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 内置篆书加载失败: {exception.Message}");
        }
        return _font;
    }

    // 重新扫描时再试一次(上次失败不会永久记住)
    public static void Reset()
    {
        if (_font == null) _loaded = false;
    }

    public static string DisplayName(string name) => name == Seal ? "敬峰中山王篆" : name;
}
