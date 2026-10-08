using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class CityTerritoryProtection
{
    public static bool CanRemoveZone(City city, TileZone zone)
    {
        if (city == null || zone == null) return false;
        return AncientWarfareCompatibility.OwnsObject(city) ||
               EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy?.isEnabled() != true ||
               !city.zones.Contains(zone) || city.zones.Count > 1;
    }
}
