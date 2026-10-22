using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 平滑国界的翻牌(用户 2026-10-16 看过演示版："可以，移植进游戏"；换色见 SmoothBorders)：
// 正在翻的地块画成一张张牌，盖在国色色块上面一层。牌是原版地块大小(8×8 格)，牌面是这块地翻之前、之后的样子
// (一格一像素，存在一张图集里)；按转过的角度带一点透视，逐列(上下翻是逐行)画成梯形，转到侧面时暗一些。
// 翻的时候底下的色块画成压暗的样子(牌翘起来的缝，见 SmoothBorders)。镜头外的不画；拉得越远一张牌切得越少。
public static class ZoneFlipOverlay
{
    // 图集 1024：全屏一起翻时拉远看大地图，同时翻的牌能上四千多张(以前 512 只放得下两千张，多出来的不画牌直接变色)
    private const int Zone = Config.CITY_ZONE_SIZE, AtlasSize = 1024, SlotsPerRow = AtlasSize / Zone, MaxSlots = SlotsPerRow * SlotsPerRow;
    // 透视(视距是牌宽的几倍)、转到侧面时暗多少
    private const float Perspective = 2.6f, SideShade = 0.38f;

    private static GameObject _object;
    private static MeshRenderer _renderer;
    private static Mesh _mesh;
    private static Texture2D _atlas;
    private static Transform _parent;
    private static bool _atlasDirty;

    private sealed class Entry
    {
        public int Old, Fresh;
        public Color32[] OldPixels, NewPixels;
    }

    private static readonly Dictionary<int, Entry> Entries = new();
    private static readonly Stack<int> Free = new();
    private static readonly HashSet<int> Touched = new();
    private static readonly List<Vector3> Vertices = new();
    private static readonly List<Vector2> Uvs = new();
    private static readonly List<Color32> Colors = new();
    private static readonly List<int> Triangles = new();
    private static readonly Color32[] Block = new Color32[Zone * Zone];
    private static Rect _view;
    private static int _strips = Zone;
    private static float _ppu = 1f;
    private static Vector2 _pivot;
    private static Color _tint = Color.white;

