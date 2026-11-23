#if !EC_SERVER
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.Terminal;

// 实时地图的图层与铭牌，照手机舆图的规矩(用户 2026-11-18 "还有国家铭牌和法理, 都按照网页的逻辑来")：
//   · 两层图：政体(帝国 → 国 → 城)、法理(天下 → 帝国法理 → 王国法理 → 城)。一层是一棵树，每块地挂在一片叶上；
//   · 逐级展开(同网页 openTree)：上面几级看这一家在屏幕里看得见的部分占了几成，占到 Cover 才展开、缩到 Uncover 以下才收起；
//     "拉近到城"只作用在叶上(一格地占到 CityPx 个点)。窗口多大都照比例算，不会越过行政区直接到城；
//   · 底图按眼下标着的那一家上色(没展开的帝国整片帝国色)，不同家之间描深界线，同一家里的各国描浅界线；展开的变了后台重涂一张、淡入；
//   · 铭牌(同网页 labPlace/drawLabel)：取这一家最大的一片地，主轴前后几个角度、几十个候选中心里二分出放得下的最大字框，
//     地形弯的字跟着弯一点；字号随远近，压着别家的不标，快盖满屏幕的淡下去。版图没变的沿用上一份算好的(后台线程算)
internal static partial class CoreLiveMap
{
    internal const int LayerPol = 0, LayerDejure = 1;
    internal static int MapLayer = LayerPol;
    private const float Cover = 0.35f, Uncover = 0.27f, CityPx = 7f, LabGap = 0.12f;

    private sealed class Lab
    {
        public float X, Y, A, HW, HH, CV, Aspect;
        public string[] Chars;
        public float[] Widths;
    }

    private sealed class Node
    {
        public int Id, Lvl, Parent = -1, N;
        public string Name = "";
        public Color32 Color;
        public bool HasColor;
        public readonly List<int> Kids = new();
        public double SX, SY;
        public float X, Y;
        public Lab Lab;
    }

    private sealed class Tree
    {
        public readonly List<Node> Nodes = new();
        public readonly List<int> Tops = new();
        public int[] ZLeaf;
        public int[] TownLeaf;
        public readonly Dictionary<string, Node> ByKey = new();
    }

    // ───────── 后台线程：建树、排铭牌 ─────────
    private static Node NodeFor(Tree tree, string key, int lvl, string name, Color32? color, Node parent)
    {
        if (tree.ByKey.TryGetValue(key, out Node node)) return node;
        node = new Node { Id = tree.Nodes.Count, Lvl = lvl, Name = name ?? "", Parent = parent?.Id ?? -1 };
        if (color.HasValue)
        {
            node.Color = color.Value;
            node.HasColor = true;
        }
        tree.ByKey[key] = node;
        tree.Nodes.Add(node);
        if (parent != null) parent.Kids.Add(node.Id);
        else tree.Tops.Add(node.Id);
        return node;
    }

    private static Tree BuildPolTree(MapData data)
    {
        var tree = new Tree();
        int n = data.W * data.H;
        var leafOf = new int[data.Realms.Length];
        for (int i = 0; i < data.Realms.Length; i++)
        {
            int e = data.RealmEmp[i];
            Node emp = e >= 0 && e < data.EmpName.Length ? NodeFor(tree, "e" + e, 1, data.EmpName[e], data.EmpColor[e], null) : null;
            leafOf[i] = NodeFor(tree, "r" + i, 2, data.Realms[i].Name, data.Realms[i].Color, emp).Id;
        }
        tree.ZLeaf = new int[n];
        for (int c = 0; c < n; c++) tree.ZLeaf[c] = data.Cells[c] < data.Realms.Length ? leafOf[data.Cells[c]] : -1;
        tree.TownLeaf = new int[data.Towns.Length];
        for (int t = 0; t < data.Towns.Length; t++)
            tree.TownLeaf[t] = data.Towns[t].R >= 0 && data.Towns[t].R < leafOf.Length ? leafOf[data.Towns[t].R] : -1;
        FinishTree(tree, data);
        return tree;
    }

