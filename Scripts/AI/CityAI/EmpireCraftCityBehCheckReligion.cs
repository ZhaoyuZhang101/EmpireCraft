using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckReligion: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (pCity.isCapitalCity()) return BehResult.Continue;
        if (!pCity.hasKingdom()) return BehResult.Continue;
        if (pCity.kingdom.IsInEmpire())  return BehResult.Continue;
        var culture = CultureService.GetMainCulture(pCity);
        if (!CultureService.IsValidCulture(culture)) culture = "Western";
        // 圣城神权自立：罗马一线(教会与王权分立的传统)；以前查文化配置的政体是不是封建，西方(古典城邦起步)漏了
        if (InstitutionSystem.GetCultureLine(culture) != "Roma") return BehResult.Continue;
        if (pCity.hasReligion())
        {
            Religion pReligion = pCity.getReligion();
            if (pReligion.GetCity() == pCity)
            {
                if (pCity.kingdom?.getReligion() != pReligion) return BehResult.Continue;
                var kingdom = pCity.makeOwnKingdom(pCity.units.First());
                Regime regime = kingdom.GetRegime();
                regime.SetReligionLevel(ReligionLevel.High);
                if (pCity.hasTitle())
                {
                    var cTitle = pCity.GetTitle();
                    if (cTitle.getCities().Count() == 1)
                    {
                        return BehResult.Continue;
                    }
                }
                KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(pCity);
                if (title == null) return BehResult.Continue;
                pCity.kingdom?.SetMainTitle(title);
                pCity.kingdom?.king?.AddOwnedTitle(title);
            }
        }
        return BehResult.Continue;
    }
}
