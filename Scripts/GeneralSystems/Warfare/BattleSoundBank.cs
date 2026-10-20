using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战声的音源与 FMOD 通道(见 BattleSound；FMOD 的调用见 FmodBridge，全是反射，源码不出现 FMOD 类型)。
//   读 GameResources/sounds/battle/*.wav(自己先验 PCM 头，再整段交给 FMOD 解成内存采样)，
//   建一个挂在主混音下的通道组，组上串一个低通(镜头越远越闷)，组音量 = 原版音效音量 × 战声自身的淡入淡出。
//   声音、通道在这里只是装箱的 FMOD 句柄(object)，没有就是 null。
//   文件名：<时代>_amb.wav 是环境声(循环)，<时代>_<种类>_<n>.wav 是单发；换成别的素材时照这个规矩命名放进去即可。
internal static class BattleSoundBank
{
    private const string Folder = "sounds/battle";
    private const string Tag = "[EmpireCraft][战声]";

    // 环境声：时代 → 声音；单发：「时代_种类」→ 各变体
    private static readonly Dictionary<string, object> Ambients = new();
    private static readonly Dictionary<string, List<object>> Shots = new();
    private static readonly HashSet<string> Reported = new();
    private static readonly Dictionary<string, int> ErrorCounts = new();

    private static object _group, _lowpass;
    private static bool _tried, _ready;
    private static float _lastCutoff = -1f, _lastVolume = -1f;
    private static bool _lastPaused;

    internal static bool Ready => _ready;

    internal static bool HasAmbient(string era) => Ambients.ContainsKey(era);

    internal static int Variants(string key) => Shots.TryGetValue(key, out var list) ? list.Count : 0;

