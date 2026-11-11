using System;
using System.Collections.Generic;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 宽窗口里的天下简图(选目标窗口 ClaimTargetWindow、外交界面 DiplomacyWindow.Map)：一张点采样的小图，图上的名字是叠在上面的文字。
// 图上看的是地图的哪一片由它自己管(地块坐标的一个矩形，形状同显示区)：滚轮放大、缩小(以指着的地方为准)，按住拖动平移；
// 每换一片就叫窗口按这一片重画一帧(Frame：像素、名字、点哪一格与指着哪一格的处理)，所以放大以后是按更细的格子重新取样的，
// 不是把原图拉大(用户 2026-11-11 "这个图我希望能放大""看更多细节""还有缩小")。最远缩到包住整张地图(RealmMiniMap.Whole)，
// 缩到头再往下滚、或整图时拖动，交还给窗口滚动页面。
// 窗口重画以后图还停在原来那一片：视野按 viewKey 记着(ForgetViews 在窗口打开时清)。贴图随物件销毁
public sealed class ClaimTargetMap : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // 一帧：这一片画出来的样子
    public sealed class Frame
    {
        public int Width, Height;
        public Color32[] Pixels;
        // 点了哪一格、指着哪一格(移出去时两个数都是 -1)
        public Action<int, int> OnClick, OnHover;
        public readonly List<(float x, float y, string text, Color color, int fontSize)> Labels = new();

        // 在图上某一格的位置叠一行字
        public void AddLabel(float x, float y, string text, Color color, int fontSize) => Labels.Add((x, y, text, color, fontSize));
    }

    // 放得最大时图上横着还有这么多格地；滚一格放大、缩小的倍数
    private const int MinTiles = 36;
    private const float ZoomStep = 1.35f;
    private static readonly Dictionary<string, RectInt> Views = new();

    // 窗口打开时清掉记着的视野
    public static void ForgetViews() => Views.Clear();

    private RectTransform _viewport, _rect;
    private RawImage _image;
    private Texture2D _texture;
    private Frame _frame;
    private Func<RectInt, Frame> _draw;
    private RectInt _tiles, _home;
    private float _aspect;
    private string _viewKey;
    private readonly List<GameObject> _labels = new();
    private bool _hovering, _dragging, _forwarding;
    private Camera _camera;
    private int _hoverX = -1, _hoverY = -1;
    private Vector2 _dragFrom;

    // home：一开始对准的那一片(地块坐标)；draw：按给定的一片画一帧
    public static ClaimTargetMap Create(Transform parent, float displayWidth, float displayHeight, RectInt home, Func<RectInt, Frame> draw,
        string viewKey = null)
    {
        var viewportObject = new GameObject("ClaimTargetMap", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D),
            typeof(LayoutElement));
        var viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(parent, false);
        viewport.sizeDelta = new Vector2(displayWidth, displayHeight);
        LayoutElement element = viewportObject.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = displayWidth;
        element.minHeight = element.preferredHeight = displayHeight;
        Image backdrop = viewportObject.GetComponent<Image>();
        backdrop.color = RealmMiniMap.Water;
        backdrop.raycastTarget = false;

        var mapObject = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        var rect = mapObject.GetComponent<RectTransform>();
        rect.SetParent(viewport, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(displayWidth, displayHeight);
        rect.anchoredPosition = Vector2.zero;
        RawImage image = mapObject.GetComponent<RawImage>();
        image.raycastTarget = true;

        var map = mapObject.AddComponent<ClaimTargetMap>();
        map._viewport = viewport;
        map._rect = rect;
        map._image = image;
        map._draw = draw;
        map._aspect = displayWidth / Mathf.Max(1f, displayHeight);
        map._viewKey = viewKey;
        map._home = map.Fit(home);
        map._tiles = !string.IsNullOrEmpty(viewKey) && Views.TryGetValue(viewKey, out RectInt kept) ? map.Fit(kept) : map._home;
        map.Redraw();
        return map;
    }

    // 回到一开始对准的那一片
    public void Home() => Show(_home);

    // 退到整张地图
    public void Whole() => Show(RealmMiniMap.Whole(_aspect));

    private void Show(RectInt tiles)
    {
        tiles = Fit(tiles);
        if (tiles.Equals(_tiles)) return;
        _tiles = tiles;
        Redraw();
    }

    // 收成显示区的形状，不小于 MinTiles、不大于整图，中心留在地图里
    private RectInt Fit(RectInt tiles)
    {
        RectInt whole = RealmMiniMap.Whole(_aspect);
        int width = Mathf.Clamp(tiles.width, Mathf.Min(MinTiles, whole.width), whole.width);
        int height = Mathf.Max(1, Mathf.RoundToInt(width / _aspect));
        float centerX = Mathf.Clamp(tiles.x + tiles.width / 2f, 0f, MapBox.width), centerY = Mathf.Clamp(tiles.y + tiles.height / 2f, 0f, MapBox.height);
        int x = Mathf.Clamp(Mathf.RoundToInt(centerX - width / 2f), whole.x, Mathf.Max(whole.x, whole.xMax - width));
        int y = Mathf.Clamp(Mathf.RoundToInt(centerY - height / 2f), whole.y, Mathf.Max(whole.y, whole.yMax - height));
        return new RectInt(x, y, width, height);
    }

    private void Redraw()
    {
        Frame frame;
        try
        {
            frame = _draw?.Invoke(_tiles);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][简图] 没画成: {exception.Message}");
            return;
        }
        if (frame?.Pixels == null || frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length != frame.Width * frame.Height) return;
        _frame = frame;
        if (_texture == null || _texture.width != frame.Width || _texture.height != frame.Height)
        {
            if (_texture != null) Destroy(_texture);
            _texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }
        _texture.SetPixels32(frame.Pixels);
        _texture.Apply(false);
        _image.texture = _texture;
        _rect.anchoredPosition = Vector2.zero;
        foreach (GameObject label in _labels)
            if (label != null) Destroy(label);
        _labels.Clear();
        foreach ((float x, float y, string text, Color color, int fontSize) in frame.Labels) PutLabel(x, y, text, color, fontSize);
        _hoverX = _hoverY = -1;
        if (!string.IsNullOrEmpty(_viewKey)) Views[_viewKey] = _tiles;
    }

    // 叠一行字(描黑边，不挡点击)
    private void PutLabel(float x, float y, string text, Color color, int fontSize)
    {
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(_rect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(80f, 12f);
        Rect area = _rect.rect;
        rect.anchoredPosition = new Vector2(((x + 0.5f) / _frame.Width - 0.5f) * area.width, ((y + 0.5f) / _frame.Height - 0.5f) * area.height);
        Text label = labelObject.GetComponent<Text>();
        label.font = LocalizedTextManager.current_font;
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.supportRichText = false;
        label.raycastTarget = false;
        label.color = color;
        label.text = text ?? "";
        Outline outline = labelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.05f, 0.06f, 0.04f, 0.9f);
        outline.effectDistance = new Vector2(0.6f, -0.6f);
        _labels.Add(labelObject);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 拖完松手不算点
        if (eventData.dragging || _frame == null) return;
        if (Pixel(eventData.position, eventData.pressEventCamera, out int x, out int y)) _frame.OnClick?.Invoke(x, y);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovering = true;
        _camera = eventData.enterEventCamera;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovering = false;
        SetHover(-1, -1);
    }

    // 滚轮：以指着的那块地为准放大、缩小
    public void OnScroll(PointerEventData eventData)
    {
        float wheel = eventData.scrollDelta.y;
        if (Mathf.Approximately(wheel, 0f)) return;
        RectInt whole = RealmMiniMap.Whole(_aspect);
        int width = Mathf.Clamp(Mathf.RoundToInt(_tiles.width * (wheel > 0f ? 1f / ZoomStep : ZoomStep)), Mathf.Min(MinTiles, whole.width), whole.width);
        if (width == _tiles.width)
        {
            // 缩到头再往下滚：滚页面
            if (wheel < 0f) Forward(eventData, ExecuteEvents.scrollHandler);
            return;
        }
        float u = 0.5f, v = 0.5f;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.enterEventCamera, out Vector2 local))
        {
            Rect area = _rect.rect;
            u = Mathf.Clamp01((local.x - area.xMin) / Mathf.Max(1f, area.width));
            v = Mathf.Clamp01((local.y - area.yMin) / Mathf.Max(1f, area.height));
        }
        float height = width / _aspect;
        float anchorX = _tiles.x + u * _tiles.width, anchorY = _tiles.y + v * _tiles.height;
        Show(new RectInt(Mathf.RoundToInt(anchorX - u * width), Mathf.RoundToInt(anchorY - v * height), width, Mathf.RoundToInt(height)));
    }

    // 拖动：先把图挪着看，松手再按新的一片重画。已经是整图时拖的是页面
    public void OnBeginDrag(PointerEventData eventData)
    {
        _forwarding = _tiles.width >= RealmMiniMap.Whole(_aspect).width;
        if (_forwarding)
        {
            Forward(eventData, ExecuteEvents.beginDragHandler);
            return;
        }
        _dragging = RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out _dragFrom);
        SetHover(-1, -1);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_forwarding)
        {
            Forward(eventData, ExecuteEvents.dragHandler);
            return;
        }
        if (_dragging && RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out Vector2 now))
            _rect.anchoredPosition = now - _dragFrom;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_forwarding)
        {
            _forwarding = false;
            Forward(eventData, ExecuteEvents.endDragHandler);
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        Vector2 moved = _rect.anchoredPosition;
        Rect area = _rect.rect;
        _rect.anchoredPosition = Vector2.zero;
        if (area.width <= 0f || area.height <= 0f) return;
        // 图往右拖 = 看左边的地
        Show(new RectInt(_tiles.x - Mathf.RoundToInt(moved.x / area.width * _tiles.width),
            _tiles.y - Mathf.RoundToInt(moved.y / area.height * _tiles.height), _tiles.width, _tiles.height));
    }

    private void Forward<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> handler) where T : IEventSystemHandler
    {
        if (_viewport != null && _viewport.parent != null) ExecuteEvents.ExecuteHierarchy(_viewport.parent.gameObject, eventData, handler);
    }

    private void Update()
    {
        if (!_hovering || _dragging) return;
        if (Pixel(Input.mousePosition, _camera, out int x, out int y)) SetHover(x, y);
        else SetHover(-1, -1);
    }

    private void OnDisable()
    {
        _hovering = _dragging = _forwarding = false;
    }

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    private void SetHover(int x, int y)
    {
        if (x == _hoverX && y == _hoverY) return;
        _hoverX = x;
        _hoverY = y;
        _frame?.OnHover?.Invoke(x, y);
    }

    private bool Pixel(Vector2 screen, Camera eventCamera, out int x, out int y)
    {
        x = y = -1;
        if (_rect == null || _frame == null ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, eventCamera, out Vector2 local)) return false;
        Rect area = _rect.rect;
        if (area.width <= 0f || area.height <= 0f) return false;
        float u = (local.x - area.xMin) / area.width, v = (local.y - area.yMin) / area.height;
        if (u < 0f || u >= 1f || v < 0f || v >= 1f) return false;
        x = Mathf.Clamp((int)(u * _frame.Width), 0, _frame.Width - 1);
        y = Mathf.Clamp((int)(v * _frame.Height), 0, _frame.Height - 1);
        return true;
    }
}
