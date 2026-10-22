using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 宽窗口左栏(UIHelper.CreateWideWindowRail)挂在窗口背景上、不在滚动区里，滚动区重画后的强制排版管不到它。
// 卡片换过一批后，左栏头一遍按卡片新建时的默认高度排了位置，卡片随后才缩回实际高度，不再排就留下一截截空隙
// (卡片被拉散，底下的按钮掉出窗口)。这里在子物体变动后的几帧里把左栏再强制排几遍。
public class RailSettler : MonoBehaviour
{
    private const int Frames = 3;
    private int _pending;

    private void OnTransformChildrenChanged() => _pending = Frames;

    private void OnEnable() => _pending = Frames;

    private void LateUpdate()
    {
        if (_pending <= 0) return;
        _pending--;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }
}
