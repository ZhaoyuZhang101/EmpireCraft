using System;
using System.Collections.Generic;
using System.IO;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GodPowers;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 文明乐：镜头拉近到某一系文明的地盘时，缓入循环播放那一系的曲子，原版背景音乐同时淡出；拉远或离开就反过来。
//   曲子：Music/civ/<文化系>_<n>.ogg(八系：huaxia 华夏、japan 日本、western 西方、norse 北欧、arab 阿拉伯、steppe 草原、
//     india 印度、america 美洲；出处见 Docs/CREDITS.md，加工见 Tools/SoundForge/civ_music_forge.py)。
//     不放 GameResources：NML 会把那里除图片、wav 以外的文件全当文本读进内存。
//     用时才从文件开一路流(FMOD 的流，不整首读进内存)，开过的留着下次用。
//   哪一系：每半秒看屏幕中心和上下左右四点(各离中心四分之一屏)所在地块的城——城的主流文化，没有就取所属国的文化；
//     中心算两票，同一系拿到三票以上才算(在边界上不来回跳)。文化 → 系见 Family。
//   镜头：拉近到中等缩放以内才放(比战声宽)，进出带一点回差；拉到看得见大半张图就不放。
//   滞后：新的结果(换一系、或者没有)要在屏幕中心持续 SettleSeconds 秒才换，免得扫过边界时来回抖。
//   切换：新曲淡入(FadeInSeconds)、旧曲淡出(FadeOutSeconds)，原版背景音乐同时淡出/淡回；响度走 Gain 的 S 形曲线，
//     从无声慢慢起来、收尾不顿(用户 2026-10-22 嫌渐入渐出突兀)。
//   续播：淡出停下时记下这首放到哪，再进这一系接着放上次那首、从停下的地方续上(用户嫌每次拉近都从头放)；
//     同一系有两首的，一首放完一遍才换另一首。
//   暂停、开窗口照常放(原版背景音乐也是)；与战声各用各的通道组，互不影响。
//   音量 = 原版音乐音量(× 主音量) × Level；显示栏"文明乐"开关(玩家设置，默认开，见 SwitchCivMusicButton)。
//   原版背景音乐的淡出：乘在原版音乐事件自己的音量上(原版只调音乐 VCA，不动它，见 FmodBridge.SetVanillaMusicVolume)。
public static class CivMusic
{
    private const string Tag = "[EmpireCraft][文明乐]";
    private const string Folder = "Music/civ";
    private const float Level = 0.8f;
    private const float SampleSeconds = 0.5f;
    private const float SettleSeconds = 3f;
    private const float FadeInSeconds = 7f, FadeOutSeconds = 5f;
    // 正交尺寸(半个屏幕高多少格)：小于 EnterOrtho 开始放，大于 LeaveOrtho 不放；小地图按最大缩放的比例收紧
    private const float EnterOrtho = 200f, LeaveOrtho = 240f;
    private const int MaxVoices = 3;

    private sealed class Voice
    {
        public string Family, Track;
        public object Channel;
        public float Volume, Target, StartedAt, LengthSeconds;
        // 放完一遍、换了下一首：下次从头放，不记位置
        public bool Finished;
    }

    private static readonly Dictionary<string, List<string>> Tracks = new();
    private static readonly Dictionary<string, object> Streams = new();
    // 系 → 上次在放的那首；曲子 → 淡出停下时放到哪(毫秒)
    private static readonly Dictionary<string, string> Current = new();
    private static readonly Dictionary<string, uint> Positions = new();
    private static readonly List<Voice> Voices = new();
    private static readonly HashSet<string> Reported = new();
    private static readonly Dictionary<string, int> Votes = new();

    private static GameObject _host;
    private static object _group;
    private static bool _tried, _ready, _zoomedIn, _announced, _crashed, _vanillaBroken;
    private static string _target, _candidate;
    private static float _candidateSince, _sampleAt = -10f, _vanilla = 1f, _lastGroupVolume = -1f, _lastVanillaSent = -1f,
        _vanillaSentAt = -10f;

    internal static void EnsureHost()
    {
        if (_host != null) return;
        _host = new GameObject("EmpireCraftCivMusic", typeof(CivMusicHost));
        UnityEngine.Object.DontDestroyOnLoad(_host);
    }

