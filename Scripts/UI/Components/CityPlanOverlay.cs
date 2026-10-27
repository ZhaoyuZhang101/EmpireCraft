using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 城市平面图层(用户 2026-10-26："城市浮雕可以单独拉出来做""不然每次地形变更都要同步格子，那就会经常出现国家颜色不刷新的问题")：
// 一格一像素的城市平面图(城墙、街巷、建筑、城外的镇村田块，画法见 PureMapPatch.Plan)以前直接画进国家色块，
// 城里盖一栋房、城市格局一变，整城的色块都要重画，和换图层、易主这些真正的换色抢每帧的上色预算。现在单独一层：
//   · 铺在国家色块之下、原版地形之上(地形是第 0 层、色块是第 2 层，见 NightMapLayerPatch)：纯净地图的国色同原版图层一样半透明，
//     盖在城市和地形上，国界照旧压在最上面；城市图层色块全透明，城市直接露出来(画得鲜艳些)；
//   · 按地块重画：平面图变了(按轮重建，见 PureMapPatch.RefreshUrbanSlice)、换了城市格局、城里的地块水陆或山地变了，
//     才重画那座城(那块地)；每帧画到 BuildBudgetMs 为止，镜头里的先画；
//   · 另每帧顺手核对一段地块(SweepPerFrame)：画成的城与眼下的城对不上(城没了、地块换了城、还没画过)就重画，不靠各处记得通知；
//   · 地图切成 ChunkTiles×ChunkTiles 格一张的小贴图，只上传改过的几张(整张地图一张贴图上传不便宜)，没画东西的不显示。
// 纯净地图下一直显示(同路网)，城市图层不论纯净地图开没开都显示
public static class CityPlanOverlay
{
    private const int ChunkTiles = 64;
    private const double BuildBudgetMs = 3d;
    private const int SweepPerFrame = 256;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000d;
    private static readonly Color32 Clear = new(0, 0, 0, 0);

    private sealed class Chunk
    {
        public int X0, Y0, W, H;
        public Color32[] Pixels;
        // 不透明度大于 0 的像素数：为 0 的不显示
        public int Painted;
        public bool Dirty;
        public GameObject Object;
        public SpriteRenderer Renderer;
        public Texture2D Texture;
    }

    private static readonly Dictionary<int, Chunk> Chunks = new();
    // 每块地画成了哪座城(没画、不归城的不在里面)；每座城画的时候用的平面图指纹
    private static readonly Dictionary<int, long> ZoneCity = new();
    private static readonly Dictionary<long, int> CityStamp = new();
    private static readonly HashSet<int> Dirty = new();
    private static readonly Queue<int> DirtyQueue = new();
    private static readonly global::System.Action<WorldTile, Color32> PutAction = Put;
    private static object _world;
    private static int _width, _height, _cols, _sweep;
    private static bool _vivid, _primed;

    public static void ResetWorldState()
    {
        foreach (Chunk chunk in Chunks.Values)
        {
            if (chunk.Object != null) Object.Destroy(chunk.Object);
            if (chunk.Texture != null) Object.Destroy(chunk.Texture);
        }
        Chunks.Clear();
        ZoneCity.Clear();
        CityStamp.Clear();
        Dirty.Clear();
        DirtyQueue.Clear();
        _world = null;
        _sweep = 0;
        _primed = false;
    }

    public static void MarkZone(TileZone zone)
    {
        if (zone != null && Dirty.Add(zone.id)) DirtyQueue.Enqueue(zone.id);
    }

    // 这座城要重画(原来画成本城、现在不归本城的地块由逐段核对发现)
    public static void MarkCity(City city)
    {
        if (city?.zones == null) return;
        foreach (TileZone zone in city.zones) MarkZone(zone);
    }

