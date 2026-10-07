using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

// 累计计时：把每帧被调用很多次的小段代码(比如每座城每帧的钩子)按标签累计，
// 每 ReportSeconds 秒在日志里报一次"平均每帧耗时"，只报超过 MinMsPerFrame 的标签。
// 用法：using (FrameProfiler.Measure("标签")) { ... }
public static class FrameProfiler
{
    private const float ReportSeconds = 10f;
    private const double MinMsPerFrame = 0.3d;

    private static readonly Dictionary<string, long> Ticks = new();
    private static float _since = -1f;
    private static int _startFrame;

    public readonly struct Scope : System.IDisposable
    {
        private readonly string _label;
        private readonly long _started;

        public Scope(string label)
        {
            _label = label;
            _started = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            long elapsed = Stopwatch.GetTimestamp() - _started;
            Ticks[_label] = Ticks.TryGetValue(_label, out long total) ? total + elapsed : elapsed;
            Report();
        }
    }

    public static Scope Measure(string label) => new(label);

    private static void Report()
    {
        float now = Time.unscaledTime;
        if (_since < 0f)
        {
            _since = now;
            _startFrame = Time.frameCount;
            return;
        }
        if (now - _since < ReportSeconds) return;
        int frames = Mathf.Max(1, Time.frameCount - _startFrame);
        var line = new StringBuilder();
        foreach (KeyValuePair<string, long> pair in Ticks)
        {
            double perFrame = pair.Value * 1000d / Stopwatch.Frequency / frames;
            if (perFrame >= MinMsPerFrame) line.Append($"{pair.Key} {perFrame:0.00} ms；");
        }
        if (line.Length > 0) LogService.LogWarning($"[EmpireCraft][每帧耗时] {line}");
        Ticks.Clear();
        _since = now;
        _startFrame = Time.frameCount;
    }
}
