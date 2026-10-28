using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 世界地图的加载界面(用户 2026-10-28："加载地图的时候记得给个专门的加载界面""和进度条")：
// 生成期间全屏盖一层——时代名与年份、范围大小、时代说明，金色进度条，下面写当前阶段与百分比；生成完淡出。
// 进度取原版加载队列(SmoothLoader)已走的步数/总步数；摆城建国时队列会一批批往后加，进度只进不退
public class WorldMapLoadingScreen : MonoBehaviour
{
    private const string Gold = "#F3C34A";
    private static WorldMapLoadingScreen _instance;
    private Text _title, _subtitle, _description, _stage, _percent;
    private RectTransform _fill;
    private CanvasGroup _group;
    private float _progress;
    private string _stageKey = "world_map_stage_prepare";
    private bool _closing;
    // 点下生成后原版先播过场动画，加载队列这时还没开始；看到过加载才算开工
    private bool _sawLoading;
    // 加载队列走完后还要等进世界的安排(行政区、多京、殖民地、兵力、名人)做完再揭开(用户 2026-10-29："为什么地图不加载进去就已经是
    // 规划好行政区的，非要进去之后才开始……画")，最多等 AfterLoadSeconds
    private bool _markedDone;
    private float _doneAt;
    private const float AfterLoadSeconds = 30f;

    // 加载步骤 id(见 WorldMapGenerator、WorldMapScenario) → 阶段文字
    private static readonly Dictionary<string, string> Stages = new()
    {
        ["World Map: Clear"] = "world_map_stage_prepare",
        ["World Map: Size"] = "world_map_stage_prepare",
        ["World Map: Laws"] = "world_map_stage_prepare",
        ["World Map: Stats"] = "world_map_stage_prepare",
        ["World Map: Ages"] = "world_map_stage_prepare",
        ["World Map: Terrain"] = "world_map_stage_terrain",
        ["World Map: Rivers"] = "world_map_stage_terrain",
        ["World Map: Chunks"] = "world_map_stage_terrain",
        ["World Map: Vegetation"] = "world_map_stage_nature",
        ["World Map: Resources"] = "world_map_stage_nature",
        ["World Map: Clean Up"] = "world_map_stage_draw",
        ["World Map: Draw"] = "world_map_stage_draw",
        ["World Map: Sprites"] = "world_map_stage_draw",
        ["World Map: Finish"] = "world_map_stage_draw",
        ["World Map: GC"] = "world_map_stage_draw",
        ["World Map: Plan"] = "world_map_stage_plan",
        ["World Map: Cultures"] = "world_map_stage_culture",
        ["World Map: Realms"] = "world_map_stage_realms",
        ["World Map: Settle"] = "world_map_stage_settle",
        ["World Map: Territory"] = "world_map_stage_territory",
        ["World Map: Fill"] = "world_map_stage_fill",
        ["World Map: De Jure"] = "world_map_stage_dejure",
        ["World Map: Empires"] = "world_map_stage_empires",
        ["World Map: Tianxia"] = "world_map_stage_finish",
        ["World Map: Camera"] = "world_map_stage_finish",
        ["World Map: Done"] = "world_map_stage_finish"
    };

    public static void Show(WorldMapEra era, string frameName, string sizeName, Vector2Int size)
    {
        if (_instance == null)
        {
            var root = new GameObject("EmpireCraftWorldMapLoading");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<WorldMapLoadingScreen>();
            _instance.Build();
        }
        _instance.gameObject.SetActive(true);
        _instance._closing = false;
        _instance._sawLoading = false;
        _instance._markedDone = false;
        _instance._progress = 0f;
        _instance._stageKey = "world_map_stage_prepare";
        _instance._group.alpha = 1f;
        Font font = LocalizedTextManager.current_font;
        foreach (Text text in _instance.GetComponentsInChildren<Text>(true))
            if (font != null) text.font = font;
        _instance._title.text = $"<color={Gold}>{era.Name}</color>  {era.YearText}";
        _instance._subtitle.text = string.Format(LM.Get("world_map_loading_subtitle"), frameName, sizeName,
            size.x * 64, size.y * 64);
        _instance._description.text = era.Description;
        _instance.Refresh();
    }

