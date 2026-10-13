using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图模式的皇帝标记：原版的君主、城主头像不画了(人物到处瞬移，头像闪来闪去，见 PureMapMode)，
// 改为在皇帝所属的城(没有就都城)城心上方画一枚皇帝标记——帝国颜色的圆形印面、旧铜环、金冕
// (Tools/IconForge/emperor_marker_forge.py，印面单独一层按帝国颜色染色)。跟着城不跟着人，所以不闪。
// 画在地名画布上，按屏幕像素定大小。共和国的元首不画。
public static class EmperorMarkerRenderer
{
    private const float Size = 22f, Lift = 18f;

    private sealed class Mark
    {
        public RectTransform Root;
        public Image Field, Overlay;
    }

    private static RectTransform _root;
    private static readonly List<Mark> Marks = new();
    private static Sprite _field, _overlay;

    public static void EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return;
        if (_root != null && _root.parent == canvas.transform) return;
        if (_root != null) Object.Destroy(_root.gameObject);
        Marks.Clear();
        var rootObject = new GameObject("EmpireCraftEmperors", typeof(RectTransform), typeof(EmperorMarkerHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsFirstSibling();
    }

    internal static void HostLateUpdate()
    {
        int used = 0;
        if (PureMapMode.Enabled && World.world?.camera != null && CanvasMain.instance?.canvas_map_names != null &&
            ModClass.EMPIRE_MANAGER != null)
        {
            Camera camera = World.world.camera;
            float scale = Mathf.Max(0.01f, CanvasMain.instance.canvas_map_names.scaleFactor);
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                Actor emperor = empire?.data == null ? null : empire.Emperor;
                if (emperor?.data == null || emperor.isRekt() || !emperor.isAlive() || RepublicSystem.IsRepublic(empire)) continue;
                City city = emperor.city?.data != null && !emperor.city.isRekt() ? emperor.city : empire.CoreKingdom?.capital;
                WorldTile tile = city?.getTile();
                if (tile == null) continue;
                Vector3 screen = camera.WorldToScreenPoint(tile.posV);
                if (screen.z < 0f || screen.x < -40f || screen.x > Screen.width + 40f || screen.y < -40f ||
                    screen.y > Screen.height + 40f) continue;
                Mark mark = Get(used++);
                mark.Root.gameObject.SetActive(true);
                mark.Root.position = screen + new Vector3(0f, Lift);
                mark.Root.sizeDelta = new Vector2(Size, Size) / scale;
                Color color = empire.getColor()?.getColorMain() ?? Color.white;
                color.a = 1f;
                mark.Field.color = color;
            }
        }
        for (int i = used; i < Marks.Count; i++)
            if (Marks[i].Root.gameObject.activeSelf) Marks[i].Root.gameObject.SetActive(false);
    }

    private static Mark Get(int index)
    {
        while (Marks.Count <= index)
        {
            var rootObject = new GameObject("EmperorMark", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(_root, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            Marks.Add(new Mark
            {
                Root = root,
                Field = Layer(root, "Field", _field ??= Load("ui/icons/emperor/marker_field")),
                Overlay = Layer(root, "Overlay", _overlay ??= Load("ui/icons/emperor/marker"))
            });
        }
        return Marks[index];
    }

    private static Sprite Load(string path)
    {
        Sprite sprite = SpriteTextureLoader.getSprite(path);
        if (sprite != null) sprite.texture.filterMode = FilterMode.Point;
        return sprite;
    }

    private static Image Layer(RectTransform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }
}

public sealed class EmperorMarkerHost : MonoBehaviour
{
    private void LateUpdate() => EmperorMarkerRenderer.HostLateUpdate();
}
