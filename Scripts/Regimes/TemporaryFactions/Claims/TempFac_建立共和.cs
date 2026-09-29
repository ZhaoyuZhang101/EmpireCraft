using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

// 建立共和(见 RepublicSystem)：本文化已施行"废除君主制"后，执政一方占三分之二议席时和平退位；
// 革命型政党在正统崩溃、阶层怨气高涨时发动革命。
public class TempFac_建立共和 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_建立共和();
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
        FixedFaction party = GetFaction();
        if (empire != null && party != null) RepublicSystem.PushRepublic(empire, party);
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        FixedFaction party = GetFaction();
        return empire != null && party != null &&
               (RepublicSystem.CanAbdicate(empire, party) || RepublicSystem.CanRevolt(empire, party));
    }
}
