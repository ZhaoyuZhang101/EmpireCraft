using UnityEngine;
using UnityEngine.Scripting;

namespace EmpireCraft.Scripts.GeneralSystems;

// 垃圾回收节奏(用户 2026-10-27"还是太卡了")：游戏开着增量 GC(boot.config 的 gc-max-time-slice=3，每帧至多 3 毫秒)，
// 原版与模组每秒分配几十 MB 时增量标记追不上，Boehm 退回整段停顿回收，一停一两秒——性能日志里平常的帧五六十毫秒，
// 隔一会儿就有一帧 800~1000 毫秒全在主循环之外，托管堆十秒涨两三百 MB，平均帧数被这几帧拖到个位数。
// 每秒按托管堆的增长速度调每帧的增量时间片：分配得快就多给(按 FastAllocMb 一档加 BaseSlice，至多 MaxSlice)，
// 帧本身已经很长时按帧长的 MaxFrameShare 封顶——宁可每帧多花几毫秒，也不整段停一两秒；分配慢了回到游戏原来的时间片。
public static class GcPacing
{
    private const float SampleSeconds = 1f;
    private const double FastAllocMb = 6d, Smoothing = 0.3d, MaxFrameShare = 0.12d;
    private const ulong MinSlice = 3_000_000UL, MaxSlice = 15_000_000UL;

    private static float _since = -1f;
    private static long _heap;
    private static double _rate;
    private static ulong _original;
    private static bool _captured;

    // 每帧(ModClass.Update)
    public static void Tick()
    {
        if (!GarbageCollector.isIncremental) return;
        if (!_captured)
        {
            _original = GarbageCollector.incrementalTimeSliceNanoseconds;
            _captured = true;
        }
        float now = Time.unscaledTime;
        if (_since >= 0f && now >= _since && now - _since < SampleSeconds) return;
        long heap = global::System.GC.GetTotalMemory(false);
        if (_since < 0f || now < _since)
        {
            _since = now;
            _heap = heap;
            return;
        }
        double grown = (heap - _heap) / (1024d * 1024d) / (now - _since);
        _since = now;
        _heap = heap;
        // 这一秒里回收过，堆变小了：不算数
        if (grown < 0d) return;
        _rate = _rate <= 0d ? grown : _rate * (1d - Smoothing) + grown * Smoothing;
        ulong baseSlice = global::System.Math.Max(MinSlice, _original);
        ulong slice = baseSlice;
        if (_rate > FastAllocMb)
        {
            slice = (ulong)(baseSlice * global::System.Math.Min(_rate / FastAllocMb, MaxSlice / (double)baseSlice));
            ulong frameCap = (ulong)(Time.smoothDeltaTime * 1e9 * MaxFrameShare);
            slice = global::System.Math.Max(baseSlice, global::System.Math.Min(slice, global::System.Math.Max(baseSlice, frameCap)));
        }
        if (slice != GarbageCollector.incrementalTimeSliceNanoseconds)
            GarbageCollector.incrementalTimeSliceNanoseconds = slice;
    }
}
