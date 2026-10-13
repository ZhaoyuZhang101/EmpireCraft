using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.Layer;

// 纯净地图模式下的城市发展图标(群友建议：建筑都隐藏了，看不出城市发展到哪一步)：
//   城心下方一排小图标(衬半透明深色底板)——住房(右下角写民居最高等级)、工厂(多于一座写座数)、
//   市场、港口、兵营、寺庙，有才画；图标由 IconForge 生成(ui/icons/citydev/*)。
//   每座城的建筑统计缓存两秒；镜头拉得太远不画。由 PureMapPatch 每帧提交，没提交的那帧自动隐藏。
public static class CityDevelopmentRenderer
{
    private const float IconWorldSize = 6f;
    private const float SpacingShare = 0.1f;
    private const float MinPixelsPerTile = 0.8f;
    private const float MinIconPixels = 14f;
    private const float MaxIconPixels = 30f;
    private const int MaxCities = 400;
    private const float CacheSeconds = 2f;

    private sealed class Stats
    {
        public int house = -1, factories;
        public bool market, port, barracks, temple;
        public float at;
    }

    private static readonly Dictionary<long, Stats> Cache = new();
    private static readonly Dictionary<string, Sprite> Sprites = new();
    private static RectTransform _root;
    private static CityDevelopmentHost _host;
    private static Font _font;
    private static int _submittedFrame = -1;
    private static readonly List<Image> _icons = new();
    private static readonly List<Text> _labels = new();
    private static readonly List<Image> _plates = new();
    private static int _usedIcons, _usedLabels, _usedPlates;
    private static readonly List<(string icon, string label)> Row = new();

    public static void Submit()
    {
        if (!EnsureHost()) return;
        _submittedFrame = Time.frameCount;
        _root.gameObject.SetActive(true);
        _usedIcons = _usedLabels = _usedPlates = 0;
        Camera camera = World.world?.camera;
        if (camera == null || camera.orthographicSize <= 0f) return;
        float scale = _root.lossyScale.x <= 0f ? 1f : _root.lossyScale.x;
        float pixelsPerTile = Screen.height / (2f * camera.orthographicSize);
        if (pixelsPerTile < MinPixelsPerTile)
        {
            HideUnused();
            return;
        }
        float iconPixels = Mathf.Clamp(IconWorldSize * pixelsPerTile, MinIconPixels, MaxIconPixels);
        float iconUnits = iconPixels / scale;
        float step = iconPixels * (1f + SpacingShare);
        int cities = 0;
        float now = Time.unscaledTime;
        foreach (City city in World.world.cities)
        {
            if (cities >= MaxCities) break;
            if (city?.data == null || city.isRekt() || city.kingdom == null) continue;
            Vector3 screen = camera.WorldToScreenPoint(city.city_center);
            if (screen.z < 0f || screen.x < -40f || screen.y < -40f || screen.x > Screen.width + 40f ||
                screen.y > Screen.height + 40f) continue;
            Stats stats = StatsOf(city, now);
            Row.Clear();
            if (stats.house >= 0) Row.Add(("house", stats.house.ToString()));
            if (stats.factories > 0) Row.Add(("factory", stats.factories > 1 ? stats.factories.ToString() : null));
            if (stats.market) Row.Add(("market", null));
            if (stats.port) Row.Add(("port", null));
            if (stats.barracks) Row.Add(("barracks", null));
            if (stats.temple) Row.Add(("temple", null));
            if (Row.Count == 0) continue;
            cities++;
            float y = screen.y - iconPixels * 1.3f;
            Image plate = NextPlate();
            plate.rectTransform.sizeDelta = new Vector2((Row.Count * step + iconPixels * 0.2f) / scale, iconPixels * 1.15f / scale);
            plate.rectTransform.position = new Vector3(screen.x, y, 0f);
            float x = screen.x - (Row.Count - 1) * step / 2f;
            foreach ((string iconId, string text) in Row)
            {
                Sprite sprite = SpriteOf(iconId);
                if (sprite != null)
                {
                    Image icon = NextIcon();
                    icon.sprite = sprite;
                    icon.rectTransform.sizeDelta = new Vector2(iconUnits, iconUnits);
                    icon.rectTransform.position = new Vector3(x, y, 0f);
                }
                if (!string.IsNullOrEmpty(text))
                {
                    Text label = NextLabel();
                    label.text = text;
                    label.fontSize = Mathf.Clamp(Mathf.RoundToInt(iconUnits * 0.5f), 4, 30);
                    label.rectTransform.sizeDelta = new Vector2(iconUnits, iconUnits * 0.6f);
                    label.rectTransform.position = new Vector3(x + iconPixels * 0.3f, y - iconPixels * 0.3f, 0f);
                }
                x += step;
            }
        }
        HideUnused();
    }

