using System.Collections.Generic;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.Layer;

public sealed class MineralMapColor : MetaObject<IdeologyMapColorData>
{
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.Mineral;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static MineralMapColor Create(string name, string hex)
    {
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new MineralMapColor { _color = asset };
        color.loadData(new IdeologyMapColorData { name = name });
        return color;
    }
}

// 矿产图层(MetaTypeExtension.Mineral)：不画铭牌，每座城上方并排显示本城矿藏的图标(用资源原有图标)。
// 三种显示模式(图层按钮切换)：0 全部矿藏；1 正在开采的(矿场等级够、未枯竭)；2 枯竭冷却中的。
// 枯竭冷却中的矿：图标变灰，图标下方标出还要冷却几年。地块按城描边，有矿藏的城淡淡上色。
public static class MineralMap
{
    private static MineralMapColor _withDeposits, _empty;

    private static MineralMapColor WithDeposits => _withDeposits ??= MineralMapColor.Create("mineral_has", "#b08a4a");
    private static MineralMapColor Empty => _empty ??= MineralMapColor.Create("mineral_none", "#5a5a5a");

    private static bool Shown(City city) =>
        city != null && !city.isRekt() && city.kingdom != null && !AncientWarfareCompatibility.OwnsObject(city);

    // 按模式筛选后本城要显示的矿
    // 每座城的矿藏一秒刷新一次(每帧逐城数矿场太费)
    private static readonly Dictionary<City, (float at, List<(string id, bool minable, float remaining, int cooldown)> list)>
        Cache = new();

    public static List<(string id, bool minable, float remaining, int cooldown)> Visible(City city, int mode)
    {
        if (!Cache.TryGetValue(city, out var cached) || Time.unscaledTime - cached.at > 1f)
        {
            if (Cache.Count > (World.world?.cities?.Count ?? 0) * 2) Cache.Clear();
            Cache[city] = cached = (Time.unscaledTime, MineralResourceSystem.Deposits(city));
        }
        List<(string id, bool minable, float remaining, int cooldown)> all = cached.list;
        if (mode == 0) return all;
        var list = new List<(string, bool, float, int)>();
        foreach ((string id, bool minable, float remaining, int cooldown) entry in all)
            if (mode == 1 ? entry.minable && entry.cooldown <= 0 : entry.cooldown > 0) list.Add(entry);
        return list;
    }

    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            foreach (City city in World.world.cities)
            {
                if (!Shown(city)) continue;
                MineralMapColor color = Visible(city, mode).Count > 0 ? WithDeposits : Empty;
                foreach (TileZone zone in city.zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone,
                        zone.zone_up?.city != city, zone.zone_down?.city != city,
                        zone.zone_left?.city != city, zone.zone_right?.city != city, color.data, meta);
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            City hovered = tile?.zone?.city;
            if (!Shown(hovered)) return;
            QuantumSpriteLibrary.colorZones(highlight, hovered.zones, highlight.color);
        });
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) => false;
        asset.click_action_zone = new MetaZoneClickAction((tile, power) => false);
    }
}

// 矿产图标层：画在地图名字画布上(和领土大字同一层)，每帧由矿产图层的铭牌动作提交，没提交的帧整层隐藏
public static class MineralIconRenderer
{
    private const float IconSize = 16f;
    private const float Spacing = 2f;
    private const int MaxCities = 400;

    private static RectTransform _root;
    private static MineralIconHost _host;
    private static Font _font;
    private static int _submittedFrame = -1;
    private static readonly List<Image> _icons = new();
    private static readonly List<Text> _labels = new();
    private static int _usedIcons, _usedLabels;

