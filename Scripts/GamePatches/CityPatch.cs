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
using EmpireCraft.Scripts.GameLibrary;
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

        new Harmony(nameof(hasReachedWorldLawLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.hasReachedWorldLawLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(hasReachedWorldLawLimit))
        );
        
        new Harmony(nameof(getPopulationMaximum)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getPopulationMaximum)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationMaximum))
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
    public static bool getPopulationMaximum(City __instance, ref int __result)
    {
        if (__instance.GetMaxPopulationLimitStats())
        {
            __result = __instance.GetMaxPopulation();
            return false;
        }
        // 先按住房量给一个基础上限
        int cap = __instance.status.housing_total;

        // 依次套用 20/50/100 三个世界法令的限制（哪个开了就取 min）
        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_20?.isEnabled() == true)
            cap = Math.Min(cap, 20);

        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_50?.isEnabled() == true)
            cap = Math.Min(cap, 50);

        if (WorldLawLibrary.world_law_civ_limit_population_100?.isEnabled() == true)
            cap = Math.Min(cap, 100);

        __result = cap;
        return false;
    }
    public static bool hasReachedWorldLawLimit(City __instance, ref bool __result)
    {
        if (WorldLawLibrary.world_law_civ_limit_population_100.isEnabled() && __instance.getPopulationPeople() >= 100)
        {
            __result = true;
            return false;
        }
        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_50.isEnabled() && __instance.getPopulationPeople() >= 50)
        {
            __result = true;
            return false;
        }
        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_20.isEnabled() && __instance.getPopulationPeople() >= 20)
        {
            __result = true;
            return false;
        }
        __result = false;
        return false;
    }
    public static void removeLeader(City __instance)
    {
        if (__instance.leader!=null)
        {
            if (__instance.kingdom.isInEmpire())
            {
                Empire empire = __instance.kingdom.GetEmpire();
                __instance.leader.ChangeOfficialLevel(OfficialLevel.officiallevel_10);
            }
            else
            {
                __instance.leader.RemoveIdentity();
                if (__instance.leader.hasTrait("officer"))
                {
                    __instance.leader.removeTrait("officer");
                }
            }
        }
    }

    public static bool setLeader(City __instance, Actor pActor, bool pNew)
    {
        if (__instance.hasProvince())
        {
            if (__instance.GetProvince() != null)
            {
                if (__instance.GetProvince().officer!=null)
                {
                    if (__instance.GetProvince().officer == pActor)
                    {
                        return false ;
                    }
                }
            }
        }
        if (pActor != null && __instance.kingdom.king != pActor)
        {
            if (__instance.kingdom.isInEmpire())
            {
                Empire empire = __instance.kingdom.GetEmpire();
                OfficeIdentity identity = pActor.GetIdentity(empire);
                if (identity==null)
                {
                    identity = new OfficeIdentity();
                    identity.init(empire, pActor);
                    pActor.SetIdentity(identity, true);
                }
                pActor.ChangeOfficialLevel(OfficialLevel.officiallevel_9);
                pActor.SetIdentityType();
                pActor.addTrait("officer");
            }

            __instance.leader = pActor;
            __instance.leader.setProfession(UnitProfession.Leader);
            CityData cityData = __instance.data;
            long leaderID = (__instance.data.last_leader_id = pActor.data.id);
            cityData.leaderID = leaderID;
            if (pNew)
            {
                __instance.data.total_leaders++;
                __instance.leader.changeHappiness("become_leader");
                __instance.data.addRuler(pActor);
            }
        }
        pActor.CheckSpecificClan();
        return false;
    }
    public static bool joinAnotherKingdom(City __instance, Kingdom pNewSetKingdom, bool pCaptured = false, bool pRebellion = false)
    {
        // 参数检查
        if (__instance == null || pNewSetKingdom == null)
        {
            return false;
        }

        if (__instance.hasProvince())
        {
            Province province = __instance.GetProvince();
            bool isSameEmpire = province?.empire?.CoreKingdom?.isInSameEmpire(pNewSetKingdom) ?? false;
            bool isCurrentlyOccupied = province?.occupied_cities?.ContainsKey(__instance) ?? false;
            double currentTime = World.world.getCurWorldTime();

            // 不同帝国时标记为占领
            if (!isSameEmpire)
            {
                if (province.occupied_cities == null)
                {
                    province.occupied_cities = new Dictionary<City, double>();
                }

                province.occupied_cities[__instance] = currentTime;
                return true;
            }
            // 同一帝国时移除占领状态
            else if (isCurrentlyOccupied)
            {
                province.occupied_cities.Remove(__instance);
            }
        }
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
            if (empire.GetEmpirePeriod()!= EmpirePeriod.天命丧失&&empire.GetEmpirePeriod() != EmpirePeriod.下降)
            {
                pNewSetKingdom = pNewSetKingdom.GetEmpire().CoreKingdom;
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
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy.isEnabled())
        {
            return false;
        }

        return true;
    }
    private static void UpdateCityOccupationStatus(Province province, City city)
    {
        double currentTime = World.world.getCurWorldTime();

        if (province.occupied_cities == null)
        {
            province.occupied_cities = new Dictionary<City, double>();
        }

        // 简化字典操作
        province.occupied_cities[city] = currentTime;
    }

    private static void ApplyRebellionPenalty(Province province)
    {
        Empire empire = province.empire;
        if (empire != null && empire.CoreKingdom != null)
        {
            int renownLoss = empire.CoreKingdom.getRenown() / 2;
            empire.AddRenown(-renownLoss);

            // 可选：添加日志记录
            LogService.LogInfo($"Empire {empire.name} lost {renownLoss} renown due to rebellion");
        }
    }
    public static bool addZone(City __instance, TileZone pZone)
    {
        if (!__instance.zones.Contains(pZone))
        {
            if (pZone.city != null)
            {
                if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy.isEnabled())
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
        if (__instance == null || pActor == null)
        {
            return false;
        }

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
            kingdom.data.name = __instance.GetCityName() + "\u200A" + LM.Get("Rebellion");
        }
        __result = kingdom;

        if (__instance.hasProvince())
        {
            Province province = __instance.GetProvince();
            if (province == null)
            {
                return true;
            }

            // 更新占领状态
            UpdateCityOccupationStatus(province, __instance);

            // 处理叛乱惩罚
            if (pRebellion)
            {
                ApplyRebellionPenalty(province);
            }
        }
        return false;
    }


    public static void city_update(City __instance, float pElapsed)
    {
        if (__instance.hasTitle())
        {
            if (__instance.GetTitle() != null)
            {
                __instance.GetTitle().isBeenControlled();
            }
        }
    }
    public static bool removeObject(CitiesManager __instance, City pObject)
    {
        return true;
    }

    public static void setKingdom(City __instance, Kingdom pKingdom)
    {
        Kingdom oldKingdom = __instance.kingdom;
        if (oldKingdom != null) 
        {
            if (oldKingdom.isProvince())
            {
                oldKingdom.checkLostProvince();
            }
        }
        if (__instance.hasTitle())
        {
            __instance.GetTitle().isBeenControlled();
        }
    }

    public static bool zone_steal(CityBehBorderSteal __instance, City pCity)
    {
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy.isEnabled())
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
        if (__instance.hasProvince()) 
        {
            __instance.GetProvince().removeCity(__instance);
            if (__instance.GetProvince() == null) return;
            if (__instance.GetProvince().occupied_cities == null) return;
            if (__instance.GetProvince().occupied_cities.ContainsKey(__instance))
            {
                __instance.GetProvince().occupied_cities.Remove(__instance);
            }

        }
    }
    public static void removeData(City __instance)
    {
        __instance.RemoveExtraData<City, CityExtraData>();
    }
}
