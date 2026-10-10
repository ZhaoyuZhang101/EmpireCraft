using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国库预算统筹：各城每月轮流结算治理、驻军、野战军费，同一国家国库原先先到先得，
// 先结算的城可能把钱用光，首都、前线反而断饷。现在每个付款国家按月统筹：
//   需求 = 上个月各城付到这一级时还差的钱(真实需求，不是预估)，分“要地”与“一般地方”两类；
//   要地(首都、与交战敌国接壤的前线、稳定度低于叛乱线的城)足额；
//   一般地方按 (月初余额 - 要地需求) / 一般地方需求 的比例拨付，付不上的照旧记欠饷。
//   没有上月记录(刚读档、新国家)时不限额。偿还旧欠款本就只用安全储备以上的钱，不在此列。
// 只在付款时读写，按付款国家记一行，不扫描全国、不进存档。
public static class FiscalBudgetPlanner
{
    private sealed class Plan
    {
        public double Start = -1d;
        public long Balance;
        public double NormalRatio = 1d;
        // 本月累计需求(下个月用来定比例)
        public long PriorityNeed, NormalNeed;
        // 上月需求(界面显示)
        public long LastPriorityNeed, LastNormalNeed;
    }

    private static readonly Dictionary<long, Plan> Plans = new();
    private static MapBox _world;

    public static void ResetWorldState()
    {
        Plans.Clear();
        _world = null;
    }

    private static Plan Get(Kingdom payer)
    {
        if (!ReferenceEquals(_world, World.world)) { Plans.Clear(); _world = World.world; }
        if (!Plans.TryGetValue(payer.id, out Plan plan)) Plans[payer.id] = plan = new Plan();
        double now = World.world.getCurWorldTime();
        if (plan.Start < 0d || now < plan.Start || Date.getMonthsSince(plan.Start) >= 1)
        {
            bool hasHistory = plan.Start >= 0d;
            plan.LastPriorityNeed = plan.PriorityNeed;
            plan.LastNormalNeed = plan.NormalNeed;
            plan.PriorityNeed = plan.NormalNeed = 0L;
            plan.Start = now;
            plan.Balance = Math.Max(0L, payer.GetTreasuryBalance());
            long left = plan.Balance - plan.LastPriorityNeed;
            plan.NormalRatio = !hasHistory || plan.LastNormalNeed <= 0L ? 1d
                : Math.Max(0d, Math.Min(1d, left / (double)plan.LastNormalNeed));
        }
        return plan;
    }

    public static bool IsPriority(City city, Kingdom payer)
    {
        if (city?.data == null || city.isRekt()) return false;
        Kingdom owner = city.kingdom;
        if (city == payer.capital || city == owner?.capital || city == owner?.GetEmpire()?.CoreKingdom?.capital) return true;
        var state = city.GetOrCreate().stability;
        if (state != null && state.stability < CityStabilityRules.RebellionThreshold) return true;
        if (owner == null || !owner.hasEnemies() || city.neighbours_kingdoms == null) return false;
        foreach (Kingdom neighbour in city.neighbours_kingdoms)
            if (neighbour != null && !neighbour.isRekt() && owner.isInWarWith(neighbour)) return true;
        return false;
    }

    // 付到 payer 这一级时，本城这笔账最多能从 payer 拿多少(remaining 为此时还差的钱)
    public static int Allowance(City city, Kingdom payer, int remaining)
    {
        if (remaining <= 0 || payer == null) return 0;
        Plan plan = Get(payer);
        if (IsPriority(city, payer))
        {
            plan.PriorityNeed = SafeAdd(plan.PriorityNeed, remaining);
            return remaining;
        }
        plan.NormalNeed = SafeAdd(plan.NormalNeed, remaining);
        return plan.NormalRatio >= 1d ? remaining : (int)Math.Ceiling(remaining * plan.NormalRatio);
    }

    // 界面：一般地方本月的拨付比例(1 = 足额)
    public static double NormalRatio(Kingdom payer) =>
        payer != null && Plans.TryGetValue(payer.id, out Plan plan) ? plan.NormalRatio : 1d;

    private static long SafeAdd(long a, int b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}
