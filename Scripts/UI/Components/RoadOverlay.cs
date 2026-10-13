using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图的路网图层(见 RoadNetwork)：干道画成黄色实线，航线画成浅色虚线，盖在国家色块之上——
// 海面与无主地在色块里是空的，所以单独用一张与地图等大的贴图画，跟着色块图层一起显示。
// 城里建成的地块有自己的街格，干道不压上去。路网变了(至少隔三秒)、或每十秒(城在长)重画一次。
public static class RoadOverlay
{
    private const float RedrawSeconds = 10f, MinRedrawSeconds = 3f;
    private static readonly Color32 Road = new(255, 205, 100, 215), Lane = new(232, 244, 255, 205), Clear = new(0, 0, 0, 0);

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
        _renderer.enabled = source.enabled;
        _renderer.sortingLayerID = source.sortingLayerID;
        _renderer.sortingOrder = source.sortingOrder + 1;
        float now = Time.unscaledTime;
        float since = now - _drawnAt;
        bool due = since < 0f || since >= RedrawSeconds || _drawnVersion != RoadNetwork.Version && since >= MinRedrawSeconds;
        if (!due) return;
        _drawnVersion = RoadNetwork.Version;
        _drawnAt = now;
        Redraw();
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

    private static void Redraw()
    {
        foreach (int id in Painted) _pixels[id] = Clear;
        Painted.Clear();
        WorldTile[] tiles = World.world.tiles_list;
        foreach (int id in RoadNetwork.LaneTileIds) Paint(id, Lane);
        foreach (int id in RoadNetwork.RoadTileIds)
        {
            WorldTile tile = tiles != null && id >= 0 && id < tiles.Length ? tiles[id] : null;
            if (tile?.zone == null || PureMapPatch.Built(tile.zone)) continue;
            Paint(id, Road);
        }
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    private static void Paint(int id, Color32 color)
    {
        if (id < 0 || id >= _pixels.Length) return;
        _pixels[id] = color;
        Painted.Add(id);
    }
}
