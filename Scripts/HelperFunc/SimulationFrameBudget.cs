using System;
using System.Diagnostics;
using UnityEngine;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.HelperFunc;

// 所有后台队列共用预算。40 FPS = 25 ms，给原版模拟与绘制留出大部分时间。
public static class SimulationFrameBudget
{
    private static bool Enabled => EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled;
    public const double TargetFramesPerSecond = 40d;
    public const double TargetFrameMilliseconds = 1000d / TargetFramesPerSecond;
    private static int _frame = -1;
    private static long _started;
    private static double _budget;
    private static double _spent;
    private static int _depth;
    private static int _sampleFrames, _slowFrames;
    private static double _sampleSeconds, _worstFrame;
    public static double LastMeasuredFramesPerSecond { get; private set; }

    public static double ResolveMilliseconds(double previousFrameMilliseconds) =>
        previousFrameMilliseconds >= 33d ? 0.5d : previousFrameMilliseconds >= TargetFrameMilliseconds ? 0.75d :
        Math.Max(1d, Math.Min(4d, (TargetFrameMilliseconds - previousFrameMilliseconds) * 0.5d));

    public static void BeginFrame()
    {
        if (!Enabled) { Reset(); return; }
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        _spent = 0d;
        _depth = 0;
        _budget = ResolveMilliseconds(Math.Max(0d, Time.unscaledDeltaTime * 1000d));
        double seconds = Math.Max(0d, Time.unscaledDeltaTime);
        _sampleSeconds += seconds;
        _sampleFrames++;
        _worstFrame = Math.Max(_worstFrame, seconds * 1000d);
        if (seconds * 1000d > TargetFrameMilliseconds) _slowFrames++;
        if (_sampleSeconds >= 30d)
        {
            LastMeasuredFramesPerSecond = _sampleFrames / _sampleSeconds;
            LogService.LogInfo($"[EmpireCraft][帧率] 最近 {_sampleSeconds:0} 秒平均 {LastMeasuredFramesPerSecond:0.0} FPS；" +
                $"最慢帧 {_worstFrame:0.0} ms；超过 25 ms 的帧 {_slowFrames}/{_sampleFrames}；目标 40 FPS");
            _sampleSeconds = _worstFrame = 0d;
            _sampleFrames = _slowFrames = 0;
        }
    }

    public static bool HasTime
    {
        get
        {
            if (!Enabled) return true;
            BeginFrame();
            double running = _depth > 0 ? (Stopwatch.GetTimestamp() - _started) * 1000d / Stopwatch.Frequency : 0d;
            return _spent + running < _budget;
        }
    }

    public static void Reset()
    {
        _frame = -1;
        _depth = 0;
        _spent = 0d;
        _sampleSeconds = _worstFrame = LastMeasuredFramesPerSecond = 0d;
        _sampleFrames = _slowFrames = 0;
    }

    // 只计入模组工作，原版模拟经过的时间不会让后调用的队列永远没机会运行；嵌套也不会重复记账。
    public static WorkScope Measure()
    {
        if (!Enabled) return new WorkScope();
        BeginFrame();
        if (_depth++ == 0) _started = Stopwatch.GetTimestamp();
        return new WorkScope(true);
    }

    public readonly struct WorkScope : IDisposable
    {
        private readonly bool _measured;
        internal WorkScope(bool measured) { _measured = measured; }
        public void Dispose()
        {
            if (!_measured || !Enabled) return;
            if (_depth <= 0) return;
            if (--_depth == 0) _spent += (Stopwatch.GetTimestamp() - _started) * 1000d / Stopwatch.Frequency;
        }
    }
}
