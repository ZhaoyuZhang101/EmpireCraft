using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;

namespace EmpireCraft.Scripts.GamePatches;
public class FamilyPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(set_family_name)).Patch(
            AccessTools.Method(typeof(Family), nameof(Family.newFamily)),
            postfix: new HarmonyMethod(GetType(), nameof(set_family_name))
        );
        LogService.LogInfo("家族命名补丁加载成功");
    }

    public static void set_family_name(Family __instance, Actor pActor1, Actor pActor2, WorldTile pTile)
    {
        Culture culture = null;
        if (pActor1 != null)
        {
            if (pActor1.GetModName().hasFamilyName(pActor1))
            {
                string cityName = pActor1.city.GetCityName();
                string familyName = pActor1.GetModName().familyName;
                string familyEnd = LM.Get("Family");
                __instance.data.name = string.Join("\u200A", cityName, familyName, familyEnd);
                OverallHelperFunc.SetFamilyCityPre(__instance);
            }
            else
            {
                if ( pActor1.hasCulture())
                {
                    culture = pActor1.culture;
                    __instance.data.name = culture.getOnomasticData(MetaType.Family).generateName();
                    OverallHelperFunc.SetFamilyCityPre(__instance, false);
                    if (!pActor1.GetModName().hasFamilyName(pActor1))
                    {
                        pActor1.SetFamilyName(__instance.getFamilyName());
                    }
                    if (pActor1.GetModName().has_whole_name(pActor1))
                    {
                        pActor1.GetModName().SetName(pActor1);
                    }
                    if (pActor2 != null) 
                    {
                        if (!pActor2.hasCulture())
                        {
                            pActor2.setCulture(culture);
                        }
                        if (!pActor2.GetModName().hasFamilyName(pActor2))
                        {
                            pActor2.SetFamilyName(__instance.getFamilyName());
                        }
                        if (pActor2.GetModName().has_whole_name(pActor2))
                        {
                            pActor2.GetModName().SetName(pActor2);
                        }
                    }
                }
            }
        }

        if (pActor2 != null)
        {
            if (pActor2.GetModName().hasFamilyName(pActor2))
            {
                string cityName = pActor2.city.GetCityName();
                string familyName = pActor2.GetModName().familyName;
                string familyEnd = LM.Get("Family");
                __instance.data.name = string.Join("\u200A", cityName, familyName, familyEnd);
                OverallHelperFunc.SetFamilyCityPre(__instance);
            }
            else
            {
                if (pActor2.hasCulture())
                {
                    culture = pActor2.culture;
                    __instance.data.name = culture.getOnomasticData(MetaType.Family).generateName();
                    OverallHelperFunc.SetFamilyCityPre(__instance, false);
                    if (!pActor2.GetModName().hasFamilyName(pActor2))
                    {
                        
                        pActor2.SetFamilyName(__instance.getFamilyName());
                    }
                    if (pActor2.GetModName().has_whole_name(pActor2))
                    {
                        pActor2.GetModName().SetName(pActor2);
                    }
                    if (pActor1 != null)
                    {
                        if (!pActor1.hasCulture())
                        {
                            pActor1.setCulture(culture);
                        }
                        if (!pActor1.GetModName().hasFamilyName(pActor1))
                        {
                            pActor1.SetFamilyName(__instance.getFamilyName());
                        }
                        if (pActor1.GetModName().has_whole_name(pActor1))
                        {
                            pActor1.GetModName().SetName(pActor1);
                        }
                    }
                }
            }
        }
    }
}
