using ai.behaviours;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 无小人城市失去城主且旧存档的创建物种已失效时，原版建造 AI 会每次抛空引用。
public class CityBuildSafetyPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(CityBuildSafetyPatch));
        harmony.Patch(AccessTools.Method(typeof(City), nameof(City.getActorAsset)),
            postfix: new HarmonyMethod(GetType(), nameof(AfterGetActorAsset)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.calcPossibleBuildings)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeCalcPossibleBuildings)));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.buildTick)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeBuildTick)));
    }

    private static bool HasBuildTemplate(ActorAsset asset) =>
        asset != null && !string.IsNullOrEmpty(asset.build_order_template_id) &&
        AssetManager.city_build_orders?.get(asset.build_order_template_id)?.list != null;

    public static void AfterGetActorAsset(City __instance, ref ActorAsset __result)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || HasBuildTemplate(__result) ||
            __instance?.data == null || AncientWarfareCompatibility.OwnsObject(__instance)) return;
        ActorAsset best = null;
        float largest = -1f;
        var population = CityPopulationSystem.Get(__instance);
        if (population?.groups != null)
            foreach (var group in population.groups)
            {
                if (group == null || group.size <= largest || string.IsNullOrEmpty(group.species)) continue;
                ActorAsset candidate = AssetManager.actor_library?.get(group.species);
                if (!HasBuildTemplate(candidate)) continue;
                best = candidate;
                largest = group.size;
            }
        if (best == null)
        {
            ActorAsset ruler = __instance.kingdom?.king?.getActorAsset();
            if (HasBuildTemplate(ruler)) best = ruler;
        }
        if (best == null) return;
        __result = best;
        __instance.data.original_actor_asset = best.id;
    }

    public static bool BeforeCalcPossibleBuildings(City pCity) =>
        !CityPopulationSystem.AbstractPopulationEnabled ||
        pCity != null && AncientWarfareCompatibility.OwnsObject(pCity) ||
        pCity?.data != null && !pCity.isRekt() && HasBuildTemplate(pCity.getActorAsset());

    public static bool BeforeBuildTick(City pCity, ref bool __result)
    {
        if (BeforeCalcPossibleBuildings(pCity)) return true;
        __result = false;
        return false;
    }
}
