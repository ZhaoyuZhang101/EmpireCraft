using System;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

// 无小人模式：城里的人不睡觉(见 BeforeAddStatusEffect)；太平时兵额压到 1(只驻扎一名将领)；原版每次刷新城市状态时，把没有实体单位的背景人口也算进城市人口和已占用住房。
// 这样住房被背景人口占着，原版不会因为"空房很多"而无限生育；住满后又会照常盖新民居，
// 城市能容纳的人数随建筑增长(容量本身仍由 getPopulationMaximum 按住房计算)。
public class NoCommonersPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        // 无小人模式下留在地图上的人(名人、劳动者、将领、征召兵)不睡觉：拦下原版的"睡眠"状态
        try
        {
            var harmony = new Harmony(nameof(NoCommonersPatch) + ".Sleep");
            foreach (global::System.Reflection.MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Actor)))
            {
                if (method.Name != "addStatusEffect") continue;
                global::System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string)) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(BeforeAddStatusEffect)));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式免睡补丁未生效: {exception.Message}");
        }
        try
        {
            new Harmony(nameof(NoCommonersPatch)).Patch(AccessTools.Method(typeof(City), "updateCityStatus"),
                postfix: new HarmonyMethod(typeof(NoCommonersPatch), nameof(AfterUpdateCityStatus))
                    { priority = Priority.Last });
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 无小人模式住房补丁未生效: {exception.Message}");
        }
    }

    // 返回 false 表示不加这个状态
    public static bool BeforeAddStatusEffect(Actor __instance, string __0)
    {
        if (__0 != "sleeping" || !CityPopulationSystem.AbstractPopulationEnabled) return true;
        return __instance?.city == null ||
               EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance);
    }

    public static void AfterUpdateCityStatus(City __instance)
    {
        if (__instance?.status == null || !CityPopulationSystem.AbstractPopulationEnabled ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        // 太平时只驻扎一名实体将领：原版兵额压到 1，免得把留下的劳动者拉去当兵；交战时兵额照常，由征召兵补足
        if (!CityPopulationSystem.IsAtWar(__instance) && __instance.status.warrior_slots > 1)
            __instance.status.warrior_slots = 1;
        int background = CityPopulationSystem.BackgroundCount(__instance);
        if (background <= 0) return;
        // 原版的人口统计可能已经经过 getPopulationPeople(本模组已把背景人口算进去)，
        // 为避免重复计算，这里直接按"实体单位 + 背景人口"重新给出人口与空余住房
        int units = 0;
        if (__instance.units != null)
            foreach (Actor actor in __instance.units)
                if (actor != null && !actor.isRekt() && actor.isAlive()) units++;
        __instance.status.population = units + background;
        __instance.status.housing_free = Math.Max(0, __instance.status.housing_total - __instance.status.population);
    }
}
