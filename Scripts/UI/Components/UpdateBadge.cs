using EmpireCraft.Scripts.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 更新按钮右上角的小红点：检测到新版本时闪烁，没有新版本时隐藏
public class UpdateBadge : MonoBehaviour
{
    private const float PollSeconds = 1f;
    private const int DotSize = 9;
    private static Sprite _sprite;

    private Image _dot;
    private float _nextPoll;
    private bool _visible;

    public static void Attach(Component button)
    {
        if (button == null || button.GetComponentInChildren<UpdateBadge>(true) != null) return;
        var go = new GameObject("UpdateBadge", typeof(RectTransform));
        go.transform.SetParent(button.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-1f, -1f);
        rect.sizeDelta = new Vector2(DotSize, DotSize);
        var image = go.AddComponent<Image>();
        image.sprite = GetSprite();
        image.raycastTarget = false;
        var badge = go.AddComponent<UpdateBadge>();
        badge._dot = image;
        badge.SetVisible(false);
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + PollSeconds;
            SetVisible(EmpireCraftUpdateService.HasUpdateAvailable);
        }
        if (!_visible) return;
        // 闪烁：透明度在 0.25 ~ 1 之间往复
        float t = Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
        Color c = _dot.color;
        c.a = Mathf.Lerp(0.25f, 1f, t);
        _dot.color = c;
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        _dot.enabled = visible;
        transform.SetAsLastSibling();
    }

    // 像素风红点：深红描边 + 红色填充 + 左上高光
    private static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        var tex = new Texture2D(DotSize, DotSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        float c = (DotSize - 1) / 2f;
        for (int y = 0; y < DotSize; y++)
        for (int x = 0; x < DotSize; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            Color color = d > c + 0.3f ? Color.clear
                : d > c - 0.9f ? new Color32(110, 10, 16, 255)
                : new Color32(235, 40, 40, 255);
            tex.SetPixel(x, y, color);
        }
        // 高光(纹理 y 轴向上)
        tex.SetPixel(3, 5, new Color32(255, 170, 160, 255));
        tex.SetPixel(3, 6, new Color32(255, 170, 160, 255));
        tex.SetPixel(2, 5, new Color32(255, 170, 160, 255));
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, DotSize, DotSize), new Vector2(0.5f, 0.5f), 100f);
        return _sprite;
    }
}
