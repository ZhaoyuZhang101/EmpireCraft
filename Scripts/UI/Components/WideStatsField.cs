using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 原版键值行的两个文本区是固定窄尺寸；宽窗口里按比例分栏，长值自动换行。
// 仅挂在模组自己的宽窗口行上，保留原版的点击、提示和隔行底色。
public sealed class WideStatsField : MonoBehaviour
{
    private KeyValueField _field;
    private RectTransform _rect;
    private float _width = -1f;
    private float _parentWidth = -1f;

    public void Bind(KeyValueField field)
    {
        _field = field;
        _rect = transform as RectTransform;
        // 原版预制体把两个文本放在一个仍为 13 高的 Text 容器里。
        // 直接挂在行上，行增高时文本也随之增高，保留文本自身的按钮和提示组件。
        if (field.name_text != null && field.name_text.transform.parent != transform)
            field.name_text.transform.SetParent(transform, false);
        if (field.value != null && field.value.transform.parent != transform)
            field.value.transform.SetParent(transform, false);
        Reflow();
    }

    private void OnEnable() => Reflow();
    private void LateUpdate()
    {
        var parent = transform.parent as RectTransform;
        if (_rect != null && (!Mathf.Approximately(_width, _rect.rect.width) ||
            (parent != null && !Mathf.Approximately(_parentWidth, parent.rect.width)))) Reflow();
    }

    private void Reflow()
    {
        if (_field == null || _rect == null) return;
        var parent = transform.parent as RectTransform;
        if (parent == null) return;
        _parentWidth = parent.rect.width;
        var layout = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
        float width = Mathf.Max(120f, parent.rect.width - (layout?.padding.horizontal ?? 0));
        _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        _width = width;
        float split = width * 0.56f;
        float labelInset = _field.icon != null && _field.icon.gameObject.activeSelf ? 17f : 4f;
        Place(_field.name_text, labelInset, width - split + 5f, TextAnchor.MiddleLeft);
        Place(_field.value, split + 4f, 4f, TextAnchor.MiddleRight);
        // preferredHeight uses the widths assigned above, including rich text and multiline values.
        float height = Mathf.Max(14f, Mathf.Max(_field.name_text.preferredHeight, _field.value.preferredHeight) + 4f);
        _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        var element = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.minWidth = element.preferredWidth = width;
        // 背景可能就是行本身，不能把行锚定为铺满父面板。
        if (_field.background != null && _field.background.rectTransform != _rect)
            Stretch(_field.background.rectTransform, 0f, 0f);
        if (_field.icon != null)
        {
            RectTransform icon = _field.icon.rectTransform;
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(13f, 13f);
            icon.anchoredPosition = new Vector2(9f, 0f);
        }
    }

    private static void Place(Text text, float left, float right, TextAnchor alignment)
    {
        if (text == null) return;
        text.resizeTextForBestFit = false;
        text.fontSize = 8;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        Stretch(text.rectTransform, left, right);
    }

    private static void Stretch(RectTransform rect, float left, float right)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, 0f);
        rect.offsetMax = new Vector2(-right, 0f);
    }
}
