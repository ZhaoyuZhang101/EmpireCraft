using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 随模组分发的篆书字体(见 Assets/Fonts 下各目录的 README)：
//   全字库说文解字(小篆，6745 字，按繁体编码，显示前把简体转成繁体再查字)；
//   敬峰中山王篆(战国中山国铜器文字，归大篆，3060 字，简繁都有)。
// 加载：本机装了同名字体就用系统字体；有预先画好的字形包(.glyphs)就用字形包(不靠 Unity 读字体文件，见 BakedGlyphFont)；
// 都没有才直接读模组里的字体文件(游戏里常常画不出字)。
// 缺字检查一律读字体文件的 cmap 表(从文件建的 Unity 字体 HasCharacter 不可靠)。
public sealed class BundledFont
{
    public readonly string Id;
    public readonly string Name;
    public readonly string StyleKey;
    // 字体按繁体编码：显示前把简体转成繁体
    public readonly bool Traditional;
    private readonly string _folder;
    private readonly string _file;
    private readonly string[] _osNames;
    // 字形包文件名(同目录)；没有为 null
    private readonly string _glyphPack;
    private BakedGlyphFont _baked;
    private bool _loaded;
    private Font _font;
    private HashSet<int> _codepoints;

    public BundledFont(string id, string name, string styleKey, bool traditional, string folder, string file,
        string glyphPack, params string[] osNames)
    {
        _glyphPack = glyphPack;
        Id = id;
        Name = name;
        StyleKey = styleKey;
        Traditional = traditional;
        _folder = folder;
        _file = file;
        _osNames = osNames;
    }

    public string FilePath => Path.Combine(ModClass._declare.FolderPath, "Assets", "Fonts", _folder, _file);
    public Font Font => _font;
    // 正在用字形包显示(不是系统字体或字体文件)
    public BakedGlyphFont Baked => _font != null && _baked != null && _font == _baked.Font ? _baked : null;
    public bool Available => Load() != null;

