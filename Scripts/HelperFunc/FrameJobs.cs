using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

// 全局帧预算作业(用户 2026-11-11 第一档③"全局帧预算")：模组各系统的每帧入口(战略调度、人口、牧业……以及战略调度里各个按月、按年结算的子系统)
// 都从这里过，共用 SimulationFrameBudget 那一份每帧预算(1~4 毫秒随帧率调，快进按倍数放宽)。
// 分三档：高档(军团、战略调度)用得满预算；中档(月结)用到八成五；低档(年结、后台杂活)只用前六成——预算紧时低档先让。
// 挤不上就顺延到下一帧；每档顺延有上限(高档 4 帧，中档 20 帧，低档 90 帧；另按真实时间兜底 0.5 / 2 / 6 秒)，到了不看预算硬跑一次，谁也不会饿死。
// 一帧至多硬跑一项，其余再让一帧(等过了兜底时间两倍的不让)。2026-11-24 群友"11/5 那版更流畅，之后越来越卡"：
// 以前兜底是 0.1 / 0.5 / 3 秒，帧率掉到十帧以下时高档每帧都硬跑；卡过半秒的一帧之后，所有中档又挤在下一帧一起硬跑——
// 越卡硬跑越多，正好把本来要拆开的"同一帧整批结算"又凑了回来。
// 以前月初、年初几个子系统撞在同一帧整批结算，战略调度一帧几十毫秒(11-05 日志峰值 57 毫秒)；现在各子系统还是整批结，但撞上了就一帧一个往后排。
// 被顺延的子系统只是晚几帧才查"到期没有"——它们都按游戏日月或真实时间判断，不靠每帧累加时间，晚几帧不丢东西
public static class FrameJobs
{
    public enum Priority
    {
        High,
        Normal,
        Low
    }

    private static readonly double[] Share = { 1d, 0.85d, 0.6d };
    private static readonly int[] MaxSkipFrames = { 4, 20, 90 };
    private static readonly float[] MaxSkipSeconds = { 0.5f, 2f, 6f };
    // 这一帧已经硬跑过一项
    private static int _forcedFrame = -1;

    private sealed class State
    {
        public int Frame;
        public float At;
        public int Deferred, Forced;
    }

    private static readonly Dictionary<string, State> States = new();

    // 跑这一项(这一帧还没跑过、预算里这一档还有余量，或者已经顺延到头)；跑了返回 true。
    // measure：外面套一层同名计时段(子系统自己里面已经分段计时的传 false，免得日志里重复计)
    public static bool Run(string key, Priority priority, Action tick, bool measure = true)
    {
        int frame = Time.frameCount;
        float now = Time.realtimeSinceStartup;
        if (!States.TryGetValue(key, out State state))
            States[key] = state = new State { Frame = frame - 1, At = now };
        if (state.Frame == frame) return false;
        int level = (int)priority;
        if (!SimulationFrameBudget.HasShare(Share[level]))
        {
            float waited = now - state.At;
            if (frame - state.Frame < MaxSkipFrames[level] && waited < MaxSkipSeconds[level] ||
                _forcedFrame == frame && waited < MaxSkipSeconds[level] * 2f)
            {
                state.Deferred++;
                return false;
            }
            state.Forced++;
            _forcedFrame = frame;
        }
        state.Frame = frame;
        state.At = now;
        using (SimulationFrameBudget.Measure())
        {
            if (measure)
                using (FrameProfiler.Measure(key))
                    tick();
            else tick();
        }
        return true;
    }

    // 性能诊断日志一个窗口一行：预算多大、顺延与硬跑各几次、顺延最多的几项；报完清零(没有顺延过返回 null)
    public static string BuildReport()
    {
        int deferred = 0, forced = 0;
        foreach (State state in States.Values)
        {
            deferred += state.Deferred;
            forced += state.Forced;
        }
        if (deferred == 0 && forced == 0) return null;
        var ordered = new List<KeyValuePair<string, State>>(States);
        ordered.Sort((a, b) => b.Value.Deferred.CompareTo(a.Value.Deferred));
        var line = new StringBuilder();
        line.Append($"[EmpireCraft][帧预算] 每帧预算 {SimulationFrameBudget.BudgetMilliseconds:0.0} ms，" +
                    $"顺延 {deferred} 次、顺延到头硬跑 {forced} 次；");
        for (int i = 0; i < ordered.Count && i < 6; i++)
        {
            if (ordered[i].Value.Deferred == 0 && ordered[i].Value.Forced == 0) break;
            line.Append($"{ordered[i].Key} 顺延 {ordered[i].Value.Deferred}、硬跑 {ordered[i].Value.Forced}；");
        }
        foreach (State state in States.Values) state.Deferred = state.Forced = 0;
        return line.ToString();
    }

    public static void Reset()
    {
        States.Clear();
        _forcedFrame = -1;
    }
}
