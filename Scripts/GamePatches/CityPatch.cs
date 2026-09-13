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
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;

namespace EmpireCraft.Scripts.GamePatches;

public class CityPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    // ============================================================
    // 低兵力城市：帝国军入城即降
    // ============================================================

    // 整个守方 Kingdom 只剩 3 名及以下存活 Warrior 时触发。
    private const int ImperialArrivalSurrenderWarriorThreshold = 3;

    // 每帧只扫描一次 World.world.units，避免每座城市都全量扫描单位。
    private static int _imperialPresenceCacheFrame = -1;

    // 同一帧内缓存“整个 Kingdom 的存活 Warrior 总数”。
    // 统计包含在外征战、驻扎其他城市、正在移动的所有本国 Warrior，
    // 而不是只统计当前城市里的守军。
    private static int _kingdomWarriorCountCacheFrame = -1;
    private static readonly Dictionary<Kingdom, int> _kingdomLivingWarriorCounts = new();

    // key = 当前帝国士兵实际所在的城市
    // value = 第一个检测到的、与该城市所属国家交战的帝国士兵所属 Kingdom
    private static readonly Dictionary<City, Kingdom> _imperialAttackerByCity = new();

    // 防止 finishCapture -> 城市状态变化 -> update 重入导致重复归降。
    private static readonly HashSet<City> _imperialArrivalSurrenderGuard = new();

    public void Initialize()
    {

        new Harmony(nameof(destroy_city)).Patch(
            AccessTools.Method(typeof(City), nameof(City.destroyCity)),
            prefix: new HarmonyMethod(GetType(), nameof(destroy_city))
        );

        new Harmony(nameof(HasReachedWorldLawLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.hasReachedWorldLawLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(HasReachedWorldLawLimit))
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

        new Harmony(nameof(GetHouseLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getHouseLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(GetHouseLimit))
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

        new Harmony(nameof(RecalculateMaxHouses)).Patch(
            AccessTools.Method(typeof(City), nameof(City.recalculateMaxHouses)),
            prefix: new HarmonyMethod(GetType(), nameof(RecalculateMaxHouses))
        );

        new Harmony(nameof(AddCapturePoints)).Patch(
            original: AccessTools.Method(
                typeof(City),
                nameof(City.addCapturePoints),
                new Type[] { typeof(Kingdom), typeof(int) }
            ),
            prefix: new HarmonyMethod(GetType(), nameof(AddCapturePoints))
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
        new Harmony(nameof(CanUseBuildAsset)).Patch(
            AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.canUseBuildAsset)),
            prefix: new HarmonyMethod(GetType(), nameof(CanUseBuildAsset))
        );
    }
    public static bool CanUseBuildAsset(CityBehBuild __instance, BuildOrder pBuildAsset, City pCity, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pCity)) return true;
        BuildingAsset buildingAsset = pBuildAsset.getBuildingAsset(pCity);
        if (pBuildAsset.min_zones != 0 && pCity.zones.Count < pBuildAsset.min_zones)
        {
            __result = false;
            return false;
        }
        int num = pCity.countBuildingsType(buildingAsset.type, false);
        if (pBuildAsset.check_house_limit)
        {
            if (pCity.status.housing_free > 10)
            {
                __result = false;
                return false;
            }
            int houseLimit = pCity.getHouseLimit();
            if (num >= houseLimit)
            {
                __result = false;
                return false;
            }
        }
        int limitOfBuildingsType = pCity.getLimitOfBuildingsType(pBuildAsset);
        if (!pCity.HasBeenCombined())
        {
            if (pCity.status.population < pBuildAsset.required_pop ||
                pCity.buildings.Count < pBuildAsset.required_buildings)
            {
                __result = false;
                return false;
            }
        }
        if (limitOfBuildingsType != 0 && num >= limitOfBuildingsType ||
            pBuildAsset.check_full_village && pCity.status.housing_free != 0 ||
            !CityBehBuild.haveRequiredBuildings(pBuildAsset, pCity) ||
            !CityBehBuild.haveRequiredBuildingTypes(pBuildAsset.requirements_types, pCity))
        {
            __result = false;
            return false;
        }
        if (pBuildAsset.upgrade)
        {
            List<Building> buildingListOfId = pCity.getBuildingListOfID(buildingAsset.id);
            if (buildingListOfId == null || buildingListOfId.Count == 0)
            {
                __result = false;
                return false;
            }
        }
        else if (buildingAsset.docks && CityBehBuild.getDockTile(pCity) == null)
        {
            __result = false;
            return false;
        }
        __result = true;
        return false;
    }
    public static bool GetHouseLimit(City __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.buildings.Any(b => b.asset.id.Contains("city_")))
        {
            __result = 1;
            return false;
        }
        return true;
    } 
    private static bool RecalculateMaxHouses(City __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.buildings.Any(b => b.asset.id.Contains("city_")))
        {
            __instance.status.houses_max = 0;
            return false;
        }
        return true;
    }
    public static bool AddCapturePoints(City __instance, Kingdom pKingdom, int pValue)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled()) return false;
        if (__instance.TryTriggerOccupationCaptureEvent(pKingdom))
        {
            return false;
        }
        int num;
        __instance._capturing_units.TryGetValue(pKingdom, out num);
        //正统值加成
        var mandateAddition = 0;
        //原帝国
        var originEmpirePoint = (__instance.kingdom?.GetEmpire()?.Mandate??0);
        //新帝国
        var captureEmpirePoint = (pKingdom.GetEmpire()?.Mandate??0);
        mandateAddition += Mathf.Clamp(captureEmpirePoint-originEmpirePoint, 0, 30);
        //同文化加成
        var sameCultureFactor = (__instance.kingdom?.GetEmpireCraftCulture()==pKingdom.GetEmpireCraftCulture())?2:0;
        mandateAddition *= sameCultureFactor;
        var kingDeathAddition = 0;
        if (__instance.kingdom == null || !__instance.kingdom.hasKing() || __instance.kingdom.king == null || __instance.kingdom.king.isRekt() || !__instance.kingdom.king.isAlive())
        {
            kingDeathAddition = 20;
        }
        __instance._capturing_units[pKingdom] = num + pValue + mandateAddition + kingDeathAddition;
        return false;
    }
    public static bool getMainSubspecies(City __instance, ref Subspecies __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.CountLivingPopulation() == 0)
        {
            __result = null;
            return false;
        }
        return true;
    }

    public static bool TryToMakeWarrior(City __instance, Actor pActor)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        return false;
    }
    public static bool FinishedCapture(City __instance, Kingdom pNewKingdom)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        Kingdom oldKingdom = __instance.kingdom;
        bool isEmpireCapital = oldKingdom.IsEmpire() && oldKingdom.capital == __instance;
        Empire targetEmpire = oldKingdom.GetEmpire();
        if (__instance.kingdom.hasKing() && __instance.kingdom.king.city == __instance)
            __instance.kingdom.kingFledCity();
        if (World.world.cities.isLocked())
            return false;
        __instance.clearCapture();
        __instance.ClearOccupiedStatus();
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
            War war = null;
            War expendWar = null;
            War chaoGongWar = null;
            War religionWar = null;
            War empireRoyalAcquireEmpireWar = null;
            bool attackerInEmpire = joinAfterCapture.isAttacker() && joinAfterCapture.IsInEmpire();
            foreach (War w in pWars)
            {
                LogService.LogInfo($"战争名称{w.name}类型：{w.GetEmpireWarType().ToString()}");
                EmpireWarType warType = w.GetEmpireWarType();
                if (war == null && attackerInEmpire && warType == EmpireWarType.劫掠)
                    war = w;
                if (expendWar == null && attackerInEmpire && warType == EmpireWarType.游牧扩张)
                    expendWar = w;
                if (chaoGongWar == null && attackerInEmpire && warType == EmpireWarType.迫使朝贡)
                    chaoGongWar = w;
                if (religionWar == null && joinAfterCapture.isAttacker() && warType == EmpireWarType.神圣)
                    religionWar = w;
                if (empireRoyalAcquireEmpireWar == null && attackerInEmpire && warType == EmpireWarType.藩王索取皇位)
                    empireRoyalAcquireEmpireWar = w;
            }
            //检测城市是否被劫掠如果是则不执行占领城市逻辑但是相应的城市金库会被劫走
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
            if (chaoGongWar != null)
            {
                LogService.LogInfo("迫使朝贡战争");
                Empire empire = joinAfterCapture.GetEmpire();
                if (__instance.isCapitalCity())
                {
                    __instance.kingdom.JoinTakenAlliance(empire, pForce: true);
                    chaoGongWar.lostWar(__instance.kingdom);
                    return false;
                }
            }
            //检测是否为迫使朝贡战争，如果是则迫使该国家加入朝贡体系
            if (religionWar != null)
            {
                LogService.LogInfo("宗教圣战");
                __instance.setReligion(joinAfterCapture.religion);
                __instance.units.ForEach(a=>a.setReligion(joinAfterCapture.religion));
                TranslateHelper.LogReligionWarTransfer(__instance, joinAfterCapture.religion);
            }
            if (empireRoyalAcquireEmpireWar != null)
            {
                LogService.LogInfo("藩王之乱");
                if (__instance.isCapitalCity())
                {
                    var empire = joinAfterCapture.GetEmpire();
                    var newEmperor = joinAfterCapture.king;
                    if (newEmperor != null&&newEmperor.GetSpecificClan()==empire.EmpireSpecificClan)
                    {
                        TranslateHelper.LogRoyalKingBecomeEmperor(empire, joinAfterCapture.GetMainTitle()??joinAfterCapture.capital.GetTitle(), newEmperor);
                        empire.CoreKingdom.GetOffice().meta_object = empire.CoreKingdom;
                        empire.CoreKingdom.GetOffice().SetActor(newEmperor);
                        joinAfterCapture.cities.ForEach(c=>c.joinAnotherKingdom(empire.CoreKingdom));
                        return false;
                    }
                    empireRoyalAcquireEmpireWar.leaveWar(joinAfterCapture);
                    joinAfterCapture.EndLocalRebelling();
                    joinAfterCapture.GetRegime().SetAllowArmy(false);
                    joinAfterCapture.GetRegime().SetAllowSupportCenterArmy(false);
                    joinAfterCapture.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
                    return false;
                }
            }
            if (TryTriggerEmpirePressureSurrender(__instance, joinAfterCapture, oldKingdom))
            {
                return false;
            }
            if (!__instance.checkRebelWar(joinAfterCapture, pWars))
                joinAfterCapture.data.timestamp_new_conquest = World.world.getCurWorldTime();
            __instance.removeSoldiers();
            __instance.joinAnotherKingdom(joinAfterCapture, true);
        }

        return false;
    }

    private static bool TryTriggerEmpirePressureSurrender(City capturedCity, Kingdom attackerKingdom, Kingdom defenderKingdom)
    {
        if (capturedCity == null || attackerKingdom == null || defenderKingdom == null) return false;
        if (capturedCity.isRekt() || attackerKingdom.isRekt() || defenderKingdom.isRekt()) return false;

        Empire attackerEmpire = attackerKingdom.GetEmpire();
        Empire defenderEmpire = defenderKingdom.GetEmpire();
        if (attackerEmpire == null || defenderEmpire == null || attackerEmpire == defenderEmpire) return false;
        if (defenderEmpire.CoreKingdom == null || defenderEmpire.CoreKingdom.isRekt()) return false;
        if (defenderEmpire.cities_list == null) return false;
        if (!attackerKingdom.isInWarWith(defenderEmpire.CoreKingdom) && !defenderEmpire.CoreKingdom.isInWarWith(attackerKingdom)) return false;

        bool hitEmpireCapital = defenderEmpire.CoreKingdom.capital == capturedCity;
        bool hitKingdomCapital = defenderKingdom.capital == capturedCity;
        int totalCities = defenderEmpire.cities_list?.Count(c => c != null && !c.isRekt()) ?? 0;
        if (totalCities <= 0) return false;

        int occupiedCities = 0;
        for (int i = 0; i < defenderEmpire.cities_list.Count; i++)
        {
            City city = defenderEmpire.cities_list[i];
            if (city == null || city.isRekt()) continue;
            if (city == capturedCity)
            {
                occupiedCities++;
                continue;
            }

            Kingdom cityKingdom = city.kingdom;
            if (cityKingdom != null && !cityKingdom.isRekt() && cityKingdom.IsInSameEmpire(attackerKingdom))
            {
                occupiedCities++;
            }
        }

        bool reachedThird = occupiedCities * 3 >= totalCities;
        if (!hitEmpireCapital && !reachedThird) return false;

        int surrenderChance = CalculateEmpirePressureSurrenderChance(defenderEmpire, hitEmpireCapital, reachedThird);
        if (surrenderChance <= 0) return false;
        if (surrenderChance < 70 && UnityEngine.Random.Range(0, 100) >= surrenderChance) return false;

        string targetName;
        bool cityOnly = false;
        if (hitEmpireCapital)
        {
            ForceEmpirePressureSurrenderTo(defenderEmpire, attackerKingdom, capturedCity);
            targetName = defenderEmpire.CoreKingdom.GetKingdomName();
        }
        else if (hitKingdomCapital)
        {
            ForceSurrenderKingdomTo(defenderKingdom, attackerKingdom, capturedCity);
            targetName = defenderKingdom.GetKingdomName();
        }
        else
        {
            capturedCity.joinAnotherKingdom(attackerKingdom, true);
            targetName = capturedCity.GetCityName();
            cityOnly = true;
        }

        defenderEmpire.CoreKingdom.EndWarWith(attackerEmpire.CoreKingdom);
        defenderKingdom.EndWarWith(attackerEmpire.CoreKingdom);
        TranslateHelper.LogEmpirePressureSurrender(attackerEmpire, defenderEmpire, targetName, cityOnly);
        return true;
    }

    private static int CalculateEmpirePressureSurrenderChance(Empire defenderEmpire, bool hitEmpireCapital, bool reachedThird)
    {
        if (defenderEmpire == null || defenderEmpire.CoreKingdom == null) return 0;
        int mandate = Mathf.Clamp(defenderEmpire.Mandate, 0, 100);
        if (mandate > 70) return 0;

        int chance = Mathf.Clamp(70 - mandate, 0, 100);
        if (reachedThird) chance += 15;
        if (hitEmpireCapital) chance += 25;
        if (defenderEmpire.CoreKingdom.GetMoney() <= 0) chance += 35;
        chance = Mathf.Clamp(chance, 0, 100);
        return chance;
    }

    private static void ForceSurrenderKingdomTo(Kingdom defeated, Kingdom occupier, City capturedCity)
    {
        if (defeated == null || occupier == null || defeated.isRekt() || occupier.isRekt() || defeated == occupier)
        {
            return;
        }

        List<City> citySnapshot = new List<City>(defeated.cities);
        if (capturedCity != null && !capturedCity.isRekt() && capturedCity.kingdom == defeated)
        {
            capturedCity.joinAnotherKingdom(occupier, true);
        }

        for (int i = 0; i < citySnapshot.Count; i++)
        {
            City targetCity = citySnapshot[i];
            if (targetCity == null || targetCity.isRekt() || targetCity == capturedCity || targetCity.kingdom != defeated)
            {
                continue;
            }

            targetCity.joinAnotherKingdom(occupier, true);
        }
    }

    private static void ForceEmpirePressureSurrenderTo(Empire defenderEmpire, Kingdom occupier, City capturedCity)
    {
        if (defenderEmpire == null || occupier == null || occupier.isRekt()) return;

        HashSet<Kingdom> surrenderedKingdoms = new HashSet<Kingdom>();
        if (defenderEmpire.CoreKingdom != null && !defenderEmpire.CoreKingdom.isRekt())
        {
            surrenderedKingdoms.Add(defenderEmpire.CoreKingdom);
        }

        for (int i = 0; i < defenderEmpire.kingdoms_list.Count; i++)
        {
            Kingdom kingdom = defenderEmpire.kingdoms_list[i];
            if (kingdom == null || kingdom.isRekt() || kingdom == defenderEmpire.CoreKingdom) continue;
            Regime regime = kingdom.GetRegime();
            if (regime != null && !regime.IsAllowDiplomacy())
            {
                surrenderedKingdoms.Add(kingdom);
            }
        }

        foreach (var kingdom in surrenderedKingdoms)
        {
            ForceSurrenderKingdomTo(kingdom, occupier, capturedCity);
            kingdom.EndWarWith(occupier);
        }
    }
    public static bool countWarriors(City __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        __result = __instance.CountLivingWarriors();
        return false;
    }
    public static bool getPopulationPeople(City __instance, ref int __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        __result = __instance.CountLivingPopulation();
        return false;
    }
    public static bool isArmyOverLimit(City __instance, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
    public static bool HasReachedWorldLawLimit(City __instance, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
        pKingdom?.InvalidateCityValueContext();
        pNewSetKingdom.InvalidateCityValueContext();
        __instance.InvalidateCityStrategicValue();
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
        __instance.ClearOccupiedStatus();
        __instance.InvalidateCityStrategicValue();
        pNewSetKingdom.InvalidateCityValueContext();
        return false;
    }
    public static bool removeZone(City __instance, TileZone pZone)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy.isEnabled())
        {
            return false;
        }

        return true;
    }
    public static bool addZone(City __instance, TileZone pZone)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
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
        CityValueSnapshot rebellionOriginValue = pRebellion ? __instance.GetCityStrategicValue() : default;
        pKingdom?.InvalidateCityValueContext();
        __instance.InvalidateCityStrategicValue();
        Empire rebellionOrigin = pRebellion ? pKingdom?.GetEmpire() : null;
        __instance.removeFromCurrentKingdom();
        __instance.removeLeader();
        Kingdom kingdom = World.world.kingdoms.makeNewCivKingdom(pActor);
        __instance.newForceKingdomEvent(__instance.units, __instance._boats, kingdom, pHappinessEvent);
        __instance.setKingdom(kingdom);
        __instance.switchedKingdom();
        kingdom.copyMetasFromOtherKingdom(pKingdom);
        kingdom.setCityMetas(__instance);
        kingdom.RememberRebellionOrigin(rebellionOrigin);
        if (pRebellion) kingdom.SetRebellionOriginCityValue(rebellionOriginValue);
        kingdom.InvalidateCityValueContext();
        __instance.InvalidateCityStrategicValue();
        __result = kingdom;
        return false;
    }


    public static void city_update(City __instance, float pElapsed)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;

        // 守城军已经被打到 3 人及以下时：
        // 只要敌对帝国的 Warrior 实际进入本城市任意 Zone，
        // 该城立即归降。
        TryImmediateSurrenderOnImperialArmyArrival(__instance);

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

    /// <summary>
    /// 城市低兵力时，检测敌对帝国 Warrior 是否已经实际进入该城市。
    /// 满足条件后直接调用 finishCapture，而不是 setKingdom：
    /// 这样仍然会进入本 CityPatch 已有的 FinishedCapture 逻辑，
    /// 包括特殊战争、帝国压力投降、占领清理、日志等。
    /// </summary>
    private static void TryImmediateSurrenderOnImperialArmyArrival(City city)
    {
        if (city == null || city.isRekt())
            return;

        if (city.kingdom == null || city.kingdom.isRekt())
            return;

        // 避免重入。
        if (_imperialArrivalSurrenderGuard.Contains(city))
            return;

        // 不再使用 city.kingdom.hasEnemies() 作为前置条件。
        // 帝国战争可能只登记在帝国核心国上，地方王国本身未必 hasEnemies()。

        Kingdom defenderKingdom = city.kingdom;

        // 这里检查的是“整个国家”的存活 Warrior 总数，
        // 不是这座城市自己的 countWarriors()。
        int kingdomWarriors = GetKingdomLivingWarriorCount(defenderKingdom);

        // 整个国家只剩 0 / 1 / 2 / 3 个 Warrior 时，
        // 才允许帝国军入城触发该城立即归降。
        if (kingdomWarriors > ImperialArrivalSurrenderWarriorThreshold)
            return;

        EnsureImperialPresenceCache();

        if (!_imperialAttackerByCity.TryGetValue(city, out Kingdom attackerKingdom))
            return;

        if (!IsValidImperialSurrenderAttacker(city, attackerKingdom))
            return;

        try
        {
            _imperialArrivalSurrenderGuard.Add(city);

            // 直接走原版/本模组“完成占领”的入口。
            //
            // 不直接 city.joinAnotherKingdom()，
            // 因为 finishCapture 已被本 CityPatch Patch，
            // 会继续执行劫掠、游牧扩张、迫使朝贡、圣战、
            // 藩王索取皇位、帝国压力投降等现有规则。
            LogService.LogInfo(
                $"国家兵力崩溃，城市立即归降：{city.name}，{defenderKingdom.name}全国剩余士兵={kingdomWarriors}，归降于={attackerKingdom.name}");

            city.finishCapture(attackerKingdom);
        }
        catch (Exception e)
        {
            LogService.LogWarning(
                $"帝国军入城即降失败：城市={city?.name}, 攻方={attackerKingdom?.name}, {e.Message}");
        }
        finally
        {
            _imperialArrivalSurrenderGuard.Remove(city);
        }
    }

    /// <summary>
    /// 返回整个 Kingdom 当前所有存活 Warrior 的总数。
    ///
    /// 注意：
    /// - 不按 City 统计；
    /// - 士兵人在国外、前线、船上或其他城市，只要 actor.kingdom == kingdom，
    ///   且仍然存活并且 isWarrior()，都算作这个国家的兵力。
    /// </summary>
    private static int GetKingdomLivingWarriorCount(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt())
            return 0;

        EnsureKingdomWarriorCountCache();

        return _kingdomLivingWarriorCounts.TryGetValue(kingdom, out int count)
            ? count
            : 0;
    }

    private static void EnsureKingdomWarriorCountCache()
    {
        int frame = Time.frameCount;

        if (_kingdomWarriorCountCacheFrame == frame)
            return;

        _kingdomWarriorCountCacheFrame = frame;
        _kingdomLivingWarriorCounts.Clear();

        if (World.world == null || World.world.units == null)
            return;

        foreach (Actor actor in World.world.units)
        {
            if (actor == null ||
                !actor.isAlive() ||
                actor.isRekt() ||
                !actor.isWarrior())
            {
                continue;
            }

            Kingdom kingdom = actor.kingdom;

            if (kingdom == null || kingdom.isRekt())
                continue;

            _kingdomLivingWarriorCounts.TryGetValue(kingdom, out int count);
            _kingdomLivingWarriorCounts[kingdom] = count + 1;
        }
    }

    /// <summary>
    /// 每帧最多扫描一次所有 Actor。
    ///
    /// 判断“到达该地”的标准：
    /// Warrior.current_tile.zone.city == city
    ///
    /// 即只要士兵脚下的 Zone 属于该城市，就算已经真正进入城市领土，
    /// 不要求必须抵达中心格。
    /// </summary>
    private static void EnsureImperialPresenceCache()
    {
        int frame = Time.frameCount;

        if (_imperialPresenceCacheFrame == frame)
            return;

        _imperialPresenceCacheFrame = frame;
        _imperialAttackerByCity.Clear();

        if (World.world == null || World.world.units == null)
            return;

        foreach (Actor actor in World.world.units)
        {
            if (actor == null || !actor.isAlive() || actor.isRekt())
                continue;

            if (!actor.isWarrior())
                continue;

            Kingdom troopKingdom = actor.kingdom;

            if (troopKingdom == null || troopKingdom.isRekt())
                continue;

            // 必须是真正的帝国体系军队：
            // 帝国核心国或帝国成员国均可。
            Empire attackerEmpire = troopKingdom.GetEmpire();
            if (attackerEmpire == null && !troopKingdom.IsEmpire())
                continue;

            WorldTile tile = actor.current_tile;
            TileZone zone = tile?.zone;
            City enteredCity = zone?.city;

            if (enteredCity == null || enteredCity.isRekt())
                continue;

            Kingdom defenderKingdom = enteredCity.kingdom;

            if (defenderKingdom == null ||
                defenderKingdom == troopKingdom ||
                defenderKingdom.isRekt())
            {
                continue;
            }

            // 同一帝国内部不触发投降。
            if (defenderKingdom.IsInSameEmpire(troopKingdom))
                continue;

            // 关键修正：
            // 不再要求 troopKingdom 自己必须直接和 defenderKingdom 开战。
            //
            // 帝国战争中常见情况：
            //   CoreKingdom A <-> CoreKingdom B 正在战争
            //   但实际进入城市的是 A 帝国下属 Kingdom C 的士兵。
            //
            // ResolveImperialCaptureKingdom 会把这种情况解析到真正的参战方。
            Kingdom captureKingdom =
                ResolveImperialCaptureKingdom(
                    troopKingdom,
                    defenderKingdom);

            if (captureKingdom == null)
                continue;

            // 如果多个帝国同时进入同一城市，
            // 当前帧保留第一个有效敌对帝国。
            if (!_imperialAttackerByCity.ContainsKey(enteredCity))
            {
                _imperialAttackerByCity[enteredCity] = captureKingdom;
            }
        }
    }

    private static bool IsValidImperialSurrenderAttacker(
        City city,
        Kingdom attackerKingdom)
    {
        if (city == null ||
            attackerKingdom == null ||
            attackerKingdom.isRekt())
        {
            return false;
        }

        Kingdom defenderKingdom = city.kingdom;

        if (defenderKingdom == null ||
            defenderKingdom.isRekt() ||
            defenderKingdom == attackerKingdom)
        {
            return false;
        }

        if (!attackerKingdom.IsInEmpire() &&
            !attackerKingdom.IsEmpire())
        {
            return false;
        }

        if (defenderKingdom.IsInSameEmpire(attackerKingdom))
            return false;

        // attackerKingdom 已经由 ResolveImperialCaptureKingdom
        // 解析成真正可代表此次战争的一方。
        return AreWarSidesHostile(
            attackerKingdom,
            defenderKingdom);
    }

    /// <summary>
    /// 从“实际进入城市的士兵所属 Kingdom”
    /// 解析出应该用于 finishCapture 的真正参战 Kingdom。
    ///
    /// 支持：
    /// 1. 成员国直接参战；
    /// 2. 帝国核心国参战、成员国士兵作战；
    /// 3. 双方都是帝国，战争登记在两个 CoreKingdom 上。
    /// </summary>
    private static Kingdom ResolveImperialCaptureKingdom(
        Kingdom troopKingdom,
        Kingdom defenderKingdom)
    {
        if (troopKingdom == null ||
            defenderKingdom == null)
        {
            return null;
        }

        Empire attackerEmpire =
            troopKingdom.GetEmpire();

        Kingdom attackerCore =
            attackerEmpire?.CoreKingdom;

        // 某些实现中核心国的 GetEmpire() 可能为空，
        // 但 IsEmpire() 为 true，此时本身就是战争核心。
        if (attackerCore == null &&
            troopKingdom.IsEmpire())
        {
            attackerCore = troopKingdom;
        }

        Empire defenderEmpire =
            defenderKingdom.GetEmpire();

        Kingdom defenderCore =
            defenderEmpire?.CoreKingdom;

        if (defenderCore == null &&
            defenderKingdom.IsEmpire())
        {
            defenderCore = defenderKingdom;
        }

        // 1. 实际部队所属国家自己就是直接参战方。
        if (AreDirectlyAtWar(
                troopKingdom,
                defenderKingdom))
        {
            return troopKingdom;
        }

        // 2. 攻方核心国 vs 当前守城国。
        if (attackerCore != null &&
            AreDirectlyAtWar(
                attackerCore,
                defenderKingdom))
        {
            return attackerCore;
        }

        // 3. 实际部队所属国 vs 守方帝国核心。
        if (defenderCore != null &&
            AreDirectlyAtWar(
                troopKingdom,
                defenderCore))
        {
            return troopKingdom;
        }

        // 4. 最常见的帝国战争：
        //    攻方 CoreKingdom vs 守方 CoreKingdom。
        if (attackerCore != null &&
            defenderCore != null &&
            AreDirectlyAtWar(
                attackerCore,
                defenderCore))
        {
            return attackerCore;
        }

        return null;
    }

    private static bool AreWarSidesHostile(
        Kingdom attackerKingdom,
        Kingdom defenderKingdom)
    {
        if (attackerKingdom == null ||
            defenderKingdom == null)
        {
            return false;
        }

        if (AreDirectlyAtWar(
                attackerKingdom,
                defenderKingdom))
        {
            return true;
        }

        Empire attackerEmpire =
            attackerKingdom.GetEmpire();

        Kingdom attackerCore =
            attackerEmpire?.CoreKingdom;

        if (attackerCore == null &&
            attackerKingdom.IsEmpire())
        {
            attackerCore = attackerKingdom;
        }

        Empire defenderEmpire =
            defenderKingdom.GetEmpire();

        Kingdom defenderCore =
            defenderEmpire?.CoreKingdom;

        if (defenderCore == null &&
            defenderKingdom.IsEmpire())
        {
            defenderCore = defenderKingdom;
        }

        if (attackerCore != null &&
            AreDirectlyAtWar(
                attackerCore,
                defenderKingdom))
        {
            return true;
        }

        if (defenderCore != null &&
            AreDirectlyAtWar(
                attackerKingdom,
                defenderCore))
        {
            return true;
        }

        if (attackerCore != null &&
            defenderCore != null &&
            AreDirectlyAtWar(
                attackerCore,
                defenderCore))
        {
            return true;
        }

        return false;
    }

    private static bool AreDirectlyAtWar(
        Kingdom a,
        Kingdom b)
    {
        if (a == null ||
            b == null ||
            a == b ||
            a.isRekt() ||
            b.isRekt())
        {
            return false;
        }

        try
        {
            return a.isInWarWith(b) ||
                   b.isInWarWith(a);
        }
        catch
        {
            return false;
        }
    }
    public static bool removeObject(CityManager __instance, City pObject)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pObject)) return true;
        return true;
    }

    public static void setKingdom(City __instance, Kingdom pKingdom)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance.hasTitle())
        {
            __instance.GetTitle().isBeenControlled();
        }
    }
    
    //创建新城市时触发
    public static void newCity(City __instance, Actor pActor)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        
    }

    public static bool zone_steal(CityBehBorderSteal __instance, City pCity)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pCity)) return true;
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy.isEnabled())
        {
            return false;
        }
        return true;
    }

    public static void destroy_city(City __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        __instance.RemoveExtraData<City, CityExtraData>();
    }
}
