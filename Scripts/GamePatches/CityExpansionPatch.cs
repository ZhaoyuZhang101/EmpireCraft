using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

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
        // 不跨山越水：大半是山地、水面的区块不领
        if (__result && !AncientWarfareCompatibility.OwnsObject(__instance) && !Passable(pZone)) __result = false;
    }

    // 区块里至少三成是可住的平地(非山、非水、非障碍)才算能扩张进去
    public static bool Passable(TileZone zone)
    {
        if (zone?.tiles == null) return false;
        int usable = 0, total = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.Type == null) continue;
            total++;
            if (tile.Type.ground && !tile.Type.mountains && !tile.Type.block && !tile.Type.liquid) usable++;
        }
        return total == 0 || usable * 10 >= total * 3;
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
        WorldTile origin = pActor?.current_tile ?? pCity.getTile();
        if (origin == null) return;
        foreach (ZoneConnection connection in __instance._zones_checked)
        {
            TileZone candidate = connection.zone;
            if (candidate == null || candidate.city != null || candidate.tiles_with_ground == 0 ||
                !candidate.canBeClaimedByCity(pCity) || !Passable(candidate) ||
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
            fallback.centerTile.isSameIsland(origin) && Passable(fallback) &&
            (pActor?.subspecies == null || fallback.checkCanSettleInThisBiomes(pActor.subspecies)))
            __result = fallback;
    }

    public static void GrowVirtualCity(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || ModClass.IS_CLEAR ||
            city?.data == null || city.isRekt() || AncientWarfareCompatibility.OwnsObject(city) ||
            !WorldLawLibrary.world_law_kingdom_expansion.isEnabled() || !city.canGrowZones() || AtLimit(city)) return;
        Actor actor = city.leader;
        if (actor?.data == null || actor.isRekt() || actor.city != city) return;
        CityZoneGrowth growth = World.world?.city_zone_helper?.city_growth;
        if (growth == null) return;
        TileZone target = growth.getZoneToClaim(actor, city);
        if (target?.city != null || target == null || !city.isZoneToClaimStillGood(actor, target, city.getTile())) return;
        // 此时虚拟居民代表执行原版的领取工作；只领取一块空地，下一月再重算。
        city.addZone(target);
    }
}
