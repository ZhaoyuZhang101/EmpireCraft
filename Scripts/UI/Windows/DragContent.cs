using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace EmpireCraft.Scripts.UI.Windows;
public class DragContent : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _rt;
    private Vector2 _pointerStartLocalPos;
    private Vector2 _contentStartPos;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rt, e.position, e.pressEventCamera, out _pointerStartLocalPos);

        _contentStartPos = _rt.anchoredPosition;
    }

    // 拖拽时，不断计算偏移量，更新 content 的 anchoredPosition
    public void OnDrag(PointerEventData e)
    {
        Vector2 pointerLocalPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rt, e.position, e.pressEventCamera, out pointerLocalPos);

        Vector2 diff = pointerLocalPos - _pointerStartLocalPos;
        _rt.anchoredPosition = _contentStartPos + diff;
    }
}