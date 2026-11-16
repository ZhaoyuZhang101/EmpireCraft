using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EmpireCraft.Scripts.Terminal;

// 轻量主机模式：这台电脑只当终端的主机用时，停掉本机的画面和声音，省下显卡和一大半处理器；世界照常走——
// 原版主循环、月度结算、自动存档都不动，终端照常给手机备数据、办手机发来的事。
//   · 进：设置里打开开关，终端在跑、世界读完、窗口都关了、世界没暂停，就进(读档时可能暂停着：等关窗、恢复模拟再进)；
//   · 停的是什么：各摄像机什么都不画只清成黑色、界面的画布全关、原版的渲染入口跳过(见 LightweightHostPatch)、
//     声音从主通道静掉(原版的声音走 FMOD，不走 Unity 的 AudioListener；两样都静)；
//   · 黑屏上留一块牌子：写着世界到了哪年哪月、走得多快、终端的地址与配对码、几部手机连着，和一个「恢复画面」的按钮
//     (不留的话主机前的人分不清是在跑还是卡死了)。牌子自带一整屏的黑底：原版每帧把摄像机的底色改回海色
//     (LibraryMaterials.updateNight)，光靠两秒一次把底色涂黑，屏幕就是蓝的、两秒闪一下黑；
//   · 世界每帧走的时长是固定的(固定帧长 × 速度档)，所以帧数就是世界的速度：牌子上写速度档和每分钟走几个月，
//     比光写帧数说明问题(二十倍速下一帧顶一倍速的二十帧，帧数低不等于世界走得慢)；
//   · 帧数掉到每秒 3 帧以下、连着十秒：自己开一轮性能诊断(二十五秒)，把各段耗时记进性能日志，过后好查是谁拖的；
//   · 出：点那个按钮，或按 Ctrl+Shift+F10；开关随之关掉。终端停了、换世界读档时自己先退出来，好了再进。
// 手机版游戏上不启用。AudioListener 按名字反射：手机上编译时未必带着声音那个程序集(见 mobile-nml-compat)
internal static class LightweightHostMode
{
    internal const string SettingId = "terminal_lightweight_host_toggle";
    internal static bool Enabled;
    internal static bool HooksReady;
    internal static bool Active { get; private set; }
    internal static bool SkipDrawing => Active && Config.game_loaded && !SmoothLoader.isLoading();

    private static readonly Dictionary<Camera, (int mask, CameraClearFlags clear, Color background)> Cameras = new();
    private static readonly HashSet<Canvas> Canvases = new();
    private static readonly PropertyInfo ListenerVolume =
        Type.GetType("UnityEngine.AudioListener, UnityEngine.AudioModule")?.GetProperty("volume", BindingFlags.Public | BindingFlags.Static);
    private static bool _runInBackground, _wasEnabled;
    private static float _volume = 1f, _nextScan, _nextText, _fpsSince;
    private static int _fpsFrames, _fps;
    // 世界的速度：上次刷牌子时的世界时刻与每分钟走的月数(平滑过)
    private static double _paceWorld = -1d;
    private static float _pace;
    // 帧数过低时自己开的那一轮性能诊断
    private const int CrawlFps = 3, CrawlSeconds = 10;
    private const float ProbeSeconds = 25f, ProbeGap = 300f;
    private static int _crawl;
    private static bool _probing;
    private static float _probeUntil, _probeNext;
    private static GameObject _panel;
    private static Canvas _panelCanvas;
    private static Text _status, _link, _title, _note, _face, _face2;
    // 黑屏眼下是替服务器核心挂着的(核心放手的那一帧恢复画面)
    private static bool _forCore;

