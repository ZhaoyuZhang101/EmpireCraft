using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 城市扩张守原版的规矩：原版选地(CityZoneGrowth.getZoneToClaim)、能不能领(canBeClaimedByCity：地貌能否定居、
// 别国的地要开着原版"边界侵蚀"且两国交战)、与本城接壤；原版还要小人走到那块地里去领，走不到(隔水、翻山)就领不到。
// 模组只加了每城地块上限(模组设置)，以及无小人模式下由虚拟居民代领时按"走得到"核对(见 Reachable)。
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
    }

    public static bool AtLimit(City city) => city?.zones != null &&
        ModClass.CITY_MAX_ZONES > 0 && city.zones.Count >= ModClass.CITY_MAX_ZONES;

    public static void AfterCanGrowZones(City __instance, ref bool __result)
    {
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && AtLimit(__instance))
            __result = false;
    }

    public static void AfterIsZoneToClaimStillGood(City __instance, TileZone pZone, ref bool __result)
    {
        // 到达目标前再次核对，防止多个扩张者和 expansionists 连续领取越过上限。
        AfterCanGrowZones(__instance, ref __result);
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
        if (pDebug || __result != null || pCity?.data == null || pCity.isRekt() || AtLimit(pCity) ||
            AncientWarfareCompatibility.OwnsObject(pCity) || !pCity.canGrowZones()) return;
        // 原版随机选中一条受阻边界时，检查本次波搜索已经证明连通的其它区域。
        // 不保存“无法扩张”状态，每次请求都按当前地形重算；不会跨水域/山脉硬抢地。
        // 起点用城心(纯净地图里人物到处瞬移，所在格不一定在本城)
        WorldTile origin = pCity.getTile();
        if (origin == null) return;
        foreach (ZoneConnection connection in __instance._zones_checked)
        {
            TileZone candidate = connection.zone;
            if (candidate == null || candidate.city != null || candidate.tiles_with_ground == 0 ||
                !candidate.canBeClaimedByCity(pCity) ||
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
            fallback.centerTile.isSameIsland(origin) &&
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
            if (target.city == city || !Reachable(target, city)) continue;
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
