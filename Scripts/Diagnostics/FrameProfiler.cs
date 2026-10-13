using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

// 累计计时：把每帧被调用很多次的小段代码(比如每座城每帧的钩子)按标签累计，
// 每 ReportSeconds 秒在日志里报一次"平均每帧耗时"，只报超过 MinMsPerFrame 的标签。
// 用法：using (FrameProfiler.Measure("标签")) { ... }
// 另外做帧率体检(定位"帧数一直在跳")：每个窗口报 均帧/最差帧/卡顿帧占比/GC 次数，
// 并对 ≥50ms 的尖峰帧逐帧归因——列出当帧各计时段的耗时，剩下的就是模组计时段之外(原版或 GC)。
// ModClass.Update 每帧先调 FrameTick()。
public static class FrameProfiler
{
    private const float ReportSeconds = 10f;
    private const double MinMsPerFrame = 0.3d;
    private const double SpikeMs = 50d;      // 单帧 ≥50ms(瞬时低于 20fps) 记为尖峰帧
    private const float SlowFrameSeconds = 1f / 30f;
    private const int MaxSpikeLines = 3;     // 每个窗口最多详列 3 个尖峰帧，其余只计数

    private struct Sample
    {
        internal long Ticks, Peak;
        internal int Calls;
    }
    private static readonly Dictionary<string, Sample> Ticks = new();
    private static float _since = -1f;
    private static int _startFrame;

    // 当帧各标签耗时(尖峰归因用)
    private static readonly Dictionary<string, long> FrameTicks = new();
    private static int _currentFrame = -1;
    // 窗口帧率统计
    private static int _frames, _slowFrames, _spikes, _spikeListed;
    private static float _frameSum, _frameWorst;
    private static readonly StringBuilder SpikeDetail = new();
    // 窗口 GC 基线
    private static int _gc0, _gc1, _gc2;
    private static long _heap;

    public readonly struct Scope : System.IDisposable
    {
        private readonly string _label;
        private readonly long _started;

        public Scope(string label)
        {
            EnsureFrame();
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
            FrameTicks.TryGetValue(_label, out long frameTicks);
            FrameTicks[_label] = frameTicks + elapsed;
            Report();
        }
    }

    public static Scope Measure(string label) => new(label);

    // 原版主循环总耗时(MapBox.Update，包含各"原版·"分项，不参与分项相加)
    public const string MainLoopLabel = "总·原版主循环";

    // 每帧由 ModClass.Update 最先调用：结算刚结束的那一帧
    public static void FrameTick()
    {
        EnsureFrame();
        Report();
    }

    // 新帧的第一次调用(FrameTick 或任何 Measure)把上一帧归档
    private static void EnsureFrame()
    {
        int frame = Time.frameCount;
        if (frame == _currentFrame) return;
        // 新帧首次读到的 unscaledDeltaTime 正是刚结束那一帧的时长
        if (_currentFrame >= 0 && frame == _currentFrame + 1) SettleFrame(Time.unscaledDeltaTime);
        else FrameTicks.Clear();
        _currentFrame = frame;
    }

    private static void SettleFrame(float dt)
    {
        // 暂停、读档期间的帧不掺进统计
        if (!Config.game_loaded || Config.paused || SmoothLoader.isLoading()) { FrameTicks.Clear(); return; }
        _frames++;
        _frameSum += dt;
        if (dt > _frameWorst) _frameWorst = dt;
        if (dt >= SlowFrameSeconds) _slowFrames++;
        double ms = dt * 1000d;
        if (ms >= SpikeMs)
        {
            _spikes++;
            if (_spikeListed < MaxSpikeLines)
            {
                _spikeListed++;
                AppendSpikeDetail(ms);
            }
        }
        FrameTicks.Clear();
    }

