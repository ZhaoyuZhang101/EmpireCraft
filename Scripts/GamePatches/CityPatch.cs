using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;

public class CityPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {

        new Harmony(nameof(destroy_city)).Patch(
            AccessTools.Method(typeof(City), nameof(City.destroyCity)),
            prefix: new HarmonyMethod(GetType(), nameof(destroy_city))
        );

        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(City), nameof(City.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(removeData))
        );

        new Harmony(nameof(setKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.setKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(setKingdom))
        );

        new Harmony(nameof(zone_steal)).Patch(
            AccessTools.Method(typeof(CityBehBorderSteal), nameof(CityBehBorderSteal.tryStealZone)),
            prefix: new HarmonyMethod(GetType(), nameof(zone_steal))
        );

        new Harmony(nameof(removeObject)).Patch(
            AccessTools.Method(typeof(CitiesManager), nameof(CitiesManager.removeObject)),
            prefix: new HarmonyMethod(GetType(), nameof(removeObject))
        );

        new Harmony(nameof(city_update)).Patch(
            AccessTools.Method(typeof(City), nameof(City.update)),
            prefix: new HarmonyMethod(GetType(), nameof(city_update))
        );

        new Harmony(nameof(joinAnotherKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.joinAnotherKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(joinAnotherKingdom))
        );

        new Harmony(nameof(makeOwnKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.makeOwnKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(makeOwnKingdom))
        );

        new Harmony(nameof(addZone)).Patch(
            AccessTools.Method(typeof(City), nameof(City.addZone)),
            prefix: new HarmonyMethod(GetType(), nameof(addZone))
        );

        new Harmony(nameof(removeZone)).Patch(
            AccessTools.Method(typeof(City), nameof(City.removeZone)),
            prefix: new HarmonyMethod(GetType(), nameof(removeZone))
        );
    }

    public static bool joinAnotherKingdom(City __instance, Kingdom pNewSetKingdom, bool pCaptured = false, bool pRebellion = false)
    {
        string pHappinessEvent = null;
        if (pCaptured)
        {
            World.world.game_stats.data.citiesConquered++;
            World.world.map_stats.citiesConquered++;
            pHappinessEvent = "was_conquered";
        }

        if (pRebellion)
        {
            World.world.game_stats.data.citiesRebelled++;
            World.world.map_stats.citiesRebelled++;
            pHappinessEvent = "just_rebelled";
        }

        Kingdom pKingdom = __instance.kingdom;
        __instance.removeFromCurrentKingdom();
        if (pNewSetKingdom.isInEmpire()&&pCaptured&&!pKingdom.isEmpire())
        {
            Empire empire = pNewSetKingdom.GetEmpire();
            // 如果新加入的王国是帝国的一部分，并且城市被占领，则将城市加入帝国
            if (empire.getEmpirePeriod()!= EmpirePeriod.天命丧失&&empire.getEmpirePeriod() != EmpirePeriod.下降)
            {
                pNewSetKingdom = pNewSetKingdom.GetEmpire().empire;
            }
        }
        __instance.setKingdom(pNewSetKingdom);
        __instance.newForceKingdomEvent(__instance.units, __instance._boats, pNewSetKingdom, pHappinessEvent);
        __instance.switchedKingdom();
        pNewSetKingdom.capturedFrom(pKingdom);
        return false;
    }
    public static bool removeZone(City __instance, TileZone pZone)
    {
        if (ConfigData.PREVENT_CITY_DESTROY)
        {
            return false;
        }

        return true;
    }

    public static bool addZone(City __instance, TileZone pZone)
    {
        if (!__instance.zones.Contains(pZone))
        {
            if (pZone.city != null)
            {
                if (ConfigData.PREVENT_CITY_DESTROY)
                {
                    return false;
                }
                pZone.city.removeZone(pZone);
            }
            __instance.zones.Add(pZone);
            pZone.setCity(__instance);
            __instance.updateCityCenter();
            if (World.world.city_zone_helper.city_place_finder.hasPossibleZones())
            {
                World.world.city_zone_helper.city_place_finder.setDirty();
            }
            __instance.setStatusDirty();
        }
        return false;
    }
    public static bool makeOwnKingdom(City __instance, Actor pActor, bool pRebellion, bool pFellApart, ref Kingdom __result)
    {
        string pHappinessEvent = null;
        if (pRebellion)
        {
            World.world.game_stats.data.citiesRebelled++;
            World.world.map_stats.citiesRebelled++;
            pHappinessEvent = "just_rebelled";
        }
        if (pFellApart)
        {
            pHappinessEvent = "kingdom_fell_apart";
        }
        Kingdom pKingdom = __instance.kingdom;
        __instance.removeFromCurrentKingdom();
        __instance.removeLeader();
        Kingdom kingdom = World.world.kingdoms.makeNewCivKingdom(pActor);
        __instance.newForceKingdomEvent(__instance.units, __instance._boats, kingdom, pHappinessEvent);
        __instance.setKingdom(kingdom);
        __instance.switchedKingdom();
        kingdom.copyMetasFromOtherKingdom(pKingdom);
        kingdom.setCityMetas(__instance);
        if (pRebellion) 
        {
            kingdom.data.name = __instance.GetCityName() + " " + LM.Get("Rebellion");
        }
        __result = kingdom;
        return false;
    }


    public static void city_update(City __instance, float pElapsed)
    {
        if (ConfigData.PREVENT_CITY_DESTROY)
        {
            int v = Date.getYearsSince(__instance.data.created_time);
            if (v >= 1)
            {
                // 如果城市创建时间超过12个月，则不允许被摧毁
                if (__instance.isAlive())
                {
                    if (__instance.units.Count()<=1)
                    {
                        TransferUnits(__instance);
                    }
                }
            }
        }
        if (__instance.hasTitle())
        {
            __instance.GetTitle().isBeenControlled();
        }
    }
    public static bool removeObject(CitiesManager __instance, City pObject)
    {
        if (ConfigData.PREVENT_CITY_DESTROY)
        {
            int v = Date.getYearsSince(pObject.data.created_time);
            if (v >= 1)
            {
                {
                    TransferUnits(pObject);
                    return false;
                }
            }
        }
        return true;
    }

    public static void TransferUnits(City pCity)
    {
        double unitCount;
        if (pCity.hasKingdom())
        {
            // 如果城市有王国，则将国家人口1/20的士兵单位转移到城市中
            Kingdom kingdom = pCity.kingdom;
            unitCount = Math.Ceiling(kingdom.getPopulationTotal() / 20.0f);
            if (unitCount <= 0)
            {
                if (pCity.neighbours_kingdoms.Count() <=0)
                {
                    return;
                }
                pCity.joinAnotherKingdom(pCity.neighbours_kingdoms.GetRandom());
                return;
            }
            Army pArmy;
            if (kingdom.hasKing())
            {
                pArmy = World.world.armies.newArmy(kingdom.king, pCity);
            } else
            {
                pArmy = World.world.armies.newArmy(kingdom.units.GetRandom(), pCity);
            }
            kingdom.units.FindAll(u => u.isWarrior()).ForEach(u =>
            {
                if (u.isAlive())
                {
                    u.setArmy(pArmy);
                    u.setCity(pCity);
                    unitCount--;
                }
            });
        } else
        {
            if (pCity.neighbours_kingdoms.Count() <= 0)
            {
                return;
            }
            pCity.joinAnotherKingdom(pCity.neighbours_kingdoms.GetRandom());
            return;
        }
    }

    public static void setKingdom(City __instance, Kingdom pKingdom)
    {
        if (__instance.hasTitle())
        {
            __instance.GetTitle().isBeenControlled();
        }
    }

    public static bool zone_steal(CityBehBorderSteal __instance, City pCity)
    {
        if (ConfigData.PREVENT_CITY_DESTROY)
        {
            return false;
        }
        return true;
    }

    public static void destroy_city(City __instance)
    {
        if (__instance.hasTitle())
        {
            __instance.GetTitle().removeCity(__instance);
        }
    }
    public static void removeData(City __instance)
    {
        __instance.RemoveExtraData();
    }
}
