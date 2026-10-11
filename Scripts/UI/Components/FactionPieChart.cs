using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 派系占比扇形图(帝国主页)：每块扇区按理念色填充，扇区中线摆理念徽章；
// 扇区太窄放不下时徽章挪到圆外，用一根短线指回扇区(相邻的小扇区里外交错，避免徽章叠在一起)。
// 鼠标悬停按"相对圆心的角度"判断指着哪一块：提亮该扇区并弹出该派系的详情提示；点击打开派系详情窗口。
public sealed class FactionPieChart : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public sealed class Slice
    {
        public float Share;                     // 占比 0..1(各块之和为 1)
        public Color Color;
        public Sprite Emblem;
        public string TipTitleKey, TipBodyKey;  // 已注册到当前语言的提示文本 key
        public Action OnClick;
    }

    private const float InsideEmblemShare = 0.12f;
    private const int DiscResolution = 40;      // 低分辨率 + 点采样：圆边保持像素风
    private static Sprite _disc;

    private readonly List<Slice> _slices = new();
    private readonly List<Image> _highlights = new();
    private RectTransform _rect;
    private float _radius;
    private bool _hovering;
    private Camera _camera;
    private int _hovered = -1;

    public static FactionPieChart Create(Transform parent, float size, float radius, List<Slice> slices)
    {
        var chartObject = new GameObject("FactionPieChart", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(LayoutElement));
        var rect = chartObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(size, size);
        LayoutElement element = chartObject.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = size;
        element.minHeight = element.preferredHeight = size;
        // 透明底图只负责接收指针事件
        Image hitArea = chartObject.GetComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;
        var chart = chartObject.AddComponent<FactionPieChart>();
        chart.Build(rect, radius, slices);
        return chart;
    }

    private void Build(RectTransform rect, float radius, List<Slice> slices)
    {
        _rect = rect;
        _radius = radius;
        _slices.AddRange(slices);
        Sprite disc = Disc();
        // 深色底圆比饼图大一圈，充当描边
        AddDisc("Rim", disc, radius + 1.5f, new Color(0.11f, 0.12f, 0.1f, 1f), 1f, 0f);
        float start = 0f;   // 从正上方顺时针
        int outside = 0;
        foreach (Slice slice in _slices)
        {
            float share = Mathf.Clamp01(slice.Share);
            if (share <= 0f)
            {
                _highlights.Add(null);
                continue;
            }
            AddDisc("Slice", disc, radius, slice.Color, share, start);
            Image highlight = AddDisc("Highlight", disc, radius, new Color(1f, 1f, 1f, 0.35f), share, start);
            highlight.gameObject.SetActive(false);
            _highlights.Add(highlight);
            float middle = (start + share * 0.5f) * Mathf.PI * 2f;
            bool inside = share >= InsideEmblemShare;
            float emblemRadius = inside ? radius * 0.55f : radius + (outside++ % 2 == 0 ? 7f : 13f);
            if (!inside) AddLeader(middle, radius - 2f, emblemRadius - 4f);
            AddEmblem(slice.Emblem, middle, emblemRadius, inside ? 13f : 9f);
            start += share;
        }
    }

    private Image AddDisc(string name, Sprite sprite, float radius, Color color, float fill, float start)
    {
        var discObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = discObject.GetComponent<RectTransform>();
        rect.SetParent(_rect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(radius * 2f, radius * 2f);
        // 径向填充从正上方顺时针开始；整体顺时针转到本扇区的起始角
        rect.localRotation = Quaternion.Euler(0f, 0f, -start * 360f);
        Image image = discObject.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Top;
        image.fillClockwise = true;
        image.fillAmount = fill;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void AddLeader(float angle, float from, float to)
    {
        var lineObject = new GameObject("Leader", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = lineObject.GetComponent<RectTransform>();
        rect.SetParent(_rect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        float middle = (from + to) * 0.5f;
        rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * middle;
        rect.sizeDelta = new Vector2(1f, Mathf.Max(1f, to - from));
        rect.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
        Image image = lineObject.GetComponent<Image>();
        image.color = new Color(0.85f, 0.82f, 0.75f, 0.9f);
        image.raycastTarget = false;
    }

    private void AddEmblem(Sprite emblem, float angle, float radius, float size)
    {
        if (emblem == null) return;
        var emblemObject = new GameObject("Emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = emblemObject.GetComponent<RectTransform>();
        rect.SetParent(_rect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
        rect.sizeDelta = new Vector2(size, size);
        Image image = emblemObject.GetComponent<Image>();
        image.sprite = emblem;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovering = true;
        _camera = eventData.enterEventCamera;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovering = false;
        SetHovered(-1);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        int index = SliceAt(eventData.position, eventData.pressEventCamera);
        if (index >= 0) _slices[index].OnClick?.Invoke();
    }

    private void Update()
    {
        if (_hovering) SetHovered(SliceAt(Input.mousePosition, _camera));
    }

    private void OnDisable()
    {
        _hovering = false;
        SetHovered(-1);
    }

    private int SliceAt(Vector2 screenPosition, Camera eventCamera)
    {
        if (_rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPosition,
                eventCamera, out Vector2 local)) return -1;
        local -= _rect.rect.center;
        if (local.magnitude > _radius) return -1;
        float angle = Mathf.Atan2(local.x, local.y) / (Mathf.PI * 2f);
        if (angle < 0f) angle += 1f;
        float start = 0f;
        for (int i = 0; i < _slices.Count; i++)
        {
            float share = Mathf.Clamp01(_slices[i].Share);
            if (share > 0f && angle >= start && angle < start + share) return i;
            start += share;
        }
        return -1;
    }

    private void SetHovered(int index)
    {
        if (index == _hovered) return;
        if (_hovered >= 0 && _hovered < _highlights.Count && _highlights[_hovered] != null)
            _highlights[_hovered].gameObject.SetActive(false);
        _hovered = index;
        if (index < 0)
        {
            Tooltip.hideTooltip();
            return;
        }
        if (_highlights[index] != null) _highlights[index].gameObject.SetActive(true);
        Slice slice = _slices[index];
        if (string.IsNullOrEmpty(slice.TipTitleKey) && string.IsNullOrEmpty(slice.TipBodyKey)) return;
        Tooltip.show(gameObject, "normal", new TooltipData
        {
            tip_name = slice.TipTitleKey,
            tip_description = slice.TipBodyKey,
            tip_description_2 = ""
        });
    }

    private static Sprite Disc()
    {
        if (_disc != null) return _disc;
        const int n = DiscResolution;
        var texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[n * n];
        float center = (n - 1) * 0.5f, radius = n * 0.5f;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = x - center, dy = y - center;
            pixels[y * n + x] = dx * dx + dy * dy <= radius * radius
                ? new Color32(255, 255, 255, 255)
                : new Color32(255, 255, 255, 0);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        _disc = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return _disc;
    }
}
