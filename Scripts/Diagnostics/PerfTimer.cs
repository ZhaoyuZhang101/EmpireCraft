using System;
using System.Diagnostics;
using NeoModLoader.services;

// 计时一段代码，超过阈值就在日志里记一条"[EmpireCraft][性能]"，用来定位周期性卡顿
public readonly struct PerfTimer : IDisposable
{
    private const double WarnMilliseconds = 8d;
    private readonly string _label;
    private readonly long _started;

    public PerfTimer(string label)
    {
        _label = label;
        _started = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        double elapsed = (Stopwatch.GetTimestamp() - _started) * 1000d / Stopwatch.Frequency;
        if (elapsed >= WarnMilliseconds) LogService.LogWarning($"[EmpireCraft][性能] {_label} 耗时 {elapsed:0.0} ms");
    }
}
