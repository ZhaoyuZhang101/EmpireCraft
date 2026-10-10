using ai.behaviours;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GamePatches;

// 无小人模式：城市人口按“户”计(一户约 100 人)，原版风车要求人口 ≥ 6(6 个小人)，
// 六百人以下的小城永远建不起风车 → 没有农田、只靠自给口粮(每户每年半份) → 长期饥荒 → 长不大 → 还是建不起风车。
// 判断风车建造条件时，城里有人就算达到人口门槛；建筑数量、每城一座等其它条件照旧。
public class WindmillRequirementPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var harmony = new Harmony(nameof(WindmillRequirementPatch));
        harmony.Patch(AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.canUseBuildAsset)),
            prefix: new HarmonyMethod(typeof(WindmillRequirementPatch), nameof(Before)),
            postfix: new HarmonyMethod(typeof(WindmillRequirementPatch), nameof(After)));
    }

    public static void Before(BuildOrder pBuildAsset, City pCity, out int __state)
    {
        __state = -1;
        if (!CityPopulationSystem.AbstractPopulationEnabled || pBuildAsset == null || pCity?.status == null ||
            AncientWarfareCompatibility.OwnsObject(pCity)) return;
        if (pCity.status.population <= 0 || pCity.status.population >= pBuildAsset.required_pop) return;
        if (pBuildAsset.getBuildingAsset(pCity)?.type != "type_windmill") return;
        __state = pCity.status.population;
        pCity.status.population = pBuildAsset.required_pop;
    }

    public static void After(City pCity, int __state)
    {
        if (__state >= 0 && pCity?.status != null) pCity.status.population = __state;
    }
}
