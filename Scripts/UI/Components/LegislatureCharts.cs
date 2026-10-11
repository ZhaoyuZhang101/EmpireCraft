using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 立法机构的另外三种座席图(半圆议席见 HemicycleChart)：
//   · 会堂方阵(代表大会制)：上方主席台，领导党坐中间方阵，其他党派分坐两侧；
//   · 对席(君主立宪)：左边政府席、右边在野席面对面，中间过道尽头是议长席；
//   · 控制条(总统制)：按议席比例横向排开各党，中间白线是过半线。
// 每个议席一个像素方块，空缺的画暗。
public static class LegislatureCharts
{
    private static readonly Color Rostrum = new(0.45f, 0.17f, 0.13f, 1f);
    private static readonly Color Gold = new(0.95f, 0.76f, 0.29f, 1f);
    private static readonly Color MajorityLine = new(1f, 1f, 1f, 0.8f);
    private static readonly Color Gutter = new(0f, 0f, 0f, 0.35f);

    private static Color SeatColor(HemicycleChart.Seat seat) =>
        seat.Vacant ? Color.Lerp(seat.Color, Color.black, 0.55f) : seat.Color;

    // 会堂方阵：center = 领导党，left / right = 两侧党派
    public static GameObject CreateHall(Transform parent, Vector2 size, IList<HemicycleChart.Seat> center,
        IList<HemicycleChart.Seat> left, IList<HemicycleChart.Seat> right)
    {
        RectTransform root = CreateRoot(parent, size, "Hall");
        const float margin = 4f, rostrumHeight = 5f, top = 3f;
        // 主席台：金边红台
        float rostrumWidth = Mathf.Round(size.x * 0.42f);
        AddTopLeft(root, size, new Vector2((size.x - rostrumWidth) / 2f - 1f, top - 1f),
            new Vector2(rostrumWidth + 2f, rostrumHeight + 2f), Gold);
        AddTopLeft(root, size, new Vector2((size.x - rostrumWidth) / 2f, top), new Vector2(rostrumWidth, rostrumHeight), Rostrum);

        float areaTop = top + rostrumHeight + 5f;
        float areaHeight = size.y - areaTop - margin;
        float inner = size.x - margin * 2f;
        float sideWidth = Mathf.Floor(inner * 0.22f);
        float gap = Mathf.Floor(inner * 0.04f);
        float centerWidth = inner - sideWidth * 2f - gap * 2f;

        float dot = FitDot(new[] { (center.Count, centerWidth), (left.Count, sideWidth), (right.Count, sideWidth) },
            areaHeight);
        PlaceGrid(root, size, left, new Rect(margin, areaTop, sideWidth, areaHeight), dot, TextAnchor.UpperRight);
        PlaceGrid(root, size, center, new Rect(margin + sideWidth + gap, areaTop, centerWidth, areaHeight), dot,
            TextAnchor.UpperCenter);
        PlaceGrid(root, size, right, new Rect(margin + sideWidth + gap * 2f + centerWidth, areaTop, sideWidth, areaHeight),
            dot, TextAnchor.UpperLeft);
        return root.gameObject;
    }

    // 对席：government 在左、opposition 在右，各自靠过道排；过道尽头是议长席
    public static GameObject CreateBenches(Transform parent, Vector2 size, IList<HemicycleChart.Seat> government,
        IList<HemicycleChart.Seat> opposition)
    {
        RectTransform root = CreateRoot(parent, size, "Benches");
        const float margin = 4f;
        float aisle = Mathf.Max(16f, Mathf.Round(size.x * 0.16f));
        float sideWidth = Mathf.Floor((size.x - margin * 2f - aisle) / 2f);
        float areaHeight = size.y - margin * 2f;
        // 议长席：过道上方一把金椅，下面一张长桌
        float chairX = margin + sideWidth + aisle / 2f;
        AddTopLeft(root, size, new Vector2(chairX - 3f, margin), new Vector2(6f, 6f), Gold);
        AddTopLeft(root, size, new Vector2(chairX - 2f, margin + 9f), new Vector2(4f, areaHeight - 12f), Rostrum);

        float dot = FitDot(new[] { (government.Count, sideWidth), (opposition.Count, sideWidth) }, areaHeight);
        PlaceGrid(root, size, government, new Rect(margin, margin, sideWidth, areaHeight), dot, TextAnchor.UpperRight);
        PlaceGrid(root, size, opposition, new Rect(margin + sideWidth + aisle, margin, sideWidth, areaHeight), dot,
            TextAnchor.UpperLeft);
        return root.gameObject;
    }

