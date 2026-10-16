using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 城市精细层的显示(画法见 PureMapPatch.Detail；用户 2026-10-16："拉近的时候也只渲染离镜头最近的城市""没显示出来的不渲染")：
//   · 镜头拉近到一格至少 MinPixelsPerTile 个屏幕像素才画(再远看不出一格以内的细节，照一格一像素的平面图)；
//   · 只画镜头里看得到的城，按离镜头中心远近排，至多 MaxShown 座；镜头外的不生成、不画；
//   · 每座城一张贴图，平面图或底色变了才重画，每帧至多重画一座(其余先顶着旧图)；
//     离开镜头的贴图只留 MaxCached 张(来回拖动不必重算)，多了先丢最久没看的。
// 盖在国色色块之上、路网与图层长城之下(见 RoadOverlay、MapWallOverlay)。
public static class CityDetailOverlay
{
    private const float MinPixelsPerTile = MapZoomTier.Near;
    private const int MaxShown = 4, MaxCached = 8;
    // 估计一座城连同城外村镇的范围时，城的地块外再放宽几格
    private const int CountrysideMargin = 24;

    private sealed class Entry
    {
        public long City;
        public int Key;
        public GameObject Object;
        public SpriteRenderer Renderer;
        public Texture2D Texture;
        public float Seen;
    }

    private static readonly Dictionary<long, Entry> Entries = new();
    // 各城的大致范围，两秒(真实时间)重算一次
    private static readonly Dictionary<long, (Rect bounds, float at)> BoundsCache = new();
    private const float BoundsSeconds = 2f;
    private static object _world;

    public static void ResetWorldState()
    {
        foreach (Entry entry in Entries.Values) Destroy(entry);
        Entries.Clear();
        BoundsCache.Clear();
        _world = null;
    }