    // 每帧调用(矿产图层激活时)
    public static void Submit(int mode)
    {
        if (!EnsureHost()) return;
        _submittedFrame = Time.frameCount;
        _root.gameObject.SetActive(true);
        _usedIcons = 0;
        _usedLabels = 0;
        Camera camera = World.world?.camera;
        if (camera == null) return;
        int cities = 0;
        foreach (City city in World.world.cities)
        {
            if (cities >= MaxCities) break;
            if (city == null || city.isRekt() || city.kingdom == null) continue;
            Vector3 screen = camera.WorldToScreenPoint(city.city_center);
            if (screen.z < 0f || screen.x < -40f || screen.y < -40f || screen.x > Screen.width + 40f ||
                screen.y > Screen.height + 40f) continue;
            List<(string id, bool minable, float remaining, int cooldown)> deposits = MineralMap.Visible(city, mode);
            if (deposits.Count == 0) continue;
            cities++;
            float scale = _root.lossyScale.x <= 0f ? 1f : _root.lossyScale.x;
            float step = (IconSize + Spacing) * scale;
            float x = screen.x - (deposits.Count - 1) * step / 2f;
            foreach ((string id, bool minable, float _, int cooldown) in deposits)
            {
                ResourceAsset resource = AssetManager.resources.get(id);
                Sprite sprite = resource?.getSpriteIcon();
                if (sprite == null) continue;
                Image icon = NextIcon();
                icon.sprite = sprite;
                // 冷却中：灰暗；还不能开采：半透明；正在开采：原色
                icon.color = cooldown > 0 ? new Color(0.45f, 0.45f, 0.45f, 0.9f) :
                    minable ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                icon.rectTransform.position = new Vector3(x, screen.y, 0f);
                if (cooldown > 0)
                {
                    Text label = NextLabel();
                    label.text = string.Format(NeoModLoader.General.LM.Get("mineral_layer_cooldown"), cooldown);
                    label.rectTransform.position = new Vector3(x, screen.y - IconSize * 0.75f * scale, 0f);
                }
                x += step;
            }
        }
        for (int i = _usedIcons; i < _icons.Count; i++)
            if (_icons[i].gameObject.activeSelf) _icons[i].gameObject.SetActive(false);
        for (int i = _usedLabels; i < _labels.Count; i++)
            if (_labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
    }

    internal static void HostLateUpdate(MineralIconHost host)
    {
        if (host != _host || _root == null) return;
        if (_submittedFrame != Time.frameCount && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
    }

    private static Image NextIcon()
    {
        Image icon;
        if (_usedIcons < _icons.Count) icon = _icons[_usedIcons];
        else
        {
            var go = new GameObject("MineralIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            icon = go.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
            _icons.Add(icon);
        }
        _usedIcons++;
        if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
        return icon;
    }

    private static Text NextLabel()
    {
        Text label;
        if (_usedLabels < _labels.Count) label = _labels[_usedLabels];
        else
        {
            var go = new GameObject("MineralCooldown", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(_root, false);
            label = go.GetComponent<Text>();
            label.font = ResolveFont();
            label.fontSize = 8;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 0.42f, 0.4f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.rectTransform.sizeDelta = new Vector2(IconSize * 2f, 10f);
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _labels.Add(label);
        }
        _usedLabels++;
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        return label;
    }

    private static Font ResolveFont()
    {
        if (_font != null) return _font;
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        _font = canvas == null ? null : canvas.GetComponentInChildren<Text>(true)?.font;
        if (_font == null)
        {
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { _font = null; }
        }
        if (_font == null)
        {
            try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { _font = null; }
        }
        return _font;
    }

    private static bool EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return false;
        if (_root != null && _root.parent == canvas.transform) return true;
        if (_root != null) Object.Destroy(_root.gameObject);
        _icons.Clear();
        _labels.Clear();
        var rootObject = new GameObject("EmpireCraftMineralIcons", typeof(RectTransform), typeof(MineralIconHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
        _host = rootObject.GetComponent<MineralIconHost>();
        return true;
    }
}

public sealed class MineralIconHost : MonoBehaviour
{
    private void LateUpdate() => MineralIconRenderer.HostLateUpdate(this);
}
