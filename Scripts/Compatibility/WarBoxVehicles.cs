using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using UnityEngine;

namespace EmpireCraft.Scripts.Compatibility;

// 关闭「军团统领实体」(CorpsSystem.NoMilitaryUnits)时，WarBox 的坦克、飞机等载具也不留实体：
//   · 工厂不再造实体载具，改为在城里的账面上按 WarBox 原来的额度(人口 ÷ 每多少人一辆)补齐；
//   · 已有的载具实体收进所在城的账面；
//   · 账面载具照样给本国军团加火力(见 FrontSystem.Firepower)，交战伤亡时按比例折损，城破随城易主。
public static partial class WarBoxCompatibility
{
    private const float FactoryInterval = 20f;
    private static readonly Dictionary<long, float> FactoryTimers = new();
    private static readonly HashSet<string> FactoryTypes = new()
    {
        "type_lightfactory", "type_heavyfactory", "type_lightaircraftfactory", "type_heavyaircraftfactory",
        "type_lightshipyard", "type_heavyshipyard"
    };
    private static FieldInfo _perPop, _factoriesOn;

    public static bool IsMilitaryVehicle(Actor actor) =>
        actor?.asset != null && (actor.asset.id.StartsWith("warbox_") || actor.hasTrait("warbox_unit"));

    public static int VirtualVehicles(City city) => city?.data == null ? 0 : city.GetOrCreate().virtual_vehicles;

    public static int VirtualVehicles(Kingdom kingdom)
    {
        int total = 0;
        if (kingdom?.cities == null) return total;
        foreach (City city in kingdom.cities)
            if (city?.data != null && !city.isRekt()) total += city.GetOrCreate().virtual_vehicles;
        return total;
    }

    // 拦在 WarBox 工厂(Patch_CityUpdate.Prefix)之前：不留实体时按账面补齐，不再造实体
    private static bool BeforeFactory(City __instance, float pElapsed)
    {
        if (!CorpsSystem.NoMilitaryUnits || __instance?.data == null || __instance.isRekt()) return true;
        try
        {
            _factoriesOn ??= AccessTools.Field(AccessTools.TypeByName("WarBox.WarBox"), "warbox_factories");
            _perPop ??= AccessTools.Field(AccessTools.TypeByName("WarBox.WarBox"), "warbox_army_per_pop");
            if (_factoriesOn != null && !(bool)_factoriesOn.GetValue(null)) return false;
            if (World.world.isPaused()) return false;
            FactoryTimers.TryGetValue(__instance.id, out float timer);
            timer -= pElapsed;
            if (timer > 0f)
            {
                FactoryTimers[__instance.id] = timer;
                return false;
            }
            FactoryTimers[__instance.id] = FactoryInterval;
            int factories = __instance.buildings?.Count(building => building?.asset != null &&
                                                                    FactoryTypes.Contains(building.asset.type)) ?? 0;
            if (factories == 0) return false;
            int perPop = Mathf.Max(1, _perPop == null ? 25 : (int)_perPop.GetValue(null));
            int target = __instance.getPopulationPeople() / perPop;
            var data = __instance.GetOrCreate();
            // 每座工厂每轮造一辆，补到额度为止
            if (data.virtual_vehicles < target) data.virtual_vehicles = Mathf.Min(target, data.virtual_vehicles + factories);
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] WarBox 账面载具结算失败: {exception.Message}");
        }
        return false;
    }

    // 已有的载具实体收进所在城的账面(静默移除，不计战死)；重新打开统领实体时账面清零，由工厂重新造实体
    public static void SweepVehicles()
    {
        if (World.world?.units == null) return;
        if (!CorpsSystem.NoMilitaryUnits)
        {
            foreach (City city in World.world.cities)
                if (city?.data != null && !city.isRekt() && city.GetOrCreate().virtual_vehicles > 0)
                    city.GetOrCreate().virtual_vehicles = 0;
            return;
        }
        foreach (Actor actor in World.world.units.ToList())
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !IsMilitaryVehicle(actor)) continue;
            if (actor.isFavorite() || actor.isCameraFollowingUnit()) continue;
            City city = actor.city ?? actor.kingdom?.capital;
            if (city?.data != null && !city.isRekt()) city.GetOrCreate().virtual_vehicles++;
            try { actor.die(true, AttackType.Other, false, false); }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 载具收入账面失败: {exception.Message}");
            }
        }
    }

    // 交战折损：本国账面载具按伤亡比例逐辆损失
    public static void LoseVehicles(Kingdom kingdom, float fraction)
    {
        if (kingdom?.cities == null || fraction <= 0f) return;
        foreach (City city in kingdom.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            var data = city.GetOrCreate();
            int lost = 0;
            for (int i = 0; i < data.virtual_vehicles; i++)
                if (UnityEngine.Random.value < fraction) lost++;
            data.virtual_vehicles -= lost;
        }
    }
}
