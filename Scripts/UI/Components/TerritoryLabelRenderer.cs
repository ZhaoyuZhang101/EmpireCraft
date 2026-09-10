using System;
using System.Collections.Generic;
using EmpireCraft;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public enum TerritoryLabelOrientation
{
    Auto,
    Horizontal,
    Vertical
}

public sealed class TerritoryLabelStyle
{
    public Color text_color = new Color(1f, 1f, 1f, 0.8f);
    public Color outline_color = new Color(0.08f, 0.07f, 0.05f, 0.8f);
    public TerritoryLabelOrientation orientation = TerritoryLabelOrientation.Auto;
    public float size_multiplier = 0.9f;
    public float territory_padding = 0.82f;
    public int min_font_size = 3;
    public int max_font_size = 512;
    public FontStyle font_style = FontStyle.Normal;
    public bool use_gold_gradient;
    // Only the Empire label may wrap long Latin names; ordinary nameplates stay single-line.
    public bool wrap_english_name;
    public bool bridge_internal_gaps;
    // A shallow arc follows broad, irregular territories without changing the label's anchor.
    public float arc_curvature;
    public Color gold_top = new Color32(255, 244, 194, 255);
    public Color gold_bottom = new Color32(184, 156, 68, 255);
}

// Renders one rotated line inside the territory, independently of WorldBox nameplates.
public static class TerritoryLabelRenderer
{
    private const float GeometryRefreshInterval = 0.75f;
    private const float LatinOutlineDistance = 8f;
    private static readonly Dictionary<string, RuntimeLabel> _labels = new Dictionary<string, RuntimeLabel>();
    private static readonly List<RuntimeLabel> _render_order = new List<RuntimeLabel>();
    private static readonly List<Rect> _occupied_label_bounds = new List<Rect>();
    private static TerritoryLabelRendererHost _host;
    private static RectTransform _root;
    private static Text _text_template;
    private static Font _clerical_font;
    private static bool _clerical_font_resolved;
    private static Font _latin_font;
    private static bool _latin_font_resolved;
    private static int _submission_frame = -1;

    private static readonly string[] ClericalFontNames =
    {
        "LiSu",
        "隶书",
        "STLiti",
        "华文隶书"
    };

    private static readonly string[] LatinFontNames =
    {
        "Georgia",
        "Arial Black",
        "Trebuchet MS",
        "Segoe UI"
    };

