using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 挂在宽窗口左栏的最后一张卡上：把卡的底边拉到窗口底边(留 BottomMargin)，内容不够时卡里留空，
// 左栏总是排满一整列。卡的顶边由前面几张卡决定，不随自身高度变，所以每帧按顶边位置重算即可
public sealed class FillToWindowBottom : MonoBehaviour
{
    public RectTransform Background;
    public float BottomMargin = 10f;

    private LayoutElement _element;
    private readonly Vector3[] _corners = new Vector3[4];

    private void LateUpdate()
    {
        if (Background == null) return;
        if (_element == null)
        {
            _element = GetComponent<LayoutElement>();
            if (_element == null) _element = gameObject.AddComponent<LayoutElement>();
        }
        var rect = (RectTransform)transform;
        rect.GetWorldCorners(_corners);
        float top = Background.InverseTransformPoint(_corners[1]).y;
        float target = Mathf.Max(0f, Mathf.Floor(top - (Background.rect.yMin + BottomMargin)));
        if (Mathf.Abs(_element.minHeight - target) < 0.5f) return;
        _element.minHeight = target;
        LayoutRebuilder.MarkLayoutForRebuild(rect);
    }
}
