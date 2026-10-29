using System;
using System.Collections.Generic;
using System.Linq;
using ai;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 纯净地图模式(见 PureMapMode)：
//   · 国家色块填满：原版近看时国家内部画成透明、只留边框；这里任何缩放都填满。颜色、透明度同原版图层：半透明，露出原版地形，
//     跟着地图不透明度设置(用户 2026-10-26 "去掉地形浮雕改为像原版的透明图层一样就好了"；以前色块不透明，地形明暗画进色块)；
//     没选地图图层时默认显示国家图层、关了"地图图层"也照样显示；入夜不变淡、不被夜色压暗(见 NightMapLayerPatch；云照旧)；
//   · 地形由原版自己画在半透明的色块底下，色块不随地形变(以前把地形明暗画进不透明的色块，结霜、长草都要重画)；
//     块里的海、河、湖不涂国色，海岸线是真实的形状；
//   · 城市平面图(仿电子地图，位置是模拟的；另成一层铺在色块之下，见 CityPlanOverlay)：
//     建筑照常作为数据运作，地图上按每座城的建筑数据生成一张平面图——
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
//     建筑与规划会变，每秒轮流重建一批城的平面图，约十秒全图一遍，变了的才重画城市平面图层；
//   · 国家图层下，帝国与外国接壤的陆上边界画成金色，帝国内部各成员之间照旧；海岸、湖泊、无主荒地不描金
//     (原来连这些也描，整片海岸线和境内每块荒地周围都是金线，看不出哪里才是国界)；
//   · 人物要去哪里直接到位、不走路(地图上也不画人物)：原版的行为照常完成(回家、上任、相会生子)，不再寻路；
//     鼠标附近只认得船，悬停与点击都落到城市、国家上；除船以外的人物逐帧设为不可见；
//   · 纯装饰建筑(雕像、训练木桩、花园、喷泉)建造上限为 0，城市不再建；
//   · 建筑不画，城市铭牌上的种族头像换成城市性质徽记、文字后写民居等级(见 CityDevelopment)
public partial class PureMapPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(PureMapPatch));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "shouldBeClearColor"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeShouldBeClearColor)));
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeApplyColors)));
        // 平滑国界(见 SmoothBorders)：颜色定好后逐像素画，跳过原版按地块画
        harmony.Patch(AccessTools.Method(typeof(ZoneCalculator), "applyMetaColorsToZone"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(SmoothPaint)) { priority = Priority.Low });
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
        harmony.Patch(AccessTools.Method(typeof(QuantumSpriteLibrary), "drawLightAreas"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeDrawLights)));
        harmony.Patch(AccessTools.Method(typeof(QuantumSpriteLibrary), "drawFires"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeDrawFires)));
        harmony.Patch(AccessTools.Method(typeof(FireLayer), "checkAutoDisable"),
            postfix: new HarmonyMethod(typeof(PureMapPatch), nameof(AfterFireLayerCheck)));
        harmony.Patch(AccessTools.Method(typeof(QuantumSpriteLibrary), "drawPlotIcon"),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeDrawPlotIcon)));
        // 平滑国界：悬停、选中的高亮逐像素画(见 SmoothBorders.Highlight)，不叠整块方片
        harmony.Patch(AccessTools.Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.colorZones),
                new[] { typeof(QuantumSpriteAsset), typeof(List<TileZone>), typeof(Color) }),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeColorZonesList)));
        harmony.Patch(AccessTools.Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.colorZones),
                new[] { typeof(QuantumSpriteAsset), typeof(ListPool<TileZone>), typeof(Color) }),
            prefix: new HarmonyMethod(typeof(PureMapPatch), nameof(BeforeColorZonesPool)));
    }

    // 火焰不画(用户 2026-10-15："这个火焰取消掉")：近处的火苗动画、远景的红色火点、着火处的夜光都不画；
    // 火照原版蔓延、烧东西，只是看不见——和人物建筑一样留在数据里
    private static bool BeforeDrawFires() => !PureMapMode.Enabled;

    private static void AfterFireLayerCheck(FireLayer __instance)
    {
        if (PureMapMode.Enabled && __instance.sprRnd != null && __instance.sprRnd.enabled) __instance.sprRnd.enabled = false;
    }

    // 夜景灯光：原版按人物与建筑的真实位置点灯，这里人物到处瞬移、建筑不画，灯光会乱飘；
    // 改为只在城市平面图建成的地块上、每座虚拟建筑处点一盏灯——住房越高级越亮，工厂、市场、官署更亮，
    // 现代格局的地块再亮一些，都城地标最亮；成了废墟的街区不亮；火焰不画，着火处也不亮。
    // 每块地的灯按所属的城与平面图指纹记下，平面图变了(按轮重建，见 RefreshUrbanSlice)才重算；
    // 以前每块五秒一算，重算时平面图也到期了顺带重建，一帧到 83 毫秒。每帧重算至多约 LightBudgetMs 毫秒，没轮到的先用旧的
    private static readonly Dictionary<int, (City city, int stamp, List<(Vector2 at, float size)> lights)> ZoneLights = new();
    private static readonly List<(Vector2, float)> NoLights = new();
    private const double LightBudgetMs = 1d;

    private static bool LightsWanted() =>
        PlayerConfig.optionBoolEnabled("night_lights") && World.world.era_manager.shouldShowLights();

    private static bool BeforeDrawLights()
    {
        if (!PureMapMode.Enabled) return true;
        if (!LightsWanted()) return false;
        using var timing = FrameProfiler.Measure("纯净地图·夜景灯光");
        Color color = QuantumSpriteLibrary.getColorForLight();
        List<TileZone> zones = World.world.zone_camera.getVisibleZones();
        if (ZoneLights.Count > 20000) ZoneLights.Clear();
        long deadline = global::System.Diagnostics.Stopwatch.GetTimestamp() +
                        (long)(LightBudgetMs * global::System.Diagnostics.Stopwatch.Frequency / 1000d);
        bool budget = true;
        foreach (TileZone zone in zones)
        {
            City city = zone.city;
            if (city == null) continue;
            bool known = ZoneLights.TryGetValue(zone.id, out var cached);
            CityPlan plan = CachedPlan(city);
            bool fresh = known && ReferenceEquals(cached.city, city) && plan != null && cached.stamp == PlanStamp(city, plan);
            // 还没有平面图的城(还没画到)也在预算内现算一次
            if (!fresh && budget)
            {
                List<(Vector2, float)> lights = LightsOf(zone, known ? cached.lights : null);
                ZoneLights[zone.id] = cached = (city, PlanStamp(city, CachedPlan(city)), lights);
                known = true;
                budget = global::System.Diagnostics.Stopwatch.GetTimestamp() < deadline;
            }
            if (!known) continue;
            foreach ((Vector2 at, float size) in cached.lights) QuantumSpriteLibrary.showLightAt(at, color, size);
        }
        return false;
    }

    // 一块地上的灯(按城市平面图)；reuse：这块地原来那份灯的列表，清空了接着用(不每次新建)
    private static List<(Vector2, float)> LightsOf(TileZone zone, List<(Vector2, float)> reuse)
    {
        City city = zone.city;
        if (city?.data == null || city.isRekt()) return NoLights;
        CityPlan plan = PlanFor(city);
        if (!plan.built.Contains(zone.id)) return NoLights;
        int ox = zone.x * Config.CITY_ZONE_SIZE, oy = zone.y * Config.CITY_ZONE_SIZE;
        List<(Vector2, float)> lights = reuse != null && !ReferenceEquals(reuse, NoLights) ? reuse : null;
        lights?.Clear();
        if (plan.landmark.Contains(zone.id))
        {
            lights ??= new List<(Vector2, float)>(1);
            lights.Add((new Vector2(ox + 4, oy + 4), 0.9f));
            return lights;
        }
        if (!plan.zones.TryGetValue(zone.id, out Slot[] slots)) return NoLights;
        bool modern = plan.modern > 0f && Hash01(zone.id) < plan.modern;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].used || plan.ruin > 0f && Hash01(zone.id * 4L + i + 7L) < plan.ruin) continue;
            Vector2 spot = SlotSpot(i);
            int x = ox + (int)spot.x, y = oy + (int)spot.y;
            WorldTile at = World.world.GetTile(x, y);
            if (at != null && !UrbanFabric.InTown(plan.fabric, at)) continue;
            lights ??= new List<(Vector2, float)>(slots.Length);
            lights.Add((new Vector2(x, y), LightSize(slots[i].icon) * (modern ? 1.15f : 1f)));
        }
        return lights != null && lights.Count > 0 ? lights : NoLights;
    }

    private static float LightSize(string icon)
    {
        if (icon == null) return 0.4f;
        if (icon.StartsWith("house_", StringComparison.Ordinal) && int.TryParse(icon.Substring(6), out int tier))
            return 0.25f + 0.05f * tier;
        if (icon.StartsWith("industry", StringComparison.Ordinal)) return 0.6f;
        if (icon.StartsWith("market", StringComparison.Ordinal) || icon.StartsWith("civic", StringComparison.Ordinal)) return 0.55f;
        return icon switch { "temple" => 0.4f, "docks" => 0.45f, "barracks" => 0.35f, _ => 0.4f };
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

    // 纯净地图的各层(城市平面图、精细层、路网、城墙、标注)每帧跟着色块图层更新
    private static void AfterZoneAnimations(ZoneCalculator __instance)
    {
        if (__instance.sprRnd == null) return;
        // 无人区(用户 2026-10-29)：挂了法理的并进国土(见 WildernessTerritory)，类型色、边线、悬停提示(见 WildernessOverlay)，
        // 纯净地图开没开都要
        UI.Components.WildernessTerritory.Tick();
        UI.Components.WildernessOverlay.Update();
        // 城市图层不论纯净地图开没开都画路网
        if (!PureMapMode.Enabled)
        {
            UI.Components.CityPlanOverlay.Update();
            UI.Components.RoadOverlay.Update();
            UI.Components.CityDetailOverlay.Update();
            return;
        }
        RefreshUrbanSlice();
        UI.Components.CityPlanOverlay.Update();
        UI.Components.CityDetailOverlay.Update();
        UI.Components.RoadOverlay.Update();
        UI.Components.MapWallOverlay.Update();
        UI.Components.MapLabelOverlay.Update();
        UI.Components.EmperorMarkerRenderer.EnsureHost();
    }

    // 城市肌理随建筑、规划、时代变化(原版只在归属、颜色变了才重画)：约十秒把各城看一轮，
    // 平面图有变化(建成地块、建筑、规划用途、城墙、时代进度、城郊档位、种子)的城才重画它在城市平面图层里的样子
    // (见 CityPlanOverlay；以前平面图画在国家色块里，要整城的色块重画，和换色抢上色预算)。
    // 平面图只在这里按轮重建(PlanFor 只在没有时现算)：以前平面图五秒过期，上色、灯光、精细层谁先用到谁重建，
    // 好几座城赶在同一帧过期就卡一下。每帧至多重建 MaxPlanRebuilds 座，用满约 PlanRebuildBudgetMs 毫秒就停，
    // 没轮到的额度留到下一帧
    private const float UrbanCycleSeconds = 10f;
    private const int MaxPlanRebuilds = 4;
    private const double PlanRebuildBudgetMs = 2d;
    private static readonly List<City> UrbanQueue = new();
    private static int _urbanCursor;
    private static float _urbanCredit, _urbanLast = -1f;

    private static void RefreshUrbanSlice()
    {
        float now = Time.unscaledTime;
        var cities = World.world?.cities?.list;
        if (cities == null || cities.Count == 0) return;
        if (_urbanCursor >= UrbanQueue.Count)
        {
            UrbanQueue.Clear();
            _urbanCursor = 0;
            foreach (City city in cities)
            {
                if (city?.zones == null || city.isRekt()) continue;
                // 本模式下不实际铺路
                city.road_tiles_to_build.Clear();
                UrbanQueue.Add(city);
            }
        }
        float delta = _urbanLast < 0f ? 0f : Mathf.Clamp(now - _urbanLast, 0f, 0.5f);
        _urbanLast = now;
        // 欠下的额度至多攒两帧的量(卡了一下不一口气补几十座)
        _urbanCredit = Mathf.Min(_urbanCredit + UrbanQueue.Count / UrbanCycleSeconds * delta, MaxPlanRebuilds * 2f);
        // 原有一格一像素粗图随城市变化；精细贴图的近景门槛由 CityDetailOverlay 单独控制。
        bool detail = CitiesShown;
        // 粗图、精细层、铭牌(都要城市显示开着)与夜景灯光用得着平面图才重建
        bool needed = detail || LightsWanted();
        long started = global::System.Diagnostics.Stopwatch.GetTimestamp();
        long budget = (long)(PlanRebuildBudgetMs * global::System.Diagnostics.Stopwatch.Frequency / 1000d);
        int rebuilt = 0;
        while (_urbanCredit >= 1f && _urbanCursor < UrbanQueue.Count && rebuilt < MaxPlanRebuilds &&
               (rebuilt == 0 || global::System.Diagnostics.Stopwatch.GetTimestamp() - started < budget))
        {
            City city = UrbanQueue[_urbanCursor++];
            _urbanCredit -= 1f;
            if (!needed || city?.data == null || city.isRekt() || city.zones == null) continue;
            CityPlan plan = RebuildPlan(city);
            rebuilt++;
            // 城市平面图层按它画这座城时用的平面图指纹比，变了才重画(还没画过的它逐段核对时自己会画)
            if (detail) UI.Components.CityPlanOverlay.PlanRebuilt(city, plan.stamp);
        }
        RoadNetwork.Tick();
        RuralSettlementSystem.Tick();
        FactorySystem.Tick();
        CityZoningSystem.Tick();
    }

    // 平面图的指纹：画出来的样子会变的东西都算进去
    private static int Stamp(City city, CityPlan plan)
    {
        unchecked
        {
            int hash = 17;
            void Add(int value) => hash = hash * 31 + value;
            foreach (int id in plan.built) Add(id * 7919);
            foreach (int id in plan.landmark) Add(id * 104729);
            Add(plan.papal ? 1 : 0);
            foreach (KeyValuePair<int, Slot[]> pair in plan.zones)
                foreach (Slot slot in pair.Value)
                    Add(pair.Key ^ (slot.icon?.GetHashCode() ?? 0));
            foreach (TileZone zone in city.zones)
                if (zone != null && FarmlandSystem.IsPlanned(city, zone)) Add(zone.id * 13 + 1);
            Add(plan.family?.GetHashCode() ?? 0);
            Add(plan.target?.GetHashCode() ?? 0);
            // 本城的厂区(只算本城的，别处的厂区变了不必重画)
            foreach (FactorySite site in plan.factories)
            {
                Add(site.id * 92821 + site.size * 31 + (int)site.seed);
                foreach (FactoryUnit unit in site.units) Add((unit.type?.GetHashCode() ?? 0) + unit.level * 7);
            }
            Add(plan.tribal * 7 + 3);
            Add(plan.winter ? 11 : 1);
            Add(Mathf.RoundToInt(plan.rebuild * 20f));
            Add(Mathf.RoundToInt(plan.modern * 20f));
            Add(Mathf.RoundToInt(plan.ruin * 20f));
            Add((plan.walled ? 1 : 0) + (plan.moat ? 2 : 0) + (plan.axis ? 4 : 0) + (plan.burning ? 8 : 0));
            // 城心那块地与城门中线(迁城后整座城换地方，见 CityRelocationSystem)
            Add(plan.centerZone * 131 + 7);
            Add(plan.gateX * 73 + plan.gateY * 37);
            // 手动规划：中轴交点、整块并入的地块、坊市疏密(建成地块、宫城、城墙、城门、种子上面都已算进去)
            Add(plan.axisX * 61 + plan.axisY * 29);
            foreach (int id in plan.forced) Add(id * 15485863);
            Add(plan.blocks * 977 + 5);
            if (plan.fabric != null)
            {
                // 官道(邻城迁了城心，接过来的官道换了走向)
                foreach (UrbanFabric.Road road in plan.fabric.Roads)
                    if (road.Link) Add(Mathf.RoundToInt(road.X) * 92821 + Mathf.RoundToInt(road.Y) * 7919 + road.Length);
                Add(Mathf.RoundToInt(plan.fabric.Progress * 20f));
                Add(plan.fabric.Tier);
                Add(plan.fabric.Seed.GetHashCode());
                Add(plan.fabric.Rays.Length);
                Add(plan.fabric.Towns.Count * 16 + plan.fabric.Villages.Count);
                Add(plan.fabric.SettlementHash);
            }
            return hash;
        }
    }

    private static readonly Color32 EmpireGold = new(232, 184, 64, 255);

    // 帝国对外的边画成金色(纯净地图模式，国家图层)：原版画完这块地后，把对面不是本帝国的那几条边重新涂一遍。
    // 战国的周室(王畿连同离开帝国的诸侯)在国家图层和帝国图层都画，而且加粗一格，用来看出是不是同一个势力
    private static void AfterApplyColors(ZoneCalculator __instance, TileZone pZone, bool pUp, bool pDown, bool pLeft, bool pRight)
    {
        if (pZone?.tiles == null || __instance.pixels == null) return;
        // 平滑国界从缓存拷回的：收尾(水面、斜纹)已经在里面
        if (SmoothBorders.Restored(pZone)) return;
        if (PureMapMode.Enabled || Layer.CityViewMap.Active) FinishZone(__instance.pixels, pZone, pUp, pDown, pLeft, pRight);
        SmoothBorders.Finish(__instance.pixels, pZone);
        // 平滑国界：帝国金边已逐像素画好(见 SmoothBorders)；规划时整张图是灰的，不描金边
        if (SmoothBorders.Enabled) return;
        // 并进国土的无人区算那一国的(见 WildernessTerritory)：帝国金边把它圈在外面
        Kingdom kingdom = pZone.city?.kingdom ?? UI.Components.WildernessTerritory.Owner(pZone);
        MetaType? mode = World.world.getCachedMapMetaAsset()?.map_mode;
        bool kingdomLayer = mode == MetaType.Kingdom, empireLayer = mode == MetaTypeExtension.Empire;
        if (kingdom == null || !kingdomLayer && !empireLayer) return;
        Empire sphere = ZhouEraSystem.SphereOf(kingdom);
        bool bold = sphere != null && ZhouEraSystem.Era(sphere) >= ZhouEraSystem.Warring;
        Empire empire = bold ? sphere : PureMapMode.Enabled && kingdomLayer ? EmpireMembershipService.MapEmpire(kingdom) : null;
        if (empire == null) return;
        bool up = pUp && Outside(pZone.zone_up, empire), down = pDown && Outside(pZone.zone_down, empire);
        bool left = pLeft && Outside(pZone.zone_left, empire), right = pRight && Outside(pZone.zone_right, empire);
        if (!up && !down && !left && !right) return;
        Color32[] pixels = __instance.pixels;
        foreach (WorldTile tile in pZone.tiles)
        {
            WorldTileZoneBorder border = tile?.world_tile_zone_border;
            if (border == null || !border.border) continue;
            // 纯净地图下水面不涂国色，金边也不描在水上(见 FinishZone)
            if (PureMapMode.Enabled && tile.Type is TileTypeBase ground && (ground.ocean || ground.liquid && !ground.lava)) continue;
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
        // 先收尾再交给平滑国界：换图层翻牌记下的新样子要是画好的样子
        if ((PureMapMode.Enabled || Layer.CityViewMap.Active) && pZone?.tiles != null && __instance.pixels != null)
            FinishZone(__instance.pixels, pZone, false, false, false, false);
        SmoothBorders.Finish(__instance.pixels, pZone);
    }

    // 城市与道路：纯净地图下一直画；城市图层不论纯净地图开没开都画
    public static bool CitiesShown => PureMapMode.Enabled || Layer.CityViewMap.Active;

    // ───────── 地形明暗(只给城市精细层的路面用，见 PureMapPatch.Detail；国家色块不再画地形) ─────────
    // 掺地表本色的比例；块里的水掺水色的比例；高差打光的强度与上限
    private const float TerrainTint = 0.1f, WaterTint = 0.85f, SlopeLight = 0.003f, SlopeCap = 0.06f;
    private static readonly Color32 Water = new(140, 185, 235, 255);
    private static readonly global::System.Collections.Generic.Dictionary<TileTypeBase, float> TypeLight = new();
    // 并进国土的无人区国色的不透明度(城的地块的几成，见 FinishZone；平滑国界的整图画布同样淡，见 SmoothBorders.ColorZone)
    internal const float AbsorbedWildAlpha = 0.7f;

    // 色块画完的收尾：水面不涂国色；被敌军占着的地块画占领纹，兼属画斜纹。边框像素(原版按 pUp…pRight 画的外框)不动，保持清楚。
    // 城市图层不涂国色，色块全透明：城市在城市平面图层里(见 CityPlanOverlay)，地形露出来
    private static void FinishZone(Color32[] pixels, TileZone zone, bool up, bool down, bool left, bool right)
    {
        if (Layer.CityViewMap.Active)
        {
            foreach (WorldTile tile in zone.tiles)
            {
                int id = tile?.data?.tile_id ?? -1;
                if (id >= 0 && id < pixels.Length) pixels[id] = Clear;
            }
            return;
        }
        City city = zone.city != null && !zone.city.isRekt() ? zone.city : null;
        // 并进国土的无人区(挂了法理被某国取得、被一国包住的，见 WildernessTerritory、EnclaveFill)：国色淡一些(AbsorbedWildAlpha)，
        // 底下无人区图层的类型纹样透上来，看得出是这国的山脉、沙漠(用户 2026-10-29 "一旦任何一个国家获取了这个法理，那么这个无人地块
        // 就会自动被纳入国家范围")；没有城的地块在这里有颜色，只能是并进国土的
        bool wild = city == null && WildernessSystem.Enabled && WildernessSystem.IsWild(zone);
        Color32? occupied = OccupierFill(city, zone);
        // 兼属：王国法理、帝国法理、天下图层上画兼属方颜色的斜纹(见 DeJureShareSystem)
        Color32[] stripes = city == null ? null : DeJureShareSystem.StripeColors(city);
        // 平滑国界：被占的地、逐格推进吃下的格子直接按控制方上色(见 SmoothBorders)，这里不再画占领斜纹
        bool smooth = SmoothBorders.Enabled;
        if (smooth) occupied = null;
        foreach (WorldTile tile in zone.tiles)
        {
            int id = tile?.data?.tile_id ?? -1;
            if (id < 0 || id >= pixels.Length) continue;
            // 水面不涂国色(连地块边框也不画在水上)：领土只到海岸，国界贴着海岸走；城里过河的桥在城市平面图层里
            TileTypeBase ground = tile.Type;
            if (ground != null && (ground.ocean || ground.liquid && !ground.lava))
            {
                if (pixels[id].a == 0 && !smooth) continue;
                pixels[id] = Clear;
                continue;
            }
            if (pixels[id].a == 0) continue;
            WorldTileZoneBorder border = tile.world_tile_zone_border;
            bool edge;
            if (smooth)
            {
                edge = SmoothBorders.IsOutline(id);
                // 平滑后归邻国(邻城)的像素：不画本块的占领纹、兼属纹
                if (!edge && SmoothBorders.IsForeign(id)) continue;
            }
            else edge = border != null && border.border && (up && border.border_up || down && border.border_down ||
                                                            left && border.border_left || right && border.border_right);
            if (edge) continue;
            if (wild) pixels[id].a = (byte)(pixels[id].a * AbsorbedWildAlpha);
            // 斜纹同国色一样的透明度
            byte alpha = pixels[id].a;
            // 被敌军占着的地块：涂占领方的颜色，留一道道原主颜色的细斜纹
            if (occupied is Color32 fill && (tile.x + tile.y) % 4 != 0) pixels[id] = new Color32(fill.r, fill.g, fill.b, alpha);
            // 兼属：每两格换一种颜色，本色与兼属方的颜色轮流
            if (stripes != null && DeJureShareSystem.Band(tile, stripes.Length) is int band && band > 0)
            {
                Color32 stripe = stripes[band - 1];
                pixels[id] = new Color32(stripe.r, stripe.g, stripe.b, alpha);
            }
        }
    }

    // ───────── 城市平面图层(见 CityPlanOverlay) ─────────
    // 一块地每格的平面图颜色，没画东西的格透明。平面图的画法都是"在底色上按比例混进一种颜色"(街道、城墙、田垄……
    // 用 Lerp/Mix，影子是压暗)，以前直接混在国色上；底色分别取黑、白各画一遍，就能还原成"这种颜色、这么不透明"：
    // 黑底得 X·t，白底得 255·(1-t)+X·t。原样返回底色的格 t=0(透明)，不透明的建筑图块两遍一样 t=1
    private static readonly Color32 BlackBase = new(0, 0, 0, 255), WhiteBase = new(255, 255, 255, 255);

    // 返回所用平面图的指纹(城没了为 0)
    internal static int PlanZone(TileZone zone, bool vivid, Action<WorldTile, Color32> put)
    {
        City city = zone?.city;
        if (city?.data == null || city.isRekt() || zone.tiles == null) return 0;
        CityPlan plan = PlanFor(city);
        bool farm = FarmlandSystem.IsPlanned(city, zone);
        Slot[] slots = plan.zones.TryGetValue(zone.id, out Slot[] found) ? found : null;
        int ox = zone.x * Config.CITY_ZONE_SIZE, oy = zone.y * Config.CITY_ZONE_SIZE;
        UrbanFabric.Vivid = vivid;
        try
        {
            foreach (WorldTile tile in zone.tiles)
            {
                if (tile == null) continue;
                // 水面：城里干道、环路过河处是桥，其余透明
                TileTypeBase ground = tile.Type;
                if (ground != null && (ground.ocean || ground.liquid && !ground.lava))
                {
                    put(tile, UrbanFabric.Bridge(plan.fabric, tile) ? UrbanFabric.BridgeColor : Clear);
                    continue;
                }
                int lx = tile.x - ox, ly = tile.y - oy;
                put(tile, Unblend(Plan(tile, BlackBase, farm, slots, lx, ly, plan, zone),
                    Plan(tile, WhiteBase, farm, slots, lx, ly, plan, zone)));
            }
        }
        finally
        {
            UrbanFabric.Vivid = false;
        }
        return PlanStamp(city, plan);
    }

    // 黑底、白底两遍的结果还原成带不透明度的颜色(三个通道的混合比例相同，取平均抹掉取整误差)
    private static Color32 Unblend(Color32 dark, Color32 light)
    {
        float t = 1f - (light.r - dark.r + light.g - dark.g + light.b - dark.b) / (3f * 255f);
        if (t < 0.03f) return Clear;
        if (t > 1f) t = 1f;
        return new Color32((byte)Mathf.Min(255f, dark.r / t + 0.5f), (byte)Mathf.Min(255f, dark.g / t + 0.5f),
            (byte)Mathf.Min(255f, dark.b / t + 0.5f), (byte)Mathf.RoundToInt(t * 255f));
    }

    // 国家、帝国图层上被敌军占着的地块的颜色(占领方的国色，帝国图层下取它所属的帝国)；城池没易主，不然仍是原主的颜色，
    // 起义军、敌国打下的大片地方看不出归谁。友军、同一阵营的不算
    private static Color32? OccupierFill(City city, TileZone zone)
    {
        if (city?.kingdom == null) return null;
        MetaType? mode = World.world.getCachedMapMetaAsset()?.map_mode;
        bool empireLayer = mode == MetaTypeExtension.Empire;
        if (mode != MetaType.Kingdom && !empireLayer) return null;
        Kingdom occupier = city.GetTileZoneOccupier(zone);
        if (occupier?.data == null || occupier.isRekt() || occupier == city.kingdom || occupier.isInWarOnSameSide(city.kingdom) ||
            !FrontSystem.Hostile(city.kingdom, occupier))
            return null;
        Empire empire = empireLayer ? EmpireMembershipService.MapEmpire(occupier) : null;
        object meta = empire != null ? empire : occupier;
        ColorAsset color = CustomColorSystem.Of(meta) ?? (meta is Empire owner ? owner.getColor() : occupier.getColor());
        if (color == null) return null;
        Color32 fill = color.getColorBorderInsideAlpha32();
        fill.a = byte.MaxValue;
        return fill;
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
        // 本城的工厂区(见 FactorySystem、PureMapPatch.Factory)
        public readonly List<FactorySite> factories = new();
        public bool walled, axis, burning, moat;
        // 教廷驻地(普世教会的圣城、教廷握着)：城心地标画大教堂(见 PureMapPatch.Landmark 的 PapalCathedral)
        public bool papal;
        public int axisX = int.MinValue, axisY = int.MinValue;
        // 城心那块地的中线：城门只开在这两条线与城墙相交处(东西南北四门)，钟鼓楼、广场、清真寺在两线交点
        public int gateX = int.MinValue, gateY = int.MinValue, centerZone = -1;
        // 现代城市规划的过渡进度、残破度(0~1)
        public float modern, ruin;
        // 定型的文化系；改建中的目标文化系与进度(见 CityStyleSystem)
        public string family = "huaxia", target;
        public float rebuild;
        // 城市肌理：工业化以后的新城区、城外村镇农田(见 UrbanFabric)
        public UrbanFabric.Geometry fabric;
        // 住房研究到第几代
        public int housing = 1;
        // 原始聚落的阶段(见 PureMapPatch.Tribal)：-1 = 不是原始政体的城
        public int tribal = -1;
        // 游牧城的城心不在旱地上：画成冬营地(木土小屋、畜栏)，不画毡帐(见 RuralSettlementSystem.DryLand)；只管画法
        public bool winter;
        // 平面图的指纹(见 Stamp；平滑国界的上色缓存用)
        public int stamp = int.MinValue;
        // 手动规划(见 ApplyManual)：手动并入城区的地块(整块画成城区)、坊市疏密(0 照常，1 密，2 疏，见 PureMapPatch.Detail 的 Subdivide)
        public readonly HashSet<int> forced = new();
        public int blocks;
        // 每块地几个建筑位：自动的城 4；手动城区的城照坊市疏密 4、6、3(见 CityZoningSystem.SlotsPerZone、SlotIndex)
        public int slotCount = 4;
    }

    // 一块地里第几个建筑位的中心(地块内的格坐标，粗图、灯光、标注用)：前四个是四角的街区(西南、东南、西北、东北)，
    // 坊市密的第五、六个在正中一列的南、北(粗图只画得下四角，五、六只在精细图里画，见 SlotIndex)
    private static Vector2 SlotSpot(int index)
    {
        int half = Config.CITY_ZONE_SIZE / 2;
        if (index >= 4) return new Vector2(half, (index - 4) * half + half / 2f);
        return new Vector2(index % 2 * half + half / 2f, index / 2 * half + half / 2f);
    }

    // 精细图里一块宅基地(落在地块内 lx、ly 格)用第几个建筑位：四个位的按四角；六个位的(坊市密)东西两边按四角、正中一列(lx 3~5)
    // 南北各一个；三个位的(坊市疏)东北角那个位空着，多半画成树、园子、菜地(见 PureMapPatch.Detail 的 Lot)
    private static int SlotIndex(int count, int lx, int ly)
    {
        int half = Config.CITY_ZONE_SIZE / 2, row = ly >= half ? 1 : 0;
        if (count >= 6 && lx >= half - 1 && lx <= half + 1) return 4 + row;
        return row * 2 + (lx >= half ? 1 : 0);
    }

    // 本城平面图的指纹(没有城为 0)
    internal static int StampOf(City city)
    {
        if (city?.data == null || city.isRekt() || !PureMapMode.Enabled) return 0;
        return PlanStamp(city, PlanFor(city));
    }

    private static int PlanStamp(City city, CityPlan plan)
    {
        if (plan == null) return 0;
        if (plan.stamp == int.MinValue) plan.stamp = Stamp(city, plan);
        return plan.stamp;
    }

    private const string ModernPlanningTech = "modern_urban_planning";
    private const float ModernYears = 20f, RuinYears = 20f;
    // 改建时一个街区拆旧到建成新式之间算工地的那一段进度
    private const float ConstructionBand = 0.12f;
    // 高德式平面图(2026-10-14 用户选定)：城区是一片浅灰街区(带一点国色)，建筑是灰色占地块；
    // 路分三级——中轴、出城大街是黄色干道，现代环路橙色，地块之间的街白色，地块里的小巷只比街区亮一点；城墙、地标是重点
    private static readonly Color32 Ring = new(255, 178, 80, 255), Imperial = new(255, 222, 128, 255),
        PalaceWall = new(170, 60, 45, 255), Marble = new(244, 240, 230, 255), Lane = new(250, 249, 246, 255),
        Moat = new(164, 206, 244, 255), Chinampa = new(150, 200, 236, 255), Arterial = new(255, 222, 128, 255),
        Footprint = new(214, 210, 204, 255);
    private const float BlockShare = 0.72f, StreetShare = 0.95f;

    // 各文化系的城墙(没有的为 null)、中轴大道、地标拼块与广场地面
    private static Color32? WallOf(string family) => family switch
    {
        "huaxia" => new Color32(124, 112, 100, 255),
        "western" or "roma" or "persia" or "india" => new Color32(150, 145, 135, 255),
        "arab" or "egypt" => new Color32(190, 165, 120, 255),
        "viking" => new Color32(120, 90, 60, 255),
        _ => null
    };

    private static readonly Color32 Ward = new(226, 222, 214, 255), DrumRoof = new(70, 62, 66, 255),
        Plaza = new(236, 232, 224, 255);

    // 墩台：城墙颜色压暗
    private static Color32 TowerOf(Color32 wall) => new((byte)(wall.r * 0.7f), (byte)(wall.g * 0.7f), (byte)(wall.b * 0.7f), 255);

    private static Color32? AxisOf(string family) => family switch
    {
        "huaxia" => Imperial,
        "roma" => new Color32(200, 190, 170, 255),
        "aztec" => new Color32(235, 230, 220, 255),
        _ => null
    };

    private static bool Camp(string family) => family is "nomad" or "woodland";

    private static bool Maze(string family) => family is "arab" or "viking";

    private static readonly Dictionary<long, CityPlan> Plans = new();

    private static readonly Color32 Block = new(243, 241, 237, 255), Street = new(255, 255, 255, 255),
        Crop = new(214, 232, 186, 255);

    // 城市格局换了(换种子)：丢掉缓存的平面图，重画本城在城市平面图层里的样子(国家色块不画城市，不必重画)
    internal static void Redraw(City city)
    {
        if (city?.data == null) return;
        Plans.Remove(city.data.id);
        UI.Components.CityPlanOverlay.MarkCity(city);
    }

    // 画成城区的格(城墙以内、地标；近代以后城区边缘以内开发了的地块)：有自己的街格，路网图层的干道不压上去。
    // 用户 2026-10-17 "这个主干路有时候没法连在一起"：以前按整块建成地块不画，古代城的城区只占建成地块的一半，
    // 城墙外那一圈画成田野，干道就在田里提前断了。只看已经算好的平面图，不为此重算
    // (重算一座城的平面图不便宜，路网重画时顺带重算几十座城会卡一下)
    // 城市平面图里建成的地块(城区)；平面图还没算过的为 null(只读缓存，不为此重算，见 CityDistricts)
    internal static HashSet<int> BuiltZones(City city) =>
        city?.data != null && Plans.TryGetValue(city.data.id, out CityPlan plan) && plan.city == city ? plan.built : null;

    // 这座城画出来有没有城墙(现代拆成环路的不算；迁城时旧城址留有墙的大镇用，见 CityRelocationSystem)：
    // 按已算好的平面图，还没算过就现算一份
    internal static bool Walled(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones == null) return false;
        CityPlan plan = CachedPlan(city) ?? PlanFor(city);
        return plan.walled && plan.tribal < 0 && plan.fabric?.WallsDown != true;
    }

    // 这座城画的是冬营地(城貌小窗口用，见 CityStyleWindow)：按已算好的平面图，还没算过就现算一份
    internal static bool WinterCamp(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones == null) return false;
        return (CachedPlan(city) ?? PlanFor(city)).winter;
    }

    internal static bool UrbanTile(WorldTile tile)
    {
        City city = tile?.zone?.city;
        if (city?.data == null || city.isRekt() || !Plans.TryGetValue(city.data.id, out CityPlan plan) || plan.city != city) return false;
        return Urban(tile, plan) || UrbanFabric.DevelopedUrban(plan.fabric, tile);
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

    // 已经算好的平面图(不现算；没有为 null)
    private static CityPlan CachedPlan(City city) =>
        city?.data != null && Plans.TryGetValue(city.data.id, out CityPlan plan) && ReferenceEquals(plan.city, city) ? plan : null;

    // 已经算好的平面图的城市肌理(城郊的路、村镇小路；城池全图一键连路沿着它走，见 CityPlanWorks)：不现算，没有为 null
    internal static UrbanFabric.Geometry FabricOf(City city) => CachedPlan(city)?.fabric;

    // 按轮重建(见 RefreshUrbanSlice)：丢掉旧的现算一份，顺带记下指纹
    private static CityPlan RebuildPlan(City city)
    {
        Plans.Remove(city.data.id);
        CityPlan plan = PlanFor(city);
        plan.stamp = Stamp(city, plan);
        return plan;
    }

    // 本城的平面图：有就直接用(不论多久以前算的)，按轮更新见 RefreshUrbanSlice；读档后换了城对象的不算
    private static CityPlan PlanFor(City city)
    {
        if (Plans.TryGetValue(city.data.id, out CityPlan cached) && ReferenceEquals(cached.city, city)) return cached;
        float now = Time.unscaledTime;
        // 先记下所属的城：建图途中又查到本城时直接拿这一份，不会从头再建
        var plan = new CityPlan { at = now, city = city };
        if (Plans.Count > 4000) Plans.Clear();
        Plans[city.data.id] = plan;
        string culture = TechnologySystem.GetCultureOf(city) ?? "";
        bool Has(string tech) => !string.IsNullOrEmpty(culture) && TechnologySystem.HasTech(culture, tech);
        int housing = 1;
        for (int i = 0; i < HousingTechs.Length; i++) if (Has(HousingTechs[i])) housing = i + 1;
        int industryEra = Has("electricity") ? 2 : Has("industrialization") ? 1 : 0;
        // 已建起的楼不退回去(易主给科技落后的国家、易俗)：住宅代数取画到过的最高一代，工厂、市场、官署的时代随画到过的时代进度
        CityExtension.CityExtraData peak = city.GetOrCreate();
        if (PeakReady(city)) peak.urban_peak_housing = Mathf.Max(peak.urban_peak_housing, housing);
        housing = Mathf.Max(housing, peak.urban_peak_housing);
        if (peak.urban_peak_progress >= 1f) industryEra = 2;
        else if (peak.urban_peak_progress > 0f) industryEra = Mathf.Max(industryEra, 1);
        int modern = industryEra >= 2 ? 1 : 0;
        // 手动城区(见 CityZoningSystem)：建筑按实际所在的地块画，每块地的建筑位照坊市疏密；自动的城为 null
        HashSet<int> urban = CityZoningSystem.UrbanSet(city);
        plan.slotCount = urban != null ? CityZoningSystem.SlotsPerZone(city) : 4;
        // 各类建筑数(按数据；正在拆的不算)；没装工厂模组时的虚拟工厂也算
        var counts = new Dictionary<Kind, int>();
        foreach (Kind kind in Enum.GetValues(typeof(Kind))) counts[kind] = 0;
        var standing = new List<Building>();
        foreach (Building building in city.buildings ?? new List<Building>())
        {
            if (!Drawn(building)) continue;
            counts[KindOf(building.asset)]++;
            standing.Add(building);
        }
        counts[Kind.Industry] += VirtualIndustrySystem.Factories(city);
        // 官署至多画两座；手动城区的城每座都占一个建筑位，照实画
        if (urban == null) counts[Kind.Civic] = Mathf.Min(counts[Kind.Civic], MaxCivic);
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
        plan.housing = housing;
        TileZone centerZone = city.getTile()?.zone;
        List<TileZone> zones = city.zones.Where(zone => zone != null).OrderBy(zone => centerZone == null ? 0
            : (zone.x - centerZone.x) * (zone.x - centerZone.x) + (zone.y - centerZone.y) * (zone.y - centerZone.y)).ToList();
        if (urban != null)
        {
            PlaceActual(plan, standing, queues, zones, urban, centerZone);
            Layout(plan, city, culture, centerZone);
            return plan;
        }
        // 自动规划按城的格局(用户 2026-10-16 选"自动规划按时代")：方格城是坊市制，自然生长的城城心是集市(见 AutoWants)
        string established = CityStyleSystem.Established(city);
        bool grid = StyleOf(established)?.Layout is "grid" or "canal";
        int span = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(counts.Values.Sum() / 4f)));
        // 第一轮：各地块按自动规划先放对口的建筑(农耕区不放)；码头先放临水的地块
        foreach (TileZone zone in zones)
        {
            if (FarmlandSystem.IsPlanned(city, zone)) continue;
            bool water = zone.tiles.Any(tile => tile?.Type?.liquid == true);
            Kind[] wants = zone == centerZone ? grid ? new[] { Kind.Civic, Kind.Temple, Kind.Market, Kind.House }
                    : new[] { Kind.Market, Kind.Temple, Kind.Civic, Kind.House }
                : water ? new[] { Kind.Docks }
                : centerZone != null && !Camp(established) ? AutoWants(zone, centerZone, grid, span) : Array.Empty<Kind>();
            Fill(plan, zone, queues, wants);
        }
        // 第二轮：剩下的从城心往外按圈放进空着的街区(农耕区除外)，民居优先
        Kind[] order = { Kind.House, Kind.Civic, Kind.Market, Kind.Temple, Kind.Industry, Kind.Barracks, Kind.Docks };
        foreach (TileZone zone in zones)
        {
            if (queues.Values.All(queue => queue.Count == 0)) break;
            if (FarmlandSystem.IsPlanned(city, zone)) continue;
            Fill(plan, zone, queues, order);
        }
        Layout(plan, city, culture, centerZone);
        return plan;
    }

    // 自动规划(见 PlanFor)：
    //   · 方格城(华夏、日本、罗马、阿兹特克)是坊市制——城心官署，城心东西各一市，西北、东南两处寺庙，城南校场，其余是坊(民居)；
    //   · 自然生长、迷宫式的城城心是集市，东北一处寺庙，城南一处兵营，其余民居。
    // span：城大致几块地见方(按建筑数折算，每块地四座)
    private static Kind[] AutoWants(TileZone zone, TileZone center, bool grid, int span)
    {
        int dx = zone.x - center.x, dy = zone.y - center.y;
        int k = Mathf.Max(1, span / 4 + (span >= 4 ? 1 : 0));
        if (grid && dy == 0 && Mathf.Abs(dx) == k) return new[] { Kind.Market };
        if (span >= 3 && dx == 0 && dy == -Mathf.Max(1, span / 2 - 1)) return new[] { Kind.Barracks };
        if (grid ? span >= 3 && dx == -k && dy == k || span >= 5 && dx == k && dy == -k : span >= 3 && dx == 1 && dy == 1)
            return new[] { Kind.Temple };
        return new[] { Kind.House };
    }

    // 城池格局：定型的文化系、改建进度；城墙(规划过的城，游牧、林地、日本、阿兹特克不筑)、中轴(华夏、罗马、阿兹特克)、
    // 都城地标；现代规划过渡、残破度
    private static void Layout(CityPlan plan, City city, string culture, TileZone centerZone)
    {
        plan.city = city;
        plan.family = CityStyleSystem.Established(city);
        (plan.target, plan.rebuild) = CityStyleSystem.Rebuild(city);
        CityExtension.CityExtraData extra = city.GetOrCreate();
        foreach (int id in plan.zones.Keys) plan.built.Add(id);
        // 手动城区：城区地块(没有建筑的也算)一开始就算建成，城墙、中轴、宫城按它定，埃及的金字塔不落在城区里
        if (CityZoningSystem.UrbanSet(city) is HashSet<int> urban) plan.built.UnionWith(urban);
        plan.factories.AddRange(FactorySystem.InCity(city));
        if (centerZone != null && centerZone.city == city) plan.built.Add(centerZone.id);
        bool camp = Camp(plan.family);
        // 有城墙的文化，城一有两块地就筑墙；大城(六块以上)华夏、日本、西欧加护城河
        plan.walled = WallOf(plan.family) != null && plan.built.Count >= 2;
        plan.moat = plan.walled && plan.built.Count >= 6 && plan.family is "huaxia" or "japan" or "western";
        if (centerZone != null)
        {
            plan.gateX = centerZone.x * Config.CITY_ZONE_SIZE + 4;
            plan.gateY = centerZone.y * Config.CITY_ZONE_SIZE + 4;
            plan.centerZone = centerZone.id;
        }
        bool grand = centerZone != null && plan.built.Count >= 4;
        // 中轴：华夏、罗马的城三块地以上就有纵横两条大街(御道、主街)，阿兹特克的堤道要大城
        bool axial = centerZone != null && (plan.family is "huaxia" or "roma" ? plan.built.Count >= 3 : grand);
        if (axial && AxisOf(plan.family) != null)
        {
            plan.axis = true;
            plan.axisX = centerZone.x * Config.CITY_ZONE_SIZE + 4;
            plan.axisY = centerZone.y * Config.CITY_ZONE_SIZE + 4;
        }
        // 都城地标(宫城)：大城的都城；帝国核心的默认首都即皇宫，不论大小都有(见 CityPlacementSystem.BuildPalace)；
        // 易主后、手动营的宫城留着(见 HasPalace)。教廷驻地不论大小、是不是都城，城心都是大教堂(营地除外)
        plan.papal = centerZone != null && !camp && PapalSeat(city);
        if (centerZone != null && (plan.papal || HasPalace(city, extra, grand)))
        {
            // 埃及的金字塔在城外(城里第一块没建成的地块)，其余在城心；拼块式的地标大城连北面一块
            if (plan.family == "egypt" && !plan.papal)
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
        // 已改成现代规划的街区不退回去(同住宅代数)
        if (PeakReady(city)) extra.urban_peak_modern = Mathf.Max(extra.urban_peak_modern, plan.modern);
        plan.modern = Mathf.Max(plan.modern, extra.urban_peak_modern);
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
        // 原始政体的城画成原始聚落(见 PureMapPatch.Tribal)：不筑城墙、不挖护城河、没有中轴大街和都城地标。
        // 易主不改建筑：在非原始政体下画过城池的城(map_settled)易主给原始政体的部落，照旧画城池，城墙、宫城、关城、镇都留着
        if (!extra.map_settled && !Camp(plan.family) && SettledOwner(city, culture)) extra.map_settled = true;
        plan.tribal = Camp(plan.family) || extra.map_settled ? -1 : TribalStageOf(city, culture);
        // 毡帐只留在沙漠、荒原这类旱地上：城心那片地(建成地块)不是旱地的游牧城画成冬营地。按眼下的地形算，
        // 生物群系变了下回重算平面图就换画法；城市风格(map_style)和随它的人口上限、牧场保底、不修路都不动。
        // 城貌小窗口里手动定了毡帐营地、冬营地的照定的画(见 CityStyleSystem.SetByHand)
        plan.winter = plan.family == "nomad" && (extra.map_style_by_hand && extra.map_style_camp != 0
            ? extra.map_style_camp == 2
            : !RuralSettlementSystem.DryLand(city.zones.Where(zone => zone != null && plan.built.Contains(zone.id))));
        if (plan.tribal >= 0)
        {
            plan.walled = plan.moat = plan.axis = false;
            plan.landmark.Clear();
        }
        // 手动规划(城池全图里玩家改的，见 CityPlanEditing)：自动算完再按玩家的改动改，城市以后长大、改建不覆盖。
        // 挪了中轴交点、四门，城的轮廓仍以城心那块地的中线交点为中(不跟着挪)
        int townX = plan.gateX, townY = plan.gateY;
        ApplyManual(plan, city, extra);
        plan.fabric = UrbanFabric.Build(city, plan.built, plan.landmark, plan.family, culture, plan.gateX, plan.gateY,
            plan.forced, townX, townY, extra.plan_urban != null);
    }

    // 画不画宫城：大城的都城、皇宫(帝国核心的默认首都)照旧画。易主不拆宫城(用户 2026-10-20 "游牧入主中原这种情况，不能改变中原
    // 原有的建筑""至少不会拆掉")：画着宫城时记下主人，换了主人、不再是都城的照样留着(palace_kept，以后一直留着)；
    // 本国自己迁都，旧都的宫城照旧不画(一国一座)。营建页「营宫殿」手动营的也一直留着
    private static bool HasPalace(City city, CityExtension.CityExtraData extra, bool grand)
    {
        if (!PeakReady(city)) return extra.palace_kept || (grand || IsPalace(city)) && city.isCapitalCity();
        long owner = city.kingdom.id;
        if ((grand || IsPalace(city)) && city.isCapitalCity())
        {
            extra.palace_owner = owner;
            return true;
        }
        if (extra.palace_kept) return true;
        if (extra.palace_owner >= 0L && extra.palace_owner != owner)
        {
            extra.palace_kept = true;
            return true;
        }
        extra.palace_owner = -1L;
        return false;
    }

    // 读档、建图途中与没有主人的城不记宫城、最高时代(那时国家、科技还没读好)
    private static bool PeakReady(City city) =>
        global::Config.game_loaded && !SmoothLoader.isLoading() && city?.kingdom?.data != null && !city.kingdom.isRekt();

    // 主人不是原始政体(政体已定)：这座城画过城池，以后易主给部落也照旧
    private static bool SettledOwner(City city, string culture) =>
        PeakReady(city) && !city.kingdom.wild && CultureService.IsValidCulture(culture) &&
        city.kingdom.GetRegime() is EmpireCraft.Scripts.Regimes.Regime regime && !Regimes.RegimeManager.IsPrimitive(regime.type);

    // 皇宫：帝国核心记的默认首都(一个帝国只有一座)
    // 教廷驻地：普世教会(有教宗的那一路，见 ReligionFaith.IsPapal)的圣城，且教廷还在(圣城握在奉本教的国家手里，见 PapacySystem.Papacy)
    private static bool PapalSeat(City city)
    {
        if (city?.data == null || World.world?.religions == null) return false;
        foreach (Religion religion in World.world.religions)
            if (religion?.data != null && !religion.isRekt() && ReligionFaith.IsPapal(religion) &&
                PapacySystem.HolyCity(religion) == city && PapacySystem.Papacy(religion) != null)
                return true;
        return false;
    }

    private static bool IsPalace(City city)
    {
        Empire empire = city?.kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() && empire.CoreKingdom == city.kingdom &&
               EmpireCoreManager.Get(empire)?.GetCoreCapital() == city;
    }

    // 0~1 的稳定散列(地块、街区按它决定先后转现代、是否成废墟)
    private static float Hash01(long value) => (uint)(value * 2654435761L) / 4294967296f;

    // 平面图上画的建筑：建成的城市建筑(纯装饰、在建、正在拆的不画)
    private static bool Drawn(Building building) =>
        building?.asset != null && !building.isRekt() && !building.isOnRemove() && !building.isUnderConstruction() &&
        building.asset.building_type == BuildingType.Building_Civ && !PureMapMode.IsDecor(building.asset);

    // 手动城区的城(见 CityZoningSystem)：各建筑按实际所在的地块放进那块地的建筑位，离城心近的先放(民居照排好的代数由高到低)；
    // 不在城区的、不占建筑位的(仓库、矿场之类，见 CityZoningSystem.Managed)、没有实际位置的(虚拟工厂)放进离它最近、还有空位的城区地块，
    // 都满了就不画。城区地块整块算城区(见 ApplyManual)，没有建筑的城区地块也画成城区
    private static void PlaceActual(CityPlan plan, List<Building> standing, Dictionary<Kind, Queue<string>> queues, List<TileZone> zones,
        HashSet<int> urban, TileZone centerZone)
    {
        var rank = new Dictionary<int, int>();
        for (int i = 0; i < zones.Count; i++) rank[zones[i].id] = i;
        int RankOf(Building building) =>
            building.current_tile?.zone is TileZone zone && rank.TryGetValue(zone.id, out int r) ? r : int.MaxValue;
        standing.Sort((a, b) => RankOf(a).CompareTo(RankOf(b)));
        var spill = new List<(string icon, TileZone near)>();
        foreach (Building building in standing)
        {
            Queue<string> queue = queues[KindOf(building.asset)];
            if (queue.Count == 0) continue;
            string icon = queue.Dequeue();
            TileZone zone = building.current_tile?.zone;
            if (zone != null && urban.Contains(zone.id) && CityZoningSystem.Managed(building) && PutSlot(plan, zone.id, icon)) continue;
            spill.Add((icon, zone));
        }
        // 没有实际位置的(虚拟工厂)
        foreach (Queue<string> queue in queues.Values)
            while (queue.Count > 0) spill.Add((queue.Dequeue(), null));
        foreach ((string icon, TileZone near) in spill)
        {
            TileZone from = near ?? centerZone;
            TileZone best = null;
            int bestDistance = int.MaxValue;
            foreach (TileZone zone in zones)
            {
                if (!urban.Contains(zone.id) || plan.zones.TryGetValue(zone.id, out Slot[] used) && Array.TrueForAll(used, slot => slot.used))
                    continue;
                int distance = from == null ? 0 : (zone.x - from.x) * (zone.x - from.x) + (zone.y - from.y) * (zone.y - from.y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = zone;
            }
            if (best != null) PutSlot(plan, best.id, icon);
        }
    }

    // 放进这块地头一个空着的建筑位；满了返回 false
    private static bool PutSlot(CityPlan plan, int zoneId, string icon)
    {
        if (!plan.zones.TryGetValue(zoneId, out Slot[] slots)) plan.zones[zoneId] = slots = new Slot[plan.slotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].used) continue;
            slots[i] = new Slot { icon = icon, used = true };
            return true;
        }
        return false;
    }

    private static void Fill(CityPlan plan, TileZone zone, Dictionary<Kind, Queue<string>> queues, Kind[] wants)
    {
        if (wants.Length == 0) return;
        if (!plan.zones.TryGetValue(zone.id, out Slot[] slots)) slots = new Slot[plan.slotCount];
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
    private static Color32 Plan(WorldTile tile, Color32 color, bool farm, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone)
    {
        if (tile.Type != null && tile.Type.liquid) return color;
        // 工厂区：一整片厂房(见 PureMapPatch.Factory)
        if (plan != null && plan.factories.Count > 0 && FactoryCoarse(tile, color, plan) is Color32 works) return works;
        // 原始聚落：环壕或围垣、小屋点、广场；城外不画路、镇(那时还没有)，划了农田的照画垄沟
        if (plan != null && plan.tribal >= 0)
        {
            if (TribalCoarse(tile, color, plan) is Color32 tribe) return tribe;
            if (UrbanFabric.FortTile(plan.fabric, tile, color, out Color32 fort)) return fort;
            return farm && plan.fabric != null ? UrbanFabric.Farmland(plan.fabric, tile, color) : color;
        }
        bool built = plan != null && Urban(tile, plan);
        UrbanFabric.Geometry fabric = plan?.fabric;
        bool wallsDown = fabric != null && fabric.WallsDown;
        if (built && fabric != null && fabric.Modern && UrbanFabric.Tile(tile, color, fabric, true, out Color32 urban))
            return urban;
        if (built && lx >= 0 && lx < 8 && ly >= 0 && ly < 8)
        {
            bool modern = plan.modern > 0f && Hash01(zone.id) < plan.modern;
            // 这个地块的布局按哪一系(改建中逐个地块换)
            string family = plan.target != null && Hash01(zone.id * 31L + 99L) < plan.rebuild ? plan.target : plan.family;
            // 城墙：现代规划后拆成环路；东西南北四门开在城心两条中线上，门两侧与凸角是墩台
            if (plan.walled && !wallsDown && UrbanEdge(tile, plan))
            {
                if (tile.x == plan.gateX || tile.y == plan.gateY) return Lerp(color, Arterial, StreetShare);
                if (modern) return Lerp(color, Ring, StreetShare);
                Color32 wall = WallOf(plan.family) ?? Ring;
                bool tower = Mathf.Abs(tile.x - plan.gateX) == 1 || Mathf.Abs(tile.y - plan.gateY) == 1 || Corner(tile, plan);
                return Lerp(color, tower ? TowerOf(wall) : wall, 0.85f);
            }
            if (plan.landmark.Contains(zone.id)) return Landmark(tile, color, plan, lx, ly);
            if (!modern && Centerpiece(tile, color, plan, family) is Color32 centerpiece) return centerpiece;
            // 中轴大道(华夏御道、罗马主街、阿兹特克堤道)
            if (plan.axis && (tile.x == plan.axisX || tile.y == plan.axisY)) return Lerp(color, AxisOf(plan.family) ?? Arterial, StreetShare);
            if (modern) return Grid(color, slots, lx, ly, plan, zone, true);
            if (Camp(family)) return CampLayout(color, slots, lx, ly, plan, zone);
            // 华夏里坊：每块地一坊，四面坊墙、正中开坊门，坊内十字街
            if (family == "huaxia") return WardLayout(color, slots, lx, ly, plan, zone);
            // 西欧：街巷弯曲，从广场通向各城门的大街笔直
            if (family == "western")
                return tile.x == plan.gateX || tile.y == plan.gateY ? Lerp(color, Arterial, StreetShare) : Organic(color, slots, lx, ly, plan, zone);
            if (Maze(family))
                return lx == 4 || ly == 4 ? Lerp(color, Lane, 0.6f) : Organic(color, slots, lx, ly, plan, zone);
            return StyleOf(family)?.Layout is "organic" ? Organic(color, slots, lx, ly, plan, zone)
                : Grid(color, slots, lx, ly, plan, zone, false);
        }
        // 护城河：贴着城墙外侧一圈，城门处是桥
        if (plan != null && plan.moat && !wallsDown && BesideWall(tile, plan))
            return tile.x == plan.gateX || tile.y == plan.gateY ? Lerp(color, Arterial, StreetShare) : Lerp(color, Moat, 0.9f);
        // 城外：村镇、田块；工业化以后的新城区(见 UrbanFabric)
        if (!built && fabric != null && UrbanFabric.Tile(tile, color, fabric, false, out Color32 outside))
            return outside;
        // 农耕区：一小块一小块的条田(见 UrbanFabric.Farmland)，块间留空地——AI 缺粮时会把各城至多一半的地块划成农田，
        // 整片涂满就成了大片；阿兹特克的水上田园每三行一道水渠
        if (farm)
        {
            if (plan?.family == "aztec") return tile.y % 3 == 0 ? Lerp(color, Chinampa, 0.5f) : color;
            if (fabric != null) return UrbanFabric.Farmland(fabric, tile, color);
        }
        return color;
    }

    // 城墙的凸角：四邻有两格以上在城外
    private static bool Corner(WorldTile tile, CityPlan plan)
    {
        int outside = 0;
        foreach (WorldTile next in new[] { tile.tile_up, tile.tile_down, tile.tile_left, tile.tile_right })
            if (!Urban(next, plan)) outside++;
        return outside >= 2;
    }

    // 城墙外侧紧挨着的一格(护城河)
    private static bool BesideWall(WorldTile tile, CityPlan plan)
    {
        if (!UrbanFabric.NearOutline(plan.fabric, tile) || Urban(tile, plan)) return false;
        foreach (WorldTile next in new[] { tile.tile_up, tile.tile_down, tile.tile_left, tile.tile_right })
            if (Urban(next, plan) && UrbanEdge(next, plan)) return true;
        return false;
    }

    // 城心的标志(都城地标另画)：华夏钟鼓楼立在两条大街交口；西欧集市广场、北面一座教堂；罗马广场(北面神庙)；
    // 阿拉伯清真寺院落。两条中线交点为心
    private static Color32? Centerpiece(WorldTile tile, Color32 color, CityPlan plan, string family)
    {
        if (plan.gateX == int.MinValue || plan.built.Count < 2) return null;
        int dx = tile.x - plan.gateX, dy = tile.y - plan.gateY;
        switch (family)
        {
            case "huaxia":
                if (Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1) return null;
                return dx == 0 && dy == 0 ? Lerp(color, DrumRoof, 0.95f) : Lerp(color, PalaceWall, 0.9f);
            case "western":
            case "roma":
            {
                // 教堂、神庙(3×3 图块)在广场北面
                if (dy >= 3 && dy <= 5 && Mathf.Abs(dx) <= 1 && Icon(StyleIcon(family, "temple")) is Color32[] temple)
                {
                    Color32 pixel = temple[(2 - (dy - 3)) * 3 + dx + 1];
                    return pixel.a == 0 ? Lerp(color, Plaza, 0.85f) : Lerp(color, pixel, 0.9f);
                }
                if (dx * dx + dy * dy <= (family == "roma" ? 8 : 6)) return Lerp(color, Plaza, 0.85f);
                return null;
            }
            case "arab":
            {
                // 清真寺：外圈院墙、院中礼拜殿、一角宣礼塔
                if (Mathf.Abs(dx) > 2 || Mathf.Abs(dy) > 2) return null;
                if (dx == 2 && dy == 2) return Lerp(color, new Color32(244, 240, 228, 255), 0.95f);
                if (Mathf.Abs(dx) == 2 || Mathf.Abs(dy) == 2) return Lerp(color, new Color32(238, 230, 210, 255), 0.9f);
                return Mathf.Abs(dx) <= 1 && dy >= 0 ? Lerp(color, new Color32(90, 140, 120, 255), 0.85f)
                    : Lerp(color, Plaza, 0.85f);
            }
        }
        return null;
    }

    // 华夏里坊：西、南两边是坊墙(东、北两边是邻坊的墙)，坊墙正中开坊门，坊内十字街分出四个街区
    private static Color32 WardLayout(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone)
    {
        // 地块之间是白色的街(坊墙不另画，免得每四格一道亮线、满城格子)；坊内十字只是淡淡的巷
        if (lx == 0 || ly == 0) return Lerp(color, Street, StreetShare);
        if (lx == 4 || ly == 4) return Lerp(color, Lane, 0.85f);
        int index = (ly - 1) / 4 * 2 + (lx - 1) / 4;
        return Building(color, slots, index, (lx - 1) % 4, (ly - 1) % 4, plan, zone);
    }

    // 城里的一格：建成的地块落在城的轮廓里(见 UrbanFabric.InTown)；都城地标整块算
    private static bool Urban(WorldTile tile, CityPlan plan) =>
        tile?.zone != null && tile.zone.city == plan.city && (plan.built.Contains(tile.zone.id) &&
        (plan.landmark.Contains(tile.zone.id) || UrbanFabric.InTown(plan.fabric, tile)));

    // 城的外缘(城墙一线)
    private static bool UrbanEdge(WorldTile tile, CityPlan plan) =>
        !Urban(tile.tile_up, plan) || !Urban(tile.tile_down, plan) || !Urban(tile.tile_left, plan) || !Urban(tile.tile_right, plan);

    // 区域的外缘：四邻有一格不在这片区域(或不归这座城)
    private static bool OnEdge(WorldTile tile, HashSet<int> region, City city)
    {
        return Outside(tile.tile_up) || Outside(tile.tile_down) || Outside(tile.tile_left) || Outside(tile.tile_right);
        bool Outside(WorldTile next) => next?.zone == null || next.zone.city != city || !region.Contains(next.zone.id);
    }

    // 规划过的地块：白色街格切出四个街区，建筑图块整齐摆进去；现代格局的地块边换成大道
    private static Color32 Grid(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone, bool modern)
    {
        // 地块之间是白色的街(现代格局是大道)，地块里的十字只是淡淡的巷
        if (lx == 0 || ly == 0) return Lerp(color, modern ? Ring : Street, StreetShare);
        if (lx == 4 || ly == 4) return Lerp(color, Lane, 0.85f);
        int index = (ly - 1) / 4 * 2 + (lx - 1) / 4;
        return Building(color, slots, index, (lx - 1) % 4, (ly - 1) % 4, plan, zone);
    }

    // 没规划的地块(阿拉伯、维京整城都这样)：自发生长，弯曲的土路小巷穿过，房子在四个角落里错落摆放
    private static Color32 Organic(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone)
    {
        uint hash = (uint)(zone.id * 2654435761L);
        int laneX = 3 + (int)((hash >> (ly % 8 * 2)) & 1);
        int laneY = 3 + (int)((hash >> (16 + lx % 8 * 2)) & 1);
        if (lx == laneX || ly == laneY) return Lerp(color, Lane, 0.85f);
        return Scattered(color, slots, lx, ly, plan, zone, hash, BlockShare);
    }

    // 营地(游牧、林地)：没有街巷，帐篷、棚屋在草地上错落围成一圈
    private static Color32 CampLayout(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone) =>
        Scattered(color, slots, lx, ly, plan, zone, (uint)(zone.id * 2654435761L), 0.25f);

    // 四个角落里各一座，错开一格摆；空地按 ground 提亮(城里是街区底色，营地是踩出来的草地)
    private static Color32 Scattered(Color32 color, Slot[] slots, int lx, int ly, CityPlan plan, TileZone zone, uint hash,
        float ground)
    {
        int qx = lx < 4 ? 0 : 1, qy = ly < 4 ? 0 : 1;
        int index = qy * 2 + qx;
        int ox = (int)((hash >> (index * 2)) & 1), oy = (int)((hash >> (8 + index * 2)) & 1);
        int px = lx - qx * 4 - ox, py = ly - qy * 4 - oy;
        if (px < 0 || px > 2 || py < 0 || py > 2) return Lerp(color, Block, ground);
        return Building(color, slots, index, px, py, plan, zone);
    }

    // 街区里的建筑：残破的画废墟；改建中先拆旧起工地、再建成新式；否则按这座城定型的文化系画这座建筑的图块
    private static Color32 Building(Color32 color, Slot[] slots, int index, int px, int py, CityPlan plan, TileZone zone)
    {
        Color32 baseColor = Lerp(color, Block, BlockShare);
        if (slots == null || index < 0 || index >= slots.Length || !slots[index].used) return baseColor;
        string name;
        Color32[] winter = null;
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
            // 冬营地(城心不在旱地上的游牧城)：毡帐图块换成木土小屋、畜栏
            if (family == "nomad" && plan.winter) winter = WinterIcon(slots[index].icon, zone.id * 4L + index);
        }
        Color32[] icon = winter ?? Icon(name) ?? Icon(slots[index].icon);
        if (icon == null) return Lerp(baseColor, Footprint, 0.85f);
        Color32 pixel = icon[(2 - py) * 3 + px];
        // 建筑是灰色占地块，只留一点屋顶本色
        return pixel.a == 0 ? baseColor : Lerp(baseColor, Lerp(pixel, Footprint, 0.6f), 0.85f);
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
        // 教廷驻地：大教堂拼块(尖塔教堂)，柱廊广场不设围墙
        string family = plan.papal ? "papal" : plan.family;
        switch (family)
        {
            case "egypt": return Pyramid(color, lx, ly);
            case "aztec": return SteppedPyramid(color, lx, ly);
            case "nomad": return plan.winter ? WinterHall(color, lx, ly) : GreatTent(color, lx, ly);
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
            "papal" => ("western_temple", null, Marble),
            _ => ("palace_hall", (Color32?)PalaceWall, Marble)
        };
        // 中轴穿过这块地标才在中轴上开门(手动挪了宫城、中轴的，中轴不一定穿过宫城，见 ApplyManual)，不然在这块地正中开门
        bool gateColumn = plan.axis && plan.axisX >= 0 && plan.axisX / Config.CITY_ZONE_SIZE == tile.zone.x ? tile.x == plan.axisX : lx == 4;
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

    // 冬营地的图块(3×3，第一行在北)：木土小屋(土黄屋顶、正中一道屋脊)、畜栏(四角木桩、中间一只羊)、
    // 官署是一座大些的土屋、寺庙是一堆石头；集市是牲口栏；兵营、码头、作坊照通用图块
    private static readonly Color32 WinterRoof = new(178, 142, 94, 255), WinterRidge = new(122, 94, 62, 255),
        WinterFence = new(124, 96, 64, 255), WinterSheep = new(244, 240, 230, 255), WinterStone = new(168, 162, 150, 255),
        WinterStoneDark = new(120, 114, 104, 255), WinterNone = new(0, 0, 0, 0);
    private static readonly Color32[] WinterHutTile =
        { WinterRoof, WinterRidge, WinterRoof, WinterRoof, WinterRidge, WinterRoof, WinterNone, WinterNone, WinterNone };
    private static readonly Color32[] WinterPenTile =
        { WinterFence, WinterNone, WinterFence, WinterNone, WinterSheep, WinterNone, WinterFence, WinterNone, WinterFence };
    private static readonly Color32[] WinterLodgeTile =
        { WinterRoof, WinterRoof, WinterRoof, WinterRidge, WinterRidge, WinterRidge, WinterRoof, WinterRoof, WinterRoof };
    private static readonly Color32[] WinterCairnTile =
        { WinterNone, WinterStone, WinterNone, WinterStone, WinterStoneDark, WinterStone, WinterNone, WinterStone, WinterNone };

    private static Color32[] WinterIcon(string icon, long key)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        if (icon.StartsWith("house_")) return Hash01(key * 31L + 5L) < 0.3f ? WinterPenTile : WinterHutTile;
        if (icon.StartsWith("civic_")) return WinterLodgeTile;
        if (icon.StartsWith("market_")) return WinterPenTile;
        return icon == "temple" ? WinterCairnTile : null;
    }

    // 冬营地的大帐换成议事的大土屋(俯视)：一圈木栅(南面开门)，院里一座土屋，东西一道屋脊
    private static Color32 WinterHall(Color32 color, int lx, int ly)
    {
        float dx = lx - 3.5f, dy = ly - 3.5f;
        float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        if (d > 3f)
        {
            // 南门(地块里 ly 小的一边是南)
            if (dy < 0f && Mathf.Abs(dx) < 1f) return Lerp(color, Block, 0.12f);
            return Lerp(color, (lx + ly) % 2 == 0 ? new Color32(108, 82, 54, 255) : new Color32(150, 116, 76, 255), 0.9f);
        }
        if (Mathf.Abs(dx) <= 2.5f && Mathf.Abs(dy) <= 1.5f)
            return Lerp(color, Mathf.Abs(dy) < 0.6f ? WinterRidge : WinterRoof, 0.95f);
        return Lerp(color, Block, 0.12f);
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
        Kingdom other = zone?.city?.kingdom ?? UI.Components.WildernessTerritory.Owner(zone);
        if (other == null) return false;
        return (ZhouEraSystem.SphereOf(other) ?? EmpireMembershipService.MapEmpire(other)) != empire;
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

    // 国家、帝国定了自定义颜色(见 CustomColorSystem)：这块地的底色、边框换成它(任何模式都生效)
    private static ColorAsset CustomColor(TileZone zone)
    {
        if (zone == null || !CustomColorSystem.Any) return null;
        MetaTypeAsset layer = World.world.getCachedMapMetaAsset();
        if (layer == null || layer.map_mode != MetaType.Kingdom && layer.map_mode != MetaTypeExtension.Empire) return null;
        return CustomColorSystem.Of(layer.tile_get_metaobject?.Invoke(zone, layer.getZoneOptionState()));
    }

    private static Color32 Recolor(Color32 original, Color32 custom)
    {
        custom.a = original.a;
        return custom;
    }

    private static void BeforeApplyColors(TileZone pZone, ref Color32 pColorInside, ref Color32 pColorOutside)
    {
        ColorAsset custom = CustomColor(pZone);
        if (custom != null)
        {
            if (pColorInside.a > 0) pColorInside = Recolor(pColorInside, custom.getColorBorderInsideAlpha32());
            if (pColorOutside.a > 0) pColorOutside = Recolor(pColorOutside, custom.getColorMainSecond32());
        }
    }

    private static bool SmoothPaint(ZoneCalculator __instance, TileZone pZone, ref Color32 pColorInside, ref Color32 pColorOutside)
    {
        if (SmoothBorders.Paint(__instance, pZone, pColorInside, pColorOutside)) return false;
        // 原版画法的图层(教化、政令这类)：换图层时也翻牌(见 SmoothBorders.CaptureRaw)
        SmoothBorders.CaptureRaw(pZone);
        return true;
    }

    private static void BeforeApplyColorsFull(TileZone pZone, ref Color32 pColor)
    {
        SmoothBorders.CaptureRaw(pZone);
        ColorAsset custom = CustomColor(pZone);
        if (custom != null && pColor.a > 0) pColor = Recolor(pColor, custom.getColorBorderInsideAlpha32());
    }

    private static bool BeforeColorZone(ZoneCalculator __instance, TileZone pZone, ref Color32 pColor)
    {
        if ((!PureMapMode.Enabled && !SmoothBorders.Enabled) || pZone?.tiles == null || __instance.pixels == null) return true;
        SmoothBorders.CaptureRaw(pZone);
        // 原版只比较第一格，第一格已透明就跳过整块清色。平滑后同一块里颜色不同，
        // 第一格可能是海水/无主地，余下却还有旧国色和边框；清掉每一格才能真正撤掉旧图层。
        foreach (WorldTile tile in pZone.tiles) __instance.pixels[tile.data.tile_id] = pColor;
        SmoothBorders.Finish(__instance.pixels, pZone);
        __instance.setDirty();
        return false;
    }

    private static bool BeforeColorZonesList(List<TileZone> pZones) => !SmoothBorders.Highlight(pZones);
    private static bool BeforeColorZonesPool(ListPool<TileZone> pZones) => !SmoothBorders.Highlight(pZones);

    // 谋划图标：人物到处瞬移，图标画在参与者所在城市的城心上方(同一座城的参与者左右错开)，不跟着人跳
    private static bool BeforeDrawPlotIcon(QuantumSpriteAsset pAsset, Plot pPlot)
    {
        if (!PureMapMode.Enabled || pPlot?.units == null) return true;
        int index = 0;
        foreach (Actor unit in pPlot.units)
        {
            if (unit == null || unit.isRekt() || unit.current_zone == null) continue;
            City city = unit.city;
            WorldTile anchor = city?.getTile();
            Vector3 position = anchor != null ? (Vector3)anchor.posV : unit.current_position;
            TileZone zone = anchor?.zone ?? unit.current_zone;
            if (zone == null || !zone.visible) continue;
            float lift = 5.5f * QuantumSpriteLibrary.getCameraScaleZoomMultiplier(pAsset);
            if (city != null) lift *= city.mark_scale_effect;
            position.y += lift;
            position.x += (index++ - (pPlot.units.Count - 1) * 0.5f) * lift * 0.6f;
            QuantumSprite sprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, position, null, null, city, null,
                pPlot.transition_animation);
            Sprite icon = pPlot.getSprite();
            sprite.setSprite(icon);
            CircleIconShaderMod shader = sprite.GetComponent<CircleIconShaderMod>();
            shader.sprite_renderer_with_mat.sprite = icon;
            shader.setShaderVal(pPlot.getProgressMod());
        }
        return false;
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
