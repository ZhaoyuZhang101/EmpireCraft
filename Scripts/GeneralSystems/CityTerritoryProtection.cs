using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CityTerritoryProtection
{
    // 正在撤城(见 CityDissolution)：不再保护
    public static readonly global::System.Collections.Generic.HashSet<long> Dissolving = new();

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
        return !city.zones.Contains(zone) || city.zones.Count > 1 ||
               !ShouldPreventDestruction(city);
    }
}