    private static Tree BuildDejureTree(MapData data)
    {
        var tree = new Tree();
        int n = data.W * data.H;
        var leafOf = new Dictionary<int, int>();
        for (int t = 0; t < data.Towns.Length; t++)
        {
            Town town = data.Towns[t];
            if (town.Dk < 0 || town.Dk >= data.DkName.Length || leafOf.ContainsKey(town.Dk)) continue;
            Node land = town.Dt >= 0 && town.Dt < data.DtName.Length ? NodeFor(tree, "t" + town.Dt, 0, data.DtName[town.Dt], data.DtColor[town.Dt], null) : null;
            Node core = town.De >= 0 && town.De < data.DeName.Length ? NodeFor(tree, "e" + town.De, 1, data.DeName[town.De], data.DeColor[town.De], land) : null;
            leafOf[town.Dk] = NodeFor(tree, "k" + town.Dk, 2, data.DkName[town.Dk], data.DkColor[town.Dk], core ?? land).Id;
        }
        tree.TownLeaf = new int[data.Towns.Length];
        for (int t = 0; t < data.Towns.Length; t++)
            tree.TownLeaf[t] = leafOf.TryGetValue(data.Towns[t].Dk, out int leaf) ? leaf : -1;
        tree.ZLeaf = new int[n];
        for (int c = 0; c < n; c++)
        {
            ushort city = data.ZoneCity[c];
            tree.ZLeaf[c] = city < tree.TownLeaf.Length ? tree.TownLeaf[city] : -1;
        }
        FinishTree(tree, data);
        return tree;
    }

