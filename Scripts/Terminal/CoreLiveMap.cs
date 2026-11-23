#if !EC_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EmpireCraft.Scripts.Terminal;

// 游戏里看核心的实时地图(用户 2026-11-18："核心在外面并行算，然后指令给游戏本体""那就先做"——先做实时地图)：
// 服务器核心接手这一局时(见 CoreHost)，游戏本体的世界冻着、屏幕原本是黑的。这里把黑屏换成按核心报来的数据画的地图：
//   · 数据：游戏以管理员身份连本机核心(管理员码，配了账号密码的向核心要一张一次性的票)，每秒问一回
//     /api/admin/live/map 与 /api/admin/live/army(见 TerminalHost.Live)，版本号没变就不重画。下载、解析、算底图的像素都在后台线程，
//     主线程只换贴图、摆字；
//   · 底图照原版的像素(用户 2026-11-18 "核心地图模式不能完全仿照原版的像素吗")：一格地一个像素，颜色同原版(地形格的 Type.color，
//     从游戏冻住的这一局世界取一回，核心跑着时地形基本不动)；国色半透明叠上去，国界在地块边上描深一号，交战两国之间描朱红；
//     核心那边新报来的归属整张重算(后台线程)，换上时淡入，国界不再一下跳过去；
//   · 政体、法理两层(速度栏上切换)，逐级展开、铭牌贴着版图写，都照手机舆图(见 CoreLiveMap.Tree)；
//   · 上层：城与都城用原版的城、皇冠图标(拉近到城才出)；军队用游戏里兵牌那一套框
//     (按任务分色)加兵种图标，同一国挨得近的并成一个牌子写总人数，在两份军情之间按时间插值走(同手机舆图)，不再一跳一跳；
//     交战牌子用游戏里交战标记的框，拉远是原版的战争图标；鼠标底下是哪一国写在顶上；
//   · 游戏本体的世界一直冻着、不算任何东西；画面只换国色那张图(几秒一回)和每帧摆军牌；
//   · 操作：滚轮缩放(以鼠标为准)、按住左键或右键拖动；牌子缩到左上角，「收起地图」回到黑屏；
//   · 速度(用户 2026-11-18 "不要用 3000 这种计量了，还是沿用游戏的")：右上角一条速度栏，按原版的速度档(×0.5～×40，
//     见 AssetManager.time_scales)一档一档加减、暂停与继续；档位折成核心的流速(×N = N×100%，原版音速档每帧两步也算进去)
//     以管理员身份发给核心(POST /api/admin speed/pause)，游戏自己的速度档跟着改，带回后照这一档走。
//     核心接手后头一回连上时，核心改用游戏原来选的那一档；"实际"按核心报的实际流速折成 ×N；
//   · 不改游戏本体的世界：原版摄像机照旧什么都不画，这块地图挂在黑屏牌子的画布底下(黑屏关画布时跳过它，恢复画面时一起收)。
// 只看不改(指令转发是下一步)。核心那头没这两个接口(老核心)就在顶上说一声，照旧黑屏
internal static partial class CoreLiveMap
{
    // 牌子上「看地图 / 收起地图」(默认看)
    internal static bool Wanted = true;
    internal static bool Showing => _root != null && _root.activeSelf;

    // ───────── 后台线程拿到的数据 ─────────
    private struct Realm
    {
        public string Name;
        public Color32 Color;
        public int N;
        public float X, Y;
    }

    private struct Town
    {
        public string Name;
        public float X, Y;
        public int R, Z, Dk, De, Dt;
        public bool Cap;
    }

    private struct Army
    {
        public string Id, Arm, T;
        public int R, N;
        public float X, Y;
    }

    private struct Battle
    {
        public float X, Y;
        public string C;
        public int P, A, D, H, Z;
        public bool L;
    }

    private sealed class MapData
    {
        public int W, H, Stamp, Tile;
        public ushort[] Cells;
        // 每块地属哪座城(Towns 的下标，0xFFFF 没有)
        public ushort[] ZoneCity;
        public Realm[] Realms;
        public Town[] Towns;
        public HashSet<int> Fronts;
        // 各国所在帝国(EmpName 的下标，-1 独立)
        public int[] RealmEmp;
        public string[] EmpName, DkName, DeName, DtName;
        public Color32[] EmpColor, DkColor, DeColor, DtColor;
        // 政体、法理两棵树(见 CoreLiveMap.Tree)
        public Tree[] Trees;
    }

    private sealed class ArmyData
    {
        public int Map, Stamp, Speed, Actual, Year, Month;
        public bool Paused;
        public Army[] Armies;
        public Battle[] Battles;
    }

    private static Thread _worker;
    private static volatile bool _run;
    private static volatile MapData _pendingMap;
    private static volatile ArmyData _pendingArmy;
    private static volatile string _problem;
    // 主线程交给后台线程的：端口、管理员码或票
    private static volatile int _port;
    private static volatile string _code, _ticket;
    private static string _token;
    private static int _mapStamp = -1, _armyStamp = -1;
    // 发给核心的指令(速度、暂停)：主线程排进来，后台线程登入后发出去
    private static readonly ConcurrentQueue<(string id, string choice)> Orders = new();
    // 这一回核心接手后有没有把游戏原来的速度档告诉核心
    private static bool _speedSynced;

    // ───────── 主线程的画面 ─────────
    private static GameObject _root;
    private static Canvas _canvas;
    private static RectTransform _rootRect, _mapRect, _layer, _armyLayer;
    // 两张底图轮着用：新的一张淡入盖在旧的上面，淡完旧的收起(见 Fade)
    private static readonly RawImage[] MapImages = new RawImage[2];
    private static readonly Texture2D[] Textures = new Texture2D[2];
    private static int _shown = -1, _fadingTo = -1;
    private static float _fadeAt;
    private const float FadeSeconds = 0.6f;
    // 原版像素的地形(一格一个颜色，贴图的第 0 行在下)：主线程从冻住的世界取一回，后台线程拿来画底图
    private static volatile Color32[] _terrain;
    private static volatile int _terrainW, _terrainH;
    // 国色叠在地形上的浓淡(原版王国图层的样子：国色为主、地形透出来)
    private const float TintAlpha = 0.62f;
    private static Text _hover, _notice;
    private static Font _font;
    private static MapData _map;
    private static ArmyData _armies;
    private static float _cx, _cy, _zoom, _fit;
    // 上回排字时的视野：只是拖动(没缩放)时整层平移，不必每帧重排(见 Shift)
    private static float _layoutCx, _layoutCy, _layoutZoom, _layoutAt;
    private const float DragRelayoutSeconds = 0.2f;
    private static bool _fitted, _dirty;
    private static Vector3 _lastMouse;
    private static bool _dragging;
    private static readonly List<Text> Labels = new();
    private static readonly List<Image> Marks = new();
    private static readonly List<Text> MarkTexts = new();
    private static int _labelsUsed, _marksUsed, _markTextsUsed;
    private static readonly Color32 Sea = new(22, 38, 56, 255), Wild = new(74, 70, 58, 255), Front = new(214, 52, 38, 255);

