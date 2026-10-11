using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 逐月收支图：每月一列，零线以上按类别堆叠流入，零线以下堆叠流出，金线连出每月结余。
// 零线位置按最大流入与最大流出的比例放，上下两半都用满；每列有一块透明感应区，调用方在上面挂悬停说明
public static class TreasuryChart
{
    public struct Column
    {
        public List<(Color color, long amount)> Up;
        public List<(Color color, long amount)> Down;
        public long Net;
        public bool HasData;
    }

    private static readonly Color ZeroLine = new(0.78f, 0.76f, 0.70f, 0.7f);
    private static readonly Color Stripe = new(1f, 1f, 1f, 0.035f);

    public static (GameObject root, List<GameObject> hitAreas) Create(Transform parent, Vector2 size,
        IList<Column> columns, Color netColor)
    {
        var root = new GameObject("TreasuryChart", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
        root.GetComponent<Image>().raycastTarget = false;
        LayoutElement element = root.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = element.minHeight = size.y;

        var hitAreas = new List<GameObject>();
        int count = columns.Count;
        if (count == 0) return (root, hitAreas);
        const float margin = 3f;
        float plot = size.y - margin * 2f;
        long maxUp = columns.Max(column => column.Up?.Sum(item => item.amount) ?? 0L);
        long maxDown = columns.Max(column => column.Down?.Sum(item => item.amount) ?? 0L);
        long range = maxUp + maxDown;
        float zeroY = range <= 0 ? margin + plot / 2f : margin + plot * maxUp / range;
        float scale = range <= 0 ? 0f : plot / range;
        float columnWidth = size.x / count;
        float barWidth = Mathf.Max(3f, Mathf.Round(columnWidth * 0.55f));

        for (int i = 0; i < count; i++)
        {
            if (i % 2 == 1) Block(rect, size, new Vector2(i * columnWidth, 0f), new Vector2(columnWidth, size.y), Stripe);
            Column column = columns[i];
            float x = Mathf.Round(i * columnWidth + (columnWidth - barWidth) / 2f);
            float y = zeroY;
            foreach ((Color color, long amount) in column.Up ?? new List<(Color, long)>())
            {
                if (amount <= 0) continue;
                float height = Mathf.Max(1f, amount * scale);
                y -= height;
                Block(rect, size, new Vector2(x, y), new Vector2(barWidth, height), color);
            }
            y = zeroY;
            foreach ((Color color, long amount) in column.Down ?? new List<(Color, long)>())
            {
                if (amount <= 0) continue;
                float height = Mathf.Max(1f, amount * scale);
                Block(rect, size, new Vector2(x, y), new Vector2(barWidth, height), color);
                y += height;
            }
        }
        Block(rect, size, new Vector2(0f, Mathf.Round(zeroY)), new Vector2(size.x, 1f), ZeroLine);

        // 结余折线：有记账的月份连起来
        Vector2? previous = null;
        for (int i = 0; i < count; i++)
        {
            if (!columns[i].HasData) { previous = null; continue; }
            var point = new Vector2(i * columnWidth + columnWidth / 2f, zeroY - columns[i].Net * scale);
            if (previous.HasValue) Segment(rect, size, previous.Value, point, 1.5f, netColor);
            previous = point;
        }
        for (int i = 0; i < count; i++)
        {
            if (!columns[i].HasData) continue;
            var point = new Vector2(i * columnWidth + columnWidth / 2f, zeroY - columns[i].Net * scale);
            Block(rect, size, point - new Vector2(1.5f, 1.5f), new Vector2(3f, 3f), netColor);
        }

        for (int i = 0; i < count; i++)
        {
            GameObject hit = Block(rect, size, new Vector2(i * columnWidth, 0f), new Vector2(columnWidth, size.y), Color.clear);
            hit.GetComponent<Image>().raycastTarget = true;
            hitAreas.Add(hit);
        }
        return (root, hitAreas);
    }

    // 按左上角坐标放方块(根节点锚点在中心)
    private static GameObject Block(RectTransform root, Vector2 size, Vector2 topLeft, Vector2 blockSize, Color color)
    {
        var block = new GameObject("Block", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = block.GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.sizeDelta = blockSize;
        rect.anchoredPosition = new Vector2(topLeft.x + blockSize.x / 2f - size.x / 2f,
            size.y / 2f - topLeft.y - blockSize.y / 2f);
        Image image = block.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return block;
    }

    // 两点(左上角坐标系)之间的细线
    private static void Segment(RectTransform root, Vector2 size, Vector2 from, Vector2 to, float thickness, Color color)
    {
        var line = new GameObject("Segment", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = line.GetComponent<RectTransform>();
        rect.SetParent(root, false);
        Vector2 delta = to - from;
        rect.sizeDelta = new Vector2(delta.magnitude, thickness);
        Vector2 middle = (from + to) / 2f;
        rect.anchoredPosition = new Vector2(middle.x - size.x / 2f, size.y / 2f - middle.y);
        rect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        Image image = line.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}
