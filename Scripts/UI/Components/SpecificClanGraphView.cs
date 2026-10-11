using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 宗族关系的树状图视图。横向排列：一代人一列，从左到右是 上一辈(父/母) → 本辈(兄弟姐妹 +
// 本人 + 配偶) → 下一辈(本人与兄弟姐妹各自的子女) → 下二辈(本人子女各自的子女)；同一列里
// 的人纵向排开。每一辈的子女分组直接复用 SpecificClanManager.getChildren(某人)，不用反推
// father/mother 字段去猜父母是谁——这样嫡庶、多个配偶的情况也不会连错线。
// 视口/拖拽/缩放/连线这些跟"这是一棵家谱"无关的活丢给通用的 GraphView(见 GraphView.cs)；
// 这个类只留家谱这门业务专属的坐标算法。节点长什么样完全由调用方决定(SpecificClanWindow
// 直接复用卡片视图那个 ShowPersonalInfo，边框、头像、信息栏原样搬过来，这里只算坐标、画连线)。
// 这个类本身不再是 MonoBehaviour——它只是 GraphView 的一层薄壳。
public class SpecificClanGraphView
{
    public const float CardWidth = 100f;
    public const float CardHeight = 50f;
    private const float ColGap = 34f;
    private const float RowGap = 8f;
    private static readonly Vector2 CardHalfSize = new(CardWidth / 2f, CardHeight / 2f);

    private GraphView _engine;

    public Transform ContentTransform => _engine.ContentTransform;

    public static SpecificClanGraphView Create(Transform parent, Vector2 size)
    {
        var view = new SpecificClanGraphView();
        view._engine = GraphView.Create(parent, size, GraphOrientation.Horizontal,
            objectName: "SpecificClanGraphViewport");
        view._engine.SetClampMargin(CardHalfSize);
        return view;
    }

    // placeCard(identity, relation, position)：调用方负责把实际卡片(比如 ShowPersonalInfo
    // 建出来的那一套)搬到 ContentTransform 下、摆到给定的 anchoredPosition。这个方法只算
    // 坐标、清空重画连线，不建任何看得见的节点。
    public void Rebuild(PersonalClanIdentity center, Action<PersonalClanIdentity, ClanRelation, Vector2> placeCard)
    {
        _engine.ClearNodes();
        _engine.ClearEdges();
        if (center == null)
        {
            _engine.SetContent(Vector2.one, 1f, Vector2.zero);
            return;
        }

        var positions = new Dictionary<long, Vector2>();
        var identities = new Dictionary<long, (PersonalClanIdentity identity, ClanRelation relation)>();

        // ── 本代：兄弟姐妹(靠上) + 本人(居中) + 配偶(靠下)，同一列纵向排开 ──
        List<(ClanRelation, PersonalClanIdentity)> siblings = SpecificClanManager
            .GetSiblingsWithRelation(center).Where(item => item.Item2 != null)
            .OrderBy(item => item.Item2.rank).ToList();
        var spouses = new List<(ClanRelation, PersonalClanIdentity)>();
        if (center.hasLover())
        {
            PersonalClanIdentity lover = SpecificClanManager.getPerson(center.lover.identity);
            if (lover != null) spouses.Add((ClanRelation.LOV, lover));
        }
        foreach (var cob in center.concubines)
        {
            PersonalClanIdentity concubine = SpecificClanManager.getPerson(cob.identity);
            if (concubine != null) spouses.Add((ClanRelation.COB, concubine));
        }

        var sameCol = new List<(ClanRelation relation, PersonalClanIdentity identity)>();
        sameCol.AddRange(siblings);
        sameCol.Add((ClanRelation.SELF, center));
        sameCol.AddRange(spouses);

        float cursor = 0f;
        float centerY = 0f;
        foreach (var (relation, identity) in sameCol)
        {
            if (identity.id == center.id) centerY = cursor;
            positions[identity.id] = new Vector2(0f, cursor);
            identities[identity.id] = (identity, relation);
            cursor -= CardHeight + RowGap;
        }
        // 整列平移，让本人落在 y = 0
        foreach (long id in positions.Keys.ToList()) positions[id] -= new Vector2(0f, centerY);

        // ── 上一代：父母，摆在本人这一列的左边 ──
        PersonalClanIdentity father = SpecificClanManager.getPerson(center.father);
        PersonalClanIdentity mother = SpecificClanManager.getPerson(center.mother);
        if (father != null)
        {
            positions[father.id] = new Vector2(-(CardWidth + ColGap), (CardHeight + RowGap) / 2f);
            identities[father.id] = (father, ClanRelation.FAT);
        }
        if (mother != null)
        {
            positions[mother.id] = new Vector2(-(CardWidth + ColGap), -(CardHeight + RowGap) / 2f);
            identities[mother.id] = (mother, ClanRelation.MOM);
        }

        // ── 下一代：本人的子女 + 每个兄弟姐妹各自的子女，按本代的上下顺序依次排开 ──
        float childCursor = 0f;
        var childColIds = new List<long>();
        var childParentOf = new Dictionary<long, long>();
        foreach (var (relation, identity) in sameCol)
        {
            if (relation is ClanRelation.LOV or ClanRelation.COB) continue; // 配偶的子女跟本人子女是同一批，不重复摆
            List<(ClanRelation, PersonalClanIdentity)> kids = SpecificClanManager.getChildren(identity);
            foreach (var (kidRelation, kid) in kids)
            {
                if (kid == null || positions.ContainsKey(kid.id)) continue;
                positions[kid.id] = new Vector2(CardWidth + ColGap, childCursor);
                identities[kid.id] = (kid, kidRelation);
                childParentOf[kid.id] = identity.id;
                childColIds.Add(kid.id);
                childCursor -= CardHeight + RowGap;
            }
        }
        RecenterColumn(childColIds, positions);

        // ── 下二代：只有本人的孙辈(不含兄弟姐妹那边的孙辈，跟卡片视图口径一致) ──
        float grandCursor = 0f;
        var grandColIds = new List<long>();
        foreach (long childId in childColIds)
        {
            if (!childParentOf.TryGetValue(childId, out long parentId) || parentId != center.id) continue;
            if (!identities.TryGetValue(childId, out var childEntry)) continue;
            List<(ClanRelation, PersonalClanIdentity)> grandKids = SpecificClanManager.getChildren(childEntry.identity);
            foreach (var (grandRelation, grandKid) in grandKids)
            {
                if (grandKid == null || positions.ContainsKey(grandKid.id)) continue;
                positions[grandKid.id] = new Vector2((CardWidth + ColGap) * 2f, grandCursor);
                identities[grandKid.id] = (grandKid, grandRelation);
                childParentOf[grandKid.id] = childId;
                grandColIds.Add(grandKid.id);
                grandCursor -= CardHeight + RowGap;
            }
        }
        RecenterColumn(grandColIds, positions);

        // ── 连线:父母→本人/兄弟姐妹那一整列；每个上级→自己的子女 ──
        Color lineColor = new Color(0.7f, 0.75f, 0.8f, 0.6f);
        Color marriageColor = new Color(0.92f, 0.65f, 0.75f, 0.9f);
        if (father != null || mother != null)
        {
            Vector2 parentAnchor = father != null && mother != null
                ? (positions[father.id] + positions[mother.id]) / 2f
                : positions[(father ?? mother)!.id];
            // 只有本人和兄弟姐妹是父母的血亲，配偶不能往左接父母那一列。
            foreach (var (relation, identity) in sameCol)
            {
                if (relation is ClanRelation.LOV or ClanRelation.COB) continue;
                _engine.CreateElbow(parentAnchor, positions[identity.id], CardHalfSize, lineColor);
            }
        }
        foreach (long childId in childColIds)
        {
            if (childParentOf.TryGetValue(childId, out long parentId) && positions.TryGetValue(parentId, out Vector2 parentPos))
                _engine.CreateElbow(parentPos, positions[childId], CardHalfSize, lineColor);
        }
        foreach (long grandId in grandColIds)
        {
            if (childParentOf.TryGetValue(grandId, out long parentId) && positions.TryGetValue(parentId, out Vector2 parentPos))
                _engine.CreateElbow(parentPos, positions[grandId], CardHalfSize, lineColor);
        }
        // 本人 ↔ 配偶的婚姻线(同一列，竖直短线，跟父子线区分开靠粗细/无折角)
        Vector2 centerPos = positions[center.id];
        foreach (var (relation, identity) in spouses)
            _engine.CreateSegment(centerPos, positions[identity.id], marriageColor, 1.6f);

        foreach (KeyValuePair<long, Vector2> kv in positions)
        {
            if (!identities.TryGetValue(kv.Key, out var entry)) continue;
            placeCard(entry.identity, entry.relation, kv.Value);
        }

        float minX = positions.Values.Select(p => p.x).DefaultIfEmpty(0f).Min() - CardWidth;
        float maxX = positions.Values.Select(p => p.x).DefaultIfEmpty(0f).Max() + CardWidth;
        float minY = positions.Values.Select(p => p.y).DefaultIfEmpty(0f).Min() - CardHeight;
        float maxY = positions.Values.Select(p => p.y).DefaultIfEmpty(0f).Max() + CardHeight;
        Vector2 contentSize = new Vector2(maxX - minX, maxY - minY);

        Rect viewportRect = _engine.ViewportTransform.rect;
        float fitScale = Mathf.Clamp(Mathf.Min(viewportRect.width / Math.Max(1f, contentSize.x),
            viewportRect.height / Math.Max(1f, contentSize.y)) * 0.95f, 0.45f, 1f);
        // 把"本人"那一列整体对齐到视口左侧，而不是整张图的几何中心，横向的树从左往右看更顺。
        Vector2 fitPosition = new Vector2(-(minX + CardWidth / 2f) * fitScale - viewportRect.width / 2f +
                                           (CardWidth / 2f + ColGap) * fitScale, -centerPos.y * fitScale);
        _engine.SetContent(contentSize, fitScale, fitPosition);
    }

    private static void RecenterColumn(List<long> ids, Dictionary<long, Vector2> positions)
    {
        if (ids.Count == 0) return;
        float minY = ids.Select(id => positions[id].y).Min();
        float maxY = ids.Select(id => positions[id].y).Max();
        float offset = (minY + maxY) / 2f;
        foreach (long id in ids) positions[id] -= new Vector2(0f, offset);
    }

    public void ResetView() => _engine.ResetView();
}
