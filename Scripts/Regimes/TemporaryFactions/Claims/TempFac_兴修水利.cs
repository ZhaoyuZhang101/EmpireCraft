using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 兴修水利(清流、重农派、节用派的朝堂议题)：户数最多的八座城开渠灌田，各添粮储(每户约三成)；农民怨气 -4、地主 -2；征发民力记工役(见 CorveeSystem)；十年一次
public class TempFac_兴修水利 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_兴修水利();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override int CooldownYears => 10;

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        if (empire?.CoreKingdom != null)
        {
            List<City> cities = ClaimPolicySystem.Cities(empire).OrderByDescending(CityPopulationSystem.Households).Take(8).ToList();
            int grain = 0;
            foreach (City city in cities)
            {
                int amount = Mathf.Clamp(Mathf.CeilToInt(CityPopulationSystem.Households(city) * 0.3f), 10, 300);
                city.addResourcesToRandomStockpile("bread", amount);
                grain += amount;
            }
            // 开渠要征发民力：每座开渠的城记一笔工役(徭役，不算大兴土木，见 CorveeSystem)
            CorveeSystem.Add(empire.CoreKingdom, cities.Count * CorveeSystem.IrrigationPerCity, false);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -4f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, -2f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_兴修水利_done"), cities.Count, grain);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown() && CityPopulationSystem.AbstractPopulationEnabled;
    }
}