    public static void Hide()
    {
        if (_instance != null) _instance._closing = true;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = true;

        Image backdrop = Panel(transform, "Backdrop", new Color(0.05f, 0.055f, 0.07f, 0.97f));
        Stretch(backdrop.rectTransform);

        Image frame = Panel(transform, "Frame", new Color(0.64f, 0.5f, 0.2f, 1f));
        Place(frame.rectTransform, new Vector2(0f, 10f), new Vector2(700f, 300f));
        Image card = Panel(frame.transform, "Card", new Color(0.09f, 0.095f, 0.11f, 1f));
        Place(card.rectTransform, Vector2.zero, new Vector2(694f, 294f));

        _title = Label(card.transform, "Title", 34, TextAnchor.MiddleCenter, new Vector2(0f, 105f), new Vector2(660f, 50f));
        _subtitle = Label(card.transform, "Subtitle", 17, TextAnchor.MiddleCenter, new Vector2(0f, 68f), new Vector2(660f, 26f));
        _subtitle.color = new Color(0.56f, 0.63f, 0.66f);
        _description = Label(card.transform, "Description", 16, TextAnchor.UpperLeft, new Vector2(0f, -2f), new Vector2(620f, 100f));
        _description.horizontalOverflow = HorizontalWrapMode.Wrap;
        _description.color = new Color(0.86f, 0.84f, 0.78f);

        Image track = Panel(card.transform, "Track", new Color(0.03f, 0.03f, 0.035f, 1f));
        Place(track.rectTransform, new Vector2(0f, -82f), new Vector2(600f, 16f));
        Image fill = Panel(track.transform, "Fill", new Color(0.95f, 0.76f, 0.29f, 1f));
        _fill = fill.rectTransform;
        _fill.anchorMin = new Vector2(0f, 0f);
        _fill.anchorMax = new Vector2(0f, 1f);
        _fill.pivot = new Vector2(0f, 0.5f);
        _fill.offsetMin = new Vector2(2f, 2f);
        _fill.offsetMax = new Vector2(-2f, -2f);

        _stage = Label(card.transform, "Stage", 16, TextAnchor.MiddleLeft, new Vector2(-40f, -112f), new Vector2(520f, 26f));
        _percent = Label(card.transform, "Percent", 16, TextAnchor.MiddleRight, new Vector2(240f, -112f), new Vector2(120f, 26f));
        _percent.color = new Color(0.95f, 0.76f, 0.29f);
    }

    private void Update()
    {
        if (_closing)
        {
            _progress = 1f;
            Refresh();
            _group.alpha -= Time.unscaledDeltaTime * 2.5f;
            if (_group.alpha <= 0f) gameObject.SetActive(false);
            return;
        }
        int count = SmoothLoader._actions?.Count ?? 0;
        if (count > 0) _progress = Mathf.Max(_progress, Mathf.Clamp01(SmoothLoader._index / (float)count));
        if (Stages.TryGetValue(SmoothLoader.latest_called_id ?? "", out string key)) _stageKey = key;
        // 生成流程走完(含原版收尾)，或中途出错被原版重开世界：先做进世界的安排，做完淡出
        if (SmoothLoader.isLoading()) _sawLoading = true;
        else if (_sawLoading)
        {
            if (!_markedDone)
            {
                _markedDone = true;
                _doneAt = Time.realtimeSinceStartup;
                WorldMapGenerator.MarkDone();
            }
            _stageKey = "world_map_stage_after";
            _progress = Mathf.Max(_progress, 0.97f);
            if (!WorldMapScenario.AfterLoadPending || Time.realtimeSinceStartup - _doneAt > AfterLoadSeconds) _closing = true;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (_fill == null) return;
        _fill.anchorMax = new Vector2(Mathf.Max(0.01f, _progress), 1f);
        _stage.text = LM.Get(_stageKey);
        _percent.text = Mathf.RoundToInt(_progress * 100f) + "%";
    }

    private static Image Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text Label(Transform parent, string name, int size, TextAnchor anchor, Vector2 position, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Place(text.rectTransform, position, box);
        return text;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
