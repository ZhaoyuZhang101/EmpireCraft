using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GamePatches;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GameClassExtensions;

public class ZonesPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(GetCurrentMapBorderMode)).Patch(
            AccessTools.Method(typeof(Zones), nameof(Zones.getCurrentMapBorderMode)),
            prefix: new HarmonyMethod(GetType(), nameof(GetCurrentMapBorderMode))
        );
    }
    
    public static bool GetCurrentMapBorderMode(bool pCheckOnlyOption, ref MetaType __result)
    {
        if (Zones.showCultureZones(pCheckOnlyOption))
        {
            __result = MetaType.Culture;
            return false;
        }
        if (Zones.showKingdomZones(pCheckOnlyOption))
        {
            __result = MetaType.Kingdom;
            return false;
        }
        if (Zones.showClanZones(pCheckOnlyOption))
        {
            __result = MetaType.Clan;
            return false;
        }
        if (Zones.showAllianceZones(pCheckOnlyOption))
        {
            __result = MetaType.Alliance;
            return false;
        }
        if (Zones.showCityZones(pCheckOnlyOption))
        {
            __result = MetaType.City;
            return false;
        }
        if (Zones.showSpeciesZones(pCheckOnlyOption))
        {
            __result = MetaType.Subspecies;
            return false;
        }
        if (Zones.showFamiliesZones(pCheckOnlyOption))
        {
            __result = MetaType.Family;
            return false;
        }
        if (Zones.showLanguagesZones(pCheckOnlyOption))
        {
            __result = MetaType.Language;
            return false;
        }
        if (Zones.showReligionZones(pCheckOnlyOption))
        {
            __result = MetaType.Religion;
            return false;
        }
        if (Zones.showArmyZones(pCheckOnlyOption))
        {
            __result = MetaType.Army;
            return false;
        }
        if (EmpireCraftMetaTypeLibrary.empire.isActive(pCheckOnlyOption))
        {
            __result = MetaTypeExtension.Empire;
            return false;
        }
        if (EmpireCraftMetaTypeLibrary.kingdomTitle.isActive(pCheckOnlyOption))
        {
            __result = MetaTypeExtension.KingdomTitle;
            return false;
        }
        __result = MetaType.City;
        return false;
    }
}
