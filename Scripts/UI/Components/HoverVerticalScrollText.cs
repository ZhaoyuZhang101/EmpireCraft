using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public sealed class HoverVerticalScrollText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const string ViewportName = "VerticalScrollViewport";
    private const float EdgePadding = 2f;
    private const float HoverDelay = 0.35f;
    private const float ScrollSpeed = 14f;
    private const float ReturnSpeed = 42f;

    private RectTransform _root;
    private RectTransform _viewport;
    private RectTransform _content;
    private Text _text;
    private string _lastText;
    private float _lastWidth = -1f;
    private float _lastHeight = -1f;
    private float _endY;
    private float _hoverStartTime;
    private bool _hovered;

    public static HoverVerticalScrollText Attach(SimpleText simpleText)
    {
        if (simpleText == null) return null;
        HoverVerticalScrollText scroller = simpleText.GetComponent<HoverVerticalScrollText>() ??
                                           simpleText.gameObject.AddComponent<HoverVerticalScrollText>();
        scroller.Configure(simpleText);
        return scroller;
    }

    private void Configure(SimpleText simpleText)
    {
        _root = simpleText.GetComponent<RectTransform>();
        _text = simpleText.text;
        _content = _text.GetComponent<RectTransform>();

        Transform existingViewport = simpleText.transform.Find(ViewportName);
        if (existingViewport == null)
        {
            var viewportObject = new GameObject(ViewportName, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(simpleText.transform, false);
            existingViewport = viewportObject.transform;
        }

        _viewport = existingViewport.GetComponent<RectTransform>();
        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.offsetMin = new Vector2(EdgePadding, EdgePadding);
        _viewport.offsetMax = new Vector2(-EdgePadding, -EdgePadding);
        _viewport.localScale = Vector3.one;

        Image viewportImage = existingViewport.GetComponent<Image>();
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = true;
        existingViewport.GetComponent<Mask>().showMaskGraphic = false;

        if (_content.parent != _viewport) _content.SetParent(_viewport, false);
        _content.SetAsFirstSibling();
        _content.anchorMin = new Vector2(0.5f, 1f);
        _content.anchorMax = new Vector2(0.5f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _text.horizontalOverflow = HorizontalWrapMode.Wrap;
        _text.verticalOverflow = VerticalWrapMode.Overflow;
        _text.raycastTarget = false;

        RectMask2D oldMask = simpleText.GetComponent<RectMask2D>();
        if (oldMask != null) oldMask.enabled = false;
        Recalculate(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Recalculate(true);
        if (_endY <= 0f) return;
        _hovered = true;
        _hoverStartTime = Time.unscaledTime;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
    }

    private void LateUpdate()
    {
        if (_viewport == null || _content == null || _text == null) return;
        Recalculate(false);

        float targetY = _hovered && Time.unscaledTime >= _hoverStartTime + HoverDelay ? _endY : 0f;
        float speed = _hovered ? ScrollSpeed : ReturnSpeed;
        Vector2 position = _content.anchoredPosition;
        position.y = Mathf.MoveTowards(position.y, targetY, speed * Time.unscaledDeltaTime);
        _content.anchoredPosition = position;
    }

    private void OnDisable()
    {
        _hovered = false;
        if (_content != null) _content.anchoredPosition = Vector2.zero;
    }

    private void Recalculate(bool force)
    {
        float viewportWidth = _viewport.rect.width;
        float viewportHeight = _viewport.rect.height;
        if (viewportWidth <= 0f || viewportHeight <= 0f) return;
        if (!force && Mathf.Approximately(viewportWidth, _lastWidth) &&
            Mathf.Approximately(viewportHeight, _lastHeight) && _lastText == _text.text) return;

        _lastWidth = viewportWidth;
        _lastHeight = viewportHeight;
        _lastText = _text.text;

        float contentWidth = Mathf.Max(1f, viewportWidth - EdgePadding * 2f);
        _content.sizeDelta = new Vector2(contentWidth, viewportHeight);
        Canvas.ForceUpdateCanvases();
        float contentHeight = Mathf.Max(viewportHeight, Mathf.Ceil(_text.preferredHeight) + EdgePadding * 2f);
        _content.sizeDelta = new Vector2(contentWidth, contentHeight);
        _endY = Mathf.Max(0f, contentHeight - viewportHeight);
        _content.anchoredPosition = Vector2.zero;
        if (_endY <= 0f) _hovered = false;
    }
}
