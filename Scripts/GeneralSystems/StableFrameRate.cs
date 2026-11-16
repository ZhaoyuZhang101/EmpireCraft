using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 稳定帧率(模组设置"稳定帧率"开关)：把帧率锁在每秒 30 帧、关掉垂直同步(两者一起开会互相打架)。
// 平时的帧都按 33 毫秒匀速出，比"能跑多快跑多快"忽高忽低看着稳；关掉时恢复打开前的设置。
// 游戏自己的设置可能改回去，每两秒检查一次、改回来。
public static class StableFrameRate
{
    public const int Target = 30;
    // 游戏本体闲着时的帧率(见 SetIdle)
    public const int IdleTarget = 10;
    private static bool _enabled, _saved, _idle;
    private static int _savedFrameRate, _savedVSync, _idleFrameRate, _idleVSync;
    private static float _nextCheck;

    public static void Set(bool on)
    {
        if (on == _enabled) return;
        _enabled = on;
        if (on)
        {
            if (!_saved)
            {
                _savedFrameRate = Application.targetFrameRate;
                _savedVSync = QualitySettings.vSyncCount;
                _saved = true;
            }
            Apply();
        }
        else if (_saved)
        {
            Application.targetFrameRate = _savedFrameRate;
            QualitySettings.vSyncCount = _savedVSync;
            _saved = false;
        }
    }

    // 游戏本体闲着(这一局交给了服务器核心，本体只剩一块黑屏牌子，见 CoreHost)：帧率压到每秒 10 帧，处理器和显卡都省下来
    // (用户 2026-11-16 "这个本体运行着不会消耗我电脑性能吧"：当时本体空转着每秒四十多帧，占一个多核、一成显卡)。
    // 牌子一秒才换一回字，按钮、快捷键十帧也够用。放开时回到原样；"稳定帧率"开着的回到它那一档
    public static void SetIdle(bool on)
    {
        if (on == _idle) return;
        _idle = on;
        if (on)
        {
            _idleFrameRate = Application.targetFrameRate;
            _idleVSync = QualitySettings.vSyncCount;
            ApplyIdle();
        }
        else if (_enabled) Apply();
        else
        {
            Application.targetFrameRate = _idleFrameRate;
            QualitySettings.vSyncCount = _idleVSync;
        }
    }

    public static void Tick()
    {
        if (!_enabled && !_idle) return;
        float now = Time.unscaledTime;
        if (now < _nextCheck) return;
        _nextCheck = now + 2f;
        if (_idle)
        {
            if (Application.targetFrameRate != IdleTarget || QualitySettings.vSyncCount != 0) ApplyIdle();
        }
        else if (Application.targetFrameRate != Target || QualitySettings.vSyncCount != 0) Apply();
    }

    private static void ApplyIdle()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = IdleTarget;
    }

    private static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Target;
    }
}
