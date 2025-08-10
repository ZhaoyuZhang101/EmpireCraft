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
        new Harmony(nameof(set_actor_peerages)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setDefaultValues)),
            postfix: new HarmonyMethod(GetType(), nameof(set_actor_peerages)));
        new Harmony(nameof(removeData)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(removeData)));
        new Harmony(nameof(setArmy)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setArmy)),
            postfix: new HarmonyMethod(GetType(), nameof(setArmy)));
        new Harmony(nameof(removeFromArmy)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.removeFromArmy)),
            prefix: new HarmonyMethod(GetType(), nameof(removeFromArmy)));
        new Harmony(nameof(setKingdom)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(setKingdom)));
        new Harmony(nameof(showTooltip)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.showTooltip)),
            prefix: new HarmonyMethod(GetType(), nameof(showTooltip)));
        new Harmony(nameof(setLover)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setLover)),
            postfix: new HarmonyMethod(GetType(), nameof(setLover)));
        new Harmony(nameof(setParent)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setParent1)),
            postfix: new HarmonyMethod(GetType(), nameof(setParent)));
        new Harmony(nameof(setParent)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setParent2)),
            postfix: new HarmonyMethod(GetType(), nameof(setParent)));
        new Harmony(nameof(setCity)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setCity)),
            postfix: new HarmonyMethod(GetType(), nameof(setCity)));
        LogService.LogInfo("角色补丁加载成功");
    }

    public static void setCity(Actor __instance, City pCity)
    {
        if (pCity.HasReachedPlayerPopLimit())
        {
            __instance.setHealth(0);
        }
    }

    public static void setParent(Actor __instance, Actor pActor, bool pIncreaseChildren)
    {
        if (pActor.HasSpecificClan())
        {
            PersonalClanIdentity parent_identity = pActor.GetPersonalIdentity();
            if (parent_identity.is_main)
            {
                if (pActor.hasClan())
                {
                    __instance.setClan(pActor.clan);
                }

                __instance.GetModName().familyName = pActor.GetModName().familyName;
                __instance.GetModName().SetName(__instance);
                parent_identity.AddChild(__instance, true);
            }
        }
    }

    public static void setLover(Actor __instance, Actor pActor)
    {
        if (pActor==null) return;
        if(__instance.HasSpecificClan())
        {
            PersonalClanIdentity identity = __instance.GetPersonalIdentity();
            identity.SetLover(pActor);
        }
    }
    public static bool showTooltip(Actor __instance, object pUiObject)
    {
        string pType = (__instance.IsEmperor()?"actor_emperor":(__instance.isKing() ? "actor_king" : ((!__instance.isCityLeader()) ? "actor" : (__instance.isOfficer()? "actor_officer": "actor_leader"))));
        Tooltip.show(pUiObject, pType, new TooltipData
        {
            actor = __instance
        });
        return false;
    }
    public static void setKingdom(Actor __instance, Kingdom pKingdomToSet)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        if (!pKingdomToSet.isInEmpire())
        {
            if (__instance.hasArmy())
            {
                if (__instance.city.kingdom.isInEmpire())
                {
                    if (__instance.kingdom.GetCountryLevel() == countryLevel.countrylevel_2)
                    {
                        if (__instance.hasTrait("empireArmedProvinceSoldier"))
                        {
                            __instance.removeTrait("empireArmedProvinceSoldier");
                        }
                    }
                    if (__instance.kingdom.GetCountryLevel() == countryLevel.countrylevel_0)
                    {
                        if (__instance.hasTrait("empireSoldier"))
                        {
                            __instance.removeTrait("empireSoldier");
                        }

                    }
                }
            }
        }
    }
    public static void setArmy(Actor __instance, Army pObject)
    {
        if (__instance.city == null) return;
        if (__instance.city.kingdom == null) return;
        if(__instance.city.kingdom.isInEmpire())
        {
            if (__instance.kingdom.GetCountryLevel() == countryLevel.countrylevel_2)
            {
                if(!__instance.hasTrait("empireArmedProvinceSoldier")) 
                {
                    __instance.addTrait("empireArmedProvinceSoldier");
                }
            }
            if (__instance.kingdom.GetCountryLevel() == countryLevel.countrylevel_0)
            {
                if (!__instance.hasTrait("empireSoldier"))
                {
                    __instance.addTrait("empireSoldier");
                }
            }
        }
    }

    public static void removeFromArmy(Actor __instance)
    {
        if(__instance.hasArmy())
        {
            if (__instance.hasTrait("empireArmedProvinceSoldier"))
            {
                __instance.removeTrait("empireArmedProvinceSoldier");
            }
            if (__instance.hasTrait("empireSoldier")) 
            {
                __instance.removeTrait("empireSoldier");
            }
        }

    }
    public static void removeData(Actor __instance)
    {
        if (__instance.isOfficer())
        {
            long id = __instance.GetProvinceID();
            Province province = ModClass.PROVINCE_MANAGER.get(id);
            if (province != null) 
            {
                province.RemoveOfficer();
            }
        }
        if (__instance.HasSpecificClan())
        {
            PersonalClanIdentity pci = __instance.GetPersonalIdentity();
            pci.is_alive = false;
            pci.actor_id = -1L;
            pci.deathday = Date.getDate(World.world.getCurWorldTime());
            pci.RecordAllInfo();
            pci._specificClan.checkDispose();
        }
        __instance.RemoveExtraData<Actor, ActorExtraData>();
    }

    public static void set_actor_peerages(Actor __instance)
    {
        if (__instance.data == null)
        {
            return;
        }
        if (__instance.data.custom_data_string == null)
        {
            __instance.data.custom_data_string = new CustomDataContainer<string> ();
        }
        __instance.data.custom_data_string[CustomDataType.empirecraft_history_record.ToString()] = "";
        __instance.SetPeeragesLevel(PeeragesLevel.peerages_6);
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
                __instance.family.SetFamilyCityPre();
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
        else if (!pObject.data.name.Contains(LM.Get("Clan")))
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
                    pObject.SetFamilyCityPre();
                    __instance.SetFamilyName(pObject.GetFamilyName());
                }
                else
                {
                    if (!pObject.HasBeenSetBefored())
                    {
                        pObject.data.name = string.Join("\u200A", clanName, familyEnd);
                        pObject.SetFamilyCityPre(false);
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
        
        if (__instance.HasSpecificClan())
        {
            PersonalClanIdentity pci = __instance.GetPersonalIdentity();
            if (pci.is_main)
            {
                string clanName = pci._specificClan.name;
                string familyEnd = LM.Get("Family");
                if (__instance.city != null)
                {
                    string cityName = __instance.city.GetCityName();
                    pObject.data.name = string.Join("\u200A", cityName, clanName, familyEnd);
                    pObject.SetFamilyCityPre();
                }
                else
                {
                    if (!pObject.HasBeenSetBefored())
                    {
                        pObject.data.name = string.Join("\u200A", clanName, familyEnd);
                        pObject.SetFamilyCityPre(false);
                    }
                }
            }
        }
    }
}