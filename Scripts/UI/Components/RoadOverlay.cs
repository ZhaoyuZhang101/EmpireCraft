using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图的路网图层(见 RoadNetwork)：修通的路按等级画(土路淡褐、驿道浅黄、官道黄、公路金黄、柏油公路橙)，
// 在修的路段画成灰色，航线画成浅色虚线，盖在国家色块之上——
// 海面与无主地在色块里是空的，所以单独用一张与地图等大的贴图画，跟着色块图层一起显示。
// 城里建成的地块有自己的街格，干道不压上去。路网变了、或城在长，至多每十秒重画一次(整张贴图上传不便宜)。
// 路的颜色随底色换：底下的国色亮(黄、白、浅绿)就画深褐的路，暗就画黄橙的路，任何国色上都看得清；
// 换了地图图层(底色全变)半秒后重画一次。
// 远近分层：拉远到原版远景不画路(只剩国色与地形，见 PureMapPatch.DetailShown)；切到近景时路淡淡出现，越拉近越清楚。
public static class RoadOverlay
{
    private const float RedrawSeconds = 20f, MinRedrawSeconds = 10f;
    private static readonly Color32 Lane = new(232, 244, 255, 205), Building = new(170, 170, 170, 150), Clear = new(0, 0, 0, 0);
    private static readonly Color32[] Grades =
    {
        new(0, 0, 0, 0), new(190, 165, 120, 170), new(225, 198, 135, 195), new(245, 208, 115, 215),
        new(255, 200, 90, 225), new(255, 165, 60, 235)
    };
    // 亮底色上用的深色路(同样按等级由浅到深)
    private static readonly Color32[] DarkGrades =
    {
        new(0, 0, 0, 0), new(120, 95, 70, 190), new(100, 75, 50, 210), new(85, 60, 40, 225),
        new(70, 45, 30, 235), new(55, 35, 25, 240)
    };
    private const float BrightBackground = 0.55f;
    private static object _layer;
    private static int _layerOption = -1;

    private static GameObject _object;
    private static SpriteRenderer _renderer;
    private static Texture2D _texture;
    private static Color32[] _pixels;
    private static readonly List<int> Painted = new();
    private static int _drawnVersion = -1;
    private static float _drawnAt = -1000f;

    public static void Update()
    {
        ZoneCalculator zones = World.world?.zone_calculator;
        SpriteRenderer source = zones?.sprRnd;
        if (!PureMapMode.Enabled || source?.sprite == null)
        {
            if (_renderer != null) _renderer.enabled = false;
            return;
        }
        if (!Ensure(zones, source)) return;
        float alpha = ZoomAlpha();
        _renderer.enabled = source.enabled && alpha > 0.01f;
        _renderer.color = new Color(1f, 1f, 1f, alpha);
        _renderer.sortingLayerID = source.sortingLayerID;
        _renderer.sortingOrder = source.sortingOrder + 1;
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
        bool due = since < 0f || since >= RedrawSeconds || _drawnVersion != RoadNetwork.Version && since >= MinRedrawSeconds;
        if (!due) return;
        _drawnVersion = RoadNetwork.Version;
        _drawnAt = now;
        using (FrameProfiler.Measure("路网·贴图重画")) Redraw();
    }

    // 刚切进近景时四成半，拉近到远景界线的一半多时全显
    private const float NearAlpha = 0.45f, FullAt = 0.55f;

    private static float ZoomAlpha()
    {
        QualityChanger quality = World.world?.quality_changer;
        if (quality == null) return 1f;
        if (!PureMapPatch.DetailShown) return 0f;
        float bound = quality.getZoomRateBoundLow();
        Camera camera = Camera.main;
        float zoom = camera != null ? camera.orthographicSize : bound;
        return Mathf.Lerp(NearAlpha, 1f, Mathf.InverseLerp(bound, bound * FullAt, zoom));
    }

    private static bool Ensure(ZoneCalculator zones, SpriteRenderer source)
    {
        int width = MapBox.width, height = MapBox.height;
        if (width <= 0 || height <= 0) return false;
        if (_object != null && _texture != null && _texture.width == width && _texture.height == height &&
            _object.transform.parent == zones.transform)
            return true;
        if (_object != null) Object.Destroy(_object);
        _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "EmpireCraftRoads",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _pixels = new Color32[width * height];
        // 与色块图层同一锚点、同一比例，叠在它的正上方
        Sprite reference = source.sprite;
        var pivot = new Vector2(reference.pivot.x / reference.rect.width, reference.pivot.y / reference.rect.height);
        _object = new GameObject("EmpireCraftRoadOverlay");
        _object.transform.SetParent(zones.transform, false);
        _renderer = _object.AddComponent<SpriteRenderer>();
        _renderer.sprite = Sprite.Create(_texture, new Rect(0f, 0f, width, height), pivot, reference.pixelsPerUnit);
        Painted.Clear();
        _drawnAt = -1000f;
        return true;
    }

    private static readonly Dictionary<int, bool> BuiltZones = new();

    private static void Redraw()
    {
        BuiltZones.Clear();
        foreach (int id in Painted) _pixels[id] = Clear;
        Painted.Clear();
        WorldTile[] tiles = World.world.tiles_list;
        foreach (int id in RoadNetwork.LaneTileIds) Paint(id, Lane);
        foreach (int id in RoadNetwork.BuildingTileIds)
            if (Outside(tiles, id)) Paint(id, Building);
        Color32[] background = World.world.zone_calculator?.pixels;
        foreach (var pair in RoadNetwork.RoadTileGrades)
        {
            if (!Outside(tiles, pair.Key)) continue;
            int grade = Mathf.Clamp(pair.Value, 1, Grades.Length - 1);
            Paint(pair.Key, Bright(background, pair.Key) ? DarkGrades[grade] : Grades[grade]);
        }
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    // 这一格底下的国色亮不亮(亮的上面画深色的路)
    private static bool Bright(Color32[] background, int id)
    {
        if (background == null || id < 0 || id >= background.Length) return false;
        Color32 color = background[id];
        if (color.a == 0) return false;
        return (0.299f * color.r + 0.587f * color.g + 0.114f * color.b) / 255f >= BrightBackground;
    }

    // 城里建成的地块有自己的街格，路不压上去
    private static bool Outside(WorldTile[] tiles, int id)
    {
        WorldTile tile = tiles != null && id >= 0 && id < tiles.Length ? tiles[id] : null;
        if (tile?.zone == null) return false;
        if (!BuiltZones.TryGetValue(tile.zone.id, out bool built))
            BuiltZones[tile.zone.id] = built = PureMapPatch.Built(tile.zone);
        return !built;
    }

    private static void Paint(int id, Color32 color)
    {
        if (id < 0 || id >= _pixels.Length) return;
        _pixels[id] = color;
        Painted.Add(id);
    }
}
