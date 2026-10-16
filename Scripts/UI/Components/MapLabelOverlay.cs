using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 街区级标注(见 MapZoomTier ④，用户 2026-10-16 选定：街道名、建筑名、住宅小区名)：
//   · 镜头拉到一格至少 6 个屏幕像素才标，只标镜头里离中心最近的几座城(与城市精细层一致)；
//   · 街道：城里地块之间的街格线，横的叫街、竖的叫巷(古代)或路、大街(近代)，名字取吉祥字词，同城不重名；
//   · 建筑：官署(古代县衙、学宫、书院、驿站、官仓；近代人民政府、法院、医院、中学、小学、图书馆、邮政局)、
//     市场(集市、商铺；近代商场、超市、便利店)、作坊与工厂、寺庙祠观、兵营、码头，多数冠以城名或雅名；
//   · 住宅：古代每块住宅地标坊名(平康坊)，近代每栋标小区名加栋号(雪香花园 3 栋)；再拉近(一格 10 像素以上)才标住宅；
//   · 名字按城、地块、街区编号算出来，同一处每次一样，不存档；标注只在平面图变了或镜头里的城变了时重排。
// 字是世界坐标里的 TextMesh，盖在所有地图图层之上；至多 160 条，对象池复用。
public static class MapLabelOverlay
{
    private const int MaxCities = 4, MaxLabels = 160;
    private const float HouseZoom = 10f;
    // 字高(格)
    private const float StreetSize = 0.9f, BuildingSize = 0.75f, HouseSize = 0.6f;

    private sealed class Label
    {
        public GameObject Object;
        public TextMesh Text;
        public MeshRenderer Renderer;
        public string Shown;
    }

    private readonly struct Spec
    {
        public readonly Vector2 Tile;
        public readonly string Text;
        public readonly Color Color;
        public readonly float Size;
        public readonly bool Vertical;
        public readonly int Priority;

        public Spec(Vector2 tile, string text, Color color, float size, bool vertical, int priority)
        {
            Tile = tile;
            Text = text;
            Color = color;
            Size = size;
            Vertical = vertical;
            Priority = priority;
        }
    }

    private static readonly List<Label> Pool = new();
    private static readonly Dictionary<long, (int stamp, bool houses, List<Spec> specs)> Cache = new();
    private static Font _font;
    private static object _world;

    private static readonly Color StreetColor = new(0.36f, 0.38f, 0.42f), CivicColor = new(0.16f, 0.36f, 0.66f),
        MarketColor = new(0.86f, 0.42f, 0.10f), IndustryColor = new(0.45f, 0.36f, 0.30f), TempleColor = new(0.52f, 0.30f, 0.62f),
        MilitaryColor = new(0.40f, 0.46f, 0.30f), DockColor = new(0.10f, 0.50f, 0.66f), HouseColor = new(0.42f, 0.42f, 0.44f);

