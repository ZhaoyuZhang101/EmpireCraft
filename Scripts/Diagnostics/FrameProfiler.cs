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
// 各计时段另记托管堆增量(段内没发生 GC 时就是段内分配的内存)，单列"每帧分配"排行：托管堆每 10 秒涨几百 MB、
// 隔一会儿就有一两秒的 GC 停顿时，用它找是谁在大量分配。
// 补的三项指标(用户 2026-11-11 "先补指标再做第一档")：尖峰帧标出当帧有没有垃圾回收(帧前后回收计数比较，分清"计时段外"是回收停顿
// 还是原版、渲染)；帧率体检行报托管堆的绝对大小(Mono 回收停顿跟常驻堆大小成正比)；另报一行[贴图上传](见 TextureUploadCounter)。
// 以前分项明细只在无小人模式下写进 EmpireCraft.performance.log，平常模式只进游戏日志；现在开着诊断就都写。
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
        internal long Ticks, Peak, Alloc;
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
    // 窗口 GC 基线；这一帧开始时的回收计数；当帧有回收的尖峰帧数
    private static int _gc0, _gc1, _gc2;
    private static long _heap;
    private static int _frameGc, _gcSpikes;
    // 正在计时的段(嵌套时最里层在末尾)：贴图上传按它归(见 TextureUploadCounter)
    private static readonly List<string> Open = new();
    internal static string CurrentLabel => Open.Count > 0 ? Open[Open.Count - 1] : null;
    internal static string ParentLabel => Open.Count > 1 ? Open[Open.Count - 2] : null;

    public readonly struct Scope : System.IDisposable
    {
        private readonly string _label;
        private readonly long _started, _heap;

        public Scope(string label)
        {
            EmpireCraft.Scripts.Diagnostics.StackGuard.Probe();
            EnsureFrame();
            _label = label;
            Open.Add(label);
            _heap = System.GC.GetTotalMemory(false);
            _started = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_label == null) return;
            long elapsed = Stopwatch.GetTimestamp() - _started;
            for (int i = Open.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Open[i], _label))
                {
                    Open.RemoveAt(i);
                    break;
                }
            // 段内发生了 GC 时增量为负，不计
            long allocated = System.GC.GetTotalMemory(false) - _heap;
            Ticks.TryGetValue(_label, out Sample sample);
            if (allocated > 0) sample.Alloc += allocated;
            sample.Ticks += elapsed;
            sample.Peak = System.Math.Max(sample.Peak, elapsed);
            sample.Calls++;
            Ticks[_label] = sample;
            FrameTicks.TryGetValue(_label, out long frameTicks);
            FrameTicks[_label] = frameTicks + elapsed;
            Report();
        }
    }

    // 性能诊断日志开关(模组设置"性能诊断日志"，随时开关，默认关；用户 2026-10-27 "这个也会掉帧，加个开关让我随时能够开关")：
    // 关着时各计时段不计时、不报每帧耗时与帧率、尖峰帧，原版各处的计时补丁也不计(见 NativeWorldOptimizationPatch、RenderThrottlePatch)；
    // 栈保护照常探(防原生崩溃，见 StackGuard)
    public static bool Enabled;

    public static void SetEnabled(bool on)
    {
        if (Enabled == on) return;
        Enabled = on;
        if (on) EmpireCraft.Scripts.Diagnostics.TextureUploadCounter.EnsurePatched();
        Reset();
    }

    public static Scope Measure(string label)
    {
        if (Enabled) return new(label);
        EmpireCraft.Scripts.Diagnostics.StackGuard.Probe();
        return default;
    }

    // 记一段在别处量好的耗时(跨前缀、后缀量的，比如地块上色)
    public static void Record(string label, long elapsed)
    {
        if (!Enabled) return;
        EnsureFrame();
        Ticks.TryGetValue(label, out Sample sample);
        sample.Ticks += elapsed;
        sample.Peak = System.Math.Max(sample.Peak, elapsed);
        sample.Calls++;
        Ticks[label] = sample;
        FrameTicks.TryGetValue(label, out long frameTicks);
        FrameTicks[label] = frameTicks + elapsed;
    }

    // 原版主循环总耗时(MapBox.Update，包含各"原版·"分项，不参与分项相加)
    public const string MainLoopLabel = "总·原版主循环";

    // 每帧由 ModClass.Update 最先调用：结算刚结束的那一帧
    public static void FrameTick()
    {
        if (!Enabled) return;
        EnsureFrame();
        Report();
    }

    // 新帧的第一次调用(FrameTick 或任何 Measure)把上一帧归档
    private static void EnsureFrame()
    {
        int frame = Time.frameCount;
        if (frame == _currentFrame) return;
        int gc = System.GC.CollectionCount(0);
        // 新帧首次读到的 unscaledDeltaTime 正是刚结束那一帧的时长；两帧开头的回收计数之差就是那一帧里的回收次数
        if (_currentFrame >= 0 && frame == _currentFrame + 1) SettleFrame(Time.unscaledDeltaTime, gc - _frameGc);
        else FrameTicks.Clear();
        _frameGc = gc;
        _currentFrame = frame;
    }

    private static void SettleFrame(float dt, int collections)
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
            if (collections > 0) _gcSpikes++;
            if (_spikeListed < MaxSpikeLines)
            {
                _spikeListed++;
                AppendSpikeDetail(ms, collections);
            }
        }
        FrameTicks.Clear();
    }

    // 尖峰帧归因：列当帧耗时最高的几个计时段；剩余时间在模组计时段之外(原版模拟/渲染或 GC)
    private static void AppendSpikeDetail(double frameMs, int collections)
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
        SpikeDetail.Append(collections > 0 ? $"；本帧垃圾回收 {collections} 次(计时段外的多半是回收停顿)"
            : "；本帧没有垃圾回收(计时段外是原版、渲染或别的模组)");
    }

    public static void Reset()
    {
        Ticks.Clear();
        FrameTicks.Clear();
        Open.Clear();
        _since = -1f;
        _startFrame = 0;
        _currentFrame = -1;
        EmpireCraft.Scripts.Diagnostics.TextureUploadCounter.Reset();
        ResetWindowStats();
    }

    private static void ResetWindowStats()
    {
        _frames = 0; _slowFrames = 0; _spikes = 0; _spikeListed = 0; _gcSpikes = 0;
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
        // 每帧分配排行(嵌套的段会重复计入外层)
        ordered.Sort((a, b) => b.Value.Alloc.CompareTo(a.Value.Alloc));
        var alloc = new StringBuilder();
        int shown = 0;
        foreach (KeyValuePair<string, Sample> pair in ordered)
        {
            double kb = pair.Value.Alloc / 1024d / frames;
            if (kb < 4d || shown >= 12) break;
            shown++;
            alloc.Append($"{pair.Key} {kb:0} KB/帧；");
        }
        string allocReport = alloc.Length > 0 ? $"[EmpireCraft][每帧分配] {alloc}" : null;
        if (allocReport != null) LogService.LogWarning(allocReport);
        string uploadReport = EmpireCraft.Scripts.Diagnostics.TextureUploadCounter.BuildReport(frames);
        if (uploadReport != null) LogService.LogWarning(uploadReport);
        string budgetReport = EmpireCraft.Scripts.HelperFunc.FrameJobs.BuildReport();
        if (budgetReport != null) LogService.LogWarning(budgetReport);
        // 帧率体检行：每个窗口必报(写性能日志)；帧数明显在跳时同时进游戏日志
        string health = BuildFrameHealthLine();
        bool unstable = _spikes > 0 || (_frames > 0 && _slowFrames * 20 >= _frames);
        if (health != null && unstable) LogService.LogWarning(health + SpikeDetail);
        // 开着诊断就写进性能日志(以前只在无小人模式下写，平常模式的玩家回传的性能日志里只有帧率)
        EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(line.Length > 0 ? report :
            "[EmpireCraft][每帧耗时] 被计时片段均低于 0.3 ms/帧且单次峰值低于 4 ms");
        if (allocReport != null) EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(allocReport);
        if (uploadReport != null) EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(uploadReport);
        if (budgetReport != null) EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(budgetReport);
        if (health != null)
            EmpireCraft.Scripts.Diagnostics.PerformanceTraceFile.Record(health + SpikeDetail);
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
        long heapNow = System.GC.GetTotalMemory(false);
        double heapDelta = (heapNow - _heap) / (1024d * 1024d);
        return $"[EmpireCraft][帧率] 均帧 {avgMs:0.0} ms ({fps:0} fps)，最差 {_frameWorst * 1000f:0} ms，" +
               $">33ms 帧 {_slowFrames}/{_frames} ({100d * _slowFrames / _frames:0.0}%)，尖峰(≥50ms) {_spikes} 个(当帧有回收 {_gcSpikes} 个)，" +
               $"GC 0代+{gc0} 1代+{gc1} 2代+{gc2}，托管堆 {heapNow / (1024d * 1024d):0} MB({(heapDelta >= 0 ? "+" : "")}{heapDelta:0.0} MB)" +
               UnityHeapText();
    }

    // Unity 那边看到的 Mono 堆(已用 / 已向系统要的)与引擎总分配；拿不到(返回 0)就不写
    private static string UnityHeapText()
    {
        const double Megabyte = 1024d * 1024d;
        try
        {
            long used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            long heap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
            long total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            string text = heap > 0 ? $"，Mono 已用 {used / Megabyte:0} / 堆 {heap / Megabyte:0} MB" : "";
            if (total > 0) text += $"，引擎共分配 {total / Megabyte:0} MB";
            return text;
        }
        catch (System.Exception)
        {
            return "";
        }
    }
}
