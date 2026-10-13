using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图模式的皇帝标记：原版的君主、城主头像不画了(人物到处瞬移，头像闪来闪去，见 PureMapMode)，
// 改为在皇帝所属的城(没有就都城)城心上方画一枚皇帝标记——帝国颜色的圆形印面、旧铜环、金冕
// (Tools/IconForge/emperor_marker_forge.py，印面单独一层按帝国颜色染色)。不跟着实体走，所以不闪；
// 微服私访、出访、御驾亲征时标记像兵牌一样沿路走(见 EmperorTourSystem)，换了地块在一秒里匀速滑过去，暂停时不动。
// 画在地名画布上，按屏幕像素定大小。共和国的元首不画。
public static class EmperorMarkerRenderer
{
    private const float Size = 36f, Lift = 26f;

    private sealed class Mark
    {
        public RectTransform Root;
        public Image Field, Overlay;
    }

    private static RectTransform _root;
    private static readonly List<Mark> Marks = new();
    // 各帝国的标记画在哪(世界坐标)，滑向目标
    private static readonly Dictionary<long, Vector3> Drawn = new();
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
                TileZone away = EmperorTourSystem.Away(empire);
                WorldTile tile = away?.centerTile ?? EmperorTourSystem.Home(empire)?.getTile();
                if (tile == null) continue;
                Vector3 at = Glide(empire.data.id, tile.posV);
                Vector3 screen = camera.WorldToScreenPoint(at);
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

    // 一个世界秒走一块地：换了位置就匀速滑过去(太远就直接到，如换了皇帝)；暂停时不动
    private static Vector3 Glide(long id, Vector3 target)
    {
        if (!Drawn.TryGetValue(id, out Vector3 at) || (target - at).magnitude > 40f)
        {
            Drawn[id] = target;
            return target;
        }
        float delta = Config.paused ? 0f : Time.deltaTime * Mathf.Max(1f, Config.time_scale_asset?.multiplier ?? 1f);
        at = Vector3.MoveTowards(at, target, Mathf.Max(8f, (target - at).magnitude) * delta);
        Drawn[id] = at;
        return at;
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
