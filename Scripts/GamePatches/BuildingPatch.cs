using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using HarmonyLib;
using NeoModLoader.api;

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
        if (__instance == null) return;
        var k = __instance.kingdom;
        if (k == null || !__instance.hasKingdom()) return;
        if (!k.IsInEmpire()) return;
        Empire empire = k.GetEmpire();
        if (empire == null) return;
        if (empire.isRekt()) return;
        var additions = empire.Additions;
        if (additions == null) return;
        if (__instance.stats != null && __instance.stats.hasStat("health"))
        {
            __instance.stats["health"] += additions.addition[OfficerPowerType.建设] * 5;
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
        return !EmpireCraftWorldLawLibrary.empirecraft_law_prevent_building_destroy.isEnabled() || __instance.asset.building_type != BuildingType.Building_Civ || __instance.asset.tower_attack_buildings;
    }
}
