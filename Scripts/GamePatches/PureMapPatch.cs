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
//     城与城之间(同一片陆地、相邻)连黄色干道，港口之间连浅色虚线航线(路线顺着地形弯曲，见 RoadNetwork，
//     画在单独的路网图层上，见 RoadOverlay)。本模式下不再实际铺路，地上已有的路格也不画出来。
//     规划决议有分量：国家规划过(划了用途)的地块横平竖直、有白色街格；没规划的地块自发生长，没有方正的街格，
//     房子错落、中间是弯曲的土路小巷。规划过的城(至少一块规划地、建成两块以上)四面有城墙，每段在地块中点开城门；
//     华夏的城规划成片(三块以上规划地、建成四块以上)时穿城心有南北中轴御道与东西大街，都城的城心(大城连北面一块)是宫城——
//     朱红宫墙、白石广场，殿宇沿中轴对称摆满；研究出"现代城市规划"后约二十年间，规划过的地块逐块转成现代格局：
//     城墙拆成环路，地块边换成大道，没规划的老城区照旧(城中村)。屠城、战乱后按残破度把一部分街区画成废墟
//     (新近或正在交战的焦黑带火光，年久的灰色瓦砾，二十年后复原)。
//     各文化系有自己的布局与风味建筑(见 CityStyleSystem.Family)：华夏中轴宫城、日本天守与护城河、西欧木筋红瓦与城堡、
//     罗马主街与广场柱廊、维京长屋木栅、阿拉伯白屋迷巷与大清真寺、波斯土坯穹顶与百柱殿、埃及土坯屋与城外金字塔、
//     印度彩屋与门塔神庙、阿兹特克堤道、阶梯金字塔与水上田园、游牧蒙古包营地与汗帐、林地棚屋与议事长屋；
//     近代以后各系建筑一样。城市按定型的风格画，文化变了照旧，走"改建城池"决议后才逐年拆旧建新(见 CityStyleSystem)。
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
        UI.Components.RoadOverlay.Update();
        UI.Components.EmperorMarkerRenderer.EnsureHost();
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
        RoadNetwork.Tick();
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
            pixels[id] = Plan(tile, shaded, use, slots, tile.x - ox, tile.y - oy, plan, zone);
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
        public City city;
        public readonly Dictionary<int, Slot[]> zones = new();
        // 建成的地块(有建筑的，连同城心)、都城地标所占的地块
        public readonly HashSet<int> built = new(), landmark = new();
        public bool walled, axis, burning;
        public int axisX = int.MinValue, axisY = int.MinValue;
        // 现代城市规划的过渡进度、残破度(0~1)
        public float modern, ruin;
        // 定型的文化系；改建中的目标文化系与进度(见 CityStyleSystem)
        public string family = "huaxia", target;
        public float rebuild;
    }

    private const string ModernPlanningTech = "modern_urban_planning";
    private const float ModernYears = 20f, RuinYears = 20f;
    // 改建时一个街区拆旧到建成新式之间算工地的那一段进度
    private const float ConstructionBand = 0.12f;
    private static readonly Color32 Ring = new(210, 210, 214, 255), Imperial = new(232, 200, 130, 255),
        PalaceWall = new(170, 60, 45, 255), Marble = new(244, 240, 230, 255), Lane = new(214, 200, 170, 255),
        Moat = new(110, 160, 215, 255), Chinampa = new(90, 160, 200, 255);

    // 各文化系的城墙(没有的为 null)、中轴大道、地标拼块与广场地面
    private static Color32? WallOf(string family) => family switch
    {
        "huaxia" => new Color32(124, 112, 100, 255),
        "western" or "roma" or "persia" or "india" => new Color32(150, 145, 135, 255),
        "arab" or "egypt" => new Color32(190, 165, 120, 255),
        "viking" => new Color32(120, 90, 60, 255),
        _ => null
    };

    private static Color32? AxisOf(string family) => family switch
    {
        "huaxia" => Imperial,
        "roma" => new Color32(200, 190, 170, 255),
        "aztec" => new Color32(235, 230, 220, 255),
        _ => null
    };

    private static bool Camp(string family) => family is "nomad" or "woodland";

    private static bool Maze(string family) => family is "arab" or "viking";

    private const float PlanSeconds = 5f;
    private static readonly Dictionary<long, CityPlan> Plans = new();

    private static readonly Color32 Block = new(236, 232, 224, 255), Street = new(255, 255, 255, 255),
        Crop = new(228, 238, 200, 255);

    // 城里建成的地块(有自己的街格，路网图层的干道不压上去)
    internal static bool Built(TileZone zone)
    {
        City city = zone?.city;
        return city?.data != null && !city.isRekt() && PlanFor(city)?.built.Contains(zone.id) == true;
    }

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
        Layout(plan, city, culture, centerZone);
        return plan;
    }

    // 城池格局：定型的文化系、改建进度；城墙(规划过的城，游牧、林地、日本、阿兹特克不筑)、中轴(华夏、罗马、阿兹特克)、
    // 都城地标；现代规划过渡、残破度
    private static void Layout(CityPlan plan, City city, string culture, TileZone centerZone)
    {
        plan.city = city;
        plan.family = CityStyleSystem.Established(city);
        (plan.target, plan.rebuild) = CityStyleSystem.Rebuild(city);
        foreach (int id in plan.zones.Keys) plan.built.Add(id);
        if (centerZone != null && centerZone.city == city) plan.built.Add(centerZone.id);
        int planned = city.zones.Count(zone => zone != null && ZonePlanSystem.Get(city, zone) != ZoneUse.None);
        bool camp = Camp(plan.family);
        plan.walled = WallOf(plan.family) != null && (planned > 0 || plan.family == "viking") && plan.built.Count >= 2;
        bool grand = centerZone != null && plan.built.Count >= 4 && (camp || planned >= 3);
        if (grand && AxisOf(plan.family) != null)
        {
            plan.axis = true;
            plan.axisX = centerZone.x * Config.CITY_ZONE_SIZE + 4;
            plan.axisY = centerZone.y * Config.CITY_ZONE_SIZE + 4;
        }
        if (grand && city.isCapitalCity())
        {
            // 埃及的金字塔在城外(城里第一块没建成的地块)，其余在城心；拼块式的地标大城连北面一块
            if (plan.family == "egypt")
            {
                TileZone outside = city.zones.Where(zone => zone != null && !plan.built.Contains(zone.id))
                    .OrderBy(zone => (zone.x - centerZone.x) * (zone.x - centerZone.x) + (zone.y - centerZone.y) * (zone.y - centerZone.y))
                    .FirstOrDefault();
                plan.landmark.Add((outside ?? centerZone).id);
                plan.built.Add((outside ?? centerZone).id);
            }
            else
            {
                plan.landmark.Add(centerZone.id);
                TileZone north = World.world.zone_calculator.getZone(centerZone.x, centerZone.y + 1);
                if (Arranged(plan.family) && plan.built.Count >= 9 && north != null && north.city == city)
                {
                    plan.landmark.Add(north.id);
                    plan.built.Add(north.id);
                }
            }
        }
        var tech = TechnologySystem.GetState(culture);
        if (tech?.timestamps != null && tech.timestamps.TryGetValue("tech:" + ModernPlanningTech, out double since) &&
            TechnologySystem.HasTech(culture, ModernPlanningTech))
            plan.modern = Mathf.Clamp01(Date.getYearsSince(since) / ModernYears);
        // 残破：屠城死难占全城的比例，随年头淡去；正在交战再加一层
        var data = city.GetOrCreate();
        float population = CityPopulationSystem.GetBackgroundTotal(city);
        if (data.massacre_last >= 0d && data.massacre_victims > 0)
        {
            float years = Date.getYearsSince(data.massacre_last);
            float share = data.massacre_victims / Mathf.Max(1f, data.massacre_victims + population);
            plan.ruin = Mathf.Clamp01(share * 1.5f) * Mathf.Clamp01(1f - years / RuinYears);
            plan.burning = years < 2f;
        }
        if (FrontSystem.Battles.ContainsKey(city.data.id))
        {
            plan.ruin = Mathf.Max(plan.ruin, 0.25f);
            plan.burning = true;
        }
    }

    // 0~1 的稳定散列(地块、街区按它决定先后转现代、是否成废墟)
    private static float Hash01(long value) => (uint)(value * 2654435761L) / 4294967296f;

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

    // 一格：建成的地块按格局画(城墙、都城地标、中轴、规划过的街格或自发的小巷、营地、街区里的建筑图块)；
    // 农田画条垄(阿兹特克是水上田园)；其余照地形(干道画在路网图层上，见 RoadOverlay)
    private static Color32 Plan(WorldTile tile, Color32 color, ZoneUse use, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone)
    {
        if (tile.Type != null && tile.Type.liquid) return color;
        bool built = plan != null && plan.built.Contains(zone.id);
        if (built && lx >= 0 && lx < 8 && ly >= 0 && ly < 8)
        {
            bool modern = use != ZoneUse.None && plan.modern > 0f && Hash01(zone.id) < plan.modern;
            // 这个地块的布局按哪一系(改建中逐个地块换)
            string family = plan.target != null && Hash01(zone.id * 31L + 99L) < plan.rebuild ? plan.target : plan.family;
            // 城墙：现代规划后拆成环路；每段在地块中点开城门
            if (plan.walled && OnEdge(tile, plan.built, plan.city))
                return lx == 4 || ly == 4 ? Lerp(color, Street, 0.6f)
                    : modern ? Lerp(color, Ring, 0.7f) : Lerp(color, WallOf(plan.family) ?? Ring, 0.85f);
            if (plan.landmark.Contains(zone.id)) return Landmark(tile, color, plan, lx, ly);
            // 中轴大道(华夏御道、罗马主街、阿兹特克堤道)
            if (plan.axis && (tile.x == plan.axisX || tile.y == plan.axisY)) return Lerp(color, AxisOf(plan.family) ?? Street, 0.8f);
            if (modern) return Grid(color, slots, lx, ly, plan, zone, true);
            if (Camp(family)) return CampLayout(color, slots, lx, ly, plan, zone);
            if (Maze(family))
                return use != ZoneUse.None && (lx == 4 || ly == 4) ? Lerp(color, Lane, 0.6f) : Organic(color, slots, lx, ly, plan, zone);
            return use == ZoneUse.None ? Organic(color, slots, lx, ly, plan, zone) : Grid(color, slots, lx, ly, plan, zone, false);
        }
        if (use == ZoneUse.Farm)
        {
            // 阿兹特克的水上田园：一垄田一道水渠
            if (plan?.family == "aztec") return tile.y % 3 == 0 ? Lerp(color, Chinampa, 0.5f) : Lerp(color, Crop, 0.25f);
            return Lerp(color, Crop, tile.y % 2 == 0 ? 0.2f : 0.06f);
        }
        return color;
    }

    // 区域的外缘：四邻有一格不在这片区域(或不归这座城)
    private static bool OnEdge(WorldTile tile, HashSet<int> region, City city)
    {
        return Outside(tile.tile_up) || Outside(tile.tile_down) || Outside(tile.tile_left) || Outside(tile.tile_right);
        bool Outside(WorldTile next) => next?.zone == null || next.zone.city != city || !region.Contains(next.zone.id);
    }

    // 规划过的地块：白色街格切出四个街区，建筑图块整齐摆进去；现代格局的地块边换成大道
    private static Color32 Grid(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone, bool modern)
    {
        if (lx % 4 == 0 || ly % 4 == 0)
            return modern && (lx == 0 || ly == 0) ? Lerp(color, Ring, 0.75f) : Lerp(color, Street, 0.6f);
        int index = (ly - 1) / 4 * 2 + (lx - 1) / 4;
        return Building(color, slots, index, (lx - 1) % 4, (ly - 1) % 4, plan, zone);
    }

    // 没规划的地块(阿拉伯、维京整城都这样)：自发生长，弯曲的土路小巷穿过，房子在四个角落里错落摆放
    private static Color32 Organic(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone)
    {
        uint hash = (uint)(zone.id * 2654435761L);
        int laneX = 3 + (int)((hash >> (ly % 8 * 2)) & 1);
        int laneY = 3 + (int)((hash >> (16 + lx % 8 * 2)) & 1);
        if (lx == laneX || ly == laneY) return Lerp(color, Lane, 0.45f);
        return Scattered(color, slots, lx, ly, plan, zone, hash);
    }

    // 营地(游牧、林地)：没有街巷，帐篷、棚屋在草地上错落围成一圈
    private static Color32 CampLayout(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone) =>
        Scattered(color, slots, lx, ly, plan, zone, (uint)(zone.id * 2654435761L));

    // 四个角落里各一座，错开一格摆
    private static Color32 Scattered(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone, uint hash)
    {
        int qx = lx < 4 ? 0 : 1, qy = ly < 4 ? 0 : 1;
        int index = qy * 2 + qx;
        int ox = (int)((hash >> (index * 2)) & 1), oy = (int)((hash >> (8 + index * 2)) & 1);
        int px = lx - qx * 4 - ox, py = ly - qy * 4 - oy;
        if (px < 0 || px > 2 || py < 0 || py > 2) return Lerp(color, Block, 0.12f);
        return Building(color, slots, index, px, py, plan, zone);
    }

    // 街区里的建筑：残破的画废墟；改建中先拆旧起工地、再建成新式；否则按这座城定型的文化系画这座建筑的图块
    private static Color32 Building(Color32 color, Slot[] slots, int index, int px, int py, CityPlan plan, TileZone zone)
    {
        Color32 baseColor = Lerp(color, Block, 0.3f);
        if (slots == null || index < 0 || index >= slots.Length || !slots[index].used) return baseColor;
        string name;
        if (plan.ruin > 0f && Hash01(zone.id * 4L + index + 7L) < plan.ruin) name = plan.burning ? "burning" : "ruin";
        else
        {
            string family = plan.family;
            if (plan.target != null)
            {
                float turn = Hash01(zone.id * 4L + index + 13L);
                if (turn < plan.rebuild) family = plan.target;
                else if (turn < plan.rebuild + ConstructionBand) family = null;
            }
            name = family == null ? "construction" : StyleIcon(family, slots[index].icon);
        }
        Color32[] icon = Icon(name) ?? Icon(slots[index].icon);
        if (icon == null) return Lerp(color, Block, 0.55f);
        Color32 pixel = icon[(2 - py) * 3 + px];
        return pixel.a == 0 ? baseColor : Lerp(baseColor, pixel, 0.9f);
    }

    // 通用图块换成这一系的风味图块(古代民居分低中高三档、寺庙、官署、集市)；近代以后各系一样；没有风味图块用通用的
    private static string StyleIcon(string family, string icon)
    {
        if (family == "huaxia" || string.IsNullOrEmpty(icon)) return icon;
        string variant = icon switch
        {
            "house_1" or "house_2" => "house_a",
            "house_3" or "house_4" => "house_b",
            "house_5" => "house_c",
            "temple" => "temple",
            "civic_0" => "civic",
            _ => null
        };
        if (variant == null) return icon;
        string name = family + "_" + variant;
        if (Icon(name) != null) return name;
        return variant == "house_c" && Icon(family + "_house_b") != null ? family + "_house_b" : icon;
    }

    // 拼块式地标(其余是整块画的：埃及金字塔、阿兹特克阶梯金字塔、游牧汗帐)
    private static bool Arranged(string family) => family is not ("egypt" or "aztec" or "nomad");

    // 都城地标：华夏宫城、日本天守(护城河)、西欧城堡、罗马广场、维京大殿、阿拉伯大清真寺、波斯百柱殿、印度门塔神庙、
    // 林地议事长屋——外缘围墙(罗马、林地开敞)，中轴开门，地面是广场，街区里摆满这一系的地标拼块；
    // 埃及金字塔、阿兹特克阶梯金字塔、游牧汗帐整块画
    private static Color32 Landmark(WorldTile tile, Color32 color, CityPlan plan, int lx, int ly)
    {
        string family = plan.family;
        switch (family)
        {
            case "egypt": return Pyramid(color, lx, ly);
            case "aztec": return SteppedPyramid(color, lx, ly);
            case "nomad": return GreatTent(color, lx, ly);
        }
        (string piece, Color32? wall, Color32 floor) = family switch
        {
            "huaxia" => ("palace_hall", (Color32?)PalaceWall, Marble),
            "japan" => ("japan_landmark", new Color32(150, 145, 135, 255), new Color32(200, 196, 186, 255)),
            "western" => ("western_landmark", new Color32(150, 145, 135, 255), new Color32(180, 176, 168, 255)),
            "roma" => ("roma_landmark", null, Marble),
            "viking" => ("viking_landmark", new Color32(120, 90, 60, 255), new Color32(170, 150, 120, 255)),
            "arab" => ("arab_landmark", new Color32(238, 230, 210, 255), new Color32(244, 240, 228, 255)),
            "persia" => ("persia_landmark", new Color32(150, 145, 135, 255), new Color32(200, 190, 170, 255)),
            "india" => ("india_landmark", new Color32(200, 150, 90, 255), new Color32(214, 200, 176, 255)),
            "woodland" => ("woodland_landmark", null, new Color32(170, 150, 120, 255)),
            _ => ("palace_hall", (Color32?)PalaceWall, Marble)
        };
        bool gateColumn = plan.axis ? tile.x == plan.axisX : lx == 4;
        if (OnEdge(tile, plan.landmark, plan.city))
        {
            // 日本天守外有护城河
            if (family == "japan") return Lerp(color, Moat, 0.85f);
            if (wall != null && !gateColumn) return Lerp(color, wall.Value, 0.9f);
            return Lerp(color, plan.axis ? AxisOf(family) ?? floor : floor, 0.8f);
        }
        if (family == "japan" && wall != null && NextToEdge(tile, plan)) return Lerp(color, wall.Value, 0.9f);
        if (plan.axis && tile.x == plan.axisX) return Lerp(color, AxisOf(family) ?? floor, 0.85f);
        if (lx % 4 == 0 || ly % 4 == 0) return Lerp(color, floor, 0.75f);
        Color32[] icon = Icon(piece) ?? Icon("palace_hall");
        if (icon == null) return Lerp(color, floor, 0.75f);
        Color32 pixel = icon[(2 - (ly - 1) % 4) * 3 + (lx - 1) % 4];
        return pixel.a == 0 ? Lerp(color, floor, 0.75f) : Lerp(color, pixel, 0.9f);
    }

    private static bool NextToEdge(WorldTile tile, CityPlan plan) =>
        new[] { tile.tile_up, tile.tile_down, tile.tile_left, tile.tile_right }
            .Any(next => next != null && plan.landmark.Contains(next.zone?.id ?? -1) && OnEdge(next, plan.landmark, plan.city));

    // 金字塔(俯视)：北、东两面受光，南、西两面背阴，塔尖最亮
    private static Color32 Pyramid(Color32 color, int lx, int ly)
    {
        float dx = lx - 3.5f, dy = ly - 3.5f;
        float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (d > 3.5f) return Lerp(color, new Color32(220, 196, 140, 255), 0.4f);
        if (d < 1f) return Lerp(color, new Color32(246, 228, 176, 255), 0.95f);
        bool lit = Mathf.Abs(dx) >= Mathf.Abs(dy) ? dx > 0 : dy > 0;
        return Lerp(color, lit ? new Color32(232, 206, 142, 255) : new Color32(176, 146, 92, 255), 0.95f);
    }

    // 阶梯金字塔(俯视)：一层层台阶，顶上并立红、蓝两座神庙，南面一道台阶
    private static Color32 SteppedPyramid(Color32 color, int lx, int ly)
    {
        float dx = lx - 3.5f, dy = ly - 3.5f;
        float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (d < 1f) return Lerp(color, lx < 4 ? new Color32(190, 60, 50, 255) : new Color32(70, 110, 180, 255), 0.95f);
        if (dy < 0 && (lx == 3 || lx == 4)) return Lerp(color, new Color32(222, 214, 196, 255), 0.95f);
        return Lerp(color, (int)(d * 2f) % 2 == 0 ? new Color32(196, 186, 166, 255) : new Color32(160, 150, 130, 255), 0.95f);
    }

    // 汗帐(俯视)：大圆毡顶，金边，中间天窗，四条接缝
    private static Color32 GreatTent(Color32 color, int lx, int ly)
    {
        float dx = lx - 3.5f, dy = ly - 3.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r > 3.7f) return Lerp(color, Block, 0.12f);
        if (r < 0.9f) return Lerp(color, new Color32(90, 80, 70, 255), 0.95f);
        if (r > 2.9f) return Lerp(color, new Color32(225, 190, 90, 255), 0.95f);
        return Lerp(color, Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f ? new Color32(225, 220, 210, 255)
            : new Color32(246, 244, 236, 255), 0.95f);
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