    // 黑屏牌子每帧调(见 LightweightHostMode)：parent 是牌子的画布，map 为真时铺地图
    internal static void Tick(Transform parent, Canvas canvas, bool show)
    {
        if (!show)
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            StopWorker();
            return;
        }
        try
        {
            if (_root == null) Build(parent, canvas);
            if (!_root.activeSelf) _root.SetActive(true);
            _port = TerminalHost.Port;
            _code = CoreHost.AdminCodeNow;
            if (string.IsNullOrEmpty(_code) && string.IsNullOrEmpty(_ticket) && _token == null) _ticket = CoreHost.TakeLiveTicket();
            if (_terrain == null) CaptureTerrain();
            StartWorker();
            SyncSpeed();
            Apply();
            Fade();
            Filter();
            RefreshSpeedBar();
            HandleInput();
            if (_dirty || _dragging && Time.unscaledTime - _layoutAt >= DragRelayoutSeconds || _zoom != _layoutZoom) Layout();
            else Shift();
            PlaceArmies();
            _notice.text = _map == null ? _problem ?? LM.Get("terminal_live_waiting") : "";
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][实时地图] {exception.Message}");
            _problem = exception.Message;
        }
    }

    internal static void Reset()
    {
        StopWorker();
        _token = null;
        _ticket = null;
        _mapStamp = _armyStamp = -1;
        _speedSynced = false;
        while (Orders.TryDequeue(out _)) { }
        _map = null;
        _armies = null;
        _pendingMap = null;
        _pendingArmy = null;
        _fitted = false;
        _terrain = null;
        _lastTrees = null;
        _paintWant = null;
        _painted = null;
        _openKey = null;
        _openTree = null;
        Movers.Clear();
        _lastArmyAt = -1f;
        _shown = _fadingTo = -1;
        if (_root != null) _root.SetActive(false);
    }

    // ───────── 后台线程 ─────────
    private static void StartWorker()
    {
        if (_worker != null && _worker.IsAlive) return;
        _run = true;
        _worker = new Thread(Work) { IsBackground = true, Name = "EmpireCraftLiveMap" };
        _worker.Start();
    }

    private static void StopWorker()
    {
        _run = false;
        _worker = null;
    }

    private static void Work()
    {
        int nextFetch = Environment.TickCount;
        while (_run)
        {
            try
            {
                PaintPending();
            }
            catch (Exception exception)
            {
                _problem = exception.Message;
            }
            if (unchecked(Environment.TickCount - nextFetch) < 0)
            {
                Thread.Sleep(30);
                continue;
            }
            nextFetch = Environment.TickCount + 1000;
            try
            {
                if (_token == null) _token = Pair();
                if (_token != null)
                {
                    while (Orders.TryDequeue(out var order))
                    {
                        string body = new JObject { ["id"] = order.id, ["choice"] = order.choice }.ToString(Newtonsoft.Json.Formatting.None);
                        Request("POST", "/api/admin", body, _token, out int sent);
                        if (sent == 401 || sent == 403)
                        {
                            _token = null;
                            break;
                        }
                    }
                    string map = Fetch("/api/admin/live/map");
                    if (map != null) TakeMap(map);
                    string army = Fetch("/api/admin/live/army");
                    if (army != null) TakeArmies(army);
                }
            }
            catch (Exception exception)
            {
                _problem = exception.Message;
            }
        }
    }

    private static string Pair()
    {
        string code = _code, ticket = _ticket;
        if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(ticket)) return null;
        var body = new JObject();
        if (!string.IsNullOrEmpty(code)) body["code"] = code;
        else body["ticket"] = ticket;
        _ticket = null;
        string reply = Request("POST", "/api/admin/pair", body.ToString(Newtonsoft.Json.Formatting.None), null, out int status);
        if (status != 200 || reply == null) return null;
        return JObject.Parse(reply).Value<string>("token");
    }

    private static string Fetch(string path)
    {
        string reply = Request("GET", path, null, _token, out int status);
        if (status == 401 || status == 403)
        {
            _token = null;
            return null;
        }
        if (status == 404)
        {
            _problem = LM.Get("terminal_live_old_core");
            return null;
        }
        return status == 200 ? reply : null;
    }

    private static string Request(string method, string path, string body, string token, out int status)
    {
        status = 0;
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port + path);
        request.Method = method;
        request.Timeout = request.ReadWriteTimeout = 5000;
        request.Proxy = null;
        if (token != null) request.Headers["X-Terminal-Token"] = token;
        if (body != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length;
            using Stream stream = request.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);
        }
        try
        {
            using var response = (HttpWebResponse)request.GetResponse();
            status = (int)response.StatusCode;
            using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (WebException exception) when (exception.Response is HttpWebResponse response)
        {
            status = (int)response.StatusCode;
            response.Close();
            return null;
        }
    }

    private static void TakeMap(string text)
    {
        JObject json = JObject.Parse(text);
        if (json.Value<bool?>("ready") != true) return;
        int stamp = json.Value<int>("stamp");
        if (stamp == _mapStamp) return;
        int w = json.Value<int>("w"), h = json.Value<int>("h");
        byte[] low = Unpack(json.Value<string>("lo"), w * h), high = Unpack(json.Value<string>("hi"), w * h);
        var cells = new ushort[w * h];
        for (int i = 0; i < cells.Length; i++) cells[i] = (ushort)(low[i] | high[i] << 8);
        var realmRows = (JArray)json["realms"];
        var realms = new Realm[realmRows.Count];
        for (int i = 0; i < realms.Length; i++)
        {
            var row = (JObject)realmRows[i];
            realms[i] = new Realm
            {
                Name = row.Value<string>("name") ?? "", Color = ParseColor(row.Value<string>("color")), N = row.Value<int?>("n") ?? 0,
                X = row.Value<float?>("x") ?? -1f, Y = row.Value<float?>("y") ?? -1f
            };
        }
        var townRows = (JArray)json["cities"];
        var towns = new Town[townRows.Count];
        for (int i = 0; i < towns.Length; i++)
        {
            var row = (JObject)townRows[i];
            towns[i] = new Town
            {
                Name = row.Value<string>("name") ?? "", X = row.Value<float>("x"), Y = row.Value<float>("y"), R = row.Value<int?>("r") ?? -1,
                Z = row.Value<int?>("z") ?? 0, Cap = row.Value<bool?>("cap") == true, Dk = row.Value<int?>("dk") ?? -1,
                De = row.Value<int?>("de") ?? -1, Dt = row.Value<int?>("dt") ?? -1
            };
        }
        var realmEmp = new int[realms.Length];
        for (int i = 0; i < realms.Length; i++) realmEmp[i] = ((JObject)realmRows[i]).Value<int?>("emp") ?? -1;
        var fronts = new HashSet<int>();
        if (json["fronts"] is JArray pairs)
            foreach (JToken pair in pairs)
                fronts.Add(pair.Value<int>(0) << 16 | pair.Value<int>(1));
        // 每块地属哪座城(老核心没报：都算没有，法理层就空着)
        var zoneCity = new ushort[w * h];
        if (json["zl"] != null)
        {
            byte[] zl = Unpack(json.Value<string>("zl"), w * h), zh = Unpack(json.Value<string>("zh"), w * h);
            for (int i = 0; i < zoneCity.Length; i++) zoneCity[i] = (ushort)(zl[i] | zh[i] << 8);
        }
        else
        {
            for (int i = 0; i < zoneCity.Length; i++) zoneCity[i] = 0xFFFF;
        }
        void Table(JToken rows, out string[] names, out Color32[] colors)
        {
            var list = rows as JArray ?? new JArray();
            names = new string[list.Count];
            colors = new Color32[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                names[i] = list[i].Value<string>("name") ?? "";
                colors[i] = ParseColor(list[i].Value<string>("color"));
            }
        }
        var data = new MapData
        {
            W = w, H = h, Stamp = stamp, Cells = cells, ZoneCity = zoneCity, Realms = realms, Towns = towns, Fronts = fronts,
            RealmEmp = realmEmp, Tile = json.Value<int?>("tile") ?? 8
        };
        Table(json["empires"], out data.EmpName, out data.EmpColor);
        JToken dejure = json["dejure"];
        Table(dejure?["k"], out data.DkName, out data.DkColor);
        Table(dejure?["e"], out data.DeName, out data.DeColor);
        Table(dejure?["t"], out data.DtName, out data.DtColor);
        data.Trees = new[] { BuildPolTree(data), BuildDejureTree(data) };
        for (int i = 0; i < 2; i++) PlaceLabels(data.Trees[i], _lastTrees?[i], data);
        _lastTrees = data.Trees;
        _mapStamp = stamp;
        _pendingMap = data;
    }

    // 上一份图的两棵树(后台线程用)：铭牌没变的接着用
    private static Tree[] _lastTrees;

    private static void TakeArmies(string text)
    {
        JObject json = JObject.Parse(text);
        if (json.Value<bool?>("ready") != true) return;
        int stamp = json.Value<int>("stamp");
        if (stamp == _armyStamp) return;
        var armyRows = (JArray)json["armies"];
        var armies = new Army[armyRows.Count];
        for (int i = 0; i < armies.Length; i++)
        {
            var row = (JObject)armyRows[i];
            armies[i] = new Army
            {
                Id = row.Value<string>("id") ?? i.ToString(), Arm = row.Value<string>("arm") ?? "", T = row.Value<string>("t") ?? "",
                R = row.Value<int?>("r") ?? -1, N = row.Value<int?>("n") ?? 0, X = row.Value<float>("x"), Y = row.Value<float>("y")
            };
        }
        var battleRows = (JArray)json["battles"];
        var battles = new Battle[battleRows.Count];
        for (int i = 0; i < battles.Length; i++)
        {
            var row = (JObject)battleRows[i];
            battles[i] = new Battle
            {
                X = row.Value<float>("x"), Y = row.Value<float>("y"), C = row.Value<string>("c") ?? "", P = row.Value<int?>("p") ?? 0,
                A = row.Value<int?>("a") ?? -1, D = row.Value<int?>("d") ?? -1, H = row.Value<int?>("h") ?? 0, Z = row.Value<int?>("z") ?? 0,
                L = row.Value<bool?>("l") == true
            };
        }
        _armyStamp = stamp;
        _pendingArmy = new ArmyData
        {
            Map = json.Value<int?>("map") ?? 0, Stamp = stamp, Armies = armies, Battles = battles,
            Speed = json.Value<int?>("speed") ?? 0, Actual = json.Value<int?>("actual") ?? -1, Paused = json.Value<bool?>("paused") == true,
            Year = json.Value<int?>("year") ?? 0, Month = json.Value<int?>("month") ?? 0
        };
    }

    // 两两一组：值、连着几个(同 TerminalIntel.Rle)
    private static byte[] Unpack(string base64, int length)
    {
        var cells = new byte[length];
        if (string.IsNullOrEmpty(base64)) return cells;
        byte[] packed = Convert.FromBase64String(base64);
        int at = 0;
        for (int i = 0; i + 1 < packed.Length && at < length; i += 2)
            for (int run = 0; run < packed[i + 1] && at < length; run++)
                cells[at++] = packed[i];
        return cells;
    }

    private static Color32 ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7) return new Color32(150, 150, 150, 255);
        try
        {
            return new Color32(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), 255);
        }
        catch (Exception)
        {
            return new Color32(150, 150, 150, 255);
        }
    }

    // 底图的像素在 CoreLiveMap.Tree 的 PaintPending 里涂(按眼下展开的样子)

    private static Color32 Darken(Color32 color) => new((byte)(color.r * 0.5f), (byte)(color.g * 0.5f), (byte)(color.b * 0.5f), 255);

    // 主线程：从冻住的这一局取原版的地形色(一格一个，贴图的第 0 行在下，同原版的格子坐标)
    private static void CaptureTerrain()
    {
        MapBox world = World.world;
        if (world == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        int tw = MapBox.width, th = MapBox.height;
        if (tw <= 0 || th <= 0) return;
        var colors = new Color32[tw * th];
        for (int y = 0; y < th; y++)
        for (int x = 0; x < tw; x++)
        {
            WorldTile tile = world.GetTile(x, y);
            colors[y * tw + x] = tile?.Type != null ? tile.Type.color : Sea;
        }
        _terrainW = tw;
        _terrainH = th;
        _terrain = colors;
        // 先前按色块画的底图：换成原版像素的
        _openKey = null;
        _dirty = true;
    }

    // ───────── 主线程 ─────────
    private static void Build(Transform parent, Canvas canvas)
    {
        _canvas = canvas;
        _font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        _root = new GameObject("EmpireCraftLiveMap", typeof(RectTransform), typeof(Image));
        _root.transform.SetParent(parent, false);
        // 挂在黑底上面、牌子下面
        _root.transform.SetSiblingIndex(1);
        _rootRect = (RectTransform)_root.transform;
        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.one;
        _rootRect.offsetMin = _rootRect.offsetMax = Vector2.zero;
        Image sea = _root.GetComponent<Image>();
        sea.color = Sea;
        sea.raycastTarget = false;
        var mapObject = new GameObject("Map", typeof(RectTransform));
        mapObject.transform.SetParent(_root.transform, false);
        _mapRect = (RectTransform)mapObject.transform;
        _mapRect.anchorMin = _mapRect.anchorMax = _mapRect.pivot = Vector2.zero;
        for (int i = 0; i < MapImages.Length; i++)
        {
            var face = new GameObject("Face" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            face.transform.SetParent(_mapRect, false);
            var faceRect = (RectTransform)face.transform;
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = faceRect.offsetMax = Vector2.zero;
            MapImages[i] = face.GetComponent<RawImage>();
            MapImages[i].raycastTarget = false;
            MapImages[i].enabled = false;
        }
        var layer = new GameObject("Marks", typeof(RectTransform));
        layer.transform.SetParent(_root.transform, false);
        _layer = (RectTransform)layer.transform;
        _layer.anchorMin = _layer.anchorMax = _layer.pivot = Vector2.zero;
        var armyLayer = new GameObject("Armies", typeof(RectTransform));
        armyLayer.transform.SetParent(_root.transform, false);
        _armyLayer = (RectTransform)armyLayer.transform;
        _armyLayer.anchorMin = _armyLayer.anchorMax = _armyLayer.pivot = Vector2.zero;
        _hover = NewText(_root.transform, 16, new Color(0.97f, 0.93f, 0.8f));
        var hoverRect = (RectTransform)_hover.transform;
        hoverRect.anchorMin = hoverRect.anchorMax = new Vector2(0.5f, 1f);
        hoverRect.anchoredPosition = new Vector2(0f, -18f);
        _notice = NewText(_root.transform, 15, new Color(0.8f, 0.82f, 0.85f));
        ((RectTransform)_notice.transform).anchorMin = ((RectTransform)_notice.transform).anchorMax = new Vector2(0.5f, 0.5f);
        BuildSpeedBar();
        _dirty = true;
    }

    // ───────── 速度栏 ─────────
    private static RectTransform _bar;
    private static Text _speedText, _actualText, _pauseText;

    private static void BuildSpeedBar()
    {
        var bar = new GameObject("SpeedBar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(_root.transform, false);
        _bar = (RectTransform)bar.transform;
        _bar.anchorMin = _bar.anchorMax = _bar.pivot = new Vector2(1f, 1f);
        _bar.anchoredPosition = new Vector2(-8f, -8f);
        _bar.sizeDelta = new Vector2(250f, 50f);
        Image back = bar.GetComponent<Image>();
        back.color = new Color(0.05f, 0.06f, 0.07f, 0.82f);
        back.raycastTarget = true;
        Text BarButton(string name, float x, float width, string label, UnityEngine.Events.UnityAction click, float y = -6f, float height = 24f)
        {
            var holder = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            holder.transform.SetParent(_bar, false);
            var rect = (RectTransform)holder.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            holder.GetComponent<Image>().color = new Color(0.24f, 0.42f, 0.5f);
            holder.GetComponent<Button>().onClick.AddListener(click);
            Text text = NewText(holder.transform, 14, Color.white);
            ((RectTransform)text.transform).anchoredPosition = Vector2.zero;
            text.text = label;
            return text;
        }
        BarButton("Slower", 6f, 28f, "－", () => StepSpeed(-1));
        _speedText = NewText(_bar, 16, new Color(0.98f, 0.85f, 0.45f));
        var speedRect = (RectTransform)_speedText.transform;
        speedRect.anchorMin = speedRect.anchorMax = speedRect.pivot = new Vector2(0f, 1f);
        speedRect.anchoredPosition = new Vector2(38f, -6f);
        speedRect.sizeDelta = new Vector2(70f, 24f);
        BarButton("Faster", 112f, 28f, "＋", () => StepSpeed(1));
        _pauseText = BarButton("Pause", 146f, 98f, "", TogglePause);
        _actualText = NewText(_bar, 11, new Color(0.75f, 0.78f, 0.8f));
        var actualRect = (RectTransform)_actualText.transform;
        actualRect.anchorMin = actualRect.anchorMax = actualRect.pivot = new Vector2(0f, 1f);
        actualRect.anchoredPosition = new Vector2(6f, -32f);
        actualRect.sizeDelta = new Vector2(176f, 16f);
        _actualText.alignment = TextAnchor.MiddleLeft;
        _layerText = BarButton("Layer", 184f, 60f, LM.Get(MapLayer == LayerDejure ? "terminal_live_layer_dj" : "terminal_live_layer_pol"),
            ToggleLayer, -30f, 16f);
        _layerText.fontSize = 11;
    }

    // 原版的速度档，从慢到快(每档实际倍数 = 倍率 × 每帧几步)
    private static List<WorldTimeScaleAsset> Scales()
    {
        var list = new List<WorldTimeScaleAsset>();
        if (AssetManager.time_scales?.list != null)
            foreach (WorldTimeScaleAsset asset in AssetManager.time_scales.list)
                if (asset != null) list.Add(asset);
        list.Sort((a, b) => Times(a).CompareTo(Times(b)));
        return list;
    }

    private static float Times(WorldTimeScaleAsset asset) => asset.multiplier * Mathf.Max(1, asset.ticks);

    private static int PercentOf(WorldTimeScaleAsset asset) => Mathf.RoundToInt(Times(asset) * 100f);

    private static string TimesText(float times) => "×" + (times >= 10f || Mathf.Approximately(times, Mathf.Round(times)) ? times.ToString("0") : times.ToString("0.#"));

    // 核心眼下的流速最接近原版哪一档
    private static WorldTimeScaleAsset NearestScale(int percent)
    {
        WorldTimeScaleAsset best = null;
        float bestGap = float.MaxValue;
        foreach (WorldTimeScaleAsset asset in Scales())
        {
            float gap = Mathf.Abs(PercentOf(asset) - percent);
            if (gap < bestGap)
            {
                bestGap = gap;
                best = asset;
            }
        }
        return best;
    }

    private static void SetSpeed(WorldTimeScaleAsset asset)
    {
        if (asset == null) return;
        Orders.Enqueue(("speed", PercentOf(asset).ToString()));
        // 游戏自己的档位跟着改：带回以后照这一档走
        try
        {
            Config.setWorldSpeed(asset);
        }
        catch (Exception)
        {
            // 原版的速度界面眼下关着，改不了也不要紧
        }
        _shownSpeed = PercentOf(asset);
        _shownUntil = Time.unscaledTime + 3f;
        _speedText.text = TimesText(Times(asset));
    }

    private static int _shownSpeed;
    private static float _shownUntil;

    private static void StepSpeed(int step)
    {
        List<WorldTimeScaleAsset> scales = Scales();
        if (scales.Count == 0) return;
        int current = Time.unscaledTime < _shownUntil ? _shownSpeed : _armies?.Speed ?? PercentOf(Config.time_scale_asset ?? scales[0]);
        int at = scales.IndexOf(NearestScale(current));
        SetSpeed(scales[Mathf.Clamp(at + step, 0, scales.Count - 1)]);
    }

    private static void TogglePause()
    {
        bool paused = _armies?.Paused == true;
        Orders.Enqueue(("pause", paused ? "off" : "on"));
        _pauseText.text = LM.Get(paused ? "terminal_live_pause" : "terminal_live_resume");
    }

    // 核心接手后头一回：核心改用游戏原来选的速度档
    private static void SyncSpeed()
    {
        if (_speedSynced || Config.time_scale_asset == null) return;
        _speedSynced = true;
        Orders.Enqueue(("speed", PercentOf(Config.time_scale_asset).ToString()));
        _shownSpeed = PercentOf(Config.time_scale_asset);
        _shownUntil = Time.unscaledTime + 3f;
    }

    private static void RefreshSpeedBar()
    {
        if (_bar == null) return;
        bool waiting = Time.unscaledTime < _shownUntil;
        int speed = waiting ? _shownSpeed : _armies?.Speed ?? 0;
        _speedText.text = speed > 0 ? TimesText(speed / 100f) : "";
        if (_armies == null)
        {
            _actualText.text = "";
            _pauseText.text = LM.Get("terminal_live_pause");
            return;
        }
        _pauseText.text = LM.Get(_armies.Paused ? "terminal_live_resume" : "terminal_live_pause");
        string date = string.Format(LM.Get("terminal_live_date"), _armies.Year, _armies.Month);
        _actualText.text = _armies.Paused ? date + " · " + LM.Get("terminal_live_paused")
            : _armies.Actual > 0 ? date + " · " + string.Format(LM.Get("terminal_live_actual"), TimesText(_armies.Actual / 100f)) : date;
    }

    private static bool BarContains(Vector3 screen) => _bar != null && RectTransformUtility.RectangleContainsScreenPoint(_bar, screen, null);

    private static Text NewText(Transform parent, int size, Color color)
    {
        var holder = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        holder.transform.SetParent(parent, false);
        var rect = (RectTransform)holder.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(600f, 40f);
        var text = holder.GetComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        var outline = holder.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    // 后台线程备好的换上来
    private static void Apply()
    {
        MapData map = _pendingMap;
        if (map != null)
        {
            _pendingMap = null;
            bool resized = _map == null || _map.W != map.W || _map.H != map.H;
            _map = map;
            _openKey = null;
            if (resized) _fitted = false;
            _dirty = true;
        }
        Painted painted = _painted;
        if (painted != null && ReferenceEquals(painted.Map, _map))
        {
            _painted = null;
            // 上一张还在淡入：先让它到位
            if (_fadingTo >= 0) FinishFade();
            int next = _shown < 0 ? 0 : 1 - _shown;
            int tw = painted.Width, th = painted.Height;
            Texture2D texture = Textures[next];
            if (texture == null || texture.width != tw || texture.height != th)
            {
                if (texture != null) Object.Destroy(texture);
                // 带多级纹理：整张缩小着看时一格不到一个屏幕像素，硬缩会一拖就闪(见 Filter)
                Textures[next] = texture = new Texture2D(tw, th, TextureFormat.RGBA32, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            texture.SetPixels32(painted.Pixels);
            texture.Apply(true);
            _filterFor = -1f;
            RawImage face = MapImages[next];
            face.texture = texture;
            face.enabled = true;
            face.transform.SetAsLastSibling();
            bool jump = _shownTexW != tw || _shownTexH != th;
            _shownTexW = tw;
            _shownTexH = th;
            _texScale = tw / Mathf.Max(1, painted.Map.W);
            // 头一张、换了大小、从色块换成像素的：直接换上；平常淡入
            if (_shown < 0 || jump)
            {
                face.color = Color.white;
                if (_shown >= 0) MapImages[_shown].enabled = false;
                _shown = next;
            }
            else
            {
                face.color = new Color(1f, 1f, 1f, 0f);
                _fadingTo = next;
                _fadeAt = Time.unscaledTime;
            }
        }
        else if (painted != null)
        {
            // 涂的是已经换掉的那份图：不要了(新图会另涂)
            _painted = null;
        }
        ArmyData armies = _pendingArmy;
        if (armies != null)
        {
            _pendingArmy = null;
            _armies = armies;
            TakeMovers(armies);
            _dirty = true;
        }
    }

    // 一格占几个屏幕像素：不到一个半用多级纹理平滑缩小(不闪)，再大就是原版那样的硬像素
    private static float _filterFor = -1f;
    private static int _shownTexW, _shownTexH, _texScale = 1;

    private static void Filter()
    {
        if (_map == null) return;
        bool sharp = _zoom / Mathf.Max(1, _texScale) * (_canvas != null ? _canvas.scaleFactor : 1f) >= 1.5f;
        float key = sharp ? 1f : 0f;
        if (key == _filterFor) return;
        _filterFor = key;
        foreach (Texture2D texture in Textures)
            if (texture != null) texture.filterMode = sharp ? FilterMode.Point : FilterMode.Trilinear;
    }

    private static void Fade()
    {
        if (_fadingTo < 0) return;
        float t = (Time.unscaledTime - _fadeAt) / FadeSeconds;
        if (t >= 1f) FinishFade();
        else MapImages[_fadingTo].color = new Color(1f, 1f, 1f, t);
    }

    private static void FinishFade()
    {
        MapImages[_fadingTo].color = Color.white;
        if (_shown >= 0 && _shown != _fadingTo) MapImages[_shown].enabled = false;
        _shown = _fadingTo;
        _fadingTo = -1;
    }

    // ───────── 军队：两份军情之间插值 ─────────
    private sealed class Mover
    {
        public float FromX, FromY, ToX, ToY, Start, End;
        public int R, N;
        public string Arm, Task;
    }

    private static readonly Dictionary<string, Mover> Movers = new();
    private static float _lastArmyAt = -1f, _armyGap = 1f;
    // 跳得太远(换了主人、渡海、整编)的不插值，直接到位
    private const float SnapCells = 6f;

    private static void TakeMovers(ArmyData armies)
    {
        float now = Time.unscaledTime;
        if (_lastArmyAt > 0f) _armyGap = Mathf.Clamp(now - _lastArmyAt, 0.3f, 5f);
        _lastArmyAt = now;
        var seen = new HashSet<string>();
        foreach (Army army in armies.Armies)
        {
            seen.Add(army.Id);
            if (Movers.TryGetValue(army.Id, out Mover mover))
            {
                Vector2 at = Where(mover, now);
                bool jump = Mathf.Abs(at.x - army.X) > SnapCells || Mathf.Abs(at.y - army.Y) > SnapCells;
                mover.FromX = jump ? army.X : at.x;
                mover.FromY = jump ? army.Y : at.y;
            }
            else
            {
                Movers[army.Id] = mover = new Mover { FromX = army.X, FromY = army.Y };
            }
            mover.ToX = army.X;
            mover.ToY = army.Y;
            mover.Start = now;
            mover.End = now + _armyGap * 1.05f;
            mover.R = army.R;
            mover.N = army.N;
            mover.Arm = army.Arm;
            mover.Task = army.T;
        }
        var gone = new List<string>();
        foreach (string id in Movers.Keys)
            if (!seen.Contains(id)) gone.Add(id);
        foreach (string id in gone) Movers.Remove(id);
    }

    private static Vector2 Where(Mover mover, float now)
    {
        float t = mover.End <= mover.Start ? 1f : Mathf.Clamp01((now - mover.Start) / (mover.End - mover.Start));
        return new Vector2(mover.FromX + (mover.ToX - mover.FromX) * t, mover.FromY + (mover.ToY - mover.FromY) * t);
    }

    private static Vector2 ScreenSize => _rootRect.rect.size;

    private static void Fit()
    {
        Vector2 size = ScreenSize;
        _fit = Mathf.Min(size.x / _map.W, size.y / _map.H);
        _zoom = _fit;
        _cx = _map.W / 2f;
        _cy = _map.H / 2f;
        _fitted = true;
    }

    // 画布单位里的鼠标位置(画布有缩放)
    private static Vector2 Mouse()
    {
        float scale = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
        return Input.mousePosition / scale;
    }

    private static Vector2 CellAt(Vector2 point)
    {
        Vector2 size = ScreenSize;
        return new Vector2(_cx + (point.x - size.x / 2f) / _zoom, _cy - (point.y - size.y / 2f) / _zoom);
    }

    private static Vector2 ScreenOf(float x, float y)
    {
        Vector2 size = ScreenSize;
        return new Vector2(size.x / 2f + (x - _cx) * _zoom, size.y / 2f - (y - _cy) * _zoom);
    }

    private static void HandleInput()
    {
        if (_map == null) return;
        if (!_fitted) Fit();
        Vector2 mouse = Mouse();
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f && !CoreHostCardUnder())
        {
            Vector2 before = CellAt(mouse);
            _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.18f, wheel), _fit * 0.7f, Mathf.Max(_fit * 2f, _map.Tile * 16f));
            Vector2 after = CellAt(mouse);
            _cx += before.x - after.x;
            _cy += before.y - after.y;
            _dirty = true;
        }
        bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1);
        if (held && !_dragging && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !CoreHostCardUnder())
        {
            _dragging = true;
            _lastMouse = mouse;
        }
        if (_dragging)
        {
            if (!held)
            {
                _dragging = false;
                _dirty = true;
            }
            else
            {
                Vector2 delta = mouse - (Vector2)_lastMouse;
                _cx -= delta.x / _zoom;
                _cy += delta.y / _zoom;
                _lastMouse = mouse;
            }
        }
        _cx = Mathf.Clamp(_cx, 0f, _map.W);
        _cy = Mathf.Clamp(_cy, 0f, _map.H);
        // 鼠标底下是哪一国
        Vector2 cell = CellAt(mouse);
        int x = Mathf.FloorToInt(cell.x), y = Mathf.FloorToInt(cell.y);
        _hover.text = HoverName(x, y);
    }

    // 鼠标在左上角的牌子上：滚轮、拖动留给牌子(见 LightweightHostMode.CardContains)
    private static bool CoreHostCardUnder() => LightweightHostMode.CardContains(Input.mousePosition) || BarContains(Input.mousePosition);

    private static void Shift()
    {
        if (_map == null) return;
        _mapRect.anchoredPosition = ScreenOf(0f, _map.H);
        _layer.anchoredPosition = new Vector2((_layoutCx - _cx) * _zoom, (_cy - _layoutCy) * _zoom);
    }

    private static void Layout()
    {
        _dirty = false;
        _layoutCx = _cx;
        _layoutCy = _cy;
        _layoutZoom = _zoom;
        _layoutAt = Time.unscaledTime;
        _layer.anchoredPosition = Vector2.zero;
        _labelsUsed = _marksUsed = _markTextsUsed = 0;
        if (_map != null)
        {
            Vector2 origin = ScreenOf(0f, _map.H);
            _mapRect.anchoredPosition = origin;
            _mapRect.sizeDelta = new Vector2(_map.W * _zoom, _map.H * _zoom);
            Vector2 size = ScreenSize;
            if (OpenTree()) RequestPaint();
            var taken = new List<Rect>();
            PlaceBattles(size, taken);
            PlaceNameplates(size, taken);
            PlaceTowns(size, taken);
        }
        for (int i = _labelsUsed; i < Labels.Count; i++) Labels[i].gameObject.SetActive(false);
        for (int i = _marksUsed; i < Marks.Count; i++) Marks[i].gameObject.SetActive(false);
        for (int i = _markTextsUsed; i < MarkTexts.Count; i++) MarkTexts[i].gameObject.SetActive(false);
    }

    private static bool OnScreen(Vector2 at, Vector2 size, float margin) =>
        at.x > -margin && at.y > -margin && at.x < size.x + margin && at.y < size.y + margin;

    private static bool Free(List<Rect> taken, Rect rect)
    {
        foreach (Rect other in taken)
            if (other.Overlaps(rect)) return false;
        return true;
    }

    // 城(同手机舆图)：拉近到城的那几片里才出，都城用原版皇冠、别的城用原版城市图标，写城名
    private static void PlaceTowns(Vector2 size, List<Rect> taken)
    {
        Tree tree = CurrentTree;
        if (tree == null || _cityOpen == null || _cityOpen.Length != tree.Nodes.Count) return;
        Sprite crown = Icon("ui/Icons/iconKings"), city = Icon("ui/icons/iconCity");
        for (int t = 0; t < _map.Towns.Length; t++)
        {
            Town town = _map.Towns[t];
            int leaf = t < tree.TownLeaf.Length ? tree.TownLeaf[t] : -1;
            if (leaf < 0 || _cityOpen[leaf] == 0) continue;
            Vector2 at = ScreenOf(town.X, town.Y);
            if (!OnScreen(at, size, 10f)) continue;
            float half = town.Cap ? 7f : 5f;
            Sprite sprite = town.Cap ? crown : city;
            Image dot = Mark(sprite);
            dot.color = sprite != null ? Color.white : town.Cap ? new Color(0.98f, 0.82f, 0.3f) : new Color(0.93f, 0.9f, 0.82f);
            var rect = (RectTransform)dot.transform;
            rect.sizeDelta = new Vector2(half * 2f, half * 2f);
            rect.anchoredPosition = at;
            int font = town.Cap ? 13 : 11;
            float width = font * Mathf.Max(2, town.Name.Length) * 0.95f;
            var box = new Rect(at.x - width / 2f, at.y - half - font * 1.4f, width, font * 1.3f);
            if (!Free(taken, box)) continue;
            taken.Add(box);
            Text label = Label();
            label.fontSize = font;
            label.color = town.Cap ? new Color(1f, 0.9f, 0.6f) : new Color(0.9f, 0.88f, 0.8f);
            label.text = town.Name;
            ((RectTransform)label.transform).anchoredPosition = new Vector2(at.x, at.y - half - font * 0.8f);
        }
    }

    // 军队(每帧)：同一国在屏幕上挨得近的并成一个牌子，写总人数；牌子用游戏里兵牌那一套框(按任务分色)加人数最多那一支的兵种图标，
    // 左边一道本国国色。位置在两份军情之间插值(见 TakeMovers)
    private static readonly List<Image> ArmyFrames = new(), ArmyIcons = new(), ArmyBars = new();
    private static readonly List<Text> ArmyTexts = new();

    private sealed class Group
    {
        public float X, Y;
        public int N, Count, R, Lead;
        public string Arm, Task;
    }

    private static void PlaceArmies()
    {
        int used = 0;
        if (_map != null && Movers.Count > 0)
        {
            Vector2 size = ScreenSize;
            float now = Time.unscaledTime, grid = _zoom >= 20f ? 26f : 38f;
            var groups = new Dictionary<(int, int, int), Group>();
            foreach (Mover mover in Movers.Values)
            {
                Vector2 cell = Where(mover, now);
                Vector2 at = ScreenOf(cell.x, cell.y);
                if (!OnScreen(at, size, 24f)) continue;
                var key = (mover.R, Mathf.FloorToInt(at.x / grid), Mathf.FloorToInt(at.y / grid));
                if (!groups.TryGetValue(key, out Group group)) groups[key] = group = new Group { R = mover.R };
                group.X += at.x;
                group.Y += at.y;
                group.Count++;
                group.N += mover.N;
                if (mover.N >= group.Lead)
                {
                    group.Lead = mover.N;
                    group.Arm = mover.Arm;
                    group.Task = mover.Task;
                }
            }
            foreach (Group group in groups.Values)
            {
                var at = new Vector2(group.X / group.Count, group.Y / group.Count);
                Color realm = group.R >= 0 && group.R < _map.Realms.Length ? (Color)_map.Realms[group.R].Color : new Color(0.7f, 0.68f, 0.6f);
                Image frame = Pooled(ArmyFrames, used);
                Sprite box = FrameSprite(group.Task switch
                {
                    "attack" => "CorpsFrame_lacquer",
                    "defend" => "CorpsFrame_azure",
                    "retreat" => "CorpsFrame_ash",
                    "sea" => "CorpsFrame_sea",
                    _ => "CorpsFrame_olive"
                });
                frame.sprite = box;
                frame.type = box != null ? Image.Type.Sliced : Image.Type.Simple;
                frame.color = box != null ? Color.white : new Color(0.16f, 0.17f, 0.15f, 0.95f);
                var frameRect = (RectTransform)frame.transform;
                frameRect.sizeDelta = new Vector2(24f, 20f);
                frameRect.anchoredPosition = at;
                Image bar = Pooled(ArmyBars, used);
                bar.sprite = null;
                bar.color = realm;
                var barRect = (RectTransform)bar.transform;
                barRect.sizeDelta = new Vector2(3f, 14f);
                barRect.anchoredPosition = at + new Vector2(-9f, 0f);
                Image icon = Pooled(ArmyIcons, used);
                Sprite arm = string.IsNullOrEmpty(group.Arm) ? null : Icon("ui/icons/corps/arm_" + group.Arm);
                icon.sprite = arm;
                icon.enabled = arm != null;
                var iconRect = (RectTransform)icon.transform;
                iconRect.sizeDelta = new Vector2(14f, 14f);
                iconRect.anchoredPosition = at + new Vector2(2f, 0f);
                if (_armyTextsFont == null) _armyTextsFont = _font;
                Text count = PooledText(used);
                count.color = Color.Lerp(realm, Color.white, 0.35f);
                count.text = Short(group.N) + (group.Count > 1 ? " ×" + group.Count : "");
                ((RectTransform)count.transform).anchoredPosition = new Vector2(at.x, at.y - 17f);
                used++;
            }
        }
        for (int i = used; i < ArmyFrames.Count; i++)
        {
            ArmyFrames[i].gameObject.SetActive(false);
            ArmyBars[i].gameObject.SetActive(false);
            ArmyIcons[i].gameObject.SetActive(false);
            ArmyTexts[i].gameObject.SetActive(false);
        }
    }

    private static Font _armyTextsFont;

    // 军牌的几样(框、国色条、图标、人数)各一个池子，下标对齐；按建的先后叠放(框在下)
    private static Image Pooled(List<Image> pool, int index)
    {
        while (pool.Count <= index)
        {
            if (pool == ArmyFrames) EnsureArmySlot();
            else break;
        }
        Image image = pool[index];
        if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
        return image;
    }

    private static Text PooledText(int index)
    {
        Text text = ArmyTexts[index];
        if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
        return text;
    }

    private static void EnsureArmySlot()
    {
        Image Made(string name)
        {
            var holder = new GameObject(name, typeof(RectTransform), typeof(Image));
            holder.transform.SetParent(_armyLayer, false);
            var rect = (RectTransform)holder.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = holder.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }
        ArmyFrames.Add(Made("Frame"));
        ArmyBars.Add(Made("Bar"));
        Image icon = Made("Arm");
        icon.preserveAspect = true;
        ArmyIcons.Add(icon);
        ArmyTexts.Add(NewText(_armyLayer, 10, Color.white));
    }

    // 图标(原版、模组现成的像素图)：取一回记下，像素图不插值
    private static readonly Dictionary<string, Sprite> Sprites = new();

    private static Sprite Icon(string path)
    {
        if (Sprites.TryGetValue(path, out Sprite sprite)) return sprite;
        sprite = SpriteTextureLoader.getSprite(path);
        if (sprite != null) sprite.texture.filterMode = FilterMode.Point;
        Sprites[path] = sprite;
        return sprite;
    }

    // 游戏里兵牌、交战标记用的九宫格小框(见 CorpsMapRenderer.Frame，边距 6)
    private static Sprite FrameSprite(string name)
    {
        string key = "frame:" + name;
        if (Sprites.TryGetValue(key, out Sprite sliced)) return sliced;
        Sprite source = SpriteTextureLoader.getSprite("ui/" + name);
        if (source != null)
        {
            source.texture.filterMode = FilterMode.Point;
            sliced = Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, new Vector4(6f, 6f, 6f, 6f));
        }
        Sprites[key] = sliced;
        return sliced;
    }

    // 交战：拉远是原版的战争图标，拉近是游戏里交战标记的框，写战役名与攻方胜算(同手机舆图的交战牌子)
    private static void PlaceBattles(Vector2 size, List<Rect> taken)
    {
        if (_armies == null) return;
        bool far = _zoom < 9f;
        Sprite war = Icon("ui/icons/iconWar"), plateBox = FrameSprite("CorpsBattle");
        foreach (Battle battle in _armies.Battles)
        {
            Vector2 at = ScreenOf(battle.X, battle.Y) + new Vector2(0f, far ? 0f : -18f);
            if (!OnScreen(at, size, 40f)) continue;
            if (far)
            {
                var small = new Rect(at.x - 6f, at.y - 6f, 12f, 12f);
                if (!Free(taken, small)) continue;
                taken.Add(small);
                Image dot = Mark(war);
                dot.color = war != null ? Color.white : Front;
                var dotRect = (RectTransform)dot.transform;
                dotRect.sizeDelta = new Vector2(12f, 12f);
                dotRect.anchoredPosition = at;
                continue;
            }
            string title = string.Format(LM.Get(battle.L ? "terminal_live_landing" : "terminal_live_battle"), battle.C, battle.P, battle.H, battle.Z);
            float width = 11f * Mathf.Max(6, title.Length) * 0.95f + 14f;
            var box = new Rect(at.x - width / 2f, at.y - 10f, width, 20f);
            if (!Free(taken, box)) continue;
            taken.Add(box);
            Image plate = Mark(plateBox, true);
            plate.color = plateBox != null ? Color.white : new Color(0.36f, 0.08f, 0.06f, 0.92f);
            var plateRect = (RectTransform)plate.transform;
            plateRect.sizeDelta = new Vector2(width, 20f);
            plateRect.anchoredPosition = at;
            Text label = MarkText();
            label.fontSize = 11;
            label.color = new Color(1f, 0.88f, 0.6f);
            label.text = title;
            ((RectTransform)label.transform).anchoredPosition = at;
        }
    }

    private static string Short(int n)
    {
        if (n < 10000) return n.ToString();
        return OverallHelperFunc.IsEnglishLanguage() ? (n / 1000f).ToString("0") + "k" : (n / 10000f).ToString("0.#") + "万";
    }

    private static Text Label()
    {
        if (_labelsUsed == Labels.Count) Labels.Add(NewText(_layer, 12, Color.white));
        Text text = Labels[_labelsUsed++];
        text.gameObject.SetActive(true);
        text.transform.SetAsLastSibling();
        return text;
    }

    private static Text MarkText()
    {
        if (_markTextsUsed == MarkTexts.Count) MarkTexts.Add(NewText(_layer, 10, Color.white));
        Text text = MarkTexts[_markTextsUsed++];
        text.gameObject.SetActive(true);
        text.transform.SetAsLastSibling();
        return text;
    }

    private static Image Mark(Sprite sprite = null, bool sliced = false)
    {
        if (_marksUsed == Marks.Count)
        {
            var holder = new GameObject("Mark", typeof(RectTransform), typeof(Image));
            holder.transform.SetParent(_layer, false);
            var rect = (RectTransform)holder.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image made = holder.GetComponent<Image>();
            made.raycastTarget = false;
            Marks.Add(made);
        }
        Image image = Marks[_marksUsed++];
        image.gameObject.SetActive(true);
        image.transform.SetAsLastSibling();
        image.sprite = sprite;
        image.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = sprite != null && !sliced;
        return image;
    }
}
#endif