    private static Stats StatsOf(City city, float now)
    {
        if (Cache.TryGetValue(city.data.id, out Stats cached) && now - cached.at < CacheSeconds) return cached;
        var stats = new Stats { at = now };
        if (city.buildings != null)
            foreach (Building building in city.buildings)
            {
                BuildingAsset asset = building?.asset;
                if (asset == null || building.isUnderConstruction()) continue;
                switch (asset.type)
                {
                    case "type_house":
                        stats.house = Mathf.Max(stats.house, HouseTier(asset));
                        break;
                    case "type_market":
                        stats.market = true;
                        break;
                    case "type_docks":
                        stats.port = true;
                        break;
                    case "type_barracks":
                        stats.barracks = true;
                        break;
                    case "type_temple":
                        stats.temple = true;
                        break;
                }
                if (TechnologySystem.IsFactory(asset)) stats.factories++;
            }
        if (Cache.Count > 4000) Cache.Clear();
        Cache[city.data.id] = stats;
        return stats;
    }

    // 民居等级(从 1 起)：原版 house_*_N 为 N+1；近代模组的 Nhouse_*(接在原版之后)为 N+1；都不是就按升级等级
    private static int HouseTier(BuildingAsset asset)
    {
        string id = asset.id ?? "";
        int digits = 0;
        while (digits < id.Length && char.IsDigit(id[digits])) digits++;
        if (digits > 0 && int.TryParse(id.Substring(0, digits), out int prefix)) return prefix + 1;
        int underscore = id.LastIndexOf('_');
        if (underscore >= 0 && int.TryParse(id.Substring(underscore + 1), out int suffix)) return suffix + 1;
        return asset.upgrade_level + 1;
    }

    private static Sprite SpriteOf(string id)
    {
        if (Sprites.TryGetValue(id, out Sprite sprite)) return sprite;
        sprite = SpriteTextureLoader.getSprite("ui/icons/citydev/" + id);
        Sprites[id] = sprite;
        return sprite;
    }

    #region 复用的界面物件(同矿产图标，见 MineralIconRenderer)

    private static bool EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return false;
        if (_root != null && _root.parent == canvas.transform) return true;
        if (_root != null) Object.Destroy(_root.gameObject);
        _icons.Clear();
        _labels.Clear();
        _plates.Clear();
        var rootObject = new GameObject("EmpireCraftCityDevelopment", typeof(RectTransform), typeof(CityDevelopmentHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
        _host = rootObject.GetComponent<CityDevelopmentHost>();
        return true;
    }

    internal static void HostLateUpdate(CityDevelopmentHost host)
    {
        if (host != _host || _root == null) return;
        if (_submittedFrame != Time.frameCount && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
    }

    private static Image NextPlate()
    {
        Image plate;
        if (_usedPlates < _plates.Count) plate = _plates[_usedPlates];
        else
        {
            var go = new GameObject("CityDevPlate", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            go.transform.SetAsFirstSibling();
            plate = go.GetComponent<Image>();
            plate.raycastTarget = false;
            plate.color = new Color(0.06f, 0.05f, 0.04f, 0.55f);
            _plates.Add(plate);
        }
        _usedPlates++;
        if (!plate.gameObject.activeSelf) plate.gameObject.SetActive(true);
        return plate;
    }

    private static Image NextIcon()
    {
        Image icon;
        if (_usedIcons < _icons.Count) icon = _icons[_usedIcons];
        else
        {
            var go = new GameObject("CityDevIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            icon = go.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
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
            var go = new GameObject("CityDevLabel", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(_root, false);
            label = go.GetComponent<Text>();
            label.font = ResolveFont();
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.96f, 0.82f, 0.36f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            _labels.Add(label);
        }
        _usedLabels++;
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        return label;
    }

    private static void HideUnused()
    {
        for (int i = _usedPlates; i < _plates.Count; i++)
            if (_plates[i].gameObject.activeSelf) _plates[i].gameObject.SetActive(false);
        for (int i = _usedIcons; i < _icons.Count; i++)
            if (_icons[i].gameObject.activeSelf) _icons[i].gameObject.SetActive(false);
        for (int i = _usedLabels; i < _labels.Count; i++)
            if (_labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
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
        return _font;
    }

    #endregion
}

public sealed class CityDevelopmentHost : MonoBehaviour
{
    private void LateUpdate() => CityDevelopmentRenderer.HostLateUpdate(this);
}