    internal static void Tick()
    {
        // 服务器核心接手着这一局(见 CoreHost)：黑屏和牌子归它用，世界由它暂停着
        if (CoreHost.Holding)
        {
            _forCore = true;
            if (!Active) Enter();
            if (HotKey()) CoreHost.RequestStop();
            Hold();
            return;
        }
        if (_forCore)
        {
            _forCore = false;
            Restore();
        }
        if (Enabled != _wasEnabled)
        {
            _wasEnabled = Enabled;
            if (Enabled) Announce();
        }
        if (!Enabled && !Active) return;
        if (Enabled && HotKey())
        {
            TurnOff();
            return;
        }

        if (!Enabled || !HooksReady || Application.isMobilePlatform || !TerminalHost.Enabled || !TerminalHost.Running ||
            World.world == null || !Config.game_loaded || SmoothLoader.isLoading() || ScrollWindow.isWindowActive())
        {
            Restore();
            return;
        }

        if (!Active)
        {
            // 读档时可能暂停。等主机前的人先关窗、恢复模拟，再进入黑屏。
            if (World.world.isPaused()) return;
            Enter();
            LogService.LogInfo("[EmpireCraft][轻量主机] 已停止本地绘制，按 Ctrl+Shift+F10 恢复画面。");
        }
        Hold();
    }

    private static bool HotKey() =>
        Input.GetKeyDown(KeyCode.F10) &&
        (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
        (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

    private static void Enter()
    {
        _runInBackground = Application.runInBackground;
        if (ListenerVolume?.GetValue(null, null) is float volume) _volume = volume;
        Active = true;
        _nextScan = _nextText = 0f;
        _fpsSince = Time.unscaledTime;
        _fpsFrames = 0;
        _paceWorld = -1d;
        _pace = 0f;
        _crawl = 0;
    }

    // 黑屏期间每帧：刷牌子；每两秒把新冒出来的摄像机、画布也停掉
    private static void Hold()
    {
        _fpsFrames++;
        if (Time.unscaledTime >= _nextText) RefreshPanel();
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 2f;
        Application.runInBackground = true;
        Silence(true);
        // 保留启用的摄像机和 Camera.main，给仍读取坐标的游戏逻辑使用。屏幕只清成黑色。
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera.targetTexture != null) continue;
            if (!Cameras.ContainsKey(camera)) Cameras.Add(camera, (camera.cullingMask, camera.clearFlags, camera.backgroundColor));
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }
        foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
        {
            if (!canvas.enabled || canvas == _panelCanvas) continue;
            Canvases.Add(canvas);
            canvas.enabled = false;
        }
    }

    internal static void Restore()
    {
        if (!Active) return;
        Active = false;
        StopProbe();
        Application.runInBackground = _runInBackground;
        Silence(false);
        foreach (var entry in Cameras)
        {
            Camera camera = entry.Key;
            if (camera == null) continue;
            (camera.cullingMask, camera.clearFlags, camera.backgroundColor) = entry.Value;
        }
        Cameras.Clear();
        foreach (Canvas canvas in Canvases)
            if (canvas != null) canvas.enabled = true;
        Canvases.Clear();
        if (_panel != null) _panel.SetActive(false);
    }

    // 关掉这个模式(按钮、快捷键、接不上时)：画面回来，设置里的开关也拨回去
    private static void TurnOff()
    {
        Enabled = _wasEnabled = false;
        Restore();
        ModSettingsWindow.SetSwitch(SettingId, false);
    }

    // 开关刚打开：说一声眼下是什么情形(接不上、终端没开、还是关了窗就进)
    private static void Announce()
    {
        if (Application.isMobilePlatform) return;
        if (!HooksReady)
        {
            // 这一版游戏的渲染入口对不上：不硬来，开关拨回去
            Tip("terminal_light_unavailable");
            TurnOff();
            return;
        }
        Tip(TerminalHost.Enabled ? "terminal_light_armed" : "terminal_light_need_terminal");
    }

    private static void Tip(string key)
    {
        try
        {
            WorldTip.showNow(LM.Get(key), false, "top", 5f);
        }
        catch (Exception)
        {
            // 提示出不来不要紧
        }
    }

    // 声音：原版的和模组自己放的都从 FMOD 的主通道出去；Unity 自己的那一路也关上
    private static void Silence(bool on)
    {
        try
        {
            FmodBridge.MuteMaster(on);
            ListenerVolume?.SetValue(null, on ? 0f : _volume, null);
        }
        catch (Exception)
        {
            // 静不了音不碍事：画面照停
        }
    }

