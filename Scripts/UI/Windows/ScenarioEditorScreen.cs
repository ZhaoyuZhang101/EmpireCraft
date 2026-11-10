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
//   行政区与无人区(用户 10-30 "都做还有新的无人区在设置城市和刷地块的时候也要考虑到")：
//     「行政区」工具与图层：点一国的城选中这国，右栏选或新立行政区、改名、删；点或拖过本国的城划进选中的行政区(都城留在京畿，
//     划不进去)，右键点城放回京畿，Shift+点城取它所在的行政区；「点图加治所」开着时点空处加当年州府治所(开局按治所划地界)，
//     右键点治所去掉。行政区只对开局称帝的国家生效(同 WorldMapScenario)。图层上同一国各省分色，省名写在辖下城的正中；
//     「无人区」图层：按开局的划区规矩在预览上预测无人区(见 ScenarioWilderness)，按类型上色，开发度高的淡些，永久中立的打斜线；
//     国家、行政区、法理图层上无人区不算城的地，从城往外铺时铺不进去，按归属规矩上色(挂的法理归谁、周边的城同属哪国)并打斜线，
//     中立的只打灰斜线；城心那块照样归城；悬停无人区提示类型、开发度、挂哪个法理、归谁；放城、挪城到无人区里另有提示；
//     刷地块刷在无人区上定它挂哪个法理，另有「永久中立区」笔刷(键 n)；刷地形另有「开发度」笔刷(雨林大泽 0~100，0.2° 一格)。
//   操作统一(用户 2026-11-05 "用户都搞不懂怎么用"，审查 15 条三批都做)：左键做事、右键去掉、Shift+点取目标，右键拖、中键拖、空格+左键拖在各工具
//     里都能平移(刷地块、刷地形的右键是擦)；国家、王国法理、帝国法理、行政区四个工具右栏同一张「点地图时」卡：默认只选中，选了「划入」才改归属；
//     工具按版图、法理、外交、地理分组，右栏顶上有上手步骤。
//   预览等于开局(见 ScenarioWilderness.Preview)：图层颜色按块算，同开局的先后(城心、刷的地块、一起往外铺、不过长城、不进无人区)；独立的城也铺、
//     铺不到的地标明补小国；地块两种画法(方形、平滑)；放不下的城标红；碰到哪块地，哪块就整块归这一笔。
// 数据一侧见 ScenarioEditing。Esc 关闭(输入框里打字时不算)。
public static class ScenarioEditorScreen
{
    private enum Tool { View, City, Realm, Land, Kingdom, Empire, Relation, Wall, Pass, Event, Tribe, Terrain, Division }
    private enum Layer { Terrain, Realm, Kingdom, Empire, Tianxia, Division, Wild }
    // 国家、王国法理、帝国法理、行政区工具点地图时做什么：只选中、划入选中的那一块、设为兼属(只有王国法理)
    private enum Mode { Select, Write, Share }
    // 刷地块刷给谁
    private enum LandMode { Realm, Region, City, Neutral }

    // 工具分组(按做剧本的先后)
    private static readonly (string key, Tool[] tools)[] ToolGroups =
    {
        ("map", new[] { Tool.View, Tool.City, Tool.Realm, Tool.Land, Tool.Division }),
        ("title", new[] { Tool.Kingdom, Tool.Empire }),
        ("story", new[] { Tool.Relation, Tool.Event }),
        ("land", new[] { Tool.Terrain, Tool.Wall, Tool.Pass, Tool.Tribe })
    };

