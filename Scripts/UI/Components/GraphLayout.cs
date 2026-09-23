using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// "科技树"式布局：节点按 lane(车道/分支) 分列、按 tier(等级/世代) 分行——tier 落在
// rootTier 以内的节点当根节点、单独一行居中；其余节点各自落在自己车道那一列里，
// 同一车道同一层如果有好几个节点，就在这一格里再拆几个子列摆开。
// 从 InstitutionGraphView 原来内嵌的分支排列算法搬出来、把"branch/advancement"这两个
// 专属字段换成通用的 Lane/Tier，这样任何"节点按类别分道、按等级分层"形状的树状图
// (不只是文明制度)都能直接复用这份布局，不用再抄一遍这套折算车道宽度/居中的数学。
public static class GraphLayout
{
    public readonly struct Node
    {
        public readonly string Id;
        public readonly string Lane;
        public readonly int Tier;

        public Node(string id, string lane, int tier)
        {
            Id = id;
            Lane = lane;
            Tier = tier;
        }
    }

    public readonly struct Result
    {
        public readonly Dictionary<string, Vector2> Positions;
        public readonly Dictionary<string, float> LaneCenters;
        public readonly float TotalWidth;

        public Result(Dictionary<string, Vector2> positions, Dictionary<string, float> laneCenters,
            float totalWidth)
        {
            Positions = positions;
            LaneCenters = laneCenters;
            TotalWidth = totalWidth;
        }
    }

    // laneOrder：车道的固定顺序(配置里出现但没列进来的车道，会按名字排在后面，不会丢)。
    // rootTier：小于等于这个值的节点算根节点，单独居中置顶一行，不占车道。
    public static Result TieredLanes(IReadOnlyList<Node> nodes, IList<string> laneOrder,
        float nodeWidth, float nodeHeight, float laneGap, float rowGap, int rootTier = 1)
    {
        var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        List<Node> nonRoot = nodes.Where(n => n.Tier > rootTier).ToList();

        List<string> lanes = nonRoot.Select(n => n.Lane).Distinct()
            .OrderBy(lane => laneOrder.IndexOf(lane) is var index && index >= 0 ? index : laneOrder.Count)
            .ThenBy(lane => lane, StringComparer.Ordinal).ToList();

        var subColumns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string lane in lanes)
        {
            subColumns[lane] = Math.Max(1, nonRoot.Where(n => n.Lane == lane)
                .GroupBy(n => n.Tier).Select(group => group.Count()).DefaultIfEmpty(1).Max());
        }

        var laneCenter = new Dictionary<string, float>(StringComparer.Ordinal);
        float cursor = 0f;
        foreach (string lane in lanes)
        {
            float width = subColumns[lane] * nodeWidth + (subColumns[lane] - 1) * laneGap;
            laneCenter[lane] = cursor + width / 2f;
            cursor += width + laneGap * 2f;
        }
        float totalWidth = Math.Max(nodeWidth, cursor - laneGap * 2f);
        foreach (string lane in lanes) laneCenter[lane] -= totalWidth / 2f;

        float RowY(int tier) => -tier * (nodeHeight + rowGap);

        List<Node> roots = nodes.Where(n => n.Tier <= rootTier)
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        for (int i = 0; i < roots.Count; i++)
        {
            float x = (i - (roots.Count - 1) / 2f) * (nodeWidth + laneGap);
            positions[roots[i].Id] = new Vector2(x, RowY(rootTier));
        }

        foreach (string lane in lanes)
        {
            foreach (IGrouping<int, Node> tier in nonRoot.Where(n => n.Lane == lane).GroupBy(n => n.Tier))
            {
                List<Node> row = tier.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
                for (int i = 0; i < row.Count; i++)
                {
                    float x = laneCenter[lane] + (i - (row.Count - 1) / 2f) * (nodeWidth + laneGap);
                    positions[row[i].Id] = new Vector2(x, RowY(tier.Key));
                }
            }
        }

        return new Result(positions, laneCenter, totalWidth);
    }
}
