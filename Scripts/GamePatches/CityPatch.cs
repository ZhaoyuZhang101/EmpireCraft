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
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.HelperFunc;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;

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

        new Harmony(nameof(setLeader)).Patch(
            AccessTools.Method(typeof(City), nameof(City.setLeader)),
            prefix: new HarmonyMethod(GetType(), nameof(setLeader))
        );

        new Harmony(nameof(removeLeader)).Patch(
            AccessTools.Method(typeof(City), nameof(City.removeLeader)),
            prefix: new HarmonyMethod(GetType(), nameof(removeLeader))
        );
    }
    public static void removeLeader(City __instance)
    {
        // todo: 当领主被移除时触发
    }

    public static bool setLeader(City __instance, Actor pActor, bool pNew)
    {
        // todo: 设置新领主时触发
        return true;
    }
    public static bool joinAnotherKingdom(City __instance, Kingdom pNewSetKingdom, bool pCaptured = false, bool pRebellion = false)
    {
        // todo: 城市加入其他王国时触发
        return true;
    }
    public static bool removeZone(City __instance, TileZone pZone)
    {
        // todo: 城市移除区块时触发
        return true;
    }
    
    public static bool addZone(City __instance, TileZone pZone)
    {
        // todo: 城市增加区块时触发
        return true;
    }
    public static bool makeOwnKingdom(City __instance, Actor pActor, bool pRebellion, bool pFellApart, ref Kingdom __result)
    {
        // todo: 城市建立独立国家时触发
        return true;
    }


    public static void city_update(City __instance, float pElapsed)
    {
        // todo: 城市进程更新时触发
    }
    public static bool removeObject(CitiesManager __instance, City pObject)
    {
        // todo: 城市实体被移除时触发
        return true;
    }

    public static void setKingdom(City __instance, Kingdom pKingdom)
    {
        // todo: 城市设置王国时触发
    }

    public static bool zone_steal(CityBehBorderSteal __instance, City pCity)
    {
        // todo: 城市窃取边境时触发
        return true;
    }

    public static void destroy_city(City __instance)
    {
        // todo: 城市毁灭时触发
    }
    public static void removeData(City __instance)
    {
        __instance.RemoveExtraData<City, CityExtraData>();
    }
}
