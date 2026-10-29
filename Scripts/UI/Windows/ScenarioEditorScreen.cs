using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 剧本编辑器(第二、三期，见 Docs/plan-scenario-editor.md；用户 2026-10-30 "需要简单易懂，上手快""避免卡顿……地图的显示可以用像素图预览
// 不用真的加载进去地图，编辑法理和城市也可以直接在像素图里面做，就像是我之前写的全屏的那个城市编辑一样"，又要"法理首都的调整"
// "帝国核心法理首都")：全屏画布(仿 CityPlanScreen)，全程不生成世界——
//   左「剧本地图」：地表像素图(EarthTerrain.Preview，一像素 0.1°)，叠图层颜色(国家、王国法理、帝国法理、天下，从城往外铺陆地，
//     选中的那一块颜色深一些)与城点(都城大一号、选中的描金边)、长城折线与关隘、选中国家的关系连线；放大后写城名，法理图层写法理名。
//     滚轮以光标为中心缩放，右键、中键拖动平移(选「查看」时左键也拖)；Ctrl+Z 撤销；
//   工具(上一行)：查看；城市(点空地立城，拖城挪位，右键点城两下删城)；国家(点城选它和它的国家)；划城(点或拖过城划给选中的国家，
//     右键点城不再点名)；王国法理(点城选它的法理，按住 Shift 点或拖过城划进选中的法理，右键点城切换兼属)；帝国法理(点城选它所在的
//     帝国法理，按住 Shift 点城把那城的王国法理收进来)；关系(先点一国的城，再点另一国的城，按选的种类加上或去掉)；
//     长城(点一下加一个点，右键去掉最后一点)；关隘(点空处放关隘、点关隘选中，右键点关隘删掉)；
//     事件(剧情事件，第四期：右栏选事件、写名与史书里的话、开局后第几年起、一次或可重复，条件与结果一条条加，
//     要填国、城、地区的点「在地图上点」再点地图上的城；见 ScenarioEventSystem)；
//   图层(下一行)：地形、国家、王国法理、帝国法理、天下；撤销、看全图；
//   右栏随工具换：剧本、城、国家、王国法理(名、法理名、文化、首都、兼属)、帝国法理(名、首都、所属天下与天下名)、关系、长城、关隘；
//   上方：保存(写回 scenario.json，另出缩略图 preview.png)、关闭(有没保存的改动时五秒内再点一次才关)。
//   导入地图(第五期，见 WorldMapImporter)：剧本卡里「地图：地球 / 导入」切换(清空城与国家，可撤销)，导入时选颜色图或灰度高程、
//   海平面，「读入地图」读剧本文件夹里的 map.png(还没放图时显示一片海)，「打开剧本文件夹」；地形预览一像素一格。
//   笔刷与部落圈(用户 10-30 "笔刷工具……至少让我可以修改地块""画一个圈，里面就是相对文化的原始部落")：刷地块(国家图层刷给选中的
//   国家、王国法理图层刷进选中的法理，开局先归它；Shift+点取刷给谁，右键拖着擦)、刷地形(十六种地表，右键拖着复原)、部落(拖出一个圈，
//   开局在圈里立那种文化的原始部落；点圈选中，右键点圈删掉)；剧本卡里定地图范围(全图、半幅)，地图上画出框。
//   图层颜色按地形代价八方向铺开(水过不去、翻山贵，不再是菱形)，两块相接处描边；工具、图层按钮配原版与本模组的图标。
// 数据一侧见 ScenarioEditing。Esc 关闭(输入框里打字时不算)。
public static class ScenarioEditorScreen
{
    private enum Tool { View, City, Realm, Paint, Land, Kingdom, Empire, Relation, Wall, Pass, Event, Tribe, Terrain }
    private enum Layer { Terrain, Realm, Kingdom, Empire, Tianxia }

    private const float RefWidth = 1600f, RefHeight = 900f;
    private const float HeaderHeight = 46f, StatusHeight = 30f, Margin = 10f, SideWidth = 340f, ToolbarHeight = 72f;
    private const float ConfirmSeconds = 5f, MessageSeconds = 6f, ClickSlop = 6f;
    // 地形预览每几格取一格(网格 0.05°，取 2 即 0.1°)；图层颜色再粗一倍；从城往外铺至多几格(粗格)
    private const int Stride = 2, FillFactor = 2;
    // 笔刷粗细(度)、部落圈至少多大(度)、拖着刷时图层颜色至多几秒重铺一次
    private static readonly float[] BrushSizes = { 0.2f, 0.5f, 1f, 2f, 4f };
    private const float MinTribeRadius = 0.3f, FillThrottle = 0.2f;
    // 铺图层颜色：各地形大类走一格的代价(见 EarthTerrain.ClassOf；水 0 为过不去)，各等城能铺多远(大城远)
    private static readonly int[] ClassCost = { 0, 10, 13, 20, 45, 70, 14, 30 };
    private static readonly int[] RankBudget = { 160, 160, 130, 100 };
    private const int MaxBudget = 160;
    private static readonly int[] DirX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DirY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    // 刷地形的十六种地表：写法、原版图标、文本键
    private static readonly (string type, string icon, string name)[] TerrainPalette =
    {
        ("deep_ocean", "iconTileDeepOcean", "deep_ocean"), ("close_ocean", "iconTileCloseOcean", "close_ocean"),
        ("shallow_waters", "iconTileShallowWater", "shallow"), ("sand", "iconTileSand", "sand"),
        ("soil_low:grass_low", "iconTileSoil", "grass"), ("soil_high:grass_high", "iconTileHighSoil", "grass_high"),
        ("soil_low:savanna_low", "iconSeedSavanna", "savanna"), ("soil_low:jungle_low", "iconSeedJungle", "jungle"),
        ("soil_low:desert_low", "iconSeedDesert", "desert"), ("soil_low:permafrost_low", "iconSeedPermafrost", "permafrost"),
        ("soil_low:swamp_low", "iconTileSwamp", "swamp"), ("soil_low:birch_low", "iconTileForest", "forest"),
        ("hills", "iconTileHills", "hills"), ("mountains", "iconTileMountains", "mountains"),
        ("hills:snow_hills", "iconSnow", "snow_hills"), ("mountains:snow_block", "iconTileSummit", "snow_mountains")
    };

    // 工具、图层按钮的图标(原版 ui/icons 与本模组 GameResources)
    private static readonly Dictionary<Tool, string> ToolIcons = new()
    {
        [Tool.View] = "ui/icons/iconCityInspect", [Tool.City] = "ui/icons/godpowers/place_city", [Tool.Realm] = "ui/icons/iconKingdom",
        [Tool.Paint] = "ui/icons/godpowers/assign_settlement", [Tool.Land] = "ui/icons/iconBorderBrush",
        [Tool.Kingdom] = "ui/icons/iconToolTitleLayer", [Tool.Empire] = "ui/icons/iconToolCoreLayer", [Tool.Relation] = "ui/icons/iconDiplomacy",
        [Tool.Wall] = "ui/icons/iconWallOrder", [Tool.Pass] = "ui/icons/godpowers/place_fort", [Tool.Event] = "ui/icons/iconWorldLog",
        [Tool.Tribe] = "ui/icons/godpowers/place_village", [Tool.Terrain] = "ui/icons/iconTileHills"
    };

    private static readonly Dictionary<Layer, string> LayerIcons = new()
    {
        [Layer.Terrain] = "ui/icons/iconTileMountains", [Layer.Realm] = "ui/icons/iconNationLayer", [Layer.Kingdom] = "ui/icons/iconTitleLayer",
        [Layer.Empire] = "ui/icons/godpowers/empire_layer", [Layer.Tianxia] = "ui/icons/godpowers/tianxia_layer"
    };
    // 城点在屏幕上约多大(画布单位)；放大到一格多少单位以上写城名、至多写几个；最大放大到一格多少单位
    private const float MarkerScreen = 7f, LabelZoom = 2.2f, MaxZoom = 12f;
    private const int MaxLabels = 140, MaxAreaLabels = 80;

    private static readonly Color Backdrop = new Color(0.06f, 0.07f, 0.07f, 0.94f);
    private static readonly Color PanelColor = new Color(0.14f, 0.15f, 0.14f, 0.97f);
    private static readonly Color FrameColor = new Color(0.78f, 0.62f, 0.26f, 0.9f);
    private static readonly Color MapBack = new Color(0.04f, 0.05f, 0.04f, 1f);
    private static readonly Color StatusBack = new Color(0.1f, 0.11f, 0.1f, 0.97f);
    private static readonly Color TipBack = new Color(0.03f, 0.03f, 0.03f, 0.9f);
    private static readonly Color InputBack = new Color(0.07f, 0.08f, 0.08f, 1f);
    private static readonly Color ToolColor = new Color(0.66f, 0.2f, 0.16f, 1f);
    private static readonly Color ActiveColor = new Color(0.86f, 0.58f, 0.22f, 1f);
    private static readonly Color ViewColor = new Color(0.23f, 0.35f, 0.47f, 1f);
    private static readonly Color SaveColor = new Color(0.33f, 0.43f, 0.2f, 1f);
    private static readonly Color DangerColor = new Color(0.5f, 0.18f, 0.16f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.93f, 0.89f, 1f);
    private static readonly Color DimText = new Color(0.56f, 0.63f, 0.66f, 1f);
    private static readonly Color TitleColor = new Color(0.95f, 0.84f, 0.58f, 1f);
    private static readonly Color HeadColor = new Color(0.55f, 0.84f, 0.56f, 1f);
    private static readonly Color CyanText = new Color(0.5f, 0.85f, 0.92f, 1f);
    private static readonly Color AreaText = new Color(1f, 0.95f, 0.75f, 1f);
    private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    private static readonly Color32 Edge = new Color32(20, 20, 20, 255), Gold = new Color32(255, 214, 90, 255);
    private static readonly Color32 Independent = new Color32(235, 235, 235, 255), Inactive = new Color32(120, 120, 120, 160);
    private static readonly Color32 WallColor = new Color32(120, 70, 40, 255), PassColor = new Color32(250, 235, 180, 255);
    private static readonly Color32 RegionCapital = new Color32(90, 230, 255, 255), CoreCapital = new Color32(255, 110, 230, 255);
    private static readonly Color32 FrameRect = new Color32(255, 236, 160, 255);

    private sealed class Btn
    {
        public Button Button;
        public Image Image;
        public Text Label;

        public void Set(string text, Color color, bool interactable = true)
        {
            if (text != null && Label.text != text) Label.text = text;
            Image.color = color;
            Button.interactable = interactable;
            Label.color = interactable ? TextColor : DimText;
        }
    }

    private static GameObject _root;
    private static RectTransform _canvasRect, _mapArea, _viewport, _labels, _areaLabels, _tip;
    private static RawImage _map, _fillImage, _marksImage;
    private static Text _title, _subtitle, _status, _tipText;
    private static Text _scenarioInfo, _cityInfo, _realmInfo, _regionInfo, _coreInfo, _relationInfo, _wallInfo, _passInfo;
    private static readonly List<(Tool tool, Btn button)> ToolButtons = new();
    private static readonly List<(Layer layer, Btn button)> LayerButtons = new();
    private static Btn _closeBtn, _saveBtn, _undoBtn;
    private static Btn _rankBtn, _capitalBtn, _foundBtn, _deleteCityBtn, _cultureBtn, _empireBtn, _deleteRealmBtn;
    private static Btn _regionCultureBtn, _regionCapitalBtn, _shareBtn, _newRegionBtn, _deleteRegionBtn;
    private static Btn _coreCapitalBtn, _newCoreBtn, _leaveCoreBtn, _deleteCoreBtn, _tianxiaBtn;
    private static Btn _kindBtn, _goalBtn, _wallBtn;
    private static RectTransform _goalRow;
    private static InputField _nameInput, _yearInput, _authorInput, _descInput, _cityNameInput, _realmNameInput, _famInput, _givInput, _ageInput;
    private static InputField _regionNameInput, _regionTitleInput, _coreNameInput, _tianxiaNameInput, _wallNameInput, _passNameInput;
    private static RectTransform _scenarioCard, _cityCard, _realmCard, _regionCard, _coreCard, _relationCard, _wallCard, _passCard;
    // 剧情事件：选哪条、右栏里随事件重搭的那一块；在地图上点城填的是哪条条件或结果的哪一项
    private static RectTransform _eventCard, _eventBody;
    private static Btn _eventBtn;
    private static int _eventIndex = -1, _pickStep = -1;
    private static bool _pickEffect;
    private static string _pickSlot;
    private static readonly List<Text> LabelPool = new(), AreaPool = new();
    private static Btn _mapBtn, _importModeBtn;
    private static InputField _seaInput;
    private static RectTransform _importBox;
    private static Text _importInfo;
    private static float _importArmedAt = -100f;
    private static Btn _frameBtn, _landBrushBtn, _terrainBrushBtn, _tribeCultureBtn, _tribeCountBtn, _clearLandBtn, _clearTerrainBtn, _deleteTribeBtn;
    private static RectTransform _landCard, _terrainCard, _tribeCard;
    private static Text _landInfo, _terrainInfo, _tribeInfo;
    private static readonly List<(string type, Btn button)> TerrainButtons = new();
    private static int _brushSize = 2, _terrainType = 4, _tribeIndex = -1, _brushButton = -1, _pressTribe = -1, _hoverTribe = -1, _terrainApplied = -1;
    private static Vector2 _brushLast, _pressTexel;
    private static float _fillAt = -100f, _terrainUploadAt = -100f, _clearArmedAt = -100f;
    private static bool _terrainDirty;
    private static string _clearArmed;
    private static RawImage _ring;
    private static Texture2D _ringTexture;
    private static readonly HashSet<int> BrushCells = new();
    private static readonly List<int> BrushTexels = new();
    // 铺图层颜色复用的数组；FillKeys 为各块的键(国家 p:、王国法理 r:、帝国法理 k:、天下 t:)
    private static int[] _fillOwner, _fillDist, _fillBudget;
    private static bool[] _fillFixed;
    private static List<int>[] _buckets;
    private static readonly List<string> FillKeys = new();

    private static Tool _tool = Tool.City;
    private static Layer _layer = Layer.Realm;
    private static string _cityId, _realmId, _regionId, _coreId, _relationFrom, _message;
    private static int _kind, _goal, _wallIndex = -1, _passIndex = -1;
    private static float _messageAt = -100f, _closeArmedAt = -100f, _deleteCityArmedAt = -100f, _deleteRealmArmedAt = -100f;
    private static string _deleteArmedCity, _lastStroked;

    // 地形预览(按地表网格烘一次，关掉编辑器也留着)：像素、陆地、宽高、左下角经纬度、每像素几度
    private static Color32[] _terrainPixels;
    private static bool[] _land;
    private static int _w, _h;
    private static float _lon0, _lat0, _step;
    private static Texture2D _terrain, _fill, _marks;
    private static Color32[] _fillPixels, _markPixels;
    private static int _fillKey = int.MinValue, _marksKey = int.MinValue, _areaKey = int.MinValue;
    private static readonly List<(string text, Vector2 at)> Areas = new();

    private static Vector2 _center;
    private static float _zoom = 1f;
    private static int _autoFit;
    private static int _dragButton = -1;
    private static Vector2 _dragLast, _pressAt;
    private static bool _pressed, _rightPressed, _moving, _stroking;
    private static string _pressCity, _hoverCity;
    private static int _pressPass = -1, _hoverPass = -1;

    public static bool IsOpen => _root != null;

    public static void Open(WorldMapEra custom)
    {
        Close(true);
        if (!ScenarioEditing.Open(custom))
        {
            WorldTip.showNow("scenario_editor_open_failed", true, "top", 3f);
            return;
        }
        try
        {
            if (!EnsureTerrain())
            {
                WorldTip.showNow("world_map_data_missing", true, "top", 3f);
                ScenarioEditing.Close();
                return;
            }
            _cityId = _realmId = _regionId = _coreId = _relationFrom = null;
            _wallIndex = _passIndex = _tribeIndex = -1;
            _tool = Tool.City;
            _layer = Layer.Realm;
            Build();
            _autoFit = 3;
            RefreshAll();
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 剧本编辑器打开失败: {exception}");
            Close(true);
        }
    }

    public static void Close(bool force = false)
    {
        if (!force && ScenarioEditing.Dirty && Time.unscaledTime - _closeArmedAt > ConfirmSeconds)
        {
            _closeArmedAt = Time.unscaledTime;
            Say(LM.Get("scenario_editor_close_confirm"));
            RefreshButtons();
            return;
        }
        if (_root != null) Object.Destroy(_root);
        if (_workTexture != null) Object.Destroy(_workTexture);
        if (_ringTexture != null) Object.Destroy(_ringTexture);
        _workTexture = _ringTexture = null;
        _ring = null;
        _terrainPixels = _basePixels = null;
        _land = _baseLand = null;
        _classes = _baseClasses = null;
        _fillOwner = _fillDist = _fillBudget = null;
        _fillFixed = null;
        _brushButton = -1;
        TerrainButtons.Clear();
        FillKeys.Clear();
        EarthTerrain.UseEarth();
        if (_fill != null) Object.Destroy(_fill);
        if (_marks != null) Object.Destroy(_marks);
        _root = null;
        _fill = _marks = null;
        _map = _fillImage = _marksImage = null;
        _fillPixels = _markPixels = null;
        _fillKey = _marksKey = _areaKey = int.MinValue;
        _dragButton = -1;
        _pressed = _rightPressed = _moving = _stroking = false;
        _message = null;
        _closeArmedAt = -100f;
        LabelPool.Clear();
        AreaPool.Clear();
        ToolButtons.Clear();
        LayerButtons.Clear();
        ScenarioEditing.Close();
    }

    // 地形底图：地球的烘一次留着，导入地图的每次按剧本读；工作图 = 底图叠上刷的地形(见 ApplyTerrainPaint)
    private static Texture2D _workTexture;
    private static Color32[] _earthPixels, _basePixels;
    private static bool[] _earthLand, _baseLand;
    private static byte[] _earthClasses, _baseClasses, _classes;
    private static int _earthW, _earthH;
    private static float _earthLon0, _earthLat0, _earthStep;
    private static string _importError;

    private static bool EnsureTerrain()
    {
        _importError = null;
        if (ScenarioEditing.UsesImport)
        {
            WorldMapImporter.Loaded loaded = WorldMapImporter.Load(ScenarioEditing.Era, out _importError) ?? WorldMapImporter.Blank();
            EarthTerrain.UseGrid(loaded.grid, loaded.cols, loaded.rows, WorldMapImporter.Step);
            if (!EarthTerrain.Preview(1, out _basePixels, out _baseLand, out _w, out _h, out _lon0, out _lat0, out _step)) return false;
            _baseClasses = EarthTerrain.PreviewClasses(1);
        }
        else
        {
            EarthTerrain.UseEarth();
            if (_earthPixels == null || _earthClasses == null)
            {
                if (!EarthTerrain.Preview(Stride, out _earthPixels, out _earthLand, out _earthW, out _earthH, out _earthLon0, out _earthLat0,
                        out _earthStep)) return false;
                _earthClasses = EarthTerrain.PreviewClasses(Stride);
            }
            _basePixels = _earthPixels;
            _baseLand = _earthLand;
            _baseClasses = _earthClasses;
            _w = _earthW;
            _h = _earthH;
            _lon0 = _earthLon0;
            _lat0 = _earthLat0;
            _step = _earthStep;
        }
        if (_basePixels == null || _baseClasses == null) return false;
        if (_workTexture == null || _workTexture.width != _w || _workTexture.height != _h)
        {
            if (_workTexture != null) Object.Destroy(_workTexture);
            _workTexture = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        _terrain = _workTexture;
        ApplyTerrainPaint();
        return true;
    }

    // 底图叠上刷的地形，整张重传(打开、撤销、换地图时；拖着刷时只改刷到的像素，见 BrushAt)
    private static void ApplyTerrainPaint()
    {
        if (_basePixels == null || _workTexture == null) return;
        _terrainPixels = (Color32[])_basePixels.Clone();
        _land = (bool[])_baseLand.Clone();
        _classes = (byte[])_baseClasses.Clone();
        int offX = Mathf.FloorToInt(_lon0 / ScenarioCells.TerrainStep + 0.5f * _step / ScenarioCells.TerrainStep);
        int offY = Mathf.FloorToInt(_lat0 / ScenarioCells.TerrainStep + 0.5f * _step / ScenarioCells.TerrainStep);
        foreach (KeyValuePair<int, string> pair in ScenarioEditing.TerrainCells)
        {
            ScenarioCells.Unpack(pair.Key, out int i, out int j);
            int x = i - offX, y = j - offY;
            if (x >= 0 && y >= 0 && x < _w && y < _h) SetTexel(y * _w + x, pair.Value);
        }
        _workTexture.SetPixels32(_terrainPixels);
        _workTexture.Apply();
        _terrainApplied = ScenarioEditing.TerrainRevision;
        _terrainDirty = false;
        _fillKey = int.MinValue;
    }

    // 一个像素换成某种地表(type 为空就复原底图)
    private static void SetTexel(int index, string type)
    {
        if (type == null)
        {
            _terrainPixels[index] = _basePixels[index];
            _land[index] = _baseLand[index];
            _classes[index] = _baseClasses[index];
            return;
        }
        _terrainPixels[index] = EarthTerrain.PreviewColorOf(type);
        _classes[index] = EarthTerrain.ClassOf(type);
        _land[index] = _classes[index] != 0;
    }

    private static void UploadTerrain()
    {
        _workTexture.SetPixels32(_terrainPixels);
        _workTexture.Apply();
        _terrainDirty = false;
        _terrainUploadAt = Time.unscaledTime;
    }

    // 换了地图(地球/导入、读法、重读 map.png)：地形、图层、城点三张图照新尺寸重做，看全图
    private static void ReloadTerrain()
    {
        if (_map == null || !EnsureTerrain()) return;
        _map.texture = _terrain;
        MakeOverlays();
        _fillKey = _marksKey = _areaKey = int.MinValue;
        Fit();
    }

    private static void MakeOverlays()
    {
        if (_fill != null) Object.Destroy(_fill);
        if (_marks != null) Object.Destroy(_marks);
        _fill = new Texture2D(_w / FillFactor, _h / FillFactor, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        _fillPixels = new Color32[_fill.width * _fill.height];
        _fillImage.texture = _fill;
        _marks = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        _markPixels = new Color32[_w * _h];
        _marksImage.texture = _marks;
    }

    #region 搭界面

    private static void Build()
    {
        _root = new GameObject("EmpireCraftScenarioEditor", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster), typeof(ScenarioEditorHost));
        Object.DontDestroyOnLoad(_root);
        Canvas canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        _canvasRect = (RectTransform)_root.transform;

        RectTransform backdrop = Child("Backdrop", _canvasRect);
        Place(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Fill(backdrop, Backdrop, true);

        RectTransform header = Child("Header", _canvasRect);
        Place(header, new Vector2(0f, 1f), Vector2.one, new Vector2(Margin, -HeaderHeight), new Vector2(-Margin, 0f));
        Row(header, 18f, new RectOffset(6, 0, 6, 6));
        _title = Label(header, "Title", 24, TitleColor, TextAnchor.MiddleLeft);
        _subtitle = Label(header, "Subtitle", 16, CyanText, TextAnchor.MiddleLeft);
        Spacer(header);
        _saveBtn = MakeButton(header, LM.Get("scenario_editor_save"), SaveColor, Save, 16, 32f);
        _closeBtn = MakeButton(header, LM.Get("scenario_editor_close"), ViewColor, () => Close(), 16, 32f);

        RectTransform status = Child("Status", _canvasRect);
        Place(status, Vector2.zero, new Vector2(1f, 0f), new Vector2(Margin, 4f), new Vector2(-Margin, 4f + StatusHeight));
        Fill(status, StatusBack);
        _status = Label(status, "Text", 15, TextColor, TextAnchor.MiddleLeft);
        Place(_status.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));

        RectTransform body = Child("Body", _canvasRect);
        Place(body, Vector2.zero, Vector2.one, new Vector2(Margin, StatusHeight + 12f), new Vector2(-Margin, -HeaderHeight));
        BuildMap(body);
        BuildSide(body);

        _tip = Child("Tip", _canvasRect);
        _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f);
        _tip.pivot = new Vector2(0f, 1f);
        Fill(_tip, TipBack);
        Row(_tip, 0f, new RectOffset(8, 8, 4, 4));
        var fitter = _tip.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _tipText = Label(_tip, "Text", 15, TextColor, TextAnchor.MiddleLeft);
        _tip.gameObject.SetActive(false);
    }

    private static void BuildMap(RectTransform body)
    {
        _mapArea = Child("MapArea", body);
        Place(_mapArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-(SideWidth + Margin), 0f));
        Fill(_mapArea, PanelColor);
        Frame(_mapArea);

        RectTransform tools = Child("Tools", _mapArea);
        Place(tools, new Vector2(0f, 1f), Vector2.one, new Vector2(8f, -38f), new Vector2(-8f, -6f));
        Row(tools, 5f, new RectOffset(0, 0, 0, 0));
        RowTitle(tools, LM.Get("scenario_editor_row_tools"));
        foreach (Tool tool in new[]
                 {
                     Tool.View, Tool.City, Tool.Realm, Tool.Paint, Tool.Land, Tool.Kingdom, Tool.Empire, Tool.Relation, Tool.Wall, Tool.Pass,
                     Tool.Event, Tool.Tribe, Tool.Terrain
                 })
        {
            Tool choice = tool;
            ToolButtons.Add((choice, MakeButton(tools, LM.Get("scenario_editor_tool_" + choice.ToString().ToLowerInvariant()), ToolColor,
                () => SetTool(choice), 13, 28f, Icon(ToolIcons[choice]))));
        }

        RectTransform layers = Child("Layers", _mapArea);
        Place(layers, new Vector2(0f, 1f), Vector2.one, new Vector2(8f, -ToolbarHeight), new Vector2(-8f, -40f));
        Row(layers, 5f, new RectOffset(0, 0, 0, 0));
        RowTitle(layers, LM.Get("scenario_editor_row_layers"));
        foreach (Layer layer in new[] { Layer.Terrain, Layer.Realm, Layer.Kingdom, Layer.Empire, Layer.Tianxia })
        {
            Layer choice = layer;
            LayerButtons.Add((choice, MakeButton(layers, LM.Get("scenario_editor_layer_" + choice.ToString().ToLowerInvariant()), ViewColor,
                () => SetLayer(choice), 13, 28f, Icon(LayerIcons[choice]))));
        }
        Spacer(layers);
        _undoBtn = MakeButton(layers, LM.Get("scenario_editor_undo"), ViewColor, Undo, 13, 28f, Icon("ui/icons/iconGoBack"));
        MakeButton(layers, LM.Get("scenario_editor_fit"), ViewColor, Fit, 13, 28f, Icon("ui/icons/iconMapWidth"));

        _viewport = Child("Viewport", _mapArea);
        Place(_viewport, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -(ToolbarHeight + 6f)));
        Fill(_viewport, MapBack, true);
        _viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform preview = Child("Preview", _viewport);
        preview.anchorMin = preview.anchorMax = preview.pivot = new Vector2(0.5f, 0.5f);
        _map = preview.gameObject.AddComponent<RawImage>();
        _map.raycastTarget = false;
        _map.texture = _terrain;
        RectTransform fill = Child("Fill", preview);
        Place(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _fillImage = fill.gameObject.AddComponent<RawImage>();
        _fillImage.raycastTarget = false;
        RectTransform marks = Child("Marks", preview);
        Place(marks, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _marksImage = marks.gameObject.AddComponent<RawImage>();
        _marksImage.raycastTarget = false;
        // 笔刷圈与正拖出的部落圈(跟着光标走，不进城点那张图)
        RectTransform ring = Child("Ring", _viewport);
        ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(0.5f, 0.5f);
        _ring = ring.gameObject.AddComponent<RawImage>();
        _ring.raycastTarget = false;
        _ring.texture = RingTexture();
        ring.gameObject.SetActive(false);
        _areaLabels = Child("AreaLabels", _viewport);
        Place(_areaLabels, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _labels = Child("Labels", _viewport);
        Place(_labels, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        MakeOverlays();
    }

    private static void BuildSide(RectTransform body)
    {
        // 右栏可上下滚(事件写长了一屏放不下)：滚动框 → 视口(裁切) → 内容(随卡片撑高)
        RectTransform frame = Child("SideScroll", body);
        Place(frame, new Vector2(1f, 0f), Vector2.one, new Vector2(-SideWidth, 0f), Vector2.zero);
        var scroll = frame.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        RectTransform viewport = Child("Viewport", frame);
        Place(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Fill(viewport, new Color(0f, 0f, 0f, 0.001f), true);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform side = Child("Side", viewport);
        side.anchorMin = new Vector2(0f, 1f);
        side.anchorMax = Vector2.one;
        side.pivot = new Vector2(0.5f, 1f);
        side.offsetMin = side.offsetMax = Vector2.zero;
        side.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = side;
        var column = side.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = 8f;
        column.childAlignment = TextAnchor.UpperCenter;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        _scenarioCard = Card(side, LM.Get("scenario_editor_card_scenario"));
        _nameInput = Field(_scenarioCard, LM.Get("scenario_editor_name"), ScenarioEditing.SetName);
        _yearInput = Field(_scenarioCard, LM.Get("scenario_editor_year"), ScenarioEditing.SetYear);
        _authorInput = Field(_scenarioCard, LM.Get("scenario_editor_author"), ScenarioEditing.SetAuthor);
        _descInput = Field(_scenarioCard, LM.Get("scenario_editor_desc"), ScenarioEditing.SetDescription, 52f, true);
        _scenarioInfo = Label(_scenarioCard, "Info", 14, DimText, TextAnchor.UpperLeft, true);
        // 地图范围(生成时默认选这档，地图上画出框)
        _frameBtn = Cycler(_scenarioCard, LM.Get("scenario_editor_frame"), ScenarioEditing.CycleFrame);
        // 地图：地球 / 导入(map.png)；导入时：颜色图或灰度高程、海平面、读入地图、打开剧本文件夹
        _mapBtn = MakeButton(_scenarioCard, "", ToolColor, ToggleImport);
        _importBox = Child("Import", _scenarioCard);
        var importLayout = _importBox.gameObject.AddComponent<VerticalLayoutGroup>();
        importLayout.spacing = 5f;
        importLayout.childControlWidth = importLayout.childControlHeight = true;
        importLayout.childForceExpandWidth = true;
        importLayout.childForceExpandHeight = false;
        _importModeBtn = Cycler(_importBox, LM.Get("scenario_editor_import_mode"), _ =>
        {
            ScenarioEditing.CycleImportMode();
            ReloadTerrain();
        });
        _seaInput = Field(_importBox, LM.Get("scenario_editor_import_sea"), text =>
        {
            ScenarioEditing.SetImportSea(text);
            ReloadTerrain();
        });
        RectTransform importActions = Columns(_importBox);
        MakeButton(importActions, LM.Get("scenario_editor_import_reload"), ToolColor, ReloadTerrain);
        MakeButton(importActions, LM.Get("world_map_custom_folder"), ViewColor, OpenFolder);
        _importInfo = Label(_importBox, "Info", 14, DimText, TextAnchor.UpperLeft, true);

        _cityCard = Card(side, LM.Get("scenario_editor_card_city"));
        _cityNameInput = Field(_cityCard, LM.Get("scenario_editor_name"), text =>
        {
            if (_cityId != null) ScenarioEditing.RenameCity(_cityId, text);
        });
        _cityInfo = Label(_cityCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform cityActions = Columns(_cityCard);
        _rankBtn = MakeButton(cityActions, "", ToolColor, () =>
        {
            if (_cityId != null) ScenarioEditing.CycleRank(_cityId);
        });
        _capitalBtn = MakeButton(cityActions, LM.Get("scenario_editor_set_capital"), ToolColor, () =>
        {
            WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
            if (polity != null && _cityId != null) Say(ScenarioEditing.SetCapital(polity, _cityId));
        });
        _foundBtn = MakeButton(cityActions, LM.Get("scenario_editor_found"), ToolColor, () =>
        {
            if (_cityId == null) return;
            WorldMapPolity polity = ScenarioEditing.NewPolity(_cityId, out string message);
            if (polity != null) _realmId = polity.id;
            Say(message);
        });
        _deleteCityBtn = MakeButton(cityActions, LM.Get("scenario_editor_delete_city"), DangerColor, () => ArmDeleteCity(_cityId));

        _realmCard = Card(side, LM.Get("scenario_editor_card_realm"));
        _realmNameInput = Field(_realmCard, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenamePolity(ScenarioEditing.Polity(_realmId), text));
        _cultureBtn = Cycler(_realmCard, LM.Get("scenario_editor_culture"), step => ScenarioEditing.CycleCulture(ScenarioEditing.Polity(_realmId), step));
        _empireBtn = MakeButton(_realmCard, "", ToolColor, () => ScenarioEditing.ToggleEmpire(ScenarioEditing.Polity(_realmId)));
        _famInput = Field(_realmCard, LM.Get("scenario_editor_ruler_family"), text => ScenarioEditing.SetRuler(ScenarioEditing.Polity(_realmId), text, null, null));
        _givInput = Field(_realmCard, LM.Get("scenario_editor_ruler_given"), text => ScenarioEditing.SetRuler(ScenarioEditing.Polity(_realmId), null, text, null));
        _ageInput = Field(_realmCard, LM.Get("scenario_editor_ruler_age"), text => ScenarioEditing.SetRuler(ScenarioEditing.Polity(_realmId), null, null, text));
        _realmInfo = Label(_realmCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _deleteRealmBtn = MakeButton(_realmCard, LM.Get("scenario_editor_delete_realm"), DangerColor, ArmDeleteRealm);

        // 王国法理：地区名、法理名、文化、首都、兼属
        _regionCard = Card(side, LM.Get("scenario_editor_card_region"));
        _regionNameInput = Field(_regionCard, LM.Get("scenario_editor_region_name"), text => ScenarioEditing.RenameRegion(_regionId, text));
        _regionTitleInput = Field(_regionCard, LM.Get("scenario_editor_region_title"), text => ScenarioEditing.SetRegionTitle(_regionId, text));
        _regionCultureBtn = Cycler(_regionCard, LM.Get("scenario_editor_culture"), step => ScenarioEditing.CycleRegionCulture(_regionId, step));
        _regionInfo = Label(_regionCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform regionActions = Columns(_regionCard);
        _regionCapitalBtn = MakeButton(regionActions, LM.Get("scenario_editor_title_capital"), ToolColor,
            () => Say(ScenarioEditing.SetRegionCapital(_regionId, _cityId)));
        _shareBtn = MakeButton(regionActions, LM.Get("scenario_editor_share"), ToolColor, () => Say(ScenarioEditing.ToggleShare(_cityId, _regionId)));
        _newRegionBtn = MakeButton(regionActions, LM.Get("scenario_editor_new_region_from_city"), ToolColor, () =>
        {
            string id = ScenarioEditing.NewRegion(_cityId);
            if (id != null)
            {
                _regionId = id;
                Say(LM.Get("scenario_editor_region_created"));
            }
        });
        _deleteRegionBtn = MakeButton(regionActions, LM.Get("scenario_editor_delete_region"), DangerColor,
            () => Say(ScenarioEditing.DeleteRegion(_regionId)));

        // 帝国法理：名、首都、收的王国法理、所属天下与天下名
        _coreCard = Card(side, LM.Get("scenario_editor_card_core"));
        _coreNameInput = Field(_coreCard, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenameCore(_coreId, text));
        _coreInfo = Label(_coreCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform coreActions = Columns(_coreCard);
        _coreCapitalBtn = MakeButton(coreActions, LM.Get("scenario_editor_title_capital"), ToolColor,
            () => Say(ScenarioEditing.SetCoreCapital(_coreId, _cityId)));
        _newCoreBtn = MakeButton(coreActions, LM.Get("scenario_editor_new_core"), ToolColor, () =>
        {
            string id = ScenarioEditing.NewCore(_regionId);
            if (id != null)
            {
                _coreId = id;
                Say(LM.Get("scenario_editor_core_created"));
            }
        });
        _leaveCoreBtn = MakeButton(coreActions, LM.Get("scenario_editor_leave_core"), ToolColor, () => Say(ScenarioEditing.RemoveRegionFromCore(_regionId)));
        _deleteCoreBtn = MakeButton(coreActions, LM.Get("scenario_editor_delete_core"), DangerColor, () =>
        {
            ScenarioEditing.DeleteCore(_coreId);
            _coreId = null;
        });
        _tianxiaBtn = Cycler(_coreCard, LM.Get("scenario_editor_tianxia"), step => ScenarioEditing.CycleCoreTianxia(_coreId, step));
        RectTransform tianxiaRow = Child("TianxiaRow", _coreCard);
        Row(tianxiaRow, 6f, new RectOffset(0, 0, 0, 0));
        _tianxiaNameInput = Field(tianxiaRow, LM.Get("scenario_editor_tianxia_name"),
            text => ScenarioEditing.RenameTianxia(ScenarioEditing.Core(_coreId)?.group, text));
        MakeButton(tianxiaRow, LM.Get("scenario_editor_new_tianxia"), ToolColor, () => ScenarioEditing.NewTianxia(_coreId), 14, 28f);

        // 关系：种类、战争目标、起始的国家与它的关系
        _relationCard = Card(side, LM.Get("scenario_editor_card_relation"));
        _kindBtn = Cycler(_relationCard, LM.Get("scenario_editor_kind"), step =>
            _kind = (_kind + step + ScenarioEditing.RelationKinds.Length) % ScenarioEditing.RelationKinds.Length);
        _goalBtn = Cycler(_relationCard, LM.Get("scenario_editor_goal"), step =>
            _goal = (_goal + step + ScenarioEditing.WarGoals.Length) % ScenarioEditing.WarGoals.Length);
        _goalRow = (RectTransform)_goalBtn.Button.transform.parent;
        _relationInfo = Label(_relationCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        MakeButton(_relationCard, LM.Get("scenario_editor_relation_clear"), ViewColor, () => _relationFrom = null);

        // 长城：哪一道、名、几个点；新长城、去掉最后一点、删长城
        _wallCard = Card(side, LM.Get("scenario_editor_card_wall"));
        _wallBtn = Cycler(_wallCard, LM.Get("scenario_editor_wall_pick"), step =>
        {
            int count = ScenarioEditing.Walls.Count;
            if (count > 0) _wallIndex = ((_wallIndex < 0 ? 0 : _wallIndex + step) % count + count) % count;
        });
        _wallNameInput = Field(_wallCard, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenameWall(_wallIndex, text));
        _wallInfo = Label(_wallCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform wallActions = Columns(_wallCard);
        MakeButton(wallActions, LM.Get("scenario_editor_new_wall_btn"), ToolColor, () => _wallIndex = ScenarioEditing.NewWall());
        MakeButton(wallActions, LM.Get("scenario_editor_wall_pop"), ToolColor, () => ScenarioEditing.RemoveWallPoint(_wallIndex));
        MakeButton(wallActions, LM.Get("scenario_editor_delete_wall"), DangerColor, () =>
        {
            ScenarioEditing.DeleteWall(_wallIndex);
            _wallIndex = ScenarioEditing.Walls.Count - 1;
        });

        // 剧情事件：选哪条、新事件、删事件；下面那一块随选中的事件重搭(见 RefreshEvent)
        _eventCard = Card(side, LM.Get("scenario_editor_card_event"));
        _eventBtn = Cycler(_eventCard, LM.Get("scenario_editor_event_pick"), step =>
        {
            int count = ScenarioEditing.Events.Count;
            if (count > 0) _eventIndex = ((_eventIndex < 0 ? 0 : _eventIndex + step) % count + count) % count;
            _pickSlot = null;
        });
        RectTransform eventActions = Columns(_eventCard);
        MakeButton(eventActions, LM.Get("scenario_editor_new_event_btn"), ToolColor, () =>
        {
            _eventIndex = ScenarioEditing.NewEvent();
            _pickSlot = null;
        });
        MakeButton(eventActions, LM.Get("scenario_editor_delete_event"), DangerColor, () =>
        {
            ScenarioEditing.DeleteEvent(_eventIndex);
            _eventIndex = ScenarioEditing.Events.Count - 1;
            _pickSlot = null;
        });
        _eventBody = Child("EventBody", _eventCard);
        var bodyLayout = _eventBody.gameObject.AddComponent<VerticalLayoutGroup>();
        bodyLayout.spacing = 5f;
        bodyLayout.childControlWidth = bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;

        // 刷地块：刷给谁、笔刷粗细、清掉全部
        _landCard = Card(side, LM.Get("scenario_editor_card_land"));
        _landBrushBtn = Cycler(_landCard, LM.Get("scenario_editor_brush"), StepBrush);
        _landInfo = Label(_landCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _clearLandBtn = MakeButton(_landCard, LM.Get("scenario_editor_clear_land"), DangerColor, () => ArmClear("land"));

        // 刷地形：笔刷粗细、十六种地表、清掉全部
        _terrainCard = Card(side, LM.Get("scenario_editor_card_terrain"));
        _terrainBrushBtn = Cycler(_terrainCard, LM.Get("scenario_editor_brush"), StepBrush);
        RectTransform palette = Columns(_terrainCard);
        for (int i = 0; i < TerrainPalette.Length; i++)
        {
            int index = i;
            TerrainButtons.Add((TerrainPalette[i].type, MakeButton(palette, LM.Get("scenario_terrain_" + TerrainPalette[i].name), ToolColor,
                () => _terrainType = index, 13, 28f, Icon("ui/icons/" + TerrainPalette[i].icon))));
        }
        _terrainInfo = Label(_terrainCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _clearTerrainBtn = MakeButton(_terrainCard, LM.Get("scenario_editor_clear_terrain"), DangerColor, () => ArmClear("terrain"));

        // 部落圈：文化、几个部落、删掉
        _tribeCard = Card(side, LM.Get("scenario_editor_card_tribe"));
        _tribeCultureBtn = Cycler(_tribeCard, LM.Get("scenario_editor_culture"), step => ScenarioEditing.CycleTribesCulture(_tribeIndex, step));
        _tribeCountBtn = Cycler(_tribeCard, LM.Get("scenario_editor_tribe_count"), step => ScenarioEditing.CycleTribesCount(_tribeIndex, step));
        _tribeInfo = Label(_tribeCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _deleteTribeBtn = MakeButton(_tribeCard, LM.Get("scenario_editor_delete_tribe"), DangerColor, () =>
        {
            ScenarioEditing.DeleteTribes(_tribeIndex);
            _tribeIndex = -1;
        });

        _passCard = Card(side, LM.Get("scenario_editor_card_pass"));
        _passNameInput = Field(_passCard, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenamePass(_passIndex, text));
        _passInfo = Label(_passCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        MakeButton(_passCard, LM.Get("scenario_editor_delete_pass"), DangerColor, () =>
        {
            ScenarioEditing.DeletePass(_passIndex);
            _passIndex = -1;
        });
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Image Fill(RectTransform rect, Color color, bool raycast = false)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static void Frame(RectTransform rect)
    {
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = FrameColor;
        outline.effectDistance = new Vector2(1.5f, 1.5f);
    }

    private static void Row(RectTransform rect, float spacing, RectOffset padding)
    {
        var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.padding = padding;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
    }

    private static void Spacer(RectTransform parent) => Child("Spacer", parent).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

    private static void RowTitle(RectTransform parent, string text) => Label(parent, "RowTitle", 13, HeadColor, TextAnchor.MiddleLeft).text = text;

    private static Text Label(Transform parent, string name, int size, Color color, TextAnchor anchor, bool wrap = false)
    {
        Text text = Child(name, parent).gameObject.AddComponent<Text>();
        text.font = LocalizedTextManager.current_font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.supportRichText = true;
        text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite Icon(string path)
    {
        try
        {
            return string.IsNullOrEmpty(path) ? null : SpriteTextureLoader.getSprite(path);
        }
        catch
        {
            return null;
        }
    }

    private static Btn MakeButton(Transform parent, string text, Color color, UnityAction action, int size = 15, float height = 28f,
        Sprite icon = null)
    {
        RectTransform rect = Child("Button", parent);
        var button = new Btn { Image = Fill(rect, color, true) };
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);
        button.Button = rect.gameObject.AddComponent<Button>();
        button.Button.targetGraphic = button.Image;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        colors.fadeDuration = 0.06f;
        button.Button.colors = colors;
        button.Button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.Button.onClick.AddListener(() =>
        {
            EventSystem.current?.SetSelectedGameObject(null);
            action();
            RefreshAll();
        });
        var inner = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        inner.padding = new RectOffset(10, 10, 2, 2);
        inner.childAlignment = TextAnchor.MiddleCenter;
        inner.childControlWidth = inner.childControlHeight = true;
        inner.childForceExpandWidth = inner.childForceExpandHeight = true;
        var element = rect.gameObject.AddComponent<LayoutElement>();
        if (height > 0f) element.minHeight = element.preferredHeight = height;
        // 带图标：图标在左、字在右，边距收窄
        if (icon != null)
        {
            inner.padding = new RectOffset(5, 8, 3, 3);
            inner.spacing = 4f;
            inner.childForceExpandWidth = false;
            RectTransform iconRect = Child("Icon", rect);
            Image image = iconRect.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var iconElement = iconRect.gameObject.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = 18f;
            iconElement.minHeight = iconElement.preferredHeight = 18f;
            iconElement.flexibleWidth = 0f;
        }
        button.Label = Label(rect, "Text", size, TextColor, TextAnchor.MiddleCenter);
        button.Label.text = text;
        return button;
    }

    private static RectTransform Card(Transform parent, string title)
    {
        RectTransform card = Child("Card", parent);
        Fill(card, PanelColor);
        Frame(card);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 10);
        layout.spacing = 5f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        Label(card, "Title", 17, HeadColor, TextAnchor.MiddleLeft).text = title;
        return card;
    }

    private static RectTransform Columns(Transform parent)
    {
        RectTransform rect = Child("Grid", parent);
        var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2((SideWidth - 20f - 8f) / 2f, 28f);
        grid.spacing = new Vector2(8f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        return rect;
    }

    // 一行：小字、往前、当前值(点它往后)、往后
    private static Btn Cycler(Transform parent, string caption, global::System.Action<int> step)
    {
        RectTransform row = Child("Cycler", parent);
        Row(row, 6f, new RectOffset(0, 0, 0, 0));
        Text label = Label(row, "Caption", 14, DimText, TextAnchor.MiddleLeft);
        label.text = caption;
        var captionElement = label.gameObject.AddComponent<LayoutElement>();
        captionElement.minWidth = captionElement.preferredWidth = 64f;
        MakeButton(row, "<", ViewColor, () => step(-1), 15, 26f);
        Btn value = MakeButton(row, "", ViewColor, () => step(1), 15, 26f);
        value.Button.GetComponent<LayoutElement>().flexibleWidth = 1f;
        MakeButton(row, ">", ViewColor, () => step(1), 15, 26f);
        return value;
    }

    // 一行：左边小字、右边输入框；打完(回车或点别处)交给 onEnd
    private static InputField Field(Transform parent, string caption, UnityAction<string> onEnd, float height = 28f, bool multi = false)
    {
        RectTransform row = Child("Field", parent);
        Row(row, 6f, new RectOffset(0, 0, 0, 0));
        row.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Text label = Label(row, "Caption", 14, DimText, TextAnchor.MiddleLeft);
        label.text = caption;
        var captionElement = label.gameObject.AddComponent<LayoutElement>();
        captionElement.minWidth = captionElement.preferredWidth = 64f;
        RectTransform box = Child("Input", row);
        Image back = Fill(box, InputBack, true);
        var element = box.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.flexibleWidth = 1f;
        Text text = Label(box, "Text", 15, TextColor, multi ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, multi);
        text.supportRichText = false;
        Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(6f, 2f), new Vector2(-6f, -2f));
        InputField input = box.gameObject.AddComponent<InputField>();
        input.textComponent = text;
        input.targetGraphic = back;
        input.lineType = multi ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.onEndEdit.AddListener(value =>
        {
            onEnd(value);
            RefreshAll();
        });
        return input;
    }

    #endregion

    #region 每帧

    internal static void Tick(ScenarioEditorHost host)
    {
        if (_root == null || host == null || host.gameObject != _root) return;
        bool typing = Typing();
        if (Input.GetKeyDown(KeyCode.Escape) && !typing)
        {
            Close();
            return;
        }
        if (!typing && Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            Undo();
            RefreshAll();
        }
        try
        {
            if (_autoFit > 0)
            {
                _autoFit--;
                FitScenario();
            }
            HandleMouse();
            if (_brushButton < 0 && _terrainApplied != ScenarioEditing.TerrainRevision) ApplyTerrainPaint();
            else if (_terrainDirty && (_brushButton < 0 || Time.unscaledTime - _terrainUploadAt > 0.1f)) UploadTerrain();
            RenderFill();
            RenderMarks();
            UpdateLabels();
            UpdateAreaLabels();
            UpdateTip();
            UpdateStatus();
            if (_closeArmedAt > 0f && Time.unscaledTime - _closeArmedAt > ConfirmSeconds)
            {
                _closeArmedAt = -100f;
                RefreshButtons();
            }
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 剧本编辑器出错，已关闭: {exception}");
            Close(true);
        }
    }

    private static bool Typing()
    {
        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        InputField input = selected == null ? null : selected.GetComponent<InputField>();
        return input != null && input.isFocused;
    }

    private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    private static void HandleMouse()
    {
        Vector2 mouse = Input.mousePosition;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(_viewport, mouse, null);
        bool onMap = ScreenToTexel(mouse, out Vector2 texel) && inside;
        _hoverCity = onMap ? CityAt(texel) : null;
        _hoverPass = onMap && _tool == Tool.Pass ? PassAt(texel) : -1;
        _hoverTribe = onMap && _tool == Tool.Tribe ? ScenarioEditing.TribesAt(TexelToLon(texel.x), TexelToLat(texel.y)) : -1;

        float wheel = Input.mouseScrollDelta.y;
        if (inside && Mathf.Abs(wheel) > 0.01f) ZoomAt(mouse, Mathf.Pow(1.2f, wheel));

        // 平移：中键，右键拖空处(点城、关隘的右键另有用处)，「查看」下左键
        bool leftPans = _tool == Tool.View;
        bool brush = _tool is Tool.Land or Tool.Terrain;
        bool rightOnThing = _hoverCity != null || _hoverPass >= 0 || _tool == Tool.Wall || brush || _hoverTribe >= 0;
        if (_dragButton < 0)
        {
            int button = Input.GetMouseButtonDown(2) ? 2 : Input.GetMouseButtonDown(0) && leftPans ? 0
                : Input.GetMouseButtonDown(1) && !rightOnThing ? 1 : -1;
            if (button >= 0 && inside && RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse, null, out _dragLast))
            {
                _dragButton = button;
                _autoFit = 0;
            }
        }
        else if (!Input.GetMouseButton(_dragButton)) _dragButton = -1;
        else if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse, null, out Vector2 local))
        {
            _center -= (local - _dragLast) / _zoom;
            _dragLast = local;
            Layout();
        }

        // 刷地块、刷地形：左键刷、右键擦，按住拖着连成一笔(Shift+左键是取刷给谁，不刷)
        if (_brushButton >= 0)
        {
            if (Input.GetMouseButton(_brushButton))
            {
                if (onMap)
                {
                    BrushAt(_brushLast, texel, _brushButton == 1);
                    _brushLast = texel;
                }
            }
            else
            {
                _brushButton = -1;
                ScenarioEditing.EndStroke();
                _fillKey = int.MinValue;
                RefreshAll();
            }
            UpdateRing(onMap, texel);
            return;
        }
        if (brush && onMap && _dragButton < 0 && (Input.GetMouseButtonDown(0) && !Shift || Input.GetMouseButtonDown(1)))
        {
            _brushButton = Input.GetMouseButtonDown(0) ? 0 : 1;
            _brushLast = texel;
            ScenarioEditing.BeginStroke();
            BrushAt(texel, texel, _brushButton == 1);
            UpdateRing(onMap, texel);
            return;
        }
        UpdateRing(onMap, texel);

        if (Input.GetMouseButtonDown(1) && inside && rightOnThing)
        {
            _rightPressed = true;
            _pressAt = mouse;
            _pressCity = _hoverCity;
            _pressPass = _hoverPass;
            _pressTribe = _hoverTribe;
        }
        if (Input.GetMouseButtonUp(1) && _rightPressed)
        {
            _rightPressed = false;
            if ((mouse - _pressAt).sqrMagnitude <= ClickSlop * ClickSlop) RightClick();
        }

        if (leftPans) return;
        if (Input.GetMouseButtonDown(0) && onMap)
        {
            _pressed = true;
            _pressAt = mouse;
            _pressCity = _hoverCity;
            _pressPass = _hoverPass;
            _pressTexel = texel;
            _pressTribe = _tool == Tool.Tribe ? _hoverTribe : -1;
            _moving = false;
            _lastStroked = null;
            _stroking = _tool == Tool.Paint || Shift && _tool is Tool.Kingdom or Tool.Empire;
            if (_stroking)
            {
                ScenarioEditing.BeginStroke();
                StrokeAt(_hoverCity);
            }
        }
        if (_pressed && Input.GetMouseButton(0))
        {
            if (_tool == Tool.City && _pressCity != null && (mouse - _pressAt).sqrMagnitude > ClickSlop * ClickSlop) _moving = true;
            if (_stroking) StrokeAt(_hoverCity);
        }
        if (!_pressed || !Input.GetMouseButtonUp(0)) return;
        _pressed = false;
        if (_stroking)
        {
            _stroking = false;
            ScenarioEditing.EndStroke();
            RefreshAll();
            return;
        }
        bool click = (mouse - _pressAt).sqrMagnitude <= ClickSlop * ClickSlop;
        // 部落：从空处拖出一个圈
        if (_tool == Tool.Tribe && _pressTribe < 0 && !click && onMap)
        {
            float radius = (texel - _pressTexel).magnitude * _step;
            if (radius >= MinTribeRadius)
            {
                _tribeIndex = ScenarioEditing.AddTribes(TexelToLon(_pressTexel.x), TexelToLat(_pressTexel.y), radius);
                Say(LM.Get("scenario_editor_tribe_added"));
            }
            else Say(LM.Get("scenario_editor_tribe_small"));
        }
        else if (_tool == Tool.City && _moving && _pressCity != null && onMap)
        {
            ScenarioEditing.MoveCity(_pressCity, TexelToLon(texel.x), TexelToLat(texel.y));
            _cityId = _pressCity;
            Say(LM.Get("scenario_editor_moved"));
        }
        else if (click && onMap) LeftClick(texel);
        _moving = false;
        RefreshAll();
    }

    private static void LeftClick(Vector2 texel)
    {
        string city = _pressCity;
        switch (_tool)
        {
            case Tool.City:
                if (city != null) SelectCity(city);
                else if (OnLand(texel))
                {
                    _cityId = ScenarioEditing.AddCity(TexelToLon(texel.x), TexelToLat(texel.y));
                    Say(LM.Get("scenario_editor_city_added"));
                }
                break;
            case Tool.Realm:
                if (city != null) SelectCity(city);
                else _cityId = null;
                break;
            case Tool.Kingdom:
            case Tool.Empire:
                if (city == null) break;
                SelectCity(city);
                _regionId = ScenarioEditing.City(city)?.region;
                if (_tool == Tool.Empire) _coreId = ScenarioEditing.CoreOfRegion(_regionId)?.id;
                break;
            case Tool.Relation:
                if (city == null) break;
                WorldMapPolity owner = ScenarioEditing.OwnerOf(ScenarioEditing.City(city));
                if (owner == null)
                {
                    Say(LM.Get("scenario_editor_relation_independent"));
                    break;
                }
                WorldMapPolity from = ScenarioEditing.Polity(_relationFrom);
                if (from == null || from == owner)
                {
                    _relationFrom = _realmId = owner.id;
                    Say(string.Format(LM.Get("scenario_editor_relation_from"), owner.Name));
                    break;
                }
                Say(ScenarioEditing.ToggleRelation(from, owner, ScenarioEditing.RelationKinds[_kind], ScenarioEditing.WarGoals[_goal]));
                break;
            case Tool.Wall:
                if (_wallIndex < 0 || _wallIndex >= ScenarioEditing.Walls.Count) _wallIndex = ScenarioEditing.NewWall();
                ScenarioEditing.AddWallPoint(_wallIndex, TexelToLon(texel.x), TexelToLat(texel.y));
                break;
            case Tool.Event:
                if (city != null) PickInto(city);
                break;
            case Tool.Land:
                PickTarget(texel, city);
                break;
            case Tool.Tribe:
                _tribeIndex = _pressTribe;
                break;
            case Tool.Pass:
                if (_pressPass >= 0) _passIndex = _pressPass;
                else
                {
                    _passIndex = ScenarioEditing.AddPass(TexelToLon(texel.x), TexelToLat(texel.y));
                    Say(LM.Get("scenario_editor_pass_added"));
                }
                break;
        }
    }

    // 事件工具：把点的城填进正等着的那一项(国取城的归属，地区取城所在的王国法理)
    private static void PickInto(string cityId)
    {
        _cityId = cityId;
        if (_pickSlot == null)
        {
            Say(LM.Get("scenario_editor_event_pick_first"));
            return;
        }
        WorldMapCity city = ScenarioEditing.City(cityId);
        string value = null;
        if (_pickSlot is "a" or "b")
        {
            WorldMapPolity owner = ScenarioEditing.OwnerOf(city);
            if (owner == null)
            {
                Say(LM.Get("scenario_editor_relation_independent"));
                return;
            }
            value = owner.id;
        }
        else if (_pickSlot == "city") value = cityId;
        else if (_pickSlot == "region") value = city?.region;
        if (value == null) return;
        ScenarioEditing.SetStepSlot(_eventIndex, _pickEffect, _pickStep, _pickSlot, value);
        Say(string.Format(LM.Get("scenario_editor_event_picked"), SlotText(_pickSlot, value)));
        _pickSlot = null;
    }

    private static void RightClick()
    {
        switch (_tool)
        {
            case Tool.City:
                ArmDeleteCity(_pressCity);
                break;
            case Tool.Paint:
                Say(ScenarioEditing.Release(_pressCity));
                break;
            case Tool.Kingdom:
                if (_pressCity != null && _regionId != null) Say(ScenarioEditing.ToggleShare(_pressCity, _regionId));
                break;
            case Tool.Relation:
                _relationFrom = null;
                break;
            case Tool.Wall:
                ScenarioEditing.RemoveWallPoint(_wallIndex);
                break;
            case Tool.Pass:
                if (_pressPass < 0) break;
                ScenarioEditing.DeletePass(_pressPass);
                _passIndex = -1;
                Say(LM.Get("scenario_editor_pass_deleted"));
                break;
            case Tool.Tribe:
                if (_pressTribe < 0) break;
                ScenarioEditing.DeleteTribes(_pressTribe);
                _tribeIndex = -1;
                Say(LM.Get("scenario_editor_tribe_deleted"));
                break;
        }
        RefreshAll();
    }

    // 刷给谁：国家图层刷给国家(p:)，王国法理图层刷进法理(r:)；别的图层刷不了
    private static string BrushKey() => _layer switch
    {
        Layer.Realm => ScenarioEditing.Polity(_realmId) == null ? null : "p:" + _realmId,
        Layer.Kingdom => ScenarioEditing.Region(_regionId) == null ? null : "r:" + _regionId,
        _ => null
    };

    // 一笔从上一点刷到这一点(圆刷，沿途每半个半径落一次)：刷地块只刷陆地；刷地形顺手改预览的像素
    private static void BrushAt(Vector2 from, Vector2 to, bool erase)
    {
        bool land = _tool == Tool.Land;
        string key = null;
        if (land && !erase && (key = BrushKey()) == null)
        {
            Say(LM.Get(_layer is Layer.Realm or Layer.Kingdom ? "scenario_editor_land_no_target" : "scenario_editor_land_need_layer"));
            return;
        }
        string type = land || erase ? null : TerrainPalette[Mathf.Clamp(_terrainType, 0, TerrainPalette.Length - 1)].type;
        float radius = BrushSizes[_brushSize] / Mathf.Max(0.001f, _step);
        int reach = Mathf.CeilToInt(radius);
        int count = Mathf.Clamp(Mathf.CeilToInt((to - from).magnitude / Mathf.Max(1f, radius / 2f)), 0, 200);
        float step = land ? ScenarioCells.LandStep : ScenarioCells.TerrainStep;
        BrushCells.Clear();
        BrushTexels.Clear();
        for (int s = 0; s <= count; s++)
        {
            Vector2 at = count == 0 ? to : Vector2.Lerp(from, to, s / (float)count);
            int cx = Mathf.FloorToInt(at.x), cy = Mathf.FloorToInt(at.y);
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (dx * dx + dy * dy > radius * radius + 0.5f) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
                int index = y * _w + x;
                if (land && !_land[index]) continue;
                if (BrushCells.Add(ScenarioCells.Key(TexelToLon(x + 0.5f), TexelToLat(y + 0.5f), step)) && !land) BrushTexels.Add(index);
            }
        }
        if (land)
        {
            ScenarioEditing.PaintLand(BrushCells, key);
            return;
        }
        if (ScenarioEditing.PaintTerrain(BrushCells, type) == 0) return;
        foreach (int index in BrushTexels) SetTexel(index, type);
        _terrainDirty = true;
        _terrainApplied = ScenarioEditing.TerrainRevision;
    }

    // 刷地块取刷给谁：点在城上取它的国家(或王国法理)，点在空处取那里铺着的那一块
    private static void PickTarget(Vector2 texel, string cityId)
    {
        string key;
        if (cityId != null)
        {
            SelectCity(cityId);
            WorldMapCity city = ScenarioEditing.City(cityId);
            key = _layer == Layer.Kingdom ? "r:" + city?.region : ScenarioEditing.OwnerOf(city) is WorldMapPolity owner ? "p:" + owner.id : null;
        }
        else key = FillKeyAt(texel);
        if (key == null || key.Length < 3 || key[0] is not 'p' and not 'r')
        {
            Say(LM.Get(_layer is Layer.Realm or Layer.Kingdom ? "scenario_editor_land_pick_none" : "scenario_editor_land_need_layer"));
            return;
        }
        if (key[0] == 'p') _realmId = key.Substring(2);
        else _regionId = key.Substring(2);
        Say(string.Format(LM.Get("scenario_editor_land_picked"), AreaName(key)));
    }

    // 图层颜色在这一点铺的是哪一块
    private static string FillKeyAt(Vector2 texel)
    {
        if (_fillOwner == null || _fill == null) return null;
        int x = Mathf.FloorToInt(texel.x) / FillFactor, y = Mathf.FloorToInt(texel.y) / FillFactor;
        if (x < 0 || y < 0 || x >= _fill.width || y >= _fill.height || _fillOwner.Length != _fill.width * _fill.height) return null;
        int slot = _fillOwner[y * _fill.width + x];
        return slot >= 0 && slot < FillKeys.Count ? FillKeys[slot] : null;
    }

    private static void StepBrush(int step) => _brushSize = ((_brushSize + step) % BrushSizes.Length + BrushSizes.Length) % BrushSizes.Length;

    private static string BrushText() => BrushSizes[_brushSize].ToString("0.#") + "°";

    private static bool Armed(string kind) => _clearArmed == kind && Time.unscaledTime - _clearArmedAt <= ConfirmSeconds;

    // 清掉全部刷的地块或地形：五秒内再点一次(可撤销)
    private static void ArmClear(string kind)
    {
        if (Armed(kind))
        {
            if (kind == "land") ScenarioEditing.ClearLand();
            else ScenarioEditing.ClearTerrain();
            _clearArmed = null;
            Say(LM.Get("scenario_editor_cleared"));
            return;
        }
        _clearArmed = kind;
        _clearArmedAt = Time.unscaledTime;
        Say(LM.Get("scenario_editor_clear_confirm"));
    }

    // 笔刷圈(跟着光标，右键擦时发红)与正拖出的部落圈
    private static void UpdateRing(bool onMap, Vector2 texel)
    {
        if (_ring == null) return;
        bool brush = _tool is Tool.Land or Tool.Terrain && onMap && _dragButton < 0;
        bool tribe = _tool == Tool.Tribe && _pressed && _pressTribe < 0 && onMap && Input.GetMouseButton(0);
        bool show = brush || tribe;
        if (_ring.gameObject.activeSelf != show) _ring.gameObject.SetActive(show);
        if (!show) return;
        Vector2 center = brush ? texel : _pressTexel;
        float radius = brush ? BrushSizes[_brushSize] / Mathf.Max(0.001f, _step) : (texel - _pressTexel).magnitude;
        _ring.rectTransform.anchoredPosition = (center - _center) * _zoom;
        _ring.rectTransform.sizeDelta = Vector2.one * Mathf.Max(6f, radius * 2f * _zoom);
        _ring.color = tribe ? new Color(1f, 0.85f, 0.4f, 1f) : _brushButton == 1 ? new Color(1f, 0.45f, 0.4f, 1f) : Color.white;
    }

    private static Texture2D RingTexture()
    {
        const int size = 128;
        _ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude;
            pixels[y * size + x] = d >= 59f && d < 62f ? new Color32(255, 255, 255, 255)
                : d >= 62f && d < 64f ? new Color32(0, 0, 0, 170) : new Color32(0, 0, 0, 0);
        }
        _ringTexture.SetPixels32(pixels);
        _ringTexture.Apply();
        return _ringTexture;
    }

    // 一笔：划城(拖过的城划给选中的国家)、王国法理(划进选中的法理)、帝国法理(城的王国法理收进选中的帝国法理)；同一座只问一次
    private static void StrokeAt(string cityId)
    {
        if (cityId == null || cityId == _lastStroked) return;
        _lastStroked = cityId;
        string message = null;
        switch (_tool)
        {
            case Tool.Paint:
                WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
                if (polity == null) message = LM.Get("scenario_editor_paint_need_realm");
                else if (ScenarioEditing.OwnerOf(ScenarioEditing.City(cityId)) != polity) message = ScenarioEditing.Assign(cityId, polity);
                break;
            case Tool.Kingdom:
                message = _regionId == null ? LM.Get("scenario_editor_need_region") : ScenarioEditing.AssignRegion(cityId, _regionId);
                break;
            case Tool.Empire:
                message = _coreId == null
                    ? LM.Get("scenario_editor_need_core")
                    : ScenarioEditing.AddRegionToCore(_coreId, ScenarioEditing.City(cityId)?.region);
                break;
        }
        Say(message);
    }

    private static void SelectCity(string cityId)
    {
        _cityId = cityId;
        WorldMapPolity owner = ScenarioEditing.OwnerOf(ScenarioEditing.City(cityId));
        if (owner != null || _tool == Tool.Realm) _realmId = owner?.id;
    }

    private static void ArmDeleteCity(string cityId)
    {
        if (cityId == null) return;
        if (_deleteArmedCity == cityId && Time.unscaledTime - _deleteCityArmedAt <= ConfirmSeconds)
        {
            ScenarioEditing.DeleteCity(cityId);
            if (_cityId == cityId) _cityId = null;
            _deleteArmedCity = null;
            Say(LM.Get("scenario_editor_city_deleted"));
            return;
        }
        _deleteArmedCity = cityId;
        _deleteCityArmedAt = Time.unscaledTime;
        Say(string.Format(LM.Get("scenario_editor_delete_city_confirm"), ScenarioEditing.CityName(ScenarioEditing.City(cityId))));
    }

    private static void ArmDeleteRealm()
    {
        WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
        if (polity == null) return;
        if (Time.unscaledTime - _deleteRealmArmedAt <= ConfirmSeconds)
        {
            ScenarioEditing.DeletePolity(polity);
            _realmId = null;
            _deleteRealmArmedAt = -100f;
            Say(LM.Get("scenario_editor_realm_deleted"));
            return;
        }
        _deleteRealmArmedAt = Time.unscaledTime;
        Say(string.Format(LM.Get("scenario_editor_delete_realm_confirm"), polity.Name));
    }

    private static void SetTool(Tool tool)
    {
        _tool = tool;
        _message = null;
        _pickSlot = null;
        if (tool == Tool.Event && _eventIndex >= ScenarioEditing.Events.Count) _eventIndex = ScenarioEditing.Events.Count - 1;
        if (tool == Tool.Event && _eventIndex < 0 && ScenarioEditing.Events.Count > 0) _eventIndex = 0;
        // 换到法理工具时顺手换到对应图层，选中的城跟着选它的法理
        if (tool == Tool.Kingdom) _layer = Layer.Kingdom;
        if (tool == Tool.Empire && _layer is not Layer.Empire and not Layer.Tianxia) _layer = Layer.Empire;
        if (tool is Tool.Realm or Tool.Paint or Tool.Relation && _layer != Layer.Realm) _layer = Layer.Realm;
        if (tool is Tool.Kingdom or Tool.Empire && _cityId != null) _regionId = ScenarioEditing.City(_cityId)?.region;
        if (tool == Tool.Empire && _regionId != null) _coreId = ScenarioEditing.CoreOfRegion(_regionId)?.id;
        if (tool == Tool.Wall && _wallIndex >= ScenarioEditing.Walls.Count) _wallIndex = ScenarioEditing.Walls.Count - 1;
        // 刷地块要在国家或王国法理图层；刷地形看地形图层
        if (tool == Tool.Land && _layer is not Layer.Realm and not Layer.Kingdom) _layer = Layer.Realm;
        if (tool == Tool.Terrain) _layer = Layer.Terrain;
        if (tool == Tool.Tribe && _tribeIndex >= ScenarioEditing.Tribes.Count) _tribeIndex = -1;
    }

    private static void SetLayer(Layer layer) => _layer = layer;

    // 地球 ↔ 导入地图：换了城与国家都清掉，五秒内再点一次才换(可撤销)
    private static void ToggleImport()
    {
        bool hasContent = ScenarioEditing.Era.polities.Count > 0 || ScenarioEditing.Era.OwnWorld && ScenarioEditing.Cities.Count > 0;
        if (hasContent && Time.unscaledTime - _importArmedAt > ConfirmSeconds)
        {
            _importArmedAt = Time.unscaledTime;
            Say(LM.Get("scenario_editor_import_confirm"));
            return;
        }
        _importArmedAt = -100f;
        ScenarioEditing.SetImport(!ScenarioEditing.UsesImport);
        _cityId = _realmId = _regionId = _coreId = _relationFrom = null;
        _wallIndex = _passIndex = _eventIndex = -1;
        ReloadTerrain();
        Say(LM.Get(ScenarioEditing.UsesImport ? "scenario_editor_import_on" : "scenario_editor_import_off"));
    }

    private static void OpenFolder()
    {
        try
        {
            if (!string.IsNullOrEmpty(ScenarioEditing.Folder))
                Application.OpenURL("file:///" + ScenarioEditing.Folder.Replace('\\', '/'));
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 打不开剧本文件夹: {exception.Message}");
        }
    }

    private static void Undo()
    {
        bool imported = ScenarioEditing.UsesImport;
        string mode = ScenarioEditing.Era?.map?.mode;
        float sea = ScenarioEditing.Era?.map?.sea ?? 0f;
        bool done = ScenarioEditing.Undo();
        if (done && (imported != ScenarioEditing.UsesImport || mode != ScenarioEditing.Era?.map?.mode ||
                     !Mathf.Approximately(sea, ScenarioEditing.Era?.map?.sea ?? 0f))) ReloadTerrain();
        Say(LM.Get(done ? "scenario_editor_undone" : "scenario_editor_undo_none"));
    }

    private static void Save()
    {
        bool ok = ScenarioEditing.Save(Thumbnail(out int width, out int height), width, height);
        Say(LM.Get(ok ? "scenario_editor_saved" : "scenario_editor_save_failed"));
    }

    private static void Say(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _message = text;
        _messageAt = Time.unscaledTime;
    }

    #endregion

    #region 坐标与视图

    private static float TexelToLon(float x) => _lon0 + x * _step;
    private static float TexelToLat(float y) => _lat0 + y * _step;
    private static Vector2 Texel(float lon, float lat) => new Vector2((lon - _lon0) / _step, (lat - _lat0) / _step);
    private static Vector2 Texel(WorldMapCity city) => Texel(city.lon, city.lat);

    private static bool OnLand(Vector2 texel)
    {
        int x = Mathf.FloorToInt(texel.x), y = Mathf.FloorToInt(texel.y);
        return x >= 0 && y >= 0 && x < _w && y < _h && _land[y * _w + x];
    }

    private static bool ScreenToTexel(Vector2 screen, out Vector2 texel)
    {
        texel = default;
        if (_zoom <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screen, null, out Vector2 local)) return false;
        texel = _center + (local - _viewport.rect.center) / _zoom;
        return texel.x >= 0f && texel.y >= 0f && texel.x < _w && texel.y < _h;
    }

    private static float PickReach() => Mathf.Max(MarkerScreen, MarkerRadius() * _zoom + 3f) / _zoom;

    // 光标下的城：屏幕上离得够近的里最近的一座(只看开局年份在的城)
    private static string CityAt(Vector2 texel)
    {
        float best = PickReach() * PickReach();
        string found = null;
        foreach (WorldMapCity city in ScenarioEditing.Cities)
        {
            if (!ScenarioEditing.Active(city)) continue;
            float distance = (Texel(city) - texel).sqrMagnitude;
            if (distance > best) continue;
            best = distance;
            found = city.id;
        }
        return found;
    }

    private static int PassAt(Vector2 texel)
    {
        float best = PickReach() * PickReach();
        int found = -1;
        List<WorldMapPass> passes = ScenarioEditing.Passes;
        for (int i = 0; i < passes.Count; i++)
        {
            if (passes[i].at == null || passes[i].at.Length < 2) continue;
            float distance = (Texel(passes[i].at[0], passes[i].at[1]) - texel).sqrMagnitude;
            if (distance > best) continue;
            best = distance;
            found = i;
        }
        return found;
    }

    private static float FitZoom()
    {
        Rect view = _viewport.rect;
        return view.width < 2f || view.height < 2f ? 0f : Mathf.Min(view.width / _w, view.height / _h);
    }

    // 看全图：整张地表装进视口
    private static void Fit()
    {
        float fit = FitZoom();
        if (fit <= 0f) return;
        _zoom = fit * 0.98f;
        _center = new Vector2(_w / 2f, _h / 2f);
        Layout();
    }

    // 打开时：框住剧本里各国的城(没有国家就看全图)
    private static void FitScenario()
    {
        Rect view = _viewport.rect;
        if (view.width < 2f || view.height < 2f) return;
        List<Vector2> points = ScenarioEditing.Era.polities.Count == 0 ? new List<Vector2>()
            : ScenarioEditing.Cities.Where(city => ScenarioEditing.Active(city) && ScenarioEditing.OwnerOf(city) != null).Select(Texel).ToList();
        if (points.Count == 0)
        {
            Fit();
            return;
        }
        float minX = points.Min(p => p.x), maxX = points.Max(p => p.x), minY = points.Min(p => p.y), maxY = points.Max(p => p.y);
        float width = Mathf.Max(40f, maxX - minX + 20f), height = Mathf.Max(30f, maxY - minY + 20f);
        _zoom = Mathf.Clamp(Mathf.Min(view.width / width, view.height / height), FitZoom() * 0.5f, MaxZoom);
        _center = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
        Layout();
    }

    private static void ZoomAt(Vector2 screen, float factor)
    {
        float fit = FitZoom();
        if (fit <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screen, null, out Vector2 local)) return;
        _autoFit = 0;
        local -= _viewport.rect.center;
        Vector2 world = _center + local / _zoom;
        _zoom = Mathf.Clamp(_zoom * factor, fit * 0.5f, MaxZoom);
        _center = world - local / _zoom;
        Layout();
    }

    private static void Layout()
    {
        if (_map == null) return;
        _center.x = Mathf.Clamp(_center.x, 0f, _w);
        _center.y = Mathf.Clamp(_center.y, 0f, _h);
        RectTransform rect = _map.rectTransform;
        rect.sizeDelta = new Vector2(_w * _zoom, _h * _zoom);
        rect.anchoredPosition = (new Vector2(_w / 2f, _h / 2f) - _center) * _zoom;
    }

    #endregion

    #region 画

    private static int MarkerRadius() => Mathf.Clamp(Mathf.RoundToInt(MarkerScreen / 2f / Mathf.Max(0.05f, _zoom)), 0, 8);

    // 这座城在当前图层归哪一块(国家、王国法理、帝国法理、天下)；不归哪块返回 null
    private static string AreaKey(WorldMapCity city)
    {
        switch (_layer)
        {
            case Layer.Realm:
                WorldMapPolity owner = ScenarioEditing.OwnerOf(city);
                return owner == null ? null : "p:" + owner.id;
            case Layer.Kingdom:
                return string.IsNullOrEmpty(city.region) ? null : "r:" + city.region;
            case Layer.Empire:
                WorldMapCore core = ScenarioEditing.CoreOfRegion(city.region);
                return core == null ? null : "k:" + core.id;
            case Layer.Tianxia:
                string group = ScenarioEditing.CoreOfRegion(city.region)?.group;
                return string.IsNullOrEmpty(group) ? null : "t:" + group;
        }
        return null;
    }

    private static string SelectedKey() => _layer switch
    {
        Layer.Realm => _realmId == null ? null : "p:" + _realmId,
        Layer.Kingdom => _regionId == null ? null : "r:" + _regionId,
        Layer.Empire => _coreId == null ? null : "k:" + _coreId,
        Layer.Tianxia => ScenarioEditing.Core(_coreId)?.group is string group && group.Length > 0 ? "t:" + group : null,
        _ => null
    };

    // 图层颜色(用户 10-30 "地块怎么全是菱形的而且层级也好乱")：从各城往外铺陆地，按地形代价八方向走(水过不去，丘陵、山地、雪山贵，
    // 翻不过大山)，大城铺得远，谁先到归谁；刷过的地块按刷的归属钉死。选中的那一块颜色深些、刷过的再深一点，两块相接处与未归属的
    // 陆地边上描深色边线(只描一侧，线细)
    private static void RenderFill()
    {
        string selected = SelectedKey();
        int key = (ScenarioEditing.Revision * 31 + (int)_layer) * 31 + (selected?.GetHashCode() ?? 0);
        if (key == _fillKey) return;
        // 拖着刷、划的时候至多每 FillThrottle 秒重铺一次
        if ((_brushButton >= 0 || _stroking) && Time.unscaledTime - _fillAt < FillThrottle) return;
        _fillKey = key;
        _fillAt = Time.unscaledTime;
        _fillImage.enabled = _layer != Layer.Terrain;
        if (_layer == Layer.Terrain || _classes == null) return;
        int fw = _fill.width, fh = _fill.height, n = fw * fh;
        if (_fillOwner == null || _fillOwner.Length != n)
        {
            _fillOwner = new int[n];
            _fillDist = new int[n];
            _fillBudget = new int[n];
            _fillFixed = new bool[n];
        }
        for (int i = 0; i < n; i++)
        {
            _fillOwner[i] = -1;
            _fillDist[i] = int.MaxValue;
            _fillFixed[i] = false;
        }
        _buckets ??= new List<int>[MaxBudget + 1];
        foreach (List<int> bucket in _buckets) bucket?.Clear();
        FillKeys.Clear();
        var slots = new Dictionary<string, int>();

        // 刷过的地块：按刷的归属钉死(不当起点，别家也铺不进来)
        if (ScenarioEditing.LandCells.Count > 0)
        {
            var areaOf = new Dictionary<string, string>();
            int offX = Mathf.FloorToInt(_lon0 / ScenarioCells.LandStep + 0.5f * _step / ScenarioCells.LandStep);
            int offY = Mathf.FloorToInt(_lat0 / ScenarioCells.LandStep + 0.5f * _step / ScenarioCells.LandStep);
            foreach (KeyValuePair<int, string> pair in ScenarioEditing.LandCells)
            {
                if (!areaOf.TryGetValue(pair.Value, out string area)) areaOf[pair.Value] = area = PaintArea(pair.Value);
                if (area == null) continue;
                ScenarioCells.Unpack(pair.Key, out int i, out int j);
                int x = i - offX, y = j - offY;
                if (x < 0 || y < 0 || x >= fw || y >= fh) continue;
                int at = y * fw + x;
                _fillOwner[at] = SlotOf(slots, area);
                _fillFixed[at] = true;
            }
        }

        // 城：城所在那格归城，按等级给能铺多远
        foreach (WorldMapCity city in ScenarioEditing.Cities)
        {
            if (!ScenarioEditing.Active(city)) continue;
            string area = AreaKey(city);
            if (area == null) continue;
            Vector2 texel = Texel(city);
            int x = Mathf.FloorToInt(texel.x / FillFactor), y = Mathf.FloorToInt(texel.y / FillFactor);
            if (x < 0 || y < 0 || x >= fw || y >= fh) continue;
            int at = y * fw + x;
            if (_fillDist[at] == 0) continue;
            _fillOwner[at] = SlotOf(slots, area);
            _fillFixed[at] = false;
            _fillDist[at] = 0;
            _fillBudget[at] = RankBudget[Mathf.Clamp(ScenarioEditing.RankOf(city), 0, RankBudget.Length - 1)];
            (_buckets[0] ??= new List<int>()).Add(at);
        }

        // 按代价由近及远铺(代价都是小整数，按桶走)
        for (int d = 0; d <= MaxBudget; d++)
        {
            List<int> bucket = _buckets[d];
            if (bucket == null) continue;
            for (int k = 0; k < bucket.Count; k++)
            {
                int at = bucket[k];
                if (_fillDist[at] != d) continue;
                int x = at % fw, y = at / fw, limit = _fillBudget[at], slot = _fillOwner[at];
                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = x + DirX[dir], ny = y + DirY[dir];
                    if (nx < 0 || ny < 0 || nx >= fw || ny >= fh) continue;
                    int next = ny * fw + nx;
                    if (_fillFixed[next]) continue;
                    int cost = CostAt(nx, ny);
                    if (cost == 0) continue;
                    if (dir >= 4)
                    {
                        // 斜走：两边至少一边是陆地(不从水角上斜穿)
                        if (CostAt(nx, y) == 0 && CostAt(x, ny) == 0) continue;
                        cost = cost * 14 / 10;
                    }
                    int nd = d + cost;
                    if (nd > limit || nd >= _fillDist[next]) continue;
                    _fillDist[next] = nd;
                    _fillOwner[next] = slot;
                    _fillBudget[next] = limit;
                    (_buckets[nd] ??= new List<int>()).Add(next);
                }
            }
        }

        var fills = new Color32[FillKeys.Count];
        var strong = new Color32[FillKeys.Count];
        var edges = new Color32[FillKeys.Count];
        for (int s = 0; s < FillKeys.Count; s++)
        {
            Color32 color = ScenarioEditing.ColorOfKey(FillKeys[s]);
            bool chosen = FillKeys[s] == selected;
            fills[s] = new Color32(color.r, color.g, color.b, (byte)(chosen ? 190 : 110));
            strong[s] = new Color32(color.r, color.g, color.b, (byte)(chosen ? 220 : 165));
            edges[s] = new Color32((byte)(color.r / 3), (byte)(color.g / 3), (byte)(color.b / 3), 235);
        }
        for (int y = 0; y < fh; y++)
        for (int x = 0; x < fw; x++)
        {
            int at = y * fw + x, slot = _fillOwner[at];
            if (slot < 0)
            {
                _fillPixels[at] = Clear;
                continue;
            }
            // 与别的块相接只描右、上一侧；挨着未归属的陆地四边都描
            bool edge = Other(x + 1, y, fw, fh, slot, false) || Other(x, y + 1, fw, fh, slot, false) ||
                        Other(x - 1, y, fw, fh, slot, true) || Other(x, y - 1, fw, fh, slot, true) ||
                        Other(x + 1, y, fw, fh, slot, true) || Other(x, y + 1, fw, fh, slot, true);
            _fillPixels[at] = edge ? edges[slot] : _fillFixed[at] ? strong[slot] : fills[slot];
        }
        _fill.SetPixels32(_fillPixels);
        _fill.Apply();
        _areaKey = int.MinValue;
    }

    private static int SlotOf(Dictionary<string, int> slots, string area)
    {
        if (slots.TryGetValue(area, out int slot)) return slot;
        slots[area] = slot = FillKeys.Count;
        FillKeys.Add(area);
        return slot;
    }

    // 粗格(FillFactor 见方)的地形代价：取它左下那个像素的地形大类
    private static int CostAt(int fx, int fy) => ClassCost[_classes[fy * FillFactor * _w + fx * FillFactor]];

    // 邻格是别的块(unclaimed 为 true 时只看未归属的陆地)
    private static bool Other(int x, int y, int fw, int fh, int slot, bool unclaimed)
    {
        if (x < 0 || y < 0 || x >= fw || y >= fh) return false;
        int other = _fillOwner[y * fw + x];
        return unclaimed ? other < 0 && CostAt(x, y) != 0 : other >= 0 && other != slot;
    }

    // 刷的地块在这一层算哪一块：国家层 p: 照写、r: 取那块法理首都(没有就辖下头一座有主的城)的归属；王国法理层 r: 照写、
    // p: 取那国都城的法理；帝国法理、天下再往上找
    private static string PaintArea(string key)
    {
        if (key == null || key.Length < 3) return null;
        string id = key.Substring(2);
        bool byRealm = key[0] == 'p';
        string region = byRealm ? ScenarioEditing.City(ScenarioEditing.Polity(id)?.capital)?.region : id;
        switch (_layer)
        {
            case Layer.Realm:
                if (byRealm) return ScenarioEditing.Polity(id) == null ? null : key;
                WorldMapCity seat = ScenarioEditing.City(ScenarioEditing.Region(id)?.capital);
                WorldMapPolity owner = ScenarioEditing.OwnerOf(seat) ?? ScenarioEditing.Cities
                    .Where(city => city.region == id && ScenarioEditing.Active(city)).Select(ScenarioEditing.OwnerOf)
                    .FirstOrDefault(polity => polity != null);
                return owner == null ? null : "p:" + owner.id;
            case Layer.Kingdom:
                return ScenarioEditing.Region(region) == null ? null : "r:" + region;
            case Layer.Empire:
                WorldMapCore core = ScenarioEditing.CoreOfRegion(region);
                return core == null ? null : "k:" + core.id;
            case Layer.Tianxia:
                string group = ScenarioEditing.CoreOfRegion(region)?.group;
                return string.IsNullOrEmpty(group) ? null : "t:" + group;
        }
        return null;
    }

    // 城点、长城、关隘、关系连线、法理首都：数据、选中、图层、工具、点的大小变了才重画
    private static void RenderMarks()
    {
        int radius = MarkerRadius();
        int key = ((((ScenarioEditing.Revision * 31 + radius) * 31 + (int)_tool) * 31 + (int)_layer) * 31 +
                   (_cityId?.GetHashCode() ?? 0)) * 31 + (_realmId?.GetHashCode() ?? 0) + (_regionId?.GetHashCode() ?? 0) * 7 +
                  (_coreId?.GetHashCode() ?? 0) * 13 + (_relationFrom?.GetHashCode() ?? 0) * 17 + _wallIndex * 19 + _passIndex * 23 +
                  _tribeIndex * 29;
        if (key == _marksKey) return;
        _marksKey = key;
        global::System.Array.Clear(_markPixels, 0, _markPixels.Length);
        WorldMapEra era = ScenarioEditing.Era;

        // 地图范围：按中号的大小画出框
        if (era.map == null && EarthTerrain.Frames.FirstOrDefault(item => item.id == era.frame) is WorldMapFrame frame)
        {
            Vector2Int size = frame.sizes[Mathf.Min(1, frame.sizes.Length - 1)];
            frame.Bounds(size, out float bottom, out float degrees);
            float top = bottom + size.y * 64f * degrees;
            Vector2 a = Texel(frame.west, bottom), b = Texel(frame.east, bottom), c = Texel(frame.east, top), d = Texel(frame.west, top);
            Line(a, b, FrameRect, 0);
            Line(b, c, FrameRect, 0);
            Line(c, d, FrameRect, 0);
            Line(d, a, FrameRect, 0);
        }

        // 原始部落圈：按文化上色的圈(选中的描金、粗一格)，圈心一点
        List<WorldMapTribes> tribes = ScenarioEditing.Tribes;
        for (int i = 0; i < tribes.Count; i++)
        {
            if (tribes[i]?.at == null || tribes[i].at.Length < 2) continue;
            Vector2 center = Texel(tribes[i].at[0], tribes[i].at[1]);
            bool chosen = i == _tribeIndex && _tool == Tool.Tribe;
            Color32 color = chosen ? Gold : ScenarioEditing.ColorOfKey("c:" + tribes[i].culture);
            Circle(center, tribes[i].radius / _step, color, chosen ? 1 : 0);
            Square(center, Mathf.Max(0, radius - 1), color, Edge);
        }

        // 长城：折线(选中的那道金色)；关隘：小方块(选中的描金边)
        List<WorldMapWall> walls = ScenarioEditing.Walls;
        for (int i = 0; i < walls.Count; i++)
        {
            List<float[]> pts = walls[i].pts ?? new List<float[]>();
            Color32 color = i == _wallIndex && _tool == Tool.Wall ? Gold : WallColor;
            for (int p = 0; p < pts.Count; p++)
            {
                if (pts[p] == null || pts[p].Length < 2) continue;
                Vector2 at = Texel(pts[p][0], pts[p][1]);
                if (p > 0 && pts[p - 1] != null && pts[p - 1].Length >= 2) Line(Texel(pts[p - 1][0], pts[p - 1][1]), at, color, radius > 2 ? 1 : 0);
                if (_tool == Tool.Wall) Square(at, Mathf.Max(0, radius - 1), color, Edge);
            }
        }
        List<WorldMapPass> passes = ScenarioEditing.Passes;
        for (int i = 0; i < passes.Count; i++)
        {
            if (passes[i].at == null || passes[i].at.Length < 2) continue;
            Vector2 at = Texel(passes[i].at[0], passes[i].at[1]);
            Square(at, radius, PassColor, WallColor);
            if (i == _passIndex && _tool == Tool.Pass) Ring(at, radius + 3, Gold);
        }

        // 关系：选中国家(或关系工具里起始的国家)与各国都城之间连线，颜色按种类
        WorldMapPolity focus = ScenarioEditing.Polity(_tool == Tool.Relation ? _relationFrom ?? _realmId : _realmId);
        if (focus != null && _tool is Tool.Realm or Tool.Relation or Tool.Paint)
            foreach (WorldMapRelation relation in ScenarioEditing.RelationsOf(focus))
            {
                WorldMapCity a = ScenarioEditing.City(ScenarioEditing.Polity(relation.a)?.capital);
                WorldMapCity b = ScenarioEditing.City(ScenarioEditing.Polity(relation.b)?.capital);
                if (a != null && b != null) Line(Texel(a), Texel(b), KindColor(relation.kind), radius > 2 ? 1 : 0);
            }

        var capitals = new HashSet<string>(era.polities.Select(polity => polity.capital).Where(id => !string.IsNullOrEmpty(id)));
        WorldMapCity selected = null;
        foreach (WorldMapCity city in ScenarioEditing.Cities)
        {
            if (city.id == _cityId) selected = city;
            bool active = ScenarioEditing.Active(city);
            WorldMapPolity owner = active ? ScenarioEditing.OwnerOf(city) : null;
            Color32 color = !active ? Inactive : _layer == Layer.Realm && owner != null ? ScenarioEditing.ColorOf(owner)
                : _layer is Layer.Kingdom or Layer.Empire or Layer.Tianxia && AreaKey(city) is string area ? ScenarioEditing.ColorOfKey(area)
                : Independent;
            int r = capitals.Contains(city.id) ? radius + 1 : radius;
            if (!active) r = Mathf.Max(0, radius - 1);
            Square(Texel(city), r, color, Edge);
        }
        // 法理首都：王国法理工具下选中法理的首都描青边，帝国法理工具下选中帝国法理的首都描紫边
        if (_tool == Tool.Kingdom && ScenarioEditing.Region(_regionId) is WorldMapRegion region &&
            ScenarioEditing.City(region.capital) is WorldMapCity seat && seat.region == region.id)
            Ring(Texel(seat), radius + 2, RegionCapital);
        if (_tool == Tool.Empire && ScenarioEditing.Core(_coreId) is WorldMapCore core &&
            ScenarioEditing.City(core.capital) is WorldMapCity coreSeat)
            Ring(Texel(coreSeat), radius + 2, CoreCapital);
        if (selected != null) Ring(Texel(selected), radius + 3, Gold);
        _marks.SetPixels32(_markPixels);
        _marks.Apply();
    }

    private static Color32 KindColor(string kind) => kind switch
    {
        "war" => new Color32(230, 60, 50, 255),
        "truce" => new Color32(170, 170, 170, 255),
        "alliance" => new Color32(80, 200, 90, 255),
        "marriage" => new Color32(240, 130, 190, 255),
        "pact" => new Color32(110, 210, 230, 255),
        "protect" => new Color32(90, 130, 240, 255),
        "tribute" => new Color32(250, 200, 70, 255),
        "vassal" => new Color32(170, 100, 230, 255),
        "union" => new Color32(60, 190, 170, 255),
        "friend" => new Color32(170, 240, 140, 255),
        "feud" => new Color32(240, 140, 60, 255),
        _ => Independent
    };

    private static void Square(Vector2 texel, int r, Color32 color, Color32 edge)
    {
        int cx = Mathf.FloorToInt(texel.x), cy = Mathf.FloorToInt(texel.y);
        for (int dy = -r - 1; dy <= r + 1; dy++)
        for (int dx = -r - 1; dx <= r + 1; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
            bool border = Mathf.Abs(dx) > r || Mathf.Abs(dy) > r;
            _markPixels[y * _w + x] = border ? edge : color;
        }
    }

    private static void Circle(Vector2 center, float radius, Color32 color, int thick)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(radius * 0.8f), 24, 400);
        Vector2 last = center + new Vector2(radius, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float angle = i * Mathf.PI * 2f / steps;
            Vector2 at = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Line(last, at, color, thick);
            last = at;
        }
    }

    private static void Ring(Vector2 texel, int r, Color32 color)
    {
        int cx = Mathf.FloorToInt(texel.x), cy = Mathf.FloorToInt(texel.y);
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
            int x = cx + dx, y = cy + dy;
            if (x >= 0 && y >= 0 && x < _w && y < _h) _markPixels[y * _w + x] = color;
        }
    }

    // 直线(按步长逐点)，thick 为向外加几格
    private static void Line(Vector2 from, Vector2 to, Color32 color, int thick)
    {
        int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)));
        if (steps > 4000) return;
        for (int i = 0; i <= steps; i++)
        {
            Vector2 at = steps == 0 ? from : Vector2.Lerp(from, to, i / (float)steps);
            int cx = Mathf.FloorToInt(at.x), cy = Mathf.FloorToInt(at.y);
            for (int dy = -thick; dy <= thick; dy++)
            for (int dx = -thick; dx <= thick; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && y >= 0 && x < _w && y < _h) _markPixels[y * _w + x] = color;
            }
        }
    }

    // 放大后写城名：视口里看得见的城，先写大城，至多 MaxLabels 个
    private static void UpdateLabels()
    {
        int used = 0;
        if (_zoom >= LabelZoom)
        {
            Rect view = _viewport.rect;
            float halfW = view.width / 2f / _zoom, halfH = view.height / 2f / _zoom;
            foreach (WorldMapCity city in ScenarioEditing.Cities.Where(ScenarioEditing.Active).OrderBy(ScenarioEditing.RankOf))
            {
                if (used >= MaxLabels) break;
                Vector2 texel = Texel(city);
                if (Mathf.Abs(texel.x - _center.x) > halfW || Mathf.Abs(texel.y - _center.y) > halfH) continue;
                Text label = PoolText(LabelPool, _labels, used++, 13);
                label.text = ScenarioEditing.CityName(city);
                label.color = city.id == _cityId ? TitleColor : TextColor;
                label.rectTransform.anchoredPosition = (texel - _center) * _zoom + new Vector2(0f, (MarkerRadius() + 2) * _zoom + 8f);
            }
        }
        for (int i = used; i < LabelPool.Count; i++)
            if (LabelPool[i].gameObject.activeSelf) LabelPool[i].gameObject.SetActive(false);
    }

    // 法理图层写名：王国法理写法理名、帝国法理与天下写名，摆在辖下城的正中
    private static void UpdateAreaLabels()
    {
        if (_areaKey != _fillKey)
        {
            _areaKey = _fillKey;
            Areas.Clear();
            if (_layer is Layer.Kingdom or Layer.Empire or Layer.Tianxia)
                foreach (IGrouping<string, WorldMapCity> group in ScenarioEditing.Cities.Where(ScenarioEditing.Active)
                             .Select(city => (city, area: AreaKey(city))).Where(item => item.area != null)
                             .GroupBy(item => item.area, item => item.city))
                {
                    Vector2 sum = Vector2.zero;
                    int count = 0;
                    foreach (WorldMapCity city in group)
                    {
                        sum += Texel(city);
                        count++;
                    }
                    Areas.Add((AreaName(group.Key), sum / Mathf.Max(1, count)));
                }
        }
        int used = 0;
        Rect view = _viewport.rect;
        float halfW = view.width / 2f / _zoom, halfH = view.height / 2f / _zoom;
        foreach ((string text, Vector2 at) in Areas)
        {
            if (used >= MaxAreaLabels) break;
            if (Mathf.Abs(at.x - _center.x) > halfW || Mathf.Abs(at.y - _center.y) > halfH) continue;
            Text label = PoolText(AreaPool, _areaLabels, used++, 16);
            label.text = text;
            label.color = AreaText;
            label.rectTransform.anchoredPosition = (at - _center) * _zoom;
        }
        for (int i = used; i < AreaPool.Count; i++)
            if (AreaPool[i].gameObject.activeSelf) AreaPool[i].gameObject.SetActive(false);
    }

    private static string AreaName(string key)
    {
        string id = key.Substring(2);
        return key[0] switch
        {
            'r' => ScenarioEditing.Region(id)?.TitleName ?? id,
            'k' => ScenarioEditing.Core(id)?.Name ?? id,
            't' => ScenarioEditing.TianxiaName(id),
            _ => ScenarioEditing.Polity(id)?.Name ?? id
        };
    }

    private static Text PoolText(List<Text> pool, RectTransform parent, int index, int size)
    {
        while (pool.Count <= index)
        {
            Text text = Label(parent, "Name", size, TextColor, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(180f, 20f);
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);
            pool.Add(text);
        }
        Text label = pool[index];
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        return label;
    }

    // 缩略图：整张地表每 6 像素取一个，叠国家颜色(图层是国家时)
    private static Color32[] Thumbnail(out int width, out int height)
    {
        const int step = 6;
        width = _w / step;
        height = _h / step;
        if (width <= 0 || height <= 0 || _fillPixels == null) return null;
        bool tint = _layer != Layer.Terrain;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color32 terrain = _terrainPixels[y * step * _w + x * step];
            Color32 fill = _fillPixels[y * step / FillFactor * _fill.width + x * step / FillFactor];
            pixels[y * width + x] = !tint || fill.a == 0 ? terrain : Color32.Lerp(terrain, new Color32(fill.r, fill.g, fill.b, 255), 0.5f);
        }
        return pixels;
    }

    #endregion

    #region 右栏、提示、状态

    private static void RefreshAll()
    {
        if (_root == null || ScenarioEditing.Era == null) return;
        WorldMapEra era = ScenarioEditing.Era;
        // 选中的东西被撤销、删掉了：清掉
        if (ScenarioEditing.City(_cityId) == null) _cityId = null;
        if (ScenarioEditing.Polity(_realmId) == null) _realmId = null;
        if (ScenarioEditing.Polity(_relationFrom) == null) _relationFrom = null;
        if (ScenarioEditing.Region(_regionId) == null) _regionId = null;
        if (ScenarioEditing.Core(_coreId) == null) _coreId = null;
        if (_wallIndex >= ScenarioEditing.Walls.Count) _wallIndex = ScenarioEditing.Walls.Count - 1;
        if (_passIndex >= ScenarioEditing.Passes.Count) _passIndex = -1;
        if (_tribeIndex >= ScenarioEditing.Tribes.Count) _tribeIndex = -1;

        _title.text = string.Format(LM.Get("scenario_editor_title"), era.Name);
        _subtitle.text = era.YearText + (ScenarioEditing.Dirty ? "  " + LM.Get("scenario_editor_unsaved").ColorString("#E9A85B") : "");

        if (_eventIndex >= ScenarioEditing.Events.Count) _eventIndex = ScenarioEditing.Events.Count - 1;
        bool showScenario = _tool is Tool.View or Tool.City;
        bool showCity = _cityId != null && _tool is Tool.City or Tool.Realm or Tool.Paint or Tool.Kingdom or Tool.Empire;
        bool showRealm = _realmId != null && (_tool is Tool.City or Tool.Realm or Tool.Paint or Tool.Relation || _tool == Tool.Land && _layer == Layer.Realm);
        bool showRegion = _regionId != null && _tool is Tool.Kingdom or Tool.Empire;
        bool showCore = _coreId != null && _tool == Tool.Empire;
        _scenarioCard.gameObject.SetActive(showScenario);
        _cityCard.gameObject.SetActive(showCity);
        _realmCard.gameObject.SetActive(showRealm);
        _regionCard.gameObject.SetActive(showRegion);
        _coreCard.gameObject.SetActive(showCore);
        _relationCard.gameObject.SetActive(_tool == Tool.Relation);
        _wallCard.gameObject.SetActive(_tool == Tool.Wall);
        _passCard.gameObject.SetActive(_tool == Tool.Pass && _passIndex >= 0);
        _eventCard.gameObject.SetActive(_tool == Tool.Event);
        _landCard.gameObject.SetActive(_tool == Tool.Land);
        _terrainCard.gameObject.SetActive(_tool == Tool.Terrain);
        _tribeCard.gameObject.SetActive(_tool == Tool.Tribe);

        if (showScenario) RefreshScenario(era);
        if (showCity) RefreshCity(era);
        if (showRealm) RefreshRealm();
        if (showRegion) RefreshRegion();
        if (showCore) RefreshCore();
        if (_tool == Tool.Relation) RefreshRelation();
        if (_tool == Tool.Wall) RefreshWall();
        if (_tool == Tool.Pass && _passIndex >= 0) RefreshPass();
        if (_tool == Tool.Event) RefreshEvent();
        if (_tool == Tool.Land) RefreshLand();
        if (_tool == Tool.Terrain) RefreshTerrain();
        if (_tool == Tool.Tribe) RefreshTribe();
        RefreshButtons();
    }

    private static void RefreshLand()
    {
        string key = BrushKey();
        _landBrushBtn.Set(BrushText(), ViewColor);
        string target = key == null
            ? LM.Get(_layer is Layer.Realm or Layer.Kingdom ? "scenario_editor_land_no_target" : "scenario_editor_land_need_layer")
            : AreaName(key);
        _landInfo.text = string.Format(LM.Get("scenario_editor_land_info"), target, ScenarioEditing.LandCount(key), ScenarioEditing.LandCells.Count);
        _clearLandBtn.Set(LM.Get(Armed("land") ? "scenario_editor_clear_armed" : "scenario_editor_clear_land"), DangerColor,
            ScenarioEditing.LandCells.Count > 0);
    }

    private static void RefreshTerrain()
    {
        _terrainBrushBtn.Set(BrushText(), ViewColor);
        string chosen = TerrainPalette[Mathf.Clamp(_terrainType, 0, TerrainPalette.Length - 1)].type;
        foreach ((string type, Btn button) in TerrainButtons) button.Set(null, type == chosen ? ActiveColor : ToolColor);
        _terrainInfo.text = string.Format(LM.Get("scenario_editor_terrain_info"), ScenarioEditing.TerrainCells.Count);
        _clearTerrainBtn.Set(LM.Get(Armed("terrain") ? "scenario_editor_clear_armed" : "scenario_editor_clear_terrain"), DangerColor,
            ScenarioEditing.TerrainCells.Count > 0);
    }

    private static void RefreshTribe()
    {
        WorldMapTribes tribes = ScenarioEditing.TribesOf(_tribeIndex);
        _tribeCultureBtn.Set(tribes == null ? LM.Get("scenario_editor_none") : CultureName(tribes.culture), ViewColor, tribes != null);
        _tribeCountBtn.Set(tribes == null ? "" : TribeCount(tribes), ViewColor, tribes != null);
        _tribeInfo.text = tribes == null
            ? string.Format(LM.Get("scenario_editor_tribe_none"), ScenarioEditing.Tribes.Count)
            : string.Format(LM.Get("scenario_editor_tribe_info"), tribes.radius.ToString("0.#"), tribes.at[0].ToString("0.0"),
                tribes.at[1].ToString("0.0"), ScenarioEditing.Tribes.Count);
        _deleteTribeBtn.Set(null, DangerColor, tribes != null);
    }

    private static string TribeCount(WorldMapTribes tribes) => tribes.count <= 0 ? LM.Get("scenario_editor_tribe_auto") : tribes.count.ToString();

    private static void RefreshScenario(WorldMapEra era)
    {
        bool imported = era.map != null;
        _mapBtn.Set(LM.Get(imported ? "scenario_editor_map_imported" : "scenario_editor_map_earth"), imported ? ActiveColor : ToolColor);
        _importBox.gameObject.SetActive(imported);
        if (imported)
        {
            bool height = era.map.mode == "height";
            _importModeBtn.Set(LM.Get(height ? "scenario_editor_import_height" : "scenario_editor_import_color"), ViewColor);
            _seaInput.transform.parent.gameObject.SetActive(height);
            SetInput(_seaInput, era.map.sea.ToString("0.00"));
            _importInfo.text = _importError != null
                ? LM.Get(_importError).ColorString("#E9A85B")
                : string.Format(LM.Get("scenario_editor_import_info"), _w, _h);
        }
        _frameBtn.Button.transform.parent.gameObject.SetActive(!imported);
        _frameBtn.Set(string.IsNullOrEmpty(era.frame) ? LM.Get("scenario_editor_frame_any") : LM.Get("world_map_frame_" + era.frame), ViewColor);
        SetInput(_nameInput, era.Name);
        SetInput(_yearInput, era.year.ToString());
        SetInput(_authorInput, era.author);
        SetInput(_descInput, era.Description);
        int cities = ScenarioEditing.Cities.Count(ScenarioEditing.Active);
        _scenarioInfo.text = string.Format(LM.Get("scenario_editor_scenario_info"), era.polities.Count, cities,
            LM.Get(era.OwnWorld ? "scenario_editor_own_world" : "scenario_editor_builtin_world"));
    }

    private static void RefreshCity(WorldMapEra era)
    {
        WorldMapCity city = ScenarioEditing.City(_cityId);
        WorldMapPolity realm = ScenarioEditing.Polity(_realmId);
        SetInput(_cityNameInput, ScenarioEditing.CityName(city));
        WorldMapPolity owner = ScenarioEditing.OwnerOf(city);
        WorldMapRegion region = ScenarioEditing.Region(city.region);
        bool capital = era.polities.Any(polity => polity.capital == city.id);
        string shares = city.share is { Count: > 0 }
            ? "\n" + string.Format(LM.Get("scenario_editor_city_shares"),
                string.Join(LM.Get("list_separator"), city.share.Select(id => ScenarioEditing.Region(id)?.TitleName ?? id)))
            : "";
        _cityInfo.text = string.Format(LM.Get("scenario_editor_city_info"), region?.TitleName ?? city.region,
            owner == null ? LM.Get("scenario_editor_independent") : owner.Name + (capital ? LM.Get("scenario_editor_is_capital") : ""),
            string.Format("{0:0.0}, {1:0.0}", city.lon, city.lat)) + shares;
        _rankBtn.Set(LM.Get("scenario_editor_rank_" + Mathf.Clamp(ScenarioEditing.RankOf(city), 1, 3)), ToolColor);
        _capitalBtn.Set(null, ToolColor, realm != null && realm.capital != city.id);
        _foundBtn.Set(null, ToolColor, !capital);
        _deleteCityBtn.Set(null, DangerColor);
    }

    private static void RefreshRealm()
    {
        WorldMapPolity realm = ScenarioEditing.Polity(_realmId);
        SetInput(_realmNameInput, realm.Name);
        _cultureBtn.Set(CultureName(realm.culture), ViewColor);
        _empireBtn.Set(LM.Get(realm.empire ? "scenario_editor_empire_on" : "scenario_editor_empire_off"), realm.empire ? ActiveColor : ToolColor);
        SetInput(_famInput, realm.ruler?.fam == null ? "" : WorldMapData.PickParts(realm.ruler.fam));
        SetInput(_givInput, realm.ruler?.giv == null ? "" : WorldMapData.PickParts(realm.ruler.giv));
        SetInput(_ageInput, realm.ruler != null && realm.ruler.age > 0 ? realm.ruler.age.ToString() : "");
        WorldMapCity seat = ScenarioEditing.City(realm.capital);
        _realmInfo.text = string.Format(LM.Get("scenario_editor_realm_info"),
            seat == null ? LM.Get("scenario_editor_none") : ScenarioEditing.CityName(seat),
            ScenarioEditing.CityCount(realm), realm.regions?.Count ?? 0);
    }

    private static void RefreshRegion()
    {
        WorldMapRegion region = ScenarioEditing.Region(_regionId);
        WorldMapCity city = ScenarioEditing.City(_cityId);
        SetInput(_regionNameInput, region.Name);
        SetInput(_regionTitleInput, region.TitleName);
        _regionCultureBtn.Set(CultureName(ScenarioEditing.RegionCulture(region)), ViewColor);
        WorldMapCore core = ScenarioEditing.CoreOfRegion(region.id);
        _regionInfo.text = string.Format(LM.Get("scenario_editor_region_info"), ScenarioEditing.RegionCapitalName(region),
            ScenarioEditing.RegionCityCount(region.id), core?.Name ?? LM.Get("scenario_editor_none"));
        _regionCapitalBtn.Set(null, ToolColor, city != null && city.region == region.id && region.capital != city.id);
        bool shared = city?.share?.Contains(region.id) == true;
        _shareBtn.Set(LM.Get(shared ? "scenario_editor_unshare" : "scenario_editor_share"), shared ? ActiveColor : ToolColor,
            city != null && city.region != region.id);
        _newRegionBtn.Set(null, ToolColor, city != null);
        _deleteRegionBtn.Set(null, DangerColor, ScenarioEditing.Cities.All(item => item.region != region.id));
    }

    private static void RefreshCore()
    {
        WorldMapCore core = ScenarioEditing.Core(_coreId);
        WorldMapCity city = ScenarioEditing.City(_cityId);
        SetInput(_coreNameInput, core.Name);
        _coreInfo.text = string.Format(LM.Get("scenario_editor_core_info"), ScenarioEditing.CoreCapitalName(core), core.regions?.Count ?? 0,
            string.Join(LM.Get("list_separator"), (core.regions ?? new List<string>()).Take(8)
                .Select(id => ScenarioEditing.Region(id)?.TitleName ?? id)));
        _coreCapitalBtn.Set(null, ToolColor, city != null && core.regions?.Contains(city.region) == true && core.capital != city.id);
        _newCoreBtn.Set(null, ToolColor, _regionId != null);
        _leaveCoreBtn.Set(null, ToolColor, _regionId != null && core.regions?.Contains(_regionId) == true);
        _deleteCoreBtn.Set(null, DangerColor);
        _tianxiaBtn.Set(ScenarioEditing.TianxiaName(core.group), ViewColor);
        SetInput(_tianxiaNameInput, string.IsNullOrEmpty(core.group) ? "" : ScenarioEditing.TianxiaName(core.group));
        _tianxiaNameInput.interactable = !string.IsNullOrEmpty(core.group);
    }

    private static void RefreshRelation()
    {
        string kind = ScenarioEditing.RelationKinds[_kind];
        _kindBtn.Set(ScenarioEditing.KindName(kind), ViewColor);
        _goalRow.gameObject.SetActive(kind == "war");
        _goalBtn.Set(ScenarioEditing.GoalName(ScenarioEditing.WarGoals[_goal]), ViewColor);
        WorldMapPolity from = ScenarioEditing.Polity(_relationFrom);
        if (from == null)
        {
            _relationInfo.text = LM.Get("scenario_editor_relation_pick");
            return;
        }
        List<WorldMapRelation> relations = ScenarioEditing.RelationsOf(from);
        _relationInfo.text = string.Format(LM.Get("scenario_editor_relation_from"), from.Name) + "\n" +
                             (relations.Count == 0 ? LM.Get("scenario_editor_relation_none")
                                 : string.Join("\n", relations.Take(14).Select(ScenarioEditing.RelationText)));
    }

    private static void RefreshWall()
    {
        List<WorldMapWall> walls = ScenarioEditing.Walls;
        WorldMapWall wall = _wallIndex >= 0 && _wallIndex < walls.Count ? walls[_wallIndex] : null;
        _wallBtn.Set(wall == null ? LM.Get("scenario_editor_none") : ScenarioEditing.WallName(wall), ViewColor, walls.Count > 0);
        SetInput(_wallNameInput, wall == null ? "" : ScenarioEditing.WallName(wall));
        _wallNameInput.interactable = wall != null;
        _wallInfo.text = string.Format(LM.Get("scenario_editor_wall_info"), walls.Count, wall?.pts?.Count ?? 0);
    }

    private static void RefreshPass()
    {
        WorldMapPass pass = ScenarioEditing.Passes[_passIndex];
        SetInput(_passNameInput, pass.Name);
        _passInfo.text = pass.at is { Length: >= 2 } ? string.Format("{0:0.0}, {1:0.0}", pass.at[0], pass.at[1]) : "";
    }

    // 事件：选中的那条重搭一遍(名、史书里的话、开局后第几年、一次或可重复，条件、结果各一条条)
    private static void RefreshEvent()
    {
        List<WorldMapEvent> events = ScenarioEditing.Events;
        WorldMapEvent item = ScenarioEditing.Event(_eventIndex);
        _eventBtn.Set(item == null ? LM.Get("scenario_editor_none") : item.name, ViewColor, events.Count > 0);
        for (int i = _eventBody.childCount - 1; i >= 0; i--) Object.Destroy(_eventBody.GetChild(i).gameObject);
        if (item == null)
        {
            Label(_eventBody, "Empty", 14, DimText, TextAnchor.UpperLeft, true).text = LM.Get("scenario_editor_event_empty");
            return;
        }
        int index = _eventIndex;
        SetInput(Field(_eventBody, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenameEvent(index, text)), item.name);
        SetInput(Field(_eventBody, LM.Get("scenario_editor_event_text"), text => ScenarioEditing.SetEventText(index, text), 52f, true), item.text);
        SetInput(Field(_eventBody, LM.Get("scenario_editor_event_after"), text => ScenarioEditing.SetEventAfter(index, text)), item.after.ToString());
        MakeButton(_eventBody, LM.Get(item.repeat ? "scenario_editor_event_repeat" : "scenario_editor_event_once"),
            item.repeat ? ActiveColor : ToolColor, () => ScenarioEditing.ToggleEventRepeat(index));
        StepList(item.when, false, index);
        StepList(item.then, true, index);
    }

    // 条件或结果：标题、一条条(种类前后换、删；要填的各项)、加一条
    private static void StepList(List<WorldMapEventStep> steps, bool effect, int index)
    {
        Label(_eventBody, "Head", 15, HeadColor, TextAnchor.MiddleLeft).text =
            LM.Get(effect ? "scenario_editor_event_then" : "scenario_editor_event_when");
        for (int j = 0; j < steps.Count; j++)
        {
            WorldMapEventStep step = steps[j];
            int at = j;
            RectTransform head = Child("Step", _eventBody);
            Row(head, 6f, new RectOffset(0, 0, 0, 0));
            MakeButton(head, "<", ViewColor, () => ScenarioEditing.CycleStepKind(index, effect, at, -1), 15, 26f);
            Btn kind = MakeButton(head, LM.Get((effect ? "scenario_event_do_" : "scenario_event_if_") + step.kind), ToolColor,
                () => ScenarioEditing.CycleStepKind(index, effect, at, 1), 14, 26f);
            kind.Button.GetComponent<LayoutElement>().flexibleWidth = 1f;
            MakeButton(head, ">", ViewColor, () => ScenarioEditing.CycleStepKind(index, effect, at, 1), 15, 26f);
            MakeButton(head, "×", DangerColor, () =>
            {
                ScenarioEditing.RemoveStep(index, effect, at);
                _pickSlot = null;
            }, 15, 26f);
            foreach (string slot in ScenarioEditing.SlotsOf(step, effect)) SlotLine(step, effect, index, at, slot);
        }
        MakeButton(_eventBody, LM.Get(effect ? "scenario_editor_event_add_then" : "scenario_editor_event_add_when"), SaveColor,
            () => ScenarioEditing.AddStep(index, effect));
    }

    // 一项：国、城、地区——按钮写着现值，点了等地图上点城；数、国号——输入框；关系种类、战争目标、文化——前后换
    private static void SlotLine(WorldMapEventStep step, bool effect, int index, int at, string slot)
    {
        string caption = LM.Get("scenario_editor_slot_" + slot);
        switch (slot)
        {
            case "a":
            case "b":
            case "city":
            case "region":
            {
                RectTransform line = Child("Slot", _eventBody);
                Row(line, 6f, new RectOffset(12, 0, 0, 0));
                Text label = Label(line, "Caption", 14, DimText, TextAnchor.MiddleLeft);
                label.text = caption;
                var element = label.gameObject.AddComponent<LayoutElement>();
                element.minWidth = element.preferredWidth = 52f;
                string value = slot switch { "a" => step.a, "b" => step.b, "city" => step.city, _ => step.region };
                bool picking = _pickSlot == slot && _pickStep == at && _pickEffect == effect;
                Btn pick = MakeButton(line, picking ? LM.Get("scenario_editor_event_picking") : SlotText(slot, value),
                    picking ? ActiveColor : ViewColor, () =>
                    {
                        _pickSlot = slot;
                        _pickStep = at;
                        _pickEffect = effect;
                        Say(LM.Get("scenario_editor_event_pick_hint"));
                    }, 14, 26f);
                pick.Button.GetComponent<LayoutElement>().flexibleWidth = 1f;
                break;
            }
            case "value":
                SetInput(Field(_eventBody, caption, text => ScenarioEditing.SetStepValue(index, effect, at, text)), step.value.ToString());
                break;
            case "name":
                SetInput(Field(_eventBody, caption, text => ScenarioEditing.SetStepSlot(index, effect, at, "name", text)), step.name);
                break;
            case "relation":
                Cycler(_eventBody, caption, delta => ScenarioEditing.CycleStepRelation(index, at, delta))
                    .Set(ScenarioEditing.KindName(step.relation), ViewColor);
                break;
            case "goal":
                Cycler(_eventBody, caption, delta => ScenarioEditing.CycleStepGoal(index, at, delta))
                    .Set(ScenarioEditing.GoalName(string.IsNullOrEmpty(step.goal) ? ScenarioEditing.WarGoals[0] : step.goal), ViewColor);
                break;
            case "culture":
                Cycler(_eventBody, caption, delta => ScenarioEditing.CycleStepCulture(index, at, delta))
                    .Set(string.IsNullOrEmpty(step.culture) ? LM.Get("scenario_editor_none") : CultureName(step.culture), ViewColor);
                break;
        }
    }

    // 国、城、地区的现值怎么写
    private static string SlotText(string slot, string value)
    {
        if (string.IsNullOrEmpty(value)) return LM.Get("scenario_editor_event_unset");
        return slot switch
        {
            "a" or "b" => ScenarioEditing.Polity(value)?.Name ?? value,
            "city" => ScenarioEditing.City(value) is WorldMapCity city ? ScenarioEditing.CityName(city) : value,
            "region" => ScenarioEditing.Region(value)?.TitleName ?? value,
            _ => value
        };
    }

    private static string CultureName(string culture) =>
        CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture ?? "";

    private static void SetInput(InputField input, string value)
    {
        if (input == null || input.isFocused) return;
        if (input.text != (value ?? "")) input.text = value ?? "";
    }

    private static void RefreshButtons()
    {
        if (_closeBtn == null) return;
        foreach ((Tool tool, Btn button) in ToolButtons) button.Set(null, _tool == tool ? ActiveColor : ToolColor);
        foreach ((Layer layer, Btn button) in LayerButtons) button.Set(null, _layer == layer ? ActiveColor : ViewColor);
        _undoBtn.Set(null, ViewColor, ScenarioEditing.CanUndo);
        _saveBtn.Set(null, ScenarioEditing.Dirty ? ActiveColor : SaveColor);
        bool armed = _closeArmedAt > 0f && Time.unscaledTime - _closeArmedAt <= ConfirmSeconds;
        _closeBtn.Set(LM.Get(armed ? "scenario_editor_close_armed" : "scenario_editor_close"), armed ? DangerColor : ViewColor);
    }

    private static void UpdateTip()
    {
        WorldMapCity city = ScenarioEditing.City(_hoverCity);
        string text = null;
        if (city != null)
        {
            WorldMapPolity owner = ScenarioEditing.OwnerOf(city);
            string area = _layer is Layer.Kingdom or Layer.Empire or Layer.Tianxia && AreaKey(city) is string key ? "  " + AreaName(key) : "";
            text = ScenarioEditing.CityName(city) + "  " +
                   (owner == null ? LM.Get("scenario_editor_independent") : owner.Name).ColorString("#7FD8EA") + area.ColorString("#F3C34A");
        }
        else if (_hoverPass >= 0 && _hoverPass < ScenarioEditing.Passes.Count) text = ScenarioEditing.Passes[_hoverPass].Name;
        else if (ScenarioEditing.TribesOf(_hoverTribe) is WorldMapTribes tribes)
            text = string.Format(LM.Get("scenario_editor_tribe_tip"), CultureName(tribes.culture), TribeCount(tribes));
        if (text == null || _dragButton >= 0)
        {
            if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
            return;
        }
        _tipText.text = text;
        if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, null, out Vector2 local))
            _tip.anchoredPosition = local + new Vector2(16f, -12f);
    }

    private static void UpdateStatus()
    {
        string text;
        if (_message != null && Time.unscaledTime - _messageAt <= MessageSeconds) text = _message;
        else if (_tool == Tool.Paint && ScenarioEditing.Polity(_realmId) is WorldMapPolity brush)
            text = string.Format(LM.Get("scenario_editor_hint_paint_realm"), brush.Name);
        else text = LM.Get("scenario_editor_hint_" + _tool.ToString().ToLowerInvariant());
        if (_status.text != text) _status.text = text;
    }

    #endregion
}

public sealed class ScenarioEditorHost : MonoBehaviour
{
    private void Update() => ScenarioEditorScreen.Tick(this);
}
