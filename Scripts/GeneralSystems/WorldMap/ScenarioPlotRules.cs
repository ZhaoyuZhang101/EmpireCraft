using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧情人物要活到自己的戏份(见 ScenarioEventSystem.Shield)：不碰游戏对象，单独测
public static class ScenarioPlotRules
{
    // 事件迟迟等不到条件(岳飞收复了汴京，不再议和)也不能永远不死：过了预定年份这么多年就放手
    public const int GraceYears = 8;

    public static readonly char[] NameSeparators = { '、', ',', '，' };

    public static IEnumerable<string> Names(string field)
    {
        if (string.IsNullOrWhiteSpace(field)) yield break;
        foreach (string part in field.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part.Trim();
            if (name.Length > 0) yield return name;
        }
    }

    // 还没发生的事里点了名的人：编号 → 护到开局后第几年；一个人有几场戏按最晚的算
    public static Dictionary<long, int> ProtectUntil(IEnumerable<(int after, bool fired, IEnumerable<string> personFields)> events,
        IReadOnlyDictionary<string, long> people)
    {
        var until = new Dictionary<long, int>();
        foreach ((int after, bool fired, IEnumerable<string> personFields) in events)
        {
            if (fired) continue;
            foreach (string field in personFields)
            foreach (string name in Names(field))
            {
                if (!people.TryGetValue(name, out long id)) continue;
                int year = after + GraceYears;
                if (!until.TryGetValue(id, out int have) || year > have) until[id] = year;
            }
        }
        return until;
    }

    public static bool Protected(IReadOnlyDictionary<long, int> until, long id, int years) =>
        until.TryGetValue(id, out int year) && years < year;
}
