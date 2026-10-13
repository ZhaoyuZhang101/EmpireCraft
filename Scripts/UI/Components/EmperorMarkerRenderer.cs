using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图模式的皇帝标记：原版的君主、城主头像不画了(人物到处瞬移，头像闪来闪去，见 PureMapMode)，
// 改为在皇帝所属的城(没有就都城)城心上方画一枚皇帝标记——帝国颜色的圆形印面、旧铜环、金冕
// (Tools/IconForge/emperor_marker_forge.py，印面单独一层按帝国颜色染色)。不跟着实体走，所以不闪；
// 微服私访、出访、御驾亲征时标记像兵牌一样沿路走(见 EmperorTourSystem)，换了地块在一秒里匀速滑过去，暂停时不动。
// 画在地名画布上，按屏幕像素定大小。共和国的元首不画。
// 鼠标停在标记上：标记放大、置顶，显示皇帝的基本情况——帝国、年龄、在位几年、资质、眼下在做什么(在宫中、微服私访、
// 出访、御驾亲征、回銮途中)、储君、天命正统(兵牌悬停时让路)。
public static class EmperorMarkerRenderer
{
    private const float Size = 43f, Lift = 31f, HoverScale = 1.3f, TooltipRefresh = 1f;
    public const string TooltipType = "empirecraft_emperor_marker";

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
    // 本帧画出的标记：屏幕中心与帝国(悬停判定)
    private static readonly List<(Vector2 center, long empire, Mark mark)> Hits = new();
    private static long _hover = -1L;
    private static float _tipAt = float.NegativeInfinity;
    private static readonly object TipOwner = new();
    private static PointerEventData _pointer;