    // ───────── 黑屏上的牌子 ─────────
    private static void RefreshPanel()
    {
        float now = Time.unscaledTime;
        _nextText = now + 1f;
        if (CoreHost.Holding)
        {
            RefreshForCore();
            return;
        }
        float span = now - _fpsSince;
        if (span > 0f) _fps = Mathf.RoundToInt(_fpsFrames / span);
        _fpsSince = now;
        _fpsFrames = 0;
        bool paused = World.world.isPaused();
        // 一个月是 5 秒世界时长(MapStats)
        double world = World.world.getCurWorldTime();
        if (_paceWorld >= 0d && span > 0f && world >= _paceWorld)
        {
            float pace = (float)((world - _paceWorld) / 5d / span * 60d);
            _pace = _pace <= 0f || paused ? pace : Mathf.Lerp(_pace, pace, 0.3f);
        }
        _paceWorld = world;
        WatchCrawl(now, paused);
        try
        {
            if (_panel == null) BuildPanel();
            if (!_panel.activeSelf) _panel.SetActive(true);
            _title.text = LM.Get(SettingId);
            _note.text = LM.Get("terminal_light_note");
            _face.text = LM.Get("terminal_light_restore");
            _face.transform.parent.gameObject.SetActive(true);
            LayoutButtons(null);
            _status.text = string.Format(LM.Get("terminal_light_status"), Date.getCurrentYear(), Date.getCurrentMonth(), _fps,
                               _pace >= 10f ? _pace.ToString("0") : _pace.ToString("0.#"), SpeedScale().ToString("0.##")) +
                           (paused ? " · " + LM.Get("terminal_light_paused") : "");
            // PrimaryAddress 自己带着端口
            string address = TerminalHost.PublicUrl.Length > 0 ? TerminalHost.PublicUrl : TerminalHost.PrimaryAddress();
            _link.text = string.Format(LM.Get("terminal_light_link"), address, TerminalHost.Code, TerminalHost.Seats().Count(seat => seat.Watched));
        }
        catch (Exception exception)
        {
            // 牌子画不出来也不退出黑屏(快捷键还在)；只记一回
            if (_panel == null) LogService.LogWarning($"[EmpireCraft][轻量主机] 状态牌没画成: {exception.Message}");
        }
    }

    // 服务器核心接手着：牌子换成核心的字(到哪一步了、手机该连的地址、管理员码、停下并带回的按钮)
    private static void RefreshForCore()
    {
        try
        {
            if (_panel == null) BuildPanel();
            if (!_panel.activeSelf) _panel.SetActive(true);
            _title.text = LM.Get(CoreHost.SettingId);
            _note.text = LM.Get("terminal_core_note");
            _status.text = CoreHost.StatusLine();
            _link.text = CoreHost.LinkLine();
            _face.text = CoreHost.ButtonText();
            _face.transform.parent.gameObject.SetActive(CoreHost.CanStop);
            LayoutButtons(CoreHost.SecondButtonText());
        }
        catch (Exception exception)
        {
            if (_panel == null) LogService.LogWarning($"[EmpireCraft][轻量主机] 状态牌没画成: {exception.Message}");
        }
    }

    // 牌子上的按钮：核心接手着是"停下核心、带回这一局"，否则是恢复画面
    private static void OnButton()
    {
        if (CoreHost.Holding) CoreHost.OnButton(0);
        else TurnOff();
    }

    private static void OnSecondButton()
    {
        if (CoreHost.Holding) CoreHost.OnButton(1);
    }

    // 按钮照字的宽度定宽；有第二个按钮(核心问话时)就两个并排居中，没有就一个居中
    private static void LayoutButtons(string second)
    {
        var first = (RectTransform)_face.transform.parent;
        var other = (RectTransform)_face2.transform.parent;
        other.gameObject.SetActive(second != null);
        if (second != null) _face2.text = second;
        float width = Mathf.Max(150f, _face.preferredWidth + 24f);
        float otherWidth = second != null ? Mathf.Max(150f, _face2.preferredWidth + 24f) : 0f;
        const float Gap = 12f;
        first.sizeDelta = new Vector2(width, 34f);
        other.sizeDelta = new Vector2(otherWidth, 34f);
        first.anchoredPosition = new Vector2(second != null ? -(otherWidth + Gap) / 2f : 0f, -58f);
        other.anchoredPosition = new Vector2((width + Gap) / 2f, -58f);
    }

