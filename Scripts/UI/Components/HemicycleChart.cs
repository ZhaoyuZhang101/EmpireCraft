using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 半圆议席图：每个议席一个像素方块，按传入顺序从左到右排开(调用方按理念左右排好政党)。
// 执政一方的议席加金框，正中一道竖线标出过半线；空缺议席画得暗一些。
public static class HemicycleChart
{
    public struct Seat
    {
        public Color Color;
        public bool Governing;
        public bool Vacant;
    }

    private static readonly Color GoverningFrame = new(0.95f, 0.76f, 0.29f, 1f);
    private static readonly Color MajorityLine = new(1f, 1f, 1f, 0.55f);

    public static GameObject Create(Transform parent, Vector2 size, IList<Seat> seats)
    {
        var root = new GameObject("Hemicycle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        // 透明底：接收悬停，用来挂议席说明
        Image background = root.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.25f);
        LayoutElement element = root.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = element.minHeight = size.y;

        int count = seats?.Count ?? 0;
        if (count == 0) return root;
        int rows = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(count / 2.2f)), 1, 6);
        float outer = Mathf.Min(size.x / 2f, size.y) - 3f;
        float dot = Mathf.Clamp(Mathf.Floor(outer * 0.55f / rows) - 1f, 2f, 6f);
        outer -= dot / 2f;
        float inner = rows == 1 ? outer : outer * 0.42f;

        // 各排议席数按半径分配
        var radii = Enumerable.Range(0, rows)
            .Select(row => rows == 1 ? outer : inner + (outer - inner) * row / (rows - 1)).ToList();
        float radiusSum = radii.Sum();
        var perRow = radii.Select(radius => Mathf.Max(1, Mathf.RoundToInt(count * radius / radiusSum))).ToList();
        int diff = count - perRow.Sum();
        for (int row = rows - 1; diff != 0; row = row == 0 ? rows - 1 : row - 1)
        {
            if (diff < 0 && perRow[row] <= 1) { if (row == 0) break; continue; }
            perRow[row] += Math.Sign(diff);
            diff -= Math.Sign(diff);
        }

        // 所有席位按角度从左(π)到右(0)排序，再依次分给各政党
        var positions = new List<(float angle, float radius)>();
        for (int row = 0; row < rows; row++)
        {
            int n = perRow[row];
            for (int i = 0; i < n; i++)
                positions.Add((n == 1 ? Mathf.PI / 2f : Mathf.PI * (1f - i / (float)(n - 1)), radii[row]));
        }
        positions = positions.OrderByDescending(position => position.angle)
            .ThenBy(position => position.radius).Take(count).ToList();

        Vector2 origin = new(0f, -size.y / 2f + 2f + dot / 2f);
        // 过半线
        AddBlock(rect, "Majority", new Vector2(0f, origin.y + (outer + dot / 2f) / 2f),
            new Vector2(1f, outer + dot), MajorityLine);
        for (int i = 0; i < positions.Count; i++)
        {
            (float angle, float radius) = positions[i];
            Vector2 center = origin + new Vector2(Mathf.Round(Mathf.Cos(angle) * radius),
                Mathf.Round(Mathf.Sin(angle) * radius));
            Seat seat = seats[i];
            if (seat.Governing) AddBlock(rect, "Frame", center, new Vector2(dot + 2f, dot + 2f), GoverningFrame);
            Color color = seat.Color;
            if (seat.Vacant) color = Color.Lerp(color, Color.black, 0.55f);
            AddBlock(rect, "Seat", center, new Vector2(dot, dot), color);
        }
        return root;
    }

    private static void AddBlock(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var block = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = block.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = block.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}
