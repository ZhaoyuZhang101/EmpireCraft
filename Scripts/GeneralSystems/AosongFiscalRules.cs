using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 澳宋的财政节奏(用户 2026-11-04 问答定"修调度、澳宋每月结算、发流通券")：
//   · 每月优先结算：一座城走完整套月结要几十毫秒，全图一个月走不了太多。澳宋的城排在普通队列之外，
//     每走 PriorityEvery 座普通的城至多插一座，城多的时候轮得慢一点，全图的结算不至于被它拖住；
//   · 流通券：国库不到 ScripTriggerMonths 个月军饷就从民间存款筹，筹到 ScripTargetMonths 个月，不用还；
//     一座城一个月至多筹它存款的 ScripCityShare，民间没钱了才会真的欠饷。
public static class AosongFiscalRules
{
    public const int PriorityEvery = 3;
    public const int ScripTriggerMonths = 2, ScripTargetMonths = 4;
    public const double ScripCityShare = 0.01d;

    // 下一座该走澳宋的城：有城在等，而且普通队列空了，或已经走了 PriorityEvery 座
    public static bool TakePriority(int priorityWaiting, int pendingWaiting, int sincePriority) =>
        priorityWaiting > 0 && (pendingWaiting <= 0 || sincePriority >= PriorityEvery);

    // 这个月要筹多少：够 ScripTriggerMonths 个月就不筹
    public static long ScripWanted(long treasury, long monthlyBill)
    {
        if (monthlyBill <= 0L || treasury >= monthlyBill * ScripTriggerMonths) return 0L;
        return monthlyBill * ScripTargetMonths - Math.Max(0L, treasury);
    }

    public static long ScripFromCity(long wanted, float savings)
    {
        if (wanted <= 0L || float.IsNaN(savings) || float.IsInfinity(savings) || savings <= 0f) return 0L;
        return Math.Min(wanted, (long)Math.Floor((double)savings * ScripCityShare));
    }
}
