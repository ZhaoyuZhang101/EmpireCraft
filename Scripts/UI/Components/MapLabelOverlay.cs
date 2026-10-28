using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 街区级标注(见 MapZoomTier ④，用户 2026-10-16 选定：建筑名、住宅小区名；街道名 2026-10-20 去掉)：
//   · 镜头拉到一格至少 6 个屏幕像素才标，只标镜头里离中心最近的几座城(与城市精细层一致)；
//   · 官署、市场、作坊、祭祀建筑、兵营、码头与住宅按本城主流文化取名(见 UrbanLabelNaming)，近代另有词库；
//   · 住宅：古代每块住宅地标一次街区名，近代每栋标住宅区名加栋号；再拉近(一格 10 像素以上)才标住宅；
//   · 名字按城、地块、街区编号算出来，同一处每次一样，不存档；布局、城名、文化或语言变更时更新缓存。
// 字是世界坐标里的 TextMesh，盖在所有地图图层之上；至多 160 条，对象池复用。
public static class MapLabelOverlay
{
    private const int MaxCities = 4, MaxLabels = 160;
    private const float HouseZoom = 10f;
    // 字高(格)
    private const float StreetSize = 0.9f, BuildingSize = 0.75f, HouseSize = 0.6f, CitySize = 1.4f, TownSize = 1f, VillageSize = 0.8f;
    // 字高封顶(屏幕像素，按 1080 行的屏幕算；用户 2026-10-16 "字太大了")：拉近时字不再跟着地图一起放大，
    // 像电子地图一样街名约 11 像素高，建筑、住宅名按比例小一些(至少 9 像素)；拉远到不足这个高度时照旧按格算
    private const float StreetPixels = 11f, MinPixels = 9f;
    private const float BackgroundPaddingX = 0.25f, BackgroundPaddingY = 0.14f;

    private sealed class Label
    {
        public GameObject Object;
        public TextMesh Text;
        public MeshRenderer Renderer;
        public SpriteRenderer Background;
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
        public readonly bool Backdrop;

        public Spec(Vector2 tile, string text, Color color, float size, bool vertical, int priority, bool backdrop = false)
        {
            Tile = tile;
            Text = text;
            Color = color;
            Size = size;
            Vertical = vertical;
            Priority = priority;
            Backdrop = backdrop;
        }
    }

    private static readonly List<Label> Pool = new();
    private static readonly Dictionary<long, (int stamp, bool houses, bool drawn, string naming, List<Spec> specs)> Cache = new();
    private static Font _font;
    private static Sprite _backgroundSprite;
    private static object _world;

    private static readonly Color CivicColor = new(0.16f, 0.36f, 0.66f),
        MarketColor = new(0.86f, 0.42f, 0.10f), IndustryColor = new(0.45f, 0.36f, 0.30f), TempleColor = new(0.52f, 0.30f, 0.62f),
        MilitaryColor = new(0.40f, 0.46f, 0.30f), DockColor = new(0.10f, 0.50f, 0.66f), HouseColor = new(0.42f, 0.42f, 0.44f),
        CityColor = new(0.16f, 0.16f, 0.20f), SettlementColor = new(0.98f, 0.95f, 0.86f), RuinColor = new(0.82f, 0.82f, 0.80f);

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
        if (source?.sprite == null || camera == null || !source.enabled || !PureMapMode.Enabled ||
            EmpireCraft.Scripts.Layer.CityViewMap.Active || ppt < MapZoomTier.Street)
        {
            HideAll();
            return;
        }
        HandleClick();
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
        float screenScale = Mathf.Max(0.75f, Screen.height / 1080f);
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
            float cap = Mathf.Max(MinPixels, StreetPixels * spec.Size / StreetSize) * screenScale / Mathf.Max(1f, ppt);
            label.Object.transform.localScale = Vector3.one * (tileWorld * Mathf.Min(spec.Size, cap));
            label.Renderer.sortingLayerID = source.sortingLayerID;
            label.Renderer.sortingOrder = source.sortingOrder + 6;
            if (!label.Object.activeSelf) label.Object.SetActive(true);
            UpdateBackground(label, source, spec.Backdrop && !string.IsNullOrEmpty(text));
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