    // 镜头范围(格)：按色块贴图的摆放把镜头四角换成格坐标
    public static Rect CameraView()
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        SpriteRenderer source = calc?.sprRnd;
        Camera camera = World.world?.camera;
        if (source?.sprite == null || camera == null) return new Rect(0f, 0f, MapBox.width, MapBox.height);
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit;
        Vector3 a = source.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0f, 0f, 0f)));
        Vector3 b = source.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(1f, 1f, 0f)));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x) * ppu + reference.pivot.x, Mathf.Min(a.y, b.y) * ppu + reference.pivot.y,
            Mathf.Max(a.x, b.x) * ppu + reference.pivot.x, Mathf.Max(a.y, b.y) * ppu + reference.pivot.y);
    }

    // 一帧开始：记下镜头、清空上一帧的牌
    public static void Begin()
    {
        Touched.Clear();
        Vertices.Clear();
        Uvs.Clear();
        Colors.Clear();
        Triangles.Clear();
        _view = CameraView();
        SpriteRenderer source = World.world?.zone_calculator?.sprRnd;
        Camera camera = World.world?.camera;
        if (source?.sprite != null)
        {
            _ppu = source.sprite.pixelsPerUnit;
            _pivot = source.sprite.pivot;
            _tint = source.color;
        }
        // 一张牌在屏幕上多宽：宽的逐格切成八条，窄的少切几条(看不出透视)
        float pixels = camera == null || camera.orthographicSize <= 0f ? 8f : Screen.height / (2f * camera.orthographicSize) * Zone;
        _strips = pixels >= 24f ? Zone : pixels >= 8f ? 4 : 1;
    }

    // 一张正在翻的牌：progress 0~1(过了一半是新样子)；vertical 绕横轴上下翻；lift 哪一边先翘起来(+1 坐标大的那边)
    public static void Card(TileZone zone, Color32[] oldPixels, Color32[] newPixels, float progress, bool vertical, float lift)
    {
        if (zone?.tiles == null || oldPixels == null || newPixels == null) return;
        int x0 = zone.x * Zone, y0 = zone.y * Zone;
        if (x0 + Zone + 2 < _view.xMin || x0 - 2 > _view.xMax || y0 + Zone + 2 < _view.yMin || y0 - 2 > _view.yMax) return;
        if (!Entries.TryGetValue(zone.id, out Entry entry))
        {
            // 图集满了：这张不画(底下压暗的色块照样翻完)
            if (Free.Count + MaxSlots - _next < 2) return;
            entry = new Entry { Old = Take(), Fresh = Take() };
            Entries[zone.id] = entry;
        }
        if (!ReferenceEquals(entry.OldPixels, oldPixels)) Write(entry.Old, zone, entry.OldPixels = oldPixels);
        if (!ReferenceEquals(entry.NewPixels, newPixels)) Write(entry.Fresh, zone, entry.NewPixels = newPixels);
        Touched.Add(zone.id);
        float theta = Mathf.PI * Mathf.Clamp01(progress);
        bool front = theta < Mathf.PI / 2f;
        float phi = front ? theta : Mathf.PI - theta, c = Mathf.Cos(phi), s = (front ? 1f : -1f) * lift * Mathf.Sin(phi);
        float focal = Zone * Perspective, cx = x0 + Zone / 2f, cy = y0 + Zone / 2f;
        int slot = front ? entry.Old : entry.Fresh, sx = slot % SlotsPerRow * Zone, sy = slot / SlotsPerRow * Zone;
        float light = 1f - SideShade * Mathf.Sin(theta);
        var color = new Color32((byte)(255f * light * _tint.r), (byte)(255f * light * _tint.g), (byte)(255f * light * _tint.b),
            (byte)(255f * _tint.a));
        float width = Zone / (float)_strips;
        for (int j = 0; j < _strips; j++)
        {
            float u0 = -Zone / 2f + j * width, u1 = u0 + width;
            float k0 = focal / (focal - u0 * s), k1 = focal / (focal - u1 * s);
            float at0 = u0 * c * k0, at1 = u1 * c * k1, half0 = Zone * k0 / 2f, half1 = Zone * k1 / 2f;
            int index = Vertices.Count;
            if (vertical)
            {
                Vertices.Add(Local(cx - half0, cy + at0));
                Vertices.Add(Local(cx + half0, cy + at0));
                Vertices.Add(Local(cx + half1, cy + at1));
                Vertices.Add(Local(cx - half1, cy + at1));
                // 这一条对应牌面上的第 j 行
                float v0 = (sy + j * width + 0.02f) / AtlasSize, v1 = (sy + (j + 1) * width - 0.02f) / AtlasSize;
                float uL = (sx + 0.02f) / AtlasSize, uR = (sx + Zone - 0.02f) / AtlasSize;
                Uvs.Add(new Vector2(uL, v0));
                Uvs.Add(new Vector2(uR, v0));
                Uvs.Add(new Vector2(uR, v1));
                Uvs.Add(new Vector2(uL, v1));
            }
            else
            {
                Vertices.Add(Local(cx + at0, cy - half0));
                Vertices.Add(Local(cx + at1, cy - half1));
                Vertices.Add(Local(cx + at1, cy + half1));
                Vertices.Add(Local(cx + at0, cy + half0));
                float u0t = (sx + j * width + 0.02f) / AtlasSize, u1t = (sx + (j + 1) * width - 0.02f) / AtlasSize;
                float vB = (sy + 0.02f) / AtlasSize, vT = (sy + Zone - 0.02f) / AtlasSize;
                Uvs.Add(new Vector2(u0t, vB));
                Uvs.Add(new Vector2(u1t, vB));
                Uvs.Add(new Vector2(u1t, vT));
                Uvs.Add(new Vector2(u0t, vT));
            }
            for (int n = 0; n < 4; n++) Colors.Add(color);
            Triangles.Add(index);
            Triangles.Add(index + 1);
            Triangles.Add(index + 2);
            Triangles.Add(index);
            Triangles.Add(index + 2);
            Triangles.Add(index + 3);
        }
    }

    // 一帧画完：这帧没翻的牌收回图集位置，上传图集、换网格
    public static void End()
    {
        if (Entries.Count > Touched.Count)
        {
            var gone = new List<int>();
            foreach (KeyValuePair<int, Entry> pair in Entries)
                if (!Touched.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (int id in gone)
            {
                Free.Push(Entries[id].Old);
                Free.Push(Entries[id].Fresh);
                Entries.Remove(id);
            }
        }
        SpriteRenderer source = World.world?.zone_calculator?.sprRnd;
        if (Vertices.Count == 0 || source?.sprite == null || !source.enabled)
        {
            Hide(false);
            return;
        }
        if (!Ensure(source)) return;
        if (_atlasDirty)
        {
            _atlas.Apply(false);
            _atlasDirty = false;
        }
        _mesh.Clear();
        _mesh.SetVertices(Vertices);
        _mesh.SetUVs(0, Uvs);
        _mesh.SetColors(Colors);
        _mesh.SetTriangles(Triangles, 0);
        _renderer.sortingLayerID = source.sortingLayerID;
        _renderer.sortingOrder = source.sortingOrder + 2;
        if (!_renderer.enabled) _renderer.enabled = true;
    }

    // 没有在翻的牌：不画；release 时连图集位置也全收回(换世界、关平滑国界)
    public static void Hide(bool release = true)
    {
        if (_renderer != null && _renderer.enabled) _renderer.enabled = false;
        if (!release) return;
        Entries.Clear();
        Free.Clear();
        _next = 0;
    }

    private static int _next;

    private static int Take()
    {
        if (Free.Count > 0) return Free.Pop();
        return _next < MaxSlots ? _next++ : 0;
    }

    // 牌面写进图集：按格在地块里的位置摆(地块的格不一定按行排)
    private static void Write(int slot, TileZone zone, Color32[] pixels)
    {
        if (_atlas == null) CreateAtlas();
        int count = Mathf.Min(pixels.Length, zone.tiles.Length);
        for (int i = 0; i < Block.Length; i++) Block[i] = new Color32(0, 0, 0, 0);
        for (int i = 0; i < count; i++)
        {
            WorldTile tile = zone.tiles[i];
            int lx = tile.x - zone.x * Zone, ly = tile.y - zone.y * Zone;
            if (lx is >= 0 and < Zone && ly is >= 0 and < Zone) Block[ly * Zone + lx] = pixels[i];
        }
        _atlas.SetPixels32(slot % SlotsPerRow * Zone, slot / SlotsPerRow * Zone, Zone, Zone, Block);
        _atlasDirty = true;
    }

    private static void CreateAtlas()
    {
        _atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false)
        {
            name = "EmpireCraftZoneFlip", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
        };
    }

    // 格坐标换成色块贴图下的本地坐标
    private static Vector3 Local(float x, float y) => new((x - _pivot.x) / _ppu, (y - _pivot.y) / _ppu, 0f);

    private static bool Ensure(SpriteRenderer source)
    {
        if (_atlas == null) CreateAtlas();
        if (_object != null && _parent == source.transform) return true;
        if (_object != null) Object.Destroy(_object);
        _object = new GameObject("EmpireCraftZoneFlip");
        _object.layer = source.gameObject.layer;
        _parent = source.transform;
        _object.transform.SetParent(_parent, false);
        _object.transform.localPosition = Vector3.zero;
        _object.transform.localRotation = Quaternion.identity;
        _object.transform.localScale = Vector3.one;
        _mesh = new Mesh { name = "EmpireCraftZoneFlip", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        _mesh.MarkDynamic();
        _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = _object.AddComponent<MeshRenderer>();
        // 用色块同一种材质(精灵着色器，乘顶点色)，换上图集
        _renderer.sharedMaterial = new Material(source.sharedMaterial) { mainTexture = _atlas };
        _renderer.enabled = false;
        return true;
    }
}
