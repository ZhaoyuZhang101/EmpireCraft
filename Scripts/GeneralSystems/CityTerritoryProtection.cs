using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CityTerritoryProtection
{
    // World teardown marks cities dead before disposal. Only protect live cities
    // that still own territory, so loading another map can clear the old world.
    public static bool ShouldPreventDestruction(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones.Count == 0 ||
            AncientWarfareCompatibility.OwnsObject(city)) return false;
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
