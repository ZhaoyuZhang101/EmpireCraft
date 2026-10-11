using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 预先画好的字形包(.glyphs，由 Tools/FontBake/bake_seal_glyphs.py 从字体文件生成)做成的 Unity 字体。
// 从文件直接读的字体在游戏里常常画不出字(空白)，这里不靠 Unity 读字体：
// 用到哪个字才把它解开拼进一张贴图，再按贴图位置给字体填字形信息，铭牌的 Text 照常使用。
// 字形按 ReferenceSize(铭牌的参考字号)排版；非动态字体不认字号，用的地方把 Text.fontSize 设 0。
public sealed class BakedGlyphFont
{
    private const int AtlasSize = 2048;
    private const int Padding = 2;
    public const int ReferenceSize = TerritoryLabelProjection.ReferenceFontSize;

    private struct Entry
    {
        public short advance, left, top, width, height;
        public int offset, length;
    }

    private readonly Dictionary<int, Entry> _index = new();
    private readonly byte[] _data;
    private readonly int _dataStart;
    private readonly int _cell;
    private readonly int _ascent;
    private readonly int _descent;
    private readonly Texture2D _atlas;
    private readonly List<CharacterInfo> _infos = new();
    private readonly HashSet<int> _placed = new();
    private int _penX = Padding, _penY = Padding, _rowHeight;
    private bool _full;

    public Font Font { get; }
    public int Count => _index.Count;
    private float Scale => (float)ReferenceSize / _cell;
    // 一行的高度(参考字号下)
    public float LineHeight => (_ascent + _descent) * Scale;

    private BakedGlyphFont(string name, byte[] data)
    {
        _data = data;
        if (data.Length < 24 || data[0] != 'E' || data[1] != 'C' || data[2] != 'S' || data[3] != 'G')
            throw new InvalidDataException("不是字形包");
        _cell = BitConverter.ToInt32(data, 8);
        _ascent = BitConverter.ToInt32(data, 12);
        _descent = BitConverter.ToInt32(data, 16);
        int count = BitConverter.ToInt32(data, 20);
        int at = 24;
        for (int i = 0; i < count; i++, at += 22)
        {
            _index[BitConverter.ToInt32(data, at)] = new Entry
            {
                advance = BitConverter.ToInt16(data, at + 4),
                left = BitConverter.ToInt16(data, at + 6),
                top = BitConverter.ToInt16(data, at + 8),
                width = BitConverter.ToInt16(data, at + 10),
                height = BitConverter.ToInt16(data, at + 12),
                offset = BitConverter.ToInt32(data, at + 14),
                length = BitConverter.ToInt32(data, at + 18)
            };
        }
        _dataStart = at;
        _atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false)
        {
            name = name + "_atlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        var clear = new Color32[AtlasSize * AtlasSize];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(255, 255, 255, 0);
        _atlas.SetPixels32(clear);
        _atlas.Apply(false);
        Font = new Font(name);
        Shader shader = Shader.Find("UI/Default");
        Font.material = new Material(shader != null ? shader : Canvas.GetDefaultCanvasMaterial().shader) { mainTexture = _atlas };
        Font.characterInfo = Array.Empty<CharacterInfo>();
    }

    public static BakedGlyphFont TryLoad(string path, string name)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var font = new BakedGlyphFont(name, File.ReadAllBytes(path));
            LogService.LogInfo($"[EmpireCraft] 字形包已加载: {name} 字数={font.Count} 字号={font._cell}");
            return font;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 字形包加载失败 {path}: {exception.Message}");
            return null;
        }
    }

    public bool Supports(int codepoint) => _index.ContainsKey(codepoint);

    // 确保这些字都已拼进贴图；有字不在包里或贴图满了返回 false(调用方改用兜底字体)
    public bool Ensure(string text)
    {
        bool added = false;
        bool ok = true;
        foreach (char character in text ?? "")
        {
            if (char.IsWhiteSpace(character) || _placed.Contains(character)) continue;
            if (!_index.TryGetValue(character, out Entry entry) || !Place(character, entry))
            {
                ok = false;
                break;
            }
            added = true;
        }
        if (added)
        {
            _atlas.Apply(false);
            Font.characterInfo = _infos.ToArray();
        }
        return ok;
    }

    private bool Place(int codepoint, Entry entry)
    {
        if (_full) return false;
        int width = entry.width, height = entry.height;
        if (_penX + width + Padding > AtlasSize)
        {
            _penX = Padding;
            _penY += _rowHeight + Padding;
            _rowHeight = 0;
        }
        if (_penY + height + Padding > AtlasSize)
        {
            _full = true;
            LogService.LogWarning("[EmpireCraft] 字形包贴图已满，新出现的字改用兜底字体");
            return false;
        }
        byte[] pixels = Inflate(entry);
        if (pixels == null || pixels.Length < width * height) return false;
        // 字形数据从上到下逐行，贴图从下往上：翻过来写
        var colors = new Color32[width * height];
        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
            colors[(height - 1 - row) * width + column] = new Color32(255, 255, 255, pixels[row * width + column]);
        _atlas.SetPixels32(_penX, _penY, width, height, colors);

        float scale = Scale;
        // 字身(上伸到下伸)的中线放在基线上，单行铭牌上下居中
        float shift = (_ascent - _descent) * 0.5f;
        float u0 = (float)_penX / AtlasSize, v0 = (float)_penY / AtlasSize;
        float u1 = (float)(_penX + width) / AtlasSize, v1 = (float)(_penY + height) / AtlasSize;
        var info = new CharacterInfo
        {
            index = codepoint,
            advance = Mathf.RoundToInt(entry.advance * scale),
            minX = Mathf.RoundToInt(entry.left * scale),
            maxX = Mathf.RoundToInt((entry.left + width) * scale),
            maxY = Mathf.RoundToInt((entry.top - shift) * scale),
            minY = Mathf.RoundToInt((entry.top - height - shift) * scale),
            uvBottomLeft = new Vector2(u0, v0),
            uvBottomRight = new Vector2(u1, v0),
            uvTopLeft = new Vector2(u0, v1),
            uvTopRight = new Vector2(u1, v1)
        };
        _infos.Add(info);
        _placed.Add(codepoint);
        _penX += width + Padding;
        _rowHeight = Mathf.Max(_rowHeight, height);
        return true;
    }

    private byte[] Inflate(Entry entry)
    {
        try
        {
            using var input = new MemoryStream(_data, _dataStart + entry.offset, entry.length, false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            var output = new byte[entry.width * entry.height];
            int read = 0;
            while (read < output.Length)
            {
                int n = deflate.Read(output, read, output.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return output;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 字形解压失败: {exception.Message}");
            return null;
        }
    }
}
