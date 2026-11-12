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
//   · 黑屏上留一块牌子：写着世界到了哪年哪月、帧率、终端的地址与配对码、几部手机连着，和一个「恢复画面」的按钮
//     (不留的话主机前的人分不清是在跑还是卡死了)；
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
    private static GameObject _panel;
    private static Canvas _panelCanvas;
    private static Text _status, _link;

    internal static void Tick()
    {
        if (Enabled != _wasEnabled)
        {
            _wasEnabled = Enabled;
            if (Enabled) Announce();
        }
        if (!Enabled && !Active) return;
        if (Enabled && Input.GetKeyDown(KeyCode.F10) &&
            (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
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
            _runInBackground = Application.runInBackground;
            if (ListenerVolume?.GetValue(null, null) is float volume) _volume = volume;
            Active = true;
            _nextScan = _nextText = 0f;
            _fpsSince = Time.unscaledTime;
            _fpsFrames = 0;
            LogService.LogInfo("[EmpireCraft][轻量主机] 已停止本地绘制，按 Ctrl+Shift+F10 恢复画面。");
        }

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
        if (now > _fpsSince) _fps = Mathf.RoundToInt(_fpsFrames / (now - _fpsSince));
        _fpsSince = now;
        _fpsFrames = 0;
        try
        {
            if (_panel == null) BuildPanel();
            if (!_panel.activeSelf) _panel.SetActive(true);
            _status.text = string.Format(LM.Get("terminal_light_status"), Date.getCurrentYear(), Date.getCurrentMonth(), _fps) +
                           (World.world.isPaused() ? " · " + LM.Get("terminal_light_paused") : "");
            string address = TerminalHost.PublicUrl.Length > 0 ? TerminalHost.PublicUrl : TerminalHost.PrimaryAddress() + ":" + TerminalHost.Port;
            _link.text = string.Format(LM.Get("terminal_light_link"), address, TerminalHost.Code, TerminalHost.Seats().Count(seat => seat.Watched));
        }
        catch (Exception exception)
        {
            // 牌子画不出来也不退出黑屏(快捷键还在)；只记一回
            if (_panel == null) LogService.LogWarning($"[EmpireCraft][轻量主机] 状态牌没画成: {exception.Message}");
        }
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

        Line("Title", LM.Get(SettingId), 22, new Color(0.953f, 0.765f, 0.29f), 78f);
        Line("Note", LM.Get("terminal_light_note"), 13, new Color(0.78f, 0.76f, 0.71f), 46f);
        _status = Line("Status", "", 14, new Color(0.95f, 0.93f, 0.89f), 14f);
        _link = Line("Link", "", 14, new Color(0.5f, 0.85f, 0.92f), -12f);

        var button = new GameObject("Restore", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(_panel.transform, false);
        var buttonRect = (RectTransform)button.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(150f, 34f);
        buttonRect.anchoredPosition = new Vector2(0f, -58f);
        button.GetComponent<Image>().color = new Color(0.31f, 0.58f, 0.26f);
        button.GetComponent<Button>().onClick.AddListener(TurnOff);
        Text face = Line("RestoreText", LM.Get("terminal_light_restore"), 15, Color.white, -58f);
        face.transform.SetParent(button.transform, false);
        ((RectTransform)face.transform).anchoredPosition = Vector2.zero;
        Line("Key", LM.Get("terminal_light_key"), 11, new Color(0.56f, 0.63f, 0.66f), -88f);
    }
}
