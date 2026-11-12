using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 城市扩张守原版地貌定居、接壤和容量规则，在可用候选中优先沿低阻力、连通的地形扩展；
// 原版还要小人走到那块地里去领，走不到(隔水、翻山)就领不到。
// 保留每城地块上限、地形可达性与虚拟居民代领的核对；自动扩张的候选再按地形阻力和接壤面排序。
// 城的地按村镇拓展(用户 2026-10-17 选"新建村镇就划进它一片"、限制"按离城远近")：纯净地图下，无主之地只领城区(城心、
// 建成地块)或本城村镇两块地以内的(见 Settled)；再往外靠城外新建村镇划地(见 RuralSettlementSystem.Found)。
// 夹在城与城之间的空地不受这些限制，由挨着它的城里离得最近的那座领下(见 Sandwiched)。
// 已有城市的地不参与自动扩张：战争只记录临时占领，不能用边界侵蚀永久拆改城市辖区。
// 无人区(见 WildernessSystem)与城互斥：把地给城的路子碰到无人区一律挡在 CanAddZone(原版 addZone 都经它)，新城不选在无人区里
// (见 AfterCanStartCityHere)；神力手动营建、建城放行的那块地归城后当即退出无人区(见 AfterAddZone)。
// 预划县址(见 WildernessPlotSystem)同样挡在这两处：县址里的地只给这一县的县城(没设县的谁也不领)，已设县的县址里不另立新城
public class CityExpansionPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CityExpansionPatch));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.canGrowZones)),
            postfix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(AfterCanGrowZones)));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.isZoneToClaimStillGood)),
            postfix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(AfterIsZoneToClaimStillGood)));
        harmony.Patch(AccessTools.Method(typeof(CityZoneGrowth), nameof(CityZoneGrowth.getZoneToClaim)),
            prefix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(BeforeGetZoneToClaim)),
            postfix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(AfterGetZoneToClaim)));
        harmony.Patch(AccessTools.Method(typeof(CityZoneAbandon), nameof(CityZoneAbandon.check)),
            prefix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(BeforeAbandonZones)));
        // 无人区：地块归了城就退出无人区；新城(移民、殖民、穿越者降临、原版建城)不选在无人区里
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.addZone)),
            postfix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(AfterAddZone)));
        harmony.Patch(AccessTools.Method(typeof(TileZone), nameof(TileZone.canStartCityHere)),
            postfix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(AfterCanStartCityHere)));
        // 原版边界画笔(神力 border_brush)：画的这一下算神力手动划地，不受每城地块上限(见 CanAddZone)
        global::System.Reflection.MethodInfo brush = AccessTools.Method(typeof(PowerLibrary), "drawBorderBrush");
        if (brush != null)
            harmony.Patch(brush, prefix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(BeginManual)),
                finalizer: new HarmonyMethod(typeof(CityExpansionPatch), nameof(EndManual)));
        // 边界剪刀同样是神力手动划地；新城领落脚那一小片不算边界变动(都见 CityTerritoryProtection.BordersLocked)
        global::System.Reflection.MethodInfo scissors = AccessTools.Method(typeof(PowerLibrary), "drawScissors");
        if (scissors != null)
            harmony.Patch(scissors, prefix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(BeginManual)),
                finalizer: new HarmonyMethod(typeof(CityExpansionPatch), nameof(EndManual)));
        global::System.Reflection.MethodInfo founding = AccessTools.Method(typeof(CityManager), nameof(CityManager.buildNewCity));
        if (founding != null)
            harmony.Patch(founding, prefix: new HarmonyMethod(typeof(CityTerritoryProtection), nameof(CityTerritoryProtection.BeginFounding)),
                finalizer: new HarmonyMethod(typeof(CityTerritoryProtection), nameof(CityTerritoryProtection.EndFounding)));
    }

    public static bool AtLimit(City city)
    {
        if (city?.zones == null) return false;
        int limit = ZoneLimit;
        return limit > 0 && city.zones.Count >= limit;
    }

    // 每城地块上限：直接读模组设置里的当前值(至多一秒读一次)，不只靠回调写进 ModClass.CITY_MAX_ZONES(用户 2026-11-05 转群友
    // "城市面积限制的设置失效")。加载器按"类名:方法名"找回调，类名只写了 ConfigActions、靠按名字搜所有程序集，装了别的有同名类的
    // 模组时可能找错、回调不调，设置改了不生效；读到的值与 CITY_MAX_ZONES 不同时顺手写回去(移民等别处也用它)
    private static int _limit = -1;
    private static float _limitAt = -100f;

    public static int ZoneLimit
    {
        get
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_limit >= 0 && now >= _limitAt && now - _limitAt < 1f) return _limit;
            _limitAt = now;
            int value = ModClass.CITY_MAX_ZONES;
            try
            {
                if (ModClass.modConfig?["Default"] is { } items && items.TryGetValue("City_Max_Zones_Text", out ModConfigItem item) &&
                    int.TryParse(item.TextVal, out int parsed))
                    value = global::System.Math.Max(0, parsed);
            }
            catch (global::System.Exception)
            {
                // 设置还没读进来：照 CITY_MAX_ZONES
            }
            if (value != ModClass.CITY_MAX_ZONES)
            {
                NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][城市扩张] 每城地块上限按设置改为 {value}(原 {ModClass.CITY_MAX_ZONES}，回调没把新值写进来)");
                ModClass.CITY_MAX_ZONES = value;
            }
            _limit = value;
            return value;
        }
    }

    // 神力手动划地(原版边界画笔、村镇改隶)期间：玩家是神，划地不受每城地块上限。
    // 上限只管自动扩张(不设上限时一大片荒漠会裂成两百来座小城)；以前画笔也被它拦下，城满了上限就一块也画不进去
    // (玩家反馈"绘图工具绘不了城市边界了，擦除倒是还能正常")。自动扩张的规矩(Settled、走得到)本来就不在 addZone 里查
    private static int _manual;
    public static bool ManualClaim => _manual > 0;
    public static void BeginManual() => _manual++;
    public static void EndManual()
    {
        if (_manual > 0) _manual--;
    }

    // using (CityExpansionPatch.Manual()) { city.addZone(...); }：手动划地的一段，出了这段(包括出错)就恢复上限
    public static ManualScope Manual()
    {
        BeginManual();
        return default;
    }

    public readonly struct ManualScope : global::System.IDisposable
    {
        public void Dispose() => EndManual();
    }

    // 最后一道检查放在实际加地之前：领地人、村镇划地等自动入口都不能绕过设置；神力手动划地不受限(见 ManualClaim)。
    // 读档同样调用 addZone，但这里是在恢复原辖区，不能按新上限截断存档里的城市。
    // 无人区(用户 2026-10-29 "无人区层基本和城市层级同级")：扩张、村镇划地、改隶、迁城、边界画笔都领不进去；
    // 只放行神力手动立村设镇筑关、放厂区、建城与新城落脚的头一块地(见 WildernessSystem.CanTake)
    public static bool CanAddZone(City city, TileZone zone)
    {
        if (city == null || zone == null) return false;
        if (city.zones?.Contains(zone) == true || ModClass.IS_CLEAR || !Config.game_loaded || SmoothLoader.isLoading() ||
            AncientWarfareCompatibility.OwnsObject(city)) return true;
        if (CityTerritoryProtection.LocksAdd(city)) return false;
        if (WildernessSystem.IsWild(zone) && !WildernessSystem.CanTake(city) && !WildernessSystem.SettlesPlateau(city, zone)) return false;
        // 高原外缘作界线(见 PlateauSides)：城不翻外缘往那边扩；神力手动划地、建城与新城落脚的头一块地照旧放行
        if (!ManualClaim && !WildernessSystem.CanTake(city) && PlateauSides.Differ(city, zone)) return false;
        // 预划县址(用户 2026-11-03 转群友"原有的城市跨河连片，本来该断掉的地方不断")：县址里开出来的地只给这一县已设的县城，
        // 没设县的谁也不领(老城不再顺着开出来的地一块块吞过去)；神力手动划地、手动营建、建城与新城落脚的头一块地照旧放行
        // (新城落在没设县的县址里就是这一县的县城，见 WildernessPlotSystem.OnCityBuilt)
        if (!ManualClaim && !WildernessSystem.CanTake(city) && !WildernessPlotSystem.MayTake(city, zone)) return false;
        return ManualClaim || !AtLimit(city);
    }

    // 地块归了城：当即退出无人区(两者互斥；停手后重划，区域在那里断开)
    public static void AfterAddZone(City __instance, TileZone pZone)
    {
        if (pZone != null && pZone.city == __instance) WildernessSystem.OnZoneTaken(pZone);
    }

    // 新城不选在无人区里(原版建城、国家移民、殖民、穿越者降临选址都问它)；神力手动建城不问它，照旧能建。
    // 也不选在已设县的县址里(县城以外不再挤进第二座城，见 WildernessPlotSystem.Taken)；没设县的县址照选，立了就是这一县的县城
    public static void AfterCanStartCityHere(TileZone __instance, ref bool __result)
    {
        if (__result && (WildernessSystem.IsWild(__instance) || WildernessPlotSystem.Taken(__instance))) __result = false;
    }

    // 自动领地只领无主地；与手动村镇改隶、建城、读档的 addZone 分开。
    public static bool CanClaimZone(City city, TileZone zone) =>
        city != null && zone != null && zone.city == null && CanAddZone(city, zone);

    // 原版按剩余建筑找连通地块，战损后会把原辖区当作弃地删除；城市格局不能因此变化。
    // 地块固定(原版"领土扩张"法则关着，世界地图剧本开局就关，见 WorldMapScenario.SetupLaws)时不弃地：扔掉的地再也领不回来，
    // 剧本按地形、州府锚点铺好的辖地(隔河、翻岭、山口两边的)一失地块就整片被扔，运行一阵就缩边了(用户 10-30 "为什么这边
    // 运行了一段时间后就缩边了")
    public static bool BeforeAbandonZones(City pCity, bool pDebug) =>
        pDebug || !CityTerritoryProtection.BordersLocked && !CityTerritoryProtection.PreserveWarLayout(pCity) &&
        WorldLawLibrary.world_law_kingdom_expansion.isEnabled();

    // 城区或本城村镇几块地以内
    public const int SettledReach = 2;

    // 城界不成方块(用户 2026-11-12 转群友"城市扩张的时候能随机一点吗 怎么一个方块一个方块扩的……铺满时候发现个个都是
    // 长方形城市边界")。以前"两块地以内"按方框算(城心、每处村镇四周各 5×5 块)，挑下一块地又总是先填角、齐着边一圈圈长，
    // 村镇划地是整整 3×3 块，铺满了就是一个个长方形。现在每块地对每座城有个固定的随机数(同一块地、同一座城永远一样，
    // 不存档；一半是每三块地缓缓起伏的，一半是各块自己的)，用在四处：
    //   · "两块地以内"按圆算，半径一块半到近三块各处不同(见 Settled)；
    //   · 离城心多远以内各处松紧不同(八成半到一成一五)；
    //   · 挑下一块地的打分加一点随机(见 ExpansionScore)，地形阻力仍是大头，山、河、荒漠照旧后领；
    //   · 村镇划地四周八块不再全划，斜角的多半留着(见 RuralSettlementSystem.ClaimAround)，以后照常一块块领。
    // 幅度是我定的，用户没逐条确认
    internal static float Jitter(City city, TileZone zone, int salt)
    {
        long seed = (city?.data?.id ?? 0L) * 31L + salt;
        float fx = zone.x / 3f, fy = zone.y / 3f;
        int x0 = global::UnityEngine.Mathf.FloorToInt(fx), y0 = global::UnityEngine.Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        float low = global::UnityEngine.Mathf.Lerp(
            global::UnityEngine.Mathf.Lerp(Hash01(seed, x0, y0), Hash01(seed, x0 + 1, y0), tx),
            global::UnityEngine.Mathf.Lerp(Hash01(seed, x0, y0 + 1), Hash01(seed, x0 + 1, y0 + 1), tx), ty);
        return 0.5f * low + 0.5f * Hash01(seed + 7L, zone.x, zone.y);
    }

    private static float Hash01(long seed, int x, int y)
    {
        unchecked
        {
            ulong h = (ulong)seed * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)x * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)y * 0x165667B19E3779F9UL;
            h ^= h >> 29;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 32;
            return (h & 0xFFFFFFUL) / 16777216f;
        }
    }

    public static bool Settled(City city, TileZone zone)
    {
        if (!PureMapMode.Enabled || zone == null || zone.city != null || city?.zones == null) return true;
        // 这座城的平面图还没算过(刚读档、还没轮到时不算，村镇也不会新建)：照原版领地，不限在村镇附近
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        if (built == null) return true;
        if (Sandwiched(zone)) return NearestNeighbourCity(city, zone);
        // 离城心太远的不领(与城外选村址同一个距离，随城的档位放宽)：以前顺着村镇一路往外领，城界伸成长条、伸进别人家
        // (玩家反馈"边界在胡乱扩张"，用户 2026-10-17 选"破碎、夹着空地""伸得太远、蛇形")
        WorldTile seat = city.getTile();
        if (seat != null && zone.centerTile != null)
        {
            int tier = global::UnityEngine.Mathf.Clamp(city.GetOrCreate().urban_tier, 0, 4);
            float far = (24f + 8f * tier) * (0.85f + 0.3f * Jitter(city, zone, 2));
            float dx = zone.centerTile.x - seat.x, dy = zone.centerTile.y - seat.y;
            if (dx * dx + dy * dy > far * far) return false;
        }
        // 被本城的地三面以上围着的空地(城界上的豁口、夹在中间的空地)补进来，城界不再破碎
        if (Enclosed(city, zone)) return true;
        // 按圆算、各处半径不同(一块半到近三块；以前是方框，见 Jitter)
        float reach = SettledReach - 0.5f + 1.4f * Jitter(city, zone, 1);
        bool Near(TileZone anchor) => anchor != null &&
                                      (anchor.x - zone.x) * (anchor.x - zone.x) + (anchor.y - zone.y) * (anchor.y - zone.y) <= reach * reach;
        if (Near(city.getTile()?.zone)) return true;
        foreach (TileZone own in city.zones)
                if (own != null && built.Contains(own.id) && Near(own)) return true;
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
            if (Near(World.world.GetTile(settlement.x, settlement.y)?.zone)) return true;
        return false;
    }

    // 夹在城与城之间的空地(用户 2026-10-19 选"夹在中间的补领"；以前城只领城心附近的地，城与城离得远，
    // 中间成片的空地永远没人领，纯净地图上一块块露着地形，像图层坏了)：往东西南北各走 SandwichReach 块地以内，
    // 都碰得到有主的地(或水、地图边)，就是国境里、城与城之间的空地，不受离城远近与村镇的限制。
    // 往外伸的荒地总有一个方向碰不到有主的地，照旧受限，城界不会再伸成长条。
    // 无人区(见 WildernessSystem)同水、地图边一样算碰到了：城与山脉、大漠之间的空地也补领，地图填得满
    public const int SandwichReach = 6;
    private static readonly (int dx, int dy)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private static bool Sandwiched(TileZone zone)
    {
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return false;
        foreach ((int dx, int dy) in Directions)
        {
            bool closed = false;
            for (int step = 1; step <= SandwichReach && !closed; step++)
            {
                TileZone next = calc.getZone(zone.x + dx * step, zone.y + dy * step);
                closed = next == null || next.tiles_with_ground == 0 || next.city != null || WildernessSystem.IsWild(next);
            }
            if (!closed) return false;
        }
        return true;
    }

    // 挨着这块空地的城里(还能扩张的)，城心离它最近的那座来领
    private static bool NearestNeighbourCity(City city, TileZone zone)
    {
        WorldTile center = zone.centerTile, seat = city.getTile();
        if (center == null || seat == null || zone.neighbours_all == null) return true;
        float Distance(WorldTile from) => (from.x - center.x) * (from.x - center.x) + (from.y - center.y) * (from.y - center.y);
        float mine = Distance(seat);
        foreach (TileZone neighbour in zone.neighbours_all)
        {
            City other = neighbour?.city;
            // 领不了这块地的城(满了、没人、地貌不能定居、隔水翻山走不到)不挡别人
            if (other == null || other == city || other.data == null || other.isRekt() || AtLimit(other) ||
                !other.canGrowZones() || !zone.canBeClaimedByCity(other) || !Reachable(zone, other)) continue;
            WorldTile otherSeat = other.getTile();
            if (otherSeat != null && Distance(otherSeat) < mine) return false;
        }
        return true;
    }

    private static bool Enclosed(City city, TileZone zone)
    {
        var world = World.world.zone_calculator;
        int own = 0;
        if (world.getZone(zone.x - 1, zone.y)?.city == city) own++;
        if (world.getZone(zone.x + 1, zone.y)?.city == city) own++;
        if (world.getZone(zone.x, zone.y - 1)?.city == city) own++;
        if (world.getZone(zone.x, zone.y + 1)?.city == city) own++;
        return own >= 3;
    }

    public static void AfterCanGrowZones(City __instance, ref bool __result)
    {
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && (AtLimit(__instance) || CityTerritoryProtection.BordersLocked))
            __result = false;
    }

    public static void AfterIsZoneToClaimStillGood(City __instance, TileZone pZone, ref bool __result)
    {
        // 到达目标前再次核对，防止多个扩张者和 expansionists 连续领取越过上限。
        AfterCanGrowZones(__instance, ref __result);
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && !CanClaimZone(__instance, pZone)) __result = false;
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) &&
            (!Settled(__instance, pZone) || !Bordering(__instance, pZone) || !Reachable(pZone, __instance))) __result = false;
    }

    // 走得到：原版是小人走到那块地里去领(BehActorCheckZoneTarget 在区块里找一格与自己同岛的地)，
    // 山地是阻挡层、水面是液体层，都不在地面单位的"岛"里，所以隔水、翻山的地原版领不到。
    // 无小人模式没有人走这一趟，按同一条核对：区块里至少有一格与城心同岛
    // 与本城接壤、且交界有路口(不隔着山脊，见 TerrainSystem.Crossable)：领地不翻山
    // (用户 10-29 "不仅国土跨越了军队也跨越了"：以前只看接壤、同岛，山脊另一边经别处山口连得上的地照样领过去)。
    // 山地区块(山地格两成以上)更严(用户 10-29 "为什么你这个城市边界总是会跨山""印度都管到西藏去了，这么大个喜马拉雅")：
    // 城心那块也照交界算，且只从本城的平地(或城心那块，山城也能领到城边一圈)往山里领一圈，不从山里接着往山里领——
    // 山腹留空，不会过几十年把整条山脉领满
    public static bool Bordering(City city, TileZone zone)
    {
        if (city == null || zone?.neighbours == null) return false;
        bool mountainous = GeneralSystems.TerrainSystem.Mountainous(zone);
        bool shore = !GeneralSystems.TerrainSystem.RiverZone(zone);
        TileZone seat = city.getTile()?.zone;
        foreach (TileZone neighbour in zone.neighbours)
        {
            if (neighbour?.city != city) continue;
            if (mountainous && neighbour != seat && GeneralSystems.TerrainSystem.Mountainous(neighbour)) continue;
            if (shore && !RiverOpen(neighbour, seat)) continue;
            if (GeneralSystems.TerrainSystem.Crossable(neighbour, zone, seatOpen: false)) return true;
        }
        return false;
    }

    // 大河不越过(用户 2026-11-11 "改成不越过"：要黑龙江、乌苏里江、鸭绿江、松花江自然成界，"按地势建城和拓展地块"长出雄鸡的样子)：
    // 河上的地块(见 TerrainSystem.RiverZone)从岸上照领，但岸上的地不从河上那块领过去——只挨着本城河上地块的岸是对岸，不领。
    // 城就在河上的(城心那块)两岸都领；河上那块修了路(架了桥)的照通。神力手动划地、剧本开局铺地不走这里
    private static bool RiverOpen(TileZone own, TileZone seat) =>
        own == seat || !GeneralSystems.TerrainSystem.RiverZone(own) || RoadNetwork.IsRoad(own);

    // 这块岸上的地挨着本城的只有河上地块(隔河)：村镇划地、开发无人区划给城时也不越过(同 Bordering)。斜挨着的要旁边两块里有一块不在河上
    public static bool AcrossRiver(City city, TileZone zone)
    {
        if (city == null || zone == null || GeneralSystems.TerrainSystem.RiverZone(zone)) return false;
        TileZone seat = city.getTile()?.zone;
        ZoneCalculator calc = World.world?.zone_calculator;
        bool touches = false;
        foreach (TileZone own in zone.neighbours_all ?? zone.neighbours ?? global::System.Array.Empty<TileZone>())
        {
            if (own?.city != city) continue;
            touches = true;
            if (!RiverOpen(own, seat)) continue;
            if (own.x == zone.x || own.y == zone.y || calc == null) return false;
            TileZone a = calc.getZone(zone.x, own.y), b = calc.getZone(own.x, zone.y);
            if (a != null && !GeneralSystems.TerrainSystem.RiverZone(a) || b != null && !GeneralSystems.TerrainSystem.RiverZone(b)) return false;
        }
        return touches;
    }

    public static bool Reachable(TileZone zone, City city)
    {
        WorldTile origin = city?.getTile();
        if (zone?.tiles == null || origin?.region == null) return false;
        if (zone.centerTile?.region != null && zone.centerTile.isSameIsland(origin)) return true;
        foreach (WorldTile tile in zone.tiles)
            if (tile?.region != null && tile.isSameIsland(origin)) return true;
        return false;
    }

    public static bool BeforeGetZoneToClaim(City pCity, bool pDebug, ref TileZone __result)
    {
        if (pDebug || AncientWarfareCompatibility.OwnsObject(pCity) || !AtLimit(pCity)) return true;
        __result = null;
        return false;
    }

    public static void AfterGetZoneToClaim(CityZoneGrowth __instance, Actor pActor, City pCity,
        bool pDebug, ref TileZone __result)
    {
        if (pDebug || pCity?.data == null || pCity.isRekt() ||
            AncientWarfareCompatibility.OwnsObject(pCity)) return;
        if (AtLimit(pCity))
        {
            __result = null;
            return;
        }
        if (!pCity.canGrowZones()) { __result = null; return; }
        WorldTile origin = pCity.getTile();
        if (origin == null) { __result = null; return; }
        TileZone best = null;
        float bestScore = float.PositiveInfinity;
        var considered = new HashSet<TileZone>();
        void Consider(TileZone candidate)
        {
            if (candidate == null || candidate.city != null || candidate.tiles_with_ground == 0 ||
                !considered.Add(candidate) ||
                !CanClaimZone(pCity, candidate) || !candidate.canBeClaimedByCity(pCity) || !Settled(pCity, candidate) ||
                !Bordering(pCity, candidate) || !Reachable(candidate, pCity) ||
                (pActor?.subspecies != null && !candidate.checkCanSettleInThisBiomes(pActor.subspecies))) return;
            float score = ExpansionScore(pCity, candidate);
            if (score >= bestScore) return;
            best = candidate;
            bestScore = score;
        }
        Consider(__result);
        // 原版这一轮的候选，加上现有领地的直接边界；不会只因随机先碰到山坡就忽略旁边的河谷。
        foreach (ZoneConnection connection in __instance._zones_checked)
            Consider(connection.zone);
        if (pCity.zones != null)
            foreach (TileZone own in pCity.zones)
                foreach (TileZone next in own?.neighbours ?? global::System.Array.Empty<TileZone>()) Consider(next);
        if (best == null) Consider(__instance.getRandomZone(pCity));
        __result = best;
    }

    private static float ExpansionScore(City city, TileZone zone)
    {
        FrontTerrain terrain = GeneralSystems.TerrainSystem.Of(zone);
        bool mountain = GeneralSystems.TerrainSystem.Mountainous(zone);
        TileZone seat = city.getTile()?.zone;
        bool shore = !GeneralSystems.TerrainSystem.RiverZone(zone);
        int contacts = 0;
        float entry = float.PositiveInfinity;
        foreach (TileZone own in zone.neighbours ?? global::System.Array.Empty<TileZone>())
        {
            if (own?.city != city || mountain && own != seat && GeneralSystems.TerrainSystem.Mountainous(own) ||
                shore && !RiverOpen(own, seat) ||
                !GeneralSystems.TerrainSystem.Crossable(own, zone, seatOpen: false)) continue;
            contacts++;
            FrontTerrain from = GeneralSystems.TerrainSystem.Of(own);
            float cost = TerrainResistance.Base(terrain) * (from == terrain ? 0.9f : 1f);
            if ((from == FrontTerrain.River) != (terrain == FrontTerrain.River) && !RoadNetwork.IsRoad(zone))
                cost += TerrainResistance.RiverCrossing;
            if (cost < entry) entry = cost;
        }
        if (contacts == 0) return float.PositiveInfinity;
        // 接壤面多的地优先，距离只作轻微约束，不让直线远近压过山脊、河谷等地形。
        WorldTile origin = city.getTile(), target = zone.centerTile;
        float dx = target == null || origin == null ? 0f : target.x - origin.x;
        float dy = target == null || origin == null ? 0f : target.y - origin.y;
        // 再加一点各块固定的随机(见 Jitter)：不总是先填角、齐着边长
        return entry + 0.6f / contacts + (dx * dx + dy * dy) * 0.002f / (Config.CITY_ZONE_SIZE * Config.CITY_ZONE_SIZE) +
               0.45f * Jitter(city, zone, 3);
    }

    // 无小人模式：虚拟居民每月替原版的"领地人"领一块地(BehClaimZoneForCityActorBorder)。原版选地照旧，
    // 再按原版那一趟路核对：走得到(Reachable)、原版的 isZoneToClaimStillGood(能领、地貌能定居、与本城接壤)。
    // 原版任何居民都能去领，不必等城主在城里(没有城主时按原版 canBeClaimedByCity 与接壤核对)；
    // 选中的地走不到时再选，至多三次(原版有好几个领地人轮流去)
    private const int VirtualClaimTries = 3;

    public static void GrowVirtualCity(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || ModClass.IS_CLEAR ||
            city?.data == null || city.isRekt() || AncientWarfareCompatibility.OwnsObject(city) ||
            !WorldLawLibrary.world_law_kingdom_expansion.isEnabled() || !city.canGrowZones() || AtLimit(city)) return;
        WorldTile origin = city.getTile();
        CityZoneGrowth growth = World.world?.city_zone_helper?.city_growth;
        if (origin == null || growth == null || CityTerritoryProtection.BordersLocked) return;
        Actor actor = city.leader;
        if (actor?.data == null || actor.isRekt() || !actor.isAlive()) actor = null;
        for (int attempt = 0; attempt < VirtualClaimTries; attempt++)
        {
            TileZone target = growth.getZoneToClaim(actor, city);
            if (target == null) return;
            if (!CanClaimZone(city, target) || !Reachable(target, city) || !Bordering(city, target)) continue;
            bool good = actor != null
                ? city.isZoneToClaimStillGood(actor, target, origin)
                : target.canBeClaimedByCity(city) && target.neighbours.Any(neighbour => neighbour?.city == city);
            if (!good) continue;
            city.addZone(target);
            return;
        }
    }

    // 原版的"断开的地放弃"(CityZoneAbandon)：从城里各建筑沿陆路连得到的地块才算本城，连不上的(隔水、翻山)放弃，
    // 断成几片的只留最大的一片。原版只在城失去地块时查一次；读档后每座城都查一次，
    // 清掉以前模组自己的"被围空地并入""边地易手"留下的山地、隔水地和飞地
    private static object _abandonCheckedWorld;

    public static void CheckAbandonOnLoad()
    {
        MapBox world = World.world;
        if (world?.cities == null || ReferenceEquals(_abandonCheckedWorld, world)) return;
        _abandonCheckedWorld = world;
        foreach (City city in world.cities)
            if (city?.data != null && !city.isRekt() && !AncientWarfareCompatibility.OwnsObject(city))
                city.setAbandonedZonesDirty();
    }
}
