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
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General.Game.extensions;
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
        
        new Harmony(nameof(isArmyOverLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.isArmyOverLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(isArmyOverLimit))
        );

        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(City), nameof(City.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(removeData))
        );

        new Harmony(nameof(setKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.setKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(setKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(setKingdom_Postfix))
        );

        new Harmony(nameof(zone_steal)).Patch(
            AccessTools.Method(typeof(CityBehBorderSteal), nameof(CityBehBorderSteal.tryStealZone)),
            prefix: new HarmonyMethod(GetType(), nameof(zone_steal))
        );

        new Harmony(nameof(removeObject)).Patch(
            AccessTools.Method(typeof(CityManager), nameof(CityManager.removeObject)),
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

        new Harmony(nameof(TryToMakeWarrior)).Patch(
            AccessTools.Method(typeof(City), nameof(City.tryToMakeWarrior)),
            prefix: new HarmonyMethod(GetType(), nameof(TryToMakeWarrior))
        );

        new Harmony(nameof(newCity)).Patch(
            AccessTools.Method(typeof(City), nameof(City.newCityEvent)),
            prefix: new HarmonyMethod(GetType(), nameof(newCity))
        );

        new Harmony(nameof(FinishedCapture)).Patch(
            AccessTools.Method(typeof(City), nameof(City.finishCapture)),
            prefix: new HarmonyMethod(GetType(), nameof(FinishedCapture))
        );
        
        new Harmony(nameof(countWarriors)).Patch(
            AccessTools.Method(typeof(City), nameof(City.countWarriors)),
            prefix: new HarmonyMethod(GetType(), nameof(countWarriors))
        );
        new Harmony(nameof(getPopulationPeople)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getPopulationPeople)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationPeople))
        );
        new Harmony(nameof(getMainSubspecies)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getMainSubspecies)),
            prefix: new HarmonyMethod(GetType(), nameof(getMainSubspecies))
        );
    }

    public static bool getMainSubspecies(City __instance, ref Subspecies __result)
    {
        if (__instance.CountLivingPopulation() == 0)
        {
            __result = null;
            return false;
        }
        return true;
    }

    public static bool TryToMakeWarrior(City __instance, Actor pActor)
    {
        return false;
    }
    public static bool FinishedCapture(City __instance, Kingdom pNewKingdom)
    {
        Kingdom oldKingdom = __instance.kingdom;
        bool isEmpireCapital = oldKingdom.IsEmpire() && oldKingdom.capital == __instance;
        Empire targetEmpire = oldKingdom.GetEmpire();
        if (__instance.kingdom.hasKing() && __instance.kingdom.king.city == __instance)
            __instance.kingdom.kingFledCity();
        if (World.world.cities.isLocked())
            return false;
        __instance.clearCapture();
        __instance.recalculateNeighbourCities();
        pNewKingdom.increaseHappinessFromNewCityCapture();
        __instance.kingdom.decreaseHappinessFromLostCityCapture(__instance);
        if (targetEmpire != null)
        {
            if (!oldKingdom.IsInSameEmpire(pNewKingdom))
            {
                var newEmpire = pNewKingdom.GetEmpire();
                if (targetEmpire.CoreKingdom?.HasSameEmpireCraftCulture(newEmpire?.CoreKingdom) ?? false)
                {
                    targetEmpire.AddMandate(-20);
                }
                else
                {
                    targetEmpire.AddMandate(-10);
                }
            }
            else
            {
                if (oldKingdom.IsEmpire())
                {
                    targetEmpire.AddMandate(-40);
                }
            }
        }
        if (targetEmpire != null && isEmpireCapital)
        {
            Regime newRegime = pNewKingdom.GetRegime();
            if (newRegime != null && newRegime.type == RegimeType.Modern)
            {
                FixedFaction dominate = newRegime.GetDominateFaction();
                if (dominate != null)
                {
                    newRegime.BlockFactionChange(50); 
                    
                    if (newRegime.PlayerFactions != null)
                    {
                        foreach (var f in newRegime.PlayerFactions)
                        {
                            if (f.Type != dominate.Type)
                            {
                                f.BanFaction();
                                f.Ban = true;
                                f.Force = false;
                            }
                            else
                            {
                                f.Ban = false;
                                f.Force = true;
                            }
                        }
                    }

                    ActionLibrary.showWhisperTip($"{pNewKingdom.GetKingdomName()} 攻占首都，革命胜利！确立{dominate.Name}领导地位！");
                    
                    pNewKingdom.LoadRegime();
                }
            }
        }

        using (ListPool<War> pWars = new ListPool<War>(pNewKingdom.getWars()))
        {
            Kingdom joinAfterCapture = __instance.findKingdomToJoinAfterCapture(pNewKingdom, pWars);
            LogService.LogInfo($"{__instance.kingdom}的城市{__instance.name}即将被{joinAfterCapture.name}捕获");
            pWars.ToList().ForEach(w=>
            {
                LogService.LogInfo($"战争名称{w.name}类型：{w.GetEmpireWarType().ToString()}");
            });
            //检测城市是否被劫掠如果是则不执行占领城市逻辑但是相应的城市金库会被劫走
            var war = pWars.ToList().Find(w => w.GetEmpireWarType() == EmpireWarType.劫掠&&joinAfterCapture.isAttacker()&&joinAfterCapture.IsInEmpire());
            if (war!= null)
            {
                var money = 0;
                if (__instance.isCapitalCity())
                {
                    money = __instance.kingdom.GetMoney();
                    __instance.kingdom.SubMoney(__instance.kingdom.GetMoney());
                }
                else
                {
                    money = __instance.GetMoney();
                    __instance.SubMoney(__instance.GetMoney());
                }
                pNewKingdom.AddMoney(money);
                war.lostWar(__instance.kingdom);
                return false;
            }
            //检测是否为游牧扩张战争，如果是则返还法理土地，保留制度但是加入帝国
            var expendWar = pWars.ToList().Find(w => w.GetEmpireWarType() == EmpireWarType.游牧扩张&&joinAfterCapture.isAttacker()&&joinAfterCapture.IsInEmpire());
            if (expendWar != null)
            {
                LogService.LogInfo("游牧扩张战争");
                Empire empire = joinAfterCapture.GetEmpire();
                if (__instance.isCapitalCity()&&__instance.GetTitle()?.title_capital==__instance)
                {
                    __instance.GetTitle().city_list.ForEach(c =>
                    {
                        if (c.kingdom.IsInSameEmpire(joinAfterCapture) && !c.isCapitalCity())
                        {
                            c.joinAnotherKingdom(__instance.kingdom);
                        }
                    });
                    empire.join(__instance.kingdom, pForce:true);
                    expendWar.lostWar(__instance.kingdom);
                    return false;
                }
            }
            //检测是否为迫使朝贡战争，如果是则迫使该国家加入朝贡体系
            var chaoGongWar = pWars.ToList().Find(w => w.GetEmpireWarType() == EmpireWarType.迫使朝贡&&joinAfterCapture.isAttacker()&&joinAfterCapture.IsInEmpire());
            if (chaoGongWar != null)
            {
                LogService.LogInfo("迫使朝贡战争");
                Empire empire = joinAfterCapture.GetEmpire();
                if (__instance.isCapitalCity())
                {
                    __instance.kingdom.JoinTakenAlliance(empire);
                    chaoGongWar.lostWar(__instance.kingdom);
                    return false;
                }
            }
            //检测是否为迫使朝贡战争，如果是则迫使该国家加入朝贡体系
            var religionWar = pWars.ToList().Find(w => w.GetEmpireWarType() == EmpireWarType.神圣&&joinAfterCapture.isAttacker());
            if (religionWar != null)
            {
                LogService.LogInfo("宗教圣战");
                __instance.setReligion(joinAfterCapture.religion);
                __instance.units.ForEach(a=>a.setReligion(joinAfterCapture.religion));
                TranslateHelper.LogReligionWarTransfer(__instance, joinAfterCapture.religion);
            }
            if (!__instance.checkRebelWar(joinAfterCapture, pWars))
                joinAfterCapture.data.timestamp_new_conquest = World.world.getCurWorldTime();
            __instance.removeSoldiers();
            __instance.joinAnotherKingdom(joinAfterCapture, true);
        }

        return false;
    }
    public static bool countWarriors(City __instance, ref int __result)
    {
        __result = __instance.CountLivingWarriors();
        return false;
    }
    public static bool getPopulationPeople(City __instance, ref int __result)
    {
        __result = __instance.CountLivingPopulation();
        return false;
    }
    public static bool isArmyOverLimit(City __instance, ref bool __result)
    {
        if (__instance.kingdom.IsEmpire())
        {
            __result = false;
            return true;
        }
        if (__instance.status.warriors_current > __instance.status.warrior_slots)
        {
            __result = true;
            return false;
        }
        __result = false;
        return false; 
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
            if (__instance.leader.HasOfficeIdentity())
            {
                OfficeIdentity office = __instance.leader.GetIdentity();
                office.RemoveOffice();
            }
        }
    }

    public static bool setLeader(City __instance, Actor pActor, bool pNew)
    {
        if (pActor != null && __instance.kingdom.king != pActor)
        {
            __instance.leader = pActor;
            __instance.leader.setProfession(UnitProfession.Leader);
            CityData cityData = __instance.data;
            long leaderID = (__instance.data.last_leader_id = pActor.data.id);
            cityData.leaderID = leaderID;
            if (pNew)
            {
                __instance.data.total_leaders++;
                __instance.leader.changeHappiness("become_leader");
                __instance.addRuler(pActor);
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
        __instance.SetCorruptionRate(0.0f);
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
        if (pNewSetKingdom.IsInEmpire())
        {
            var empire = pNewSetKingdom.GetEmpire();
            if (empire != null)
            {
                if (!empire.cities_list.Contains(__instance))
                {
                    empire.cities_list.Add(__instance);
                }
            }
        }
        if (pNewSetKingdom.IsInEmpire()&&pCaptured&&!pKingdom.IsEmpire())
        {
            Empire empire = pNewSetKingdom.GetEmpire();
            if (!empire.CoreKingdom.isInWarWith(pKingdom))
            {
                var dominate = empire.CoreKingdom.GetRegime().GetDominateFaction();
                if (dominate != null)
                {
                    if (dominate.Type != FactionType.自治 && dominate.Type != FactionType.诸侯)
                    {
                        // 如果新加入的王国是帝国的一部分，并且城市被占领，则将城市加入帝国
                        pNewSetKingdom = empire.CoreKingdom;
                    } 
                }
            }
        }
        
        if (pKingdom != null)
        {
            var empire = pKingdom.GetEmpire();
            empire?.cities_list.Remove(__instance);
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
            kingdom.getWars()?.First()?.SetEmpireWarType(EmpireWarType.地方独立);
            kingdom.StartLocalRebelling(EmpireWarType.地方独立);
        }
        __result = kingdom;
        return false;
    }


    public static void city_update(City __instance, float pElapsed)
    {
        /*
        if (__instance.hasTitle())
        {
            if (__instance.GetTitle() != null)
            {
                __instance.GetTitle().isBeenControlled();
            }
        }
        */
    }
    public static bool removeObject(CityManager __instance, City pObject)
    {
        return true;
    }

    public static void setKingdom(City __instance, Kingdom pKingdom)
    {
        Regime regime = pKingdom.GetRegime();
        if (regime != null)
        {
            CityType cityType = regime.bureau_config.kingdoms.TryGetValue(pKingdom.GetKingdomType(), out var value)
                ?regime.bureau_config.kingdoms[pKingdom.GetKingdomType()].city_type
                : regime.bureau_config.cities.Keys.First();
            BureauSetting citySetting = regime.bureau_config.cities[cityType];
            OfficeObject officeObject = __instance.GetOffice();
            if (officeObject != null)
            {
                officeObject.InitialOffice(citySetting, isNew:false);
                officeObject.regimeType = regime.type;
            }
            else
            {
                officeObject = new OfficeObject();
                officeObject.InitialOffice(citySetting);
                officeObject.regimeType = regime.type;
                __instance.SetOffice(officeObject);
            }
        }

        if (pKingdom.IsInEmpire())
        {
            var empire = pKingdom.GetEmpire();
            if (empire != null)
            {
                empire.cities_list.Add(__instance);
                empire.cities_list = empire.cities_list.Distinct().ToList();
            }
        }
    }

    public static void setKingdom_Postfix(City __instance)
    {
        if (__instance.hasTitle())
        {
            __instance.GetTitle().isBeenControlled();
        }
    }
    
    //创建新城市时触发
    public static void newCity(City __instance, Actor pActor)
    {
        
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
        foreach (var religion in World.world.religions)
        {
            if (religion.GetCity() == __instance)
            {
                if (religion.getCities().Count() > 0)
                {
                    religion.SetCity(religion.cities.First());
                }
            }
        }

        if (__instance.hasKingdom())
        {
            var kingdom = __instance.kingdom;
            if (kingdom != null)
            {
                var empire = kingdom.GetEmpire();
                if (empire != null)
                {
                    empire.cities_list.Remove(__instance);
                }
            }
        }
        if (__instance.hasTitle())
        {
            __instance.GetTitle().removeCity(__instance);
        }
    }
    public static void removeData(City __instance)
    {
        __instance.RemoveExtraData<City, CityExtraData>();
    }
}
