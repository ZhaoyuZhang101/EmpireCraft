using System;
using System.Collections.Generic;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 整图画布分块上传(用户 2026-11-11 第一档②"贴图分块、只传脏块")：画布画在原版地块图层那张整图贴图上(地图多大它多大，
// 世界地图上八兆来)，以前改了几格就整张 SetPixels32 + Apply(原版 updatePixels)，翻牌、悬停高亮时一秒至多二十回。
// 现在画布每写一格就记下所在的 64×64 块(见 Marked)，上传时只把这几块抄进小贴图、传上去，再在显卡上拷进整图(Graphics.CopyTexture)。
// 整张传的照旧：换图层、进图(画布整张画)，脏块太多，显卡不支持块拷贝，或者有改动却一块都没记到(别处直接写的)。
// 兜底：只拷过块之后每 FullUploadSeconds 秒再整张传一回，没跟踪到的写入至多晚这么久显出来
public static partial class SmoothBorders
{
    private const int UploadChunk = 64, MaxChunkUploads = 48;
    private const float FullUploadSeconds = 2f;
    private static bool[] _chunkDirty = new bool[0];
    private static readonly List<int> DirtyChunks = new();
    private static int _chunkMapW, _chunkMapH, _chunkCols;
    private static Texture2D[] _staging = new Texture2D[0];
    private static readonly Color32[] StagingPixels = new Color32[UploadChunk * UploadChunk];
    private static float _fullUploadAt;
    private static bool _partialSinceFull;
    // 显卡支不支持块拷贝：-1 还没看，0 不支持(或拷出过错，本局都整张传)，1 支持
    private static int _copySupport = -1;

    // 画布写这一格时包在要写的颜色外面：记下所在的块，颜色原样返回(写法不变：pixels[id] = Marked(id, 颜色))
    private static Color32 Marked(int id, Color32 color)
    {
        MarkPixel(id);
        return color;
    }

    // 这一格所在的块记脏(地块图层的像素按格编号：x + y × 地图宽)
    internal static void MarkPixel(int id)
    {
        if (id < 0 || _chunkMapW <= 0) return;
        int y = id / _chunkMapW;
        if (y >= _chunkMapH) return;
        int chunk = y / UploadChunk * _chunkCols + id % _chunkMapW / UploadChunk;
        if (chunk >= _chunkDirty.Length || _chunkDirty[chunk]) return;
        _chunkDirty[chunk] = true;
        DirtyChunks.Add(chunk);
    }

    // 地图大小对上(换图、改尺寸后头一回)
    private static void EnsureUploadChunks()
    {
        int width = MapBox.width, height = MapBox.height;
        if (width == _chunkMapW && height == _chunkMapH) return;
        _chunkMapW = width;
        _chunkMapH = height;
        _chunkCols = (width + UploadChunk - 1) / UploadChunk;
        int rows = (height + UploadChunk - 1) / UploadChunk;
        _chunkDirty = new bool[Math.Max(0, _chunkCols * rows)];
        DirtyChunks.Clear();
        _partialSinceFull = false;
    }

    private static void ClearDirtyChunks()
    {
        foreach (int chunk in DirtyChunks)
            if (chunk < _chunkDirty.Length) _chunkDirty[chunk] = false;
        DirtyChunks.Clear();
    }

    private static bool CopySupported()
    {
        if (_copySupport < 0)
            _copySupport = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0 ? 1 : 0;
        return _copySupport == 1;
    }

    // 画布这一帧该不该传、怎么传(DrawCanvas 末尾调；原来那段是"有改动且过了上传间隔就整张传")
    private static void CanvasUpload(ZoneCalculator calc, float now, float interval)
    {
        EnsureUploadChunks();
        bool safety = _partialSinceFull && now - _fullUploadAt >= FullUploadSeconds;
        if (!calc._dirty && !safety) return;
        if (!(_uploadNow || safety || now < _uploadAt || now - _uploadAt >= interval)) return;
        bool full = _uploadNow || safety || DirtyChunks.Count == 0 || DirtyChunks.Count > MaxChunkUploads || !CopySupported() ||
                    calc.texture == null || calc.texture.width != _chunkMapW || calc.texture.height != _chunkMapH ||
                    calc.pixels == null || calc.pixels.Length != _chunkMapW * _chunkMapH;
        calc._dirty = false;
        _uploadNow = false;
        _uploadAt = now;
        if (!full && !UploadChunks(calc)) full = true;
        if (full)
        {
            calc.updatePixels();
            _fullUploadAt = now;
            _partialSinceFull = false;
        }
        else _partialSinceFull = true;
        ClearDirtyChunks();
    }

    // 只传脏块：每块抄进一张 64×64 的小贴图传上去，再在显卡上拷到整图的对应位置；出错返回 false(本局改回整张传)
    private static bool UploadChunks(ZoneCalculator calc)
    {
        try
        {
            EnsureStaging(DirtyChunks.Count);
            Color32[] pixels = calc.pixels;
            Texture2D target = calc.texture;
            for (int i = 0; i < DirtyChunks.Count; i++)
            {
                int chunk = DirtyChunks[i];
                int x0 = chunk % _chunkCols * UploadChunk, y0 = chunk / _chunkCols * UploadChunk;
                int width = Math.Min(UploadChunk, _chunkMapW - x0), height = Math.Min(UploadChunk, _chunkMapH - y0);
                if (width <= 0 || height <= 0) continue;
                for (int row = 0; row < height; row++)
                    Array.Copy(pixels, (y0 + row) * _chunkMapW + x0, StagingPixels, row * UploadChunk, width);
                Texture2D staging = _staging[i];
                staging.SetPixels32(StagingPixels);
                staging.Apply(false);
                Graphics.CopyTexture(staging, 0, 0, 0, 0, width, height, target, 0, 0, x0, y0);
            }
            return true;
        }
        catch (Exception exception)
        {
            _copySupport = 0;
            LogService.LogWarning($"[EmpireCraft][整图画布] 分块上传出错，本局改回整张上传：{exception.Message}");
            return false;
        }
    }

    // 小贴图：一轮里每块各用一张(同一帧里先传后拷，不拿同一张来回覆盖)，用多少备多少，至多 MaxChunkUploads 张
    private static void EnsureStaging(int count)
    {
        if (_staging.Length >= count) return;
        int old = _staging.Length;
        Array.Resize(ref _staging, Math.Min(MaxChunkUploads, Math.Max(count, old * 2)));
        for (int i = old; i < _staging.Length; i++)
            _staging[i] = new Texture2D(UploadChunk, UploadChunk, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
    }
}
