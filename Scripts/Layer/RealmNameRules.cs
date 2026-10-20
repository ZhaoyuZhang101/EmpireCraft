using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.Layer;

// 天下之名(见 RealmNamingSystem、Docs/plan-huaxia-imperial-titles.md)的纯算式：不碰游戏对象，便于单测
public static class RealmNameRules
{
    // 据有帝国法理三分之二(按城数)；帝国法理至少三城才算
    public const int CoreNameMinCities = 3;

    // 方位前缀键(与 OverallHelperFunc.DirectPrefixKeyMap、语言键一致)
    public const string East = "Eastern", West = "Western", South = "Southern", North = "Northern", Later = "Later";

    // 逼教宗加冕：国力至少是教廷的几倍算施压
    public const double PressureRatio = 3d;

    // 新君即位几年仍未由教宗加冕，普世之名收回
    public const int UncrownedRevokeYears = 3;

    // 三分之二向上取整(整数算，免得 3×2/3 算成 2.0000001 再进一位)；不到三城的帝国法理不算
    public static int RequiredCities(int total) => total < CoreNameMinCities ? int.MaxValue : (2 * total + 2) / 3;

    public static bool HoldsShare(int held, int total) => held > 0 && held >= RequiredCities(total);

    // 后者在前者的哪个方向(地图 y 轴朝北)：偏差大的那一轴定东西或南北；重合返回 null
    public static string Direction4(float fromX, float fromY, float toX, float toY)
    {
        float dx = toX - fromX, dy = toY - fromY;
        if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f) return null;
        if (Math.Abs(dx) > Math.Abs(dy)) return dx > 0f ? East : West;
        return dy > 0f ? North : South;
    }

    // 复国方位(用户 2026-10-20 选定)：新法理首都相对祖上法理首都四方位；新法理就是祖上那个法理、或两个首都重合时称"后"
    public static string RestorationPrefix(bool sameTitle, float ancestralX, float ancestralY, float newX, float newY)
    {
        if (sameTitle) return Later;
        return Direction4(ancestralX, ancestralY, newX, newY) ?? Later;
    }

    // 多京：各陪京相对主京的方位，东南西北各用一次(角度离得最近的先挑)；排不下或与主京重合的给 null
    public static string[] AssignDirections(IList<float> dx, IList<float> dy)
    {
        int count = Math.Min(dx?.Count ?? 0, dy?.Count ?? 0);
        var result = new string[count];
        string[] keys = { East, North, West, South };
        double[] centers = { 0d, 90d, 180d, 270d };
        var pairs = new List<(double gap, int index, int dir)>();
        for (int i = 0; i < count; i++)
        {
            if (Math.Abs(dx[i]) < 0.001f && Math.Abs(dy[i]) < 0.001f) continue;
            double angle = Math.Atan2(dy[i], dx[i]) * 180d / Math.PI;
            if (angle < 0d) angle += 360d;
            for (int d = 0; d < 4; d++)
            {
                double gap = Math.Abs(angle - centers[d]);
                if (gap > 180d) gap = 360d - gap;
                pairs.Add((gap, i, d));
            }
        }
        pairs.Sort((a, b) => a.gap != b.gap ? a.gap.CompareTo(b.gap) : a.index != b.index ? a.index.CompareTo(b.index) : a.dir.CompareTo(b.dir));
        var usedDir = new bool[4];
        foreach ((double _, int index, int dir) in pairs)
        {
            if (result[index] != null || usedDir[dir]) continue;
            result[index] = keys[dir];
            usedDir[dir] = true;
        }
        return result;
    }

    // 从天下整名里拆出基名：pattern 是本文明的天下名格式(如"{0}天下""{0}普世帝国")；套不上返回空
    public static string SplitBase(string fullName, string pattern)
    {
        string name = fullName?.Trim() ?? "";
        if (name.Length == 0 || string.IsNullOrEmpty(pattern)) return "";
        int slot = pattern.IndexOf("{0}", StringComparison.Ordinal);
        if (slot < 0) return "";
        string head = pattern.Substring(0, slot), tail = pattern.Substring(slot + 3);
        if (name.Length <= head.Length + tail.Length) return "";
        if (!name.StartsWith(head, StringComparison.Ordinal) || !name.EndsWith(tail, StringComparison.Ordinal)) return "";
        return name.Substring(head.Length, name.Length - head.Length - tail.Length).Trim();
    }

    // 施压：国力至少是教廷的 PressureRatio 倍(教廷国力为 0 时只要自己有国力)
    public static bool PressureMet(double power, double papacyPower) =>
        power > 0d && power >= PressureRatio * Math.Max(0d, papacyPower);

    // 新君未冕满几年收回普世之名
    public static bool UncrownedTooLong(double yearsSinceAccession) => yearsSinceAccession >= UncrownedRevokeYears;
}
