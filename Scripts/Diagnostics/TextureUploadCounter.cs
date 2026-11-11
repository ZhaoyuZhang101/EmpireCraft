using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Diagnostics;

// 贴图上传计数(性能诊断日志开着时，每个窗口报一行[贴图上传])：Texture2D.Apply 记一次上传，字节按长宽与格式估；
// SetPixels/SetPixels32 记一次写像素(CPU 那头拷数组)。都按当时最里层的计时段(FrameProfiler.Measure)归：
// 模组自己的画布在各自的计时段名下，原版图层在「原版·updatePixels」名下(见 NativeWorldOptimizationPatch)，都不在的记「计时段外」。
// 补丁头一次打开诊断时才打；之后关了诊断，前缀只做一次判断就返回
public static class TextureUploadCounter
{
    private struct Tally
    {
        internal int Uploads, Writes;
        internal long UploadBytes, WriteBytes;
    }

    private const string Outside = "计时段外";
    private const double Megabyte = 1024d * 1024d;
    private const int MaxLabels = 8;
    private static readonly Dictionary<string, Tally> Tallies = new();
    private static bool _patched;
    // Apply() 里再调 Apply(bool, bool)、SetPixels32(colors) 里再调带 miplevel 的：只记最外层那次
    private static int _depth;

    public static void EnsurePatched()
    {
        if (_patched) return;
        _patched = true;
        var harmony = new Harmony("EmpireCraft.TextureUploadCounter");
        var finalizer = new HarmonyMethod(typeof(TextureUploadCounter), nameof(AfterCall));
        foreach (MethodInfo method in typeof(Texture2D).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.IsGenericMethodDefinition) continue;
            string prefix = method.Name switch
            {
                "Apply" => nameof(BeforeApply),
                "SetPixels32" => nameof(BeforeWrite32),
                "SetPixels" => nameof(BeforeWrite),
                _ => null
            };
            if (prefix == null) continue;
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(TextureUploadCounter), prefix), finalizer: finalizer);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][贴图上传] {method} 计数补丁没打上：{exception.Message}");
            }
        }
    }

    public static void BeforeApply(Texture2D __instance)
    {
        if (_depth++ > 0 || !FrameProfiler.Enabled || __instance == null) return;
        Add(true, (long)__instance.width * __instance.height * BytesPerPixel(__instance.format));
    }

    public static void BeforeWrite32(Color32[] colors)
    {
        if (_depth++ > 0 || !FrameProfiler.Enabled) return;
        Add(false, (colors?.Length ?? 0) * 4L);
    }

    public static void BeforeWrite(Color[] colors)
    {
        if (_depth++ > 0 || !FrameProfiler.Enabled) return;
        Add(false, (colors?.Length ?? 0) * 16L);
    }

    public static Exception AfterCall(Exception __exception)
    {
        if (_depth > 0) _depth--;
        return __exception;
    }

    // 记在哪：最里层计时段，连同外面一层(模组画布里调原版 updatePixels 的，记成"模组那段 › 原版·updatePixels")；拼好的字缓存着
    private static readonly Dictionary<(string outer, string inner), string> Joined = new();

    private static string LabelNow()
    {
        string inner = FrameProfiler.CurrentLabel, outer = FrameProfiler.ParentLabel;
        if (inner == null) return Outside;
        if (outer == null || outer == FrameProfiler.MainLoopLabel) return inner;
        if (!Joined.TryGetValue((outer, inner), out string label))
        {
            if (Joined.Count > 512) Joined.Clear();
            Joined[(outer, inner)] = label = outer + " › " + inner;
        }
        return label;
    }

    private static void Add(bool upload, long bytes)
    {
        string label = LabelNow();
        Tallies.TryGetValue(label, out Tally tally);
        if (upload)
        {
            tally.Uploads++;
            tally.UploadBytes += bytes;
        }
        else
        {
            tally.Writes++;
            tally.WriteBytes += bytes;
        }
        Tallies[label] = tally;
    }

    private static int BytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.Alpha8 or TextureFormat.R8 => 1,
        TextureFormat.RGB565 or TextureFormat.RGBA4444 or TextureFormat.ARGB4444 or TextureFormat.R16 or TextureFormat.RHalf => 2,
        TextureFormat.RGB24 => 3,
        TextureFormat.RGBAHalf or TextureFormat.RGFloat => 8,
        TextureFormat.RGBAFloat => 16,
        _ => 4
    };

    // 一个窗口的汇总：总上传次数与字节(每帧均摊)、总写像素，按字节排前几个计时段；没有就是 null。报完清零
    public static string BuildReport(int frames)
    {
        if (Tallies.Count == 0) return null;
        frames = Math.Max(1, frames);
        int uploads = 0, writes = 0;
        long uploadBytes = 0L, writeBytes = 0L;
        foreach (Tally tally in Tallies.Values)
        {
            uploads += tally.Uploads;
            writes += tally.Writes;
            uploadBytes += tally.UploadBytes;
            writeBytes += tally.WriteBytes;
        }
        var ordered = new List<KeyValuePair<string, Tally>>(Tallies);
        ordered.Sort((a, b) => (b.Value.UploadBytes + b.Value.WriteBytes).CompareTo(a.Value.UploadBytes + a.Value.WriteBytes));
        var line = new StringBuilder();
        line.Append($"[EmpireCraft][贴图上传] 上传 {uploads} 次 {uploadBytes / Megabyte:0.0} MB(每帧 {uploads / (double)frames:0.0} 次 " +
                    $"{uploadBytes / 1024d / frames:0} KB)，写像素 {writes} 次 {writeBytes / Megabyte:0.0} MB；");
        for (int i = 0; i < ordered.Count && i < MaxLabels; i++)
        {
            Tally tally = ordered[i].Value;
            line.Append($"{ordered[i].Key} 上传 {tally.Uploads} 次 {tally.UploadBytes / Megabyte:0.0} MB、写 {tally.Writes} 次 " +
                        $"{tally.WriteBytes / Megabyte:0.0} MB；");
        }
        Tallies.Clear();
        return line.ToString();
    }

    public static void Reset()
    {
        Tallies.Clear();
        _depth = 0;
    }
}
