using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图的路网图层(见 RoadNetwork)：修通的路按等级画(土路淡褐、驿道浅黄、官道黄、公路金黄、柏油公路橙)，
// 在修的路段画成灰色，航线画成浅色虚线，盖在国家色块之上——
// 海面与无主地在色块里是空的，所以单独用一张与地图等大的贴图画，跟着色块图层一起显示。
// 画成城区的格(城墙以内等，见 PureMapPatch.UrbanTile)有自己的街格，干道不压上去，一直通到城墙下。路网变了、或城在长，至多每十秒重画一次(整张贴图上传不便宜)。
// 高德式(2026-10-14 用户选定)：土路浅灰、驿道白、官道浅黄、公路黄、柏油公路橙；驿道以上两侧描一道深色细边，
// 任何国色上都看得清；
// 换了地图图层(底色全变)半秒后重画一次。
// 纯净地图下一直画；城市图层也画(不要求纯净地图，见 PureMapPatch.CitiesShown)。
// 分级显示(见 MapZoomTier)：干线(官道、公路、柏油公路)一张图，中景起出现；支线(土路、驿道)与城外的巷子、零散房屋另一张图，
// 近景起出现。城市图层不分级，一直全显示。
public static class RoadOverlay
{
    private const float RedrawSeconds = 20f, MinRedrawSeconds = 10f;
    private static readonly Color32 Lane = new(232, 244, 255, 205), Building = new(170, 170, 170, 150), Clear = new(0, 0, 0, 0);
    private static readonly Color32[] Grades =
    {
        new(0, 0, 0, 0), new(238, 232, 220, 170), new(255, 255, 255, 215), new(255, 240, 170, 235),
        new(255, 216, 110, 245), new(255, 176, 70, 250)
    };
    // 干线：官道及以上
    private const int MajorGrade = 3;
    // 描边(驿道以上)
    private static readonly Color32 Casing = new(110, 92, 74, 120);
    private static object _layer;
    private static int _layerOption = -1;

    private sealed class Sheet
    {
        public GameObject Object;
        public SpriteRenderer Renderer;
        public Texture2D Texture;
        public Color32[] Pixels;
        public readonly List<int> Painted = new();
        // 上次上传时画了的格与指纹：这回画出来一样就不上传(整张与地图等大的贴图上传是大头，空地图、路没变时每 20 秒白传两张)
        public int UploadedCount = -1, UploadedHash;
    }

    private static Sheet _major, _minor;
    private static int _drawnVersion = -1;
    private static float _drawnAt = -1000f;

    public static void Update()
    {
        ZoneCalculator zones = World.world?.zone_calculator;
        SpriteRenderer source = zones?.sprRnd;
        // 纯净地图，或城市图层(不论纯净地图开没开)
        if (!PureMapMode.Enabled && !EmpireCraft.Scripts.Layer.CityViewMap.Active || source?.sprite == null)
        {
            if (_major?.Renderer != null) _major.Renderer.enabled = false;
            if (_minor?.Renderer != null) _minor.Renderer.enabled = false;
            return;
        }
        if (!Ensure(zones, source)) return;
        float shown = PureMapPatch.CitiesShown ? 1f : 0f;
        // 城市图层不分级
        bool tiered = !EmpireCraft.Scripts.Layer.CityViewMap.Active;
        Show(_major, source, shown * (tiered ? MapZoomTier.Past(MapZoomTier.Mid) : 1f), 3);
        Show(_minor, source, shown * (tiered ? MapZoomTier.Past(MapZoomTier.Near) : 1f), 3);
        float now = Time.unscaledTime;
        // 换了地图图层：底色全变，半秒后(色块重画完)重画路
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        int option = layer?.getZoneOptionState() ?? -1;
        if (!ReferenceEquals(layer, _layer) || option != _layerOption)
        {
            _layer = layer;
            _layerOption = option;
            _drawnAt = Mathf.Min(_drawnAt, now - RedrawSeconds + 0.5f);
        }
        float since = now - _drawnAt;
        // 神力画、擦了路：当场并进路网、马上重画(画的时候至多每四分之一秒一次，见 RoadPaintSystem)
        bool painted = RoadPaintSystem.TakeRedraw(now);
        bool due = painted || since < 0f || since >= RedrawSeconds ||
                   _drawnVersion != RoadNetwork.Version && since >= MinRedrawSeconds;
        if (!due) return;
        _drawnVersion = RoadNetwork.Version;
        _drawnAt = now;
        using (FrameProfiler.Measure("路网·贴图重画")) Redraw();
    }

    private static void Show(Sheet sheet, SpriteRenderer source, float alpha, int order)
    {
        sheet.Renderer.enabled = source.enabled && alpha > 0.01f;
        sheet.Renderer.color = new Color(1f, 1f, 1f, alpha);
        sheet.Renderer.sortingLayerID = source.sortingLayerID;
        // 地形底(+1)、城市精细层(+2，见 CityDetailOverlay)之上
        sheet.Renderer.sortingOrder = source.sortingOrder + order;
    }