    internal static void Tick()
    {
        if (_crashed) return;
        try
        {
            Update(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }
        catch (Exception error)
        {
            // 出错就整局停掉、把原版音乐还回去，免得每帧刷日志
            _crashed = true;
            Report("tick", $"更新出错，本局不再播放：{error}");
            try
            {
                foreach (Voice voice in Voices) FmodBridge.Stop(voice.Channel);
                Voices.Clear();
                FmodBridge.SetVanillaMusicVolume(1f, out _);
            }
            catch (Exception)
            {
                // 已经在报错路径上
            }
        }
    }

    private static void Update(float dt)
    {
        bool on = SwitchCivMusicButton.On && MusicBox.music_on;
        bool world = World.world != null && Config.game_loaded && !SmoothLoader.isLoading() && World.world.camera != null;
        if (!on && Voices.Count == 0 && _vanilla >= 1f) return;
        if (!EnsureLoaded()) return;

        float now = Time.unscaledTime;
        if (!on || !world)
        {
            // 关了开关、退出世界、读档：马上转向"没有"，不等滞后
            _target = _candidate = null;
        }
        else if (now - _sampleAt >= SampleSeconds || now < _sampleAt)
        {
            _sampleAt = now;
            string sampled = Sample(World.world.camera);
            if (sampled != _candidate)
            {
                _candidate = sampled;
                _candidateSince = now;
            }
            if (_candidate != _target && now - _candidateSince >= SettleSeconds) _target = _candidate;
        }

        Steer(now);
        Fade(dt, now);

        float music = Mathf.Clamp01(PlayerConfig.getIntValue("volume_music") / 100f);
        float volume = music * FmodBridge.MasterFactor() * Level;
        if (Mathf.Abs(volume - _lastGroupVolume) > 0.0005f)
        {
            FmodBridge.GroupVolume(_group, volume);
            _lastGroupVolume = volume;
        }

        // 原版背景音乐：文明乐真放起来了就淡到 0，没有就淡回
        float vanillaTarget = Active != null ? 0f : 1f;
        _vanilla = Mathf.MoveTowards(_vanilla, vanillaTarget, dt / (vanillaTarget > _vanilla ? FadeInSeconds : FadeOutSeconds));
        // 变了就发(至多每 0.05 秒一次)；压着的时候每两秒补发一次(原版万一重建了音乐事件，新的那个也压住)
        bool changed = Mathf.Abs(_vanilla - _lastVanillaSent) > 0.002f;
        if (!_vanillaBroken && (changed && now - _vanillaSentAt >= 0.05f || _vanilla < 1f && now - _vanillaSentAt > 2f ||
                                changed && _vanilla >= 1f))
        {
            _vanillaSentAt = now;
            if (FmodBridge.SetVanillaMusicVolume(Gain(_vanilla), out string why)) _lastVanillaSent = _vanilla;
            else if (why != null)
            {
                _vanillaBroken = true;
                Report("vanilla", $"{why}，原版背景音乐不会淡出，与文明乐叠着放");
            }
        }
    }

    #region 读曲目

    private static bool EnsureLoaded()
    {
        if (_ready) return true;
        if (_tried) return false;
        if (!FmodBridge.EnsureReady())
        {
            if (FmodBridge.Failed)
            {
                _tried = true;
                Report("bind", $"{FmodBridge.FailReason}，文明乐停用");
            }
            return false;
        }
        _tried = true;
        try
        {
            _group = FmodBridge.CreateGroup("EmpireCraftCivMusic", false, out _);
            if (_group == null)
            {
                Report("group", "建通道组失败，文明乐停用");
                return false;
            }
            string dir = Path.Combine(ModClass._declare.FolderPath, Folder.Replace('/', Path.DirectorySeparatorChar));
            int count = 0, rejected = 0;
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.ogg");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    int cut = name.LastIndexOf('_');
                    string family = cut > 0 ? name.Substring(0, cut) : "";
                    if (Array.IndexOf(Families, family) < 0 || !OggHeader(file))
                    {
                        rejected++;
                        Report("name:" + name, $"跳过 {name}.ogg(文件名要 文化系_序号，文化系是 {string.Join("/", Families)}；内容要是 Ogg)");
                        continue;
                    }
                    if (!Tracks.TryGetValue(family, out var list)) Tracks[family] = list = new List<string>();
                    list.Add(file);
                    count++;
                }
            }
            _ready = count > 0;
            var summary = new List<string>();
            foreach (string family in Families)
                summary.Add($"{family} {(Tracks.TryGetValue(family, out var list) ? list.Count : 0)}");
            NeoModLoader.services.LogService.LogInfo($"{Tag} 找到 {count} 首({string.Join("、", summary)})，跳过 {rejected} 个" +
                                                     (_ready ? "" : $"；{dir} 下没有可用的曲子，文明乐不放"));
        }
        catch (Exception error)
        {
            _ready = false;
            Report("load", $"读曲目出错，文明乐停用：{error}");
        }
        return _ready;
    }

    private static bool OggHeader(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            var head = new byte[4];
            return stream.Read(head, 0, 4) == 4 && head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S';
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HasTracks(string family) => family != null && Tracks.TryGetValue(family, out var list) && list.Count > 0;

    // 一首曲子的流：用时才开，开过的留着
    private static object Stream(string path)
    {
        if (Streams.TryGetValue(path, out object sound)) return sound;
        sound = FmodBridge.CreateStream(path, out object result);
        if (sound == null) Report("open:" + path, $"FMOD 打不开 {Path.GetFileName(path)}({result})，跳过");
        Streams[path] = sound;
        return sound;
    }

    #endregion

    #region 看哪一系

    private static readonly string[] Families = { "huaxia", "japan", "western", "norse", "arab", "steppe", "india", "america" };

    // 文化 → 八系；对不上的(精灵、其他自定义文化中不属这几条线的)不放
    internal static string Family(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return null;
        switch (culture)
        {
            case "Huaxia":
            case "China":
            case "Shanhai":
                return "huaxia";
            case "Japan":
                return "japan";
            case "Viking":
                return "norse";
            case "Arab":
            case "Egypt":
            case "Persepolis":
            case "Kosher":
                return "arab";
            case "India":
                return "india";
            case "Youmu":
                return "steppe";
            case "Aztec":
            case "Ojibwe":
                return "america";
            case "ElfFancy":
                return null;
        }
        if (culture.IsNomadCulture()) return "steppe";
        // 别的(自定义)文化按制度线：罗马线的西方各族(法兰克、日耳曼、斯拉夫、西方、罗马……)都算西方
        return InstitutionSystem.GetCultureLine(culture) switch
        {
            "Roma" => "western",
            "Huaxia" => "huaxia",
            "Japan" => "japan",
            "Arabic" => "arab",
            _ => null
        };
    }

    private static string Sample(Camera camera)
    {
        float ortho = camera.orthographicSize;
        float max = World.world.move_camera != null ? World.world.move_camera.orthographic_size_max : 130f;
        float enter = Mathf.Min(EnterOrtho, Mathf.Max(30f, max * 0.7f));
        float leave = Mathf.Min(LeaveOrtho, Mathf.Max(36f, max * 0.8f));
        _zoomedIn = _zoomedIn ? ortho <= leave : ortho <= enter;
        if (!_zoomedIn) return null;

        Votes.Clear();
        Vector3 c = camera.transform.position;
        float dx = ortho * Mathf.Max(0.5f, camera.aspect) * 0.5f, dy = ortho * 0.5f;
        Vote(c.x, c.y, 2);
        Vote(c.x - dx, c.y, 1);
        Vote(c.x + dx, c.y, 1);
        Vote(c.x, c.y - dy, 1);
        Vote(c.x, c.y + dy, 1);
        string best = null;
        int top = 0;
        foreach (KeyValuePair<string, int> pair in Votes)
            if (pair.Value > top)
            {
                best = pair.Key;
                top = pair.Value;
            }
        return top >= 3 ? best : null;
    }

    private static void Vote(float x, float y, int weight)
    {
        WorldTile tile = World.world.GetTile(Mathf.FloorToInt(x), Mathf.FloorToInt(y));
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt()) return;
        string culture = CultureService.GetMainCulture(city, initialize: false);
        if (!CultureService.IsValidCulture(culture) && city.kingdom != null) culture = CultureService.GetRealmCulture(city.kingdom);
        string family = Family(culture);
        if (family == null || !HasTracks(family)) return;
        Votes.TryGetValue(family, out int votes);
        Votes[family] = votes + weight;
    }

    #endregion

    #region 放

    private static Voice Active => Voices.Count > 0 && Voices[Voices.Count - 1].Target > 0f ? Voices[Voices.Count - 1] : null;

    // 按目标调度：换系就开新曲(接着上次那首)、旧的淡出；同一系一首放完一遍换下一首
    private static void Steer(float now)
    {
        Voice active = Active;
        if (_target == null || !HasTracks(_target))
        {
            foreach (Voice voice in Voices) voice.Target = 0f;
            return;
        }
        if (active != null && active.Family == _target)
        {
            // 一首放到快完(留一个淡出的工夫)就换同系的下一首；读不到位置就按开播以来的时间算
            if (Tracks[_target].Count > 1 && !active.Finished && active.LengthSeconds > FadeOutSeconds * 2f)
            {
                float played = FmodBridge.TryPositionMs(active.Channel, out uint ms) ? ms / 1000f : now - active.StartedAt;
                if (played >= active.LengthSeconds - FadeOutSeconds)
                {
                    active.Finished = true;
                    Start(_target, now, true);
                }
            }
            return;
        }
        // 正在淡出的同系那一路接着用(来回切时不重开)
        for (int i = Voices.Count - 1; i >= 0; i--)
        {
            Voice voice = Voices[i];
            if (voice.Family != _target || !FmodBridge.Playing(voice.Channel)) continue;
            foreach (Voice other in Voices) other.Target = 0f;
            voice.Target = 1f;
            Voices.RemoveAt(i);
            Voices.Add(voice);
            return;
        }
        Start(_target, now, false);
    }

    // advance：上一首放完了，换下一首；否则接着放这一系上次那首
    private static void Start(string family, float now, bool advance)
    {
        List<string> list = Tracks[family];
        int first = Current.TryGetValue(family, out string last) ? Mathf.Max(0, list.IndexOf(last)) : 0;
        if (advance && last != null) first++;
        for (int tried = 0; tried < list.Count; tried++)
        {
            string path = list[(first + tried) % list.Count];
            // 同一首还在淡出的话流只能放一路，换不了，留着它
            if (Voices.Exists(voice => voice.Track == path && FmodBridge.Playing(voice.Channel))) continue;
            object sound = Stream(path);
            if (sound == null) continue;
            object channel = FmodBridge.Play(sound, _group, 0f, 0f, 1f, 0f, out object result);
            if (channel == null)
            {
                Report("play:" + path, $"放不了 {Path.GetFileName(path)}({result})");
                continue;
            }
            Current[family] = path;
            // 开播时音量是 0，跳到上次停下的地方听不出来
            if (Positions.TryGetValue(path, out uint resume))
            {
                Positions.Remove(path);
                if (resume > 0u) FmodBridge.Seek(channel, resume);
            }
            foreach (Voice other in Voices) other.Target = 0f;
            Voices.Add(new Voice
            {
                Family = family, Track = path, Channel = channel, Volume = 0f, Target = 1f, StartedAt = now,
                LengthSeconds = FmodBridge.LengthMs(sound) / 1000f
            });
            if (!_announced)
            {
                _announced = true;
                NeoModLoader.services.LogService.LogInfo($"{Tag} 开始放：{Path.GetFileName(path)}");
            }
            // 同时至多 MaxVoices 路：最老的直接停
            while (Voices.Count > MaxVoices)
            {
                Park(Voices[0]);
                FmodBridge.Stop(Voices[0].Channel);
                Voices.RemoveAt(0);
            }
            return;
        }
    }

    private static void Fade(float dt, float now)
    {
        for (int i = Voices.Count - 1; i >= 0; i--)
        {
            Voice voice = Voices[i];
            voice.Volume = Mathf.MoveTowards(voice.Volume, voice.Target,
                dt / (voice.Target > voice.Volume ? FadeInSeconds : FadeOutSeconds));
            if (voice.Volume <= 0f && voice.Target <= 0f || !FmodBridge.Valid(voice.Channel))
            {
                Park(voice);
                FmodBridge.Stop(voice.Channel);
                Voices.RemoveAt(i);
                continue;
            }
            FmodBridge.SetVolume(voice.Channel, Gain(voice.Volume));
        }
    }

    // 在线更新退出时换文件前(见 EmpireCraftUpdateService.OnQuitting)：停掉在放的曲子、关掉曲子流——FMOD 开着曲子文件，
    // 模组目录改不了名；之后用到再重开，接着停下的地方放
    internal static void ReleaseFiles()
    {
        if (Voices.Count == 0 && Streams.Count == 0) return;
        foreach (Voice voice in Voices)
        {
            Park(voice);
            FmodBridge.Stop(voice.Channel);
        }
        Voices.Clear();
        foreach (object sound in Streams.Values) FmodBridge.Release(sound);
        Streams.Clear();
    }

    // 停下前记住放到哪，下次接着放；放完一遍换了下一首的从头来
    private static void Park(Voice voice)
    {
        if (voice.Finished) Positions.Remove(voice.Track);
        else if (FmodBridge.TryPositionMs(voice.Channel, out uint ms)) Positions[voice.Track] = ms;
    }

    // 淡入淡出的响度：S 形再平方，开头从无声慢慢起来、到顶不顿；两路交叉的中点各约 -12 dB
    private static float Gain(float fade)
    {
        fade = Mathf.Clamp01(fade);
        float s = fade * fade * (3f - 2f * fade);
        return s * s;
    }

    #endregion

    private static void Report(string key, string message)
    {
        if (Reported.Add(key)) NeoModLoader.services.LogService.LogWarning($"{Tag} {message}");
    }
}

// 文明乐每帧更新
public sealed class CivMusicHost : MonoBehaviour
{
    private void Update() => CivMusic.Tick();
}
