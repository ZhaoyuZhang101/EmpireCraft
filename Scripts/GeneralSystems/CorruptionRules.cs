using System;

namespace EmpireCraft.Scripts.GeneralSystems;

internal static class CorruptionRules
{
    public static double Bound(double value) => double.IsNaN(value) ? 0d : Math.Max(0d, Math.Min(1d, value));
    public static double Realm(double central, double provinces, double cities) =>
        Bound(Bound(central) * 0.4d + Bound(provinces) * 0.3d + Bound(cities) * 0.3d);

    // 沿用个人品行决定的贪腐增长；清廉任职可以逐年整顿，不因换人瞬间清零。
    // 清官按品行每年整顿 2%~10%(以前一律只降 1%，贪官却每年最多涨 10%，几十年后州县普遍贪到顶)
    public static double AnnualOfficeChange(double corruptibility, bool inEmpire, double integrity = 0d) =>
        !inEmpire ? -0.2d
        : Bound(corruptibility) > 0d ? Bound(corruptibility) * 0.1d : -(0.02d + Bound(integrity) * 0.08d);

    // 征税时被截留的份额：腐败率 100% 时截走一半(以前全部截走，州县税款整笔进了官员私囊)
    public static double Leak(double rate) => Bound(rate) * 0.5d;

    public static int RecoveredMoney(int before, int after, int treasury) => (int)Math.Max(0L,
        Math.Min(Math.Max(0L, (long)Math.Max(0, before) - Math.Max(0, after)), (long)int.MaxValue - treasury));
}
