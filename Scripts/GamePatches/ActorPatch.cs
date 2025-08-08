using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EpPathFinding.cs;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;

namespace EmpireCraft.Scripts.GamePatches;
public class ActorPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static int startSessionMonth { get; set; }
    public static bool isReadyToSet = false;
    public void Initialize()
    {
        ActorPatch.startSessionMonth = Date.getMonthsSince(World.world.getCurSessionTime());
        ActorPatch.isReadyToSet = true;
        // Actor类的补丁

        new Harmony(nameof(set_actor_family_name)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setFamily)),
            postfix: new HarmonyMethod(GetType(), nameof(set_actor_family_name)));
        new Harmony(nameof(set_actor_clan_name)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setClan)),
            postfix: new HarmonyMethod(GetType(), nameof(set_actor_clan_name)));
        new Harmony(nameof(set_actor_culture)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setCulture)),
            prefix: new HarmonyMethod(GetType(), nameof(set_actor_culture)));
        new Harmony(nameof(RemoveData)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(RemoveData)));
        new Harmony(nameof(SetArmy)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setArmy)),
            postfix: new HarmonyMethod(GetType(), nameof(SetArmy)));
        new Harmony(nameof(RemoveFromArmy)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.removeFromArmy)),
            prefix: new HarmonyMethod(GetType(), nameof(RemoveFromArmy)));
        new Harmony(nameof(SetKingdom)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(SetKingdom)));
        new Harmony(nameof(SetLover)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setLover)),
            postfix: new HarmonyMethod(GetType(), nameof(SetLover)));
        new Harmony(nameof(SetParent)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setParent1)),
            postfix: new HarmonyMethod(GetType(), nameof(SetParent)));
        new Harmony(nameof(SetParent)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setParent2)),
            postfix: new HarmonyMethod(GetType(), nameof(SetParent)));
        new Harmony(nameof(setCity)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setCity)),
            postfix: new HarmonyMethod(GetType(), nameof(setCity)));
        LogService.LogInfo("角色补丁加载成功");
    }

    public static void setCity(Actor __instance, City pCity)
    {
        // todo: 当角色加入城市时触发
    }
    public static void SetParent(Actor __instance, Actor pActor, bool pIncreaseChildren)
    {
        // todo: 当角色拥有父母时触发
    }

    public static void SetLover(Actor __instance, Actor pActor)
    {
        if (pActor==null) return;
        // todo: 当角色拥有伴侣时触发
    }
    public static void SetKingdom(Actor __instance, Kingdom pKingdomToSet)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        // todo: 当角色加入王国时触发
    }
    public static void SetArmy(Actor __instance, Army pObject)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        // todo: 当角色加入军队时触发
    }

    public static void RemoveFromArmy(Actor __instance)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        // todo: 当角色离开军队时触发
    }
    public static void RemoveData(Actor __instance)
    {
        __instance.RemoveExtraData<Actor, ActorExtraData>();
        // todo: 当角色数据被清除时触发
    }
    

    public static void set_actor_culture(Actor __instance, Culture pCulture)
    {
        if (!isReadyToSet) return;
        if (pCulture == null || __instance == null)
        {
            return;
        }
        if (__instance.GetModName().has_whole_name(__instance))
        {
            return;
        }
        __instance.initializeActorName();
        if (!__instance.GetModName().hasFirstName(__instance))
        {
            string firstName = pCulture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female);
            __instance.SetFirstName(firstName);
        }
        bool flag = false;
        if (!__instance.GetModName().hasFamilyName(__instance))
        {
            if (__instance.getParents().Any())
            {
                if (__instance.getParents().Any(p => p.GetModName().hasFamilyName(p)))
                {
                    if (__instance.hasClan() && __instance.clan.units.Any(p => p.isParentOf(__instance)))
                    {
                        __instance.SetFamilyName(__instance.clan.GetClanName());
                        flag = true;
                    }
                    else if (__instance.hasFamily() && __instance.family.units.Any(p => p.isParentOf(__instance)))
                    {
                        __instance.SetFamilyName(__instance.family.GetFamilyName());
                        flag = true;
                    }
                    else
                    {
                        __instance.SetFamilyName(__instance.getParents().ToList().FindAll(p=>p.GetModName().hasFamilyName(p)).GetRandom().GetModName().familyName);

                        flag = true;
                    }
                }
            }

            switch (flag)
            {
                case false when __instance.hasClan():
                {
                    if (!__instance.clan.units.Any(p=>p.hasCulture()))
                    {
                        __instance.clan.data.name = pCulture.getOnomasticData(MetaType.Clan).generateName();
                    }
                    if (__instance.hasFamily())
                    {
                        if (__instance.hasCity())
                        {
                            __instance.family.data.name = __instance.city.data.name + "\u200A" + __instance.clan.GetClanName() + "\u200A" + LM.Get("Family");
                            __instance.family.SetFamilyCityPre();
                        }
                        else
                        {
                            __instance.family.data.name = __instance.clan.GetClanName() + "\u200A" + LM.Get("Family");
                            __instance.family.SetFamilyCityPre(false);
                        }
                    }
                    __instance.SetFamilyName(__instance.clan.GetClanName());
                    break;
                }
                case false when __instance.hasFamily():
                {
                    if (!__instance.family.HasBeenSetBefored())
                    {
                        __instance.family.data.name = pCulture.getOnomasticData(MetaType.Family).generateName();
                        __instance.family.SetFamilyCityPre(false);
                        __instance.SetFamilyName(__instance.family.GetFamilyName());
                    }

                    break;
                }
            }
        }
        __instance.GetModName().SetName(__instance);
    }

    public static int Getmonth()
    {
        return Date.getMonthsSince(World.world.getCurSessionTime()) - startSessionMonth;
    }


    public static void set_actor_clan_name(Actor __instance, Clan pObject)
    {
        if (!isReadyToSet) return;
        if (pObject == null || __instance == null)
        {
            return;
        }
        if (__instance.GetModName().hasFamilyName(__instance))
        {
            return;
        }

        if (__instance.hasFamily())
        {

            string clanName = pObject.GetClanName();
            string familyEnd = LM.Get("Family");
            if (__instance.city != null)
            {
                string cityName = __instance.city.GetCityName();
                __instance.family.data.name = string.Join("\u200A", cityName, clanName, familyEnd);
                OverallHelperFunc.SetFamilyCityPre(__instance.family);
            } else
            {
                if (!__instance.family.HasBeenSetBefored())
                {
                    __instance.family.data.name = string.Join("\u200A", clanName, familyEnd);
                    __instance.family.SetFamilyCityPre(false);
                }
            }
        }
        //处理氏族名称尾缀英文的问题
        if (!pObject.data.name.EndsWith(LM.Get("Clan")) && pObject.data.name.Contains(LM.Get("Clan")))
        {
            pObject.data.name = pObject.data.name.Substring(0, pObject.data.name.Length - 2);
        }
        else if (!pObject.data.name.EndsWith(LM.Get("Clan")) && !pObject.data.name.Contains(LM.Get("Clan")))
        {
            if (__instance.hasCulture())
            {
                pObject.data.name = __instance.culture.getOnomasticData(MetaType.Clan).generateName();
                __instance.SetFamilyName(pObject.GetClanName());
            }
        } else
        {
            __instance.SetFamilyName(pObject.GetClanName());
        }
        __instance.initializeActorName();
        __instance.GetModName().SetName(__instance);
        if (__instance.hasClan() && __instance.HasSpecificClan()&&__instance.getChildren().Any())
        {
            PersonalClanIdentity pci = __instance.GetPersonalIdentity();
            if (pci.is_main)
            {
                foreach (var c in __instance.getChildren())
                {
                    c.setClan(pObject);
                }
            }
        }
    }

    public static void set_actor_family_name(Actor __instance, Family pObject)
    {
        if (!isReadyToSet) return;
        if (pObject == null || __instance == null)
        {
            return;
        }
        if (__instance.GetModName().hasFamilyName(__instance))
        {
            if (__instance.hasClan())
            {
                string clanName = __instance.clan.GetClanName();
                string familyEnd = LM.Get("Family");
                if (__instance.city != null)
                {
                    string cityName = __instance.city.GetCityName();
                    pObject.data.name = string.Join("\u200A", cityName, clanName, familyEnd);
                    OverallHelperFunc.SetFamilyCityPre(pObject);
                    __instance.SetFamilyName(pObject.GetFamilyName());
                }
                else
                {
                    if (!pObject.HasBeenSetBefored())
                    {
                        pObject.data.name = string.Join("\u200A", clanName, familyEnd);
                        OverallHelperFunc.SetFamilyCityPre(pObject, false);
                        __instance.SetFamilyName(pObject.GetFamilyName());
                    }
                }
                __instance.initializeActorName();
                __instance.GetModName().SetName(__instance);
            }
            return;
        }
        if (__instance.hasClan())
        {
            if (__instance.hasCulture())
            {
                string clanName = __instance.clan.GetClanName();
                string familyEnd = LM.Get("Family");
                if (__instance.city != null)
                {
                    string cityName = __instance.city.GetCityName();
                    pObject.data.name = string.Join("\u200A", cityName, clanName, familyEnd);
                    OverallHelperFunc.SetFamilyCityPre(pObject);
                }
                else
                {
                    if (!pObject.HasBeenSetBefored())
                    {
                        pObject.data.name = string.Join("\u200A", clanName, familyEnd);
                        OverallHelperFunc.SetFamilyCityPre(pObject, false);
                    }
                }
                __instance.SetFamilyName(pObject.GetFamilyName());
                __instance.GetModName().SetName(__instance);
                return;
            }
        }
        if (__instance.hasCulture())
        {
            if (pObject.units.Count>1)
            {
                if (!pObject.units.Any(a=>a!=__instance&&a.GetModName().hasFamilyName(a)))
                {
                    if (!pObject.HasBeenSetBefored())
                    {
                        pObject.data.name = __instance.culture.getOnomasticData(MetaType.Family).generateName();
                        OverallHelperFunc.SetFamilyCityPre(pObject, false);
                    }
                }
            } else
            {
                if (!pObject.HasBeenSetBefored())
                {
                    pObject.data.name = __instance.culture.getOnomasticData(MetaType.Family).generateName();
                    pObject.SetFamilyCityPre(false);
                }
            }
            __instance.SetFamilyName(pObject.GetFamilyName());
        }
        __instance.initializeActorName();
        __instance.GetModName().SetName(__instance);
    }
}