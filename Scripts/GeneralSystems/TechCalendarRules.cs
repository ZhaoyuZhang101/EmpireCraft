using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems;

// 剧本世界的日历(随存档；不是剧本开局的世界没有)：开局年份、开局时刻、剧本本身开局的最高科技档
public sealed class TechCalendarSave
{
    public int year;
    public double started;
    public int floor;
}

// 剧本世界的科技按年代封顶(用户 2026-11-06 转群友"这春秋世界怎么印度开局有飞机和装甲车了""科技不符合时代")：
// 研究只受文明等级和超时代费用约束，人多的文化几百年就从铁器走到内燃机(存档：铁器开局 214 年电气、391 年内燃机)。
// TechTree.json 的 eras[].year 是这一时代的科技从公元几年起才能研究，没写的不限；一档没到，更高的也不开。
// 剧本本身开局档次更高的(自定剧本、从当前世界导入)以开局的档次作保底，不把作者定的世界卡住。
public static class TechCalendarRules
{
    public const int NoLimit = int.MaxValue;

    // 开局年过了多少年是公元几年(公元前后之间没有 0 年)
    public static int YearAfter(int startYear, int elapsed)
    {
        int year = startYear + Math.Max(0, elapsed);
        return startYear < 0 && year >= 0 ? year + 1 : year;
    }

    // 这一年最高能研究到哪一档；eras 按档次从低到高，opens 为这一档开放的年份(空为不限)
    public static int MaxTier(int year, int floor, IEnumerable<(int tier, int? opens)> eras)
    {
        int reached = 0;
        foreach ((int tier, int? opens) in eras)
        {
            if (opens.HasValue && opens.Value > year) return Math.Max(floor, reached);
            reached = tier;
        }
        return NoLimit;
    }
}
