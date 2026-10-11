using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 扶持革命党：在国内扶植本派的基层组织——本派在中央的声势 +5、劳工农民好感上升，
// 三座最大的城市里劳工、农民转向本派理念。本派声势已过半时不再提出。
public class TempFac_扶持革命党 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_扶持革命党();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        FixedFaction faction = GetFaction();
        if (empire?.CoreKingdom != null && faction != null)
        {
            empire.CoreKingdom.TryIncreaseFactionRatio(faction, 5);
            string source = $"claim:{type}";
            FactionClassSystem.ModifyFavor(faction, SocialClass.Labour, 6f, source);
            FactionClassSystem.ModifyFavor(faction, SocialClass.Peasant, 5f, source);
            PartyIdeology ideology = faction.IsParty ? faction.Ideology : PartyIdeology.Communism;
            foreach (City city in ClaimPolicySystem.Cities(empire)
                         .OrderByDescending(city => CityPopulationSystem.GetTotal(city)).Take(3).ToList())
                CityPopulationSystem.ConvertBackground(city, ideology, socialClass =>
                    socialClass is SocialClass.Labour or SocialClass.Peasant ? 0.04f : 0.01f);
            CompletionOutcome = string.Format(LM.Get("claim_outcome_扶持革命党_named"), faction.Name);
        }
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        FixedFaction faction = GetFaction();
        return empire?.CoreKingdom != null && faction != null && !faction.Ban && faction.CentralRatio < 50;
    }
}
