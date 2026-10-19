using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 地图上的兵牌、皇帝标记画在这块画布上：与原版地名画布同级、同排序，紧跟在它后面，缩放每帧照它对齐。
// 不直接挂在地名画布下——原版关掉「地图名称」时 NameplateManager 把地名画布整个停用，挂在下面的跟着一起看不见
// (群友反映打仗也找不到军队)
public static class MapOverlayCanvas
{
    private static Canvas _canvas, _names;
    private static CanvasScaler _scaler, _namesScaler;
    private static int _syncedFrame = -1;

    public static Canvas Current => _canvas;

    // 取画布，没有就照地名画布建一块；地名画布还没有时返回空
    public static Canvas Ensure()
    {
        Canvas names = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (names == null) return null;
        if (_canvas != null && _names == names) return _canvas;
        if (_canvas != null) Object.Destroy(_canvas.gameObject);
        var canvasObject = new GameObject("EmpireCraftMapOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.layer = names.gameObject.layer;
        canvasObject.transform.SetParent(names.transform.parent, false);
        canvasObject.transform.SetSiblingIndex(names.transform.GetSiblingIndex() + 1);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = names.renderMode;
        canvas.worldCamera = names.worldCamera;
        canvas.planeDistance = names.planeDistance;
        canvas.sortingLayerID = names.sortingLayerID;
        canvas.sortingOrder = names.sortingOrder;
        canvas.overrideSorting = names.overrideSorting;
        canvas.pixelPerfect = names.pixelPerfect;
        _names = names;
        _namesScaler = names.GetComponent<CanvasScaler>();
        _canvas = canvas;
        _scaler = canvasObject.GetComponent<CanvasScaler>();
        _syncedFrame = -1;
        Sync();
        return _canvas;
    }

    // 缩放照地名画布(随原版「地名大小」设置变，见 CanvasMain.resizeMapNames)；一帧只对齐一次
    public static void Sync()
    {
        if (_namesScaler == null || _scaler == null || _syncedFrame == Time.frameCount) return;
        _syncedFrame = Time.frameCount;
        if (_scaler.uiScaleMode != _namesScaler.uiScaleMode) _scaler.uiScaleMode = _namesScaler.uiScaleMode;
        if (_scaler.referenceResolution != _namesScaler.referenceResolution)
            _scaler.referenceResolution = _namesScaler.referenceResolution;
        if (_scaler.screenMatchMode != _namesScaler.screenMatchMode) _scaler.screenMatchMode = _namesScaler.screenMatchMode;
        if (!Mathf.Approximately(_scaler.matchWidthOrHeight, _namesScaler.matchWidthOrHeight))
            _scaler.matchWidthOrHeight = _namesScaler.matchWidthOrHeight;
        if (!Mathf.Approximately(_scaler.scaleFactor, _namesScaler.scaleFactor)) _scaler.scaleFactor = _namesScaler.scaleFactor;
        if (!Mathf.Approximately(_scaler.referencePixelsPerUnit, _namesScaler.referencePixelsPerUnit))
            _scaler.referencePixelsPerUnit = _namesScaler.referencePixelsPerUnit;
    }

    // 射线打到的是地图上的画(地名画布或这块画布)，不算界面面板
    public static bool IsMap(Transform at) =>
        at != null && (_canvas != null && at.IsChildOf(_canvas.transform) || _names != null && at.IsChildOf(_names.transform));

    // 原版地名铭牌的字体
    public static Font NameplateFont() => _names == null ? null : _names.GetComponent<NameplateManager>()?.prefab?._text_name?.font;
}
