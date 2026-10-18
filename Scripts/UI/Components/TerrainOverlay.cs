using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 非平滑模式的远景地形底(见 MapZoomTier ①)：一张与地图等大的贴图，按地形画成电子地图的配色——平原薄荷绿、林地绿、丛林深绿、
// 丘陵浅黄绿、山地米灰、山巅与雪原白、荒漠米黄、沼泽灰绿；水面与国界那一格留空(国色的蓝水与国界照常露出来)。
// 盖在国色色块上，拉远时不透明度高(国色只剩薄薄一层)，拉近逐渐变淡，到近景就没了。
// 地形、国界会变：每 30 秒按行分帧重画一遍(每帧至多 48 行)，不在一帧里扫全图。
public static class TerrainOverlay
{
    private const float FarAlpha = 0.72f, MidAlpha = 0.45f;
    private const float RefreshSeconds = 30f;
    private const int RowsPerFrame = 48;
    private static readonly Color32 Clear = new(0, 0, 0, 0);
    private static readonly Color32 Plains = new(204, 236, 210, 255), Forest = new(176, 218, 180, 255),
        Jungle = new(156, 206, 166, 255), Hills = new(214, 226, 192, 255), Mountain = new(228, 223, 210, 255),
        Summit = new(246, 246, 244, 255), Snow = new(242, 246, 250, 255), Desert = new(243, 235, 214, 255),
        Swamp = new(190, 214, 198, 255);

    private static GameObject _object;
    private static SpriteRenderer _renderer;
    private static Texture2D _texture;
    private static Color32[] _pixels;
    private static int _row = -1;
    private static float _finishedAt = -1000f;
    private static object _world;

    public static void Update()
    {
        ZoneCalculator zones = World.world?.zone_calculator;
        SpriteRenderer source = zones?.sprRnd;
        // 平滑政治底图已经逐像素做地形浮雕。再盖浅色地形会冲淡国色，且这张贴图按
        // 逻辑地块挖出的国界空隙会把原版方格边界重新露出来。
        if (SmoothBorders.Enabled || !PureMapMode.Enabled || !PureMapPatch.DetailShown || EmpireCraft.Scripts.Layer.CityViewMap.Active ||
            source?.sprite == null)
        {
            if (_renderer != null) _renderer.enabled = false;
            return;
        }
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _row = -1;
            _finishedAt = -1000f;
        }
        if (!Ensure(zones, source)) return;
        // 远景最浓，中景减半，过了近景门槛就没了
        float mid = MapZoomTier.Past(MapZoomTier.Mid), near = MapZoomTier.Past(MapZoomTier.Near);
        float alpha = Mathf.Lerp(Mathf.Lerp(FarAlpha, MidAlpha, mid), 0f, near);
        _renderer.enabled = source.enabled && alpha > 0.01f;
        _renderer.color = new Color(1f, 1f, 1f, alpha);
        _renderer.sortingLayerID = source.sortingLayerID;
        _renderer.sortingOrder = source.sortingOrder + 1;
        if (_row < 0 && Time.unscaledTime - _finishedAt >= RefreshSeconds) _row = 0;
        if (_row >= 0) using (FrameProfiler.Measure("地形底·分帧重画")) Step();
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
            name = "EmpireCraftTerrain", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
        };
        _pixels = new Color32[width * height];
        Sprite reference = source.sprite;
        var pivot = new Vector2(reference.pivot.x / reference.rect.width, reference.pivot.y / reference.rect.height);
        _object = new GameObject("EmpireCraftTerrainOverlay");
        _object.transform.SetParent(zones.transform, false);
        _renderer = _object.AddComponent<SpriteRenderer>();
        _renderer.sprite = Sprite.Create(_texture, new Rect(0f, 0f, width, height), pivot, reference.pixelsPerUnit);
        _row = 0;
        return true;
    }

    private static void Step()
    {
        int width = MapBox.width, height = MapBox.height;
        int end = Mathf.Min(height, _row + RowsPerFrame);
        for (int y = _row; y < end; y++)
        for (int x = 0; x < width; x++)
        {
            WorldTile tile = World.world.GetTile(x, y);
            int id = y * width + x;
            if (id >= _pixels.Length) continue;
            _pixels[id] = tile == null ? Clear : Paint(tile);
        }
        _row = end;
        if (_row < height) return;
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
        _row = -1;
        _finishedAt = Time.unscaledTime;
    }

    private static Color32 Paint(WorldTile tile)
    {
        TileTypeBase type = tile.Type;
        if (type == null || type.liquid && !type.lava || type.ocean) return Clear;
        if (Border(tile)) return Clear;
        TileTypeBase main = tile.main_type;
        string id = type.id ?? "";
        Color32 color;
        if (type.summit || main != null && main.summit) color = Summit;
        else if (type.mountains || main != null && main.mountains) color = Mountain;
        else if (type.biome_id == "biome_permafrost" || id.StartsWith("snow") || id.StartsWith("frozen")) color = Snow;
        else if (id == "hills" || main?.id == "hills") color = Hills;
        else
        {
            color = type.biome_id switch
            {
                "biome_desert" or "biome_sand" or "biome_savanna" => Desert,
                "biome_swamp" => Swamp,
                "biome_jungle" => Jungle,
                "biome_birch" or "biome_maple" or "biome_forest" or "biome_pine" or "biome_enchanted" => Forest,
                _ => Plains
            };
        }
        // 一点细碎的明暗，看得出地表的质感
        int noise = ((tile.x * 73856093) ^ (tile.y * 19349663)) & 7;
        int delta = noise - 3;
        return new Color32((byte)Mathf.Clamp(color.r + delta, 0, 255), (byte)Mathf.Clamp(color.g + delta, 0, 255),
            (byte)Mathf.Clamp(color.b + delta, 0, 255), 255);
    }

    // 国界那一格留空：四邻里有归属不同国家的陆地
    private static bool Border(WorldTile tile)
    {
        Kingdom own = tile.zone?.city?.kingdom;
        WorldTile[] neighbours = tile.neighbours;
        if (neighbours == null) return false;
        foreach (WorldTile next in neighbours)
        {
            if (next == null) continue;
            TileTypeBase type = next.Type;
            if (type != null && (type.ocean || type.liquid && !type.lava)) continue;
            if (next.zone?.city?.kingdom != own) return true;
        }
        return false;
    }
}