    // 尖峰帧归因：列当帧耗时最高的几个计时段；剩余时间在模组计时段之外(原版模拟/渲染或 GC)
    private static void AppendSpikeDetail(double frameMs)
    {
        var ordered = new List<KeyValuePair<string, long>>(FrameTicks);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        SpikeDetail.Append($"\n[EmpireCraft][尖峰帧] {frameMs:0.0} ms：");
        double accounted = 0d;
        int listed = 0;
        double mainLoop = -1d;
        foreach (KeyValuePair<string, long> pair in ordered)
        {
            double ms = pair.Value * 1000d / Stopwatch.Frequency;
            if (pair.Key == MainLoopLabel)
            {
                mainLoop = ms;
                continue;
            }
            accounted += ms;
            if (ms < 1d || listed >= 4) continue;
            listed++;
            SpikeDetail.Append($"{pair.Key} {ms:0.0}，");
        }
        // 嵌套计时(年度扫描在城市钩子内)会重复累计，这里只取下界
        double outside = System.Math.Max(0d, frameMs - accounted);
        SpikeDetail.Append(listed == 0 ? $"模组计时段合计 <1 ms，几乎全部在计时段外(原版或 GC)"
            : $"计时段外约 {outside:0.0} ms");
        if (mainLoop >= 0d)
            SpikeDetail.Append($"；原版主循环 {mainLoop:0.0} ms，主循环外(渲染、别的模组、GC)约 {System.Math.Max(0d, frameMs - mainLoop):0.0} ms");
    }

    public static void Reset()
    {
        Ticks.Clear();
        FrameTicks.Clear();
        _since = -1f;
        _startFrame = 0;
        _currentFrame = -1;
        ResetWindowStats();
    }

    private static void ResetWindowStats()
    {
        _frames = 0; _slowFrames = 0; _spikes = 0; _spikeListed = 0;
        _frameSum = 0f; _frameWorst = 0f;
        SpikeDetail.Length = 0;
        _gc0 = System.GC.CollectionCount(0);
        _gc1 = System.GC.CollectionCount(1);
        _gc2 = System.GC.CollectionCount(2);
        _heap = System.GC.GetTotalMemory(false);
    }

    private static void Report()
    {
        float now = Time.unscaledTime;
        if (_since < 0f || now < _since || Time.frameCount < _startFrame)
        {
            _since = now;
            _startFrame = Time.frameCount;
            ResetWindowStats();
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
        // 帧率体检行：每个窗口必报(写性能日志)；帧数明显在跳时同时进游戏日志
        string health = BuildFrameHealthLine();
        bool unstable = _spikes > 0 || (_frames > 0 && _slowFrames * 20 >= _frames);
        if (health != null && unstable) LogService.LogWarning(health + SpikeDetail);
        if (EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled)
        {
            EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(line.Length > 0 ? report :
                "[EmpireCraft][每帧耗时] 被计时片段均低于 0.3 ms/帧且单次峰值低于 4 ms");
            if (health != null)
                EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(health + SpikeDetail);
        }
        Ticks.Clear();
        _since = now;
        _startFrame = Time.frameCount;
        ResetWindowStats();
    }

    private static string BuildFrameHealthLine()
    {
        if (_frames <= 0) return null;
        double avgMs = _frameSum * 1000d / _frames;
        double fps = avgMs > 0d ? 1000d / avgMs : 0d;
        int gc0 = System.GC.CollectionCount(0) - _gc0;
        int gc1 = System.GC.CollectionCount(1) - _gc1;
        int gc2 = System.GC.CollectionCount(2) - _gc2;
        double heapDelta = (System.GC.GetTotalMemory(false) - _heap) / (1024d * 1024d);
        return $"[EmpireCraft][帧率] 均帧 {avgMs:0.0} ms ({fps:0} fps)，最差 {_frameWorst * 1000f:0} ms，" +
               $">33ms 帧 {_slowFrames}/{_frames} ({100d * _slowFrames / _frames:0.0}%)，尖峰(≥50ms) {_spikes} 个，" +
               $"GC 0代+{gc0} 1代+{gc1} 2代+{gc2}，托管堆 {(heapDelta >= 0 ? "+" : "")}{heapDelta:0.0} MB";
    }
}
