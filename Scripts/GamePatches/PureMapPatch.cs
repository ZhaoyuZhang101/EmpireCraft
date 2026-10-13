using System;
using System.Collections.Generic;
using System.Linq;
using ai;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 纯净地图模式(见 PureMapMode)：
//   · 国家色块完全不透明：原版近看时国家内部画成透明、只留边框，内部颜色也是半透明的；
//     这里任何缩放都填满，颜色不透明(透明的清除色不动，否则空白处会变成白块)；
//     没选地图图层时默认显示国家图层、关了"地图图层"也照样显示；图层不随地图不透明度设置变淡
//     (入夜也不变淡、不被夜色压暗，见 NightMapLayerPatch；云照旧)；
//   · 地形浮雕：色块不透明也看得出地形——掺一点地表本色(沙漠、雪原、草原、沼泽)，山地、丘陵压暗，
//     按与西北邻格的高差轻轻打光，林地略暗；块里的海、河、湖画成干净的蓝，海岸线是真实的形状；
//   · 城市平面图(仿电子地图，位置是模拟的)：建筑照常作为数据运作，地图上按每座城的建筑数据生成一张平面图——
//     城里每个地块按固定街格(白线)切成四个街区，每个街区放一座建筑的 3×3 图块(自己画的，不靠原版或兼容模组的贴图，
//     见 Tools/IconForge/citymap_forge.py)，按本文化科技分代：住宅从茅屋到摩天楼、生态城，工业从作坊到现代工厂，
//     商业集市→商场，官署衙署→现代政府大楼；没装工厂模组时虚拟工厂(见 VirtualIndustrySystem)也画出来；
//     民居进居住区、工厂矿场进工业区、市场进商业区、兵营进军事区、官署寺庙进城心，没规划的地块从城心往外按圈放，
//     城越大城心的楼越高，码头放临水的地块；农田地块画淡绿条垄；
//     城与城之间(同一片陆地、相邻)连黄色干道。本模式下不再实际铺路，地上已有的路格也不画出来。
//     建筑与规划会变，每秒轮流重画一批城的地块，约十秒全图刷新一遍；
//   · 国家图层下，帝国与外国接壤的陆上边界画成金色，帝国内部各成员之间照旧；海岸、湖泊、无主荒地不描金
//     (原来连这些也描，整片海岸线和境内每块荒地周围都是金线，看不出哪里才是国界)；
//   · 人物要去哪里直接到位、不走路(地图上也不画人物)：原版的行为照常完成(回家、上任、相会生子)，不再寻路；
//     鼠标附近只认得船，悬停与点击都落到城市、国家上；除船以外的人物逐帧设为不可见；
//   · 纯装饰建筑(雕像、训练木桩、花园、喷泉)建造上限为 0，城市不再建；
//   · 建筑不画，城市铭牌上的种族头像换成城市性质徽记、文字后写民居等级(见 CityDevelopment)
public class PureMapPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(PureMapPatch));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "shouldBeClearColor"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeShouldBeClearColor)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeApplyColors)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZoneFull"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeApplyColorsFull)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "colorZone"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeColorZone)));
        harmony.Patch(AccessTools.Method(typeof(ActorMove), nameof(ActorMove.goTo)),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeGoTo)));
        harmony.Patch(AccessTools.Method(typeof(MapBox), nameof(MapBox.getActorNearCursor)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterActorNearCursor)));
        harmony.Patch(AccessTools.Method(typeof(BatchActors), "updateVisibility"),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterUpdateVisibility)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getLimitOfBuildingsType)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterBuildingLimit)));
        harmony.Patch(AccessTools.Method(typeof(Zones), nameof(Zones.getMapMetaAsset)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterMapMetaAsset)));
        harmony.Patch(AccessTools.Method(typeof(Zones), nameof(Zones.showMapBorders)),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeShowMapBorders)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), nameof(ZoneCalculator.updateAnimationsAndSelections)),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterZoneAnimations)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterApplyColors)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZoneFull"),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterApplyColorsFull)));
        harmony.Patch(AccessTools.Method(typeof(MapAction), nameof(MapAction.makeRoadBetween)),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeMakeRoad)));
    }

    private static MetaTypeAsset _kingdomLayer;

    // 没选地图图层时默认显示国家图层
    private static void AfterMapMetaAsset(ref MetaTypeAsset __result)
    {
        if (!PureMapMode.Enabled || __result != null) return;
        _kingdomLayer ??= AssetManager.meta_type_library.list.Find(asset =>
            asset.map_mode == MetaType.Kingdom && !string.IsNullOrEmpty(asset.option_id));
        __result = _kingdomLayer;
    }

    private static bool BeforeShowMapBorders(ref bool __result)
    {
        if (!PureMapMode.Enabled) return true;
        __result = true;
        return false;
    }

    // 图层不随地图不透明度设置变淡(亮度照原版；夜色见 NightMapLayerPatch)
    private static void AfterZoneAnimations(ZoneCalculator __instance)
    {
        if (!PureMapMode.Enabled || __instance.sprRnd == null) return;
        Color color = __instance.sprRnd.color;
        color.a = 1f;
        __instance.sprRnd.color = color;
        RefreshUrbanSlice();
    }

    // 城市肌理随建筑、规划变化：每秒让一批城的地块下次重画(原版只在归属、颜色变了才重画)
    private const float UrbanRefreshSeconds = 1f;
    private const int UrbanRefreshRounds = 10;
    private static float _nextUrbanRefresh;
    private static int _urbanCursor;

    private static void RefreshUrbanSlice()
    {
        float now = Time.unscaledTime;
        if (now < _nextUrbanRefresh && _nextUrbanRefresh - now <= UrbanRefreshSeconds) return;
        _nextUrbanRefresh = now + UrbanRefreshSeconds;
        var cities = World.world?.cities?.list;
        if (cities == null || cities.Count == 0) return;
        int slice = Mathf.Max(1, Mathf.CeilToInt(cities.Count / (float)UrbanRefreshRounds));
        for (int i = 0; i < slice; i++)
        {
            City city = cities[(_urbanCursor + i) % cities.Count];
            if (city?.zones == null || city.isRekt()) continue;
            // 本模式下不实际铺路
            city.road_tiles_to_build.Clear();
            foreach (TileZone zone in city.zones) zone?.resetRenderHelpers();
        }
        _urbanCursor = (_urbanCursor + slice) % cities.Count;
        if (now >= _nextHighways || _nextHighways - now > HighwaySeconds) RebuildHighways(now);
    }

    private static readonly Color32 EmpireGold = new(232, 184, 64, 255);

    // 帝国对外的边画成金色(纯净地图模式，国家图层)：原版画完这块地后，把对面不是本帝国的那几条边重新涂一遍。
    // 战国的周室(王畿连同离开帝国的诸侯)在国家图层和帝国图层都画，而且加粗一格，用来看出是不是同一个势力
    private static void AfterApplyColors(ZoneCalculator __instance, TileZone pZone, bool pUp, bool pDown, bool pLeft, bool pRight)
    {
        if (pZone?.tiles == null || __instance.pixels == null) return;
        if (PureMapMode.Enabled) ShadeTerrain(__instance.pixels, pZone, pUp, pDown, pLeft, pRight);
        Kingdom kingdom = pZone.city?.kingdom;
        MetaType? mode = World.world.getCachedMapMetaAsset()?.map_mode;
        bool kingdomLayer = mode == MetaType.Kingdom, empireLayer = mode == MetaTypeExtension.Empire;
        if (kingdom == null || !kingdomLayer && !empireLayer) return;
        Empire sphere = ZhouEraSystem.SphereOf(kingdom);
        bool bold = sphere != null && ZhouEraSystem.Era(sphere) >= ZhouEraSystem.Warring;
        Empire empire = bold ? sphere : PureMapMode.Enabled && kingdomLayer ? kingdom.GetEmpire() : null;
        if (empire == null) return;
        bool up = pUp && Outside(pZone.zone_up, empire), down = pDown && Outside(pZone.zone_down, empire);
        bool left = pLeft && Outside(pZone.zone_left, empire), right = pRight && Outside(pZone.zone_right, empire);
        if (!up && !down && !left && !right) return;
        Color32[] pixels = __instance.pixels;
        foreach (WorldTile tile in pZone.tiles)
        {
            WorldTileZoneBorder border = tile?.world_tile_zone_border;
            if (border == null || !border.border) continue;
            bool onUp = up && border.border_up, onDown = down && border.border_down;
            bool onLeft = left && border.border_left, onRight = right && border.border_right;
            if (!onUp && !onDown && !onLeft && !onRight) continue;
            pixels[tile.data.tile_id] = EmpireGold;
            if (!bold) continue;
            // 加粗：往里再涂一格
            Paint(pixels, onUp ? tile.tile_down : null, pZone);
            Paint(pixels, onDown ? tile.tile_up : null, pZone);
            Paint(pixels, onLeft ? tile.tile_right : null, pZone);
            Paint(pixels, onRight ? tile.tile_left : null, pZone);
        }
    }

    private static void AfterApplyColorsFull(ZoneCalculator __instance, TileZone pZone)
    {
        if (PureMapMode.Enabled && pZone?.tiles != null && __instance.pixels != null)
            ShadeTerrain(__instance.pixels, pZone, false, false, false, false);
    }

    // ───────── 地形浮雕 ─────────
    // 掺地表本色的比例；块里的水掺水色的比例；高差打光的强度与上限
    private const float TerrainTint = 0.1f, WaterTint = 0.85f, SlopeLight = 0.003f, SlopeCap = 0.06f;
    private static readonly Color32 Water = new(140, 185, 235, 255);
    private static readonly global::System.Collections.Generic.Dictionary<TileTypeBase, float> TypeLight = new();

    // 色块里每一格按地形调色；边框像素(原版按 pUp…pRight 画的外框)不动，保持清楚
    private static void ShadeTerrain(Color32[] pixels, TileZone zone, bool up, bool down, bool left, bool right)
    {
        City city = zone.city != null && !zone.city.isRekt() ? zone.city : null;
        ZoneUse use = city != null ? ZonePlanSystem.Get(city, zone) : ZoneUse.None;
        CityPlan plan = city != null ? PlanFor(city) : null;
        Slot[] slots = plan != null && plan.zones.TryGetValue(zone.id, out Slot[] found) ? found : null;
        int ox = zone.x * Config.CITY_ZONE_SIZE, oy = zone.y * Config.CITY_ZONE_SIZE;
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile?.data?.tile_id ?? -1;
            if (id < 0 || id >= pixels.Length || pixels[id].a == 0) continue;
            WorldTileZoneBorder border = tile.world_tile_zone_border;
            if (border != null && border.border && (up && border.border_up || down && border.border_down ||
                                                    left && border.border_left || right && border.border_right)) continue;
            Color32 shaded = Shade(tile, pixels[id]);
            pixels[id] = Plan(tile, shaded, use, slots, tile.x - ox, tile.y - oy);
        }
    }

    // ───────── 城市平面图(位置是模拟的) ─────────
    private enum Kind { House, Industry, Market, Temple, Barracks, Docks, Civic }

    private struct Slot
    {
        public string icon;
        public bool used;
    }

    private sealed class CityPlan
    {
        public float at;
        public readonly Dictionary<int, Slot[]> zones = new();
    }

    private const float PlanSeconds = 5f, HighwaySeconds = 30f;
    private static readonly Dictionary<long, CityPlan> Plans = new();
    private static readonly HashSet<int> Highways = new();
    private static float _nextHighways;

    private static readonly Color32 Block = new(236, 232, 224, 255), Street = new(255, 255, 255, 255),
        Highway = new(255, 210, 110, 255), Crop = new(228, 238, 200, 255);

    // 住房技术(科技树里的八项)，研究到第几项住宅就画到第几代
    private static readonly string[] HousingTechs =
    {
        "rammed_earth_housing", "tiled_housing", "courtyard_housing", "storied_housing", "ward_housing",
        "urban_housing", "skyscrapers", "arcology"
    };
    // 城心多少户一栋最高代的楼(之外一圈低一代，再外低两代)
    private const int HouseholdsPerCoreHouse = 60;
    private const int MaxCivic = 2;

    // 图块像素(行自上而下)：读 ui/citymap/<名>.png，读不到为 null(街区只铺底色)
    private static readonly Dictionary<string, Color32[]> IconPixels = new();

    private static Color32[] Icon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (IconPixels.TryGetValue(name, out Color32[] pixels)) return pixels;
        pixels = null;
        try
        {
            Sprite sprite = SpriteTextureLoader.getSprite("ui/citymap/" + name);
            if (sprite?.texture != null)
            {
                pixels = new Color32[9];
                Rect rect = sprite.textureRect;
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                        pixels[row * 3 + col] = sprite.texture.GetPixel((int)rect.x + col, (int)rect.y + 2 - row);
            }
        }
        catch
        {
            pixels = null;
        }
        IconPixels[name] = pixels;
        return pixels;
    }

    private static CityPlan PlanFor(City city)
    {
        float now = Time.unscaledTime;
        if (Plans.TryGetValue(city.data.id, out CityPlan cached) && now >= cached.at && now - cached.at < PlanSeconds) return cached;
        var plan = new CityPlan { at = now };
        if (Plans.Count > 4000) Plans.Clear();
        Plans[city.data.id] = plan;
        string culture = TechnologySystem.GetCultureOf(city) ?? "";
        bool Has(string tech) => !string.IsNullOrEmpty(culture) && TechnologySystem.HasTech(culture, tech);
        int housing = 1;
        for (int i = 0; i < HousingTechs.Length; i++) if (Has(HousingTechs[i])) housing = i + 1;
        int industryEra = Has("electricity") ? 2 : Has("industrialization") ? 1 : 0;
        int modern = Has("electricity") ? 1 : 0;
        // 各类建筑数(按数据)；没装工厂模组时的虚拟工厂也算
        var counts = new Dictionary<Kind, int>();
        foreach (Kind kind in Enum.GetValues(typeof(Kind))) counts[kind] = 0;
        foreach (Building building in city.buildings ?? new List<Building>())
        {
            if (building?.asset == null || building.isRekt() || building.isUnderConstruction() ||
                building.asset.building_type != BuildingType.Building_Civ || PureMapMode.IsDecor(building.asset)) continue;
            counts[KindOf(building.asset)]++;
        }
        counts[Kind.Industry] += VirtualIndustrySystem.Factories(city);
        counts[Kind.Civic] = Mathf.Min(counts[Kind.Civic], MaxCivic);
        // 排队：民居从最高代排起(城心先放)，城越大最高代的楼越多
        var queues = new Dictionary<Kind, Queue<string>>();
        foreach (Kind kind in Enum.GetValues(typeof(Kind))) queues[kind] = new Queue<string>();
        int core = Mathf.Max(1, CityPopulationSystem.Households(city) / HouseholdsPerCoreHouse);
        for (int k = 0; k < counts[Kind.House]; k++)
        {
            int tier = k < core ? housing : k < core * 3 ? housing - 1 : housing - 2;
            queues[Kind.House].Enqueue("house_" + Mathf.Clamp(tier, 1, HousingTechs.Length));
        }
        for (int k = 0; k < counts[Kind.Industry]; k++) queues[Kind.Industry].Enqueue("industry_" + industryEra);
        for (int k = 0; k < counts[Kind.Market]; k++) queues[Kind.Market].Enqueue("market_" + modern);
        for (int k = 0; k < counts[Kind.Civic]; k++) queues[Kind.Civic].Enqueue("civic_" + modern);
        for (int k = 0; k < counts[Kind.Temple]; k++) queues[Kind.Temple].Enqueue("temple");
        for (int k = 0; k < counts[Kind.Barracks]; k++) queues[Kind.Barracks].Enqueue("barracks");
        for (int k = 0; k < counts[Kind.Docks]; k++) queues[Kind.Docks].Enqueue("docks");
        TileZone centerZone = city.getTile()?.zone;
        List<TileZone> zones = city.zones.Where(zone => zone != null).OrderBy(zone => centerZone == null ? 0
            : (zone.x - centerZone.x) * (zone.x - centerZone.x) + (zone.y - centerZone.y) * (zone.y - centerZone.y)).ToList();
        // 第一轮：各地块按规划先放对口的建筑；码头先放临水的地块
        foreach (TileZone zone in zones)
        {
            ZoneUse use = ZonePlanSystem.Get(city, zone);
            if (use == ZoneUse.Farm) continue;
            bool water = zone.tiles.Any(tile => tile?.Type?.liquid == true);
            Kind[] wants = zone == centerZone ? new[] { Kind.Civic, Kind.Temple, Kind.Market, Kind.House }
                : water ? new[] { Kind.Docks }
                : use switch
                {
                    ZoneUse.Residential => new[] { Kind.House },
                    ZoneUse.Industry => new[] { Kind.Industry },
                    ZoneUse.Commerce => new[] { Kind.Market, Kind.Civic },
                    ZoneUse.Military => new[] { Kind.Barracks },
                    _ => Array.Empty<Kind>()
                };
            Fill(plan, zone, queues, wants);
        }
        // 第二轮：剩下的从城心往外按圈放进空着的街区(农田除外)，民居优先
        Kind[] order = { Kind.House, Kind.Civic, Kind.Market, Kind.Temple, Kind.Industry, Kind.Barracks, Kind.Docks };
        foreach (TileZone zone in zones)
        {
            if (queues.Values.All(queue => queue.Count == 0)) break;
            if (ZonePlanSystem.Get(city, zone) == ZoneUse.Farm) continue;
            Fill(plan, zone, queues, order);
        }
        return plan;
    }

    private static void Fill(CityPlan plan, TileZone zone, Dictionary<Kind, Queue<string>> queues, Kind[] wants)
    {
        if (wants.Length == 0) return;
        if (!plan.zones.TryGetValue(zone.id, out Slot[] slots)) slots = new Slot[4];
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].used) continue;
            Kind? pick = wants.Cast<Kind?>().FirstOrDefault(kind => queues[kind.Value].Count > 0);
            if (pick == null) break;
            slots[i] = new Slot { icon = queues[pick.Value].Dequeue(), used = true };
            changed = true;
        }
        if (changed) plan.zones[zone.id] = slots;
    }

    private static Kind KindOf(BuildingAsset asset)
    {
        if (TechnologySystem.IsFactory(asset) || IndustryBuildingSystem.IsMine(asset) || IndustryBuildingSystem.IsLumber(asset))
            return Kind.Industry;
        return asset.type switch
        {
            "type_house" => Kind.House,
            "type_market" => Kind.Market,
            "type_temple" => Kind.Temple,
            "type_barracks" => Kind.Barracks,
            "type_docks" => Kind.Docks,
            _ => Kind.Civic
        };
    }

    // 一格：有建筑的地块画街格与街区里的建筑图块；农田画条垄；其余照地形，干道压在上面
    private static Color32 Plan(WorldTile tile, Color32 color, ZoneUse use, Slot[] slots, int lx, int ly)
    {
        if (slots != null && lx >= 0 && lx < 8 && ly >= 0 && ly < 8)
        {
            if (lx % 4 == 0 || ly % 4 == 0) return Lerp(color, Street, 0.6f);
            int px = (lx - 1) % 4, py = (ly - 1) % 4;
            Slot slot = slots[(ly - 1) / 4 * 2 + (lx - 1) / 4];
            Color32 baseColor = Lerp(color, Block, 0.3f);
            Color32[] icon = slot.used ? Icon(slot.icon) : null;
            if (icon == null) return slot.used ? Lerp(color, Block, 0.55f) : baseColor;
            Color32 pixel = icon[(2 - py) * 3 + px];
            return pixel.a == 0 ? baseColor : Lerp(baseColor, pixel, 0.9f);
        }
        if (Highways.Contains(tile.data.tile_id)) return Lerp(color, Highway, 0.7f);
        if (use == ZoneUse.Farm) return Lerp(color, Crop, tile.y % 2 == 0 ? 0.2f : 0.06f);
        return color;
    }

    // 干道：同一片陆地上相邻的城，城心之间连直线，跨水的不连
    private static void RebuildHighways(float now)
    {
        _nextHighways = now + HighwaySeconds;
        Highways.Clear();
        var line = new List<int>();
        foreach (City city in World.world.cities.list)
        {
            WorldTile from = city?.getTile();
            if (from == null || city.isRekt()) continue;
            foreach (City other in city.neighbours_cities)
            {
                WorldTile to = other?.getTile();
                if (to == null || other.isRekt() || other.data.id <= city.data.id || !from.isSameIsland(to)) continue;
                line.Clear();
                if (Trace(from, to, line)) Highways.UnionWith(line);
            }
        }
    }

    private static bool Trace(WorldTile from, WorldTile to, List<int> line)
    {
        int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            WorldTile tile = World.world.GetTile(x0, y0);
            if (tile == null || tile.Type?.liquid == true) return false;
            line.Add(tile.data.tile_id);
            if (x0 == x1 && y0 == y1) return true;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // 本模式下不实际铺路(原版建筑落成时排的连路)
    private static bool BeforeMakeRoad() => !PureMapMode.Enabled;

    private static Color32 Shade(WorldTile tile, Color32 color)
    {
        TileTypeBase type = tile.Type;
        if (type == null) return color;
        if (type.lava) return Lerp(color, type.color, WaterTint);
        if (type.liquid) return Lerp(color, Water, WaterTint);
        // 路格(以前实际铺下的路)当普通地面，不掺路的颜色
        TileTypeBase surface = type.road ? null : tile.top_type != null ? tile.top_type : type;
        float light = Light(type);
        if (tile.building?.asset != null && tile.building.asset.building_type == BuildingType.Building_Tree) light *= 0.94f;
        int height = tile.Height;
        int north = tile.tile_up?.Height ?? height, west = tile.tile_left?.Height ?? height;
        light += Mathf.Clamp((height * 2 - north - west) * SlopeLight, -SlopeCap, SlopeCap);
        return Scale(surface == null ? color : Lerp(color, surface.color, TerrainTint), light);
    }

    // 各地形的明暗：峰顶、山地、丘陵压暗，沙地、雪地提亮
    private static float Light(TileTypeBase type)
    {
        if (TypeLight.TryGetValue(type, out float light)) return light;
        string id = type.id ?? "";
        light = type.summit ? 0.66f : type.mountains ? 0.76f : id.Contains("hills") ? 0.88f : type.sand ? 1.05f : 1f;
        if (id.Contains("snow")) light += 0.1f;
        TypeLight[type] = light;
        return light;
    }

    private static Color32 Lerp(Color32 a, Color32 b, float t) => new(
        (byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), a.a);

    private static Color32 Scale(Color32 color, float light) => new(
        (byte)Mathf.Clamp(color.r * light, 0f, 255f), (byte)Mathf.Clamp(color.g * light, 0f, 255f),
        (byte)Mathf.Clamp(color.b * light, 0f, 255f), color.a);

    private static void Paint(Color32[] pixels, WorldTile tile, TileZone zone)
    {
        if (tile?.data == null || tile.zone != zone || tile.data.tile_id < 0 || tile.data.tile_id >= pixels.Length) return;
        pixels[tile.data.tile_id] = EmpireGold;
    }

    // 对面是别的国家、且不是同一势力：周室按 SphereOf 算(王畿、郡与离开帝国的诸侯是一家)，其余按帝国。
    // 对面是海、湖或无主地不算，那里本来就看得出疆域
    private static bool Outside(TileZone zone, Empire empire)
    {
        Kingdom other = zone?.city?.kingdom;
        if (other == null) return false;
        return (ZhouEraSystem.SphereOf(other) ?? other.GetEmpire()) != empire;
    }

    // 鼠标只认得船
    private static void AfterActorNearCursor(ref Actor __result)
    {
        if (PureMapMode.Enabled && __result != null && !PureMapMode.Visible(__result)) __result = null;
    }

    // 原版逐帧算单位可见与否：除船以外一律不可见(不画、不做动画)
    private static void AfterUpdateVisibility(BatchActors __instance)
    {
        if (!PureMapMode.Enabled || __instance._array == null) return;
        Actor[] array = __instance._array;
        int count = global::System.Math.Min(__instance._count, array.Length);
        for (int i = 0; i < count; i++)
        {
            Actor actor = array[i];
            if (actor != null && actor.is_visible && !PureMapMode.Visible(actor)) actor.is_visible = false;
        }
    }

    private static void AfterBuildingLimit(City __instance, BuildOrder pElement, ref int __result)
    {
        if (PureMapMode.Enabled && __result > 0 && PureMapMode.IsDecor(pElement?.getBuildingAsset(__instance)))
            __result = 0;
    }

    private static bool BeforeShouldBeClearColor(ref bool __result)
    {
        if (!PureMapMode.Enabled) return true;
        __result = false;
        return false;
    }

    private static void Opaque(ref Color32 color)
    {
        if (color.a > 0) color.a = byte.MaxValue;
    }

    private static void BeforeApplyColors(ref Color32 pColorInside, ref Color32 pColorOutside)
    {
        if (!PureMapMode.Enabled) return;
        Opaque(ref pColorInside);
        Opaque(ref pColorOutside);
    }

    private static void BeforeApplyColorsFull(ref Color32 pColor)
    {
        if (PureMapMode.Enabled) Opaque(ref pColor);
    }

    private static void BeforeColorZone(ref Color32 pColor)
    {
        if (PureMapMode.Enabled) Opaque(ref pColor);
    }

    private static bool BeforeGoTo(Actor pActor, WorldTile pTileTarget, ref ExecuteEvent __result)
    {
        // 船照常航行(地图上要看得到它们在海上走)
        if (!PureMapMode.Enabled || pActor?.asset == null || pTileTarget == null || pActor.current_tile == null ||
            pActor.asset.is_boat)
            return true;
        pActor.clearOldPath();
        pActor.setCurrentTilePosition(pTileTarget);
        pActor.dirty_current_tile = true;
        __result = ExecuteEvent.True;
        return false;
    }
}
