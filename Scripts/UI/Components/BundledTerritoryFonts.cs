using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

public static class BundledTerritoryFonts
{
    public const string Seal = "Jingfeng_ZSKSS";
    private static bool _loaded;
    private static Font _font;
    // 字体文件里实际收录的字(读 cmap 表)。从文件建的 Unity 字体 HasCharacter 常常对所有字都报 false，
    // 铭牌的缺字检查就会把每个名字都回退成游戏字体，所以缺字检查改用这张表
    private static HashSet<int> _codepoints;

    private static string FontPath => Path.Combine(ModClass._declare.FolderPath, "Assets", "Fonts", "JFZSKSealScript",
        "JFZSKSealScript_V3.ttf");

    public static bool IsBundled(Font font) => font != null && font == _font;

    // 字体收录了这个字吗(读不到字表时退回 Unity 的判断)
    public static bool Supports(char character)
    {
        if (_codepoints != null) return _codepoints.Contains(character);
        return _font != null && _font.HasCharacter(character);
    }

    public static bool Available => Load() != null;

    public static Font Load()
    {
        if (_loaded) return _font;
        _loaded = true;
        string path = FontPath;
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
            _codepoints = ReadCodepoints(path);
            if (!has && _codepoints != null && _codepoints.Contains('华')) has = true;
            if (has)
            {
                _font = candidate;
                LogService.LogInfo($"[EmpireCraft] 内置篆书已加载: {candidate.name} 字表={_codepoints?.Count ?? -1} " +
                                   $"HasCharacter(华)={candidate.HasCharacter('华')} 动态={candidate.dynamic} " +
                                   $"材质={(candidate.material == null ? "无" : candidate.material.name)} " +
                                   $"贴图={(candidate.material?.mainTexture == null ? "无" : candidate.material.mainTexture.width + "x" + candidate.material.mainTexture.height)}");
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

    // 读 TrueType 的 cmap 表：优先 Windows Unicode 全集(3,10，格式 12)，其次 Windows BMP(3,1，格式 4)或 Unicode 平台(0,*)
    private static HashSet<int> ReadCodepoints(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            int U16(int at) => (data[at] << 8) | data[at + 1];
            int I16(int at) => (short)U16(at);
            long U32(int at) => ((long)data[at] << 24) | ((long)data[at + 1] << 16) | ((long)data[at + 2] << 8) | data[at + 3];
            int tables = U16(4);
            int cmap = -1;
            for (int i = 0; i < tables; i++)
            {
                int record = 12 + i * 16;
                if (data[record] == 'c' && data[record + 1] == 'm' && data[record + 2] == 'a' && data[record + 3] == 'p')
                    cmap = (int)U32(record + 8);
            }
            if (cmap < 0) return null;
            int best = -1, bestScore = -1;
            int subtables = U16(cmap + 2);
            for (int i = 0; i < subtables; i++)
            {
                int record = cmap + 4 + i * 8;
                int platform = U16(record), encoding = U16(record + 2);
                int offset = cmap + (int)U32(record + 4);
                int format = U16(offset);
                int score = format == 12 && (platform == 3 && encoding == 10 || platform == 0) ? 3
                    : format == 4 && (platform == 3 && encoding == 1 || platform == 0) ? 2 : -1;
                if (score > bestScore) { bestScore = score; best = offset; }
            }
            if (best < 0) return null;
            var result = new HashSet<int>();
            if (U16(best) == 12)
            {
                long groups = U32(best + 12);
                for (long g = 0; g < groups; g++)
                {
                    int at = best + 16 + (int)g * 12;
                    long start = U32(at), end = U32(at + 4);
                    for (long code = start; code <= end && code - start < 70000; code++) result.Add((int)code);
                }
            }
            else
            {
                int segments = U16(best + 6) / 2;
                int ends = best + 14, starts = ends + segments * 2 + 2;
                int deltas = starts + segments * 2, ranges = deltas + segments * 2;
                for (int seg = 0; seg < segments; seg++)
                {
                    int end = U16(ends + seg * 2), start = U16(starts + seg * 2);
                    int delta = I16(deltas + seg * 2), rangeAt = ranges + seg * 2, range = U16(rangeAt);
                    if (start == 0xFFFF) continue;
                    for (int code = start; code <= end; code++)
                    {
                        int glyph = range == 0 ? (code + delta) & 0xFFFF
                            : U16(rangeAt + range + (code - start) * 2) is int raw && raw != 0 ? (raw + delta) & 0xFFFF : 0;
                        if (glyph != 0) result.Add(code);
                    }
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 读取内置篆书字表失败: {exception.Message}");
            return null;
        }
    }

    // 重新扫描时再试一次(上次失败不会永久记住)
    public static void Reset()
    {
        if (_font == null) _loaded = false;
    }

    public static string DisplayName(string name) => name == Seal ? "敬峰中山王篆" : name;
}
