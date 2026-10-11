using System.Collections.Generic;
using System.IO;
using NeoModLoader.General;
using NeoModLoader.services;
using NeoModLoader.utils;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 文化图标：
//   1. 文化文件夹里自带的 Locales/Cultures/Culture_<文化>/icon.png(玩家自己配置新文化时放这里即可)；
//   2. 模组内置的 GameResources/ui/icons/cultures/<文化>.png(由 Tools/IconForge 生成)；
//   3. 都没有就用默认图标 ui/icons/cultures/Default。
public static class CultureIcons
{
    private const string BuiltinPath = "ui/icons/cultures/";
    private static readonly Dictionary<string, Sprite> Cache = new();

    public static Sprite Get(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return Default;
        if (Cache.TryGetValue(culture, out Sprite cached) && cached != null) return cached;
        Sprite sprite = LoadCustom(culture) ?? TryBuiltin(culture) ?? Default;
        Cache[culture] = sprite;
        return sprite;
    }

    private static Sprite Default => TryBuiltin("Default");

    private static Sprite LoadCustom(string culture)
    {
        string path = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "Culture_" + culture, "icon.png");
        if (!File.Exists(path)) return null;
        try
        {
            Sprite sprite = SpriteLoadUtils.LoadSingleSprite(path);
            HiResIconFilter.Smooth(sprite);
            return sprite;
        }
        catch (global::System.Exception e)
        {
            LogService.LogWarning($"[CultureIcons] 读取 {path} 失败: {e.Message}");
            return null;
        }
    }

    private static Sprite TryBuiltin(string name)
    {
        try
        {
            return SpriteTextureLoader.getSprite(BuiltinPath + name);
        }
        catch
        {
            return null;
        }
    }
}
