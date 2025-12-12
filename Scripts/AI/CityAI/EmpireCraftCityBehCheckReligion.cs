using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckReligion: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (pCity.isCapitalCity()) return BehResult.Continue;
        if (!pCity.hasKingdom()) return BehResult.Continue;
        if (pCity.kingdom.IsInEmpire())  return BehResult.Continue;
        var culture = ConfigData.speciesCulturePair.TryGetValue(pCity.getActorAsset().id, out string speciesCulture)? speciesCulture : "Western";
        RegimeType regimeType = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting.regime
            : RegimeType.Feudalism;
        if (regimeType != RegimeType.Feudalism )  return BehResult.Continue;
        if (pCity.hasReligion())
        {
            Religion pReligion = pCity.getReligion();
            if (pReligion.GetCity() == pCity)
            {
                if ((pCity.kingdom?.capital?.hasReligion()??false)&&(pCity.kingdom?.capital?.getReligion()?.GetCity()==pCity.kingdom?.capital)) return BehResult.Continue;
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
                ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(pCity);
                pCity.kingdom?.SetMainTitle(pCity.GetTitle());
                pCity.kingdom?.king.AddOwnedTitle(pCity.GetTitle());
            }
        }
        return BehResult.Continue;
    }
}