using System;
using System.Collections.Generic;
using EmpireCraft;
using EmpireCraft.Scripts.GameClassExtensions;
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
    public Color text_color = Color.white;
    public Color outline_color = new Color(0.08f, 0.07f, 0.05f, 0.92f);
    public TerritoryLabelOrientation orientation = TerritoryLabelOrientation.Auto;
    public float size_multiplier = 0.9f;
    public float territory_padding = 0.82f;
    public int min_font_size = 6;
    public int max_font_size = 512;
    public FontStyle font_style = FontStyle.Normal;
}

// Renders one rotated line inside the territory, independently of WorldBox nameplates.
public static class TerritoryLabelRenderer
{
    private const float GeometryRefreshInterval = 0.75f;
    private static readonly Dictionary<string, RuntimeLabel> _labels = new Dictionary<string, RuntimeLabel>();
    private static TerritoryLabelRendererHost _host;
    private static RectTransform _root;
    private static Text _text_template;
    private static int _submission_frame = -1;

    public static readonly TerritoryLabelStyle EmpireStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 0.78f, 0.22f),
        size_multiplier = 0.94f,
        min_font_size = 7,
        font_style = FontStyle.Normal
    };

    public static readonly TerritoryLabelStyle FadedEmpireStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 0.78f, 0.22f, 0.28f),
        outline_color = new Color(0.08f, 0.07f, 0.05f, 0.24f),
        size_multiplier = 0.94f,
        min_font_size = 7,
        font_style = FontStyle.Normal
    };

    public static readonly TerritoryLabelStyle KingdomStyle = new TerritoryLabelStyle
    {
        text_color = new Color(0.97f, 0.97f, 0.94f),
        size_multiplier = 0.9f,
        min_font_size = 6,
        font_style = FontStyle.Italic
    };

    public static void RenderLawLayer(int zoneOptionState)
    {
        BeginFrame();
        if (zoneOptionState == 0)
        {
            foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
            {
                if (core == null || core.id <= 0) continue;
                List<City> cities = EmpireCoreManager.GetCities(core);
                if (cities.Count == 0) continue;
                SubmitCities($"law-empire:{core.id}", EmpireCoreManager.GetDisplayName(core), cities, EmpireStyle);
            }
        }

        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            if (title == null || title.isRekt() || title.data == null) continue;
            if (zoneOptionState == 0 && IsClaimedByEmpireCore(title)) continue;
            SubmitCities($"law-kingdom:{title.id}", title.data.name, title.getCities(), KingdomStyle);
        }
        EndFrame();
    }

    public static void BeginFrame()
    {
        if (!EnsureHost()) return;
        _submission_frame = Time.frameCount;
    }

    public static void SubmitCities(string id, string text, IEnumerable<City> cities, TerritoryLabelStyle style)
    {
        if (string.IsNullOrWhiteSpace(id) || cities == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromCities(text, cities, style ?? KingdomStyle);
    }

    // Future country layers can use zones to get the same exact containment as the law layer.
    public static void SubmitZones(string id, string text, IEnumerable<TileZone> zones, TerritoryLabelStyle style)
    {
        if (string.IsNullOrWhiteSpace(id) || zones == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromZones(text, zones, style ?? KingdomStyle);
    }

    // Point input is for non-zone overlays and uses an oriented point-cloud fit.
    public static void SubmitPoints(string id, string text, IEnumerable<Vector3> points, TerritoryLabelStyle style)
    {
        if (string.IsNullOrWhiteSpace(id) || points == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromPoints(text, points, style ?? KingdomStyle);
    }

    public static void EndFrame()
    {
        if (_submission_frame != Time.frameCount) return;
        foreach (RuntimeLabel label in _labels.Values)
            if (label.last_seen_frame != _submission_frame) label.Hide();
    }

    public static void HideAll()
    {
        foreach (RuntimeLabel label in _labels.Values) label.Hide();
    }

    internal static void HostLateUpdate(TerritoryLabelRendererHost host)
    {
        if (host == _host && _submission_frame != Time.frameCount) HideAll();
    }

    internal static void HostDestroyed(TerritoryLabelRendererHost host)
    {
        if (host != _host) return;
        _host = null;
        _root = null;
        _text_template = null;
        _labels.Clear();
    }

    private static void MarkSubmissionFrame()
    {
        if (_submission_frame != Time.frameCount) _submission_frame = Time.frameCount;
    }

    private static bool IsClaimedByEmpireCore(KingdomTitle title)
    {
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
        GameObject rootObject = new GameObject("EmpireCraftTerritoryLabels", typeof(RectTransform), typeof(TerritoryLabelRendererHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
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
        private readonly HashSet<int> _visited_ids = new HashSet<int>();
        private readonly Queue<TileZone> _zone_queue = new Queue<TileZone>();
        private TerritoryPlacement _placement;
        private Text _text;
        private Outline _outline;
        private int _source_signature;
        private string _placement_text;
        private TerritoryLabelOrientation _placement_orientation;
        private float _placement_padding;
        private float _next_geometry_refresh;
        private bool _has_geometry_result;
        public int last_seen_frame;

        public RuntimeLabel(string id) => _id = id;

        public void UpdateFromCities(string text, IEnumerable<City> cities, TerritoryLabelStyle style)
        {
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectCityZones(cities, _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            Render(text, style);
        }

        public void UpdateFromZones(string text, IEnumerable<TileZone> zones, TerritoryLabelStyle style)
        {
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectZones(zones, _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            Render(text, style);
        }

        public void UpdateFromPoints(string text, IEnumerable<Vector3> points, TerritoryLabelStyle style)
        {
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
            Render(text, style);
        }

        public void Hide()
        {
            if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
        }

        private bool PlacementInputsChanged(string text, TerritoryLabelStyle style)
        {
            return !string.Equals(_placement_text, text, StringComparison.Ordinal) ||
                   _placement_orientation != style.orientation ||
                   !Mathf.Approximately(_placement_padding, style.territory_padding);
        }

        private void RefreshZonePlacement(string text, TerritoryLabelStyle style, bool inputsChanged)
        {
            int signature = CalculateZoneSignature(_zones);
            if (!_has_geometry_result || inputsChanged || signature != _source_signature)
            {
                FindLargestConnectedComponent(_zones, _zone_ids, _walk_component, _largest_component,
                    _visited_ids, _zone_queue);
                _placement = BuildZonePlacement(_largest_component, _zone_ids, text, style);
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
        }

        private void Render(string value, TerritoryLabelStyle style)
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

            int requestedSize = Mathf.Clamp(
                Mathf.FloorToInt(availableHeight * Mathf.Clamp(style.size_multiplier, 0.2f, 1f)),
                1, Mathf.Max(1, style.max_font_size));
            if (requestedSize < style.min_font_size)
            {
                Hide();
                return;
            }

            EnsureText();
            if (_text == null) return;
            RectTransform rect = _text.rectTransform;
            rect.sizeDelta = new Vector2(availableWidth, availableHeight);
            _text.text = value.Trim();
            _text.fontSize = requestedSize;
            _text.fontStyle = style.font_style;
            _text.color = style.text_color;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.resizeTextForBestFit = false;
            _text.supportRichText = false;
            _text.raycastTarget = false;

            float preferredWidth = Mathf.Max(1f, _text.preferredWidth);
            float preferredHeight = Mathf.Max(1f, _text.preferredHeight);
            float fitScale = Mathf.Min(1f, availableWidth / preferredWidth, availableHeight / preferredHeight);
            int fittedSize = Mathf.FloorToInt(requestedSize * fitScale * 0.98f);
            if (fittedSize < style.min_font_size)
            {
                Hide();
                return;
            }
            _text.fontSize = fittedSize;
            _outline.enabled = true;
            _outline.effectColor = style.outline_color;
            _outline.effectDistance = new Vector2(1f, -1f);

            rect.position = new Vector3(Mathf.Round(centerScreen.x), Mathf.Round(centerScreen.y), centerScreen.z);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(alongDelta.y, alongDelta.x) * Mathf.Rad2Deg);
            rect.localScale = Vector3.one;
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
            _text.enabled = true;
        }

        private void EnsureText()
        {
            if (_text != null || _root == null) return;
            GameObject textObject = new GameObject($"TerritoryLabel_{_id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(_root, false);
            _text = textObject.GetComponent<Text>();
            Text template = GetTextTemplate();
            _text.font = template?.font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (template?.material != null) _text.material = template.material;
            RectTransform rect = _text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            _outline = textObject.AddComponent<Outline>();
        }

        private static TerritoryPlacement BuildZonePlacement(List<TileZone> component, HashSet<int> zoneIds,
            string text, TerritoryLabelStyle style)
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
                        if (FitsInsideSparse(center, axis, normal, candidate * textAspect, candidate, zoneIds)) low = candidate;
                        else high = candidate;
                    }
                    if (low <= bestHalfHeight) continue;
                    bestHalfHeight = low;
                    bestCenter = center;
                    bestAxis = axis;
                }
            }

            if (bestHalfHeight <= 0.2f) return default;
            float halfHeight = bestHalfHeight * Mathf.Clamp(style.territory_padding, 0.5f, 0.94f);
            float halfWidth = halfHeight * textAspect;
            Vector3 bestNormal = new Vector3(-bestAxis.y, bestAxis.x);
            for (int attempt = 0; attempt < 12 &&
                 !FitsInsideDense(bestCenter, bestAxis, bestNormal, halfWidth, halfHeight, zoneIds); attempt++)
            {
                halfWidth *= 0.9f;
                halfHeight *= 0.9f;
            }
            if (!FitsInsideDense(bestCenter, bestAxis, bestNormal, halfWidth, halfHeight, zoneIds)) return default;
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

        private static bool FitsInsideSparse(Vector3 center, Vector3 axis, Vector3 normal,
            float halfWidth, float halfHeight, HashSet<int> zoneIds)
        {
            if (halfWidth <= 0f || halfHeight <= 0f) return false;
            int steps = Mathf.Max(4, Mathf.CeilToInt(halfWidth * 2f / SampleSpacing));
            for (int stripe = 0; stripe < 5; stripe++)
            {
                float across = Mathf.Lerp(-halfHeight, halfHeight, stripe * 0.25f);
                for (int step = 0; step <= steps; step++)
                {
                    float along = Mathf.Lerp(-halfWidth, halfWidth, step / (float)steps);
                    if (!ContainsTerritoryPoint(center + axis * along + normal * across, zoneIds)) return false;
                }
            }
            return true;
        }

        private static bool FitsInsideDense(Vector3 center, Vector3 axis, Vector3 normal,
            float halfWidth, float halfHeight, HashSet<int> zoneIds)
        {
            int xSteps = Mathf.Max(2, Mathf.CeilToInt(halfWidth * 2f / SampleSpacing));
            int ySteps = Mathf.Max(2, Mathf.CeilToInt(halfHeight * 2f / SampleSpacing));
            for (int y = 0; y <= ySteps; y++)
            {
                float across = Mathf.Lerp(-halfHeight, halfHeight, y / (float)ySteps);
                for (int x = 0; x <= xSteps; x++)
                {
                    float along = Mathf.Lerp(-halfWidth, halfWidth, x / (float)xSteps);
                    if (!ContainsTerritoryPoint(center + axis * along + normal * across, zoneIds)) return false;
                }
            }
            return true;
        }

        private static bool ContainsTerritoryPoint(Vector3 point, HashSet<int> zoneIds)
        {
            if (World.world == null) return false;
            WorldTile tile = World.world.GetTile(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
            return tile?.zone != null && zoneIds.Contains(tile.zone.id);
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
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsWhiteSpace(character)) width += 0.35f;
                else if (character >= '\u3400' && character <= '\u9fff') width += 1f;
                else if (char.IsUpper(character)) width += 0.72f;
                else width += 0.58f;
            }
            return Mathf.Clamp(width * 1.08f, 1.4f, 24f);
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
        CanvasScaler scaler = canvas == null ? null : canvas.GetComponent<CanvasScaler>();
        return scaler == null ? 1f : Mathf.Max(0.01f, scaler.scaleFactor);
    }
}

public sealed class TerritoryLabelRendererHost : MonoBehaviour
{
    private void LateUpdate() => TerritoryLabelRenderer.HostLateUpdate(this);
    private void OnDestroy() => TerritoryLabelRenderer.HostDestroyed(this);
}
