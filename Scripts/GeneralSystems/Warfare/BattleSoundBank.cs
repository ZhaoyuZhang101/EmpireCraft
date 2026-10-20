using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战声的音源与 FMOD 通道(见 BattleSound)。
//   原版工程把 Unity 自带音频整个关了(AudioManager 的 Disable Audio)，AudioSource 放不出声，全部声音走 FMOD；
//   这里用 FMOD 底层接口：读 GameResources/sounds/battle/*.wav(自己先验 PCM 头，再整段交给 FMOD 解成内存采样)，
//   建一个挂在主混音下的通道组，组上串一个低通(镜头越远越闷)，组音量 = 原版音效音量 × 战声自身的淡入淡出。
//   文件名：<时代>_amb.wav 是环境声(循环)，<时代>_<种类>_<n>.wav 是单发；换成别的素材时照这个规矩命名放进去即可。
internal static class BattleSoundBank
{
    private const string Folder = "sounds/battle";
    private const string Tag = "[EmpireCraft][战声]";

    // 环境声：时代 → 声音；单发：「时代_种类」→ 各变体
    private static readonly Dictionary<string, FMOD.Sound> Ambients = new();
    private static readonly Dictionary<string, List<FMOD.Sound>> Shots = new();
    private static readonly HashSet<string> Reported = new();
    private static readonly Dictionary<string, int> ErrorCounts = new();

    private static FMOD.ChannelGroup _group;
    private static FMOD.DSP _lowpass;
    private static bool _hasLowpass;
    private static bool _tried, _ready;
    private static float _lastCutoff = -1f, _lastVolume = -1f;
    private static bool _lastPaused;

    internal static bool Ready => _ready;

    internal static bool HasAmbient(string era) => Ambients.ContainsKey(era);

    internal static int Variants(string key) => Shots.TryGetValue(key, out var list) ? list.Count : 0;

