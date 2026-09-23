using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GeneralSystems;
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
        new Harmony(nameof(showStatsRows)).Patch(
            AccessTools.Method(typeof(FamilyWindow), nameof(FamilyWindow.showStatsRows)),
            postfix: new HarmonyMethod(GetType(), nameof(showStatsRows))
        );
    }

    public static void showStatsRows(FamilyWindow __instance)
    {
        Family family = __instance?.meta_object;
        if (family == null) return;
        SocialClass socialClass = LandEconomySystem.GetFamilyEconomicClass(family);
        __instance.showStatRow("family_economic_class", LM.Get($"class_{socialClass}"), "#F3C34A",
            pIconPath: "iconKings");
        List<FamilyLandHoldingView> holdings = LandEconomySystem.GetFamilyHoldings(family);
        if (holdings.Count == 0)
        {
            __instance.showStatRow("family_land_share", LM.Get("family_land_no_city"), "#B8C6CC",
                pIconPath: "iconKings");
            return;
        }
        foreach (FamilyLandHoldingView holding in holdings.Take(10))
        {
            string value = holding.MarketOpen
                ? string.Format(LM.Get("family_land_private_share"), holding.CityName, holding.OwnershipShare)
                : string.Format(LM.Get("family_land_feudal_tenure"), holding.CityName, holding.TenureShare);
            string color = holding.MarketOpen && holding.OwnershipShare <= 0f ? "#E66B66" :
                holding.MarketOpen ? "#7FD8EA" : "#B8C6CC";
            __instance.showStatRow("family_land_share", value, color, pIconPath: "iconKings");
        }
    }

    public static void set_family_name(Family __instance, Actor pActor1, Actor pActor2, WorldTile pTile)
    {
        if (__instance?.data == null) return;
        CulturePatch.EnsureEmpireNaming(pActor1?.culture);
        CulturePatch.EnsureEmpireNaming(pActor2?.culture);
        Culture culture = null;
        if (pActor1 != null)
        {
            if (pActor1.GetModName().hasFamilyName(pActor1))
            {
                string cityName = pActor1.city.GetCityName();
                string familyName = pActor1.GetModName().familyName;
                string familyEnd = LM.Get("Family");
                __instance.data.name = OverallHelperFunc.JoinNameParts(cityName, familyName, familyEnd);
                OverallHelperFunc.SetFamilyCityPre(__instance);
            }
            else
            {
                if ( pActor1.hasCulture())
                {
                    culture = pActor1.culture;
                    __instance.data.name = culture.getOnomasticData(MetaType.Family).generateName()
                        .UseLocalizedNameSeparator();
                    OverallHelperFunc.SetFamilyCityPre(__instance, false);
                    if (!pActor1.GetModName().hasFamilyName(pActor1))
                    {
                        pActor1.SetFamilyName(__instance.GetFamilyName());
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
                            pActor2.SetFamilyName(__instance.GetFamilyName());
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
                __instance.data.name = OverallHelperFunc.JoinNameParts(cityName, familyName, familyEnd);
                OverallHelperFunc.SetFamilyCityPre(__instance);
            }
            else
            {
                if (pActor2.hasCulture())
                {
                    culture = pActor2.culture;
                    __instance.data.name = culture.getOnomasticData(MetaType.Family).generateName()
                        .UseLocalizedNameSeparator();
                    OverallHelperFunc.SetFamilyCityPre(__instance, false);
                    if (!pActor2.GetModName().hasFamilyName(pActor2))
                    {
                        
                        pActor2.SetFamilyName(__instance.GetFamilyName());
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
                            pActor1.SetFamilyName(__instance.GetFamilyName());
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