    public static void Update()
    {
        MapBox world = World.world;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        ZoneCalculator zones = world?.zone_calculator;
        SpriteRenderer source = zones?.sprRnd;
        Camera camera = world?.camera;
        if (source?.sprite == null || camera == null || !source.enabled || !GeneralSystems.PureMapMode.Enabled || !PureMapPatch.DetailShown ||
            Layer.CityViewMap.Active || GeneralSystems.ZonePlanSystem.SteadyOverlay || PixelsPerTile(camera) < MinPixelsPerTile)
        {
            HideAll();
            return;
        }
        // 镜头范围(格)
        Rect view = ViewInTiles(camera, source);
        var candidates = new List<(City city, float distance)>();
        foreach (City city in world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) continue;
            if (!Overlaps(Bounds(city), view)) continue;
            WorldTile center = city.getTile();
            if (center == null) continue;
            float dx = center.x - view.center.x, dy = center.y - view.center.y;
            candidates.Add((city, dx * dx + dy * dy));
        }
        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
        var shown = new HashSet<long>();
        bool rebuilt = false;
        foreach ((City city, _) in candidates)
        {
            if (shown.Count >= MaxShown) break;
            if (!PureMapPatch.DetailEligible(city)) continue;
            Color32 tint = PureMapPatch.DetailTint(city);
            int key = PureMapPatch.DetailKey(city, tint);
            Entries.TryGetValue(city.id, out Entry entry);
            if (entry == null || entry.Key != key)
            {
                if (rebuilt)
                {
                    if (entry == null) continue;
                }
                else
                {
                    rebuilt = true;
                    entry = Build(city, tint, entry, source);
                    if (entry == null) continue;
                }
            }
            entry.Seen = Time.unscaledTime;
            shown.Add(city.id);
        }
        foreach (Entry entry in Entries.Values)
        {
            bool on = shown.Contains(entry.City);
            if (entry.Renderer.enabled != on) entry.Renderer.enabled = on;
            if (!on) continue;
            entry.Renderer.sortingLayerID = source.sortingLayerID;
            entry.Renderer.sortingOrder = source.sortingOrder + 2;
        }
        Trim(shown);
    }

    private static float PixelsPerTile(Camera camera) =>
        camera.orthographicSize <= 0f ? 0f : Screen.height / (2f * camera.orthographicSize);

    // 镜头四角换成格坐标(按色块贴图的摆放)
    private static Rect ViewInTiles(Camera camera, SpriteRenderer source)
    {
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit;
        Vector3 a = source.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0f, 0f, 0f)));
        Vector3 b = source.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(1f, 1f, 0f)));
        float x0 = Mathf.Min(a.x, b.x) * ppu + reference.pivot.x, x1 = Mathf.Max(a.x, b.x) * ppu + reference.pivot.x;
        float y0 = Mathf.Min(a.y, b.y) * ppu + reference.pivot.y, y1 = Mathf.Max(a.y, b.y) * ppu + reference.pivot.y;
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // 一座城连同城外村镇大致占的范围(格)
    private static Rect Bounds(City city)
    {
        float now = Time.unscaledTime;
        if (BoundsCache.TryGetValue(city.id, out var cached) && now >= cached.at && now - cached.at < BoundsSeconds) return cached.bounds;
        Rect bounds = ComputeBounds(city);
        if (BoundsCache.Count > 4000) BoundsCache.Clear();
        BoundsCache[city.id] = (bounds, now);
        return bounds;
    }

    private static Rect ComputeBounds(City city)
    {
        int size = Config.CITY_ZONE_SIZE, x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null) continue;
            x0 = Mathf.Min(x0, zone.x * size);
            y0 = Mathf.Min(y0, zone.y * size);
            x1 = Mathf.Max(x1, zone.x * size + size);
            y1 = Mathf.Max(y1, zone.y * size + size);
        }
        return Rect.MinMaxRect(x0 - CountrysideMargin, y0 - CountrysideMargin, x1 + CountrysideMargin, y1 + CountrysideMargin);
    }

    private static bool Overlaps(Rect a, Rect b) => a.xMin <= b.xMax && a.xMax >= b.xMin && a.yMin <= b.yMax && a.yMax >= b.yMin;

    private static Entry Build(City city, Color32 tint, Entry entry, SpriteRenderer source)
    {
        PureMapPatch.DetailImage image;
        using (FrameProfiler.Measure("城市精细层·生成")) image = PureMapPatch.BuildDetail(city, tint);
        if (image == null) return entry;
        int scale = PureMapPatch.DetailScale, width = image.W * scale, height = image.H * scale;
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit;
        if (entry == null)
        {
            entry = new Entry { City = city.id };
            Entries[city.id] = entry;
        }
        if (entry.Texture == null || entry.Texture.width != width || entry.Texture.height != height)
        {
            if (entry.Object != null) Object.Destroy(entry.Object);
            if (entry.Texture != null) Object.Destroy(entry.Texture);
            entry.Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "EmpireCraftCityDetail", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            entry.Object = new GameObject("EmpireCraftCityDetail");
            entry.Renderer = entry.Object.AddComponent<SpriteRenderer>();
            entry.Renderer.sprite = Sprite.Create(entry.Texture, new Rect(0f, 0f, width, height), Vector2.zero, ppu * scale);
        }
        entry.Object.transform.SetParent(source.transform, false);
        entry.Object.transform.localPosition = new Vector3((image.X0 - reference.pivot.x) / ppu, (image.Y0 - reference.pivot.y) / ppu, 0f);
        entry.Texture.SetPixels32(image.Pixels);
        entry.Texture.Apply(false);
        entry.Key = image.Key;
        return entry;
    }

    // 缓存多了：丢最久没看的(正在显示的不丢)
    private static void Trim(HashSet<long> shown)
    {
        while (Entries.Count > MaxCached)
        {
            Entry oldest = null;
            foreach (Entry entry in Entries.Values)
                if (!shown.Contains(entry.City) && (oldest == null || entry.Seen < oldest.Seen)) oldest = entry;
            if (oldest == null) return;
            Destroy(oldest);
            Entries.Remove(oldest.City);
        }
    }

    private static void HideAll()
    {
        foreach (Entry entry in Entries.Values)
            if (entry.Renderer != null && entry.Renderer.enabled) entry.Renderer.enabled = false;
    }

    private static void Destroy(Entry entry)
    {
        if (entry.Object != null) Object.Destroy(entry.Object);
        if (entry.Texture != null) Object.Destroy(entry.Texture);
    }
}