    // 控制条：segments 依次为各党(颜色, 议席数)，按比例占宽
    public static GameObject CreateControlBar(Transform parent, Vector2 size, IList<(Color color, int seats)> segments,
        bool majorityLine = true)
    {
        RectTransform root = CreateRoot(parent, size, "ControlBar");
        int total = segments.Sum(segment => Mathf.Max(0, segment.seats));
        if (total <= 0) return root.gameObject;
        float inner = size.x - 2f;
        float x = 1f;
        int used = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            int seats = Mathf.Max(0, segments[i].seats);
            if (seats == 0) continue;
            used += seats;
            float end = used >= total ? 1f + inner : 1f + Mathf.Round(inner * used / total);
            AddTopLeft(root, size, new Vector2(x, 1f), new Vector2(Mathf.Max(1f, end - x), size.y - 2f), segments[i].color);
            if (used < total) AddTopLeft(root, size, new Vector2(end - 1f, 1f), new Vector2(1f, size.y - 2f), Gutter);
            x = end;
        }
        if (majorityLine)
            AddTopLeft(root, size, new Vector2(Mathf.Round(size.x / 2f) - 1f, 0f), new Vector2(2f, size.y), MajorityLine);
        return root.gameObject;
    }

    private static RectTransform CreateRoot(Transform parent, Vector2 size, string name)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        // 半透明底：接收悬停，用来挂议席说明
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
        LayoutElement element = root.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = element.minHeight = size.y;
        return rect;
    }

    // 能放下所有方阵的最大方块边长(2~6)
    private static float FitDot((int count, float width)[] blocks, float height)
    {
        for (float dot = 6f; dot > 2f; dot--)
        {
            float pitch = dot + (dot >= 4f ? 2f : 1f);
            bool fits = blocks.All(block =>
            {
                if (block.count <= 0) return true;
                int columns = Mathf.Max(1, Mathf.FloorToInt((block.width + pitch - dot) / pitch));
                int rows = Mathf.CeilToInt(block.count / (float)columns);
                return rows * pitch - (pitch - dot) <= height;
            });
            if (fits) return dot;
        }
        return 2f;
    }

    // 在 area(左上角坐标)里逐行排方块，按 align 对齐到左、中、右
    private static void PlaceGrid(RectTransform root, Vector2 size, IList<HemicycleChart.Seat> seats, Rect area, float dot,
        TextAnchor align)
    {
        if (seats == null || seats.Count == 0) return;
        float pitch = dot + (dot >= 4f ? 2f : 1f);
        int columns = Mathf.Max(1, Mathf.FloorToInt((area.width + pitch - dot) / pitch));
        int rows = Mathf.CeilToInt(seats.Count / (float)columns);
        for (int row = 0; row < rows; row++)
        {
            int inRow = Mathf.Min(columns, seats.Count - row * columns);
            float rowWidth = inRow * pitch - (pitch - dot);
            float startX = align switch
            {
                TextAnchor.UpperRight => area.xMax - rowWidth,
                TextAnchor.UpperCenter => area.x + Mathf.Round((area.width - rowWidth) / 2f),
                _ => area.x
            };
            for (int column = 0; column < inRow; column++)
            {
                HemicycleChart.Seat seat = seats[row * columns + column];
                AddTopLeft(root, size, new Vector2(startX + column * pitch, area.y + row * pitch), new Vector2(dot, dot),
                    SeatColor(seat));
            }
        }
    }

    // 按左上角坐标放方块(根节点的锚点在中心)
    private static void AddTopLeft(RectTransform root, Vector2 size, Vector2 topLeft, Vector2 blockSize, Color color)
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
    }
}
