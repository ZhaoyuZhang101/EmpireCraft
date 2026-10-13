using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 稳定帧率(模组设置"稳定帧率"开关)：把帧率锁在每秒 30 帧、关掉垂直同步(两者一起开会互相打架)。
// 平时的帧都按 33 毫秒匀速出，比"能跑多快跑多快"忽高忽低看着稳；关掉时恢复打开前的设置。
// 游戏自己的设置可能改回去，每两秒检查一次、改回来。
public static class StableFrameRate
{
    public const int Target = 30;
    private static bool _enabled, _saved;
    private static int _savedFrameRate, _savedVSync;
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

    public static void Tick()
    {
        if (!_enabled) return;
        float now = Time.unscaledTime;
        if (now < _nextCheck) return;
        _nextCheck = now + 2f;
        if (Application.targetFrameRate != Target || QualitySettings.vSyncCount != 0) Apply();
    }

    private static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Target;
    }
}
