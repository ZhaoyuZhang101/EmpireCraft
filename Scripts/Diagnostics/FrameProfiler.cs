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

    private struct Sample
    {
        internal long Ticks, Peak;
        internal int Calls;
    }
    private static readonly Dictionary<string, Sample> Ticks = new();
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
            if (_label == null) return;
            long elapsed = Stopwatch.GetTimestamp() - _started;
            Ticks.TryGetValue(_label, out Sample sample);
            sample.Ticks += elapsed;
            sample.Peak = System.Math.Max(sample.Peak, elapsed);
            sample.Calls++;
            Ticks[_label] = sample;
            Report();
        }
    }

    public static Scope Measure(string label) => new(label);

    public static void Reset()
    {
        Ticks.Clear();
        _since = -1f;
        _startFrame = 0;
    }

    private static void Report()
    {
        float now = Time.unscaledTime;
        if (_since < 0f || now < _since || Time.frameCount < _startFrame)
        {
            _since = now;
            _startFrame = Time.frameCount;
            return;
        }
        if (now - _since < ReportSeconds) return;
        int frames = Mathf.Max(1, Time.frameCount - _startFrame);
        var line = new StringBuilder();
        var ordered = new List<KeyValuePair<string, Sample>>(Ticks);
        ordered.Sort((a, b) => b.Value.Ticks.CompareTo(a.Value.Ticks));
        foreach (KeyValuePair<string, Sample> pair in ordered)
        {
            double perFrame = pair.Value.Ticks * 1000d / Stopwatch.Frequency / frames;
            double peak = pair.Value.Peak * 1000d / Stopwatch.Frequency;
            if (perFrame >= MinMsPerFrame || peak >= 4d)
                line.Append($"{pair.Key} {perFrame:0.00} ms/帧，单次峰值 {peak:0.00} ms，{pair.Value.Calls} 次；");
        }
        string report = $"[EmpireCraft][每帧耗时] {line}";
        if (line.Length > 0) LogService.LogWarning(report);
        if (EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled)
            EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(line.Length > 0 ? report :
                "[EmpireCraft][每帧耗时] 被计时片段均低于 0.3 ms/帧且单次峰值低于 4 ms");
        Ticks.Clear();
        _since = now;
        _startFrame = Time.frameCount;
    }
}
