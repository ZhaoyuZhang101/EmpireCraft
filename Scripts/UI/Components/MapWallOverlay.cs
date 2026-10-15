using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 图层城墙(见 MapWallSystem)：按像素画成长城——墙身石色，垛口一明一暗相间；斜着走的地方补一格转角砖，墙身连成一线
// (不是一串虚点)；隔六格左右一座敌楼(3×3 的方台：深色外圈、浅色楼顶)；东南一侧落一道影子。单独一张与地图等大的贴图，盖在色块与路网之上(无主的荒地、山上也画得出)，远近都画——
// 长城是地标，拉远也看得见。城墙变了才重画
public static class MapWallOverlay
{
    private static readonly Color32 StoneLight = new(214, 204, 180, 255), StoneDark = new(172, 160, 134, 255),
        TowerRoof = new(186, 170, 144, 255), TowerBase = new(100, 88, 76, 255), Shadow = new(0, 0, 0, 70),
        Clear = new(0, 0, 0, 0);
    // 敌楼之间至少隔几格
    private const int TowerSpacing = 6;

    private static GameObject _object;
    private static SpriteRenderer _renderer;
    private static Texture2D _texture;
    private static Color32[] _pixels;
    private static readonly List<int> Painted = new();
    private static int _drawnVersion = -1;

    public static void Update()
    {
        ZoneCalculator zones = World.world?.zone_calculator;
        SpriteRenderer source = zones?.sprRnd;
        if (!PureMapMode.Enabled || source?.sprite == null || MapWallSystem.Walls.Count == 0 && Painted.Count == 0)
        {
            if (_renderer != null) _renderer.enabled = false;
            return;
        }
        if (!Ensure(zones, source)) return;
        _renderer.enabled = source.enabled;
        _renderer.sortingLayerID = source.sortingLayerID;
        _renderer.sortingOrder = source.sortingOrder + 3;
        if (_drawnVersion == MapWallSystem.Version) return;
        _drawnVersion = MapWallSystem.Version;
        using (FrameProfiler.Measure("图层城墙·贴图重画")) Redraw();
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
            name = "EmpireCraftWalls",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _pixels = new Color32[width * height];
        Sprite reference = source.sprite;
        var pivot = new Vector2(reference.pivot.x / reference.rect.width, reference.pivot.y / reference.rect.height);
        _object = new GameObject("EmpireCraftWallOverlay");
        _object.transform.SetParent(zones.transform, false);
        _renderer = _object.AddComponent<SpriteRenderer>();
        _renderer.sprite = Sprite.Create(_texture, new Rect(0f, 0f, width, height), pivot, reference.pixelsPerUnit);
        Painted.Clear();
        _drawnVersion = -1;
        return true;
    }

    private static void Redraw()
    {
        foreach (int id in Painted) _pixels[id] = Clear;
        Painted.Clear();
        WorldTile[] tiles = World.world.tiles_list;
        var walls = new List<int>(MapWallSystem.Walls.Keys);
        walls.Sort();
        // 影子：墙东南一格
        foreach (int id in walls)
        {
            WorldTile tile = Tile(tiles, id);
            WorldTile shade = tile == null ? null : World.world.GetTile(tile.x + 1, tile.y - 1);
            if (shade?.data != null && !MapWallSystem.Contains(shade)) Paint(shade.data.tile_id, Shadow);
        }
        // 敌楼：按格序挑，离已有的敌楼至少隔 TowerSpacing 格(沿着墙大致等距)
        var towers = new List<WorldTile>();
        foreach (int id in walls)
        {
            WorldTile tile = Tile(tiles, id);
            if (tile == null) continue;
            bool near = false;
            foreach (WorldTile other in towers)
                if (Mathf.Abs(other.x - tile.x) <= TowerSpacing && Mathf.Abs(other.y - tile.y) <= TowerSpacing)
                {
                    near = true;
                    break;
                }
            if (!near) towers.Add(tile);
        }
        var towerSet = new HashSet<int>();
        foreach (WorldTile tower in towers) towerSet.Add(tower.data.tile_id);
        // 墙身：垛口一明一暗
        foreach (int id in walls)
        {
            WorldTile tile = Tile(tiles, id);
            if (tile == null) continue;
            Paint(id, (tile.x + tile.y) % 2 == 0 ? StoneLight : StoneDark);
        }
        // 转角砖：两格墙只斜着相连(中间两格都不是墙)时补上一格，墙身连成一线
        foreach (int id in walls)
        {
            WorldTile tile = Tile(tiles, id);
            if (tile == null) continue;
            for (int dx = -1; dx <= 1; dx += 2)
            {
                WorldTile diagonal = World.world.GetTile(tile.x + dx, tile.y + 1);
                if (diagonal == null || !MapWallSystem.Contains(diagonal)) continue;
                WorldTile side = World.world.GetTile(tile.x + dx, tile.y), up = World.world.GetTile(tile.x, tile.y + 1);
                if (side == null || up == null || MapWallSystem.Contains(side) || MapWallSystem.Contains(up)) continue;
                WorldTile corner = side.Type?.liquid != true ? side : up;
                Paint(corner.data.tile_id, StoneDark);
            }
        }
        // 敌楼：3×3 的方台，深色外圈、浅色楼顶
        foreach (WorldTile tower in towers)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    WorldTile part = World.world.GetTile(tower.x + dx, tower.y + dy);
                    if (part?.data == null || part.Type?.liquid == true) continue;
                    Paint(part.data.tile_id, dx == 0 && dy == 0 ? TowerRoof : TowerBase);
                }
        }
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    private static WorldTile Tile(WorldTile[] tiles, int id) => tiles != null && id >= 0 && id < tiles.Length ? tiles[id] : null;

    private static void Paint(int id, Color32 color)
    {
        if (id < 0 || id >= _pixels.Length) return;
        if (_pixels[id].a == 0) Painted.Add(id);
        _pixels[id] = color;
    }
}
