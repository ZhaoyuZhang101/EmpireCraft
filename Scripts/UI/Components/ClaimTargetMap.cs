using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 选目标窗口里的简化地图(见 ClaimTargetWindow)：一张点采样的小图，点一下、指着哪一格由窗口处理；
// 图上的名字是叠在上面的文字(AddLabel)。贴图随物件销毁
public sealed class ClaimTargetMap : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private RectTransform _rect;
    private Texture2D _texture;
    private int _width, _height;
    private Action<int, int> _onClick, _onHover;
    private bool _hovering;
    private Camera _camera;
    private int _hoverX = -1, _hoverY = -1;

    public static ClaimTargetMap Create(Transform parent, float displayWidth, float displayHeight, int width, int height,
        Color32[] pixels, Action<int, int> onClick, Action<int, int> onHover)
    {
        var mapObject = new GameObject("ClaimTargetMap", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage),
            typeof(LayoutElement));
        var rect = mapObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(displayWidth, displayHeight);
        LayoutElement element = mapObject.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = displayWidth;
        element.minHeight = element.preferredHeight = displayHeight;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false);
        RawImage image = mapObject.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = true;
        var map = mapObject.AddComponent<ClaimTargetMap>();
        map._rect = rect;
        map._texture = texture;
        map._width = width;
        map._height = height;
        map._onClick = onClick;
        map._onHover = onHover;
        return map;
    }

    // 在图上某一格的位置叠一行字(描黑边，不挡点击)
    public void AddLabel(float x, float y, string text, Color color, int fontSize)
    {
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(_rect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(80f, 12f);
        Rect area = _rect.rect;
        rect.anchoredPosition = new Vector2(((x + 0.5f) / _width - 0.5f) * area.width, ((y + 0.5f) / _height - 0.5f) * area.height);
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
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Pixel(eventData.position, eventData.pressEventCamera, out int x, out int y)) _onClick?.Invoke(x, y);
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

    private void Update()
    {
        if (!_hovering) return;
        if (Pixel(Input.mousePosition, _camera, out int x, out int y)) SetHover(x, y);
        else SetHover(-1, -1);
    }

    private void OnDisable()
    {
        _hovering = false;
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
        _onHover?.Invoke(x, y);
    }

    private bool Pixel(Vector2 screen, Camera eventCamera, out int x, out int y)
    {
        x = y = -1;
        if (_rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, eventCamera, out Vector2 local))
            return false;
        Rect area = _rect.rect;
        if (area.width <= 0f || area.height <= 0f) return false;
        float u = (local.x - area.xMin) / area.width, v = (local.y - area.yMin) / area.height;
        if (u < 0f || u >= 1f || v < 0f || v >= 1f) return false;
        x = Mathf.Clamp((int)(u * _width), 0, _width - 1);
        y = Mathf.Clamp((int)(v * _height), 0, _height - 1);
        return true;
    }
}