    private static bool Ensure(ZoneCalculator zones, SpriteRenderer source)
    {
        int width = MapBox.width, height = MapBox.height;
        if (width <= 0 || height <= 0) return false;
        if (Valid(_major, zones, width, height) && Valid(_minor, zones, width, height)) return true;
        _major = Create("EmpireCraftRoadsMajor", _major, zones, source, width, height);
        _minor = Create("EmpireCraftRoadsMinor", _minor, zones, source, width, height);
        _drawnAt = -1000f;
        return true;
    }

    private static bool Valid(Sheet sheet, ZoneCalculator zones, int width, int height) =>
        sheet?.Object != null && sheet.Texture != null && sheet.Texture.width == width && sheet.Texture.height == height &&
        sheet.Object.transform.parent == zones.transform;

    private static Sheet Create(string name, Sheet old, ZoneCalculator zones, SpriteRenderer source, int width, int height)
    {
        if (old?.Object != null) Object.Destroy(old.Object);
        var sheet = new Sheet
        {
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            },
            Pixels = new Color32[width * height]
        };
        // 与色块图层同一锚点、同一比例，叠在它的正上方
        Sprite reference = source.sprite;
        var pivot = new Vector2(reference.pivot.x / reference.rect.width, reference.pivot.y / reference.rect.height);
        sheet.Object = new GameObject(name);
        sheet.Object.transform.SetParent(zones.transform, false);
        sheet.Renderer = sheet.Object.AddComponent<SpriteRenderer>();
        sheet.Renderer.sprite = Sprite.Create(sheet.Texture, new Rect(0f, 0f, width, height), pivot, reference.pixelsPerUnit);
        return sheet;
    }

    private static void Redraw()
    {
        Clean(_major);
        Clean(_minor);
        WorldTile[] tiles = World.world.tiles_list;
        foreach (int id in RoadNetwork.LaneTileIds) Paint(_minor, id, Lane);
        foreach (int id in RoadNetwork.BuildingTileIds)
            if (Outside(tiles, id)) Paint(_minor, id, Building);
        // 先描边(路两侧不是路的格)，再画路面，路面压在描边上；描边跟着各自那一级的路
        var roads = RoadNetwork.RoadTileGrades;
        foreach (var pair in roads)
        {
            if (pair.Value < 2 || !Outside(tiles, pair.Key)) continue;
            Sheet sheet = pair.Value >= MajorGrade ? _major : _minor;
            WorldTile tile = tiles[pair.Key];
            foreach (WorldTile next in tile.neighbours ?? global::System.Array.Empty<WorldTile>())
            {
                int id = next?.data?.tile_id ?? -1;
                if (id < 0 || roads.ContainsKey(id) || sheet.Pixels[id].a > 0 || !Outside(tiles, id)) continue;
                Paint(sheet, id, Casing);
            }
        }
        foreach (var pair in roads)
        {
            if (!Outside(tiles, pair.Key)) continue;
            int grade = Mathf.Clamp(pair.Value, 1, Grades.Length - 1);
            Paint(grade >= MajorGrade ? _major : _minor, pair.Key, Grades[grade]);
        }
        Upload(_major);
        Upload(_minor);
    }

    private static void Clean(Sheet sheet)
    {
        foreach (int id in sheet.Painted) sheet.Pixels[id] = Clear;
        sheet.Painted.Clear();
    }

    private static void Upload(Sheet sheet)
    {
        int hash = 17;
        foreach (int id in sheet.Painted)
        {
            Color32 color = sheet.Pixels[id];
            hash = unchecked(hash * 31 + id);
            hash = unchecked(hash * 31 + (color.r | color.g << 8 | color.b << 16 | color.a << 24));
        }
        if (sheet.UploadedCount == sheet.Painted.Count && sheet.UploadedHash == hash) return;
        sheet.UploadedCount = sheet.Painted.Count;
        sheet.UploadedHash = hash;
        sheet.Texture.SetPixels32(sheet.Pixels);
        sheet.Texture.Apply(false);
    }

    // 画成城区的格有自己的街格，路不压上去(按格看，不按整块建成地块，见 PureMapPatch.UrbanTile)
    private static bool Outside(WorldTile[] tiles, int id)
    {
        WorldTile tile = tiles != null && id >= 0 && id < tiles.Length ? tiles[id] : null;
        return tile?.zone != null && !PureMapPatch.UrbanTile(tile);
    }

    private static void Paint(Sheet sheet, int id, Color32 color)
    {
        if (id < 0 || id >= sheet.Pixels.Length) return;
        sheet.Pixels[id] = color;
        sheet.Painted.Add(id);
    }
}
