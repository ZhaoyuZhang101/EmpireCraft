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
//   · 城市肌理(仿电子地图)：建筑虽不画，占地照样显出来——每座建筑一块实心浅色块、块边描深一圈，民居等级越高越实
//     (大城一大片街区，小县只一小撮)，工业淡紫灰、商业淡橙、码头淡蓝、兵营淡红灰、寺庙米黄，庄稼淡绿；
//     城里的路是白线、城外连城的路是黄线，成片路面的中间当广场；规划区只铺很淡的平涂色，城心略亮；
//     地形打光与掺色都调淡，河湖用干净的蓝。看图就能看出路网与各地发展到哪一步；
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
            foreach (TileZone zone in city.zones) zone?.resetRenderHelpers();
        }
        _urbanCursor = (_urbanCursor + slice) % cities.Count;
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
        bool center = city != null && ZonePlanSystem.IsCenter(city, zone);
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile?.data?.tile_id ?? -1;
            if (id < 0 || id >= pixels.Length || pixels[id].a == 0) continue;
            WorldTileZoneBorder border = tile.world_tile_zone_border;
            if (border != null && border.border && (up && border.border_up || down && border.border_down ||
                                                    left && border.border_left || right && border.border_right)) continue;
            Color32 shaded = Shade(tile, pixels[id]);
            pixels[id] = city != null ? Urban(tile, shaded, use, center) : shaded;
        }
    }

    // ───────── 城市肌理(仿电子地图：路网与建筑块) ─────────
    // 建筑占地画成实心浅色块、块边描深一圈(一座座房子分得开)；城里的路是白线，城外连城的路是黄线，
    // 成片路面的中间当广场铺浅灰；规划区只铺很淡的平涂色
    private static readonly Color32 Block = new(236, 232, 224, 255), Civic = new(232, 228, 236, 255),
        Industrial = new(214, 208, 226, 255), Market = new(250, 226, 196, 255), Temple = new(246, 238, 214, 255),
        Garrison = new(230, 206, 204, 255), Port = new(206, 224, 242, 255), Crop = new(228, 238, 200, 255),
        Street = new(255, 255, 255, 255), Highway = new(255, 210, 110, 255), Plaza = new(222, 220, 216, 255);
    private const float BlockEdge = 0.78f;

    private static int RoadNeighbours(WorldTile tile)
    {
        int count = 0;
        if (tile.tile_up?.Type?.road == true) count++;
        if (tile.tile_down?.Type?.road == true) count++;
        if (tile.tile_left?.Type?.road == true) count++;
        if (tile.tile_right?.Type?.road == true) count++;
        return count;
    }

    // 块边：四邻里有一格不是同一座建筑
    private static bool BlockBorder(WorldTile tile)
    {
        Building building = tile.building;
        return tile.tile_up?.building != building || tile.tile_down?.building != building ||
               tile.tile_left?.building != building || tile.tile_right?.building != building;
    }

    private static Color32 Urban(WorldTile tile, Color32 color, ZoneUse use, bool center)
    {
        BuildingAsset asset = tile.building?.asset;
        if (asset != null && !tile.building.isRekt() && asset.building_type != BuildingType.Building_Tree)
        {
            if (asset.building_type == BuildingType.Building_Wheat) return Lerp(color, Crop, 0.3f);
            (Color32 fill, float share) = asset.type switch
            {
                "type_house" => (Block, Mathf.Min(0.72f, 0.55f + 0.03f * CityDevelopment.Tier(asset))),
                "type_market" => (Market, 0.65f),
                "type_docks" => (Port, 0.6f),
                "type_barracks" => (Garrison, 0.6f),
                "type_temple" => (Temple, 0.65f),
                _ => TechnologySystem.IsFactory(asset) || IndustryBuildingSystem.IsMine(asset) || IndustryBuildingSystem.IsLumber(asset)
                    ? (Industrial, 0.6f)
                    : asset.building_type == BuildingType.Building_Civ ? (Civic, 0.6f) : (default(Color32), 0f)
            };
            if (share > 0f)
            {
                Color32 block = Lerp(color, fill, share);
                return BlockBorder(tile) ? Scale(block, BlockEdge) : block;
            }
        }
        if (tile.Type != null && tile.Type.road)
        {
            // 城外连城的路是黄线；成片路面(四面都是路)的中间当广场
            if (tile.zone?.city == null) return Lerp(color, Highway, 0.7f);
            return RoadNeighbours(tile) >= 4 ? Lerp(color, Plaza, 0.35f) : Lerp(color, Street, 0.65f);
        }
        color = use switch
        {
            ZoneUse.Residential => Lerp(color, Block, 0.15f),
            ZoneUse.Industry => Lerp(color, Industrial, 0.2f),
            ZoneUse.Commerce => Lerp(color, Market, 0.2f),
            ZoneUse.Farm => Lerp(color, Crop, 0.15f),
            ZoneUse.Military => Lerp(color, Garrison, 0.15f),
            _ => color
        };
        return center ? Lerp(color, Block, 0.2f) : color;
    }

    private static Color32 Shade(WorldTile tile, Color32 color)
    {
        TileTypeBase type = tile.Type;
        if (type == null) return color;
        if (type.lava) return Lerp(color, type.color, WaterTint);
        if (type.liquid) return Lerp(color, Water, WaterTint);
        TileTypeBase surface = tile.top_type != null ? tile.top_type : type;
        float light = Light(type);
        if (tile.building?.asset != null && tile.building.asset.building_type == BuildingType.Building_Tree) light *= 0.94f;
        int height = tile.Height;
        int north = tile.tile_up?.Height ?? height, west = tile.tile_left?.Height ?? height;
        light += Mathf.Clamp((height * 2 - north - west) * SlopeLight, -SlopeCap, SlopeCap);
        return Scale(Lerp(color, surface.color, TerrainTint), light);
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