    public Font Load()
    {
        if (_loaded) return _font;
        _loaded = true;
        string path = FilePath;
        string packPath = _glyphPack == null ? null : Path.Combine(ModClass._declare.FolderPath, "Assets", "Fonts", _folder, _glyphPack);
        if (!File.Exists(path) && (packPath == null || !File.Exists(packPath)))
        {
            LogService.LogWarning($"[EmpireCraft] 内置字体文件不存在: {path}");
            return null;
        }
        if (File.Exists(path)) _codepoints = BundledTerritoryFonts.ReadCodepoints(path);
        string source = "文件";
        try
        {
            // 本机装了这款字体：用系统字体，和其它本机字体走同一条路
            string installed = null;
            try
            {
                string[] names = Font.GetOSInstalledFontNames();
                installed = _osNames.FirstOrDefault(name => names.Contains(name, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // 读不到本机字体列表就直接读文件
            }
            Font candidate = null;
            if (installed != null)
            {
                candidate = Font.CreateDynamicFontFromOSFont(installed, 64);
                source = "本机已安装(" + installed + ")";
            }
            // 字形包：按包里收的字查字
            if (candidate == null && packPath != null && (_baked = BakedGlyphFont.TryLoad(packPath, Id)) != null)
            {
                _font = _baked.Font;
                _codepoints = null;
                LogService.LogInfo($"[EmpireCraft] 内置字体已加载: {Name} 来源=字形包 字数={_baked.Count}");
                return _font;
            }
            if (candidate == null && !File.Exists(path)) return null;
            // 带目录的路径走 Unity 的 Internal_CreateFontFromPath：直接读字体文件，不要求系统安装
            if (candidate == null)
            {
                candidate = new Font(path);
                source = "文件";
            }
            candidate.RequestCharactersInTexture(Traditional ? "華夏國" : "华夏国", 64, FontStyle.Normal);
            bool has = _codepoints != null && _codepoints.Count > 0 || candidate.HasCharacter(Traditional ? '華' : '华');
            if (has)
            {
                _font = candidate;
                LogService.LogInfo($"[EmpireCraft] 内置字体已加载: {Name} 来源={source} 字表={_codepoints?.Count ?? -1} " +
                                   $"HasCharacter={candidate.HasCharacter(Traditional ? '華' : '华')} 动态={candidate.dynamic} " +
                                   $"材质={(candidate.material == null ? "无" : candidate.material.name)} " +
                                   $"贴图={(candidate.material?.mainTexture == null ? "无" : candidate.material.mainTexture.width + "x" + candidate.material.mainTexture.height)}");
            }
            else
            {
                LogService.LogWarning($"[EmpireCraft] 内置字体 {Name} 读入但取不到字形");
                UnityEngine.Object.Destroy(candidate);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 内置字体 {Name} 加载失败: {exception.Message}");
        }
        return _font;
    }

    public void Reset()
    {
        if (_font == null) _loaded = false;
    }

    public bool Supports(int codepoint)
    {
        if (Baked != null) return Baked.Supports(codepoint);
        if (_codepoints != null) return _codepoints.Contains(codepoint);
        return _font != null && _font.HasCharacter((char)codepoint);
    }

    // 把要显示的文字改写成这款字体能显示的样子(繁体字体先转繁体)；有字显示不了返回 false。
    // 富文本的标签原样保留，空白不查
    public bool TryAdapt(string text, bool richText, out string adapted)
    {
        adapted = text ?? "";
        if (Load() == null) return false;
        var builder = new StringBuilder(adapted.Length);
        bool insideTag = false;
        foreach (char character in adapted)
        {
            if (richText && character == '<') insideTag = true;
            if (insideTag || char.IsWhiteSpace(character))
            {
                if (insideTag && character == '>') insideTag = false;
                builder.Append(character);
                continue;
            }
            char shown = character;
            if (Traditional)
            {
                // 一简对多繁时取字体里有的第一个；都没有就看原字
                shown = '\0';
                foreach (char candidate in BundledTerritoryFonts.Traditionals(character))
                    if (Supports(candidate)) { shown = candidate; break; }
                if (shown == '\0' && Supports(character)) shown = character;
                if (shown == '\0') return false;
            }
            else if (!Supports(character)) return false;
            builder.Append(shown);
        }
        adapted = builder.ToString();
        return true;
    }
}

public static class BundledTerritoryFonts
{
    // 字体设置里用的名字(保持旧的 Seal 名字不变，旧设置文件仍然有效)
    public const string Seal = "Jingfeng_ZSKSS";
    public const string ShuoWen = "QuanZiKu_ShuoWen";

    public static readonly BundledFont ShuoWenFont = new(ShuoWen, "全字库说文解字", "font_style_seal_small", true,
        "QuanZiKuShuoWen", "QuanZiKuShuoWen.ttf", "ShuoWen.glyphs", "全字庫說文解字", "EBAS");
    public static readonly BundledFont JingfengFont = new(Seal, "敬峰中山王篆", "font_style_seal_large", false,
        "JFZSKSealScript", "JFZSKSealScript_V3.ttf", null, "Jingfeng_ZSKSS", "大学论语敬峰中山王篆", "敬峰中山王篆", "JFZSKSealScript");

    public static readonly BundledFont[] All = { ShuoWenFont, JingfengFont };
    // 华夏国号用篆书：只用说文小篆(中山王篆是战国铜器文字，笔画细长，不像通常的篆书国号)，缺字走兜底
    public static readonly BundledFont[] SealChain = { ShuoWenFont };

    public static BundledFont Find(string id) => All.FirstOrDefault(font => font.Id == id);

    public static BundledFont Of(Font font) => font == null ? null : All.FirstOrDefault(item => item.Font == font);

    public static bool Available => All.Any(font => font.Available);

    public static IEnumerable<string> AvailableIds => All.Where(font => font.Available).Select(font => font.Id);

    // 旧接口：中山王篆
    public static Font Load() => JingfengFont.Load();

    public static bool IsBundled(Font font) => Of(font) != null;

    // 用字形包显示的字体(非动态字体：不认字号，Text.fontSize 要设 0，行高按 LineHeight)
    public static BakedGlyphFont BakedOf(Font font) => Of(font)?.Baked;

    public static bool Supports(Font font, char character)
    {
        BundledFont bundled = Of(font);
        return bundled != null ? bundled.TryAdapt(character.ToString(), false, out _) : font != null && font.HasCharacter(character);
    }

    public static string DisplayName(string name) => Find(name)?.Name ?? name;

    // ---- 简繁对照(OpenCC STCharacters.txt) ----
    private static Dictionary<char, char[]> _traditional;

    public static IEnumerable<char> Traditionals(char character)
    {
        if (_traditional == null) LoadTraditional();
        return _traditional.TryGetValue(character, out char[] values) ? values : Array.Empty<char>();
    }

    private static void LoadTraditional()
    {
        _traditional = new Dictionary<char, char[]>();
        string path = Path.Combine(ModClass._declare.FolderPath, "Assets", "Fonts", "OpenCC", "STCharacters.txt");
        try
        {
            if (!File.Exists(path))
            {
                LogService.LogWarning($"[EmpireCraft] 简繁对照表不存在: {path}");
                return;
            }
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Length < 3 || line[0] == '#') continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 2 || parts[0].Length != 1) continue;
                char[] values = parts[1].Split(' ').Where(value => value.Length == 1).Select(value => value[0]).ToArray();
                if (values.Length > 0) _traditional[parts[0][0]] = values;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 读取简繁对照表失败: {exception.Message}");
        }
    }

    // 读 TrueType 的 cmap 表：优先 Windows Unicode 全集(3,10，格式 12)，其次 Windows BMP(3,1，格式 4)或 Unicode 平台(0,*)
    public static HashSet<int> ReadCodepoints(string path)
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
        foreach (BundledFont font in All) font.Reset();
    }
}