    // FMOD 起来以后读一次；FMOD 还没起来就下帧再试；找不到 FMOD 或读失败就整局不响(只报一次)
    internal static bool EnsureLoaded()
    {
        if (_ready) return true;
        if (_tried) return false;
        if (!FmodBridge.EnsureReady())
        {
            if (FmodBridge.Failed)
            {
                _tried = true;
                Report("bind", $"{FmodBridge.FailReason}，战声停用");
            }
            return false;
        }
        _tried = true;
        var watch = Stopwatch.StartNew();
        try
        {
            _group = FmodBridge.CreateGroup("EmpireCraftBattle", true, out _lowpass);
            if (_group == null)
            {
                Report("group", "建通道组失败，战声停用");
                return false;
            }

            string dir = Path.Combine(ModClass._declare.FolderPath, "GameResources", Folder.Replace('/', Path.DirectorySeparatorChar));
            int ambients = 0, shots = 0, rejected = 0;
            long bytes = 0;
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.wav");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    string[] parts = name.Split('_');
                    bool ambient = parts.Length == 2 && parts[1] == "amb";
                    if (!ambient && parts.Length != 3)
                    {
                        rejected++;
                        Report("name:" + name, $"文件名不合规矩(要 时代_amb 或 时代_种类_序号)，跳过：{name}.wav");
                        continue;
                    }
                    byte[] data = File.ReadAllBytes(file);
                    if (!CheckWav(data, out string why))
                    {
                        rejected++;
                        Report("wav:" + name, $"{name}.wav 不是 16 位 PCM WAV({why})，跳过");
                        continue;
                    }
                    object sound = FmodBridge.CreateSound(data, ambient, out object result);
                    if (sound == null)
                    {
                        rejected++;
                        Report("fmod:" + name, $"FMOD 读不了 {name}.wav({result})，跳过");
                        continue;
                    }
                    bytes += data.Length;
                    if (ambient)
                    {
                        Ambients[parts[0]] = sound;
                        ambients++;
                    }
                    else
                    {
                        string key = parts[0] + "_" + parts[1];
                        if (!Shots.TryGetValue(key, out var list)) Shots[key] = list = new List<object>();
                        list.Add(sound);
                        shots++;
                    }
                }
            }
            _ready = ambients + shots > 0;
            NeoModLoader.services.LogService.LogInfo(
                $"{Tag} 读入 {ambients} 段环境声、{shots} 段单发({Shots.Count} 类)，共 {bytes / 1024} KB，" +
                $"跳过 {rejected} 个，用时 {watch.ElapsedMilliseconds} ms{(_lowpass != null ? "" : "；低通没挂上，远近只调音量")}" +
                (_ready ? "" : $"；{dir} 下没有可用的音频，战声不响"));
        }
        catch (Exception error)
        {
            _ready = false;
            Report("load", $"读音频出错，战声停用：{error}");
        }
        return _ready;
    }

    // 验 RIFF/WAVE 头：要有 fmt 块(PCM、1~2 声道、16 位、8~48 kHz)和非空的 data 块
    private static bool CheckWav(byte[] data, out string why)
    {
        why = "";
        if (data.Length < 44 || ReadTag(data, 0) != "RIFF" || ReadTag(data, 8) != "WAVE")
        {
            why = "没有 RIFF/WAVE 头";
            return false;
        }
        bool fmt = false;
        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            string id = ReadTag(data, pos);
            int size = BitConverter.ToInt32(data, pos + 4);
            if (size < 0 || pos + 8 + (long)size > data.Length + 1L)
            {
                why = $"{id} 块长度不对";
                return false;
            }
            if (id == "fmt ")
            {
                if (size < 16)
                {
                    why = "fmt 块太短";
                    return false;
                }
                int format = BitConverter.ToUInt16(data, pos + 8);
                int channels = BitConverter.ToUInt16(data, pos + 10);
                int rate = BitConverter.ToInt32(data, pos + 12);
                int bits = BitConverter.ToUInt16(data, pos + 22);
                if (format != 1 || bits != 16 || channels < 1 || channels > 2 || rate < 8000 || rate > 48000)
                {
                    why = $"格式 {format}、{channels} 声道、{rate} Hz、{bits} 位";
                    return false;
                }
                fmt = true;
            }
            else if (id == "data")
            {
                if (!fmt)
                {
                    why = "data 块在 fmt 块前面";
                    return false;
                }
                if (size <= 0)
                {
                    why = "data 块是空的";
                    return false;
                }
                return true;
            }
            pos += 8 + size + (size & 1);
        }
        why = "找不到 data 块";
        return false;
    }

    private static string ReadTag(byte[] data, int at) =>
        at + 4 > data.Length ? "" : new string(new[] { (char)data[at], (char)data[at + 1], (char)data[at + 2], (char)data[at + 3] });

    internal static object Ambient(string era) => Ambients.TryGetValue(era, out var sound) ? sound : null;

    internal static object Shot(string key, int index) =>
        Shots.TryGetValue(key, out var list) && index >= 0 && index < list.Count ? list[index] : null;

    // 先暂停着开播，设好音量、声像、音高、起点再放开；失败返回 null
    internal static object Play(object sound, float volume, float pan, float pitch, float startFraction = 0f)
    {
        if (!_ready || sound == null) return null;
        try
        {
            object channel = FmodBridge.Play(sound, _group, volume, pan, pitch, startFraction, out object result);
            if (channel == null) CountError("play", result);
            return channel;
        }
        catch (Exception error)
        {
            Report("play-ex", $"播放出错：{error.GetBaseException().Message}");
            return null;
        }
    }

    internal static bool Valid(object channel) => _ready && FmodBridge.Valid(channel);

    internal static bool Playing(object channel) => _ready && FmodBridge.Playing(channel);

    internal static void SetVolume(object channel, float volume, float pan)
    {
        if (!Valid(channel)) return;
        FmodBridge.SetVolume(channel, volume);
        FmodBridge.SetPan(channel, pan);
    }

    internal static void Stop(ref object channel)
    {
        FmodBridge.Stop(channel);
        channel = null;
    }

    // 整组：音量(原版音效音量已乘进去)、低通截止频率、暂停
    internal static void SetGroup(float volume, float cutoff, bool paused)
    {
        if (!_ready) return;
        if (Mathf.Abs(volume - _lastVolume) > 0.0005f)
        {
            FmodBridge.GroupVolume(_group, volume);
            _lastVolume = volume;
        }
        if (_lowpass != null && Mathf.Abs(cutoff - _lastCutoff) > 20f)
        {
            FmodBridge.LowpassCutoff(_lowpass, cutoff);
            _lastCutoff = cutoff;
        }
        if (paused != _lastPaused)
        {
            FmodBridge.GroupPaused(_group, paused);
            _lastPaused = paused;
        }
    }

    internal static void StopAll()
    {
        if (_ready) FmodBridge.GroupStop(_group);
    }

    internal static float MasterFactor() => FmodBridge.MasterFactor();

    internal static void Report(string key, string message)
    {
        if (Reported.Add(key)) NeoModLoader.services.LogService.LogWarning($"{Tag} {message}");
    }

    // 同类错误只报第 1、10、100……次
    private static void CountError(string what, object result)
    {
        string key = what + ":" + result;
        ErrorCounts.TryGetValue(key, out int count);
        ErrorCounts[key] = ++count;
        if (count == 1 || count == 10 || count == 100 || count % 1000 == 0)
            NeoModLoader.services.LogService.LogWarning($"{Tag} {what} 失败({result})，累计 {count} 次");
    }
}
