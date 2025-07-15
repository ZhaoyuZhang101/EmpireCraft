using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EpPathFinding.cs;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        LogService.LogInfo("角色补丁加载成功");
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
        __instance.RemoveExtraData();
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
        if (!isReadyToSet)
        {
            return;
        }
        if (pCulture == null || __instance == null)
        {
            return;
        }
        bool is_invert = false;
        bool has_family_sex_post = false;
        bool has_clan_sex_post = false;
        if (ConfigData.speciesCulturePair.TryGetValue(__instance.asset.id, out var pair))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(pair, out Setting setting))
            {
                is_invert = setting.Unit.is_invert;
                has_family_sex_post = setting.Family.has_sex_post;
                has_clan_sex_post = setting.Clan.has_sex_post;
            }
        }
        if (__instance.hasCulture())
        {
            return;
        }
        else
        {
            string firstName = pCulture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female);
            if (__instance.hasClan())
            {
                Actor[] parents = __instance.getParents().ToArray();
                if (parents != null && parents.Any(t => t.hasCulture()))
                {
                    Actor parent = parents.FirstOrDefault(t => t.hasCulture());
                    if (parent.hasClan())
                    {
                        if (is_invert)
                        {
                            __instance.data.name = String.Join(" ", firstName, parent.clan.GetClanName(__instance.data.sex, has_clan_sex_post));
                        }
                        else
                        {
                            __instance.data.name = String.Join(" ", parent.clan.GetClanName(__instance.data.sex, has_clan_sex_post), firstName);
                        }

                    }
                    else
                    {
                        if (is_invert)
                        {
                            __instance.data.name = String.Join(" ", firstName, __instance.clan.GetClanName(__instance.data.sex, has_clan_sex_post));
                        }
                        else
                        {
                            __instance.data.name = String.Join(" ", __instance.clan.GetClanName(__instance.data.sex, has_clan_sex_post), firstName);
                        }
                    }
                }
                else
                {
                    __instance.clan.data.name = pCulture.getOnomasticData(MetaType.Clan).generateName();
                    string familyName = __instance.clan.GetClanName(__instance.data.sex, has_clan_sex_post);
                    if (is_invert)
                    {
                        __instance.data.name = String.Join(" ", firstName, familyName);
                    }
                    else
                    {
                        __instance.data.name = String.Join(" ", familyName, firstName);
                    }

                }

            }
            else
            {
                if (__instance.hasFamily())
                {
                    Actor[] parents = __instance.getParents().ToArray();
                    if (parents != null && parents.Any(t => t.hasCulture()))
                    {
                        Actor parent = parents.FirstOrDefault(t => t.hasCulture());
                        if (parent.hasFamily())
                        {
                            if (is_invert)
                            {
                                __instance.data.name = String.Join(" ", firstName, parent.family.getFamilyName(__instance.data.sex, has_family_sex_post));
                            }
                            else
                            {
                                __instance.data.name = String.Join(" ", parent.family.getFamilyName(__instance.data.sex, has_family_sex_post), firstName);
                            }

                        }
                        else
                        {
                            if (is_invert)
                            {
                                __instance.data.name = String.Join(" ", firstName, __instance.family.getFamilyName(__instance.data.sex, has_family_sex_post));
                            }
                            else
                            {
                                __instance.data.name = String.Join(" ", __instance.family.getFamilyName(__instance.data.sex, has_family_sex_post), firstName);
                            }

                        }
                    }
                    else
                    {
                        foreach (Actor parent in __instance.getParents())
                        {
                            if (parent.data.name != null)
                            {
                                if (is_invert)
                                {
                                    parent.data.name = String.Join(" ",
                                        parent.GetActorName(),
                                        __instance.family.getFamilyName(__instance.data.sex, has_family_sex_post));
                                }
                                else
                                {
                                    parent.data.name = String.Join(" ", __instance.family.getFamilyName(__instance.data.sex, has_family_sex_post),
                                        parent.GetActorName());
                                }

                            }
                        }
                        string familyName = __instance.family.getFamilyName(__instance.data.sex, has_family_sex_post);
                        if (is_invert)
                        {
                            __instance.data.name = String.Join(" ", firstName, familyName);
                        }
                        else
                        {
                            __instance.data.name = String.Join(" ", familyName, firstName);
                        }

                    }
                }
                else
                {
                    __instance.data.name = firstName;
                }
            }
        }
    }

    public static int Getmonth()
    {
        return Date.getMonthsSince(World.world.getCurSessionTime()) - startSessionMonth;
    }

    public static void set_actor_clan_name(Actor __instance, Clan pObject)
    {
        if (!isReadyToSet)
        {
            return;
        }
        if (pObject == null || __instance == null)
        {
            return;
        }
        if (__instance.hasFamily())
        {
            if (__instance.city == null)
            {
                return;
            }
            string cityName = __instance.city.GetCityName();
            string clanName = pObject.name.Split(' ')[0];
            string familyEnd = LM.Get("Family");
            if (__instance.family == null)
            {
                return;
            }
            __instance.family.data.name = string.Join(" ", cityName, clanName, familyEnd);
            if (__instance.family.data.custom_data_bool == null)
            {
                __instance.family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            if (__instance.family.data.custom_data_bool.TryGetValue("has_city_pre", out bool has_city_pre))
            {
                has_city_pre = true;
            } else
            {
                __instance.family.data.custom_data_bool["has_city_pre"] = true;
            }
        }
        if (pObject == null)
        {
            return;
        }
        if (!pObject.data.name.EndsWith(LM.Get("Clan")) && pObject.data.name.Contains(LM.Get("Clan")))
        {
            pObject.data.name = pObject.data.name.Substring(0, pObject.data.name.Length - 2);
        }
        else if (!pObject.data.name.EndsWith(LM.Get("Clan")) && !pObject.data.name.Contains(LM.Get("Clan")))
        {
            if (__instance.hasCulture())
            {
                pObject.data.name = __instance.culture.getOnomasticData(MetaType.Clan).generateName();
            }
            else
            {
                pObject.data.name = string.Join(" ", pObject.data.name, LM.Get("Clan"));
            }
        }
        bool is_invert = false;
        bool has_family_sex_post = false;
        bool has_clan_sex_post = false;
        if (ConfigData.speciesCulturePair.TryGetValue(__instance.asset.id, out var pair))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(pair, out Setting setting))
            {
                is_invert = setting.Unit.is_invert;
                has_family_sex_post = setting.Family.has_sex_post;
                has_clan_sex_post = setting.Clan.has_sex_post;
            }
        }
        if (__instance.data.name == null || __instance.data.name == "")
        {
            if (__instance.hasCulture())
            {
                if (is_invert)
                {
                    __instance.data.name = __instance.culture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female) + " " + pObject.GetClanName(__instance.data.sex, has_clan_sex_post);
                }
                else
                {
                    __instance.data.name = pObject.GetClanName(__instance.data.sex, has_clan_sex_post) + " " +
                        __instance.culture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female);
                }

            }
            return;
        }

        if (__instance.data.name.Split(' ').Length > 1)
        {
            string firstName = __instance.data.name.Split(' ')[1];
            string familyName = __instance.data.name.Split(' ')[0];
            if (is_invert)
            {
                __instance.data.name = string.Join(" ", firstName, pObject.GetClanName(__instance.data.sex, has_clan_sex_post));
            }
            else
            {
                __instance.data.name = string.Join(" ", pObject.GetClanName(__instance.data.sex, has_clan_sex_post), firstName);
            }

        }
        else
        {
            if (is_invert)
            {
                __instance.data.name = string.Join(" ", __instance.data.name, pObject.GetClanName(__instance.data.sex, has_clan_sex_post));
            }
            else
            {
                __instance.data.name = string.Join(" ", pObject.GetClanName(__instance.data.sex, has_clan_sex_post), __instance.data.name);
            }


        }
    }

    public static void set_actor_family_name(Actor __instance, Family pObject)
    {
        if (!isReadyToSet)
        {
            return;
        }
        if (__instance.hasClan())
        {
            if (__instance.city == null)
            {
                return;
            }
            string cityName = __instance.city.GetCityName();
            string clanName = __instance.clan.GetClanName();
            string familyName = LM.Get("Family");
            if (pObject == null)
            {
                return;
            }
            pObject.data.name = string.Join(" ", cityName, clanName, familyName);
            if (__instance.family.data.custom_data_bool == null)
            {
                __instance.family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            if (__instance.family.data.custom_data_bool.TryGetValue("has_city_pre", out bool has_city_pre))
            {
                has_city_pre = true;
            }
            else
            {
                __instance.family.data.custom_data_bool["has_city_pre"] = true;
            }

        }
        else
        {
            if (pObject == null)
            {
                return;
            }
            bool is_invert = false;
            bool has_family_sex_post = false;
            bool has_clan_sex_post = false;
            if (ConfigData.speciesCulturePair.TryGetValue(__instance.asset.id, out var pair))
            {
                if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(pair, out Setting setting))
                {
                    is_invert = setting.Unit.is_invert;
                    has_family_sex_post = setting.Family.has_sex_post;
                    has_clan_sex_post = setting.Clan.has_sex_post;
                }
            }
            if (__instance.data.name == null || __instance.data.name == "")
            {
                if (__instance.hasCulture())
                {
                    if (is_invert)
                    {
                        __instance.data.name = __instance.culture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female) + " " +
                         pObject.getFamilyName(__instance.data.sex, has_family_sex_post);
                    }
                    else
                    {
                        __instance.data.name = pObject.getFamilyName(__instance.data.sex, has_family_sex_post) + " " +
                               __instance.culture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female);
                    }
                }
                return;
            }

            if (__instance.data.name.Split(' ').Length > 1)
            {
                return;
            }
            if (is_invert)
            {
                __instance.data.name = string.Join(" ", __instance.data.name, pObject.getFamilyName(__instance.data.sex, has_family_sex_post));
            }
            else
            {
                __instance.data.name = string.Join(" ", pObject.getFamilyName(__instance.data.sex, has_family_sex_post), __instance.data.name);
            }

        }
    }
}