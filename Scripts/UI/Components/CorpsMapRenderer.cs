using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 军团在地图上的样子(借鉴钢铁雄心)：
//   · 战线：交战双方控制区相接的地块边，画成金色粗线；
//   · 兵牌：国色小旗 + 兵力 + 兵力条(绿)与组织度条(青)；进攻红框、防守蓝框、溃退灰；有统领实体时跟在统领头顶；
//     同一地块同一国的几支军合并成一块(标"×N"、兵力相加)，不同国并排；
//   · 交战标记：正在交战的城，标出本月攻方胜算。
// 画在地名画布上，按屏幕像素定大小(不随界面缩放放大)，镜头拉远时略缩小。
public static class CorpsMapRenderer
{
    // 以屏幕像素计
    private const float CounterWidth = 42f, CounterHeight = 13f;
    private const float LineWidth = 2.5f;

    private sealed class Counter
    {
        public RectTransform Root;
        public Image Frame, Fill, Flag, Strength, Org;
        public Text Label;
    }

    private sealed class Marker
    {
        public RectTransform Root;
        public Image Frame;
        public Text Label;
    }

    private static RectTransform _root;
    private static Font _font;
    private static Sprite _white;
    private static readonly List<Counter> Counters = new();
    private static readonly List<Image> Lines = new();
    private static readonly List<Marker> Markers = new();
    private static readonly Dictionary<int, int> StackCounts = new();

    private sealed class CounterGroup
    {
        public Kingdom Kingdom;
        public TileZone Zone;
        public Vector3 World;
        public string Task;
        public int Count;
        public float Strength, Full, Org;
    }

    private static readonly Dictionary<(int zone, long kingdom), CounterGroup> Groups = new();

    private static readonly Color FrameAttack = new Color32(232, 88, 79, 255);
    private static readonly Color FrameDefend = new Color32(127, 168, 232, 255);
    private static readonly Color FrameRetreat = new Color32(120, 120, 120, 255);
    private static readonly Color FrameNormal = new Color32(17, 17, 17, 255);
    private static readonly Color FrontColor = new Color32(243, 195, 74, 235);

    public static void EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return;
        if (_root != null && _root.parent == canvas.transform) return;
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        Counters.Clear();
        Lines.Clear();
        Markers.Clear();
        var rootObject = new GameObject("EmpireCraftCorps", typeof(RectTransform), typeof(CorpsMapRendererHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
        _font = canvas.GetComponent<NameplateManager>()?.prefab?._text_name?.font;
    }

    private static Sprite White
    {
        get
        {
            if (_white != null) return _white;
            var texture = new Texture2D(2, 2) { filterMode = FilterMode.Point };
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            _white = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            return _white;
        }
    }

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = White;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text NewText(Transform parent, int size)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = _font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Counter GetCounter(int index)
    {
        while (Counters.Count <= index)
        {
            var go = new GameObject("CorpsCounter", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var counter = new Counter { Root = go.GetComponent<RectTransform>() };
            counter.Root.sizeDelta = new Vector2(CounterWidth, CounterHeight);
            counter.Frame = NewImage(go.transform, "Frame", FrameNormal);
            Place(counter.Frame.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            counter.Fill = NewImage(go.transform, "Fill", new Color32(29, 32, 24, 235));
            Place(counter.Fill.rectTransform, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));
            counter.Flag = NewImage(go.transform, "Flag", Color.white);
            Place(counter.Flag.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 2f), new Vector2(10f, -2f));
            counter.Label = NewText(go.transform, 7);
            Place(counter.Label.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 1f), new Vector2(12f, 0f), new Vector2(-1f, 0f));
            counter.Strength = NewImage(go.transform, "Strength", new Color32(101, 214, 110, 255));
            counter.Org = NewImage(go.transform, "Org", new Color32(127, 216, 234, 255));
            Counters.Add(counter);
        }
        return Counters[index];
    }