    private const float RefWidth = 1600f, RefHeight = 900f;
    private const float HeaderHeight = 46f, StatusHeight = 30f, Margin = 10f, SideWidth = 340f, ToolbarHeight = 72f;
    private const float ConfirmSeconds = 5f, MessageSeconds = 6f, ClickSlop = 6f;
    // 地形预览每几格取一格(网格 0.05°，取 2 即 0.1°)；图层颜色再粗一倍；从城往外铺至多几格(粗格)
    private const int Stride = 2, FillFactor = 2;
    // 部落圈至少多大(度)、拖着刷时图层颜色至多几秒重铺一次；笔刷大小和形状见 ScenarioBrush
    private const float MinTribeRadius = 0.3f, FillThrottle = 0.2f;
    // 水面不上色：各地形大类(见 EarthTerrain.ClassOf)是不是陆地，0 为水
    private static readonly int[] ClassCost = { 0, 10, 13, 20, 45, 70, 14, 30 };
    // 地块网格：一块地在屏幕上至少多大才画；平滑地块投票时各家无人区、水面的类别
    private const float ZoneGridMinScreen = 10f;
    // 比预设最大一档还大得多的图提醒一句(区块数)
    private const int BigMapChunks = 1024;
    private const int WildClass = -10, SkipClass = int.MinValue;

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
        [Tool.Land] = "ui/icons/iconBorderBrush",
        [Tool.Kingdom] = "ui/icons/iconToolTitleLayer", [Tool.Empire] = "ui/icons/iconToolCoreLayer", [Tool.Relation] = "ui/icons/iconDiplomacy",
        [Tool.Wall] = "ui/icons/iconWallOrder", [Tool.Pass] = "ui/icons/godpowers/place_fort", [Tool.Event] = "ui/icons/iconWorldLog",
        [Tool.Tribe] = "ui/icons/godpowers/place_village", [Tool.Terrain] = "ui/icons/iconTileHills",
        [Tool.Division] = "ui/icons/iconToolProvinceCreate"
    };

    private static readonly Dictionary<Layer, string> LayerIcons = new()
    {
        [Layer.Terrain] = "ui/icons/iconTileMountains", [Layer.Realm] = "ui/icons/iconNationLayer", [Layer.Kingdom] = "ui/icons/iconTitleLayer",
        [Layer.Empire] = "ui/icons/godpowers/empire_layer", [Layer.Tianxia] = "ui/icons/godpowers/tianxia_layer",
        [Layer.Division] = "ui/icons/godpowers/governance_layer", [Layer.Wild] = "ui/icons/godpowers/wild_neutral"
    };
    // 城点在屏幕上约多大(画布单位)；放大到一格多少单位以上写城名、至多写几个；最大放大到一格多少单位
    private const float MarkerScreen = 7f, LabelZoom = 2.2f, MaxZoom = 12f;
    private const int MaxLabels = 140, MaxAreaLabels = 80;
    // 无人区图层写类型名：至少几块的那几片
    private const int MinLabelZones = 6;

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
    // 没主的无人区、永久中立区的灰斜线与底
    private static readonly Color32 NeutralStripe = new Color32(70, 70, 70, 190), NeutralFill = new Color32(150, 150, 150, 40);
    // 开局补小国或并给邻国的地、开局放不下的城
    private static readonly Color32 FillerStripe = new Color32(215, 195, 130, 150), FillerFill = new Color32(215, 195, 130, 40);
    private static readonly Color32 Unplaced = new Color32(235, 70, 55, 255);

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
    private static Btn _regionCultureBtn, _regionCapitalBtn, _newRegionBtn, _deleteRegionBtn;
    // 上手步骤；点地图时(选中、划入、兼属)；选国家、法理的前后翻；认领法理；地块画法；帮助
    private const int GuideSteps = 6;
    private const string GuidePref = "EmpireCraft.ScenarioGuideClosed";
    private static readonly Btn[] GuideBtns = new Btn[GuideSteps];
    private static RectTransform _guideCard, _modeCard;
    private static bool _guideOpen = true, _kingdomVisited;
    private static Btn _modeSelectBtn, _modeWriteBtn, _modeShareBtn, _realmPickBtn, _regionPickBtn, _corePickBtn, _claimBtn, _styleBtn;
    private static Text _modeInfo;
    private static Mode _mode = Mode.Select;
    // 剧本卡：地图长宽、照原图分地、本剧本无人区、清空全部城
    private static InputField _sizeWInput, _sizeHInput;
    private static Text _sizeInfo, _frameInfo, _exactInfo;
    private static Btn _exactBtn, _scenarioWildBtn, _clearCitiesBtn;
    private static int _scenarioVersion = int.MinValue;
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
    private static Btn _landShapeBtn, _terrainShapeBtn, _landModeBtn;
    private static LandMode _landMode = LandMode.Realm;
    private static string _landCityId;
    // 这一笔碰到了哪些块地、有没有刷到地图外头
    private static readonly HashSet<int> StrokeZones = new(), ZoneExtra = new();
    private static bool _strokeOutside;
    private static RectTransform _landCard, _terrainCard, _tribeCard;
    private static Text _landInfo, _terrainInfo, _tribeInfo;
    private static readonly List<(string type, Btn button)> TerrainButtons = new();
    private static ScenarioBrush.Shape _brushShape;
    private static readonly Dictionary<(ScenarioBrush.Shape, int), Texture2D> BrushOutlines = new();
    private static int _brushSize = ScenarioBrush.DefaultSize, _terrainType = 4, _tribeIndex = -1, _brushButton = -1, _pressTribe = -1, _hoverTribe = -1, _terrainApplied = -1;
    private static Vector2 _brushLast, _pressTexel;
    private static float _fillAt = -100f, _terrainUploadAt = -100f, _clearArmedAt = -100f;
    private static bool _terrainDirty;
    private static string _clearArmed;
    private static RawImage _ring;
    private static Texture2D _ringTexture;
    private static readonly HashSet<int> BrushCells = new();
    private static readonly List<int> BrushTexels = new();
    // 图层颜色复用的数组；FillKeys 为各块的键(国家 p:、独立的城 i:、王国法理 r:、帝国法理 k:、天下 t:)
    // _fillCity：粗格归哪座城(预测的 Seats 下标)，城与城相接处描细线(城市边界)
    private static int[] _fillOwner, _fillCity;
    private static bool[] _fillFixed;
    private static readonly List<string> FillKeys = new();
    // 各块地开局归谁(见 ScenarioWilderness.Preview)、在当前图层算哪一块、平滑地块投票的类别；Seats 下标 → 城
    private static int[] _zoneOwner, _zoneSlot, _zoneClass;
    private static bool[] _zonePainted, _zoneIsWild;
    private static WorldMapCity[] _seatCities = new WorldMapCity[0];
    private static int _zoneOwnerKey = int.MinValue;
    private static bool _smoothFill;
    private static readonly int[] VoteZones = new int[4];
    private static readonly float[] VoteWeights = new float[4];
    // 刷地块时盖在图上的地块网格
    private static RawImage _zoneGrid;
    private static Texture2D _zoneGridTexture;
    // 事件卡只在内容变了、鼠标松开之后才重搭(按着的按钮一重搭就没了，点击白点)
    private static string _eventSignature;
    private static bool _eventPending;
    private static int _mouseUpFrame;
    // 行政区：选中国家(_realmId)的第几个行政区(-1 为京畿直辖)；「点图加治所」开着时左键点空处加治所，右键点治所去掉
    private static int _divisionIndex = -1;
    private static bool _seatMode;
    private static RectTransform _divisionCard;
    // 国祚与积弊卡：各项的输入框与框里的灰字(默认值，见 RefreshDecline)
    private static RectTransform _declineCard;
    private static Text _declineInfo;
    private static Btn _declineClearBtn;
    private static readonly Dictionary<string, (InputField input, Text hint)> DeclineInputs = new();
    private static Text _divisionHead, _divisionInfo;
    private static Btn _divisionBtn, _newDivisionBtn, _deleteDivisionBtn, _seatModeBtn, _clearSeatsBtn;
    private static InputField _divisionNameInput;
    private static (int division, int seat) _hoverSeat = (-2, -1), _pressSeat = (-2, -1);
    // 无人区预测(见 ScenarioWilderness)：右栏卡片(预测按哪档大小、几片几块)；刷地块的「永久中立区」笔刷；刷地形的「开发度」笔刷与刷的数
    private static RectTransform _wildCard, _terrainPalette;
    private static Text _wildInfo;
    private static Btn _wildSizeBtn, _wildOwnBtn, _developBrushBtn, _developValueBtn, _clearDevelopBtn;
    private static bool _developBrush;
    private static int _developValue = 30, _wildInfoVersion = int.MinValue;
    // 预览各像素的地形标记(给无人区预测，见 ScenarioWilderness.CellOf)：地球的烘一次留着，工作的叠刷的地形，改一次 +1
    private static byte[] _earthCells, _baseCells, _cells;
    private static int _cellsRevision;
    private static WorldMapFrame _importFrame;
    // 图层粗格落在预测的哪块地上(-1 在范围外)、是不是无人区、斜线(1 无人区、2 刷的中立区)
    private static int[] _fillZone;
    private static int _fillZoneVersion = int.MinValue;
    private static bool[] _fillWild;
    private static byte[] _fillHatch;
    // 光标在地图上哪儿(悬停提示无人区用)
    private static Vector2 _hoverTexel;
    private static bool _hoverOnMap;

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
            ScenarioWilderness.Reset();
            if (!EnsureTerrain())
            {
                WorldTip.showNow("world_map_data_missing", true, "top", 3f);
                ScenarioEditing.Close();
                return;
            }
            _cityId = _realmId = _regionId = _coreId = _relationFrom = _landCityId = null;
            _wallIndex = _passIndex = _tribeIndex = _divisionIndex = -1;
            _seatMode = _developBrush = _kingdomVisited = false;
            _mode = Mode.Select;
            _landMode = LandMode.Realm;
            _smoothFill = SmoothBorders.Enabled;
            _guideOpen = PlayerPrefs.GetInt(GuidePref, 0) == 0;
            // 照原图分地的旧剧本没记大小：一像素一格就是小档，别按中档把地块全打乱
            if (ScenarioEditing.Era.exact && !ScenarioEditing.Era.HasSize && ScenarioEditing.UsesImport) ScenarioWilderness.SizeIndex = 0;
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
        if (_zoneGridTexture != null) Object.Destroy(_zoneGridTexture);
        _workTexture = _ringTexture = _zoneGridTexture = null;
        _zoneGrid = null;
        _ring = null;
        _terrainPixels = _basePixels = null;
        _land = _baseLand = null;
        _classes = _baseClasses = null;
        _cells = _baseCells = null;
        _importFrame = null;
        _fillOwner = _fillCity = null;
        _zoneOwner = _zoneSlot = _zoneClass = null;
        _zonePainted = _zoneIsWild = null;
        _seatCities = new WorldMapCity[0];
        _zoneOwnerKey = _scenarioVersion = int.MinValue;
        _eventSignature = null;
        _eventPending = false;
        _fillFixed = null;
        _fillZone = null;
        _fillWild = null;
        _fillHatch = null;
        _fillZoneVersion = _wildInfoVersion = int.MinValue;
        ScenarioWilderness.Reset();
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
            _baseCells = ScenarioWilderness.CellsOf(EarthTerrain.PreviewCodes(1));
            _importFrame = loaded.frame;
        }
        else
        {
            EarthTerrain.UseEarth();
            if (_earthPixels == null || _earthClasses == null || _earthCells == null)
            {
                if (!EarthTerrain.Preview(Stride, out _earthPixels, out _earthLand, out _earthW, out _earthH, out _earthLon0, out _earthLat0,
                        out _earthStep)) return false;
                _earthClasses = EarthTerrain.PreviewClasses(Stride);
                _earthCells = ScenarioWilderness.CellsOf(EarthTerrain.PreviewCodes(Stride));
            }
            _basePixels = _earthPixels;
            _baseLand = _earthLand;
            _baseClasses = _earthClasses;
            _baseCells = _earthCells;
            _importFrame = null;
            _w = _earthW;
            _h = _earthH;
            _lon0 = _earthLon0;
            _lat0 = _earthLat0;
            _step = _earthStep;
        }
        if (_basePixels == null || _baseClasses == null || _baseCells == null) return false;
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
        _cells = (byte[])_baseCells.Clone();
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
        GridChanged();
    }

    // 地形标记改了(整张或刷的几格)：交给无人区预测(停手后重算)
    private static void GridChanged()
    {
        _cellsRevision++;
        ScenarioWilderness.SetGrid(_cells, _w, _h, _lon0, _lat0, _step, _importFrame, _cellsRevision);
    }

    // 一个像素换成某种地表(type 为空就复原底图)
    private static void SetTexel(int index, string type)
    {
        if (type == null)
        {
            _terrainPixels[index] = _basePixels[index];
            _land[index] = _baseLand[index];
            _classes[index] = _baseClasses[index];
            _cells[index] = _baseCells[index];
            return;
        }
        _terrainPixels[index] = EarthTerrain.PreviewColorOf(type);
        _classes[index] = EarthTerrain.ClassOf(type);
        _land[index] = _classes[index] != 0;
        _cells[index] = ScenarioWilderness.CellOf(type);
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
        _fillZoneVersion = int.MinValue;
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
        MakeButton(header, LM.Get("scenario_editor_help"), ViewColor, () => SetGuide(!_guideOpen), 16, 32f);
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
        foreach ((string key, Tool[] group) in ToolGroups)
        {
            RowTitle(tools, LM.Get("scenario_editor_group_" + key));
            foreach (Tool tool in group)
            {
                Tool choice = tool;
                ToolButtons.Add((choice, MakeButton(tools, LM.Get("scenario_editor_tool_" + choice.ToString().ToLowerInvariant()), ToolColor,
                    () => SetTool(choice), 13, 28f, Icon(ToolIcons[choice]))));
            }
        }

        RectTransform layers = Child("Layers", _mapArea);
        Place(layers, new Vector2(0f, 1f), Vector2.one, new Vector2(8f, -ToolbarHeight), new Vector2(-8f, -40f));
        Row(layers, 5f, new RectOffset(0, 0, 0, 0));
        RowTitle(layers, LM.Get("scenario_editor_row_layers"));
        foreach (Layer layer in new[] { Layer.Terrain, Layer.Wild, Layer.Realm, Layer.Division, Layer.Kingdom, Layer.Empire, Layer.Tianxia })
        {
            Layer choice = layer;
            LayerButtons.Add((choice, MakeButton(layers, LM.Get("scenario_editor_layer_" + choice.ToString().ToLowerInvariant()), ViewColor,
                () => SetLayer(choice), 13, 28f, Icon(LayerIcons[choice]))));
        }
        Spacer(layers);
        _styleBtn = MakeButton(layers, "", ViewColor, () => _smoothFill = !_smoothFill, 13, 28f);
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
        RectTransform zoneGrid = Child("ZoneGrid", preview);
        _zoneGrid = zoneGrid.gameObject.AddComponent<RawImage>();
        _zoneGrid.raycastTarget = false;
        _zoneGrid.texture = ZoneGridTexture();
        _zoneGrid.color = new Color(1f, 1f, 1f, 0.3f);
        zoneGrid.gameObject.SetActive(false);
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

        // 上手步骤：每步一个按钮，点了切到那一步的工具，做完打勾
        _guideCard = Card(side, LM.Get("scenario_editor_card_guide"));
        for (int i = 0; i < GuideSteps; i++)
        {
            int step = i;
            GuideBtns[i] = MakeButton(_guideCard, "", ViewColor, () => GuideGo(step), 14, 26f);
        }
        Label(_guideCard, "Mouse", 13, DimText, TextAnchor.UpperLeft, true).text = LM.Get("scenario_editor_guide_mouse");
        MakeButton(_guideCard, LM.Get("scenario_editor_guide_close"), ViewColor, () => SetGuide(false), 13, 24f);

        // 点地图时：国家、王国法理、帝国法理、行政区工具默认只选中，选了划入才改归属
        _modeCard = Card(side, LM.Get("scenario_editor_card_mode"));
        RectTransform modeRow = Columns(_modeCard);
        _modeSelectBtn = MakeButton(modeRow, LM.Get("scenario_editor_mode_select"), ToolColor, () => _mode = Mode.Select);
        _modeWriteBtn = MakeButton(modeRow, "", ToolColor, () => _mode = Mode.Write);
        _modeShareBtn = MakeButton(modeRow, LM.Get("scenario_editor_mode_share"), ToolColor, () => _mode = Mode.Share);
        _modeInfo = Label(_modeCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);

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
        _frameInfo = Label(_scenarioCard, "FrameInfo", 14, DimText, TextAnchor.UpperLeft, true);
        // 地图大小：直接填长宽(格)就定死，空着开局时选大中小
        _sizeWInput = Field(_scenarioCard, LM.Get("scenario_editor_map_width"), text => ScenarioEditing.SetMapSide(false, text));
        _sizeHInput = Field(_scenarioCard, LM.Get("scenario_editor_map_height"), text => ScenarioEditing.SetMapSide(true, text));
        _sizeInfo = Label(_scenarioCard, "SizeInfo", 14, DimText, TextAnchor.UpperLeft, true);
        _exactBtn = MakeButton(_scenarioCard, "", ToolColor, ScenarioEditing.ToggleExact);
        _exactInfo = Label(_scenarioCard, "ExactInfo", 14, DimText, TextAnchor.UpperLeft, true);
        _scenarioWildBtn = MakeButton(_scenarioCard, "", ToolColor, ScenarioEditing.ToggleOwnWilderness);
        _clearCitiesBtn = MakeButton(_scenarioCard, "", DangerColor, () => ArmClear("cities"));

        // 无人区(预测)：按哪档大小预测(世界地图窗口里生成时选的那档)、预测出几片几块、开发度按哪个时代的表
        _wildCard = Card(side, LM.Get("scenario_editor_card_wild"));
        _wildSizeBtn = Cycler(_wildCard, LM.Get("scenario_editor_wild_size"), step => ScenarioWilderness.SizeIndex = ((ScenarioWilderness.SizeIndex + step) % 3 + 3) % 3);
        _wildOwnBtn = Cycler(_wildCard, LM.Get("scenario_editor_wild_own"), _ => ScenarioEditing.ToggleOwnWilderness());
        _wildInfo = Label(_wildCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);

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
        _realmPickBtn = Cycler(_realmCard, LM.Get("scenario_editor_realm_pick"), StepRealm);
        _realmNameInput = Field(_realmCard, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenamePolity(ScenarioEditing.Polity(_realmId), text));
        _cultureBtn = Cycler(_realmCard, LM.Get("scenario_editor_culture"), step => ScenarioEditing.CycleCulture(ScenarioEditing.Polity(_realmId), step));
        _empireBtn = MakeButton(_realmCard, "", ToolColor, () => ScenarioEditing.ToggleEmpire(ScenarioEditing.Polity(_realmId)));
        _famInput = Field(_realmCard, LM.Get("scenario_editor_ruler_family"), text => ScenarioEditing.SetRuler(ScenarioEditing.Polity(_realmId), text, null, null));
        _givInput = Field(_realmCard, LM.Get("scenario_editor_ruler_given"), text => ScenarioEditing.SetRuler(ScenarioEditing.Polity(_realmId), null, text, null));
        _ageInput = Field(_realmCard, LM.Get("scenario_editor_ruler_age"), text => ScenarioEditing.SetRuler(ScenarioEditing.Polity(_realmId), null, null, text));
        _realmInfo = Label(_realmCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _claimBtn = MakeButton(_realmCard, "", ToolColor, () => Say(ScenarioEditing.ToggleClaim(ScenarioEditing.Polity(_realmId), ScenarioEditing.City(_cityId)?.region)));
        _deleteRealmBtn = MakeButton(_realmCard, LM.Get("scenario_editor_delete_realm"), DangerColor, ArmDeleteRealm);

        // 国祚与积弊：开局国祚、王朝积弊、六弊(写进国家的 decline，见 ScenarioEditing.SetDecline)；空着照默认，框里灰字是默认值。
        // 只对开局称帝的国家生效
        _declineCard = Card(side, LM.Get("scenario_editor_card_decline"));
        foreach (string key in ScenarioEditing.DeclineKeys)
        {
            string field = key;
            InputField input = Field(_declineCard, LM.Get(DeclineCaption(field)),
                text => ScenarioEditing.SetDecline(ScenarioEditing.Polity(_realmId), field, text));
            DeclineInputs[field] = (input, Placeholder(input));
        }
        _declineInfo = Label(_declineCard, "Info", 13, DimText, TextAnchor.UpperLeft, true);
        _declineClearBtn = MakeButton(_declineCard, LM.Get("scenario_editor_decline_clear"), ToolColor,
            () => Say(ScenarioEditing.ClearDecline(ScenarioEditing.Polity(_realmId))));

        // 王国法理：地区名、法理名、文化、首都、兼属
        _regionCard = Card(side, LM.Get("scenario_editor_card_region"));
        _regionPickBtn = Cycler(_regionCard, LM.Get("scenario_editor_region_pick"), StepRegion);
        _regionNameInput = Field(_regionCard, LM.Get("scenario_editor_region_name"), text => ScenarioEditing.RenameRegion(_regionId, text));
        _regionTitleInput = Field(_regionCard, LM.Get("scenario_editor_region_title"), text => ScenarioEditing.SetRegionTitle(_regionId, text));
        _regionCultureBtn = Cycler(_regionCard, LM.Get("scenario_editor_culture"), step => ScenarioEditing.CycleRegionCulture(_regionId, step));
        _regionInfo = Label(_regionCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform regionActions = Columns(_regionCard);
        _regionCapitalBtn = MakeButton(regionActions, LM.Get("scenario_editor_title_capital"), ToolColor,
            () => Say(ScenarioEditing.SetRegionCapital(_regionId, _cityId)));
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
        _corePickBtn = Cycler(_coreCard, LM.Get("scenario_editor_core_pick"), StepCore);
        _coreNameInput = Field(_coreCard, LM.Get("scenario_editor_name"), text => ScenarioEditing.RenameCore(_coreId, text));
        _coreInfo = Label(_coreCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform coreActions = Columns(_coreCard);
        _coreCapitalBtn = MakeButton(coreActions, LM.Get("scenario_editor_core_capital"), ToolColor,
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

        // 行政区：选中国家的哪个行政区(京畿在头)、名、城与治所；新行政区、删、点图加治所、清掉治所
        _divisionCard = Card(side, LM.Get("scenario_editor_card_division"));
        _divisionHead = Label(_divisionCard, "Head", 14, TextColor, TextAnchor.UpperLeft, true);
        _divisionBtn = Cycler(_divisionCard, LM.Get("scenario_editor_division_pick_label"), step =>
        {
            int count = ScenarioEditing.Divisions(ScenarioEditing.Polity(_realmId)).Count + 1;
            _divisionIndex = ((_divisionIndex + 1 + step) % count + count) % count - 1;
        });
        _divisionNameInput = Field(_divisionCard, LM.Get("scenario_editor_name"),
            text => ScenarioEditing.RenameDivision(ScenarioEditing.Polity(_realmId), _divisionIndex, text));
        _divisionInfo = Label(_divisionCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        RectTransform divisionActions = Columns(_divisionCard);
        _newDivisionBtn = MakeButton(divisionActions, LM.Get("scenario_editor_new_division_btn"), ToolColor, () =>
        {
            WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
            if (polity == null) return;
            _divisionIndex = ScenarioEditing.NewDivision(polity);
            // 新立了行政区多半就是要往里划城
            _mode = Mode.Write;
            Say(LM.Get("scenario_editor_division_created"));
        });
        _deleteDivisionBtn = MakeButton(divisionActions, LM.Get("scenario_editor_delete_division"), DangerColor, () =>
        {
            ScenarioEditing.DeleteDivision(ScenarioEditing.Polity(_realmId), _divisionIndex);
            _divisionIndex = -1;
        });
        _seatModeBtn = MakeButton(divisionActions, "", ToolColor, () => _seatMode = !_seatMode);
        _clearSeatsBtn = MakeButton(divisionActions, LM.Get("scenario_editor_clear_seats"), DangerColor,
            () => ScenarioEditing.ClearSeats(ScenarioEditing.Polity(_realmId), _divisionIndex));

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
            // 不选上一道：再点地图是另起一道，不从上一道的末尾拉线
            _wallIndex = -1;
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

        // 刷地块：刷给谁(或永久中立区)、笔刷粗细、清掉全部
        _landCard = Card(side, LM.Get("scenario_editor_card_land"));
        _landBrushBtn = Cycler(_landCard, LM.Get("scenario_editor_brush"), StepBrush);
        _landShapeBtn = Cycler(_landCard, LM.Get("scenario_editor_brush_shape"), StepShape);
        _landModeBtn = Cycler(_landCard, LM.Get("scenario_editor_land_mode"), StepLandMode);
        _landInfo = Label(_landCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _clearLandBtn = MakeButton(_landCard, LM.Get("scenario_editor_clear_land"), DangerColor, () => ArmClear("land"));

        // 刷地形：笔刷粗细、地形/开发度、十六种地表(或开发度刷几)、清掉全部
        _terrainCard = Card(side, LM.Get("scenario_editor_card_terrain"));
        _terrainBrushBtn = Cycler(_terrainCard, LM.Get("scenario_editor_brush"), StepBrush);
        _terrainShapeBtn = Cycler(_terrainCard, LM.Get("scenario_editor_brush_shape"), StepShape);
        _developBrushBtn = MakeButton(_terrainCard, "", ToolColor, () =>
        {
            _developBrush = !_developBrush;
            _layer = _developBrush ? Layer.Wild : Layer.Terrain;
        });
        _developValueBtn = Cycler(_terrainCard, LM.Get("scenario_editor_develop_value"), step => _developValue = ((_developValue / 10 + step) % 11 + 11) % 11 * 10);
        RectTransform palette = Columns(_terrainCard);
        _terrainPalette = palette;
        for (int i = 0; i < TerrainPalette.Length; i++)
        {
            int index = i;
            TerrainButtons.Add((TerrainPalette[i].type, MakeButton(palette, LM.Get("scenario_terrain_" + TerrainPalette[i].name), ToolColor,
                () => _terrainType = index, 13, 28f, Icon("ui/icons/" + TerrainPalette[i].icon))));
        }
        _terrainInfo = Label(_terrainCard, "Info", 14, TextColor, TextAnchor.UpperLeft, true);
        _clearTerrainBtn = MakeButton(_terrainCard, LM.Get("scenario_editor_clear_terrain"), DangerColor, () => ArmClear("terrain"));
        _clearDevelopBtn = MakeButton(_terrainCard, LM.Get("scenario_editor_clear_develop"), DangerColor, () => ArmClear("develop"));

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
        // 一笔还没收尾时不撤销：这一笔的快照只在头一下存，撤销了剩下的部分就没有快照
        if (!typing && _brushButton < 0 && !_stroking && Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
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
            UpdateZoneGrid();
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) _mouseUpFrame = Time.frameCount;
            if (_eventPending && Time.frameCount - _mouseUpFrame >= 2) RefreshEvent();
            if (_brushButton < 0 && _terrainApplied != ScenarioEditing.TerrainRevision) ApplyTerrainPaint();
            else if (_terrainDirty && (_brushButton < 0 || Time.unscaledTime - _terrainUploadAt > 0.1f)) UploadTerrain();
            // 无人区预测：拖着刷、划的时候取样那一段不重算(停手后再算)，归属那一段隔一会儿算一次
            ScenarioWilderness.Update(_brushButton < 0 && !_stroking);
            if (_wildCard != null && _wildCard.gameObject.activeSelf && _wildInfoVersion != ScenarioWilderness.Version) RefreshWild();
            if (_scenarioCard != null && _scenarioCard.gameObject.activeSelf && _scenarioVersion != ScenarioWilderness.Version) RefreshScenario(ScenarioEditing.Era);
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
        _hoverTexel = texel;
        _hoverOnMap = onMap;
        _hoverCity = onMap ? CityAt(texel) : null;
        _hoverSeat = onMap && _tool == Tool.Division && _seatMode ? SeatAt(texel) : (-2, -1);
        _hoverPass = onMap && _tool == Tool.Pass ? PassAt(texel) : -1;
        _hoverTribe = onMap && _tool == Tool.Tribe ? ScenarioEditing.TribesAt(TexelToLon(texel.x), TexelToLat(texel.y)) : -1;

        float wheel = Input.mouseScrollDelta.y;
        if (inside && Mathf.Abs(wheel) > 0.01f) ZoomAt(mouse, Mathf.Pow(1.2f, wheel));

        // 平移：中键、空格+左键(各工具都行)，「查看」下左键，右键拖(刷地块、刷地形里右键是擦，不平移)
        bool space = !Typing() && Input.GetKey(KeyCode.Space);
        bool leftPans = _tool == Tool.View || space;
        bool brush = _tool is Tool.Land or Tool.Terrain;
        if (_dragButton < 0)
        {
            int button = Input.GetMouseButtonDown(2) ? 2 : Input.GetMouseButtonDown(0) && leftPans ? 0 : -1;
            // 右键按下不动是"去掉"，拖开才算平移
            if (button < 0 && _rightPressed && !brush && Input.GetMouseButton(1) && (mouse - _pressAt).sqrMagnitude > ClickSlop * ClickSlop)
            {
                button = 1;
                _rightPressed = false;
            }
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
                StrokeDone(_brushButton == 1);
                _brushButton = -1;
                ScenarioEditing.EndStroke();
                _fillKey = int.MinValue;
                RefreshAll();
            }
            UpdateRing(onMap, texel);
            return;
        }
        if (brush && onMap && _dragButton < 0 && !space && (Input.GetMouseButtonDown(0) && !Shift || Input.GetMouseButtonDown(1)))
        {
            _brushButton = Input.GetMouseButtonDown(0) ? 0 : 1;
            _brushLast = texel;
            StrokeZones.Clear();
            _strokeOutside = false;
            ScenarioEditing.BeginStroke();
            BrushAt(texel, texel, _brushButton == 1);
            UpdateRing(onMap, texel);
            return;
        }
        UpdateRing(onMap, texel);

        // 左右键同时按时只认先按的那个，按下记录是共用的
        if (Input.GetMouseButtonDown(1) && inside && !brush && !_pressed)
        {
            _rightPressed = true;
            _pressAt = mouse;
            _pressCity = _hoverCity;
            _pressPass = _hoverPass;
            _pressTribe = _hoverTribe;
            _pressSeat = _hoverSeat;
        }
        if (Input.GetMouseButtonUp(1) && _rightPressed)
        {
            _rightPressed = false;
            if ((mouse - _pressAt).sqrMagnitude <= ClickSlop * ClickSlop) RightClick();
        }

        // 已经按下的那一下照常收尾(按着左键再按空格，不能卡在按下状态)
        if (leftPans && !_pressed) return;
        if (Input.GetMouseButtonDown(0) && onMap && !leftPans && !_rightPressed)
        {
            _pressed = true;
            _pressAt = mouse;
            _pressCity = _hoverCity;
            _pressPass = _hoverPass;
            _pressTexel = texel;
            _pressTribe = _tool == Tool.Tribe ? _hoverTribe : -1;
            _moving = false;
            _lastStroked = null;
            // 划入：点或拖过城(Shift+点是换目标；行政区「点图加治所」开着时点空处加治所，城照样能划)
            _stroking = Writing() && !Shift && !(_tool == Tool.Division && _seatMode && _hoverCity == null);
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
            if (OnLand(texel))
            {
                ScenarioEditing.MoveCity(_pressCity, TexelToLon(texel.x), TexelToLat(texel.y));
                _cityId = _pressCity;
                Say(LM.Get("scenario_editor_moved") + WildHint(TexelToLon(texel.x), TexelToLat(texel.y)));
            }
            else Say(LM.Get("scenario_editor_city_on_water"));
        }
        else if (click && onMap) LeftClick(texel);
        _moving = false;
        RefreshAll();
    }

    // 划入选中的那一块(国家、王国法理、帝国法理、行政区工具选了「划入」)
    private static bool Writing() => _mode == Mode.Write && _tool is Tool.Realm or Tool.Kingdom or Tool.Empire or Tool.Division;

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
                    Say(LM.Get("scenario_editor_city_added") + WildHint(TexelToLon(texel.x), TexelToLat(texel.y)) +
                        (ScenarioEditing.Era?.exact == true ? LM.Get("scenario_editor_city_added_exact") : ""));
                }
                else Say(LM.Get("scenario_editor_city_on_water"));
                break;
            case Tool.Realm:
                if (city != null) SelectCity(city);
                else if (_mode == Mode.Select) _cityId = null;
                break;
            case Tool.Kingdom:
                if (city == null) break;
                if (_mode == Mode.Share && !Shift)
                {
                    Say(_regionId == null ? LM.Get("scenario_editor_need_region") : ScenarioEditing.ToggleShare(city, _regionId));
                    break;
                }
                SelectCity(city);
                _regionId = ScenarioEditing.City(city)?.region;
                break;
            case Tool.Empire:
                if (city == null) break;
                SelectCity(city);
                _regionId = ScenarioEditing.City(city)?.region;
                _coreId = ScenarioEditing.CoreOfRegion(_regionId)?.id;
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
            case Tool.Division:
                if (city != null) PickDivision(city);
                else if (_seatMode)
                {
                    WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
                    if (polity == null)
                    {
                        Say(LM.Get("scenario_editor_division_pick"));
                        break;
                    }
                    ScenarioEditing.AddSeat(polity, _divisionIndex, TexelToLon(texel.x), TexelToLat(texel.y));
                    Say(string.Format(LM.Get("scenario_editor_seat_added"), ScenarioEditing.DivisionName(polity, _divisionIndex)));
                }
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

    // 行政区工具点城：选中这座城的国家与它所在的行政区(都城、不在行政区的为京畿)
    private static void PickDivision(string cityId)
    {
        _cityId = cityId;
        WorldMapPolity owner = ScenarioEditing.OwnerOf(ScenarioEditing.City(cityId));
        if (owner == null)
        {
            Say(LM.Get("scenario_editor_relation_independent"));
            return;
        }
        _realmId = owner.id;
        _divisionIndex = ScenarioEditing.DivisionOf(owner, cityId);
        Say(string.Format(LM.Get("scenario_editor_division_picked"), owner.Name, ScenarioEditing.DivisionName(owner, _divisionIndex)));
    }

    // 光标下选中国家的治所(行政区下标 -1 为京畿，治所下标)；没有为 (-2, -1)
    private static (int division, int seat) SeatAt(Vector2 texel)
    {
        WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
        if (polity == null) return (-2, -1);
        float best = PickReach() * PickReach();
        (int, int) found = (-2, -1);
        for (int d = -1; d < ScenarioEditing.Divisions(polity).Count; d++)
        {
            List<float[]> seats = ScenarioEditing.SeatsOf(polity, d);
            if (seats == null) continue;
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i] == null || seats[i].Length < 2) continue;
                float distance = (Texel(seats[i][0], seats[i][1]) - texel).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                found = (d, i);
            }
        }
        return found;
    }

    // 右键一律是"去掉"：删城、让城独立、去掉兼属、把法理拿出来、放回京畿、删长城的最后一点、删关隘、删部落圈
    private static void RightClick()
    {
        switch (_tool)
        {
            case Tool.City:
                ArmDeleteCity(_pressCity);
                break;
            case Tool.Division:
                if (_pressCity != null && _mode == Mode.Write) Say(ScenarioEditing.RemoveFromDivision(_pressCity));
                else if (_pressSeat.seat >= 0 && ScenarioEditing.Polity(_realmId) is WorldMapPolity polity &&
                         ScenarioEditing.SeatsOf(polity, _pressSeat.division) is List<float[]> seats && _pressSeat.seat < seats.Count &&
                         ScenarioEditing.RemoveSeat(polity, _pressSeat.division, seats[_pressSeat.seat][0], seats[_pressSeat.seat][1], 0.001f))
                    Say(LM.Get("scenario_editor_seat_removed"));
                break;
            case Tool.Realm:
                if (_pressCity != null && _mode == Mode.Write) Say(ScenarioEditing.MakeIndependent(_pressCity));
                break;
            case Tool.Kingdom:
                if (_pressCity != null && _regionId != null && _mode == Mode.Share &&
                    ScenarioEditing.City(_pressCity)?.share?.Contains(_regionId) == true)
                    Say(ScenarioEditing.ToggleShare(_pressCity, _regionId));
                break;
            case Tool.Empire:
                if (_pressCity != null && _mode == Mode.Write && _coreId != null &&
                    ScenarioEditing.CoreOfRegion(ScenarioEditing.City(_pressCity)?.region)?.id == _coreId)
                    Say(ScenarioEditing.RemoveRegionFromCore(ScenarioEditing.City(_pressCity)?.region));
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

    // 刷给谁：右栏选的那一种(国家 p:、王国法理 r:、这座城 c:、永久中立 n)，和看哪个图层无关
    private static string BrushKey() => _landMode switch
    {
        LandMode.Neutral => "n",
        LandMode.Realm => ScenarioEditing.Polity(_realmId) == null ? null : "p:" + _realmId,
        LandMode.Region => ScenarioEditing.Region(_regionId) == null ? null : "r:" + _regionId,
        _ => ScenarioEditing.City(_landCityId) == null ? null : "c:" + _landCityId
    };

    private static Layer LandLayer() => _landMode switch
    {
        LandMode.Region => Layer.Kingdom,
        LandMode.Neutral => Layer.Wild,
        _ => Layer.Realm
    };

    private static void StepLandMode(int step)
    {
        _landMode = (LandMode)(((int)_landMode + step + 4) % 4);
        _layer = LandLayer();
        if (_landMode == LandMode.City && ScenarioEditing.City(_landCityId) == null) _landCityId = _cityId;
        if (_landMode == LandMode.Region && ScenarioEditing.Region(_regionId) == null) _regionId = ScenarioEditing.City(_cityId)?.region;
    }

    // 一笔从上一点刷到这一点(沿途每半个半径落一次，最细的笔刷一格也不跳)：刷地块、刷开发度只刷陆地(0.2° 一格)；刷地形顺手改预览的像素
    private static void BrushAt(Vector2 from, Vector2 to, bool erase)
    {
        bool develop = _tool == Tool.Terrain && _developBrush;
        bool land = _tool == Tool.Land || develop;
        string key = null;
        if (_tool == Tool.Land && !erase && (key = BrushKey()) == null)
        {
            Say(LM.Get("scenario_editor_land_no_target"));
            return;
        }
        if (develop && !erase) key = _developValue.ToString();
        string type = land || erase ? null : TerrainPalette[Mathf.Clamp(_terrainType, 0, TerrainPalette.Length - 1)].type;
        BrushGrid(out float step, out int span, out int offX, out int offY);
        int radius = ScenarioBrush.Radii[_brushSize];
        (int dx, int dy)[] cover = ScenarioBrush.Cover(_brushShape, radius);
        int count = Mathf.Clamp(Mathf.CeilToInt((to - from).magnitude / (span * Mathf.Max(0.5f, radius / 2f))), 0, 2000);
        BrushCells.Clear();
        BrushTexels.Clear();
        for (int s = 0; s <= count; s++)
        {
            BrushCellAt(count == 0 ? to : Vector2.Lerp(from, to, s / (float)count), step, out int ci, out int cj);
            foreach ((int dx, int dy) in cover)
            {
                int i = ci + dx, j = cj + dy, x = (i - offX) * span, y = (j - offY) * span;
                if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
                if (land && !AnyLand(x, y, span)) continue;
                if (BrushCells.Add(ScenarioCells.Pack(i, j)) && !land) BrushTexels.Add(y * _w + x);
            }
        }
        if (land)
        {
            TouchZones(erase);
            if (develop) ScenarioEditing.PaintDevelop(BrushCells, key);
            else ScenarioEditing.PaintLand(BrushCells, key);
            return;
        }
        if (ScenarioEditing.PaintTerrain(BrushCells, type) == 0) return;
        foreach (int index in BrushTexels) SetTexel(index, type);
        _terrainDirty = true;
        _terrainApplied = ScenarioEditing.TerrainRevision;
        GridChanged();
    }

    // 现在这支笔刷按哪种格子刷：一格多少度、占几个预览像素、格子编号和预览像素的换算
    private static void BrushGrid(out float step, out int span, out int offX, out int offY)
    {
        step = _tool == Tool.Land || _tool == Tool.Terrain && _developBrush ? ScenarioCells.LandStep : ScenarioCells.TerrainStep;
        span = Mathf.Max(1, Mathf.RoundToInt(step / Mathf.Max(0.001f, _step)));
        offX = Mathf.FloorToInt(_lon0 / step + 0.5f * _step / step);
        offY = Mathf.FloorToInt(_lat0 / step + 0.5f * _step / step);
    }

    // 光标所在那一格
    private static void BrushCellAt(Vector2 texel, float step, out int i, out int j)
    {
        float lon = TexelToLon(Mathf.FloorToInt(texel.x) + 0.5f), lat = TexelToLat(Mathf.FloorToInt(texel.y) + 0.5f);
        ScenarioCells.Unpack(ScenarioCells.Key(lon, lat, step), out i, out j);
    }

    private static bool AnyLand(int x, int y, int span)
    {
        for (int b = y; b < y + span && b < _h; b++)
        for (int a = x; a < x + span && a < _w; a++)
            if (_land[b * _w + a]) return true;
        return false;
    }

    // 开局按块取值，中心那格说了算：刷到哪块地就连它的中心格一起写，碰到的那块整块归这一笔，图上看到的就是开局结果；
    // 擦掉要把这块地盖住的格子都擦干净，不然剩下的格子又把它算回去
    private static void TouchZones(bool erase)
    {
        ZoneExtra.Clear();
        foreach (int cell in BrushCells)
        {
            ScenarioCells.Unpack(cell, out int i, out int j);
            int zone = ScenarioWilderness.ZoneAt((i + 0.5f) * ScenarioCells.LandStep, (j + 0.5f) * ScenarioCells.LandStep);
            if (zone < 0)
            {
                _strokeOutside = true;
                continue;
            }
            if (!StrokeZones.Add(zone)) continue;
            if (erase)
            {
                ScenarioWilderness.ZoneCells(zone, ZoneExtra);
                continue;
            }
            ScenarioWilderness.CenterCells(zone, out int first, out int second);
            ZoneExtra.Add(first);
            ZoneExtra.Add(second);
        }
        BrushCells.UnionWith(ZoneExtra);
    }

    // 一笔刷完说一声：碰到几块地、归谁；全在地图外头也说一声
    private static void StrokeDone(bool erase)
    {
        if (_tool != Tool.Land) return;
        if (StrokeZones.Count == 0)
        {
            if (_strokeOutside) Say(LM.Get("scenario_editor_land_outside"));
            return;
        }
        string key = BrushKey();
        if (erase) Say(string.Format(LM.Get(ScenarioEditing.RestoresBase ? "scenario_editor_land_stroke_base" : "scenario_editor_land_stroke_erase"), StrokeZones.Count));
        else if (key != null) Say(string.Format(LM.Get("scenario_editor_land_stroke"), StrokeZones.Count, AreaName(key)));
    }

    // Shift+点取刷给谁：点在城上就是这座城，点在别处取那块地开局归的城；无人区取它挂的法理、归的国家
    private static void PickTarget(Vector2 texel, string cityId)
    {
        int zone = ScenarioWilderness.ZoneAt(TexelToLon(texel.x), TexelToLat(texel.y));
        WorldMapCity city = ScenarioEditing.City(cityId);
        if (city == null)
        {
            EnsureZoneOwners();
            if (_zoneOwner != null && zone >= 0 && zone < _zoneOwner.Length && _zoneOwner[zone] >= 0 && _zoneOwner[zone] < _seatCities.Length)
                city = _seatCities[_zoneOwner[zone]];
        }
        if (city != null)
        {
            SelectCity(city.id);
            if (_landMode == LandMode.Region) _regionId = city.region;
            else if (_landMode == LandMode.City) _landCityId = city.id;
            else if (ScenarioEditing.OwnerOf(city) is WorldMapPolity owner)
            {
                _realmId = owner.id;
                _landMode = LandMode.Realm;
            }
            else
            {
                // 独立的城没有国家可刷，就刷给这座城
                _landCityId = city.id;
                _landMode = LandMode.City;
            }
        }
        else if (_landMode == LandMode.Region && ScenarioWilderness.TitleOf(zone) is string title) _regionId = title;
        else if (_landMode != LandMode.Region && ScenarioWilderness.OwnerOf(zone) is string realm && realm.StartsWith("p:"))
        {
            _realmId = realm.Substring(2);
            _landMode = LandMode.Realm;
        }
        else
        {
            Say(LM.Get("scenario_editor_land_pick_none"));
            return;
        }
        // 城的地区对不上的手写剧本取不到法理
        string picked = BrushKey();
        Say(picked == null ? LM.Get("scenario_editor_land_pick_none") : string.Format(LM.Get("scenario_editor_land_picked"), AreaName(picked)));
    }

    private static void StepBrush(int step) =>
        _brushSize = ((_brushSize + step) % ScenarioBrush.Radii.Length + ScenarioBrush.Radii.Length) % ScenarioBrush.Radii.Length;

    private static void StepShape(int step) =>
        _brushShape = (ScenarioBrush.Shape)(((int)_brushShape + step + ScenarioBrush.ShapeCount) % ScenarioBrush.ShapeCount);

    private static string BrushText() => string.Format(LM.Get("scenario_editor_brush_cells"), ScenarioBrush.Radii[_brushSize] * 2 + 1);

    private static string ShapeText() => LM.Get("scenario_editor_shape_" + _brushShape.ToString().ToLowerInvariant());

    private static bool Armed(string kind) => _clearArmed == kind && Time.unscaledTime - _clearArmedAt <= ConfirmSeconds;

    // 清掉全部刷的地块、地形、开发度，清空全部城：五秒内再点一次(可撤销)
    private static void ArmClear(string kind)
    {
        if (Armed(kind))
        {
            _clearArmed = null;
            if (kind == "cities")
            {
                Say(ScenarioEditing.ClearAllCities());
                _cityId = _realmId = _relationFrom = _landCityId = null;
                return;
            }
            bool restored = kind == "land" && ScenarioEditing.RestoresBase;
            if (kind == "land") ScenarioEditing.ClearLand();
            else if (kind == "develop") ScenarioEditing.ClearDevelop();
            else ScenarioEditing.ClearTerrain();
            Say(LM.Get(restored ? "scenario_editor_cleared_base" : "scenario_editor_cleared"));
            return;
        }
        _clearArmed = kind;
        _clearArmedAt = Time.unscaledTime;
        Say(LM.Get(kind == "cities" ? "scenario_editor_clear_cities_confirm"
            : kind == "land" && ScenarioEditing.RestoresBase ? "scenario_editor_clear_confirm_base" : "scenario_editor_clear_confirm"));
    }

    // 笔刷框(跟着光标对齐到格子，形状和大小就是落笔盖住的那些格子，右键擦时发红)与正拖出的部落圈
    private static void UpdateRing(bool onMap, Vector2 texel)
    {
        if (_ring == null) return;
        bool brush = _tool is Tool.Land or Tool.Terrain && onMap && _dragButton < 0;
        bool tribe = _tool == Tool.Tribe && _pressed && _pressTribe < 0 && onMap && Input.GetMouseButton(0);
        bool show = brush || tribe;
        if (_ring.gameObject.activeSelf != show) _ring.gameObject.SetActive(show);
        if (!show) return;
        Vector2 center = _pressTexel;
        float width = (texel - _pressTexel).magnitude * 2f;
        Texture wanted = _ringTexture;
        if (brush)
        {
            BrushGrid(out float step, out int span, out int offX, out int offY);
            BrushCellAt(texel, step, out int i, out int j);
            int radius = ScenarioBrush.Radii[_brushSize];
            center = new Vector2((i - offX + 0.5f) * span, (j - offY + 0.5f) * span);
            width = (radius * 2 + 1) * span;
            wanted = BrushOutline(_brushShape, radius);
        }
        if (_ring.texture != wanted) _ring.texture = wanted;
        _ring.rectTransform.anchoredPosition = (center - _center) * _zoom;
        _ring.rectTransform.sizeDelta = Vector2.one * Mathf.Max(6f, width * _zoom);
        _ring.color = tribe ? new Color(1f, 0.85f, 0.4f, 1f) : _brushButton == 1 ? new Color(1f, 0.45f, 0.4f, 1f) : Color.white;
    }

    // 笔刷框的图：一个像素就是一个刷的格子，边上描白，里面淡淡一层
    private static Texture2D BrushOutline(ScenarioBrush.Shape shape, int radius)
    {
        if (BrushOutlines.TryGetValue((shape, radius), out Texture2D cached) && cached != null) return cached;
        int size = radius * 2 + 1;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int dx = x - radius, dy = y - radius;
            if (!ScenarioBrush.Inside(shape, dx, dy, radius)) continue;
            bool edge = !ScenarioBrush.Inside(shape, dx + 1, dy, radius) || !ScenarioBrush.Inside(shape, dx - 1, dy, radius) ||
                        !ScenarioBrush.Inside(shape, dx, dy + 1, radius) || !ScenarioBrush.Inside(shape, dx, dy - 1, radius);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(edge ? 235 : 46));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return BrushOutlines[(shape, radius)] = texture;
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

    // 一笔(选了划入)：国家(拖过的城划给选中的国家)、王国法理(划进选中的法理)、帝国法理(城的王国法理收进选中的帝国法理)、行政区；同一座只问一次
    private static void StrokeAt(string cityId)
    {
        if (cityId == null || cityId == _lastStroked) return;
        _lastStroked = cityId;
        string message = null;
        switch (_tool)
        {
            case Tool.Realm:
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
            case Tool.Division:
                WorldMapPolity divided = ScenarioEditing.Polity(_realmId);
                message = divided == null ? LM.Get("scenario_editor_division_pick") : ScenarioEditing.AssignDivision(divided, _divisionIndex, cityId);
                break;
        }
        Say(message);
    }

    private static void SelectCity(string cityId)
    {
        _cityId = cityId;
        WorldMapPolity owner = ScenarioEditing.OwnerOf(ScenarioEditing.City(cityId));
        if (owner != null || _tool == Tool.Realm && _mode == Mode.Select) _realmId = owner?.id;
    }

    private static void ArmDeleteCity(string cityId)
    {
        if (cityId == null) return;
        if (_deleteArmedCity == cityId && Time.unscaledTime - _deleteCityArmedAt <= ConfirmSeconds)
        {
            Say(ScenarioEditing.DeleteCity(cityId));
            if (_cityId == cityId) _cityId = null;
            _deleteArmedCity = null;
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
            Say(ScenarioEditing.DeletePolity(polity));
            _realmId = null;
            _deleteRealmArmedAt = -100f;
            return;
        }
        _deleteRealmArmedAt = Time.unscaledTime;
        Say(string.Format(LM.Get("scenario_editor_delete_realm_confirm"), polity.Name));
    }

    private static void SetTool(Tool tool)
    {
        // 按着鼠标换工具：这一笔到此为止，别接着算到新工具头上
        if (_brushButton >= 0 || _stroking) ScenarioEditing.EndStroke();
        _brushButton = -1;
        _stroking = _pressed = false;
        _tool = tool;
        _mode = Mode.Select;
        _message = null;
        _pickSlot = null;
        if (tool == Tool.Kingdom) _kingdomVisited = true;
        if (tool == Tool.Event && _eventIndex >= ScenarioEditing.Events.Count) _eventIndex = ScenarioEditing.Events.Count - 1;
        if (tool == Tool.Event && _eventIndex < 0 && ScenarioEditing.Events.Count > 0) _eventIndex = 0;
        // 换到法理工具时顺手换到对应图层，选中的城跟着选它的法理
        if (tool == Tool.Kingdom) _layer = Layer.Kingdom;
        if (tool == Tool.Empire && _layer is not Layer.Empire and not Layer.Tianxia) _layer = Layer.Empire;
        if (tool is Tool.Realm or Tool.Relation && _layer != Layer.Realm) _layer = Layer.Realm;
        if (tool is Tool.Kingdom or Tool.Empire && _cityId != null) _regionId = ScenarioEditing.City(_cityId)?.region;
        if (tool == Tool.Empire && _regionId != null) _coreId = ScenarioEditing.CoreOfRegion(_regionId)?.id;
        if (tool == Tool.Wall && _wallIndex >= ScenarioEditing.Walls.Count) _wallIndex = ScenarioEditing.Walls.Count - 1;
        // 刷地块：刷给谁在右栏选，和图层无关；停在地形这种看不出归属的图层上才换
        if (tool == Tool.Land)
        {
            if (_layer is not Layer.Realm and not Layer.Kingdom and not Layer.Wild and not Layer.Division) _layer = LandLayer();
            if (_landMode == LandMode.City && ScenarioEditing.City(_landCityId) == null) _landCityId = _cityId;
            if (_landMode == LandMode.Region && ScenarioEditing.Region(_regionId) == null) _regionId = ScenarioEditing.City(_cityId)?.region;
        }
        // 刷地形看地形图层，刷开发度看无人区图层
        if (tool == Tool.Terrain) _layer = _developBrush ? Layer.Wild : Layer.Terrain;
        // 行政区：看行政区图层，选中的城跟着选它的国家与行政区
        if (tool == Tool.Division)
        {
            _layer = Layer.Division;
            _seatMode = false;
            if (ScenarioEditing.OwnerOf(ScenarioEditing.City(_cityId)) is WorldMapPolity owner)
            {
                _realmId = owner.id;
                _divisionIndex = ScenarioEditing.DivisionOf(owner, _cityId);
            }
            if (ScenarioEditing.Division(ScenarioEditing.Polity(_realmId), _divisionIndex) == null) _divisionIndex = -1;
        }
        if (tool == Tool.Tribe && _tribeIndex >= ScenarioEditing.Tribes.Count) _tribeIndex = -1;
    }

    // 选国家、法理的前后翻：没有城的国家、法理在地图上点不到，只能从这里选
    private static string StepId<T>(List<T> list, global::System.Func<T, string> id, string current, int step)
    {
        if (list.Count == 0) return null;
        int at = list.FindIndex(item => id(item) == current);
        return id(list[at < 0 ? 0 : ((at + step) % list.Count + list.Count) % list.Count]);
    }

    private static void StepRealm(int step) => _realmId = StepId(ScenarioEditing.Era.polities, item => item.id, _realmId, step);

    private static void StepRegion(int step) => _regionId = StepId(ScenarioEditing.Regions, item => item.id, _regionId, step);

    private static void StepCore(int step) => _coreId = StepId(ScenarioEditing.Cores, item => item.id, _coreId, step);

    // 上手步骤：打开、收起(记住)；点一步切到那一步的工具
    private static void SetGuide(bool open)
    {
        _guideOpen = open;
        PlayerPrefs.SetInt(GuidePref, open ? 0 : 1);
    }

    private static void GuideGo(int step)
    {
        switch (step)
        {
            case 0:
            case 1:
                SetTool(Tool.City);
                break;
            case 2:
                SetTool(Tool.Realm);
                _mode = Mode.Write;
                break;
            case 3:
                SetTool(Tool.Kingdom);
                break;
            case 4:
                SetTool(Tool.Land);
                break;
            default:
                Save();
                break;
        }
    }

    private static bool GuideDone(int step)
    {
        WorldMapEra era = ScenarioEditing.Era;
        return step switch
        {
            0 => ScenarioEditing.Cities.Any(ScenarioEditing.Active),
            1 => era.polities.Count > 0,
            2 => era.polities.Any(polity => ScenarioEditing.CityCount(polity) >= 2),
            3 => _kingdomVisited,
            4 => ScenarioEditing.LandEdited,
            _ => !ScenarioEditing.Dirty && era.polities.Count > 0
        };
    }

    private static void SetLayer(Layer layer)
    {
        _layer = layer;
        // 天下挂在帝国法理上，没有单独的工具：看天下图层就直接进帝国法理工具，右栏改所属天下
        if (layer != Layer.Tianxia || _tool == Tool.Empire) return;
        SetTool(Tool.Empire);
        Say(LM.Get("scenario_editor_tianxia_hint"));
    }

    // 天下图层上的一块：没写所属天下的帝国法理开局自成一个天下，也单独算一块(以前不画，导入的图看着天下缺了一大片)
    private static string TianxiaArea(WorldMapCore core) =>
        core == null ? null : !string.IsNullOrEmpty(core.group) ? "t:" + core.group : "x:" + core.id;

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
        _cityId = _realmId = _regionId = _coreId = _relationFrom = _landCityId = null;
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

    // 这座城在当前图层归哪一块(国家、行政区、王国法理、帝国法理、天下)；不归哪块返回 null
    private static string AreaKey(WorldMapCity city)
    {
        switch (_layer)
        {
            case Layer.Realm:
                // 独立的城开局自成一国，也有自己的一块地
                WorldMapPolity owner = ScenarioEditing.OwnerOf(city);
                return owner == null ? "i:" + city.id : "p:" + owner.id;
            case Layer.Division:
                // 本国第几个行政区("d:国家:下标")，都城与不在行政区的为京畿("j:国家")
                WorldMapPolity realm = ScenarioEditing.OwnerOf(city);
                if (realm == null) return "i:" + city.id;
                int division = ScenarioEditing.DivisionOf(realm, city.id);
                return division >= 0 ? "d:" + realm.id + ":" + division : "j:" + realm.id;
            case Layer.Kingdom:
                return string.IsNullOrEmpty(city.region) ? null : "r:" + city.region;
            case Layer.Empire:
                WorldMapCore core = ScenarioEditing.CoreOfRegion(city.region);
                return core == null ? null : "k:" + core.id;
            case Layer.Tianxia:
                return TianxiaArea(ScenarioEditing.CoreOfRegion(city.region));
        }
        return null;
    }

    private static string SelectedKey() => _layer switch
    {
        Layer.Realm => _realmId == null ? null : "p:" + _realmId,
        Layer.Division => _realmId == null ? null : _divisionIndex >= 0 ? "d:" + _realmId + ":" + _divisionIndex : "j:" + _realmId,
        Layer.Kingdom => _regionId == null ? null : "r:" + _regionId,
        Layer.Empire => _coreId == null ? null : "k:" + _coreId,
        Layer.Tianxia => TianxiaArea(ScenarioEditing.Core(_coreId)),
        _ => null
    };

    // 各块地开局归谁：归属、刷的地块、预测变了才重算
    private static void EnsureZoneOwners()
    {
        int key = ScenarioEditing.Revision * 31 + ScenarioWilderness.Version;
        if (key == _zoneOwnerKey && _zoneOwner != null) return;
        _zoneOwnerKey = key;
        if (!ScenarioWilderness.Preview(ref _zoneOwner, ref _zonePainted))
        {
            _zoneOwner = null;
            return;
        }
        IReadOnlyList<string> seats = ScenarioWilderness.Seats;
        var byId = new Dictionary<string, WorldMapCity>();
        foreach (WorldMapCity city in ScenarioEditing.Cities)
            if (!string.IsNullOrEmpty(city?.id)) byId[city.id] = city;
        _seatCities = new WorldMapCity[seats.Count];
        for (int i = 0; i < seats.Count; i++) byId.TryGetValue(seats[i], out _seatCities[i]);
    }

    // 图层颜色：照开局的结果按块上色(各块归谁见 ScenarioWilderness.Preview)，所以看到的国界就是开局的国界。独立的城也铺，灰的；
    // 铺不到的地开局补小国或并给邻国，浅色斜线；无人区不是城的地，按归属规矩上色(国家、行政区图层看归哪国，法理图层看挂哪块法理)，
    // 淡色打斜线，没主的只打灰斜线；刷过的地块颜色深一点。选中的那一块颜色深些，两块相接处描深色边线(只描一侧，线细)。
    // 地块两种画法：方形(原版的方块)、平滑(一格看周围四块地，方块的台阶磨成斜线)
    private static void RenderFill()
    {
        string selected = SelectedKey();
        int key = (((ScenarioEditing.Revision * 31 + (int)_layer) * 31 + (selected?.GetHashCode() ?? 0)) * 31 + ScenarioWilderness.Version) * 2 + (_smoothFill ? 1 : 0);
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
            _fillCity = new int[n];
            _fillFixed = new bool[n];
        }
        EnsureFillZones();
        if (_layer == Layer.Wild)
        {
            RenderWild();
            return;
        }
        for (int i = 0; i < n; i++)
        {
            _fillOwner[i] = -1;
            _fillCity[i] = -1;
            _fillFixed[i] = false;
            _fillWild[i] = false;
            _fillHatch[i] = 0;
        }
        FillKeys.Clear();
        EnsureZoneOwners();
        if (_zoneOwner != null && ScenarioWilderness.Grid(out float west, out float bottom, out float side, out int zw, out int zh))
            FillFromZones(west, bottom, side, zw, zh);
        else _fillKey = int.MinValue;

        // 刷的中立区打灰斜线(刷在城的地上不起作用，也让它看得见)
        if (ScenarioEditing.LandCells.Count > 0)
        {
            int offX = Mathf.FloorToInt(_lon0 / ScenarioCells.LandStep + 0.5f * _step / ScenarioCells.LandStep);
            int offY = Mathf.FloorToInt(_lat0 / ScenarioCells.LandStep + 0.5f * _step / ScenarioCells.LandStep);
            foreach (KeyValuePair<int, string> pair in ScenarioEditing.LandCells)
            {
                if (pair.Value != "n") continue;
                ScenarioCells.Unpack(pair.Key, out int i, out int j);
                int x = i - offX, y = j - offY;
                if (x < 0 || y < 0 || x >= fw || y >= fh) continue;
                int at = y * fw + x;
                if (_fillOwner[at] < 0 && !_fillWild[at]) _fillHatch[at] = 2;
            }
        }

        var fills = new Color32[FillKeys.Count];
        var strong = new Color32[FillKeys.Count];
        var edges = new Color32[FillKeys.Count];
        var faint = new Color32[FillKeys.Count];
        // 城市边界：同一块里两座城相接处，比块的边浅一档的细线
        var cityEdges = new Color32[FillKeys.Count];
        for (int s = 0; s < FillKeys.Count; s++)
        {
            Color32 color = ScenarioEditing.ColorOfKey(FillKeys[s]);
            bool chosen = FillKeys[s] == selected;
            fills[s] = new Color32(color.r, color.g, color.b, (byte)(chosen ? 190 : 110));
            strong[s] = new Color32(color.r, color.g, color.b, (byte)(chosen ? 220 : 165));
            edges[s] = new Color32((byte)(color.r / 3), (byte)(color.g / 3), (byte)(color.b / 3), 235);
            faint[s] = new Color32(color.r, color.g, color.b, (byte)(chosen ? 120 : 60));
            cityEdges[s] = new Color32((byte)(color.r * 3 / 5), (byte)(color.g * 3 / 5), (byte)(color.b * 3 / 5), 200);
        }
        for (int y = 0; y < fh; y++)
        for (int x = 0; x < fw; x++)
        {
            int at = y * fw + x, slot = _fillOwner[at];
            if (slot < 0)
            {
                // 开局补小国的地：浅色斜线；没主的无人区、刷的中立区：灰斜线
                _fillPixels[at] = _fillHatch[at] == 3 ? (x + y) % 6 == 0 ? FillerStripe : FillerFill
                    : _fillHatch[at] != 0 ? Stripe(x, y) || HatchAlone(x, y, fw, fh) ? NeutralStripe : NeutralFill : Clear;
                continue;
            }
            // 与别的块相接只描右、上一侧；挨着未归属的陆地四边都描
            bool edge = Other(x + 1, y, fw, fh, slot, false) || Other(x, y + 1, fw, fh, slot, false) ||
                        Other(x - 1, y, fw, fh, slot, true) || Other(x, y - 1, fw, fh, slot, true) ||
                        Other(x + 1, y, fw, fh, slot, true) || Other(x, y + 1, fw, fh, slot, true);
            // 城市边界(用户 2026-11-02 转群友"剧本编辑还是不显示城市边界啊 这真得改改")：同一块里与别的城相接只描右、上一侧
            bool border = !edge && _fillCity[at] >= 0 &&
                          (OtherCity(x + 1, y, fw, fh, slot, _fillCity[at]) || OtherCity(x, y + 1, fw, fh, slot, _fillCity[at]));
            _fillPixels[at] = edge ? edges[slot] : border ? cityEdges[slot]
                : _fillWild[at] ? Stripe(x, y) ? strong[slot] : faint[slot] : _fillFixed[at] ? strong[slot] : fills[slot];
        }
        _fill.SetPixels32(_fillPixels);
        _fill.Apply();
        _areaKey = int.MinValue;
    }

    // 各块地在当前图层算哪一块，再落到图层的粗格上
    private static void FillFromZones(float west, float bottom, float side, int zw, int zh)
    {
        int zones = zw * zh;
        if (_zoneSlot == null || _zoneSlot.Length != zones)
        {
            _zoneSlot = new int[zones];
            _zoneClass = new int[zones];
            _zoneIsWild = new bool[zones];
        }
        var slots = new Dictionary<string, int>();
        var seatSlot = new int[_seatCities.Length];
        for (int s = 0; s < _seatCities.Length; s++)
        {
            string area = _seatCities[s] == null ? null : AreaKey(_seatCities[s]);
            seatSlot[s] = area == null ? -1 : SlotOf(slots, area);
        }
        var wildArea = new Dictionary<string, string>();
        for (int z = 0; z < zones; z++)
        {
            int owner = _zoneOwner[z];
            bool wild = ScenarioWilderness.RegionOf(z) >= 0;
            _zoneIsWild[z] = wild;
            if (wild)
            {
                string area = WildArea(z, wildArea);
                _zoneSlot[z] = area == null ? -1 : SlotOf(slots, area);
                _zoneClass[z] = WildClass - _zoneSlot[z];
            }
            else if (owner >= 0)
            {
                _zoneSlot[z] = owner < seatSlot.Length ? seatSlot[owner] : -1;
                _zoneClass[z] = owner;
            }
            else
            {
                _zoneSlot[z] = -1;
                _zoneClass[z] = owner == ScenarioPreviewRules.Filler ? owner
                    : ScenarioWilderness.Claimable(z) ? ScenarioPreviewRules.Unclaimed : SkipClass;
            }
        }
        int fw = _fill.width, fh = _fill.height;
        var colI = new int[fw];
        var rowJ = new int[fh];
        var colT = new float[fw];
        var rowT = new float[fh];
        for (int x = 0; x < fw; x++)
        {
            float at = (TexelToLon(x * FillFactor + FillFactor / 2f) - west) / side - 0.5f;
            colI[x] = Mathf.FloorToInt(at);
            colT[x] = at - colI[x];
        }
        for (int y = 0; y < fh; y++)
        {
            float at = (TexelToLat(y * FillFactor + FillFactor / 2f) - bottom) / side - 0.5f;
            rowJ[y] = Mathf.FloorToInt(at);
            rowT[y] = at - rowJ[y];
        }
        int ZoneIndex(int i, int j) => i < 0 || j < 0 || i >= zw || j >= zh ? -1 : j * zw + i;
        for (int y = 0; y < fh; y++)
        for (int x = 0; x < fw; x++)
        {
            int at = y * fw + x, zone = _fillZone[at];
            if (zone < 0 || zone >= zones || CostAt(x, y) == 0) continue;
            if (_smoothFill)
            {
                int i = colI[x], j = rowJ[y];
                float a = colT[x], b = rowT[y];
                VoteZones[0] = ZoneIndex(i, j);
                VoteZones[1] = ZoneIndex(i + 1, j);
                VoteZones[2] = ZoneIndex(i, j + 1);
                VoteZones[3] = ZoneIndex(i + 1, j + 1);
                VoteWeights[0] = (1f - a) * (1f - b);
                VoteWeights[1] = a * (1f - b);
                VoteWeights[2] = (1f - a) * b;
                VoteWeights[3] = a * b;
                zone = ScenarioPreviewRules.Vote(VoteZones, VoteWeights, _zoneClass, SkipClass);
                if (zone < 0) continue;
            }
            int owner = _zoneOwner[zone];
            _fillOwner[at] = _zoneSlot[zone];
            _fillCity[at] = owner >= 0 ? owner : -1;
            _fillFixed[at] = _zonePainted[zone];
            _fillWild[at] = _zoneIsWild[zone];
            _fillHatch[at] = _zoneIsWild[zone] ? (byte)1 : owner == ScenarioPreviewRules.Filler ? (byte)3 : (byte)0;
        }
    }

    // 刷地块时盖在图上的地块网格(一块地一格)：一张小图重复铺，缩放、平移都不用重画
    private static Texture2D ZoneGridTexture()
    {
        const int size = 16;
        _zoneGridTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            pixels[y * size + x] = x == 0 || y == 0 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        _zoneGridTexture.SetPixels32(pixels);
        _zoneGridTexture.Apply();
        return _zoneGridTexture;
    }

    private static void UpdateZoneGrid()
    {
        if (_zoneGrid == null) return;
        bool ready = ScenarioWilderness.Grid(out float west, out float bottom, out float side, out int zw, out int zh);
        bool show = ready && _tool == Tool.Land && side / _step * _zoom >= ZoneGridMinScreen;
        if (_zoneGrid.gameObject.activeSelf != show) _zoneGrid.gameObject.SetActive(show);
        if (!show) return;
        RectTransform rect = _zoneGrid.rectTransform;
        Vector2 min = new Vector2((west - _lon0) / _step / _w, (bottom - _lat0) / _step / _h);
        rect.anchorMin = min;
        rect.anchorMax = min + new Vector2(zw * side / _step / _w, zh * side / _step / _h);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _zoneGrid.uvRect = new Rect(0f, 0f, zw, zh);
    }

    private static bool Stripe(int x, int y) => (x + y) % 4 == 0;

    // 单独一格的记号不按花纹隔着跳过，最细的笔刷点下去也看得见
    private static bool HatchAlone(int x, int y, int fw, int fh) =>
        (x + 1 >= fw || _fillHatch[y * fw + x + 1] == 0) && (x == 0 || _fillHatch[y * fw + x - 1] == 0) &&
        (y + 1 >= fh || _fillHatch[(y + 1) * fw + x] == 0) && (y == 0 || _fillHatch[(y - 1) * fw + x] == 0);

    // 图层粗格落在预测的哪块地上：地块的范围、大小变了，或换了地图才重算
    private static void EnsureFillZones()
    {
        int fw = _fill.width, fh = _fill.height, n = fw * fh;
        if (_fillZone != null && _fillZone.Length == n && _fillZoneVersion == ScenarioWilderness.GeometryVersion) return;
        _fillZoneVersion = ScenarioWilderness.GeometryVersion;
        if (_fillZone == null || _fillZone.Length != n)
        {
            _fillZone = new int[n];
            _fillWild = new bool[n];
            _fillHatch = new byte[n];
        }
        for (int y = 0; y < fh; y++)
        {
            float lat = TexelToLat(y * FillFactor + FillFactor / 2f);
            for (int x = 0; x < fw; x++) _fillZone[y * fw + x] = ScenarioWilderness.ZoneAt(TexelToLon(x * FillFactor + FillFactor / 2f), lat);
        }
    }

    // 一块无人区在当前图层算哪一块：国家、行政区图层看归哪国(独立的城、补空小国、中立的不上色)，法理图层看挂哪块法理；
    // cache 按归属、法理记(一次重铺里几万块地只查几十种)
    private static string WildArea(int zone, Dictionary<string, string> cache)
    {
        bool byOwner = _layer is Layer.Realm or Layer.Division;
        string raw = byOwner ? ScenarioWilderness.OwnerOf(zone) : ScenarioWilderness.TitleOf(zone);
        if (raw == null) return null;
        if (cache.TryGetValue(raw, out string area)) return area;
        switch (_layer)
        {
            case Layer.Realm:
            case Layer.Division:
                area = raw.StartsWith("c:") ? "i:" + raw.Substring(2)
                    : !raw.StartsWith("p:") || ScenarioEditing.Polity(raw.Substring(2)) == null ? null
                    : _layer == Layer.Division ? "j:" + raw.Substring(2) : raw;
                break;
            case Layer.Kingdom:
                area = ScenarioEditing.Region(raw) == null ? null : "r:" + raw;
                break;
            case Layer.Empire:
                WorldMapCore core = ScenarioEditing.CoreOfRegion(raw);
                area = core == null ? null : "k:" + core.id;
                break;
            case Layer.Tianxia:
                area = TianxiaArea(ScenarioEditing.CoreOfRegion(raw));
                break;
        }
        cache[raw] = area;
        return area;
    }

    // 无人区图层：按类型上色(同游戏里的无人区图层色系)，雨林大泽开发度高的淡些，两片相接处描边，永久中立的打斜线；
    // 刷了开发度的格子、刷的中立区另打一层浅点子，拖着刷时立刻看得见
    private static void RenderWild()
    {
        int fw = _fill.width, fh = _fill.height, n = fw * fh;
        FillKeys.Clear();
        for (int i = 0; i < n; i++)
        {
            _fillOwner[i] = -1;
            _fillHatch[i] = 0;
        }
        int offX = Mathf.FloorToInt(_lon0 / ScenarioCells.LandStep + 0.5f * _step / ScenarioCells.LandStep);
        int offY = Mathf.FloorToInt(_lat0 / ScenarioCells.LandStep + 0.5f * _step / ScenarioCells.LandStep);
        foreach (KeyValuePair<int, string> pair in ScenarioEditing.DevelopCells.Concat(ScenarioEditing.LandCells.Where(item => item.Value == "n")))
        {
            ScenarioCells.Unpack(pair.Key, out int i, out int j);
            int x = i - offX, y = j - offY;
            if (x >= 0 && y >= 0 && x < fw && y < fh) _fillHatch[y * fw + x] = (byte)(pair.Value == "n" ? 2 : 1);
        }
        for (int y = 0; y < fh; y++)
        for (int x = 0; x < fw; x++)
        {
            int at = y * fw + x, zone = _fillZone[at], region = ScenarioWilderness.RegionOf(zone);
            Color32 pixel = Clear;
            if (region >= 0 && CostAt(x, y) != 0)
            {
                Color32 color = ScenarioWilderness.KindColor(ScenarioWilderness.KindOf(zone));
                float dev = ScenarioWilderness.DevOf(zone);
                byte alpha = (byte)(190f * (dev > 0f ? 1f - 0.65f * Mathf.Min(dev, 100f) / 100f : 1f));
                bool edge = WildEdge(x + 1, y, fw, fh, region) || WildEdge(x - 1, y, fw, fh, region) ||
                            WildEdge(x, y + 1, fw, fh, region) || WildEdge(x, y - 1, fw, fh, region);
                pixel = edge ? new Color32((byte)(color.r / 2), (byte)(color.g / 2), (byte)(color.b / 2), 230)
                    : ScenarioWilderness.NeutralAt(zone) && Stripe(x, y) ? NeutralStripe
                    : new Color32(color.r, color.g, color.b, alpha);
            }
            if (_fillHatch[at] != 0 && ((x + y) % 2 == 0 || HatchAlone(x, y, fw, fh)) && CostAt(x, y) != 0)
                pixel = _fillHatch[at] == 2 ? NeutralStripe : new Color32(255, 250, 220, 150);
            _fillPixels[at] = pixel;
        }
        _fill.SetPixels32(_fillPixels);
        _fill.Apply();
        _areaKey = int.MinValue;
    }

    // 邻格不是这一片(别片、不是无人区)
    private static bool WildEdge(int x, int y, int fw, int fh, int region) =>
        x >= 0 && y >= 0 && x < fw && y < fh && ScenarioWilderness.RegionOf(_fillZone[y * fw + x]) != region;

    private static int SlotOf(Dictionary<string, int> slots, string area)
    {
        if (slots.TryGetValue(area, out int slot)) return slot;
        slots[area] = slot = FillKeys.Count;
        FillKeys.Add(area);
        return slot;
    }

    // 粗格(FillFactor 见方)的地形代价：取它左下那个像素的地形大类
    private static int CostAt(int fx, int fy) => ClassCost[_classes[fy * FillFactor * _w + fx * FillFactor]];

    // 邻格同一块、归另一座城
    private static bool OtherCity(int x, int y, int fw, int fh, int slot, int city)
    {
        if (x < 0 || y < 0 || x >= fw || y >= fh) return false;
        int at = y * fw + x, other = _fillCity[at];
        return other >= 0 && other != city && _fillOwner[at] == slot;
    }

    // 邻格是别的块(unclaimed 为 true 时只看未归属的陆地)
    private static bool Other(int x, int y, int fw, int fh, int slot, bool unclaimed)
    {
        if (x < 0 || y < 0 || x >= fw || y >= fh) return false;
        int other = _fillOwner[y * fw + x];
        return unclaimed ? other < 0 && CostAt(x, y) != 0 : other >= 0 && other != slot;
    }

    // 城点、长城、关隘、关系连线、法理首都：数据、选中、图层、工具、点的大小变了才重画
    private static void RenderMarks()
    {
        int radius = MarkerRadius();
        int key = ((((ScenarioEditing.Revision * 31 + radius) * 31 + (int)_tool) * 31 + (int)_layer) * 31 +
                   (_cityId?.GetHashCode() ?? 0)) * 31 + (_realmId?.GetHashCode() ?? 0) + (_regionId?.GetHashCode() ?? 0) * 7 +
                  (_coreId?.GetHashCode() ?? 0) * 13 + (_relationFrom?.GetHashCode() ?? 0) * 17 + _wallIndex * 19 + _passIndex * 23 +
                  _tribeIndex * 29 + _divisionIndex * 37 + ScenarioWilderness.GeometryVersion * 41 + ScenarioWilderness.Version * 43;
        if (key == _marksKey) return;
        _marksKey = key;
        global::System.Array.Clear(_markPixels, 0, _markPixels.Length);
        WorldMapEra era = ScenarioEditing.Era;

        // 地图范围：画出现在按哪个范围、哪档大小预演(没定范围的按预演用的那档，框外的开局没有)
        if (era.map == null && ScenarioWilderness.Frame is WorldMapFrame frame && ScenarioWilderness.Size.x > 0)
        {
            Vector2Int size = ScenarioWilderness.Size;
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
        if (focus != null && _tool is Tool.Realm or Tool.Relation)
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
            // 开局放不下的城标红
            Color32 color = !active ? Inactive : !ScenarioWilderness.Placed(city) ? Unplaced
                : _layer == Layer.Realm && owner != null ? ScenarioEditing.ColorOf(owner)
                : _layer is Layer.Kingdom or Layer.Empire or Layer.Tianxia or Layer.Division && AreaKey(city) is string area
                    ? ScenarioEditing.ColorOfKey(area)
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
        // 行政区工具：选中国家的都城描青边(京畿)，各行政区与京畿的治所画小方块(选中那一块的描金边)
        if (_tool == Tool.Division && ScenarioEditing.Polity(_realmId) is WorldMapPolity divided)
        {
            if (ScenarioEditing.City(divided.capital) is WorldMapCity capital) Ring(Texel(capital), radius + 2, RegionCapital);
            for (int d = -1; d < ScenarioEditing.Divisions(divided).Count; d++)
            {
                List<float[]> seats = ScenarioEditing.SeatsOf(divided, d);
                if (seats == null) continue;
                Color32 seatColor = ScenarioEditing.ColorOfKey(d >= 0 ? "d:" + divided.id + ":" + d : "j:" + divided.id);
                foreach (float[] point in seats)
                    if (point != null && point.Length >= 2)
                        Square(Texel(point[0], point[1]), Mathf.Max(0, radius - 2), seatColor, d == _divisionIndex ? Gold : Edge);
            }
        }
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
            // 无人区图层：大些的几片写类型(永久中立的注明)
            if (_layer == Layer.Wild)
                for (int r = 0; r < ScenarioWilderness.Pieces.Count; r++)
                {
                    ScenarioWilderness.Piece piece = ScenarioWilderness.Pieces[r];
                    if (piece.Zones.Count < MinLabelZones) continue;
                    Areas.Add((KindName(piece.Kind) + (ScenarioWilderness.Neutral(r) ? LM.Get("scenario_editor_wild_neutral_suffix") : ""),
                        Texel(piece.Lon, piece.Lat)));
                }
            if (_layer is Layer.Kingdom or Layer.Empire or Layer.Tianxia or Layer.Division)
                foreach (IGrouping<string, WorldMapCity> group in ScenarioEditing.Cities.Where(ScenarioEditing.Active)
                             .Select(city => (city, area: AreaKey(city))).Where(item => item.area != null && item.area[0] != 'i')
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
        if (string.IsNullOrEmpty(key)) return "";
        if (key == "n") return LM.Get("scenario_editor_neutral_name");
        string id = key.Substring(2);
        int cut = key.LastIndexOf(':');
        return key[0] switch
        {
            'd' => cut > 2 && int.TryParse(key.Substring(cut + 1), out int index)
                ? ScenarioEditing.DivisionName(ScenarioEditing.Polity(key.Substring(2, cut - 2)), index) : id,
            'j' => ScenarioEditing.DivisionName(ScenarioEditing.Polity(id), -1),
            'r' => ScenarioEditing.Region(id)?.TitleName ?? id,
            'k' => ScenarioEditing.Core(id)?.Name ?? id,
            't' => ScenarioEditing.TianxiaName(id),
            'x' => ScenarioEditing.Core(id)?.Name ?? id,
            'c' or 'i' => ScenarioEditing.City(id) is WorldMapCity city ? ScenarioEditing.CityName(city) : id,
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
        if (ScenarioEditing.Division(ScenarioEditing.Polity(_realmId), _divisionIndex) == null) _divisionIndex = -1;

        _title.text = string.Format(LM.Get("scenario_editor_title"), era.Name);
        _subtitle.text = era.YearText + (era.exact ? "  " + LM.Get("scenario_editor_exact_on").ColorString("#F3C34A") : "") +
                         (ScenarioEditing.Dirty ? "  " + LM.Get("scenario_editor_unsaved").ColorString("#E9A85B") : "");

        if (_eventIndex >= ScenarioEditing.Events.Count) _eventIndex = ScenarioEditing.Events.Count - 1;
        bool showScenario = _tool is Tool.View or Tool.City;
        bool showCity = _cityId != null && _tool is Tool.City or Tool.Realm or Tool.Kingdom or Tool.Empire;
        // 国家、王国法理、帝国法理工具里卡片常在：没有城的国家、法理在地图上点不到，得从卡片上翻到
        bool showRealm = _tool == Tool.Realm || _realmId != null && (_tool is Tool.City or Tool.Relation or Tool.Division ||
                                                                     _tool == Tool.Land && _landMode == LandMode.Realm);
        bool showRegion = _tool == Tool.Kingdom || _regionId != null && _tool == Tool.Empire;
        bool showCore = _tool == Tool.Empire;
        bool showMode = _tool is Tool.Realm or Tool.Kingdom or Tool.Empire or Tool.Division;
        _guideCard.gameObject.SetActive(_guideOpen);
        _modeCard.gameObject.SetActive(showMode);
        _scenarioCard.gameObject.SetActive(showScenario);
        _cityCard.gameObject.SetActive(showCity);
        _realmCard.gameObject.SetActive(showRealm);
        _declineCard.gameObject.SetActive(showRealm && ScenarioEditing.Polity(_realmId) != null);
        _regionCard.gameObject.SetActive(showRegion);
        _coreCard.gameObject.SetActive(showCore);
        _relationCard.gameObject.SetActive(_tool == Tool.Relation);
        _wallCard.gameObject.SetActive(_tool == Tool.Wall);
        _passCard.gameObject.SetActive(_tool == Tool.Pass && _passIndex >= 0);
        _eventCard.gameObject.SetActive(_tool == Tool.Event);
        _landCard.gameObject.SetActive(_tool == Tool.Land);
        _terrainCard.gameObject.SetActive(_tool == Tool.Terrain);
        _tribeCard.gameObject.SetActive(_tool == Tool.Tribe);
        _divisionCard.gameObject.SetActive(_tool == Tool.Division);
        _wildCard.gameObject.SetActive(_layer == Layer.Wild);

        if (_guideOpen) RefreshGuide();
        if (showMode) RefreshMode();
        if (showScenario) RefreshScenario(era);
        if (showCity) RefreshCity(era);
        if (showRealm) RefreshRealm();
        if (showRealm && ScenarioEditing.Polity(_realmId) != null) RefreshDecline();
        if (showRegion) RefreshRegion();
        if (showCore) RefreshCore();
        if (_tool == Tool.Relation) RefreshRelation();
        if (_tool == Tool.Wall) RefreshWall();
        if (_tool == Tool.Pass && _passIndex >= 0) RefreshPass();
        if (_tool == Tool.Event) RefreshEvent();
        if (_tool == Tool.Land) RefreshLand();
        if (_tool == Tool.Terrain) RefreshTerrain();
        if (_tool == Tool.Tribe) RefreshTribe();
        if (_tool == Tool.Division) RefreshDivision();
        if (_layer == Layer.Wild) RefreshWild();
        RefreshButtons();
    }

    private static void RefreshGuide()
    {
        for (int i = 0; i < GuideSteps; i++)
        {
            bool done = GuideDone(i);
            GuideBtns[i].Set((done ? "√ " : "") + LM.Get("scenario_editor_guide_" + i), done ? SaveColor : ViewColor);
        }
    }

    // 点地图时：选中、划入(行政区、法理各有叫法)、兼属(只有王国法理)；下面一句说现在点地图会怎样
    private static void RefreshMode()
    {
        string tool = _tool.ToString().ToLowerInvariant();
        _modeSelectBtn.Set(null, _mode == Mode.Select ? ActiveColor : ToolColor);
        _modeWriteBtn.Set(LM.Get("scenario_editor_mode_write_" + tool), _mode == Mode.Write ? ActiveColor : ToolColor);
        bool share = _tool == Tool.Kingdom;
        if (!share && _mode == Mode.Share) _mode = Mode.Select;
        _modeShareBtn.Button.gameObject.SetActive(share);
        _modeShareBtn.Set(null, _mode == Mode.Share ? ActiveColor : ToolColor);
        _modeInfo.text = _mode == Mode.Select ? LM.Get("scenario_editor_mode_select_info") : ModeHint();
    }

    // 划入、兼属时现在点地图会怎样
    private static string ModeHint()
    {
        string target = _tool switch
        {
            Tool.Realm => ScenarioEditing.Polity(_realmId)?.Name,
            Tool.Kingdom => ScenarioEditing.Region(_regionId)?.TitleName,
            Tool.Empire => ScenarioEditing.Core(_coreId)?.Name,
            Tool.Division => ScenarioEditing.Polity(_realmId) is WorldMapPolity polity ? ScenarioEditing.DivisionName(polity, _divisionIndex) : null,
            _ => null
        };
        if (target == null) return LM.Get("scenario_editor_hint_need_target");
        return string.Format(LM.Get(_mode == Mode.Share ? "scenario_editor_hint_kingdom_share"
            : "scenario_editor_hint_" + _tool.ToString().ToLowerInvariant() + "_write"), target);
    }

    private static void RefreshLand()
    {
        string key = BrushKey();
        _landBrushBtn.Set(BrushText(), ViewColor);
        _landShapeBtn.Set(ShapeText(), ViewColor);
        _landModeBtn.Set(LM.Get("scenario_editor_land_mode_" + _landMode.ToString().ToLowerInvariant()), ViewColor);
        string target = key == null ? LM.Get("scenario_editor_land_no_target") : AreaName(key);
        _landInfo.text = string.Format(LM.Get("scenario_editor_land_info"), target, ScenarioEditing.LandCount(key), ScenarioEditing.LandCells.Count) +
                         "\n" + LM.Get("scenario_editor_land_note_" + _landMode.ToString().ToLowerInvariant()) +
                         "\n" + LM.Get("scenario_editor_land_zone_note") +
                         (ScenarioEditing.RestoresBase ? "\n" + LM.Get("scenario_editor_land_base_note") : "");
        bool restores = ScenarioEditing.RestoresBase;
        _clearLandBtn.Set(LM.Get(Armed("land") ? "scenario_editor_clear_armed" : restores ? "scenario_editor_clear_land_base" : "scenario_editor_clear_land"),
            DangerColor, ScenarioEditing.LandEdited);
    }

    private static void RefreshTerrain()
    {
        _terrainBrushBtn.Set(BrushText(), ViewColor);
        _terrainShapeBtn.Set(ShapeText(), ViewColor);
        _developBrushBtn.Set(LM.Get(_developBrush ? "scenario_editor_develop_on" : "scenario_editor_develop_off"), _developBrush ? ActiveColor : ToolColor);
        _developValueBtn.Button.transform.parent.gameObject.SetActive(_developBrush);
        _developValueBtn.Set(_developValue.ToString(), ViewColor);
        _terrainPalette.gameObject.SetActive(!_developBrush);
        string chosen = TerrainPalette[Mathf.Clamp(_terrainType, 0, TerrainPalette.Length - 1)].type;
        foreach ((string type, Btn button) in TerrainButtons) button.Set(null, type == chosen ? ActiveColor : ToolColor);
        _terrainInfo.text = _developBrush
            ? string.Format(LM.Get("scenario_editor_develop_info"), _developValue, ScenarioEditing.DevelopCells.Count)
            : string.Format(LM.Get("scenario_editor_terrain_info"), ScenarioEditing.TerrainCells.Count);
        _clearTerrainBtn.Button.gameObject.SetActive(!_developBrush);
        _clearDevelopBtn.Button.gameObject.SetActive(_developBrush);
        _clearTerrainBtn.Set(LM.Get(Armed("terrain") ? "scenario_editor_clear_armed" : "scenario_editor_clear_terrain"), DangerColor,
            ScenarioEditing.TerrainCells.Count > 0);
        _clearDevelopBtn.Set(LM.Get(Armed("develop") ? "scenario_editor_clear_armed" : "scenario_editor_clear_develop"), DangerColor,
            ScenarioEditing.DevelopCells.Count > 0);
    }

    // 行政区卡：选中的国家(不称帝时提醒不生效)、选中的行政区(京畿在头)、名、城与治所
    private static void RefreshDivision()
    {
        WorldMapPolity polity = ScenarioEditing.Polity(_realmId);
        _divisionNameInput.interactable = polity != null && _divisionIndex >= 0;
        _seatModeBtn.Set(LM.Get(_seatMode ? "scenario_editor_seat_mode_on" : "scenario_editor_seat_mode_off"), _seatMode ? ActiveColor : ToolColor,
            polity != null);
        if (polity == null)
        {
            _divisionHead.text = LM.Get("scenario_editor_division_pick");
            _divisionBtn.Set(LM.Get("scenario_editor_none"), ViewColor, false);
            SetInput(_divisionNameInput, "");
            _divisionInfo.text = LM.Get("scenario_editor_division_help");
            _newDivisionBtn.Set(null, ToolColor, false);
            _deleteDivisionBtn.Set(null, DangerColor, false);
            _clearSeatsBtn.Set(null, DangerColor, false);
            return;
        }
        List<WorldMapDivision> divisions = ScenarioEditing.Divisions(polity);
        _divisionHead.text = string.Format(LM.Get("scenario_editor_division_head"), polity.Name, divisions.Count) +
                             (polity.empire ? "" : "\n" + LM.Get("scenario_editor_division_not_empire").ColorString("#E9A85B"));
        _divisionBtn.Set(ScenarioEditing.DivisionName(polity, _divisionIndex), ViewColor);
        SetInput(_divisionNameInput, _divisionIndex >= 0 ? ScenarioEditing.DivisionName(polity, _divisionIndex) : "");
        // 这一块的城：本国开局归它的城里，在这个行政区的(京畿连同都城)
        List<string> names = ScenarioEditing.Cities
            .Where(city => ScenarioEditing.Active(city) && ScenarioEditing.OwnerOf(city) == polity && ScenarioEditing.DivisionOf(polity, city.id) == _divisionIndex)
            .OrderBy(city => city.id == polity.capital ? 0 : 1).ThenBy(ScenarioEditing.RankOf).Select(ScenarioEditing.CityName).ToList();
        int seats = ScenarioEditing.SeatsOf(polity, _divisionIndex)?.Count ?? 0;
        WorldMapCity capital = ScenarioEditing.City(polity.capital);
        _divisionInfo.text = string.Format(LM.Get("scenario_editor_division_info"), names.Count,
                                 names.Count == 0 ? LM.Get("scenario_editor_none") : string.Join(LM.Get("list_separator"), names.Take(10)) +
                                                                                    (names.Count > 10 ? "…" : ""), seats) + "\n" +
                             string.Format(LM.Get("scenario_editor_division_capital_note"),
                                 capital == null ? LM.Get("scenario_editor_none") : ScenarioEditing.CityName(capital)) + "\n" +
                             LM.Get("scenario_editor_division_seat_note");
        _newDivisionBtn.Set(null, ToolColor);
        _deleteDivisionBtn.Set(null, DangerColor, _divisionIndex >= 0);
        _clearSeatsBtn.Set(null, DangerColor, seats > 0);
    }

    // 无人区卡：按哪个范围、哪档大小预测，一块地多大，几片几块(各类)，永久中立几片，开发度按哪个时代的表
    private static void RefreshWild()
    {
        _wildInfoVersion = ScenarioWilderness.Version;
        _wildSizeBtn.Set(SizeText(), ViewColor, ScenarioEditing.Era?.HasSize != true);
        bool ownWild = ScenarioEditing.Era?.own_wilderness == true;
        _wildOwnBtn.Set(LM.Get(ownWild ? "scenario_editor_wild_own_on" : "scenario_editor_wild_own_follow"), ViewColor);
        // 按类型编号记(1 山脉 … 6 熔岩，见 WildKind)；多了类型这里跟着放大
        int kindCount = (int)WildKind.Lava + 1;
        var regions = new int[kindCount];
        var zones = new int[kindCount];
        ScenarioWilderness.Summary(regions, zones, out int neutral);
        string kinds = string.Join(LM.Get("list_separator"), Enumerable.Range(1, kindCount - 1).Where(kind => regions[kind] > 0)
            .Select(kind => string.Format(LM.Get("scenario_editor_wild_kind_count"), KindName((WildKind)kind), regions[kind], zones[kind])));
        WorldMapFrame frame = ScenarioWilderness.Frame;
        Vector2Int size = ScenarioWilderness.Size;
        string table = WorldMapData.BaseOf(ScenarioEditing.Era);
        string tableText = ScenarioEditing.UsesImport ? LM.Get("scenario_editor_wild_imported")
            : table == null ? LM.Get("scenario_editor_wild_no_table")
            : string.Format(LM.Get("scenario_editor_wild_table"), WorldMapData.Eras.FirstOrDefault(era => era.id == table)?.Name ?? table);
        _wildInfo.text = string.Format(LM.Get("scenario_editor_wild_info"), frame == null ? "" : LM.Get("world_map_frame_" + frame.id),
                             size.x * 64, size.y * 64, ScenarioWilderness.ZoneDegrees.ToString("0.##"), regions.Sum(), zones.Sum(),
                             kinds.Length == 0 ? LM.Get("scenario_editor_none") : kinds, neutral) + "\n" + tableText + "\n" +
                         (ScenarioWilderness.SettingOff ? LM.Get("scenario_editor_wild_setting_off") + "\n"
                             : ownWild && ScenarioEditing.Era?.exact == true ? LM.Get("scenario_editor_wild_own_exact") + "\n"
                             : ownWild ? LM.Get("scenario_editor_wild_own_note") + "\n"
                             : ScenarioEditing.Era?.exact == true ? LM.Get("scenario_editor_wild_exact") + "\n" : "") +
                         LM.Get("scenario_editor_wild_note");
    }

    // 现在按多大预演：东西 × 南北 多少格
    private static string SizeText()
    {
        Vector2Int size = ScenarioWilderness.Size;
        return ScenarioEditing.Era?.HasSize == true
            ? string.Format(LM.Get("scenario_editor_size_fixed"), size.x * 64, size.y * 64)
            : string.Format(LM.Get("scenario_editor_size_value"), LM.Get("world_map_size_" + Mathf.Clamp(ScenarioWilderness.SizeIndex, 0, 2)), size.x * 64, size.y * 64);
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
        if (ScenarioEditing.Era?.exact == true) _tribeInfo.text += "\n" + LM.Get("scenario_editor_tribe_exact");
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
        _scenarioVersion = ScenarioWilderness.Version;
        _frameBtn.Set(string.IsNullOrEmpty(era.frame) ? LM.Get("scenario_editor_frame_any") : LM.Get("world_map_frame_" + era.frame), ViewColor);
        _frameInfo.text = !imported && string.IsNullOrEmpty(era.frame) && ScenarioWilderness.Frame != null
            ? string.Format(LM.Get("scenario_editor_frame_preview"), LM.Get("world_map_frame_" + ScenarioWilderness.Frame.id)) : "";
        // 地图大小
        SetInput(_sizeWInput, era.HasSize ? (era.size[0] * 64).ToString() : "");
        SetInput(_sizeHInput, era.HasSize ? (era.size[1] * 64).ToString() : "");
        WorldMapFrame preview = ScenarioWilderness.Frame;
        _sizeInfo.text = era.HasSize ? LM.Get("scenario_editor_size_note_fixed") + (era.size[0] * era.size[1] > BigMapChunks ? "\n" + LM.Get("scenario_editor_size_note_big") : "")
            : preview?.sizes == null ? ""
            : string.Format(LM.Get("scenario_editor_size_note_free"), string.Join(" / ", Enumerable.Range(0, preview.sizes.Length).Select(index =>
                string.Format(LM.Get("scenario_editor_size_value"), LM.Get("world_map_size_" + index), preview.sizes[index].x * 64, preview.sizes[index].y * 64))));
        // 照原图分地、本剧本无人区、清空全部城
        bool canExact = ScenarioEditing.CanExact;
        _exactBtn.Button.gameObject.SetActive(canExact);
        _exactInfo.gameObject.SetActive(canExact);
        _exactBtn.Set(LM.Get(era.exact ? "scenario_editor_exact_on" : "scenario_editor_exact_off"), era.exact ? ActiveColor : ToolColor);
        _exactInfo.text = LM.Get(era.exact ? "scenario_editor_exact_note_on" : "scenario_editor_exact_note_off");
        _scenarioWildBtn.Set(LM.Get("scenario_editor_wild_own") + "：" + LM.Get(era.own_wilderness ? "scenario_editor_wild_own_on" : "scenario_editor_wild_own_follow"),
            era.own_wilderness ? ActiveColor : ToolColor);
        _clearCitiesBtn.Set(LM.Get(Armed("cities") ? "scenario_editor_clear_armed" : "scenario_editor_clear_cities"), DangerColor,
            ScenarioEditing.Cities.Count > 0);
        SetInput(_nameInput, era.Name);
        SetInput(_yearInput, era.year.ToString());
        SetInput(_authorInput, era.author);
        SetInput(_descInput, era.Description);
        int cities = ScenarioEditing.Cities.Count(ScenarioEditing.Active);
        int placed = ScenarioEditing.Cities.Count(city => ScenarioEditing.Active(city) && ScenarioWilderness.Placed(city));
        _scenarioInfo.text = string.Format(LM.Get("scenario_editor_scenario_info"), era.polities.Count, cities,
            LM.Get(era.OwnWorld ? "scenario_editor_own_world" : "scenario_editor_builtin_world"), placed);
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
        // 城在无人区里：只留城心那块(见 ScenarioWilderness)
        WildKind wild = ScenarioWilderness.KindNear(city.lon, city.lat);
        string wildLine = wild == WildKind.None ? "" : "\n" + string.Format(LM.Get("scenario_editor_city_in_wild"), KindName(wild)).ColorString("#E9A85B");
        _cityInfo.text = string.Format(LM.Get("scenario_editor_city_info"), region?.TitleName ?? city.region,
            owner == null ? LM.Get("scenario_editor_independent") : owner.Name + (capital ? LM.Get("scenario_editor_is_capital") : ""),
            string.Format("{0:0.0}, {1:0.0}", city.lon, city.lat)) + shares + wildLine +
            (ScenarioWilderness.PlaceProblem(city) is string problem ? "\n" + LM.Get(problem).ColorString("#E9A85B") : "");
        _rankBtn.Set(LM.Get("scenario_editor_rank_" + Mathf.Clamp(ScenarioEditing.RankOf(city), 1, 3)), ToolColor);
        // 写明立给谁：选中的国家可能不是这座城所在的国
        _capitalBtn.Set(realm == null ? LM.Get("scenario_editor_set_capital") : string.Format(LM.Get("scenario_editor_set_capital_of"),
            realm.Name.Length > 5 ? realm.Name.Substring(0, 5) + "…" : realm.Name), ToolColor, realm != null && realm.capital != city.id);
        _foundBtn.Set(null, ToolColor, !capital);
        _deleteCityBtn.Set(null, DangerColor);
    }

    private static void RefreshRealm()
    {
        WorldMapPolity realm = ScenarioEditing.Polity(_realmId);
        List<WorldMapPolity> polities = ScenarioEditing.Era.polities;
        _realmPickBtn.Set(realm?.Name ?? LM.Get("scenario_editor_none"), ViewColor, polities.Count > 0);
        _realmNameInput.interactable = _famInput.interactable = _givInput.interactable = _ageInput.interactable = realm != null;
        WorldMapCity picked = ScenarioEditing.City(_cityId);
        if (realm == null)
        {
            SetInput(_realmNameInput, "");
            SetInput(_famInput, "");
            SetInput(_givInput, "");
            SetInput(_ageInput, "");
            _cultureBtn.Set(LM.Get("scenario_editor_none"), ViewColor, false);
            _empireBtn.Set(LM.Get("scenario_editor_empire_off"), ToolColor, false);
            _realmInfo.text = LM.Get(polities.Count > 0 ? "scenario_editor_realm_pick_hint" : "scenario_editor_realm_none");
            _claimBtn.Set(LM.Get("scenario_editor_claim_add"), ToolColor, false);
            _deleteRealmBtn.Set(null, DangerColor, false);
            return;
        }
        SetInput(_realmNameInput, realm.Name);
        _cultureBtn.Set(CultureName(realm.culture), ViewColor);
        _empireBtn.Set(LM.Get(realm.empire ? "scenario_editor_empire_on" : "scenario_editor_empire_off"), realm.empire ? ActiveColor : ToolColor);
        SetInput(_famInput, realm.ruler?.fam == null ? "" : WorldMapData.PickParts(realm.ruler.fam));
        SetInput(_givInput, realm.ruler?.giv == null ? "" : WorldMapData.PickParts(realm.ruler.giv));
        SetInput(_ageInput, realm.ruler != null && realm.ruler.age > 0 ? realm.ruler.age.ToString() : "");
        WorldMapCity seat = ScenarioEditing.City(realm.capital);
        string claims = string.Join(LM.Get("list_separator"), (realm.regions ?? new List<string>()).Take(8).Select(id => ScenarioEditing.Region(id)?.TitleName ?? id));
        _realmInfo.text = string.Format(LM.Get("scenario_editor_realm_info"),
                              seat == null ? LM.Get("scenario_editor_none") : ScenarioEditing.CityName(seat), ScenarioEditing.CityCount(realm)) +
                          (seat == null ? "\n" + LM.Get("scenario_editor_realm_no_capital").ColorString("#E9A85B") : "") +
                          (realm.regions is { Count: > 0 } ? "\n" + string.Format(LM.Get("scenario_editor_realm_claims"), claims) : "");
        bool claimed = picked != null && realm.regions?.Contains(picked.region) == true;
        _claimBtn.Set(LM.Get(claimed ? "scenario_editor_claim_remove" : "scenario_editor_claim_add"), claimed ? ActiveColor : ToolColor, picked != null);
        _deleteRealmBtn.Set(null, DangerColor);
    }

    // 国祚与积弊卡：写了的照写的显示，空着的框里灰字是开局会给的默认值；不称帝的国家不生效，框灰掉(写了的照样显示，可清回默认)
    private static void RefreshDecline()
    {
        WorldMapPolity realm = ScenarioEditing.Polity(_realmId);
        bool on = realm?.empire == true;
        foreach (string key in ScenarioEditing.DeclineKeys)
        {
            if (!DeclineInputs.TryGetValue(key, out var field)) continue;
            field.input.interactable = on;
            SetInput(field.input, ScenarioEditing.DeclineText(realm, key));
            field.hint.text = on ? string.Format(LM.Get("scenario_editor_decline_default"),
                ScenarioEditing.DeclineFormat(key, ScenarioEditing.DeclineDefault(realm, key))) : "";
        }
        _declineInfo.text = LM.Get(on ? "scenario_editor_decline_hint" : "scenario_editor_decline_off");
        _declineClearBtn.Set(null, ToolColor, realm?.decline is { Count: > 0 });
    }

    private static string DeclineCaption(string key) => key switch
    {
        ScenarioEditing.DeclineAge => "scenario_editor_dynasty_age",
        ScenarioEditing.DeclineStrain => "scenario_editor_strain",
        _ => "malady_" + key
    };

    private static void RefreshRegion()
    {
        WorldMapRegion region = ScenarioEditing.Region(_regionId);
        WorldMapCity city = ScenarioEditing.City(_cityId);
        _regionPickBtn.Set(region?.TitleName ?? LM.Get("scenario_editor_none"), ViewColor, ScenarioEditing.Regions.Count > 0);
        _regionNameInput.interactable = _regionTitleInput.interactable = region != null;
        if (region == null)
        {
            SetInput(_regionNameInput, "");
            SetInput(_regionTitleInput, "");
            _regionCultureBtn.Set(LM.Get("scenario_editor_none"), ViewColor, false);
            _regionInfo.text = LM.Get("scenario_editor_region_none");
            _regionCapitalBtn.Set(null, ToolColor, false);
            _newRegionBtn.Set(null, ToolColor, city != null);
            _deleteRegionBtn.Set(null, DangerColor, false);
            return;
        }
        SetInput(_regionNameInput, region.Name);
        SetInput(_regionTitleInput, region.TitleName);
        _regionCultureBtn.Set(CultureName(ScenarioEditing.RegionCulture(region)), ViewColor);
        WorldMapCore core = ScenarioEditing.CoreOfRegion(region.id);
        WorldMapPolity named = ScenarioEditing.RegionNamedAfter(region, out List<WorldMapPolity> others);
        _regionInfo.text = string.Format(LM.Get("scenario_editor_region_info"), ScenarioEditing.RegionCapitalName(region),
                               ScenarioEditing.RegionCityCount(region.id), core?.Name ?? LM.Get("scenario_editor_none")) +
                           (named != null ? "\n" + string.Format(LM.Get("scenario_editor_region_named_after"), named.Name).ColorString("#E9A85B") : "") +
                           (others.Count > 0 ? "\n" + string.Format(LM.Get("scenario_editor_region_own_titles"),
                               string.Join(LM.Get("list_separator"), others.Select(item => item.Name))).ColorString("#E9A85B") : "");
        _regionCapitalBtn.Set(null, ToolColor, city != null && city.region == region.id && region.capital != city.id);
        _newRegionBtn.Set(null, ToolColor, city != null);
        _deleteRegionBtn.Set(null, DangerColor, ScenarioEditing.Cities.All(item => item.region != region.id));
    }

    private static void RefreshCore()
    {
        WorldMapCore core = ScenarioEditing.Core(_coreId);
        WorldMapCity city = ScenarioEditing.City(_cityId);
        _corePickBtn.Set(core?.Name ?? LM.Get("scenario_editor_none"), ViewColor, ScenarioEditing.Cores.Count > 0);
        _coreNameInput.interactable = core != null;
        if (core == null)
        {
            SetInput(_coreNameInput, "");
            _coreInfo.text = LM.Get("scenario_editor_core_none");
            _coreCapitalBtn.Set(null, ToolColor, false);
            _newCoreBtn.Set(null, ToolColor, _regionId != null);
            _leaveCoreBtn.Set(null, ToolColor, false);
            _deleteCoreBtn.Set(null, DangerColor, false);
            _tianxiaBtn.Set(ScenarioEditing.TianxiaName(""), ViewColor, false);
            SetInput(_tianxiaNameInput, "");
            _tianxiaNameInput.interactable = false;
            return;
        }
        SetInput(_coreNameInput, core.Name);
        _coreInfo.text = string.Format(LM.Get("scenario_editor_core_info"), ScenarioEditing.CoreCapitalName(core), core.regions?.Count ?? 0,
            string.Join(LM.Get("list_separator"), (core.regions ?? new List<string>()).Take(8)
                .Select(id => ScenarioEditing.Region(id)?.TitleName ?? id)));
        _coreCapitalBtn.Set(null, ToolColor, city != null && core.regions?.Contains(city.region) == true && core.capital != city.id);
        _newCoreBtn.Set(null, ToolColor, _regionId != null);
        _leaveCoreBtn.Set(null, ToolColor, _regionId != null && core.regions?.Contains(_regionId) == true);
        _deleteCoreBtn.Set(null, DangerColor);
        _tianxiaBtn.Set(ScenarioEditing.TianxiaName(core.group), ViewColor, true);
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
            // 用一句话写明先点的是谁、后点的是谁
            _relationInfo.text = string.Format(LM.Get("scenario_editor_relation_pick"), ScenarioEditing.RelationSentence(kind, "A", "B"));
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
        _wallInfo.text = string.Format(LM.Get("scenario_editor_wall_info"), walls.Count, wall?.pts?.Count ?? 0) + "\n" +
                         LM.Get(wall == null ? "scenario_editor_wall_start" : "scenario_editor_wall_continue") +
                         (wall != null && (wall.pts?.Count ?? 0) < 2 ? "\n" + LM.Get("scenario_editor_wall_short").ColorString("#E9A85B") : "") + "\n" +
                         LM.Get("scenario_editor_wall_note");
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
        // 内容没变不重搭；鼠标按着或刚松开也先不搭：按着的按钮一拆，这一下点击就白点了(打完字直接点别的按钮时最常见)
        string signature = item == null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(item) + "|" + _eventIndex + "|" + _pickSlot + "|" + _pickStep + "|" + _pickEffect +
                                                "|" + ScenarioEditing.EventProblems(item);
        if (signature == _eventSignature && !_eventPending) return;
        if (Input.GetMouseButton(0) || Time.frameCount - _mouseUpFrame < 2)
        {
            _eventPending = true;
            return;
        }
        _eventPending = false;
        _eventSignature = signature;
        for (int i = _eventBody.childCount - 1; i >= 0; i--) Object.Destroy(_eventBody.GetChild(i).gameObject);
        if (item == null)
        {
            Label(_eventBody, "Empty", 14, DimText, TextAnchor.UpperLeft, true).text = LM.Get("scenario_editor_event_empty");
            return;
        }
        int index = _eventIndex;
        int problems = ScenarioEditing.EventProblems(item);
        if (problems > 0)
            Label(_eventBody, "Problems", 14, TextColor, TextAnchor.UpperLeft, true).text =
                string.Format(LM.Get("scenario_editor_event_problems"), problems).ColorString("#E9A85B");
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
            case "person":
                SetInput(Field(_eventBody, caption, text => ScenarioEditing.SetStepSlot(index, effect, at, "person", text)), step.person);
                // 人物只认剧本写了名字的：填错当场说，别等开局才发现事件没做成
                List<string> unknown = ScenarioEditing.UnknownPeople(step.person);
                Label(_eventBody, "Note", 13, DimText, TextAnchor.UpperLeft, true).text = unknown.Count > 0
                    ? string.Format(LM.Get("scenario_editor_event_person_unknown"), string.Join(LM.Get("list_separator"), unknown)).ColorString("#E9A85B")
                    : LM.Get("scenario_editor_event_person_note");
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
            "a" or "b" => ScenarioEditing.Polity(value)?.Name ?? LM.Get("scenario_editor_event_stale"),
            "city" => ScenarioEditing.City(value) is WorldMapCity city ? ScenarioEditing.CityName(city) : LM.Get("scenario_editor_event_stale"),
            "region" => ScenarioEditing.Region(value)?.TitleName ?? LM.Get("scenario_editor_event_stale"),
            _ => value
        };
    }

    private static string CultureName(string culture) =>
        CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture ?? "";

    // 输入框空着时的灰字
    private static Text Placeholder(InputField input)
    {
        Text text = Label(input.transform, "Placeholder", 15, DimText, TextAnchor.MiddleLeft);
        text.supportRichText = false;
        Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(6f, 2f), new Vector2(-6f, -2f));
        input.placeholder = text;
        return text;
    }

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
        _styleBtn.Set(LM.Get(_smoothFill ? "scenario_editor_style_smooth" : "scenario_editor_style_square"), ViewColor);
        _saveBtn.Set(null, ScenarioEditing.Dirty ? ActiveColor : SaveColor);
        bool armed = _closeArmedAt > 0f && Time.unscaledTime - _closeArmedAt <= ConfirmSeconds;
        _closeBtn.Set(LM.Get(armed ? "scenario_editor_close_armed" : "scenario_editor_close"), armed ? DangerColor : ViewColor);
    }

    private static void UpdateTip()
    {
        WorldMapCity city = ScenarioEditing.City(_hoverCity);
        string text = null;
        if (_moving && _tool == Tool.City && _hoverOnMap && ScenarioEditing.City(_pressCity) is WorldMapCity moving)
        {
            // 拖着城：落在这里会不会在无人区里
            WildKind kind = ScenarioWilderness.KindNear(TexelToLon(_hoverTexel.x), TexelToLat(_hoverTexel.y));
            text = ScenarioEditing.CityName(moving) + (kind == WildKind.None ? ""
                : "  " + string.Format(LM.Get("scenario_editor_city_in_wild_short"), KindName(kind)).ColorString("#E9A85B"));
        }
        else if (city != null)
        {
            WorldMapPolity owner = ScenarioEditing.OwnerOf(city);
            string area = _layer is Layer.Kingdom or Layer.Empire or Layer.Tianxia or Layer.Division && AreaKey(city) is string key && key[0] != 'i'
                ? "  " + AreaName(key) : "";
            WildKind kind = ScenarioWilderness.KindNear(city.lon, city.lat);
            string wild = kind == WildKind.None ? "" : "  " + string.Format(LM.Get("scenario_editor_city_in_wild_short"), KindName(kind));
            text = ScenarioEditing.CityName(city) + "  " +
                   (owner == null ? LM.Get("scenario_editor_independent") : owner.Name).ColorString("#7FD8EA") + area.ColorString("#F3C34A") +
                   wild.ColorString("#E9A85B") +
                   (ScenarioWilderness.PlaceProblem(city) is string problem ? "\n" + LM.Get(problem).ColorString("#E9A85B") : "");
        }
        else if (_hoverPass >= 0 && _hoverPass < ScenarioEditing.Passes.Count) text = ScenarioEditing.Passes[_hoverPass].Name;
        else if (ScenarioEditing.TribesOf(_hoverTribe) is WorldMapTribes tribes)
            text = string.Format(LM.Get("scenario_editor_tribe_tip"), CultureName(tribes.culture), TribeCount(tribes));
        else if (_hoverOnMap && _brushButton < 0)
        {
            int zone = ScenarioWilderness.ZoneAt(TexelToLon(_hoverTexel.x), TexelToLat(_hoverTexel.y));
            text = WildTip(zone) ?? ZoneTip(zone);
        }
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

    // 悬停一块地：开局归哪座城、哪国(刷的注明)；补小国、没人领的也说一声
    private static string ZoneTip(int zone)
    {
        if (_zoneOwner == null || _layer is Layer.Terrain or Layer.Wild || zone < 0 || zone >= _zoneOwner.Length) return null;
        int owner = _zoneOwner[zone];
        if (owner == ScenarioPreviewRules.Filler) return LM.Get("scenario_editor_zone_filler");
        if (owner < 0) return ScenarioEditing.Era?.exact == true && ScenarioWilderness.Claimable(zone) ? LM.Get("scenario_editor_zone_unclaimed") : null;
        if (owner >= _seatCities.Length || _seatCities[owner] == null) return null;
        WorldMapCity city = _seatCities[owner];
        WorldMapPolity polity = ScenarioEditing.OwnerOf(city);
        return string.Format(LM.Get("scenario_editor_zone_tip"), ScenarioEditing.CityName(city), polity?.Name ?? LM.Get("scenario_editor_independent")) +
               (_zonePainted[zone] ? LM.Get("scenario_editor_zone_painted") : "");
    }

    // 悬停无人区(预测)：类型、几块、开发度；挂哪块法理(刷的、史实、最近的城)；归谁(取得法理、周边城池同属)或中立；不是无人区为 null
    private static string WildTip(int zone)
    {
        int region = ScenarioWilderness.RegionOf(zone);
        if (region < 0) return null;
        WildKind kind = ScenarioWilderness.KindOf(zone);
        string head = string.Format(LM.Get("scenario_editor_wild_tip_head"), KindName(kind), ScenarioWilderness.Pieces[region].Zones.Count);
        float dev = ScenarioWilderness.DevOf(zone);
        if (dev >= 0f) head += "  " + string.Format(LM.Get("scenario_editor_wild_tip_dev"), dev.ToString("0"));
        var lines = new List<string> { head.ColorString("#F3C34A") };
        ScenarioWilderness.Source source = ScenarioWilderness.SourceOf(zone);
        string title = ScenarioWilderness.TitleOf(zone);
        if (title != null)
            lines.Add(string.Format(LM.Get("scenario_editor_wild_tip_title"), ScenarioEditing.Region(title)?.TitleName ?? title,
                LM.Get("scenario_editor_wild_source_" + source.ToString().ToLowerInvariant())));
        string owner = ScenarioWilderness.OwnerOf(zone);
        lines.Add(source == ScenarioWilderness.Source.Neutral ? LM.Get("scenario_editor_wild_tip_neutral")
            : owner == null ? LM.Get("scenario_editor_wild_tip_unowned")
            : string.Format(LM.Get("scenario_editor_wild_tip_owner"), OwnerName(owner),
                LM.Get(source == ScenarioWilderness.Source.Surround ? "scenario_editor_wild_rule_surround" : "scenario_editor_wild_rule_title")));
        return string.Join("\n", lines);
    }

    // 无人区归谁："p:国家"、"c:独立的城"、"f" 补空小国
    private static string OwnerName(string key)
    {
        if (key == "f") return LM.Get("scenario_editor_wild_filler");
        if (key.StartsWith("c:"))
            return string.Format(LM.Get("scenario_editor_wild_independent"), ScenarioEditing.CityName(ScenarioEditing.City(key.Substring(2))));
        return ScenarioEditing.Polity(key.Substring(2))?.Name ?? key;
    }

    private static string KindName(WildKind kind) => LM.Get("wild_kind_" + kind.ToString().ToLowerInvariant());

    // 放城、挪城落在无人区里时跟在提示后面的一句
    private static string WildHint(float lon, float lat)
    {
        WildKind kind = ScenarioWilderness.KindNear(lon, lat);
        return kind == WildKind.None ? "" : "  " + string.Format(LM.Get("scenario_editor_city_in_wild"), KindName(kind));
    }

    private static void UpdateStatus()
    {
        string text;
        if (_message != null && Time.unscaledTime - _messageAt <= MessageSeconds) text = _message;
        else if (_tool == Tool.Terrain && _developBrush) text = LM.Get("scenario_editor_hint_develop");
        else if (_tool == Tool.Land && _landMode == LandMode.Neutral) text = LM.Get("scenario_editor_hint_neutral");
        else if (_mode != Mode.Select && _tool is Tool.Realm or Tool.Kingdom or Tool.Empire or Tool.Division) text = ModeHint();
        else text = LM.Get("scenario_editor_hint_" + _tool.ToString().ToLowerInvariant());
        if (_status.text != text) _status.text = text;
    }

    #endregion
}

public sealed class ScenarioEditorHost : MonoBehaviour
{
    private void Update() => ScenarioEditorScreen.Tick(this);
}