    // 村镇名用浅字配半透明黑底；底板按文字的本地包围盒留边，跟随文字一起缩放、隐藏并复用。
    private static void UpdateBackground(Label label, SpriteRenderer source, bool shown)
    {
        if (!shown)
        {
            if (label.Background != null) label.Background.enabled = false;
            return;
        }
        if (label.Background == null)
        {
            _backgroundSprite ??= Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);
            var background = new GameObject("EmpireCraftMapLabelBackground");
            background.transform.SetParent(label.Object.transform, false);
            label.Background = background.AddComponent<SpriteRenderer>();
            label.Background.sprite = _backgroundSprite;
            label.Background.color = new Color(0f, 0f, 0f, 0.58f);
        }
        Bounds bounds = label.Renderer.localBounds;
        label.Background.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, 0f);
        label.Background.transform.localScale = new Vector3(bounds.size.x + BackgroundPaddingX * 2f,
            bounds.size.y + BackgroundPaddingY * 2f, 1f);
        label.Background.sortingLayerID = source.sortingLayerID;
        label.Background.sortingOrder = label.Renderer.sortingOrder - 1;
        label.Background.enabled = true;
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
        UrbanLabelNaming.Names names = UrbanLabelNaming.For(city);
        List<PureMapPatch.DetailLabel> drawn = PureMapPatch.DrawnLabelsOf(city);
        if (Cache.TryGetValue(city.id, out var cached) && cached.stamp == stamp && cached.houses == houses &&
            cached.drawn == (drawn != null) && cached.naming == names.Key) return cached.specs;
        // 精细图画到哪里就标到哪里(见 PureMapPatch.DetailLabel)；这座城画精细图、只是还没画好的，先不标，免得字和画面对不上
        if (drawn != null)
        {
            List<Spec> fromDrawing = SpecsOfDrawing(city, drawn, houses, names);
            Settlements(city, PureMapPatch.LabelsOf(city), fromDrawing);
            Cache[city.id] = (stamp, houses, true, names.Key, fromDrawing);
            if (Cache.Count > 64) Cache.Remove(Cache.Keys.First());
            return fromDrawing;
        }
        if (PureMapPatch.DetailEligible(city)) return new List<Spec>();
        PureMapPatch.LabelPlan plan = PureMapPatch.LabelsOf(city);
        if (plan == null) return new List<Spec>();
        var specs = new List<Spec>();
        bool modern = plan.Modern >= 0.5f;
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
                    residences[slot.Zone] = estate = names.Residence(modern, city.id * 7919L + slot.Zone);
                // 古代一块住宅地标一次坊名；近代每栋标楼号
                if (!modern)
                {
                    if (slot.Index != FirstHouse(plan, slot.Zone)) continue;
                    specs.Add(new Spec(slot.Tile, estate, HouseColor, HouseSize, false, 3));
                }
                else specs.Add(new Spec(slot.Tile, names.Numbered(estate, ++houseNumber), HouseColor, HouseSize, false, 3));
                continue;
            }
            (string text, Color color) = BuildingName(icon, modern, names, seed);
            if (!string.IsNullOrEmpty(text)) specs.Add(new Spec(slot.Tile, text, color, BuildingSize, false, 1));
        }
        Settlements(city, plan, specs);
        Cache[city.id] = (plan.Stamp, houses, false, names.Key, specs);
        if (Cache.Count > 64) Cache.Remove(Cache.Keys.First());
        return specs;
    }

    // 按精细图实际画出来的街、大道、房子标注
    private static List<Spec> SpecsOfDrawing(City city, List<PureMapPatch.DetailLabel> drawn, bool houses, UrbanLabelNaming.Names names)
    {
        var specs = new List<Spec>();
        bool modern = PureMapPatch.LabelsOf(city)?.Modern >= 0.5f;
        var residences = new Dictionary<long, string>();
        int houseNumber = 0;
        foreach (PureMapPatch.DetailLabel label in drawn.OrderBy(label => label.Key))
        {
            // 街道不标名
            if (label.Icon is "street" or "avenue") continue;
            long zone = label.Key / 4;
            if (label.Icon.StartsWith("house_"))
            {
                if (!houses) continue;
                if (!residences.TryGetValue(zone, out string estate))
                    residences[zone] = estate = names.Residence(modern, city.id * 7919L + zone);
                specs.Add(new Spec(label.Tile, modern ? names.Numbered(estate, ++houseNumber) : estate, HouseColor,
                    HouseSize, false, 3));
                continue;
            }
            (string text, Color color) = BuildingName(label.Icon, modern, names, city.id * 1000003L + label.Key);
            if (!string.IsNullOrEmpty(text)) specs.Add(new Spec(label.Tile, text, color, BuildingSize, false, 1));
        }
        return specs;
    }

    // 城名(用户 2026-10-16 "周边的村子还有中间的城似乎也没名字")：城北沿上方标城名；城外的镇、村按所在城的文化起名
    // (用户 2026-10-19：姓氏、地势、字库几种方式，文化配置可绑独有字库，见 SettlementNaming)。同城不重名。
    // 镇村的名字头一回标注时按位置起，记进存档(见 RuralSettlementSystem)，从此不随先后、归属变；废墟标"某某·废墟"
    private static void Settlements(City city, PureMapPatch.LabelPlan plan, List<Spec> specs)
    {
        if (plan == null) return;
        if (!float.IsNaN(plan.NameAt.x)) specs.Add(new Spec(plan.NameAt, city.GetCityName() ?? "", CityColor, CitySize, false, 0));
        var used = new HashSet<string>(RuralSettlementSystem.InCity(city).Select(settlement => settlement.name)
            .Where(name => !string.IsNullOrEmpty(name)));
        for (int i = 0; i < plan.Towns.Count; i++)
        {
            string name = NameOf(RuralSettlementSystem.At(plan.TownTiles[i].x, plan.TownTiles[i].y), true, city, used,
                city.id * 7349L + i * 131L + 17L);
            specs.Add(new Spec(plan.Towns[i].at, name, SettlementColor, TownSize, false, 1, backdrop: true));
        }
        for (int i = 0; i < plan.Villages.Count; i++)
        {
            string name = NameOf(RuralSettlementSystem.At(plan.VillageTiles[i].x, plan.VillageTiles[i].y), false, city, used,
                city.id * 9973L + i * 257L + 29L);
            specs.Add(new Spec(plan.Villages[i], name, SettlementColor, VillageSize, false, 2, backdrop: true));
        }
        foreach ((Vector2 at, int x, int y) in plan.Forts)
        {
            RuralSettlement settlement = RuralSettlementSystem.At(x, y);
            if (settlement == null) continue;
            specs.Add(new Spec(at, NameOf(settlement, settlement.town, city, used, 0L), SettlementColor, TownSize, false, 1, backdrop: true));
        }
        foreach ((Vector2 at, int x, int y) in plan.Ruins)
        {
            RuralSettlement settlement = RuralSettlementSystem.At(x, y);
            if (settlement == null) continue;
            string name = NameOf(settlement, settlement.town, city, used, 0L);
            specs.Add(new Spec(at, string.Format(NeoModLoader.General.LM.Get("settlement_ruin_label"), name), RuinColor,
                settlement.town ? TownSize : VillageSize, false, 2, backdrop: true));
        }
    }

    // 镇村的名字：存档里有就用，没有就按所在城的文化起一个记下(姓氏、地势、字库，见 SettlementNaming)——
    // 种子按原来的"城与编号"(镇村按生成先后排)，没有编号的(废墟、点开的)按位置起
    private static string NameOf(RuralSettlement settlement, bool town, City city, HashSet<string> used, long fallback) =>
        SettlementNaming.Name(settlement, city, town, used, fallback);

    // 点开镇村(没选神力时点一下；拖动地图不算)：打开村镇小窗口(改名、升降级、换个样子、毁弃，见 SettlementWindow)
    private static Vector3 _pressedAt;
    private static bool _pressed;

    private static void HandleClick()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _pressedAt = Input.mousePosition;
            _pressed = !World.world.isOverUI();
            return;
        }
        if (!Input.GetMouseButtonUp(0) || !_pressed) return;
        _pressed = false;
        if ((Input.mousePosition - _pressedAt).sqrMagnitude > 64f || World.world.isOverUI() || World.world.isAnyPowerSelected()) return;
        WorldTile clicked = World.world.getMouseTilePos();
        // 点到厂区：打开厂区小窗口(见 FactoryWindow)
        FactorySite factory = FactorySystem.At(clicked);
        if (factory != null)
        {
            EmpireCraft.Scripts.UI.Windows.FactoryWindow.Open(factory);
            return;
        }
        RuralSettlement settlement = RuralSettlementSystem.Near(clicked);
        if (settlement == null)
        {
            // 点到主城的城区：打开城池小窗口(同村镇小窗口，见 CityStyleWindow；用户 2026-10-28 转群友"可以给主城设置一个界面吗")；
            // 纯净地图开着时改开城池全图(用户 2026-10-28 "窗口全屏，整座城显示完整"，见 CityPlanScreen)
            City urban = clicked?.zone?.city;
            if (urban?.data != null && !urban.isRekt() && PureMapPatch.UrbanTile(clicked))
            {
                if (PureMapMode.Enabled) EmpireCraft.Scripts.UI.Windows.CityPlanScreen.Open(urban);
                else EmpireCraft.Scripts.UI.Windows.CityStyleWindow.Open(urban);
            }
            return;
        }
        // 名字头一回标注以前就点开的：先按标注的规矩起好(见 NameOf)
        City city = World.world.GetTile(settlement.x, settlement.y)?.zone?.city;
        bool huaxia = city != null && PureMapPatch.LabelsOf(city)?.Family == "huaxia";
        var used = new HashSet<string>(city == null ? Enumerable.Empty<string>()
            : RuralSettlementSystem.InCity(city).Select(other => other.name).Where(name => !string.IsNullOrEmpty(name)));
        NameOf(settlement, settlement.town, city, used, 0L);
        // 打开村镇小窗口(见 SettlementWindow)
        EmpireCraft.Scripts.UI.Windows.SettlementWindow.Open(settlement);
    }

    private static int FirstHouse(PureMapPatch.LabelPlan plan, int zone) =>
        plan.Slots.Where(slot => slot.Zone == zone && slot.Icon.StartsWith("house_")).Select(slot => slot.Index)
            .DefaultIfEmpty(-1).Min();

    private static (string text, Color color) BuildingName(string icon, bool modern, UrbanLabelNaming.Names names, long seed)
    {
        Color color = icon.Split('_')[0] switch
        {
            "civic" or "palace" => CivicColor,
            "market" => MarketColor,
            "industry" => IndustryColor,
            "temple" => TempleColor,
            "barracks" => MilitaryColor,
            "docks" => DockColor,
            _ => Color.white
        };
        return (names.Building(icon, modern, seed), color);
    }
}
