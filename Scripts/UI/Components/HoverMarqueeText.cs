using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public sealed class HoverMarqueeText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float EdgePadding = 2f;
    private const float HoverDelay = 0.35f;
    private const float ScrollSpeed = 18f;
    private const float ReturnSpeed = 48f;

    private RectTransform _viewport;
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
        _viewport = simpleText.GetComponent<RectTransform>();
        _text = simpleText.text;
        _content = _text.GetComponent<RectTransform>();
        if (simpleText.GetComponent<RectMask2D>() == null)
        {
            simpleText.gameObject.AddComponent<RectMask2D>();
        }

        _text.raycastTarget = true;
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
        if (!_overflows) return;

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

    private void Recalculate(bool force)
    {
        float viewportWidth = _viewport.rect.width;
        if (viewportWidth <= 0f) viewportWidth = _viewport.sizeDelta.x;
        if (!force && Mathf.Approximately(viewportWidth, _lastViewportWidth) && _lastText == _text.text) return;

        _lastViewportWidth = viewportWidth;
        _lastText = _text.text;
        float contentWidth = Mathf.Ceil(_text.preferredWidth) + EdgePadding * 2f;
        _overflows = contentWidth > viewportWidth;

        _content.anchorMin = new Vector2(0f, 0.5f);
        _content.anchorMax = new Vector2(0f, 0.5f);
        _content.pivot = new Vector2(0f, 0.5f);
        _content.sizeDelta = new Vector2(Mathf.Max(contentWidth, viewportWidth), _viewport.rect.height * 0.95f);
        _startX = EdgePadding;
        _endX = viewportWidth - contentWidth - EdgePadding;
        _content.anchoredPosition = new Vector2(_startX, 0f);
        if (!_overflows) _hovered = false;
    }
}