    // FMOD 起来以后读一次；FMOD 还没起来就下帧再试，读失败就整局不响(只报一次)
    internal static bool EnsureLoaded()
    {
        if (_ready) return true;
        if (_tried) return false;
        try
        {
            if (!FMODUnity.RuntimeManager.IsInitialized) return false;
        }
        catch (Exception error)
        {
            _tried = true;
            Report("init", $"FMOD 不可用，战声关闭：{error.Message}");
            return false;
        }
        _tried = true;
        var watch = Stopwatch.StartNew();
        try
        {
            FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;
            FMOD.RESULT result = core.createChannelGroup("EmpireCraftBattle", out _group);
            if (result != FMOD.RESULT.OK)
            {
                Report("group", $"建通道组失败({result})，战声关闭");
                return false;
            }
            // 新建的通道组默认挂在主混音下；万一没挂上(没有上级)再手动挂过去
            if ((_group.getParentGroup(out FMOD.ChannelGroup parent) != FMOD.RESULT.OK || !parent.hasHandle()) &&
                core.getMasterChannelGroup(out FMOD.ChannelGroup master) == FMOD.RESULT.OK)
                master.addGroup(_group);
            if (core.createDSPByType(FMOD.DSP_TYPE.LOWPASS_SIMPLE, out _lowpass) == FMOD.RESULT.OK &&
                _group.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, _lowpass) == FMOD.RESULT.OK)
                _hasLowpass = true;
            _group.setVolume(0f);

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
                    var info = new FMOD.CREATESOUNDEXINFO
                    {
                        cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),
                        length = (uint)data.Length
                    };
                    FMOD.MODE mode = FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D |
                                     (ambient ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.LOOP_OFF);
                    result = core.createSound(data, mode, ref info, out FMOD.Sound sound);
                    if (result != FMOD.RESULT.OK)
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
                        if (!Shots.TryGetValue(key, out var list)) Shots[key] = list = new List<FMOD.Sound>();
                        list.Add(sound);
                        shots++;
                    }
                }
            }
            _ready = ambients + shots > 0;
            NeoModLoader.services.LogService.LogInfo(
                $"{Tag} 读入 {ambients} 段环境声、{shots} 段单发({Shots.Count} 类)，共 {bytes / 1024} KB，" +
                $"跳过 {rejected} 个，用时 {watch.ElapsedMilliseconds} ms{(_hasLowpass ? "" : "；低通没挂上，远近只调音量")}" +
                (_ready ? "" : $"；{dir} 下没有可用的音频，战声不响"));
        }
        catch (Exception error)
        {
            _ready = false;
            Report("load", $"读音频出错，战声关闭：{error}");
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

    internal static FMOD.Sound Ambient(string era) => Ambients.TryGetValue(era, out var sound) ? sound : default;

    internal static FMOD.Sound Shot(string key, int index) =>
        Shots.TryGetValue(key, out var list) && index >= 0 && index < list.Count ? list[index] : default;

    // 先暂停着开播，设好音量、声像、音高、起点再放开，免得第一帧满音量冒一下
    internal static FMOD.Channel Play(FMOD.Sound sound, float volume, float pan, float pitch, float startFraction = 0f)
    {
        if (!_ready || !sound.hasHandle()) return default;
        try
        {
            FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;
            FMOD.RESULT result = core.playSound(sound, _group, true, out FMOD.Channel channel);
            if (result != FMOD.RESULT.OK)
            {
                CountError("play", result);
                return default;
            }
            channel.setVolume(volume);
            channel.setPan(pan);
            if (Mathf.Abs(pitch - 1f) > 0.001f) channel.setPitch(pitch);
            if (startFraction > 0f && sound.getLength(out uint length, FMOD.TIMEUNIT.MS) == FMOD.RESULT.OK && length > 0)
                channel.setPosition((uint)(length * Mathf.Clamp01(startFraction)) % length, FMOD.TIMEUNIT.MS);
            channel.setPaused(false);
            return channel;
        }
        catch (Exception error)
        {
            Report("play-ex", $"播放出错：{error.Message}");
            return default;
        }
    }

    internal static bool Playing(FMOD.Channel channel) =>
        channel.hasHandle() && channel.isPlaying(out bool playing) == FMOD.RESULT.OK && playing;

    internal static void SetVolume(FMOD.Channel channel, float volume, float pan)
    {
        if (!channel.hasHandle()) return;
        channel.setVolume(volume);
        channel.setPan(pan);
    }

    internal static void Stop(ref FMOD.Channel channel)
    {
        if (channel.hasHandle()) channel.stop();
        channel = default;
    }

    // 整组：音量(原版音效音量已乘进去)、低通截止频率、暂停
    internal static void SetGroup(float volume, float cutoff, bool paused)
    {
        if (!_ready) return;
        if (Mathf.Abs(volume - _lastVolume) > 0.0005f)
        {
            _group.setVolume(volume);
            _lastVolume = volume;
        }
        if (_hasLowpass && Mathf.Abs(cutoff - _lastCutoff) > 20f)
        {
            _lowpass.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, Mathf.Clamp(cutoff, 200f, 22000f));
            _lastCutoff = cutoff;
        }
        if (paused != _lastPaused)
        {
            _group.setPaused(paused);
            _lastPaused = paused;
        }
    }

    internal static void StopAll()
    {
        if (!_ready) return;
        _group.stop();
    }

    // 原版的主音量是不是已经作用在主混音上(原版用 FMOD Studio 的主总线调主音量，那就是我们这组的上级)：
    // 主混音的音量正好等于主音量设置时就不再乘一遍，否则自己乘上
    private static float _masterCheckedAt = -10f, _masterFactor = 1f;

    internal static float MasterFactor()
    {
        if (Time.unscaledTime - _masterCheckedAt < 1f) return _masterFactor;
        _masterCheckedAt = Time.unscaledTime;
        float master = Mathf.Clamp01(PlayerConfig.getIntValue("volume_master_sound") / 100f);
        _masterFactor = master;
        if (master >= 0.999f)
        {
            _masterFactor = 1f;
            return _masterFactor;
        }
        try
        {
            if (FMODUnity.RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK &&
                group.getVolume(out float applied) == FMOD.RESULT.OK && Mathf.Abs(applied - master) < 0.02f)
                _masterFactor = 1f;
        }
        catch (Exception)
        {
            // 读不到就按自己乘
        }
        return _masterFactor;
    }

    internal static void Report(string key, string message)
    {
        if (Reported.Add(key)) NeoModLoader.services.LogService.LogWarning($"{Tag} {message}");
    }

    // 同类错误只报第 1、10、100……次
    private static void CountError(string what, FMOD.RESULT result)
    {
        string key = what + ":" + result;
        ErrorCounts.TryGetValue(key, out int count);
        ErrorCounts[key] = ++count;
        if (count == 1 || count == 10 || count == 100 || count % 1000 == 0)
            NeoModLoader.services.LogService.LogWarning($"{Tag} {what} 失败({result})，累计 {count} 次");
    }
}
