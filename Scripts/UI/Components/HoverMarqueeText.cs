using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public sealed class HoverMarqueeText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const string ViewportName = "MarqueeViewport";
    private const float EdgePadding = 2f;
    private const float HoverDelay = 0.35f;
    private const float ScrollSpeed = 18f;
    private const float ReturnSpeed = 48f;

    private RectTransform _viewport;
    private RectTransform _root;
    private RectTransform _content;
    private Text _text;
    private float _startX;
    private float _endX;
    private float _lastViewportWidth = -1f;
    private string _lastText;
    private bool _hovered;
    private bool _overflows;
    private float _hoverStartTime;

    public static HoverMarqueeText Attach(SimpleText simpleText)
    {
        if (simpleText == null) return null;
        HoverMarqueeText marquee = simpleText.GetComponent<HoverMarqueeText>() ??
                                    simpleText.gameObject.AddComponent<HoverMarqueeText>();
        marquee.Configure(simpleText);
        return marquee;
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
        _viewport.offsetMin = Vector2.zero;
        _viewport.offsetMax = Vector2.zero;
        _viewport.localScale = Vector3.one;

        Image viewportImage = existingViewport.GetComponent<Image>();
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = true;
        existingViewport.GetComponent<Mask>().showMaskGraphic = false;

        if (_content.parent != _viewport)
        {
            _content.SetParent(_viewport, false);
        }
        _content.SetAsFirstSibling();
        _text.raycastTarget = false;

        RectMask2D oldMask = simpleText.GetComponent<RectMask2D>();
        if (oldMask != null) oldMask.enabled = false;
        Recalculate(force: true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_overflows) return;
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
        Recalculate(force: false);
        if (!_overflows)
        {
            _content.anchoredPosition = new Vector2(_startX, 0f);
            return;
        }

        Vector2 position = _content.anchoredPosition;
        if (_hovered)
        {
            if (Time.unscaledTime < _hoverStartTime + HoverDelay) return;
            position.x = Mathf.MoveTowards(position.x, _endX, ScrollSpeed * Time.unscaledDeltaTime);
        }
        else
        {
            position.x = Mathf.MoveTowards(position.x, _startX, ReturnSpeed * Time.unscaledDeltaTime);
        }
        _content.anchoredPosition = position;
    }

    private void OnDisable()
    {
        _hovered = false;
        if (_content != null) _content.anchoredPosition = new Vector2(_startX, 0f);
    }

    private void Recalculate(bool force)
    {
        float viewportWidth = _viewport.rect.width;
        if (viewportWidth <= 0f && _root != null) viewportWidth = _root.rect.width;
        if (viewportWidth <= 0f && _root != null) viewportWidth = _root.sizeDelta.x;
        if (viewportWidth <= 0f) return;
        if (!force && Mathf.Approximately(viewportWidth, _lastViewportWidth) && _lastText == _text.text) return;

        _lastViewportWidth = viewportWidth;
        _lastText = _text.text;
        float contentWidth = Mathf.Ceil(_text.preferredWidth) + EdgePadding * 2f;
        _overflows = contentWidth > viewportWidth;

        _content.anchorMin = new Vector2(0f, 0.5f);
        _content.anchorMax = new Vector2(0f, 0.5f);
        _content.pivot = new Vector2(0f, 0.5f);
        float viewportHeight = _viewport.rect.height;
        if (viewportHeight <= 0f && _root != null) viewportHeight = _root.rect.height;
        _content.sizeDelta = new Vector2(Mathf.Max(contentWidth, viewportWidth), viewportHeight * 0.95f);
        _startX = EdgePadding;
        _endX = viewportWidth - contentWidth - EdgePadding;
        _content.anchoredPosition = new Vector2(_startX, 0f);
        if (!_overflows) _hovered = false;
    }
}
