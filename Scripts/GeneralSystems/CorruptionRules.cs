using System;

namespace EmpireCraft.Scripts.GeneralSystems;

internal static class CorruptionRules
{
    public static double Bound(double value) => double.IsNaN(value) ? 0d : Math.Max(0d, Math.Min(1d, value));
    public static double Realm(double central, double provinces, double cities) =>
        Bound(Bound(central) * 0.4d + Bound(provinces) * 0.3d + Bound(cities) * 0.3d);

    // 沿用个人品行决定的贪腐增长；清廉任职可以逐年整顿，不因换人瞬间清零。
    public static double AnnualOfficeChange(double corruptibility, bool inEmpire) => !inEmpire ? -0.2d
        : Bound(corruptibility) > 0d ? Bound(corruptibility) * 0.1d : -0.01d;

    public static int RecoveredMoney(int before, int after, int treasury) => (int)Math.Max(0L,
        Math.Min(Math.Max(0L, (long)Math.Max(0, before) - Math.Max(0, after)), (long)int.MaxValue - treasury));
}