    // 各家占几块地、重心在哪(一块地算进它那片叶和上头各级)；上一级没颜色的照里头占地最大的那一家
    private static void FinishTree(Tree tree, MapData data)
    {
        List<Node> nodes = tree.Nodes;
        for (int c = 0; c < tree.ZLeaf.Length; c++)
        {
            int leaf = tree.ZLeaf[c];
            if (leaf < 0) continue;
            int x = c % data.W, y = c / data.W;
            for (Node up = nodes[leaf]; up != null; up = up.Parent >= 0 ? nodes[up.Parent] : null)
            {
                up.N++;
                up.SX += x + 0.5d;
                up.SY += y + 0.5d;
            }
        }
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            Node g = nodes[i];
            g.X = g.N > 0 ? (float)(g.SX / g.N) : 0f;
            g.Y = g.N > 0 ? (float)(g.SY / g.N) : 0f;
            if (g.Kids.Count == 0 || g.HasColor) continue;
            Node big = null;
            foreach (int id in g.Kids)
                if (big == null || nodes[id].N > big.N) big = nodes[id];
            g.Color = big?.Color ?? new Color32(136, 136, 136, 255);
            g.HasColor = true;
        }
    }

    // 各家的铭牌：版图没变(块数、重心一样)的沿用上一份的，其余重排
    private static void PlaceLabels(Tree tree, Tree previous, MapData data)
    {
        Dictionary<string, Node> before = null;
        if (previous != null)
        {
            before = new Dictionary<string, Node>();
            foreach (Node old in previous.Nodes) before[old.Lvl + "|" + old.Name] = old;
        }
        // 每家的地(连同下面各级的)
        var lists = new List<int>[tree.Nodes.Count];
        for (int c = 0; c < tree.ZLeaf.Length; c++)
        {
            int leaf = tree.ZLeaf[c];
            if (leaf < 0) continue;
            for (Node up = tree.Nodes[leaf]; up != null; up = up.Parent >= 0 ? tree.Nodes[up.Parent] : null)
                (lists[up.Id] ??= new List<int>()).Add(c);
        }
        var mark = new int[tree.ZLeaf.Length];
        int stamp = 0;
        foreach (Node node in tree.Nodes)
        {
            if (node.N == 0 || string.IsNullOrEmpty(node.Name) || lists[node.Id] == null) continue;
            if (before != null && before.TryGetValue(node.Lvl + "|" + node.Name, out Node old) && old.Lab != null && old.N == node.N &&
                Mathf.Abs(old.X - node.X) < 0.01f && Mathf.Abs(old.Y - node.Y) < 0.01f)
            {
                node.Lab = old.Lab;
                continue;
            }
            stamp += 2;
            node.Lab = LabPlace(lists[node.Id], node.Name, data.W, data.H, mark, stamp);
        }
    }

    private static (string[] chars, float[] widths, float aspect) Measure(string name)
    {
        var chars = new List<string>();
        var widths = new List<float>();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsHighSurrogate(c) && i + 1 < name.Length)
            {
                chars.Add(name.Substring(i, 2));
                widths.Add(1f);
                i++;
                continue;
            }
            chars.Add(c.ToString());
            widths.Add(c == ' ' ? 0.12f : c == ' ' ? 0.3f : c < 0x2E80 ? 0.58f : 1f);
        }
        float sum = 0f;
        foreach (float w in widths) sum += w;
        float gap = chars.Count > 1 ? LabGap : 0f;
        return (chars.ToArray(), widths.ToArray(), Mathf.Max(0.5f, sum + gap * (chars.Count - 1)));
    }

    // 同网页 labPlace：排一家的铭牌(地块坐标)：中心、角度、半宽半高、弯度。没处放回 null
    private static Lab LabPlace(List<int> zones, string name, int W, int H, int[] mark, int stamp)
    {
        int own = stamp - 1, seen = stamp;
        foreach (int z in zones) mark[z] = own;
        // 最大的一片(八邻连着的算一片)
        List<int> cmp = null;
        foreach (int start in zones)
        {
            if (mark[start] != own) continue;
            var part = new List<int> { start };
            mark[start] = seen;
            for (int j = 0; j < part.Count; j++)
            {
                int c = part[j], cx = c % W, cy = c / W;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int nz = ny * W + nx;
                    if (mark[nz] == own)
                    {
                        mark[nz] = seen;
                        part.Add(nz);
                    }
                }
            }
            if (cmp == null || part.Count > cmp.Count) cmp = part;
        }
        if (cmp == null) return null;
        int m = cmp.Count;
        var px = new double[m];
        var py = new double[m];
        double mx = 0, my = 0;
        for (int i = 0; i < m; i++)
        {
            px[i] = cmp[i] % W + 0.5;
            py[i] = cmp[i] / W + 0.5;
            mx += px[i];
            my += py[i];
        }
        mx /= m;
        my /= m;
        double xx = 0, xy = 0, yy = 0, nd = 1e18;
        int near = 0;
        for (int i = 0; i < m; i++)
        {
            double ex = px[i] - mx, ey = py[i] - my, d = ex * ex + ey * ey;
            xx += ex * ex;
            xy += ex * ey;
            yy += ey * ey;
            if (d < nd)
            {
                nd = d;
                near = i;
            }
        }
        // 只有这一片里的地算"在这一家地上"
        bool Inside(double x, double y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return false;
            return mark[(int)Math.Floor(y) * W + (int)Math.Floor(x)] == seen;
        }
        var angles = new List<double>();
        void AddAngle(double a)
        {
            while (a > 90) a -= 180;
            while (a <= -90) a += 180;
            foreach (double q in angles)
                if (Math.Abs(q - a) < 1) return;
            angles.Add(a);
        }
        double pa = 0.5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI;
        for (int off = -30; off <= 30; off += 10) AddAngle(pa + off);
        AddAngle(0);
        AddAngle(90);
        var centres = new List<int> { near };
        int cc = Math.Min(m, 40);
        for (int i = 0; i < cc; i++)
        {
            int j = cc == 1 ? 0 : (int)Math.Round(i * (m - 1) / (double)(cc - 1));
            if (j != near) centres.Add(j);
        }
        bool Fits(double cx, double cy, double ax, double ay, double hw, double hh, bool dense)
        {
            int sa = Math.Max(dense ? 2 : 4, (int)Math.Ceiling(hw * 2 / 0.4)), sb = dense ? Math.Max(2, (int)Math.Ceiling(hh * 2 / 0.4)) : 4;
            for (int b = 0; b <= sb; b++)
            {
                double ac = -hh + 2 * hh * b / sb;
                for (int a = 0; a <= sa; a++)
                {
                    double al = -hw + 2 * hw * a / sa;
                    if (!Inside(cx + ax * al - ay * ac, cy + ay * al + ax * ac)) return false;
                }
            }
            return true;
        }
        (string[] chars, float[] widths, float aspect) = Measure(name);
        double asp = aspect, bh = 0, bx = 0, by = 0, ba = 0;
        foreach (double deg in angles)
        {
            double r = deg * Math.PI / 180, ax = Math.Cos(r), ay = Math.Sin(r), ra = 0.5 * (Math.Abs(ax) + Math.Abs(ay));
            double mina = 1e18, maxa = -1e18, minn = 1e18, maxn = -1e18;
            for (int t = 0; t < m; t++)
            {
                double al = px[t] * ax + py[t] * ay, an = -px[t] * ay + py[t] * ax;
                if (al - ra < mina) mina = al - ra;
                if (al + ra > maxa) maxa = al + ra;
                if (an - ra < minn) minn = an - ra;
                if (an + ra > maxn) maxn = an + ra;
            }
            foreach (int ci in centres)
            {
                double cx = px[ci], cy = py[ci], ca = cx * ax + cy * ay, cn = -cx * ay + cy * ax;
                double hi = Math.Min(Math.Min(ca - mina, maxa - ca) / asp, Math.Min(cn - minn, maxn - cn)), lo = 0;
                if (hi <= bh) continue;
                for (int it = 0; it < 7; it++)
                {
                    double mid = (lo + hi) / 2;
                    if (Fits(cx, cy, ax, ay, mid * asp, mid, false)) lo = mid;
                    else hi = mid;
                }
                if (lo > bh)
                {
                    bh = lo;
                    bx = cx;
                    by = cy;
                    ba = r;
                }
            }
        }
        if (bh <= 0.05) return null;
        double fax = Math.Cos(ba), fay = Math.Sin(ba), fhh = bh * 0.82, fhw = fhh * asp;
        for (int tr = 0; tr < 12 && !Fits(bx, by, fax, fay, fhw, fhh, true); tr++)
        {
            fhw *= 0.9;
            fhh *= 0.9;
        }
        if (!Fits(bx, by, fax, fay, fhw, fhh, true)) return null;
        // 弯度：沿字的方向把这片地切七段，看各段中线偏离两头连线多少；直的国家字也直，弯的才弯(最多一成)
        double mina2 = 1e18, maxa2 = -1e18, curv = 0;
        var offs = new double[7];
        var counts = new int[7];
        for (int i = 0; i < m; i++)
        {
            double a2 = (px[i] - bx) * fax + (py[i] - by) * fay;
            if (a2 < mina2) mina2 = a2;
            if (a2 > maxa2) maxa2 = a2;
        }
        double span = maxa2 - mina2;
        if (m >= 3 && span > 0.1)
        {
            for (int i = 0; i < m; i++)
            {
                double ddx = px[i] - bx, ddy = py[i] - by;
                int sl = Math.Min(6, Math.Max(0, (int)Math.Floor((ddx * fax + ddy * fay - mina2) / span * 7)));
                offs[sl] += -ddx * fay + ddy * fax;
                counts[sl]++;
            }
            int f0 = -1, f1 = -1;
            for (int i = 0; i < 7; i++)
            {
                if (counts[i] == 0) continue;
                offs[i] /= counts[i];
                if (f0 < 0) f0 = i;
                f1 = i;
            }
            if (f0 >= 0 && f1 - f0 >= 2)
            {
                double rs = 0, ws = 0;
                for (int i = f0 + 1; i < f1; i++)
                {
                    if (counts[i] == 0) continue;
                    double pr = (i - f0) / (double)(f1 - f0), pf = 4 * pr * (1 - pr);
                    rs += (offs[i] - (offs[f0] + (offs[f1] - offs[f0]) * pr)) * pf;
                    ws += pf * pf;
                }
                if (ws > 0.01)
                {
                    double lim = Math.Min(0.1, fhh * 0.45 / fhw);
                    curv = Math.Max(-lim, Math.Min(lim, rs / ws / fhw));
                }
            }
        }
        return new Lab
        {
            X = (float)bx, Y = (float)by, A = (float)ba, HW = (float)fhw, HH = (float)fhh, CV = (float)curv, Aspect = aspect,
            Chars = chars, Widths = widths
        };
    }

    // ───────── 主线程：逐级展开 ─────────
    private static int[] _shownOf;
    private static byte[] _cityOpen, _wasOpen;
    private static List<int> _front = new();
    private static string _openKey;
    private static Tree _openTree;

    private static Tree CurrentTree => _map?.Trees == null ? null : _map.Trees[MapLayer == LayerDejure ? 1 : 0];

    // 按眼下的视野定哪几家展开了；展开的变了就要后台重涂底图(返回 true)
    private static bool OpenTree()
    {
        Tree tree = CurrentTree;
        if (tree == null) return false;
        int n = tree.Nodes.Count;
        if (!ReferenceEquals(_openTree, tree) || _shownOf == null || _shownOf.Length != n)
        {
            // 新图(每秒一份)接着记哪几家展开着：不然卡在两道门槛之间的那家每来一份图就收起一回
            var wasOpen = new HashSet<string>();
            if (_openTree != null && _wasOpen != null && _wasOpen.Length == _openTree.Nodes.Count)
                foreach (Node old in _openTree.Nodes)
                    if (_wasOpen[old.Id] != 0) wasOpen.Add(old.Lvl + "|" + old.Name);
            _shownOf = new int[n];
            _cityOpen = new byte[n];
            _wasOpen = new byte[n];
            foreach (Node node in tree.Nodes)
                if (wasOpen.Contains(node.Lvl + "|" + node.Name)) _wasOpen[node.Id] = 1;
            _openTree = tree;
            _openKey = null;
        }
        Vector2 size = ScreenSize;
        float vx = _cx - size.x / 2f / _zoom, vy = _cy - size.y / 2f / _zoom, vw = size.x / _zoom, vh = size.y / _zoom;
        int W = _map.W, H = _map.H;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(vx)), y0 = Mathf.Max(0, Mathf.FloorToInt(vy));
        int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(vx + vw) - 1), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(vy + vh) - 1);
        var vis = new int[n];
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int leaf = tree.ZLeaf[y * W + x];
            if (leaf >= 0) vis[leaf]++;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            int p = tree.Nodes[i].Parent;
            if (p >= 0) vis[p] += vis[i];
        }
        float total = Mathf.Max(1f, vw * vh);
        bool cityZoom = _zoom / Mathf.Max(1, _map.Tile) >= CityPx;
        _front.Clear();
        void Walk(int id, int shown)
        {
            Node node = tree.Nodes[id];
            bool leaf = node.Kids.Count == 0;
            bool open = leaf ? cityZoom : vis[id] >= total * (_wasOpen[id] != 0 ? Uncover : Cover);
            _wasOpen[id] = (byte)(open ? 1 : 0);
            if (shown < 0 && !open)
            {
                shown = id;
                _front.Add(id);
            }
            if (leaf)
            {
                _shownOf[id] = shown >= 0 ? shown : id;
                _cityOpen[id] = (byte)(shown < 0 ? 1 : 0);
                return;
            }
            foreach (int kid in node.Kids) Walk(kid, shown);
        }
        foreach (int top in tree.Tops) Walk(top, -1);
        var key = new StringBuilder(n * 4);
        for (int i = 0; i < n; i++)
            if (tree.Nodes[i].Kids.Count == 0) key.Append(_shownOf[i]).Append(_cityOpen[i] != 0 ? '+' : ',');
        string text = key.ToString();
        if (text == _openKey) return false;
        _openKey = text;
        return true;
    }

    // 底图要按眼下展开的样子重涂一张(后台线程做，做好淡入，见 Apply)
    private sealed class PaintJob
    {
        public MapData Map;
        public Tree Tree;
        public int[] ShownOf;
        public byte[] CityOpen;
        public bool Political;
    }

    private sealed class Painted
    {
        public MapData Map;
        public Color32[] Pixels;
        public int Width, Height;
    }

    private static PaintJob _paintWant;
    private static volatile Painted _painted;

    private static void RequestPaint()
    {
        if (_map?.Trees == null) return;
        var job = new PaintJob { Map = _map, Tree = CurrentTree, ShownOf = (int[])_shownOf.Clone(), CityOpen = (byte[])_cityOpen.Clone(), Political = MapLayer != LayerDejure };
        Interlocked.Exchange(ref _paintWant, job);
    }

    // 后台线程：有要重涂的就涂
    private static void PaintPending()
    {
        PaintJob job = Interlocked.Exchange(ref _paintWant, null);
        if (job == null) return;
        MapData data = job.Map;
        Color32[] terrain = _terrain;
        int s = data.Tile, w = data.W, h = data.H, tw = w * s, th = h * s;
        if (terrain == null || _terrainW != tw || _terrainH != th)
        {
            // 还没取到原版的地形：海照海、陆地一律淡褐
            s = Mathf.Clamp(1024 / Mathf.Max(1, Mathf.Max(w, h)), 1, 6);
            tw = w * s;
            th = h * s;
            terrain = new Color32[tw * th];
            for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
                terrain[y * tw + x] = data.Cells[(h - 1 - y / s) * w + x / s] == TerminalHost.LiveWater ? Sea : new Color32(118, 112, 88, 255);
        }
        Tree tree = job.Tree;
        int nodes = tree.Nodes.Count;
        var tint = new Color32[nodes];
        var dark = new Color32[nodes];
        var light = new Color32[nodes];
        for (int i = 0; i < nodes; i++)
        {
            Node shown = tree.Nodes[job.ShownOf[i] >= 0 && job.ShownOf[i] < nodes ? job.ShownOf[i] : i];
            tint[i] = shown.Color;
            dark[i] = Darken(shown.Color);
            light[i] = new Color32((byte)(shown.Color.r * 0.8f), (byte)(shown.Color.g * 0.8f), (byte)(shown.Color.b * 0.8f), 255);
        }
        var pixels = new Color32[tw * th];
        int[] zl = tree.ZLeaf;
        for (int y = 0; y < th; y++)
        {
            int cy = h - 1 - y / s, ly = y % s;
            for (int x = 0; x < tw; x++)
            {
                int at = y * tw + x;
                Color32 ground = terrain[at];
                int cx = x / s, cell = cy * w + cx, leaf = zl[cell];
                if (leaf < 0)
                {
                    pixels[at] = ground;
                    continue;
                }
                int lx = x % s, other = cell;
                if (lx == 0 && cx > 0 && zl[cell - 1] != leaf) other = cell - 1;
                else if (lx == s - 1 && cx < w - 1 && zl[cell + 1] != leaf) other = cell + 1;
                else if (ly == 0 && cy < h - 1 && zl[cell + w] != leaf) other = cell + w;
                else if (ly == s - 1 && cy > 0 && zl[cell - w] != leaf) other = cell - w;
                if (other != cell)
                {
                    int otherLeaf = zl[other];
                    if (otherLeaf < 0)
                    {
                        if (data.Cells[other] != TerminalHost.LiveWater)
                        {
                            pixels[at] = dark[leaf];
                            continue;
                        }
                    }
                    else if (job.ShownOf[otherLeaf] != job.ShownOf[leaf])
                    {
                        ushort a = data.Cells[cell], b = data.Cells[other];
                        pixels[at] = job.Political && a < data.Realms.Length && b < data.Realms.Length &&
                                     data.Fronts.Contains(Math.Min(a, b) << 16 | Math.Max(a, b)) ? Front : dark[leaf];
                        continue;
                    }
                    else
                    {
                        pixels[at] = light[leaf];
                        continue;
                    }
                }
                // 拉近到城的那几片：城与城之间也描一道浅线
                if (job.CityOpen[leaf] != 0)
                {
                    ushort town = data.ZoneCity[cell];
                    int near = -1;
                    if (lx == 0 && cx > 0) near = cell - 1;
                    else if (lx == s - 1 && cx < w - 1) near = cell + 1;
                    else if (ly == 0 && cy < h - 1) near = cell + w;
                    else if (ly == s - 1 && cy > 0) near = cell - w;
                    if (near >= 0 && zl[near] == leaf && data.ZoneCity[near] != town)
                    {
                        pixels[at] = light[leaf];
                        continue;
                    }
                }
                Color32 t = tint[leaf];
                pixels[at] = new Color32((byte)(ground.r + (t.r - ground.r) * TintAlpha), (byte)(ground.g + (t.g - ground.g) * TintAlpha),
                    (byte)(ground.b + (t.b - ground.b) * TintAlpha), 255);
            }
        }
        _painted = new Painted { Map = data, Pixels = pixels, Width = tw, Height = th };
    }

    // ───────── 主线程：铭牌(同网页 drawLabel) ─────────
    private static readonly List<Text> LabelChars = new();
    private static int _labelCharsUsed;
    private const int LabelFont = 40;

    private struct Box
    {
        public float X, Y, C, S, W, H;
    }

    // 两个斜放的字框压没压着(分离轴)
    private static bool Hit(Box a, Box b)
    {
        float[] axes = { a.C, a.S, -a.S, a.C, b.C, b.S, -b.S, b.C };
        float dx = b.X - a.X, dy = b.Y - a.Y;
        for (int i = 0; i < 8; i += 2)
        {
            float ux = axes[i], uy = axes[i + 1];
            float ra = a.W * Mathf.Abs(a.C * ux + a.S * uy) + a.H * Mathf.Abs(-a.S * ux + a.C * uy);
            float rb = b.W * Mathf.Abs(b.C * ux + b.S * uy) + b.H * Mathf.Abs(-b.S * ux + b.C * uy);
            if (Mathf.Abs(dx * ux + dy * uy) > ra + rb) return false;
        }
        return true;
    }

    private static void PlaceNameplates(Vector2 size, List<Rect> taken)
    {
        _labelCharsUsed = 0;
        Tree tree = CurrentTree;
        if (tree != null)
        {
            var boxes = new List<Box>();
            var order = new List<int>(_front);
            order.Sort((a, b) => tree.Nodes[a].Lvl != tree.Nodes[b].Lvl ? tree.Nodes[a].Lvl.CompareTo(tree.Nodes[b].Lvl) : tree.Nodes[b].N.CompareTo(tree.Nodes[a].N));
            float small = 9f, cap = Mathf.Max(16f, Mathf.Min(size.x, size.y) * 0.22f);
            foreach (int id in order)
            {
                Node node = tree.Nodes[id];
                Lab lab = node.Lab;
                if (lab == null || node.N == 0) continue;
                float f = 2f * lab.HH * _zoom * 0.9f;
                if (f < small) continue;
                float alpha = Mathf.Min(1f, (f - small) / 3f + 0.25f);
                f = Mathf.Min(f, cap);
                float hs = lab.Aspect * f / 2f, c = Mathf.Cos(lab.A), s = Mathf.Sin(lab.A);
                Vector2 at = ScreenOf(lab.X, lab.Y);
                // 屏幕的 y 朝上：斜放的方向反过来
                var box = new Box { X = at.x, Y = at.y, C = c, S = -s, W = hs, H = f * 0.55f + Mathf.Abs(lab.CV) * hs };
                float reach = Mathf.Max(box.W, box.H);
                if (at.x < -reach || at.y < -reach || at.x > size.x + reach || at.y > size.y + reach) continue;
                bool blocked = false;
                foreach (Box other in boxes)
                    if (Hit(box, other))
                    {
                        blocked = true;
                        break;
                    }
                if (blocked) continue;
                boxes.Add(box);
                float ex = box.W * Mathf.Abs(c) + box.H * Mathf.Abs(s), ey = box.W * Mathf.Abs(s) + box.H * Mathf.Abs(c);
                taken.Add(new Rect(at.x - ex, at.y - ey, ex * 2f, ey * 2f));
                // 快盖满屏幕的大字淡下去
                float cover = Mathf.Max(ex * 2f / size.x, ey * 2f / size.y), tc = Mathf.Clamp01((cover - 0.7f) / 0.3f);
                alpha *= 1f - 0.82f * tc * tc * (3f - 2f * tc);
                Color fill = node.Lvl == 0 ? new Color(1f, 0.94f, 0.77f) : node.Lvl == 1 ? new Color(1f, 0.957f, 0.76f) : Color.white;
                Color edge = node.Lvl == 0 ? new Color(0.14f, 0.055f, 0.23f, 0.86f) : node.Lvl == 1 ? new Color(0.21f, 0.18f, 0.08f, 0.8f) : new Color(0.06f, 0.06f, 0.05f, 0.8f);
                fill.a = alpha;
                edge.a *= alpha;
                float cursor = -hs;
                for (int i = 0; i < lab.Chars.Length; i++)
                {
                    float cw = lab.Widths[i] * f, cx = cursor + cw / 2f, u = hs > 0f ? cx / hs : 0f;
                    cursor += cw + LabGap * f;
                    if (lab.Chars[i] == " " || lab.Chars[i] == " ") continue;
                    float ly = lab.CV * hs * (1f - u * u), turn = Mathf.Atan(-2f * lab.CV * u);
                    // 地块坐标(y 朝下)里转 A，再翻到屏幕(y 朝上)
                    float dx = cx * c - ly * s, dy = cx * s + ly * c;
                    Text glyph = LabelChar();
                    glyph.text = lab.Chars[i];
                    glyph.color = fill;
                    glyph.GetComponent<Outline>().effectColor = edge;
                    var rect = (RectTransform)glyph.transform;
                    rect.anchoredPosition = new Vector2(at.x + dx, at.y - dy);
                    rect.localScale = Vector3.one * (f / LabelFont);
                    rect.localRotation = Quaternion.Euler(0f, 0f, -(lab.A + turn) * Mathf.Rad2Deg);
                }
            }
        }
        for (int i = _labelCharsUsed; i < LabelChars.Count; i++) LabelChars[i].gameObject.SetActive(false);
    }

    private static Text LabelChar()
    {
        if (_labelCharsUsed == LabelChars.Count)
        {
            Text made = NewText(_layer, LabelFont, Color.white);
            made.fontStyle = FontStyle.Bold;
            ((RectTransform)made.transform).sizeDelta = new Vector2(LabelFont * 1.6f, LabelFont * 1.4f);
            made.GetComponent<Outline>().effectDistance = new Vector2(2.5f, -2.5f);
            LabelChars.Add(made);
        }
        Text text = LabelChars[_labelCharsUsed++];
        if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
        return text;
    }

    // 鼠标底下：眼下标着的那一家；拉近到城的写城名
    private static string HoverName(int x, int y)
    {
        Tree tree = CurrentTree;
        if (_map == null || x < 0 || y < 0 || x >= _map.W || y >= _map.H) return "";
        int cell = y * _map.W + x;
        if (tree == null || _shownOf == null || _shownOf.Length != tree.Nodes.Count)
        {
            ushort value = _map.Cells[cell];
            return value < _map.Realms.Length ? _map.Realms[value].Name : "";
        }
        int leaf = tree.ZLeaf[cell];
        if (leaf < 0) return "";
        if (_cityOpen[leaf] != 0)
        {
            ushort town = _map.ZoneCity[cell];
            string up = tree.Nodes[leaf].Name;
            return town < _map.Towns.Length ? _map.Towns[town].Name + " · " + up : up;
        }
        return tree.Nodes[_shownOf[leaf]].Name;
    }

    // 图层切换(速度栏上的按钮)
    private static void ToggleLayer()
    {
        MapLayer = MapLayer == LayerDejure ? LayerPol : LayerDejure;
        _openKey = null;
        _openTree = null;
        _dirty = true;
        if (_layerText != null) _layerText.text = LM.Get(MapLayer == LayerDejure ? "terminal_live_layer_dj" : "terminal_live_layer_pol");
    }

    private static Text _layerText;
}
#endif
