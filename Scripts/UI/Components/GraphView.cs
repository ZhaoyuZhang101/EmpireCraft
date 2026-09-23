using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public enum GraphOrientation
{
    Vertical,   // 子节点在父节点下方——科技树那种画法，折线走"竖-横-竖"
    Horizontal  // 下一代在右边——家谱那种画法，折线走"横-竖-横"
}

// 通用的"可拖拽平移/滚轮缩放"树状图/关系图引擎。只管三件跟具体业务无关的活：
//   1. 开一个带遮罩的视口 + 一层用绝对坐标摆节点的内容层，处理拖拽/缩放/回正/越界收紧；
//   2. 按两点画折线(方向按 orientation 决定是"竖-横-竖"还是"横-竖-横")或直线段；
//   3. 记住"整张图装得下的缩放/位置"，负责回正(ResetView)。
// 节点具体长什么样、坐标怎么算——这两件事完全不归这个类管，调用方自己算坐标、自己在
// ContentTransform 下面建节点。InstitutionGraphView(纵向·科技树排列)和
// SpecificClanGraphView(横向·家谱排列)都是在这上面搭的薄壳，各自只保留"这门业务的节点
// 坐标该怎么算、节点该长什么样"，viewport/拖拽/缩放/连线这些通用的活都丢给这个类。
// 以后要加新的树状图，不用再重抄一遍这一整套 viewport+拖拽+缩放+连线，只需要:
//   1. GraphView.Create(...) 开视口；
//   2. 自己算一份 "id -> 坐标" 的字典(科技树式的排列可以直接用 GraphLayout.TieredLanes)；
//   3. 用 CreateElbow/CreateSegment 画连线，自己在 ContentTransform 下建节点；
//   4. 调 SetContent(...) 告诉这个类"整张图多大、缩放/起始位置该是多少"，然后就能拖能缩了。
public class GraphView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    private const float MinScale = 0.45f;
    private const float MaxScale = 2.5f;

    private RectTransform _viewport;
    private RectTransform _content;
    private RectTransform _lines;
    private GraphOrientation _orientation = GraphOrientation.Vertical;
    private Vector2 _contentSize = Vector2.one;
    private float _scale = 1f;
    private float _fitScale = 1f;
    private Vector2 _fitPosition;
    private Vector2 _dragStartContent;
    private Vector2 _dragStartPointer;

    public Transform ContentTransform => _content;
    public RectTransform ViewportTransform => _viewport;
    public Transform LinesTransform => _lines;
    public GraphOrientation Orientation => _orientation;

    public static GraphView Create(Transform parent, Vector2 size,
        GraphOrientation orientation = GraphOrientation.Vertical, Color? backgroundColor = null,
        string objectName = "GraphViewport")
    {
        var viewportObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(LayoutElement), typeof(GraphView));
        var viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(parent, false);
        viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.sizeDelta = size;

        // 视口自己得是个能被射线命中的 Graphic，否则拖拽/滚轮事件根本不会派发到这里。
        Image background = viewportObject.GetComponent<Image>();
        background.sprite = null;
        background.color = backgroundColor ?? new Color(0.09f, 0.10f, 0.13f, 0.55f);
        background.raycastTarget = true;

        LayoutElement layout = viewportObject.GetComponent<LayoutElement>();
        layout.preferredWidth = size.x;
        layout.preferredHeight = size.y;
        layout.minWidth = size.x;
        layout.minHeight = size.y;

        var view = viewportObject.GetComponent<GraphView>();
        view._viewport = viewport;
        view._orientation = orientation;

        var contentObject = new GameObject("Content", typeof(RectTransform));
        view._content = contentObject.GetComponent<RectTransform>();
        view._content.SetParent(viewport, false);
        view._content.anchorMin = view._content.anchorMax = new Vector2(0.5f, 0.5f);
        view._content.pivot = new Vector2(0.5f, 0.5f);
        view._content.anchoredPosition = Vector2.zero;

        // 连线容器先建，保证它在层级里排在节点前面——UGUI 按层级顺序绘制，
        // 先建的先画，连线才会压在节点卡片底下而不是盖在上面。
        var linesObject = new GameObject("Edges", typeof(RectTransform));
        view._lines = linesObject.GetComponent<RectTransform>();
        view._lines.SetParent(view._content, false);
        view._lines.anchorMin = view._lines.anchorMax = new Vector2(0.5f, 0.5f);
        view._lines.pivot = new Vector2(0.5f, 0.5f);
        view._lines.anchoredPosition = Vector2.zero;
        view._lines.sizeDelta = Vector2.zero;

        return view;
    }

    // 清空内容层里的节点(连线容器本身不会被清掉，见 ClearEdges)。调用方在 Rebuild 开头
    // 一般是 ClearEdges() + ClearNodes() 一起调，再重新画一遍。
    public void ClearNodes()
    {
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            Transform child = _content.GetChild(i);
            if (child != _lines) Destroy(child.gameObject);
        }
    }

    public void ClearEdges()
    {
        for (int i = _lines.childCount - 1; i >= 0; i--) Destroy(_lines.GetChild(i).gameObject);
    }

    // 折线：orientation=Vertical 时从父节点底边垂直下来、中间横过去、再垂直进子节点顶边
    // (科技树那种画法)；Horizontal 时反过来，从父节点侧边横过去、中间竖过去、再横进子节点
    // 侧边(家谱那种画法)。同一条车道/同一行内(起止点在折线方向上重合)只画一根直线，不拐角。
    public void CreateElbow(Vector2 from, Vector2 to, Vector2 nodeHalfSize, Color color, float thickness = 1f)
    {
        if (_orientation == GraphOrientation.Vertical)
        {
            Vector2 start = from + new Vector2(0f, -nodeHalfSize.y);
            Vector2 end = to + new Vector2(0f, nodeHalfSize.y);
            if (Mathf.Abs(start.x - end.x) < 0.5f)
            {
                CreateSegment(start, new Vector2(start.x, end.y), color, thickness);
                return;
            }
            float midY = (start.y + end.y) / 2f;
            CreateSegment(start, new Vector2(start.x, midY), color, thickness);
            CreateSegment(new Vector2(start.x, midY), new Vector2(end.x, midY), color, thickness);
            CreateSegment(new Vector2(end.x, midY), end, color, thickness);
        }
        else
        {
            float dir = to.x >= from.x ? 1f : -1f;
            Vector2 start = from + new Vector2(dir * nodeHalfSize.x, 0f);
            Vector2 end = to - new Vector2(dir * nodeHalfSize.x, 0f);
            if (Mathf.Abs(start.y - end.y) < 0.5f)
            {
                CreateSegment(start, end, color, thickness);
                return;
            }
            float midX = (start.x + end.x) / 2f;
            CreateSegment(start, new Vector2(midX, start.y), color, thickness);
            CreateSegment(new Vector2(midX, start.y), new Vector2(midX, end.y), color, thickness);
            CreateSegment(new Vector2(midX, end.y), end, color, thickness);
        }
    }

    // 不拐角的直线段——婚姻线这种"同一列/同一行内的短连接"，或者调用方想自己控制折线形状时用。
    public void CreateSegment(Vector2 a, Vector2 b, Color color, float thickness = 1f)
    {
        var segmentObject = new GameObject("Segment", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = segmentObject.GetComponent<RectTransform>();
        rect.SetParent(_lines, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (a + b) / 2f;
        bool horizontal = Mathf.Abs(a.y - b.y) < 0.5f;
        rect.sizeDelta = horizontal
            ? new Vector2(Mathf.Abs(b.x - a.x) + thickness, thickness)
            : new Vector2(thickness, Mathf.Abs(b.y - a.y) + thickness);
        Image image = segmentObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    // 调用方算完所有节点坐标之后调一次：告诉这个类整张图多大(用来收紧拖拽越界)、
    // 一进来该缩到多少、起始位置在哪(通常是"刚好把整张图或者关注的那一部分塞进视口")。
    // 具体怎么算 fitScale/fitPosition 完全由调用方决定——纵向的科技树只按宽度收缩、
    // 顶端对齐视口上沿；横向的家谱树按宽高都收缩、把"本人"那一列贴到视口左侧——
    // 这两种对齐方式差异太大，没法在这里生一套通用公式，所以干脆不猜，调用方给多少用多少。
    public void SetContent(Vector2 contentSize, float fitScale, Vector2 fitPosition)
    {
        _contentSize = contentSize.x > 0f && contentSize.y > 0f ? contentSize : Vector2.one;
        _content.sizeDelta = _contentSize;
        _fitScale = Mathf.Clamp(fitScale, MinScale, 1f);
        _fitPosition = fitPosition;
        ResetView();
    }

    public void ResetView()
    {
        _scale = _fitScale;
        _content.localScale = new Vector3(_scale, _scale, 1f);
        _content.anchoredPosition = _fitPosition;
        ClampPosition();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragStartContent = _content.anchoredPosition;
        _dragStartPointer = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 用屏幕坐标差 / canvas 缩放，避免跟 content 自己的 localScale 叠加成"越放大拖得越快"
        float canvasScale = _viewport.lossyScale.x <= 0f ? 1f : _viewport.lossyScale.x;
        _content.anchoredPosition = _dragStartContent + (eventData.position - _dragStartPointer) / canvasScale;
        ClampPosition();
    }

    public void OnScroll(PointerEventData eventData)
    {
        float delta = eventData.scrollDelta.y;
        if (Mathf.Approximately(delta, 0f)) return;
        float previous = _scale;
        _scale = Mathf.Clamp(_scale * (1f + Mathf.Sign(delta) * 0.12f), MinScale, MaxScale);
        if (Mathf.Approximately(previous, _scale)) return;
        // 以光标所在点为锚缩放：算出光标在视口里的局部坐标，按比例变化反推内容该挪多少
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position,
                eventData.pressEventCamera, out Vector2 local))
        {
            Vector2 offset = _content.anchoredPosition - local;
            _content.anchoredPosition = local + offset * (_scale / previous);
        }
        _content.localScale = new Vector3(_scale, _scale, 1f);
        ClampPosition();
    }

    // 允许拖出去一部分，但不允许整张图被拖出视口外面找不回来。margin 通常传半个节点尺寸，
    // 跟原来两份实现的口径一致(拖到只剩半个节点露在视口边缘就收紧)。
    private Vector2 _clampMargin = new(25f, 12f);
    public void SetClampMargin(Vector2 margin) => _clampMargin = margin;

    private void ClampPosition()
    {
        Vector2 half = new Vector2(_contentSize.x * _scale / 2f, _contentSize.y * _scale / 2f);
        Vector2 limit = new Vector2(half.x + _viewport.rect.width / 2f - _clampMargin.x * _scale,
            half.y + _viewport.rect.height / 2f - _clampMargin.y * _scale);
        Vector2 position = _content.anchoredPosition;
        position.x = Mathf.Clamp(position.x, -limit.x, limit.x);
        position.y = Mathf.Clamp(position.y, -limit.y, limit.y);
        _content.anchoredPosition = position;
    }
}
