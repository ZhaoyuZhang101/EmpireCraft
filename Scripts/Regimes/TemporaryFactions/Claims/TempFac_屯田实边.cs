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

// 屯田实边(变法派、将门的朝堂议题)：与外国接壤的边城(至多六座)屯田积粮；军人怨气 -5、农民服役 +2；十年一次。
// 本国不服水土的旱地城计时往回拨五年(不晚于现在)，只是拖延，免不掉(见 ShuiTuSystem.Delay)
public class TempFac_屯田实边 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_屯田实边();
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
            List<City> cities = ClaimPolicySystem.ForeignBorders(empire).Select(pair => pair.own).Distinct().Take(6).ToList();
            int grain = 0;
            foreach (City city in cities)
            {
                int amount = Mathf.Clamp(Mathf.CeilToInt(CityPopulationSystem.Households(city) * 0.3f), 10, 300);
                city.addResourcesToRandomStockpile("bread", amount);
                grain += amount;
            }
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Army, -5f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, 2f);
            CompletionOutcome = string.Format(GeneralSystems.CultureDoctrines.Text("claim_outcome_屯田实边_done", GetEmpire()), cities.Count, grain);
            int delayed = ShuiTuSystem.Delay(empire.CoreKingdom);
            if (delayed > 0) CompletionOutcome += string.Format(LM.Get("claim_outcome_屯田实边_shuitu"), delayed);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        return empire?.CoreKingdom != null && OffCooldown() && CityPopulationSystem.AbstractPopulationEnabled && ClaimPolicySystem.HasForeignBorder(empire);
    }
}
