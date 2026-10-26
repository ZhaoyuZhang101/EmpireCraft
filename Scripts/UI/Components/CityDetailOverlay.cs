using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 城市精细层的显示(画法见 PureMapPatch.Detail；用户 2026-10-16："拉近的时候也只渲染离镜头最近的城市""没显示出来的不渲染")：
//   · 镜头拉近到一格至少 MinPixelsPerTile 个屏幕像素才画；再远时保留底图的一格一像素城市粗图；
//   · 只画镜头里看得到的城，按离镜头中心远近排，至多 MaxShown 座；镜头外的不生成、不画；
//   · 每座城一张贴图，平面图或底色变了才重画，其余先顶着旧图；生成一张要几十毫秒(性能日志"城市精细层·生成"到 98 毫秒)，
//     镜头在动(拖动、缩放，停下不到 CameraSettleSeconds 秒)时不生成，停下后每 BuildEveryFrames 帧至多生成一张；
//   · 每座城另留上一版贴图：悬停换底色(帝国图层悬停某省)来回切换时直接换回，不重画；
//     离开镜头的贴图只留 MaxCached 张(来回拖动不必重算)，连同上一版至多 MaxTextures 张，多了先丢最久没看的。
// 盖在国色色块之上、路网与图层长城之下(见 RoadOverlay、MapWallOverlay)。
public static class CityDetailOverlay
{
    private const float MinPixelsPerTile = MapZoomTier.Near;
    // 镜头里同时出精细图的城(以前四座，城一多，第五座起的村镇就看不见；战线一动城的范围变了，入选的几座换来换去，
    // 看着像村镇反复消失又冒出来——用户 2026-10-17 "为什么这些村镇会在每次战线变化的时候就重复刷新消失和生成新的村镇")
    private const int MaxShown = 12, MaxCached = 16, MaxTextures = 24;
    // 估计一座城连同城外村镇的范围时，城的地块外再放宽几格
    private const int CountrysideMargin = 24;
    private const float CameraSettleSeconds = 0.2f;
    private const int BuildEveryFrames = 3;

    // 一版贴图(按精细图指纹)
    private sealed class Variant
    {
        public int Key;
        public Texture2D Texture;
        public Sprite Sprite;
        public Vector3 Position;
    }

    private sealed class Entry
    {
        public long City;
        public GameObject Object;
        public SpriteRenderer Renderer;
        // 正在显示的一版、上一版
        public Variant Shown, Spare;
        public float Seen;
    }