    public static readonly TerritoryLabelStyle EmpireStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.8f),
        outline_color = new Color32(54, 46, 20, 204),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 1,
        font_style = FontStyle.Normal,
        use_gold_gradient = true,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.035f
    };

    public static readonly TerritoryLabelStyle FadedEmpireStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.224f),
        outline_color = new Color32(54, 46, 20, 49),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 1,
        font_style = FontStyle.Normal,
        use_gold_gradient = true,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.035f
    };

    public static readonly TerritoryLabelStyle KingdomStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.9f),
        size_multiplier = 0.95f,
        min_font_size = 3,
        font_style = FontStyle.Normal,
        arc_curvature = 0.025f
    };

    public static readonly TerritoryLabelStyle RebellionKingdomStyle = new TerritoryLabelStyle
    {
        text_color = new Color32(235, 65, 57, 204),
        outline_color = new Color32(74, 15, 12, 204),
        size_multiplier = 0.9f,
        min_font_size = 3,
        font_style = FontStyle.Normal,
        arc_curvature = 0.025f
    };

    public static void RenderLawLayer(int zoneOptionState)
    {
        BeginFrame();
        City hoveredCity = World.world?.getMouseTilePosCachedFrame()?.zone_city;
        if (zoneOptionState == 0)
        {
            foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
            {
                if (core == null || core.id <= 0) continue;
                SubmitEmpireCore($"law-empire:{core.id}", EmpireCoreManager.GetDisplayName(core), core, EmpireStyle,
                    hoveredCity != null && hoveredCity.GetEmpireCoreID() == core.id);
            }
        }

        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            if (title == null || title.isRekt() || title.data == null) continue;
            if (zoneOptionState == 0 && IsClaimedByEmpireCore(title)) continue;
            IEnumerable<City> cities = title.getCities();
            SubmitCities($"law-kingdom:{title.id}", title.data.name, cities, KingdomStyle,
                hoveredCity?.GetTitle() == title);
        }
        EndFrame();
    }

    public static void BeginFrame()
    {
        if (!EnsureHost()) return;
        _submission_frame = Time.frameCount;
    }

    public static void SubmitCities(string id, string text, IEnumerable<City> cities, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || cities == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromCities(text, cities, style ?? KingdomStyle, fullyOpaque);
    }

    public static void SubmitEmpireCore(string id, string text, EmpireCore core, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || core == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromEmpireCore(text, core, style ?? EmpireStyle, fullyOpaque);
    }

    // Future country layers can use zones to get the same exact containment as the law layer.
    public static void SubmitZones(string id, string text, IEnumerable<TileZone> zones, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || zones == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromZones(text, zones, style ?? KingdomStyle, fullyOpaque);
    }

    // Point input is for non-zone overlays and uses an oriented point-cloud fit.
    public static void SubmitPoints(string id, string text, IEnumerable<Vector3> points, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || points == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromPoints(text, points, style ?? KingdomStyle, fullyOpaque);
    }

    public static void EndFrame()
    {
        if (_submission_frame != Time.frameCount) return;
        foreach (RuntimeLabel label in _labels.Values)
            if (label.last_seen_frame != _submission_frame) label.Hide();
    }

    private static bool ContainsLatin(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
        }
        return false;
    }

    private static Font ResolveInstalledFont(string[] candidates, Font fallback, ref Font cachedFont, ref bool resolved)
    {
        if (resolved) return cachedFont ?? fallback;
        resolved = true;

        try
        {
            string[] installedFonts = Font.GetOSInstalledFontNames();
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string candidate = candidates[candidateIndex];
                for (int installedIndex = 0; installedIndex < installedFonts.Length; installedIndex++)
                {
                    if (!string.Equals(installedFonts[installedIndex], candidate, StringComparison.OrdinalIgnoreCase)) continue;
                    cachedFont = Font.CreateDynamicFontFromOSFont(installedFonts[installedIndex], 64);
                    if (cachedFont != null) return cachedFont;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"EmpireCraft could not load a territory label font: {exception.Message}");
        }

        return fallback;
    }

    private static Font ResolveTerritoryFont(Font fallback, string text)
    {
        return ContainsLatin(text)
            ? ResolveInstalledFont(LatinFontNames, fallback, ref _latin_font, ref _latin_font_resolved)
            : ResolveInstalledFont(ClericalFontNames, fallback, ref _clerical_font, ref _clerical_font_resolved);
    }

    public static void HideAll()
    {
        foreach (RuntimeLabel label in _labels.Values) label.Hide();
    }

    internal static void HostLateUpdate(TerritoryLabelRendererHost host)
    {
        if (host != _host) return;
        if (_submission_frame != Time.frameCount) { HideAll(); return; }
        _render_order.Clear();
        foreach (RuntimeLabel label in _labels.Values)
            if (label.last_seen_frame == Time.frameCount) _render_order.Add(label);
        _render_order.Sort((left, right) => right.RenderPriority.CompareTo(left.RenderPriority));
        _occupied_label_bounds.Clear();
        foreach (RuntimeLabel label in _render_order) label.RenderSubmitted();
    }

    internal static void HostDestroyed(TerritoryLabelRendererHost host)
    {
        if (host != _host) return;
        _host = null;
        _root = null;
        _text_template = null;
        _labels.Clear();
        _render_order.Clear();
        _occupied_label_bounds.Clear();
    }

    private static void MarkSubmissionFrame()
    {
        if (_submission_frame != Time.frameCount) _submission_frame = Time.frameCount;
    }

    private static bool IsClaimedByEmpireCore(KingdomTitle title)
    {
        EmpireCore capitalCore = title?.title_capital?.GetEmpireCore();
        if (capitalCore != null) return EmpireCoreManager.ContainsTitle(capitalCore, title);
        foreach (City city in title.getCities())
        {
            EmpireCore core = city?.GetEmpireCore();
            if (core != null && EmpireCoreManager.ContainsTitle(core, title)) return true;
        }
        return false;
    }

    private static RuntimeLabel GetOrCreateLabel(string id)
    {
        if (_labels.TryGetValue(id, out RuntimeLabel label)) return label;
        label = new RuntimeLabel(id);
        _labels.Add(id, label);
        return label;
    }

    private static bool EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return false;
        if (_root != null && _root.parent == canvas.transform) return true;
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);

        _labels.Clear();
        _text_template = null;
        GameObject rootObject = new GameObject("EmpireCraftTerritoryLabels", typeof(RectTransform), typeof(Canvas), typeof(TerritoryLabelRendererHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        // Disable pixel snapping for this overlay only; vanilla nameplates retain their settings.
        Canvas overlayCanvas = rootObject.GetComponent<Canvas>();
        overlayCanvas.overridePixelPerfect = true;
        overlayCanvas.pixelPerfect = false;
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
        _host = rootObject.GetComponent<TerritoryLabelRendererHost>();
        return true;
    }

    private static Text GetTextTemplate()
    {
        if (_text_template != null) return _text_template;
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        NameplateManager manager = canvas == null ? null : canvas.GetComponent<NameplateManager>();
        _text_template = manager?.prefab?._text_name;
        return _text_template;
    }

    private sealed class RuntimeLabel
    {
        private const int MaxCandidateCenters = 48;
        private const int SearchIterations = 7;
        private const float ZoneSize = 8f;
        private const float SampleSpacing = 3.25f;

        private readonly string _id;
        private readonly List<Vector3> _points = new List<Vector3>(64);
        private readonly List<TileZone> _zones = new List<TileZone>(64);
        private readonly List<TileZone> _walk_component = new List<TileZone>(64);
        private readonly List<TileZone> _largest_component = new List<TileZone>(64);
        private readonly HashSet<int> _zone_ids = new HashSet<int>();
        private readonly HashSet<int> _component_zone_ids = new HashSet<int>();
        private readonly HashSet<int> _visited_ids = new HashSet<int>();
        private readonly Queue<TileZone> _zone_queue = new Queue<TileZone>();
        private readonly Queue<Vector2Int> _gap_queue = new Queue<Vector2Int>();
        private readonly HashSet<int> _gap_tiles = new HashSet<int>();
        private readonly Dictionary<int, bool> _small_gap_cache = new Dictionary<int, bool>();
        private TerritoryPlacement _placement;
        private Text _text;
        private TerritoryLabelGoldGradient _gold_gradient;
        private Outline _outline;
        private int _source_signature;
        private string _placement_text;
        private TerritoryLabelOrientation _placement_orientation;
        private float _placement_padding;
        private bool _placement_bridges_gaps;
        private float _next_geometry_refresh;
        private bool _has_geometry_result;
        private string _render_text;
        private TerritoryLabelStyle _render_style;
        private bool _render_fully_opaque;
        private bool _render_requested;
        private string _metrics_text;
        private FontStyle _metrics_style;
        private Font _metrics_font;
        private Font _fallback_font;
        private float _reference_width;
        private float _reference_height;
        public int last_seen_frame;

        public float RenderPriority
        {
            get
            {
                float area = _placement.valid ? _placement.half_width * _placement.half_height : 0f;
                return (_render_style?.wrap_english_name == true ? 1000000f : 0f) + area;
            }
        }

        public RuntimeLabel(string id) => _id = id;

        public void UpdateFromCities(string text, IEnumerable<City> cities, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectCityZones(cities, _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            QueueRender(text, style, fullyOpaque);
        }

        public void UpdateFromEmpireCore(string text, EmpireCore core, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectCityZones(EmpireCoreManager.EnumerateCities(core), _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            QueueRender(text, style, fullyOpaque);
        }

        public void UpdateFromZones(string text, IEnumerable<TileZone> zones, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectZones(zones, _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            QueueRender(text, style, fullyOpaque);
        }

        public void UpdateFromPoints(string text, IEnumerable<Vector3> points, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                _points.Clear();
                int signature = 17;
                unchecked
                {
                    foreach (Vector3 point in points)
                    {
                        _points.Add(point);
                        signature = signature * 31 + Mathf.RoundToInt(point.x * 4f);
                        signature = signature * 31 + Mathf.RoundToInt(point.y * 4f);
                    }
                }
                if (!_has_geometry_result || inputsChanged || signature != _source_signature)
                {
                    _placement = BuildPointPlacement(_points, text, style);
                    _source_signature = signature;
                    RememberPlacementInputs(text, style);
                    _has_geometry_result = true;
                }
                _next_geometry_refresh = Time.unscaledTime + GeometryRefreshInterval;
            }
            QueueRender(text, style, fullyOpaque);
        }

        public void Hide()
        {
            _render_requested = false;
            if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
        }

        private void QueueRender(string text, TerritoryLabelStyle style, bool fullyOpaque)
        {
            _render_text = text;
            _render_style = style;
            _render_fully_opaque = fullyOpaque;
            _render_requested = true;
        }

        private static string FormatDisplayText(string text, TerritoryLabelStyle style)
        {
            text ??= string.Empty;
            return style?.wrap_english_name == true ? OverallHelperFunc.WrapEnglishDisplayName(text) : text;
        }

        public void RenderSubmitted()
        {
            if (last_seen_frame != Time.frameCount) { Hide(); return; }
            if (_render_requested) Render(_render_text, _render_style, _render_fully_opaque);
        }

        private bool PlacementInputsChanged(string text, TerritoryLabelStyle style)
        {
            return !string.Equals(_placement_text, text, StringComparison.Ordinal) ||
                   _placement_orientation != style.orientation ||
                   !Mathf.Approximately(_placement_padding, style.territory_padding) ||
                   _placement_bridges_gaps != style.bridge_internal_gaps;
        }

        private void RefreshZonePlacement(string text, TerritoryLabelStyle style, bool inputsChanged)
        {
            int signature = CalculateZoneSignature(_zones);
            if (!_has_geometry_result || inputsChanged || signature != _source_signature)
            {
                // Never join exclaves. Only the dominant connected territory is allowed to host its label.
                FindLargestConnectedComponent(_zones, _zone_ids, _walk_component, _largest_component,
                    _visited_ids, _zone_queue);
                _component_zone_ids.Clear();
                for (int i = 0; i < _largest_component.Count; i++)
                    _component_zone_ids.Add(_largest_component[i].id);
                _small_gap_cache.Clear();
                _placement = BuildZonePlacement(_largest_component, _component_zone_ids, text, style,
                    style.bridge_internal_gaps);
                _source_signature = signature;
                RememberPlacementInputs(text, style);
                _has_geometry_result = true;
            }
            _next_geometry_refresh = Time.unscaledTime + GeometryRefreshInterval;
        }

        private void RememberPlacementInputs(string text, TerritoryLabelStyle style)
        {
            _placement_text = text;
            _placement_orientation = style.orientation;
            _placement_padding = style.territory_padding;
            _placement_bridges_gaps = style.bridge_internal_gaps;
        }

        private void Render(string value, TerritoryLabelStyle style, bool fullyOpaque)
        {
            if (!_placement.valid || string.IsNullOrWhiteSpace(value) || World.world?.camera == null)
            {
                Hide();
                return;
            }

            Camera camera = World.world.camera;
            Vector3 centerScreen = camera.WorldToScreenPoint(_placement.center);
            Vector3 alongScreen = camera.WorldToScreenPoint(_placement.center + _placement.axis * _placement.half_width);
            Vector3 acrossScreen = camera.WorldToScreenPoint(_placement.center + _placement.normal * _placement.half_height);
            if (centerScreen.z < 0f)
            {
                Hide();
                return;
            }

            Vector2 alongDelta = (Vector2)(alongScreen - centerScreen);
            Vector2 acrossDelta = (Vector2)(acrossScreen - centerScreen);
            float canvasScale = GetCanvasScale();
            float availableWidth = alongDelta.magnitude * 2f / canvasScale;
            float availableHeight = acrossDelta.magnitude * 2f / canvasScale;
            float screenMargin = Mathf.Max(availableWidth, availableHeight) * canvasScale * 0.55f;
            if (centerScreen.x < -screenMargin || centerScreen.x > Screen.width + screenMargin ||
                centerScreen.y < -screenMargin || centerScreen.y > Screen.height + screenMargin)
            {
                Hide();
                return;
            }

            EnsureText();
            if (_text == null) return;

            string trimmedValue = value.Trim();
            Font desiredFont = ResolveTerritoryFont(_fallback_font ?? _text.font, trimmedValue);
            if (_text.font != desiredFont)
            {
                _text.font = desiredFont;
                Text template = GetTextTemplate();
                if (desiredFont == _fallback_font && template?.material != null)
                    _text.material = template.material;
                else if (desiredFont?.material != null)
                    _text.material = desiredFont.material;

                _metrics_text = null;
                _metrics_font = null;
            }

            if (_gold_gradient == null)
                _gold_gradient = _text.GetComponent<TerritoryLabelGoldGradient>() ??
                                 _text.gameObject.AddComponent<TerritoryLabelGoldGradient>();
            RectTransform rect = _text.rectTransform;
            bool containsLatin = ContainsLatin(trimmedValue);
            FontStyle effectiveFontStyle = containsLatin ? FontStyle.Bold : style.font_style;
            if (_metrics_text != trimmedValue || _metrics_style != effectiveFontStyle || _metrics_font != _text.font)
            {
                _text.text = trimmedValue;
                _text.fontSize = TerritoryLabelProjection.ReferenceFontSize;
                _text.fontStyle = effectiveFontStyle;
                _reference_width = Mathf.Max(1f, _text.preferredWidth);
                _reference_height = Mathf.Max(1f, _text.preferredHeight);
                rect.sizeDelta = new Vector2(_reference_width + 2f, _reference_height + 2f);
                _metrics_text = trimmedValue;
                _metrics_style = effectiveFontStyle;
                _metrics_font = _text.font;
            }
            float outlineDistance = containsLatin
                ? LatinOutlineDistance
                : TerritoryLabelProjection.OutlineDistance;
            float scale = TerritoryLabelProjection.FitScale(availableWidth, availableHeight,
                _reference_width, _reference_height, style.size_multiplier, style.max_font_size,
                outlineDistance);
            float visibility = TerritoryLabelProjection.Visibility(scale, style.min_font_size);
            if (visibility <= 0f) { Hide(); return; }
            float textAlpha = (fullyOpaque ? 1f : style.text_color.a) * visibility;
            if (style.use_gold_gradient)
            {
                _text.color = Color.white;
                _gold_gradient.Configure(style.gold_top, style.gold_bottom, textAlpha);
            }
            else
            {
                Color textColor = style.text_color;
                textColor.a = textAlpha;
                _text.color = textColor;
                _gold_gradient.DisableGradient();
            }
            _outline.enabled = true;
            Color outlineColor = containsLatin
                ? new Color(0f, 0f, 0f, style.outline_color.a)
                : style.outline_color;
            if (fullyOpaque && outlineColor.a > 0f) outlineColor.a = 1f;
            outlineColor.a *= visibility;
            _outline.effectColor = outlineColor;
            _outline.effectDistance = new Vector2(outlineDistance, -outlineDistance);

            float rotation = Mathf.Atan2(alongDelta.y, alongDelta.x) * Mathf.Rad2Deg;
            float arcHeight = Mathf.Abs(style.arc_curvature) * _reference_width * scale * canvasScale;
            float widthPixels = _reference_width * scale * canvasScale + outlineDistance * 2f;
            float heightPixels = _reference_height * scale * canvasScale + outlineDistance * 2f + arcHeight;
            float radians = rotation * Mathf.Deg2Rad;
            float boundWidth = Mathf.Abs(Mathf.Cos(radians)) * widthPixels +
                               Mathf.Abs(Mathf.Sin(radians)) * heightPixels;
            float boundHeight = Mathf.Abs(Mathf.Sin(radians)) * widthPixels +
                                Mathf.Abs(Mathf.Cos(radians)) * heightPixels;
            Rect screenBounds = Rect.MinMaxRect(centerScreen.x - boundWidth * 0.5f,
                centerScreen.y - boundHeight * 0.5f, centerScreen.x + boundWidth * 0.5f,
                centerScreen.y + boundHeight * 0.5f);
            if (OverlapsSubmittedLabel(screenBounds))
            {
                Hide();
                return;
            }
            _occupied_label_bounds.Add(screenBounds);

            rect.position = centerScreen;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            rect.localScale = new Vector3(scale, scale, 1f);
            TerritoryLabelArc arc = _text.GetComponent<TerritoryLabelArc>() ??
                                    _text.gameObject.AddComponent<TerritoryLabelArc>();
            arc.Configure(style.arc_curvature);
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
            _text.enabled = true;
        }

        private static bool OverlapsSubmittedLabel(Rect bounds)
        {
            for (int i = 0; i < _occupied_label_bounds.Count; i++)
                if (_occupied_label_bounds[i].Overlaps(bounds)) return true;
            return false;
        }

        private void EnsureText()
        {
            if (_text != null || _root == null) return;
            GameObject textObject = new GameObject($"TerritoryLabel_{_id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(_root, false);
            _text = textObject.GetComponent<Text>();
            Text template = GetTextTemplate();
            Font fallbackFont = template?.font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _fallback_font = fallbackFont;
            _text.font = fallbackFont;
            _text.fontSize = TerritoryLabelProjection.ReferenceFontSize;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.resizeTextForBestFit = false;
            _text.supportRichText = false;
            _text.raycastTarget = false;
            if (template?.material != null)
                _text.material = template.material;
            else if (fallbackFont?.material != null)
                _text.material = fallbackFont.material;
            RectTransform rect = _text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            _gold_gradient = textObject.AddComponent<TerritoryLabelGoldGradient>();
            _outline = textObject.AddComponent<Outline>();
            textObject.AddComponent<TerritoryLabelArc>();
        }

        private TerritoryPlacement BuildZonePlacement(List<TileZone> component, HashSet<int> zoneIds,
            string text, TerritoryLabelStyle style, bool allowSmallEmptyGaps)
        {
            if (component == null || component.Count == 0) return default;
            float textAspect = EstimateTextAspect(text);
            Vector3 centroid = CalculateZoneCentroid(component);
            float principalAngle = CalculatePrincipalAngle(component, centroid);
            List<float> angles = BuildCandidateAngles(principalAngle, style.orientation);
            List<Vector3> centers = BuildCandidateCenters(component, centroid);
            float bestHalfHeight = 0f;
            Vector3 bestCenter = default;
            Vector3 bestAxis = Vector3.right;

            for (int angleIndex = 0; angleIndex < angles.Count; angleIndex++)
            {
                float radians = angles[angleIndex] * Mathf.Deg2Rad;
                Vector3 axis = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians));
                Vector3 normal = new Vector3(-axis.y, axis.x);
                GetProjectedBounds(component, axis, normal, out float minAlong, out float maxAlong,
                    out float minAcross, out float maxAcross);
                for (int centerIndex = 0; centerIndex < centers.Count; centerIndex++)
                {
                    Vector3 center = centers[centerIndex];
                    float centerAlong = Vector3.Dot(center, axis);
                    float centerAcross = Vector3.Dot(center, normal);
                    float high = Mathf.Min(
                        Mathf.Min(centerAlong - minAlong, maxAlong - centerAlong) / textAspect,
                        Mathf.Min(centerAcross - minAcross, maxAcross - centerAcross));
                    if (high <= bestHalfHeight) continue;
                    float low = 0f;
                    for (int iteration = 0; iteration < SearchIterations; iteration++)
                    {
                        float candidate = (low + high) * 0.5f;
                        if (FitsInsideSparse(center, axis, normal, candidate * textAspect, candidate, zoneIds,
                                allowSmallEmptyGaps)) low = candidate;
                        else high = candidate;
                    }
                    if (low <= bestHalfHeight) continue;
                    bestHalfHeight = low;
                    bestCenter = center;
                    bestAxis = axis;
                }
            }

            if (bestHalfHeight <= 0.2f) return default;
            float halfHeight = bestHalfHeight * Mathf.Clamp(style.territory_padding, 0.5f, 0.98f);
            float halfWidth = halfHeight * textAspect;
            Vector3 bestNormal = new Vector3(-bestAxis.y, bestAxis.x);
            for (int attempt = 0; attempt < 12 &&
                 !FitsInsideDense(bestCenter, bestAxis, bestNormal, halfWidth, halfHeight, zoneIds,
                     allowSmallEmptyGaps); attempt++)
            {
                halfWidth *= 0.9f;
                halfHeight *= 0.9f;
            }
            if (!FitsInsideDense(bestCenter, bestAxis, bestNormal, halfWidth, halfHeight, zoneIds,
                    allowSmallEmptyGaps)) return default;
            return TerritoryPlacement.Create(bestCenter, bestAxis, halfWidth, halfHeight);
        }

        private static TerritoryPlacement BuildPointPlacement(List<Vector3> points, string text, TerritoryLabelStyle style)
        {
            if (points == null || points.Count == 0) return default;
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < points.Count; i++) centroid += points[i];
            centroid /= points.Count;
            float angle = style.orientation == TerritoryLabelOrientation.Horizontal ? 0f :
                style.orientation == TerritoryLabelOrientation.Vertical ? 90f : CalculatePrincipalAngle(points, centroid);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector3 normal = new Vector3(-axis.y, axis.x);
            GetProjectedBounds(points, axis, normal, out float minAlong, out float maxAlong,
                out float minAcross, out float maxAcross);
            float aspect = EstimateTextAspect(text);
            float halfHeight = Mathf.Min((maxAlong - minAlong) / (2f * aspect),
                (maxAcross - minAcross) * 0.5f) * Mathf.Clamp(style.territory_padding, 0.5f, 0.94f);
            return halfHeight <= 0.2f ? default : TerritoryPlacement.Create(centroid, axis, halfHeight * aspect, halfHeight);
        }

        private bool FitsInsideSparse(Vector3 center, Vector3 axis, Vector3 normal,
            float halfWidth, float halfHeight, HashSet<int> zoneIds, bool allowSmallEmptyGaps)
        {
            if (halfWidth <= 0f || halfHeight <= 0f) return false;
            int steps = Mathf.Max(4, Mathf.CeilToInt(halfWidth * 2f / SampleSpacing));
            for (int stripe = 0; stripe < 5; stripe++)
            {
                float across = Mathf.Lerp(-halfHeight, halfHeight, stripe * 0.25f);
                for (int step = 0; step <= steps; step++)
                {
                    float along = Mathf.Lerp(-halfWidth, halfWidth, step / (float)steps);
                    if (!ContainsTerritoryPoint(center + axis * along + normal * across, zoneIds,
                            allowSmallEmptyGaps)) return false;
                }
            }
            return true;
        }

        private bool FitsInsideDense(Vector3 center, Vector3 axis, Vector3 normal,
            float halfWidth, float halfHeight, HashSet<int> zoneIds, bool allowSmallEmptyGaps)
        {
            int xSteps = Mathf.Max(2, Mathf.CeilToInt(halfWidth * 2f / SampleSpacing));
            int ySteps = Mathf.Max(2, Mathf.CeilToInt(halfHeight * 2f / SampleSpacing));
            for (int y = 0; y <= ySteps; y++)
            {
                float across = Mathf.Lerp(-halfHeight, halfHeight, y / (float)ySteps);
                for (int x = 0; x <= xSteps; x++)
                {
                    float along = Mathf.Lerp(-halfWidth, halfWidth, x / (float)xSteps);
                    if (!ContainsTerritoryPoint(center + axis * along + normal * across, zoneIds,
                            allowSmallEmptyGaps)) return false;
                }
            }
            return true;
        }

        private bool ContainsTerritoryPoint(Vector3 point, HashSet<int> zoneIds, bool allowSmallEmptyGaps)
        {
            if (World.world == null) return false;
            int x = Mathf.FloorToInt(point.x);
            int y = Mathf.FloorToInt(point.y);
            WorldTile tile = World.world.GetTile(x, y);
            if (tile?.zone != null && zoneIds.Contains(tile.zone.id)) return true;
            return allowSmallEmptyGaps && IsSmallNeutralGap(x, y, zoneIds);
        }

        // A gap is usable only when it is a tiny, closed neutral pocket. City-owned enclaves always reject it.
        private bool IsSmallNeutralGap(int startX, int startY, HashSet<int> zoneIds)
        {
            const int maxGapTiles = 20;
            int startKey = GetTileKey(startX, startY);
            if (_small_gap_cache.TryGetValue(startKey, out bool cached)) return cached;

            _gap_queue.Clear();
            _gap_tiles.Clear();
            _gap_queue.Enqueue(new Vector2Int(startX, startY));
            _gap_tiles.Add(startKey);
            int friendlyBorder = 0;
            bool valid = true;

            while (_gap_queue.Count > 0 && valid)
            {
                Vector2Int point = _gap_queue.Dequeue();
                WorldTile tile = World.world.GetTile(point.x, point.y);
                if (tile == null || tile.zone_city != null)
                {
                    valid = false;
                    break;
                }

                for (int direction = 0; direction < 4; direction++)
                {
                    int x = point.x + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                    int y = point.y + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                    WorldTile neighbour = World.world.GetTile(x, y);
                    if (neighbour == null)
                    {
                        valid = false;
                        break;
                    }
                    if (neighbour.zone != null && zoneIds.Contains(neighbour.zone.id))
                    {
                        friendlyBorder++;
                        continue;
                    }
                    // Even a very small country inside another country is never treated as empty space.
                    if (neighbour.zone_city != null)
                    {
                        valid = false;
                        break;
                    }
                    int key = GetTileKey(x, y);
                    if (_gap_tiles.Add(key))
                    {
                        if (_gap_tiles.Count > maxGapTiles)
                        {
                            valid = false;
                            break;
                        }
                        _gap_queue.Enqueue(new Vector2Int(x, y));
                    }
                }
            }

            valid &= friendlyBorder >= 4;
            foreach (int key in _gap_tiles) _small_gap_cache[key] = valid;
            return valid;
        }

        private static int GetTileKey(int x, int y)
        {
            unchecked { return x * 486187739 + y; }
        }

        private static List<float> BuildCandidateAngles(float principalAngle, TerritoryLabelOrientation orientation)
        {
            List<float> result = new List<float>(9);
            if (orientation == TerritoryLabelOrientation.Horizontal) result.Add(0f);
            else if (orientation == TerritoryLabelOrientation.Vertical) result.Add(90f);
            else
            {
                for (int offset = -30; offset <= 30; offset += 10) AddUniqueAngle(result, principalAngle + offset);
                AddUniqueAngle(result, 0f);
                AddUniqueAngle(result, 90f);
            }
            return result;
        }

        private static void AddUniqueAngle(List<float> angles, float angle)
        {
            angle = NormalizeReadableAngle(angle);
            for (int i = 0; i < angles.Count; i++)
                if (Mathf.Abs(Mathf.DeltaAngle(angles[i], angle)) < 1f) return;
            angles.Add(angle);
        }

        private static float NormalizeReadableAngle(float angle)
        {
            while (angle > 90f) angle -= 180f;
            while (angle < -90f) angle += 180f;
            return angle;
        }

        private static List<Vector3> BuildCandidateCenters(List<TileZone> component, Vector3 centroid)
        {
            List<Vector3> result = new List<Vector3>(Mathf.Min(component.Count, MaxCandidateCenters) + 1);
            Vector3 nearest = ZoneCenter(component[0]);
            float nearestDistance = (nearest - centroid).sqrMagnitude;
            for (int i = 1; i < component.Count; i++)
            {
                Vector3 point = ZoneCenter(component[i]);
                float distance = (point - centroid).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = point;
            }
            result.Add(nearest);
            int count = Mathf.Min(component.Count, MaxCandidateCenters);
            for (int i = 0; i < count; i++)
            {
                int index = count == 1 ? 0 : Mathf.RoundToInt(i * (component.Count - 1f) / (count - 1f));
                Vector3 point = ZoneCenter(component[index]);
                if ((point - nearest).sqrMagnitude > 0.1f) result.Add(point);
            }
            return result;
        }

        private static float CalculatePrincipalAngle(List<TileZone> zones, Vector3 centroid)
        {
            float xx = 0f, xy = 0f, yy = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                Vector3 delta = ZoneCenter(zones[i]) - centroid;
                xx += delta.x * delta.x;
                xy += delta.x * delta.y;
                yy += delta.y * delta.y;
            }
            return NormalizeReadableAngle(0.5f * Mathf.Atan2(2f * xy, xx - yy) * Mathf.Rad2Deg);
        }

        private static float CalculatePrincipalAngle(List<Vector3> points, Vector3 centroid)
        {
            float xx = 0f, xy = 0f, yy = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - centroid;
                xx += delta.x * delta.x;
                xy += delta.x * delta.y;
                yy += delta.y * delta.y;
            }
            return NormalizeReadableAngle(0.5f * Mathf.Atan2(2f * xy, xx - yy) * Mathf.Rad2Deg);
        }

        private static Vector3 CalculateZoneCentroid(List<TileZone> zones)
        {
            Vector3 result = Vector3.zero;
            for (int i = 0; i < zones.Count; i++) result += ZoneCenter(zones[i]);
            return result / zones.Count;
        }

        private static void GetProjectedBounds(List<TileZone> zones, Vector3 axis, Vector3 normal,
            out float minAlong, out float maxAlong, out float minAcross, out float maxAcross)
        {
            minAlong = minAcross = float.MaxValue;
            maxAlong = maxAcross = float.MinValue;
            float alongRadius = ZoneSize * 0.5f * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y));
            float acrossRadius = ZoneSize * 0.5f * (Mathf.Abs(normal.x) + Mathf.Abs(normal.y));
            for (int i = 0; i < zones.Count; i++)
            {
                Vector3 center = ZoneCenter(zones[i]);
                float along = Vector3.Dot(center, axis);
                float across = Vector3.Dot(center, normal);
                minAlong = Mathf.Min(minAlong, along - alongRadius);
                maxAlong = Mathf.Max(maxAlong, along + alongRadius);
                minAcross = Mathf.Min(minAcross, across - acrossRadius);
                maxAcross = Mathf.Max(maxAcross, across + acrossRadius);
            }
        }

        private static void GetProjectedBounds(List<Vector3> points, Vector3 axis, Vector3 normal,
            out float minAlong, out float maxAlong, out float minAcross, out float maxAcross)
        {
            minAlong = minAcross = float.MaxValue;
            maxAlong = maxAcross = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                float along = Vector3.Dot(points[i], axis);
                float across = Vector3.Dot(points[i], normal);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
                minAcross = Mathf.Min(minAcross, across);
                maxAcross = Mathf.Max(maxAcross, across);
            }
        }

        private static Vector3 ZoneCenter(TileZone zone)
        {
            return new Vector3(zone.x * ZoneSize + ZoneSize * 0.5f, zone.y * ZoneSize + ZoneSize * 0.5f);
        }

        private static float EstimateTextAspect(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 1.5f;
            float width = 0f;
            float maxWidth = 0f;
            int lineCount = 1;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == '\n')
                {
                    maxWidth = Mathf.Max(maxWidth, width);
                    width = 0f;
                    lineCount++;
                    continue;
                }
                if (char.IsWhiteSpace(character)) width += 0.35f;
                else if (character >= '\u3400' && character <= '\u9fff') width += 1f;
                else if (char.IsUpper(character)) width += 0.72f;
                else width += 0.58f;
            }
            maxWidth = Mathf.Max(maxWidth, width);
            return Mathf.Clamp(maxWidth * 1.08f / Mathf.Max(1f, lineCount * 0.92f), 1.4f, 24f);
        }
    }

    private struct TerritoryPlacement
    {
        public bool valid;
        public Vector3 center;
        public Vector3 axis;
        public Vector3 normal;
        public float half_width;
        public float half_height;

        public static TerritoryPlacement Create(Vector3 center, Vector3 axis, float halfWidth, float halfHeight)
        {
            axis.Normalize();
            return new TerritoryPlacement
            {
                valid = halfWidth > 0f && halfHeight > 0f,
                center = center,
                axis = axis,
                normal = new Vector3(-axis.y, axis.x),
                half_width = halfWidth,
                half_height = halfHeight
            };
        }
    }

    private static void CollectCityZones(IEnumerable<City> cities, List<TileZone> zones, HashSet<int> zoneIds)
    {
        zones.Clear();
        zoneIds.Clear();
        foreach (City city in cities)
        {
            if (city == null || city.isRekt() || city.zones == null) continue;
            for (int i = 0; i < city.zones.Count; i++)
            {
                TileZone zone = city.zones[i];
                if (zone == null || !zoneIds.Add(zone.id)) continue;
                zones.Add(zone);
            }
        }
    }

    private static void CollectZones(IEnumerable<TileZone> source, List<TileZone> zones, HashSet<int> zoneIds)
    {
        zones.Clear();
        zoneIds.Clear();
        foreach (TileZone zone in source)
        {
            if (zone == null || !zoneIds.Add(zone.id)) continue;
            zones.Add(zone);
        }
    }

    private static int CalculateZoneSignature(List<TileZone> zones)
    {
        unchecked
        {
            int sum = 0, xor = 0;
            for (int i = 0; i < zones.Count; i++)
            {
                int value = zones[i].id * 397;
                sum += value;
                xor ^= value + (value << 11);
            }
            return (zones.Count * 486187739) ^ sum ^ xor;
        }
    }

    private static void FindLargestConnectedComponent(List<TileZone> zones, HashSet<int> zoneIds,
        List<TileZone> walk, List<TileZone> largest, HashSet<int> visited, Queue<TileZone> queue)
    {
        largest.Clear();
        visited.Clear();
        queue.Clear();
        for (int i = 0; i < zones.Count; i++)
        {
            TileZone start = zones[i];
            if (!visited.Add(start.id)) continue;
            walk.Clear();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                TileZone zone = queue.Dequeue();
                walk.Add(zone);
                TileZone[] neighbours = zone.neighbours;
                if (neighbours == null) continue;
                for (int n = 0; n < neighbours.Length; n++)
                {
                    TileZone neighbour = neighbours[n];
                    if (neighbour == null || !zoneIds.Contains(neighbour.id) || !visited.Add(neighbour.id)) continue;
                    queue.Enqueue(neighbour);
                }
            }
            if (walk.Count <= largest.Count) continue;
            largest.Clear();
            largest.AddRange(walk);
        }
    }

    private static float GetCanvasScale()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        return canvas == null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
    }
}

