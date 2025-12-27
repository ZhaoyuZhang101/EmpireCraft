using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using HarmonyLib;
using NeoModLoader.api;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.GamePatches;

public class BuildingPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {

        new Harmony(nameof(UpdateStats)).Patch(
            AccessTools.Method(typeof(Building), nameof(Building.updateStats)),
            postfix: new HarmonyMethod(GetType(), nameof(UpdateStats))
        );

        new Harmony(nameof(GetHit)).Patch(
            AccessTools.Method(typeof(Building), nameof(Building.getHit)),
            prefix: new HarmonyMethod(GetType(), nameof(GetHit))
        );
    }

    public static void UpdateStats(Building __instance)
    {
        if (!__instance.hasKingdom()) return;
        if (__instance.kingdom.IsInEmpire())
        {
            Empire empire = __instance.kingdom.GetEmpire();
            if (empire.isRekt())
            {
                __instance.stats["health"] += empire.Additions.addition[OfficerPowerType.建设]*5;
            }
        }
        
    }

    public static bool GetHit(
        Building __instance,
        float pDamage,
        bool pFlash,
        AttackType pAttackType,
        BaseSimObject pAttacker,
        bool pSkipIfShake,
        bool pMetallicWeapon,
        bool pCheckDamageReduction)
    {
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_building_destroy.isEnabled()&&__instance.asset.building_type == BuildingType.Building_Civ&&!__instance.asset.tower_attack_buildings )
        {
            return false;
        }
        return true;
    }
}