    // 全部重画：先排镜头里的
    public static void MarkAll()
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return;
        foreach (int id in ZoneCity.Keys)
        {
            TileZone zone = calc.getZoneByID(id);
            if (zone != null && zone.visible) MarkZone(zone);
        }
        foreach (City city in World.world.cities)
            if (city?.zones != null)
                foreach (TileZone zone in city.zones)
                    if (zone != null && zone.visible) MarkZone(zone);
        foreach (int id in ZoneCity.Keys) MarkZone(calc.getZoneByID(id));
        foreach (City city in World.world.cities) MarkCity(city);
    }

    // 这座城的平面图按轮重建过(见 PureMapPatch.RefreshUrbanSlice)：和画它的时候用的不一样了才重画；还没画过的不管
    public static void PlanRebuilt(City city, int stamp)
    {
        if (city?.data != null && CityStamp.TryGetValue(city.data.id, out int drawn) && drawn != stamp) MarkCity(city);
    }

    public static void Update()
    {
        MapBox world = World.world;
        if (!ReferenceEquals(_world, world) || MapBox.width != _width || MapBox.height != _height)
        {
            ResetWorldState();
            _world = world;
            _width = MapBox.width;
            _height = MapBox.height;
            _cols = (_width + ChunkTiles - 1) / ChunkTiles;
        }
        ZoneCalculator calc = world?.zone_calculator;
        SpriteRenderer source = calc?.sprRnd;
        bool cityLayer = CityViewMap.Active;
        if (source?.sprite == null || !source.enabled || _width <= 0 || _height <= 0 ||
            !cityLayer && !PureMapMode.Enabled)
        {
            // 不显示期间平面图不按轮重建(没开纯净地图时)，再显示时整张重画
            _primed = false;
            Hide();
            return;
        }
        // 城市图层画得鲜艳些(见 UrbanFabric.Vivid)：切换时整张重画
        if (!_primed || cityLayer != _vivid)
        {
            _primed = true;
            _vivid = cityLayer;
            MarkAll();
        }
        using (FrameProfiler.Measure("城市平面图层·重画"))
        {
            Sweep(calc);
            Build(calc);
            Upload(source);
        }
        Show(source);
    }

    private static long Owner(TileZone zone)
    {
        City city = zone.city;
        return city?.data != null && !city.isRekt() && !AncientWarfareCompatibility.OwnsObject(city) ? city.data.id : -1L;
    }

    // 逐段核对：画成的城与眼下的城对不上的地块重画
    private static void Sweep(ZoneCalculator calc)
    {
        List<TileZone> all = calc.zones;
        if (all == null || all.Count == 0) return;
        for (int i = 0; i < SweepPerFrame && i < all.Count; i++)
        {
            if (_sweep >= all.Count) _sweep = 0;
            TileZone zone = all[_sweep++];
            if (zone == null) continue;
            long drawn = ZoneCity.TryGetValue(zone.id, out long id) ? id : -1L;
            if (Owner(zone) != drawn) MarkZone(zone);
        }
    }

    // 画排着的地块，画到预算为止：先过一遍只画镜头里的，还有时间再画镜头外的
    private static void Build(ZoneCalculator calc)
    {
        if (DirtyQueue.Count == 0) return;
        long deadline = Stopwatch.GetTimestamp() + (long)(BuildBudgetMs * TicksPerMs);
        for (int pass = 0; pass < 2; pass++)
        {
            int count = DirtyQueue.Count;
            for (int i = 0; i < count; i++)
            {
                if (Stopwatch.GetTimestamp() > deadline) return;
                int id = DirtyQueue.Dequeue();
                TileZone zone = calc.getZoneByID(id);
                if (zone == null)
                {
                    Dirty.Remove(id);
                    continue;
                }
                if (pass == 0 && !zone.visible)
                {
                    DirtyQueue.Enqueue(id);
                    continue;
                }
                Dirty.Remove(id);
                Draw(zone);
            }
        }
    }

    private static void Draw(TileZone zone)
    {
        long owner = Owner(zone);
        if (owner < 0)
        {
            ZoneCity.Remove(zone.id);
            if (zone.tiles != null)
                foreach (WorldTile tile in zone.tiles)
                    if (tile != null) Put(tile, Clear);
            return;
        }
        ZoneCity[zone.id] = owner;
        CityStamp[owner] = PureMapPatch.PlanZone(zone, _vivid, PutAction);
    }

    private static void Put(WorldTile tile, Color32 color)
    {
        int x = tile.x, y = tile.y;
        if (x < 0 || y < 0 || x >= _width || y >= _height) return;
        int key = y / ChunkTiles * _cols + x / ChunkTiles;
        if (!Chunks.TryGetValue(key, out Chunk chunk))
        {
            if (color.a == 0) return;
            int x0 = x / ChunkTiles * ChunkTiles, y0 = y / ChunkTiles * ChunkTiles;
            chunk = new Chunk
            {
                X0 = x0, Y0 = y0, W = Mathf.Min(ChunkTiles, _width - x0), H = Mathf.Min(ChunkTiles, _height - y0)
            };
            chunk.Pixels = new Color32[chunk.W * chunk.H];
            Chunks[key] = chunk;
        }
        int i = (y - chunk.Y0) * chunk.W + (x - chunk.X0);
        Color32 old = chunk.Pixels[i];
        if (old.r == color.r && old.g == color.g && old.b == color.b && old.a == color.a) return;
        if (old.a == 0 && color.a > 0) chunk.Painted++;
        else if (old.a > 0 && color.a == 0) chunk.Painted--;
        chunk.Pixels[i] = color;
        chunk.Dirty = true;
    }

    // 改过的小贴图上传；与色块图层同一锚点、同一比例(同 CityDetailOverlay)
    private static void Upload(SpriteRenderer source)
    {
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit;
        foreach (Chunk chunk in Chunks.Values)
        {
            if (!chunk.Dirty) continue;
            chunk.Dirty = false;
            if (chunk.Texture == null)
                chunk.Texture = new Texture2D(chunk.W, chunk.H, TextureFormat.RGBA32, false)
                {
                    name = "EmpireCraftCityPlan", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
            chunk.Texture.SetPixels32(chunk.Pixels);
            chunk.Texture.Apply(false);
            if (chunk.Object == null)
            {
                chunk.Object = new GameObject("EmpireCraftCityPlan");
                chunk.Renderer = chunk.Object.AddComponent<SpriteRenderer>();
                chunk.Renderer.sprite = Sprite.Create(chunk.Texture, new Rect(0f, 0f, chunk.W, chunk.H), Vector2.zero, ppu);
            }
            if (chunk.Object.transform.parent != source.transform) chunk.Object.transform.SetParent(source.transform, false);
            chunk.Object.transform.localPosition =
                new Vector3((chunk.X0 - reference.pivot.x) / ppu, (chunk.Y0 - reference.pivot.y) / ppu, 0f);
        }
    }

    private static void Show(SpriteRenderer source)
    {
        foreach (Chunk chunk in Chunks.Values)
        {
            if (chunk.Renderer == null) continue;
            bool on = chunk.Painted > 0;
            if (chunk.Renderer.enabled != on) chunk.Renderer.enabled = on;
            if (!on) continue;
            chunk.Renderer.sortingLayerID = source.sortingLayerID;
            // 色块之下、地形之上；拉远到原版小地图模式时色块不透明度是 1(原版 minimap_opacity)，压在下面整张看不见
            // (用户 2026-10-27 "简略城市和主干道在拉远后不见了""以前同样远看得见"：以前平面图画进色块里)，这时改到色块之上；
            // 路网在城市之下(见 RoadOverlay：平常 −2，小地图模式 +1)
            chunk.Renderer.sortingOrder = MapBox.isRenderMiniMap() ? source.sortingOrder + 2 : source.sortingOrder - 1;
        }
    }

    private static void Hide()
    {
        foreach (Chunk chunk in Chunks.Values)
            if (chunk.Renderer != null && chunk.Renderer.enabled) chunk.Renderer.enabled = false;
    }
}
