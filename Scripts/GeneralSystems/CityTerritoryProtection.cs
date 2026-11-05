using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CityTerritoryProtection
{
    // 正在撤城(见 CityDissolution)：不再保护
    public static readonly global::System.Collections.Generic.HashSet<long> Dissolving = new();

    // 城市边界固定(世界法则，以前是模组设置)：城的地块不再自动增减——不扩张、不缩界、不弃地、不被侵蚀、开荒不并地，无人区也不从城里划地；整城易主照常。
    // 还能动的：世界生成、剧本开局与读档；神力手动划界、营建、建城(ManualClaim、Placing)；新城领落脚那一小片(BeginFounding)；撤城
    private static int _founding;

    public static void BeginFounding() => _founding++;

    public static void EndFounding()
    {
        if (_founding > 0) _founding--;
    }

    public static bool BordersFixed => GameLibrary.EmpireCraftWorldLawLibrary.empirecraft_law_fixed_city_borders?.isEnabled() == true;

    public static bool BordersLocked => BordersFixed && !ModClass.IS_CLEAR && Config.game_loaded && !SmoothLoader.isLoading() &&
                                        !WorldMap.WorldMapGenerator.Generating && !WorldMap.WorldMapScenario.AfterLoadPending;

    private static bool ManualChange => GamePatches.CityExpansionPatch.ManualClaim || CityPlacementSystem.Placing;

    public static bool LocksAdd(City city) => BordersLocked && !ManualChange && _founding == 0 && city?.zones?.Count > 0;

    public static bool LocksRemove(City city) => BordersLocked && !ManualChange && city?.data != null && !city.isRekt() &&
                                                 !Dissolving.Contains(city.id);

    // 战争、临时占领都不能触发自动缩界或弃地；只保护辖区，不阻挡整城换国。
    public static bool PreserveWarLayout(City city)
    {
        if (ModClass.IS_CLEAR || !Config.game_loaded || SmoothLoader.isLoading() || city?.data == null || city.isRekt() ||
            AncientWarfareCompatibility.OwnsObject(city) || Dissolving.Contains(city.id)) return false;
        return city.kingdom?.hasEnemies() == true || city.GetOrCreate().OccupiedStatus?.Count > 0;
    }

    // World teardown marks cities dead before disposal. Only protect live cities
    // that still own territory, so loading another map can clear the old world.
    public static bool ShouldPreventDestruction(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones.Count == 0 ||
            AncientWarfareCompatibility.OwnsObject(city) || Dissolving.Contains(city.id)) return false;
        return EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy?.isEnabled() == true ||
               CityPopulationSystem.AbstractPopulationEnabled &&
               (CityPopulationSystem.GetBackgroundTotal(city) > 0f ||
                CityPopulationSystem.Get(city)?.levied > 0f);
    }

    public static bool ShouldKeepVirtualOwner(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.kingdom == null ||
            city.kingdom.wild || city.kingdom.isRekt()) return false;
        return ShouldPreventDestruction(city) || city.data != null && !city.isRekt() &&
               city.zones.Count > 0 && !AncientWarfareCompatibility.OwnsObject(city) &&
               StateSettlementSystem.AwaitingPopulation(city);
    }

    public static bool CanRemoveZone(City city, TileZone zone)
    {
        if (city == null || zone == null) return false;
        if (city.zones.Contains(zone) && LocksRemove(city)) return false;
        return !city.zones.Contains(zone) || city.zones.Count > 1 ||
               !ShouldPreventDestruction(city);
    }
}