    // 每帧用的候选城、显示的城(复用，不每帧新建)
    private static readonly List<(City city, float distance)> Candidates = new();
    private static readonly HashSet<long> Shown = new();
    private static readonly global::System.Comparison<(City city, float distance)> ByDistance = (a, b) => a.distance.CompareTo(b.distance);
    private static int _lastBuildFrame = -100;
    private static Vector3 _cameraPosition;
    private static float _cameraSize = -1f, _cameraMovedAt = -10f;

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
        if (source?.sprite == null || camera == null || !source.enabled || !GeneralSystems.PureMapMode.Enabled ||
            Layer.CityViewMap.Active || PixelsPerTile(camera) < MinPixelsPerTile)
        {
            HideAll();
            return;
        }
        bool canBuild = CameraSettled(camera) && Time.frameCount - _lastBuildFrame >= BuildEveryFrames;
        // 镜头范围(格)
        Rect view = ViewInTiles(camera, source);
        List<(City city, float distance)> candidates = Candidates;
        candidates.Clear();
        foreach (City city in world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) continue;
            if (!Overlaps(Bounds(city), view)) continue;
            WorldTile center = city.getTile();
            if (center == null) continue;
            float dx = center.x - view.center.x, dy = center.y - view.center.y;
            candidates.Add((city, dx * dx + dy * dy));
        }
        candidates.Sort(ByDistance);
        HashSet<long> shown = Shown;
        shown.Clear();
        foreach ((City city, _) in candidates)
        {
            if (shown.Count >= MaxShown) break;
            if (!PureMapPatch.DetailEligible(city)) continue;
            Color32 tint = PureMapPatch.DetailTint(city);
            int key = PureMapPatch.DetailKey(city, tint);
            Entries.TryGetValue(city.id, out Entry entry);
            if (entry?.Shown == null || entry.Shown.Key != key)
            {
                // 上一版正是要的样子(悬停换底色又换回)：直接换回
                if (entry?.Spare != null && entry.Spare.Key == key) Swap(entry);
                else if (canBuild)
                {
                    canBuild = false;
                    _lastBuildFrame = Time.frameCount;
                    entry = Build(city, tint, entry, source);
                    if (entry?.Shown == null) continue;
                }
                else if (entry?.Shown == null) continue;
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

    // 镜头停下(位置、缩放都没变)已经 CameraSettleSeconds 秒
    private static bool CameraSettled(Camera camera)
    {
        Vector3 position = camera.transform.position;
        float size = camera.orthographicSize;
        if ((position - _cameraPosition).sqrMagnitude > 1e-4f || Mathf.Abs(size - _cameraSize) > 1e-3f)
        {
            _cameraPosition = position;
            _cameraSize = size;
            _cameraMovedAt = Time.unscaledTime;
        }
        return Time.unscaledTime - _cameraMovedAt >= CameraSettleSeconds;
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
        if (entry.Object == null)
        {
            entry.Object = new GameObject("EmpireCraftCityDetail");
            entry.Renderer = entry.Object.AddComponent<SpriteRenderer>();
        }
        // 新的一版画在上一版那张贴图上(尺寸一样就沿用)，眼下这版留作上一版
        Variant target = entry.Spare ?? new Variant();
        if (target.Texture == null || target.Texture.width != width || target.Texture.height != height)
        {
            Release(target);
            target.Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "EmpireCraftCityDetail", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            target.Sprite = Sprite.Create(target.Texture, new Rect(0f, 0f, width, height), Vector2.zero, ppu * scale);
        }
        target.Position = new Vector3((image.X0 - reference.pivot.x) / ppu, (image.Y0 - reference.pivot.y) / ppu, 0f);
        target.Texture.SetPixels32(image.Pixels);
        target.Texture.Apply(false);
        target.Key = image.Key;
        entry.Spare = entry.Shown;
        entry.Shown = target;
        entry.Object.transform.SetParent(source.transform, false);
        Show(entry);
        return entry;
    }

    // 换回上一版
    private static void Swap(Entry entry)
    {
        (entry.Shown, entry.Spare) = (entry.Spare, entry.Shown);
        Show(entry);
    }

    private static void Show(Entry entry)
    {
        if (entry.Renderer == null || entry.Shown == null) return;
        entry.Renderer.sprite = entry.Shown.Sprite;
        entry.Object.transform.localPosition = entry.Shown.Position;
    }

    // 缓存多了：丢最久没看的(正在显示的不丢)；贴图连同上一版超过 MaxTextures 张，先丢最久没看的上一版
    private static void Trim(HashSet<long> shown)
    {
        while (Entries.Count > MaxCached)
        {
            Entry oldest = null;
            foreach (Entry entry in Entries.Values)
                if (!shown.Contains(entry.City) && (oldest == null || entry.Seen < oldest.Seen)) oldest = entry;
            if (oldest == null) break;
            Destroy(oldest);
            Entries.Remove(oldest.City);
        }
        while (true)
        {
            int textures = 0;
            Entry oldest = null;
            foreach (Entry entry in Entries.Values)
            {
                if (entry.Shown != null) textures++;
                if (entry.Spare == null) continue;
                textures++;
                if (oldest == null || entry.Seen < oldest.Seen) oldest = entry;
            }
            if (textures <= MaxTextures || oldest == null) return;
            Release(oldest.Spare);
            oldest.Spare = null;
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
        Release(entry.Shown);
        Release(entry.Spare);
        entry.Shown = entry.Spare = null;
    }

    private static void Release(Variant variant)
    {
        if (variant == null) return;
        if (variant.Sprite != null) Object.Destroy(variant.Sprite);
        if (variant.Texture != null) Object.Destroy(variant.Texture);
        variant.Sprite = null;
        variant.Texture = null;
    }
}
