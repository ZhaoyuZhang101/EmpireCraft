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

    // 标签要拼字符串(带城名、步骤号)又调用得很频繁时：先记起点，超过阈值才拼标签记日志，免得每次都拼
    public static long Begin() => Stopwatch.GetTimestamp();

    public static bool Slow(long started, out double elapsed)
    {
        elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        return elapsed >= WarnMilliseconds;
    }

    public static void Warn(string label, double elapsed) =>
        LogService.LogWarning($"[EmpireCraft][性能] {label} 耗时 {elapsed:0.0} ms");
}