    // 悬停皇帝标记时原版地图提示让路(见 CorpsPatch)
    public static bool Hovering => _hover >= 0;
    private static readonly List<RaycastResult> RaycastHits = new();

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
        using var timing = FrameProfiler.Measure("纯净地图·皇帝标记");
        int used = 0;
        Hits.Clear();
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
                mark.Root.localScale = Vector3.one;
                Hits.Add((new Vector2(screen.x, screen.y + Lift), empire.data.id, mark));
                Color color = empire.getColor()?.getColorMain() ?? Color.white;
                color.a = 1f;
                mark.Field.color = color;
            }
        }
        for (int i = used; i < Marks.Count; i++)
            if (Marks[i].Root.gameObject.activeSelf) Marks[i].Root.gameObject.SetActive(false);
        UpdateHover();
    }

    private static void UpdateHover()
    {
        long found = -1L;
        Mark mark = null;
        if (InputHelpers.mouseSupported && !ScrollWindow.isWindowActive() && !ScrollWindow.isAnimationActive() &&
            !CorpsMapRenderer.Hovering)
        {
            Vector2 mouse = Input.mousePosition;
            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                float radius = Size * 0.5f * (Hits[i].empire == _hover ? HoverScale : 1f);
                if ((mouse - Hits[i].center).sqrMagnitude > radius * radius) continue;
                found = Hits[i].empire;
                mark = Hits[i].mark;
                break;
            }
            if (found >= 0 && PointerOverPanels()) found = -1L;
        }
        if (found < 0)
        {
            if (_hover < 0) return;
            _hover = -1L;
            Tooltip.hideTooltip(null, true, string.Empty);
            return;
        }
        if (found != _hover)
        {
            _hover = found;
            _tipAt = float.NegativeInfinity;
        }
        mark.Root.localScale = Vector3.one * HoverScale;
        mark.Root.SetAsLastSibling();
        float now = Time.unscaledTime;
        if (now - _tipAt < TooltipRefresh) return;
        _tipAt = now;
        Empire empire = ModClass.EMPIRE_MANAGER?.get(found);
        Tooltip.show(TipOwner, TooltipType, new TooltipData
        {
            kingdom = empire?.CoreKingdom, tooltip_scale = 0.7f, is_sim_tooltip = true, sound_allowed = false
        });
    }

    // 鼠标压在界面面板上(地名画布以外的 UI)时不算悬停
    private static bool PointerOverPanels()
    {
        if (EventSystem.current == null) return false;
        _pointer ??= new PointerEventData(EventSystem.current);
        _pointer.position = Input.mousePosition;
        RaycastHits.Clear();
        EventSystem.current.RaycastAll(_pointer, RaycastHits);
        Transform names = CanvasMain.instance?.canvas_map_names?.transform;
        return RaycastHits.Any(hit => hit.gameObject != null && (names == null || !hit.gameObject.transform.IsChildOf(names)));
    }

    // 悬停详情
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Empire empire = data?.kingdom?.GetEmpire();
        Actor emperor = empire?.Emperor;
        if (emperor?.data == null || !emperor.isAlive()) return;
        tooltip.clear();
        string colorHex = empire.getColor()?.color_text ?? "#F3C34A";
        tooltip.setTitle(emperor.getName(), "emperor_tip_subtitle", colorHex);
        const string Plain = "#D8D2B8";
        tooltip.addLineText(LM.Get("emperor_tip_empire"), empire.GetEmpireFullName(), colorHex, pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("emperor_tip_age"), string.Format(LM.Get("emperor_tip_age_value"), emperor.getAge(),
            empire.GetEmperorYear()), Plain, pPercent: false, pLocalize: false);
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(emperor);
        tooltip.addLineText(LM.Get("emperor_tip_grade"), LM.Get(grade switch
            {
                RulerQualitySystem.Grade.Wise => "trait_" + RulerQualitySystem.Wise,
                RulerQualitySystem.Grade.Weak => "trait_" + RulerQualitySystem.Weak,
                RulerQualitySystem.Grade.Fatuous => "trait_" + RulerQualitySystem.Fatuous,
                RulerQualitySystem.Grade.Dissolute => "trait_" + RulerQualitySystem.Dissolute,
                RulerQualitySystem.Grade.Tyrant => "trait_" + RulerQualitySystem.Tyrant,
                _ => "emperor_tip_grade_average"
            }), grade == RulerQualitySystem.Grade.Wise ? "#7CD27C" : grade == RulerQualitySystem.Grade.Average ? Plain : "#E07A5F",
            pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("emperor_tip_state"), State(empire), "#9FD0E8", pPercent: false, pLocalize: false);
        Kingdom core = empire.CoreKingdom;
        Actor heir = core != null && core.HasHeir() ? core.GetHeir() : null;
        bool designated = core != null && core.GetOrCreate().designated_heir_id >= 0;
        tooltip.addLineText(LM.Get("emperor_tip_heir"), heir?.data == null
                ? LM.Get("emperor_tip_no_heir")
                : string.Format(LM.Get(designated ? "emperor_tip_heir_designated" : "emperor_tip_heir_law"), heir.getName(), heir.getAge()),
            heir == null ? "#E07A5F" : Plain, pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("emperor_tip_mandate"), empire.Mandate.ToString(), "#F3C34A", pPercent: false, pLocalize: false);
    }

    // 眼下在做什么(见 EmperorTourSystem)
    private static string State(Empire empire)
    {
        var data = empire.data;
        if (string.IsNullOrEmpty(data.tour_kind))
            return string.Format(LM.Get("emperor_tip_state_palace"), EmperorTourSystem.Home(empire)?.GetCityName() ?? "");
        if (data.tour_returning) return LM.Get("emperor_tip_state_returning");
        if (data.tour_kind == EmperorTourSystem.Campaign)
        {
            CorpsData corps = CorpsSystem.Find(empire.CoreKingdom, data.tour_corps);
            return string.Format(LM.Get("emperor_tip_state_campaign"), corps == null ? "" : CorpsSystem.Name(corps));
        }
        City city = World.world.cities.get(data.tour_city);
        string where = data.tour_kind == EmperorTourSystem.Visit ? city?.kingdom?.data?.name ?? "" : city?.GetCityName() ?? "";
        return string.Format(LM.Get(data.tour_kind == EmperorTourSystem.Visit ? "emperor_tip_state_visit" : "emperor_tip_state_tour"), where);
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
