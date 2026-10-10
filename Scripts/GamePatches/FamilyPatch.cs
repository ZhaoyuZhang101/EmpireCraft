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
        // Choose once. Previously the second founder always overwrote the first,
        // even when the first founder was the paternal/primary lineage.
        Actor founder = SurnameInheritanceSystem.SelectParent(new[] { pActor1, pActor2 })
                        ?? (pActor1?.hasCulture() == true ? pActor1 : pActor2);
        if (founder?.data == null) return;
        string surname = founder.GetModName()?.familyName;
        bool cityPrefix = !string.IsNullOrWhiteSpace(surname) && founder.city != null;
        if (string.IsNullOrWhiteSpace(surname))
        {
            OnomasticsData names = CulturePatch.GetOnomasticDataSafe(founder.culture, MetaType.Family);
            if (names == null) return;
            __instance.data.name = names.generateName().UseLocalizedNameSeparator();
            __instance.SetFamilyCityPre(false);
            surname = __instance.GetFamilyName();
        }
        else
        {
            __instance.data.name = OverallHelperFunc.JoinNameParts(
                cityPrefix ? founder.city.GetCityName() : "", surname, LM.Get("Family"));
            __instance.SetFamilyCityPre(cityPrefix);
        }
        foreach (Actor actor in new[] { pActor1, pActor2 })
        {
            if (actor?.data == null) continue;
            if (!actor.hasCulture() && founder.culture != null) actor.setCulture(founder.culture);
            Name name = actor.GetModName();
            if (name == null) continue;
            if (!name.hasFamilyName(actor)) actor.SetFamilyName(surname);
            if (name.has_whole_name(actor)) name.SetName(actor);
        }
    }
}
