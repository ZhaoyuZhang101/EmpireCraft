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
//   · 底图：一块地几个像素，按归属涂国色，国与国之间描深一号的界线，交战两国之间描朱红战线，水与无主地另配色；
//   · 上层：国名(版图大才写，按版图大小定字号，互不压着)、都城与城(拉近了才出)、军队(同一国挨得近的并成一个牌子，写总人数)、
//     交战牌子(拉远是小红方块，拉近写"某城之战 胜算")；鼠标底下是哪一国写在顶上；
//   · 操作：滚轮缩放(以鼠标为准)、按住左键或右键拖动；牌子缩到左上角，「收起地图」回到黑屏；
//   · 速度(用户 2026-11-18 "不要用 3000 这种计量了，还是沿用游戏的")：右上角一条速度栏，按原版的速度档(×0.5～×40，
//     见 AssetManager.time_scales)一档一档加减、暂停与继续；档位折成核心的流速(×N = N×100%，原版音速档每帧两步也算进去)
//     以管理员身份发给核心(POST /api/admin speed/pause)，游戏自己的速度档跟着改，带回后照这一档走。
//     核心接手后头一回连上时，核心改用游戏原来选的那一档；"实际"按核心报的实际流速折成 ×N；
//   · 不改游戏本体的世界：原版摄像机照旧什么都不画，这块地图挂在黑屏牌子的画布底下(黑屏关画布时跳过它，恢复画面时一起收)。
// 只看不改(指令转发是下一步)。核心那头没这两个接口(老核心)就在顶上说一声，照旧黑屏
internal static class CoreLiveMap
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
        public int R, Z;
        public bool Cap;
    }

    private struct Army
    {
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
        public int W, H, Stamp, Scale;
        public ushort[] Cells;
        public Realm[] Realms;
        public Town[] Towns;
        public Color32[] Pixels;
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
    private static RectTransform _rootRect, _mapRect, _layer;
    private static RawImage _mapImage;
    private static Texture2D _texture;
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
            StartWorker();
            SyncSpeed();
            Apply();
            RefreshSpeedBar();
            HandleInput();
            if (_dirty || _dragging && Time.unscaledTime - _layoutAt >= DragRelayoutSeconds || _zoom != _layoutZoom) Layout();
            else Shift();
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
        while (_run)
        {
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
            Thread.Sleep(1000);
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
                Z = row.Value<int?>("z") ?? 0, Cap = row.Value<bool?>("cap") == true
            };
        }
        var fronts = new HashSet<int>();
        if (json["fronts"] is JArray pairs)
            foreach (JToken pair in pairs)
                fronts.Add(pair.Value<int>(0) << 16 | pair.Value<int>(1));
        var data = new MapData
        {
            W = w, H = h, Stamp = stamp, Cells = cells, Realms = realms, Towns = towns,
            Scale = Mathf.Clamp(1024 / Mathf.Max(1, Mathf.Max(w, h)), 1, 6)
        };
        data.Pixels = Paint(data, fronts);
        _mapStamp = stamp;
        _pendingMap = data;
    }

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
            armies[i] = new Army { R = row.Value<int?>("r") ?? -1, N = row.Value<int?>("n") ?? 0, X = row.Value<float>("x"), Y = row.Value<float>("y") };
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

    // 底图的像素(后台线程)：一块地 Scale×Scale 个像素，贴图的第 0 行在下
    private static Color32[] Paint(MapData data, HashSet<int> fronts)
    {
        int s = data.Scale, w = data.W, h = data.H, tw = w * s, th = h * s;
        var pixels = new Color32[tw * th];
        Color32 ColorOf(ushort value) =>
            value == TerminalHost.LiveWater ? Sea : value >= data.Realms.Length ? Wild : data.Realms[value].Color;
        bool Realm(ushort value) => value < data.Realms.Length;
        bool Fronted(ushort a, ushort b) => Realm(a) && Realm(b) && fronts.Contains(Math.Min(a, b) << 16 | Math.Max(a, b));
        for (int cy = 0; cy < h; cy++)
        for (int cx = 0; cx < w; cx++)
        {
            ushort here = data.Cells[cy * w + cx];
            Color32 fill = ColorOf(here);
            ushort left = cx > 0 ? data.Cells[cy * w + cx - 1] : here, right = cx < w - 1 ? data.Cells[cy * w + cx + 1] : here;
            ushort up = cy > 0 ? data.Cells[(cy - 1) * w + cx] : here, down = cy < h - 1 ? data.Cells[(cy + 1) * w + cx] : here;
            for (int py = 0; py < s; py++)
            for (int px = 0; px < s; px++)
            {
                Color32 color = fill;
                ushort other = here;
                if (px == 0 && left != here) other = left;
                else if (px == s - 1 && right != here) other = right;
                else if (py == 0 && up != here) other = up;
                else if (py == s - 1 && down != here) other = down;
                if (other != here && Realm(here))
                    color = Fronted(here, other) ? Front
                        : Realm(other) || other == TerminalHost.LiveWild ? Darken(fill) : fill;
                int x = cx * s + px, y = th - 1 - (cy * s + py);
                pixels[y * tw + x] = color;
            }
        }
        return pixels;
    }

    private static Color32 Darken(Color32 color) => new((byte)(color.r * 0.5f), (byte)(color.g * 0.5f), (byte)(color.b * 0.5f), 255);

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
        var mapObject = new GameObject("Map", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        mapObject.transform.SetParent(_root.transform, false);
        _mapRect = (RectTransform)mapObject.transform;
        _mapRect.anchorMin = _mapRect.anchorMax = _mapRect.pivot = Vector2.zero;
        _mapImage = mapObject.GetComponent<RawImage>();
        _mapImage.raycastTarget = false;
        _mapImage.enabled = false;
        var layer = new GameObject("Marks", typeof(RectTransform));
        layer.transform.SetParent(_root.transform, false);
        _layer = (RectTransform)layer.transform;
        _layer.anchorMin = _layer.anchorMax = _layer.pivot = Vector2.zero;
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
        Text BarButton(string name, float x, float width, string label, UnityEngine.Events.UnityAction click)
        {
            var holder = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            holder.transform.SetParent(_bar, false);
            var rect = (RectTransform)holder.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -6f);
            rect.sizeDelta = new Vector2(width, 24f);
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
        actualRect.sizeDelta = new Vector2(238f, 16f);
        _actualText.alignment = TextAnchor.MiddleLeft;
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
            int tw = map.W * map.Scale, th = map.H * map.Scale;
            if (_texture == null || _texture.width != tw || _texture.height != th)
            {
                if (_texture != null) Object.Destroy(_texture);
                _texture = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            _texture.SetPixels32(map.Pixels);
            _texture.Apply(false);
            map.Pixels = null;
            _mapImage.texture = _texture;
            _mapImage.enabled = true;
            bool resized = _map == null || _map.W != map.W || _map.H != map.H;
            _map = map;
            if (resized) _fitted = false;
            _dirty = true;
        }
        ArmyData armies = _pendingArmy;
        if (armies != null)
        {
            _pendingArmy = null;
            _armies = armies;
            _dirty = true;
        }
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
            _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.18f, wheel), _fit * 0.7f, 48f);
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
        string hover = "";
        if (x >= 0 && y >= 0 && x < _map.W && y < _map.H)
        {
            ushort value = _map.Cells[y * _map.W + x];
            if (value < _map.Realms.Length) hover = _map.Realms[value].Name;
        }
        _hover.text = hover;
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
            var taken = new List<Rect>();
            PlaceBattles(size, taken);
            PlaceArmies(size, taken);
            PlaceRealms(size, taken);
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

    // 国名：版图大的先写，地方不够大、压着别的字的不写
    private static void PlaceRealms(Vector2 size, List<Rect> taken)
    {
        var order = new List<int>();
        for (int i = 0; i < _map.Realms.Length; i++)
            if (_map.Realms[i].N > 0 && _map.Realms[i].X >= 0f) order.Add(i);
        order.Sort((a, b) => _map.Realms[b].N.CompareTo(_map.Realms[a].N));
        foreach (int i in order)
        {
            Realm realm = _map.Realms[i];
            float area = realm.N * _zoom * _zoom;
            if (area < 1600f) break;
            Vector2 at = ScreenOf(realm.X, realm.Y);
            if (!OnScreen(at, size, 0f)) continue;
            int font = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(area) * 0.16f), 10, 30);
            float width = font * Mathf.Max(2, realm.Name.Length) * 0.95f, height = font * 1.3f;
            var rect = new Rect(at.x - width / 2f, at.y - height / 2f, width, height);
            if (!Free(taken, rect)) continue;
            taken.Add(rect);
            Text label = Label();
            label.fontSize = font;
            label.color = new Color(0.98f, 0.95f, 0.86f);
            label.text = realm.Name;
            ((RectTransform)label.transform).anchoredPosition = at;
        }
    }

    // 城：都城拉到一国看得清时出(带名字)，别的城再拉近些才出
    private static void PlaceTowns(Vector2 size, List<Rect> taken)
    {
        if (_zoom < 5f) return;
        foreach (Town town in _map.Towns)
        {
            if (!town.Cap && _zoom < 11f) continue;
            Vector2 at = ScreenOf(town.X, town.Y);
            if (!OnScreen(at, size, 10f)) continue;
            float half = town.Cap ? 4f : 2.5f;
            Image dot = Mark();
            dot.color = town.Cap ? new Color(0.98f, 0.82f, 0.3f) : new Color(0.93f, 0.9f, 0.82f);
            var rect = (RectTransform)dot.transform;
            rect.sizeDelta = new Vector2(half * 2f, half * 2f);
            rect.anchoredPosition = at;
            if (_zoom < (town.Cap ? 8f : 16f)) continue;
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

    // 军队：同一国在屏幕上挨得近(一格 36 个单位)的并成一个牌子，写总人数
    private static void PlaceArmies(Vector2 size, List<Rect> taken)
    {
        if (_armies == null || _armies.Map != _map.Stamp && _armies.Map != 0) return;
        float grid = _zoom >= 20f ? 24f : 36f;
        var groups = new Dictionary<(int, int, int), (float x, float y, int n, int count)>();
        foreach (Army army in _armies.Armies)
        {
            Vector2 at = ScreenOf(army.X, army.Y);
            if (!OnScreen(at, size, 20f)) continue;
            var key = (army.R, Mathf.FloorToInt(at.x / grid), Mathf.FloorToInt(at.y / grid));
            groups.TryGetValue(key, out var group);
            groups[key] = (group.x + at.x, group.y + at.y, group.n + army.N, group.count + 1);
        }
        foreach (KeyValuePair<(int, int, int), (float x, float y, int n, int count)> pair in groups)
        {
            var at = new Vector2(pair.Value.x / pair.Value.count, pair.Value.y / pair.Value.count);
            int r = pair.Key.Item1;
            Color edge = r >= 0 && r < _map.Realms.Length ? (Color)_map.Realms[r].Color : new Color(0.7f, 0.68f, 0.6f);
            Image frame = Mark();
            frame.color = edge;
            var frameRect = (RectTransform)frame.transform;
            frameRect.sizeDelta = new Vector2(14f, 12f);
            frameRect.anchoredPosition = at;
            Image face = Mark();
            face.color = new Color(0.16f, 0.17f, 0.15f, 0.95f);
            var faceRect = (RectTransform)face.transform;
            faceRect.sizeDelta = new Vector2(10f, 8f);
            faceRect.anchoredPosition = at;
            Text count = MarkText();
            count.fontSize = 10;
            count.color = edge;
            count.text = Short(pair.Value.n) + (pair.Value.count > 1 ? " ×" + pair.Value.count : "");
            ((RectTransform)count.transform).anchoredPosition = new Vector2(at.x, at.y - 13f);
            taken.Add(new Rect(at.x - 16f, at.y - 18f, 32f, 26f));
        }
    }

    // 交战：拉远一个小红方块，拉近写战役名与攻方胜算(同手机舆图的交战牌子)
    private static void PlaceBattles(Vector2 size, List<Rect> taken)
    {
        if (_armies == null) return;
        bool far = _zoom < 9f;
        foreach (Battle battle in _armies.Battles)
        {
            Vector2 at = ScreenOf(battle.X, battle.Y) + new Vector2(0f, far ? 0f : -16f);
            if (!OnScreen(at, size, 40f)) continue;
            if (far)
            {
                var small = new Rect(at.x - 5f, at.y - 5f, 10f, 10f);
                if (!Free(taken, small)) continue;
                taken.Add(small);
                Image dot = Mark();
                dot.color = Front;
                var dotRect = (RectTransform)dot.transform;
                dotRect.sizeDelta = new Vector2(8f, 8f);
                dotRect.anchoredPosition = at;
                continue;
            }
            string title = string.Format(LM.Get(battle.L ? "terminal_live_landing" : "terminal_live_battle"), battle.C, battle.P, battle.H, battle.Z);
            float width = 11f * Mathf.Max(6, title.Length) * 0.95f + 10f;
            var box = new Rect(at.x - width / 2f, at.y - 9f, width, 18f);
            if (!Free(taken, box)) continue;
            taken.Add(box);
            Image plate = Mark();
            plate.color = new Color(0.36f, 0.08f, 0.06f, 0.92f);
            var plateRect = (RectTransform)plate.transform;
            plateRect.sizeDelta = new Vector2(width, 18f);
            plateRect.anchoredPosition = at;
            Text label = MarkText();
            label.fontSize = 11;
            label.color = new Color(1f, 0.85f, 0.55f);
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

    private static Image Mark()
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
        return image;
    }
}
#endif
