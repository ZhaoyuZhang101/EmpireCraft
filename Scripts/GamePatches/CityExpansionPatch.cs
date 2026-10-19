using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 城市扩张守原版选地(CityZoneGrowth.getZoneToClaim)、地貌定居和接壤规则；
// 原版还要小人走到那块地里去领，走不到(隔水、翻山)就领不到。
// 模组只加了每城地块上限(模组设置)，以及无小人模式下由虚拟居民代领时按"走得到"核对(见 Reachable)。
// 城的地按村镇拓展(用户 2026-10-17 选"新建村镇就划进它一片"、限制"按离城远近")：纯净地图下，无主之地只领城区(城心、
// 建成地块)或本城村镇两块地以内的(见 Settled)；再往外靠城外新建村镇划地(见 RuralSettlementSystem.Found)。
// 夹在城与城之间的空地不受这些限制，由挨着它的城里离得最近的那座领下(见 Sandwiched)。
// 已有城市的地不参与自动扩张：战争只记录临时占领，不能用边界侵蚀永久拆改城市辖区。
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
        // 原版边界画笔(神力 border_brush)：画的这一下算神力手动划地，不受每城地块上限(见 CanAddZone)
        global::System.Reflection.MethodInfo brush = AccessTools.Method(typeof(PowerLibrary), "drawBorderBrush");
        if (brush != null)
            harmony.Patch(brush, prefix: new HarmonyMethod(typeof(CityExpansionPatch), nameof(BeginManual)),
                finalizer: new HarmonyMethod(typeof(CityExpansionPatch), nameof(EndManual)));
    }

    public static bool AtLimit(City city) => city?.zones != null &&
        ModClass.CITY_MAX_ZONES > 0 && city.zones.Count >= ModClass.CITY_MAX_ZONES;

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
    public static bool CanAddZone(City city, TileZone zone)
    {
        if (city == null || zone == null) return false;
        if (city.zones?.Contains(zone) == true || ModClass.IS_CLEAR || !Config.game_loaded || SmoothLoader.isLoading() ||
            AncientWarfareCompatibility.OwnsObject(city) || ManualClaim) return true;
        return !AtLimit(city);
    }

    // 自动领地只领无主地；与手动村镇改隶、建城、读档的 addZone 分开。
    public static bool CanClaimZone(City city, TileZone zone) =>
        city != null && zone != null && zone.city == null && CanAddZone(city, zone);

    // 原版按剩余建筑找连通地块，战损后会把原辖区当作弃地删除；城市格局不能因此变化。
    public static bool BeforeAbandonZones(City pCity, bool pDebug) =>
        pDebug || !CityTerritoryProtection.PreserveWarLayout(pCity);

    // 城区或本城村镇几块地以内
    public const int SettledReach = 2;

    public static bool Settled(City city, TileZone zone)
    {
        if (!PureMapMode.Enabled || zone == null || zone.city != null || city?.zones == null) return true;
        // 这座城的平面图还没算过(关着地形浮雕、不画城市时不算，村镇也不会新建)：照原版领地，不限在村镇附近
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        if (built == null) return true;
        if (Sandwiched(zone)) return NearestNeighbourCity(city, zone);
        // 离城心太远的不领(与城外选村址同一个距离，随城的档位放宽)：以前顺着村镇一路往外领，城界伸成长条、伸进别人家
        // (玩家反馈"边界在胡乱扩张"，用户 2026-10-17 选"破碎、夹着空地""伸得太远、蛇形")
        WorldTile seat = city.getTile();
        if (seat != null && zone.centerTile != null)
        {
            int tier = global::UnityEngine.Mathf.Clamp(city.GetOrCreate().urban_tier, 0, 4);
            float far = 24f + 8f * tier;
            float dx = zone.centerTile.x - seat.x, dy = zone.centerTile.y - seat.y;
            if (dx * dx + dy * dy > far * far) return false;
        }
        // 被本城的地三面以上围着的空地(城界上的豁口、夹在中间的空地)补进来，城界不再破碎
        if (Enclosed(city, zone)) return true;
        bool Near(TileZone anchor) => anchor != null && global::System.Math.Abs(anchor.x - zone.x) <= SettledReach &&
                                      global::System.Math.Abs(anchor.y - zone.y) <= SettledReach;
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
    // 往外伸的荒地总有一个方向碰不到有主的地，照旧受限，城界不会再伸成长条
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
                closed = next == null || next.tiles_with_ground == 0 || next.city != null;
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
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && AtLimit(__instance))
            __result = false;
    }

    public static void AfterIsZoneToClaimStillGood(City __instance, TileZone pZone, ref bool __result)
    {
        // 到达目标前再次核对，防止多个扩张者和 expansionists 连续领取越过上限。
        AfterCanGrowZones(__instance, ref __result);
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && !CanClaimZone(__instance, pZone)) __result = false;
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && !Settled(__instance, pZone)) __result = false;
    }

    // 走得到：原版是小人走到那块地里去领(BehActorCheckZoneTarget 在区块里找一格与自己同岛的地)，
    // 山地是阻挡层、水面是液体层，都不在地面单位的"岛"里，所以隔水、翻山的地原版领不到。
    // 无小人模式没有人走这一趟，按同一条核对：区块里至少有一格与城心同岛
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
        // 无主之地只领城区、村镇附近的(见 Settled)
        if (__result != null)
        {
            if (CanClaimZone(pCity, __result) && Settled(pCity, __result)) return;
            __result = null;
        }
        if (!pCity.canGrowZones()) return;
        // 原版随机选中一条受阻边界时，检查本次波搜索已经证明连通的其它区域。
        // 不保存“无法扩张”状态，每次请求都按当前地形重算；不会跨水域/山脉硬抢地。
        // 起点用城心(纯净地图里人物到处瞬移，所在格不一定在本城)
        WorldTile origin = pCity.getTile();
        if (origin == null) return;
        foreach (ZoneConnection connection in __instance._zones_checked)
        {
            TileZone candidate = connection.zone;
            if (candidate == null || candidate.city != null || candidate.tiles_with_ground == 0 ||
                !candidate.canBeClaimedByCity(pCity) || !Settled(pCity, candidate) ||
                (pActor?.subspecies != null && !candidate.checkCanSettleInThisBiomes(pActor.subspecies))) continue;
            bool adjacent = false;
            foreach (TileZone neighbour in candidate.neighbours)
                if (neighbour?.city == pCity) { adjacent = true; break; }
            if (!adjacent) continue;
            foreach (WorldTile tile in candidate.tiles)
            {
                if (tile != null && tile.isSameIsland(origin))
                {
                    __result = candidate;
                    return;
                }
            }
        }
        // 波搜索可能提早停在第一个空区，再尝试原版完整的边界邻接检查。
        TileZone fallback = __instance.getRandomZone(pCity);
        if (fallback?.city == null && fallback?.centerTile != null &&
            fallback.centerTile.isSameIsland(origin) && Settled(pCity, fallback) &&
            (pActor?.subspecies == null || fallback.checkCanSettleInThisBiomes(pActor.subspecies)))
            __result = fallback;
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
        if (origin == null || growth == null) return;
        Actor actor = city.leader;
        if (actor?.data == null || actor.isRekt() || !actor.isAlive()) actor = null;
        for (int attempt = 0; attempt < VirtualClaimTries; attempt++)
        {
            TileZone target = growth.getZoneToClaim(actor, city);
            if (target == null) return;
            if (!CanClaimZone(city, target) || !Reachable(target, city)) continue;
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
