using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Compatibility;

// 给兼容模组(WarBox、modernmod)补中文。不改它们自己的文件：译文放在本模组 Locales/Compat/<模组>/<语言>.json，
// 只有对应模组装了才写进游戏。
// 它们自己也会往当前语言里写英文(modernmod 在初始化时 AddToCurrentLocale，WarBox 随 NML 加载)，
// 谁后写谁生效，所以在这几个时机都补写一遍：所有模组加载完后的第一帧、每次切换语言之后、进世界时。
public class CompatLocalization : GamePatch
{
    public ModDeclare declare { get; set; }

    private static readonly (string folder, string probeType)[] Mods =
    {
        ("WarBox", "WarBox.Content.DiplomacyRelations"),
        ("ModernMod", "ModernMod.Code.Buildings"),
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new();

    public void Initialize()
    {
        new Harmony("EmpireCraft.CompatLocalization").Patch(
            AccessTools.Method(typeof(LocalizedTextManager), nameof(LocalizedTextManager.setLanguage)),
            postfix: new HarmonyMethod(typeof(CompatLocalization), nameof(AfterSetLanguage)) { priority = Priority.Last });
    }

    public static void AfterSetLanguage() => Apply();

    public static void Apply()
    {
        if (ModClass._declare == null || LocalizedTextManager.instance == null) return;
        string language = LocalizedTextManager.instance.language;
        if (string.IsNullOrEmpty(language)) return;
        foreach ((string folder, string probeType) in Mods)
        {
            if (AccessTools.TypeByName(probeType) == null) continue;
            Dictionary<string, string> texts = Load(folder, language);
            if (texts == null) continue;
            foreach (KeyValuePair<string, string> pair in texts) LM.AddToCurrentLocale(pair.Key, pair.Value);
        }
    }

    private static Dictionary<string, string> Load(string folder, string language)
    {
        string cacheKey = folder + "/" + language;
        if (Cache.TryGetValue(cacheKey, out Dictionary<string, string> cached)) return cached;
        Dictionary<string, string> texts = null;
        string path = Path.Combine(ModClass._declare.FolderPath, "Locales", "Compat", folder, language + ".json");
        if (File.Exists(path))
        {
            try
            {
                texts = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 兼容模组译文读取失败 {path}: {exception.Message}");
            }
        }
        Cache[cacheKey] = texts;
        return texts;
    }
}