    private static Marker GetMarker(int index)
    {
        while (Markers.Count <= index)
        {
            var go = new GameObject("CorpsBattle", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var marker = new Marker { Root = go.GetComponent<RectTransform>() };
            marker.Root.sizeDelta = new Vector2(42f, 10f);
            marker.Frame = NewImage(go.transform, "Frame", FrameAttack);
            Place(marker.Frame.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image fill = NewImage(go.transform, "Fill", new Color32(43, 26, 20, 240));
            Place(fill.rectTransform, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));
            marker.Label = NewText(go.transform, 6);
            marker.Label.alignment = TextAnchor.MiddleCenter;
            marker.Label.color = new Color32(255, 214, 200, 255);
            Place(marker.Label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Markers.Add(marker);
        }
        return Markers[index];
    }

    private static Image GetLine(int index)
    {
        while (Lines.Count <= index)
        {
            Image line = NewImage(_root, "FrontLine", FrontColor);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Lines.Add(line);
        }
        return Lines[index];
    }

    internal static void HostLateUpdate()
    {
        int counters = 0, lines = 0, markers = 0;
        try
        {
            if (_root != null && CorpsSystem.Enabled && World.world?.camera != null && Config.game_loaded &&
                !SmoothLoader.isLoading())
                Render(out counters, out lines, out markers);
        }
        catch (Exception)
        {
            // 绘制出错时本帧不画，下一帧再试
        }
        for (int i = counters; i < Counters.Count; i++) Counters[i].Root.gameObject.SetActive(false);
        for (int i = lines; i < Lines.Count; i++) Lines[i].gameObject.SetActive(false);
        for (int i = markers; i < Markers.Count; i++) Markers[i].Root.gameObject.SetActive(false);
    }

    private static bool OnScreen(Vector3 screen, float margin) =>
        screen.z >= 0f && screen.x > -margin && screen.x < Screen.width + margin &&
        screen.y > -margin && screen.y < Screen.height + margin;

    private static void Render(out int counterCount, out int lineCount, out int markerCount)
    {
        Camera camera = World.world.camera;
        Canvas canvas = CanvasMain.instance.canvas_map_names;
        float scale = Mathf.Max(0.01f, canvas.scaleFactor);
        counterCount = lineCount = markerCount = 0;

        // 战线
        const int zoneSize = Config.CITY_ZONE_SIZE;
        foreach ((TileZone a, TileZone b) in FrontSystem.ContestedEdges())
        {
            Vector3 from, to;
            if (b == a.zone_right)
            {
                float x = (a.x + 1) * zoneSize - 0.5f;
                from = new Vector3(x, a.y * zoneSize - 0.5f);
                to = new Vector3(x, (a.y + 1) * zoneSize - 0.5f);
            }
            else
            {
                float y = (a.y + 1) * zoneSize - 0.5f;
                from = new Vector3(a.x * zoneSize - 0.5f, y);
                to = new Vector3((a.x + 1) * zoneSize - 0.5f, y);
            }
            Vector3 p1 = camera.WorldToScreenPoint(from), p2 = camera.WorldToScreenPoint(to);
            if (!OnScreen(p1, 40f) && !OnScreen(p2, 40f)) continue;
            Image line = GetLine(lineCount++);
            line.gameObject.SetActive(true);
            Vector2 delta = p2 - p1;
            RectTransform rect = line.rectTransform;
            rect.position = (p1 + p2) * 0.5f;
            rect.sizeDelta = new Vector2((delta.magnitude + LineWidth) / scale, LineWidth / scale);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // 以屏幕像素定大小：抵消画布缩放，镜头拉远时缩到七成
        float zoom = Mathf.Clamp(40f / Mathf.Max(1f, camera.orthographicSize), 0.7f, 1.15f);
        Vector3 pixelScale = Vector3.one * (zoom / scale);

        // 兵牌：同一地块同一国的军合并成一块，不同国并排
        Groups.Clear();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (corps.task == "reserve" && !CorpsSystem.IsAtWar(kingdom)) continue;
            TileZone zone = CorpsSystem.Zone(corps);
            if (zone?.centerTile == null) continue;
            var key = (zone.id, kingdom.id);
            if (!Groups.TryGetValue(key, out CounterGroup group))
            {
                group = new CounterGroup { Kingdom = kingdom, Zone = zone, World = zone.centerTile.posV, Task = corps.task };
                Groups[key] = group;
            }
            group.Count++;
            group.Strength += corps.strength;
            group.Full += corps.full;
            group.Org += corps.org;
            // 进攻优先显示，其次防守
            if (corps.task == "attack" || corps.task == "defend" && group.Task != "attack") group.Task = corps.task;
            Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
            if (group.Count == 1 && general?.data != null && !general.isRekt() && general.isAlive() &&
                general.current_tile?.zone != null && CorpsSystem.ZoneDistance(general.current_tile.zone, zone) <= 2f)
                group.World = (Vector3)general.current_position + new Vector3(0f, 3f);
        }
        StackCounts.Clear();
        foreach (CounterGroup group in Groups.Values)
        {
            StackCounts.TryGetValue(group.Zone.id, out int index);
            StackCounts[group.Zone.id] = index + 1;
            Vector3 screen = camera.WorldToScreenPoint(group.World);
            if (!OnScreen(screen, 60f)) continue;
            Counter counter = GetCounter(counterCount++);
            counter.Root.gameObject.SetActive(true);
            counter.Root.localScale = pixelScale;
            // 同一地块的第二国、第三国往右错开
            counter.Root.position = screen + new Vector3(index * (CounterWidth + 2f) * zoom, 0f);
            counter.Frame.color = group.Task switch
            {
                "attack" => FrameAttack,
                "defend" => FrameDefend,
                "retreat" => FrameRetreat,
                _ => FrameNormal
            };
            counter.Flag.color = group.Kingdom.getColor()?.getColorMain() ?? Color.gray;
            counter.Label.text = group.Count > 1
                ? $"{CorpsSystem.ShortStrength(group.Strength)}×{group.Count}"
                : CorpsSystem.ShortStrength(group.Strength);
            float strength = group.Full <= 0f ? 0f : Mathf.Clamp01(group.Strength / group.Full);
            float org = Mathf.Clamp01(group.Org / group.Count / 100f);
            Place(counter.Strength.rectTransform, Vector2.zero, Vector2.zero, new Vector2(12f, 3.5f),
                new Vector2(12f + 27f * strength, 5f));
            Place(counter.Org.rectTransform, Vector2.zero, Vector2.zero, new Vector2(12f, 1.5f),
                new Vector2(12f + 27f * org, 3f));
        }

        // 交战标记(画在交战地块下方，不压住兵牌)
        foreach (FrontSystem.BattleView battle in FrontSystem.Battles.Values)
        {
            TileZone zone = CorpsSystem.ZoneById(battle.zone_id);
            if (zone?.centerTile == null) continue;
            Vector3 screen = camera.WorldToScreenPoint(zone.centerTile.posV);
            if (!OnScreen(screen, 60f)) continue;
            Marker marker = GetMarker(markerCount++);
            marker.Root.gameObject.SetActive(true);
            marker.Root.localScale = pixelScale;
            marker.Root.position = screen - new Vector3(0f, (CounterHeight + 2f) * zoom);
            marker.Label.text = string.Format(LM.Get("corps_battle_marker"), Mathf.RoundToInt(battle.ratio * 100f));
        }
    }
}

[DefaultExecutionOrder(10001)]
public sealed class CorpsMapRendererHost : MonoBehaviour
{
    private void LateUpdate() => CorpsMapRenderer.HostLateUpdate();
}