    public static void Update()
    {
        MapBox world = World.world;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            Cache.Clear();
            HideAll();
        }
        SpriteRenderer source = world?.zone_calculator?.sprRnd;
        Camera camera = world?.camera;
        float ppt = MapZoomTier.PixelsPerTile(camera);
        if (source?.sprite == null || camera == null || !source.enabled || !PureMapMode.Enabled || !PureMapPatch.DetailShown ||
            EmpireCraft.Scripts.Layer.CityViewMap.Active || ZonePlanSystem.SteadyOverlay || ppt < MapZoomTier.Street)
        {
            HideAll();
            return;
        }
        bool houses = ppt >= HouseZoom;
        Rect view = ViewInTiles(camera, source);
        var cities = new List<(City city, float distance)>();
        foreach (City city in world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0) continue;
            WorldTile center = city.getTile();
            if (center == null) continue;
            float dx = center.x - view.center.x, dy = center.y - view.center.y;
            float reach = Mathf.Max(view.width, view.height) * 0.5f + 48f;
            if (Mathf.Abs(dx) > reach || Mathf.Abs(dy) > reach) continue;
            cities.Add((city, dx * dx + dy * dy));
        }
        cities.Sort((a, b) => a.distance.CompareTo(b.distance));
        var specs = new List<Spec>();
        foreach ((City city, _) in cities.Take(MaxCities))
            specs.AddRange(SpecsOf(city, houses).Where(spec => view.Contains(spec.Tile)));
        // 主次：街道、官署先标，满了不标住宅
        specs.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        Font font = LabelFont();
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit, tileWorld = 1f / ppu;
        int used = 0;
        foreach (Spec spec in specs)
        {
            if (used >= MaxLabels) break;
            Label label = Take(used++, source, font);
            string text = spec.Vertical ? string.Join("\n", spec.Text.Select(ch => ch.ToString())) : spec.Text;
            // 字没变不重设(重设会重建网格)
            if (label.Shown != text)
            {
                label.Text.text = text;
                label.Shown = text;
            }
            label.Text.color = spec.Color;
            label.Object.transform.localPosition = new Vector3((spec.Tile.x - reference.pivot.x) / ppu,
                (spec.Tile.y - reference.pivot.y) / ppu, 0f);
            label.Object.transform.localScale = Vector3.one * (tileWorld * spec.Size);
            label.Renderer.sortingLayerID = source.sortingLayerID;
            label.Renderer.sortingOrder = source.sortingOrder + 6;
            if (!label.Object.activeSelf) label.Object.SetActive(true);
        }
        for (int i = used; i < Pool.Count; i++)
            if (Pool[i].Object != null && Pool[i].Object.activeSelf) Pool[i].Object.SetActive(false);
    }

    private static Font LabelFont()
    {
        if (_font != null) return _font;
        try
        {
            _font = UnityEngine.Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft JhengHei", "PingFang SC", "Noto Sans CJK SC", "SimHei", "Arial" }, 48);
        }
        catch
        {
            _font = null;
        }
        return _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static Label Take(int index, SpriteRenderer source, Font font)
    {
        while (Pool.Count <= index) Pool.Add(new Label());
        Label label = Pool[index];
        if (label.Object == null)
        {
            label.Object = new GameObject("EmpireCraftMapLabel");
            label.Text = label.Object.AddComponent<TextMesh>();
            label.Renderer = label.Object.GetComponent<MeshRenderer>();
            label.Text.anchor = TextAnchor.MiddleCenter;
            label.Text.alignment = TextAlignment.Center;
            label.Text.fontSize = 48;
            // 字号 48、字高约 characterSize×4.8 个单位；按格缩放后约一格高
            label.Text.characterSize = 0.21f;
        }
        if (label.Text.font != font)
        {
            label.Text.font = font;
            label.Renderer.sharedMaterial = font.material;
        }
        if (label.Object.transform.parent != source.transform) label.Object.transform.SetParent(source.transform, false);
        return label;
    }

    private static void HideAll()
    {
        foreach (Label label in Pool)
            if (label.Object != null && label.Object.activeSelf) label.Object.SetActive(false);
    }

    private static Rect ViewInTiles(Camera camera, SpriteRenderer source)
    {
        Sprite reference = source.sprite;
        float ppu = reference.pixelsPerUnit;
        Vector3 a = source.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0f, 0f, 0f)));
        Vector3 b = source.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(1f, 1f, 0f)));
        float x0 = Mathf.Min(a.x, b.x) * ppu + reference.pivot.x, x1 = Mathf.Max(a.x, b.x) * ppu + reference.pivot.x;
        float y0 = Mathf.Min(a.y, b.y) * ppu + reference.pivot.y, y1 = Mathf.Max(a.y, b.y) * ppu + reference.pivot.y;
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // ───────── 一座城的标注 ─────────

    private static List<Spec> SpecsOf(City city, bool houses)
    {
        // 平面图没变就用上次排好的(不每帧重新取平面图)
        int stamp = PureMapPatch.StampOf(city);
        if (Cache.TryGetValue(city.id, out var cached) && cached.stamp == stamp && cached.houses == houses) return cached.specs;
        PureMapPatch.LabelPlan plan = PureMapPatch.LabelsOf(city);
        if (plan == null) return new List<Spec>();
        var specs = new List<Spec>();
        bool modern = plan.Modern >= 0.5f;
        string cityName = CityName(city);
        Streets(city, plan, modern, specs);
        var residences = new Dictionary<int, string>();
        int houseNumber = 0;
        foreach (PureMapPatch.LabelSlot slot in plan.Slots)
        {
            long seed = city.id * 1000003L + slot.Zone * 31L + slot.Index;
            string icon = slot.Icon;
            if (icon.StartsWith("house_"))
            {
                if (!houses) continue;
                if (!residences.TryGetValue(slot.Zone, out string estate))
                    residences[slot.Zone] = estate = Pick(EstateWords, city.id * 7919L + slot.Zone) +
                                                     (modern ? Pick(EstateSuffixModern, slot.Zone) : Pick(EstateSuffixOld, slot.Zone));
                // 古代一块住宅地标一次坊名；近代每栋标楼号
                if (!modern)
                {
                    if (slot.Index != FirstHouse(plan, slot.Zone)) continue;
                    specs.Add(new Spec(slot.Tile, estate, HouseColor, HouseSize, false, 3));
                }
                else specs.Add(new Spec(slot.Tile, estate + (++houseNumber % 30 + 1) + Building(), HouseColor, HouseSize, false, 3));
                continue;
            }
            (string text, Color color) = BuildingName(icon, modern, cityName, seed);
            if (!string.IsNullOrEmpty(text)) specs.Add(new Spec(slot.Tile, text, color, BuildingSize, false, 1));
        }
        Cache[city.id] = (plan.Stamp, houses, specs);
        if (Cache.Count > 64) Cache.Remove(Cache.Keys.First());
        return specs;
    }

    private static int FirstHouse(PureMapPatch.LabelPlan plan, int zone) =>
        plan.Slots.Where(slot => slot.Zone == zone && slot.Icon.StartsWith("house_")).Select(slot => slot.Index)
            .DefaultIfEmpty(-1).Min();

    // 街格线：地块之间的边。横线按行、竖线按列，连成一段的建成地块中间标一个街名
    private static void Streets(City city, PureMapPatch.LabelPlan plan, bool modern, List<Spec> specs)
    {
        int size = Config.CITY_ZONE_SIZE;
        var rows = plan.Built.GroupBy(zone => zone.y).ToList();
        var columns = plan.Built.GroupBy(zone => zone.x).ToList();
        var usedNames = new HashSet<string>();
        foreach (var row in rows)
        {
            int x0 = row.Min(zone => zone.x), x1 = row.Max(zone => zone.x);
            if (x1 - x0 < 1 && row.Count() < 2) continue;
            string name = StreetName(city.id * 131L + row.Key, usedNames, modern ? StreetSuffixModernRow : StreetSuffixOldRow);
            specs.Add(new Spec(new Vector2((x0 + x1 + 1) * size / 2f, row.Key * size), name, StreetColor, StreetSize, false, 0));
        }
        foreach (var column in columns)
        {
            int y0 = column.Min(zone => zone.y), y1 = column.Max(zone => zone.y);
            if (y1 - y0 < 1 && column.Count() < 2) continue;
            string name = StreetName(city.id * 137L + column.Key + 7919L, usedNames,
                modern ? StreetSuffixModernColumn : StreetSuffixOldColumn);
            specs.Add(new Spec(new Vector2(column.Key * size, (y0 + y1 + 1) * size / 2f), name, StreetColor, StreetSize,
                Chinese, 0));
        }
    }

    private static string StreetName(long seed, HashSet<string> used, string[] suffixes)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string name = Pick(StreetWords, seed + attempt * 104729L) + Pick(suffixes, seed);
            if (used.Add(name)) return name;
        }
        return Pick(StreetWords, seed) + Pick(suffixes, seed + 1);
    }

    private static (string text, Color color) BuildingName(string icon, bool modern, string cityName, long seed)
    {
        string kind = icon.Split('_')[0];
        switch (kind)
        {
            case "civic":
            {
                string[] pool = modern ? CivicModern : CivicOld;
                string name = Pick(pool, seed);
                return (name.StartsWith("·") ? cityName + name.Substring(1) : name, CivicColor);
            }
            case "market":
                return (Pick(Elegant, seed) + Pick(modern ? MarketModern : MarketOld, seed + 3), MarketColor);
            case "industry":
                return (Pick(Elegant, seed) + Pick(modern || icon.EndsWith("1") || icon.EndsWith("2") ? FactoryModern : FactoryOld, seed + 5),
                    IndustryColor);
            case "temple":
                return (Pick(Elegant, seed) + Pick(TempleNames, seed + 7), TempleColor);
            case "barracks":
                return (cityName + Pick(modern ? BarracksModern : BarracksOld, seed), MilitaryColor);
            case "docks":
                return (cityName + Pick(modern ? DocksModern : DocksOld, seed), DockColor);
            default:
                return (null, Color.white);
        }
    }

    private static string CityName(City city)
    {
        string name = city.GetCityName() ?? "";
        return name.Length > 2 && Chinese ? name.Substring(0, 2) : name;
    }

    private static bool Chinese
    {
        get
        {
            try
            {
                string language = LocalizedTextManager.instance?.language ?? "cz";
                return language == "cz" || language == "ch";
            }
            catch
            {
                return true;
            }
        }
    }

    private static string Building() => Chinese ? "栋" : "";

    private static string Pick(string[] pool, long seed)
    {
        unchecked
        {
            ulong x = (ulong)seed * 0x9E3779B97F4A7C15UL;
            x ^= x >> 29;
            return pool[(int)(x % (ulong)pool.Length)];
        }
    }

    // ───────── 词库(中文界面用汉字，其余语言用英文) ─────────
    private static string[] StreetWords => Chinese ? StreetZh : StreetEn;
    private static string[] StreetSuffixOldRow => Chinese ? new[] { "街", "大街", "街" } : new[] { " Street", " Lane" };
    private static string[] StreetSuffixOldColumn => Chinese ? new[] { "巷", "胡同", "巷" } : new[] { " Alley", " Lane" };
    private static string[] StreetSuffixModernRow => Chinese ? new[] { "路", "大道", "路" } : new[] { " Road", " Avenue" };
    private static string[] StreetSuffixModernColumn => Chinese ? new[] { "街", "路", "大街" } : new[] { " Street", " Boulevard" };
    private static string[] EstateWords => Chinese ? EstateZh : EstateEn;
    private static string[] EstateSuffixOld => Chinese ? new[] { "坊", "里", "坊" } : new[] { " Ward", " Quarter" };
    private static string[] EstateSuffixModern => Chinese ? new[] { "花园", "小区", "家园", "新村", "公寓" } : new[] { " Gardens ", " Court ", " Heights " };
    private static string[] Elegant => Chinese ? EstateZh : EstateEn;
    private static string[] CivicOld => Chinese
        ? new[] { "·县衙", "·府衙", "学宫", "书院", "驿站", "官仓", "·察院", "·文庙" }
        : new[] { "Magistrate", "Academy", "Post Station", "Granary", "Court House" };
    private static string[] CivicModern => Chinese
        ? new[] { "·人民政府", "·人民法院", "·公安局", "·人民医院", "·第一中学", "·实验小学", "·图书馆", "·邮政局", "·税务局", "·博物馆" }
        : new[] { "City Hall", "Court", "Police Station", "General Hospital", "High School", "Primary School", "Library", "Post Office" };
    private static string[] MarketOld => Chinese ? new[] { "集市", "商铺", "酒楼", "茶肆", "布庄", "粮行" } : new[] { " Market", " Inn", " Tea House" };
    private static string[] MarketModern => Chinese
        ? new[] { "商场", "超市", "便利店", "百货", "购物中心", "饭店", "酒店" }
        : new[] { " Mall", " Supermarket", " Store", " Hotel" };
    private static string[] FactoryOld => Chinese ? new[] { "作坊", "窑", "染坊", "铁铺" } : new[] { " Workshop", " Kiln", " Forge" };
    private static string[] FactoryModern => Chinese ? new[] { "工厂", "机械厂", "纺织厂", "钢铁厂", "化工厂" } : new[] { " Works", " Mill", " Factory" };
    private static string[] TempleNames => Chinese ? new[] { "寺", "庙", "观", "祠", "庵" } : new[] { " Temple", " Shrine", " Chapel" };
    private static string[] BarracksOld => Chinese ? new[] { "军营", "校场", "卫所" } : new[] { " Barracks", " Garrison" };
    private static string[] BarracksModern => Chinese ? new[] { "军营", "武装部" } : new[] { " Barracks", " Armory" };
    private static string[] DocksOld => Chinese ? new[] { "码头", "渡口" } : new[] { " Wharf", " Ferry" };
    private static string[] DocksModern => Chinese ? new[] { "港", "客运码头" } : new[] { " Port", " Terminal" };

    private static readonly string[] StreetZh =
    {
        "建安", "平安", "雅云", "安邻", "学邻", "文治", "兴华", "长乐", "永宁", "通济", "崇文", "宣武", "光明", "和平", "人民",
        "新华", "朝阳", "东风", "胜利", "团结", "太平", "清平", "泰和", "广济", "惠民", "仁和", "德胜", "安定", "延寿", "永安",
        "崇仁", "怀德", "保宁", "宣化", "富春", "望江", "临河", "迎宾", "解放", "青年", "东大", "西大", "南门", "北门", "中山"
    };

    private static readonly string[] StreetEn =
    {
        "Elm", "Oak", "Maple", "Cedar", "Pine", "King", "Queen", "Church", "Market", "Mill", "River", "Bridge", "High", "Station",
        "Park", "Hill", "Garden", "Castle", "Harbor", "Victoria", "Union", "Liberty", "Spring", "Water", "Grove"
    };

    private static readonly string[] EstateZh =
    {
        "雪香", "风华", "金百合", "桃花", "石榴", "月亮湾", "枇杷", "都市桃源", "南湖", "锦绣", "翠苑", "丽景", "金桂", "碧水",
        "阳光", "幸福", "康乐", "平康", "安仁", "永和", "长寿", "怡和", "春晖", "紫荆", "梧桐", "海棠", "牡丹", "清风", "明月", "嘉禾"
    };

    private static readonly string[] EstateEn =
    {
        "Rose", "Willow", "Sunny", "Lake", "Meadow", "Golden", "Silver", "Green", "Orchard", "Ivy", "Laurel", "Briar", "Fern", "Clover"
    };
}
