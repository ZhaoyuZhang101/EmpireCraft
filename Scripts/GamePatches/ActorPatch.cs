using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
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
public class ActorPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        // Actor类的补丁
        
        new Harmony(nameof(set_actor_family_name)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setFamily)),
            postfix: new HarmonyMethod(GetType(), nameof(set_actor_family_name)));
        new Harmony(nameof(set_actor_clan_name)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setClan)),
            postfix: new HarmonyMethod(GetType(), nameof(set_actor_clan_name)));
        new Harmony(nameof(set_actor_culture)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setCulture)),
            prefix: new HarmonyMethod(GetType(), nameof(set_actor_culture)));
        new Harmony(nameof(set_actor_peerages)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.setDefaultValues)),
            postfix: new HarmonyMethod(GetType(), nameof(set_actor_peerages)));
        new Harmony(nameof(add_child)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.addChild)),
            postfix: new HarmonyMethod(GetType(), nameof(add_child)));
        new Harmony(nameof(removeData)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.Dispose)),
            postfix: new HarmonyMethod(GetType(), nameof(removeData)));
        LogService.LogInfo("角色姓名命名补丁加载成功");
    }
    public static void add_child(Actor __instance, BaseActorComponent pObject)
    {

    }    
    public static void removeData(Actor __instance)
    {
        __instance.RemoveExtraData();
    }

    public static void set_actor_peerages(Actor __instance)
    {
        if (__instance.data==null)
        {
            return;
        }
        __instance.SetPeeragesLevel(PeeragesLevel.peerages_6);
    }

    public static void set_actor_culture(Actor __instance, Culture pCulture)
    {
        if (pCulture == null || __instance == null)
        {
            return;
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
                if (parents != null && parents.Any(t=>t.hasCulture()))
                {
                    Actor parent = parents.FirstOrDefault(t => t.hasCulture());
                    if (parent.hasClan())
                    {
                        __instance.data.name = String.Join(" ", parent.clan.name.Split(' ')[0], firstName);
                    } else
                    {
                        __instance.data.name = String.Join(" ", __instance.clan.name.Split(' ')[0], firstName);
                    }
                } else
                {
                    __instance.clan.data.name = pCulture.getOnomasticData(MetaType.Clan).generateName();
                    string familyName = __instance.clan.name.Split(' ')[0];
                    __instance.data.name = String.Join(" ", familyName, firstName);
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
                            __instance.data.name = String.Join(" ", HelperFunc.getFamilyName(parent.family.name), firstName);
                        }
                        else
                        {
                            __instance.data.name = String.Join(" ", HelperFunc.getFamilyName(__instance.family.name), firstName);
                        }
                    }
                    else
                    {
                        foreach (Actor parent in __instance.getParents())
                        {
                            if (parent.data.name != null) 
                            {
                                parent.data.name = String.Join(" ", HelperFunc.getFamilyName(__instance.family.name),
                                    parent.data.name.Split(' ')[parent.data.name.Split(' ').Length-1]);
                            }
                        }
                        string familyName = HelperFunc.getFamilyName(__instance.family.name);
                        __instance.data.name = String.Join(" ", familyName, firstName);
                    }
                } else
                {
                    __instance.data.name = firstName;
                }
            }
        }
    }

    public static void set_actor_clan_name(Actor __instance, Clan pObject)
    {
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
            string cityName = __instance.city.name.Split(' ')[0];
            string clanName = pObject.name.Split(' ')[0];
            string familyEnd = LM.Get("Family");
            if (__instance.family == null)
            {
                return;
            }
            __instance.family.data.name = string.Join(" ", cityName, clanName, familyEnd);
        }
        if (pObject == null)
        {
            return;
        }
        if (!pObject.data.name.EndsWith(LM.Get("Clan")) && pObject.data.name.Contains(LM.Get("Clan")))
        {
            pObject.data.name = pObject.data.name.Substring(0, pObject.data.name.Length - 2);
        } else if (!pObject.data.name.EndsWith(LM.Get("Clan")) && !pObject.data.name.Contains(LM.Get("Clan")))
        {
            if (__instance.hasCulture())
            {
                pObject.data.name = __instance.culture.getOnomasticData(MetaType.Clan).generateName();
            } else
            {
                pObject.data.name = string.Join(" ", pObject.data.name, LM.Get("Clan"));
            }
        }
        if (__instance.data.name == null || __instance.data.name == "")
        {
            if (__instance.hasCulture())
            {
                __instance.data.name = pObject.name.Split(' ')[0] + " " +
                    __instance.culture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale() ? ActorSex.Male : ActorSex.Female);
            }
            return;
        }

        if (__instance.data.name.Split(' ').Length > 1)
        {
            string firstName = __instance.data.name.Split(' ')[1];
            string familyName = __instance.data.name.Split(' ')[0];
            __instance.data.name = string.Join(" ", pObject.data.name.Split(' ')[0], firstName);
        }
        else
        {
            __instance.data.name = string.Join(" ", pObject.data.name.Split(' ')[0], __instance.data.name);

        }
    }

    public static void set_actor_family_name(Actor __instance,Family pObject)
    {
        if (__instance.hasClan())
        {
            if (__instance.city == null)
            {
                return;
            }
            string cityName = __instance.city.name.Split(' ')[0];
            string clanName = __instance.clan.name.Split(' ')[0];
            string familyName = LM.Get("Family");
            if (pObject == null)
            {
                return;
            }
            pObject.data.name = string.Join(" ", cityName, clanName, familyName);
        } else {
            if (pObject == null)
            {
                return;
            }

            if (__instance.data.name == null || __instance.data.name == "")
            {
                if (__instance.hasCulture())
                {
                    // Fix: Replace the invalid negative index with a valid index.  
                    __instance.data.name = HelperFunc.getFamilyName(pObject.name) + " " + 
                        __instance.culture.getOnomasticData(MetaType.Unit).generateName(__instance.isSexMale()?ActorSex.Male:ActorSex.Female);
                }
                return;
            }

            if (__instance.data.name.Split(' ').Length > 1)
            {
                return;
            }
            __instance.data.name = string.Join(" ", HelperFunc.getFamilyName(pObject.name), __instance.data.name);
        }
    }
}
