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
// 只记录，不改任何东西。
// 2026-10-21(群友"好像就没见过水军打登陆战")补：航线 0 写明是陆路接敌(与都城同一片陆地、不必渡海)，不再像卡在航线上；
// 航程在记录当月取(以前年底才取，国已亡的写成 0)；没有海滩的写明原因(没有出海港口、海路不通、敌方制海截断、超出航程)；
// 每月派没派兵、没派的原因(守城吃紧、缺运力、兵力不足……，见 FrontSystem.Landing)按月计数，取最多的三条
public static class SeaLandingDiagnostics
{
    private const int MaxLines = 10;

    private sealed class PairState
    {
        public string Side, Hostile, Target, Wanted, NoRoute;
        public int Route, Range, Months, Landable, Sent, SentMonths, GoingMonths;
        // 同一片陆地上隔着第三国、沿海渡海
        public bool Around;
        // 没派的原因(括号前的部分) → (几个月, 最近一次的全文)
        public readonly Dictionary<string, (int months, string text)> Why = new();
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

    // 本月对这个敌国的登陆：target 选中的城(没有为空)，wanted 第一座够得着的跨海的城，route 航线经过几个海区(陆路接敌为 0)，
    // around：同一片陆地上隔着第三国、沿海渡海
    public static void Pair(Kingdom side, Kingdom hostile, City target, City wanted, int route, bool around = false)
    {
        if (side?.data == null || hostile?.data == null) return;
        if (!Pairs.TryGetValue((side.id, hostile.id), out PairState pair))
        {
            if (Pairs.Count >= 256) return;
            Pairs[(side.id, hostile.id)] = pair = new PairState();
        }
        pair.Side = side.GetKingdomName();
        pair.Hostile = hostile.GetKingdomName();
        pair.Range = NavySystem.MaxRange(side);
        pair.Months++;
        if (target != null)
        {
            pair.Landable++;
            pair.Target = target.GetCityName();
            pair.Route = route;
            pair.Around = around;
        }
        if (wanted != null) pair.Wanted = wanted.GetCityName();
        // 找不到海滩：一年里头一回查一下为什么
        if (target == null && wanted != null && pair.NoRoute == null)
            pair.NoRoute = SeaLanding.WhyNoBeachhead(side, wanted.getTile()?.zone);
    }

    // 本月这一路登陆：新派了几支军(sent)、上月派的还在路上或船上(going)；一支没派时 why 写原因
    public static void Landing(Kingdom side, Kingdom hostile, int sent, bool going, string why)
    {
        if (side?.data == null || hostile?.data == null || !Pairs.TryGetValue((side.id, hostile.id), out PairState pair)) return;
        if (sent > 0)
        {
            pair.SentMonths++;
            pair.Sent += sent;
        }
        else if (going) pair.GoingMonths++;
        if (string.IsNullOrEmpty(why)) return;
        int cut = why.IndexOf('(');
        string key = cut > 0 ? why.Substring(0, cut) : why;
        pair.Why.TryGetValue(key, out var entry);
        pair.Why[key] = (entry.months + 1, why);
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
            // 可登陆 L/M 月：一年里查了 M 个月(交战、轮到这一路)，其中 L 个月有能登陆的城与航线
            string goal = pair.Target != null
                ? pair.Route > 0
                    ? $"登陆{pair.Target}({(pair.Around ? "同一片陆地隔着第三国，沿海" : "")}渡海，航线{pair.Route}/{pair.Range}海区，" +
                      $"{pair.Landable}/{pair.Months}月可登陆)"
                    : $"陆路接敌{pair.Target}(与都城同一片陆地、陆路走得通，不必渡海，{pair.Landable}/{pair.Months}月)"
                : pair.Wanted != null
                    ? $"{pair.Wanted}无可靠岸的海滩({pair.NoRoute ?? "原因不明"}，航程{pair.Range}海区)"
                    : $"没有够得着的城(水师辐射{Mathf.RoundToInt(StraitReach.Radius(side))}格)";
            string why = pair.Why.Count == 0 ? ""
                : "；没派：" + string.Join("、", pair.Why.Values.OrderByDescending(item => item.months).Take(3)
                    .Select(item => $"{item.text}{item.months}月"));
            LogService.LogInfo($"[EmpireCraft][渡海诊断] {pair.Side}→{pair.Hostile}：{goal}；可战军{state?.Ready ?? 0}支，" +
                               $"运力{Mathf.RoundToInt(state?.Shipping ?? 0f)}人/最小军{Mathf.RoundToInt(state?.Smallest ?? 0f)}人，" +
                               $"派兵{pair.SentMonths}月共{pair.Sent}支、在途{pair.GoingMonths}月，" +
                               $"先遣{state?.Vanguards ?? 0}，在船{embarked}，靠岸{state?.Landed ?? 0}，断补给{cut}{why}");
        }
    }
}