[DefaultExecutionOrder(10000)]
public sealed class TerritoryLabelRendererHost : MonoBehaviour
{
    private void LateUpdate() => TerritoryLabelRenderer.HostLateUpdate(this);
    private void OnDestroy() => TerritoryLabelRenderer.HostDestroyed(this);
}

public sealed class TerritoryLabelGoldGradient : BaseMeshEffect
{
    private Color _top = new Color32(255, 244, 194, 255);
    private Color _bottom = new Color32(184, 156, 68, 255);
    private float _alpha = 1f;

    public void Configure(Color top, Color bottom, float alpha)
    {
        bool changed = !enabled || _top != top || _bottom != bottom || !Mathf.Approximately(_alpha, alpha);
        _top = top;
        _bottom = bottom;
        _alpha = Mathf.Clamp01(alpha);
        enabled = true;
        if (changed && graphic != null) graphic.SetVerticesDirty();
    }

    public void DisableGradient()
    {
        if (!enabled) return;
        enabled = false;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0) return;

        UIVertex vertex = default;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            minY = Mathf.Min(minY, vertex.position.y);
            maxY = Mathf.Max(maxY, vertex.position.y);
        }

        float height = Mathf.Max(0.001f, maxY - minY);
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            Color color = Color.Lerp(_bottom, _top, (vertex.position.y - minY) / height);
            color.a *= _alpha;
            vertex.color = color;
            vertexHelper.SetUIVertex(vertex, i);
        }
    }
}

// Bends the baseline very slightly along the territory's main axis. It is disabled for zero curvature.
public sealed class TerritoryLabelArc : BaseMeshEffect
{
    private float _curvature;

    public void Configure(float curvature)
    {
        curvature = Mathf.Clamp(curvature, -0.12f, 0.12f);
        bool changed = !Mathf.Approximately(_curvature, curvature);
        _curvature = curvature;
        enabled = !Mathf.Approximately(_curvature, 0f);
        if (changed && graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0) return;
        UIVertex vertex = default;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            minX = Mathf.Min(minX, vertex.position.x);
            maxX = Mathf.Max(maxX, vertex.position.x);
        }

        float halfSpan = (maxX - minX) * 0.5f;
        if (halfSpan <= 0.01f) return;
        float centerX = (minX + maxX) * 0.5f;
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            float normalized = (vertex.position.x - centerX) / halfSpan;
            vertex.position.y += _curvature * halfSpan * (1f - normalized * normalized);
            vertexHelper.SetUIVertex(vertex, i);
        }
    }
}
