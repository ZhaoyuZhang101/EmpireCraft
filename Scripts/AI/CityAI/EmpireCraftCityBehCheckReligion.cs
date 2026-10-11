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
        RegimeType regimeType = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting.regime
            : RegimeType.Feudalism;
        if (regimeType != RegimeType.Feudalism )  return BehResult.Continue;
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
