using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 赈济灾荒(公共议题)：帝国境内有城市闹饥荒时，请旨开仓放粮(经费见 ClaimTuning)。
// 每座饥荒城市按半数户发粮入仓，农民与劳工的怨气消减。
public class TempFac_赈济灾荒 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_赈济灾荒();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    private const int MaxCities = 6;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        List<City> cities = FamineCities(empire).Take(MaxCities).ToList();
        int grain = 0;
        foreach (City city in cities)
        {
            int amount = Mathf.Clamp(Mathf.CeilToInt(CityPopulationSystem.Households(city) * 0.5f), 10, 500);
            city.addResourcesToRandomStockpile("bread", amount);
            grain += amount;
        }
        if (cities.Count > 0)
        {
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -6f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, -3f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_赈济灾荒_cities"), cities.Count, grain);
        }
        End();
    }

    public override bool CheckCondition() =>
        CityPopulationSystem.AbstractPopulationEnabled && FamineCities(GetEmpire()).Any();

    private static IEnumerable<City> FamineCities(Empire empire) =>
        ClaimPolicySystem.Cities(empire).Where(city => CityPopulationSystem.GetGrowthFactors(city).Famine);
}
