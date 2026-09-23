using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

/// <summary>
/// 宽窗口不能继续使用空窗口预制体的固定窄尺寸。该组件在 LateUpdate 最后统一锁定几何，
/// 避免开窗动画、ContentSizeFitter 和 ScrollRect 的布局阶段把滚动条拉回窗口中央。
/// </summary>
public sealed class WideWindowScrollArea : MonoBehaviour
{
    private const string CustomScrollbarName = "EmpireCraft Wide Scrollbar";
    private RectTransform _background;
    private RectTransform _scrollView;
    private RectTransform _viewport;
    private RectTransform _content;
    private ScrollRect _scrollRect;
    private Scrollbar _scrollbar;
    private List<Scrollbar> _legacyScrollbars = new List<Scrollbar>();
    private float _topMargin;
    private float _bottomMargin;
    private float _sideMargin;

    public void Configure(float topMargin, float bottomMargin, float sideMargin)
    {
        _background = transform as RectTransform;
        _scrollView = transform.Find("Scroll View") as RectTransform;
        _viewport = transform.Find("Scroll View/Viewport") as RectTransform;
        _content = transform.Find("Scroll View/Viewport/Content") as RectTransform;
        _scrollRect = _scrollView?.GetComponent<ScrollRect>();
        _topMargin = topMargin;
        _bottomMargin = bottomMargin;
        _sideMargin = sideMargin;
        if (_background == null || _scrollView == null || _viewport == null || _scrollRect == null) return;
        EnsureDedicatedScrollbar();
        HideNarrowDecorations();
        ApplyLayout();
        // 只在(重新)配置时回到顶部；LateUpdate 里不能再动 y，否则每帧都把滚动位置清零。
        if (_content != null) _content.anchoredPosition = Vector2.zero;
    }

    private void LateUpdate()
    {
        if (_background == null || _scrollView == null || _viewport == null || _scrollRect == null) return;
        ApplyLayout();
    }

    private void EnsureDedicatedScrollbar()
    {
        Transform existing = transform.Find(CustomScrollbarName);
        if (existing != null) _scrollbar = existing.GetComponent<Scrollbar>();
        // 横向滚动条也要一起藏掉：宽窗口不横向滚动，留着它就是底部一块固定窄宽度的暗色条。
        List<Scrollbar> originals = transform.GetComponentsInChildren<Scrollbar>(true)
            .Where(scrollbar => scrollbar != null && scrollbar.transform.name != CustomScrollbarName)
            .ToList();
        _legacyScrollbars = originals;
        Scrollbar visualSource = originals.FirstOrDefault(scrollbar =>
            scrollbar.direction is Scrollbar.Direction.BottomToTop or Scrollbar.Direction.TopToBottom);
        if (_scrollbar == null) _scrollbar = CreateScrollbar(visualSource);
        foreach (Scrollbar original in originals) original.gameObject.SetActive(false);
        _scrollRect.horizontalScrollbar = null;
        _scrollRect.verticalScrollbar = _scrollbar;
        _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        _scrollRect.verticalScrollbarSpacing = 2f;
    }

    // 空窗口预制体在 Background 下还有按窄滚动区(232 宽)摆好的纯装饰图片，拉宽之后它们
    // 会孤零零地留在窗口中下部，透过树状图的半透明底色显成一块黑框。只藏纯图片的子物体，
    // 带文字/按钮/滚动组件的(标题等)一律保留。
    private void HideNarrowDecorations()
    {
        foreach (Transform child in transform)
        {
            if (child == _scrollView || child.name == "TitleBackground" || child.name == CustomScrollbarName)
                continue;
            if (child.GetComponent<Image>() == null) continue;
            if (child.GetComponentInChildren<Text>(true) != null ||
                child.GetComponentInChildren<Selectable>(true) != null ||
                child.GetComponentInChildren<ScrollRect>(true) != null) continue;
            child.gameObject.SetActive(false);
        }
    }