    // 眼下的速度档折成倍数：档位的倍数 × 每帧走几步，锁 30 帧时原版再翻倍，终端流速放慢时再乘上
    private static float SpeedScale()
    {
#if EC_SERVER
        return 1f;
#else
        WorldTimeScaleAsset scale = Config.time_scale_asset;
        if (scale == null) return 1f;
        return scale.multiplier * Mathf.Max(1, scale.ticks) * (Config.fps_lock_30 ? 2f : 1f) * TerminalHost.SpeedFactor;
#endif
    }

    // 帧数过低连着十秒：开一轮性能诊断，二十五秒后关(设置里本来就开着的不动)；五分钟里至多一轮
    private static void WatchCrawl(float now, bool paused)
    {
        if (_probing)
        {
            if (now >= _probeUntil) StopProbe();
            return;
        }
        _crawl = !paused && _fps <= CrawlFps ? _crawl + 1 : 0;
        if (_crawl < CrawlSeconds || now < _probeNext || FrameProfiler.Enabled) return;
        _crawl = 0;
        _probing = true;
        _probeUntil = now + ProbeSeconds;
        _probeNext = now + ProbeGap;
        LogService.LogWarning($"[EmpireCraft][轻量主机] 每秒只有 {_fps} 帧(速度档 {SpeedScale():0.##} 倍，每分钟走 {_pace:0.#} 个月)，" +
                              "开一轮性能诊断，各段耗时见 EmpireCraft.performance.log。");
        FrameProfiler.SetEnabled(true);
    }

    private static void StopProbe()
    {
        if (!_probing) return;
        _probing = false;
        FrameProfiler.SetEnabled(false);
    }

    private static void BuildPanel()
    {
        _panel = new GameObject("EmpireCraftLightweightHost", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(_panel);
        _panelCanvas = _panel.GetComponent<Canvas>();
        _panelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _panelCanvas.sortingOrder = 32000;
        var scaler = _panel.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(960f, 540f);
        scaler.matchWidthOrHeight = 0.5f;
        Font font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        // 一整屏的黑底(盖住摄像机清出来的海色)，也挡住点到下面去
        var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(_panel.transform, false);
        var backdropRect = (RectTransform)backdrop.transform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = Color.black;

        Text Line(string name, string text, int size, Color color, float y)
        {
            var holder = new GameObject(name, typeof(RectTransform), typeof(Text));
            holder.transform.SetParent(_panel.transform, false);
            var rect = (RectTransform)holder.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, size + 10f);
            rect.anchoredPosition = new Vector2(0f, y);
            var label = holder.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        _title = Line("Title", LM.Get(SettingId), 22, new Color(0.953f, 0.765f, 0.29f), 78f);
        _note = Line("Note", LM.Get("terminal_light_note"), 13, new Color(0.78f, 0.76f, 0.71f), 46f);
        _status = Line("Status", "", 14, new Color(0.95f, 0.93f, 0.89f), 14f);
        _link = Line("Link", "", 14, new Color(0.5f, 0.85f, 0.92f), -12f);

        Text PanelButton(string name, Color color, UnityEngine.Events.UnityAction click)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(_panel.transform, false);
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(150f, 34f);
            buttonRect.anchoredPosition = new Vector2(0f, -58f);
            button.GetComponent<Image>().color = color;
            button.GetComponent<Button>().onClick.AddListener(click);
            Text face = Line(name + "Text", "", 15, Color.white, -58f);
            face.transform.SetParent(button.transform, false);
            ((RectTransform)face.transform).anchoredPosition = Vector2.zero;
            return face;
        }

        _face = PanelButton("Restore", new Color(0.31f, 0.58f, 0.26f), OnButton);
        _face.text = LM.Get("terminal_light_restore");
        // 第二个按钮只在服务器核心问话时出来(见 CoreHost.SecondButtonText)
        _face2 = PanelButton("Second", new Color(0.55f, 0.36f, 0.24f), OnSecondButton);
        _face2.transform.parent.gameObject.SetActive(false);
        Line("Key", LM.Get("terminal_light_key"), 11, new Color(0.56f, 0.63f, 0.66f), -88f);
    }
}
