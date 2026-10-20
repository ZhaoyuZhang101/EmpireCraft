using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 战声的音源与 FMOD 通道(见 BattleSound)。
//   原版工程把 Unity 自带音频整个关了(AudioManager 的 Disable Audio)，AudioSource 放不出声，全部声音走 FMOD；
//   这里用 FMOD 底层接口：读 GameResources/sounds/battle/*.wav(自己先验 PCM 头，再整段交给 FMOD 解成内存采样)，
//   建一个挂在主混音下的通道组，组上串一个低通(镜头越远越闷)，组音量 = 原版音效音量 × 战声自身的淡入淡出。
//   FMOD 全部按名字反射调用(见 Api)：源码不出现 FMOD 的类型、编译不依赖 FMODUnity.dll——手机版 NML 在手机上编译源码，
//   引用不到的程序集会让整个模组编译失败；运行时找不到类型或方法就记一次日志、战声安静停用。
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

    private static object _core, _group, _lowpass;
    private static bool _tried, _ready;
    private static float _lastCutoff = -1f, _lastVolume = -1f;
    private static bool _lastPaused;

    internal static bool Ready => _ready;

    internal static bool HasAmbient(string era) => Ambients.ContainsKey(era);

    internal static int Variants(string key) => Shots.TryGetValue(key, out var list) ? list.Count : 0;

    #region FMOD 反射

    // FMOD 的类型、方法与常量(按名字找，找齐了才用)
    private static class Api
    {
        public static PropertyInfo IsInitialized, CoreSystem;
        public static MethodInfo CreateChannelGroup, GetMasterChannelGroup, CreateDspByType, CreateSound, PlaySound;
        public static MethodInfo GroupGetParent, GroupAddGroup, GroupAddDsp, GroupSetVolume, GroupGetVolume, GroupSetPaused,
            GroupStop, GroupHasHandle;
        public static MethodInfo DspSetParameterFloat;
        public static MethodInfo SoundGetLength, SoundHasHandle;
        public static MethodInfo ChannelSetVolume, ChannelSetPan, ChannelSetPitch, ChannelSetPosition, ChannelSetPaused,
            ChannelIsPlaying, ChannelStop, ChannelHasHandle;
        public static Type ModeType, ExInfoType;
        public static object TimeUnitMs, LowpassType;
        public static int LowpassCutoff, DspHead;
        public static uint ModeOneShot, ModeLoop;

        // 找不到的名字记进 missing；全部找齐返回 true
        public static bool Bind(List<string> missing)
        {
            Type manager = FindType("FMODUnity.RuntimeManager", missing);
            Type core = FindType("FMOD.System", missing);
            Type group = FindType("FMOD.ChannelGroup", missing);
            Type channel = FindType("FMOD.Channel", missing);
            Type sound = FindType("FMOD.Sound", missing);
            Type dsp = FindType("FMOD.DSP", missing);
            Type dspType = FindType("FMOD.DSP_TYPE", missing);
            Type timeUnit = FindType("FMOD.TIMEUNIT", missing);
            ModeType = FindType("FMOD.MODE", missing);
            ExInfoType = FindType("FMOD.CREATESOUNDEXINFO", missing);
            if (missing.Count > 0) return false;

            IsInitialized = manager.GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            CoreSystem = manager.GetProperty("CoreSystem", BindingFlags.Public | BindingFlags.Static);
            if (IsInitialized == null) missing.Add("RuntimeManager.IsInitialized");
            if (CoreSystem == null) missing.Add("RuntimeManager.CoreSystem");

            CreateChannelGroup = Need(core, "createChannelGroup", missing, typeof(string), group.MakeByRefType());
            GetMasterChannelGroup = Need(core, "getMasterChannelGroup", missing, group.MakeByRefType());
            CreateDspByType = Need(core, "createDSPByType", missing, dspType, dsp.MakeByRefType());
            CreateSound = Need(core, "createSound", missing, typeof(byte[]), ModeType, ExInfoType.MakeByRefType(),
                sound.MakeByRefType());
            PlaySound = Need(core, "playSound", missing, sound, group, typeof(bool), channel.MakeByRefType());

            GroupGetParent = Need(group, "getParentGroup", missing, group.MakeByRefType());
            GroupAddGroup = Need(group, "addGroup", missing, group, typeof(bool));
            GroupAddDsp = Need(group, "addDSP", missing, typeof(int), dsp);
            GroupSetVolume = Need(group, "setVolume", missing, typeof(float));
            GroupGetVolume = Need(group, "getVolume", missing, typeof(float).MakeByRefType());
            GroupSetPaused = Need(group, "setPaused", missing, typeof(bool));
            GroupStop = Need(group, "stop", missing);
            GroupHasHandle = Need(group, "hasHandle", missing);

            DspSetParameterFloat = Need(dsp, "setParameterFloat", missing, typeof(int), typeof(float));
            SoundGetLength = Need(sound, "getLength", missing, typeof(uint).MakeByRefType(), timeUnit);
            SoundHasHandle = Need(sound, "hasHandle", missing);

            ChannelSetVolume = Need(channel, "setVolume", missing, typeof(float));
            ChannelSetPan = Need(channel, "setPan", missing, typeof(float));
            ChannelSetPitch = Need(channel, "setPitch", missing, typeof(float));
            ChannelSetPosition = Need(channel, "setPosition", missing, typeof(uint), timeUnit);
            ChannelSetPaused = Need(channel, "setPaused", missing, typeof(bool));
            ChannelIsPlaying = Need(channel, "isPlaying", missing, typeof(bool).MakeByRefType());
            ChannelStop = Need(channel, "stop", missing);
            ChannelHasHandle = Need(channel, "hasHandle", missing);

            TimeUnitMs = EnumValue(timeUnit, "MS", missing);
            LowpassType = EnumValue(dspType, "LOWPASS_SIMPLE", missing);
            ModeOneShot = Flags(missing, "OPENMEMORY", "CREATESAMPLE", "_2D", "LOOP_OFF");
            ModeLoop = Flags(missing, "OPENMEMORY", "CREATESAMPLE", "_2D", "LOOP_NORMAL");
            if (ExInfoType.GetField("cbsize") == null) missing.Add("CREATESOUNDEXINFO.cbsize");
            if (ExInfoType.GetField("length") == null) missing.Add("CREATESOUNDEXINFO.length");

            // 这两个找不到就用 FMOD 2.x 的值：低通的截止频率是 0 号参数，DSP 链的头是 -1
            Type cutoff = FindType("FMOD.DSP_LOWPASS_SIMPLE", null);
            LowpassCutoff = cutoff != null && Enum.IsDefined(cutoff, "CUTOFF") ? Convert.ToInt32(Enum.Parse(cutoff, "CUTOFF")) : 0;
            FieldInfo head = FindType("FMOD.CHANNELCONTROL_DSP_INDEX", null)?.GetField("HEAD");
            DspHead = head != null && head.IsLiteral ? Convert.ToInt32(head.GetRawConstantValue()) : -1;
            return missing.Count == 0;
        }

        private static Type FindType(string fullName, List<string> missing)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch (Exception)
                {
                    continue;
                }
                if (type != null) return type;
            }
            missing?.Add(fullName);
            return null;
        }

        private static MethodInfo Need(Type type, string name, List<string> missing, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, args, null);
            if (method == null) missing.Add(type.Name + "." + name);
            return method;
        }

        private static object EnumValue(Type type, string name, List<string> missing)
        {
            if (Enum.IsDefined(type, name)) return Enum.Parse(type, name);
            missing.Add(type.Name + "." + name);
            return null;
        }

        private static uint Flags(List<string> missing, params string[] names)
        {
            uint flags = 0;
            foreach (string name in names)
            {
                if (Enum.IsDefined(ModeType, name)) flags |= Convert.ToUInt32(Enum.Parse(ModeType, name));
                else missing.Add("MODE." + name);
            }
            return flags;
        }
    }

    private static readonly object[] Args1 = new object[1], Args2 = new object[2], Args4 = new object[4];

    // 调一个返回 FMOD.RESULT 的方法，OK(0) 为真
    private static bool Ok(MethodInfo method, object target, object[] args) =>
        Convert.ToInt32(method.Invoke(target, args)) == 0;

    private static bool Ok1(MethodInfo method, object target, object a)
    {
        Args1[0] = a;
        bool ok = Ok(method, target, Args1);
        Args1[0] = null;
        return ok;
    }

    private static bool Ok2(MethodInfo method, object target, object a, object b)
    {
        Args2[0] = a;
        Args2[1] = b;
        bool ok = Ok(method, target, Args2);
        Args2[0] = Args2[1] = null;
        return ok;
    }

    // 调一个只有一个 out 参数的方法，取出那个值
    private static bool Out1(MethodInfo method, object target, out object value)
    {
        Args1[0] = null;
        bool ok = Ok(method, target, Args1);
        value = Args1[0];
        Args1[0] = null;
        return ok;
    }

    private static bool Valid(MethodInfo hasHandle, object handle) =>
        handle != null && hasHandle.Invoke(handle, null) is bool has && has;

    #endregion

    // FMOD 起来以后读一次；FMOD 还没起来就下帧再试；找不到 FMOD 或读失败就整局不响(只报一次)
    internal static bool EnsureLoaded()
    {
        if (_ready) return true;
        if (_tried) return false;
        if (!_bound)
        {
            _bound = true;
            var missing = new List<string>();
            bool bound;
            try
            {
                bound = Api.Bind(missing);
            }
            catch (Exception error)
            {
                bound = false;
                missing.Add(error.Message);
            }
            if (!bound)
            {
                _tried = true;
                Report("bind", $"找不到 FMOD 接口({string.Join("、", missing)})，战声停用");
                return false;
            }
        }
        try
        {
            if (!(Api.IsInitialized.GetValue(null, null) is bool initialized && initialized)) return false;
        }
        catch (Exception error)
        {
            _tried = true;
            Report("init", $"FMOD 不可用，战声停用：{error.GetBaseException().Message}");
            return false;
        }
        _tried = true;
        var watch = Stopwatch.StartNew();
        try
        {
            _core = Api.CoreSystem.GetValue(null, null);
            Args2[0] = "EmpireCraftBattle";
            Args2[1] = null;
            bool created = Ok(Api.CreateChannelGroup, _core, Args2);
            _group = Args2[1];
            Args2[0] = Args2[1] = null;
            if (!created || !Valid(Api.GroupHasHandle, _group))
            {
                Report("group", "建通道组失败，战声停用");
                return false;
            }
            // 新建的通道组默认挂在主混音下；万一没挂上(没有上级)再手动挂过去
            if ((!Out1(Api.GroupGetParent, _group, out object parent) || !Valid(Api.GroupHasHandle, parent)) &&
                Out1(Api.GetMasterChannelGroup, _core, out object master))
                Ok2(Api.GroupAddGroup, master, _group, true);
            Args2[0] = Api.LowpassType;
            Args2[1] = null;
            bool made = Ok(Api.CreateDspByType, _core, Args2);
            object dsp = Args2[1];
            Args2[0] = Args2[1] = null;
            if (made && dsp != null && Ok2(Api.GroupAddDsp, _group, Api.DspHead, dsp)) _lowpass = dsp;
            Ok1(Api.GroupSetVolume, _group, 0f);

            string dir = Path.Combine(ModClass._declare.FolderPath, "GameResources", Folder.Replace('/', Path.DirectorySeparatorChar));
            int ambients = 0, shots = 0, rejected = 0;
            long bytes = 0;
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.wav");
                Array.Sort(files, StringComparer.Ordinal);
                int exSize = Marshal.SizeOf(Api.ExInfoType);
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
                    // CREATESOUNDEXINFO 是结构体：装箱后改字段(改的是箱里那份)，按 ref 传进去
                    object info = Activator.CreateInstance(Api.ExInfoType);
                    Api.ExInfoType.GetField("cbsize").SetValue(info, exSize);
                    Api.ExInfoType.GetField("length").SetValue(info, (uint)data.Length);
                    Args4[0] = data;
                    Args4[1] = Enum.ToObject(Api.ModeType, ambient ? Api.ModeLoop : Api.ModeOneShot);
                    Args4[2] = info;
                    Args4[3] = null;
                    object result = Api.CreateSound.Invoke(_core, Args4);
                    object sound = Args4[3];
                    Args4[0] = Args4[1] = Args4[2] = Args4[3] = null;
                    if (Convert.ToInt32(result) != 0 || !Valid(Api.SoundHasHandle, sound))
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

    private static bool _bound;

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

    // 先暂停着开播，设好音量、声像、音高、起点再放开，免得第一帧满音量冒一下；失败返回 null
    internal static object Play(object sound, float volume, float pan, float pitch, float startFraction = 0f)
    {
        if (!_ready || sound == null) return null;
        try
        {
            Args4[0] = sound;
            Args4[1] = _group;
            Args4[2] = true;
            Args4[3] = null;
            object result = Api.PlaySound.Invoke(_core, Args4);
            object channel = Args4[3];
            Args4[0] = Args4[1] = Args4[2] = Args4[3] = null;
            if (Convert.ToInt32(result) != 0 || !Valid(Api.ChannelHasHandle, channel))
            {
                CountError("play", result);
                return null;
            }
            Ok1(Api.ChannelSetVolume, channel, volume);
            Ok1(Api.ChannelSetPan, channel, pan);
            if (Mathf.Abs(pitch - 1f) > 0.001f) Ok1(Api.ChannelSetPitch, channel, pitch);
            if (startFraction > 0f)
            {
                Args2[0] = null;
                Args2[1] = Api.TimeUnitMs;
                bool known = Ok(Api.SoundGetLength, sound, Args2);
                uint length = known && Args2[0] is uint value ? value : 0u;
                Args2[0] = Args2[1] = null;
                if (length > 0) Ok2(Api.ChannelSetPosition, channel, (uint)(length * Mathf.Clamp01(startFraction)) % length, Api.TimeUnitMs);
            }
            Ok1(Api.ChannelSetPaused, channel, false);
            return channel;
        }
        catch (Exception error)
        {
            Report("play-ex", $"播放出错：{error.GetBaseException().Message}");
            return null;
        }
    }

    internal static bool Valid(object channel) => _ready && Valid(Api.ChannelHasHandle, channel);

    internal static bool Playing(object channel)
    {
        if (!Valid(channel)) return false;
        Args1[0] = null;
        bool ok = Ok(Api.ChannelIsPlaying, channel, Args1);
        bool playing = ok && Args1[0] is bool value && value;
        Args1[0] = null;
        return playing;
    }

    internal static void SetVolume(object channel, float volume, float pan)
    {
        if (!Valid(channel)) return;
        Ok1(Api.ChannelSetVolume, channel, volume);
        Ok1(Api.ChannelSetPan, channel, pan);
    }

    internal static void Stop(ref object channel)
    {
        if (Valid(channel)) Api.ChannelStop.Invoke(channel, null);
        channel = null;
    }

    // 整组：音量(原版音效音量已乘进去)、低通截止频率、暂停
    internal static void SetGroup(float volume, float cutoff, bool paused)
    {
        if (!_ready) return;
        if (Mathf.Abs(volume - _lastVolume) > 0.0005f)
        {
            Ok1(Api.GroupSetVolume, _group, volume);
            _lastVolume = volume;
        }
        if (_lowpass != null && Mathf.Abs(cutoff - _lastCutoff) > 20f)
        {
            Ok2(Api.DspSetParameterFloat, _lowpass, Api.LowpassCutoff, Mathf.Clamp(cutoff, 200f, 22000f));
            _lastCutoff = cutoff;
        }
        if (paused != _lastPaused)
        {
            Ok1(Api.GroupSetPaused, _group, paused);
            _lastPaused = paused;
        }
    }

    internal static void StopAll()
    {
        if (!_ready) return;
        Api.GroupStop.Invoke(_group, null);
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
        if (!_ready) return _masterFactor;
        try
        {
            if (Out1(Api.GetMasterChannelGroup, _core, out object group) && Out1(Api.GroupGetVolume, group, out object applied) &&
                applied is float value && Mathf.Abs(value - master) < 0.02f)
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
    private static void CountError(string what, object result)
    {
        string key = what + ":" + result;
        ErrorCounts.TryGetValue(key, out int count);
        ErrorCounts[key] = ++count;
        if (count == 1 || count == 10 || count == 100 || count % 1000 == 0)
            NeoModLoader.services.LogService.LogWarning($"{Tag} {what} 失败({result})，累计 {count} 次");
    }
}