    private Scrollbar CreateScrollbar(Scrollbar source)
    {
        var scrollbarObject = new GameObject(CustomScrollbarName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.SetParent(transform, false);
        Image track = scrollbarObject.GetComponent<Image>();
        CopyImage(source?.GetComponent<Image>(), track, new Color(0.20f, 0.22f, 0.19f, 0.95f));

        var slidingObject = new GameObject("Sliding Area", typeof(RectTransform));
        RectTransform slidingRect = slidingObject.GetComponent<RectTransform>();
        slidingRect.SetParent(scrollbarRect, false);
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(1f, 2f);
        slidingRect.offsetMax = new Vector2(-1f, -2f);

        var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        handleRect.SetParent(slidingRect, false);
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        Image handle = handleObject.GetComponent<Image>();
        CopyImage(source?.handleRect?.GetComponent<Image>(), handle, new Color(0.88f, 0.25f, 0.20f, 1f));

        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.numberOfSteps = 0;
        return scrollbar;
    }

    private static void CopyImage(Image source, Image target, Color fallback)
    {
        target.sprite = source?.sprite;
        target.type = source?.type ?? Image.Type.Sliced;
        target.color = source != null ? source.color : fallback;
        target.material = source?.material;
        target.raycastTarget = true;
    }

    private void ApplyLayout()
    {
        foreach (Scrollbar legacy in _legacyScrollbars)
            if (legacy != null && legacy.gameObject.activeSelf) legacy.gameObject.SetActive(false);
        float width = _background.rect.width > 0f ? _background.rect.width : _background.sizeDelta.x;
        float height = _background.rect.height > 0f ? _background.rect.height : _background.sizeDelta.y;
        float scrollWidth = Mathf.Max(40f, width - _sideMargin * 2f);
        float scrollHeight = Mathf.Max(40f, height - _topMargin - _bottomMargin);
        float centerY = (_bottomMargin - _topMargin) * 0.5f;

        _scrollView.anchorMin = _scrollView.anchorMax = new Vector2(0.5f, 0.5f);
        _scrollView.pivot = new Vector2(0.5f, 0.5f);
        _scrollView.sizeDelta = new Vector2(scrollWidth, scrollHeight);
        _scrollView.anchoredPosition = new Vector2(0f, centerY);
        _scrollView.localScale = Vector3.one;

        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.pivot = new Vector2(0.5f, 0.5f);
        _viewport.anchoredPosition = Vector2.zero;
        _viewport.offsetMin = Vector2.zero;
        _viewport.offsetMax = new Vector2(-12f, 0f);

        if (_content != null)
        {
            float contentHeight = Mathf.Max(_viewport.rect.height, LayoutUtility.GetPreferredHeight(_content));
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _content.anchoredPosition.y);
            _content.sizeDelta = new Vector2(0f, contentHeight);
            Image oldNarrowBackground = _content.GetComponent<Image>();
            if (oldNarrowBackground != null)
            {
                oldNarrowBackground.raycastTarget = false;
                oldNarrowBackground.enabled = false;
            }
        }

        RectTransform scrollbarRect = _scrollbar?.transform as RectTransform;
        if (scrollbarRect != null)
        {
            if (scrollbarRect.parent != transform) scrollbarRect.SetParent(transform, false);
            scrollbarRect.anchorMin = scrollbarRect.anchorMax = new Vector2(0.5f, 0.5f);
            scrollbarRect.pivot = new Vector2(0.5f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(8f, Mathf.Max(20f, scrollHeight - 4f));
            scrollbarRect.anchoredPosition = new Vector2(width * 0.5f - _sideMargin - 5f, centerY);
            scrollbarRect.localScale = Vector3.one;
            scrollbarRect.SetAsLastSibling();
            scrollbarRect.gameObject.SetActive(true);
        }

        _scrollRect.viewport = _viewport;
        if (_content != null) _scrollRect.content = _content;
        if (_scrollRect.verticalScrollbar != _scrollbar) _scrollRect.verticalScrollbar = _scrollbar;
        _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
    }
}
