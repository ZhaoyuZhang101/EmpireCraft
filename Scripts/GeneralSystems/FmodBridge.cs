using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// FMOD 底层接口的反射桥(战声 BattleSoundBank、文明乐 CivMusic 共用)。
//   原版工程把 Unity 自带音频整个关了(AudioManager 的 Disable Audio)，AudioSource 放不出声，全部声音走 FMOD。
//   FMOD 全部按名字反射调用：源码不出现 FMOD 的类型、编译不依赖 FMODUnity.dll——手机版 NML 在手机上编译源码，
//   引用不到的程序集会让整个模组编译失败；运行时找不到类型或方法就记一次日志、相关功能安静停用。
//   声音、通道、通道组、DSP 在这里都是装箱的 FMOD 句柄(object)，没有就是 null；方法返回的 FMOD.RESULT 按 0 = OK 判。
internal static class FmodBridge
{
    // 0 还没绑定，1 绑定好了、等 FMOD 起来，2 可用，-1 不可用
    private static int _state;
    private static object _core;

    internal static bool Failed => _state < 0;
    internal static string FailReason { get; private set; } = "";

    #region 绑定

    // FMOD 的类型、方法与常量(按名字找，找齐了才用)
    private static class Api
    {
        public static PropertyInfo IsInitialized, CoreSystem;
        public static MethodInfo CreateChannelGroup, GetMasterChannelGroup, CreateDspByType, CreateSound, CreateSoundPath, PlaySound;
        public static MethodInfo GroupGetParent, GroupAddGroup, GroupAddDsp, GroupSetVolume, GroupGetVolume, GroupSetPaused,
            GroupStop, GroupHasHandle;
        public static MethodInfo DspSetParameterFloat;
        public static MethodInfo SoundGetLength, SoundHasHandle;
        public static MethodInfo ChannelSetVolume, ChannelSetPan, ChannelSetPitch, ChannelSetPosition, ChannelSetPaused,
            ChannelIsPlaying, ChannelStop, ChannelHasHandle;
        // 可选：读播放位置(文明乐续播用)，没有就从头放
        public static MethodInfo ChannelGetPosition;
        public static Type ModeType, ExInfoType;
        public static object TimeUnitMs, LowpassType;
        public static int LowpassCutoff, DspHead;
        public static uint ModeSample, ModeSampleLoop, ModeFileStreamLoop;

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
            CreateSoundPath = Need(core, "createSound", missing, typeof(string), ModeType, sound.MakeByRefType());
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
            ChannelGetPosition = channel.GetMethod("getPosition", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(uint).MakeByRefType(), timeUnit }, null);

            TimeUnitMs = EnumValue(timeUnit, "MS", missing);
            LowpassType = EnumValue(dspType, "LOWPASS_SIMPLE", missing);
            // 单发与环境声从内存解成采样(OPENMEMORY 配 CREATESAMPLE 时 FMOD 会复制一份，原数组可以丢)；
            // 整首曲子从文件流式解码(ogg 留不了压缩的内存采样；从内存流的话 FMOD 一直读我们的数组，托管数组会被挪走)
            ModeSample = Flags(missing, "OPENMEMORY", "CREATESAMPLE", "_2D", "LOOP_OFF");
            ModeSampleLoop = Flags(missing, "OPENMEMORY", "CREATESAMPLE", "_2D", "LOOP_NORMAL");
            ModeFileStreamLoop = Flags(missing, "CREATESTREAM", "_2D", "LOOP_NORMAL");
            if (ExInfoType.GetField("cbsize") == null) missing.Add("CREATESOUNDEXINFO.cbsize");
            if (ExInfoType.GetField("length") == null) missing.Add("CREATESOUNDEXINFO.length");

