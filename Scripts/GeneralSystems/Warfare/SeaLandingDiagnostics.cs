using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诊断(玩家反馈 2026-10-16："远洋打仗还是有问题，两个隔海的国家一千五百年没打下来")：每年给日志记几行[渡海诊断]，
// 查跨海的仗卡在哪一关。只记与本方没有陆上接触的敌国(见 FrontSystem.Sectors)，每对交战国一年至多一行、全图一年至多 MaxLines 行：
// 登陆目标与航线(或卡在"无航线"、"够不着")、能上阵的军、运力与最小的一支军、分出的先遣军、在船上的军、靠岸次数、断了补给的军。
// 只记录，不改任何东西
public static class SeaLandingDiagnostics
{
    private const int MaxLines = 10;

    private sealed class PairState
    {
        public string Side, Hostile, Target, Wanted;
        public int Route, Months, Landable;
    }

    private sealed class SideState
    {
        public int Ready, Vanguards, Landed;
        public float Shipping, Smallest;
    }

    private static readonly Dictionary<(long side, long hostile), PairState> Pairs = new();
    private static readonly Dictionary<long, SideState> Sides = new();
    private static int _year = int.MinValue;

    public static void ResetWorldState()
    {
        Pairs.Clear();
        Sides.Clear();
        _year = int.MinValue;
    }

    private static SideState Of(long id)
    {
        if (!Sides.TryGetValue(id, out SideState state)) Sides[id] = state = new SideState();
        return state;
    }

    // 本月对这个敌国的登陆：target 选中的城(没有为空)，wanted 第一座够得着的跨海的城，route 航线经过几个海区
    public static void Pair(Kingdom side, Kingdom hostile, City target, City wanted, int route)
    {
        if (side?.data == null || hostile?.data == null) return;
        if (!Pairs.TryGetValue((side.id, hostile.id), out PairState pair))
        {
            if (Pairs.Count >= 256) return;
            Pairs[(side.id, hostile.id)] = pair = new PairState();
        }
        pair.Side = side.GetKingdomName();
        pair.Hostile = hostile.GetKingdomName();
        pair.Months++;
        if (target != null)
        {
            pair.Landable++;
            pair.Target = target.GetCityName();
            pair.Route = route;
        }
        if (wanted != null) pair.Wanted = wanted.GetCityName();
    }

    // 本月有登陆战线时：能上阵的军(取一年里最多的一次)、运力、最小的一支军
    public static void Deployable(Kingdom side, List<CorpsData> ready, float shipping)
    {
        if (side?.data == null) return;
        SideState state = Of(side.id);
        state.Ready = Mathf.Max(state.Ready, ready.Count);
        state.Shipping = shipping;
        state.Smallest = ready.Count == 0 ? 0f : ready.Min(corps => corps.strength);
    }

    public static void Vanguard(Kingdom side)
    {
        if (side?.data != null) Of(side.id).Vanguards++;
    }

    public static void Landed(long kingdomId)
    {
        if (Sides.TryGetValue(kingdomId, out SideState state)) state.Landed++;
    }

    // 每月(战线结算前)：过了一年就把去年的记下来
    public static void MonthTick()
    {
        int year = Date.getCurrentYear();
        if (_year == int.MinValue || year == _year)
        {
            _year = year;
            return;
        }
        _year = year;
        try { Report(); }
        finally
        {
            Pairs.Clear();
            Sides.Clear();
        }
    }

    private static void Report()
    {
        int lines = 0;
        foreach (var entry in Pairs.OrderByDescending(entry => entry.Value.Landable).ThenByDescending(entry => entry.Value.Wanted != null))
        {
            if (lines++ >= MaxLines) break;
            PairState pair = entry.Value;
            Kingdom side = World.world.kingdoms.get(entry.Key.side);
            Sides.TryGetValue(entry.Key.side, out SideState state);
            List<CorpsData> corps = CorpsSystem.Of(side);
            int embarked = corps.Count(item => item.embarked);
            int cut = side == null ? 0 : corps.Count(item => NavySystem.IsCutOff(side, item));
            string goal = pair.Target != null
                ? $"登陆{pair.Target}(航线{pair.Route}/{(side == null ? 0 : NavySystem.MaxRange(side))}海区，{pair.Landable}/{pair.Months}月可登陆)"
                : pair.Wanted != null
                    ? $"{pair.Wanted}无可靠岸的海滩(无港口、敌方制海截断或超出航程)"
                    : $"没有够得着的城(水师辐射{Mathf.RoundToInt(StraitReach.Radius(side))}格)";
            LogService.LogInfo($"[EmpireCraft][渡海诊断] {pair.Side}→{pair.Hostile}：{goal}；可战军{state?.Ready ?? 0}支，" +
                               $"运力{Mathf.RoundToInt(state?.Shipping ?? 0f)}人/最小军{Mathf.RoundToInt(state?.Smallest ?? 0f)}人，" +
                               $"先遣{state?.Vanguards ?? 0}，在船{embarked}，靠岸{state?.Landed ?? 0}，断补给{cut}");
        }
    }
}