            // 这两个找不到就用 FMOD 2.x 的值：低通的截止频率是 0 号参数，DSP 链的头是 -1
            Type cutoff = FindType("FMOD.DSP_LOWPASS_SIMPLE", null);
            LowpassCutoff = cutoff != null && Enum.IsDefined(cutoff, "CUTOFF") ? Convert.ToInt32(Enum.Parse(cutoff, "CUTOFF")) : 0;
            FieldInfo head = FindType("FMOD.CHANNELCONTROL_DSP_INDEX", null)?.GetField("HEAD");
            DspHead = head != null && head.IsLiteral ? Convert.ToInt32(head.GetRawConstantValue()) : -1;
            return missing.Count == 0;
        }

        private static MethodInfo Need(Type type, string name, List<string> missing,
            params Type[] args)
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

    internal static Type FindType(string fullName, List<string> missing)
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

    // FMOD 可用返回 true；还没起来返回 false(下帧再试)；永久不可用时 Failed 为真、FailReason 写明原因
    internal static bool EnsureReady()
    {
        if (_state == 2) return true;
        if (_state < 0) return false;
        if (_state == 0)
        {
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
                _state = -1;
                FailReason = $"找不到 FMOD 接口({string.Join("、", missing)})";
                return false;
            }
            _state = 1;
        }
        try
        {
            if (!(Api.IsInitialized.GetValue(null, null) is bool initialized && initialized)) return false;
            _core = Api.CoreSystem.GetValue(null, null);
            _state = 2;
            return true;
        }
        catch (Exception error)
        {
            _state = -1;
            FailReason = $"FMOD 不可用：{error.GetBaseException().Message}";
            return false;
        }
    }

    #endregion

    #region 调用

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

    private static bool Has(MethodInfo hasHandle, object handle) =>
        handle != null && hasHandle.Invoke(handle, null) is bool has && has;

    // 建一个挂在主混音下的通道组(音量先置 0)；lowpass 为真时在组头串一个低通，挂不上就是 null
    internal static object CreateGroup(string name, bool lowpass, out object dsp)
    {
        dsp = null;
        Args2[0] = name;
        Args2[1] = null;
        bool created = Ok(Api.CreateChannelGroup, _core, Args2);
        object group = Args2[1];
        Args2[0] = Args2[1] = null;
        if (!created || !Has(Api.GroupHasHandle, group)) return null;
        // 新建的通道组默认挂在主混音下；万一没挂上(没有上级)再手动挂过去
        if ((!Out1(Api.GroupGetParent, group, out object parent) || !Has(Api.GroupHasHandle, parent)) &&
            Out1(Api.GetMasterChannelGroup, _core, out object master))
            Ok2(Api.GroupAddGroup, master, group, true);
        if (lowpass)
        {
            Args2[0] = Api.LowpassType;
            Args2[1] = null;
            bool made = Ok(Api.CreateDspByType, _core, Args2);
            object madeDsp = Args2[1];
            Args2[0] = Args2[1] = null;
            if (made && madeDsp != null && Ok2(Api.GroupAddDsp, group, Api.DspHead, madeDsp)) dsp = madeDsp;
        }
        Ok1(Api.GroupSetVolume, group, 0f);
        return group;
    }

    // 从文件建一路循环的流(整首曲子)；失败返回 null，result 是 FMOD 的返回值
    internal static object CreateStream(string path, out object result)
    {
        object[] args = { path, Enum.ToObject(Api.ModeType, Api.ModeFileStreamLoop), null };
        result = Api.CreateSoundPath.Invoke(_core, args);
        object sound = args[2];
        return Convert.ToInt32(result) == 0 && Has(Api.SoundHasHandle, sound) ? sound : null;
    }

    // 声音长度(毫秒)，读不到是 0
    internal static uint LengthMs(object sound)
    {
        if (_state != 2 || sound == null) return 0u;
        Args2[0] = null;
        Args2[1] = Api.TimeUnitMs;
        bool known = Ok(Api.SoundGetLength, sound, Args2);
        uint length = known && Args2[0] is uint value ? value : 0u;
        Args2[0] = Args2[1] = null;
        return length;
    }

    // 从内存建声音(解成内存采样)；失败返回 null，result 是 FMOD 的返回值
    internal static object CreateSound(byte[] data, bool loop, out object result)
    {
        // CREATESOUNDEXINFO 是结构体：装箱后改字段(改的是箱里那份)，按 ref 传进去
        object info = Activator.CreateInstance(Api.ExInfoType);
        Api.ExInfoType.GetField("cbsize").SetValue(info, Marshal.SizeOf(Api.ExInfoType));
        Api.ExInfoType.GetField("length").SetValue(info, (uint)data.Length);
        uint mode = loop ? Api.ModeSampleLoop : Api.ModeSample;
        Args4[0] = data;
        Args4[1] = Enum.ToObject(Api.ModeType, mode);
        Args4[2] = info;
        Args4[3] = null;
        result = Api.CreateSound.Invoke(_core, Args4);
        object sound = Args4[3];
        Args4[0] = Args4[1] = Args4[2] = Args4[3] = null;
        return Convert.ToInt32(result) == 0 && Has(Api.SoundHasHandle, sound) ? sound : null;
    }

    // 先暂停着开播，设好音量、声像、音高、起点再放开，免得第一帧满音量冒一下；失败返回 null
    internal static object Play(object sound, object group, float volume, float pan, float pitch, float startFraction,
        out object result)
    {
        result = null;
        if (_state != 2 || sound == null || group == null) return null;
        Args4[0] = sound;
        Args4[1] = group;
        Args4[2] = true;
        Args4[3] = null;
        result = Api.PlaySound.Invoke(_core, Args4);
        object channel = Args4[3];
        Args4[0] = Args4[1] = Args4[2] = Args4[3] = null;
        if (Convert.ToInt32(result) != 0 || !Has(Api.ChannelHasHandle, channel)) return null;
        Ok1(Api.ChannelSetVolume, channel, volume);
        Ok1(Api.ChannelSetPan, channel, pan);
        if (Mathf.Abs(pitch - 1f) > 0.001f) Ok1(Api.ChannelSetPitch, channel, pitch);
        if (startFraction > 0f)
        {
            uint length = LengthMs(sound);
            if (length > 0) Ok2(Api.ChannelSetPosition, channel, (uint)(length * Mathf.Clamp01(startFraction)) % length, Api.TimeUnitMs);
        }
        Ok1(Api.ChannelSetPaused, channel, false);
        return channel;
    }

    internal static bool Valid(object channel) => _state == 2 && Has(Api.ChannelHasHandle, channel);

    internal static bool Playing(object channel)
    {
        if (!Valid(channel)) return false;
        Args1[0] = null;
        bool ok = Ok(Api.ChannelIsPlaying, channel, Args1);
        bool playing = ok && Args1[0] is bool value && value;
        Args1[0] = null;
        return playing;
    }

    // 放到哪了(毫秒)；读不到返回 false
    internal static bool TryPositionMs(object channel, out uint ms)
    {
        ms = 0u;
        if (Api.ChannelGetPosition == null || !Valid(channel)) return false;
        Args2[0] = null;
        Args2[1] = Api.TimeUnitMs;
        bool ok = Ok(Api.ChannelGetPosition, channel, Args2);
        if (ok && Args2[0] is uint value) ms = value;
        else ok = false;
        Args2[0] = Args2[1] = null;
        return ok;
    }

    internal static void Seek(object channel, uint ms)
    {
        if (Valid(channel)) Ok2(Api.ChannelSetPosition, channel, ms, Api.TimeUnitMs);
    }

    internal static void SetVolume(object channel, float volume)
    {
        if (Valid(channel)) Ok1(Api.ChannelSetVolume, channel, volume);
    }

    internal static void SetPan(object channel, float pan)
    {
        if (Valid(channel)) Ok1(Api.ChannelSetPan, channel, pan);
    }

    internal static void Stop(object channel)
    {
        if (Valid(channel)) Api.ChannelStop.Invoke(channel, null);
    }

    internal static void GroupVolume(object group, float volume)
    {
        if (_state == 2 && group != null) Ok1(Api.GroupSetVolume, group, volume);
    }

    internal static void GroupPaused(object group, bool paused)
    {
        if (_state == 2 && group != null) Ok1(Api.GroupSetPaused, group, paused);
    }

    internal static void GroupStop(object group)
    {
        if (_state == 2 && group != null) Api.GroupStop.Invoke(group, null);
    }

    internal static void LowpassCutoff(object dsp, float hz)
    {
        if (_state == 2 && dsp != null) Ok2(Api.DspSetParameterFloat, dsp, Api.LowpassCutoff, Mathf.Clamp(hz, 200f, 22000f));
    }

    // 原版的主音量是不是已经作用在主混音上(原版用 FMOD Studio 的主总线调主音量，那就是我们各组的上级)：
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
        if (_state != 2) return _masterFactor;
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

    #endregion

    #region 原版背景音乐

    // 原版背景音乐是 FMOD Studio 的一个事件实例(MusicBox._music_event)，原版只调它的 VCA 音量、从不动事件自己的音量，
    // 这里乘一个 0~1 的系数做淡入淡出，不跟原版的音量设置打架
    private static int _musicState;
    private static FieldInfo _musicEventField;
    private static MethodInfo _eventSetVolume, _eventIsValid;

    // 成功返回 true；找不到接口返回 false(只在第一次失败时给出原因)
    internal static bool SetVanillaMusicVolume(float volume, out string why)
    {
        why = null;
        if (_musicState < 0) return false;
        if (_musicState == 0)
        {
            _musicState = -1;
            _musicEventField = typeof(MusicBox).GetField("_music_event", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Type eventType = _musicEventField?.FieldType;
            _eventSetVolume = eventType?.GetMethod("setVolume", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            _eventIsValid = eventType?.GetMethod("isValid", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (_musicEventField == null || _eventSetVolume == null)
            {
                why = "找不到 MusicBox._music_event 或其 setVolume";
                return false;
            }
            _musicState = 1;
        }
        if (MusicBox.inst == null) return false;
        object instance = _musicEventField.GetValue(MusicBox.inst);
        if (instance == null || _eventIsValid != null && !(_eventIsValid.Invoke(instance, null) is bool valid && valid)) return false;
        Args1[0] = Mathf.Clamp01(volume);
        bool ok = Ok(_eventSetVolume, instance, Args1);
        Args1[0] = null;
        return ok;
    }

    #endregion
}
